$ErrorActionPreference = 'Stop'
$python = 'C:\Users\mjo24\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
& $python (Join-Path $PSScriptRoot 'app.py')
