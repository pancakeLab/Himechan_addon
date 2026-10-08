using RotationSolver.Basic.Himechan;

namespace RotationSolver.Himechan;

/// <summary>Allows off-target selection in manual mode for the CanUse calls inside this scope (#20).</summary>
internal readonly struct HimechanOffTargetScope : IDisposable
{
	private readonly bool _previous;

	public HimechanOffTargetScope()
	{
		_previous = HimechanBasicHooks.AllowOffTargetInManual;
		HimechanBasicHooks.AllowOffTargetInManual = true;
	}

	public void Dispose() => HimechanBasicHooks.AllowOffTargetInManual = _previous;
}
