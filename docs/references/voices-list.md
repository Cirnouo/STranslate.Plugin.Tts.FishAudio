# Voice list: GET /model

`GET https://api.fish.audio/model` lists voice assets. It does not enumerate TTS engines. The official endpoint documents Bearer authentication. Anonymous access and the plugin's `Bearer dummy` public-access convention are observations to verify independently, not a promise that private voices can be read without credentials. Sources: [List Models](https://docs.fish.audio/api-reference/endpoint/model/list-models), [OpenAPI schema](https://docs.fish.audio/api-reference/openapi.json).

| Query | Documented meaning |
| --- | --- |
| `page_size` | Integer 1–100, default 10. |
| `page_number` | Integer at least 1, default 1. |
| `title` | Optional title search. |
| `sort_by` | `score` (default), `task_count`, or `created_at`. |
| `tag`, `language`, `title_language` | Optional filters; preserve the schema's accepted representation. |
| `self` | Default false; true selects the active workspace's voices. |
| `author_id` | Public-author filter, ignored with `self=true`. |
| `licensed` | Default false; true selects licensed voices, ignored with `self=true`. |

The response wraps `items: ModelEntity[]` and `total`. Pagination also exposes `max_offset`, `accessible_upper_bound`, `window_limited`, `total_is_exact`, and `has_more`. Do not infer unlimited browsing from an approximate total. The plugin needs `_id`, `type`, `title`, `author`, `cover_image`, and `samples`; additional fields should not break parsing. Filter usable voice records by `type: tts` rather than treating unrelated model types as speakers.

In the current native schema `samples` is an array of `SampleEntity`; each item requires four strings: `title`, `text`, `task_id`, and **`audio`**. The existing plugin shape `samples[].audio: string` remains valid. There is no documented migration to `audio.url` or a required synthesis request for preview. See [preview assets](preview-assets.md) for URL retrieval and expiration concerns. The schema permits an empty sample array.

The probe records `voices-anonymous`, `public-voices-default`, `public-voices-search`, and optional `*-voices-page-2` / `*-voices-no-result`; authenticated equivalents repeat visible-list behavior with the supplied key. List responses provide actual IDs used by [detail](voices-detail.md) and synthesis cases. A title search may validly return zero results. The unlikely no-result keyword is not a server guarantee of emptiness; the actual body establishes that outcome. See the [evidence index](../evidences/2026-10-20/manifest.json) for statuses and bodies.

All cases read existing data. No `self=true` account enumeration, mutation, generated sample, or paid engine is needed to test the public voice picker contract.
