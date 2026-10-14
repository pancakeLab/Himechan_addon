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
	private const float Gap = 4f;
	private const int MaxReserved = 2; // user decision 2026-10-14: two reserved GCD boxes (the stack holds two)
	private const int MaxQueue = 5;

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
	private static readonly uint ColSlideGlow = Color(0.5f, 0.88f, 0.63f, 0.28f);
	private static readonly uint ColQueueWindow = Color(0.45f, 0.93f, 0.95f, 0.95f);
	private static readonly uint ColGapGlow = Color(0.5f, 0.88f, 0.63f, 0.22f);
	private static readonly uint ColStop = Color(1f, 0.55f, 0.25f, 1f);

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
		var sideWidth = MathF.Max(MaxReserved, MaxQueue) * (SmallIcon + Gap) * s;
		var ringBox = (Ring + RingThickness) * 2f * s;
		var height = ringBox + fontSize * 2.4f;
		var leftWidth = MaxReserved * (SmallIcon + Gap) * s;
		var rightWidth = MaxQueue * (SmallIcon + Gap) * s;
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
		DrawRing(draw, center, s, whm, settings, font, fontSize, small, centerGcd);

		// Reserved GCDs (left, right-aligned toward the ring).
		var reserved = whm.HudReservedGcds(centerGcd);
		var rowY = center.Y - SmallIcon * 0.5f * s;
		draw.AddText(font, small, new Vector2(origin.X + 6f * s, rowY - small - 2f * s), ColLabel, "예약 GCD");
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
		draw.AddText(font, small, new Vector2(x, rowY - small - 2f * s), ColLabel, "예약 oGCD (우선순위 순)");
		for (var i = 0; i < MaxQueue; i++)
		{
			var pos = new Vector2(x, rowY);
			var max = pos + new Vector2(SmallIcon * s, SmallIcon * s);
			var open = i < places;
			if (open)
			{
				draw.AddRectFilled(pos - new Vector2(2f * s), max + new Vector2(2f * s), ColGapGlow, 6f * s);
			}

			if (i < queue.Count)
			{
				DrawIcon(draw, pos, SmallIcon * s, queue[i], open ? ColSlideOk : ColFrame, font, small);
			}
			else
			{
				draw.AddRect(pos, max, open ? ColSlideOk : ColDim, 4f * s, ImDrawFlags.None, open ? 1.5f : 1f);
			}
			x += (SmallIcon + Gap) * s;
		}

		SavePositionIfMoved(settings);
	}

	private static void DrawRing(ImDrawListPtr draw, Vector2 center, float s, WHM_Himechan whm, HimechanSettings settings, ImFontPtr font, float fontSize, float small, WHM_Himechan.HudEntry? centerGcd)
	{
		var r = Ring * s;
		var thick = RingThickness * s;
		var total = DataCenter.DefaultGCDTotal;
		var remain = DataCenter.DefaultGCDRemain;
		var elapsed = total > 0f ? Math.Clamp(total - remain, 0f, total) : 0f;
		var frac = total > 0f ? elapsed / total : 0f;
		const float start = -MathF.PI / 2f;

		// Slidecast state first: when it is safe to move, the whole ring glows green.
		var player = Player.Object;
		var frameColor = ColFrameNext;
		var slideSafe = false;
		var castLeft = 0f;
		if (player != null && player.IsCasting && total > 0f)
		{
			castLeft = player.TotalCastTime - player.CurrentCastTime;
			slideSafe = castLeft <= Math.Clamp(settings.HudSlidecastWindow, 0f, 2f);
		}

		if (slideSafe)
		{
			draw.AddCircle(center, r, ColSlideGlow, 48, thick * 2.6f);
			frameColor = ColSlideOk;
		}
		else if (centerGcd is { NeedsCast: true, Badge: "다음" })
		{
			frameColor = ColStop; // The next GCD needs a cast: stand still when it fires.
		}

		draw.AddCircle(center, r, slideSafe ? ColRingSafe : ColRingBase, 48, thick);

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

		// One window (user decision 2026-10-14): from the slidecast point of the current cast to the end of the GCD you may
		// move AND your next GCD press is accepted into the stack. Drawn as an inner green arc with a tick at its start.
		if (player != null && player.IsCasting && total > 0f)
		{
			var slide = Math.Clamp(settings.HudSlidecastWindow, 0f, 2f);
			var at = Math.Clamp((elapsed + castLeft - slide) / total, 0f, 1f);
			var from = start + MathF.Tau * at;
			var inner = r - thick * 0.5f - 2f * s;
			draw.PathArcTo(center, inner, from, start + MathF.Tau, 24);
			draw.PathStroke(ColSlideOk, ImDrawFlags.None, 2f * s);
			var dir = new Vector2(MathF.Cos(from), MathF.Sin(from));
			draw.AddLine(center + dir * (inner - 4f * s), center + dir * (r + thick * 0.5f), ColSlide, 2f * s);
		}

		// Center: the GCD being cast (player or RSR), else RSR's next GCD.
		var half = CenterIcon * 0.5f * s;
		var min = center - new Vector2(half, half);
		if (centerGcd is { } cg && IconSet.GetTexture((ActionID)cg.ActionId, out var tex))
		{
			draw.AddImage(tex.Handle, min, center + new Vector2(half, half));
		}
		draw.AddRect(min, center + new Vector2(half, half), frameColor, 5f * s, ImDrawFlags.None, 2f * s);
		if (centerGcd is { } badge)
		{
			var bsize = ImGui.CalcTextSize(badge.Badge) * 0.8f * s;
			draw.AddText(font, small, new Vector2(center.X - bsize.X * 0.5f, center.Y - r - thick - small - 2f * s), ColLabel, badge.Badge);
		}

		// Time under the ring (gap usage is shown on the oGCD slots themselves).
		var timeText = remain > 0f ? $"{remain:F2}s" : "준비";
		var size = ImGui.CalcTextSize(timeText) * s;
		draw.AddText(font, fontSize, new Vector2(center.X - size.X * 0.5f, center.Y + r + thick + 2f * s), ColText, timeText);
		if (slideSafe)
		{
			const string moveText = "이동 가능";
			var msize = ImGui.CalcTextSize(moveText) * 0.8f * s;
			draw.AddText(font, small, new Vector2(center.X - msize.X * 0.5f, center.Y + r + thick + 2f * s + fontSize), ColSlideOk, moveText);
		}
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
		// A cast-time GCD without Swiftcast: orange frame + "정지" so the player knows to stand still when it fires.
		var frameCol = entry.NeedsCast ? ColStop : entry.Dim ? ColDim : frame;
		draw.AddRect(pos, max, frameCol, 4f, ImDrawFlags.None, entry.NeedsCast ? 2f : entry.Dim ? 1f : 1.5f);
		if (entry.NeedsCast)
		{
			draw.AddRectFilled(new Vector2(pos.X, pos.Y), new Vector2(pos.X + size, pos.Y + 4f), ColStop);
		}

		var badge = entry.NeedsCast ? entry.Badge + "·정지" : entry.Badge;
		var badgeSize = ImGui.CalcTextSize(badge) * 0.8f;
		draw.AddText(font, small, new Vector2(pos.X + (size - badgeSize.X) * 0.5f, max.Y + 1f), entry.NeedsCast ? ColStop : entry.Dim ? ColDim : ColLabel, badge);
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
