# Native ordinary WS TTS: /v1/tts/live

Connect to `wss://api.fish.audio/v1/tts/live` with Bearer authentication and an exact native `model` header. Client and server messages are binary **MessagePack**, not JSON text. `start.request` shares the native HTTP request fields. The ordinary WS model enum includes `s2.1-pro-free` and `drama-3`; absence or an unknown value falls back to paid `s2.1-pro`. Source: [official WS protocol](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live).

| Direction | Message shape | Meaning |
| --- | --- | --- |
| Send first | `{event: "start", request: {..., text: ""}}` | Configure one session. |
| Send | `{event: "text", text: "Hello."}` | Append input text. Multiple text messages are permitted. |
| Send | `{event: "flush"}` | Request synthesis of currently buffered text. No flush acknowledgement is documented. |
| Send | `{event: "stop"}` | End input; the server synthesizes remaining buffered text. |
| Receive | `{event: "audio", audio: <binary>}` | Ordered encoded audio bytes in the requested format. |
| Receive | `{event: "finish", reason: "stop"}` | Successful completion, followed by connection closure. `reason: "error"` means failure. |

JSON-like notation above describes decoded data only. Reassemble all receive fragments of one message before decoding MessagePack. Extract each `audio` binary field in order and concatenate or feed a persistent decoder. Keep receiving after `stop` through `finish`; a client close frame immediately after `stop` can lose the tail. The ordinary protocol closes after one completed session, so a generic guide's warm-connection advice does not establish reusable ordinary sessions.

## Bidirectional evidence and text strategy

The probe starts its receive loop, sends empty-text `start`, sends `Hello.`, then `flush`. It withholds every tail text event and `stop` until a **real nonempty audio event** has been decoded. `ws-pcm` and `ws-mp3` then send a short tail. `ws-pcm-segmented` instead sends about 250 characters through three separate tail events and stops normally. This sequence distinguishes real bidirectional streaming from a client that uploads the whole text before receiving audio. `events.json` records ordering, UTC, monotonic elapsed time, original MessagePack message artifacts, fragment completeness, and audio mappings. See the [evidence index](../evidences/2026-10-20/manifest.json).

`ws-cancel` aborts locally after actual first audio and records `cancelled-by-case`; it does not blame the service. `ws-missing-auth` captures the actual upgrade failure, including a body when exposed by the .NET HTTP transport. Missing bodies are explicitly marked uncaptured.

**Documented buffering:** the server buffers text according to `chunk_length`; `flush` forces processing of the buffered prefix. The native schema permits `chunk_length` 100–300 (default 300), `min_chunk_length` 0–100 (default 50), and `latency` `normal`/`balanced`/`low` (default `normal`). The probe sets 100 and `balanced` explicitly. SDK guide examples differ in defaults and describe chunk units inconsistently, so they must not override the native schema. Sources: [native fields](https://docs.fish.audio/api-reference/openapi.json), [streaming guidance](https://docs.fish.audio/features/realtime-streaming).

**Unverified limits:** the ordinary native documentation publishes no exact maximum text length, per-text-message limit, session duration, idle timeout, or contractual time to first audio. `max_new_tokens: 1024` is an audio-token generation limit per chunk, not a text-character cap. Compatibility-layer limits and SDK keepalive settings are not native WS guarantees. The probe's 120-second / 8-MiB bounds are local limits.

The plugin can guarantee bounded local first-prefix preparation, prompt sending, concurrent receive, and incremental decoder feeding. It cannot guarantee that network transit, provider queueing, model context buffering, decoding, audio-device buffering, or initial generated silence always produce an audible first word within a fixed interval. A first nonempty encoded audio event, first decoded sample, playback start, and first audible word are separate measurements. The [free tier explicitly has no latency guarantee](https://fish.audio/blog/s2-1-pro-free-api/).

PCM and MP3 are the selected ordinary-stream formats; see [format tradeoffs](audio-formats.md). The current free PCM/MP3 observations do not validate paid Drama 3 behavior. Timestamp protocols remain future external references and are not conflated with this session lifecycle.
