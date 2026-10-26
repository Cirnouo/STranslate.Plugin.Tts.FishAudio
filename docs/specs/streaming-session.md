# Native streaming transport and session

Status: current Unreleased implementation. Owners: [NativeStreamingTtsSession](../../STranslate.Plugin.Tts.FishAudio/Streaming/NativeStreamingTtsSession.cs), [FishAudioSocket](../../STranslate.Plugin.Tts.FishAudio/Streaming/FishAudioSocket.cs), [protocol](../../STranslate.Plugin.Tts.FishAudio/Streaming/FishAudioStreamingProtocol.cs) and [activity accounting](../../STranslate.Plugin.Tts.FishAudio/Streaming/StreamingActivity.cs). This is the plugin's contract; provider capabilities and dated observations are separately documented in the [native WS reference](../references/tts-websocket.md).

## Wire contract

One single-use session opens `wss://api.fish.audio/v1/tts/live` with `Authorization: Bearer <captured key>` and the exact captured `model` header. All messages are binary MessagePack. The client sends an initial `start`, zero or more ordered `text`/`flush` events and a final `stop`. `start.request.text` is empty; its format is `pcm` or `mp3`, sample rate is 44100 and server `chunk_length` is 100. It includes temperature, top_p, normalize, latency, request-local conditioning and prosody speed/volume. `mp3_bitrate` appears only for MP3. A nonblank Voice ID becomes `reference_id`; blank omits it. `normalize_loudness` appears only for supported S2-family models, never `s1` or `drama-3`.

The available model list is checked before connection, before every send and by the session watchdog. Exact membership is mandatory because the provider documents a paid default for unknown or missing model values. Crossing the free cutoff aborts a long free session locally. No path substitutes another model or retries synthesis. The client does not use JSON text, compatibility endpoints, SSE or a whole-response HTTP route.

Receive starts independently of sending and local playback. Receive calls may be fragmented; the receiver aggregates one complete application message before decoding it. It accepts nonempty/empty `audio`, successful `finish` with `reason=stop`, and remote error messages. Empty audio does not count as first audio or reset the audio-arrival deadline. A finish is successful only after the client's stop has been initiated. Invalid/nonbinary/oversized messages, duplicate keys, malformed MessagePack, unknown events, error finish, remote error and close before successful finish fail the session. Provider strings and arbitrary metadata never enter diagnostics.

## Bounded handoff and backpressure

| Boundary | Current limit / behavior |
|---|---|
| Receive fragment | 16 KiB scratch buffer. |
| Complete MessagePack message | Eight MiB aggregation buffer; overflow fails before decoding. This allocation is separate from the encoded queue. |
| Owned encoded audio handoff | At most 32 queued blocks of at most 16 KiB each: 512 KiB queue capacity. Blocks are copied before reusing message storage. |
| Active ownership outside queue | The current message, the producer's pending block and the consumer's current block/decoder state are separately owned; 512 KiB is not the total pipeline memory bound. |
| PCM output | A separate five-second ring and four-/three-second pressure watermarks; see [playback](streaming-playback.md). |

The queue waits for capacity and preserves every byte's order. A large message is sliced into blocks without expanding all audio into another message-sized object graph. Decoding likewise advances only as bounded PCM writes permit. A receiver blocked on the encoded queue cannot simultaneously read a later finish/error/close from the same connection. This is an explicit tradeoff; independent cancellation and device-progress detection release blocked work.

`StreamingActivity` tracks the union of actual encoded-queue and PCM-buffer waiting intervals. Overlap counts once. Mere decoding, startup, buffer occupancy or an idle input source does not itself count as pressure.

## Deadlines and completion

| Deadline | Scope |
|---|---|
| 15 seconds | Connection establishment; caller/lifetime cancellation retains cancellation semantics. |
| 120 seconds | From connected to first playable PCM, not merely encoded audio. |
| 30 active seconds | After playback starts, since the last nonempty audio arrival; excludes the union of actual queue/PCM backpressure waits. Playback start resets this baseline. |
| 10 seconds | Output position fails to advance while real PCM remains outstanding; owned by the audio pump. |

The watchdog uses an injected clock and a 100 ms polling interval. These are local liveness budgets, not provider latency guarantees or a total session deadline. Active WebSocket keepalive timeout is disabled (`Timeout.InfiniteTimeSpan`) so a pressure-blocked receive cannot cause a false PING/PONG timeout. It does not replace the explicit deadlines above.

Provider `finish`, encoded queue completion, decoder completion and device drain are separate milestones. Normal receive completion closes the queue; audio keeps decoding and draining. A successful session requires provider finish, first playable PCM, successful local playback start, all sender/receiver/audio/watchdog tasks observed, and device position past the final real-sample tail. Finish alone does not report playback success.

The first worker failure is retained, aborts the socket and cancels sibling workers. Caller, session disposal and initialization retirement use the same cancellation path. A session is single-use; disposal is idempotent. Cleanup must not turn an earlier transport/decoder/device failure into success. [Main](../../STranslate.Plugin.Tts.FishAudio/Main.cs) waits for retirement before giving a successor output ownership.

## Routing and observations

REST metadata, credit and sample downloads use host `IHttpService`. The native socket always uses .NET `ClientWebSocket` with the system proxy (`HttpClient.DefaultProxy`); the plugin provides no proxy controls or custom credentials. Host-specific HTTP overrides are not automatically a native WebSocket proxy contract. Former proxy settings are ignored and removed on ordinary canonical saves. See [settings](settings-and-model-policy.md) and [DD-055](../adr/current/DD-055.md).

Observations record monotonic request-relative elapsed time for Request, Connected, PrefixSent, FirstAudio, FirstPcm, PlaybackStarted, Finish and Drained. Observer failures cannot alter playback. Log fields describe software boundaries only. The [dated probe](../evidences/2026-10-20/README.md) gates its tail on network audio; production gates on successful output start, so those measurements are not interchangeable.

Verification: [session suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/Streaming/StreamingSessionSuite.cs), [entry suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/Streaming/StreamingEntrySuite.cs). Rationale: [DD-050](../adr/current/DD-050.md), [DD-053](../adr/current/DD-053.md).
