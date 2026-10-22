# Incremental decoding and output

Status: current Unreleased implementation. [StreamingAudioPlayer](../../STranslate.Plugin.Tts.FishAudio/Playback/StreamingAudioPlayer.cs) owns decoder, PCM ring, independent output pump and cleanup for one session. It does not use a full-response memory stream or temporary audio file. This contract is separate from [bounded whole-sample preview](voice-browsing-and-preview.md).

## Formats and continuing decoder state

[PcmStreamingDecoder](../../STranslate.Plugin.Tts.FishAudio/Playback/PcmStreamingDecoder.cs) interprets raw PCM as 44100 Hz, signed 16-bit little-endian mono. A single leftover byte carries across arbitrary audio-event/block boundaries; an odd final byte fails rather than being padded or discarded. This signedness/byte order is an engineering interpretation supported by [real response statistics](../evidences/2026-10-20/audio-analysis.json), not an explicit provider guarantee. The documented rate/depth/channel contract and its observation limits remain in the [audio reference](../references/audio-formats.md).

[IncrementalMp3Decoder](../../STranslate.Plugin.Tts.FishAudio/Playback/IncrementalMp3Decoder.cs) preserves one Windows ACM decompressor across the stream, including the codec's frame dependencies. It parses MPEG Layer III frames incrementally, accepts VBR bitrate changes while requiring stable sample rate/channel count, skips bounded leading ID3v2 metadata and accepts a final ID3v1 tag. Metadata larger than one MiB, corrupt/unsupported frame headers, data after the terminal tag, incomplete terminal metadata/frame and audio without a complete frame fail. Input fragments do not define codec boundaries. One decoded frame is yielded at a time, so a large encoded event cannot decode its full PCM expansion before pressure applies. Residual storage grows only to the current bounded header/frame; decoded output is bounded to 64 KiB per frame.

The player accepts nonempty aligned 16-bit PCM blocks and requires a stable `WaveFormat`. MP3 uses the actual decompressor output format; it does not reinterpret compressed bytes as PCM or force a mono/rate conversion. WAV/Opus are not exposed. No format failure triggers another synthesis.

## Start, ring and pressure

The first PCM block creates a five-second software ring in the decoder output format. The producer writes aligned slices of at most 16 KiB and advances only by the accepted count. At four seconds buffered it waits until three seconds or less; the ring's five-second capacity is an independent hard bound. Waiting, rather than dropping, preserves ordered real samples.

An independent 10 ms clock/device pump is installed before input production, including synchronously available large input. It starts output when any of these holds: at least 250 ms of real PCM is buffered; 100 ms has elapsed since first PCM; or decoding has completed with playable PCM. The 250 ms threshold is audio duration, not a mandatory wall-clock delay. A short prefix followed by a silent/blocked source therefore starts after 100 ms without needing another input event or provider finish.

The producer pauses at the startup threshold until the pump has initialized the output and `Play` has returned successfully. Cancellation/failure is rechecked before publishing playback start. The session's text-tail gate is released only by that successful output milestone. `FirstPcm` and `PlaybackStarted` remain separate observations.

## Output position, underflow and drain

[WaveOutStreamingAudioOutput](../../STranslate.Plugin.Tts.FishAudio/Playback/WaveOutStreamingAudioOutput.cs) uses NAudio.WinMM 2.3.0 `WaveOutEvent`, desired latency 200 ms and four buffers, retaining approximately 50 ms per device buffer. The additional nominal 100 ms device-queue margin is independent of the software startup conditions and total text length; it can add output queuing delay and is not an acoustic latency guarantee. Construction avoids capturing a WPF synchronization context for output-stop callbacks. The device adapter extends wrapping 32-bit WinMM byte positions into a continuing cursor. Unexpected stop, including one without an exception, signals failure to interrupt every waiting phase.

[StreamingPcmBuffer](../../STranslate.Plugin.Tts.FishAudio/Playback/StreamingPcmBuffer.cs) returns silence for an underflow so later real PCM can resume. The device/source cursor includes that silence, while the final real-PCM position is tracked separately. This silence policy concerns output underflow; it does not zero-pad truncated input samples. Ten seconds without device-position advance fails only while real PCM is outstanding; an empty source during upstream waiting does not itself trigger the device-progress timeout.

After decoder completion, the pump waits until the ring is empty and device position reaches the end of the last real PCM, including the position offset introduced by earlier underflow silence. Queue empty, decoder read-ahead and provider finish alone cannot establish drain. No separate resampler is introduced in this adapter.

Every owned output, decoder and cancellation source gets an independent cleanup attempt. An earlier playback failure is preserved if cleanup also fails; cleanup failure surfaces when no earlier failure exists. Cancellation interrupts blocked source enumeration, pressure, startup and drain. A source that ignores its token cannot keep output resources alive indefinitely; late input completion/disposal is observed separately. Player disposal is idempotent and the instance is single-use.

## Verification boundary

[Playback regressions](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/StreamingPlaybackSuite.cs) cover arbitrary PCM/MP3 splits, persistent ACM state, VBR/metadata, truncation, large-event pressure, short-prefix startup under silent input, exact byte order, underflow recovery, position wrap, drain, device failure and cleanup. The two reused MP3 fixtures remain unchanged; fixture success is not new provider evidence. Fake output positions establish the software contract. They do not establish coexistence with real application audio, removal/no-device behavior on every host or physical sound. Installed-host production streaming acceptance and live free-model output measurements require their own record under the [verification policy](../policies/streaming-validation.md).

Rationale: [DD-052](../adr/current/DD-052.md), [DD-061](../adr/current/DD-061.md).
