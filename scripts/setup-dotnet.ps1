$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskTools = Join-Path $taskRoot '.tools'
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
$taskInstaller = Join-Path $taskTools 'dotnet-install.ps1'
Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $taskInstaller
$taskSdkVersion = (Get-Content -LiteralPath (Join-Path $taskRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
& $taskInstaller -Version $taskSdkVersion -InstallDir (Join-Path $taskTools 'dotnet') -NoPath
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'SDK 安装失败。' }
& (Join-Path $taskTools 'dotnet\dotnet.exe') --version
