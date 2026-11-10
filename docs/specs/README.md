# Current implementation specifications

These specifications describe the v1.2.0 implementation, with SDK 1.0.15, native WebSocket TTS with incremental local audio, bounded local preview playback and provider-window-aware voice discovery. Dated evidence records production streaming, billing and audio observations separately. [Evidence index](../evidences/README.md)

- [Architecture and lifecycle](architecture-and-lifecycle.md)
- [Settings and TTS model policy](settings-and-model-policy.md)
- [Formal TTS and credit](tts-and-credit.md)
- [Native streaming transport and session](streaming-session.md)
- [Bounded streaming text preparation](streaming-text.md)
- [Incremental decoding and output](streaming-playback.md)
- [Voice browsing, preview and cover cache](voice-browsing-and-preview.md)
- [Build and release](build-and-release.md)

Use the [glossary](../glossary.md), [policies](../policies/documentation.md) and [ADR register](../adr/README.md) for terminology, maintenance rules and rationale. API references describe provider capabilities separately from this implemented subset.

Formal TTS uses one native binary MessagePack session at `/v1/tts/live`, an initial bounded prefix, concurrent receiving and incremental PCM/MP3 playback. It does not retry synthesis or fall back to another model, HTTP or another transport. New settings use PCM; schema 0/1 migration retains MP3 compatibility and valid bitrate. Previews download an existing bounded sample through host HTTP and play the seekable memory stream locally. Voice browsing separates reported totals from accessible pagination.

The [dated provider probes](../evidences/2026-10-20/README.md) establish observations about the provider protocol and response bytes. Their first-audio timings do not measure the production plugin's first PCM, output start or physical sound. Current offline regression coverage and any separately recorded host acceptance must be identified by their own evidence boundaries.
