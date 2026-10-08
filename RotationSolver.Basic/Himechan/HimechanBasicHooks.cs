using RotationSolver.Basic.Configuration;

namespace RotationSolver.Basic.Himechan;

/// <summary>
/// Hook points inside RotationSolver.Basic that the Himechan code in the main assembly fills in.
/// Basic cannot reference the main assembly, so each hook is a delegate (or flag) that does nothing unless Himechan sets it.
/// </summary>
internal static class HimechanBasicHooks
{
	/// <summary>
	/// Called at the start of <see cref="Configs.Save"/>. Returns true when the save was handled
	/// (the Himechan profile was written to its own file), so the original RotationSolver.json is left untouched.
	/// </summary>
	internal static Func<Configs, bool>? ConfigSave;

	internal delegate bool QueuedGcdOverride(CustomRotation rotation, IBaseAction queued, out IAction? action);

	/// <summary>#20 (rev24 OverrideQueuedGCD): lets the current rotation replace a queued (command) GCD.</summary>
	internal static QueuedGcdOverride? OverrideQueuedGCD;

	/// <summary>
	/// #20 (rev24 AllowOffTargetInManual): while true, target selection in manual mode may use targets other than
	/// the hard target. Set only for the duration of Himechan's own CanUse calls (opener, managed DoT).
	/// </summary>
	internal static bool AllowOffTargetInManual;
}
