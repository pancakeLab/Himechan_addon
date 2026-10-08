using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.DalamudServices;
using ECommons.GameHelpers;

namespace RotationSolver.Himechan;

/// <summary>
/// Single place that decides "who counts as a tank" for every Himechan feature (opener tank, Divine Benison,
/// high-end Aquaveil, raise priority). Spec 3.11: in test mode, while no other player is in the party,
/// the player is treated as the tank so tank-targeted features can be tested solo (e.g. unsynced old savage).
/// </summary>
internal static class HimechanTanks
{
	private static bool _warnedNotSolo;

	/// <summary>Test mode is on and the player is alone, so it is actually in effect.</summary>
	public static bool TestModeActive => HimechanSettings.Current.TestMode && IsSolo();

	/// <summary>
	/// No other player character in the party list (Trust/Duty Support NPCs do not count). Uses the game's party list,
	/// not DataCenter.PartyMembers, so a party member who is far away or not loaded still counts as "not solo".
	/// </summary>
	public static bool IsSolo()
	{
		var me = Player.Object;
		if (me == null)
		{
			return false;
		}

		foreach (var member in Svc.Party)
		{
			if (member == null || member.EntityId == me.EntityId)
			{
				continue;
			}

			// Not loaded (null) -> unknown -> treat as another player (conservative).
			if (member.GameObject is not IBattleChara chara || chara is IPlayerCharacter)
			{
				return false;
			}
		}

		return true;
	}

	public static bool IsPlayer(IBattleChara? chara) =>
		chara != null && chara.GameObjectId == Player.Object?.GameObjectId;

	public static bool IsTank(IBattleChara? chara) =>
		chara != null && (chara.IsJobCategory(JobRole.Tank) || (IsPlayer(chara) && TestModeActive));

	/// <summary>Party tanks (living or not); in active test mode the player is included.</summary>
	public static List<IBattleChara> PartyTanks()
	{
		List<IBattleChara> tanks = [];
		var playerListed = false;
		foreach (var member in DataCenter.PartyMembers)
		{
			if (IsPlayer(member))
			{
				playerListed = true;
			}

			if (IsTank(member))
			{
				tanks.Add(member);
			}
		}

		if (!playerListed && TestModeActive && Player.Object is { } me)
		{
			tanks.Add(me);
		}

		return tanks;
	}

	/// <summary>Called every check: warn once when test mode is on but has no effect because of other players.</summary>
	public static void Update()
	{
		if (!HimechanSettings.Current.TestMode || !Player.Available)
		{
			_warnedNotSolo = false;
			return;
		}

		var solo = IsSolo();
		if (!solo && !_warnedNotSolo)
		{
			_warnedNotSolo = true;
			HimechanProfile.Notify("파티에 다른 플레이어가 있어 테스트 모드가 멈췄습니다. 혼자가 되면 다시 동작합니다.");
		}
		else if (solo)
		{
			_warnedNotSolo = false;
		}
	}
}
