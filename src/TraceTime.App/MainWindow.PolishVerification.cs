using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    internal async Task PreparePolishCaptureAsync(string path)
    {
        if(!_demo) throw new InvalidOperationException("仅演示模式允许隔离验证。");
        static void Check(bool value,string reason) {if(!value) throw new Exception(reason);}
        var root=Path.Combine(Path.GetDirectoryName(path)!,"polish-cache");
        var pixels=new byte[32*32*4]; for(int i=0;i<pixels.Length;i+=4){pixels[i]=96;pixels[i+1]=155;pixels[i+2]=36;pixels[i+3]=255;}
        var generated=BitmapSource.Create(32,32,96,96,PixelFormats.Bgra32,null,pixels,32*4); generated.Freeze();
        var calls=0; var app=new AppIdentity("qa-cache-app","缓存测试应用","C:\\qa\\example.exe");
        var cache=new AppIconCache(root,Dispatcher,_=>{Interlocked.Increment(ref calls);return generated;}); var one=cache.Get(app); var two=cache.Get(app);await cache.WhenReadyAsync();
        Check(ReferenceEquals(one,two)&&one.Icon is not null&&calls==1,"图标缓存未复用或图标未完成");
        Check(File.Exists(cache.CacheFile(app.Key)),"磁盘缓存未写入");
        var reopened=new AppIconCache(root,Dispatcher,_=>throw new Exception("不应重新提取"));var fromDisk=reopened.Get(app with {ExecutablePath=null});await reopened.WhenReadyAsync();Check(fromDisk.Icon is not null,"卸载或重启后不能读缓存");
        File.WriteAllText(cache.CacheFile("bad-cache"),"broken png"); var repaired=new AppIconCache(root,Dispatcher,_=>generated);var fixedIcon=repaired.Get(new("bad-cache","缓存修复"));await repaired.WhenReadyAsync();Check(fixedIcon.Icon is not null,"损坏图标未回退");
        var missing=reopened.Get(new("missing-icon","缺失应用"));await reopened.WhenReadyAsync();Check(missing.Icon is null && missing.Initial=="缺","缺失应用占位异常");
        for(int i=0;i<260;i++) cache.Get(new("bound-"+i,"图标")); await cache.WhenReadyAsync();Check(Directory.GetFiles(Path.GetDirectoryName(cache.CacheFile(app.Key))!,"*.png").Length<=256,"图标磁盘缓存无界");
        // A real executable resource verifies extraction without fabricating a third-party logo.
        var realApp=new AppIdentity("qa-local-exe","本机图标验证",Environment.ProcessPath);var real=_icons.Get(realApp);await _icons.WhenReadyAsync();Check(real.Icon is not null,"本机可执行资源图标读取失败");
        foreach(var item in _db.RecordedApps()) _icons.Get(item);await _icons.WhenReadyAsync();
        var badge=new AppIconView {Value=real};badge.Measure(new Size(28,28));badge.Arrange(new Rect(0,0,28,28));
        Check(ReferenceEquals(VisualChildren(badge).OfType<Image>().Single().Source,real.Icon),"图标控件未绑定实际资源");
        var legacy=Path.Combine(Path.GetDirectoryName(path)!,"legacy-ai.json");
        var bytes=Encoding.UTF8.GetBytes("synthetic-old-key");var encrypted=Convert.ToBase64String(UserSecret.Protect(bytes));CryptographicOperations.ZeroMemory(bytes);
        File.WriteAllText(legacy,System.Text.Json.JsonSerializer.Serialize(new{Endpoint="https://example.test/v1/chat/completions",Model="old-model",ProtectedKey=encrypted,Prompt="旧版自定义不应再生效",Anonymous=false}));
        var old=AiSettings.Load(legacy);Check(old.Model=="old-model"&&old.Key()=="synthetic-old-key"&&!old.Anonymous,"旧配置迁移丢失");old.Save(legacy);Check(!File.ReadAllText(legacy).Contains("Prompt"),"旧提示词继续保存");
        var config=new AiConfigWindow(old,legacy);config.Show();config.LocalPreset.IsChecked=true;config.ManualModel.Text="local-model";
        using(var modelsClient=new HttpClient(new UiMockHandler((request,token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"id\":\"local-model\"},{\"id\":\"other-model\"}]}")}))))
            await config.FetchModelsAsync(modelsClient);
        Check(config.ModelList.Items.Count==2&&config.SelectModelMode.IsChecked==true,"模型列表选项未呈现");
        config.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(config.Saved is not null && config.Saved.ProtectedKey=="","跨服务不应复用旧密钥");
        var before=File.ReadAllText(legacy);var cancelled=new AiConfigWindow(config.Saved!,legacy);cancelled.Show();cancelled.ManualModel.Text="unsaved";cancelled.Close();Check(File.ReadAllText(legacy)==before,"取消配置修改了持久化");
        var inflight=new AiConfigWindow(config.Saved!,legacy);inflight.Show();
        using(var delayed=new HttpClient(new UiMockHandler(async (_,token)=>{await Task.Delay(Timeout.Infinite,token);return new HttpResponseMessage(HttpStatusCode.OK);})))
        {
            var waiting=inflight.FetchModelsAsync(delayed);Check(!inflight.SaveButton.IsEnabled,"模型请求期间保存未锁定");
            inflight.AddressInput.Text="http://localhost:4321/v1";await waiting;
            Check(inflight.SaveButton.IsEnabled && inflight.Feedback.Text.Contains("取消"),"修改地址未取消模型请求");
        }
        inflight.Close();
        using(var calendarDb=new TraceTime.Storage.UsageDatabase(Path.Combine(Path.GetDirectoryName(path)!,"calendar-refresh","usage.db")))
        {
            var window=new MainWindow(calendarDb,null,true);window.Show();var day=DateOnly.FromDateTime(DateTime.Today);
            var (start,end)=ReportBuilder.Bounds(day,TimeZoneInfo.Local);
            window.DayPicker.IsDropDownOpen=true;window.UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            bool HasToday(){window._recordedCalendar.RefreshOpen();var popup=(Popup)window.DayPicker.Template.FindName("PART_Popup",window.DayPicker);return RecordedCalendar.GetHasRecord(VisualChildren(popup.Child).OfType<CalendarDayButton>().First(x=>x.DataContext is DateTime dt&&DateOnly.FromDateTime(dt)==day));}
            Check(!HasToday(),"空数据库日期错误高亮");calendarDb.Save(new([new("today","today",new("calendar-app","日期测试"),start.AddHours(1),start.AddHours(2))],[]));Check(HasToday(),"记录新增后日历未更新");
            calendarDb.DeleteRange(start,end);Check(!HasToday(),"删除后日历未刷新");window.CloseTransientViews();window.DisposeTray();window.Hide();
        }
        File.WriteAllText(Path.ChangeExtension(path,"polish.txt"),"PASS memory/disk icon reuse, missing/corrupt icon fallback, 256-file cap, executable extraction, legacy AI migration, internal prompt removal, model-list selection, cross-service key isolation and cancel. No real model-list network call.");
    }

    private void VerifyRecordedCalendar(DatePicker picker,DateOnly recorded,DateOnly empty)
    {
        picker.IsDropDownOpen=true;UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);_recordedCalendar.RefreshOpen();
        var popup=(Popup)picker.Template.FindName("PART_Popup",picker);var buttons=VisualChildren(popup.Child).OfType<CalendarDayButton>().ToArray();
        var yes=buttons.First(x=>x.DataContext is DateTime d && DateOnly.FromDateTime(d)==recorded);
        var no=buttons.First(x=>x.DataContext is DateTime d && DateOnly.FromDateTime(d)==empty);
        if(!RecordedCalendar.GetHasRecord(yes)||RecordedCalendar.GetHasRecord(no)||!no.IsEnabled) throw new Exception("有记录/空记录日期标记或可选性错误");
        picker.IsDropDownOpen=false;
    }
}
