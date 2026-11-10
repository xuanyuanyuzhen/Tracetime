using TraceTime.App;
using TraceTime.Core;

internal static class ChartResourceTests
{
    public static IEnumerable<(string Name, Action Test)> All => new (string, Action)[]
    {
        ("应用占比合并其余时双口径总量无损", () =>
        {
            var apps = Enumerable.Range(1, 12).Select(i => new AppUsage(new("share-" + i, "应用 " + i), TimeSpan.FromSeconds(i * 83 + 1), TimeSpan.FromSeconds(i * 17 + 2))).ToArray();
            foreach (var metric in new[] { false, true })
            {
                var shares = UsageDistribution.Build(apps, metric);
                Check(shares.Count == 6 && shares[^1].IsOther && shares[^1].AppCount == 7, "应用合并数量不正确");
                Check(shares.Sum(x => x.Foreground.Ticks) == apps.Sum(x => x.Foreground.Ticks), "前台时长丢失");
                Check(shares.Sum(x => x.WithoutIdle.Ticks) == apps.Sum(x => x.WithoutIdle.Ticks), "空闲后时长丢失");
            }
        }),
        ("应用占比随口径重新排序并处理零值和单应用", () =>
        {
            AppUsage[] apps = [new(new("A", "阅读"), TimeSpan.FromHours(3), TimeSpan.Zero), new(new("B", "编辑"), TimeSpan.FromHours(2), TimeSpan.FromHours(1))];
            Check(UsageDistribution.Build(apps, false)[0].Key == "A", "前台排序错误");
            var active = UsageDistribution.Build(apps, true);
            Check(active.Count == 1 && active[0].Key == "B" && active[0].WithoutIdle == TimeSpan.FromHours(1), "零值排除或单应用错误");
            Check(UsageDistribution.Build([], false).Count == 0 && UsageDistribution.Build([apps[0]], true).Count == 0, "空口径未返回空状态");
            var six = Enumerable.Range(1, 6).Select(i => new AppUsage(new(i.ToString(), "应用"), TimeSpan.FromSeconds(1), TimeSpan.Zero)).ToArray();
            Check(UsageDistribution.Build(six, false).All(x => !x.IsOther), "六个应用不应多余合并");
        }),
        ("CPU占用按逻辑处理器容量和稳定时间归一化", () =>
        {
            Check(ProcessResourceSampler.CalculateCpuPercent(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), 8) == 25, "多核归一化错误");
            Check(ProcessResourceSampler.CalculateCpuPercent(TimeSpan.Zero, TimeSpan.FromSeconds(2), 8) == 0, "空闲CPU错误");
            Check(ProcessResourceSampler.CalculateCpuPercent(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(2), 8) == 100, "CPU上限错误");
            Check(ProcessResourceSampler.CalculateCpuPercent(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(2), 8) is null, "计数倒退必须重建基线");
            Check(ProcessResourceSampler.CalculateCpuPercent(TimeSpan.Zero, TimeSpan.Zero, 8) is null, "零间隔不能产生数值");
        }),
        ("资源采样首帧与恢复基线不伪造CPU值", () =>
        {
            using var sampler = new ProcessResourceSampler();
            var first = sampler.Sample();
            Check(first.CpuPercent is null && first.WorkingSetBytes > 0, "首帧应等待CPU采样且有内存值");
            sampler.Reset();
            Check(sampler.Sample().CpuPercent is null, "恢复后不能沿用隐藏前CPU基线");
        })
    };

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
