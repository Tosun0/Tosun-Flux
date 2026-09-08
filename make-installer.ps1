$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$packageRoot = Join-Path $projectRoot 'packaged\Tosun Flux'
$payloadArchive = Join-Path $env:TEMP 'TosunFluxPayload.zip'
$installerProject = Join-Path $projectRoot 'installer\TosunFlux.Setup.csproj'
$installerBuild = Join-Path $env:TEMP 'TosunFluxInstallerBuild'
$installerPath = Join-Path $packageRoot 'Tosun Flux Setup.exe'

if (-not (Test-Path -LiteralPath (Join-Path $packageRoot 'Tosun Flux.exe'))) {
    throw "패키지 EXE를 찾을 수 없습니다: $packageRoot"
}
if (Test-Path -LiteralPath $payloadArchive) {
    Remove-Item -LiteralPath $payloadArchive -Force
}
if (Test-Path -LiteralPath $installerBuild) {
    Remove-Item -LiteralPath $installerBuild -Recurse -Force
}

Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $payloadArchive -CompressionLevel Optimal -Force
dotnet publish $installerProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PayloadPath="$payloadArchive" -o $installerBuild

$builtInstaller = Join-Path $installerBuild 'Tosun Flux Setup.exe'
if (-not (Test-Path -LiteralPath $builtInstaller)) {
    throw "설치파일 빌드에 실패했습니다: $builtInstaller"
}
Copy-Item -LiteralPath $builtInstaller -Destination $installerPath -Force

$size = (Get-Item -LiteralPath $installerPath).Length / 1MB
Write-Output ("Installer created: {0} ({1:N2} MB)" -f $installerPath, $size)
