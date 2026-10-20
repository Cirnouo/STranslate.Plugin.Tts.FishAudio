# Current implementation specifications

These specifications describe the implemented baseline: plugin v1.1.2 behavior plus the SDK 1.0.15 and offline CI maintenance update, plus server-window-aware voice discovery. They describe code contracts; they do not establish a successful live TTS request, billing outcome or physical audio measurement.

- [Architecture and lifecycle](architecture-and-lifecycle.md)
- [Settings and TTS model policy](settings-and-model-policy.md)
- [Formal TTS and credit](tts-and-credit.md)
- [Voice browsing, preview and cover cache](voice-browsing-and-preview.md)
- [Build and release](build-and-release.md)

Use the [glossary](../glossary.md), [policies](../policies/documentation.md) and [ADR register](../adr/README.md) for terminology, maintenance rules and rationale. API references describe provider capabilities separately from this implemented subset.

The current formal TTS path is one complete-buffer HTTP MP3 request followed by host playback. Current previews open provider sample URLs in WPF MediaPlayer. Voice browsing separates reported totals from accessible pagination. There is no production WebSocket synthesis, incremental PCM player, format selector/schema-2 migration, controlled preview-audio downloader in this baseline. Later code changes must update these specs when those capabilities are implemented.
