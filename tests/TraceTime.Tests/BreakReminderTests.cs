using TraceTime.Core;

internal static class BreakReminderTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);
    private static BreakReminderClock Clock(bool enabled = true)
    {
        var clock = new BreakReminderClock(); clock.Configure(enabled, S(300), S(0), ComputerUseState.Using); return clock;
    }
    private static void Use(BreakReminderClock clock, int from, int through)
    { for (var t = from; t <= through; t++) clock.Observe(S(t), ComputerUseState.Using); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static IEnumerable<(string Name, Action Test)> All => new (string, Action)[]
    {
        ("休息提醒默认关闭且无累计", () => { var c = Clock(false); Use(c, 1, 600); Check(c.Used == S(0) && !c.IsDue(true), "默认关闭仍提醒"); Check(!new BreakReminderSettings().Enabled, "新用户默认启用"); }),
        ("连续用机阈值、阻塞与展示去重", () => { var c = Clock(); Use(c, 1, 299); Check(!c.IsDue(true), "提前提醒"); Use(c,300,300); Check(c.IsDue(true) && !c.IsDue(false), "阈值/阻塞错误"); c.Presented(); Use(c,301,360); Check(!c.IsDue(true), "重复提醒"); c.ReleasePresentation(); Check(c.IsDue(true), "释放后不能提醒"); }),
        ("连续用机不受应用切换或键鼠空闲影响", () => { var c = Clock(); Use(c,1,600); Check(c.Used == S(600), "累计错误"); }),
        ("短锁屏保留累计且不补算离开", () => { var c = Clock(); Use(c,1,120); c.Observe(S(120),ComputerUseState.Away); c.Observe(S(300),ComputerUseState.Using); Use(c,301,360); Check(c.Used == S(180), "短离开错误"); }),
        ("锁屏休眠满五分钟重置且不会立即提醒", () => { var c = Clock(); Use(c,1,360); c.Presented(); c.Observe(S(360),ComputerUseState.Away); c.Observe(S(660),ComputerUseState.Using); Check(c.Used == S(0) && !c.Pending && !c.IsDue(true), "满五分钟没有重置"); Use(c,661,959); Check(!c.IsDue(true), "重置后提前提醒"); Use(c,960,960); Check(c.IsDue(true), "新一轮阈值错误"); }),
        ("暂停维护不重置也不补时", () => { var c = Clock(); Use(c,1,120); c.Observe(S(120),ComputerUseState.Paused); c.Observe(S(1000),ComputerUseState.Using); Use(c,1001,1060); Check(c.Used == S(180), "暂停维护被当成休息"); }),
        ("未知采样空档及倒退不补时不冒充休息", () => { var c = Clock(); Use(c,1,120); c.Observe(S(900),ComputerUseState.Using); Check(c.Used == S(120), "未知空档补计"); c.Observe(S(500),ComputerUseState.Using); Check(c.Used == S(120), "倒退改变累计"); }),
        ("主动休息提前结束保留满五分钟重置", () => { var c = Clock(); Use(c,1,120); c.BeginRest(S(120),ComputerUseState.Using); c.Resume(S(300),ComputerUseState.Using); Check(c.Used == S(120) && !c.ManualRest, "提前结束丢时长"); c.BeginRest(S(300),ComputerUseState.Using); c.Observe(S(600),ComputerUseState.Using); Check(c.Used == S(0) && c.ManualRest && !c.IsDue(true), "完整休息错误"); c.Resume(S(601),ComputerUseState.Using); Use(c,602,661); Check(c.Used == S(60), "恢复累计错误"); }),
        ("稍后提醒按五分钟使用累计且可多轮", () => { var c=Clock(); Use(c,1,300); c.Presented(); c.Snooze(); Use(c,301,599); Check(!c.IsDue(true),"稍后提前提醒"); Use(c,600,600); Check(c.IsDue(true),"稍后未提醒"); c.Presented(); c.Snooze(); Check(c.NextReminder==S(900),"重复稍后错误"); }),
        ("轻度通知按用户间隔再次提醒", () => { var c=Clock(); c.Configure(true,S(1800),S(0),ComputerUseState.Using); Use(c,1,1800); c.NotificationShown(); Check(c.NextReminder==S(3600),"通知未沿用用户间隔"); }),
        ("关闭再启用及配置修改重建计时", () => { var c=Clock(); Use(c,1,300); c.Configure(false,S(1800),S(300),ComputerUseState.Using); Use(c,301,600); Check(c.Used==S(0),"关闭仍累计"); c.Configure(true,S(1200),S(600),ComputerUseState.Using); Check(c.NextReminder==S(1200)&&c.Used==S(0),"配置未重建"); }),
        ("休息配置往返今日静默跨日与损坏回退", () => {
            var dir=Path.Combine(Path.GetTempPath(),"tracetime-break-"+Guid.NewGuid()); Directory.CreateDirectory(dir);
            try { var path=Path.Combine(dir,"break.json"); var day=new DateOnly(2026,11,10); var s=new BreakReminderSettings(true,45,ReminderLevel.Severe,day); s.Save(path); Check(BreakReminderSettings.Load(path)==s,"配置未恢复"); Check(s.IgnoredOn!=day.AddDays(1),"静默跨日不解除"); File.WriteAllText(path,"{broken"); Check(!BreakReminderSettings.Load(path).Enabled,"损坏配置启用"); }
            finally { Directory.Delete(dir,true); }
        }),
        ("休息间隔非法值拒绝", () => { foreach(var n in new[]{-1,0,4,241}) { try { new BreakReminderSettings(true,n).Validate(); throw new Exception("非法值被接受"); } catch(ArgumentException){} } })
    };
}
