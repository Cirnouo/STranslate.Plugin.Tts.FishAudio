# Real API evidence: 2026-10-20

These are actual network responses, collected sequentially by the repository probe. No fixture is presented as a real response. See the [machine-readable run index](manifest.json), [API references](../../references/README.md), and [audio analysis](audio-analysis.json).

Only exact `s2.1-pro-free` synthesis was sent. No model fallback or automatic synthesis retry was enabled. The request model guard and UTC deadline fail closed. Read-only requests include deliberately missing or invalid authentication. Public resource downloads carry no Authorization header.

## Coverage

| Specification object | Real cases | Findings |
|---|---:|---|
| GET /model | 12 | Anonymous/dummy/key browsing, title search, second page, and a real empty search result. New approximate-total/window metadata is preserved. |
| GET /model/{id} | 14 | Two public voices, repeated reads, and nonexistent ID 404. |
| GET /wallet/self/api-credit | 6 | Successful reads before and after synthesis; missing/invalid authentication 401. |
| API-returned sample/cover resources | 8 | Distinct list/detail samples and cover, ordinary 200 and Range 206. The selected current URLs were unsigned; signed-URL redaction and expiry are offline coverage only. |
| POST /v1/tts | 5 | Repeated short English PCM and Chinese MP3 succeed; invalid audio format returns a genuine 400. The model header remains exactly the free ID even for the invalid-format request. |
| WS /v1/tts/live | 5 | PCM, MP3, segmented continuation, client cancellation after audio, and missing-auth handshake 401. |
| GET /compat/v1/models | 2 | Anonymous full catalog and speech filter; four TTS engines, no drama-3 entry. |

## Interpretation and limits

All three successful native WebSocket sessions received nonempty audio before sending the withheld tail and stop. The segmented case sends three further text events in one session. The trace records raw MessagePack and audio bytes, send/receive order, UTC and monotonic elapsed time. These probes demonstrate upstream duplex generation; they do not measure plugin output start, device drain, or guarantee cloud latency. Those belong to runtime integration.

The cancelled session is `cancelled-by-case`, not a service failure. Its audio is intentionally incomplete. The 400/401/404 results are actual server responses, not locally manufactured errors. Offline probe self-tests are kept outside this real-response index.

The observed wallet credit was `24.480430` before synthesis and `24.480430` afterward. Equality is supporting observation only; delayed accounting means it is not by itself proof of zero billing. Zero-cost behavior relies on the exact free model and refusal of fallback.

PCM interpretation is supported by byte parity, requested 44.1 kHz mono, and much smoother signed little-endian adjacent samples than big-endian interpretation. See the numeric report. Signedness and byte order remain an empirically supported client assumption, not an explicit official guarantee. MP3 responses were identified as 44.1 kHz mono / 128 kbps and fully decoded with FFmpeg without error. Sound-device validation is recorded with the runtime PR.

## Redaction and integrity

API keys, Authorization/cookies, proxy credentials, sensitive query parameters, and private wallet account IDs are removed from published bodies/metadata. Raw response byte counts and SHA-256 values are preserved separately from stored counts/hashes; JSON formatting can also change the stored hash. Public voice IDs and publicly returned voice metadata remain intact. Signed query parameters, when present, are stripped as a group and the credential-redaction flag identifies the transformation.

Every case links its sanitized request, response headers, body, capture completeness and outcome. A successful HTTP status alone does not mean the response body was captured completely. WS raw fragments/messages have explicit completeness metadata. Verify the stored hashes against the published bytes; raw hashes describe the pre-redaction bytes and cannot reconstruct removed credentials.

## Case index

| Case | Endpoint group | Status | Outcome | Bytes stored |
|---|---|---:|---|---:|
| [catalog-all](run-20261020T112556066Z-32c1bf7f671141179d631215fcd2a0e5/cases/catalog-all/case.json) | engine-catalog | 200 | success | 4877 |
| [catalog-speech](run-20261020T112556066Z-32c1bf7f671141179d631215fcd2a0e5/cases/catalog-speech/case.json) | engine-catalog | 200 | success | 3948 |
| [public-cover-full](run-20261020T112556066Z-32c1bf7f671141179d631215fcd2a0e5/cases/public-cover-full/case.json) | preview-assets | 200 | success | 22986 |
| [public-sample-full](run-20261020T112556066Z-32c1bf7f671141179d631215fcd2a0e5/cases/public-sample-full/case.json) | preview-assets | 200 | success | 405420 |
| [public-sample-range](run-20261020T112556066Z-32c1bf7f671141179d631215fcd2a0e5/cases/public-sample-range/case.json) | preview-assets | 206 | success | 1024 |
| [public-voice-detail-1](run-20261020T112556066Z-32c1bf7f671141179d631215fcd2a0e5/cases/public-voice-detail-1/case.json) | voice-detail | 200 | success | 2198 |
| [public-voice-detail-2](run-20261020T112556066Z-32c1bf7f671141179d631215fcd2a0e5/cases/public-voice-detail-2/case.json) | voice-detail | 200 | success | 5157 |
| [public-voice-detail-missing](run-20261020T112556066Z-32c1bf7f671141179d631215fcd2a0e5/cases/public-voice-detail-missing/case.json) | voice-detail | 404 | expected-http-error | 54 |
| [public-voices-default](run-20261020T112556066Z-32c1bf7f671141179d631215fcd2a0e5/cases/public-voices-default/case.json) | voices | 200 | success | 17046 |
| [public-voices-search](run-20261020T112556066Z-32c1bf7f671141179d631215fcd2a0e5/cases/public-voices-search/case.json) | voices | 200 | success | 7150 |
| [voices-anonymous](run-20261020T112556066Z-32c1bf7f671141179d631215fcd2a0e5/cases/voices-anonymous/case.json) | voices | 200 | success | 10413 |
| [authenticated-cover-full](run-20261020T112942161Z-b8c9ef1370e04ba282971e6e5ab7fa0e/cases/authenticated-cover-full/case.json) | preview-assets | 200 | success | 22986 |
| [authenticated-sample-detail-full](run-20261020T112942161Z-b8c9ef1370e04ba282971e6e5ab7fa0e/cases/authenticated-sample-detail-full/case.json) | preview-assets | 200 | success | 226114 |
| [authenticated-sample-detail-range](run-20261020T112942161Z-b8c9ef1370e04ba282971e6e5ab7fa0e/cases/authenticated-sample-detail-range/case.json) | preview-assets | 206 | success | 1024 |
| [authenticated-sample-full](run-20261020T112942161Z-b8c9ef1370e04ba282971e6e5ab7fa0e/cases/authenticated-sample-full/case.json) | preview-assets | 200 | success | 405420 |
| [authenticated-sample-range](run-20261020T112942161Z-b8c9ef1370e04ba282971e6e5ab7fa0e/cases/authenticated-sample-range/case.json) | preview-assets | 206 | success | 1024 |
| [authenticated-voice-detail-1](run-20261020T112942161Z-b8c9ef1370e04ba282971e6e5ab7fa0e/cases/authenticated-voice-detail-1/case.json) | voice-detail | 200 | success | 2198 |
| [authenticated-voice-detail-2](run-20261020T112942161Z-b8c9ef1370e04ba282971e6e5ab7fa0e/cases/authenticated-voice-detail-2/case.json) | voice-detail | 200 | success | 5157 |
| [authenticated-voice-detail-missing](run-20261020T112942161Z-b8c9ef1370e04ba282971e6e5ab7fa0e/cases/authenticated-voice-detail-missing/case.json) | voice-detail | 404 | expected-http-error | 54 |
| [authenticated-voices-default](run-20261020T112942161Z-b8c9ef1370e04ba282971e6e5ab7fa0e/cases/authenticated-voices-default/case.json) | voices | 200 | success | 17046 |
| [authenticated-voices-search](run-20261020T112942161Z-b8c9ef1370e04ba282971e6e5ab7fa0e/cases/authenticated-voices-search/case.json) | voices | 200 | success | 7150 |
| [balance-missing-auth](run-20261020T112942161Z-b8c9ef1370e04ba282971e6e5ab7fa0e/cases/balance-missing-auth/case.json) | balance | 401 | expected-http-error | 42 |
| [balance-valid](run-20261020T112942161Z-b8c9ef1370e04ba282971e6e5ab7fa0e/cases/balance-valid/case.json) | balance | 200 | success | 281 |
| [http-invalid-format](run-20261020T113028751Z-1a8010d96eee470688478546b4ca8ea6/cases/http-invalid-format/case.json) | http-tts | 400 | expected-http-error | 231 |
| [http-mp3-zh](run-20261020T113028751Z-1a8010d96eee470688478546b4ca8ea6/cases/http-mp3-zh/case.json) | http-tts | 200 | success | 20897 |
| [http-mp3-zh-repeat](run-20261020T113028751Z-1a8010d96eee470688478546b4ca8ea6/cases/http-mp3-zh-repeat/case.json) | http-tts | 200 | success | 21732 |
| [http-pcm-en](run-20261020T113028751Z-1a8010d96eee470688478546b4ca8ea6/cases/http-pcm-en/case.json) | http-tts | 200 | success | 102400 |
| [http-pcm-en-repeat](run-20261020T113028751Z-1a8010d96eee470688478546b4ca8ea6/cases/http-pcm-en-repeat/case.json) | http-tts | 200 | success | 102400 |
| [synthesis-discovery-voice-detail-1](run-20261020T113028751Z-1a8010d96eee470688478546b4ca8ea6/cases/synthesis-discovery-voice-detail-1/case.json) | voice-detail | 200 | success | 2198 |
| [synthesis-discovery-voice-detail-2](run-20261020T113028751Z-1a8010d96eee470688478546b4ca8ea6/cases/synthesis-discovery-voice-detail-2/case.json) | voice-detail | 200 | success | 5157 |
| [synthesis-discovery-voice-detail-missing](run-20261020T113028751Z-1a8010d96eee470688478546b4ca8ea6/cases/synthesis-discovery-voice-detail-missing/case.json) | voice-detail | 404 | expected-http-error | 54 |
| [synthesis-discovery-voices-default](run-20261020T113028751Z-1a8010d96eee470688478546b4ca8ea6/cases/synthesis-discovery-voices-default/case.json) | voices | 200 | success | 17046 |
| [synthesis-discovery-voices-search](run-20261020T113028751Z-1a8010d96eee470688478546b4ca8ea6/cases/synthesis-discovery-voices-search/case.json) | voices | 200 | success | 7150 |
| [synthesis-discovery-voice-detail-1](run-20261020T113058545Z-ba5e847ad6ae4191ae36f9da31870e83/cases/synthesis-discovery-voice-detail-1/case.json) | voice-detail | 200 | success | 2198 |
| [synthesis-discovery-voice-detail-2](run-20261020T113058545Z-ba5e847ad6ae4191ae36f9da31870e83/cases/synthesis-discovery-voice-detail-2/case.json) | voice-detail | 200 | success | 5157 |
| [synthesis-discovery-voice-detail-missing](run-20261020T113058545Z-ba5e847ad6ae4191ae36f9da31870e83/cases/synthesis-discovery-voice-detail-missing/case.json) | voice-detail | 404 | expected-http-error | 54 |
| [synthesis-discovery-voices-default](run-20261020T113058545Z-ba5e847ad6ae4191ae36f9da31870e83/cases/synthesis-discovery-voices-default/case.json) | voices | 200 | success | 17046 |
| [synthesis-discovery-voices-search](run-20261020T113058545Z-ba5e847ad6ae4191ae36f9da31870e83/cases/synthesis-discovery-voices-search/case.json) | voices | 200 | success | 7150 |
| [ws-cancel](run-20261020T113058545Z-ba5e847ad6ae4191ae36f9da31870e83/cases/ws-cancel/case.json) | ws-tts | 101 | cancelled-by-case | 40960 |
| [ws-missing-auth](run-20261020T113058545Z-ba5e847ad6ae4191ae36f9da31870e83/cases/ws-missing-auth/case.json) | ws-tts | 401 | expected-http-error | 0 |
| [ws-mp3](run-20261020T113058545Z-ba5e847ad6ae4191ae36f9da31870e83/cases/ws-mp3/case.json) | ws-tts | 101 | success | 42630 |
| [ws-pcm](run-20261020T113058545Z-ba5e847ad6ae4191ae36f9da31870e83/cases/ws-pcm/case.json) | ws-tts | 101 | success | 225280 |
| [ws-pcm-segmented](run-20261020T113058545Z-ba5e847ad6ae4191ae36f9da31870e83/cases/ws-pcm-segmented/case.json) | ws-tts | 101 | success | 1850090 |
| [authenticated-voice-detail-1](run-20261020T113301927Z-60d40c3b8caf46b99e44fa56de21cfbf/cases/authenticated-voice-detail-1/case.json) | voice-detail | 200 | success | 2198 |
| [authenticated-voice-detail-2](run-20261020T113301927Z-60d40c3b8caf46b99e44fa56de21cfbf/cases/authenticated-voice-detail-2/case.json) | voice-detail | 200 | success | 5157 |
| [authenticated-voices-default](run-20261020T113301927Z-60d40c3b8caf46b99e44fa56de21cfbf/cases/authenticated-voices-default/case.json) | voices | 200 | success | 17046 |
| [authenticated-voices-no-result](run-20261020T113301927Z-60d40c3b8caf46b99e44fa56de21cfbf/cases/authenticated-voices-no-result/case.json) | voices | 200 | success | 166 |
| [authenticated-voices-page-2](run-20261020T113301927Z-60d40c3b8caf46b99e44fa56de21cfbf/cases/authenticated-voices-page-2/case.json) | voices | 200 | success | 9292 |
| [balance-invalid-auth](run-20261020T113301927Z-60d40c3b8caf46b99e44fa56de21cfbf/cases/balance-invalid-auth/case.json) | balance | 401 | expected-http-error | 52 |
| [balance-missing-auth](run-20261020T113301927Z-60d40c3b8caf46b99e44fa56de21cfbf/cases/balance-missing-auth/case.json) | balance | 401 | expected-http-error | 42 |
| [balance-repeat](run-20261020T113301927Z-60d40c3b8caf46b99e44fa56de21cfbf/cases/balance-repeat/case.json) | balance | 200 | success | 281 |
| [balance-valid](run-20261020T113301927Z-60d40c3b8caf46b99e44fa56de21cfbf/cases/balance-valid/case.json) | balance | 200 | success | 281 |
