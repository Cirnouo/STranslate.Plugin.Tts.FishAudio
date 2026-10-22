# Favorites and settings-layout acceptance in STranslate 2.0.10

The `.10`/`.11` observations below are historical build observations. Their editable connection modes, custom-address card and the related layout regression were superseded by the later decision to use the system proxy without plugin proxy settings. They do not describe a current configuration interface.

## Build and scope

Observed on 2026-10-20 around 15:27–15:43 UTC in the normal-permission Windows STranslate 2.0.10 host. Both the isolated storage/cache foundation and the complete feature passed `./build.ps1 -Release -Clean -Test`, including package inspection and the executable offline regression runner.

The first full-feature source SPKG SHA-256 was `a191435fad5fb59d8f0433039204ea8fee12d457a87e4bdb04af2de2f9716220`. Its private validation version was `1.1.2.10`, with SPKG SHA-256 `3827f615dc6e2e21af91eb9ecad3b3324e4b9bdf7b1b4ce04b29356b4b50decd`. A later one-property layout correction and its regression produced source SPKG `701611275beec487f5f4fcf87dadf7f877cf97d0c075d1cda76f3d5f77c46f1e`; private version `1.1.2.11` had SHA-256 `61423834780246350c25cdfcd66a18d54a5f5b0976b37c0c15e73b865073423f`.

Each private package changed only the manifest version because the host rejects equal-version upgrades. Every non-manifest ZIP entry was verified byte-for-byte against its source package. These versions are validation artifacts, not published releases. The host installed and restarted for both checks. Acceptance actions used public voice metadata and existing sample assets; no TTS synthesis was invoked. This is not an exhaustive audit of host startup/background HTTP requests.

## Observations

| Scenario | Installed-host observation |
|---|---|
| Search and stars | Searching `Eileen` returned 200 reported results and six cards per page. Six first-page stars changed from outline to gold; adding one second-page card produced seven favorites. |
| Search fold | The count stayed on the left with one icon-only toggle on the right. Mouse collapse and Space expansion preserved the query and second-page cards. Search remained collapsed after switching through the other panels. |
| Favorites and paging | Favorites initially expanded independently of Search and showed seven IDs, six cards per page. Removing the only second-page item immediately showed six items on the first page, with no now-unnecessary second page. |
| By-ID lookup | Confirmation displayed one Preview/star/Apply card without pagination or a fold control. A private settings comparison confirmed lookup retained the selected voice. Only Apply changed it. The ID card's star also added the voice to Favorites. |
| New preview entries | By-ID and Favorites sample buttons showed an active ring and square stop state, then returned to idle. These observations do not establish precise cancellation timing or a listening assessment. The held-response and originating-card races are covered separately offline. |
| Durable file | Inspection of `favorite-voices.json` established a JSON array of seven canonical ID strings only. The host profile stayed at schema 2 with no favorites field. No metadata, image copy or credential was written into the bookmark array. |
| Restart | After installing the final layout build and restarting the host, all seven favorites remained. Cold cards initially showed IDs and then populated public details. The original selected voice and MP3/192 settings survived. |
| PCM and bitrate | PCM hid the entire bitrate card, including its title and spacing. Switching back to MP3 restored the original 192 kbps value. |
| Network grouping | Reading Network appeared separately after generation settings, with formal-reading-only help and a System default. The globe/link icons and header text aligned with existing cards. Custom showed its address card; returning to System hid it. |
| Narrow content alignment | The first build exposed a centered custom-address field/error line, roughly 24 pixels right of the normal content column. The final build aligned the field, visible validation text and header at the same left edge in the approximately 402-pixel host pane. |
| Cleanup | All seven task-created favorites were removed through their stars. The count reached zero while the selected voice remained. Private comparisons confirmed the original Key, voice, model, bitrate, generation preferences, System connection and retained proxy address. |

Two existing per-ID cover files retained identical hashes and modification times across the restart and panel checks. This establishes their continued reuse as files, not a live HTTP request count. The offline shared-cover regression separately counts downloads and verifies that clearing cover images preserves durable IDs and shared metadata.

## Verification boundaries

At the time of the `.11` check, the narrow-layout regression reproduced a `22.825688`-pixel extra inset before the fix, compared the compiled address/validation geometry at 400 and 600 pixels, and passed after a one-property XAML correction. Its custom-address assertions were subsequently removed with the proxy settings. The current [SettingsLayoutSuite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/SettingsLayoutSuite.cs) remains the entry point for implemented settings interactions; historical results do not require restoring removed controls.

Deterministic [storage/cache](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/FavoriteStorageCacheSuite.cs), [settings independence](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/FavoriteSettingsIntegrationSuite.cs) and [browsing](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/FavoriteVoicesSuite.cs) tests cover damaged files, bounded persistence, lease retirement, cache TTL/LRU, current-page failures, stale results and removed-card preview ownership. Those fixtures are not provider responses. A loading ring alone is not evidence that caching is absent, and an unchanged cover file alone cannot prove that no network access occurred. No reusable audio-byte cache or acoustic measurement is asserted.

After these checks, the computer-control session was released. No credential, full profile, local machine path or screenshot is included in this record.

## Final controls and numeric-editing acceptance

The later checks on 2026-10-20 UTC used STranslate 2.0.10 with private validation versions `1.1.2.12` and `1.1.2.13`. The final configuration comparison was recorded at 16:58:59 UTC. The `.12` source SPKG SHA-256 was `b5679256c3b33ca9ccf3de2dd6780d374bbd97ba59b009b5894ecc737e46ed24`; its version-only validation package was `1b4f12a0b4eea9751cac963dd910ab26dcdbbd7e9ed1ab5d7144f6cd193cc20b`. The `.13` source package was 520,413 bytes with SHA-256 `4b201adbd0a9844602af3de7a7174f26f7ae66937ddee440fb0a23cbc78db405`; its validation package was `2b934c71140fa4bf508117401fb37eaf87e705f432c3d438cc67fbc60ed042bb`. For each version, all non-manifest ZIP entries were verified byte-for-byte against the source package.

| Scenario | Installed-host observation |
|---|---|
| Shared selection | The selected voice and its matching Search result both showed Cross. Clearing from Search changed the selected area to random voice and the result to Tick, while retaining its favorite star. Applying from Favorite Voices restored the selected card and matching Cross. Looking up the same ID showed Cross without changing selection; that Cross cleared selection, and its subsequent Tick reapplied it. |
| Favorite ownership | The selected-card star synchronized with the matching Search, By-ID and favorite cards. Removing the only favorite from Favorite Voices immediately changed its exact count to zero and removed the result; the applied voice remained selected and its star became an outline. The temporary favorite was removed before completion. |
| Input and action presentation | API Key and By-ID Paste controls appeared within the right input edge; the full public voice ID remained readable before the reserved paste area. By-ID used the same magnifier as Search. Tick/Cross appeared without a rectangular idle surround, with a circular focus/hover treatment. The Favorites tab included its icon and the updated label. |
| Format card | Format icon, header, description and control followed the native card content column. Switching PCM to MP3 showed the retained 192 kbps setting; switching back hid its entire card. |
| Network and model policy | The obsolete plugin connection controls and network group were absent. S1 and Drama 3 hid the complete loudness card; S2 Pro restored it with its retained value and the S2-family description. These were UI changes only: no synthesis was invoked for any selected model. |
| Direct editing | Each numeric text label opened an input at the same location with the same bounds. Speed `1.23` and Volume `-2.3` committed and moved their sliders. Clicking another numeric label or blank space committed; Enter also committed. The `dB` unit stayed outside the input. |
| Precise persistence and invalid input | Temperature `0.82` and Top P `0.83` remained unchanged after saving. Entering Top P `0` and moving focus restored `0.83`. After installing `.13` and restarting the application, both off-tick values still displayed `0.82`/`0.83`. |
| Native theme viewport defect | `.12` exposed the host's automatic TextBox clear button inside the compact editor; it clipped a valid `1.23` draft. `.13` disabled that button locally. Real focused inputs then displayed complete numeric text, including `-10.0`, without changing the outer size or moving `dB`. Escape cancelled the Volume draft and restored `0.0`. |
| Cleanup | Temporary numeric values were restored to the latest pre-acceptance preferences. A private comparison confirmed API Key, applied Voice ID, model, all generation preferences and retained bitrate; PCM remained selected. Schema stayed 2, retired proxy fields were absent, and the favorite file was an empty ID array. Both previously sampled cover files retained their hashes and modification times. |

The native viewport regression separately reproduces the clipping with a setter taken from the installed theme and checks actual content-host width against formatted `1.23` and `-10.0` glyphs. That offline style-state fixture does not claim physical focus; the `.13` observations above provide the separate installed-host check. Layout and numeric test groups use the same shared STA with separate unchanged per-group deadlines. A first combined-group timeout was retained as a failed validation attempt; the final complete `./build.ps1 -Release -Clean -Test` passed with zero warnings/errors, package inspection, regressions and cleanup.

This final acceptance exercised UI, existing public metadata and local persistence. It did not invoke TTS or provide a new acoustic, billing or exhaustive HTTP measurement. Earlier streaming and preview acceptance retain their own evidence boundaries. The computer-control kernel was reset after the final checks.

The final input and navigation follow-up is recorded in [2026-10-22 acceptance](favorites-controls-host-2026-10-22.md), including the shorter Favorites label, accent selection, native content alignment and stricter draft constraints.
