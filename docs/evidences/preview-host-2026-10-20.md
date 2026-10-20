# Preview acceptance in STranslate 2.0.10

## Build and scope

Observed on 2026-10-20, approximately 12:31–12:34 UTC, on Windows in STranslate 2.0.10 with a normal-permission process. The plugin package was built using `./build.ps1 -Release -Clean -Test`; the complete regression runner passed. The original SPKG SHA-256 was `08A619EBB54C553AE03E1FCF175868E7B3BA5D827A1E0498D449AC2E07D10903`.

The host rejects an upgrade with an equal manifest version. A private validation package changed only `plugin.json` Version from `1.1.2` to `1.1.2.3`. Every other ZIP entry was checked byte-for-byte by SHA-256 against the original. This validation manifest is not a published release version. The host installed the package, restarted and logged successful plugin loading. Its installed directory contained the plugin, NAudio.Core 2.3.0 and NAudio.WinMM 2.3.0, without a second host SDK or UI framework.

The public voice “Eileen 3” was found through the plugin's search and used for both entry points. Only existing voice metadata and sample assets were requested; these actions did not invoke synthesis.

## Observations

| Configuration | Entry point | Observed result |
|---|---|---|
| API Key configured | Search-result sample button | Playback began; the play glyph became a stop glyph and the progress ring advanced. |
| API Key configured | Selected-voice sample button after selecting the same result | Playback began and the selected card displayed the advancing progress ring. |
| API Key empty | Selected-voice sample button | Playback began with an empty Key field. A second click stopped playback and removed the progress ring. |
| API Key empty | Search-result sample button in the existing results | Playback began; matching selected/search cards shared playback state. Without further input, playback ended and both cards returned to play glyphs with no progress ring. |

Selecting the search result stopped its prior preview before publishing the new selected card. The original API Key was restored after the keyless checks. No credential, local profile, machine path or screenshot is included in this record.

## Interpretation and limits

These observations exercise the installed plugin, native MP3 decoding, output initialization, UI dispatch, cancellation by a second click and natural completion. The displayed progress is derived from `WaveOutEvent.GetPosition()`, rather than decoder read-ahead, so an advancing ring establishes output-position progress. This record does not claim a human listening assessment or an independent acoustic recording, and it does not measure cloud TTS latency.

Timeout, oversized-body, failed-output, stale-response, expired-URL and cancellation race cases are covered by the offline [preview regressions](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/PreviewDownloadSuite.cs). They are distinct from these real-host observations. Raw provider endpoint responses remain in the separate [API evidence collection](2026-10-20/README.md).

## Preparing-state follow-up

The [request-state follow-up](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/issues/6) was checked in the same host around 13:20–13:23 UTC. Its source SPKG SHA-256 was `8364968331e7f8f514485d87e1b743d9c0c723b84f6e6d5517d27428c4284ae0`. A private manifest-only version `1.1.2.4` allowed installation; all non-manifest entries were checked identical to that source package. The host installed and loaded it successfully.

For the selected “Eileen 3” card, a click visibly displayed a rotating ring with a square center; the accessible button name became “正在准备试听… 点击取消”. The next immediate click restored the play glyph and “试听” name. A later observation remained idle. Starting the search-result card showed the same preparing/cancel state simultaneously on the matching selected and search cards. Clicking that search button again restored both to idle, which a subsequent observation confirmed.

The real asset acquisition can complete between UI observations, so these observations do not measure a precise pending duration or prove which network substep was active at the second click. The held-response [preparation regressions](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/PreviewPendingStateSuite.cs) separately establish publication before the first await and cancellation while requests are unresolved. This follow-up used existing public samples and did not invoke TTS.
