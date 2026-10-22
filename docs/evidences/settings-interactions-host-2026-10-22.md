# Settings interaction acceptance — 2026-10-22 UTC

## Build identity and scope

STranslate 2.0.10 on Windows loaded private validation version `1.1.2.15`. The source SPKG was 523,310 bytes, SHA-256 `70cd2f92a1b63cfa6baaf76f8a8d682549f1d87969b3001121065f7094fda57a`; the version-only validation package hash was `028576e5f977fcddc07fe424b1dfde68d2d2c2724705b889ed121aabfcf19ed6`. All non-manifest ZIP entries were identical. The packaged plugin DLL hash was `b3bcac7831d8cae7278866127146a5daf6dd1093cb166e714d3beb5d36072736`.

The frozen source passed `./build.ps1 -Release -Clean -Test` with zero warnings/errors, the complete offline regression runner, package checks and intermediate cleanup. All 393 source/fixture hashes matched after verification. The source manifest hash was `553897b388154bddb4052fb0212ed9cfe3c2f14ed2262e14a0b99b7d86547617`; the release log hash was `b69de058d73114a4eff2bf08df89bd035879af822240c16b1478afbbd897c19e`. This acceptance note was added afterward without changing executable source or fixtures.

## Installed-host observations

| Scenario | Observation |
|---|---|
| Copy a voice ID | Clicking the selected voice's title displayed the host's floating success Snackbar, `声音 ID 已复制`. Pasting into the By-ID draft produced that same canonical ID. The temporary draft was cleared without querying, changing selection, or playing audio. |
| Selected navigation | Switching to By ID and Favorites retained the accent on the selected tab under the pointer. The neighboring tabs stayed in place. Held-pointer pressed state and light theme were checked by native-theme fixtures, not by this installed-host pass. |
| By-ID input | Its visible text baseline matched the existing Search row. Only By-ID padding changed; the Search input retained its original style. The Paste reserve remained separate from the text viewport. |
| Empty Favorites after collapse | A temporary bookmark produced one result and a fold control. After collapsing and removing that last bookmark from the selected card, only the localized empty hint remained. Count, fold and pagination were absent; the selected voice stayed applied. |
| Resting numeric labels | One upward wheel notch changed Speed `1.00 → 1.01`, Volume `1.0 → 1.1`, Temperature `0.20 → 0.21`, and Top P `0.25 → 0.26`. The page stayed in place while the pointer was on each label. Matching downward notches restored the original values. |
| Editing numeric values | After opening the Speed editor at `1.00`, a downward wheel notch scrolled the settings pane while leaving the edited value at `1.00`. Enter ended editing without changing it. Scrolling elsewhere in a card continued to move the page. |
| Persistence and cleanup | A private comparison confirmed the latest pre-check API Key, Voice ID, model, all generation values and bitrate were retained. PCM and schema 2 remained selected; retired proxy fields stayed absent. Favorites returned to an empty ID array. Two previously sampled cover files retained their hashes and modification times. |

No TTS command, paid synthesis, microphone capture or acoustic assessment was part of this UI check. The computer-control kernel was reset immediately afterward. The reported intermittent PCM crackle is a separate investigation; this UI validation does not claim to resolve it.

## Offline regression boundaries

[SettingsInputInteractionSuite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/SettingsInputInteractionSuite.cs) verifies actual native button-template overlay rendering in both themes and all five locales, stable navigation geometry, By-ID caret/viewport alignment against unchanged Search, and keyboard focus behavior. It routes wheel events from the resting glyph and the actual editing content host, checks bounds, partial notches, units/card passthrough, and all four display precisions. It also covers last-bookmark removal while collapsed, distinct storage errors, all four title locations, clipboard failure, and unchanged selection/favorites/preview/API state.

Clipboard contract tests use an injected writer rather than the system clipboard. The installed-host copy-and-paste observation above exercises the real clipboard and floating host notification. Author and description controls remain ordinary text; only the title executes the copy command.
