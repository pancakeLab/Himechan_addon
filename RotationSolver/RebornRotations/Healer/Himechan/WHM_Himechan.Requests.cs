using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using RotationSolver.Himechan;

namespace RotationSolver.RebornRotations.Healer;

// Request commands: F2 + rev27-29 Solace request (#15, /히메짱백합) and the Aquaveil request (#31, /히메짱물장, 2026-10-13).
// The automatic tankbuster Aquaveil (#12, rev30-32 + cactbot list) was rolled back on 2026-10-13 by user decision;
// Aquaveil is now used only through /히메짱물장. The cactbot data sync (HimechanCactbotTankbusters) is kept for later use.
public sealed partial class WHM_Himechan
{
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

	#region Aquaveil request (/히메짱물장, #31)
	/// <summary>
	/// Friendly target -> Aquaveil on it; enemy / no target -> the party tank with a tank stance (if several, the one the
	/// current enemy is attacking); none with stance -> the tank the enemy is attacking. The use itself goes through the
	/// single-weave queue (#32) in order with other inputs, so it never clips or double weaves. Test mode: the player is the tank.
	/// </summary>
	internal void HandleAquaveilCommand()
	{
		if (!IsEnabled || !DataCenter.State || Player is not { } player || player.IsDead || DataCenter.IsPvP)
		{
			Svc.Chat.Print("[히메짱] 히메짱 WHM과 RSR을 켠 생존 상태에서 요청하세요.");
			return;
		}

		if (OpenerOwnsInput || _manualRaise)
		{
			Svc.Chat.Print("[히메짱] 오프너 또는 수동 부활 진행 중에는 물의 장막 요청을 받지 않습니다.");
			return;
		}

		if (!AquaveilPvE.EnoughLevel)
		{
			Svc.Chat.Print("[히메짱] 물의 장막을 아직 배우지 않았습니다.");
			return;
		}

		var cooldown = AquaveilPvE.Cooldown;
		if (!cooldown.HasOneCharge)
		{
			var wait = cooldown.RecastTimeRemainOneCharge + DataCenter.DefaultGCDRemain;
			if (wait > WHMHimechanPolicy.WeaveQueueLifetime)
			{
				Svc.Chat.Print($"[히메짱] 물의 장막 재사용 대기 중입니다 ({wait:F0}초).");
				return;
			}
		}

		IBattleChara? target = null;
		var how = "대상";
		if (Svc.Targets.Target is IBattleChara selected && !selected.IsEnemy() && selected.CurrentHp != 0)
		{
			target = selected;
		}
		else
		{
			target = SelectStanceTank(out how);
		}

		if (target == null)
		{
			Svc.Chat.Print("[히메짱] 스탠스를 켠 탱커(또는 적이 공격 중인 탱커)가 없어 물의 장막 대상을 정하지 못했습니다. 아군을 대상으로 지정한 뒤 다시 요청하세요.");
			return;
		}

		if (target.HasStatus(false, (StatusID)WHMHimechanPolicy.AquaveilStatus))
		{
			Svc.Chat.Print($"[히메짱] {target.Name.TextValue}에게 이미 물의 장막이 있습니다.");
			return;
		}

		if (!WeaveQueueActive || !InCombat)
		{
			// Outside combat (or with the queue off) nothing can be clipped: use it right away.
			UseAquaveilNow(target, how);
			return;
		}

		EnqueueAbility(AquaveilPvE.ID, AquaveilPvE.AdjustedID, target.GameObjectId, false, "물의 장막", true, $"request-{how}");
		Svc.Chat.Print($"[히메짱] 물의 장막 요청: {target.Name.TextValue} ({how}). 다음 안전한 틈에 사용합니다.");
	}

	private unsafe void UseAquaveilNow(IBattleChara target, string how)
	{
		var manager = ActionManager.Instance();
		var ok = manager != null && manager->UseAction(ActionType.Action, AquaveilPvE.ID, target.GameObjectId);
		RecordHimechanDiagnostic($"AQ NOW target=0x{target.GameObjectId:X} how={how} ok={ok}");
		if (!ok)
		{
			Svc.Chat.Print("[히메짱] 물의 장막 사용이 거부되었습니다 (거리·재사용 대기를 확인하세요).");
		}
	}

	/// <summary>Stance tank first (the one the current enemy attacks wins), then the tank the enemy attacks. In active test mode the player counts as a tank.</summary>
	private static IBattleChara? SelectStanceTank(out string how)
	{
		how = "스탠스 탱커";
		var attacked = DataCenter.HostileTarget?.TargetObjectId ?? 0;
		IBattleChara? first = null;
		foreach (var tank in HimechanTanks.PartyTanks())
		{
			if (tank.IsDead || tank.CurrentHp == 0)
			{
				continue;
			}

			var stance = tank.HasStatus(false, StatusHelper.TankStanceStatus) || (HimechanTanks.IsPlayer(tank) && HimechanTanks.TestModeActive);
			if (!stance)
			{
				continue;
			}

			if (tank.GameObjectId == attacked)
			{
				return tank;
			}
			first ??= tank;
		}

		if (first != null)
		{
			return first;
		}

		how = "공격받는 탱커";
		foreach (var tank in HimechanTanks.PartyTanks())
		{
			if (!tank.IsDead && tank.CurrentHp != 0 && tank.GameObjectId == attacked)
			{
				return tank;
			}
		}
		return null;
	}
	#endregion
}
