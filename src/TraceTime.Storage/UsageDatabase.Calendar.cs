using TraceTime.Core;

namespace TraceTime.Storage;

public sealed partial class UsageDatabase
{
    public long HistoryRevision { get; private set; }
    public HashSet<DateOnly> RecordedDates(DateOnly first, DateOnly last, TimeZoneInfo zone)
    {
        if(last<first || last.DayNumber-first.DayNumber>370) throw new ArgumentException("日期范围须在一年内。");
        var (a,_)=ReportBuilder.Bounds(first,zone); var (_,b)=ReportBuilder.Bounds(last,zone);
        using var cmd=Command("SELECT start_ticks,end_ticks FROM usage WHERE end_ticks>start_ticks AND end_ticks>$a AND start_ticks<$b",null,("$a",a.UtcTicks),("$b",b.UtcTicks));
        using var reader=cmd.ExecuteReader(); var days=new HashSet<DateOnly>();
        while(reader.Read())
        {
            var start=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Stamp(Math.Max(a.UtcTicks,reader.GetInt64(0))),zone).DateTime);
            var end=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Stamp(Math.Min(b.UtcTicks,reader.GetInt64(1))-1),zone).DateTime);
            for(var day=start;day<=end;day=day.AddDays(1)) days.Add(day);
        }
        return days;
    }
}
