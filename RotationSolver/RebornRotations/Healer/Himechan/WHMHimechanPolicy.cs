using System.Globalization;

namespace RotationSolver.RebornRotations.Healer;

/// <summary>
/// Pure decision rules of the Himechan WHM rotation (no game state). Ported from rev24
/// (WHMHimechanPolicy / WHMMaintenancePolicy / WHMManualRaisePolicy / WHMOpenerPolicy) and rev25-32.
/// </summary>
internal static class WHMHimechanPolicy
{
	#region Movement / lily (rev24 WHMHimechanPolicy)
	internal static bool CanUseMovementSwiftcast(float moving, float threshold, bool hasGcd) =>
		!hasGcd && float.IsFinite(moving) && float.IsFinite(threshold)
		&& threshold >= 0.1f && threshold <= 5f && moving >= threshold;

	internal static bool IsLilyRetryWindow(int lilies, float seconds, float start, float end) =>
		lilies == 2 && float.IsFinite(seconds) && float.IsFinite(start) && float.IsFinite(end)
		&& end >= 0 && start >= end && start <= 20 && seconds >= end && seconds <= start;
	#endregion

	#region Weaving (rev24 WHMMaintenancePolicy, spec #11)
	/// <summary>0.65s + max(0.20s, observed animation-lock delay); 0.85s when the estimate is invalid. Conservative, not a game constant.</summary>
	internal static float WeaveBudget(float observedDelay) =>
		float.IsFinite(observedDelay) && observedDelay >= 0f
			? 0.65f + Math.Max(0.20f, observedDelay)
			: 0.85f;

	/// <summary>A GCD is planned: only weave when the remaining GCD is finite and strictly above the budget (0 is not a window).</summary>
	internal static bool CanWeave(float gcdRemain, float animationLock, bool casting, bool queued, float observedDelay) =>
		!casting && !queued && float.IsFinite(gcdRemain)
		&& float.IsFinite(animationLock) && animationLock <= 0f
		&& gcdRemain > WeaveBudget(observedDelay);

	/// <summary>No GCD is planned (e.g. downtime): nothing can be clipped, so only casting / lock / game queue block an ability.</summary>
	internal static bool CanUseWithoutGcd(float animationLock, bool casting, bool queued) =>
		!casting && !queued && float.IsFinite(animationLock) && animationLock <= 0f;

	internal static bool BenisonDue(int current, int maximum, float timeToCap, float lead, float gcdTotal)
	{
		if (maximum < 2 || current < 1 || current > maximum)
		{
			return false;
		}

		if (current == maximum)
		{
			return true;
		}

		return current + 1 == maximum && float.IsFinite(timeToCap) && timeToCap >= 0f
			&& float.IsFinite(lead)
			&& (timeToCap <= lead || (float.IsFinite(gcdTotal) && gcdTotal > 0f && timeToCap <= gcdTotal + 0.15f));
	}

	/// <summary>Spec #10: random lead before the charge cap, drawn once per charge cycle from [min, max] seconds.</summary>
	internal static float BenisonLead(float min, float max, Random random)
	{
		if (!float.IsFinite(min) || min < 0f)
		{
			min = 0.5f;
		}

		if (!float.IsFinite(max) || max < min)
		{
			max = min;
		}

		return MathF.Round(min + ((float)random.NextDouble() * (max - min)), 2);
	}
	#endregion

	#region Manual raise (rev24 WHMManualRaisePolicy). Role: tank=1, healer=2, DPS=3.
	internal static float RequestTimeout(float configured) =>
		float.IsFinite(configured) ? Math.Clamp(configured, 1f, 10f) : 5f;

	internal static int RaisePriority(int role, int eligibleDeadTanks, bool livingTank) =>
		role == 1 && !livingTank && eligibleDeadTanks >= 2 ? 0 :
		role == 2 ? 1 : role == 1 ? 2 : 3;

	/// <summary>Never insert an oGCD at the tail of a running GCD; prepare at idle if the window was missed.</summary>
	internal static bool RaiseCanWeave(float remaining, float animationLock) =>
		float.IsFinite(remaining) && float.IsFinite(animationLock) && animationLock <= 0
		&& (remaining <= 0 || remaining >= 0.85f);

	internal static bool OtherRaiseWins(float ours, float theirs) =>
		theirs >= 0 && theirs + 0.3f < ours;
	#endregion

	#region Opener (rev24 WHMOpenerPolicy, spec #2)
	internal static float Lead(float value) =>
		float.IsFinite(value) ? MathF.Round(Math.Clamp(value, 0f, 5f), 2) : 0.5f;

	internal static float PreparationLead(float gcd, float lead) => lead + (2 * gcd) + 1;

	internal static bool CanArm(float countdown, float gcd, float cast, float lead) =>
		float.IsFinite(countdown) && float.IsFinite(gcd) && float.IsFinite(cast)
		&& gcd > 0 && cast >= 0 && countdown >= PreparationLead(gcd, lead) + cast;

	internal static bool OpenerWeave(float remaining, float animationLock, float budget) =>
		float.IsFinite(remaining) && animationLock <= 0 && remaining >= budget;
	#endregion

	#region Auto Dia exclusion (spec #9)
	/// <summary>Parses "0x4AE1, 0x4AE2, 19169" (hex or decimal, separated by comma / space / semicolon).</summary>
	internal static HashSet<uint> ParseBaseIds(string? text)
	{
		HashSet<uint> result = [];
		if (string.IsNullOrWhiteSpace(text))
		{
			return result;
		}

		foreach (var raw in text.Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
		{
			var token = raw.Trim();
			uint value;
			var ok = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
				? uint.TryParse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
				: uint.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
			if (ok && value != 0)
			{
				_ = result.Add(value);
			}
		}

		return result;
	}
	#endregion

	#region Manual oGCD defer (rev26 F3, spec #19)
	/// <summary>Emergency actions never deferred: Benediction, Rescue. Tetragrammaton (3570) is an automatic action and is not exempt (rev30).</summary>
	internal static bool IsDeferExempt(uint adjustedId) => adjustedId is 140 or 7571
		// Ground-targeted (Asylum, Liturgy of the Bell): the position cannot be replayed.
		or 3569 or 25862;

	internal const double DeferExpireAfterWindow = 5.0;
	#endregion

	#region Solace request (rev25-29 F2, spec #15)
	internal const uint SolaceId = 16531;
	internal const uint RaptureId = 16534;
	internal const double SolaceRequestLifetime = 4.0;
	internal const double SolaceInFlight = 4.0;
	internal const double SolaceQueueGrace = 0.8;
	internal const double SolaceRecent = 2.5;
	internal const float SolaceQueueWindow = 0.5f;

	/// <summary>Lower HP wins; on a tie the lower role rank wins (Healer 0 &lt; DPS 1 &lt; Tank 2).</summary>
	internal static bool SolaceBetter(float hp, int role, float bestHp, int bestRole) =>
		hp < bestHp || (hp <= bestHp && role < bestRole);
	#endregion

	#region High-end Aquaveil (rev30 F4, spec #12)
	internal const uint AquaveilStatus = 2708;
	internal const float AquaveilCastWindow = 5f;
	#endregion
}

/// <summary>ActionConfig.IsEnabled already includes ForceEnable. Never write the stored toggle.</summary>
internal readonly struct WHMRequestedActionScope : IDisposable
{
	private readonly bool _previousForce;

	internal WHMRequestedActionScope(IBaseAction action)
	{
		_previousForce = IBaseAction.ForceEnable;
		IBaseAction.ForceEnable = true;
	}

	public void Dispose() => IBaseAction.ForceEnable = _previousForce;
}
