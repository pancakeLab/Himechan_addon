using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

namespace RotationSolver.Himechan;

/// <summary>
/// Himechan settings window (/히메짱). All text is Korean. Uses plain ImGui instead of RSR's own UI kit,
/// so upstream UI redesigns do not affect it.
/// </summary>
internal sealed class HimechanWindow : Window
{
	public HimechanWindow() : base("히메짱 WHM###HimechanMain", ImGuiWindowFlags.NoCollapse)
	{
		Size = new Vector2(460, 480);
		SizeCondition = ImGuiCond.FirstUseEver;
		RespectCloseHotkey = true;
	}

	public override bool DrawConditions()
	{
		return Svc.ClientState.IsLoggedIn;
	}

	public override void Draw()
	{
		var settings = HimechanSettings.Current;

		var enabled = settings.Enabled;
		if (ImGui.Checkbox("히메짱 사용", ref enabled))
		{
			settings.Enabled = enabled;
			HimechanSettings.Save();
		}
		ImGui.TextWrapped("켜 두면 백마도사일 때 히메짱 전용 설정 프로필을 사용합니다. 다른 직업은 항상 RSR 원본 설정을 씁니다. 전환은 전투 밖에서만 일어납니다.");

		ImGui.Separator();
		ImGui.TextUnformatted($"현재 상태: {HimechanMain.ProfileStatusText}");
		if (HimechanProfile.IsActive)
		{
			ImGui.TextWrapped("지금 RSR 설정 창에서 바꾸는 값은 히메짱 프로필에만 저장되고, RSR 원본 설정은 바뀌지 않습니다.");
		}

		ImGui.Separator();
		ImGui.TextUnformatted("히메짱 프로필 다시 만들기");
		ImGui.TextWrapped("지금의 히메짱 프로필을 백업 폴더로 옮기고, 현재 RSR 원본 설정을 복제해 새로 시작합니다. 전투 밖에서만 가능합니다.");
		var ctrl = ImGui.GetIO().KeyCtrl;
		if (!ctrl)
		{
			ImGui.BeginDisabled();
		}
		if (ImGui.Button("다시 만들기 (Ctrl을 누른 채 클릭)") && ctrl)
		{
			if (!HimechanProfile.Recreate())
			{
				HimechanProfile.Notify("전투 중에는 프로필을 다시 만들 수 없습니다.");
			}
		}
		if (!ctrl)
		{
			ImGui.EndDisabled();
		}

		ImGui.Separator();
		var translate = settings.TranslateRsrUi;
		if (ImGui.Checkbox("RSR 설정 창을 한국어로 표시", ref translate))
		{
			settings.TranslateRsrUi = translate;
			HimechanSettings.Save();
		}
		ImGui.TextWrapped($"RSR의 영어 문구를 사전({HimechanLocalization.DictionaryCount}개)으로 바꿔 보여 줍니다. 사전에 없는 새 문구는 영어 그대로 보입니다. 직접 고치려면 설정 폴더의 ko.override.json에 {{\"영어 원문\": \"한국어\"}} 형식으로 적으세요.");
		if (ImGui.Button("사전 다시 읽기"))
		{
			HimechanLocalization.Reload();
			HimechanProfile.Notify($"번역 사전을 다시 읽었습니다 ({HimechanLocalization.DictionaryCount}개).");
		}

		ImGui.Separator();
		ImGui.TextUnformatted("디버그 · 테스트");

		var debug = settings.DebugMode;
		if (ImGui.Checkbox("디버그 모드 (전투마다 로그 파일 저장)", ref debug))
		{
			settings.DebugMode = debug;
			HimechanSettings.Save();
		}
		ImGui.TextWrapped("카운트다운부터 전투 종료까지 히메짱의 판단·사용·효과·적 시전을 파일로 남깁니다(최근 20개). 문제가 생기면 이 파일을 전달해 주세요.");
		if (HimechanLog.LastFilePath != null)
		{
			ImGui.TextDisabled($"마지막 로그: {HimechanLog.LastFilePath}");
		}

		var test = settings.TestMode;
		if (ImGui.Checkbox("테스트 모드 (혼자일 때 나를 탱커로 취급)", ref test))
		{
			settings.TestMode = test;
			if (test)
			{
				HimechanProfile.Notify("테스트 모드를 켰습니다. 파티에 다른 플레이어가 없을 때만 탱커 대상 기능(오프너 탱커·물의 장막·신성한 축복)이 나를 대상으로 동작합니다. 게임을 다시 시작하면 꺼집니다.");
			}
		}
		ImGui.TextWrapped(HimechanTanks.TestModeActive
			? "테스트 모드 동작 중: 나를 탱커로 취급합니다."
			: settings.TestMode ? "테스트 모드 켜짐 — 파티에 다른 플레이어가 있어 멈춰 있습니다." : "실제 파티에서는 쓰지 마세요. 저장되지 않아 재시작하면 꺼집니다.");

		if (ImGui.Button("진단 내용을 채팅창에 출력"))
		{
			HimechanMain.PrintStatus();
		}

		ImGui.Separator();
		ImGui.TextDisabled($"버전 {HimechanMain.VersionText}");
		ImGui.TextDisabled($"설정 폴더: {HimechanPaths.Directory}");
	}
}
