# Architecture decision records

These records explain decisions in the current plugin and the designs they replaced. Current/history classification is semantic: an old decision can remain current, while a recent superseded design belongs in history. Numbers are permanent; new records must not reuse an existing DD number.

The migration baseline was v1.1.2 behavior with SDK 1.0.15. Current Unreleased code adds host-HTTP memory-backed previews (DD-047), server-window-aware pagination (DD-048), immediate cancellable preview preparation state (DD-049) and native streaming TTS (DD-050–053). DD-046 remains the API Key compatibility decision. An unmerged experiment is not a current ADR.

Each migrated record retains its original rationale, identifies successors and states the current interpretation when applicable. Historical file/type names in the recorded rationale are provenance; the implementation-basis links and current specs locate today's owners. No record establishes a successful live TTS or physical audio measurement.

See the [glossary](../glossary.md), [current behavior specs](../specs/README.md) and [documentation policy](../policies/documentation.md). The [legacy index](../DESIGN_DECISIONS.md) retains old heading anchors.

## Replacement relationships

- DD-030 replaces the confirm/applied API Key lifecycle in DD-001/003/012/013/020/022; DD-039 restores non-blocking startup credit without restoring validation state.
- DD-038 replaces DD-007's Settings shim migration.
- DD-040 replaces DD-037's unconditional selected-preview detail refresh and stale-URL fallback.
- DD-045 replaces DD-036's cutoff date; DD-031's centralized policy and normalization remain current.
- DD-046 replaces DD-002's shared/legacy-only API Key format assumption. DD-016 retains Voice ID validation with a separate regex.
- DD-043's one-time v1.1.0 Unreleased staging instruction is historical; its build switches and output isolation remain current.
- DD-049 amends DD-047's pending state presentation and same-voice cancellation across preview entry points; its bounded download and actual playback rules remain current.
- DD-050 replaces the complete-buffer formal transport and continue-through-reinitialization portions of DD-009/039/041. Their settings, initialization, credit and ownership constraints remain current.
- DD-053 advances DD-038's schema version and amends DD-009/031/041 for format, proxy and model choices. It preserves their other constraints. No fully replaced record is moved to history merely because one portion is amended.

## Complete register

| ID | Decision | Classification |
|---|---|---|
| DD-001 | [Silent credit refresh on startup](history/DD-001.md) | History |
| DD-002 | [API Key format validation before API call](history/DD-002.md) | History |
| DD-003 | [API Key persistence — confirm-button-only](history/DD-003.md) | History |
| DD-004 | [Voice search and voice-by-ID use dummy API Key when user key is invalid](current/DD-004.md) | Current |
| DD-005 | [Auto-save on property change](current/DD-005.md) | Current |
| DD-006 | [Latency text is shown temporarily after manual refresh](current/DD-006.md) | Current |
| DD-007 | [Config migration for renamed fields](history/DD-007.md) | History |
| DD-008 | [Preview state keyed by voice ID, not by UI card](current/DD-008.md) | Current |
| DD-009 | [`condition_on_previous_chunks` and `mp3_bitrate` as user-configurable settings](current/DD-009.md) | Current |
| DD-010 | [Random voice when VoiceId is empty](current/DD-010.md) | Current |
| DD-011 | [TextBox focus behavior — click blank space to unfocus](current/DD-011.md) | Current |
| DD-012 | [API Key commit on validation success](history/DD-012.md) | History |
| DD-013 | [API Key validation via explicit confirm button, simplified state](history/DD-013.md) | History |
| DD-014 | [Pagination centering with Grid layout](current/DD-014.md) | Current |
| DD-015 | [Preview stop icon instead of pause](current/DD-015.md) | Current |
| DD-016 | [Voice ID format validation before API call](current/DD-016.md) | Current |
| DD-017 | [Page input — LostFocus commit and numeric-only restriction](current/DD-017.md) | Current |
| DD-018 | [Persistent UI text must use DynamicResource, not GetTranslation](current/DD-018.md) | Current |
| DD-019 | [Enter key commits focused settings inputs](current/DD-019.md) | Current |
| DD-020 | [API Key validation status is an inline state, not raw text](history/DD-020.md) | History |
| DD-021 | [Voice tab initial height is stabilized](current/DD-021.md) | Current |
| DD-022 | [Draft API Key edits do not clear applied account state](history/DD-022.md) | History |
| DD-023 | [Build script includes an optional regression test step](current/DD-023.md) | Current |
| DD-024 | [Cover image cache uses the plugin cache directory](current/DD-024.md) | Current |
| DD-025 | [Cache cleanup is an explicit text action](current/DD-025.md) | Current |
| DD-026 | [Cover image cache size is measured from disk](current/DD-026.md) | Current |
| DD-027 | [Cache cleanup has a busy state with timeout recovery](current/DD-027.md) | Current |
| DD-028 | [GitHub Actions publishes tagged releases from the matching changelog section](current/DD-028.md) | Current |
| DD-029 | [README documentation follows the user setup workflow](current/DD-029.md) | Current |
| DD-030 | [API Key saves immediately with request-time preflight](current/DD-030.md) | Current |
| DD-031 | [Temporary s2.1-pro-free model policy and startup normalization](current/DD-031.md) | Current |
| DD-032 | [Context conditioning copy clarifies request-local scope](current/DD-032.md) | Current |
| DD-033 | [Slider tooltips and context copy match visible settings intent](current/DD-033.md) | Current |
| DD-034 | [SettingsViewModel delegates preview playback and cover cache display](current/DD-034.md) | Current |
| DD-035 | [Voice lookup and media cache hardening](current/DD-035.md) | Current |
| DD-036 | [Free model availability follows the extended UTC cutoff](history/DD-036.md) | History |
| DD-037 | [Selected voice preview refreshes expiring sample URLs](history/DD-037.md) | History |
| DD-038 | [Versioned settings are interpreted and guarded by the plugin](current/DD-038.md) | Current |
| DD-039 | [Startup credit refresh is a non-blocking initialization cycle](current/DD-039.md) | Current |
| DD-040 | [Preview refresh is driven by signed URL expiry](current/DD-040.md) | Current |
| DD-041 | [Fish Audio plugin uses a compatibility-preserving hybrid architecture](current/DD-041.md) | Current |
| DD-042 | [Build cleanup timing is selected by command mode](current/DD-042.md) | Current |
| DD-043 | [Release is the build script's only configuration switch](current/DD-043.md) | Current |
| DD-044 | [README build guidance stays task-oriented](current/DD-044.md) | Current |
| DD-045 | [Free model availability is extended through November 2026](current/DD-045.md) | Current |
| DD-046 | [API Key preflight accepts legacy and prefixed keys](current/DD-046.md) | Current |
| DD-047 | [Download public voice previews through host HTTP before local playback](current/DD-047.md) | Current |
| DD-048 | [Keep voice totals separate from accessible pagination](current/DD-048.md) | Current |
| DD-049 | [Publish cancellable preview preparation before playback](current/DD-049.md) | Current |
| DD-050 | [Formal TTS owns one native streaming session through device drain](current/DD-050.md) | Current |
| DD-051 | [Prepare a lazy readable prefix and gate the tail on output start](current/DD-051.md) | Current |
| DD-052 | [Decode incrementally into a bounded PCM ring with an independent pump](current/DD-052.md) | Current |
| DD-053 | [Preserve MP3 compatibility and make native streaming routing explicit](current/DD-053.md) | Current |
| DD-054 | [Persist favorite IDs separately and share browsing caches](current/DD-054.md) | Current |
| DD-055 | [Use the system proxy without plugin network settings](current/DD-055.md) | Current |
| DD-056 | [Share selection state across every voice card](current/DD-056.md) | Current |
| DD-057 | [Keep nucleus sampling above zero as a client compatibility rule](current/DD-057.md) | Current |
