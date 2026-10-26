# Formal TTS and account credit

Status: current Unreleased implementation, SDK 1.0.15. [Main](../../STranslate.Plugin.Tts.FishAudio/Main.cs) composes formal playback; [NativeStreamingTtsSession](../../STranslate.Plugin.Tts.FishAudio/Streaming/NativeStreamingTtsSession.cs) owns one synthesis session. Credit remains a host HTTP operation under [FishAudioApi](../../STranslate.Plugin.Tts.FishAudio/FishAudio/FishAudioApi.cs).

## Entry and operation ownership

Formal TTS rejects blank text, then performs network → non-empty API Key → API Key shape preflight. Accepted shapes are 32 lowercase hex characters or `sk-fish-` plus 43 ASCII letters/digits/underscore/hyphen. The supplied key is forwarded unchanged as a Bearer credential. Shape validation does not authenticate it, and no remote validation request or applied-key state precedes synthesis. Native streaming uses the system proxy without plugin proxy settings.

An operation captures its initialization's Context, detached Settings snapshot, optional ViewModel and lifetime token before its first await. Its immutable `SynthesisRequest` class holds exactly that request's synthesis values, including the API Key; it contains no proxy mode or address. Later settings edits cannot change a running session. Snapshot capture uses the short settings-object publication lock, so a host load blocked on the persistence gate does not block capture of the last published initialization. See [lifecycle](architecture-and-lifecycle.md).

Only one formal playback may own output at a time. A newer accepted operation cancels its predecessor and waits for the predecessor's completion, including cleanup, before creating its own session. A third overlapping call cannot overtake that retirement chain. Caller cancellation, supersession, initialization retirement and plugin disposal interrupt the owned session. Intentional cancellation propagates as `TaskCanceledException`, which the host treats as quiet stop; it does not produce a generic error snackbar.

The optional captured ViewModel holds counted API Key activity through session playback and drain. Its corresponding activity ends in `finally`; an old operation never releases activity on a replacement ViewModel. Successful session completion triggers a silent credit refresh only through the captured ViewModel. Disposed ViewModels reject late refresh work.

API Key and credit activity counts change immediately under their state gate. Their entire versioned observable publisher runs through the ViewModel's UI invoker, outside that gate. Queued publication reads the newest desired state, so an earlier background End cannot unlock a newer Begin. Bound command notifications therefore retain WPF dispatcher ownership when native playback resumes or cleans up on a worker. The [real WPF dispatcher regressions](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/Lifecycle/OperationActivityDispatcherSuite.cs) cover timeout classification, quiet cancellation and queued retirement.

## Synthesis and playback

The current path is one authenticated native `wss://api.fish.audio/v1/tts/live` session. `start.request.text` is empty. Bounded text events and flushes feed the session, while an independent receive loop extracts ordered audio events. The unread tail after the first safe readable prefix waits until the local output's actual `Play` call succeeds. PCM and MP3 feed a continuing local decoder/output pipeline; formal playback does not materialize a full audio response or invoke host `AudioPlayer.PlayAsync(byte[])`.

See the [session contract](streaming-session.md), [text contract](streaming-text.md) and [audio contract](streaming-playback.md) for wire messages, bounds, deadlines, markup limits and completion semantics. There is no synthesis retry, model fallback, HTTP fallback, SSE path or connection reuse. The retained HTTP request builder supports tests/reference compatibility; it is not the formal playback route.

Failures publish a fixed localized streaming failure or timeout message. Logs contain stage/elapsed software observations and safe exception type names. They do not include exception contents, provider error text, the API Key, request text or network credentials. These software milestones do not establish audible first-word latency.

## Credit and evidence

Credit uses `GET /wallet/self/api-credit`, a Bearer credential, the same preflight and a fifteen-second timeout. The response credit field is deserialized as a string. Manual refresh displays request latency for four seconds and interactive errors. Startup refresh is non-blocking and silent; post-TTS refresh is silent. Results apply only while their key, ViewModel and initialization/credit cycle remain current. Overlapping activity must not unlock input early.

A random/default voice does not establish free synthesis. Provider deduction timing and a temporarily unchanged balance cannot establish cost. [Provider probes](../evidences/2026-10-20/README.md) demonstrate real free-model duplex generation; they are separate from production plugin playback. No current production first-audible-word measurement or paid Drama 3 test is asserted. Follow the [API evidence policy](../policies/api-evidence.md) for authorized real runs.

[Production-DLL evidence](../evidences/streaming-2026-10-20/README.md) records long PCM/MP3 sessions, output-callback tail gating, intentional cancellation and a separate bounded-prefix benchmark. [Installed-host acceptance](../evidences/streaming-host-2026-10-20.md) records short PCM/MP3 playback through device drain, settings persistence, and the preserved initial connection/cleanup failure. These scopes remain distinct from physical listening and cloud latency guarantees.

Verification: [entry suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/Streaming/StreamingEntrySuite.cs), [session suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/Streaming/StreamingSessionSuite.cs), [playback suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/Playback/StreamingPlaybackSuite.cs), [credit suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/Settings/CreditRefreshSuite.cs), [startup credit suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/Lifecycle/StartupCreditSuite.cs). Rationale: [DD-009](../adr/current/DD-009.md), [DD-030](../adr/current/DD-030.md), [DD-039](../adr/current/DD-039.md), [DD-046](../adr/current/DD-046.md), [DD-050](../adr/current/DD-050.md).
