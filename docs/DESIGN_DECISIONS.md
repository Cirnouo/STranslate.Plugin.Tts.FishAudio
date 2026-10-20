# Design Decisions

This file is a migration index. Decision content now lives in the [ADR register](adr/README.md), with [current records](adr/current/) and [historical records](adr/history/) classified by replacement relationships. Use the [current specs](specs/README.md) for implemented behavior and the [documentation policy](policies/documentation.md) when maintaining these records.

The original DD numbers and heading anchors below are retained so old links continue to land on a forwarding entry. Do not append decision bodies to this index or reuse a number. Current DD-046 is the API Key compatibility decision; unmerged streaming experiments are not part of this baseline.

## DD-001: Silent credit refresh on startup

Moved to [historical DD-001](adr/history/DD-001.md).

## DD-002: API Key format validation before API call

Moved to [historical DD-002](adr/history/DD-002.md).

## DD-003: API Key persistence — confirm-button-only

Moved to [historical DD-003](adr/history/DD-003.md).

## DD-004: Voice search and voice-by-ID use dummy API Key when user key is invalid

Moved to [current DD-004](adr/current/DD-004.md).

## DD-005: Auto-save on property change

Moved to [current DD-005](adr/current/DD-005.md).

## DD-006: Latency text is shown temporarily after manual refresh

Moved to [current DD-006](adr/current/DD-006.md).

## DD-007: Config migration for renamed fields

Moved to [historical DD-007](adr/history/DD-007.md).

## DD-008: Preview state keyed by voice ID, not by UI card

Moved to [current DD-008](adr/current/DD-008.md).

## DD-009: `condition_on_previous_chunks` and `mp3_bitrate` as user-configurable settings

Moved to [current DD-009](adr/current/DD-009.md).

## DD-010: Random voice when VoiceId is empty

Moved to [current DD-010](adr/current/DD-010.md).

## DD-011: TextBox focus behavior — click blank space to unfocus

Moved to [current DD-011](adr/current/DD-011.md).

## DD-012: API Key commit on validation success

Moved to [historical DD-012](adr/history/DD-012.md).

## DD-013: API Key validation via explicit confirm button, simplified state

Moved to [historical DD-013](adr/history/DD-013.md).

## DD-014: Pagination centering with Grid layout

Moved to [current DD-014](adr/current/DD-014.md).

## DD-015: Preview stop icon instead of pause

Moved to [current DD-015](adr/current/DD-015.md).

## DD-016: Voice ID format validation before API call

Moved to [current DD-016](adr/current/DD-016.md).

## DD-017: Page input — LostFocus commit and numeric-only restriction

Moved to [current DD-017](adr/current/DD-017.md).

## DD-018: Persistent UI text must use DynamicResource, not GetTranslation

Moved to [current DD-018](adr/current/DD-018.md).

## DD-019: Enter key commits focused settings inputs

Moved to [current DD-019](adr/current/DD-019.md).

## DD-020: API Key validation status is an inline state, not raw text

Moved to [historical DD-020](adr/history/DD-020.md).

## DD-021: Voice tab initial height is stabilized

Moved to [current DD-021](adr/current/DD-021.md).

## DD-022: Draft API Key edits do not clear applied account state

Moved to [historical DD-022](adr/history/DD-022.md).

## DD-023: Build script includes an optional regression test step

Moved to [current DD-023](adr/current/DD-023.md).

## DD-024: Cover image cache uses the plugin cache directory

Moved to [current DD-024](adr/current/DD-024.md).

## DD-025: Cache cleanup is an explicit text action

Moved to [current DD-025](adr/current/DD-025.md).

## DD-026: Cover image cache size is measured from disk

Moved to [current DD-026](adr/current/DD-026.md).

## DD-027: Cache cleanup has a busy state with timeout recovery

Moved to [current DD-027](adr/current/DD-027.md).

## DD-028: GitHub Actions publishes tagged releases from the matching changelog section

Moved to [current DD-028](adr/current/DD-028.md).

## DD-029: README documentation follows the user setup workflow

Moved to [current DD-029](adr/current/DD-029.md).

## DD-030: API Key saves immediately with request-time preflight

Moved to [current DD-030](adr/current/DD-030.md).

## DD-031: Temporary s2.1-pro-free model policy and startup normalization

Moved to [current DD-031](adr/current/DD-031.md).

## DD-032: Context conditioning copy clarifies request-local scope

Moved to [current DD-032](adr/current/DD-032.md).

## DD-033: Slider tooltips and context copy match visible settings intent

Moved to [current DD-033](adr/current/DD-033.md).

## DD-034: SettingsViewModel delegates preview playback and cover cache display

Moved to [current DD-034](adr/current/DD-034.md).

## DD-035: Voice lookup and media cache hardening

Moved to [current DD-035](adr/current/DD-035.md).

## DD-036: Free model availability follows the extended UTC cutoff

Moved to [historical DD-036](adr/history/DD-036.md).

## DD-037: Selected voice preview refreshes expiring sample URLs

Moved to [historical DD-037](adr/history/DD-037.md).

## DD-038: Versioned settings are interpreted and guarded by the plugin

Moved to [current DD-038](adr/current/DD-038.md).

## DD-039: Startup credit refresh is a non-blocking initialization cycle

Moved to [current DD-039](adr/current/DD-039.md).

## DD-040: Preview refresh is driven by signed URL expiry

Moved to [current DD-040](adr/current/DD-040.md).

## DD-041: Fish Audio plugin uses a compatibility-preserving hybrid architecture

Moved to [current DD-041](adr/current/DD-041.md).

## DD-042: Build cleanup timing is selected by command mode

Moved to [current DD-042](adr/current/DD-042.md).

## DD-043: Release is the build script's only configuration switch

Moved to [current DD-043](adr/current/DD-043.md).

## DD-044: README build guidance stays task-oriented

Moved to [current DD-044](adr/current/DD-044.md).

## DD-045: Free model availability is extended through November 2026

Moved to [current DD-045](adr/current/DD-045.md).

## DD-046: API Key preflight accepts legacy and prefixed keys

Moved to [current DD-046](adr/current/DD-046.md).
