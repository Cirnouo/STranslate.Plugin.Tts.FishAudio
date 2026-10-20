# API credit: GET /wallet/self/api-credit

`GET https://api.fish.audio/wallet/self/api-credit` reads the key owner's current API credit. The official route is `/wallet/{user_id}/api-credit`; `self` is the documented default. Bearer authentication is required. Source: [Get API Credit](https://docs.fish.audio/api-reference/endpoint/wallet/get-api-credit).

The response includes `_id`, `user_id`, `credit`, `cumulative_top_up`, `created_at`, `updated_at`, `has_phone_sha256`, and nullable `has_free_credit`. `credit` and `cumulative_top_up` are **strings**, so parse monetary decimals with an invariant culture rather than assuming JSON numbers or the machine's decimal separator. A credit observation is account-specific; it does not identify which engine is free.

The documented optional query parameters are `check_free_credit` (default false) and `team_id`. The probe leaves both unset and uses only `self`; it neither applies for free credits nor switches a workspace or account. Documented error statuses include `401` and `503` with `status`, `message`, and `reason` fields.

`balance-valid` and `balance-repeat` record real authenticated bodies; `balance-missing-auth` and `balance-invalid-auth` capture actual omitted/invalid-authentication failures. An error body is saved even when the response is unsuccessful. Wallet `_id` and `user_id` are sanitized before publication, with original/stored hashes retained; public voice-owner identifiers are not affected. Read failures retain their actual received prefix with `complete: false`. See the [evidence index](../evidences/2026-10-20/manifest.json) for the captured account result; public documentation examples are not balance evidence.

This is a read-only operation. It does not top up, enroll in an offer, grant credit, or modify billing. Do not interpret unchanged rounded balance strings alone as proof that every synthesis request was unbilled; the probe additionally pins the documented free model and enforces its local UTC cutoff.
