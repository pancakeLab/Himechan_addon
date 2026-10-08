using RotationSolver.Basic.Configuration;

namespace RotationSolver.Basic.Himechan;

/// <summary>
/// Hook points inside RotationSolver.Basic that the Himechan code in the main assembly fills in.
/// Basic cannot reference the main assembly, so each hook is a delegate that stays null unless Himechan is loaded.
/// </summary>
internal static class HimechanBasicHooks
{
	/// <summary>
	/// Called at the start of <see cref="Configs.Save"/>. Returns true when the save was handled
	/// (the Himechan profile was written to its own file), so the original RotationSolver.json is left untouched.
	/// </summary>
	internal static Func<Configs, bool>? ConfigSave;
}
