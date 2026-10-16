using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace RotationSolver.Himechan;

/// <summary>
/// #37 cast assist (slidecast lock, user request 2026-10-16). While armed, a movement input during a cast is held
/// back until the slidecast window opens (HUD "이동·선입력 가능 시작"), then released at once: a movement key or the
/// mouse "walk" gesture held early starts the character exactly at the slide point, and the cast never breaks.
/// Armed = mode Toggle with the toggle on, or mode MouseHold while both mouse buttons are down (the game's walk-forward
/// gesture; keyboard movement stays vanilla and breaks the cast as usual). Outside a cast nothing is blocked.
/// Emergency release is RSR's own (Service.CanMove honours DataCenter.NoPoslock): the unlock modifier key or gamepad R1.
/// Owns Service.CanMove while active; RSR's MovingUpdater steps aside (HIMECHAN-HOOK: Move). Requires RSR's
/// PoslockCasting in the Himechan profile (switched on here, switched back off when the assist is turned off).
/// </summary>
internal static class HimechanCastAssist
{
	public const int ModeOff = 0;
	public const int ModeMouseHold = 1;
	public const int ModeToggle = 2;

	private static bool _owned;
	private static bool _lockingNow;
	private static bool _wasInCombat;

	/// <summary>The assist is switched on and would lock the next cast.</summary>
	public static bool Armed { get; private set; }

	/// <summary>Movement is blocked this frame (cast running, slide point not reached).</summary>
	public static bool LockingNow => _lockingNow;

	/// <summary>True while the assist owns the movement flag (RSR's MovingUpdater is skipped).</summary>
	public static bool Active
	{
		get
		{
			var settings = HimechanSettings.Current;
			return settings.Enabled && settings.CastAssistMode != ModeOff && HimechanProfile.IsActive;
		}
	}

	public static string StatusText
	{
		get
		{
			var settings = HimechanSettings.Current;
			if (!Active)
			{
				return settings.CastAssistMode == ModeOff ? "꺼짐" : "대기 (히메짱 프로필이 적용되면 동작)";
			}

			if (!Armed)
			{
				return settings.CastAssistMode == ModeToggle ? "토글 꺼짐 (/히메짱슬캐)" : "대기 (마우스 좌+우를 누르면 동작)";
			}

			return _lockingNow ? "동작 중: 이동 보류" : "동작 중";
		}
	}

	public static void Update()
	{
		var settings = HimechanSettings.Current;
		if (!Active)
		{
			Release();
			return;
		}

		EnsureProfileConfig(settings);

		var inCombat = DataCenter.InCombat;
		if (settings.CastAssistMode == ModeToggle && settings.CastAssistAutoOff && settings.CastAssistToggleOn && _wasInCombat && !inCombat)
		{
			settings.CastAssistToggleOn = false;
			HimechanProfile.Notify("CAST ASSIST를 껐습니다 (전투 종료).");
		}
		_wasInCombat = inCombat;

		Armed = settings.CastAssistMode switch
		{
			ModeToggle => settings.CastAssistToggleOn,
			ModeMouseHold => InputManager.IsLeftMouseDown() && InputManager.IsRightMouseDown(),
			_ => false,
		};

		var lockNow = false;
		var left = 0f;
		var player = Player.Object;
		if (Armed && player != null && player.IsCasting && player.TotalCastTime > 0f
			&& Array.IndexOf(ActionBasicInfo.ActionsNoNeedCasting, player.CastActionId) < 0)
		{
			left = player.TotalCastTime - player.CurrentCastTime;
			lockNow = left > Math.Clamp(settings.HudSlidecastWindow, 0.2f, 1.0f);
		}

		// RSR's setter already ORs in NoPoslock (unlock key / gamepad R1 / occupied), so the emergency release costs nothing here.
		Service.CanMove = !lockNow;
		_owned = true;
		if (lockNow != _lockingNow)
		{
			_lockingNow = lockNow;
			HimechanLog.Write("MOVE", lockNow
				? string.Format("CAST ASSIST LOCK id={0} left={1:F2}", player?.CastActionId ?? 0, left)
				: string.Format("CAST ASSIST RELEASE left={0:F2}", left));
		}
	}

	/// <summary>Give the movement flag (and RSR's lock option) back. Safe to call repeatedly.</summary>
	public static void Release()
	{
		Armed = false;
		_wasInCombat = false;
		if (_owned)
		{
			_owned = false;
			_lockingNow = false;
			Service.CanMove = true;
		}

		var settings = HimechanSettings.Current;
		if (settings.CastAssistOwnsPoslock && HimechanProfile.IsActive)
		{
			settings.CastAssistOwnsPoslock = false;
			Service.Config.PoslockCasting.Value = false;
			HimechanSettings.Save();
			PluginLog.Information("[Himechan] Cast assist off: RSR movement lock switched back off in the Himechan profile.");
		}
	}

	/// <summary>Service.CanMove can only lock while PoslockCasting is on; the mouse L+R unlock would cancel the arming gesture.</summary>
	private static void EnsureProfileConfig(HimechanSettings settings)
	{
		var config = Service.Config;
		if (config.PoslockCasting && !config.PosLockMouse && settings.CastAssistOwnsPoslock)
		{
			return;
		}

		config.PoslockCasting.Value = true;
		config.PosLockMouse = false;
		settings.CastAssistOwnsPoslock = true;
		HimechanSettings.Save();
		PluginLog.Information("[Himechan] Cast assist on: RSR movement lock switched on in the Himechan profile (mouse L+R unlock off).");
	}
}

/// <summary>Yellow "CAST ASSIST" text shown while the assist is armed. Unlocked: always visible and draggable (position saved).</summary>
internal sealed class HimechanCastAssistWindow : Window
{
	private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar
		| ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground
		| ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoSavedSettings;

	private const string Text = "CAST ASSIST";

	private static readonly uint ColText = Color(1f, 0.86f, 0.2f, 1f);
	private static readonly uint ColTextDim = Color(1f, 0.86f, 0.2f, 0.45f);
	private static readonly uint ColShadow = Color(0f, 0f, 0f, 0.85f);
	private static readonly uint ColPanel = Color(0.04f, 0.05f, 0.08f, 0.55f);
	private static readonly uint ColUnlocked = Color(1f, 0.83f, 0.30f, 0.9f);

	private Vector2 _lastSavedPos = new(-1, -1);

	public HimechanCastAssistWindow() : base("히메짱 CAST ASSIST###HimechanCastAssist", BaseFlags)
	{
		IsOpen = true;
		RespectCloseHotkey = false;
	}

	public override bool DrawConditions()
	{
		var settings = HimechanSettings.Current;
		if (!settings.Enabled || !Svc.ClientState.IsLoggedIn || settings.CastAssistMode == HimechanCastAssist.ModeOff)
		{
			return false;
		}

		return !settings.CastAssistTextLocked || HimechanCastAssist.Armed;
	}

	public override void PreDraw()
	{
		var settings = HimechanSettings.Current;
		Flags = BaseFlags;
		if (settings.CastAssistTextLocked)
		{
			Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMouseInputs;
		}

		if (settings.CastAssistPosX >= 0 && settings.CastAssistPosY >= 0)
		{
			Position = new Vector2(settings.CastAssistPosX, settings.CastAssistPosY);
			PositionCondition = settings.CastAssistTextLocked ? ImGuiCond.Always : ImGuiCond.Appearing;
		}
		else
		{
			var viewport = ImGui.GetMainViewport();
			Position = viewport.Pos + new Vector2(viewport.Size.X * 0.5f - 90f * settings.CastAssistTextScale, viewport.Size.Y * 0.4f);
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
			PluginLog.Warning($"[Himechan] Cast assist text draw failed: {ex.Message}");
		}
	}

	private void DrawCore()
	{
		var settings = HimechanSettings.Current;
		var s = Math.Clamp(settings.CastAssistTextScale, 0.5f, 3f);
		var font = ImGui.GetFont();
		var fontSize = ImGui.GetFontSize() * s;
		var textSize = ImGui.CalcTextSize(Text) * s;
		var pad = new Vector2(10f, 6f) * s;
		var hint = settings.CastAssistTextLocked ? Vector2.Zero : ImGui.CalcTextSize("끌어서 옮긴 뒤 /히메짱 창에서 잠금");
		var box = new Vector2(Math.Max(textSize.X, hint.X) + pad.X * 2f, textSize.Y + pad.Y * 2f + (hint.Y > 0 ? hint.Y + 4f : 0f));

		var draw = ImGui.GetWindowDrawList();
		var origin = ImGui.GetCursorScreenPos();
		ImGui.Dummy(box);

		if (!settings.CastAssistTextLocked)
		{
			draw.AddRectFilled(origin, origin + box, ColPanel, 6f * s);
			draw.AddRect(origin, origin + box, ColUnlocked, 6f * s, ImDrawFlags.None, 1.5f);
			draw.AddText(font, ImGui.GetFontSize(), origin + new Vector2(pad.X, pad.Y * 2f + textSize.Y), ColUnlocked, "끌어서 옮긴 뒤 /히메짱 창에서 잠금");
		}

		var pos = origin + pad;
		var color = HimechanCastAssist.Armed ? ColText : ColTextDim;
		draw.AddText(font, fontSize, pos + new Vector2(1.5f, 1.5f) * s, ColShadow, Text);
		draw.AddText(font, fontSize, pos, color, Text);
		if (HimechanCastAssist.LockingNow)
		{
			var y = pos.Y + textSize.Y + 1f * s;
			draw.AddLine(new Vector2(pos.X, y), new Vector2(pos.X + textSize.X, y), ColText, 2f * s);
		}

		SavePositionIfMoved(settings);
	}

	private void SavePositionIfMoved(HimechanSettings settings)
	{
		if (settings.CastAssistTextLocked)
		{
			return;
		}

		var pos = ImGui.GetWindowPos();
		if (Vector2.Distance(pos, _lastSavedPos) < 0.5f || ImGui.IsMouseDown(ImGuiMouseButton.Left))
		{
			return;
		}

		_lastSavedPos = pos;
		settings.CastAssistPosX = pos.X;
		settings.CastAssistPosY = pos.Y;
		HimechanSettings.Save();
	}

	private static uint Color(float r, float g, float b, float a) => ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));
}
