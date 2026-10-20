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
