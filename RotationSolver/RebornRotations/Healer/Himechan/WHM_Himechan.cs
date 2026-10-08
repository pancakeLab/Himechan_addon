using System.ComponentModel;
using RotationSolver.Himechan;

namespace RotationSolver.RebornRotations.Healer;

// Keep this type name and namespace: rotation settings are stored under its full name, so existing
// Himechan users keep their settings. Ported from rev24 (+ rev25-32 IL features); spec chapter 3.
[Rotation("히메짱 WHM", CombatType.PvE, GameVersion = "7.55")]
public sealed partial class WHM_Himechan : WhiteMageRotation
{
	private const float MovementSwiftcastFollowupTimeout = 6f;

	private readonly HashSet<ulong> _manuallyTaggedAddIds = [];
	private bool _wasInCombatForDotTracking;
	private float _lastTrackedCombatTime;
	private float _movementSwiftcastRequestedAt = float.MinValue;

	#region Config Options
	[RotationConfig(CombatType.PvE, Name = "고난도 임무에서 밸런스 오프너 사용")]
	public bool UseOpenerHighEnd { get; set; } = true;

	[RotationConfig(CombatType.PvE, Name = "쾌속의 마법 사용 직전에 환혹약/보석수 사용")]
	public bool UseMedicine { get; set; } = true;

	[RotationConfig(CombatType.PvE, Name = "부활 자동 상태에서 신속한 마법을 레이즈용으로 보호")]
	public bool SwiftLogic { get; set; } = true;

	// Spec #7: kept as-is (no rework), default OFF.
	[RotationConfig(CombatType.PvE, Name = "이동 중 GCD 후보가 없으면 신속한 마법 후 글레어 사용")]
	public bool UseSwiftcastForMovementFallback { get; set; } = false;

	[Range(0.1f, 5f, ConfigUnitType.Seconds, 0.1f)]
	[RotationConfig(CombatType.PvE, Name = "이동 신속마: 필요한 연속 이동 시간", Parent = nameof(UseSwiftcastForMovementFallback), ParentValue = true)]
	public float MovementSwiftcastThreshold { get; set; } = 1.3f;

	[RotationConfig(CombatType.PvE, Name = "일반 GCD 회복 허용 (제한형 백합·부활과 별개, 1힐러여도 적용)")]
	public bool GCDHeal { get; set; } = false;

	[RotationConfig(CombatType.PvE, Name = "내가 먼저 DoT를 부여한 비보스 대상의 DoT를 자동 갱신")]
	public bool RefreshManuallyTaggedAdds { get; set; } = true;

	[Range(0, 120, ConfigUnitType.Seconds, 1)]
	[RotationConfig(CombatType.PvE, Name = "직접 DoT를 부여한 비보스 대상의 DoT 갱신에 필요한 최소 예상 생존 시간", Parent = nameof(RefreshManuallyTaggedAdds), ParentValue = true)]
	public float ManuallyTaggedAddMinimumTtk { get; set; } = 15f;

	// Spec #9: was hard-coded to M9S (Fatal Flail 0x4AE1, Deadly Doornail 0x4AE2).
	[RotationConfig(CombatType.PvE, Name = "자동 디아 제외 대상 (BaseId, 쉼표로 구분. 직접 디아는 허용)",
		Tooltip = "여기 적은 적에게는 자동 디아를 처음 걸거나 갱신하지 않습니다. 16진수(0x4AE1) 또는 10진수로 적습니다. 기본값은 M9S 기믹 오브젝트입니다.")]
	public string AutoDiaExcludedBaseIds { get; set; } = "0x4AE1, 0x4AE2";

	[RotationConfig(CombatType.PvE, Name = "제한형 치유의 백합 자동 사용")]
	public bool UseRestrictedLilyAutomation { get; set; } = true;

	[Range(1, 20, ConfigUnitType.Seconds, 1)]
	[RotationConfig(CombatType.PvE, Name = "백합 재시도 시작: 다음 충전까지 남은 시간", Parent = nameof(UseRestrictedLilyAutomation), ParentValue = true)]
	public float LilyOvercapWindow { get; set; } = 9f;

	[Range(0, 20, ConfigUnitType.Seconds, 0.1f)]
	[RotationConfig(CombatType.PvE, Name = "백합 재시도 종료: 다음 충전까지 남은 시간", Parent = nameof(UseRestrictedLilyAutomation), ParentValue = true)]
	public float LilyRetryEnd { get; set; } = 3f;

	[Range(0, 1, ConfigUnitType.Percent)]
	[RotationConfig(CombatType.PvE, Name = "황홀한 마음을 선택할 파티 평균 HP 기준값", Parent = nameof(UseRestrictedLilyAutomation), ParentValue = true)]
	public float LilyAreaHealThreshold { get; set; } = 0.95f;

	[RotationConfig(CombatType.PvE, Name = "전투 시작 카운트다운 5초에 탱커에게 리제네 사용")]
	public bool UsePreRegen { get; set; } = true;

	[RotationConfig(CombatType.PvE, Name = "신성한 손길을 사용 가능해지는 즉시 사용")]
	public bool UseDivine { get; set; } = false;

	[RotationConfig(CombatType.PvE, Name = "정지 중 단일 대상 회복에 성소 추가 사용")]
	public bool AsylumSingle { get; set; } = false;

	[Range(0, 1, ConfigUnitType.Percent)]
	[RotationConfig(CombatType.PvE, Name = "거룩한 축복을 사용할 파티원 HP 기준값")]
	public float BenedictionHeal { get; set; } = 0.3f;

	[Range(0, 1, ConfigUnitType.Percent)]
	[RotationConfig(CombatType.PvE, Name = "마지막 신의 이름을 사용하도록 허용할 응급 HP 기준값")]
	public float TetragrammatonEmergencyHeal { get; set; } = 0.4f;

	[Range(0, 1, ConfigUnitType.Percent)]
	[RotationConfig(CombatType.PvE, Name = "파티원 HP가 이 값 미만이면 해당 대상에게 리제네를 사용하지 않음")]
	public float RegenHeal { get; set; } = 0.3f;

	[Range(0, 10000, ConfigUnitType.None, 100)]
	[RotationConfig(CombatType.PvE, Name = "실바람을 사용할 주문의 최소 MP 소모량")]
	public float ThinAirNeed { get; set; } = 1000;

	[RotationConfig(CombatType.PvE, Name = "실바람의 마지막 충전 관리 방식")]
	public ThinAirUsageStrategy ThinAirLastChargeUsage { get; set; } = ThinAirUsageStrategy.ReserveLastChargeForRaise;

	// Stored by RSR as the Description text: never change these texts, or saved values stop matching.
	// The default comes first, because an unreadable saved value falls back to the first entry.
	public enum ThinAirUsageStrategy : byte
	{
		[Description("마지막 충전을 레이즈용으로 보존")]
		ReserveLastChargeForRaise,

		[Description("MP 소모량이 큰 주문에 모든 실바람 충전 사용")]
		UseAllCharges,

		[Description("마지막 충전을 수동 사용용으로 보존")]
		ReserveLastCharge,
	}

	// Spec #10: random lead before Divine Benison reaches its charge cap (rev24 used 1.0-3.0s).
	[Range(0, 5, ConfigUnitType.Seconds, 0.1f)]
	[RotationConfig(CombatType.PvE, Name = "신성한 축복: 충전 상한 전 사용 시점 최소 (초)")]
	public float BenisonLeadMin { get; set; } = 0.5f;

	[Range(0, 5, ConfigUnitType.Seconds, 0.1f)]
	[RotationConfig(CombatType.PvE, Name = "신성한 축복: 충전 상한 전 사용 시점 최대 (초)")]
	public float BenisonLeadMax { get; set; } = 2.0f;
	#endregion

	#region Countdown Logic
	protected override IAction? CountDownAction(float remainTime)
	{
		if (UseHimechanOpener)
		{
			return null; // Dedicated opener owns or skips this countdown.
		}

		if (remainTime < StonePvE.Info.CastTime + CountDownAhead
			&& StonePvE.CanUse(out var act))
		{
			return act;
		}

		if (UseMedicine && remainTime < 3 && UseBurstMedicine(out act))
		{
			return act;
		}

		if (UsePreRegen && remainTime <= 5 && remainTime > 3)
		{
			if (RegenPvE.CanUse(out act, targetOverride: TargetType.Tank))
			{
				return act;
			}
		}
		return base.CountDownAction(remainTime);
	}
	#endregion

	#region oGCD Logic
	// Spec #11: every automatic ability chosen by Himechan goes through WeaveGate first.
	protected override bool EmergencyAbility(IAction nextGCD, out IAction? act) =>
		WeaveGate(nextGCD, EmergencyAbilityCore, out act);

	private bool EmergencyAbilityCore(IAction nextGCD, out IAction? act)
	{
		if (TryTankbusterAquaveil(nextGCD, out act))
		{
			return true;
		}

		if (TryUseLastTetragrammatonForEmergency(out act))
		{
			return true;
		}

		if (TryUseSpareTetragrammaton(out act))
		{
			return true;
		}

		// rev25 F1: a death or a planned Raise alone no longer spends Thin Air (manual raise has its own stages);
		// the MP trigger also excludes Raise.
		var useLastThinAirCharge = ThinAirLastChargeUsage == ThinAirUsageStrategy.UseAllCharges;
		if (nextGCD is IBaseAction action && !ReferenceEquals(nextGCD, RaisePvE)
			&& action.Info.MPNeed >= ThinAirNeed && IsLastAction() == IsLastGCD()
			&& ThinAirPvE.CanUse(out act, usedUp: useLastThinAirCharge))
		{
			return true;
		}

		if (StatusHelper.PlayerWillStatusEndGCD(2, 0, true, StatusID.DivineGrace) && DivineCaressPvE.CanUse(out act))
		{
			return true;
		}

		if (UseMedicine && !PresenceOfMindPvE.Cooldown.IsCoolingDown && UseBurstMedicine(out act))
		{
			return true;
		}

		if (nextGCD.IsTheSameTo(true, AfflatusRapturePvE, MedicaPvE, MedicaIiPvE, CureIiiPvE)
			&& (MergedStatus.HasFlag(AutoStatus.HealAreaSpell) || MergedStatus.HasFlag(AutoStatus.HealSingleSpell)))
		{
			if (PlenaryIndulgencePvE.CanUse(out act))
			{
				return true;
			}
		}

		return base.EmergencyAbility(nextGCD, out act);
	}

	[RotationDesc(ActionID.DivineBenisonPvE, ActionID.DivineCaressPvE)]
	protected override bool GeneralAbility(IAction nextGCD, out IAction? act) =>
		WeaveGate(nextGCD, GeneralAbilityCore, out act);

	private bool GeneralAbilityCore(IAction nextGCD, out IAction? act)
	{
		if (TryUseBenisonAtChargeCap(out act))
		{
			return true;
		}

		if (UseDivine && DivineCaressPvE.CanUse(out act))
		{
			return true;
		}

		if (LucidDreamingPvE.CanUse(out act))
		{
			return true;
		}

		if (TryUseSwiftcastForMovementFallback(nextGCD, out act))
		{
			return true;
		}

		return base.GeneralAbility(nextGCD, out act);
	}

	[RotationDesc(ActionID.TemperancePvE, ActionID.PlenaryIndulgencePvE)]
	protected override bool DefenseAreaAbility(IAction nextGCD, out IAction? act) =>
		WeaveGate(nextGCD, DefenseAreaAbilityCore, out act);

	private bool DefenseAreaAbilityCore(IAction nextGCD, out IAction? act)
	{
		// Bell is manual-only in this assist; its cooldown must not suppress other mitigation.
		if (PlenaryIndulgencePvE.CanUse(out act) || TemperancePvE.CanUse(out act)
			|| DivineCaressPvE.CanUse(out act))
		{
			return true;
		}
		return base.DefenseAreaAbility(nextGCD, out act);
	}

	protected override bool DefenseSingleAbility(IAction nextGCD, out IAction? act)
	{
		// Aquaveil is used only by Himechan's tankbuster logic (TryTankbusterAquaveil in EmergencyAbility),
		// never by RSR's generic single-target defense trigger (user request 2026-10-08: no "on cooldown" use).
		// Divine Benison keeps its own policy in GeneralAbility.
		act = null;
		return false;
	}

	[RotationDesc(ActionID.AsylumPvE)]
	protected override bool HealAreaAbility(IAction nextGCD, out IAction? act) =>
		WeaveGate(nextGCD, HealAreaAbilityCore, out act);

	private bool HealAreaAbilityCore(IAction nextGCD, out IAction? act)
	{
		if (AsylumPvE.CanUse(out act))
		{
			return true;
		}
		return base.HealAreaAbility(nextGCD, out act);
	}

	[RotationDesc(ActionID.BenedictionPvE, ActionID.AsylumPvE, ActionID.TetragrammatonPvE)]
	protected override bool HealSingleAbility(IAction nextGCD, out IAction? act) =>
		WeaveGate(nextGCD, HealSingleAbilityCore, out act);

	private bool HealSingleAbilityCore(IAction nextGCD, out IAction? act)
	{
		if (BenedictionPvE.CanUse(out act) &&
			BenedictionPvE.Target.Target.GetHealthRatio() < BenedictionHeal)
		{
			return true;
		}

		if (IsLastAction(ActionID.BenedictionPvE))
		{
			return base.HealSingleAbility(nextGCD, out act);
		}

		if (AsylumSingle && !IsMoving && AsylumPvE.CanUse(out act))
		{
			return true;
		}

		return base.HealSingleAbility(nextGCD, out act);
	}

	protected override bool AttackAbility(IAction nextGCD, out IAction? act) =>
		WeaveGate(nextGCD, AttackAbilityCore, out act);

	private bool AttackAbilityCore(IAction nextGCD, out IAction? act)
	{
		if (InCombat)
		{
			if (!IsInHighEndDuty || !UseOpenerHighEnd || (IsInHighEndDuty && UseOpenerHighEnd && !CombatElapsedLessGCD(3)))
			{
				if (PresenceOfMindPvE.CanUse(out act, skipTTKCheck: IsInHighEndDuty))
				{
					return true;
				}
			}

			if (!IsInHighEndDuty || !UseOpenerHighEnd || (IsInHighEndDuty && UseOpenerHighEnd && !CombatElapsedLessGCD(4)))
			{
				if (AssizePvE.CanUse(out act, skipAoeCheck: true))
				{
					return true;
				}
			}
		}

		return base.AttackAbility(nextGCD, out act);
	}
	#endregion

	#region GCD Logic
	[RotationDesc(ActionID.MedicaIiPvE, ActionID.CureIiiPvE, ActionID.MedicaPvE)]
	protected override bool HealAreaGCD(out IAction? act)
	{
		act = null;
		if (!GCDHeal)
		{
			return false;
		}

		if ((HasSwift || IsLastAction(ActionID.SwiftcastPvE)) && SwiftLogic && MergedStatus.HasFlag(AutoStatus.Raise))
		{
			return base.HealAreaGCD(out act);
		}

		var hasMedica2 = 0;
		var partyCount = 0;
		foreach (var n in PartyMembers)
		{
			partyCount++;
			if (n.HasStatus(true, StatusID.MedicaIi))
			{
				hasMedica2++;
			}
		}

		if (MedicaIiPvE.EnoughLevel)
		{
			if (MedicaIiiPvE.EnoughLevel && MedicaIiiPvE.CanUse(out act) && hasMedica2 < partyCount / 2 && !IsLastAction(true, MedicaIiPvE))
			{
				return true;
			}

			if (!MedicaIiiPvE.EnoughLevel && MedicaIiPvE.CanUse(out act) && hasMedica2 < partyCount / 2 && !IsLastAction(true, MedicaIiPvE))
			{
				return true;
			}
		}

		if (CureIiiPvE.CanUse(out act))
		{
			return true;
		}

		if (MedicaPvE.CanUse(out act))
		{
			return true;
		}

		return base.HealAreaGCD(out act);
	}

	[RotationDesc(ActionID.RegenPvE, ActionID.CureIiPvE, ActionID.CurePvE)]
	protected override bool HealSingleGCD(out IAction? act)
	{
		act = null;
		if (!GCDHeal)
		{
			return false;
		}

		if ((HasSwift || IsLastAction(ActionID.SwiftcastPvE)) && SwiftLogic && MergedStatus.HasFlag(AutoStatus.Raise))
		{
			return base.HealSingleGCD(out act);
		}

		if (RegenPvE.CanUse(out act) && (RegenPvE.Target.Target.GetHealthRatio() > RegenHeal))
		{
			return true;
		}

		if (CureIiPvE.CanUse(out act))
		{
			return true;
		}

		if (CurePvE.CanUse(out act))
		{
			return true;
		}

		return base.HealSingleGCD(out act);
	}

	[RotationDesc(ActionID.RaisePvE)]
	protected override bool RaiseGCD(out IAction? act)
	{
		if (RaisePvE.CanUse(out act))
		{
			return true;
		}

		return base.RaiseGCD(out act);
	}

	protected override bool GeneralGCD(out IAction? act)
	{
		if (HasThinAir && MergedStatus.HasFlag(AutoStatus.Raise))
		{
			return RaiseGCD(out act);
		}

		if ((HasSwift || IsLastAction(ActionID.SwiftcastPvE)) && SwiftLogic && MergedStatus.HasFlag(AutoStatus.Raise))
		{
			return base.GeneralGCD(out act);
		}

		if (TryUseCurrentManagedDot(out act))
		{
			return true;
		}

		if (TryUseMovementSwiftcastGlare(out act))
		{
			return true;
		}

		var liliesNearlyFull = WHMHimechanPolicy.IsLilyRetryWindow(Lily,
			LilyTime + DataCenter.DefaultGCDRemain, LilyOvercapWindow, LilyRetryEnd);
		// Afflatus Misery is reserved for manual burst use.

		if (InCombat && UseRestrictedLilyAutomation
			&& liliesNearlyFull
			&& !HasPresenceOfMind
			&& !IsLastAction(ActionID.PresenceOfMindPvE))
		{
			if (TryUseCurrentManagedDot(out act))
			{
				return true;
			}

			if (TryUsePreferredLilyHeal(out act))
			{
				return true;
			}
		}

		if (GlareIvPvE.CanUse(out act))
		{
			return true;
		}

		if (HolyPvE.EnoughLevel)
		{
			if (HolyIiiPvE.EnoughLevel && HolyIiiPvE.CanUse(out act))
			{
				return true;
			}
			if (HolyPvE.EnoughLevel && !HolyIiiPvE.EnoughLevel && HolyPvE.CanUse(out act))
			{
				return true;
			}
		}

		if (TryUseLeveledManagedDot(out act))
		{
			return true;
		}

		if (GlareIiiPvE.EnoughLevel && GlareIiiPvE.CanUse(out act))
		{
			return true;
		}
		if (GlarePvE.EnoughLevel && !GlareIiiPvE.EnoughLevel && GlarePvE.CanUse(out act))
		{
			return true;
		}
		if (StoneIvPvE.EnoughLevel && !GlarePvE.EnoughLevel && StoneIvPvE.CanUse(out act))
		{
			return true;
		}
		if (StoneIiiPvE.EnoughLevel && !StoneIvPvE.EnoughLevel && StoneIiiPvE.CanUse(out act))
		{
			return true;
		}
		if (StoneIiPvE.EnoughLevel && !StoneIiiPvE.Info.EnoughLevelAndQuest() && StoneIiPvE.CanUse(out act))
		{
			return true;
		}
		if (!StoneIiPvE.EnoughLevel && StonePvE.CanUse(out act))
		{
			return true;
		}

		if (TryUseLeveledManagedDot(out act))
		{
			return true;
		}

		return base.GeneralGCD(out act);
	}

	private bool TryUseLeveledManagedDot(out IAction? act)
	{
		act = null;
		if (!AeroPvE.EnoughLevel)
		{
			return false;
		}

		if (DiaPvE.EnoughLevel)
		{
			return TryUseManagedDot(DiaPvE, out act);
		}

		if (AeroIiPvE.EnoughLevel)
		{
			return TryUseManagedDot(AeroIiPvE, out act);
		}

		return TryUseManagedDot(AeroPvE, out act);
	}
	#endregion

	#region Extra Methods
	protected override void UpdateInfo()
	{
		base.UpdateInfo();
		MigrateLegacyOpenerSettings();
		if (!InCombat || DataCenter.CombatTimeRaw < _lastTrackedCombatTime)
		{
			ResetMaintenanceState();
		}

		if (DataCenter.CombatTimeRaw < _lastTrackedCombatTime)
		{
			_manuallyTaggedAddIds.Clear();
			_wasInCombatForDotTracking = false;
		}
		_lastTrackedCombatTime = DataCenter.CombatTimeRaw;
		UpdateManuallyTaggedAdds();
		if (!InCombat || !UseSwiftcastForMovementFallback
			|| DataCenter.CombatTimeRaw < _movementSwiftcastRequestedAt
			|| DataCenter.CombatTimeRaw - _movementSwiftcastRequestedAt > MovementSwiftcastFollowupTimeout)
		{
			_movementSwiftcastRequestedAt = float.MinValue;
		}
	}

	public override void DisplayBaseStatus()
	{
		base.DisplayBaseStatus();
		ImGui.Text($"히메짱 {HimechanMain.VersionText} | 오프너: {_openerStatus} | 실제 GCD: {GlareIiiPvE.Cooldown.RecastTimeOneChargeRaw:F2}s");
		ImGui.Text($"수동 부활: {ManualRaiseStatus} | 테스트 모드: {(HimechanTanks.TestModeActive ? "동작" : "꺼짐")}");
		ImGui.Text($"백합 충전까지: {LilyTime + DataCenter.DefaultGCDRemain:F2}s | 재시도 {LilyRetryEnd:F1}..{LilyOvercapWindow:F1}s | 일반 GCD 회복: {GCDHeal}");
		ImGui.Text($"BMR: {BMRActive} | 타임라인 TB: {DataCenter.BMRDebugTimelineTankbuster} | 종합 TB: {BMRTankbusterIn}");
		ImGui.Text($"이동: {DataCenter.MovingRaw:F2}/{MovementSwiftcastThreshold:F1}s");
	}

	private bool TryUseSwiftcastForMovementFallback(IAction nextGCD, out IAction? act)
	{
		act = null;
		var hasPlannedGcd = nextGCD is IBaseAction gcd && gcd.Info.IsRealGCD;

		if (!UseSwiftcastForMovementFallback
			|| !InCombat
			|| !IsMoving
			|| !WHMHimechanPolicy.CanUseMovementSwiftcast(DataCenter.MovingRaw, MovementSwiftcastThreshold, hasPlannedGcd)
			|| HasSwift
			|| IsLastAction(ActionID.SwiftcastPvE)
			|| MergedStatus.HasFlag(AutoStatus.Raise)
			|| !CanUseMovementGlare(out _, skipCastingCheck: true)
			|| !CanUseMovementSwiftcast(out act))
		{
			return false;
		}

		if (!IBaseAction.ActionPreview)
		{
			_movementSwiftcastRequestedAt = DataCenter.CombatTimeRaw;
		}
		return true;
	}

	private bool TryUseMovementSwiftcastGlare(out IAction? act)
	{
		act = null;
		if (!UseSwiftcastForMovementFallback
			|| !InCombat
			|| _movementSwiftcastRequestedAt <= float.MinValue / 2
			|| DataCenter.CombatTimeRaw - _movementSwiftcastRequestedAt > MovementSwiftcastFollowupTimeout
			|| MergedStatus.HasFlag(AutoStatus.Raise))
		{
			_movementSwiftcastRequestedAt = float.MinValue;
			return false;
		}

		return HasSwift && CanUseMovementGlare(out act);
	}

	private bool CanUseMovementSwiftcast(out IAction? act)
	{
		var movementAction = SwiftcastPvE;
		var originalEnabled = movementAction.IsEnabled;
		movementAction.IsEnabled = true;
		try
		{
			return SwiftcastPvE.CanUse(out act);
		}
		finally
		{
			movementAction.IsEnabled = originalEnabled;
		}
	}

	private bool CanUseMovementGlare(out IAction? act, bool skipCastingCheck = false)
	{
		var movementAction = GlareIiiPvE.EnoughLevel ? GlareIiiPvE
			: GlarePvE.EnoughLevel ? GlarePvE
			: StoneIvPvE.EnoughLevel ? StoneIvPvE
			: StoneIiiPvE.EnoughLevel ? StoneIiiPvE
			: StoneIiPvE.EnoughLevel ? StoneIiPvE : StonePvE;
		var originalEnabled = movementAction.IsEnabled;
		movementAction.IsEnabled = true;
		try
		{
			return movementAction.CanUse(out act, skipCastingCheck: skipCastingCheck);
		}
		finally
		{
			movementAction.IsEnabled = originalEnabled;
		}
	}

	private static bool IsDotCompetingFiller(uint id) => (ActionID)id is
		ActionID.StonePvE or ActionID.StoneIiPvE or ActionID.StoneIiiPvE or ActionID.StoneIvPvE
		or ActionID.GlarePvE or ActionID.GlareIiiPvE or ActionID.HolyPvE or ActionID.HolyIiiPvE;

	/// <summary>#20: a queued (command) filler GCD gives way to a due managed DoT. Called from the Basic GCD hook.</summary>
	internal bool OverrideQueuedGCD(IBaseAction queued, out IAction? action)
	{
		action = null;
		if (!IsEnabled || !InCombat || DataCenter.IsPvP || _manualRaise || OpenerOwnsInput
			|| MergedStatus.HasFlag(AutoStatus.NoCasting) || MergedStatus.HasFlag(AutoStatus.Raise)
			|| !IsDotCompetingFiller(queued.AdjustedID))
		{
			return false;
		}

		var force = IBaseAction.ForceEnable;
		IBaseAction.ForceEnable = false;
		try
		{
			return TryUseCurrentManagedDot(out action);
		}
		finally
		{
			IBaseAction.ForceEnable = force;
		}
	}

	/// <summary>#18: hold a manual filler press while RSR's next GCD is the managed DoT.</summary>
	internal bool ShouldDeferFillerForDot(uint actionType, uint id)
	{
		if (actionType != 1 || !IsEnabled || !DataCenter.State || !InCombat || DataCenter.IsPvP
			|| _manualRaise || OpenerOwnsInput || !IsDotCompetingFiller(id))
		{
			return false;
		}

		var next = RotationSolver.Updaters.ActionUpdater.NextGCDAction;
		if (next == null || !((ActionID)next.AdjustedID is ActionID.DiaPvE or ActionID.AeroIiPvE or ActionID.AeroPvE))
		{
			return false;
		}

		// Do not hold manual filler for a stale automatic candidate on an excluded target.
		if (next.Target.Target is { } target && IsExcludedFromAutoDia(target))
		{
			return false;
		}

		return !MergedStatus.HasFlag(AutoStatus.NoCasting) && !MergedStatus.HasFlag(AutoStatus.Raise);
	}

	private bool TryUseCurrentManagedDot(out IAction? act) => TryUseLeveledManagedDot(out act);

	private bool TryUsePreferredLilyHeal(out IAction? act)
	{
		act = null;
		// Every automatic caller obeys the same overcap policy.
		if (!UseRestrictedLilyAutomation || !WHMHimechanPolicy.IsLilyRetryWindow(Lily,
			LilyTime + DataCenter.DefaultGCDRemain, LilyOvercapWindow, LilyRetryEnd))
		{
			return false;
		}

		var previousAutoHeal = IBaseAction.AutoHealCheck;
		var previousTarget = IBaseAction.TargetOverride;
		IBaseAction.AutoHealCheck = false;
		IBaseAction.TargetOverride = null;
		try
		{
			return TrySelectPreferredLilyHeal(out act);
		}
		finally
		{
			IBaseAction.AutoHealCheck = previousAutoHeal;
			IBaseAction.TargetOverride = previousTarget;
		}
	}

	private bool TrySelectPreferredLilyHeal(out IAction? act)
	{
		act = null;
		if (!InCombat || Lily == 0 || BloodLily >= 3 || Player is not { IsDead: false })
		{
			return false;
		}

		if (PartyMembersAverHP <= LilyAreaHealThreshold
			&& AfflatusRapturePvE.CanUse(out act, skipAoeCheck: true, targetOverride: TargetType.Self))
		{
			return true;
		}

		foreach (var target in GetLilyTargetsByMissingHp())
		{
			if (TryUseSolaceOn(target, out act))
			{
				return true;
			}
		}

		// At full party HP Solace can reject every candidate. Rapture with the
		// AoE threshold bypass is the deterministic overcap fallback.
		return AfflatusRapturePvE.CanUse(out act, skipAoeCheck: true, targetOverride: TargetType.Self);
	}

	private static List<IBattleChara> GetLilyTargetsByMissingHp()
	{
		List<IBattleChara> targets = [];
		if (Player is { IsDead: false } player)
		{
			targets.Add(player);
		}

		foreach (var member in PartyMembers)
		{
			if (!member.IsDead && member.GameObjectId != Player?.GameObjectId)
			{
				targets.Add(member);
			}
		}
		targets.Sort((a, b) => MissingHp(b).CompareTo(MissingHp(a)));
		return targets;
	}

	private static ulong MissingHp(IBattleChara target) =>
		target.MaxHp > target.CurrentHp ? target.MaxHp - target.CurrentHp : 0;

	private bool TryUseSolaceOn(IBattleChara target, out IAction? act)
	{
		act = null;
		var targetId = target.GameObjectId;
		var originalFilter = AfflatusSolacePvE.Setting.CanTarget;
		var originalTargetType = AfflatusSolacePvE.Setting.TargetType;
		// LowHP avoids the generic Heal filter that discards full-HP targets.
		AfflatusSolacePvE.Setting.TargetType = TargetType.LowHP;
		AfflatusSolacePvE.Setting.CanTarget = candidate =>
			originalFilter(candidate) && candidate.GameObjectId == targetId;

		try
		{
			return AfflatusSolacePvE.CanUse(out act, targetOverride: TargetType.LowHP);
		}
		finally
		{
			AfflatusSolacePvE.Setting.CanTarget = originalFilter;
			AfflatusSolacePvE.Setting.TargetType = originalTargetType;
		}
	}

	private bool TryUseSpareTetragrammaton(out IAction? act)
	{
		act = null;
		if (!InCombat || Player is not { IsDead: false } || !TetragrammatonPvE.IsEnabled
			|| TetragrammatonPvE.Cooldown.MaxCharges < 2 || TetragrammatonPvE.Cooldown.CurrentCharges < 2)
		{
			return false;
		}

		var ratio = TetragrammatonPvE.Config.AutoHealRatio;
		var previousAutoHeal = IBaseAction.AutoHealCheck;
		IBaseAction.AutoHealCheck = false;
		try
		{
			var targets = GetLilyTargetsByMissingHp();
			targets.Sort((a, b) => a.GetHealthRatio().CompareTo(b.GetHealthRatio()));
			foreach (var target in targets)
			{
				if (target.GetHealthRatio() < ratio && TryUseTetragrammatonOn(target, out act))
				{
					return true;
				}
			}
			return false;
		}
		finally
		{
			IBaseAction.AutoHealCheck = previousAutoHeal;
		}
	}

	private bool TryUseLastTetragrammatonForEmergency(out IAction? act)
	{
		act = null;
		if (TetragrammatonPvE.Cooldown.MaxCharges < 2
			|| TetragrammatonPvE.Cooldown.CurrentCharges != 1)
		{
			return false;
		}

		List<IBattleChara> candidates = [];
		IBattleChara? player = Player;
		if (player != null && !player.IsDead && player.GetHealthRatio() < TetragrammatonEmergencyHeal)
		{
			candidates.Add(player);
		}

		foreach (var member in PartyMembers)
		{
			if (member.IsDead
				|| member.GameObjectId == player?.GameObjectId
				|| member.GetHealthRatio() >= TetragrammatonEmergencyHeal
				|| (!member.IsJobCategory(JobRole.Healer) && !member.IsJobCategory(JobRole.AllDPS)))
			{
				continue;
			}

			candidates.Add(member);
		}

		candidates.Sort((left, right) =>
		{
			var priorityComparison = GetTetragrammatonEmergencyPriority(left, player)
				.CompareTo(GetTetragrammatonEmergencyPriority(right, player));
			return priorityComparison != 0
				? priorityComparison
				: left.GetHealthRatio().CompareTo(right.GetHealthRatio());
		});

		foreach (var candidate in candidates)
		{
			if (TryUseTetragrammatonOn(candidate, out act))
			{
				return true;
			}
		}

		return false;
	}

	private static int GetTetragrammatonEmergencyPriority(IBattleChara member, IBattleChara? player)
	{
		if (member.GameObjectId == player?.GameObjectId)
		{
			return 0;
		}

		return member.IsJobCategory(JobRole.Healer) ? 1 : 2;
	}

	private bool TryUseTetragrammatonOn(IBattleChara target, out IAction? act)
	{
		act = null;
		var targetId = target.GameObjectId;
		var originalFilter = TetragrammatonPvE.Setting.CanTarget;
		TetragrammatonPvE.Setting.CanTarget = candidate =>
			originalFilter(candidate) && candidate.GameObjectId == targetId;

		try
		{
			return TetragrammatonPvE.CanUse(out act, usedUp: true, targetOverride: TargetType.LowHP);
		}
		finally
		{
			TetragrammatonPvE.Setting.CanTarget = originalFilter;
		}
	}

	/// <summary>The tank most enemies are attacking (lowest HP on a tie). Test mode: the player counts as a tank.</summary>
	private static IBattleChara? FindCurrentlyAttackedTank()
	{
		IBattleChara? result = null;
		var mostAttackers = 0;
		var lowestHealthRatio = float.MaxValue;

		foreach (var member in HimechanTanks.PartyTanks())
		{
			if (member.IsDead)
			{
				continue;
			}

			var attackerCount = 0;
			foreach (var hostile in AllHostileTargets)
			{
				if (hostile.TargetObject?.GameObjectId == member.GameObjectId)
				{
					attackerCount++;
				}
			}

			var healthRatio = member.GetHealthRatio();
			if (attackerCount > mostAttackers
				|| (attackerCount == mostAttackers && attackerCount > 0 && healthRatio < lowestHealthRatio))
			{
				result = member;
				mostAttackers = attackerCount;
				lowestHealthRatio = healthRatio;
			}
		}

		return result;
	}

	private bool TryUseBenisonAtChargeCap(out IAction? act)
	{
		act = null;

		if (!BenisonRoutineReady())
		{
			return false;
		}

		var attackedTank = FindCurrentlyAttackedTank();
		if (attackedTank == null)
		{
			return false;
		}

		var attackedTankId = attackedTank.GameObjectId;
		var originalFilter = DivineBenisonPvE.Setting.CanTarget;
		DivineBenisonPvE.Setting.CanTarget = target =>
			originalFilter(target) && target.GameObjectId == attackedTankId;

		try
		{
			// The player cannot be picked through LowHP filtering when it is the test-mode tank.
			var targetType = HimechanTanks.IsPlayer(attackedTank) ? TargetType.Self : TargetType.LowHP;
			return DivineBenisonPvE.CanUse(out act, usedUp: true, targetOverride: targetType);
		}
		finally
		{
			DivineBenisonPvE.Setting.CanTarget = originalFilter;
		}
	}

	private bool TryUseManagedDot(IBaseAction dot, out IAction? act)
	{
		act = null;
		if (!InCombat)
		{
			return false; // Automatic maintenance never initiates combat; the opener uses OpenerAction.
		}

		var originalFilter = dot.Setting.CanTarget;
		dot.Setting.CanTarget = target => originalFilter(target) && IsManagedDotTarget(target);

		try
		{
			using var offTarget = new HimechanOffTargetScope();
			return dot.CanUse(out act);
		}
		finally
		{
			dot.Setting.CanTarget = originalFilter;
		}
	}

	private void UpdateManuallyTaggedAdds()
	{
		if (!InCombat)
		{
			_manuallyTaggedAddIds.Clear();
			_wasInCombatForDotTracking = false;
			return;
		}

		if (!_wasInCombatForDotTracking)
		{
			_manuallyTaggedAddIds.Clear();
			_wasInCombatForDotTracking = true;
		}

		if (!RefreshManuallyTaggedAdds)
		{
			_manuallyTaggedAddIds.Clear();
			return;
		}

		foreach (var hostile in AllHostileTargets)
		{
			if (hostile.IsDead || hostile.IsBossFromIcon())
			{
				continue;
			}

			// A direct DoT on an excluded target must not enroll automatic refreshes.
			if (IsExcludedFromAutoDia(hostile))
			{
				continue;
			}

			if (hostile.HasStatus(true, StatusID.Aero, StatusID.AeroIi, StatusID.Dia))
			{
				_ = _manuallyTaggedAddIds.Add(hostile.GameObjectId);
			}
		}
	}

	private string? _excludedBaseIdsText;
	private HashSet<uint> _excludedBaseIds = [];

	/// <summary>Spec #9: BaseId list from the setting (parsed again only when the text changes).</summary>
	private bool IsExcludedFromAutoDia(IBattleChara target)
	{
		var text = AutoDiaExcludedBaseIds;
		if (!string.Equals(text, _excludedBaseIdsText, StringComparison.Ordinal))
		{
			_excludedBaseIds = WHMHimechanPolicy.ParseBaseIds(text);
			_excludedBaseIdsText = text;
		}

		return _excludedBaseIds.Count > 0 && _excludedBaseIds.Contains(target.BaseId);
	}

	private bool IsManagedDotTarget(IBattleChara target)
	{
		// Applies before BOTH allow paths (boss and manually tagged adds).
		if (IsExcludedFromAutoDia(target))
		{
			return false;
		}

		if (target.IsBossFromIcon())
		{
			return true;
		}

		if (!RefreshManuallyTaggedAdds || !_manuallyTaggedAddIds.Contains(target.GameObjectId))
		{
			return false;
		}

		var estimatedTimeToKill = target.GetTTK();
		return !float.IsNaN(estimatedTimeToKill)
			&& estimatedTimeToKill >= ManuallyTaggedAddMinimumTtk;
	}

	public override bool CanHealSingleSpell => base.CanHealSingleSpell && GCDHeal;
	public override bool CanHealAreaSpell => base.CanHealAreaSpell && GCDHeal;

	/// <summary>Diagnostic line for the in-memory trace and the debug combat log.</summary>
	internal static void RecordHimechanDiagnostic(string message) => HimechanLog.Write("WHM", message);

	internal string DescribeHimechanAction(IAction action) =>
		$"사용 접수 {action.Name}({action.AdjustedID}) | 백합={Lily}, 충전까지={LilyTime + DataCenter.DefaultGCDRemain:F2}s, 피의 백합={BloodLily} | 대상=0x{(action as IBaseAction)?.Target.Target?.GameObjectId:X} | 오프너={_opening}";

	internal void PrintHimechanDiagnostics()
	{
		ECommons.DalamudServices.Svc.Chat.Print($"[히메짱] 오프너={_opening}/{_openerStatus}, 확인 대기={_openerPending} | GCD={GlareIiiPvE.Cooldown.RecastTimeOneChargeRaw:F2}s | 백합={Lily}, 충전까지={LilyTime + DataCenter.DefaultGCDRemain:F2}s");
		ECommons.DalamudServices.Svc.Chat.Print($"[히메짱] 부활={ManualRaiseStatus} | 신의 이름={TetragrammatonPvE.Cooldown.CurrentCharges}/{TetragrammatonPvE.Cooldown.MaxCharges}, 사용 HP<{TetragrammatonPvE.Config.AutoHealRatio:P0}");
	}
	#endregion
}
