# HIMECHAN: create (or reuse) a GitHub issue for a sync problem. Needs GH_TOKEN with issues:write.
param(
    [Parameter(Mandatory)] [string]$Title,
    [Parameter(Mandatory)] [string]$Body
)

$ErrorActionPreference = 'Stop'
$label = 'himechan-sync'
# gh picks the remote named 'upstream' (added by the sync workflow) as its default repo, which gave HTTP 403 on every
# conflict since 2026-10-21 and no issue was ever filed: always address this repository explicitly.
$repo = $env:GITHUB_REPOSITORY
if (-not $repo) { $repo = 'pancakeLab/Himechan_addon' }
gh label create -R $repo $label --description "automatic upstream sync problems" --color D93F0B --force | Out-Null

$open = gh issue list -R $repo --label $label --state open --json number,title | ConvertFrom-Json
$same = $open | Where-Object { $_.title -eq $Title } | Select-Object -First 1
if ($same) {
    gh issue comment -R $repo $same.number --body "같은 문제가 다시 발생했습니다.`n`n$Body"
    "commented on existing issue #$($same.number)"
    return
}

$bodyFile = Join-Path $env:RUNNER_TEMP 'himechan-issue.md'
[IO.File]::WriteAllText($bodyFile, $Body, [Text.UTF8Encoding]::new($false))
gh issue create -R $repo --title $Title --label $label --body-file $bodyFile
