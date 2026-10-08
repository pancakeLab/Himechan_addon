using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using RotationSolver.Basic.Configuration;
using RotationSolver.Basic.Himechan;
using RotationSolver.Updaters;

namespace RotationSolver.Himechan;

/// <summary>
/// #28 Himechan settings profile (spec 3.8 / 3.8.1).
/// While Himechan is enabled and the player is a White Mage, <see cref="Service.Config"/> is replaced with the
/// Himechan profile (Himechan/profile.json). RotationSolver.json always keeps the original RSR settings:
/// only the original object is ever written there, so a crash or uninstall cannot leave Himechan values in it.
/// </summary>
internal static class HimechanProfile
{
	private const int MaxBackups = 10;
	private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(500);
	private static readonly object _fileLock = new();

	// The profile object we put into Service.Config. Null while the original RSR settings are active.
	private static Configs? _installed;
	private static DateTime _nextCheck = DateTime.MinValue;
	private static bool _pendingRotationReapply;
	private static bool _backedUpThisSession;

	/// <summary>True while the Himechan profile is the active RSR config.</summary>
	public static bool IsActive => _installed != null && ReferenceEquals(Service.Config, _installed);

	/// <summary>True when a swap is wanted but waits for combat to end.</summary>
	public static bool IsWaitingForCombatEnd { get; private set; }

	public static void Init()
	{
		HimechanBasicHooks.ConfigSave = OnConfigSave;
	}

	public static void Dispose()
	{
		// Called after RSR's own final Save(), so the profile has already been written to its file.
		HimechanBasicHooks.ConfigSave = null;
		_installed = null;
	}

	public static void Update()
	{
		var now = DateTime.Now;
		if (now < _nextCheck)
		{
			return;
		}
		_nextCheck = now + CheckInterval;

		try
		{
			// RSR's own "Restore" / "Reset" put a new object into Service.Config. Those always act on the
			// original settings (they write RotationSolver.json); the Himechan profile file is untouched.
			if (_installed != null && !ReferenceEquals(Service.Config, _installed))
			{
				_installed = null;
				Notify("RSR 설정 복원/초기화가 원본 설정에 적용되었습니다. 히메짱 프로필은 그대로 유지되며 곧 다시 적용됩니다.");
			}

			if (!Player.Available)
			{
				return;
			}

			var want = HimechanSettings.Current.Enabled && Player.Job == Job.WHM;
			var active = _installed != null;
			if (want != active)
			{
				if (DataCenter.InCombat)
				{
					IsWaitingForCombatEnd = true;
				}
				else
				{
					IsWaitingForCombatEnd = false;
					if (want)
					{
						Activate();
					}
					else
					{
						Deactivate();
					}
				}
			}
			else
			{
				IsWaitingForCombatEnd = false;
			}

			if (_pendingRotationReapply && DataCenter.CurrentRotation != null)
			{
				_pendingRotationReapply = false;
				ReapplyRotationConfigs(DataCenter.CurrentRotation.Configs);
				if (DataCenter.CurrentDutyRotation != null)
				{
					ReapplyRotationConfigs(DataCenter.CurrentDutyRotation.Configs);
				}
			}
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Profile update failed: {ex}");
		}
	}

	/// <summary>
	/// Throws the current profile away and makes a fresh copy of the original RSR settings on the next check.
	/// The old profile is kept in the backup folder. Out of combat only.
	/// </summary>
	public static bool Recreate()
	{
		if (DataCenter.InCombat)
		{
			return false;
		}

		if (_installed != null)
		{
			Deactivate();
		}

		lock (_fileLock)
		{
			if (File.Exists(HimechanPaths.ProfileFile))
			{
				BackupProfileFile("recreate");
				File.Delete(HimechanPaths.ProfileFile);
			}
		}

		Notify("히메짱 프로필을 지웠습니다. 다음 적용 시 현재 RSR 원본 설정을 복제해 새로 만듭니다.");
		return true;
	}

	private static void Activate()
	{
		// Write the latest original settings to RotationSolver.json before swapping them out.
		Service.Config.Save();

		var profile = LoadProfileOrClone(Service.Config);
		CopySharedKeys(Service.Config, profile);

		_installed = profile;
		Service.Config = profile;
		Service.Config.Save();
		AfterSwap();

		Notify("히메짱 설정 프로필로 전환했습니다. 지금부터 RSR 설정 창에서 바꾸는 값은 히메짱 프로필에 저장됩니다.");
	}

	private static void Deactivate()
	{
		var profile = _installed;
		if (profile != null && ReferenceEquals(Service.Config, profile))
		{
			profile.Save();
		}

		var original = LoadOriginal();
		if (profile != null)
		{
			CopySharedKeys(profile, original);
		}

		_installed = null;
		Service.Config = original;
		AfterSwap();

		Notify("RSR 원본 설정으로 돌아왔습니다.");
	}

	private static void AfterSwap()
	{
		// A1 (spec 3.8.1): RotationUpdater only re-reads RotationChoice when job / PvE-PvP changes.
		// Clearing the current rotation makes it pick again from the new config on the next frame.
		DataCenter.CurrentRotation = null;
		// A2: rotation config values were copied into the rotation object when it was created; push the new ones.
		_pendingRotationReapply = true;
	}

	private static void ReapplyRotationConfigs(IRotationConfigSet configs)
	{
		var stored = Service.Config.RotationConfigurations;
		foreach (var config in configs)
		{
			if (stored.TryGetValue(config.Name, out var value))
			{
				config.Value = value;
			}
			else
			{
				// Reset to the rotation default without pinning that default into the profile,
				// so a later upstream change of the default still applies.
				config.Value = config.DefaultValue;
				_ = stored.TryRemove(config.Name, out _);
			}
		}
	}

	/// <summary>Settings that should not differ between profiles (otherwise the tutorial / changelog pop up on every swap).</summary>
	private static void CopySharedKeys(Configs from, Configs to)
	{
		to.TutorialDone = from.TutorialDone;
		to.LastSeenChangelog = from.LastSeenChangelog;
	}

	private static bool OnConfigSave(Configs config)
	{
		if (_installed == null || !ReferenceEquals(config, _installed))
		{
			return false; // Original settings: let RSR write RotationSolver.json as usual.
		}

		try
		{
			lock (_fileLock)
			{
				HimechanPaths.WriteAtomic(HimechanPaths.ProfileFile, JsonConvert.SerializeObject(config, Formatting.Indented));
			}
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Failed to save the Himechan profile: {ex.Message}");
		}

		// Even on failure the profile must never be written into RotationSolver.json.
		return true;
	}

	private static Configs LoadProfileOrClone(Configs original)
	{
		lock (_fileLock)
		{
			if (File.Exists(HimechanPaths.ProfileFile))
			{
				if (!_backedUpThisSession)
				{
					BackupProfileFile("session");
					_backedUpThisSession = true;
				}

				try
				{
					var loaded = JsonConvert.DeserializeObject<Configs>(File.ReadAllText(HimechanPaths.ProfileFile));
					if (loaded != null && loaded.Version == Configs.CurrentVersion)
					{
						return loaded;
					}

					BackupProfileFile("old-version");
					Notify("RSR 설정 형식이 바뀌어 히메짱 프로필을 현재 RSR 원본 설정에서 다시 만들었습니다. 이전 프로필은 백업 폴더에 있습니다.");
				}
				catch (Exception ex)
				{
					PluginLog.Error($"[Himechan] Profile file is unreadable: {ex.Message}");
					BackupProfileFile("unreadable");
					Notify("히메짱 프로필 파일을 읽을 수 없어 현재 RSR 원본 설정에서 다시 만들었습니다. 이전 파일은 백업 폴더에 있습니다.");
				}
			}
		}

		var clone = JsonConvert.DeserializeObject<Configs>(JsonConvert.SerializeObject(original)) ?? new Configs();
		HimechanMigration.OnProfileCreated(clone);
		return clone;
	}

	private static Configs LoadOriginal()
	{
		// Same steps as RotationSolverPlugin.LoadAsync.
		try
		{
			var path = Svc.PluginInterface.ConfigFile.FullName;
			if (File.Exists(path))
			{
				var configs = JsonConvert.DeserializeObject<Configs>(File.ReadAllText(path)) ?? new Configs();
				configs = Configs.Migrate(configs);
				return configs.Version == Configs.CurrentVersion ? configs : new Configs();
			}
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"[Himechan] Failed to read RotationSolver.json: {ex.Message}");
		}
		return new Configs();
	}

	private static void BackupProfileFile(string reason)
	{
		try
		{
			System.IO.Directory.CreateDirectory(HimechanPaths.BackupDirectory);
			var target = Path.Combine(HimechanPaths.BackupDirectory, $"profile_{DateTime.Now:yyyyMMdd_HHmmss}_{reason}.json");
			File.Copy(HimechanPaths.ProfileFile, target, true);

			var files = System.IO.Directory.GetFiles(HimechanPaths.BackupDirectory, "profile_*.json");
			Array.Sort(files, StringComparer.Ordinal);
			for (var i = 0; i < files.Length - MaxBackups; i++)
			{
				File.Delete(files[i]);
			}
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"[Himechan] Profile backup failed: {ex.Message}");
		}
	}

	internal static void Notify(string message)
	{
		PluginLog.Information($"[Himechan] {message}");
		Svc.Chat.Print($"[히메짱] {message}");
	}
}

/// <summary>
/// One-time conversions applied when a profile is first created from the original settings.
/// #6 (Thin Air strategy stored as enum name) is added here when the Himechan rotation is ported.
/// </summary>
internal static class HimechanMigration
{
	public static void OnProfileCreated(Configs profile)
	{
	}
}
