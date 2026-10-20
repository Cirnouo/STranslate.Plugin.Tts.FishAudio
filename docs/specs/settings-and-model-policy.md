# Settings and TTS model policy

Status: current Unreleased implementation; schema version is **2**. See [Settings](../../STranslate.Plugin.Tts.FishAudio/Configuration/Settings.cs), [migrator](../../STranslate.Plugin.Tts.FishAudio/Configuration/SettingsMigrator.cs), [converter](../../STranslate.Plugin.Tts.FishAudio/Configuration/SettingsJsonConverter.cs) and [normalizer](../../STranslate.Plugin.Tts.FishAudio/Configuration/SettingsNormalizer.cs).

Canonical JSON writes SchemaVersion first. Missing or invalid version markers are interpreted as legacy version 0. Supported schema 0/1 settings migrate to 2 with MP3 as the missing/malformed/duplicate/unsupported audio-format fallback, preserving valid bitrate. Explicit supported PCM/MP3 choices survive. New configurations and current schema-2 invalid/missing formats default to PCM. Valid current VoiceId/CachedVoice values take precedence over ReferenceId/CachedModel independent of property order; wrong-type current fields may recover valid legacy fields. Ordinary malformed/duplicate properties fall back independently. Supported schemas are canonically saved after migration/normalization or unknown/deleted-field cleanup. A duplicate SchemaVersion or future schema is read-only: compatible fields remain usable in memory but host saves are skipped.

API Key input changes, including clearing the password field, update the ViewModel and settings storage immediately before focus leaves the control. The password binding explicitly uses `TwoWay` and `PropertyChanged`: SDK 1.0.15 already defaults to two-way binding, but its default `LostFocus` update trigger would delay this save. Editing the key does not itself call Fish Audio.

API Key and By-ID paste buttons sit inside their inputs at the right edge. The editable content reserves that width so long text, password bullets and the caret do not overlap the button. The By-ID lookup action remains outside the input and uses the same magnifier as Search. Inline layout does not change clipboard, immediate-save, key-lock or lookup-versus-selection behavior.

| Setting | Current default / normalization |
|---|---|
| API Key / Voice ID | Empty; text is not silently discarded by numeric normalization. API Key accepts the legacy or prefixed shape; Voice ID keeps the existing 32-lowercase-hex validator. |
| TTS model | Determined by the UTC model policy below; valid saved selection is preserved. |
| Speed | 1.0; range 0.5–2.0, step 0.05. |
| Volume | 0.0 dB; range -10–10, step 0.1. This is not a client 0–1 mute/volume scale. |
| Normalize loudness | true; sent only for supported TTS models. |
| Temperature / top_p | 0.7 / 0.7; range 0–1, step 0.05. |
| Latency | `normal`; supported values `normal`, `balanced`, `low`. |
| Normalize text | false. |
| Audio format | PCM for new configurations; PCM/MP3 choices; legacy migration defaults to MP3. |
| MP3 bitrate | 192; supported values 64/128/192. Retained under PCM; the whole bitrate card is hidden for PCM, visible for MP3, and the value is sent only for MP3. |
| Condition on previous chunks | true; describes chunks within the same synthesis request. |

Non-finite/out-of-range numeric values use their defaults; valid values snap to their step with midpoint-away-from-zero rounding. PCM sample rate is fixed at 44100, not exposed as another setting. All new fields participate in conversion, cloning, transactional save/rollback, ViewModel initialization and auto-save. CachedVoice contains selected voice display metadata and a sample URL; it is not an audio-byte cache.

REST metadata, credit and sample downloads retain host HTTP routing. Native WebSocket always uses the system proxy (`HttpClient.DefaultProxy`) and exposes no plugin proxy settings or network group. Former `StreamingProxyMode` and `StreamingProxyAddress` fields are obsolete: their values are ignored and removed on ordinary canonical saves without changing schema 2. Old direct/custom/invalid values cannot affect routing or block synthesis. Future/duplicate-schema read-only protection remains active. Private request values are captured in a class without generated record value printing; raw exception contents and API Keys are never logged. See [DD-055](../adr/current/DD-055.md).

Favorites do not add any Settings property or change schema 2. Their separate ID-only file uses the SDK's persistent `PluginSettingsDirectoryPath`; the shared in-memory metadata cache and existing disposable cover-image cache have different lifetimes. See [voice browsing and preview](voice-browsing-and-preview.md) and [DD-054](../adr/current/DD-054.md).

Audio-format settings use the same native SettingsCard header icon, header, description and content slots as existing settings. [Compiled layout tests](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/SettingsLayoutSuite.cs) exercise native card geometry and PCM visibility; [installed-host acceptance](../evidences/favorites-host-2026-10-20.md) distinguishes superseded proxy controls from the final interface.

[FishAudioModelPolicy](../../STranslate.Plugin.Tts.FishAudio/Configuration/FishAudioModelPolicy.cs) uses an exclusive cutoff of `2026-12-01T00:00:00Z`. Before it, the ordered list is `s2.1-pro-free`, `drama-3`, `s2.1-pro`, `s2-pro`, `s1`, and the default is `s2.1-pro-free`. At/after it, the list is `drama-3`, `s2.1-pro`, `s2-pro`, `s1`, and the default is `s2.1-pro`. List order and default selection are distinct functions. Existing valid selections are not replaced merely because the list is reordered. An unavailable selection normalizes through the default policy. Streaming checks exact membership before connect/send and during its watchdog, including the free cutoff.

`normalize_loudness` is enabled by the explicit three-model S2-family capability policy. The complete card is hidden and the field omitted for `s1` and `drama-3`, while its stored value survives switching. Official documentation says the field has no effect on S1; Drama 3's effect is unconfirmed, so its omission is client policy rather than proof of provider incompatibility. The description says the setting supports the S2 family. Drama 3 request/UI compatibility has offline coverage; no paid real test is asserted. The promo card has a stable placeholder; clicking selects the free model without dismissing the card, while close persists dismissal. Startup uses local UTC first and a three-second online UTC attempt with local fallback.

**Future maintenance fact, verified 2026-10-20:** the [official native WS page](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live) says S1 is deprecated and retires on December 31, 2026, after which S1 requests are served by S2.1-Pro. The current client list deliberately retains S1; no client migration is implemented here. Revisit the list and user guidance before retirement. Exact client selection cannot override a future provider-side migration.

These are current application rules, not independent evidence of provider billing eligibility. Apply the [zero-cost testing policy](../policies/api-evidence.md) before any real synthesis.

Verification: [favorite/streaming integration suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/FavoriteSettingsIntegrationSuite.cs), [favorites suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/FavoriteVoicesSuite.cs), [streaming settings suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/StreamingSettingsSuite.cs), [settings storage suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/SettingsStorageSuite.cs), [API Key compatibility suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/ApiKeyCompatibilitySuite.cs), [presentation suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/PresentationContractSuite.cs). Rationale: [DD-038](../adr/current/DD-038.md), [DD-045](../adr/current/DD-045.md), [DD-046](../adr/current/DD-046.md), [DD-053](../adr/current/DD-053.md).
