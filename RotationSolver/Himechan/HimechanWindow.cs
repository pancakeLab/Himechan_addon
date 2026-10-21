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
			HimechanFonts.Rebuild();
		}
		ImGui.TextWrapped($"RSR의 영어 문구를 사전({HimechanLocalization.DictionaryCount}개)으로 바꿔 보여 줍니다. 사전에 없는 새 문구는 영어 그대로 보입니다. 직접 고치려면 설정 폴더의 ko.override.json에 {{\"영어 원문\": \"한국어\"}} 형식으로 적으세요.");
		if (ImGui.Button("사전 다시 읽기"))
		{
			HimechanLocalization.Reload();
			HimechanProfile.Notify($"번역 사전을 다시 읽었습니다 ({HimechanLocalization.DictionaryCount}개).");
		}

		ImGui.Separator();
		ImGui.TextUnformatted("HUD (GCD 링 · 예약 GCD/oGCD)");

		var hud = settings.HudEnabled;
		if (ImGui.Checkbox("HUD 표시", ref hud))
		{
			settings.HudEnabled = hud;
			HimechanSettings.Save();
		}

		var locked = settings.HudLocked;
		if (ImGui.Checkbox("HUD 잠금 (잠그면 클릭이 통과하고 움직이지 않음)", ref locked))
		{
			settings.HudLocked = locked;
			HimechanSettings.Save();
		}
		ImGui.TextWrapped(locked
			? "잠겨 있습니다. 옮기려면 잠금을 풀고 HUD의 노란 테두리를 끌어 놓은 뒤 다시 잠그세요."
			: "잠금이 풀려 있어 HUD가 항상 보이고 끌어서 옮길 수 있습니다. 위치는 자동 저장됩니다.");

		var compact = settings.HudCompact;
		if (ImGui.Checkbox("컴팩트 모드 (캐스팅 링 없이 GCD 2칸 + oGCD 4칸만)", ref compact))
		{
			settings.HudCompact = compact;
			HimechanSettings.Save();
		}

		var combatOnly = settings.HudCombatOnly;
		if (ImGui.Checkbox("전투·카운트다운 중에만 표시 (잠금 상태에서)", ref combatOnly))
		{
			settings.HudCombatOnly = combatOnly;
			HimechanSettings.Save();
		}

		var scale = settings.HudScale;
		ImGui.SetNextItemWidth(200);
		if (ImGui.SliderFloat("크기 배율", ref scale, 0.5f, 2.5f, "%.2f"))
		{
			settings.HudScale = scale;
		}
		if (ImGui.IsItemDeactivatedAfterEdit())
		{
			HimechanSettings.Save();
		}

		var slide = settings.HudSlidecastWindow;
		ImGui.SetNextItemWidth(200);
		if (ImGui.SliderFloat("이동·선입력 가능 시작 (시전 종료 전, 초)", ref slide, 0.2f, 1.0f, "%.2f"))
		{
			settings.HudSlidecastWindow = slide;
		}
		if (ImGui.IsItemDeactivatedAfterEdit())
		{
			HimechanSettings.Save();
		}

		if (ImGui.Button("HUD 위치 초기화 (화면 중앙 아래)"))
		{
			settings.HudPosX = -1f;
			settings.HudPosY = -1f;
			HimechanSettings.Save();
		}
		ImGui.TextWrapped("링: 파랑 = 지난 GCD, 빨강 = 위빙 불가 구간, 안쪽 초록 호 = 이동·다음 GCD 선입력이 가능한 구간(시작점에 노란 눈금, 구간에 들어가면 링 전체가 초록). 왼쪽 = 예약 GCD(다음·요청·명령·선입력), 오른쪽 = 대기 oGCD(우선순위 순, 초록 칸 수 = 이번 틈에 더 들어갈 수, 흐린 칸 = 다음 자동 후보). 주황 테두리 + \"정지\" = 시전이 필요한 GCD.");

		ImGui.Separator();
		ImGui.TextUnformatted("슬라이딩 캐스팅 보조 (CAST ASSIST)");

		var mode = settings.CastAssistMode;
		var modeChanged = ImGui.RadioButton("끔 (바닐라)", ref mode, HimechanCastAssist.ModeOff);
		modeChanged |= ImGui.RadioButton("마우스 좌+우 버튼으로 이동할 때만 (키보드 이동은 평소대로 시전을 끊음)", ref mode, HimechanCastAssist.ModeMouseHold);
		modeChanged |= ImGui.RadioButton("토글 (/히메짱슬캐 로 켜고 끔)", ref mode, HimechanCastAssist.ModeToggle);
		if (modeChanged)
		{
			settings.CastAssistMode = mode;
			HimechanSettings.Save();
		}
		ImGui.TextWrapped("켜진 동안 시전 중 이동 입력은 슬라이딩 캐스팅 시점(위 \"이동·선입력 가능 시작\" 값)까지 보류됐다가 그 순간 출발하고, 다음 GCD가 시전형이면 GCD 준비 직전(아래 여유)에 다시 멈춰 시전이 끊기지 않게 이어집니다. 그 사이 틈에서는 자유롭게 움직입니다. 급히 끊고 움직이려면 RSR 설정의 \"이동 잠금을 잠시 푸는 보조 키\"(기본 Ctrl) 또는 게임패드 R1을 누른 채 이동하세요. 히메짱 프로필의 RSR \"시전 중 이동 잠금\"은 이 기능이 켜고 끕니다.");

		var stopMargin = settings.CastAssistStopMargin;
		ImGui.SetNextItemWidth(200);
		if (ImGui.SliderFloat("시전 전 정지 여유 (GCD 준비 전, 초)", ref stopMargin, 0f, 0.5f, "%.2f"))
		{
			settings.CastAssistStopMargin = stopMargin;
		}
		if (ImGui.IsItemDeactivatedAfterEdit())
		{
			HimechanSettings.Save();
		}
		ImGui.TextWrapped("HUD의 주황 \"정지\" 표시도 같은 값을 씁니다. 시전이 시작되자마자 끊기면(로그 CAST ASSIST MEASURE INTERRUPTED) 값을 올리세요. 다음 GCD 계획(게임 선입력·RSR 다음 GCD)이 시전형일 때 멈춥니다. 계획이 없을 때는 자유롭게 움직이고, 움직이는 중에 시전 주문을 누르면 잠깐 멈춘 뒤(약 0.05초) 자동으로 시전합니다. 디버그 모드에서는 시전마다 측정 로그(MEASURE START/FX/OK)가 남습니다.");

		if (mode == HimechanCastAssist.ModeMouseHold)
		{
			var keyboardBreaks = settings.CastAssistKeyboardBreaks;
			if (ImGui.Checkbox("마우스 양 버튼을 누른 채 키보드 이동 키를 누르면 즉시 끊고 이동", ref keyboardBreaks))
			{
				settings.CastAssistKeyboardBreaks = keyboardBreaks;
				HimechanSettings.Save();
			}
			ImGui.TextWrapped("양 버튼을 누르고 있는 동안은 키보드 이동도 잠기므로, 이 옵션이 꺼져 있으면 RSR 보조 키(기본 Ctrl)나 버튼을 떼야 끊을 수 있습니다. 게임 키 설정의 이동 키를 읽고, 기본 키(WASD·QE·방향키)는 예비로 함께 봅니다.");
		}

		var unplanned = settings.CastAssistStopWhenUnplanned;
		if (ImGui.Checkbox("계획이 없어도 GCD 준비 직전에 정지 (수동 모드에서 미리 멈춤)", ref unplanned))
		{
			settings.CastAssistStopWhenUnplanned = unplanned;
			HimechanSettings.Save();
		}
		ImGui.TextWrapped("켜면 시전할 것이 없을 때도 GCD가 준비될 때마다 최대 1초 멈춥니다. 평시 이동이 거슬리면 끄세요(기본 꺼짐).");

		var alwaysStop = settings.CastAssistAlwaysStop;
		if (ImGui.Checkbox("항상 정지 (다음 GCD가 즉시 시전이어도 준비 직전에 멈춤)", ref alwaysStop))
		{
			settings.CastAssistAlwaysStop = alwaysStop;
			HimechanSettings.Save();
		}
		ImGui.TextWrapped("계획이 마지막 순간에 바뀌어 끊기는 일까지 막는 가장 확실한 방식. 대신 디아·백합 때도 잠깐 멈춥니다.");

		if (mode == HimechanCastAssist.ModeToggle)
		{
			var autoOff = settings.CastAssistAutoOff;
			if (ImGui.Checkbox("전투가 끝나면 토글을 자동으로 끔", ref autoOff))
			{
				settings.CastAssistAutoOff = autoOff;
				HimechanSettings.Save();
			}

			if (ImGui.Button(settings.CastAssistToggleOn ? "토글 끄기" : "토글 켜기"))
			{
				settings.CastAssistToggleOn = !settings.CastAssistToggleOn;
			}
			ImGui.SameLine();
		}
		ImGui.TextUnformatted($"상태: {HimechanCastAssist.StatusText}");

		var textLocked = settings.CastAssistTextLocked;
		if (ImGui.Checkbox("CAST ASSIST 글자 잠금 (잠그면 클릭이 통과하고 움직이지 않음)", ref textLocked))
		{
			settings.CastAssistTextLocked = textLocked;
			HimechanSettings.Save();
		}
		ImGui.TextWrapped(textLocked
			? "잠겨 있어 보조가 동작 중일 때만 노란 글자가 보입니다. 옮기려면 잠금을 풀고 끌어 놓은 뒤 다시 잠그세요."
			: "잠금이 풀려 있어 글자가 항상 보이고 끌어서 옮길 수 있습니다. 위치는 자동 저장됩니다.");

		var textScale = settings.CastAssistTextScale;
		ImGui.SetNextItemWidth(200);
		if (ImGui.SliderFloat("글자 크기 배율", ref textScale, 0.5f, 3f, "%.2f"))
		{
			settings.CastAssistTextScale = textScale;
		}
		if (ImGui.IsItemDeactivatedAfterEdit())
		{
			HimechanSettings.Save();
		}

		if (ImGui.Button("글자 위치 초기화 (화면 위쪽 가운데)"))
		{
			settings.CastAssistPosX = -1f;
			settings.CastAssistPosY = -1f;
			HimechanSettings.Save();
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
				HimechanProfile.Notify("테스트 모드를 켰습니다. 파티에 다른 플레이어가 없을 때만 탱커 대상 기능(오프너 탱커·물의 장막 요청·신성한 축복)이 나를 대상으로 동작합니다. 게임을 다시 시작하면 꺼집니다.");
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
