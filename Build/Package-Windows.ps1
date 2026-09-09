param(
    [string]$Python = $env:TOSUN_PYTHON,
    [string]$Ffmpeg = $env:TOSUN_FFMPEG,
    [string]$PopplerBin = $env:TOSUN_POPPLER_BIN
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
function Resolve-Executable([string]$value, [string]$name) {
    if (-not [string]::IsNullOrWhiteSpace($value)) {
        if (Test-Path -LiteralPath $value) { return (Resolve-Path -LiteralPath $value).Path }
        $command = Get-Command $value -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) { return $command.Source }
    }
    $command = Get-Command $name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    throw "$name 실행 파일을 찾을 수 없습니다. -$name 경로 또는 TOSUN_$($name.ToUpper()) 환경변수를 지정하세요."
}
$python = Resolve-Executable $Python 'python'
$ffmpeg = Resolve-Executable $Ffmpeg 'ffmpeg'
if ([string]::IsNullOrWhiteSpace($PopplerBin)) { throw 'Poppler 경로가 없습니다. -PopplerBin 또는 TOSUN_POPPLER_BIN을 지정하세요.' }
$popplerBin = (Resolve-Path -LiteralPath $PopplerBin).Path
$packageRoot = Join-Path $projectRoot 'packaged\Tosun Flux Dev'
$backendBuildRoot = Join-Path $projectRoot 'Build\Intermediate\TosunFluxBackend'
$backendDistRoot = Join-Path $packageRoot 'backend'
$wpfProject = Join-Path $projectRoot 'Source\TosunFlux\TosunFlux.csproj'
$backendEntry = Join-Path $projectRoot 'Source\TosunFluxBackend\TosunFluxBackend.py'
if (-not (Test-Path -LiteralPath $ffmpeg)) { throw "FFmpeg를 찾을 수 없습니다: $ffmpeg" }
if (-not (Test-Path -LiteralPath (Join-Path $popplerBin 'pdftoppm.exe'))) { throw "Poppler를 찾을 수 없습니다: $popplerBin" }
New-Item -ItemType Directory -Path (Split-Path -Parent $packageRoot) -Force | Out-Null
if (Test-Path -LiteralPath $packageRoot) { Remove-Item -LiteralPath $packageRoot -Recurse -Force }
if (Test-Path -LiteralPath $backendBuildRoot) { Remove-Item -LiteralPath $backendBuildRoot -Recurse -Force }
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
dotnet publish $wpfProject -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $packageRoot
if ($LASTEXITCODE -ne 0) { throw "WPF publish failed with exit code $LASTEXITCODE" }
$binaryArgs = @('--add-binary', "$ffmpeg;vendor")
Get-ChildItem -LiteralPath $popplerBin -File | ForEach-Object {
    $binaryArgs += '--add-binary'
    $binaryArgs += "$($_.FullName);vendor"
}
& $python -m PyInstaller --noconfirm --clean --onedir --console --name 'TosunFluxBackend' --distpath $backendDistRoot --workpath $backendBuildRoot --specpath $backendBuildRoot --exclude-module numpy --exclude-module scipy @binaryArgs $backendEntry
if ($LASTEXITCODE -ne 0) { throw "Backend packaging failed with exit code $LASTEXITCODE" }
$backendInternalRoot = Join-Path $backendDistRoot 'TosunFluxBackend\_internal'
$popplerOnlyDuplicates = @('icudt78.dll', 'icuin78.dll', 'icuuc78.dll', 'icutu78.dll', 'poppler.dll')
foreach ($name in $popplerOnlyDuplicates) {
    $rootFile = Join-Path $backendInternalRoot $name
    $vendorFile = Join-Path $backendInternalRoot (Join-Path 'vendor' $name)
    if ((Test-Path -LiteralPath $rootFile) -and (Test-Path -LiteralPath $vendorFile)) {
        if ((Get-FileHash -LiteralPath $rootFile -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $vendorFile -Algorithm SHA256).Hash) { Remove-Item -LiteralPath $rootFile -Force }
    }
}
$readme = @"
# Tosun Flux

토순의 파일 컨버터

버전: v1.0.14

실행 파일: Tosun Flux.exe
"@
[System.IO.File]::WriteAllText((Join-Path $packageRoot 'RUNME.md'), $readme, [System.Text.Encoding]::UTF8)
Write-Output "Package created: $packageRoot"
