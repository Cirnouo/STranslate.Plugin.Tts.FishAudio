using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using NAudio.Wave;
using STranslate.Plugin.Tts.FishAudio.Playback;
using STranslate.Plugin.Tts.FishAudio.Streaming;

internal sealed class CaptureLimitException:Exception;
internal static class Program
{
    internal const int ReplayBytes=44100*2*15;
    internal const string SourceHash="3a7783c5ccb27cbebf977a5c75c6f954b1ec6bf50359882b808d584feb32397f";
    internal const string ProductionHash="e023d87d35745c422edc0aa310ce7e7802e039ac27d0db0bc898061ac9ca7c1f";
    internal static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static void ValidateLength(long length)
    {if(length<ReplayBytes || length%2!=0 || length>20*1024*1024)throw new InvalidDataException("Invalid raw PCM length.");}
    internal static byte[] LoadPcm(string path)
    {
        ValidateLength(new FileInfo(path).Length);
        var bytes=File.ReadAllBytes(path);
        if(Hash(bytes)!=SourceHash)throw new InvalidDataException("Expected saved capture PCM.");
        return bytes[..ReplayBytes];
    }
    internal static Arrival[] LoadPlan(string path)
    {
        if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException();
        using var document=JsonDocument.Parse(File.ReadAllBytes(path));
        var events=document.RootElement.EnumerateArray().ToArray();
        var play=events.Single(x=>x.GetProperty("kind").GetString()=="actualPlaybackCallbackEntry").GetProperty("elapsedMs").GetDouble();
        var plan=new List<Arrival>();var remaining=ReplayBytes;
        foreach(var item in events.Where(x=>x.GetProperty("kind").GetString()=="decodedIncomingEvent"
            && x.GetProperty("details").GetProperty("event").GetString()=="audio"))
        {
            var bytes=Math.Min(remaining,item.GetProperty("details").GetProperty("audioBytes").GetInt32());
            if(bytes<=0 || bytes%2!=0)throw new InvalidDataException();
            var due=plan.Count==0?0:Math.Max(0,item.GetProperty("elapsedMs").GetDouble()-play);
            plan.Add(new(due,bytes));remaining-=bytes;if(remaining==0)break;
        }
        if(remaining!=0 || plan.Count!=3 || plan[0].Bytes!=684032 || plan[1].Bytes!=557056 || plan[2].Bytes!=81912
            || Math.Abs(plan[1].DueAfterPlayMs-4201.2674)>.01 || Math.Abs(plan[2].DueAfterPlayMs-4354.5656)>.01)
            throw new InvalidDataException("Unexpected saved arrival plan.");
        return plan.ToArray();
    }
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if(Hash(File.ReadAllBytes(typeof(StreamingAudioPlayer).Assembly.Location))!=ProductionHash)throw new InvalidOperationException();
            if(args.Length==1 && args[0]=="--self-test")return await SelfTests.RunAsync();
            if(args.Length==3 && args[0]=="--dry-run")
            {
                var pcm=LoadPcm(args[1]);var plan=LoadPlan(args[2]);
                Console.WriteLine(JsonSerializer.Serialize(new {outcome="dry-run-passed",seconds=15,bytes=pcm.Length,pcmSha256=Hash(pcm),sourceSha256=SourceHash,plan,noNetwork=true,noCredentials=true,audioDevice=false}));return 0;
            }
            if(args.Length!=5 || args[0]!="--replay" || args[3] is not ("100x2" or "200x4"))return 2;
            var source=LoadPcm(args[1]);var arrivals=LoadPlan(args[2]);
            var outputPath=Path.GetFullPath(args[4]);
            if(Directory.Exists(outputPath)||File.Exists(outputPath))throw new IOException();
            var began=DateTimeOffset.UtcNow;var watch=Stopwatch.StartNew();
            var ledger=new Ledger(watch,began);
            var diagnostic=new DiagnosticPcmCapture(ledger.Reserve,()=>watch.Elapsed.TotalMilliseconds,began);
            using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ConsoleCancelEventHandler cancel=(_,eventArgs)=>{eventArgs.Cancel=true;cancellation.Cancel();};
            Console.CancelKeyPress+=cancel;
            var start=new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            string outcome;string? failureType=null;
            try
            {
                using var player=new StreamingAudioPlayer(()=>new RecordingOutput(CreateOutput(args[3]),diagnostic,ledger));
                await player.PlayAsync(Feed(source,arrivals,start,ledger,cancellation.Token),"pcm",()=>
                {ledger.Event("playback-started");start.TrySetResult(Stopwatch.GetTimestamp());},new StreamingActivity(TimeProvider.System),cancellation.Token);
                outcome="completed";
            }
            catch(Exception error){outcome=cancellation.IsCancellationRequested?"cancelled-or-deadline":"failed";failureType=error.GetType().FullName;}
            finally{Console.CancelKeyPress-=cancel;}
            var snapshot=diagnostic.Snapshot();var comparison=snapshot.Compare(source);
            Directory.CreateDirectory(outputPath);
            File.WriteAllBytes(Path.Combine(outputPath,"rendered-audio.pcm"),snapshot.Rendered);
            Write("provider-reads.json",snapshot.Reads);Write("device-positions.json",snapshot.Positions);Write("events.json",ledger.Events);
            Write("summary.json",new {outcome,failureType,configuration=args[3],durationSeconds=15,sourceSha256=SourceHash,
                replaySha256=Hash(source),productionSha256=ProductionHash,noNetwork=true,noCredentials=true,deadlineSeconds=30,
                format=new{sampleRate=44100,bits=16,channels=1,signed=true,endianness="little"},arrivals,comparison,
                instrumentation="Production StreamingAudioPlayer, PcmStreamingDecoder and StreamingPcmBuffer; provider wrapper captures before driver. One case per invocation. No acoustic loopback."});
            Console.WriteLine(JsonSerializer.Serialize(new {outcome,configuration=args[3],summaryPath=Path.Combine(outputPath,"summary.json"),comparison}));
            return outcome=="completed" && comparison.ExactlyEqual && comparison.HighestDevicePosition>=comparison.LastRealPcmEnd?0:1;
            void Write(string name,object value)=>File.WriteAllText(Path.Combine(outputPath,name),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error){Console.WriteLine(JsonSerializer.Serialize(new{outcome="refused-or-failed",failureType=error.GetType().FullName}));return 1;}
    }
    private static IStreamingAudioOutput CreateOutput(string mode)
    {
        if(mode=="100x2")return new WaveOutStreamingAudioOutput();
        var context=SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var wave=new WaveOutEvent{DesiredLatency=200,NumberOfBuffers=4};
            return new WaveOutStreamingAudioOutput(wave,wave.GetPosition);
        }
        finally{SynchronizationContext.SetSynchronizationContext(context);}
    }
    internal static async IAsyncEnumerable<ReadOnlyMemory<byte>> Feed(byte[] source,Arrival[] plan,TaskCompletionSource<long> started,
        Ledger ledger,[EnumeratorCancellation]CancellationToken token)
    {
        var offset=0;
        foreach(var arrival in plan)
        {
            if(offset>0)
            {
                var stamp=await started.Task.WaitAsync(token);
                var delay=TimeSpan.FromMilliseconds(arrival.DueAfterPlayMs)-Stopwatch.GetElapsedTime(stamp);
                if(delay>TimeSpan.Zero)await Task.Delay(delay,token);
            }
            ledger.Event("saved-audio-available",new{arrival.Bytes,arrival.DueAfterPlayMs,sourceOffset=offset});
            for(var left=arrival.Bytes;left>0;)
            {
                token.ThrowIfCancellationRequested();var count=Math.Min(16384,left);
                yield return source.AsMemory(offset,count);offset+=count;left-=count;
            }
        }
    }
}
internal readonly record struct Arrival(double DueAfterPlayMs,int Bytes);
internal sealed class Ledger(Stopwatch watch,DateTimeOffset began)
{
    private readonly object _gate=new();private int _bytes;
    internal readonly List<object> Events=[];
    internal void Reserve(int count){lock(_gate){if(count<0 || count>4*1024*1024-_bytes)throw new CaptureLimitException();_bytes+=count;}}
    internal void Event(string kind,object? details=null){lock(_gate){if(Events.Count<100)Events.Add(new{kind,elapsedMs=watch.Elapsed.TotalMilliseconds,utc=began+watch.Elapsed,details});}}
}
internal sealed class RecordingOutput(IStreamingAudioOutput inner,DiagnosticPcmCapture capture,Ledger ledger):IStreamingAudioOutput
{
    public CancellationToken FailureToken=>inner.FailureToken;
    public void Init(IWaveProvider provider){if(provider is not StreamingPcmBuffer ring)throw new InvalidDataException();inner.Init(new RecordingWaveProvider(ring,capture));capture.Position(inner.GetPlayedBytes(),"initial");ledger.Event("init");}
    public void Play(){ledger.Event("play-entry");inner.Play();ledger.Event("play-returned");}
    public long GetPlayedBytes(){var value=inner.GetPlayedBytes();capture.Position(value,"pump");return value;}
    public void ThrowIfFailed()=>inner.ThrowIfFailed();
    public void Stop(){try{ledger.Event("stop");}finally{inner.Stop();}}
    public void Dispose(){try{inner.Dispose();}finally{ledger.Event("disposed");}}
}
