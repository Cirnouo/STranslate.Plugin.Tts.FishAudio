# AGENTS.md — STranslate.Plugin.Tts.FishAudio

## Project

Fish Audio TTS plugin for STranslate. Implements `ITtsPlugin` from `STranslate.Plugin` SDK (v1.0.12).

## Build

```powershell
# Recommended Release build + regression tests; cleans intermediates afterward
.\build.ps1 -Release -Clean -Test

# Debug build; cleans intermediates before building
.\build.ps1

# Debug build, then clean intermediates; retains the root .spkg
.\build.ps1 -Clean

# Debug build + regression tests, then clean intermediates
.\build.ps1 -Clean -Test
```

Requires .NET 10 SDK. Target framework: `net10.0-windows` with WPF.

Debug is the default configuration; `-Release` is the only configuration switch. Build cleanup always covers repository `bin`, `obj`, and `.artifacts`; plugin-project `bin`, `obj`, and `.artifacts`; and test-project `obj` and `bin`. `-Clean` performs its final cleanup even when build, packaging, or regression tests fail, while preserving any root `.spkg` already copied. `-CleanOnly` removes the same directories without building.

## Architecture

- **Main.cs** — `ITtsPlugin` entry and composition root. It delegates initialization to `Lifecycle/PluginInitializationCoordinator`, keeps the public plugin entry points, and owns the TTS request/playback flow. `PlayAudioAsync` captures one Context/Settings/ViewModel/audio-player snapshot before calling `PostAsBytesAsync` → `PlayAsync(byte[])`.
- **Configuration/** — Versioned settings schema, JSON converter/migrator, validation, normalization, model availability policy, and the only host settings load/save boundary (`SettingsStore`).
- **FishAudio/** — `FishAudioApi` is the static wrapper for all Fish Audio endpoints; `FishAudioRequestPolicy` owns request-time preflight, timeout, and error mapping.
- **FishAudio/Models/** — Fish Audio JSON DTOs. They retain the public `STranslate.Plugin.Tts.FishAudio.Model` namespace for compatibility.
- **Lifecycle/** — `PluginInitializationCoordinator` owns initialization generations, leases, detached settings snapshots, replacement publication, and startup metadata coordination.
- **Runtime/** — `FishAudioClock` owns local/online UTC selection and fallback.
- **Caching/** — `CoverImageCacheService` owns cover-image cache I/O and bounded downloads.
- **Presentation/** — `SettingsViewModel` remains the XAML binding facade and auto-save coordinator. `CreditRefreshCoordinator`, `VoiceDiscoveryCoordinator`, and `PreviewRefreshCoordinator` own their respective workflows; `PreviewPlaybackController` owns `MediaPlayer` preview lifecycle/progress; `CoverImageCacheDisplayManager` owns cache display and cleanup orchestration. `VoiceSearchItem` remains observable with per-result preview state, while these moved files retain the public `.ViewModel` namespace where required.
- **Presentation/View/** — WPF UserControls using iNKORE Modern UI (`ui:SettingsCard`, `ui:ToggleSwitch`), retaining the existing `.View` namespace and `x:Class`.
- **Presentation/Converters/** — Value converters including `ProgressToDashOffsetConverter` for the circular progress ring, retaining the existing `.Converter` namespace.

## Terminology

- **声音 (Voice)**: A specific Fish Audio voice identity (formerly called "模型/Model" in UI). Maps to `reference_id` in the API.
- **模型 (Model)**: The synthesis engine — `s2.1-pro-free`, `s2.1-pro`, `s2-pro`, or `s1` (formerly called "引擎/Engine" in UI). Maps to the `model` HTTP header in the API.
- Internal C# property `SelectedModel` refers to the synthesis model (s2.1-pro-free/s2.1-pro/s2-pro/s1), consistent with the API's `model` header.
- `ModelEntity`, `ModelListResponse` are API DTO names matching Fish Audio's API terminology — not renamed.
- `Settings.VoiceId` (serialized as `VoiceId`) is the voice reference ID. Old config field `ReferenceId` is auto-migrated on first load.
- `Settings.CachedVoice` (type `CachedVoiceInfo`) caches selected voice metadata. Old config field `CachedModel` is auto-migrated.
- `Settings` and `CachedVoiceInfo` live in `STranslate.Plugin.Tts.FishAudio.Configuration`. Canonical settings JSON writes `SchemaVersion` first; current is 1 and missing means legacy 0.
- The custom settings converter accepts every v1.0.x shape. Valid current `VoiceId`/`CachedVoice` values win over legacy fields regardless of JSON order; wrong-type current values fall back to valid legacy values. Canonical saves remove legacy, unknown, and deleted fields.

## Key API Facts

- TTS (`POST /v1/tts`): Returns **raw binary audio** (not JSON, not a URL) on 200, JSON error on 4xx. `model` header required (`s2.1-pro-free`/`s2.1-pro`/`s2-pro`/`s1`). `mp3_bitrate` and `condition_on_previous_chunks` are configurable. Context conditioning uses earlier chunks from the same synthesis audio only; it does not reference previously generated audio. `normalize_loudness` is supported by `s2.1-pro-free`, `s2.1-pro`, and `s2-pro`; do not send it for `s1`.
- When `reference_id` is omitted from the TTS request body, Fish Audio still returns 200 with valid audio using a system default/random voice. This is **not free** — credit is deducted asynchronously (may be delayed by ~60s).
- Credit (`GET /wallet/self/api-credit`): `credit` field is a **string**, not number. Requires valid API Key.
- Get Model (`GET /model/{id}`): Returns 404 `{"status":404,"message":"Model not found"}` for non-existent IDs. Does not require valid API Key.
- List Models (`GET /model`): Does not require valid API Key.
- API Key format: 32 lowercase hex characters (`[0-9a-f]{32}`), same as MD5 output.
- Model images: CDN base = `https://public-platform.r2.fish.audio/`, supports `/cdn-cgi/image/width=W,format=auto/` resizing.
- List and detail endpoint audio URLs can be signed R2 URLs. Inspect `X-Amz-Date` and `X-Amz-Expires`; unsigned public CDN URLs remain reusable.
- Credit purchase/top-up: Console > Developer > Billing > Balance > Purchase Credits — `https://fish.audio/app/developers/billing/`.

## Key Behaviors

- **API Key behavior**: API Key input is saved immediately on edit, same as other settings. There is no confirm button, no Enter-to-confirm binding, no "verified and applied" UI, and no runtime validation state. Startup credit refresh, TTS, manual credit refresh, and post-TTS silent credit refresh use shared request-time preflight: local network availability (`NetworkInterface.GetIsNetworkAvailable()`), non-empty key, and strict 32 lowercase hex format. User-triggered failures show localized messages; startup and silent refresh failures only log. Request timeouts show `STranslate_Plugin_Tts_FishAudio_Request_Timeout` only for user-triggered requests. Do not log plaintext API Keys. See DD-002/DD-030/DD-039.
- **API Key input locking**: TTS and credit refresh lock the API Key input and paste button while requests are running. API Key and credit activity use separate counters with one shared gate that updates counts/desired versions and elects a single publisher; observable setters run outside the gate and the publisher loops until state converges. Pending versions are published before an earlier setter exception is rethrown; a second setter failure clears publisher ownership and reports both failures so later operations can recover. This avoids deadlocks when property or command callbacks wait on another thread, prevents overlapping startup/manual/TTS completion from unlocking early, and stops unmatched completion calls at zero. See DD-030/DD-039.
- **Voice search/by-ID API token**: Voice search and by-ID lookup use `"dummy"` token because those endpoints do not require a valid API Key. See DD-004/DD-030.
- **Model availability policy**: Before `2026-09-01T00:00:00Z`, including the final tick of `2026-08-31` UTC, available synthesis models are `s2.1-pro-free`, `s2.1-pro`, `s2-pro`, and `s1`, with `s2.1-pro-free` as default. At and after that UTC cutoff, hide `s2.1-pro-free` and default to `s2.1-pro`. Policy and defaults live in `Configuration/FishAudioModelPolicy`; do not scatter model/date literals. See DD-031/DD-036/DD-041.
- **s2.1-pro-free promo card**: Uses the local WPF resource `s2-pro-free-promo.webp` with a fixed 1024:540 placeholder to avoid layout shifts. Clicking the card selects `s2.1-pro-free`, saves, scrolls to the synthesis model card, and flashes it, but does not dismiss the promo. Only the close button persists dismissal. See DD-031.
- **Normalize loudness UI**: The setting remains visible for all synthesis models. It is enabled only for `s2.1-pro-free`, `s2.1-pro`, and `s2-pro`; it is disabled for `s1`, and TTS requests omit `prosody.normalize_loudness` for `s1`. See DD-031.
- **Versioned settings storage**: `SettingsStore.Load()` is the only production host load entry. Main's leased path clones the host-owned object, migrates and normalizes the detached runtime snapshot, then copies it back and performs any canonical save only after replacement preparation succeeds. All production saves use `SettingsStore`. Duplicate `SchemaVersion` and future schemas are read-only: compatible current fields remain usable in memory, while saves only log and preserve the host file. Missing or wrong-type version markers migrate from legacy version 0. The plugin never implements raw settings file I/O, so host atomic write and `.bak` behavior remain intact. See DD-038/DD-039.
- **Startup settings normalization**: `SettingsNormalizer` repairs structured settings with clear defaults (model, latency, MP3 bitrate, speed, volume, temperature, top_p) and saves if corrected. Speed, temperature, and top_p snap to 0.05; volume snaps to 0.1 using nearest-step midpoint-away-from-zero rounding. It does not silently clear API Key or Voice ID text. Startup first uses local UTC, then a bounded online UTC check with TimeAPI.io and local fallback. After reserving a post-cutoff model-policy revision, the candidate transaction always completes even if a concurrent user edit already chose a valid paid model and normalization changes no value; the user's model stays selected while the current Models/promo policy is still published. See DD-031/DD-038/DD-039.
- **Startup selected-voice refresh**: If `Settings.VoiceId` is non-empty and valid, startup captures that Voice ID and silently refreshes cached metadata via Get Model using `"dummy"`. The leased candidate update/save proceeds only while its Voice ID still equals the request, and the final UI-thread application rechecks both visible and runtime IDs; selecting another voice or clearing it makes a late response a no-save/no-UI-op. Skip on local network failure or invalid format; preserve existing cached voice on server/network/API failure. See DD-031/DD-039.
- **Voice ID format validation**: Voice IDs share the same 32-hex format as API Keys. `SubmitVoiceIdAsync` checks `SettingsValidation.IsValidVoiceIdFormat` before calling API — skips the call on format failure. See DD-002/DD-016.
- When `VoiceId` is empty, `FishAudioApi.PostTtsAsync` omits `reference_id` from the request body. Do not send empty string.
- When `VoiceId` is empty or `CachedVoice` is null, the UI shows a localized "Random Voice" title (key: `STranslate_Plugin_Tts_FishAudio_RandomVoice`) instead of blank.
- Voice selection has two modes (tab-based): **Search** (default) and **By ID**. Search uses `SearchModelsAsync` with pagination; By ID uses `GetModelAsync` on explicit submit. Search runs via the icon button or Enter while focused; By ID runs via the checkmark button or Enter while focused. Both modes keep the initial one-row panel height stable so the following "Model" section does not jump when switching tabs. See DD-019/DD-021.
- Cover image cache stores `cover_image` files under `PluginCacheDirectoryPath\cover_images` as `<voiceId>.jpg`. Search and selected voice images use the cache first, then fall back to the CDN and create the cache in the background. The Misc section shows cache size by scanning actual `cover_images/*.jpg` files and uses an explicit localized text button for cache cleanup. Cleanup runs in the background, shows a rotating busy indicator, disables repeat clicks, and restores the button after a 10-second timeout. UI display/cleanup orchestration lives in `CoverImageCacheDisplayManager`. See DD-024/DD-025/DD-026/DD-027/DD-034.
- Search resets to page 1 on new query; pagination supports editable page input with Enter key and LostFocus commit. Page input is numeric-only. Result count and pagination hidden before first search. Prev/Next buttons auto-hide at boundary pages. Pagination uses Grid layout (`*`/`Auto`/`*` columns) to keep page indicator centered regardless of button visibility. See DD-014/DD-017.
- Preview system uses `Presentation/PreviewPlaybackController` through an internal player abstraction for real `MediaPlayer` audio playback, progress tracking, and asynchronous failure reporting. `PreviewAudioUrlPolicy` validates and evaluates signed URLs; selected and search preview URLs are reused until 30 seconds before the expiry calculated from `X-Amz-Date + X-Amz-Expires`; malformed signed timing is refreshed conservatively. Expiring URLs refresh model detail with the `"dummy"` token and 15-second timeout. Selected refresh saves complete cached metadata, while search refresh updates the observable result's sample URL and visible metadata without saving settings. Refresh failure never falls back to an expiring URL and shows network-unavailable or `Preview_RefreshFailed`; successful responses without a usable sample remain authoritative. Repeated stop clicks do not use the network, and target changes, replaced search results, Dispose, or late responses cannot update stale metadata, save, or start playback. See DD-008/DD-015/DD-034/DD-040/DD-041.
- **Startup credit refresh**: After settings migration/normalization, each `Main.Init()` starts one non-blocking credit request in parallel with the existing startup refresh work. It snapshots the API Key, uses shared preflight and the existing 15-second credit timeout, shows no latency or Snackbar, and only applies while the Key, ViewModel lifetime, and initialization cycle still match. Publishing a replacement atomically invalidates the old credit cycle before old ViewModel disposal, without waiting for either balance request. The account row stays visible with localized `Credit_NotLoaded` text until a balance is available. See DD-039.
- **Atomic initialization snapshot**: Each `Main.Init()` declares its generation before waiting on an independent transition gate and rechecks that ticket before host Load, so an overtaken initialization never loads or canonical-saves. `SettingsStore` serializes every Load, Save, lease transition, runtime mutation, and commit under one gate. Each published initialization owns a detached runtime Settings snapshot and write lease captured by all production ViewModel and startup-metadata saves; replacement drains and retires the old lease before Load, grants a new lease even when the host returns the same storage instance, and commits runtime values to that host object only after required ViewModel construction succeeds. Failed Load/construction restores the old lease without mutating or saving host storage. Leased updates mutate a detached candidate and publish it to runtime only after host save success; startup metadata reserves its revision before competing for the transition gate, rotates the current InitializationState publication credential after a successful runtime commit, and rolls the revision back without a success credential if commit fails. Lazy ViewModel publication briefly crosses the transition gate, so every candidate captured before replacement or runtime commit retries against current settings while construction remains outside global gates. Gate acquisition order decides startup-save versus replacement precedence; generation declaration alone does not reject a save that already entered the transition gate. The previous complete Context/Settings/ViewModel/audio-player state stays readable and usable for load-window TTS/credit until atomic replacement, but late old saves skip and retired ViewModels perform no final save. Host I/O and observable/callback work never run under the state gate. `PlayAudioAsync` captures all four dependencies together at entry, so an old operation cannot mix dependencies, play through, unlock, show errors on, or silently refresh the replacement initialization. See DD-039.
- Manual refresh shows latency for 4 seconds then auto-hides; startup refresh does not show latency. See DD-006/DD-030/DD-039.
- Auto-save on every property change, including API Key. See DD-005/DD-030.
- After TTS playback, immediately refreshing credit may show stale balance due to async deduction.
- API Key remains stored in the existing plaintext settings storage; encryption/migration is an accepted residual risk for this batch. See DD-030.

## Conventions

- Mirror patterns from sibling plugin `STranslate.Plugin.Tts.Vocu`.
- i18n key prefix: `STranslate_Plugin_Tts_FishAudio_`.
- 5 locales: zh-cn, zh-tw, en, ja, ko (XAML resource dictionaries + JSON plugin descriptions).
- Auto-save on every property change, including API Key — no explicit save button.
- Plugin ID: `f1183ad1f848454dad5c4a770a68f36a`.
- GitHub Release notes must use only the matching version section from `CHANGELOG.md` (for example, only `## [1.0.2] ...` through the line before the next version). Never paste the entire changelog into a release description.
- Each changelog version entry records only net changes relative to the previous released version. Do not list both an addition and removal for behavior that was added and removed during the same unreleased development cycle with no net user-visible change.
- GitHub Actions publishes tagged releases on pushed `v*` tags by running `.\build.ps1 -Release -Clean -Test`, extracting the matching `CHANGELOG.md` section, and using the tag name itself as the GitHub Release title. Keep that workflow and the changelog sections in sync.
- Design decisions are recorded in `docs/DESIGN_DECISIONS.md` using ADR format. Add a new entry when introducing non-obvious behavior. **This file must be updated after each requirement batch**, alongside CHANGELOG.md, locale files, and CLAUDE.md/AGENTS.md.
- README build guidance stays task-oriented: document runnable commands, default Debug behavior, `-CleanOnly`, and root package output without repeating internal cleanup directories or failure-path mechanics. Free-model tips use the calendar date without the parenthetical UTC all-day note; the model table owns exact UTC boundary details. See DD-044.
- **i18n runtime rule**: Persistent on-screen text must use `DynamicResource` in XAML (auto-updates on language switch). Never store `GetTranslation()` results in ViewModel properties for display — use `DataTrigger` + `DynamicResource` instead. Transient error messages (snackbar, momentary inline errors) may use `GetTranslation()`. See DD-018.
- `.claude/rules/` contains local Claude Code rule files for this workspace. They are local-only when ignored by `.gitignore`; do not assume they are committed unless tracking is explicitly changed.

## API Docs

See `docs/` folder for per-endpoint documentation with request/response examples.
