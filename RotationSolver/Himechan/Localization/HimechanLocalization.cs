using System.Collections.Concurrent;
using System.ComponentModel;
using ECommons.Logging;

namespace RotationSolver.Himechan;

/// <summary>
/// #29 Korean display for RSR's own UI (spec 3.12). Two upstream hooks ask here: enum descriptions
/// (UiString.GetDescription) and setting names/descriptions (Searchable). The dictionary is keyed by the
/// English text, so a string upstream changes simply shows in English until the dictionary catches up.
/// Users can add or fix entries without a rebuild: Himechan/ko.override.json ({ "English": "한국어", ... }).
/// </summary>
internal static class HimechanLocalization
{
	private static readonly Dictionary<string, string> _override = new(StringComparer.Ordinal);
	private static readonly ConcurrentDictionary<(Type, string), string?> _enumCache = new();
	private static bool _loaded;

	public static bool Enabled => HimechanSettings.Current.TranslateRsrUi;

	public static int DictionaryCount => HimechanKoreanStrings.Map.Count + _override.Count;

	/// <summary>Korean text for an English UI string, or the input itself when disabled / unknown / empty.</summary>
	public static string Translate(string? text)
	{
		if (string.IsNullOrEmpty(text) || !Enabled)
		{
			return text ?? string.Empty;
		}

		EnsureLoaded();
		if (_override.TryGetValue(text, out var ko) || HimechanKoreanStrings.Map.TryGetValue(text, out ko))
		{
			return ko;
		}

		return text;
	}

	/// <summary>
	/// Korean text for an enum value's [Description]. Reflection is cached per (type, name); the result respects
	/// the toggle on every call, so switching it in the Himechan window applies immediately.
	/// </summary>
	public static bool TryTranslate(Enum value, out string text)
	{
		text = string.Empty;
		if (!Enabled)
		{
			return false;
		}

		try
		{
			var key = (value.GetType(), value.ToString());
			var english = _enumCache.GetOrAdd(key, static k =>
				k.Item1.GetField(k.Item2)?.GetCustomAttribute<DescriptionAttribute>()?.Description);
			if (string.IsNullOrEmpty(english))
			{
				return false;
			}

			var translated = Translate(english);
			if (ReferenceEquals(translated, english) || translated == english)
			{
				return false;
			}

			text = translated;
			return true;
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"[Himechan] Enum translation failed: {ex.Message}");
			return false;
		}
	}

	/// <summary>Re-reads ko.override.json (also called once lazily).</summary>
	public static void Reload()
	{
		_override.Clear();
		_loaded = true;
		try
		{
			var path = HimechanPaths.OverrideFile;
			if (!File.Exists(path))
			{
				return;
			}

			var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
			if (map == null)
			{
				return;
			}

			foreach (var pair in map)
			{
				if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
				{
					_override[pair.Key] = pair.Value;
				}
			}

			PluginLog.Information($"[Himechan] ko.override.json: {_override.Count} entries");
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"[Himechan] Failed to read ko.override.json: {ex.Message}");
		}
	}

	private static void EnsureLoaded()
	{
		if (!_loaded)
		{
			Reload();
		}
	}
}
