# Final input and navigation acceptance — 2026-10-22 UTC

## Build and scope

STranslate 2.0.10 on Windows loaded private validation version `1.1.2.14`. Its source SPKG was 521,779 bytes, SHA-256 `8a886375839b51f3e8b5657c46e68b2975e74ce184a8dda4ca4e49cdf1fbe1f4`; the version-only validation package was `fd82e7e2cfcf2e21cf742c2c10e4de3b3ebb1267e66b9c2d8c72c205b1d1bec3`. All non-manifest ZIP entries were verified byte-for-byte. The packaged plugin DLL hash was `e023d87d35745c422edc0aa310ce7e7802e039ac27d0db0bc898061ac9ca7c1f`.

The final source passed `./build.ps1 -Release -Clean -Test`, with zero build warnings/errors, package inspection, the complete offline regression runner and intermediate cleanup. The same 392 source/fixture hashes remained unchanged after verification. Earlier feature acceptance is recorded [separately](favorites-host-2026-10-20.md); this follow-up exercises the final navigation, input and alignment changes, rather than claiming every earlier scenario was repeated.

## Installed-host observations

| Scenario | Observation |
|---|---|
| Navigation selection | Search, By ID and the renamed Simplified Chinese Favorites tab (`收藏夹`) used a persistent host-accent background. Moving the pointer away retained the selected background. Switching through all three tabs kept neighboring controls in place; no selected-label weight change was visible. |
| Empty query layouts | Switching between empty Search and By ID retained equal query-row height and a stable following Model heading. The former shorter By-ID input and extra empty-result spacing were absent. |
| Voice actions | The selected voice's star, preview and Cross appeared on the same horizontal centerline. The full four-card/state matrix is a compiled-control test, not a claim of repeated live loading of every card in this check. |
| Voice ID edits | A partial `abc` remained unchanged after typing `g`. A full 32-character lowercase hexadecimal draft was readable beside the reserved Paste area; an additional lowercase `a` keypress was rejected. The temporary draft was cleared without querying or changing the selected voice. No native clear button appeared. |
| Slider placement | All four slider tracks began at the existing native content column in the approximately 402-pixel pane. The previous additional right inset was absent. |
| Numeric drafts | Speed opened at the numeric label's existing bounds. `1.23` remained unchanged after an extra digit and an invalid letter replacement; Escape restored `1.00`. Volume accepted the complete `-10.0` draft, with `dB` outside and no clear adornment; Escape restored `0.0`. No temporary numeric value was retained. |
| Configuration and caches | A private comparison at 17:30 UTC confirmed the latest pre-check API Key, Voice ID, model, all generation values, bitrate and PCM choice were retained. Schema remained 2, retired proxy fields were absent, and the favorites file remained an empty ID array. The two previously sampled cover files retained hashes and modification times. |

This was UI and local-persistence acceptance. No TTS command, paid-model synthesis, acoustic measurement, billing measurement or exhaustive network audit was performed. The computer-control kernel was reset immediately after the checks.

## Deterministic regression boundaries

[SettingsControlAlignmentSuite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/SettingsControlAlignmentSuite.cs) exercises navigation positions and selected native backgrounds/paired foregrounds in all five locales at 400/600 pixels. Separate fixtures cover native responsive content slots, visible icon drawing bounds, focus-state viewports, complete Voice ID composition/paste replacement, and stable empty-query geometry; those other fixtures are not all repeated in every locale. Native font ink permits a 0.25-DIP tolerance; Tick's approximately 0.135-DIP bearing is retained without replacing native text rendering. These are geometric fixture observations, not a physical-pixel or listening guarantee.

Meaningful pre-fix failures included a selected font-weight change, a four-DIP empty-mode Model-heading displacement, and a whitespace-suffixed raw ID issuing one detail GET. The final strict query test rejects whitespace, URLs, uppercase, short and overlength identifiers with zero detail GETs and unchanged selection/favorites, then verifies exact-ID inspection and explicit Apply.

[InlineNumericEditorSuite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/InlineNumericEditorSuite.cs) separately covers all four bounds/precisions, selected replacement, whole-payload paste rejection, incomplete drafts, decimal cultures, excess digits, commit validation, and unchanged storage precision. Each culture executes on the shared STA with its own unchanged deadline; a prior grouped timeout is a failed attempt, not a passed validation. Native PasswordBox reveal-space behavior is covered by the compiled theme-state fixture; this check did not edit or disclose the API Key.
