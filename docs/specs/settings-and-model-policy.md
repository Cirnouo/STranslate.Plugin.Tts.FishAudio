# Settings and TTS model policy

Status: current implementation; schema version is **1**. See [Settings](../../STranslate.Plugin.Tts.FishAudio/Configuration/Settings.cs), [migrator](../../STranslate.Plugin.Tts.FishAudio/Configuration/SettingsMigrator.cs), [converter](../../STranslate.Plugin.Tts.FishAudio/Configuration/SettingsJsonConverter.cs) and [normalizer](../../STranslate.Plugin.Tts.FishAudio/Configuration/SettingsNormalizer.cs).

Canonical JSON writes SchemaVersion first. Missing or invalid version markers are interpreted as legacy version 0 and migrate to 1. Valid current VoiceId/CachedVoice values take precedence over ReferenceId/CachedModel independent of property order; wrong-type current fields may recover valid legacy fields. Ordinary malformed/duplicate properties fall back independently. Supported schemas are canonically saved after migration/normalization or unknown/deleted-field cleanup. A duplicate SchemaVersion or future schema is read-only: compatible fields remain usable in memory but host saves are skipped.

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
| MP3 bitrate | 192; supported values 64/128/192. |
| Condition on previous chunks | true; describes chunks within the same synthesis request. |

Non-finite/out-of-range numeric values use their defaults; valid values snap to their step with midpoint-away-from-zero rounding. Schema 1 has no output-format, PCM sample-rate or WebSocket settings. CachedVoice contains selected voice display metadata and a sample URL; it is not an audio-byte cache.

[FishAudioModelPolicy](../../STranslate.Plugin.Tts.FishAudio/Configuration/FishAudioModelPolicy.cs) currently uses an exclusive cutoff of `2026-12-01T00:00:00Z`. Before it, the ordered list is `s2.1-pro-free`, `s2.1-pro`, `s2-pro`, `s1`, and the default is `s2.1-pro-free`. At/after it, the list is `s2.1-pro`, `s2-pro`, `s1`, and the default is `s2.1-pro`. List order and default selection are distinct functions. Existing valid selections are not replaced merely because the list is reordered. An unavailable selection normalizes through the default policy.

`normalize_loudness` is supported in the plugin for the three S2-family choices and omitted for `s1`; its UI stays visible but disabled for `s1`. The promo card has a stable placeholder; clicking selects the free model without dismissing the card, while close persists dismissal. Startup uses local UTC first and a three-second online UTC attempt with local fallback.

These are current application rules, not independent evidence of provider billing eligibility. Apply the [zero-cost testing policy](../policies/api-evidence.md) before any real synthesis.

Verification: [settings storage suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/SettingsStorageSuite.cs), [API Key compatibility suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/ApiKeyCompatibilitySuite.cs), [presentation suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/PresentationContractSuite.cs). Rationale: [DD-038](../adr/current/DD-038.md), [DD-045](../adr/current/DD-045.md), [DD-046](../adr/current/DD-046.md).
