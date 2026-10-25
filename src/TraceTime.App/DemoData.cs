using System;
using TraceTime.Core;
using TraceTime.Storage;

namespace TraceTime.App;

internal static class DemoData
{
    // Explicit demo mode only, always isolated from the user's normal data directory.
    public static void Seed(UsageDatabase db)
    {
        var day=DateOnly.FromDateTime(DateTime.Now);
        var (start,end)=ReportBuilder.Bounds(day,TimeZoneInfo.Local);
        db.DeleteRange(start,end);
        var editor=new AppIdentity("demo-editor","Visual Studio Code");
        var browser=new AppIdentity("demo-browser","Microsoft Edge");
        var notes=new AppIdentity("demo-notes","Obsidian");
        var files=new AppIdentity("demo-files","文件资源管理器");
        Add(9,0,10,12,editor,35,48); Add(10,12,10,42,browser); Add(10,42,11,32,editor);
        Add(11,32,11,58,notes); Add(13,10,13,43,browser,12,25); Add(13,43,14,5,files);
        Add(14,5,15,0,editor); Add(15,0,15,28,notes); Add(15,28,16,12,browser,15,32);
        db.SaveGap(new("demo-gap",start.AddHours(12),start.AddHours(13),"已暂停"));
        var (edgeStart,edgeEnd)=ReportBuilder.Bounds(day.AddDays(-2),TimeZoneInfo.Local);
        db.DeleteRange(edgeStart,edgeEnd);
        for(var i=0;i<45;i++)
        {
            var scope="edge-"+i; var a=edgeStart.AddHours(9).AddMinutes(i*2); var b=a.AddMinutes(2);
            db.Save(new([new(scope,scope,i%2==0?editor:browser,a,b)],[new(scope,scope,a,b)]));
        }
        void Add(int h,int m,int eh,int em,AppIdentity app,int? idleStart=null,int? idleEnd=null)
        {
            var scope=Guid.NewGuid().ToString("N"); var a=start.AddHours(h).AddMinutes(m); var b=start.AddHours(eh).AddMinutes(em);
            db.Save(new([new(scope,scope,app,a,b)],idleStart is null?[]:[new(scope,scope,a.AddMinutes(idleStart.Value),a.AddMinutes(idleEnd!.Value))]));
        }
    }
}
