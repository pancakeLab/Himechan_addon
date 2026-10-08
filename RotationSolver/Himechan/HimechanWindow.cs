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
		Size = new Vector2(420, 260);
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
		ImGui.TextDisabled($"버전 {HimechanMain.VersionText}");
		ImGui.TextDisabled($"설정 폴더: {HimechanPaths.Directory}");
	}
}
