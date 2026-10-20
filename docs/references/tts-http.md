# Native HTTP TTS: POST /v1/tts

`POST https://api.fish.audio/v1/tts` accepts `application/json` or `application/msgpack` and returns generated audio bytes. Bearer authentication and an explicit native `model` header select the key and engine. `text` is required by the request schema. A stored speaker is selected by body field `reference_id`; it is independent of the engine header. Inline binary `references` require MessagePack. Sources: [Text to Speech](https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech), [native OpenAPI schema](https://docs.fish.audio/api-reference/openapi.json).

The schema's model choices are `s1`, `s2-pro`, `s2.1-pro`, `s2.1-pro-free`, and `drama-3`. Omitted or unrecognized headers fall back to paid `s2.1-pro`. The probe always sends exact `s2.1-pro-free`, including its invalid-format case. It never tests an omitted, misspelled, or paid engine. S1 is deprecated and is documented to move to S2.1 Pro serving/billing after 2026-12-31. Sources: [HTTP model header](https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech), [pricing and retirement](https://docs.fish.audio/developer-guide/models-pricing/pricing-and-rate-limits).

## Streaming behavior

HTTP takes the request text before synthesis and streams response audio as it becomes available. There is no required `streaming: true` switch in the native request schema. Read with `ResponseHeadersRead` and feed the bytes incrementally into one continuing audio pipeline. Collecting the whole response first defeats early playback even though the server streams. See [official streaming guidance](https://docs.fish.audio/features/realtime-streaming).

HTTP response read chunks are arbitrary transport divisions, not codec frames, PCM sample boundaries, or separate playable files. Preserve ordering and decoder state. The audio [format contract and unresolved representation details](audio-formats.md) apply equally to HTTP and ordinary WS.

## Probe coverage

The successful-case payload fixes `reference_id` to a genuine returned voice ID, `sample_rate` to 44100, `latency` to `balanced`, and `chunk_length` to 100. MP3 additionally uses `mp3_bitrate: 128`. These are explicit probe choices, not the service defaults. `http-pcm-en` and its repeat synthesize `Hello.`; `http-mp3-zh` and its repeat synthesize `你好。`. The invalid-format case changes only the format to a deliberately unsupported value and expects an actual client error while preserving the exact free model. Individual outcomes and real bytes are in the [evidence index](../evidences/2026-10-20/manifest.json).

The official [free offer](https://fish.audio/blog/s2-1-pro-free-api/) and [zero-price entry](https://docs.fish.audio/developer-guide/models-pricing/pricing-and-rate-limits) support the free-only policy. Local guards also enforce the UTC deadline, single concurrency, no automatic retries, 120 seconds, and 8 MiB per case. There is no paid fallback after a free-tier failure. Authentication, validation, service, and incomplete-read results are saved separately from successful audio; a failed response is never replaced by a sample fixture.

See [native request fields](tts-fields.md) for current schema additions and their model-specific limits. This reference does not authorize testing Drama 3 or any other paid engine.
