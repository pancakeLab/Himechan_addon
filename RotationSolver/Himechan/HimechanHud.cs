using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using RotationSolver.RebornRotations.Healer;
using RotationSolver.Updaters;

namespace RotationSolver.Himechan;

/// <summary>
/// #34 HUD, design C (ring): a GCD ring with RSR's next GCD in the middle, the reserved GCDs on the left and the oGCD
/// queue (priority order) on the right, meant to sit right above the game's cast bar. Plain ImGui draw-list code with
/// RSR's icon cache only, so upstream UI changes cannot break it. Locked = click-through and immovable.
/// </summary>
internal sealed class HimechanHud : Window
{
	private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar
		| ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground
		| ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings;

	// Layout in unscaled pixels (design C).
	private const float Ring = 38f;
	private const float RingThickness = 7f;
	private const float CenterIcon = 40f;
	private const float SmallIcon = 34f;
	private const float AbilityIcon = 22f; // oGCD slots at ~66% of the GCD boxes (user decision 2026-10-15)
	private const float Gap = 4f;
	private const int MaxReserved = 2; // user decision 2026-10-14: two reserved GCD boxes (the stack holds two)
	private const int MaxQueue = 4; // user decision 2026-10-14 (was 5)

	private static readonly uint ColRingBase = Color(0.12f, 0.16f, 0.23f, 0.9f);
	private static readonly uint ColRingElapsed = Color(0.31f, 0.70f, 1f, 1f);
	private static readonly uint ColRingNoWeave = Color(0.88f, 0.27f, 0.35f, 0.75f);
	private static readonly uint ColSlide = Color(1f, 0.83f, 0.30f, 1f);
	private static readonly uint ColSlideOk = Color(0.5f, 0.88f, 0.63f, 1f);
	private static readonly uint ColText = Color(0.91f, 0.94f, 0.97f, 1f);
	private static readonly uint ColLabel = Color(0.62f, 0.70f, 0.78f, 1f);
	private static readonly uint ColDim = Color(1f, 1f, 1f, 0.35f);
	private static readonly uint ColFrame = Color(0.24f, 0.30f, 0.39f, 1f);
	private static readonly uint ColFrameNext = Color(0.56f, 0.76f, 1f, 1f);
	private static readonly uint ColFrameHead = Color(1f, 0.88f, 0.54f, 1f);
	private static readonly uint ColPanel = Color(0.04f, 0.05f, 0.08f, 0.55f);
	private static readonly uint ColUnlocked = Color(1f, 0.83f, 0.30f, 0.9f);
	private static readonly uint ColRingSafe = Color(0.16f, 0.36f, 0.26f, 0.95f);
	private static readonly uint ColQueueWindow = Color(0.45f, 0.93f, 0.95f, 0.95f);
	private static readonly uint ColGapGlow = Color(0.5f, 0.88f, 0.63f, 0.22f);
	private static readonly uint ColStop = Color(1f, 0.55f, 0.25f, 1f);
	private static readonly uint ColIntercept = Color(0.78f, 0.45f, 1f, 1f);

	private Vector2 _lastSavedPos = new(-1, -1);

	public HimechanHud() : base("히메짱 HUD###HimechanHud", BaseFlags)
	{
		IsOpen = true;
		RespectCloseHotkey = false;
	}

	private static WHM_Himechan? Himechan => DataCenter.CurrentRotation as WHM_Himechan;

	public override bool DrawConditions()
	{
		var settings = HimechanSettings.Current;
		if (!settings.HudEnabled || !Svc.ClientState.IsLoggedIn || Himechan == null)
		{
			return false;
		}

		// Unlocked: always visible so it can be placed. Locked + combat only: combat / countdown.
		return !settings.HudLocked || !settings.HudCombatOnly || DataCenter.InCombat || Service.CountDownTime > 0;
	}

	public override void PreDraw()
	{
		var settings = HimechanSettings.Current;
		Flags = BaseFlags;
		if (settings.HudLocked)
		{
			Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMouseInputs;
		}

		if (settings.HudPosX >= 0 && settings.HudPosY >= 0)
		{
			Position = new Vector2(settings.HudPosX, settings.HudPosY);
			PositionCondition = settings.HudLocked ? ImGuiCond.Always : ImGuiCond.Appearing;
		}
		else
		{
			var viewport = ImGui.GetMainViewport();
			Position = viewport.Pos + new Vector2(viewport.Size.X * 0.5f - 230f * settings.HudScale, viewport.Size.Y * 0.58f);
			PositionCondition = ImGuiCond.Appearing;
		}
		base.PreDraw();
	}

	public override void Draw()
	{
		try
		{
			DrawCore();
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"[Himechan] HUD draw failed: {ex.Message}");
		}
	}

	private void DrawCore()
	{
		var settings = HimechanSettings.Current;
		var whm = Himechan;
		if (whm == null)
		{
			return;
		}

		var s = Math.Clamp(settings.HudScale, 0.5f, 2.5f);
		var draw = ImGui.GetWindowDrawList();
		var origin = ImGui.GetCursorScreenPos();
		var font = ImGui.GetFont();
		var fontSize = ImGui.GetFontSize() * s;
		var small = fontSize * 0.8f;

		// Columns: [reserved GCDs] [ring] [queue]
		var ringBox = (Ring + RingThickness) * 2f * s;
		var height = ringBox + fontSize * 1.4f;
		var leftWidth = MaxReserved * (SmallIcon + Gap) * s;
		var rightWidth = MaxQueue * (AbilityIcon + Gap) * s;
		var total = new Vector2(leftWidth + ringBox + rightWidth + 24f * s, height + 8f * s);
		ImGui.Dummy(total);

		if (!settings.HudLocked)
		{
			draw.AddRectFilled(origin, origin + total, ColPanel, 8f * s);
			draw.AddRect(origin, origin + total, ColUnlocked, 8f * s, ImDrawFlags.None, 1.5f);
			draw.AddText(font, small, origin + new Vector2(6f * s, 2f * s), ColUnlocked, "히메짱 HUD: 끌어서 옮긴 뒤 /히메짱 창에서 잠금");
		}

		var center = origin + new Vector2(leftWidth + 12f * s + ringBox * 0.5f, 6f * s + ringBox * 0.5f);
		var centerGcd = whm.HudCenterGcd();
		var reserved = whm.HudReservedGcds(centerGcd);
		// The GCD that fires next: the first reserved entry while casting, otherwise the planned centre entry.
		var nextNeedsCast = reserved.Count > 0 ? reserved[0].NeedsCast : centerGcd is { Badge: "다음", NeedsCast: true };
		DrawRing(draw, center, s, whm, settings, font, fontSize, small, centerGcd, nextNeedsCast);

		// Reserved GCDs (left, right-aligned toward the ring).
		var rowY = center.Y - SmallIcon * 0.5f * s;
		var x = center.X - ringBox * 0.5f - 12f * s - SmallIcon * s;
		for (var i = 0; i < MaxReserved; i++)
		{
			if (i < reserved.Count)
			{
				DrawIcon(draw, new Vector2(x, rowY), SmallIcon * s, reserved[i], i == 0 ? ColFrameNext : ColFrame, font, small);
			}
			else
			{
				draw.AddRect(new Vector2(x, rowY), new Vector2(x + SmallIcon * s, rowY + SmallIcon * s), ColDim, 4f * s);
			}
			x -= (SmallIcon + Gap) * s;
		}

		// oGCD queue (right). The first HudGapPlaces slots glow: that many abilities still fit into the current gap.
		var queue = whm.HudQueueEntries();
		var places = whm.HudGapPlaces;
		x = center.X + ringBox * 0.5f + 12f * s;
		for (var i = 0; i < MaxQueue; i++)
		{
			var pos = new Vector2(x, rowY + (SmallIcon - AbilityIcon) * 0.5f * s);
			var max = pos + new Vector2(AbilityIcon * s, AbilityIcon * s);
			var open = i < places;
			if (open)
			{
				draw.AddRectFilled(pos - new Vector2(2f * s), max + new Vector2(2f * s), ColGapGlow, 6f * s);
			}

			if (i < queue.Count)
			{
				DrawIcon(draw, pos, AbilityIcon * s, queue[i], open ? ColSlideOk : ColFrame, font, small);
			}
			else
			{
				draw.AddRect(pos, max, open ? ColSlideOk : ColDim, 4f * s, ImDrawFlags.None, open ? 1.5f : 1f);
			}
			x += (AbilityIcon + Gap) * s;
		}

		SavePositionIfMoved(settings);
	}

	private void DrawRing(ImDrawListPtr draw, Vector2 center, float s, WHM_Himechan whm, HimechanSettings settings, ImFontPtr font, float fontSize, float small, WHM_Himechan.HudEntry? centerGcd, bool nextNeedsCast)
	{
		var r = Ring * s;
		var thick = RingThickness * s;
		var total = DataCenter.DefaultGCDTotal;
		var remain = DataCenter.DefaultGCDRemain;
		var elapsed = total > 0f ? Math.Clamp(total - remain, 0f, total) : 0f;
		var frac = total > 0f ? elapsed / total : 0f;
		const float start = -MathF.PI / 2f;
		const float stopMargin = 0.15f; // stand still this long before a cast-time GCD fires

		// Move-safe (user decision 2026-10-14): every moment where moving cannot delay a cast. Casting: from the slidecast
		// point. Not casting: always, except the last moment before a cast-time GCD fires (then "stop").
		var player = Player.Object;
		var casting = player != null && player.IsCasting && total > 0f;
		var castLeft = casting ? player!.TotalCastTime - player.CurrentCastTime : 0f;
		var slide = Math.Clamp(settings.HudSlidecastWindow, 0f, 2f);
		var moveSafe = casting ? castLeft <= slide : !nextNeedsCast || remain > stopMargin;
		var stopNow = !moveSafe && !casting;

		// Smooth fade so the state change reads as a glow, not a flash.
		var dt = Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.1f);
		_moveAlpha += ((moveSafe ? 1f : 0f) - _moveAlpha) * Math.Min(1f, dt * 14f);
		var a = _moveAlpha;

		// Simple single halo (user decision 2026-10-15), still faded in and out.
		if (a > 0.02f)
		{
			draw.AddCircle(center, r, Fade(ColSlideOk, 0.28f * a), 48, thick * 2.6f);
		}

		draw.AddCircle(center, r, Lerp(ColRingBase, ColRingSafe, a), 48, thick);

		// No-weave zone: the last ManualWeaveTail seconds of the GCD.
		if (total > 0f)
		{
			var tail = Math.Clamp(WHMHimechanPolicy.ManualTail(whm.ManualWeaveTail), 0f, total);
			var from = start + MathF.Tau * (1f - tail / total);
			draw.PathArcTo(center, r, from, start + MathF.Tau, 24);
			draw.PathStroke(ColRingNoWeave, ImDrawFlags.None, thick);
		}

		if (frac > 0.005f)
		{
			draw.PathArcTo(center, r, start, start + MathF.Tau * frac, 48);
			draw.PathStroke(ColRingElapsed, ImDrawFlags.None, thick);
		}

		// While casting: inner arc from the slidecast point to the end of the GCD = move + next press accepted.
		if (casting)
		{
			var at = Math.Clamp((elapsed + castLeft - slide) / total, 0f, 1f);
			var from = start + MathF.Tau * at;
			var inner = r - thick * 0.5f - 2f * s;
			draw.PathArcTo(center, inner, from, start + MathF.Tau, 24);
			draw.PathStroke(Fade(ColSlideOk, 0.6f + 0.4f * a), ImDrawFlags.None, 2f * s);
			var dir = new Vector2(MathF.Cos(from), MathF.Sin(from));
			draw.AddLine(center + dir * (inner - 4f * s), center + dir * (r + thick * 0.5f), ColSlide, 2f * s);
		}

		// Center: the GCD being cast (player or RSR), else RSR's next GCD. Frame: green while moving is safe, orange when it is time to stop.
		var half = CenterIcon * 0.5f * s;
		var min = center - new Vector2(half, half);
		if (centerGcd is { } cg && IconSet.GetTexture((ActionID)cg.ActionId, out var tex))
		{
			draw.AddImage(tex.Handle, min, center + new Vector2(half, half));
		}
		var frameColor = stopNow ? ColStop : Lerp(ColFrameNext, ColSlideOk, a);
		draw.AddRect(min, center + new Vector2(half, half), frameColor, 5f * s, ImDrawFlags.None, 2f * s);

		// The only text on the HUD (user decision 2026-10-15): the remaining GCD time. Everything else is colour.
		if (remain > 0f)
		{
			var timeText = $"{remain:F2}s";
			var size = ImGui.CalcTextSize(timeText) * s;
			draw.AddText(font, fontSize, new Vector2(center.X - size.X * 0.5f, center.Y + r + thick + 2f * s), ColText, timeText);
		}
	}

	private float _moveAlpha;

	private static uint Fade(uint color, float alpha)
	{
		var v = ImGui.ColorConvertU32ToFloat4(color);
		v.W *= Math.Clamp(alpha, 0f, 1f);
		return ImGui.ColorConvertFloat4ToU32(v);
	}

	private static uint Lerp(uint from, uint to, float t)
	{
		var f = ImGui.ColorConvertU32ToFloat4(from);
		var g = ImGui.ColorConvertU32ToFloat4(to);
		return ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(f, g, Math.Clamp(t, 0f, 1f)));
	}

	private static void DrawIcon(ImDrawListPtr draw, Vector2 pos, float size, WHM_Himechan.HudEntry entry, uint frame, ImFontPtr font, float small)
	{
		var max = pos + new Vector2(size, size);
		bool found;
		Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap tex;
		if (entry.IsItem)
		{
			found = IconSet.GetTexture(entry.IconId, out tex, 0);
		}
		else
		{
			found = IconSet.GetTexture((ActionID)entry.ActionId, out tex);
		}

		if (found)
		{
			draw.AddImage(tex.Handle, pos, max, Vector2.Zero, Vector2.One, entry.Dim ? ColDim : 0xFFFFFFFF);
		}
		else
		{
			draw.AddRectFilled(pos, max, ColRingBase, 4f);
		}
		// Frame: purple right after jumping the stack, orange for a cast-time GCD (stand still when it fires), else as given.
		var frameCol = entry.Intercepted ? ColIntercept : entry.NeedsCast ? ColStop : entry.Dim ? ColDim : frame;
		var thickness = entry.Intercepted || entry.NeedsCast ? 2f : entry.Dim ? 1f : 1.5f;
		draw.AddRect(pos, max, frameCol, 4f, ImDrawFlags.None, thickness);

		// Status marker without text (user decision 2026-10-15): a filled strip along the bottom edge in the frame colour.
		if (entry.Intercepted || entry.NeedsCast)
		{
			var strip = MathF.Max(3f, size * 0.14f);
			draw.AddRectFilled(new Vector2(pos.X + 1f, max.Y - strip - 1f), new Vector2(max.X - 1f, max.Y - 1f), frameCol, 3f, ImDrawFlags.RoundCornersBottom);
		}
	}

	private void SavePositionIfMoved(HimechanSettings settings)
	{
		if (settings.HudLocked)
		{
			return;
		}

		var pos = ImGui.GetWindowPos();
		if (Vector2.Distance(pos, _lastSavedPos) < 0.5f || ImGui.IsMouseDown(ImGuiMouseButton.Left))
		{
			return;
		}

		_lastSavedPos = pos;
		settings.HudPosX = pos.X;
		settings.HudPosY = pos.Y;
		HimechanSettings.Save();
	}

	private static uint Color(float r, float g, float b, float a) => ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));
}
