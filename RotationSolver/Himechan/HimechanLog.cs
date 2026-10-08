using ECommons.GameHelpers;
using ECommons.Logging;

namespace RotationSolver.Himechan;

/// <summary>
/// Spec 3.11 / #27. Always keeps the last lines in memory (shown by /히메짱상태); with debug mode on,
/// every countdown + fight is written to Himechan/logs/전투_yyyyMMdd_HHmmss.log (last 20 files, 5 MB each).
/// </summary>
internal static class HimechanLog
{
	private const int RecentLines = 64;
	private const int MaxFiles = 20;
	private const long MaxBytes = 5 * 1024 * 1024;
	private static readonly TimeSpan CloseAfterIdle = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

	private static readonly object _lock = new();
	private static readonly Queue<string> _recent = new();
	private static readonly HashSet<(ulong Id, uint Cast)> _seenCasts = [];
	private static readonly HashSet<(ulong Id, uint Cast)> _stillCasting = [];

	private static StreamWriter? _writer;
	private static long _bytes;
	private static bool _truncated;
	private static DateTime _idleSince = DateTime.MinValue;
	private static DateTime _nextFlush = DateTime.MinValue;

	public static string? LastFilePath { get; private set; }
	public static bool IsWriting => _writer != null;

	public static void Write(string category, string message)
	{
		var line = $"{DateTime.Now:HH:mm:ss.fff} [{category}] {message}";
		lock (_lock)
		{
			while (_recent.Count >= RecentLines)
			{
				_ = _recent.Dequeue();
			}
			_recent.Enqueue(line);

			if (_writer == null || _truncated)
			{
				return;
			}

			try
			{
				var combat = DataCenter.InCombat ? $"{DataCenter.CombatTimeRaw,7:F2}" : "   준비";
				var text = $"{line[..12]} {combat} {line[13..]}";
				_writer.WriteLine(text);
				_bytes += (text.Length * 2) + 2;
				if (_bytes > MaxBytes)
				{
					_writer.WriteLine("--- 파일 크기 한도(5MB)에 도달해 이 전투의 나머지 기록을 생략합니다 ---");
					_truncated = true;
				}
			}
			catch (Exception ex)
			{
				PluginLog.Warning($"[Himechan] Log write failed: {ex.Message}");
				CloseUnsafe();
			}
		}
	}

	public static List<string> Recent()
	{
		lock (_lock)
		{
			return [.. _recent];
		}
	}

	/// <summary>Called every frame from HimechanHooks.</summary>
	public static void Update()
	{
		var wanted = HimechanSettings.Current.DebugMode && Player.Available
			&& (DataCenter.InCombat || Service.CountDownTime > 0);

		if (wanted)
		{
			_idleSince = DateTime.MinValue;
			if (_writer == null)
			{
				Open();
			}
			ScanEnemyCasts();
		}
		else if (_writer != null)
		{
			if (!HimechanSettings.Current.DebugMode)
			{
				Close("디버그 모드 꺼짐");
			}
			else if (_idleSince == DateTime.MinValue)
			{
				_idleSince = DateTime.Now;
			}
			else if (DateTime.Now - _idleSince > CloseAfterIdle)
			{
				Close("전투 종료");
			}
		}

		if (_writer != null && DateTime.Now >= _nextFlush)
		{
			_nextFlush = DateTime.Now + FlushInterval;
			lock (_lock)
			{
				try
				{
					_writer?.Flush();
				}
				catch
				{
					CloseUnsafe();
				}
			}
		}
	}

	public static void Dispose()
	{
		Close("플러그인 종료");
	}

	private static void Open()
	{
		try
		{
			Directory.CreateDirectory(HimechanPaths.LogDirectory);
			var path = Path.Combine(HimechanPaths.LogDirectory, $"전투_{DateTime.Now:yyyyMMdd_HHmmss}.log");
			lock (_lock)
			{
				_writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
				_bytes = 0;
				_truncated = false;
				LastFilePath = path;
			}

			_seenCasts.Clear();
			var territory = DataCenter.Territory;
			Write("LOG", $"히메짱 전투 로그 | 버전 {HimechanMain.VersionText}");
			Write("LOG", $"지역 {territory?.Id} {territory?.Name} / {territory?.ContentFinderName} | 고난도={territory?.IsHighEndDuty} | 직업 {Player.Job}");
			Write("LOG", $"로테이션 {DataCenter.CurrentRotation?.GetType().Name} | 프로필 {(HimechanProfile.IsActive ? "히메짱" : "원본")} | 테스트 모드 {(HimechanTanks.TestModeActive ? "동작" : HimechanSettings.Current.TestMode ? "켜짐(파티원 있음, 정지)" : "꺼짐")}");
			PruneOldFiles();
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"[Himechan] Failed to open combat log: {ex.Message}");
			lock (_lock)
			{
				CloseUnsafe();
			}
		}
	}

	private static void Close(string reason)
	{
		if (_writer == null)
		{
			return;
		}

		Write("LOG", $"기록 종료: {reason}");
		lock (_lock)
		{
			CloseUnsafe();
		}
		_seenCasts.Clear();
	}

	private static void CloseUnsafe()
	{
		try
		{
			_writer?.Flush();
			_writer?.Dispose();
		}
		catch
		{
			// Ignore: the file may already be gone.
		}
		_writer = null;
	}

	private static void PruneOldFiles()
	{
		try
		{
			var files = Directory.GetFiles(HimechanPaths.LogDirectory, "*.log");
			Array.Sort(files, StringComparer.Ordinal);
			for (var i = 0; i < files.Length - MaxFiles; i++)
			{
				File.Delete(files[i]);
			}
		}
		catch (Exception ex)
		{
			PluginLog.Warning($"[Himechan] Failed to prune combat logs: {ex.Message}");
		}
	}

	/// <summary>Logs each enemy cast once when it starts: who, cast id, target, length.</summary>
	private static void ScanEnemyCasts()
	{
		_stillCasting.Clear();
		foreach (var hostile in DataCenter.AllHostileTargets)
		{
			if (hostile == null || !hostile.IsCasting)
			{
				continue;
			}

			var key = (hostile.GameObjectId, hostile.CastActionId);
			_ = _stillCasting.Add(key);
			if (_seenCasts.Add(key))
			{
				Write("CAST", $"{hostile.Name} base=0x{hostile.BaseId:X} id={hostile.CastActionId} 대상=0x{hostile.CastTargetObjectId:X} 길이={hostile.TotalCastTime:F1}s");
			}
		}

		_seenCasts.IntersectWith(_stillCasting);
	}
}
