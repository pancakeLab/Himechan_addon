using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RotationSolver.Himechan;

/// <summary>
/// #37 cast assist (slidecast lock, user request 2026-10-16). While armed, a movement input during a cast is held
/// back until the slidecast window opens (HUD "이동·선입력 가능 시작"), then released at once: a movement key or the
/// mouse "walk" gesture held early starts the character exactly at the slide point, and the cast never breaks.
/// Armed = mode Toggle with the toggle on, or mode MouseHold while both mouse buttons are down (the game's walk-forward
/// gesture; keyboard movement stays vanilla and breaks the cast as usual). Between casts only the last CastAssistStopMargin
/// seconds before a planned cast GCD are blocked (a cast started while moving is interrupted at once), so with the walk
/// gesture held the loop is cast → slide → run through the gap → stop → cast. Idle movement is free (user decision
/// 2026-10-16 evening): a cast GCD pressed while moving is held for a few frames, the character stops, then it is re-issued.
/// Emergency release is RSR's own (Service.CanMove honours DataCenter.NoPoslock): the unlock modifier key or gamepad R1.
/// Owns Service.CanMove while active; RSR's MovingUpdater steps aside (HIMECHAN-HOOK: Move). Requires RSR's
/// PoslockCasting in the Himechan profile (switched on here, switched back off when the assist is turned off).
/// Measurement logs (7.5.7.213, for tuning the stop margin): MEASURE START / FX / OK / INTERRUPTED, see Measure.
/// </summary>
internal static class HimechanCastAssist
{
	public const int ModeOff = 0;
	public const int ModeMouseHold = 1;
	public const int ModeToggle = 2;

	private const int PhaseNone = 0;
	private const int PhasePreCast = 1;
	private const int PhaseCast = 2;
	private const long PreStopLimitMs = 1000;

	// A cast GCD pressed while moving: stand still this long before re-issuing it (3 frames at 60 fps; measured 0.05s works).
	private const long HoldMs = 50;
	private const long HoldLimitMs = 600;

	private const long KeyBreakMs = 300;
	private static long _keyBreakUntil;

	private static bool _owned;
	private static bool _lockingNow;
	private static bool _wasInCombat;
	private static int _phase;
	private static long _preStopSince;

	// Held cast press (idle intercept).
	private static uint _heldId;
	private static ulong _heldTarget;
	private static long _heldAt;
	private static bool _replaying;

	// Measurement state.
	private static bool _wasCasting;
	private static float _lastCastLeft;
	private static uint _castId;
	private static long _castStartedAt;
	private static long _stopEngagedAt;      // 0 = no pre-stop before this cast
	private static float _stopGcdRemain;
	private static bool _movingAtStop;
	private static bool _queuedLastFrame;
	private static Vector3 _lastPos;
	private static bool _movingLastFrame;
	private static float _firstMoveLeft = -1f; // cast-bar time left when the character first moved after the release
	private static bool _releasedThisCast;
	private static float _fxLeft = -1f;
	private static bool _fxArrived;          // the server resolved the current cast: moving is certainly safe now
	private static ulong _castTarget;
	private static string _castSource = "manual";
	private static bool _pathsLogged;

	/// <summary>The assist is switched on and would lock the next cast.</summary>
	public static bool Armed { get; private set; }

	/// <summary>Movement is blocked this frame (cast running, slide point not reached, or pre-cast stop).</summary>
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

			return _phase switch
			{
				PhaseCast => "동작 중: 시전 중 이동 보류",
				PhasePreCast => "동작 중: 시전 전 정지",
				_ => "동작 중",
			};
		}
	}

	public static unsafe void Update()
	{
		var settings = HimechanSettings.Current;
		if (!Active)
		{
			Release();
			return;
		}

		// Direct lock (7.5.7.403): the movement counter itself, so RSR's unlock modifier (Ctrl, user complaint 2026-10-21:
		// every Ctrl+hotkey mid-cast dropped the lock) no longer applies. RSR's setter path stays as the fallback.
		var direct = DirectLockAvailable && !settings.CastAssistAllowRsrUnlockKey;
		if (direct)
		{
			ReleaseOwnedPoslock(settings);
		}
		else
		{
			EnsureProfileConfig(settings);
		}

		if (!_pathsLogged)
		{
			_pathsLogged = true;
			HimechanLog.Write("MOVE", string.Format("CAST ASSIST PATHS lock={0} mouseWin32={1} keyInputData={2}",
				direct ? "direct" : "rsr-setter", !_win32Failed, !_inputDataFailed));
		}

		var inCombat = DataCenter.InCombat;
		if (settings.CastAssistMode == ModeToggle && settings.CastAssistAutoOff && settings.CastAssistToggleOn && _wasInCombat && !inCombat)
		{
			settings.CastAssistToggleOn = false;
			HimechanProfile.Notify("CAST ASSIST를 껐습니다 (전투 종료).");
		}
		_wasInCombat = inCombat;

		// Mouse mode: the lock stops every movement input, so a keyboard movement key while the buttons are held (user report
		// 2026-10-21: could not break a hard-cast Raise with the keyboard) counts as "break the cast now" and disarms for a moment.
		var buttons = settings.CastAssistMode == ModeMouseHold && BothMouseButtonsDown();
		var now = Environment.TickCount64;
		if (buttons && settings.CastAssistKeyboardBreaks)
		{
			var keySource = KeyboardMoveIntent();
			if (keySource != null)
			{
				if (now >= _keyBreakUntil)
				{
					HimechanLog.Write("MOVE", "CAST ASSIST KEY BREAK src=" + keySource);
				}
				_keyBreakUntil = now + KeyBreakMs;
			}
		}

		Armed = settings.CastAssistMode switch
		{
			ModeToggle => settings.CastAssistToggleOn,
			ModeMouseHold => buttons && now >= _keyBreakUntil,
			_ => false,
		};

		var slide = Math.Clamp(settings.HudSlidecastWindow, 0.2f, 1.0f);
		var stopMargin = Math.Clamp(settings.CastAssistStopMargin, 0f, 0.5f);
		var player = Player.Object;
		var casting = player != null && player.IsCasting && player.TotalCastTime > 0f;
		var left = casting ? player!.TotalCastTime - player.CurrentCastTime : 0f;

		// Movement sample: did the character actually move since the last frame?
		var moving = false;
		if (player != null)
		{
			var pos = player.Position;
			moving = Vector3.DistanceSquared(pos, _lastPos) > 0.0001f;
			_lastPos = pos;
		}

		Measure(player, casting, left, slide, stopMargin, moving);

		var phase = PhaseNone;
		uint planned = 0;
		if (Armed && player != null)
		{
			if (casting)
			{
				// Release when the server has resolved the cast (its effect packet arrived: certain), otherwise only at
				// slide - 0.05. Game log 2026-10-21: one Glare released at 0.47 was still rejected (effect would have come at 0.43).
				var slideSafe = _fxArrived || left <= Math.Max(0.3f, slide - 0.05f);
				if (Array.IndexOf(ActionBasicInfo.ActionsNoNeedCasting, player.CastActionId) < 0 && !slideSafe)
				{
					phase = PhaseCast;
				}
			}
			else if (_heldId != 0)
			{
				phase = PhasePreCast; // a cast press is waiting for the character to stand still
			}
			else
			{
				// Pre-cast stop: a cast is expected and the GCD is about to be ready, so stand still before it fires.
				// Expected = planned cast (game queue / RSR next / request / command), optionally also "nothing planned"
				// (CastAssistStopWhenUnplanned), or always (CastAssistAlwaysStop). Held for at most PreStopLimit once the
				// GCD is ready, in case no cast ever comes.
				var plan = PlannedCast(out planned);
				var expectCast = settings.CastAssistAlwaysStop
					|| plan == RebornRotations.Healer.WHM_Himechan.NextGcdCast
					|| (settings.CastAssistStopWhenUnplanned && plan == RebornRotations.Healer.WHM_Himechan.NextGcdNone);
				var remain = DataCenter.DefaultGCDRemain;
				// Sticky (game log 2026-10-21): between the game queue firing and IsCasting turning on there can be one frame
				// with nothing planned; releasing there let the character step once and the cast was interrupted at 0.11s.
				if ((expectCast || _preStopSince != 0) && remain <= stopMargin)
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
		ApplyLock(lockNow, direct);
		if (phase != _phase)
		{
			if (phase == PhasePreCast)
			{
				_stopEngagedAt = Environment.TickCount64;
				_stopGcdRemain = DataCenter.DefaultGCDRemain;
				_movingAtStop = _movingLastFrame;
			}

			if (_phase == PhaseCast && phase == PhaseNone && casting)
			{
				_releasedThisCast = true;
			}

			_phase = phase;
			_lockingNow = lockNow;
			HimechanLog.Write("MOVE", phase switch
			{
				PhaseCast => string.Format("CAST ASSIST LOCK id={0} left={1:F2}", player?.CastActionId ?? 0, left),
				PhasePreCast => string.Format("CAST ASSIST STOP next={0} gcd={1:F2} margin={2:F2} moving={3} dt={4:F0}ms{5}",
					planned, DataCenter.DefaultGCDRemain, stopMargin, _movingLastFrame, Svc.Framework.UpdateDelta.TotalMilliseconds,
					_heldId != 0 ? " held=" + Service.GetAdjustedActionId(_heldId) : string.Empty),
				_ => string.Format("CAST ASSIST RELEASE left={0:F2}", left),
			});
		}

		ReplayHeldCast(player, casting, moving);

		_movingLastFrame = moving;
		var manager = ActionManager.Instance();
		_queuedLastFrame = manager != null && manager->ActionQueued;
	}

	/// <summary>
	/// Input hook (after the opener / DoT / smart-target checks): a cast GCD pressed directly while the character is
	/// moving would start and be interrupted within ~60 ms. While armed we swallow the press, stop the character and
	/// re-issue it a few frames later (ReplayHeldCast). The game's own queue executions (useType 1) are not touched:
	/// the pre-cast stop already knows about them.
	/// </summary>
	public static bool TryHoldCastInput(uint actionType, uint actionId, ulong targetObjectId, uint useType)
	{
		if (_replaying || actionType != 1 || useType == 1 || !Active || !Armed || _heldId != 0)
		{
			return false;
		}

		var player = Player.Object;
		if (player == null || player.IsCasting || !_movingLastFrame)
		{
			return false;
		}

		var adjusted = Service.GetAdjustedActionId(actionId);
		if (!RebornRotations.Healer.WHM_Himechan.IsGcdActionId(adjusted) || !RebornRotations.Healer.WHM_Himechan.NeedsCastFor(adjusted)
			|| Array.IndexOf(ActionBasicInfo.ActionsNoNeedCasting, adjusted) >= 0)
		{
			return false;
		}

		// With more GCD left than the stop margin the game only queues the press, and the pre-cast stop handles it.
		if (DataCenter.DefaultGCDRemain > Math.Clamp(HimechanSettings.Current.CastAssistStopMargin, 0f, 0.5f) + 0.02f)
		{
			return false;
		}

		_heldId = actionId;
		_heldTarget = targetObjectId;
		_heldAt = Environment.TickCount64;
		ApplyLock(true, DirectLockAvailable && !HimechanSettings.Current.CastAssistAllowRsrUnlockKey); // stop this very frame
		HimechanLog.Write("MOVE", string.Format("CAST ASSIST HOLD id={0} gcd={1:F2}", adjusted, DataCenter.DefaultGCDRemain));
		return true;
	}

	private static unsafe void ReplayHeldCast(IBattleChara? player, bool casting, bool moving)
	{
		if (_heldId == 0)
		{
			return;
		}

		var waited = Environment.TickCount64 - _heldAt;
		if (casting || waited > HoldLimitMs || player == null)
		{
			HimechanLog.Write("MOVE", string.Format("CAST ASSIST HOLD DROP id={0} waited={1}ms armed={2} casting={3}",
				Service.GetAdjustedActionId(_heldId), waited, Armed, casting));
			_heldId = 0;
			return;
		}

		// Game log 2026-10-21: releasing the mouse buttons right after the press (stop walking, then heal) disarmed the assist
		// and the press was dropped. Disarmed = give the press back at once, exactly as the game would have taken it.
		var disarmed = !Armed;
		if (!disarmed && (waited < HoldMs || moving))
		{
			return;
		}

		var manager = ActionManager.Instance();
		if (manager == null)
		{
			_heldId = 0;
			return;
		}

		_replaying = true;
		bool ok;
		try
		{
			ok = manager->UseAction(ActionType.Action, _heldId, _heldTarget);
		}
		finally
		{
			_replaying = false;
		}

		_castSource = "held";
		HimechanLog.Write("MOVE", string.Format("CAST ASSIST HOLD REPLAY id={0} waited={1}ms ok={2}{3}",
			Service.GetAdjustedActionId(_heldId), waited, ok, disarmed ? " disarmed" : string.Empty));
		_heldId = 0;
	}

	/// <summary>
	/// Measurement for the stop-margin tuning (user request 2026-10-16). One line per cast while armed:
	/// START (who issued it, time since the pre-stop engaged, GCD remaining, was the character moving the frame before),
	/// FX (cast-bar time left when the server's effect arrived = server lead), OK / INTERRUPTED (with the realised slide point).
	/// </summary>
	private static void Measure(IBattleChara? player, bool casting, float left, float slide, float stopMargin, bool moving)
	{
		if (casting && !_wasCasting && player != null)
		{
			_castId = player.CastActionId;
			_castTarget = player.CastTargetObjectId;
			_castStartedAt = Environment.TickCount64;
			_firstMoveLeft = -1f;
			_releasedThisCast = false;
			_fxLeft = -1f;
			_fxArrived = false;
			if (Armed)
			{
				var sinceStop = _stopEngagedAt > 0 ? (Environment.TickCount64 - _stopEngagedAt) / 1000f : -1f;
				var source = _castSource == "held" ? "held"
					: _queuedLastFrame ? "queue"
					: DataCenter.State && Updaters.ActionUpdater.NextGCDAction?.AdjustedID == _castId ? "rsr" : "manual";
				var tweak = ActionManagerEx.Instance.AnimationLockTweak;
				HimechanLog.Write("MOVE", string.Format(
					"CAST ASSIST MEASURE START id={0} bar={1:F2} src={2} sinceStop={3:F2} stopGcd={4:F2} margin={5:F2} movingBefore={6} movingAtStop={7} rtt={8:F3}",
					_castId, player.TotalCastTime, source, sinceStop, _stopGcdRemain, stopMargin, _movingLastFrame, _movingAtStop, tweak.DelayAverage));
			}
			_castSource = "manual";
		}

		if (casting && _releasedThisCast && moving && _firstMoveLeft < 0f)
		{
			_firstMoveLeft = left;
		}

		if (_wasCasting && !casting && _castId != 0)
		{
			// The client ends the cast at the bar end (left 0); the server effect arrives around 0.47. Anything else is a cancel.
			var interrupted = _lastCastLeft > 0.1f;
			var sinceStart = (Environment.TickCount64 - _castStartedAt) / 1000f;
			HimechanLog.Write("MOVE", interrupted
				? string.Format("CAST ASSIST MEASURE INTERRUPTED id={0} left={1:F2} sinceStart={2:F2} hadStop={3} armed={4} slideAt={5:F2} moving={6} bmrCancel={7}{8}",
					_castId, _lastCastLeft, sinceStart, _stopEngagedAt > 0, Armed, _firstMoveLeft, _movingLastFrame,
					DataCenter.BMRForceCancelCast || DataCenter.BMRForceCancelCastAI, RaiseTargetState())
				: string.Format("CAST ASSIST MEASURE OK id={0} slideAt={1:F2} fxLeft={2:F2} armed={3}",
					_castId, _firstMoveLeft, _fxLeft, Armed));
			_castId = 0;
			_stopEngagedAt = 0;
		}

		_wasCasting = casting;
		_lastCastLeft = left;
	}

	/// <summary>Called from the action-effect event: the server resolved the cast this many cast-bar seconds before the bar ends.</summary>
	public static void OnEffect(uint actionId)
	{
		if (!Active || actionId != _castId || _castId == 0)
		{
			return;
		}

		var player = Player.Object;
		_fxLeft = player != null && player.IsCasting ? player.TotalCastTime - player.CurrentCastTime : 0f;
		_fxArrived = true;
		HimechanLog.Write("MOVE", string.Format("CAST ASSIST MEASURE FX id={0} left={1:F2} sinceStart={2:F2}",
			actionId, _fxLeft, (Environment.TickCount64 - _castStartedAt) / 1000f));
	}

	/// <summary>For an interrupted Raise: was the target already raised by someone else (the likely cause)?</summary>
	private static string RaiseTargetState()
	{
		if (_castId != (uint)ActionID.RaisePvE || _castTarget == 0)
		{
			return string.Empty;
		}

		try
		{
			var target = Svc.Objects.SearchById(_castTarget) as IBattleChara;
			return target == null ? " target=gone"
				: string.Format(" targetDead={0} targetRaised={1}", target.IsDead, target.HasStatus(false, StatusID.Raise, StatusID.ResurrectionDenied));
		}
		catch
		{
			return string.Empty;
		}
	}

	/// <summary>Give the movement flag (and RSR's lock option) back. Safe to call repeatedly.</summary>
	public static void Release()
	{
		Armed = false;
		_wasInCombat = false;
		_preStopSince = 0;
		_phase = PhaseNone;
		_stopEngagedAt = 0;
		_heldId = 0;
		_lockingNow = false;
		ReleaseDirectLock();
		if (_owned)
		{
			_owned = false;
			Service.CanMove = true;
		}

		ReleaseOwnedPoslock(HimechanSettings.Current);
	}

	private static void ReleaseOwnedPoslock(HimechanSettings settings)
	{
		if (settings.CastAssistOwnsPoslock && HimechanProfile.IsActive)
		{
			settings.CastAssistOwnsPoslock = false;
			Service.Config.PoslockCasting.Value = false;
			HimechanSettings.Save();
			PluginLog.Information("[Himechan] Cast assist: RSR movement lock switched back off in the Himechan profile.");
		}
	}

	#region Movement flag
	// The game's "movement disabled" counter that RSR's Service.CanMove wraps. RSR's setter ORs in NoPoslock (unlock modifier
	// key, gamepad R1, cutscene), so the counter is read through RSR's private pointer instead: same signature, no second
	// scan, and the Himechan profile no longer needs PoslockCasting. Falls back to the setter when the field is not found.
	private static nint _counterPtr;
	private static bool _counterLookedUp;
	private static bool _directCounted;

	private static unsafe bool DirectLockAvailable
	{
		get
		{
			if (!_counterLookedUp)
			{
				_counterLookedUp = true;
				try
				{
					var field = typeof(Service).GetField("forceDisableMovementPtr", BindingFlags.NonPublic | BindingFlags.Static);
					var value = field?.GetValue(null);
					_counterPtr = value is nint p ? p : 0;
				}
				catch (Exception ex)
				{
					_counterPtr = 0;
					PluginLog.Warning($"[Himechan] Cast assist: movement counter unavailable, using RSR's setter: {ex.Message}");
				}
			}

			return _counterPtr != 0;
		}
	}

	private static unsafe ref int Counter => ref *(int*)(_counterPtr + 4);

	/// <summary>Direct path: only a cutscene or gamepad R1 releases; keyboard break and the mouse buttons are handled by Armed.</summary>
	private static unsafe void ApplyLock(bool lockNow, bool direct)
	{
		if (!direct)
		{
			ReleaseDirectLock();
			Service.CanMove = !lockNow;
			_owned = true;
			return;
		}

		if (_owned)
		{
			_owned = false;
			Service.CanMove = true;
		}

		if (lockNow && (Svc.Condition[ConditionFlag.OccupiedInEvent] || Svc.GamepadState.Raw(GamepadButtons.R1) >= 0.5f))
		{
			lockNow = false;
		}

		if (lockNow && !_directCounted)
		{
			Counter++;
			_directCounted = true;
		}
		else if (!lockNow)
		{
			ReleaseDirectLock();
		}
	}

	private static unsafe void ReleaseDirectLock()
	{
		if (!_directCounted)
		{
			return;
		}

		_directCounted = false;
		if (_counterPtr != 0 && Counter > 0)
		{
			Counter--;
		}
	}
	#endregion

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

	#region Mouse state
	// The game's InputManager reflects the previous tick, so a press made between frames is seen one frame late and the
	// character already moved for that frame (game log 2026-10-16: pressing L+R mid-cast cancelled the cast). The Win32
	// state is live, and Framework.Update runs before the game's own tick, so the lock lands in the same frame as the press.
	private const int VkLButton = 0x01;
	private const int VkRButton = 0x02;
	private static bool _win32Failed;

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vKey);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

	private static readonly InputId[] MoveInputs =
	[
		InputId.MOVE_FORE, InputId.MOVE_BACK, InputId.MOVE_LEFT, InputId.MOVE_RIGHT, InputId.MOVE_STRIFE_L, InputId.MOVE_STRIFE_R,
	];

	// Default keybinds for the Win32 fallback: W A S D Q E and the arrow keys.
	private static readonly int[] MoveVirtualKeys = [0x57, 0x41, 0x53, 0x44, 0x51, 0x45, 0x25, 0x26, 0x27, 0x28];
	private static bool _inputDataFailed;

	private static readonly VirtualKey[] MoveDalamudKeys =
	[
		VirtualKey.W, VirtualKey.A, VirtualKey.S, VirtualKey.D, VirtualKey.Q, VirtualKey.E,
		VirtualKey.LEFT, VirtualKey.UP, VirtualKey.RIGHT, VirtualKey.DOWN,
	];

	/// <summary>
	/// A keyboard movement key is down. Three sources, because the game may not register movement inputs while the
	/// movement counter is set (game log 2026-10-21: no KEY BREAK ever fired during a lock): the game's keybind state,
	/// Dalamud's key state and Win32 for the default keys. Returns the source that saw it, null when none did.
	/// </summary>
	private static unsafe string? KeyboardMoveIntent()
	{
		if (!_inputDataFailed)
		{
			try
			{
				var input = UIInputData.Instance();
				if (input != null)
				{
					foreach (var id in MoveInputs)
					{
						if (input->IsInputIdDown(id))
						{
							return "inputdata";
						}
					}
				}
			}
			catch (Exception ex)
			{
				_inputDataFailed = true;
				PluginLog.Warning($"[Himechan] Cast assist: keybind state unavailable, using default keys only: {ex.Message}");
			}
		}

		try
		{
			foreach (var key in MoveDalamudKeys)
			{
				if (Svc.KeyState[key])
				{
					return "keystate";
				}
			}
		}
		catch (Exception ex)
		{
			PluginLog.Debug($"[Himechan] Cast assist: Dalamud key state failed: {ex.Message}");
		}

		if (_win32Failed)
		{
			return null;
		}

		try
		{
			_ = GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
			if (pid != Environment.ProcessId)
			{
				return null;
			}

			foreach (var key in MoveVirtualKeys)
			{
				if ((GetAsyncKeyState(key) & 0x8000) != 0)
				{
					return "win32";
				}
			}
		}
		catch (Exception ex)
		{
			_win32Failed = true;
			PluginLog.Warning($"[Himechan] Cast assist: live key state unavailable: {ex.Message}");
		}

		return null;
	}

	private static bool BothMouseButtonsDown()
	{
		if (InputManager.IsLeftMouseDown() && InputManager.IsRightMouseDown())
		{
			return true;
		}

		if (_win32Failed)
		{
			return false;
		}

		try
		{
			_ = GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
			if (pid != Environment.ProcessId)
			{
				return false; // the game window is not in front: the buttons belong to another program
			}

			return (GetAsyncKeyState(VkLButton) & 0x8000) != 0 && (GetAsyncKeyState(VkRButton) & 0x8000) != 0;
		}
		catch (Exception ex)
		{
			_win32Failed = true;
			PluginLog.Warning($"[Himechan] Cast assist: live mouse state unavailable, using the game's input state only: {ex.Message}");
			return false;
		}
	}
	#endregion
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
