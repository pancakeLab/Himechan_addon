# HIMECHAN: create (or reuse) a GitHub issue for a sync problem. Needs GH_TOKEN with issues:write.
param(
    [Parameter(Mandatory)] [string]$Title,
    [Parameter(Mandatory)] [string]$Body
)

$ErrorActionPreference = 'Stop'
$label = 'himechan-sync'
gh label create $label --description "automatic upstream sync problems" --color D93F0B --force | Out-Null

$open = gh issue list --label $label --state open --json number,title | ConvertFrom-Json
$same = $open | Where-Object { $_.title -eq $Title } | Select-Object -First 1
if ($same) {
    gh issue comment $same.number --body "같은 문제가 다시 발생했습니다.`n`n$Body"
    "commented on existing issue #$($same.number)"
    return
}

$bodyFile = Join-Path $env:RUNNER_TEMP 'himechan-issue.md'
[IO.File]::WriteAllText($bodyFile, $Body, [Text.UTF8Encoding]::new($false))
gh issue create --title $Title --label $label --body-file $bodyFile
