using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using STranslate.Plugin.Tts.FishAudio.Streaming;

internal static class Program
{
    private const int WarmupPerLength = 1000;
    private const int RepetitionsPerLength = 10000;
    private static readonly int[] Lengths = [1000, 1000000, 1000000000];
    private const string First100 = "The morning light falls softly across the quiet room. A small clock marks the steady passage of time";
    private static readonly string ExpectedPrefix = First100[..(First100.IndexOf('.') + 1)];

    public static int Main(string[] args)
    {
        if (args.Length != 1 || First100.Length != 100 || ExpectedPrefix.Length != 53) return 2;
        var outputDirectory = Path.GetFullPath(args[0]);
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory)) return 2;
        Directory.CreateDirectory(outputDirectory);
        var beganUtc = DateTimeOffset.UtcNow;
        var sentinelVerified = false;
        try { _ = new VirtualSource(1000)[100]; }
        catch (UnreadTailException) { sentinelVerified = true; }
        if (!sentinelVerified) throw new InvalidOperationException("Sentinel self-check failed.");

        var results = new List<object>();
        var samplesCsv = new StringBuilder("virtual_length,repetition,readnext_allocated_bytes,setup_plus_readnext_allocated_bytes,readnext_elapsed_ticks\n");
        foreach (var length in Lengths)
        {
            for (var iteration = 0; iteration < WarmupPerLength; iteration++) _ = Measure(length);
            var samples = new Sample[RepetitionsPerLength];
            for (var iteration = 0; iteration < samples.Length; iteration++) samples[iteration] = Measure(length);
            foreach (var (sample, iteration) in samples.Select((sample, iteration) => (sample, iteration)))
                samplesCsv.Append(FormattableString.Invariant($"{length},{iteration},{sample.AllocatedBytes},{sample.SetupPlusReadAllocatedBytes},{sample.ElapsedTicks}\n"));
            results.Add(new
            {
                virtualLengthUtf16 = length,
                warmupIterations = WarmupPerLength,
                measuredIterations = samples.Length,
                indexedReadsPerIteration = 100,
                highestIndexRead = 99,
                firstForbiddenIndex = 100,
                prefixUtf16Length = ExpectedPrefix.Length,
                prefixSha256 = Hash(Encoding.UTF8.GetBytes(ExpectedPrefix)),
                flushSafe = true,
                isFinal = false,
                readNextAllocatedBytes = Distribution(samples.Select(sample => (double)sample.AllocatedBytes)),
                setupPlusReadNextAllocatedBytes = Distribution(samples.Select(sample => (double)sample.SetupPlusReadAllocatedBytes)),
                readNextElapsedMicroseconds = Distribution(samples.Select(sample => sample.ElapsedTicks * 1000000.0 / Stopwatch.Frequency)),
            });
        }

        var sampleBytes = Encoding.UTF8.GetBytes(samplesCsv.ToString());
        File.WriteAllBytes(Path.Combine(outputDirectory, "samples.csv"), sampleBytes);
        var pluginBytes = File.ReadAllBytes(typeof(StreamingTextReader).Assembly.Location);
        var result = new
        {
            schema = 1,
            classification = "offline-first-prefix-microbenchmark",
            outcome = "passed",
            beganUtc,
            completedUtc = DateTimeOffset.UtcNow,
            runtime = RuntimeInformation.FrameworkDescription,
            operatingSystem = OperatingSystem.IsWindows() ? "Windows" : "other",
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            serverGc = GCSettings.IsServerGC,
            stopwatchFrequency = Stopwatch.Frequency,
            tieredCompilation = false,
            productionAssembly = new { name = typeof(StreamingTextReader).Assembly.GetName().Name,
                bytes = pluginBytes.Length, sha256 = Hash(pluginBytes) },
            scope = "One actual production StreamingTextReader.ReadNext(100), with fresh reader/source setup excluded from primary timing and allocation and included in the secondary allocation metric.",
            source = new { kind = "IStreamingTextSource virtual length", first100 = First100, expectedPrefix = ExpectedPrefix,
                tailReadSentinelIndex = 100, sentinelSelfCheckPassed = sentinelVerified, wholeInputStringAllocated = false },
            limitations = new[] {
                "The caller owns any original input string; this benchmark does not include its allocation or whole-input validation.",
                "No network, decoder, queue, audio output, full-session memory, or device latency is measured.",
                "Timing distributions describe one local process and are not a latency guarantee or a PCM/MP3 comparison."
            },
            networkCalls = 0,
            audioDeviceOperations = 0,
            environmentCredentialReads = 0,
            results,
            samples = new { name = "samples.csv", bytes = sampleBytes.Length, sha256 = Hash(sampleBytes) },
        };
        File.WriteAllBytes(Path.Combine(outputDirectory, "results.json"), JsonSerializer.SerializeToUtf8Bytes(result,
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { outcome = "passed", lengths = Lengths, repetitionsPerLength = RepetitionsPerLength }));
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Sample Measure(int length)
    {
        var beforeSetup = GC.GetAllocatedBytesForCurrentThread();
        var source = new VirtualSource(length);
        var reader = new StreamingTextReader(source);
        var beforeRead = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        var chunk = reader.ReadNext(100);
        var stopped = Stopwatch.GetTimestamp();
        var afterRead = GC.GetAllocatedBytesForCurrentThread();
        if (source.ReadCount != 100 || source.HighestIndex != 99 || chunk.Text != ExpectedPrefix || !chunk.FlushSafe || chunk.IsFinal)
            throw new InvalidOperationException("First-prefix invariant failed.");
        return new Sample(afterRead - beforeRead, afterRead - beforeSetup, stopped - started);
    }

    private static object Distribution(IEnumerable<double> sequence)
    {
        var values = sequence.Order().ToArray();
        var mean = values.Average();
        double Percentile(double fraction) => values[(int)Math.Ceiling(fraction * values.Length) - 1];
        return new { count = values.Length, min = values[0], median = Percentile(0.5), p90 = Percentile(0.9),
            p95 = Percentile(0.95), p99 = Percentile(0.99), max = values[^1], mean,
            standardDeviation = Math.Sqrt(values.Average(value => (value - mean) * (value - mean))), percentileMethod = "nearest-rank" };
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private readonly record struct Sample(long AllocatedBytes, long SetupPlusReadAllocatedBytes, long ElapsedTicks);
    private sealed class UnreadTailException : Exception { }
    private sealed class VirtualSource(int length) : IStreamingTextSource
    {
        public int Length => length;
        internal int ReadCount { get; private set; }
        internal int HighestIndex { get; private set; } = -1;
        public char this[int index]
        {
            get
            {
                if (index < 0 || index >= 100) throw new UnreadTailException();
                ReadCount++;
                HighestIndex = Math.Max(HighestIndex, index);
                return First100[index];
            }
        }
    }
}
