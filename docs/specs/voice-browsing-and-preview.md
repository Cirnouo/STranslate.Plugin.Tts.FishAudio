# Voice browsing, preview and cover cache

Status: current Unreleased implementation. Browsing and preview do not require the user's synthesis API Key. [VoiceDiscoveryCoordinator](../../STranslate.Plugin.Tts.FishAudio/Presentation/VoiceDiscoveryCoordinator.cs) uses the `dummy` bearer token for `/model` and `/model/{id}`, with 15-second metadata request timeouts. Only a detail 404 maps to not-found; other request errors remain distinguishable. By-ID lookup validates Voice ID shape and rejects explicit non-TTS types. Missing/blank type remains compatible with old responses.

## Search and pagination

Search is explicit through its button or Enter, with pages of six raw records, `sort_by=score` and an escaped title filter. New successful queries reset to page one. Page input supports Enter/LostFocus and numeric typing; boundary buttons hide without moving the center control. Applied results, pagination and query change together; an unsubmitted input edit does not affect a later page request. Failed, replaced or disposed operations cannot publish late results.

[VoiceSearchPagination](../../STranslate.Plugin.Tts.FishAudio/Presentation/VoiceSearchPagination.cs) keeps reported total, exactness, last accessible page and Next availability separate. `max_offset` restricts starting offsets; `accessible_upper_bound` limits accessible records. Precise `total` can add a bound; approximate `total` cannot authorize jumps. The minimum applicable bound still permits a partial final page, such as page 167 at offset 996 under a 1000-record window. Window limitation can be inferred when a hard bound truncates an exact total, even if the flag is missing.

An explicit `has_more=false` or empty raw items stops forward navigation. Raw full pages without any total/metadata allow one further attempt; raw short pages stop unless the server explicitly permits more. Without a known bound, only visited pages and the currently permitted next page are reachable. Empty pages are not automatically retried or skipped. Raw record counts govern paging before explicit non-TTS records are filtered from selectable cards.

Pagination is published before cover resolution. The stable numeric facade properties remain, but navigation follows the applied policy rather than an editable presentation value. UI uses `≈` for approximate counts, `?` for unknowns and `≤` for accessible page bounds; all five locales explain the window. Exact provider window-edge semantics have not been live-tested at every offset; see [DD-048](../adr/current/DD-048.md).

## Preview ownership and download

Selected metadata is cached with settings; search results are independent observable objects. Preview state remains keyed by Voice ID, keeping the selected card and matching search result synchronized. Both public commands allow a second click during pending work to stop it.

[PreviewAudioUrlPolicy](../../STranslate.Plugin.Tts.FishAudio/Presentation/PreviewAudioUrlPolicy.cs) still permits only HTTPS `platform.r2.fish.audio` and non-empty subdomains of `r2.cloudflarestorage.com`. Unsigned allowed URLs are reusable. Signed URLs refresh thirty seconds before expiry; missing, duplicate, malformed or overflowing timing data requires conservative refresh.

[PreviewRefreshCoordinator](../../STranslate.Plugin.Tts.FishAudio/Presentation/PreviewRefreshCoordinator.cs) owns one operation generation through refresh, download and output start. Selected refresh saves complete current CachedVoice metadata; search refresh updates its current card. A fresh response without a sample is authoritative. Expiry refresh failure never falls back to the old URL. A sample 403/404 can consume one detail-refresh budget and retry its newly returned URL once; an earlier expiry refresh already consumes that budget. Other errors and a repeated 403/404 do not retry. No preview path invokes TTS.

[PreviewAudioDownloader](../../STranslate.Plugin.Tts.FishAudio/Presentation/PreviewAudioDownloader.cs) retrieves the sample using host `IHttpService`, with no account or dummy authorization. A single fifteen-second operation token includes metadata refresh, resource request and successful body reads. It copies into seekable memory, checking at most eight MiB plus one overflow-detection byte. Empty, oversized and failed downloads do not create playback. The host stream API internally handles unsuccessful response bodies before returning; the plugin's cap describes its returned-stream copy, not all host-owned error buffering.

Stop, selection changes, entry switching, result removal and disposal invalidate pending work and stop owned playback. Late streams are disposed and cannot publish state, change a new card or restart output. Logs identify operation and exception type without publishing signed resource URIs.

## Local audio and progress

[PreviewPlaybackController](../../STranslate.Plugin.Tts.FishAudio/Presentation/PreviewPlaybackController.cs) takes ownership of the memory stream on every start, including factory/open/play failure. [NAudioPreviewAudioPlayer](../../STranslate.Plugin.Tts.FishAudio/Presentation/NAudioPreviewAudioPlayer.cs) uses NAudio.Core/WinMM 2.3.0, seekable MP3/WAVE reading, Windows ACM MP3 decompression and WaveOut output. Client volume is 1.0. The memory stream is independent of the already closed HTTP response stream.

Playback state is published only after local Play returns successfully and the player generation remains current. A 50 ms dispatcher timer reports device output position against total duration; decoder read-ahead is not presented as played progress. End/failure stops output and clears state. Old events are ignored after switch/disposal, including callbacks already queued to the dispatcher. Controller cleanup releases timer, player and buffer even when a stop/dispose step fails.

Offline tests inject player doubles and do not prove physical sound. [Installed-host acceptance](../evidences/preview-host-2026-10-20.md) records both entry points with and without an API Key, output-position progress, explicit stop and natural completion, with its observation limits. The bounded whole-sample preview design does not apply to formal TTS streaming. Rationale: [DD-008](../adr/current/DD-008.md), [DD-034](../adr/current/DD-034.md), [DD-040](../adr/current/DD-040.md), [DD-047](../adr/current/DD-047.md).

## Cover cache

[CoverImageCacheService](../../STranslate.Plugin.Tts.FishAudio/Caching/CoverImageCacheService.cs) stores `cover_images/<voiceId>.jpg` under the host cache directory. A local hit wins; a miss displays CDN content and downloads in the background. A ten-second token and 256 KiB plus one detection byte limit downloads. Only non-empty JPEG/PNG/GIF/WebP-looking content up to 256 KiB is cached. Cache usage rescans actual JPG files. Cleanup is scoped to cover_images, exposes busy state, recovers its button after ten seconds and prevents a late old task from unlocking a newer operation.

Verification: [voice discovery](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/VoiceDiscoverySuite.cs), [preview](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/PreviewSuite.cs), [bounded download/lifecycle scenarios](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/PreviewDownloadSuite.cs), [cover cache](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/CoverImageCacheSuite.cs).
