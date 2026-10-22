using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NAudio.Wave;
using STranslate.Plugin.Tts.FishAudio.Playback;
using STranslate.Plugin.Tts.FishAudio.Streaming;

internal static class SelfTests
{
    internal static async Task<int> RunAsync()
    {
        foreach(var length in new long[]{0,2,Program.ReplayBytes-2,Program.ReplayBytes+1,21*1024*1024})
        {try{Program.ValidateLength(length);throw new InvalidOperationException("invalid-length-accepted");}catch(InvalidDataException){}}
        foreach(var cancel in new[]{false,true})
        {
            var output=new FakeOutput();using var token=new CancellationTokenSource(TimeSpan.FromMilliseconds(cancel?60:2000));
            var started=0;
            using var player=new StreamingAudioPlayer(()=>output);
            try
            {
                await player.PlayAsync(Source(cancel,token.Token),"pcm",()=>started++,new StreamingActivity(TimeProvider.System),token.Token);
                if(cancel)throw new InvalidOperationException("cancel-not-observed");
            }
            catch(OperationCanceledException)when(cancel){}
            if(output.Stops!=1||output.Disposes!=1||started!=1)throw new InvalidOperationException("cleanup-or-start-count");
        }
        Console.WriteLine(JsonSerializer.Serialize(new{outcome="self-test-passed",noNetwork=true,noCredentials=true,audioDevice=false,
            tests=new[]{"invalid-short-odd-oversize-input","actual-player-local-output-drain","cancellation-stop-dispose"}}));return 0;
    }
    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> Source(bool block,[EnumeratorCancellation]CancellationToken token)
    {yield return new byte[22050];if(block)await Task.Delay(Timeout.InfiniteTimeSpan,token);}
    private sealed class FakeOutput:IStreamingAudioOutput
    {
        private IWaveProvider? _provider;private long _played;internal int Stops,Disposes;
        public CancellationToken FailureToken=>CancellationToken.None;
        public void Init(IWaveProvider provider)=>_provider=provider;
        public void Play(){}
        public long GetPlayedBytes(){_provider!.Read(new byte[4410],0,4410);return _played+=4410;}
        public void ThrowIfFailed(){}
        public void Stop()=>Stops++;
        public void Dispose()=>Disposes++;
    }
}
