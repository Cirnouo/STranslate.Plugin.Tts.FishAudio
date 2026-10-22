using NAudio.Wave;
using STranslate.Plugin.Tts.FishAudio.Playback;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

internal sealed class DiagnosticPcmCapture(Action<int> reserve, Func<double> elapsed, DateTimeOffset beganUtc)
{
    private readonly object _gate = new();
    private readonly List<byte[]> _parts = [];
    private readonly List<ProviderRead> _reads = [];
    private readonly List<DevicePosition> _positions = [];
    private long _bytes;
    internal int ReadCount { get { lock(_gate) return _reads.Count; } }
    internal void RecordRead(byte[] bytes, int offset, int count, long cursor, int actual, long tail, double durationMs)
    {
        lock(_gate)
        {
            if(_reads.Count >= 5000 || actual < 0 || actual > count) throw new CaptureLimitException();
            reserve(count);
            var time = elapsed();
            _parts.Add(bytes.AsSpan(offset,count).ToArray());
            _reads.Add(new(_reads.Count,time,beganUtc.AddMilliseconds(time),_bytes,cursor,count,count,actual,count-actual,tail,durationMs));
            _bytes += count;
        }
    }
    internal void Position(long bytes, string origin)
    {
        lock(_gate)
        {
            if(_positions.Count >= 15000) throw new CaptureLimitException();
            var time = elapsed();
            _positions.Add(new(_positions.Count,time,beganUtc.AddMilliseconds(time),bytes,origin));
        }
    }
    internal DiagnosticSnapshot Snapshot()
    {
        lock(_gate)
        {
            var bytes = new byte[checked((int)_bytes)];
            var offset=0;
            foreach(var part in _parts) { part.CopyTo(bytes,offset); offset+=part.Length; }
            return new(bytes,_reads.ToArray(),_positions.ToArray());
        }
    }
}

internal readonly record struct ProviderRead(int Sequence,double ElapsedMs,DateTimeOffset Utc,long RenderedByteOffset,
    long DeviceSourceCursorBefore,int RequestedBytes,int ReturnedBytes,int RealBytes,int PaddingBytes,long LastRealPcmEnd,double ReadDurationMs);
internal readonly record struct DevicePosition(int Sequence,double ElapsedMs,DateTimeOffset Utc,long Bytes,string Origin);
internal sealed record DiagnosticSnapshot(byte[] Rendered,ProviderRead[] Reads,DevicePosition[] Positions)
{
    internal PcmComparison Compare(byte[] upstream)
    {
        using var realHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long source=0;
        long? mismatch=null;
        foreach(var read in Reads)
        {
            var real=Rendered.AsSpan(checked((int)read.RenderedByteOffset),read.RealBytes);
            realHash.AppendData(real);
            var available=(int)Math.Min(real.Length,Math.Max(0,upstream.LongLength-source));
            if(mismatch is null)
            {
                for(var i=0;i<available;i++) if(real[i]!=upstream[checked((int)source)+i]) { mismatch=source+i; break; }
                if(mismatch is null && available<real.Length) mismatch=source+available;
            }
            source+=real.Length;
        }
        var hash=Convert.ToHexString(realHash.GetHashAndReset()).ToLowerInvariant();
        var upstreamHash=Convert.ToHexString(SHA256.HashData(upstream)).ToLowerInvariant();
        var padding=Reads.Sum(read=>(long)read.PaddingBytes);
        return new(source==upstream.LongLength && mismatch is null,upstream.LongLength,source,padding,mismatch,
            upstreamHash,hash,Reads.Count(read=>read.PaddingBytes>0),
            Reads.LastOrDefault().LastRealPcmEnd,Positions.Length==0?0:Positions.Max(position=>position.Bytes));
    }
}
internal readonly record struct PcmComparison(bool ExactlyEqual,long UpstreamBytes,long RenderedRealBytes,long InsertedPaddingBytes,
    long? FirstMismatchSourceByteOffset,string UpstreamSha256,string RenderedRealSha256,int ReadsWithPadding,
    long LastRealPcmEnd,long HighestDevicePosition);

internal sealed class RecordingWaveProvider(StreamingPcmBuffer inner, DiagnosticPcmCapture capture) : IWaveProvider
{
    private long _cursor;
    public WaveFormat WaveFormat => inner.WaveFormat;
    public int Read(byte[] bytes, int offset, int count)
    {
        var before=Stopwatch.GetTimestamp();
        var returned = inner.Read(bytes, offset, count);
        if(returned!=count) throw new InvalidDataException("Unexpected production ring return count.");
        var tail=inner.LastRealPcmEnd;
        var actual=checked((int)Math.Clamp(tail-_cursor,0,returned));
        capture.RecordRead(bytes, offset, returned, _cursor, actual, tail,Stopwatch.GetElapsedTime(before).TotalMilliseconds);
        _cursor += returned;
        return returned;
    }
}
