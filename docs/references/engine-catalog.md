# Engine catalog: GET /compat/v1/models

`GET https://api.fish.audio/compat/v1/models` is an official, read-only, anonymous catalog of inference engines. The documented query `output_modalities=speech` selects TTS engines; `text` selects the other output direction. It is separate from the native `/model` voice library. Sources: [OpenAI compatibility guide](https://docs.fish.audio/developer-guide/compat/migrate-from-openai), [OpenRouter model catalog](https://docs.fish.audio/developer-guide/compat/migrate-from-openrouter).

The `/compat/api/v1` and `/compat/openai/v1` prefixes are compatibility aliases. They are not additional independent reference/evidence surfaces here. Reading the catalog does not switch plugin synthesis away from the native HTTP/WS APIs.

## Dated observation

The anonymous full and speech-filtered requests returned HTTP 200 on 2026-10-20. The full response contained five entries; the speech filter contained four. Actual bodies and headers are linked through the [evidence index](../evidences/2026-10-20/manifest.json), cases `catalog-all` and `catalog-speech`.

| Catalog ID | Native model header for known TTS mapping | Output |
| --- | --- | --- |
| `fish-audio/s2.1-pro` | `s2.1-pro` | speech |
| `fish-audio/s2.1-pro-free` | `s2.1-pro-free` | speech |
| `fish-audio/s2-pro` | `s2-pro` | speech |
| `fish-audio/s1` | `s1` | speech |
| `fish-audio/transcribe-1` | Not a TTS model | text |

`drama-3` was absent from this dated catalog, although the native [HTTP](https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech) and [WS](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live) enums explicitly include it. Therefore the catalog is useful discovery metadata and is not an exhaustive native capability registry. A future unknown namespaced ID needs an explicit reviewed native mapping; do not blindly strip a prefix and send it as a synthesis header.

The observed `canonical_slug` matched `id`. The response's paid TTS entries carried `pricing.prompt: "0.000015"` and `pricing.completion: "0"`; the free entry carried zero strings. The catalog does **not** state the unit of those numbers. The separate [native pricing page](https://docs.fish.audio/developer-guide/models-pricing/pricing-and-rate-limits) lists S2-family pricing per million UTF-8 input bytes. Do not present the catalog values as token prices, or apply that unit to Drama 3 without its own source.

The catalog exposes no ability rank, comprehensive benchmark, or sort score. `created` timestamps are metadata, not a verified release chronology. `context_length`, `default_parameters`, `per_request_limits`, and `supported_voices` are documented as null; optional fields can be absent. The observed `supported_parameters` subset does not describe every native request field. An item appearing first does not make it the strongest or cheapest engine.

## Model selection implications

The requested presentation order is free first, then a curated descending capability order within each price group. That order is project policy, not a catalog claim. Official material identifies [Drama 3](https://docs.fish.audio/features/text-to-speech/drama-3) as the latest model with expanded voice direction, while the [S2.1 launch](https://fish.audio/blog/s2-1-pro-free-api/) supplies a comparison against S2 Pro. Greater expressive control alone does not prove Drama 3 dominates every latency, language, cloning, and fidelity metric. Capability labels must state their basis.

The free offer currently runs through 2026-11-30 under fair use, without a latency SLA. This project's probe refuses synthesis from `2026-12-01T00:00:00Z`; UTC is a local explicit policy because the official article does not specify a timezone. The catalog's zero price is not a substitute for that guard. The native API falls back to paid `s2.1-pro` for an omitted or unknown model header, so neither omission nor typo testing is allowed in the zero-cost probe.

The separate [official student program](https://fish.audio/students/) describes `.edu` verification and free credits. Its current public page does not state a fixed $5 amount. The README's $5 wording is retained at the user's explicit request and is not independently verified as a current fixed grant by this reference. No login, eligibility application, or credit-claim action was performed for research or probing.
