# Domain glossary

Use these terms consistently in code reviews, specifications, API references and measurements. Provider field names remain unchanged even when they use a different vocabulary.

| Term | Meaning | Distinction |
|---|---|---|
| Voice | A voice identity selected for synthesis, normally identified by `reference_id`. | A voice is not the synthesis engine. |
| Voice ID | The identifier of a Voice. The plugin stores it as `Settings.VoiceId`. | It is independent of the API Key. |
| TTS model | The synthesis engine selected by the `model` HTTP header, such as `s2-pro`. | UI discussions should say “TTS model” when “model” could mean a Voice. |
| Provider model entity | A voice metadata object returned by Fish Audio's `/model` endpoints. | `ModelEntity` and related DTO names follow the provider and remain valid code identifiers. |
| Selected voice | The applied Voice ID and cached metadata shown in the settings selection area. | A card is applied when its ID matches that selection; matching cards still have distinct originating object identities for asynchronous ownership. |
| Favorite voice | A locally bookmarked Voice ID persisted independently of settings metadata. | Display metadata comes from the same in-memory cache used by search; bookmarking does not select the voice, store audio bytes or modify a provider account. |
| Voice sample / preview | Provider-supplied sample audio played while browsing voices. | Preview playback does not synthesize the user's text. |
| Random/default voice | The provider's voice choice when the plugin omits `reference_id`. | Omission is not evidence of free synthesis or stable identity across requests. |
| API Key | A credential used for authenticated synthesis and credit operations. | Local shape validation is not authentication or entitlement verification. |
| Credit | The account balance returned by the provider's wallet API. | A delayed or unchanged balance does not prove a synthesis was free. |
| Synthesis request | One provider operation that turns text into audio. | Transport retries and replacement requests can create additional operations and cost. |
| Synthesis session | One logical streaming synthesis operation with its own protocol lifetime. | The current ordinary native WebSocket session is single-use; provider finish is separate from device drain. |
| Readable prefix | Initial transmitted text containing readable content at a safe flush boundary. | It can require several bounded windows when a recognized markup span remains open. It does not establish an audible word. |
| Text window | A bounded local visit of input used to select the next text segment. | Window size is measured in Unicode scalars, not graphemes, UTF-16 units or provider audio tokens. |
| Text segment | A bounded portion of input text selected for transmission or synthesis. | It is not necessarily a sentence, provider text chunk, transport message or audio boundary. |
| Transport message | A complete application message, such as a WebSocket MessagePack event or an SSE event. | One receive call may contain only a fragment; one message may contain metadata or audio. |
| Audio chunk | An ordered byte payload containing part of an encoded or raw audio stream. | It may end inside an MP3 frame, PCM sample frame or container structure. “Chunk” alone is ambiguous. |
| Encoded audio frame | A codec-defined unit, such as an MP3 frame. | It generally requires decoding before PCM playback. |
| PCM sample | One channel's amplitude value at one sampling instant. | A 16-bit sample occupies two bytes; stereo has two samples per sample frame. |
| PCM sample frame | All channel samples at one sampling instant. | `BlockAlign = channels × bitsPerSample / 8`; device reads/writes must preserve complete frames. |
| Audio format | The rules for interpreting audio bytes: codec/container or raw PCM parameters. | Streaming does not remove format. Raw PCM lacks a file header but still needs rate, channels, bit depth, encoding and byte order. |
| TTFA | Time to first audio, always qualified by the measured boundary. | Report “first audio payload”, “first decodable frame” or “first PCM” rather than an unqualified TTFA. |
| Output start | The point where the playback device has been initialized and playback has been started successfully. | A `Play` call or PCM submission does not prove physical sound has reached the listener. |
| Time to first audible sound | A measurement of actual audio output under a stated device/loopback method. | It must not be inferred from network arrival or a software output-start signal. |
| Prebuffer | Audio accumulated before starting output, expressed as audio duration or bytes. | A 250 ms audio threshold is not a fixed 250 ms wall-clock wait. |
| Backpressure | A downstream limit that makes upstream production wait. | It must not silently drop audio, remove memory bounds or prevent cancellation. |
| Active idle time | Time since the last audio arrival after output start, excluding actual local pressure waits. | Overlapping pressure intervals are counted once; it is distinct from wall time, first-PCM deadline and device-progress timeout. |
| Underflow | Output requests audio when insufficient real samples are available. | Silence insertion, waiting and failure are separate policies that must be specified. |
| Drain | Completion of queued audio delivery, including any output/resampler tail required by the playback contract. | Provider finish, queue empty and device drain are different milestones. |
| Audio cache | Plugin-managed audio bytes reusable across separate play operations. | CachedVoice metadata/sample URL and the cover-image cache are not audio caches; one play's disposed memory buffer is not reusable caching. |
| Initialization generation | One declared plugin initialization attempt. | A later generation can overtake an earlier attempt before publication. |
| Settings write lease | The credential that authorizes a specific runtime settings generation to save through the host. | Having a reference to Settings does not authorize an obsolete generation to persist it. |
| Inline numeric editor | A numeric text label that temporarily becomes an input within the same bounds. | Its draft is separate from the committed setting, and its decimal precision is independent of a slider's interaction step. |
| Current ADR | A decision with an active implementation or maintenance constraint, possibly amended by another record. | Classification is based on supersession, not date. |
| Historical ADR | A replaced design retained to explain why behavior changed. | Its recorded rationale is not a current implementation requirement. |

The [current specifications](specs/README.md) define the implemented streaming and preview contracts. Provider capabilities, probe observations, software milestones and physical audio measurements remain separate evidence classes.
