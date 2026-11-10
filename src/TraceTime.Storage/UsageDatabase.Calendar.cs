using TraceTime.Core;

namespace TraceTime.Storage;

public sealed partial class UsageDatabase
{
    public long HistoryRevision { get; private set; }
    public void InvalidateHistory() => HistoryRevision++;
    public DateOnly? FindRecordedDay(DateOnly pivot, int direction, TimeZoneInfo zone)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var (start,end) = ReportBuilder.Bounds(pivot,zone);
        var edge = direction < 0 ? start.UtcTicks : end.UtcTicks;
        using var cmd = Command(direction < 0
            ? "SELECT MIN(end_ticks,$edge)-1 FROM usage WHERE start_ticks<$edge AND end_ticks>start_ticks ORDER BY end_ticks DESC LIMIT 1"
            : "SELECT MAX(start_ticks,$edge) FROM usage WHERE end_ticks>$edge AND end_ticks>start_ticks ORDER BY start_ticks LIMIT 1",null,("$edge",edge));
        return cmd.ExecuteScalar() is long ticks ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Stamp(ticks),zone).DateTime) : null;
    }
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
