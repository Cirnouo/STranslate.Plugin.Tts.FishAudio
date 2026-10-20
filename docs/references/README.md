# Fish Audio API references

Reviewed against official cloud documentation on 2026-10-20. These references cover the plugin's seven API surfaces. A **documented** statement describes the official contract; an **observed** statement describes a dated captured response; an **inference** is a local implementation conclusion; an **unverified** statement requires additional evidence. Cloud contracts take precedence over similarly named self-hosted Fish Speech schemas.

| Surface | Reference | Real-call coverage plan | Account effect |
| --- | --- | --- | --- |
| `GET /model` | [Voice list](voices-list.md) | Default page, title search, anonymous/dummy/key authentication observations | Reads visible voices. |
| `GET /model/{id}` | [Voice detail](voices-detail.md) | Two returned IDs and a nonexistent ID | Reads a visible voice. |
| `GET /wallet/self/api-credit` | [API credit](wallet-credit.md) | Valid key and omitted key | Reads the current account's balance. |
| API-returned resource `GET` | [Preview assets](preview-assets.md) | Full/range samples from list and detail plus cover | Retrieves existing media. |
| `POST /v1/tts` | [HTTP TTS](tts-http.md) | Repeat short PCM English and MP3 Chinese; invalid-format response | Runs synthesis; free-only probe policy required. |
| `WS /v1/tts/live` | [Ordinary WS TTS](tts-websocket.md) | PCM, MP3, segmented tail, client cancellation, omitted-auth handshake | Runs synthesis; free-only probe policy required. |
| `GET /compat/v1/models` | [Engine catalog](engine-catalog.md) | Anonymous full list and documented speech filter | Reads public engine metadata. |

The [real evidence index](../evidences/2026-10-20/manifest.json) is the authority for what actually ran. A planned case, offline self-test, skipped dependency, and successful service response are distinct. Expected failure cases retain actual status and response bodies when exposed. Consult individual case outcomes rather than treating every directory as successful coverage.

The [probe instructions](../../tools/FishAudioProbe/README.md) describe invocation, case selection, byte limits, sanitization, and hashes. Repeated invocations append unique runs. No reference page authorizes paid synthesis, account changes, guessed endpoints, or automatic fallback to a paid engine.

Shared details are collected in [native request fields](tts-fields.md) and [audio formats and stream boundaries](audio-formats.md). Voice IDs from `/model` identify a speaker asset; engine IDs such as `s2.1-pro-free` select inference. Neither list order nor the word `model` makes them interchangeable.

## Findings and recommended use

| Finding | Practical use | Decision for this revision |
| --- | --- | --- |
| [Engine catalog](engine-catalog.md) lists inference IDs but omits Drama 3 and has no ability rank. | Maintainer discovery and reviewed mapping to native short IDs. | Preserve free-first, curated capability ordering; do not automatically rank or synthesize new entries. |
| [Voice pagination](voices-list.md) includes window/approximate-total metadata. | Bound browsing by server window and `has_more`, and use search for a narrower set. | Preserve metadata; avoid an unbounded page loop based on `total` alone. |
| [`language`](tts-fields.md) affects normalization and future timestamp alignment. | Correct readings for dates, numbers, and units when text language is known. | Omit for automatic detection now; UI language cannot stand in for text language. |
| [`quality-guard`](tts-fields.md) is backend-dependent. | Candidate for a future quality-priority option. | Report only; effect, latency, backend availability, and cost implications are not established by this probe. |
| [Pronunciation dictionaries](tts-fields.md) replace specific text. | Names, brands, abbreviations, and specialist terms. | Report inline/managed forms; no management UI or CRUD endpoints. |
| Timestamp streams supply alignment data. | Future word highlighting and subtitles. | External official links only; no separate endpoint/evidence claim in this revision. |
| [Drama 3](tts-fields.md) adds advanced direction capabilities and is a paid choice. | User-selected expressive content and dialogue. | Document official support and unknowns; never tested with a paid request or ranked as better in every dimension. |

These are evidence-informed recommendations, not new runtime features in the documentation/probe change.

Timestamp streaming is a future candidate. The official [HTTP timestamp stream](https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech-stream-with-timestamps) and [WS timestamp stream](https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech-live-with-timestamps) are external reading only. They are not separate reference surfaces or claimed evidence coverage in this revision. Pronunciation-dictionary management, voice creation, uploads, likes, deletion, billing mutations, compatibility synthesis, and other account APIs are outside this bounded reference set.

Primary source entry points: [official documentation](https://docs.fish.audio), [native OpenAPI schema](https://docs.fish.audio/api-reference/openapi.json), [official compatibility guide](https://docs.fish.audio/developer-guide/compat/capabilities), and [official SDK](https://github.com/fishaudio/fish-audio-python).
