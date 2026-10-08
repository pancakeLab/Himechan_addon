using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using RotationSolver.Himechan;
using RotationSolver.Updaters;

namespace RotationSolver.RebornRotations.Healer;

// rev24 manual raise (#14): /히메짱레이즈. Dead check -> Thin Air (if possible) -> Swiftcast (if possible) -> Raise.
public sealed partial class WHM_Himechan
{
	[Range(1f, 10f, ConfigUnitType.Seconds, 0.5f)]
	[RotationConfig(CombatType.PvE, Name = "수동 부활 요청 대기시간 (레이즈 시도 전)")]
	public float ManualRaiseRequestTimeout { get; set; } = 5f;

	private bool _manualRaise;
	private uint _raiseTerritory;
	private ulong _raisePlayer;
	private int _raiseStage;
	private DateTime _raiseWaitUntil;
	private ulong _raiseTarget;
	private DateTime _raiseRequestDeadline;
	private DateTime _submittedCastDeadline;
	private ulong _submittedRaiseTarget;
	private uint _pendingRaisePrep;
	private DateTime _prepDeadline;
	private DateTime _swiftDeadline;

	internal string ManualRaiseStatus { get; private set; } = "대기";

	/// <summary>/히메짱레이즈 [취소|상태]. A repeated request while one is running does nothing.</summary>
	internal void HandleRaiseCommand(string argument)
	{
		var arg = argument.Trim().ToLowerInvariant();
		if (arg is "취소" or "off")
		{
			StopManualRaise("수동 취소", true);
			HimechanProfile.Notify("부활 요청을 취소했습니다.");
			return;
		}

		if (arg is "상태" or "status")
		{
			Svc.Chat.Print($"[히메짱] 부활: {ManualRaiseStatus}");
			return;
		}

		if (arg.Length > 0)
		{
			Svc.Chat.Print("[히메짱] 사용법: /히메짱레이즈 (요청), /히메짱레이즈 취소, /히메짱레이즈 상태");
			return;
		}

		if (_manualRaise)
		{
			return; // Repeated macro presses must not reset preparation.
		}

		if (!IsEnabled || !DataCenter.State || Player == null || Player.IsDead || DataCenter.IsPvP)
		{
			Svc.Chat.Print("[히메짱] 히메짱 WHM과 RSR을 켠 생존 상태에서 요청하세요.");
			return;
		}

		if (RaiseCandidates().Count == 0)
		{
			Svc.Chat.Print("[히메짱] 현재 부활 가능한 파티원이 없습니다.");
			return;
		}

		_manualRaise = true;
		_raiseTerritory = Svc.ClientState.TerritoryType;
		_raisePlayer = Player.GameObjectId;
		_raiseStage = 0;
		_pendingRaisePrep = 0;
		_swiftDeadline = DateTime.MinValue;
		_raiseRequestDeadline = DateTime.UtcNow.AddSeconds(WHMHimechanPolicy.RequestTimeout(ManualRaiseRequestTimeout));
		_submittedRaiseTarget = 0;
		_raiseWaitUntil = DateTime.MinValue;
		ManualRaiseStatus = "실바람 준비";
		RecordHimechanDiagnostic("RAISE REQ");
	}

	internal unsafe void StopManualRaise(string reason, bool cancelCast = false)
	{
		if (cancelCast && _submittedRaiseTarget != 0 && Player?.IsCasting == true
			&& Player.CastActionId == (uint)ActionID.RaisePvE)
		{
			var ui = UIState.Instance();
			if (ui != null)
			{
				ui->Hotbar.CancelCast();
			}
		}

		if (_manualRaise)
		{
			ActionUpdater.ClearNextAction();
			RecordHimechanDiagnostic($"RAISE END: {reason}");
		}

		_manualRaise = false;
		_submittedRaiseTarget = 0;
		ManualRaiseStatus = reason;
	}

	private static bool IsRaiseAction(uint id) => id is (uint)ActionID.RaisePvE
		or (uint)ActionID.ResurrectionPvE or (uint)ActionID.AscendPvE
		or (uint)ActionID.EgeiroPvE or (uint)ActionID.VerraisePvE
		or (uint)ActionID.AngelWhisperPvE;

	private bool OtherIsRaising(IBattleChara target, float ours = float.MaxValue)
	{
		foreach (var p in DataCenter.AllTargets)
		{
			if (p.GameObjectId != Player?.GameObjectId && !p.IsDead
				&& !p.IsEnemy() && p.IsCasting && IsRaiseAction(p.CastActionId)
				&& p.CastTargetObjectId == target.GameObjectId
				&& WHMHimechanPolicy.OtherRaiseWins(ours, p.TotalCastTime - p.CurrentCastTime))
			{
				return true;
			}
		}
		return false;
	}

	private List<IBattleChara> RaiseCandidates()
	{
		List<IBattleChara> result = [];
		foreach (var p in DataCenter.PartyMembers)
		{
			if (p.GameObjectId != Player?.GameObjectId && p.IsDead && p.CurrentHp == 0
				&& p.IsTargetable && !p.IsEnemy() && p.DistanceToPlayer() <= 30 && p.CanSee()
				&& !p.HasStatus(false, StatusID.Raise, StatusID.ResurrectionDenied)
				&& p.CanBeRaised())
			{
				result.Add(p);
			}
		}
		return result;
	}

	private static unsafe int PartyDisplayIndex(IBattleChara member)
	{
		var hud = AgentHUD.Instance();
		if (hud != null)
		{
			for (var i = 0; i < Math.Min((int)hud->PartyMemberCount, 8); i++)
			{
				if (hud->PartyMembers[i].EntityId == member.EntityId)
				{
					return i;
				}
			}
		}

		// Do not rely on object-table order if the HUD is not available.
		for (var i = 0; i < Svc.Party.Length; i++)
		{
			if (Svc.Party[i]?.EntityId == member.EntityId)
			{
				return 100 + i;
			}
		}
		return int.MaxValue;
	}

	private IBattleChara? SelectRaiseTarget(List<IBattleChara> candidates)
	{
		var livingTank = false;
		foreach (var p in DataCenter.PartyMembers)
		{
			if (!p.IsDead && HimechanTanks.IsTank(p))
			{
				livingTank = true;
				break;
			}
		}

		var deadTanks = 0;
		foreach (var p in candidates)
		{
			if (HimechanTanks.IsTank(p))
			{
				deadTanks++;
			}
		}

		IBattleChara? best = null;
		var bestPriority = int.MaxValue;
		var bestIndex = int.MaxValue;
		foreach (var p in candidates)
		{
			if (OtherIsRaising(p))
			{
				continue;
			}

			var role = HimechanTanks.IsTank(p) ? 1 : p.IsJobCategory(JobRole.Healer) ? 2 : 3;
			var priority = WHMHimechanPolicy.RaisePriority(role, deadTanks, livingTank);
			var index = PartyDisplayIndex(p);
			if (best == null || priority < bestPriority || (priority == bestPriority && index < bestIndex))
			{
				best = p;
				bestPriority = priority;
				bestIndex = index;
			}
		}
		return best;
	}

	/// <summary>Called instead of the normal rotation only while explicitly requested. true + null means wait.</summary>
	internal bool TryManualRaise(out IAction? next, out IBaseAction? gcd)
	{
		next = null;
		gcd = null;
		if (!_manualRaise)
		{
			return false;
		}

		if (!IsEnabled || !DataCenter.State || Player == null || Player.IsDead || DataCenter.IsPvP
			|| Svc.ClientState.TerritoryType != _raiseTerritory || Player.GameObjectId != _raisePlayer)
		{
			StopManualRaise("상태 변경으로 종료");
			return false;
		}

		if (DateTime.UtcNow >= _raiseRequestDeadline)
		{
			StopManualRaise("부활 요청 대기시간 만료");
			return false;
		}

		if (DateTime.UtcNow < _raiseWaitUntil || Player.IsCasting)
		{
			return true;
		}

		if (_pendingRaisePrep != 0)
		{
			var applied = _pendingRaisePrep == (uint)ActionID.ThinAirPvE ? HasThinAir : HasSwift;
			if (!applied && DateTime.UtcNow < _prepDeadline)
			{
				return true;
			}
			_pendingRaisePrep = 0;
		}

		var candidates = RaiseCandidates();
		if (candidates.Count == 0)
		{
			StopManualRaise("부활 후보 없음 — 종료");
			return false;
		}

		var target = SelectRaiseTarget(candidates);
		if (target == null)
		{
			ManualRaiseStatus = "다른 파티원의 부활 시전 대기";
			return true;
		}

		_raiseTarget = target.GameObjectId;
		if (DataCenter.MergedStatus.HasFlag(AutoStatus.NoCasting)
			|| (Service.Config.UseBmrTimeline && (DataCenter.BMRForceCancelCast || DataCenter.BMRForceCancelCastAI)))
		{
			return true;
		}

		if (_raiseStage == 0 && (HasThinAir || !ThinAirPvE.Info.EnoughLevelAndQuest() || !ThinAirPvE.Cooldown.HasOneCharge))
		{
			_raiseStage = 1;
		}

		if (_raiseStage == 1 && (HasSwift || !SwiftcastPvE.Info.EnoughLevelAndQuest() || !SwiftcastPvE.Cooldown.HasOneCharge))
		{
			_raiseStage = 2;
		}

		if (_raiseStage > 0 && !HasThinAir && Player.CurrentMp < RaisePvE.Info.MPNeed)
		{
			ManualRaiseStatus = "레이즈 MP 부족 — 대기";
			return true;
		}

		var preview = IBaseAction.ActionPreview;
		var force = IBaseAction.ForceEnable;
		var end = IBaseAction.ShouldEndSpecial;
		var targetOverride = IBaseAction.TargetOverride;
		try
		{
			IBaseAction.ActionPreview = false;
			IBaseAction.ForceEnable = true; // Explicit request; do not change stored settings.
			IBaseAction.ShouldEndSpecial = false;
			IBaseAction.TargetOverride = null;
			if (_raiseStage < 2)
			{
				ManualRaiseStatus = _raiseStage == 0 ? "실바람 위빙 대기" : "신속마 위빙 대기";
				if (!WHMHimechanPolicy.RaiseCanWeave(DataCenter.DefaultGCDRemain, DataCenter.AnimationLock))
				{
					return true;
				}

				var ability = _raiseStage == 0 ? ThinAirPvE : SwiftcastPvE;
				using var enabled = new WHMRequestedActionScope(ability);
				if (ability.CanUse(out var act, usedUp: true, skipTTKCheck: true, targetOverride: TargetType.Self))
				{
					next = act;
				}
				return true;
			}

			if (!HasSwift && DateTime.UtcNow < _swiftDeadline)
			{
				return true;
			}

			ManualRaiseStatus = HasSwift ? "즉시 레이즈 대기" : "정지 후 레이즈 대기";
			if (!HasSwift && DataCenter.IsMoving)
			{
				return true;
			}

			var originalTarget = DataCenter.DeathTarget;
			var filter = RaisePvE.Setting.CanTarget;
			var check = RaisePvE.Setting.ActionCheck;
			try
			{
				DataCenter.DeathTarget = target;
				RaisePvE.Setting.CanTarget = p => p.GameObjectId == target.GameObjectId && filter(p);
				// Explicit Raise uses the actual MP cost; the general auto-raise reserve does not veto it.
				RaisePvE.Setting.ActionCheck = () => HasThinAir || Player.CurrentMp >= RaisePvE.Info.MPNeed;
				using var enabled = new WHMRequestedActionScope(RaisePvE);
				if (RaisePvE.CanUse(out var act, usedUp: true, skipTTKCheck: true, targetOverride: TargetType.Death))
				{
					next = act;
					gcd = RaisePvE;
				}
			}
			finally
			{
				DataCenter.DeathTarget = originalTarget;
				RaisePvE.Setting.CanTarget = filter;
				RaisePvE.Setting.ActionCheck = check;
			}
			return true;
		}
		finally
		{
			IBaseAction.ActionPreview = preview;
			IBaseAction.ForceEnable = force;
			IBaseAction.ShouldEndSpecial = end;
			IBaseAction.TargetOverride = targetOverride;
		}
	}

	internal bool ValidateManualRaiseAction(IAction action)
	{
		if (!_manualRaise)
		{
			return true;
		}

		if (DateTime.UtcNow >= _raiseRequestDeadline)
		{
			StopManualRaise("부활 요청 대기시간 만료");
			return false;
		}

		if (action.ID != (uint)ActionID.RaisePvE && action.ID != (uint)ActionID.ThinAirPvE
			&& action.ID != (uint)ActionID.SwiftcastPvE)
		{
			return false;
		}

		var target = SelectRaiseTarget(RaiseCandidates());
		if (target == null || target.GameObjectId != _raiseTarget)
		{
			return false;
		}

		if (action.ID == (uint)ActionID.RaisePvE)
		{
			return (!DataCenter.IsMoving || HasSwift) && (action as IBaseAction)?.Target.Target?.GameObjectId == _raiseTarget;
		}

		return WHMHimechanPolicy.RaiseCanWeave(DataCenter.DefaultGCDRemain, DataCenter.AnimationLock);
	}

	internal void ManualRaiseActionSubmitted(uint id)
	{
		if (!_manualRaise)
		{
			return;
		}

		if (id is (uint)ActionID.ThinAirPvE or (uint)ActionID.SwiftcastPvE)
		{
			// Advance on accepted execution, never on mere CanUse evaluation.
			_raiseStage = id == (uint)ActionID.ThinAirPvE ? 1 : 2;
			_pendingRaisePrep = id;
			_prepDeadline = DateTime.UtcNow.AddSeconds(2);
			if (id == (uint)ActionID.SwiftcastPvE)
			{
				_swiftDeadline = _prepDeadline;
			}
			_raiseWaitUntil = DateTime.UtcNow.AddSeconds(0.05);
		}

		if (id == (uint)ActionID.RaisePvE)
		{
			// One accepted attempt consumes the request, even if the cast is later cancelled.
			_submittedRaiseTarget = _raiseTarget;
			_submittedCastDeadline = DateTime.UtcNow.AddSeconds(12);
			_manualRaise = false;
			ActionUpdater.ClearNextAction();
			ManualRaiseStatus = "레이즈 시도 — 요청 종료";
			RecordHimechanDiagnostic($"RAISE SUBMIT target=0x{_raiseTarget:X}");
		}
	}

	internal void ManualRaiseEffect(uint id, ulong targetId)
	{
		if (_manualRaise && id == _pendingRaisePrep)
		{
			_pendingRaisePrep = 0;
			return;
		}

		if (id == (uint)ActionID.RaisePvE && targetId == _submittedRaiseTarget)
		{
			_submittedRaiseTarget = 0;
		}
	}

	/// <summary>Cancel our Raise cast when the target no longer needs it (alive, raised, or someone else finishes first).</summary>
	internal bool ShouldCancelManualRaise(IBattleChara? target) =>
		_submittedRaiseTarget != 0 && DateTime.UtcNow < _submittedCastDeadline
		&& Player?.CastTargetObjectId == _submittedRaiseTarget && Player?.CastActionId == (uint)ActionID.RaisePvE
		&& (target == null || !target.IsDead || target.HasStatus(false, StatusID.Raise, StatusID.ResurrectionDenied)
			|| OtherIsRaising(target, Player.TotalCastTime - Player.CurrentCastTime));
}
