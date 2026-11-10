param([switch]$AppearanceOnly, [string]$ReleaseRoot = 'artifacts\release')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskProject = [xml](Get-Content -LiteralPath (Join-Path $taskRoot 'src\TraceTime.App\TraceTime.App.csproj'))
$taskVersion = $taskProject.Project.PropertyGroup.Version
$taskReleaseRoot = if ([System.IO.Path]::IsPathRooted($ReleaseRoot)) { $ReleaseRoot } else { Join-Path $taskRoot $ReleaseRoot }
$taskExe = Join-Path $taskReleaseRoot "TraceTime-$taskVersion-win-x64\TraceTime.exe"
if (!(Test-Path -LiteralPath $taskExe)) { throw '请先生成发布包。' }
# Use the system-installed runtime, not the development SDK runtime.
Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue
Remove-Item Env:DOTNET_ROOT_X64 -ErrorAction SilentlyContinue
$taskQa = Join-Path $taskRoot ('artifacts\qa\release-' + $taskVersion + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $taskQa -Force | Out-Null
function Invoke-ReleaseCheck([string[]]$TaskArguments, [int]$TaskTimeout = 45000) {
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList $TaskArguments -WindowStyle Hidden -PassThru
    if (!$taskProcess.WaitForExit($TaskTimeout)) { $taskProcess.Kill(); throw '发布包验证超时。' }
    if ($taskProcess.ExitCode -ne 0) { throw "程序异常退出：$($taskProcess.ExitCode)" }
}
if (!$AppearanceOnly) {
$taskFlow = Join-Path $taskQa 'release-flow'
Invoke-ReleaseCheck @('--data-dir', $taskFlow, '--verify-flow')
$taskFlowResult = Get-Content -LiteralPath (Join-Path $taskFlow 'flow-result.txt')
$taskFlowResult
if ($taskFlowResult -match '^FAIL') { throw '应用流程检查失败。' }
$taskSingle = Join-Path $taskQa 'release-single'
$taskFirst = Start-Process -FilePath $taskExe -ArgumentList @('--data-dir', $taskSingle, '--tray', '--smoke-seconds', '8') -WindowStyle Hidden -PassThru
try {
    if ($taskFirst.WaitForExit(1000)) { throw '首个实例提前退出。' }
    Invoke-ReleaseCheck @('--data-dir', $taskSingle, '--tray') 4000
    if (!$taskFirst.WaitForExit(15000)) { throw '首个实例未按时退出。' }
    if ($taskFirst.ExitCode -ne 0) { throw '首个实例退出码异常。' }
    'PASS second instance exits while first instance continues'
    Get-Content -LiteralPath (Join-Path $taskSingle 'smoke-result.txt')
} finally { if (!$taskFirst.HasExited) { $taskFirst.Kill() } }
}
Invoke-ReleaseCheck @('--demo', '--data-dir', (Join-Path $taskQa 'demo'), '--capture', (Join-Path $taskQa 'overview.png'))
if (Test-Path -LiteralPath (Join-Path $taskQa 'overview.error.txt')) { throw (Get-Content -LiteralPath (Join-Path $taskQa 'overview.error.txt') -Raw) }
Get-Content -LiteralPath (Join-Path $taskQa 'overview.checks.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.features.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.ai-flow.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.polish.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.day-sound.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.audio-flow.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.navigation.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.limit-options.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.email-flow.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.history-quick.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.chart-resources.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.break-reminders.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.break-audio-cancel.txt')
Get-Content -LiteralPath (Join-Path $taskQa 'overview.performance.txt')
'PASS published app works with the system .NET runtime; demo captures created'

# Restart with a saved choice, then check that the launched UI actually uses it.
$taskAppearance = Join-Path $taskQa 'appearance-restart'
New-Item -ItemType Directory -Path $taskAppearance -Force | Out-Null
Invoke-ReleaseCheck @('--demo', '--data-dir', $taskAppearance, '--verify-appearance-load')
if ((Get-Content -LiteralPath (Join-Path $taskAppearance 'appearance-loaded.txt') -Raw) -notmatch '^system\|glass\|#24777B\|') { throw '首次默认外观不正确。' }
@{ Mode = 'dark'; Material = 'minimal'; Accent = '#7956B3' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskAppearance 'appearance.json') -Encoding utf8
Invoke-ReleaseCheck @('--demo', '--data-dir', $taskAppearance, '--verify-appearance-load')
if ((Get-Content -LiteralPath (Join-Path $taskAppearance 'appearance-loaded.txt') -Raw).Trim() -ne 'dark|minimal|#7956B3|dark=True') { throw '重新启动没有恢复已选外观。' }
'PASS first launch follows system with glass; restart restores saved dark/minimal/custom color'
Invoke-ReleaseCheck @('--demo', '--data-dir', $taskAppearance, '--verify-view-load')
if ((Get-Content -LiteralPath (Join-Path $taskAppearance 'view-loaded.txt') -Raw).Trim() -ne '200|bar|收起侧边栏') { throw '首次导航默认状态不正确。' }
if ((Get-Content -LiteralPath (Join-Path $taskAppearance 'temporary-loaded.txt') -Raw).Trim() -ne '10') { throw '旧配置缺省临时分钟错误。' }
@{ SidebarCollapsed = $true; DayChart = 'ring'; TemporaryMinutes = 25 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskAppearance 'view-preferences.json') -Encoding utf8
Invoke-ReleaseCheck @('--demo', '--data-dir', $taskAppearance, '--verify-view-load')
if ((Get-Content -LiteralPath (Join-Path $taskAppearance 'view-loaded.txt') -Raw).Trim() -ne '72|ring|展开侧边栏') { throw '重启没有恢复图标侧栏和环图选择。' }
'PASS new process restores collapsed sidebar and selected chart'
if ((Get-Content -LiteralPath (Join-Path $taskAppearance 'temporary-loaded.txt') -Raw).Trim() -ne '25') { throw '新进程未恢复常用临时分钟。' }
'PASS default and preferred temporary minutes restore in a new process'
Write-Output "验证目录：$taskQa"

$taskBreakRestart = Join-Path $taskQa 'break-restart'
Invoke-ReleaseCheck @('--demo', '--data-dir', $taskBreakRestart, '--verify-break-load')
if ((Get-Content -LiteralPath (Join-Path $taskBreakRestart 'break-loaded.txt') -Raw).Trim() -ne 'False|30|Medium||0') { throw '首次休息提醒没有默认关闭。' }
@{ Enabled = $true; Minutes = 45; Level = 2; IgnoredOn = '2026-11-10' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskBreakRestart 'break-reminder.json') -Encoding utf8
Invoke-ReleaseCheck @('--demo', '--data-dir', $taskBreakRestart, '--verify-break-load')
if ((Get-Content -LiteralPath (Join-Path $taskBreakRestart 'break-loaded.txt') -Raw).Trim() -ne 'True|45|Severe|2026-11-10|0') { throw '休息设置或新启动从零计时未恢复。' }
'PASS break reminder defaults off; new process restores enabled/interval/intensity/today-ignore and starts a fresh session'
