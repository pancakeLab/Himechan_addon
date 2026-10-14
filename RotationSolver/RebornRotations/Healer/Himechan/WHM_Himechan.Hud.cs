namespace RotationSolver.RebornRotations.Healer;

// #34 HUD data (2026-10-14): what the overlay shows for the GCD side. The oGCD side comes from the weave queue (HudQueueEntries).
public sealed partial class WHM_Himechan
{
	/// <summary>Reserved GCDs after RSR's next GCD: the RSR command GCD (/rsr DoActions) and the Solace request.</summary>
	internal List<HudEntry> HudReservedGcds()
	{
		List<HudEntry> list = [];
		var next = RotationSolver.Updaters.ActionUpdater.NextGCDAction;

		if (DataCenter.CommandNextAction is IBaseAction command && command.Info.IsRealGCD && !ReferenceEquals(command, next))
		{
			list.Add(new HudEntry(command.AdjustedID, false, 0, command.Name, "명령", false));
		}

		if (_solaceRequest)
		{
			var solace = _solaceTarget != 0 ? AfflatusSolacePvE : AfflatusRapturePvE;
			if (!ReferenceEquals(solace, next))
			{
				list.Add(new HudEntry(solace.AdjustedID, false, 0, solace.Name, "요청", false));
			}
		}

		return list;
	}

	/// <summary>Text under the ring: gap usage for this GCD.</summary>
	internal string HudGapText => !WeaveQueueActive ? string.Empty
		: WeaveSlotFree ? "틈 ◇◇"
		: WeaveSecondSlotFree ? "틈 ◆◇"
		: _weaveGapWide ? "틈 ◆◆" : "틈 ◆";
}
