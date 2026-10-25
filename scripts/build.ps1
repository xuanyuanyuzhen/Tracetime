param([switch]$Test, [switch]$Publish, [switch]$SelfContained, [switch]$Offline)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location $taskRoot
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (!$Offline) {
    & $taskDotnet restore TraceTime.sln
    if ($LASTEXITCODE -ne 0) { throw '依赖恢复失败。' }
}
& $taskDotnet build TraceTime.sln -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
if ($Test) {
    & $taskDotnet run --project tests/TraceTime.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw '场景验证失败。' }
}
if ($Publish) {
    $taskProject = [xml](Get-Content -LiteralPath (Join-Path $taskRoot 'src\TraceTime.App\TraceTime.App.csproj'))
    $taskVersion = $taskProject.Project.PropertyGroup.Version
    $taskPackageName = "TraceTime-$taskVersion-win-x64"
    $taskOutput = Join-Path $taskRoot "artifacts\release\$taskPackageName"
    if ($SelfContained) {
        & $taskDotnet publish src/TraceTime.App -c Release -r win-x64 --self-contained true -o $taskOutput -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
    } else {
        & $taskDotnet publish src/TraceTime.App -c Release --no-restore --self-contained false -o $taskOutput -p:DebugType=None -p:DebugSymbols=false
    }
    if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $taskOutput 'README.md')
    $taskPackageDocs = Join-Path $taskOutput 'docs'
    $taskPackageScreenshots = Join-Path $taskPackageDocs 'screenshots'
    New-Item -ItemType Directory -Path $taskPackageScreenshots -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\VALIDATION.md') -Destination $taskPackageDocs
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\LIMITS-AND-ANALYSIS.md') -Destination $taskPackageDocs
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\AI-PROMPT.txt') -Destination $taskPackageDocs
    Get-ChildItem -LiteralPath (Join-Path $taskRoot 'docs\screenshots') -Filter '*.png' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $taskPackageScreenshots }
    Set-Content -LiteralPath (Join-Path $taskOutput 'PACKAGE-TYPE.txt') -Value $(if ($SelfContained) { 'Self-contained Windows x64 package.' } else { 'Requires Microsoft .NET 10 Desktop Runtime x64.' }) -Encoding UTF8
    $taskArchive = Join-Path $taskRoot "artifacts\release\$taskPackageName.zip"
    Compress-Archive -Path $taskOutput -DestinationPath $taskArchive -Force
    $taskHash = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$taskArchive.sha256" -Value "$taskHash  $taskPackageName.zip" -Encoding ascii
    Write-Output "发布目录：$taskOutput"
    Write-Output "压缩包：$taskArchive"
}
