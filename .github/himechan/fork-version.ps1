# HIMECHAN: fork version = upstream a.b.c.d -> a.b.c.(d*100 + n), n = number of fork releases already made
# for that upstream tag (spec 2.4). Requires: upstream tags fetched into refs/upstream-tags/* and origin reachable.
#   git fetch upstream '+refs/tags/*:refs/upstream-tags/*' --no-tags
param(
    [string]$Ref = 'HEAD',
    [string]$Remote = 'origin'
)

$ErrorActionPreference = 'Stop'

function Get-UpstreamBaseTag([string]$ref) {
    $mergeBase = (git merge-base $ref upstream/main).Trim()
    if (-not $mergeBase) { throw "merge-base of $ref and upstream/main not found" }

    # Prefer a tag that points exactly at the merged upstream commit; otherwise the nearest earlier tag.
    $exact = git for-each-ref --points-at $mergeBase --format '%(refname:strip=2)' refs/upstream-tags |
        Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_ } -Descending |
        Select-Object -First 1
    if ($exact) { return [string]$exact, $mergeBase }

    $tags = git for-each-ref --format '%(refname:strip=2) %(objectname)' refs/upstream-tags |
        ForEach-Object { $p = $_ -split ' '; [pscustomobject]@{ Name = $p[0]; Sha = $p[1] } } |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending
    foreach ($t in $tags) {
        $peeled = (git rev-parse "$($t.Sha)^{commit}").Trim()
        git merge-base --is-ancestor $peeled $mergeBase
        if ($LASTEXITCODE -eq 0) { return $t.Name, $mergeBase }
    }
    throw "no upstream tag is an ancestor of $mergeBase"
}

$baseTag, $mergeBase = Get-UpstreamBaseTag $Ref
$base = [version]$baseTag
if ($base.Revision -gt 655) { throw "upstream revision $($base.Revision) too large for the d*100+n rule" }

# Existing fork releases for this upstream base: tags a.b.c.X on origin where floor(X/100) == d.
$existing = git ls-remote --tags --refs $Remote |
    ForEach-Object { ($_ -split "`t")[1] -replace '^refs/tags/', '' } |
    Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } |
    ForEach-Object { [version]$_ } |
    Where-Object { $_.Major -eq $base.Major -and $_.Minor -eq $base.Minor -and $_.Build -eq $base.Build -and [math]::Floor($_.Revision / 100) -eq $base.Revision }

$n = 0
if ($existing) { $n = ($existing | ForEach-Object { $_.Revision % 100 } | Measure-Object -Maximum).Maximum + 1 }
if ($n -gt 99) { throw "more than 100 fork releases for upstream $baseTag; bump upstream first" }

$version = '{0}.{1}.{2}.{3}' -f $base.Major, $base.Minor, $base.Build, ($base.Revision * 100 + $n)
$sha = (git rev-parse --short=8 $Ref).Trim()
$info = "$version+upstream.$baseTag.himechan.$sha"

"upstream base tag : $baseTag ($mergeBase)"
"fork version      : $version (n=$n)"
"informational     : $info"

if ($env:GITHUB_OUTPUT) {
    "base_tag=$baseTag" | Out-File -Append -FilePath $env:GITHUB_OUTPUT
    "version=$version" | Out-File -Append -FilePath $env:GITHUB_OUTPUT
    "info_version=$info" | Out-File -Append -FilePath $env:GITHUB_OUTPUT
    "himechan_sha=$sha" | Out-File -Append -FilePath $env:GITHUB_OUTPUT
}
