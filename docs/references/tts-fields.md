# Native TTS request field notes

This is a compact field report for the shared HTTP body and ordinary WS `start.request`. The authoritative, changeable definition is the [cloud OpenAPI schema](https://docs.fish.audio/api-reference/openapi.json); its [HTTP field guide](https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech) explains semantics. A shared field being accepted is not evidence that every engine implements its effect identically.

| Field | Native schema / purpose | Current reference and probe policy |
| --- | --- | --- |
| `text` | Required string. No `maxLength` is published. | Tiny HTTP input; WS start uses empty string and subsequent text events. |
| `reference_id` | String, array of speaker IDs, or null. | One genuine public speaker ID; no cloning or dialogue cases. |
| `references` | Inline binary reference samples; single/multiple-speaker forms. | Requires MessagePack; not used by the probe. |
| `prosody.speed` | Default 1; guide describes 0.5–2. | Existing plugin option; no new probe matrix. |
| `prosody.volume` | Default 0 dB. | Existing plugin option. |
| `prosody.normalize_loudness` | Default true; explicitly effective on S2 family; accepted without effect on S1. | Drama 3 effect is unspecified; do not claim support from the default. |
| `format`, `sample_rate` | Default MP3; nullable integer rate with format-specific choices. | [Audio format contract](audio-formats.md). |
| `mp3_bitrate` | 64/128/192 **kbps**, default 128. | Only sent for MP3. |
| `opus_bitrate` | -1000/24000/32000/48000/64000 **bps**, schema default -1000. | No Opus probe; prose's 32-kbps default differs from schema. |
| `temperature`, `top_p` | Both default 0.7, range 0–1. | Omitted by probe. |
| `chunk_length` | Integer 100–300, default 300; text segment size. | Explicit 100. It is not a WS transport frame size. |
| `min_chunk_length` | Integer 0–100, default 50; minimum characters before a split. | Omitted by probe. |
| `latency` | `normal` (default), `balanced`, `low`; quality/latency tradeoff. | Explicit `balanced`; no fixed delay guarantee. |
| `condition_on_previous_chunks` | Default true; previous audio as consistency context. | Shared schema; Drama 3-specific behavior is unverified. |
| `max_new_tokens` | Default 1024; maximum audio tokens per text chunk. | Not an input text-length limit. |
| `repetition_penalty` | Default 1.2; discourages repeated audio patterns. | Omitted by probe. |
| `early_stop_threshold` | Default 1, range 0–1; batch early stopping. | Omitted by probe. |
| `normalize` | Default true; expands numbers, dates, units, and symbols. | Server defaults. |
| `language` | Nullable BCP-47-style tag, maximum 35, pattern `^[A-Za-z0-9_-]*$`. | Reported addition; omitted for automatic per-chunk detection. |
| `features` | String array; `quality-guard` requests backend-dependent guarding. | Reported addition; no new UI or capability guarantee. |
| `pronunciation_dictionary` | Up to 3 references or inline dictionaries. | Reported addition; no management endpoints or probe cases. |

## Language and optional features

`language` affects text normalization and word timestamp alignment. It does not select the speech model's language, change a voice's accent, or improve its audio fidelity. `auto`, empty, null, and omission allow per-chunk detection. Mixed-language text should leave it unset. UI locale is not text language; the current plugin callback does not provide a trustworthy source/target language context. This reference therefore documents the option without adding a guessed language value. Source: [native language definition](https://docs.fish.audio/api-reference/openapi.json).

`features: ["quality-guard"]` is a request-scoped hint whose availability is decided by the inference backend. No universal quality, latency, or engine-support guarantee follows from accepting the string. It remains a reported candidate rather than a new UI control or an extra endpoint.

Pronunciation dictionaries use either `{id, version}` references or inline `{items: [{key, value, case_sensitive?}]}` dictionaries; the two forms must not be mixed. Versions are explicit and immutable, with no implicit latest. Each inline dictionary permits up to 5000 entries; keys/values have schema bounds of 256/1024 characters. Matching is plain substring, leftmost-longest, normally case insensitive. Missing managed references are silently dropped. These are replacement rules, not phoneme-tag interpretation. Sources: [schema](https://docs.fish.audio/api-reference/openapi.json), [official pronunciation guide](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/pronunciation-dictionaries). Dictionary CRUD is outside this revision.

## Drama 3 capability boundary

The official [Drama 3 guide](https://docs.fish.audio/features/text-to-speech/drama-3) documents native HTTP usage, MP3 examples, direction/phoneme tags, and multi-speaker dialogue. The [WS enum](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live) explicitly includes `drama-3`. The shared request schema describes PCM, latency, and previous-chunk conditioning, but supplies no Drama 3-specific validation of their effect. Loudness normalization explicitly names the S2 family and leaves Drama 3 unspecified.

Consequently, clients should avoid promising S2-only loudness behavior for Drama 3 and retain the distinction between an accepted field and a confirmed effect. No paid request is needed or authorized to fill that gap in the zero-cost evidence set. Current free-model responses establish only the recorded free-engine paths.

## Nucleus sampling boundary

Rechecked on 2026-10-20 UTC: the [native OpenAPI](https://docs.fish.audio/api-reference/openapi.json), at `TTSRequest.properties.top_p`, still has minimum 0, maximum 1 and default 0.7 without an exclusive minimum. The [HTTP field guide](https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech) likewise includes zero. The [real-time streaming best practice](https://docs.fish.audio/developer-guide/best-practices/real-time-streaming) instead recommends 0.1–1.0. This discrepancy does not establish a different hard backend validation boundary.

The user reported errors at zero; no new real request in this revision reproduces that failure or establishes its model coverage. The plugin's 0.05 floor is a client compatibility guard based on its existing positive step, documented in [DD-057](../adr/current/DD-057.md). It must not be presented as an official minimum or a live-tested matrix. Temperature remains zero-inclusive. The same schema recheck continues to confirm loudness normalization for the three S2-family IDs, no effect on S1, and no explicit Drama 3 effect guarantee.

## S1 retirement notice

Rechecked on 2026-10-23 against the [official native WS specification](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live): S1 retires on December 31, 2026; subsequent requests specifying `s1` are served by `s2.1-pro`. No timezone is stated there. The README announcement retains the maintainer-supplied wording that this substitution uses the same price and recommends switching to `s2.1-pro` beforehand. The accessed specification and its HTML confirm the retirement and replacement, but do not independently confirm that price clause. No paid request was made to test future billing behavior. The plugin retains its current S1 selection policy in this revision.

## Numeric parameter granularity

Rechecked on 2026-10-23T03:14:18.104105+00:00: the [official native OpenAPI](https://docs.fish.audio/api-reference/openapi.json) declares `ProsodyControl.speed`, `ProsodyControl.volume`, `TTSRequest.temperature` and `TTSRequest.top_p` as `number`. None of those four property schemas specifies `multipleOf`, a discrete value enumeration or a decimal-place limit. Speed's description gives 0.5–2.0; Temperature and top_p have inclusive 0–1 schema bounds; Volume is a decibel adjustment. The [WebSocket contract](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live) explicitly reuses the HTTP TTS request parameters. Full OpenAPI response SHA-256 at this observation: `d71d1ae1e15d3da830901fa14ddd86f5fb5c073af93beeb9b61f2c1692a191ad`.

The plugin's previous 0.05 drag ticks and 0.1 Volume precision were client interaction choices, not published upstream step constraints. A 0.01 client step fits the declared numeric representation; it is not an official maximum precision or a guarantee of an audible difference for every increment. The plugin retains its existing parameter ranges and client-only positive top_p guard. This schema inspection is not a new live synthesis validation matrix. [DD-058](../adr/current/DD-058.md) records the uniform two-decimal client behavior.
