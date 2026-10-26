# API evidence

Captured snapshots remain byte-identical to their manifests, including historical source paths. The current [regression catalog](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Runner/RegressionCatalog.cs) identifies test ownership after domain-directory moves.

[2026-10-20 real API evidence and endpoint coverage](2026-10-20/README.md) includes raw audio, sanitized requests and responses, hashes, explicit failure/cancellation outcomes and timing limits. Use [the probe](../../tools/FishAudioProbe/README.md) for reproducible capture and offline safety tests.

[Preview acceptance in STranslate 2.0.10](preview-host-2026-10-20.md) records installed-plugin observations for both entry points with and without an API Key, including the distinction between device-position progress and an acoustic assessment.

[Production streaming and bounded first-prefix evidence](streaming-2026-10-20/README.md) records real PCM/MP3 plugin sessions through Windows WaveOut, intentional cancellation after playback starts, and a separate offline 1k/1m/1b virtual-tail prefix benchmark. Raw captures, source, timing limits, and an integrity verifier are included; host UI acceptance is tracked separately.

[Streaming acceptance in STranslate 2.0.10](streaming-host-2026-10-20.md) records installed-host short-text MP3/PCM completion, immediate key persistence, format/bitrate preservation, and PCM persistence across restart. It preserves the earlier PCM connection timeout and secondary WPF cleanup failure separately from the corrected client's successful later run.

[Favorites and settings-layout acceptance](favorites-host-2026-10-20.md) records ID-only persistence across host restart, lookup versus Apply, paging/removal, independent count-row fold controls, hidden PCM bitrate and native card alignment. Public sample observations and cache/file checks retain their measurement limits.

[Input and navigation follow-up](favorites-controls-host-2026-10-22.md) records the installed-host input constraints, numeric editing and stable navigation layout, with separate native-control regression boundaries.

[Settings interaction follow-up](settings-interactions-host-2026-10-22.md) records real title-copy notifications, collapsed last-favorite removal, resting numeric wheel adjustment and editing-state page scrolling, distinguishing host observations from theme fixtures.

[Voice glyph follow-up](voice-glyph-host-2026-10-22.md) records outline/raster verification and installed-host visual checks for the reported high Cross, including the failed first test attempt and corrected coverage.

[PCM output queue comparison](pcm-output-2026-10-22.md) preserves real native responses, provider-byte reconstruction and the user's same-source two/four-buffer listening comparison, with portable integrity checks and explicit inference limits.

[Four-buffer installed-host acceptance](pcm-output-host-2026-10-22.md) records the exact validation-package identity, retained user state and the user's same-sample confirmation that the reported electrical noise/stuttering disappeared after the audio update.

[Hundredth-step slider and focus acceptance](settings-wheel-host-2026-10-23.md) records installed-host dragging, slider/text wheel, native value feedback, Volume hundredth persistence and pointer-versus-keyboard fold focus, with preserved user state and explicit fixture boundaries.

[Positive-zero Volume acceptance](settings-zero-host-2026-10-23.md) records negative-to-zero slider/text wheel and native dragging, visible positive-zero feedback, persisted sign and the distinction between retained user choices and a refreshed sample URL.
