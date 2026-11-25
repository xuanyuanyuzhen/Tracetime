param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Label, [ValidateRange(10,90)][int]$SampleSeconds = 25, [ValidateRange(1,90)][int]$Days = 1, [switch]$WithRules)
$ErrorActionPreference = 'Stop'
$taskExe = (Resolve-Path -LiteralPath $Exe).Path
$taskRepository = Split-Path $PSScriptRoot -Parent
$taskRoot = Join-Path $taskRepository ('artifacts/qa/performance-' + $Label)
New-Item -ItemType Directory -Path $taskRoot -Force | Out-Null
if (!('TaskWindowState' -as [type])) { Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TaskWindowState {
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int command);
    [StructLayout(LayoutKind.Sequential)] public struct IoCounters {
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GetProcessIoCounters(IntPtr h, out IoCounters counters);
}
'@
}
$taskDotnet = Join-Path $taskRepository '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = 'dotnet' }
$taskTests = Join-Path $taskRepository 'tests/TraceTime.Tests/bin/Release/net10.0/TraceTime.Tests.dll'
if (!(Test-Path -LiteralPath $taskTests)) { throw 'Run scripts/build.ps1 -Test first.' }
$taskResults = @()
foreach ($taskMode in @('tray','overview','minimized')) {
    $taskData = Join-Path $taskRoot ($taskMode + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
    & $taskDotnet $taskTests '--seed-performance' $taskData $Days.ToString() $WithRules.IsPresent.ToString() | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic seed failed.' }
    $taskArgs = @('--data-dir',('"' + $taskData + '"'),'--smoke-seconds',($SampleSeconds+18).ToString())
    if ($taskMode -eq 'tray') { $taskArgs += '--tray' }
    $taskProcess = Start-Process -FilePath $taskExe -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 6
    $taskProcess.Refresh()
    if ($taskProcess.HasExited) { throw 'Benchmark exited early.' }
    if ($taskMode -eq 'minimized') {
        if ($taskProcess.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Owned benchmark window not found.' }
        $null = [TaskWindowState]::ShowWindowAsync($taskProcess.MainWindowHandle,6)
    }
    Start-Sleep -Seconds 2
    $taskProcess.Refresh()
    $taskCpuStart = $taskProcess.TotalProcessorTime.TotalMilliseconds
    $taskIoStart = [TaskWindowState+IoCounters]::new()
    if (![TaskWindowState]::GetProcessIoCounters($taskProcess.Handle,[ref]$taskIoStart)) { throw 'Cannot read owned process I/O counters.' }
    $taskWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $taskSamples = @()
    for ($taskIndex=0; $taskIndex -lt $SampleSeconds; $taskIndex++) {
        Start-Sleep -Seconds 1
        $taskProcess.Refresh()
        if ($taskProcess.HasExited) { throw 'Benchmark exited during sample.' }
        $taskSamples += [pscustomobject]@{ WorkingSetMiB=$taskProcess.WorkingSet64/1MB; PrivateMiB=$taskProcess.PrivateMemorySize64/1MB }
    }
    $taskElapsed = $taskWatch.Elapsed.TotalSeconds
    $taskCpuMs = $taskProcess.TotalProcessorTime.TotalMilliseconds - $taskCpuStart
    $taskIoEnd = [TaskWindowState+IoCounters]::new()
    if (![TaskWindowState]::GetProcessIoCounters($taskProcess.Handle,[ref]$taskIoEnd)) { throw 'Cannot read owned process I/O counters.' }
    $taskRow = [pscustomobject]@{
        Label=$Label; Mode=$taskMode; Days=$Days; Rules=$(if($WithRules){40}else{0}); Seconds=[math]::Round($taskElapsed,3); CpuMilliseconds=[math]::Round($taskCpuMs,3)
        CpuWholeMachinePercent=[math]::Round($taskCpuMs/($taskElapsed*1000*[Environment]::ProcessorCount)*100,4)
        WorkingSetMeanMiB=[math]::Round(($taskSamples | Measure-Object WorkingSetMiB -Average).Average,2)
        PrivateMeanMiB=[math]::Round(($taskSamples | Measure-Object PrivateMiB -Average).Average,2)
        LogicalReadBytes=$taskIoEnd.ReadBytes-$taskIoStart.ReadBytes; LogicalWriteBytes=$taskIoEnd.WriteBytes-$taskIoStart.WriteBytes
    }
    if (!$taskProcess.WaitForExit(20000)) { throw 'Owned benchmark did not exit on its smoke timer.' }
    if (!(Test-Path -LiteralPath (Join-Path $taskData 'smoke-result.txt'))) { throw 'Benchmark collection/exit check missing.' }
    $taskResults += $taskRow
    $taskResults | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot 'results.json') -Encoding utf8
    $taskRow | ConvertTo-Json -Compress
}
