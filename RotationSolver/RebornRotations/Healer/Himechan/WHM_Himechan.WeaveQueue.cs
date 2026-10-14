using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using RotationSolver.Updaters;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace RotationSolver.RebornRotations.Healer;

// Spec #32 (user request 2026-10-13, replaces #19): single-weave queue.
// Between two GCDs at most ONE ability (oGCD) goes out, whoever asked for it (RSR's automation, a hotbar/macro press,
// or a Himechan request command). A player press that cannot fit safely into the current gap is stored and replayed,
// in press order, in the first safe gap after the next GCD. Only the manual raise (Thin Air + Swiftcast) may double weave.
public sealed partial class WHM_Himechan
{
	[RotationConfig(CombatType.PvE, Name = "GCD 사이 능력(oGCD) 1개만 사용 — 수동 입력은 순서대로 다음 틈에 (위빙 큐)")]
	public bool SingleWeaveQueue { get; set; } = true;

	[Range(0.5f, 1.2f, ConfigUnitType.Seconds, 0.05f)]
	[RotationConfig(CombatType.PvE, Name = "수동 능력 입력을 다음 틈으로 미루는 기준: 남은 GCD가 이 값 이하이면 미룸 (초)", Parent = nameof(SingleWeaveQueue), ParentValue = true,
		Tooltip = "능력의 선모션(애니메이션 잠금 약 0.6초)에 서버 지연을 더한 값입니다. 자동 능력은 더 보수적인 공통 위빙 예산(0.85초 이상)을 그대로 씁니다.")]
	public float ManualWeaveTail { get; set; } = 0.75f;

	[RotationConfig(CombatType.PvE, Name = "거룩한 축복은 위빙 큐를 거치지 않고 즉시 사용 (끄면 다른 능력처럼 틈에 맞춰 사용)", Parent = nameof(SingleWeaveQueue), ParentValue = true)]
	public bool BenedictionBypassesQueue { get; set; } = true;

	[RotationConfig(CombatType.PvE, Name = "구출은 위빙 큐를 거치지 않고 즉시 사용 (끄면 다른 능력처럼 틈에 맞춰 사용)", Parent = nameof(SingleWeaveQueue), ParentValue = true)]
	public bool RescueBypassesQueue { get; set; } = true;

	// User order 2026-10-13 (the manual raise request always goes first through its own path).
	[RotationConfig(CombatType.PvE, Name = "위빙 큐 우선순위 (쉼표 구분, 앞이 먼저. 기술 이름 또는 ID. 목록에 없는 기술은 맨 뒤, 같은 순위는 누른 순서)", Parent = nameof(SingleWeaveQueue), ParentValue = true,
		Tooltip = "여러 능력이 동시에 대기 중일 때 이 순서로 사용합니다. /rsr DoActions 로 넣은 능력도 같은 순위표로 비교합니다. 이름은 게임 클라이언트 언어의 기술 이름(한섭이면 한국어)이며, ID(예: 3569)도 됩니다. 수동 부활 요청(/히메짱레이즈)은 항상 가장 먼저입니다.")]
	public string WeaveQueuePriority { get; set; } = "쾌속의 마법, 에테르 전이, 견고한 마법, 구출, 전력 질주, 거룩한 축복, 절제, 심판, 대사면, 성소, 예배종, 신의 이름, 물의 장막, 신성한 축복, 실바람, 신성한 손길";

	private string? _priorityText;
	private Dictionary<uint, int> _priorityRank = [];

	/// <summary>Rank of an action in the priority list (0 = first); int.MaxValue when not listed.</summary>
	private int WeaveRank(uint adjustedId)
	{
		var text = WeaveQueuePriority;
		if (!string.Equals(text, _priorityText, StringComparison.Ordinal))
		{
			_priorityRank = ParsePriority(text);
			_priorityText = text;
		}
		return _priorityRank.TryGetValue(adjustedId, out var rank) ? rank : int.MaxValue;
	}

	private static Dictionary<uint, int> ParsePriority(string? text)
	{
		Dictionary<uint, int> result = [];
		if (string.IsNullOrWhiteSpace(text))
		{
			return result;
		}

		var tokens = text.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		Dictionary<string, int> byName = new(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < tokens.Length; i++)
		{
			if (uint.TryParse(tokens[i], out var id))
			{
				_ = result.TryAdd(id, i);
			}
			else
			{
				_ = byName.TryAdd(tokens[i], i);
			}
		}

		if (byName.Count > 0)
		{
			try
			{
				foreach (var row in Service.GetSheet<LuminaAction>())
				{
					if (row.IsPvP || row.ActionCategory.RowId != (uint)ActionCate.Ability)
					{
						continue;
					}

					var name = row.Name.ExtractText();
					if (name.Length > 0 && byName.TryGetValue(name, out var rank))
					{
						_ = result.TryAdd(row.RowId, rank);
					}
				}
			}
			catch (Exception ex)
			{
				ECommons.Logging.PluginLog.Warning($"[Himechan] Priority name lookup failed: {ex.Message}");
			}
		}

		return result;
	}

	/// <summary>The queued entry to fire next: best rank, then press order.</summary>
	private int SelectQueueHead()
	{
		var best = 0;
		var bestRank = int.MaxValue;
		for (var i = 0; i < _weaveQueue.Count; i++)
		{
			var rank = WeaveRank(Service.GetAdjustedActionId(_weaveQueue[i].Id));
			if (i == 0 || rank < bestRank)
			{
				best = i;
				bestRank = rank;
			}
		}
		return best;
	}

	/// <summary>An ability RSR is about to use from its own command queue (/rsr DoActions) must wait while a higher-ranked entry waits here.</summary>
	internal bool QueueOutranksCommand(IAction command)
	{
		if (_weaveQueue.Count == 0)
		{
			return false;
		}

		var commandRank = WeaveRank(command.AdjustedID);
		return WeaveRank(Service.GetAdjustedActionId(_weaveQueue[SelectQueueHead()].Id)) < commandRank;
	}

	/// <summary>Tail rule for RSR command abilities and queue replays: the player's threshold, not the automatic budget.</summary>
	internal bool ManualWeaveFits(float remain) => remain > WHMHimechanPolicy.ManualTail(ManualWeaveTail);

	private sealed class QueuedAbility
	{
		public uint Id;
		public ulong Target;
		public bool GroundTarget;
		public double Expire;
		public string Label = string.Empty;
		public bool IsRequest; // Himechan command (e.g. /히메짱물장): chat feedback on failure
	}

	// 2026-10-14 user request: after an instant GCD (Dia, lilies, Glare IV, Swiftcast Glare) the gap is wide enough for two
	// abilities. A second ability in such a gap is allowed only for player-driven inputs (queue replays, /rsr DoActions);
	// automation stays at one per gap.
	[RotationConfig(CombatType.PvE, Name = "넓은 틈(즉시 시전 GCD 뒤)에서는 대기 중인 수동 입력·명령을 2개까지 사용", Parent = nameof(SingleWeaveQueue), ParentValue = true)]
	public bool DoubleWeaveInWideGap { get; set; } = true;

	[Range(1.0f, 2.5f, ConfigUnitType.Seconds, 0.1f)]
	[RotationConfig(CombatType.PvE, Name = "넓은 틈 기준: 첫 능력 사용 시점의 남은 GCD가 이 값 이상 (초)", Parent = nameof(DoubleWeaveInWideGap), ParentValue = true,
		Tooltip = "글레어 시전 뒤 틈은 약 0.9초라 해당 없고, 디아·백합·글레어 IV·신속마 글레어 뒤 틈(약 1.8초 이상)만 해당됩니다. 두 번째 능력도 수동 기준(남은 GCD > 미루는 기준)을 지킵니다.")]
	public float WideGapMinimum { get; set; } = 1.5f;

	// 2026-10-14 user decision: automation may take the second place too, but only when the automatic weave budget
	// (remaining GCD above 0.85s+) still holds, so the next GCD is never pushed.
	[RotationConfig(CombatType.PvE, Name = "자동 능력도 넓은 틈에서 2개까지 (두 번째는 자동 위빙 예산을 통과할 때만)", Parent = nameof(DoubleWeaveInWideGap), ParentValue = true)]
	public bool DoubleWeaveAutomation { get; set; } = true;

	private readonly List<QueuedAbility> _weaveQueue = [];
	private double _weaveSlotEnd; // the current GCD gap holds at least one ability until this time
	private int _weaveGapCount;   // abilities used in the current gap
	private bool _weaveGapWide;   // the gap had room for two when its first ability went out
	private bool _weaveReplaying;
	private double _weaveHoldLogAfter;

	internal bool WeaveQueueActive => SingleWeaveQueue && IsEnabled && DataCenter.State && !DataCenter.IsPvP;
	internal int WeaveQueueCount => _weaveQueue.Count;
	internal string WeaveQueueText => _weaveQueue.Count == 0 ? "비어 있음" : string.Join(" → ", _weaveQueue.ConvertAll(q => q.Label));

	/// <summary>True while no ability has gone out in the GCD gap that is currently rolling.</summary>
	internal bool WeaveSlotFree => Now >= _weaveSlotEnd;

	/// <summary>One ability already went out in this gap, but the gap is wide (instant GCD) and still has a second place for a player-driven ability.</summary>
	internal bool WeaveSecondSlotFree => !WeaveSlotFree && DoubleWeaveInWideGap && _weaveGapWide && _weaveGapCount < 2;

	/// <summary>Any ability went out (RSR, a replay, a pass-through press): the current gap is taken until the GCD comes.</summary>
	internal void MarkWeaveUsed(string source, uint id)
	{
		var remain = DataCenter.DefaultGCDRemain;
		if (remain <= 0f)
		{
			return; // GCD idle (downtime): nothing to protect.
		}

		if (WeaveSlotFree)
		{
			_weaveGapCount = 0;
			_weaveGapWide = remain >= Math.Clamp(WideGapMinimum, 1.0f, 2.5f);
		}

		_weaveGapCount++;
		_weaveSlotEnd = Now + remain;
		RecordHimechanDiagnostic($"WEAVE SLOT src={source} id={id} gap={remain:F2}s n={_weaveGapCount} wide={_weaveGapWide}");
	}

	/// <summary>After DoAction used one of RSR's abilities (any path: rotation picks, opener, raise, RSR command queue).</summary>
	internal void AutoAbilitySubmitted(IAction action)
	{
		if (action is IBaseAction a && a.Info.IsAbility)
		{
			MarkWeaveUsed("rsr", action.AdjustedID);
		}
	}

	/// <summary>
	/// UseActionDetour: a hotbar / macro ability press. Passes through when it fits the current gap (and marks the gap used);
	/// otherwise it is stored (in order, latest after the others) and rejected, to be replayed in the next safe gap.
	/// Benediction / Rescue pass at once when their bypass option is on (default); ground placement by cursor cannot be replayed and always passes.
	/// </summary>
	internal bool OnPlayerAbilityInput(uint actionType, uint actionId, ulong targetObjectId, uint useType)
	{
		try
		{
			if (_weaveReplaying || actionType != 1 || useType == 1 // 1 = the game executing its own queued input
				|| !WeaveQueueActive || !InCombat || OpenerOwnsInput || _manualRaise)
			{
				return false;
			}

			var adjusted = Service.GetAdjustedActionId(actionId);
			if (!IsAbilityId(adjusted, out var groundTarget))
			{
				return false;
			}

			var validTarget = targetObjectId != 0 && targetObjectId != 0xE0000000;
			var exempt = (adjusted == WHMHimechanPolicy.BenedictionId && BenedictionBypassesQueue)
				|| (adjusted == WHMHimechanPolicy.RescueId && RescueBypassesQueue);
			if (exempt || (groundTarget && !validTarget))
			{
				MarkWeaveUsed("player-exempt", adjusted);
				return false;
			}

			// An ability that stays on cooldown longer than the queue lives: let the game show its own error.
			if (((ActionID)adjusted).GetActionFromID(true, RotationUpdater.CurrentRotationActions) is IBaseAction known
				&& !known.Cooldown.HasOneCharge
				&& known.Cooldown.RecastTimeRemainOneCharge + DataCenter.DefaultGCDRemain > WHMHimechanPolicy.WeaveQueueLifetime)
			{
				return false;
			}

			var remain = DataCenter.DefaultGCDRemain;
			var reason = _weaveQueue.Count > 0 ? "queue"
				: !WeaveSlotFree ? (WeaveSecondSlotFree ? "slot-wide" : "slot")
				: Player?.IsCasting == true ? "casting"
				: DataCenter.AnimationLock > 0f ? "lock"
				: remain > 0f && remain <= WHMHimechanPolicy.ManualTail(ManualWeaveTail) ? "tail"
				: null;
			if (reason == null)
			{
				MarkWeaveUsed("player", adjusted);
				return false;
			}

			EnqueueAbility(actionId, adjusted, targetObjectId, groundTarget, ActionLabel(adjusted), false, reason);
			return true;
		}
		catch (Exception ex)
		{
			ECommons.Logging.PluginLog.Warning($"[Himechan] Weave queue input check failed, input passed through: {ex.Message}");
			return false;
		}
	}

	private void EnqueueAbility(uint actionId, uint adjusted, ulong target, bool groundTarget, string label, bool isRequest, string reason)
	{
		foreach (var q in _weaveQueue)
		{
			if (q.Id == actionId)
			{
				q.Target = target; // Repeated press: keep the position, take the latest target.
				RecordHimechanDiagnostic($"WEAVE Q REPEAT {label}({adjusted}) target=0x{target:X}");
				return;
			}
		}

		if (_weaveQueue.Count >= WHMHimechanPolicy.WeaveQueueMax)
		{
			RecordHimechanDiagnostic($"WEAVE Q FULL drop {label}({adjusted})");
			if (isRequest)
			{
				Svc.Chat.Print("[히메짱] 대기 중인 능력이 많아 요청을 받지 않았습니다.");
			}
			return;
		}

		_weaveQueue.Add(new QueuedAbility
		{
			Id = actionId,
			Target = target,
			GroundTarget = groundTarget,
			Expire = Now + WHMHimechanPolicy.WeaveQueueLifetime,
			Label = label,
			IsRequest = isRequest,
		});
		RecordHimechanDiagnostic($"WEAVE Q ADD {label}({adjusted}) target=0x{target:X} why={reason} gcd={DataCenter.DefaultGCDRemain:F2} n={_weaveQueue.Count}");
	}

	/// <summary>Priority step in UpdateNextAction: fire the head of the queue in the first safe gap. RSR is never held while waiting.</summary>
	internal unsafe bool TryReplayQueued()
	{
		if (_weaveQueue.Count == 0)
		{
			return false;
		}

		if (!WeaveQueueActive || OpenerOwnsInput || _manualRaise)
		{
			ClearWeaveQueue("상태 변경");
			return false;
		}

		var now = Now;
		while (_weaveQueue.Count > 0 && now > _weaveQueue[0].Expire)
		{
			DropQueued(0, "시간 초과");
		}

		if (_weaveQueue.Count == 0)
		{
			return false;
		}

		var headIndex = SelectQueueHead();
		var head = _weaveQueue[headIndex];
		IBattleChara? target = null;
		if (head.Target != 0 && head.Target != 0xE0000000)
		{
			target = Svc.Objects.SearchById(head.Target) as IBattleChara;
			if (target == null || (target.IsDead && !head.GroundTarget))
			{
				DropQueued(headIndex, "대상 없음");
				return false;
			}
		}

		var player = Player;
		var manager = ActionManager.Instance();
		if (player == null || manager == null || player.IsCasting || DataCenter.AnimationLock > 0f || manager->ActionQueued
			|| !(WeaveSlotFree || WeaveSecondSlotFree))
		{
			return false;
		}

		// An RSR command ability (/rsr DoActions) with a better rank goes first.
		if (DataCenter.CommandNextAction is IBaseAction command && command.Info.IsAbility
			&& WeaveRank(command.AdjustedID) < WeaveRank(Service.GetAdjustedActionId(head.Id)))
		{
			return false;
		}

		// Room in this gap (player threshold), or the GCD is idle and RSR has nothing to cast (downtime): otherwise let the GCD go first.
		var remain = DataCenter.DefaultGCDRemain;
		var fits = ManualWeaveFits(remain);
		var idle = remain <= DataCenter.CalculatedActionAhead && (!InCombat || ActionUpdater.NextGCDAction == null);
		if (!fits && !idle)
		{
			if (now >= _weaveHoldLogAfter && remain > 0f)
			{
				_weaveHoldLogAfter = now + 1.0;
				RecordHimechanDiagnostic($"WEAVE Q HOLD {head.Label} gcd={remain:F2}");
			}
			return false;
		}

		if (head.IsRequest && target != null && target.DistanceToPlayer() > 30)
		{
			return false; // Wait for the target to come back into range (until the request expires).
		}

		bool ok;
		_weaveReplaying = true;
		try
		{
			if (head.GroundTarget && target != null)
			{
				var loc = target.Position;
				ok = manager->UseActionLocation(ActionType.Action, head.Id, target.GameObjectId, &loc);
			}
			else
			{
				ok = manager->UseAction(ActionType.Action, head.Id, head.Target);
			}
		}
		finally
		{
			_weaveReplaying = false;
		}

		_weaveQueue.RemoveAt(headIndex);
		if (!ok)
		{
			RecordHimechanDiagnostic($"WEAVE Q REJECTED {head.Label}({head.Id})");
			if (head.IsRequest)
			{
				Svc.Chat.Print($"[히메짱] {head.Label} 사용이 거부되었습니다 (거리·재사용 대기·대상을 확인하세요).");
			}
			return false;
		}

		MarkWeaveUsed("replay", head.Id);
		RecordHimechanDiagnostic($"WEAVE Q REPLAY {head.Label}({head.Id}) target=0x{head.Target:X} gcd={remain:F2} left={_weaveQueue.Count}");
		return true;
	}

	private void DropQueued(int index, string reason)
	{
		var q = _weaveQueue[index];
		_weaveQueue.RemoveAt(index);
		RecordHimechanDiagnostic($"WEAVE Q DROP {q.Label}({q.Id}): {reason}");
		if (q.IsRequest)
		{
			Svc.Chat.Print($"[히메짱] {q.Label} 요청을 취소했습니다: {reason}");
		}
	}

	internal void ClearWeaveQueue(string reason)
	{
		while (_weaveQueue.Count > 0)
		{
			DropQueued(0, reason);
		}
	}

	private static bool IsAbilityId(uint adjustedId, out bool groundTarget)
	{
		groundTarget = false;
		var row = Service.GetSheet<LuminaAction>().GetRowOrDefault(adjustedId);
		if (row is not { } action || action.ActionCategory.RowId != (uint)ActionCate.Ability)
		{
			return false;
		}

		groundTarget = action.TargetArea;
		return true;
	}

	private static string ActionLabel(uint adjustedId)
	{
		var row = Service.GetSheet<LuminaAction>().GetRowOrDefault(adjustedId);
		var name = row is { } action ? action.Name.ExtractText() : string.Empty;
		return string.IsNullOrEmpty(name) ? adjustedId.ToString() : name;
	}
}
