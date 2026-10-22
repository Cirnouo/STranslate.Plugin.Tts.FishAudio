# API evidence

[2026-10-20 real API evidence and endpoint coverage](2026-10-20/README.md) includes raw audio, sanitized requests and responses, hashes, explicit failure/cancellation outcomes and timing limits. Use [the probe](../../tools/FishAudioProbe/README.md) for reproducible capture and offline safety tests.

[Preview acceptance in STranslate 2.0.10](preview-host-2026-10-20.md) records installed-plugin observations for both entry points with and without an API Key, including the distinction between device-position progress and an acoustic assessment.

[Production streaming and bounded first-prefix evidence](streaming-2026-10-20/README.md) records real PCM/MP3 plugin sessions through Windows WaveOut, intentional cancellation after playback starts, and a separate offline 1k/1m/1b virtual-tail prefix benchmark. Raw captures, source, timing limits, and an integrity verifier are included; host UI acceptance is tracked separately.

[Streaming acceptance in STranslate 2.0.10](streaming-host-2026-10-20.md) records installed-host short-text MP3/PCM completion, immediate key persistence, format/bitrate preservation, and PCM persistence across restart. It preserves the earlier PCM connection timeout and secondary WPF cleanup failure separately from the corrected client's successful later run.

[Favorites and settings-layout acceptance](favorites-host-2026-10-20.md) records ID-only persistence across host restart, lookup versus Apply, paging/removal, independent count-row fold controls, hidden PCM bitrate and native card alignment. Public sample observations and cache/file checks retain their measurement limits.
