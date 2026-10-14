using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace RotationSolver.RebornRotations.Healer;

// Spec #36 (user request 2026-10-14): GCD input stack. The game queues only one input; Himechan stores up to three GCD
// presses and feeds them in press order, one per GCD, into the game's queue window. GCDs that Himechan itself plans
// (Dia refresh, Solace request, RSR's next GCD) go first, so a stored press can never starve them (game test 2026-10-14:
// Glare presses kept the game queue busy and the Solace request expired unused, Dia refreshed late).
public sealed partial class WHM_Himechan
{
	[RotationConfig(CombatType.PvE, Name = "GCD 선입력 스택 (최대 2개, 누른 순서. 히메짱이 계획한 GCD(디아 갱신·백합 요청)가 먼저)")]
	public bool GcdInputQueue { get; set; } = true;

	private const int GcdQueueMax = 2; // user decision 2026-10-14 (was 3)
	private const double GcdQueueLifetime = 8.0;
	private const float GameQueueWindow = 0.5f;

	// User request 2026-10-14: a rapid double press (two presses of the same GCD closer than this) jumps the stack:
	// the entry that would fire next is dropped and the pressed GCD takes its place. Himechan's own Dia refresh is not in
	// the stack and keeps going first.
	[Range(50f, 400f, ConfigUnitType.None, 10f)]
	[RotationConfig(CombatType.PvE, Name = "GCD 빠른 연타 인터셉트 기준 (ms): 같은 GCD를 이 간격 안에 두 번 누르면 다음 선입력을 밀어내고 앞에 섬", Parent = nameof(GcdInputQueue), ParentValue = true)]
	public float GcdInterceptMs { get; set; } = 150f;

	private sealed class QueuedGcd
	{
		public uint Id;
		public ulong Target;
		public double Expire;
		public string Label = string.Empty;
		public double InterceptedAt = double.MinValue; // HUD: purple for a moment after jumping the stack
	}

	private readonly List<QueuedGcd> _gcdQueue = [];
	private bool _gcdReplaying;
	private double _gcdInFlightUntil;
	private uint _lastGcdPressId;
	private double _lastGcdPressAt = double.MinValue;
	private uint _lastGcdUsedId;
	private double _lastGcdUsedAt = double.MinValue;

	internal bool GcdQueueActive => GcdInputQueue && IsEnabled && DataCenter.State && !DataCenter.IsPvP;
	internal int GcdQueueCount => _gcdQueue.Count;

	/// <summary>HUD lines for the stored GCD presses, in firing order.</summary>
	internal List<HudEntry> HudGcdQueueEntries()
	{
		List<HudEntry> list = [];
		var now = Now;
		foreach (var q in _gcdQueue)
		{
			var adjusted = Service.GetAdjustedActionId(q.Id);
			list.Add(new HudEntry(adjusted, false, 0, q.Label, "선입력", false, NeedsCastFor(adjusted), now - q.InterceptedAt < 1.0));
		}
		return list;
	}

	/// <summary>
	/// A Himechan-planned GCD is pending (Solace request, managed DoT refresh): stored presses wait behind it.
	/// RSR's ordinary planned GCD (Auto mode) is not counted, otherwise presses would never be released there.
	/// </summary>
	private bool PlannedGcdPending
	{
		get
		{
			if (_solaceRequest || Now < _solaceInFlightUntil)
			{
				return true;
			}

			var next = RotationSolver.Updaters.ActionUpdater.NextGCDAction;
			return next != null && (ActionID)next.AdjustedID is ActionID.DiaPvE or ActionID.AeroIiPvE or ActionID.AeroPvE
				&& !(next.Target.Target is { } target && IsExcludedFromAutoDia(target));
		}
	}

	/// <summary>
	/// UseActionDetour: a hotbar / macro GCD press. Passes through when the game would take it right now (queue window,
	/// nothing stored, nothing planned); otherwise it is stored (max 3, repeats ignored) and rejected.
	/// </summary>
	internal bool OnPlayerGcdInput(uint actionType, uint actionId, ulong targetObjectId, uint useType)
	{
		try
		{
			if (_gcdReplaying || actionType != 1 || useType == 1 || !GcdQueueActive || !InCombat || OpenerOwnsInput || _manualRaise)
			{
				return false;
			}

			var adjusted = Service.GetAdjustedActionId(actionId);
			if (!IsGcdActionId(adjusted))
			{
				return false;
			}

			// No resources (lilies, blood lilies, MP): never stored; the game shows its own error (user request 2026-10-14).
			if (!HasResourcesFor(adjusted))
			{
				RecordHimechanDiagnostic($"GCD Q SKIP no resource {ActionLabel(adjusted)}({adjusted})");
				return false;
			}

			var remain = DataCenter.DefaultGCDRemain;
			var player = Player;
			var casting = player?.IsCasting == true;

			// User decision 2026-10-14: during a cast, presses are accepted from the slidecast point on (the same moment moving
			// becomes safe). Earlier presses pass through untouched, which the game ignores as it always did.
			if (casting && player != null
				&& player.TotalCastTime - player.CurrentCastTime > Math.Clamp(RotationSolver.Himechan.HimechanSettings.Current.HudSlidecastWindow, 0f, 2f))
			{
				return false;
			}

			var reason = _gcdQueue.Count > 0 ? "queue"
				: PlannedGcdPending ? "planned"
				: casting ? "casting"
				: remain > GameQueueWindow ? "early"
				: null;
			if (reason == null)
			{
				_lastGcdUsedId = actionId;
				_lastGcdUsedAt = Now;
				return false; // The game takes it now (its own queue window).
			}

			// Repeated macro lines right after the same GCD was handed to the game: dropped, as the game itself would.
			if (actionId == _lastGcdUsedId && Now - _lastGcdUsedAt < WHMHimechanPolicy.RepeatDebounce)
			{
				return true;
			}

			var now = Now;
			var rapid = _lastGcdPressId == actionId && (now - _lastGcdPressAt) * 1000.0 < Math.Clamp(GcdInterceptMs, 50f, 400f);
			_lastGcdPressId = actionId;
			_lastGcdPressAt = now;

			var label = ActionLabel(adjusted);
			if (rapid && _gcdQueue.Count > 0 && _gcdQueue[0].Id != actionId)
			{
				// Jump the stack: drop what would fire next, put this press in front (an existing later copy moves up).
				var victim = _gcdQueue[0];
				_gcdQueue.RemoveAt(0);
				RecordHimechanDiagnostic($"GCD Q INTERCEPT {label}({adjusted}) drops {victim.Label}({victim.Id})");
				var existing = _gcdQueue.FindIndex(q => q.Id == actionId);
				QueuedGcd entry;
				if (existing >= 0)
				{
					entry = _gcdQueue[existing];
					_gcdQueue.RemoveAt(existing);
				}
				else
				{
					entry = new QueuedGcd { Id = actionId, Label = label, Expire = now + GcdQueueLifetime };
				}
				entry.Target = targetObjectId;
				entry.InterceptedAt = now;
				_gcdQueue.Insert(0, entry);
				return true;
			}

			foreach (var q in _gcdQueue)
			{
				if (q.Id == actionId)
				{
					q.Target = targetObjectId;
					return true; // Held key: one entry per action.
				}
			}
			if (_gcdQueue.Count >= GcdQueueMax)
			{
				RecordHimechanDiagnostic($"GCD Q FULL drop {label}({adjusted})");
				return true;
			}

			_gcdQueue.Add(new QueuedGcd { Id = actionId, Target = targetObjectId, Expire = now + GcdQueueLifetime, Label = label });
			RecordHimechanDiagnostic($"GCD Q ADD {label}({adjusted}) target=0x{targetObjectId:X} why={reason} gcd={remain:F2} n={_gcdQueue.Count}");
			return true;
		}
		catch (Exception ex)
		{
			ECommons.Logging.PluginLog.Warning($"[Himechan] GCD queue input check failed, input passed through: {ex.Message}");
			return false;
		}
	}

	/// <summary>Priority step in UpdateNextAction: hand the head of the stack to the game queue when the window opens. Never holds RSR.</summary>
	internal unsafe bool TryReplayGcd()
	{
		if (_gcdQueue.Count == 0)
		{
			return false;
		}

		if (!GcdQueueActive || OpenerOwnsInput || _manualRaise)
		{
			ClearGcdQueue("상태 변경");
			return false;
		}

		var now = Now;
		while (_gcdQueue.Count > 0 && now > _gcdQueue[0].Expire)
		{
			DropGcd(0, "시간 초과");
		}

		if (_gcdQueue.Count == 0 || now < _gcdInFlightUntil || PlannedGcdPending)
		{
			return false;
		}

		var head = _gcdQueue[0];
		if (head.Target != 0 && head.Target != 0xE0000000
			&& (Svc.Objects.SearchById(head.Target) is not IBattleChara target || target.IsDead))
		{
			DropGcd(0, "대상 없음");
			return false;
		}

		var manager = ActionManager.Instance();
		if (manager == null || manager->ActionQueued || DataCenter.DefaultGCDRemain > GameQueueWindow)
		{
			return false;
		}

		bool ok;
		_gcdReplaying = true;
		try
		{
			// Goes through the input hook again: the smart target (#33) may still fix the target.
			ok = manager->UseAction(ActionType.Action, head.Id, head.Target);
		}
		finally
		{
			_gcdReplaying = false;
		}

		_gcdQueue.RemoveAt(0);
		if (!ok)
		{
			RecordHimechanDiagnostic($"GCD Q REJECTED {head.Label}({head.Id})");
			return false;
		}

		// Short guard only: while the game holds it, ActionQueued blocks the next replay; once it fired, the restarted GCD
		// (remaining > queue window) does. A guard of "remaining GCD + 0.3s" here lengthened every GCD by ~0.3s (log 2026-10-14).
		_gcdInFlightUntil = now + 0.6;
		_lastGcdUsedId = head.Id;
		_lastGcdUsedAt = now;
		RecordHimechanDiagnostic($"GCD Q REPLAY {head.Label}({head.Id}) target=0x{head.Target:X} gcd={DataCenter.DefaultGCDRemain:F2} left={_gcdQueue.Count}");
		return false;
	}

	/// <summary>Resource gate for stored GCDs: lily heals need a lily, Afflatus Misery three blood lilies, casts their MP (Thin Air waives it).</summary>
	private bool HasResourcesFor(uint adjustedId)
	{
		switch ((ActionID)adjustedId)
		{
			case ActionID.AfflatusSolacePvE:
			case ActionID.AfflatusRapturePvE:
				return Lily > 0;
			case ActionID.AfflatusMiseryPvE:
				return BloodLily >= 3;
		}

		if (((ActionID)adjustedId).GetActionFromID(true, RotationSolver.Updaters.RotationUpdater.CurrentRotationActions) is IBaseAction known)
		{
			return HasThinAir || DataCenter.CurrentMp >= known.Info.MPNeed;
		}

		return true;
	}

	private void DropGcd(int index, string reason)
	{
		var q = _gcdQueue[index];
		_gcdQueue.RemoveAt(index);
		RecordHimechanDiagnostic($"GCD Q DROP {q.Label}({q.Id}): {reason}");
	}

	internal void ClearGcdQueue(string reason)
	{
		while (_gcdQueue.Count > 0)
		{
			DropGcd(0, reason);
		}
	}
}
