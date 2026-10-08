using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Hooks;
using ECommons.Hooks.ActionEffectTypes;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using RotationSolver.Basic.Himechan;
using RotationSolver.RebornRotations.Healer;
using RotationSolver.Updaters;

namespace RotationSolver.Himechan;

/// <summary>
/// The only Himechan entry points called from upstream files (each call is one line marked HIMECHAN-HOOK).
/// Every entry catches its own exceptions and falls back to upstream behaviour, so Himechan can never stop RSR.
/// Things that need no upstream hook (effect packets, cast cancel, RSR off / rotation change cleanup)
/// are handled here from ECommons events and the framework update.
/// </summary>
internal static class HimechanHooks
{
	private static ICustomRotation? _lastRotation;
	private static bool _lastState;

	/// <summary>True while RSR itself is inside action.Use() (the native hook sees RSR's own call, not a player input).</summary>
	[ThreadStatic]
	private static bool _rsrUsing;

	private static WHM_Himechan? Himechan => DataCenter.CurrentRotation as WHM_Himechan;

	public static void Init()
	{
		ActionEffect.ActionEffectEvent += OnActionEffect;
		HimechanBasicHooks.OverrideQueuedGCD = OverrideQueuedGcd;
	}

	public static void Dispose()
	{
		ActionEffect.ActionEffectEvent -= OnActionEffect;
		HimechanBasicHooks.OverrideQueuedGCD = null;
		HimechanBasicHooks.AllowOffTargetInManual = false;
	}

	/// <summary>Framework update (every frame).</summary>
	public static void Update()
	{
		try
		{
			HimechanTanks.Update();
			HimechanLog.Update();

			var rotation = DataCenter.CurrentRotation;
			if (!ReferenceEquals(rotation, _lastRotation))
			{
				if (_lastRotation is WHM_Himechan previous)
				{
					previous.StopManualRaise("로테이션 변경");
					previous.StopOpener("로테이션 변경");
				}
				_lastRotation = rotation;
			}

			var state = DataCenter.State;
			if (_lastState && !state && rotation is WHM_Himechan stopped)
			{
				// rev24 hooked CancelState / the OFF commands; watching the state covers every OFF path.
				stopped.StopManualRaise("RSR 종료");
				stopped.StopOpener("RSR 종료");
			}
			_lastState = state;

			UpdateCancelCast(rotation as WHM_Himechan);
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Update failed: {ex}");
		}
	}

	/// <summary>rev24 CancelCastUpdater hooks: cancel our own cast during opener preparation, or a Raise that is no longer needed.</summary>
	private static unsafe void UpdateCancelCast(WHM_Himechan? whm)
	{
		var player = Player.Object;
		if (whm == null || player == null || !player.IsCasting || !DataCenter.State)
		{
			return;
		}

		var castTarget = Svc.Objects.SearchById(player.CastTargetObjectId) as IBattleChara;
		var opener = whm.ShouldCancelOpenerPreparation;
		if (!opener && !whm.ShouldCancelManualRaise(castTarget))
		{
			return;
		}

		var ui = UIState.Instance();
		if (ui != null)
		{
			ui->Hotbar.CancelCast();
			HimechanLog.Write("WHM", opener ? "CANCEL CAST: opener preparation" : "CANCEL CAST: raise no longer needed");
		}
	}

	/// <summary>HIMECHAN-HOOK ActionUpdater.UpdateNextAction: manual raise -> deferred replay -> Solace request -> opener.</summary>
	public static bool TryPriorityAction(ICustomRotation? rotation, out IAction? next, out IBaseAction? gcd)
	{
		next = null;
		gcd = null;
		if (rotation is not WHM_Himechan whm)
		{
			return false;
		}

		try
		{
			if (whm.TryManualRaise(out next, out gcd))
			{
				whm.StopOpener("부활 요청 우선");
				return true;
			}

			if (whm.TryDeferredReplay())
			{
				next = null;
				gcd = null;
				return true;
			}

			if (whm.TrySolaceRequest(out next, out gcd))
			{
				return true;
			}

			if (whm.TryOpener(out next, out gcd))
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Priority action failed: {ex}");
			whm.StopOpener("실행 검사 오류로 종료");
		}

		next = null;
		gcd = null;
		return false;
	}

	/// <summary>HIMECHAN-HOOK ActionQueueManager.UseActionDetour: true = reject this input (opener protection, #18, #19).</summary>
	public static bool ShouldRejectInput(uint actionType, uint actionId, ulong targetObjectId, uint useType)
	{
		try
		{
			var whm = Himechan;
			if (whm == null || _rsrUsing)
			{
				return false; // RSR's own Use() (already validated) is never treated as a player input.
			}

			if (whm.ShouldBlockOpenerInput(actionType, actionId, targetObjectId, useType))
			{
				return true;
			}

			if (actionType == 1 && whm.ShouldDeferFillerForDot(actionType, Service.GetAdjustedActionId(actionId)))
			{
				return true;
			}

			return whm.ShouldDeferManualAbility(actionType, actionId, targetObjectId, useType);
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Input check failed, input passed through: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// HIMECHAN-HOOK RSCommands.DoAction, replaces nextAction.Use(): validate (raise / opener / weave), use inside the
	/// opener scope, then report the accepted use to every Himechan feature.
	/// </summary>
	public static bool Use(IAction action)
	{
		var whm = Himechan;
		if (whm == null)
		{
			return action.Use();
		}

		string? description = null;
		try
		{
			if (!whm.ValidateManualRaiseAction(action) || !whm.ValidateOpenerAction(action) || !whm.ValidateMaintenanceAction(action))
			{
				return false;
			}

			description = whm.DescribeHimechanAction(action);
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Use validation failed: {ex}");
			whm.StopOpener("실행 검사 오류로 종료");
		}

		bool used;
		_rsrUsing = true;
		try
		{
			used = whm.UseWithOpenerScope(action);
		}
		finally
		{
			_rsrUsing = false;
		}

		if (!used)
		{
			return false;
		}

		try
		{
			if (description != null)
			{
				HimechanLog.Write("USE", description);
			}

			whm.ManualRaiseActionSubmitted(action.AdjustedID);
			whm.OpenerSubmitted(action);
			whm.SolaceSubmitted(action.AdjustedID);
			whm.AutoAbilitySubmitted(action);
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Submit callbacks failed: {ex}");
		}
		return true;
	}

	/// <summary>HIMECHAN-HOOK RSCommands.CanDoAnAction: the opener potion may clip the GCD tail (rev24 contract).</summary>
	public static bool AllowOpenerItemInGcdTail()
	{
		try
		{
			return Himechan?.IsOpenerPotionDue(ActionUpdater.NextAction) == true;
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Opener potion check failed: {ex.Message}");
			return false;
		}
	}

	/// <summary>HIMECHAN-HOOK RSCommands.UpdateRotationState: keep RSR on through the countdown-to-pull handoff of the opener.</summary>
	public static bool KeepStateAfterCountdown()
	{
		try
		{
			var keep = Himechan?.ContinueOpenerAfterCountdown == true;
			return keep;
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Countdown handoff check failed: {ex.Message}");
			return false;
		}
	}

	private static bool OverrideQueuedGcd(CustomRotation rotation, IBaseAction queued, out IAction? action)
	{
		action = null;
		try
		{
			return rotation is WHM_Himechan whm && whm.OverrideQueuedGCD(queued, out action);
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Queued GCD override failed: {ex.Message}");
			action = null;
			return false;
		}
	}

	/// <summary>Own subscription to the effect packets (upstream Watcher is not modified). Same filters as Watcher.ActionFromSelf.</summary>
	private static void OnActionEffect(ActionEffectSet set)
	{
		try
		{
			var whm = Himechan;
			var player = Player.Object;
			if (whm == null || player == null || set.Source == null || set.Source.GameObjectId != player.GameObjectId
				|| set.Action == null || set.Action.Value.ActionCategory.RowId == (uint)ActionCate.Autoattack
				|| set.TargetEffects.Length == 0)
			{
				return;
			}

			var id = set.Action.Value.RowId;
			whm.ManualRaiseEffect(id, set.Target?.GameObjectId ?? 0);

			// The native effect-header action kind is one byte; ECommons' field reads four (flags/target count included).
			var isAction = unchecked((byte)set.Header.ActionType) == 1;
			HimechanLog.Write("FX", $"id={id} kind={unchecked((byte)set.Header.ActionType)} 대상=0x{set.Target?.GameObjectId ?? 0:X}");
			if (isAction)
			{
				whm.OpenerEffect(id);
				whm.MaintenanceEffect(id);
				whm.SolaceEffect(id);
			}
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Effect handling failed: {ex.Message}");
		}
	}
}
