using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using ECommons.DalamudServices;
using ECommons.Logging;

namespace RotationSolver.Himechan;

/// <summary>
/// RSR draws headings with the game's Axis font, which has no Hangul glyphs (they render as boxes / "=").
/// While Korean display is on, the heading font is built as Axis plus the Hangul ranges merged in from
/// Dalamud's bundled CJK font, so Latin keeps the Axis look and Korean becomes readable. Body text already
/// uses Dalamud's default font and needs nothing.
/// </summary>
internal static class HimechanFonts
{
	// Hangul Jamo, compatibility Jamo, syllables, CJK punctuation and general punctuation (·, …, ‥).
	private static readonly ushort[] HangulRanges =
	[
		0x1100, 0x11FF,
		0x3130, 0x318F,
		0xAC00, 0xD7A3,
		0x3000, 0x303F,
		0x2000, 0x206F,
		0x00B7, 0x00B7,
		0,
	];

	/// <summary>Called from FontManager.GetFont (HIMECHAN-HOOK: HeaderFont) for every heading font size.</summary>
	public static IFontHandle CreateHeaderFont(int px)
	{
		var atlas = Svc.PluginInterface.UiBuilder.FontAtlas;
		var style = new GameFontStyle(GameFontFamily.Axis, px);
		if (!HimechanLocalization.Enabled)
		{
			return atlas.NewGameFontHandle(style);
		}

		try
		{
			return atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
			{
				var axis = tk.AddGameGlyphs(style, null, default);
				_ = tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansCjkMedium, new SafeFontConfig
				{
					SizePx = px,
					MergeFont = axis,
					GlyphRanges = HangulRanges,
				});
			}));
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"[Himechan] Korean heading font failed, using the game font: {ex.Message}");
			return atlas.NewGameFontHandle(style);
		}
	}

	/// <summary>Drop cached heading fonts so a toggle change rebuilds them with/without Hangul.</summary>
	public static void Rebuild()
	{
		try
		{
			UI.FontManager.DisposeAll();
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"[Himechan] Font cache reset failed: {ex.Message}");
		}
	}
}
