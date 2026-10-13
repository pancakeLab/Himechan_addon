# HIMECHAN: publishes a KRWardrobeCleaner (히메짱 옷장 정리기) build through the pancakeLab distribution repo
# and adds/replaces its entry in this fork's pluginmaster.json (same Dalamud repo URL as the WHM fork).
# Source of the plugin lives in a private repo; only the built zip is distributed.
#   ./.github/himechan/wardrobe-release.ps1 -Version 0.8.1.0 -ZipPath C:\path\KRWardrobeCleaner.zip -Notes "변경 내용"
# Then: git add pluginmaster.json; git commit -m "HIMECHAN: pluginmaster.json 옷장 정리기 0.8.1.0 [skip ci]"; git push origin himechan
param(
    [Parameter(Mandatory)] [string]$Version,                 # e.g. 0.8.1.0 (must equal AssemblyVersion inside the zip)
    [Parameter(Mandatory)] [string]$ZipPath,                 # KRWardrobeCleaner.zip built from the private source repo
    [string]$Notes = '',                                     # release notes / Changelog (Korean)
    [string]$Repo = 'pancakeLab/KRWardrobeCleaner',          # public distribution repo (releases only)
    [string]$PluginmasterPath = 'pluginmaster.json',
    [switch]$SkipRelease,                                    # only rewrite pluginmaster.json (release already exists)
    [switch]$Testing                                         # publish as a Dalamud testing build (prerelease); stable fields stay as they are
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path $ZipPath)) { throw "zip not found: $ZipPath" }
$zipFull = (Resolve-Path $ZipPath).Path

# Read the plugin manifest inside the zip and check the version.
$archive = [IO.Compression.ZipFile]::OpenRead($zipFull)
try {
    $entryJson = $archive.Entries | Where-Object { $_.Name -eq 'KRWardrobeCleaner.json' } | Select-Object -First 1
    if (-not $entryJson) { throw 'KRWardrobeCleaner.json not found inside the zip' }
    $reader = [IO.StreamReader]::new($entryJson.Open(), [Text.UTF8Encoding]::new($false))
    try { $m = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
} finally { $archive.Dispose() }
if ($m.InternalName -ne 'KRWardrobeCleaner') { throw "unexpected InternalName $($m.InternalName)" }
if ($m.AssemblyVersion -ne $Version) { throw "zip AssemblyVersion $($m.AssemblyVersion) != $Version" }

$sha = (Get-FileHash $zipFull -Algorithm SHA256).Hash
$tag = "v$Version"
$download = "https://github.com/$Repo/releases/download/$tag/KRWardrobeCleaner.zip"

$lines = @("히메짱 옷장 정리기 $Version")
if ($Notes) { $lines += ''; $lines += $Notes }
$lines += ''; $lines += "- KRWardrobeCleaner.zip SHA256: $sha"
$changelog = $lines -join "`n"

if (-not $SkipRelease) {
    $sums = Join-Path ([IO.Path]::GetTempPath()) 'SHA256SUMS.txt'
    [IO.File]::WriteAllText($sums, "KRWardrobeCleaner.zip  $sha`n", [Text.UTF8Encoding]::new($false))
    $notesFile = Join-Path ([IO.Path]::GetTempPath()) 'wardrobe-release-notes.md'
    [IO.File]::WriteAllText($notesFile, $changelog + "`n", [Text.UTF8Encoding]::new($false))
    gh release view $tag -R $Repo *> $null
    if ($LASTEXITCODE -ne 0) {
        $extra = @()
        if ($Testing) { $extra += "--prerelease"; $extra += "--latest=false" }
        gh release create $tag -R $Repo "$zipFull#Dalamud install package" "$sums#SHA-256 checksums" --title "히메짱 옷장 정리기 $Version$(if ($Testing) { " (테스트)" })" --notes-file $notesFile @extra
    } else {
        gh release upload $tag -R $Repo "$zipFull#Dalamud install package" "$sums#SHA-256 checksums" --clobber
    }
    if ($LASTEXITCODE -ne 0) { throw "gh release failed for $tag" }
}

$entry = [ordered]@{
    Author                 = 'pancakeLab'
    Name                   = '히메짱 옷장 정리기'
    InternalName           = $m.InternalName
    AssemblyVersion        = $Version
    TestingAssemblyVersion = $Version
    Description            = "4개 탭으로 분리된 FF14 장비 정리 도구. 옷장 복원, 옷장 저장, 장비함 이동, 스마트 자동 파기와 4번 인벤토리 전체 파기를 지원합니다.`n`n한국 서버 전용입니다. 소스는 비공개이며 이 저장소는 배포 전용입니다."
    Punchline              = '만사가 귀찮은 공주님들을 위한 자동 정리 집사님!'
    ApplicableVersion      = $m.ApplicableVersion
    RepoUrl                = "https://github.com/$Repo"
    Tags                   = @('환상의 옷장', '추억의 보관함', '장비함', '투영세트', '자동 정리', 'glamour', 'inventory', 'armoire', 'utility', 'korean')
    CategoryTags           = @('inventory')
    DalamudApiLevel        = $m.DalamudApiLevel
    TestingDalamudApiLevel = $m.DalamudApiLevel
    LoadRequiredState      = $m.LoadRequiredState
    LoadSync               = $m.LoadSync
    CanUnloadAsync         = $m.CanUnloadAsync
    LoadPriority           = $m.LoadPriority
    IsTestingExclusive     = $false
    IsHide                 = $false
    AcceptsFeedback        = $false
    DownloadLinkInstall    = $download
    DownloadLinkUpdate     = $download
    DownloadLinkTesting    = $download
    Changelog              = $changelog
    LastUpdate             = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    DownloadCount          = 0
}

$all = @()
$existing = $null
if (Test-Path $PluginmasterPath) {
    $parsed = @(Get-Content $PluginmasterPath -Raw -Encoding UTF8 | ConvertFrom-Json)
    $all = @($parsed | Where-Object { $_.InternalName -ne 'KRWardrobeCleaner' })
    $existing = $parsed | Where-Object { $_.InternalName -eq 'KRWardrobeCleaner' } | Select-Object -First 1
}
if ($Testing -and $existing) {
    # Keep the stable channel untouched; only the testing channel points at the new build.
    $entry.AssemblyVersion     = $existing.AssemblyVersion
    $entry.DownloadLinkInstall = $existing.DownloadLinkInstall
    $entry.DownloadLinkUpdate  = $existing.DownloadLinkUpdate
    $entry.Changelog           = "테스트 빌드 $Version (안정판 $($existing.AssemblyVersion))`n`n" + $changelog
}
$json = ConvertTo-Json @($all + @($entry)) -Depth 5
[IO.File]::WriteAllText($PluginmasterPath, $json + "`n", [Text.UTF8Encoding]::new($false))
"wrote $PluginmasterPath (KRWardrobeCleaner $Version, download $download)"
