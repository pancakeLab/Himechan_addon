<p align="center"><img src="himechan/icon.png" alt="Himechan WHM" width="200"></p>

> [!WARNING]
> **This is NOT the official Rotation Solver Reborn repository.**
> This is an unofficial personal fork that adds one custom White Mage rotation (히메짱 WHM).
> It is not affiliated with or supported by the CombatReborn team.
>
> **Looking for the real RSR?** Go here:
> - Official repository: https://github.com/FFXIV-CombatReborn/RotationSolverReborn
> - Official Dalamud repo URL: `https://raw.githubusercontent.com/FFXIV-CombatReborn/CombatRebornRepo/main/pluginmaster.json`
> - Official Discord: https://discord.gg/p54TZMPnC9
>
> Please **do not report problems with this fork to the official RSR team.** Use this repository's Issues instead.

> [!WARNING]
> **이 저장소는 공식 Rotation Solver Reborn(RSR)이 아닙니다.**
> 히메짱 WHM 로테이션 하나를 추가한 비공식 개인 포크이며, CombatReborn 팀과 관계가 없습니다.
> 공식 RSR을 찾으신다면 위 공식 저장소 링크를 이용하세요.
> 이 포크에서 생긴 문제는 공식 RSR 팀이 아니라 이 저장소의 Issues에 남겨 주세요.

# Himechan WHM fork of Rotation Solver Reborn

Source fork of [RotationSolverReborn](https://github.com/FFXIV-CombatReborn/RotationSolverReborn)
that adds the **히메짱 WHM** rotation. Upstream code is licensed under GPL-3.0 / LGPL-3.0
(see [`COPYING`](../COPYING) and [`COPYING.LESSER`](../COPYING.LESSER)); this fork is distributed
under the same terms. The upstream README is kept unchanged at [`/README.md`](../README.md).

## Branches
- `main`: untouched mirror of upstream `main` (no direct commits)
- `himechan`: upstream + Himechan code and hooks. Default branch; releases are built from here.

## How this fork is maintained
- Himechan logic lives in new files. Changes to upstream files are limited to small hook calls
  marked with `// HIMECHAN-HOOK: <name>`.
- Upstream files (including workflows and the upstream README) are not modified, so upstream
  updates merge cleanly. Himechan workflows are `.github/workflows/himechan-*.yaml`.
- Versioning: upstream `a.b.c.d` → fork `a.b.c.(d×100 + n)` (upstream 7.5.6.19 → 7.5.6.1900).

## Install
This fork is a complete build of RSR with the Himechan rotation included. It **replaces** the
official plugin; it is not an add-on.
- Both use the same internal name `RotationSolver`, so **uninstall the official RSR first** and
  register only this fork's repo URL. Do not keep both repo URLs registered.
- Dalamud custom repo URL:
  `https://raw.githubusercontent.com/pancakeLab/Himechan_addon/himechan/pluginmaster.json`
- Existing settings in `pluginConfigs/RotationSolver` are kept.
- Upstream RSR updates are merged into this fork automatically (`Himechan Sync`, every 6 hours),
  so you still receive them. Fork versions are `a.b.c.(d*100+n)` of upstream `a.b.c.d`.

## 설치 (한국어)
- 공식 RSR과 내부 이름이 같아 **동시에 설치할 수 없습니다.** 공식 RSR을 제거한 뒤 아래 주소를
  Dalamud 설정 → 실험적 기능 → 사용자 지정 플러그인 저장소에 등록하세요.
  `https://raw.githubusercontent.com/pancakeLab/Himechan_addon/himechan/pluginmaster.json`
- 기존 RSR 설정은 그대로 유지됩니다. 게임 안에서 `/히메짱` 으로 설정 창을 엽니다.
- 원본 RSR 업데이트는 6시간마다 자동으로 병합·배포됩니다. 한섭 Dalamud가 아직 지원하지 않는
  API 레벨로 원본이 올라가면 자동 배포를 멈추고 이슈로 알립니다.
