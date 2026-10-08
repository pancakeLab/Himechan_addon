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

			_window = new HimechanWindow();
			_windowSystem = new WindowSystem("Himechan");
			_windowSystem.AddWindow(_window);
			Svc.PluginInterface.UiBuilder.Draw += _windowSystem.Draw;

			_ = Svc.Commands.AddHandler(SettingsCommand, new CommandInfo(OnSettingsCommand)
			{
				HelpMessage = "히메짱 설정 창을 엽니다.",
				ShowInHelp = true,
			});
			_ = Svc.Commands.AddHandler(StatusCommand, new CommandInfo(OnStatusCommand)
			{
				HelpMessage = "히메짱 버전과 프로필 상태를 채팅창에 표시합니다.",
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
	}

	private static void OnSettingsCommand(string command, string arguments)
	{
		_window?.Toggle();
	}

	private static void OnStatusCommand(string command, string arguments)
	{
		Svc.Chat.Print($"[히메짱] 버전: {VersionText}");
		Svc.Chat.Print($"[히메짱] 설정: {ProfileStatusText}");
	}
}
