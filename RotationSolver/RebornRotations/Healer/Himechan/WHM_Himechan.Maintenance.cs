using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace RotationSolver.RebornRotations.Healer;

// rev24 maintenance policy + spec #10 (Divine Benison plan) and #11 (common weave policy for every automatic ability).
public sealed partial class WHM_Himechan
{
	private delegate bool AbilityPicker(IAction nextGCD, out IAction? act);

	private bool _benisonPlanValid;
	private float _benisonPlanLead;
	private int _benisonObservedCharges;
	private float _benisonObservedRemain;
	private double _benisonHoldUntil;
	private double _weaveLogAfter;

	// The last ability this rotation picked through WeaveGate; DoAction re-checks only that one,
	// so abilities RSR picks on its own (e.g. Surecast) are never held back here.
	private IAction? _gatedPick;

	private static double Now => Environment.TickCount64 / 1000d;

	/// <summary>Spec #11: every automatic ability of Himechan obeys one weaving rule (in combat).</summary>
	private bool WeaveGate(IAction nextGCD, AbilityPicker picker, out IAction? act)
	{
		act = null;
		if (InCombat && !AutoAbilityWeaveOk(nextGCD))
		{
			LogWeaveDefer(nextGCD);
			return false;
		}

		if (!picker(nextGCD, out act))
		{
			return false;
		}

		if (!IBaseAction.ActionPreview)
		{
			_gatedPick = act;
		}
		return true;
	}

	/// <summary>
	/// With a GCD planned: only inside the weave budget (rev24 rule; 0 remaining is not a window).
	/// Without a GCD (RSR passes a placeholder): nothing can be clipped, so healing oGCDs still work in downtime.
	/// </summary>
	private unsafe bool AutoAbilityWeaveOk(IAction? nextGCD)
	{
		var player = Player;
		var manager = ActionManager.Instance();
		if (player == null || manager == null)
		{
			return false;
		}

		var hasGcd = nextGCD is IBaseAction gcd && gcd.Info.IsRealGCD && !ReferenceEquals(nextGCD, AddlePvE);
		if (!hasGcd)
		{
			return WHMHimechanPolicy.CanUseWithoutGcd(DataCenter.AnimationLock, player.IsCasting, manager->ActionQueued);
		}

		return MaintenanceWeaveSafe();
	}

	internal unsafe bool MaintenanceWeaveSafe()
	{
		var player = ECommons.GameHelpers.Player.Object;
		var manager = ActionManager.Instance();
		if (player == null || manager == null)
		{
			return false;
		}

		return WHMHimechanPolicy.CanWeave(DataCenter.DefaultGCDRemain,
			DataCenter.AnimationLock, player.IsCasting, manager->ActionQueued,
			ActionManagerEx.Instance.GetAnimationLockDelayEstimate());
	}

	/// <summary>Execution-time recheck (DoAction) of an ability this rotation picked; explicit commands, opener and raise keep their own rules.</summary>
	internal bool ValidateMaintenanceAction(IAction? action)
	{
		if (action == null || !ReferenceEquals(action, _gatedPick))
		{
			return true;
		}

		if (OpenerOwnsInput || _manualRaise || !InCombat
			|| ReferenceEquals(DataCenter.CommandNextAction, action))
		{
			return true;
		}

		if (!AutoAbilityWeaveOk(RotationSolver.Updaters.ActionUpdater.NextGCDAction))
		{
			LogWeaveDefer(RotationSolver.Updaters.ActionUpdater.NextGCDAction);
			return false;
		}

		return action.AdjustedID != (uint)ActionID.DivineBenisonPvE || BenisonRoutineReady(recheck: true);
	}

	private void LogWeaveDefer(IAction? nextGCD)
	{
		if (IBaseAction.ActionPreview)
		{
			return;
		}

		var now = Now;
		if (now < _weaveLogAfter)
		{
			return;
		}

		_weaveLogAfter = now + 1.0;
		RecordHimechanDiagnostic(string.Format("WEAVE DEFER gcd={0:F3} budget={1:F3} next={2}",
			DataCenter.DefaultGCDRemain,
			WHMHimechanPolicy.WeaveBudget(ActionManagerEx.Instance.GetAnimationLockDelayEstimate()),
			nextGCD?.AdjustedID ?? 0));
	}

	/// <summary>Spec #10. The random lead is drawn once per charge cycle; preview never advances the plan.</summary>
	internal bool BenisonRoutineReady(bool recheck = false)
	{
		if (IBaseAction.ActionPreview && !recheck)
		{
			return false;
		}

		if (!InCombat)
		{
			ResetMaintenanceState();
			return false;
		}

		if (Now < _benisonHoldUntil)
		{
			return false;
		}

		var cooldown = DivineBenisonPvE.Cooldown;
		var maximum = cooldown.MaxCharges;
		var current = cooldown.CurrentCharges;
		var remain = cooldown.RecastTimeRemain;
		if (maximum < 2 || current < 1 || current > maximum)
		{
			_benisonPlanValid = false;
			_benisonObservedCharges = current;
			_benisonObservedRemain = remain;
			return false;
		}

		if (current < _benisonObservedCharges || remain > _benisonObservedRemain + 0.25f)
		{
			_benisonPlanValid = false;
		}

		_benisonObservedCharges = current;
		_benisonObservedRemain = remain;
		if (!_benisonPlanValid && current != maximum)
		{
			if (!float.IsFinite(remain) || remain < 0f)
			{
				return false;
			}

			_benisonPlanLead = WHMHimechanPolicy.BenisonLead(BenisonLeadMin, BenisonLeadMax, Random.Shared);
			_benisonPlanValid = true;
			RecordHimechanDiagnostic(string.Format("BENISON PLAN lead={0:F2}s before cap", _benisonPlanLead));
		}

		// Order (spec #10): GCD push guard (weave) -> charge cap -> random preferred moment.
		return MaintenanceWeaveSafe()
			&& WHMHimechanPolicy.BenisonDue(current, maximum, remain, _benisonPlanLead, DataCenter.DefaultGCDTotal);
	}

	internal void ResetMaintenanceState()
	{
		_benisonPlanValid = false;
		_benisonPlanLead = 0f;
		_benisonObservedCharges = 0;
		_benisonObservedRemain = 0f;
		_benisonHoldUntil = 0;
		_weaveLogAfter = 0;
	}

	internal void MaintenanceEffect(uint id)
	{
		if (id != (uint)ActionID.DivineBenisonPvE)
		{
			return;
		}

		_benisonPlanValid = false;
		// Routine-only duplicate guard after an actual effect, not selection/Use acceptance.
		_benisonHoldUntil = Now + 0.50;
	}
}
