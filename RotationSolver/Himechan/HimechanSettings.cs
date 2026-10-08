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
