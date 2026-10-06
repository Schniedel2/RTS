using RTS;
using System.Text.Json;
internal static class ClientDiagnosticsChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(bool value,string text) { if(!value) throw new Exception("Client diagnostics: "+text); checks++; }
        var samples=new TimingSamples();
        JsonElement Snapshot(object value) => JsonSerializer.SerializeToElement(value);
        var empty=Snapshot(samples.Snapshot());
        Check(empty.GetProperty("Count").GetInt32()==0 && empty.GetProperty("Mean").GetDouble()==0,"empty stats stay finite");
        for(int i=1;i<=100;i++) samples.Add(i);
        var stats=Snapshot(samples.Snapshot());
        Check(stats.GetProperty("Mean").GetDouble()==50.5 && stats.GetProperty("P95").GetDouble()==95 && stats.GetProperty("P99").GetDouble()==99,"quantile and mean calculation");
        foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,-1d})
        { bool rejected=false; try {samples.Add(invalid);} catch(ArgumentOutOfRangeException){rejected=true;} Check(rejected,"invalid samples rejected"); }
        for(int i=0;i<100001;i++) samples.Add(0);
        Check(Snapshot(samples.Snapshot()).GetProperty("SampleCount").GetInt32()==100000 && samples.Count==100101,"sample storage bounded while total count grows");
        var metrics=new ClientRunDiagnostics();
        Parallel.For(0,1000,_=>{metrics.Wire(true,10);metrics.Wire(false,20);});
        var wire=Snapshot(metrics.Snapshot());
        Check(wire.GetProperty("sentBytes").GetInt64()==10000 && wire.GetProperty("receivedBytes").GetInt64()==20000,"concurrent transport counters exact");
        var id=Guid.NewGuid();metrics.Request(id);metrics.Request(id);metrics.Reply(id,true);metrics.Reply(id,true);
        Check(metrics.RequestCount==1 && metrics.Rejections==1 && metrics.Replies.Count==1,"duplicate requests and replies counted once");
        var army=Guid.NewGuid();
        metrics.Heartbeat(army,1,1);metrics.HeartbeatReply(army,2,1);
        Check(metrics.HeartbeatRoundTrip.Count==0,"stale heartbeat echo ignored");
        metrics.HeartbeatReply(army,1,1);metrics.HeartbeatReply(army,1,1);
        Check(metrics.HeartbeatRoundTrip.Count==1,"heartbeat echo counted once");
        string report=Path.GetTempFileName();
        try
        {
            metrics.Save(report);metrics.Save(report);
            using var saved=JsonDocument.Parse(File.ReadAllText(report));
            Check(saved.RootElement.GetProperty("metrics").GetProperty("heartbeatRoundTripMs").GetProperty("Count").GetInt32()==1 && !File.Exists(report+".tmp"),"report serializes and replaces atomically");
        }
        finally { File.Delete(report); }
        metrics.Frame(2,16,7,2);metrics.Frame(1,20,3,1);
        Check(metrics.MaximumInbox==7 && metrics.MaximumControllers==2,"peak inbox and shared controller count");
        bool previous=PerformanceMeasurements.Enabled;
        PerformanceMeasurements.Enabled=true; PerformanceMeasurements.Reset();
        using(PerformanceMeasurements.Measure("test")){}
        var first=PerformanceMeasurements.Snapshot();
        using(PerformanceMeasurements.Measure("test")){}
        Check(first[0].Calls==1 && PerformanceMeasurements.Snapshot()[0].Calls==2,"scope snapshots detached from live counters");
        PerformanceMeasurements.Reset();PerformanceMeasurements.Enabled=previous;
        return checks;
    }
}
