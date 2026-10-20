# Offline first-prefix benchmark

This standalone project invokes the built production `StreamingTextReader.ReadNext(100)` through the existing test friend assembly. It makes no API calls, opens no audio device, and reads no credential environment variable or host settings. Its results are **offline measurements**, separate from the real API/device recordings in [the case index](../README.md).

The custom `IStreamingTextSource` reports lengths of 1,000, 1,000,000, and 1,000,000,000 UTF-16 units. It stores only the fixed first 100 ASCII characters; indexing `100` or beyond throws. The sentinel itself is checked once before measurements. It never constructs an input string of the virtual length. Every measured call must read exactly 100 characters, finish at index `99`, and return the same 53-character first sentence with `FlushSafe=true` and `IsFinal=false`.

Each length receives 1,000 warmup calls followed by 10,000 measured calls. `GC.GetAllocatedBytesForCurrentThread` brackets each `ReadNext` call. A secondary allocation measurement includes the freshly created source and reader; their setup is excluded from primary allocation and elapsed time. `Stopwatch.GetTimestamp` brackets the call and the raw counter frequency is recorded. Tiered compilation is disabled for this benchmark. Timing includes the two timestamp calls' measurement overhead and remains a local-process observation. Validation and CSV serialization occur outside the measured call.

| Virtual length | `ReadNext` allocation, min–max | Including reader/source setup, min–max | Median call time | p95 | p99 | Maximum |
|---:|---:|---:|---:|---:|---:|---:|
| 1,000 | 600–600 bytes | 912–912 bytes | 1.8 µs | 4.2 µs | 6.5 µs | 16.9 µs |
| 1,000,000 | 600–600 bytes | 912–912 bytes | 1.7 µs | 2.0 µs | 4.1 µs | 74.4 µs |
| 1,000,000,000 | 600–600 bytes | 912–912 bytes | 1.7 µs | 2.0 µs | 2.1 µs | 39.7 µs |

See [results.json](results.json) for the measured runtime, production-DLL SHA-256, full distributions and scope; [samples.csv](samples.csv) retains all 30,000 samples. Distribution percentiles use nearest rank. All three lengths used the same production DLL hash as the live recordings and the same prefix SHA-256.

The equal source-read counts and allocation sizes demonstrate constant additional first-prefix work for this fixed 100-character window despite the virtual tail growing. A real caller still owns its original string: its allocation, whole-input validation and any earlier processing are outside this measurement. The benchmark does not claim constant total session memory, benchmark the network/player, rank PCM against MP3, or promise a particular latency. Full-session tail gating is covered separately by the repository's `TailWaitsForSuccessfulPlaybackAndFinishWaitsForDrainAsync` test and by the actual callback snapshots in the three real cases.

To reproduce from the repository root, first obtain the current production DLL and full test runtime as described in the [harness instructions](../harness/README.md), then build this sibling project into its own output directory:

```powershell
dotnet build docs/evidences/streaming-2026-10-20/prefix-benchmark/PrefixBenchmark.csproj -c Release `
  "-p:ProductionPluginDir=$pluginDir" "-p:ProductionDependencyDir=$dependencyDir" --nologo
$benchmarkExe = Join-Path $repoRoot 'docs/evidences/streaming-2026-10-20/prefix-benchmark/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests.exe'
$newResults = Join-Path ([System.IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
& $benchmarkExe $newResults
```

Use a new output directory so the published original result is preserved. This project also uses `STranslate.Plugin.Tts.FishAudio.Tests` as its isolated friend assembly name and must not be installed in the host plugin folder. It references the actual DLL and does not copy implementation sources or use text-reader stubs.
