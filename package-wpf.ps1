$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$python = 'C:\Users\mjo24\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
$ffmpeg = 'C:\Program Files\ffmpeg-8.1.1-essentials_build\bin\ffmpeg.exe'
$popplerBin = 'C:\Users\mjo24\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\poppler\Library\bin'
$packageRoot = Join-Path $projectRoot 'packaged\Tosun Flux'
$backendBuildRoot = Join-Path $projectRoot 'build-wpf\backend'
$backendDistRoot = Join-Path $packageRoot 'backend'
$wpfProject = Join-Path $projectRoot 'wpf\TosunConverter.Wpf.csproj'

if (-not (Test-Path -LiteralPath $python)) { throw "Bundled Python was not found: $python" }
if (-not (Test-Path -LiteralPath $ffmpeg)) { throw "FFmpeg was not found: $ffmpeg" }
if (-not (Test-Path -LiteralPath (Join-Path $popplerBin 'pdftoppm.exe'))) { throw "Poppler was not found: $popplerBin" }

New-Item -ItemType Directory -Path (Split-Path -Parent $packageRoot) -Force | Out-Null
if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $backendBuildRoot) {
    Remove-Item -LiteralPath $backendBuildRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

dotnet publish $wpfProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -o $packageRoot

$binaryArgs = @('--add-binary', "$ffmpeg;vendor")
Get-ChildItem -LiteralPath $popplerBin -File | ForEach-Object {
    $binaryArgs += '--add-binary'
    $binaryArgs += "$($_.FullName);vendor"
}

& $python -m PyInstaller `
    --noconfirm `
    --clean `
    --onedir `
    --console `
    --name 'TosunConverter.Backend' `
    --distpath $backendDistRoot `
    --workpath $backendBuildRoot `
    --specpath $backendBuildRoot `
    --exclude-module numpy `
    --exclude-module scipy `
    @binaryArgs `
    (Join-Path $projectRoot 'backend_cli.py')

# Poppler's ICU runtime is copied once as a PyInstaller dependency and once
# beside the bundled pdftoppm tools. Keep the vendor copy used by bundled_tool.
$backendInternalRoot = Join-Path $backendDistRoot 'TosunConverter.Backend\_internal'
$popplerOnlyDuplicates = @('icudt78.dll', 'icuin78.dll', 'icuuc78.dll', 'icutu78.dll', 'poppler.dll')
foreach ($name in $popplerOnlyDuplicates) {
    $rootFile = Join-Path $backendInternalRoot $name
    $vendorFile = Join-Path $backendInternalRoot (Join-Path 'vendor' $name)
    if ((Test-Path -LiteralPath $rootFile) -and (Test-Path -LiteralPath $vendorFile)) {
        $rootHash = (Get-FileHash -LiteralPath $rootFile -Algorithm SHA256).Hash
        $vendorHash = (Get-FileHash -LiteralPath $vendorFile -Algorithm SHA256).Hash
        if ($rootHash -eq $vendorHash) {
            Remove-Item -LiteralPath $rootFile -Force
        }
    }
}

$readme = @"
# Tosun Flux

토순의 파일 컨버터

버전: v0.4.1

실행 파일: Tosun Flux.exe

Windows 네이티브 Acrylic 글래스, Per-Monitor DPI, 파일 드래그 앤 드롭을 지원합니다.
이미지, PDF, DOCX, 텍스트/CSV/JSON, 영상/음성 파일을 로컬에서 변환합니다.
이미지·영상 최적화와 해상도/화면비 조정, PDF 내부 이미지·구조 최적화를 지원합니다.
"@
[System.IO.File]::WriteAllText((Join-Path $packageRoot 'RUNME.md'), $readme, [System.Text.Encoding]::UTF8)

Write-Output "Package created: $packageRoot"
