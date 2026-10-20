# Voice detail: GET /model/{id}

`GET https://api.fish.audio/model/{id}` retrieves one voice asset. Its `id` is a value obtained from a real list response or an explicitly configured voice, not an inference-engine name. The official contract requires Bearer authentication and documents `200`, `403`, and `404` results. Source: [Get Model](https://docs.fish.audio/api-reference/endpoint/model/get-model).

A successful response is `ModelEntity` directly, without the list's `items` wrapper. Relevant fields include `_id`, `type`, `title`, `author`, `cover_image`, `samples`, `languages`, and visibility/state metadata. The [native schema](https://docs.fish.audio/api-reference/openapi.json) defines `samples[].audio` as a string alongside `title`, `text`, and `task_id`. This matches the current plugin data type.

**Observed behavior to preserve in evidence:** detail sample URLs can be signed even when list samples use public CDN URLs. Treat URLs as opaque, potentially expiring media locations. Do not persist signed query credentials or infer a universal expiration duration. Refresh detail when a fresh preview URL is needed. [Preview assets](preview-assets.md) explains the separate resource request.

The probe reads two IDs returned by a real list, then an all-zero nonexistent ID as a harmless negative case. IDs are URL escaped. `public-voice-detail-1/2` use the plugin's public dummy-Bearer convention; `authenticated-voice-detail-1/2` use the supplied environment key. The missing-ID case retains the actual response and does not synthesize a fixture. The latest detail sample feeds dedicated `*-sample-detail-full/range` asset cases. See the [evidence index](../evidences/2026-10-20/manifest.json).

This endpoint only reads metadata. No create/update/delete operation is part of the plugin's lookup or preview path. A denied or missing voice should remain a visible failure rather than silently selecting a different speaker.
