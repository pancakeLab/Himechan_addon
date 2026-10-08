using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using RotationSolver.Basic.Configuration;
using RotationSolver.Himechan;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace RotationSolver.RebornRotations.Healer;

// Ported from the rev25-32 managed-IL features (ref tools/himechan_features.py):
// F2 + rev27-29 Solace request (#15), F3 manual oGCD defer (#19), F4 + rev31-32 high-end Aquaveil (#12).
public sealed partial class WHM_Himechan
{
	[RotationConfig(CombatType.PvE, Name = "자동 능력 직후 수동 능력 입력 시 1GCD 뒤로 미루기 (거룩한 축복·구조 제외)")]
	public bool DeferManualAbilityOnAutoWeave { get; set; } = false;

	// Setting key kept (AutoAquaveilHighEnd) so existing values carry over; it now applies to every duty.
	[RotationConfig(CombatType.PvE, Name = "탱커버스터에 물의 장막 자동 사용 (적 시전 대상 탱커·탱커버스터 목록·VFX, 잔여 5초 이내. 이 외에는 자동 사용 안 함)")]
	public bool AutoAquaveilHighEnd { get; set; } = true;

	[RotationConfig(CombatType.PvE, Name = "고난도(영식·극·절)에서만 사용", Parent = nameof(AutoAquaveilHighEnd), ParentValue = true)]
	public bool AquaveilHighEndOnly { get; set; } = false;

	#region Solace request (/히메짱백합)
	private bool _solaceRequest;
	private ulong _solaceTarget;
	private double _solaceDeadline;
	private double _solaceInFlightUntil; // one queue slot: submitted, effect not yet observed
	private double _solaceLastEffectAt = double.MinValue;

	internal bool HasSolaceRequest => _solaceRequest;

	/// <summary>Friendly target -> Solace on it; enemy / no target -> lowest-HP party member within 30y; everyone full -> Rapture.</summary>
	internal void HandleSolaceCommand()
	{
		if (!IsEnabled || !DataCenter.State || Player is not { } player || player.IsDead || DataCenter.IsPvP)
		{
			Svc.Chat.Print("[히메짱] 히메짱 WHM과 RSR을 켠 생존 상태에서 요청하세요.");
			return;
		}

		if (OpenerOwnsInput || _manualRaise)
		{
			Svc.Chat.Print("[히메짱] 오프너 또는 수동 부활 진행 중에는 위로의 마음 요청을 받지 않습니다.");
			return;
		}

		var now = Now;
		if (_solaceRequest || now < _solaceInFlightUntil)
		{
			RecordHimechanDiagnostic("SOLACE REPEAT IGNORED");
			return;
		}

		// Within one GCD after a Solace/Rapture effect, accept only inside the game's queue window.
		if (now - _solaceLastEffectAt < WHMHimechanPolicy.SolaceRecent && DataCenter.DefaultGCDRemain > WHMHimechanPolicy.SolaceQueueWindow)
		{
			RecordHimechanDiagnostic("SOLACE EARLY IGNORED");
			return;
		}

		if (Lily == 0)
		{
			Svc.Chat.Print("[히메짱] 치유의 백합이 없어 위로의 마음/황홀한 마음을 사용할 수 없습니다.");
			return;
		}

		_solaceTarget = 0;
		if (Svc.Targets.Target is IBattleChara target && !target.IsEnemy() && target.CurrentHp != 0)
		{
			_solaceTarget = target.GameObjectId;
		}

		_solaceRequest = true;
		_solaceDeadline = now + WHMHimechanPolicy.SolaceRequestLifetime;
		RecordHimechanDiagnostic($"SOLACE REQ target=0x{_solaceTarget:X}");
	}

	/// <summary>Priority step in UpdateNextAction. true + null next = hold RSR this frame.</summary>
	internal unsafe bool TrySolaceRequest(out IAction? next, out IBaseAction? gcd)
	{
		next = null;
		gcd = null;
		var now = Now;

		if (!_solaceRequest)
		{
			// A submitted Solace/Rapture still waiting in the game queue: hold RSR so its next GCD cannot overwrite it (rev28).
			if (now >= _solaceInFlightUntil)
			{
				return false;
			}

			var manager = ActionManager.Instance();
			if (manager != null && manager->ActionQueued)
			{
				return true;
			}

			if (_solaceInFlightUntil > now + WHMHimechanPolicy.SolaceQueueGrace)
			{
				_solaceInFlightUntil = now + WHMHimechanPolicy.SolaceQueueGrace;
				RecordHimechanDiagnostic("SOLACE QUEUE LEFT");
			}
			return false;
		}

		if (!IsEnabled || !DataCenter.State || Player is not { } player || player.IsDead || DataCenter.IsPvP
			|| now > _solaceDeadline || Lily == 0 || OpenerOwnsInput)
		{
			StopSolaceRequest();
			return false;
		}

		if (player.IsCasting)
		{
			return true;
		}

		IBaseAction action;
		IBattleChara? target;
		if (_solaceTarget != 0)
		{
			target = Svc.Objects.SearchById(_solaceTarget) as IBattleChara;
			if (target == null || target.CurrentHp == 0 || target.IsDead)
			{
				StopSolaceRequest();
				return false;
			}

			if (target.DistanceToPlayer() > 30)
			{
				return true;
			}
			action = AfflatusSolacePvE;
		}
		else
		{
			target = SelectSolaceTarget(out var bestHp);
			if (target == null)
			{
				StopSolaceRequest();
				return false;
			}

			if (bestHp < 1f)
			{
				action = AfflatusSolacePvE;
			}
			else
			{
				action = AfflatusRapturePvE;
				target = null;
			}
		}

		var force = IBaseAction.ForceEnable;
		var preview = IBaseAction.ActionPreview;
		IBaseAction.ForceEnable = true;
		IBaseAction.ActionPreview = false;
		try
		{
			// CanUse writes the action into its out parameter even on failure, so keep it in a local.
			if (action.CanUse(out var act, usedUp: true, skipAoeCheck: true, skipTTKCheck: true, targetOverride: TargetType.Self))
			{
				if (target != null && act is IBaseAction chosen)
				{
					chosen.Target = new TargetResult(target, [target], null);
				}
				next = act;
				gcd = action;
			}
		}
		finally
		{
			IBaseAction.ForceEnable = force;
			IBaseAction.ActionPreview = preview;
		}

		return true;
	}

	private void StopSolaceRequest()
	{
		_solaceRequest = false;
		RecordHimechanDiagnostic("SOLACE END");
	}

	private static IBattleChara? SelectSolaceTarget(out float bestHp)
	{
		IBattleChara? best = null;
		bestHp = 2f;
		var bestRole = 9;
		foreach (var p in DataCenter.PartyMembers)
		{
			if (p == null || p.IsDead || p.CurrentHp == 0 || p.DistanceToPlayer() > 30)
			{
				continue;
			}

			var hp = p.GetHealthRatio();
			var role = p.IsJobCategory(JobRole.Healer) ? 0 : p.IsJobCategory(JobRole.Tank) ? 2 : 1;
			if (WHMHimechanPolicy.SolaceBetter(hp, role, bestHp, bestRole))
			{
				best = p;
				bestHp = hp;
				bestRole = role;
			}
		}
		return best;
	}

	internal void SolaceSubmitted(uint id)
	{
		if (id is not (WHMHimechanPolicy.SolaceId or WHMHimechanPolicy.RaptureId) || !_solaceRequest)
		{
			return;
		}

		_solaceRequest = false;
		_solaceInFlightUntil = Now + WHMHimechanPolicy.SolaceInFlight;
		RecordHimechanDiagnostic("SOLACE SUBMIT");
	}

	internal void SolaceEffect(uint id)
	{
		if (id is not (WHMHimechanPolicy.SolaceId or WHMHimechanPolicy.RaptureId))
		{
			return;
		}

		_solaceInFlightUntil = 0;
		_solaceLastEffectAt = Now;
		RecordHimechanDiagnostic("SOLACE EFFECT");
	}
	#endregion

	#region Manual oGCD defer (#19)
	private double _autoWindowEnd;
	private uint _deferId;
	private ulong _deferTarget;
	private double _deferAfter;
	private double _deferExpire;
	private bool _deferReplaying;

	/// <summary>After DoAction used one of RSR's abilities: the current GCD gap ends at now + remaining GCD.</summary>
	internal void AutoAbilitySubmitted(IAction action)
	{
		if (action is IBaseAction a && a.Info.IsAbility)
		{
			_autoWindowEnd = Now + DataCenter.DefaultGCDRemain;
		}
	}

	/// <summary>
	/// UseActionDetour: a new player ability right after an RSR ability, when a second weave would clip,
	/// is stored (latest one only) and rejected; it is replayed after the next GCD (TryDeferredReplay).
	/// </summary>
	internal bool ShouldDeferManualAbility(uint actionType, uint actionId, ulong targetObjectId, uint useType)
	{
		try
		{
			if (_deferReplaying || !DeferManualAbilityOnAutoWeave || actionType != 1 || useType == 1
				|| !IsEnabled || !DataCenter.State || !DataCenter.InCombat || DataCenter.IsPvP
				|| OpenerOwnsInput || _manualRaise || _solaceRequest
				|| Service.Config.InterceptAction3)
			{
				return false;
			}

			if (Now >= _autoWindowEnd)
			{
				return false; // No RSR ability in this gap.
			}

			var adjusted = Service.GetAdjustedActionId(actionId);
			if (WHMHimechanPolicy.IsDeferExempt(adjusted) || !IsAbilityId(adjusted))
			{
				return false;
			}

			// A second weave still fits: no need to defer.
			if (DataCenter.DefaultGCDRemain - DataCenter.AnimationLock
				> WHMHimechanPolicy.WeaveBudget(ActionManagerEx.Instance.GetAnimationLockDelayEstimate()))
			{
				return false;
			}

			_deferId = actionId;
			_deferTarget = targetObjectId;
			_deferAfter = _autoWindowEnd;
			_deferExpire = _autoWindowEnd + WHMHimechanPolicy.DeferExpireAfterWindow;
			RecordHimechanDiagnostic($"DEFER CAPTURE id={actionId} target=0x{targetObjectId:X}");
			return true;
		}
		catch (Exception ex)
		{
			ECommons.Logging.PluginLog.Warning($"[Himechan] Defer check failed: {ex.Message}");
			return false;
		}
	}

	private static bool IsAbilityId(uint adjustedId)
	{
		var row = Service.GetSheet<LuminaAction>().GetRowOrDefault(adjustedId);
		return row is { } action && action.ActionCategory.RowId == (uint)ActionCate.Ability;
	}

	/// <summary>Priority step in UpdateNextAction: replay the stored press in the first safe window after the gap.</summary>
	internal unsafe bool TryDeferredReplay()
	{
		if (_deferId == 0)
		{
			return false;
		}

		var now = Now;
		if (!DeferManualAbilityOnAutoWeave || !IsEnabled || !DataCenter.State || DataCenter.IsPvP
			|| OpenerOwnsInput || _manualRaise || _solaceRequest || now > _deferExpire)
		{
			DropDeferred();
			return false;
		}

		if (now < _deferAfter || !MaintenanceWeaveSafe())
		{
			return false;
		}

		if (_deferTarget != 0 && _deferTarget != 0xE0000000)
		{
			if (Svc.Objects.SearchById(_deferTarget) is not IBattleChara target || target.IsDead)
			{
				DropDeferred();
				return false;
			}
		}

		var manager = ActionManager.Instance();
		var ok = false;
		_deferReplaying = true;
		try
		{
			ok = manager != null && manager->UseAction(ActionType.Action, _deferId, _deferTarget);
		}
		finally
		{
			_deferReplaying = false;
		}

		var id = _deferId;
		_deferId = 0;
		if (!ok)
		{
			RecordHimechanDiagnostic($"DEFER REPLAY REJECTED id={id}");
			return false;
		}

		_autoWindowEnd = now + DataCenter.DefaultGCDRemain;
		RecordHimechanDiagnostic($"DEFER REPLAY id={id}");
		return true;
	}

	private void DropDeferred()
	{
		if (_deferId == 0)
		{
			return;
		}

		RecordHimechanDiagnostic($"DEFER DROP id={_deferId}");
		_deferId = 0;
	}
	#endregion

	#region Tankbuster Aquaveil (#12)
	private double _aquaveilScanLogAt = double.MinValue;
	private double _aquaveilBlockLogAt = double.MinValue;

	/// <summary>
	/// In combat (any duty unless "high-end only"): (1) an enemy cast aimed at a party tank with &lt;= 5s left;
	/// (2) a cast not aimed at a tank whose id is in RSR's tankbuster list (HostileCastingTank) -> the boss's current
	/// target tank; (3) RSR's tankbuster VFX. This is the only automatic use of Aquaveil. Test mode: the player is the tank.
	/// </summary>
	private bool TryTankbusterAquaveil(IAction nextGCD, out IAction? act)
	{
		act = null;
		if (!AutoAquaveilHighEnd || !InCombat || (AquaveilHighEndOnly && !IsInHighEndDuty))
		{
			return false;
		}

		var tank = FindTankbusterTarget();
		if (tank == null)
		{
			return false;
		}

		if (tank.IsDead || tank.CurrentHp == 0)
		{
			return LogAquaveilBlock("AQ BLOCK tank dead");
		}

		if (tank.HasStatus(false, (StatusID)WHMHimechanPolicy.AquaveilStatus))
		{
			return LogAquaveilBlock("AQ BLOCK tank already has Aquaveil");
		}

		// Same rule as every other automatic ability (#11): with a planned GCD only inside the weave budget,
		// without one (downtime) only casting / animation lock / game queue block it.
		if (!AutoAbilityWeaveOk(nextGCD))
		{
			return LogAquaveilBlock("AQ BLOCK weave window");
		}

		var force = IBaseAction.ForceEnable;
		var preview = IBaseAction.ActionPreview;
		IBaseAction.ForceEnable = true;
		IBaseAction.ActionPreview = false;
		bool ok;
		try
		{
			ok = AquaveilPvE.CanUse(out act, skipAoeCheck: true, skipTTKCheck: true, targetOverride: TargetType.Self);
			if (ok && act is IBaseAction chosen)
			{
				chosen.Target = new TargetResult(tank, [tank], null);
			}
		}
		finally
		{
			IBaseAction.ForceEnable = force;
			IBaseAction.ActionPreview = preview;
		}

		if (!ok)
		{
			act = null;
			return LogAquaveilBlock("AQ BLOCK CanUse false (cooldown/level/target)");
		}

		RecordHimechanDiagnostic($"AQUAVEIL HIGH-END TANKBUSTER target=0x{tank.GameObjectId:X}");
		return true;
	}

	private IBattleChara? FindTankbusterTarget()
	{
		var busterList = OtherConfiguration.HostileCastingTank;
		var now = Now;
		foreach (var hostile in DataCenter.AllHostileTargets)
		{
			if (hostile == null || !hostile.IsCasting)
			{
				continue;
			}

			var remain = hostile.TotalCastTime - hostile.CurrentCastTime;
			if (now - _aquaveilScanLogAt >= 2.0)
			{
				_aquaveilScanLogAt = now;
				var known = HimechanCactbotTankbusters.CastIds.Contains(hostile.CastActionId) ? "cactbot" : (busterList != null && busterList.Contains(hostile.CastActionId) ? "rsr" : "-");
				RecordHimechanDiagnostic($"AQ CAST id={hostile.CastActionId} tgt={hostile.CastTargetObjectId} rem={remain:F2} list={known}");
			}

			if (remain > WHMHimechanPolicy.AquaveilCastWindow)
			{
				continue;
			}

			if (Svc.Objects.SearchById(hostile.CastTargetObjectId) is IBattleChara castTarget
				&& HimechanTanks.IsTank(castTarget) && !castTarget.IsEnemy())
			{
				return castTarget;
			}

			// Cast not aimed at a tank (e.g. a self-targeted savage buster): use RSR's tankbuster action list
			// (List tab, user-editable) or the list generated from cactbot's triggers; target = the boss's
			// current target (rev32, from KR savage logs).
			if (((busterList != null && busterList.Contains(hostile.CastActionId)) || HimechanCactbotTankbusters.CastIds.Contains(hostile.CastActionId))
				&& Svc.Objects.SearchById(hostile.TargetObjectId) is IBattleChara bossTarget
				&& HimechanTanks.IsTank(bossTarget) && !bossTarget.IsEnemy())
			{
				return bossTarget;
			}
		}

		if (!DataCenter.IsCastingTankVfx())
		{
			return null;
		}

		foreach (var target in DataCenter.TankbusterTargets)
		{
			if (target != null && HimechanTanks.IsTank(target))
			{
				return target;
			}
		}
		return null;
	}

	private bool LogAquaveilBlock(string message)
	{
		var now = Now;
		if (now - _aquaveilBlockLogAt >= 1.0)
		{
			_aquaveilBlockLogAt = now;
			RecordHimechanDiagnostic(message);
		}
		return false;
	}
	#endregion
}
