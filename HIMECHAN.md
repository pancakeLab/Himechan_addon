# Himechan WHM fork of Rotation Solver Reborn

This is a source fork of [RotationSolverReborn](https://github.com/FFXIV-CombatReborn/RotationSolverReborn)
(GPL-3.0 / LGPL-3.0, see `COPYING` and `COPYING.LESSER`) that adds the **히메짱 WHM** rotation.

## Branches
- `main`: untouched mirror of upstream `main` (no direct commits)
- `himechan`: upstream + Himechan code and hooks. **Default branch; releases are built from here.**

## Rules
- Himechan logic lives in new files. Changes to upstream files are limited to small hook calls
  marked with `// HIMECHAN-HOOK: <name>`.
- Upstream workflow files are not modified. Himechan workflows are `.github/workflows/himechan-*.yaml`.

## Versioning
Upstream `a.b.c.d` → fork `a.b.c.(d×100 + n)`. Upstream 7.5.6.19 → first fork release 7.5.6.1900.

## Install
The fork and the official plugin share `InternalName` `RotationSolver`, so remove the official
Rotation Solver Reborn before installing. Existing settings in `pluginConfigs/RotationSolver` are kept.
(Custom repository URL will be added once releases are published.)
