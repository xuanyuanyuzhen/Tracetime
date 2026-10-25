using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TraceTime.Storage;
using Calendar=System.Windows.Controls.Calendar;

namespace TraceTime.App;

public sealed class RecordedCalendar
{
    public static readonly DependencyProperty HasRecordProperty=DependencyProperty.RegisterAttached("HasRecord",typeof(bool),typeof(RecordedCalendar),new PropertyMetadata(false));
    public static bool GetHasRecord(DependencyObject element)=>(bool)element.GetValue(HasRecordProperty);
    public static void SetHasRecord(DependencyObject element,bool value)=>element.SetValue(HasRecordProperty,value);
    private readonly UsageDatabase _db;
    private readonly List<(DatePicker Picker,Calendar Calendar)> _calendars=[];
    private readonly Dictionary<(DateOnly First,long Revision),HashSet<DateOnly>> _cache=new();
    internal RecordedCalendar(UsageDatabase db,params DatePicker[] pickers)
    {
        _db=db;
        foreach(var picker in pickers) picker.CalendarOpened+=(_,_)=>{
            picker.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{
                if(picker.Template.FindName("PART_Popup",picker) is not Popup {Child:DependencyObject root}) return;
                var calendar=Descendants(root).OfType<Calendar>().FirstOrDefault() ?? root as Calendar;
                if(calendar is null) return;
                if(!_calendars.Any(x=>x.Calendar==calendar))
                {
                    _calendars.Add((picker,calendar));
                    calendar.DisplayDateChanged+=(_,_)=>Refresh(calendar);
                    calendar.DisplayModeChanged+=(_,_)=>calendar.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>Refresh(calendar)));
                }
                Refresh(calendar);
            }));
        };
    }
    internal void RefreshOpen() { foreach(var entry in _calendars.Where(x=>x.Picker.IsDropDownOpen)) Refresh(entry.Calendar); }
    private void Refresh(Calendar calendar)
    {
        if(calendar.DisplayMode!=CalendarMode.Month) return;
        calendar.UpdateLayout();
        var visible=Descendants(calendar).OfType<CalendarDayButton>().Where(x=>x.DataContext is DateTime).ToArray();
        if(visible.Length==0) return;
        var first=DateOnly.FromDateTime(visible.Min(x=>(DateTime)x.DataContext)); var last=DateOnly.FromDateTime(visible.Max(x=>(DateTime)x.DataContext));
        try
        {
            var key=(first,_db.HistoryRevision);
            if(!_cache.TryGetValue(key,out var dates)) { _cache.Clear(); dates=_db.RecordedDates(first,last,TimeZoneInfo.Local); _cache[key]=dates; }
            foreach(var button in visible)
            {
                var date=DateOnly.FromDateTime((DateTime)button.DataContext); var recorded=dates.Contains(date);
                SetHasRecord(button,recorded);
                var label=$"{date:yyyy年M月d日} · {(recorded?"有使用记录":"无使用记录")}";
                button.ToolTip=label; AutomationProperties.SetHelpText(button,label);
            }
        }
        catch { foreach(var button in visible) { SetHasRecord(button,false); button.ToolTip="记录状态暂不可用，仍可选择日期。"; } }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i); yield return child; foreach(var item in Descendants(child)) yield return item; }
    }
}
