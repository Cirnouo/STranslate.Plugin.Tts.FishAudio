# Numeric wheel and focus acceptance — 2026-10-23 UTC

## Build identity

STranslate 2.0.10 on Windows loaded private validation version `1.1.2.18`. The executable source snapshot corresponds to feature commit `b0d3ff0`; it was built from the frozen working tree based on `1dbd327`. The source SPKG SHA-256 is `a6980c6a3bf300ec4404bea78b138f4ecd98c7ecd3a85a84ff1ace366a356c12`. The Version-only validation package SHA-256 is `7d07310dc8431cf657d5e2bbf904dcb124f46fd5b5c8c280eacca0c4a029147c`; all non-manifest payload entries were identical, and only `plugin.json.Version` changed. All 19 installed package entries matched. The installed plugin DLL SHA-256 is `757ca57f4be4cf538421f5f06db9d61afd57e6f9e942e8977694be398d8983fc`.

The full `./build.ps1 -Release -Clean -Test` run passed with zero warnings/errors, required runtime/package checks, complete regressions and cleanup. All 728 frozen public files matched afterward. Its source-manifest SHA-256 is `d339da57844a078e1b8f62c1f29acd7de3c91ecfc5e5f615fdafec65628e5109`; the Release log SHA-256 is `58c61c1e4683c7e7154d7ab1d2d6d0f5bd491ad511234545e1f176a454caf694`. This acceptance note and its index link were added afterward without changing executable source or fixtures.

## Installed-host observations

| Scenario | Observation |
|---|---|
| Speed dragging | A short physical thumb drag changed `1.00` to `1.06`, displaying the native current-value bubble. Resting numeric-text wheel input then restored `1.00` in hundredth increments. |
| Slider wheel | One upward notch changed Speed `1.00 → 1.01`, Volume `0.00 → 0.01`, Temperature `0.00 → 0.01` and top_p `0.05 → 0.06`. The parameter rows stayed in place. Downward input restored all values. |
| Native value feedback | Wheel input produced a native current-value ToolTip, including a directly observed Speed `1.00` bubble above its thumb. The bubble's presence was also exposed by the host accessibility tree. This is separate from the compiled all-four-control lifecycle matrix. |
| Lower bound | Downward top_p input beyond its minimum stayed at `0.05` and did not scroll the page. |
| Volume direct entry | Opening the actual inline editor, entering `0.03` through its accessibility value interface and clicking outside committed the value. A private read of the host settings file confirmed Volume `0.03`; numeric-label wheel input restored `0.00`. This is not a claim about physical keystroke filtering. |
| Pointer focus | Clicking Favorites' fold control collapsed the result. Moving the pointer away without clicking another control left no persistent focus border. |
| Keyboard focus | Tab moved from the fold control to the model selector. Shift+Tab returned to the fold control with a visible contrasting outline; Space expanded the result. Search was restored afterward. |
| State preservation | All 14 compared user fields, including API Key, selected voice/model, numeric settings, format, bitrate and schema, matched the latest private pre-check snapshot. The one-entry favorite ID file was byte-identical. All 157 pre-existing cover files retained their hashes and modification times. |

The latest user preferences were captured immediately before this pass; earlier acceptance snapshots were not restored. The native app remained installed at the validation version, and the computer-control session was released after verification. No formal TTS invocation or new acoustic comparison was part of this UI pass. The earlier user-confirmed audio result remains in [four-buffer host acceptance](pcm-output-host-2026-10-22.md).

## Boundaries

[Compiled input tests](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/SettingsInputInteractionSuite.cs) cover all four sliders, native drag and small increments, exact save counts, persistence, bounds, partial deltas, separate text-editor scrolling, both themes' focus behavior and popup ownership through hidden presentation sources. They also cover native style/template agreement, current-culture content, repeated wheel updates, idle expiration, real dragging and template replacement. Hidden-source fixtures and this installed-host observation support different claims.

The [current precision contract](../specs/settings-and-model-policy.md#numeric-wheel-interaction) replaces the older mixed-step behavior recorded in earlier dated UI notes. The [API numeric schema research](../references/tts-fields.md#numeric-parameter-granularity) distinguishes the client's hundredth step from upstream bounds; this pass establishes no new live synthesis precision matrix.
