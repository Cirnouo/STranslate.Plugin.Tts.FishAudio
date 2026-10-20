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
