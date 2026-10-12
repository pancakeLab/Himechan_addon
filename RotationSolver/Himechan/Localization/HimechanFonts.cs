using System.Reflection;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using ECommons.DalamudServices;
using ECommons.Logging;

namespace RotationSolver.Himechan;

/// <summary>
/// RSR's UI library (RebornMaterial, a NuGet package since upstream 7.5.6.24) draws headings with the game's Axis font,
/// which has no Hangul glyphs (they render as boxes / "="). The package has no font API, but it gets its fonts from the
/// IDalamudPluginInterface passed to M3.Initialize. HIMECHAN-HOOK: HeaderFont wraps that interface in a transparent
/// proxy that forwards everything unchanged except IFontAtlas.NewGameFontHandle, which returns the same game font with
/// the Hangul ranges merged in from Dalamud's bundled CJK font. Body text already uses Dalamud's default font.
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

	private static readonly Dictionary<object, object> _wrapped = new(ReferenceEqualityComparer.Instance);

	/// <summary>Wraps the plugin interface given to M3.Initialize. On any failure the original is returned (headings stay as upstream).</summary>
	public static IDalamudPluginInterface WrapForM3(IDalamudPluginInterface pluginInterface)
	{
		try
		{
			return Wrap(pluginInterface, static (method, _, result) =>
				method.Name == "get_UiBuilder" && result is IUiBuilder builder ? WrapUiBuilder(builder) : result);
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"[Himechan] Font proxy unavailable, headings keep the game font: {ex.Message}");
			return pluginInterface;
		}
	}

	private static IUiBuilder WrapUiBuilder(IUiBuilder builder) =>
		Wrap(builder, static (method, _, result) =>
			method.Name == "get_FontAtlas" && result is IFontAtlas atlas ? WrapAtlas(atlas) : result);

	private static IFontAtlas WrapAtlas(IFontAtlas atlas) =>
		Wrap(atlas, null, (method, args) =>
			method.Name == nameof(IFontAtlas.NewGameFontHandle) && args is [GameFontStyle style]
				? (true, CreateHeaderFont(atlas, style))
				: (false, null));

	private static T Wrap<T>(T target, Func<MethodInfo, object?[]?, object?, object?>? after, Func<MethodInfo, object?[]?, (bool Handled, object? Value)>? before = null) where T : class
	{
		lock (_wrapped)
		{
			if (_wrapped.TryGetValue(target, out var existing))
			{
				return (T)existing;
			}

			var proxy = HimechanForwardingProxy<T>.Create(target, before, after);
			_wrapped[target] = proxy;
			return proxy;
		}
	}

	/// <summary>The game font plus Hangul (when Korean display is on). Built as a delegate handle so a toggle only needs an atlas rebuild.</summary>
	private static IFontHandle CreateHeaderFont(IFontAtlas atlas, GameFontStyle style)
	{
		var px = style.SizePt * 4f / 3f;
		return atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
		{
			var game = tk.AddGameGlyphs(style, null, default);
			if (!HimechanLocalization.Enabled)
			{
				return;
			}

			try
			{
				_ = tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansCjkMedium, new SafeFontConfig
				{
					SizePx = px,
					MergeFont = game,
					GlyphRanges = HangulRanges,
				});
			}
			catch (Exception ex)
			{
				PluginLog.Warning($"[Himechan] Hangul merge into heading font failed: {ex.Message}");
			}
		}));
	}

	/// <summary>Rebuilds the atlas so heading fonts pick up a toggle change.</summary>
	public static void Rebuild()
	{
		try
		{
			_ = Svc.PluginInterface.UiBuilder.FontAtlas.BuildFontsAsync();
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"[Himechan] Font rebuild failed: {ex.Message}");
		}
	}
}

/// <summary>Transparent forwarding proxy: every call goes to the target; optional before/after interceptors.</summary>
internal class HimechanForwardingProxy<T> : DispatchProxy where T : class
{
	private T _target = null!;
	private Func<MethodInfo, object?[]?, (bool Handled, object? Value)>? _before;
	private Func<MethodInfo, object?[]?, object?, object?>? _after;

	public static T Create(T target, Func<MethodInfo, object?[]?, (bool Handled, object? Value)>? before, Func<MethodInfo, object?[]?, object?, object?>? after)
	{
		var proxy = Create<T, HimechanForwardingProxy<T>>();
		var self = (HimechanForwardingProxy<T>)(object)proxy;
		self._target = target;
		self._before = before;
		self._after = after;
		return proxy;
	}

	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
	{
		if (targetMethod == null)
		{
			return null;
		}

		if (_before != null)
		{
			var (handled, value) = _before(targetMethod, args);
			if (handled)
			{
				return value;
			}
		}

		object? result;
		try
		{
			result = targetMethod.Invoke(_target, args);
		}
		catch (TargetInvocationException ex) when (ex.InnerException != null)
		{
			throw ex.InnerException;
		}

		return _after != null ? _after(targetMethod, args, result) : result;
	}
}
