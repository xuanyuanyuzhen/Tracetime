using TraceTime.App.Windows;

internal static class StartupTests
{
    private const string OldPath = @"C:\Apps\旧版 0.4\TraceTime.exe";
    private const string CurrentPath = @"C:\Apps\新版 0.8.2\TraceTime.exe";
    private static readonly StartupEntry OldEntry = new($"\"{OldPath}\" --tray");
    private static readonly StartupEntry CurrentEntry = new($"\"{CurrentPath}\" --tray");
    private static StartupPreferences Service(Store store, StartupExecutable? old = null) =>
        new(store, CurrentPath, new(0,8,2,0), _ => old ?? new(true,new(0,4,0,0)));

    internal static IEnumerable<(string Name, Action Test)> All => new (string, Action)[]
    {
        ("升级保留已开启选择并迁移带空格中文路径", () => {
            var store = new Store(OldEntry); var service = Service(store);
            Check(service.IsEnabled && service.PointsElsewhere, "旧路径未识别为已开启");
            Check(service.Synchronize() == StartupMigration.Updated && store.Entry == CurrentEntry && store.Writes == 1, "未切换到新路径");
            Check(service.Synchronize() == StartupMigration.Current && store.Writes == 1, "同路径反复写入");
        }),
        ("未开启的自启动保持关闭且不查询旧程序", () => {
            var store = new Store(null); var service = new StartupPreferences(store,CurrentPath,new(0,8,2,0),_=>throw new Exception("不应查询文件"));
            Check(!service.IsEnabled && service.Synchronize() == StartupMigration.Disabled && store.Writes == 0, "自动开启了启动项");
        }),
        ("缺失旧EXE和同版本移动可接续启动项", () => {
            foreach (var metadata in new[] {new StartupExecutable(false,null), new(true,new Version(0,8,2,0))}) {
                var store = new Store(OldEntry);
                Check(Service(store,metadata).Synchronize() == StartupMigration.Updated && store.Entry == CurrentEntry, "移动后未迁移");
            }
        }),
        ("误开较旧版本不会自动抢回新版启动路径", () => {
            var store = new Store(OldEntry); var service = Service(store,new(true,new(0,9,0,0)));
            Check(service.Synchronize() == StartupMigration.NewerVersion && store.Entry == OldEntry && store.Writes == 0, "自动回退版本");
            service.Save(true,()=>{});
            Check(store.Entry == CurrentEntry, "无法通过用户显式保存切换版本");
        }),
        ("异常命令和无法确认身份的EXE不自动覆盖", () => {
            foreach (var entry in new StartupEntry[] {new("not a command"),new($"\"{OldPath}\" --tray --data-dir C:\\other"),new("\"TraceTime.exe\" --tray"),new("\"C:\\Apps\\Other.exe\" --tray"),new(OldEntry.Command,false)}) {
                var store = new Store(entry); var service = Service(store);
                Check(!service.IsEnabled && service.Synchronize() == StartupMigration.Unrecognized && store.Writes == 0, "未知命令被接管");
            }
            var owned = new Store(OldEntry);
            Check(Service(owned,new(true,null)).Synchronize() == StartupMigration.Unrecognized && owned.Writes == 0,"不明EXE被接管");
        }),
        ("演示独立数据验证参数及开发运行不读取或修改启动项", () => {
            Check(StartupPreferences.IsNormalLaunch([],true) && StartupPreferences.IsNormalLaunch(["--tray"],true), "正式启动未允许");
            foreach (var args in new[] {new[]{"--demo"},new[]{"--data-dir","C:\\qa"},new[]{"--tray","--smoke-seconds","8"},new[]{"--verify-flow"},new[]{"--capture","a.png"}})
                Check(!StartupPreferences.IsNormalLaunch(args,true),"隔离入口允许自动迁移");
            Check(!StartupPreferences.IsNormalLaunch([],false),"开发构建允许自动迁移");
            var store = new Store(OldEntry){FailRead=true};
            Check(Service(store).Synchronize(false) == StartupMigration.Skipped && store.Writes == 0,"跳过仍访问启动项");
        }),
        ("显式保存勾选更新路径取消删除旧路径", () => {
            var store = new Store(OldEntry); var service = Service(store); var saved = 0;
            service.Save(true,()=>saved++);
            Check(store.Entry == CurrentEntry && saved == 1, "已勾选但未重写旧路径");
            store.Entry=OldEntry; service.Save(false,()=>saved++);
            Check(store.Entry is null && saved == 2, "取消时未删除旧路径");
            service.Save(true,()=>saved++);
            Check(store.Entry == CurrentEntry && saved == 3, "用户无法首次开启");
        }),
        ("保存失败精确恢复原命令及原关闭状态", () => {
            foreach (var initial in new StartupEntry?[] {new("  " + OldEntry.Command + "  "),null,CurrentEntry})
            foreach (var enabled in new[] {true,false}) {
                var store = new Store(initial); var service = Service(store);
                ExpectFailure(()=>service.Save(enabled,()=>throw new IOException("模拟保存失败")));
                Check(store.Entry == initial,"失败后丢失原始启动命令");
            }
        }),
        ("启动项写入失败或并发变更不会伪报成功", () => {
            var denied = new Store(OldEntry){FailWrite=true}; var saved = false;
            ExpectFailure(()=>Service(denied).Synchronize());
            ExpectFailure(()=>Service(denied).Save(true,()=>saved=true));
            Check(denied.Entry == OldEntry && !saved,"注册失败后仍保存或更改原项");
            var changed = new Store(OldEntry);
            ExpectFailure(()=>Service(changed).Save(true,()=> {changed.Entry=null; throw new IOException("模拟外部删除");}));
            Check(changed.Entry is null,"回滚复活了用户刚删除的启动项");
        }),
        ("超长启动命令不覆盖原值及路径大小写不重复写", () => {
            var store = new Store(OldEntry);
            var service = new StartupPreferences(store,@"C:\"+new string('a',245)+@"\TraceTime.exe",new(0,8,2,0),_=>new(false,null));
            ExpectFailure(()=>service.Synchronize()); Check(store.Entry == OldEntry && store.Writes == 0,"写入过长命令");
            store.Entry=new(CurrentEntry.Command!.ToUpperInvariant().Replace("--TRAY","--tray"));
            Check(Service(store).Synchronize() == StartupMigration.Current && store.Writes == 0,"大小写造成重写");
        })
    };

    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void ExpectFailure(Action action)
    {
        try { action(); } catch (IOException) { return; } catch (InvalidOperationException) { return; }
        throw new Exception("预期失败但成功");
    }
    private sealed class Store(StartupEntry? entry) : IStartupEntryStore
    {
        internal StartupEntry? Entry = entry;
        internal int Writes;
        internal bool FailWrite, FailRead;
        public StartupEntry? Read() => FailRead ? throw new IOException("模拟读取失败") : Entry;
        public void Replace(StartupEntry? expected, StartupEntry? value)
        {
            if(FailWrite) throw new IOException("模拟注册写入失败");
            if(Entry != expected) throw new InvalidOperationException("模拟并发修改");
            Entry=value; Writes++;
        }
    }
}
