using System.ComponentModel;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using RotationSolver.Himechan;
using RotationSolver.Updaters;

namespace RotationSolver.RebornRotations.Healer;

// rev24 opener (#1, #3, #17) + rev30 F6 (actions forced onto the chosen tank) + spec #2 (numeric lead, medicine choice list).
public sealed partial class WHM_Himechan
{
	[RotationConfig(CombatType.PvE, Name = "히메짱 오프너 사용")]
	public bool UseHimechanOpener { get; set; } = false;

	[Range(0, 5, ConfigUnitType.Seconds, 0.01f)]
	[RotationConfig(CombatType.PvE, Name = "오프너: 카운트다운 종료 전 글레어 시전 완료 (0.00~5.00초)", Parent = nameof(UseHimechanOpener), ParentValue = true)]
	public float OpenerLeadSeconds { get; set; } = 0.5f;

	[RotationConfig(CombatType.PvE, Name = "오프너 영약 (정신력)", Parent = nameof(UseHimechanOpener), ParentValue = true)]
	public OpenerMedicineChoice OpenerMedicine { get; set; } = OpenerMedicineChoice.None;

	[RotationConfig(CombatType.PvE, Name = "오프너 영약 HQ 사용 (끄면 NQ, 품질 자동 대체 없음)", Parent = nameof(UseHimechanOpener), ParentValue = true)]
	public bool OpenerMedicineHQ { get; set; } = true;

	[RotationConfig(CombatType.PvE, Name = "오프너에 성소 사용", Parent = nameof(UseHimechanOpener), ParentValue = true)]
	public bool OpenerAsylum { get; set; } = false;

	// Stored as the Description text; keep texts unchanged. The default comes first.
	public enum OpenerMedicineChoice : byte
	{
		[Description("사용 안 함")]
		None,

		[Description("보유한 가장 높은 등급의 정신력 영약")]
		BestMind,
	}

	private enum Opening { Idle, Waiting, Guard, Heal, Benison, Reserved, FirstGlare, Potion, Dia, Asylum, LastGlare, LastCast }

	private Opening _opening;
	private float _previousCountdown;
	private double _openerEnd, _openerLimit, _openerPendingUntil, _openerCastAt, _openerHealAt, _openerGuardAt;
	private uint _openerTerritory, _openerPending, _openerExpected;
	private ulong _openerPlayer, _openerDiaTarget;
	private bool _openerSawCountdown, _openerUseAsylum, _openerSubmitting;
	private IBaseAction? _openerGlare;
	private WHMOpenerMedicine? _openerMedicine;
	private IAction? _openerSelected;
	private string _openerStatus = "대기";
	private bool _openerFirstGlareStarted, _openerCombatHandoff;

	// Refresh actual cast/combat observations before countdown cancellation, action
	// validation and native queue replay. A request/queue is NOT a cast start.
	// The handoff latch belongs to this countdown only; explicit OFF, a different
	// player/territory, disabled rotation and the existing deadline still win.
	internal bool ContinueOpenerAfterCountdown
	{
		get
		{
			if ((_opening == Opening.Idle && !_openerCombatHandoff)
				|| !UseHimechanOpener || !IsEnabled || !DataCenter.State || _manualRaise
				|| DataCenter.IsPvP || Player == null || Player.IsDead
				|| Player.GameObjectId != _openerPlayer || Svc.ClientState.TerritoryType != _openerTerritory
				|| Now > _openerLimit)
			{
				return false;
			}

			if (!_openerFirstGlareStarted && _opening == Opening.FirstGlare
				&& _openerGlare != null && _openerPending == _openerGlare.AdjustedID
				&& Player.IsCasting && Player.CastActionId == _openerGlare.AdjustedID)
			{
				_openerFirstGlareStarted = true;
				RecordHimechanDiagnostic("OP CAST START first Glare");
			}

			// The boss's combat flag also covers a party pull before this healer
			// personally enters combat. Do not infer a pull from target selection.
			if (!_openerCombatHandoff && (InCombat || OpenerBoss()?.InCombat() == true))
			{
				_openerCombatHandoff = true;
				if (!_openerFirstGlareStarted && _opening < Opening.Potion)
				{
					StopOpener("OP PULL EARLY: before first Glare cast - normal rotation");
				}
				else
				{
					RecordHimechanDiagnostic("OP PULL KEEP: first Glare started/confirmed");
				}
			}
			return _openerCombatHandoff;
		}
	}

	internal unsafe void StopOpener(string reason)
	{
		// Revoke only this opener's still-queued request. Do not erase another queue.
		var manager = ActionManager.Instance();
		var queuedAction = _openerSelected as IBaseAction;
		var queuedTarget = queuedAction?.Target.Target;
		if (_openerPending != 0 && queuedTarget != null && manager != null
			&& manager->ActionQueued && manager->QueuedActionType == ActionType.Action
			&& manager->QueuedActionId == _openerPending
			&& manager->QueuedTargetId.Id == queuedTarget.GameObjectId)
		{
			manager->ActionQueued = false;
			RecordHimechanDiagnostic(string.Format("OP QUEUE CLEAR id={0}", _openerPending));
		}

		if (_opening != Opening.Idle)
		{
			ECommons.Logging.PluginLog.Information($"[Himechan opener] {_opening}: {reason}");
			ActionUpdater.ClearNextAction();
		}

		_opening = Opening.Idle;
		_openerPending = 0;
		_openerSelected = null;
		_openerExpected = 0;
		_openerStatus = reason;
		RecordHimechanDiagnostic($"오프너 종료: {reason}");
	}

	// Only combat action/item calls are gated; movement, UI and RSR OFF remain available.
	internal bool OpenerOwnsInput => _opening != Opening.Idle && UseHimechanOpener && IsEnabled
		&& DataCenter.State && !_manualRaise && !DataCenter.IsPvP && Player != null && !Player.IsDead
		&& Player.GameObjectId == _openerPlayer && Svc.ClientState.TerritoryType == _openerTerritory
		&& Now >= _openerGuardAt && Now <= _openerLimit
		&& !(Service.CountDownTime <= 0 && Now < _openerEnd - 0.3 && !_openerCombatHandoff)
		&& !((_opening == Opening.LastGlare || _opening == Opening.LastCast) && Now > _openerPendingUntil)
		&& !(_opening == Opening.LastCast && Player.IsCasting && Player.CastActionId == _openerGlare?.AdjustedID);

	internal bool ShouldBlockOpenerInput(uint actionType, uint actionId, ulong targetObjectId, uint useType)
	{
		var wasOpenerRequest = _openerSubmitting || (actionType == 1 && useType == 1
			&& _openerPending != 0 && actionId == _openerPending);
		_ = ContinueOpenerAfterCountdown;
		// Also expire from the native hook so direct input need not wait for a rotation-evaluation tick.
		if ((_opening == Opening.LastGlare || _opening == Opening.LastCast) && Now > _openerPendingUntil)
		{
			StopOpener("OP LAST TIMEOUT: input released");
		}

		// Clearing ActionQueued alone cannot reject a replay already in this hook.
		if (wasOpenerRequest && _opening == Opening.Idle)
		{
			return true;
		}

		if (!OpenerOwnsInput || actionType is not (1 or 2))
		{
			return false;
		}

		if (_openerSubmitting)
		{
			RecordHimechanDiagnostic(string.Format("OP HOOK SUBMIT id={0} mode={1}", actionId, useType));
			return false;
		}

		// UseActionMode.Queue == 1 is the game's execution of a prior input, not a new hotbar (0) or macro (2) press.
		// OpenerSubmitted retains the selection ONLY when that exact action/target was observed in the game queue.
		var queuedAction = _openerSelected as IBaseAction;
		var queuedTarget = queuedAction?.Target.Target;
		if (actionType == 1 && useType == 1 && _openerPending != 0
			&& actionId == _openerPending && actionId == _openerExpected
			&& queuedAction != null && queuedAction.AdjustedID == actionId
			&& queuedTarget != null && queuedTarget.GameObjectId == targetObjectId
			&& Now <= _openerPendingUntil
			&& (_opening != Opening.FirstGlare || Now <= _openerCastAt + 0.25))
		{
			// Consume just the native replay permission. Pending remains until FX ACK.
			_openerSelected = null;
			RecordHimechanDiagnostic(string.Format("OP QUEUE PASS id={0} target={1:X}", actionId, targetObjectId));
			return false;
		}

		// A repeated rejected queue must not flood the trace every frame.
		if (useType == 1 && _openerStatus != "native-queue-blocked")
		{
			_openerStatus = "native-queue-blocked";
			RecordHimechanDiagnostic(string.Format("OP QUEUE BLOCK id={0} pending={1} target={2:X}", actionId, _openerPending, targetObjectId));
		}
		return true;
	}

	internal bool ShouldCancelOpenerPreparation => OpenerOwnsInput && _opening <= Opening.Heal && _openerPending == 0;

	internal bool UseWithOpenerScope(IAction action)
	{
		// A selected action ID alone must never whitelist a separate manual input.
		var previous = _openerSubmitting;
		_openerSubmitting = OpenerOwnsInput && ReferenceEquals(action, _openerSelected);
		try
		{
			return action.Use();
		}
		finally
		{
			_openerSubmitting = previous;
		}
	}

	/// <summary>Opener tank (#3): the tank with stance on, else the top tank in the party list. Test mode: the player.</summary>
	private static IBattleChara? OpenerTank(bool stanceOnly)
	{
		IBattleChara? best = null;
		var bestIndex = int.MaxValue;
		foreach (var member in HimechanTanks.PartyTanks())
		{
			if (member.IsDead || !member.IsTargetable)
			{
				continue;
			}

			if (stanceOnly && !HimechanTanks.IsPlayer(member) && !member.HasStatus(false, StatusHelper.TankStanceStatus))
			{
				continue;
			}

			var index = PartyDisplayIndex(member);
			if (best == null || index < bestIndex)
			{
				best = member;
				bestIndex = index;
			}
		}
		return best;
	}

	private static bool OpenerEnemy(IBattleChara? p) =>
		p != null && !p.IsDead && p.IsTargetable && p.IsEnemy() && p.DistanceToPlayer() <= 25 && p.CanSee();

	private static IBattleChara? OpenerBoss()
	{
		IBattleChara? onlyBoss = null;
		var bossCount = 0;
		foreach (var p in DataCenter.AllTargets)
		{
			if (!p.IsDead && p.IsEnemy() && p.IsBossFromIcon())
			{
				onlyBoss = p;
				bossCount++;
			}
		}

		if (bossCount == 1 && OpenerEnemy(onlyBoss))
		{
			return onlyBoss;
		}

		var tank = OpenerTank(true);
		var target = tank == null ? null : Svc.Objects.SearchById(tank.TargetObjectId) as IBattleChara;
		return OpenerEnemy(target) ? target : null;
	}

	/// <summary>Called before the general rotation; manual Raise keeps the highest priority.</summary>
	internal bool TryOpener(out IAction? next, out IBaseAction? gcd)
	{
		next = null;
		gcd = null;
		var now = Now;
		var cd = Service.CountDownTime;
		var start = cd > 0 && (!_openerSawCountdown || _previousCountdown <= 0 || cd > _previousCountdown + 1);
		_openerSawCountdown = true;
		_previousCountdown = cd;
		if (!UseHimechanOpener || !IsEnabled || !DataCenter.State || Player == null || Player.IsDead || DataCenter.IsPvP || _manualRaise)
		{
			if (_opening != Opening.Idle)
			{
				StopOpener("상태 변경·부활 요청으로 종료");
			}
			return false;
		}

		if (start && !InCombat)
		{
			if (_opening != Opening.Idle)
			{
				StopOpener("새 카운트다운으로 재준비");
			}

			_openerStatus = "시작 조건 확인";
			_openerGlare = GlareIiiPvE.EnoughLevel ? GlareIiiPvE : GlarePvE.EnoughLevel ? GlarePvE : null;
			var g = _openerGlare?.Cooldown.RecastTimeOneChargeRaw ?? 0;
			var c = _openerGlare?.Info.CastTime ?? 0; // Live, adjusted hardcast duration.
			var lead = WHMHimechanPolicy.Lead(OpenerLeadSeconds);
			if (!WHMHimechanPolicy.CanArm(cd, g, c, lead))
			{
				_openerStatus = $"준비 시간 부족: {cd:F2}s / 필요 {WHMHimechanPolicy.PreparationLead(g, lead) + c:F2}s";
				ECommons.Logging.PluginLog.Information($"[Himechan opener] 생략: {_openerStatus}");
				RecordHimechanDiagnostic(_openerStatus);
				return false;
			}

			if (_openerGlare == null || !DiaPvE.EnoughLevel)
			{
				StopOpener("글레어 또는 디아 레벨 부족 — 생략");
				return false;
			}

			// rev24 also required ActionQueueManager.OpenerHookReady. The use-action hook is installed
			// unconditionally at plugin start, so that upstream hook point is not needed any more.
			if (!RegenPvE.EnoughLevel)
			{
				StopOpener("리제네 레벨 부족 — 생략");
				return false;
			}

			_openerEnd = now + cd;
			_openerCastAt = _openerEnd - lead - c;
			_openerHealAt = _openerCastAt - g;
			_openerGuardAt = _openerEnd - WHMHimechanPolicy.PreparationLead(g, lead) - c;
			_openerLimit = _openerEnd + 15;
			_openerTerritory = Svc.ClientState.TerritoryType;
			_openerPlayer = Player.GameObjectId;
			_openerUseAsylum = OpenerAsylum;
			_openerDiaTarget = 0;
			_openerFirstGlareStarted = false;
			_openerCombatHandoff = false;
			_openerMedicine = PickOpenerMedicine();
			_opening = Opening.Waiting;
			_openerStatus = "카운트다운 준비";
			RecordHimechanDiagnostic($"OP ARM cd={cd:F2} lead={lead:F2} gcd={g:F2} cast={c:F2} potion={_openerMedicine?.Name ?? "없음"}");
		}

		_ = ContinueOpenerAfterCountdown;
		if (_opening == Opening.Idle)
		{
			return false;
		}

		if (now > _openerLimit || _openerTerritory != Svc.ClientState.TerritoryType || _openerPlayer != Player.GameObjectId)
		{
			StopOpener("시간 초과 또는 지역 변경");
			return false;
		}

		if (cd <= 0 && now < _openerEnd - 0.3 && !_openerCombatHandoff)
		{
			StopOpener("Countdown cancelled before combat");
			return false;
		}

		if (_opening == Opening.Waiting)
		{
			// Recompute both cast and GCD timing until the protection window.
			var g = _openerGlare!.Cooldown.RecastTimeOneChargeRaw;
			var c = _openerGlare.Info.CastTime;
			if (!float.IsFinite(g) || g <= 0 || !float.IsFinite(c) || c < 0)
			{
				StopOpener("Invalid live GCD/cast duration");
				return false;
			}

			_openerCastAt = _openerEnd - WHMHimechanPolicy.Lead(OpenerLeadSeconds) - c;
			_openerHealAt = _openerCastAt - g;
			_openerGuardAt = _openerCastAt - (2 * g) - 1;
			if (now < _openerGuardAt)
			{
				return false;
			}

			if (now > _openerGuardAt + 0.25)
			{
				StopOpener("Preparation window missed");
				return false;
			}

			ActionUpdater.ClearNextAction();
			_opening = Opening.Guard;
		}

		if (_opening == Opening.Guard)
		{
			_openerStatus = "입력 보호·기존 시전 취소 후 준비";
			if (now < _openerHealAt)
			{
				return true;
			}
			_opening = Opening.Heal;
		}

		// A late opener step must not hold input until the whole opener limit.
		if (_opening == Opening.LastGlare && now > _openerPendingUntil)
		{
			StopOpener("OP LAST READY TIMEOUT: input released");
			return false;
		}

		// An accepted cast must actually begin. No automatic RSR OFF on completion.
		if (_opening == Opening.LastCast)
		{
			if (Player.IsCasting && Player.CastActionId == _openerGlare?.AdjustedID)
			{
				RecordHimechanDiagnostic("OP CAST START last Glare - input released");
				StopOpener("마지막 글레어 시전 시작 — 완료");
				return false;
			}

			if (now > _openerPendingUntil)
			{
				StopOpener("OP LAST START TIMEOUT: no cast observed, input released");
				return false;
			}
			return true;
		}

		if (_openerPending != 0)
		{
			if (now > _openerPendingUntil)
			{
				StopOpener("기술 실행 확인 시간 초과 (효과 미확인)");
				return false;
			}
			return true;
		}

		if (_opening <= Opening.FirstGlare && now > (_opening <= Opening.Heal ? _openerHealAt : _openerCastAt) + 0.25)
		{
			StopOpener("준비 또는 첫 글레어 시간 경과");
			return false;
		}

		if (Player.IsCasting)
		{
			return true;
		}

		_openerSelected = null;
		var target = OpenerBoss();
		switch (_opening)
		{
			case Opening.Heal:
				if (now < _openerHealAt)
				{
					return true;
				}

				if (now > _openerHealAt + 0.25)
				{
					StopOpener("회복 시작 시간 경과");
					return false;
				}

				var tank = OpenerTank(true) ?? OpenerTank(false) ?? Player;
				if (tank == null)
				{
					StopOpener("리제네 대상 없음");
					return false;
				}

				if (OpenerAction(RegenPvE, tank, out next))
				{
					gcd = RegenPvE;
				}
				break;

			case Opening.Benison:
				if (now >= _openerCastAt - 0.85 || !DivineBenisonPvE.Cooldown.HasOneCharge || !DivineBenisonPvE.EnoughLevel)
				{
					_opening = Opening.FirstGlare;
					return true;
				}

				var shieldTank = OpenerTank(true) ?? OpenerTank(false);
				if (shieldTank == null)
				{
					_opening = Opening.FirstGlare;
					return true;
				}

				if (WHMHimechanPolicy.OpenerWeave(DataCenter.DefaultGCDRemain, DataCenter.AnimationLock, 0.85f))
				{
					_ = OpenerAction(DivineBenisonPvE, shieldTank, out next);
				}
				break;

			case Opening.FirstGlare:
				if (now < _openerCastAt)
				{
					return true;
				}

				if (now > _openerCastAt + 0.25 || target == null)
				{
					StopOpener("선시전 시간 경과 또는 대상 없음");
					return false;
				}

				if (OpenerAction(_openerGlare!, target, out next))
				{
					gcd = _openerGlare;
				}
				break;

			case Opening.Potion:
				if (_openerMedicine == null || !_openerMedicine.Available)
				{
					var reason = _openerMedicine == null ? "not selected / none owned" : "selected quality unavailable / on cooldown";
					RecordHimechanDiagnostic($"OP POTION SKIP: {reason}");
					_opening = Opening.Dia;
					return true;
				}

				// Explicitly allow GCD clipping here only. Still wait for the
				// observed Glare effect, end of casting, lock and item usability.
				if (DataCenter.AnimationLock <= 0 && _openerMedicine.CanUse(out var potion))
				{
					next = potion;
				}
				break;

			case Opening.Dia:
				if (target == null)
				{
					StopOpener("디아 대상 없음");
					return false;
				}

				if (OpenerAction(DiaPvE, target, out next))
				{
					gcd = DiaPvE;
				}
				break;

			case Opening.Asylum:
				if (!_openerUseAsylum || !AsylumPvE.Cooldown.HasOneCharge || DataCenter.DefaultGCDRemain < 0.85f)
				{
					SkipOpenerAsylum("토글 꺼짐·충전 없음·위빙 시간 부족");
					return true;
				}

				var boss = Svc.Objects.SearchById(_openerDiaTarget) as IBattleChara;
				if (!OpenerEnemy(boss))
				{
					SkipOpenerAsylum("디아 대상 유효성 상실");
					return true;
				}

				if (WHMHimechanPolicy.OpenerWeave(DataCenter.DefaultGCDRemain, DataCenter.AnimationLock, 0.85f)
					&& OpenerAction(AsylumPvE, Player, out next) && AsylumPvE is BaseAction asylum)
				{
					if (System.Numerics.Vector3.Distance(Player.Position, boss!.Position) > 30
						|| DataCenter.BMRIsPositionSafe?.Invoke(boss.Position) == false)
					{
						next = null;
						SkipOpenerAsylum("거리 또는 BMR 위치 판정");
						return true;
					}
					asylum.Target = new TargetResult(Player, [], boss.Position);
				}
				break;

			case Opening.LastGlare:
				if (target == null)
				{
					StopOpener("마지막 글레어 대상 없음");
					return false;
				}

				// Both the cached shared GCD and the action's live recast must
				// be inside this conservative queue window (not a game constant).
				var finalGcd = DataCenter.DefaultGCDRemain;
				var finalRecast = _openerGlare!.Cooldown.RecastTimeRemain;
				if (!float.IsFinite(finalGcd) || !float.IsFinite(finalRecast))
				{
					StopOpener("OP LAST INVALID RECAST: input released");
					return false;
				}

				if (finalGcd > 0.40f || finalRecast > 0.40f)
				{
					if (_openerStatus != "last-glare-gcd-wait")
					{
						_openerStatus = "last-glare-gcd-wait";
						RecordHimechanDiagnostic($"OP LAST READY WAIT gcd={finalGcd:F3} recast={finalRecast:F3}");
					}
					return true;
				}

				if (OpenerAction(_openerGlare!, target, out next))
				{
					gcd = _openerGlare;
				}
				break;
		}

		_openerSelected = next;
		_openerExpected = next?.AdjustedID ?? 0;
		_openerStatus = _opening.ToString();
		return true;
	}

	private void SkipOpenerAsylum(string reason)
	{
		ECommons.Logging.PluginLog.Information($"[Himechan opener] 성소 생략: {reason}");
		RecordHimechanDiagnostic($"OP ASYLUM SKIP: {reason}");
		_opening = Opening.LastGlare;
		_openerPendingUntil = Now + Math.Max(0f, DataCenter.DefaultGCDRemain) + 1d;
	}

	private bool OpenerAction(IBaseAction action, IBattleChara? target, out IAction? next)
	{
		next = null;
		if (target == null)
		{
			return false;
		}

		var filter = action.Setting.CanTarget;
		var force = IBaseAction.ForceEnable;
		var preview = IBaseAction.ActionPreview;
		var end = IBaseAction.ShouldEndSpecial;
		var type = IBaseAction.TargetOverride;
		try
		{
			using var enabled = new WHMRequestedActionScope(action);
			using var offTarget = new HimechanOffTargetScope();
			IBaseAction.ForceEnable = true;
			IBaseAction.ActionPreview = false;
			IBaseAction.ShouldEndSpecial = false;
			IBaseAction.TargetOverride = null;
			action.Setting.CanTarget = p => p.GameObjectId == target.GameObjectId && filter(p);
			var targetType = target.GameObjectId == Player?.GameObjectId ? TargetType.Self : target.IsEnemy() ? TargetType.Big : TargetType.Tank;
			if (!action.CanUse(out var act, usedUp: true, skipAoeCheck: true, skipTTKCheck: true, skipStatusProvideCheck: true, targetOverride: targetType))
			{
				return false;
			}

			// rev30 F6: RSR's tank targeting ignores CanTarget, so force the chosen member.
			// Ground-targeted actions (Asylum, Liturgy of the Bell) keep RSR's position result.
			if (act.ID is not (3569 or 25862) && act is IBaseAction chosen)
			{
				chosen.Target = new TargetResult(target, [target], null);
			}

			next = act;
			return true;
		}
		finally
		{
			action.Setting.CanTarget = filter;
			IBaseAction.ForceEnable = force;
			IBaseAction.ActionPreview = preview;
			IBaseAction.ShouldEndSpecial = end;
			IBaseAction.TargetOverride = type;
		}
	}

	internal bool ValidateOpenerAction(IAction action)
	{
		var wasSelected = ReferenceEquals(action, _openerSelected);
		_ = ContinueOpenerAfterCountdown;
		if (wasSelected && _opening == Opening.Idle)
		{
			return false;
		}

		if (_opening == Opening.Idle || (_opening == Opening.Waiting && !OpenerOwnsInput))
		{
			return true;
		}

		if (!UseHimechanOpener || _manualRaise || !ReferenceEquals(action, _openerSelected) || action.AdjustedID != _openerExpected)
		{
			return false;
		}

		if (action is WHMOpenerMedicine medicine)
		{
			return medicine.Available && Player?.IsCasting != true && DataCenter.AnimationLock <= 0;
		}

		if (action is IBaseAction hostile && hostile.Target.Target?.IsEnemy() == true && !OpenerEnemy(hostile.Target.Target))
		{
			return false;
		}

		if (action == AsylumPvE && AsylumPvE is BaseAction ground)
		{
			var boss = Svc.Objects.SearchById(_openerDiaTarget) as IBattleChara;
			if (!OpenerEnemy(boss) || Player == null || System.Numerics.Vector3.Distance(Player.Position, boss!.Position) > 30
				|| DataCenter.BMRIsPositionSafe?.Invoke(boss.Position) == false)
			{
				return false;
			}
			ground.Target = new TargetResult(Player, [], boss.Position);
		}

		if (action is IBaseAction a && a.Info.IsAbility)
		{
			return WHMHimechanPolicy.OpenerWeave(DataCenter.DefaultGCDRemain, DataCenter.AnimationLock, 0.85f);
		}

		if (_opening == Opening.LastGlare)
		{
			// Recheck at execution time: the selected action can outlive a frame.
			var finalGcd = DataCenter.DefaultGCDRemain;
			var finalRecast = _openerGlare!.Cooldown.RecastTimeRemain;
			return Player?.IsCasting != true && float.IsFinite(finalGcd) && float.IsFinite(finalRecast)
				&& finalGcd <= 0.40f && finalRecast <= 0.40f && Now <= _openerPendingUntil;
		}

		return _opening != Opening.FirstGlare || (Now >= _openerCastAt && Now <= _openerCastAt + 0.25);
	}

	/// <summary>True while the opener's potion is the next action (lets CanDoAnAction skip its 0.5s GCD-tail rule for it).</summary>
	internal bool IsOpenerPotionDue(IAction? next) =>
		next is WHMOpenerMedicine && OpenerOwnsInput && ValidateOpenerAction(next);

	internal unsafe void OpenerSubmitted(IAction action)
	{
		if (_opening <= Opening.Guard || !ReferenceEquals(action, _openerSelected))
		{
			return;
		}

		if (_opening == Opening.Potion)
		{
			_opening = Opening.Dia;
			_openerSelected = null;
			return;
		}

		if (_opening == Opening.Dia)
		{
			_openerDiaTarget = (action as IBaseAction)?.Target.Target?.GameObjectId ?? 0;
		}

		_openerPending = action.AdjustedID;
		_openerPendingUntil = Now + ((action as IBaseAction)?.Info.CastTime ?? 0) + 2;
		if (_opening == Opening.LastGlare)
		{
			_opening = Opening.LastCast;
			// Wait for START, not the whole cast plus an effect-packet timeout.
			var queuedWait = DataCenter.DefaultGCDRemain;
			if (!float.IsFinite(queuedWait) || queuedWait < 0f)
			{
				queuedWait = 0f;
			}

			if (queuedWait > 0.40f)
			{
				queuedWait = 0.40f;
			}
			_openerPendingUntil = Now + queuedWait + 0.50d;
		}

		// Use() == true can mean queued, not executed. Bind one native replay to
		// the exact queued action AND target, and never advance the phase here.
		var manager = ActionManager.Instance();
		var queuedAction = action as IBaseAction;
		var queuedTarget = queuedAction?.Target.Target;
		var ownedQueue = manager != null && manager->ActionQueued
			&& manager->QueuedActionType == ActionType.Action
			&& manager->QueuedActionId == _openerPending
			&& queuedTarget != null && manager->QueuedTargetId.Id == queuedTarget.GameObjectId;
		if (!ownedQueue)
		{
			_openerSelected = null;
		}

		RecordHimechanDiagnostic(string.Format("OP SUBMIT id={0} ownedQueue={1} queueId={2} gcd={3:F3}",
			_openerPending, ownedQueue, manager == null ? 0u : manager->QueuedActionId, DataCenter.DefaultGCDRemain));
		if (_opening == Opening.LastCast)
		{
			RecordHimechanDiagnostic(string.Format("OP LAST START WAIT casting={0} castId={1} budget={2:F3}",
				Player?.IsCasting == true, Player?.CastActionId ?? 0, _openerPendingUntil - Now));
		}
	}

	internal void OpenerEffect(uint id)
	{
		if (_opening <= Opening.Guard)
		{
			return; // Pre-protection or canceled preparation actions are not opener actions.
		}

		RecordHimechanDiagnostic(string.Format("OP FX id={0} pending={1} stage={2}", id, _openerPending, _opening));
		if (id != _openerPending)
		{
			// An unrelated manual spell/ability owns the player's intent.
			if (_openerPending == 0 || id != _openerExpected)
			{
				StopOpener("다른 기술 사용으로 종료");
			}
			return;
		}

		RecordHimechanDiagnostic(string.Format("OP ACK id={0}", id));
		if (_opening == Opening.FirstGlare)
		{
			_openerFirstGlareStarted = true;
		}

		_openerPending = 0;
		_opening = _opening switch
		{
			Opening.Heal => Opening.Benison,
			Opening.Benison => Opening.FirstGlare,
			Opening.FirstGlare => Opening.Potion,
			Opening.Dia => Opening.Asylum,
			Opening.Asylum => Opening.LastGlare,
			_ => Opening.Idle,
		};

		if (_opening == Opening.LastGlare)
		{
			_openerPendingUntil = Now + Math.Max(0f, DataCenter.DefaultGCDRemain) + 1d;
		}

		if (_opening == Opening.Idle)
		{
			StopOpener("오프너 완료");
		}
	}

	/// <summary>Spec #2: the highest-grade Mind medicine owned in the chosen quality (RSR's list is newest first).</summary>
	private WHMOpenerMedicine? PickOpenerMedicine()
	{
		if (OpenerMedicine == OpenerMedicineChoice.None)
		{
			return null;
		}

		var sheet = Service.GetSheet<Item>();
		foreach (var medicine in Medicines)
		{
			if (medicine.Type != MedicineType.Mind)
			{
				continue;
			}

			var row = sheet.GetRowOrDefault(medicine.ID);
			if (row is not { } item)
			{
				continue;
			}

			var candidate = new WHMOpenerMedicine(item, OpenerMedicineHQ);
			if (candidate.Owned)
			{
				return candidate;
			}
		}

		return null;
	}

	private object? _legacyCheckedFor;

	/// <summary>
	/// rev24 stored the opener medicine as an item id string (OpenerMedicineId). A non-zero choice becomes
	/// "best owned Mind medicine" once per profile; the old key is removed so it is not migrated again.
	/// </summary>
	private void MigrateLegacyOpenerSettings()
	{
		if (ReferenceEquals(_legacyCheckedFor, Service.Config))
		{
			return;
		}

		_legacyCheckedFor = Service.Config;
		var stored = Service.Config.RotationConfigurations;
		if (!stored.TryGetValue("OpenerMedicineId", out var legacy))
		{
			return;
		}

		if (legacy != "0" && !stored.ContainsKey(nameof(OpenerMedicine)))
		{
			OpenerMedicine = OpenerMedicineChoice.BestMind;
			stored[nameof(OpenerMedicine)] = "보유한 가장 높은 등급의 정신력 영약";
			RecordHimechanDiagnostic($"MIGRATE OpenerMedicineId={legacy} -> BestMind");
		}

		_ = stored.TryRemove("OpenerMedicineId", out _);
	}
}

/// <summary>
/// Explicit opener selection. Does not use the general automatic tincture policy,
/// and never falls back to another quality or item.
/// </summary>
internal sealed class WHMOpenerMedicine(Item item, bool hq) : BaseItem(item), IAction
{
	internal uint UseId => ID + (hq ? 1000000u : 0u);

	internal unsafe bool Owned => InventoryManager.Instance() != null
		&& InventoryManager.Instance()->GetInventoryItemCount(ID, hq) > 0;

	internal unsafe bool Available => Owned && ActionManager.Instance() != null && !Cooldown.IsCoolingDown;

	public override unsafe bool CanUse(out IAction action, bool clippingCheck = false)
	{
		action = this;
		return Available && ActionManager.Instance()->GetActionStatus(ActionType.Item, UseId) == 0;
	}

	bool IAction.Use() => UseSelected();

	private unsafe bool UseSelected() => Available && ECommons.GameHelpers.Player.Object != null
		&& ActionManager.Instance()->UseAction(ActionType.Item, UseId, ECommons.GameHelpers.Player.Object.GameObjectId, A4);
}
