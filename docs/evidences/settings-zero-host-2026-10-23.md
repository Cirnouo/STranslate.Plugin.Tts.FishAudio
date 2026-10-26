# Positive-zero Volume acceptance in STranslate

Observed on 2026-10-23 UTC in Windows, STranslate 2.0.10 running normally, with private validation plugin version `1.1.2.19`. This follow-up covers the reported `-0.00` after approaching zero from a negative Volume value. It does not add a synthesis, listening or latency measurement.

## Package identity

The validation package has the same 19 entries as the package built from the reviewed source state on main `fca628447eced3f23e0a94fb5d1bbf9626ead278` plus this change. Only the manifest Version differs; all other manifest fields and payload bytes match.

| Artifact | SHA-256 |
|---|---|
| Source SPKG | `681a16a75a64ee2645479c2600861b4904b14493015f4f2f41ffbd6288aa8733` |
| Validation SPKG | `7dc941908703150d19945e01cad9d8339b22a51f0ddd705c118801574ac45ffa` |
| Installed plugin DLL | `a341d8029928044300b637c4b82421ce44aca3ac022eb30c05304eea8cb9f400` |

## Installed-host observations

| Action | Observed result |
|---|---|
| Open Volume's numeric editor, set `-0.01` through its accessibility value interface and click outside to commit. | Slider and displayed value become `-0.01`. This is not a physical typing claim. |
| Move one wheel notch upward over the actual slider thumb. | Label and the visible native value bubble both show `0.00`. |
| Drag the native thumb from zero into the negative range and back over the same pointer distance. | Display changes `0.00 → -1.18 → 0.00`; the final slider accessibility value is zero. |
| Use the slider wheel to move `0.00 → 0.01 → 0.00`. | The final label shows `0.00`, matching the negative approach. |
| Use the resting numeric text's wheel to move `0.00 → -0.01 → 0.00`. | The final label shows `0.00` and the slider stays synchronized. |

The final settings file contains positive-zero Volume. Of the 16 serialized settings fields, the 15 fields other than CachedVoice retain their compared values. Within CachedVoice, only SampleAudioUrl refreshed during host startup; its other fields and the separately stored selected VoiceId remain unchanged. The favorite file is byte-identical, and all 157 pre-existing covers retain both content hashes and modification times. Fresh private snapshots were used; earlier acceptance baselines were not restored.

No formal TTS or preview playback was invoked. Computer control was released after these observations. The [compiled zero-boundary scenarios](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/Input/SettingsInputInteractionSuite.cs) separately cover both cultures, two widths, direct `-0.00` commit, signed-zero storage repair, notification/save counts and range boundaries. Those fixtures do not claim installed-host observations for every scenario. See the [current settings contract](../specs/settings-and-model-policy.md) and [DD-058](../adr/current/DD-058.md) for supported input boundaries.
