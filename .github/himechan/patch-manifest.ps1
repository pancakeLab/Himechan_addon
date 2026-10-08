# HIMECHAN: Dalamud shows the installed plugin with the manifest that is INSIDE latest.zip, not the
# pluginmaster entry. The upstream manifest file (RotationSolver/RotationSolver.json) is never edited;
# instead the packaged copy is rewritten after the build and put back into latest.zip.
# InternalName stays "RotationSolver" (same plugin identity as upstream; required by the profile design).
param(
    [Parameter(Mandatory)] [string]$BuildDir,   # build/RotationSolver (contains RotationSolver.json + latest.zip)
    [Parameter(Mandatory)] [string]$Repo        # owner/name
)

$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $BuildDir 'RotationSolver.json'
$zipPath = Join-Path $BuildDir 'latest.zip'
if (-not (Test-Path $manifestPath) -or -not (Test-Path $zipPath)) { throw "packaged manifest or latest.zip not found in $BuildDir" }

$m = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($m.InternalName -ne 'RotationSolver') { throw "unexpected InternalName $($m.InternalName)" }

$m.Name = 'Rotation Solver Reborn - 히메짱 WHM'
$m.Author = "pancakeLab (비공식 포크) / 원본: $($m.Author)"
$m.Punchline = 'RSR 비공식 포크 + 히메짱 WHM 로테이션'
$m.Description = "Rotation Solver Reborn(RSR)의 비공식 소스 포크입니다. 원본 RSR 전체에 백마도사 전용 로테이션 '히메짱 WHM'과 한국어 설정 창이 추가되어 있습니다.`n`n명령: /히메짱 (설정 창), /히메짱상태, /히메짱레이즈, /히메짱백합`n`n공식 RSR과 내부 이름이 같아 동시에 설치할 수 없습니다. 이 포크의 문제는 공식 RSR 팀이 아니라 https://github.com/$Repo 의 Issues에 남겨 주세요."
$m.RepoUrl = "https://github.com/$Repo"
$m.IconUrl = "https://raw.githubusercontent.com/$Repo/himechan/.github/himechan/icon.png"
$m.AcceptsFeedback = $false

$json = ConvertTo-Json $m -Depth 5
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($manifestPath, $json + "`n", $utf8)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Update)
try {
    $entry = $zip.GetEntry('RotationSolver.json')
    if ($null -eq $entry) { throw 'RotationSolver.json not found inside latest.zip' }
    $entry.Delete()
    $entry = $zip.CreateEntry('RotationSolver.json', [IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    try { $bytes = $utf8.GetBytes($json + "`n"); $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}
finally { $zip.Dispose() }

"manifest patched: Name='$($m.Name)' Author='$($m.Author)' AssemblyVersion=$($m.AssemblyVersion)"
