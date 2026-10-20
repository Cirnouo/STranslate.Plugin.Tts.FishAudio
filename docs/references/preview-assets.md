# Preview audio and cover resources

The native voice schema already returns preview media: `samples[].audio` is a string, and `cover_image` is a string. Retrieve an existing sample with `GET` on the actual returned audio URL. There is no additional preview-generation endpoint in this reference set and no reason to call paid TTS to hear a stored sample. Sources: [voice list](https://docs.fish.audio/api-reference/endpoint/model/list-models), [voice detail](https://docs.fish.audio/api-reference/endpoint/model/get-model), [SampleEntity schema](https://docs.fish.audio/api-reference/openapi.json).

**Historical compatibility concern:** signed/expiring detail sample URLs motivated the baseline's defensive expiry handling; this is not a finding from the 2026-10-20 captures. All captured list/detail sample URLs were unsigned, without query or a separate expiration field. The official SampleEntity schema, checked 2026-10-20, has only `title`, `text`, `task_id` and string `audio`; it does not guarantee that future URLs remain unsigned or permanent. Covers can be relative paths resolved against `https://public-platform.r2.fish.audio/`. Capture source metadata and the media request; do not reconstruct signatures, invent asset paths or assume future hosts are trusted.

The probe allows only HTTPS destinations on its bounded Fish/R2 host allowlist, disables redirects and cookies, and sends **no Authorization header** to resources. A new host is refused for review. Request evidence strips URL user information and signed query credentials. Response bodies containing such URLs are sanitized too; original/stored SHA-256 values disclose the transformation. The signed URI is used only in memory for the real request.

| Cases | What they establish |
| --- | --- |
| `*-sample-full` / `*-sample-range` | List-derived sample retrieval and a `bytes=0-1023` range request. |
| `*-sample-detail-full` / `*-sample-detail-range` | Latest detail-derived sample retrieval; captured URLs were unsigned. |
| `*-cover-full` | Existing cover retrieval from the returned path. |

A range request can receive `206`, a complete `200` if the server ignores the range, or an actual error; record that result rather than assuming partial-content support. HTTP semantics are defined by [RFC 9110 range requests](https://www.rfc-editor.org/rfc/rfc9110.html#name-range-requests). Each capture stores actual headers, byte count, completeness, and hashes. See the [evidence index](../evidences/2026-10-20/manifest.json).

The 2026-10-20 captured sample URLs were unsigned. These responses therefore establish full/range media retrieval, not a successful signed-URL request. Signed-query stripping, URI user-information removal, nested JSON, and MessagePack sanitization are separately checked by offline synthetic self-tests.

Successful binary retrieval proves that the server returned those bytes at that time. Playback format, decoding, audible output, and signed-URL lifetime need separate validation. If a preview URL expires, refresh its metadata instead of adding a synthesis fallback. URL lifetime is not uniformly specified by the native voice schema.
