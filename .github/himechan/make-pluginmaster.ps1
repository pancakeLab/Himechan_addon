# HIMECHAN: writes the Dalamud custom-repo file (pluginmaster.json) for this fork from the packaged manifest.
# Users register: https://raw.githubusercontent.com/<repo>/himechan/pluginmaster.json
param(
    [Parameter(Mandatory)] [string]$ManifestPath,   # build/RotationSolver/RotationSolver.json (packaged, has AssemblyVersion)
    [Parameter(Mandatory)] [string]$Version,        # e.g. 7.5.6.1900
    [Parameter(Mandatory)] [string]$BaseTag,        # upstream tag, e.g. 7.5.6.19
    [Parameter(Mandatory)] [string]$Repo,           # owner/name
    [Parameter(Mandatory)] [string]$OutPath,
    [string]$Changelog = ''
)

$ErrorActionPreference = 'Stop'
$m = Get-Content $ManifestPath -Raw | ConvertFrom-Json
if ($m.InternalName -ne 'RotationSolver') { throw "unexpected InternalName $($m.InternalName)" }
if ($m.AssemblyVersion -ne $Version) { throw "packaged AssemblyVersion $($m.AssemblyVersion) != $Version" }

$download = "https://github.com/$Repo/releases/download/$Version/latest.zip"
$entry = [ordered]@{
    Author                 = "pancakeLab (비공식 포크) / 원본: $($m.Author)"
    Name                   = "Rotation Solver Reborn - 히메짱 WHM"
    InternalName           = $m.InternalName
    AssemblyVersion        = $Version
    TestingAssemblyVersion = $Version
    Description            = "Rotation Solver Reborn(RSR)의 비공식 소스 포크입니다. 원본 RSR 전체에 백마도사 전용 로테이션 '히메짱 WHM'과 한국어 설정 창이 추가되어 있습니다.`n`n공식 RSR과 내부 이름이 같아 동시에 설치할 수 없습니다. 공식 RSR을 제거한 뒤 설치하세요. 기존 RSR 설정은 그대로 유지됩니다.`n`n원본 기준: RSR $BaseTag"
    Punchline              = "RSR 비공식 포크 + 히메짱 WHM 로테이션 (원본 $BaseTag 기준)"
    ApplicableVersion      = $m.ApplicableVersion
    RepoUrl                = "https://github.com/$Repo"
    Tags                   = @($m.Tags) + @('himechan', 'whm', 'korean')
    CategoryTags           = @($m.CategoryTags)
    DalamudApiLevel        = $m.DalamudApiLevel
    TestingDalamudApiLevel = $m.DalamudApiLevel
    LoadRequiredState      = $m.LoadRequiredState
    LoadSync               = $m.LoadSync
    CanUnloadAsync         = $m.CanUnloadAsync
    LoadPriority           = $m.LoadPriority
    IsTestingExclusive     = $false
    IsHide                 = $false
    IconUrl                = "https://raw.githubusercontent.com/$Repo/himechan/.github/himechan/icon.png"
    AcceptsFeedback        = $false
    DownloadLinkInstall    = $download
    DownloadLinkUpdate     = $download
    DownloadLinkTesting    = $download
    Changelog              = $Changelog
    LastUpdate             = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    DownloadCount          = 0
}

$json = ConvertTo-Json @($entry) -Depth 5
[IO.File]::WriteAllText($OutPath, $json + "`n", [Text.UTF8Encoding]::new($false))
"wrote $OutPath ($Version, download $download)"
