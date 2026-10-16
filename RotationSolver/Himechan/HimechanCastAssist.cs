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
/// gesture; keyboard movement stays vanilla and breaks the cast as usual). Between casts only the last CastAssistStopMargin
/// seconds before a planned cast GCD are blocked (a cast started while moving is interrupted at once), so with the walk
/// gesture held the loop is cast → slide → run through the gap → stop → cast.
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

		var slide = Math.Clamp(settings.HudSlidecastWindow, 0.2f, 1.0f);
		var stopMargin = Math.Clamp(settings.CastAssistStopMargin, 0.05f, 0.5f);
		var player = Player.Object;
		var casting = player != null && player.IsCasting && player.TotalCastTime > 0f;
		var left = casting ? player!.TotalCastTime - player.CurrentCastTime : 0f;

		// Game log 2026-10-16 (7.5.7.210): a cast that starts while the character is already moving is interrupted within
		// ~60 ms, so locking only once the cast is running is too late. Diagnostic for that case.
		if (_wasCasting && !casting && _lastCastLeft > slide + 0.15f)
		{
			HimechanLog.Write("MOVE", string.Format("CAST ASSIST INTERRUPTED left={0:F2}", _lastCastLeft));
		}
		_wasCasting = casting;
		_lastCastLeft = left;

		var phase = PhaseNone;
		uint planned = 0;
		if (Armed && player != null)
		{
			if (casting)
			{
				if (Array.IndexOf(ActionBasicInfo.ActionsNoNeedCasting, player.CastActionId) < 0 && left > slide)
				{
					phase = PhaseCast;
				}
			}
			else
			{
				// Pre-cast stop: a cast is expected and the GCD is about to be ready, so stand still before it fires.
				// Expected = planned cast, or nothing planned (B, user decision 2026-10-16: manual play needs no pre-queue),
				// or always (option C). Held for at most PreStopLimit once the GCD is ready, in case no cast ever comes.
				var plan = PlannedCast(out planned);
				var expectCast = settings.CastAssistAlwaysStop
					|| plan == RebornRotations.Healer.WHM_Himechan.NextGcdCast
					|| plan == RebornRotations.Healer.WHM_Himechan.NextGcdNone;
				var remain = DataCenter.DefaultGCDRemain;
				if (expectCast && remain <= stopMargin)
				{
					if (_preStopSince == 0)
					{
						_preStopSince = Environment.TickCount64;
					}

					if (Environment.TickCount64 - _preStopSince <= PreStopLimitMs)
					{
						phase = PhasePreCast;
					}
				}
				else
				{
					_preStopSince = 0;
				}
			}
		}

		if (casting || !Armed)
		{
			_preStopSince = 0;
		}

		var lockNow = phase != PhaseNone;
		// RSR's setter already ORs in NoPoslock (unlock key / gamepad R1 / occupied), so the emergency release costs nothing here.
		Service.CanMove = !lockNow;
		_owned = true;
		if (phase != _phase)
		{
			_phase = phase;
			_lockingNow = lockNow;
			HimechanLog.Write("MOVE", phase switch
			{
				PhaseCast => string.Format("CAST ASSIST LOCK id={0} left={1:F2}", player?.CastActionId ?? 0, left),
				PhasePreCast => string.Format("CAST ASSIST STOP next={0} gcd={1:F2}", planned, DataCenter.DefaultGCDRemain),
				_ => string.Format("CAST ASSIST RELEASE left={0:F2}", left),
			});
		}
	}

	private const int PhaseNone = 0;
	private const int PhasePreCast = 1;
	private const int PhaseCast = 2;
	private const long PreStopLimitMs = 1000;

	private static int _phase;
	private static long _preStopSince;
	private static bool _wasCasting;
	private static float _lastCastLeft;

	/// <summary>What the next GCD needs (HUD determination: game queue → RSR next → request → command); None when unknown.</summary>
	private static int PlannedCast(out uint id)
	{
		id = 0;
		try
		{
			return DataCenter.CurrentRotation is RebornRotations.Healer.WHM_Himechan whm
				? whm.NextGcdPlan(out id)
				: RebornRotations.Healer.WHM_Himechan.NextGcdNone;
		}
		catch (Exception ex)
		{
			PluginLog.Debug($"[Himechan] Cast assist plan check failed: {ex.Message}");
			return RebornRotations.Healer.WHM_Himechan.NextGcdNone;
		}
	}

	/// <summary>Give the movement flag (and RSR's lock option) back. Safe to call repeatedly.</summary>
	public static void Release()
	{
		Armed = false;
		_wasInCombat = false;
		_preStopSince = 0;
		_phase = PhaseNone;
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
