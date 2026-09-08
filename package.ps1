$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$python = 'C:\Users\mjo24\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
$pythonRoot = Split-Path -Parent $python
$ffmpeg = 'C:\Program Files\ffmpeg-8.1.1-essentials_build\bin\ffmpeg.exe'
$popplerBin = 'C:\Users\mjo24\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\poppler\Library\bin'
$tclRoot = 'C:\Program Files\Epic Games\UE_5.4\Engine\Binaries\ThirdParty\Python3\Win64\tcl'
$packageParent = Join-Path $root 'packaged'
$packageName = 'Tosun-Converter'
$packageRoot = Join-Path $packageParent $packageName
$buildRoot = Join-Path $root 'build'

if (-not (Test-Path -LiteralPath $python)) { throw "Bundled Python was not found: $python" }
if (-not (Test-Path -LiteralPath $ffmpeg)) { throw "FFmpeg was not found: $ffmpeg" }
$pdftoppm = Join-Path $popplerBin 'pdftoppm.exe'
if (-not (Test-Path -LiteralPath $pdftoppm)) { throw "Poppler pdftoppm was not found: $pdftoppm" }
if (-not (Test-Path -LiteralPath (Join-Path $tclRoot 'tcl8.6'))) { throw "Tcl runtime was not found: $tclRoot" }

New-Item -ItemType Directory -Path $packageParent -Force | Out-Null
if (Test-Path -LiteralPath $packageRoot) {
    try {
        Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction Stop
    } catch {
        $packageName = 'Tosun-Converter-v0.1.0'
        $packageRoot = Join-Path $packageParent $packageName
        if (Test-Path -LiteralPath $packageRoot) { Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction Stop }
    }
}
if (Test-Path -LiteralPath $buildRoot) { Remove-Item -LiteralPath $buildRoot -Recurse -Force }
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
$env:TCL_LIBRARY = Join-Path $tclRoot 'tcl8.6'
$env:TK_LIBRARY = Join-Path $tclRoot 'tk8.6'

$binaryArgs = @(
    '--add-binary', "$ffmpeg;vendor",
    '--add-binary', "$pdftoppm;vendor",
    '--add-binary', "$(Join-Path $pythonRoot 'DLLs\_tkinter.pyd');.",
    '--add-binary', "$(Join-Path $pythonRoot 'DLLs\tcl86t.dll');.",
    '--add-binary', "$(Join-Path $pythonRoot 'DLLs\tk86t.dll');."
)
Get-ChildItem -LiteralPath $popplerBin -Filter '*.dll' | ForEach-Object {
    $binaryArgs += '--add-binary'
    $binaryArgs += "$($_.FullName);vendor"
}

& $python -m PyInstaller `
    --noconfirm `
    --clean `
    --onedir `
    --windowed `
    --name $packageName `
    --add-data "$pythonRoot\Lib\tkinter;tkinter" `
    --add-data "$root\assets;assets" `
    --runtime-hook (Join-Path $root 'runtime_hook.py') `
    --distpath $packageParent `
    --workpath $buildRoot `
    --specpath $buildRoot `
    @binaryArgs `
    (Join-Path $root 'app.py')

$readme = @"
# Tosun-Converter

토순이의 파일 컨버터

버전: v0.1.0

실행 파일: $packageName.exe

이미지, PDF, DOCX, 텍스트/CSV/JSON, 영상/음성 파일을 로컬에서 변환합니다.
변환된 파일은 GUI에서 지정한 저장 폴더에 생성됩니다.
"@
[System.IO.File]::WriteAllText((Join-Path $packageRoot 'RUNME.md'), $readme, [System.Text.Encoding]::UTF8)
Write-Output "Package created: $packageRoot"
