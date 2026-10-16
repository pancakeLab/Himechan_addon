using FFXIVClientStructs.FFXIV.Client.Game;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace RotationSolver.RebornRotations.Healer;

// #34 HUD data (2026-10-14): what the overlay shows for the GCD side. The oGCD side comes from the weave queue (HudQueueEntries).
// In RSR's manual mode the player casts the attack GCDs, so the "next GCD" must come from the player's own cast and the
// game's input queue as well as from RSR (game test 2026-10-14: the centre stayed empty except for the Dia refresh).
public sealed partial class WHM_Himechan
{
	// The first automatic ability candidate that the weave gate held back this frame (preview evaluation), for the HUD only.
	private IAction? _hudAutoCandidate;

	internal void HudResetFrame() => _hudAutoCandidate = null;

	internal void HudRememberCandidate(IAction? candidate)
	{
		if (candidate != null && _hudAutoCandidate == null)
		{
			_hudAutoCandidate = candidate;
		}
	}

	/// <summary>Centre of the ring: the GCD being cast right now (player or RSR), otherwise RSR's planned next GCD.</summary>
	internal unsafe HudEntry? HudCenterGcd()
	{
		var player = Player;
		if (player != null && player.IsCasting && player.CastActionId != 0 && IsGcdActionId(player.CastActionId))
		{
			return new HudEntry(player.CastActionId, false, 0, ActionLabel(player.CastActionId), "시전", false);
		}

		var next = RotationSolver.Updaters.ActionUpdater.NextGCDAction;
		return next != null ? new HudEntry(next.AdjustedID, false, 0, next.Name, "다음", false, NeedsCastFor(next.AdjustedID)) : null;
	}

	/// <summary>Reserved GCDs after the centre: the game's own queued input, RSR's next GCD, the Solace request, the RSR command GCD.</summary>
	internal unsafe List<HudEntry> HudReservedGcds(HudEntry? center)
	{
		List<HudEntry> list = [];
		var centerId = center?.ActionId ?? 0;

		var manager = ActionManager.Instance();
		if (manager != null && manager->ActionQueued && manager->QueuedActionType == ActionType.Action)
		{
			var queued = Service.GetAdjustedActionId(manager->QueuedActionId);
			if (IsGcdActionId(queued) && (center is not { Badge: "다음" } || queued != centerId))
			{
				list.Add(new HudEntry(queued, false, 0, ActionLabel(queued), "선입력", false, NeedsCastFor(queued)));
			}
		}

		var next = RotationSolver.Updaters.ActionUpdater.NextGCDAction;
		if (next != null && next.AdjustedID != centerId && !Contains(list, next.AdjustedID))
		{
			list.Add(new HudEntry(next.AdjustedID, false, 0, next.Name, "다음", false, NeedsCastFor(next.AdjustedID)));
		}

		if (_solaceRequest)
		{
			var solace = _solaceTarget != 0 ? AfflatusSolacePvE : AfflatusRapturePvE;
			if (solace.AdjustedID != centerId && !Contains(list, solace.AdjustedID))
			{
				list.Add(new HudEntry(solace.AdjustedID, false, 0, solace.Name, "요청", false));
			}
		}

		if (DataCenter.CommandNextAction is IBaseAction command && command.Info.IsRealGCD
			&& command.AdjustedID != centerId && !Contains(list, command.AdjustedID))
		{
			list.Add(new HudEntry(command.AdjustedID, false, 0, command.Name, "명령", false, NeedsCastFor(command.AdjustedID)));
		}

		return list;
	}

	/// <summary>How many abilities can still go into the current gap: lights up that many oGCD slots.</summary>
	internal int HudGapPlaces
	{
		get
		{
			if (!WeaveQueueActive || !InCombat)
			{
				return 1;
			}

			if (WeaveSlotFree)
			{
				var remain = DataCenter.DefaultGCDRemain;
				return DoubleWeaveInWideGap && remain >= Math.Clamp(WideGapMinimum, 1.0f, 2.5f) ? 2 : 1;
			}

			return WeaveSecondSlotFree ? 1 : 0;
		}
	}

	/// <summary>Extra oGCD lines beyond the weave queue: the game's queued ability input and the held-back automatic candidate.</summary>
	internal unsafe void HudAppendAbilityExtras(List<HudEntry> list)
	{
		var manager = ActionManager.Instance();
		if (manager != null && manager->ActionQueued && manager->QueuedActionType == ActionType.Action)
		{
			var queued = Service.GetAdjustedActionId(manager->QueuedActionId);
			if (!IsGcdActionId(queued) && !Contains(list, queued))
			{
				list.Add(new HudEntry(queued, false, 0, ActionLabel(queued), "선입력", false));
			}
		}

		if (_hudAutoCandidate is IBaseAction candidate && candidate.Info.IsAbility && !Contains(list, candidate.AdjustedID))
		{
			list.Add(new HudEntry(candidate.AdjustedID, false, 0, candidate.Name, "자동 대기", true));
		}
	}

	private static bool Contains(List<HudEntry> list, uint actionId)
	{
		foreach (var e in list)
		{
			if (!e.IsItem && e.ActionId == actionId)
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsGcdActionId(uint adjustedId)
	{
		var row = Service.GetSheet<LuminaAction>().GetRowOrDefault(adjustedId);
		return row is { } action && action.ActionCategory.RowId is (uint)ActionCate.Spell or (uint)ActionCate.Weaponskill;
	}

	/// <summary>A GCD with a cast time while Swiftcast is not up: moving when it fires would cancel it.</summary>
	internal const int NextGcdNone = 0;
	internal const int NextGcdInstant = 1;
	internal const int NextGcdCast = 2;

	/// <summary>
	/// #37 cast assist: what the GCD that fires next (game queue, RSR's next GCD, request, command) needs, using the same
	/// determination as the HUD "정지" warning. None = nothing planned (or already casting); Instant / Cast with its id.
	/// </summary>
	internal int NextGcdPlan(out uint id)
	{
		id = 0;
		var player = Player;
		if (player != null && player.IsCasting)
		{
			return NextGcdNone;
		}

		var center = HudCenterGcd();
		var reserved = HudReservedGcds(center);
		var next = reserved.Count > 0 ? reserved[0] : center is { Badge: "다음" } ? center : null;
		if (next is not { } entry)
		{
			return NextGcdNone;
		}

		id = entry.ActionId;
		return entry.NeedsCast ? NextGcdCast : NextGcdInstant;
	}

	private static bool NeedsCastFor(uint adjustedId)
	{
		var row = Service.GetSheet<LuminaAction>().GetRowOrDefault(adjustedId);
		return row is { } action && action.Cast100ms > 0 && !HasSwift;
	}

	/// <summary>Text under the ring: gap usage for this GCD.</summary>
	internal string HudGapText => !WeaveQueueActive ? string.Empty
		: WeaveSlotFree ? "틈 ◇◇"
		: WeaveSecondSlotFree ? "틈 ◆◇"
		: _weaveGapWide ? "틈 ◆◆" : "틈 ◆";
}
