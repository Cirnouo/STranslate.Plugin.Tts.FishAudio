# Production streaming evidence: 2026-10-20

Three real Fish Audio WebSocket sessions exercised the built production `NativeStreamingTtsSession`, `FishAudioSocket`, `StreamingAudioPlayer`, production decoders, and Windows `WaveOutStreamingAudioOutput`. The harness wraps those concrete implementations to record their behavior; it does not replace the transport or audio device with doubles. Each invocation exited `0` after checking its expected outcome. See the [machine-readable archive index](manifest.json), [reusable harness](harness/README.md), and [separate offline prefix benchmark](prefix-benchmark/README.md).

Every live request selected exactly `s2.1-pro-free`, with no retry or fallback. The harness checked the exact model and UTC cutoff before connection and every send, and enforced the earlier of 120 seconds or `2026-12-01T00:00:00Z`. Request summaries retain the fixed endpoint and safe synthesis parameters; Authorization is `[REDACTED]`. No key, host settings file, signed URL, or built executable is published here.

The tested production DLL is identified in each request summary by SHA-256:

```text
c02a1c36ef7ee6ef15e9419d3e815e605cb8d072a318f00d0c79cb8530ccc8d7
```

This DLL was also used in the initial installed-host validation. The recordings below come from the isolated production-DLL harness and retain that original artifact identity. The separate [installed-host acceptance record](../streaming-host-2026-10-20.md) documents successful short-text MP3 and PCM playback through device drain, key editing/persistence, legacy format/bitrate preservation, and PCM persistence across restart. Its final PCM run uses a later DLL containing a UI cleanup-notification correction, whose hash is recorded there. Native streaming is the formal TTS path; no streaming enable toggle was tested or required. Long-text tail gating and cancellation remain isolated-harness evidence, not installed-host acceptance claims.

## Observed production sessions

Times below are seconds from the **native session's own monotonic activity start**. All eight stage slots are copied from the native completion observer. The two absent cancellation stages are expected and remain `null` in the original JSON.

| Native stage | [PCM long](cases/pcm-long/summary.json) | [MP3 long](cases/mp3-long/summary.json) | [PCM cancel](cases/pcm-cancel/summary.json) |
|---|---:|---:|---:|
| Request | 0.000 | 0.000 | 0.000 |
| Connected | 1.949 | 0.828 | 1.067 |
| PrefixSent | 2.008 | 0.890 | 1.131 |
| FirstAudio | 5.596 | 3.909 | 13.803 |
| FirstPcm | 5.598 | 3.925 | 13.805 |
| PlaybackStarted | 5.687 | 4.021 | 13.901 |
| Finish | 12.742 | 10.308 | not observed: cancelled |
| Drained | 22.628 | 21.512 | not observed: cancelled |

The harness also has its own stopwatch for indexed source reads, socket fragments/messages, and the actual WaveOut calls and callback. Its start precedes native session activity by approximately 9–12 ms in these runs. Compare timestamps within one clock. For example, PCM's native playback stage is `5687.2041 ms`, while the harness's callback entry is `5698.9841 ms`; that difference is clock origin and callback instrumentation, not a separately measured audio delay.

Both completed runs initialized and played the real WaveOut device, observed device-position progress, received a successful stop finish, and waited for the device to pass the real PCM tail. This is evidence of the production output lifecycle and drain accounting. No physical listening or acoustic-quality assessment is claimed.

| Case | Outcome | Raw audio bytes | Complete incoming messages / receive fragments | Highest observed device position |
|---|---|---:|---:|---:|
| PCM long | completed | 1,428,056 | 5 / 91 | 1,428,740 bytes |
| MP3 long | completed | 399,986 encoded MP3 bytes | 6 / 28 | 1,470,644 decoded/device bytes |
| PCM cancel | cancelled after actual playback callback | 286,720 | 1 / 18 | 0 bytes before immediate cancellation |

Device position includes output-buffer alignment or silence and is not identical to raw encoded audio length. In particular, MP3 device bytes describe decoded PCM. The cancellation was requested immediately after the production playback callback; `System.OperationCanceledException` is the expected result. WaveOut `Init`/`Play` returned, but no positive device position was sampled before cancellation. That case claims neither audible output nor drain completion.

## Prefix and tail ordering

The input is the same fixed neutral 215-character English text in all three cases. At the actual production playback callback, each indexed source had read exactly 100 characters, with highest index `99`; only the first 53-character sentence had been sent in one `text` event. No tail or `stop` event had been sent. The raw outgoing MessagePack and the callback snapshot corroborate those counters.

After playback started, the two successful long cases sent the remaining text in a second event, bringing the total to 215 characters, then sent one stop. The cancellation case kept the source at 100 reads and sent neither tail nor stop. Reading a 100-character window to choose a 53-character sentence is intentional bounded lookahead; the remainder of that window can be revisited after playback. Therefore the successful runs' total read count of 262 is not a count of distinct input characters.

The local gap from native FirstPcm to PlaybackStarted was approximately **89 ms for PCM** and **96 ms for MP3**. Cloud response time varied substantially, including the later cancellation run. These are single observations, not a controlled PCM/MP3 comparison or a latency guarantee. No format-speed ranking follows from them.

## Offline bounded-prefix measurement

The [prefix benchmark](prefix-benchmark/README.md) invokes the actual production `StreamingTextReader.ReadNext(100)` with virtual source lengths of 1,000, 1,000,000, and 1,000,000,000. A sentinel throws at index `100`; no whole virtual tail string is created. After 1,000 warmups per length, all 10,000 measured reads per length visited exactly indices 0–99 and returned the same 53-character prefix.

Every measured call allocated **600 bytes** for `ReadNext` and **912 bytes** including fresh reader/source setup. Median measured call times were 1.8, 1.7, and 1.7 microseconds respectively; the [raw samples](prefix-benchmark/samples.csv) and [full distributions](prefix-benchmark/results.json) retain outliers and measurement details. This supports constant additional work for this first-prefix operation as the unvisited tail length grows. It does not measure caller-owned input-string allocation, whole-input validation, network, decoding, full-session memory, or audio-device latency.

The repository's existing [streaming session test](../../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/StreamingSessionSuite.cs) separately holds the playback callback and verifies that the sender leaves the tail unread and withholds stop until playback begins. The [text-reader tests](../../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/StreamingTextSuite.cs) cover bounded Unicode windows and protected markup. The benchmark adds allocation and local timing measurements; it is classified separately from real API evidence.

## Capture integrity and limits

Each case directory is copied byte for byte from the completed recorder output. `summary.json` lists original/persisted artifact sizes and SHA-256 values. Headers were sanitized when recorded; no additional binary redaction was needed (`anyArtifactRedacted: false`). All retained incoming MessagePack files are complete WebSocket binary messages assembled from the documented receive fragments. No `-incomplete` file exists in these three captures. The cancelled audio is an intentionally incomplete synthesis prefix even though its one stored WebSocket message is complete.

`raw-audio.pcm` concatenates audio payloads without a WAV header; the request specifies 44.1 kHz PCM and the production decoder uses mono signed 16-bit little-endian samples. `raw-audio.mp3` concatenates the encoded MP3 audio payloads. The MessagePack files preserve server metadata and audio envelopes; outgoing MessagePack contains synthesis parameters and text, never the Authorization header. The combined raw message/audio budgets were 2,856,821, 800,734, and 573,789 bytes respectively, below the 8 MiB per-case bound.

The archive's [manifest](manifest.json) classifies every file and also hashes summaries and source. Run the offline verifier from the repository root:

```powershell
python docs/evidences/streaming-2026-10-20/verify.py
```

It checks the archive inventory, every case artifact size/hash, decoded MessagePack and concatenated audio, request-model/redaction policy, stage origin and outcome classification, callback/tail ordering, and benchmark samples/distributions. The repository's existing `.gitattributes` rule marks `docs/evidences/**` as `-text`, preserving captured bytes through checkouts. The verifier does not read keys or settings and makes no network or audio-device calls.
