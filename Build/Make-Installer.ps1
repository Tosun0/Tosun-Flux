$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$devPackageRoot = Join-Path $projectRoot 'packaged\Tosun Flux Dev'
$userInstallRoot = Join-Path $projectRoot 'packaged\User Install'
$installerRoot = Join-Path $projectRoot 'packaged\Installer'
$releasePackageRoot = Join-Path $projectRoot 'packaged\Tosun Flux'
$payloadArchive = Join-Path $env:TEMP 'TosunFluxPayload.zip'
$installerProject = Join-Path $projectRoot 'Source\TosunFluxInstaller\TosunFluxInstaller.csproj'
$installerBuild = Join-Path $projectRoot 'Build\Intermediate\TosunFluxInstaller'
$installerPath = Join-Path $installerRoot 'Tosun Flux Setup.exe'
if (-not (Test-Path -LiteralPath (Join-Path $devPackageRoot 'Tosun Flux.exe'))) { throw "개발 패키지 EXE를 찾을 수 없습니다: $devPackageRoot" }
foreach ($path in @($userInstallRoot, $installerRoot, $releasePackageRoot)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}
if (Test-Path -LiteralPath $payloadArchive) { Remove-Item -LiteralPath $payloadArchive -Force }
if (Test-Path -LiteralPath $installerBuild) { Remove-Item -LiteralPath $installerBuild -Recurse -Force }
Copy-Item -Path (Join-Path $devPackageRoot '*') -Destination $userInstallRoot -Recurse -Force
Compress-Archive -Path (Join-Path $userInstallRoot '*') -DestinationPath $payloadArchive -CompressionLevel Optimal -Force
dotnet publish $installerProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PayloadPath="$payloadArchive" -o $installerBuild
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed with exit code $LASTEXITCODE" }
$builtInstaller = Join-Path $installerBuild 'Tosun Flux Setup.exe'
if (-not (Test-Path -LiteralPath $builtInstaller)) { throw "설치파일 빌드에 실패했습니다: $builtInstaller" }
Copy-Item -LiteralPath $builtInstaller -Destination $installerPath -Force
Copy-Item -Path (Join-Path $userInstallRoot '*') -Destination $releasePackageRoot -Recurse -Force
Copy-Item -LiteralPath $installerPath -Destination (Join-Path $releasePackageRoot 'Tosun Flux Setup.exe') -Force
$size = (Get-Item -LiteralPath $installerPath).Length / 1MB
Write-Output ("User install package: {0}" -f $userInstallRoot)
Write-Output ("Installer created: {0} ({1:N2} MB)" -f $installerPath, $size)
Write-Output ("Release package: {0}" -f $releasePackageRoot)