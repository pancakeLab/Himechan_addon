using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Logging;

namespace RotationSolver.Himechan;

/// <summary>
/// Entry point for all Himechan code. Called from two one-line hooks in RotationSolverPlugin
/// (HIMECHAN-HOOK: Init / Dispose); everything else is registered here.
/// </summary>
internal static class HimechanMain
{
	public const string SettingsCommand = "/히메짱";
	public const string StatusCommand = "/히메짱상태";
	public const string RaiseCommand = "/히메짱레이즈";
	public const string SolaceCommand = "/히메짱백합";
	public const string AquaveilCommand = "/히메짱물장";
	public const string CastAssistCommand = "/히메짱슬캐";

	private static WindowSystem? _windowSystem;
	private static HimechanWindow? _window;
	private static bool _initialized;

	public static void Init()
	{
		if (_initialized)
		{
			return;
		}

		try
		{
			HimechanSettings.Load();
			HimechanProfile.Init();
			HimechanHooks.Init();

			_window = new HimechanWindow();
			_windowSystem = new WindowSystem("Himechan");
			_windowSystem.AddWindow(_window);
			_windowSystem.AddWindow(new HimechanHud());
			_windowSystem.AddWindow(new HimechanCastAssistWindow());
			Svc.PluginInterface.UiBuilder.Draw += _windowSystem.Draw;

			_ = Svc.Commands.AddHandler(SettingsCommand, new CommandInfo(OnSettingsCommand)
			{
				HelpMessage = "히메짱 설정 창을 엽니다.",
				ShowInHelp = true,
			});
			_ = Svc.Commands.AddHandler(StatusCommand, new CommandInfo(OnStatusCommand)
			{
				HelpMessage = "히메짱 버전·프로필·로테이션 상태와 최근 진단 기록을 채팅창에 표시합니다.",
				ShowInHelp = true,
			});
			_ = Svc.Commands.AddHandler(RaiseCommand, new CommandInfo(OnRaiseCommand)
			{
				HelpMessage = "수동 부활을 한 번 요청합니다 (실바람 → 신속마 → 레이즈). 취소: /히메짱레이즈 취소",
				ShowInHelp = true,
			});
			_ = Svc.Commands.AddHandler(SolaceCommand, new CommandInfo(OnSolaceCommand)
			{
				HelpMessage = "다음 GCD에 위로의 마음을 한 번 요청합니다 (아군 대상 / HP가 가장 낮은 파티원 / 전원 만피면 황홀한 마음).",
				ShowInHelp = true,
			});
			_ = Svc.Commands.AddHandler(AquaveilCommand, new CommandInfo(OnAquaveilCommand)
			{
				HelpMessage = "물의 장막을 한 번 요청합니다 (아군 대상 / 적·무대상이면 스탠스를 켠 탱커). 다음 안전한 위빙 틈에 사용합니다.",
				ShowInHelp = true,
			});
			_ = Svc.Commands.AddHandler(CastAssistCommand, new CommandInfo(OnCastAssistCommand)
			{
				HelpMessage = "슬라이딩 캐스팅 보조(CAST ASSIST) 토글을 켜고 끕니다 (켬 / 끔 / 인자 없으면 전환). 방식이 '토글'일 때 적용됩니다.",
				ShowInHelp = true,
			});

			Svc.Framework.Update += OnFrameworkUpdate;
			_initialized = true;
		}
		catch (Exception ex)
		{
			// Himechan must never stop RSR itself from loading.
			PluginLog.Error($"[Himechan] Init failed: {ex}");
		}
	}

	public static void Dispose()
	{
		if (!_initialized)
		{
			return;
		}

		try
		{
			Svc.Framework.Update -= OnFrameworkUpdate;
			_ = Svc.Commands.RemoveHandler(SettingsCommand);
			_ = Svc.Commands.RemoveHandler(StatusCommand);
			_ = Svc.Commands.RemoveHandler(RaiseCommand);
			_ = Svc.Commands.RemoveHandler(SolaceCommand);
			_ = Svc.Commands.RemoveHandler(AquaveilCommand);
			_ = Svc.Commands.RemoveHandler(CastAssistCommand);
			HimechanHooks.Dispose();
			HimechanLog.Dispose();
			if (_windowSystem != null)
			{
				Svc.PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
				_windowSystem.RemoveAllWindows();
			}
			HimechanProfile.Dispose();
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Dispose failed: {ex}");
		}
		finally
		{
			_windowSystem = null;
			_window = null;
			_initialized = false;
		}
	}

	/// <summary>Plugin version plus build info, e.g. "7.5.6.19 (7.5.6.19+abcdef)".</summary>
	public static string VersionText
	{
		get
		{
			var assembly = typeof(HimechanMain).Assembly;
			var version = assembly.GetName().Version?.ToString() ?? "?";
			var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
			return string.IsNullOrEmpty(info) || info == version ? version : $"{version} ({info})";
		}
	}

	public static string ProfileStatusText
	{
		get
		{
			if (HimechanProfile.IsWaitingForCombatEnd)
			{
				return "전투가 끝나면 설정 프로필을 전환합니다.";
			}

			if (HimechanProfile.IsActive)
			{
				return "히메짱 프로필 적용 중";
			}

			return HimechanSettings.Current.Enabled
				? "RSR 원본 설정 사용 중 (백마도사가 아님)"
				: "RSR 원본 설정 사용 중 (히메짱 꺼짐)";
		}
	}

	private static void OnFrameworkUpdate(IFramework framework)
	{
		HimechanProfile.Update();
		HimechanHooks.Update();
	}

	private static void OnSettingsCommand(string command, string arguments)
	{
		_window?.Toggle();
	}

	private static void OnStatusCommand(string command, string arguments)
	{
		PrintStatus();
	}

	private static void OnCastAssistCommand(string command, string arguments)
	{
		try
		{
			var settings = HimechanSettings.Current;
			var on = arguments.Trim().ToLowerInvariant() switch
			{
				"켬" or "on" or "켜기" => true,
				"끔" or "off" or "끄기" => false,
				_ => !settings.CastAssistToggleOn,
			};
			settings.CastAssistToggleOn = on;
			if (settings.CastAssistMode != HimechanCastAssist.ModeToggle)
			{
				Svc.Chat.Print($"[히메짱] CAST ASSIST 토글 {(on ? "켬" : "끔")} — 방식이 '토글'이 아니라 적용되지 않습니다. /히메짱 창에서 방식을 바꾸세요.");
				return;
			}

			Svc.Chat.Print(on
				? "[히메짱] CAST ASSIST 켬: 시전 중 이동이 슬라이딩 캐스팅 시점까지 보류됩니다. 급히 끊으려면 RSR 이동 잠금 보조 키(기본 Ctrl)나 패드 R1."
				: "[히메짱] CAST ASSIST 끔");
		}
		catch (Exception ex)
		{
			PluginLog.Error($"[Himechan] Cast assist command failed: {ex.Message}");
		}
	}

	internal static void PrintStatus()
	{
		Svc.Chat.Print($"[히메짱] 버전: {VersionText}");
		Svc.Chat.Print($"[히메짱] 설정: {ProfileStatusText}");
		Svc.Chat.Print($"[히메짱] 테스트 모드: {(HimechanTanks.TestModeActive ? "동작 중" : HimechanSettings.Current.TestMode ? "켜짐 (파티원이 있어 정지)" : "꺼짐")} | 디버그 로그: {(HimechanSettings.Current.DebugMode ? "켜짐" : "꺼짐")}");
		if (HimechanLog.LastFilePath != null)
		{
			Svc.Chat.Print($"[히메짱] 마지막 전투 로그: {HimechanLog.LastFilePath}");
		}

		if (DataCenter.CurrentRotation is RebornRotations.Healer.WHM_Himechan whm)
		{
			whm.PrintHimechanDiagnostics();
		}
		else
		{
			Svc.Chat.Print($"[히메짱] 현재 로테이션: {DataCenter.CurrentRotation?.GetType().Name ?? "없음"} (히메짱 WHM 아님)");
		}

		var recent = HimechanLog.Recent();
		for (var i = Math.Max(0, recent.Count - 10); i < recent.Count; i++)
		{
			Svc.Chat.Print(recent[i]);
		}
	}

	private static RebornRotations.Healer.WHM_Himechan? RequireHimechan()
	{
		if (DataCenter.CurrentRotation is RebornRotations.Healer.WHM_Himechan whm)
		{
			return whm;
		}

		Svc.Chat.Print("[히메짱] 백마도사에서 로테이션을 \"히메짱 WHM\"으로 선택했을 때만 사용할 수 있습니다.");
		return null;
	}

	private static void OnRaiseCommand(string command, string arguments)
	{
		RequireHimechan()?.HandleRaiseCommand(arguments ?? string.Empty);
	}

	private static void OnSolaceCommand(string command, string arguments)
	{
		RequireHimechan()?.HandleSolaceCommand();
	}

	private static void OnAquaveilCommand(string command, string arguments)
	{
		RequireHimechan()?.HandleAquaveilCommand();
	}
}
