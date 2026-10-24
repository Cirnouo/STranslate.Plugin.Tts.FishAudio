# Voice list: GET /model

`GET https://api.fish.audio/model` lists voice assets. It does not enumerate TTS engines. The official endpoint documents Bearer authentication. Anonymous access and the plugin's `Bearer dummy` public-access convention are observations to verify independently, not a promise that private voices can be read without credentials. Sources: [List Models](https://docs.fish.audio/api-reference/endpoint/model/list-models), [OpenAPI schema](https://docs.fish.audio/api-reference/openapi.json).

| Query | Documented meaning |
| --- | --- |
| `page_size` | Integer 1–100, default 10. |
| `page_number` | Integer at least 1, default 1. |
| `title` | Optional title search. |
| `sort_by` | `score` (default), `task_count`, or `created_at`. |
| `language` | Optional `array<string> \| string \| null` language filter; absent/null means no language filter. |
| `tag`, `title_language` | Optional filters; preserve the schema's accepted representation. |
| `self` | Default false; true selects the active workspace's voices. |
| `author_id` | Public-author filter, ignored with `self=true`. |
| `licensed` | Default false; true selects licensed voices, ignored with `self=true`. |

The response wraps `items: ModelEntity[]` and `total`. Pagination also exposes `max_offset`, `accessible_upper_bound`, `window_limited`, `total_is_exact`, and `has_more`. Do not infer unlimited browsing from an approximate total. The plugin needs `_id`, `type`, `title`, `author`, `cover_image`, and `samples`; additional fields should not break parsing. Filter usable voice records by `type: tts` rather than treating unrelated model types as speakers.

Dated evidence distinguishes these cases: the [authenticated default response](../evidences/2026-10-20/run-20261020T113301927Z-60d40c3b8caf46b99e44fa56de21cfbf/cases/authenticated-voices-default/body.json) has total 1000 with `total_is_exact=false` and a limited window, while a [keyword response](../evidences/2026-10-20/run-20261020T113058545Z-ba5e847ad6ae4191ae36f9da31870e83/cases/synthesis-discovery-voices-search/body.json) has total 200 with `total_is_exact=true`. Approximation is provider metadata, not a client estimate. These captures establish current behavior, not a rollout date. The old client's absence of these fields cannot prove that all earlier provider totals were exact.

In the current native schema `samples` is an array of `SampleEntity`; each item requires four strings: `title`, `text`, `task_id`, and **`audio`**. The existing plugin shape `samples[].audio: string` remains valid. There is no documented migration to `audio.url` or a required synthesis request for preview. See [preview assets](preview-assets.md) for URL retrieval and expiration concerns. The schema permits an empty sample array.

The probe records `voices-anonymous`, `public-voices-default`, `public-voices-search`, and optional `*-voices-page-2` / `*-voices-no-result`; authenticated equivalents repeat visible-list behavior with the supplied key. List responses provide actual IDs used by [detail](voices-detail.md) and synthesis cases. A title search may validly return zero results. The unlikely no-result keyword is not a server guarantee of emptiness; the actual body establishes that outcome. See the [evidence index](../evidences/2026-10-20/manifest.json) for statuses and bodies.

All cases read existing data. No `self=true` account enumeration, mutation, generated sample, or paid engine is needed to test the public voice picker contract.

## Search options: contract and bounded observations

The [pinned official OpenAPI schema](https://github.com/fishaudio/docs/blob/9e9b9e8b1a23a08c2105f4ad7e609dcb723a3bd1/api-reference/openapi.json) documents the three `sort_by` values and `language` as `array<string> | string | null`, with no enum or array-length limit. It does not document a sort-direction parameter, tie order or a numeric `score` field/formula. The plugin explicitly sends one selected sort and omits the language parameter for All. Specific selections use repeated keys, for example `language=en&language=zh`, in preset order in one GET. Its presets are `en`, `zh`, `de`, `ja`, `fr`, `es`, `ko`, `ar`, `ru` and `pt`; these UI presets are not a provider capability whitelist.

The [pinned official Go SDK](https://github.com/fishaudio/fish-audio-go/blob/4b6149bd1722a886c448f43c4f30ba3adbd11ec7/voices.go#L155) calls `query.Add("language", lang)` per value. The [pinned Python request owner](https://github.com/fishaudio/fish-audio-python/blob/448150989781e6f91c3ca9f6cc59711a87df8130/src/fishaudio/resources/voices.py#L21) accepts a string or list and forwards query parameters to HTTPX. These sources establish multiple-value request representation, not AND/OR semantics. Three bounded public metadata GETs on 2026-10-24 at page size one accepted `language=en`, `language=zh` and `language=en&language=zh`; the repeated-key result had ja/zh/en metadata. All reported approximate limited totals of 1000. One item and capped totals cannot prove the language combination operator or synthesis capability. No key, cookie, synthesis or audio download was used.

On 2026-10-24, three separate unauthenticated public first-page GET observations used `page_size=6&page_number=1` with `task_count` (03:02:28 UTC), `created_at` (03:02:31 UTC) and `score` (03:02:35 UTC). The popularity response counts were 19962974, 4952573, 4202767, 4007516, 3279050 and 2993860, descending; creation timestamps were latest first. The score response exposed no numeric score and was monotonic in neither count nor creation date. No raw response capture was retained for these observations. They establish only the ordering seen on those first pages, not a documented direction, stable tie order, global ranking or future guarantee. The observations did not use credentials/cookies, retry or synthesize speech.

The official Python SDK [voice type at commit 448150989781e6f91c3ca9f6cc59711a87df8130](https://github.com/fishaudio/fish-audio-python/blob/448150989781e6f91c3ca9f6cc59711a87df8130/src/fishaudio/types/voices.py#L62) describes `task_count` in terms of generation uses. The UI therefore calls its descending view Most used first (热门优先). Provider `score` is labeled Relevance first (相关优先). The source website uses Recommended for score; the plugin title is a product choice for search, and no scoring formula or numeric direction is inferred from it. Newest first (最新优先) maps to `created_at`. The UI shows these three titles without helper descriptions. Descending provider order remains the bounded observation above, rather than a documented guarantee.
