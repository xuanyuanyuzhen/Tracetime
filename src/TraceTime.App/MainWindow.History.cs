using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using TraceTime.Core;
using TraceTime.Storage;

namespace TraceTime.App;

public partial class MainWindow
{
    private bool _historyBusy;
    internal bool HistoryBusy => _historyBusy;
    private string BackupFolder => Path.Combine(Path.GetDirectoryName(_db.FilePath)!,"backups");
    private void SetHistoryBusy(bool busy,string text)
    {
        _historyBusy=busy;HistoryActions.IsEnabled=!busy;SettingsPanel.IsEnabled=!busy;PauseButton.IsEnabled=!busy&&!_demo;
        HistoryFeedback.Text=text;
        if(!busy)RefreshReport();
    }
    private void RefreshHistoryStorage()
    {
        var counts=_db.HistoryCounts();
        long Size(string file)=>File.Exists(file)?new FileInfo(file).Length:0;
        static string Format(long bytes)=>bytes<1024*1024?$"{bytes/1024d:0.0} KiB":$"{bytes/1024d/1024:0.00} MiB";
        HistoryStorageText.Text=$"数据库 {Format(Size(_db.FilePath))} · 写入日志 {Format(Size(_db.FilePath+"-wal"))} · {counts.Usage:N0} 个应用时间片\n默认保留全部记录，不自动删除；归档后移除记录会留下可复用空间，文件不一定立即变小。";
        ArchiveMonth.SelectedDate ??= DateTime.Today.AddMonths(-1);
        ArchiveMonth.DisplayDateEnd=DateTime.Today;
    }
    private string? ChooseArchiveDestination(string fileName)
    {
        try
        {
            Directory.CreateDirectory(BackupFolder);
            var dialog=new SaveFileDialog {Filter="TraceTime 历史归档 (*.ttarchive)|*.ttarchive",DefaultExt=".ttarchive",AddExtension=true,OverwritePrompt=true,InitialDirectory=BackupFolder,FileName=fileName};
            return dialog.ShowDialog(this)==true?dialog.FileName:null;
        }
        catch(Exception ex){HistoryFeedback.Text="无法选择备份位置："+ex.Message;return null;}
    }
    private async void BackupHistory(object sender,RoutedEventArgs e)
    {
        if(_historyBusy)return;
        var path=ChooseArchiveDestination($"TraceTime-history-{DateTime.Now:yyyyMMdd-HHmmss}.ttarchive");if(path is null)return;
        SetHistoryBusy(true,"正在创建一致性快照并检查归档，记录继续进行…");
        try
        {
            _tracker?.Flush();if(_tracker?.Error is { } error)throw new InvalidOperationException(error);
            var archive=await Task.Run(()=>UsageDatabase.CreateHistoryArchive(_db.FilePath,path,TimeZoneInfo.Local));
            HistoryFeedback.Text=$"已备份 {archive.Records:N0} 条历史区间，原记录保留。\n{path}";
        }
        catch(Exception ex) {HistoryFeedback.Text="备份未完成："+ex.Message;}
        finally {SetHistoryBusy(false,HistoryFeedback.Text);RefreshHistoryStorage();}
    }
    private async void ArchiveHistoryMonth(object sender,RoutedEventArgs e)
    {
        if(_historyBusy || ArchiveMonth.SelectedDate is not { } selected)return;
        var month=new DateOnly(selected.Year,selected.Month,1);var remove=RemoveArchivedMonth.IsChecked==true;
        if(remove && month>=new DateOnly(DateTime.Today.Year,DateTime.Today.Month,1)) {HistoryFeedback.Text="归档后移除只适用于已经结束的月份。当前月可以归档并保留。";return;}
        var path=ChooseArchiveDestination($"TraceTime-{month:yyyy-MM}.ttarchive");if(path is null)return;
        SetHistoryBusy(true,"正在归档并验证所选月份…");
        var maintaining=false;
        try
        {
            _tracker?.Flush();if(_tracker?.Error is { } error)throw new InvalidOperationException(error);
            var archive=await Task.Run(()=>UsageDatabase.CreateHistoryArchive(_db.FilePath,path,TimeZoneInfo.Local,month));
            HistoryFeedback.Text=$"已归档 {month:yyyy-MM}，{archive.Records:N0} 条区间，原记录保留。\n{path}";
            if(remove && MessageBox.Show(this,$"归档已通过完整性检查：\n{path}\n\n现在从当前数据库移除 {month:yyyy-MM} 的记录吗？以后可通过“恢复历史归档”找回。其他月份和规则保留。","归档后移除",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes)
            {
                using var archiveLease = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                var rechecked=await Task.Run(()=>UsageDatabase.InspectHistoryArchive(path));
                if(rechecked.Sha256!=archive.Sha256)throw new InvalidOperationException("归档已变化，未移除原记录。");
                _tracker?.BeginMaintenance();maintaining=true;
                await Task.Run(()=> {using var db=new UsageDatabase(_db.FilePath);db.DeleteRange(archive.Start,archive.End,TimeZoneInfo.Local);});
                RefreshAfterHistoryChange();HistoryFeedback.Text=$"已归档并移除 {month:yyyy-MM} 的当前记录。可恢复文件：\n{path}";
            }
        }
        catch(Exception ex) {HistoryFeedback.Text="归档操作未完成："+ex.Message;}
        finally {if(maintaining)_tracker?.EndMaintenance();SetHistoryBusy(false,HistoryFeedback.Text);RefreshHistoryStorage();}
    }
    private static string ArchiveSummary(HistoryArchiveInfo info)
    {
        // Display the original archive's zone; restore always uses its exact UTC interval.
        TimeZoneInfo zone;try {zone=TimeZoneInfo.FindSystemTimeZoneById(info.Zone);}catch {zone=TimeZoneInfo.Utc;}
        return $"{TimeZoneInfo.ConvertTime(info.Start,zone):yyyy-MM-dd HH:mm} 至 {TimeZoneInfo.ConvertTime(info.End,zone):yyyy-MM-dd HH:mm}（结束时间不含，{zone.Id}）\n应用时间片 {info.UsageCount:N0}，空闲区间 {info.IdleCount:N0}，中断区间 {info.GapCount:N0}";
    }
    private async void RestoreHistory(object sender,RoutedEventArgs e)
    {
        if(_historyBusy)return;
        var dialog=new OpenFileDialog {Filter="TraceTime 历史归档 (*.ttarchive)|*.ttarchive",CheckFileExists=true};if(dialog.ShowDialog(this)!=true)return;
        SetHistoryBusy(true,"正在检查归档…");var maintaining=false;var restored=false;string? safetyPath=null;
        try
        {
            var archive=await Task.Run(()=>UsageDatabase.InspectHistoryArchive(dialog.FileName));
            if(MessageBox.Show(this,$"{ArchiveSummary(archive)}\n\n恢复会替换该范围内现有记录，不会叠加计时。先自动保存恢复前备份，失败则保留原记录。规则、外观和账号配置不变。为避免旧记录触发通知，恢复后今日提醒全部静默，可手动恢复。\n\n继续恢复吗？","恢复历史归档",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
            _tracker?.BeginMaintenance();maintaining=true;_emailService.CancelActive();CloseLimitAlert();
            Directory.CreateDirectory(BackupFolder);var safety=Path.Combine(BackupFolder,$"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.ttarchive");
            safetyPath=safety;
            HistoryFeedback.Text="正在备份恢复前数据并恢复历史，暂缓采集…";
            var today=DateOnly.FromDateTime(DateTime.Today);
            var result=await Task.Run(()=>{using var db=new UsageDatabase(_db.FilePath);return db.RestoreHistoryArchive(archive.Path,archive.Sha256,safety,today,TimeZoneInfo.Local);});
            restored=true;
            RefreshAfterHistoryChange();HistoryFeedback.Text=$"历史已恢复，今日提醒全部静默。恢复前备份：\n{safety}";
            if(result.CleanupWarning is { } warning)HistoryFeedback.Text+="\n"+warning;
        }
        catch(Exception ex){HistoryFeedback.Text=restored?$"历史已恢复，但界面刷新失败：{ex.Message}\n请重新打开应用。恢复前备份：{safetyPath}":"未能恢复，原记录保留："+ex.Message;}
        finally {if(maintaining)_tracker?.EndMaintenance();SetHistoryBusy(false,HistoryFeedback.Text);RefreshHistoryStorage();}
    }
    private void RefreshAfterHistoryChange()
    {
        _db.InvalidateHistory();_knownLimitApps.Clear();RefreshRecordedApps(this,new());RefreshReport();RenderLimitProgress();_recordedCalendar.RefreshOpen();
        // Old analysis/AI previews refer to the pre-maintenance history.
        _aiCancellation?.Cancel();InvalidateAiPreview();_analysis=null;_lastAiOutput="";_lastAiScope="";AiResult.Text="";
    }
    private void OpenBackups(object sender,RoutedEventArgs e)
    {
        try {Directory.CreateDirectory(BackupFolder);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",$"\"{BackupFolder}\""){UseShellExecute=true});}
        catch(Exception ex){HistoryFeedback.Text="无法打开备份目录："+ex.Message;}
    }
}
