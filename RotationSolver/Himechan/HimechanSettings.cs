using ECommons.DalamudServices;
using ECommons.Logging;

namespace RotationSolver.Himechan;

/// <summary>
/// Himechan's own settings. Kept outside the RSR config (and outside the Himechan profile),
/// because the profile swap itself depends on <see cref="Enabled"/>.
/// File: pluginConfigs/RotationSolver/Himechan/settings.json
/// </summary>
internal sealed class HimechanSettings
{
	public int Version { get; set; } = 1;

	/// <summary>Use the Himechan profile while playing White Mage.</summary>
	public bool Enabled { get; set; } = false;

	/// <summary>Spec 3.11 / #27: write a combat log file per fight (Himechan/logs).</summary>
	public bool DebugMode { get; set; } = false;

	/// <summary>#29: show RSR's own settings window / messages in Korean (central dictionary, spec 3.12).</summary>
	public bool TranslateRsrUi { get; set; } = true;

	/// <summary>#34 HUD (ring design C, 2026-10-14): show the GCD ring with the reserved GCD / oGCD stacks.</summary>
	public bool HudEnabled { get; set; } = true;

	/// <summary>Locked: no mouse input reaches the HUD window (click-through) and it cannot be dragged.</summary>
	public bool HudLocked { get; set; } = false;

	public float HudScale { get; set; } = 1.0f;

	/// <summary>Show only in combat / countdown (the unlocked HUD is always shown so it can be placed).</summary>
	public bool HudCombatOnly { get; set; } = true;

	/// <summary>Compact layout: no cast ring, just the two GCD boxes over the four oGCD slots.</summary>
	public bool HudCompact { get; set; } = false;

	/// <summary>Slidecast window: moving is safe once this much of the cast is left (seconds).</summary>
	public float HudSlidecastWindow { get; set; } = 0.5f;

	/// <summary>Input deadline marker: the game's queue window opens this many seconds before the GCD ends.</summary>
	public float HudQueueWindow { get; set; } = 0.5f;

	/// <summary>Saved window position (screen pixels); negative = not placed yet (centered above the cast bar).</summary>
	public float HudPosX { get; set; } = -1f;
	public float HudPosY { get; set; } = -1f;

	/// <summary>#37 cast assist: 0 off, 1 armed while both mouse buttons are down, 2 toggle (/히메짱슬캐). Slide window = HudSlidecastWindow.</summary>
	public int CastAssistMode { get; set; } = HimechanCastAssist.ModeOff;

	/// <summary>Toggle mode: switch the toggle off when combat ends.</summary>
	public bool CastAssistAutoOff { get; set; } = true;

	/// <summary>"CAST ASSIST" text: locked = click-through, immovable, shown only while armed.</summary>
	public bool CastAssistTextLocked { get; set; } = false;
	public float CastAssistTextScale { get; set; } = 1.4f;
	public float CastAssistPosX { get; set; } = -1f;
	public float CastAssistPosY { get; set; } = -1f;

	/// <summary>True while the assist switched RSR's PoslockCasting on in the Himechan profile (switched back off with the assist).</summary>
	public bool CastAssistOwnsPoslock { get; set; } = false;

	/// <summary>Toggle mode state. Never saved: always off after a restart.</summary>
	[JsonIgnore]
	public bool CastAssistToggleOn { get; set; } = false;

	/// <summary>Spec 3.11 / #30: while solo, treat the player as the tank. Never saved, so it is always off after a restart.</summary>
	[JsonIgnore]
	public bool TestMode { get; set; } = false;

	[JsonIgnore]
	public static HimechanSettings Current { get; private set; } = new();

	public static void Load()
	{
		try
		{
			if (File.Exists(HimechanPaths.SettingsFile))
			{
				Current = JsonConvert.DeserializeObject<HimechanSettings>(File.ReadAllText(HimechanPaths.SettingsFile)) ?? new();
			}
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"[Himechan] Failed to read settings, using defaults: {ex.Message}");
			Current = new();
		}
	}

	public static void Save()
	{
		try
		{
			HimechanPaths.WriteAtomic(HimechanPaths.SettingsFile, JsonConvert.SerializeObject(Current, Formatting.Indented));
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Failed to save settings: {ex.Message}");
		}
	}
}

internal static class HimechanPaths
{
	public static string Directory => Path.Combine(Svc.PluginInterface.ConfigDirectory.FullName, "Himechan");
	public static string SettingsFile => Path.Combine(Directory, "settings.json");
	public static string ProfileFile => Path.Combine(Directory, "profile.json");
	public static string BackupDirectory => Path.Combine(Directory, "backups");
	public static string LogDirectory => Path.Combine(Directory, "logs");
	public static string OverrideFile => Path.Combine(Directory, "ko.override.json");

	/// <summary>Write to a temp file first and then replace, so a crash never leaves a half-written file.</summary>
	public static void WriteAtomic(string path, string content)
	{
		System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var temp = path + ".tmp";
		File.WriteAllText(temp, content);
		File.Move(temp, path, true);
	}
}
