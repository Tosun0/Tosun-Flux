param(
    [string]$Destination = (Join-Path $PSScriptRoot 'ThirdParty\RealESRGAN\v0.2.5.0-windows')
)
$ErrorActionPreference = 'Stop'
$uri = 'https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip'
$archive = Join-Path $env:TEMP 'realesrgan-ncnn-vulkan-20220424-windows.zip'
if (Test-Path -LiteralPath $Destination) { throw "Destination already exists: $Destination" }
New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
Invoke-WebRequest -Uri $uri -OutFile $archive
Expand-Archive -LiteralPath $archive -DestinationPath $Destination
Write-Output "Real-ESRGAN downloaded: $Destination"
