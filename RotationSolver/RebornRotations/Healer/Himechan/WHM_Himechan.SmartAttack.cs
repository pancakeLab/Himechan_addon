using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace RotationSolver.RebornRotations.Healer;

// Spec #33 (user request 2026-10-13): smart target for single-target attack spells pressed by the player.
// With no target or a friendly target, the spell is cast on the enemy my attacks hit most recently; if that one is gone or
// out of range, on the nearest attackable enemy; with none around the press is dropped. The hard target is NOT changed.
// Player input only (manual mode); RSR's own picks in Auto mode keep RSR's targeting.
public sealed partial class WHM_Himechan
{
	[RotationConfig(CombatType.PvE, Name = "공격 주문 스마트 대상: 대상 없음·아군 대상으로 누르면 최근 공격한 적(없으면 가장 가까운 적)에게 시전 (하드 타겟은 유지)")]
	public bool SmartAttackTarget { get; set; } = true;

	private const float AttackSpellRange = 25f;

	private ulong _lastHitEnemy;
	private double _lastHitAt;
	private bool _redirectingAttack;

	private static bool IsSingleTargetAttackSpell(uint adjustedId) => (ActionID)adjustedId is
		ActionID.StonePvE or ActionID.StoneIiPvE or ActionID.StoneIiiPvE or ActionID.StoneIvPvE
		or ActionID.GlarePvE or ActionID.GlareIiiPvE or ActionID.GlareIvPvE
		or ActionID.AeroPvE or ActionID.AeroIiPvE or ActionID.DiaPvE;

	/// <summary>Effect packet of my own action: remember the enemy a damaging spell actually hit.</summary>
	internal void AttackEffect(uint id, IGameObject? target)
	{
		if (target is IBattleChara chara && chara.IsEnemy() && (IsSingleTargetAttackSpell(id) || (ActionID)id is ActionID.HolyPvE or ActionID.HolyIiiPvE or ActionID.AfflatusMiseryPvE or ActionID.AssizePvE))
		{
			_lastHitEnemy = chara.GameObjectId;
			_lastHitAt = Now;
		}
	}

	/// <summary>UseActionDetour: true = this press was replaced (re-issued on an enemy) or dropped (no enemy around).</summary>
	internal unsafe bool TryRedirectAttackInput(uint actionType, uint actionId, ulong targetObjectId, uint useType)
	{
		try
		{
			if (_redirectingAttack || !SmartAttackTarget || actionType != 1 || useType == 1
				|| !IsEnabled || !DataCenter.State || DataCenter.IsPvP || Player is not { IsDead: false })
			{
				return false;
			}

			var adjusted = Service.GetAdjustedActionId(actionId);
			if (!IsSingleTargetAttackSpell(adjusted))
			{
				return false;
			}

			if (targetObjectId != 0 && targetObjectId != 0xE0000000
				&& Svc.Objects.SearchById(targetObjectId) is IBattleChara current && current.IsEnemy())
			{
				return false; // Already aimed at an enemy: untouched.
			}

			var enemy = PickAttackTarget(out var why);
			if (enemy == null)
			{
				RecordHimechanDiagnostic($"ATTACK RETARGET id={adjusted} from=0x{targetObjectId:X} to=none: input dropped");
				return true;
			}

			var manager = ActionManager.Instance();
			if (manager == null)
			{
				return false;
			}

			bool ok;
			_redirectingAttack = true;
			try
			{
				ok = manager->UseAction(ActionType.Action, actionId, enemy.GameObjectId);
			}
			finally
			{
				_redirectingAttack = false;
			}

			RecordHimechanDiagnostic($"ATTACK RETARGET id={adjusted} from=0x{targetObjectId:X} to=0x{enemy.GameObjectId:X} why={why} ok={ok}");
			return true;
		}
		catch (Exception ex)
		{
			ECommons.Logging.PluginLog.Warning($"[Himechan] Attack retarget failed, input passed through: {ex.Message}");
			return false;
		}
	}

	private IBattleChara? PickAttackTarget(out string why)
	{
		why = "last";
		if (_lastHitEnemy != 0 && Svc.Objects.SearchById(_lastHitEnemy) is IBattleChara last && IsAttackable(last))
		{
			return last;
		}

		why = "nearest";
		IBattleChara? best = null;
		var bestDistance = float.MaxValue;
		foreach (var hostile in DataCenter.AllHostileTargets)
		{
			if (hostile == null || !IsAttackable(hostile))
			{
				continue;
			}

			var distance = hostile.DistanceToPlayer();
			if (distance < bestDistance)
			{
				best = hostile;
				bestDistance = distance;
			}
		}

		if (best == null)
		{
			why = "none";
		}
		return best;
	}

	private static bool IsAttackable(IBattleChara chara) =>
		!chara.IsDead && chara.CurrentHp != 0 && chara.IsTargetable && chara.IsEnemy()
		&& chara.DistanceToPlayer() <= AttackSpellRange && chara.CanSee();
}
