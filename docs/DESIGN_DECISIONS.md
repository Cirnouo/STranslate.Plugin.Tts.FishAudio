# Design Decisions

Architecture Decision Records (ADR) for STranslate.Plugin.Tts.FishAudio.
Each entry records a behavior, its motivation, and which code it affects.

---

## DD-001: Silent credit refresh on startup

**Date:** 2026-04-27
**Context:** When the plugin initializes, it validates the saved API Key by calling `GetCreditAsync`. Showing error UI during startup would surprise users who haven't interacted with the plugin yet.
**Decision:** The startup credit check does not show error snackbars or latency text, but when it succeeds it does surface the applied state: credit balance plus the localized "verified and applied" status. If the key is invalid, the user discovers it when they interact with a feature that requires it.
**Affects:** `Main.Init()`, `SettingsViewModel` constructor, `ApplyPendingCreditAsync`, `FetchCreditAsync` `showError`/`showLatency` params.

---

## DD-002: API Key format validation before API call

**Date:** 2026-05-06
**Context:** Fish Audio API Keys follow the MD5 hex format: exactly 32 lowercase hex characters `[0-9a-f]{32}`. Calling the credit API with an obviously invalid key wastes a network round-trip and may confuse users with a generic error.
**Decision:** Validate 32-hex format client-side first. Only call the API when the format passes. Display a specific "format incorrect" message for malformed inputs. This applies to both API Key (`Settings.IsValidApiKeyFormat`) and Voice ID (`Settings.IsValidVoiceIdFormat`) — they share the same regex (`^[0-9a-f]{32}$`).
**Affects:** `Settings.IsValidApiKeyFormat()`, `Settings.IsValidVoiceIdFormat()`, `SettingsViewModel.ConfirmApiKeyAsync()`, `SettingsViewModel.SubmitVoiceIdAsync()`.

---

## DD-003: API Key persistence — confirm-button-only

**Date:** 2026-05-06
**Context:** If an incorrect API Key is persisted, the plugin will attempt to use it on next launch and fail silently. Users might not realize their key is bad.
**Decision:** API Key is persisted exclusively through the confirm button click. On success → write key to config and save. On any failure (empty, bad format, API rejection) → write empty string to config and save. `Dispose` does not touch `_settings.ApiKey`. If the user edits the key but never clicks confirm, the previously persisted value (from the last successful confirm, or empty) is retained.
**Affects:** `SettingsViewModel.ConfirmApiKeyAsync()`, `SettingsViewModel.OnPropertyChanged()` (does not persist), `SettingsViewModel.Dispose()` (does not touch API Key).

---

## DD-004: Voice search and voice-by-ID use dummy API Key when user key is invalid

**Date:** 2026-05-06
**Context:** Fish Audio's model list (`GET /model`) and model detail (`GET /model/{id}`) endpoints do not require a valid API Key. Users should be able to browse and preview voices even before entering their key.
**Decision:** When `IsApiKeyValid` is false, pass `"dummy"` as the Authorization bearer token for search and get-model calls. When `IsApiKeyValid` is true, pass the user's real key (for potentially better rate limits). Remove all API Key gates from search and by-ID flows.
**Affects:** `SettingsViewModel.EffectiveApiKeyForSearch`, `ExecuteSearchAsync()`, `SubmitVoiceIdAsync()`.

---

## DD-005: Auto-save on property change for all settings except API Key

**Date:** 2026-04-27 (extended 2026-05-06)
**Context:** Users expect slider and toggle changes to persist immediately without a "Save" button. However, API Key is special — it goes through a validation lifecycle and should only be saved when valid (see DD-003).
**Decision:** `OnPropertyChanged` writes every settings property to `_settings` and calls `SaveSettingStorage` immediately — except for `ApiKey`, which triggers validation instead.
**Affects:** `SettingsViewModel.OnPropertyChanged()`.

---

## DD-006: Latency text is shown temporarily after manual refresh

**Date:** 2026-05-06
**Context:** The API latency value (`XXX ms`) is only meaningful right after a refresh. Leaving it visible permanently may mislead users into thinking it's a live indicator.
**Decision:** After a manual credit refresh, show the latency text for 4 seconds, then auto-hide it. A `DispatcherTimer` handles the timeout. Startup credit checks do not show latency at all (see DD-001).
**Affects:** `SettingsViewModel.StartLatencyHideTimer()`, `FetchCreditAsync` `showLatency` param.

---

## DD-007: Config migration for renamed fields

**Date:** 2026-05-06
**Context:** v1.0.2 renamed `Settings.ReferenceId` to `VoiceId` and `CachedModel` to `CachedVoice`. Users upgrading from v1.0.x have configs with the old field names.
**Decision:** Keep nullable "shim" properties (`ReferenceId`, `CachedModel`) in `Settings`. The JSON deserializer populates them from old configs. `Settings.Migrate()` copies values to the new fields, nulls the old ones, and `Main.Init()` calls it once on load, then saves.
**Affects:** `Settings.cs` (shim properties, `NeedsMigration`, `Migrate()`), `Main.Init()`.

---

## DD-008: Preview state keyed by voice ID, not by UI card

**Date:** 2026-05-06
**Context:** The same voice can appear in both the display area (selected voice) and the search results list simultaneously. If preview state were bound to a specific UI card, switching pages or tabs would desync the play/pause button state.
**Decision:** Preview state (`PreviewingVoiceId`, `PreviewProgress`) is stored in the ViewModel and keyed by voice ID. `UpdatePreviewState()` and `SyncPreviewStateToResults()` propagate the state to all VoiceSearchItems that match the current previewing voice ID. The display area's `IsDisplayVoicePreviewing` is also derived from the same voice ID comparison.
**Affects:** `SettingsViewModel` preview system, `VoiceSearchItem.IsBeingPreviewed`/`PreviewProgress`.

---

## DD-009: `condition_on_previous_chunks` and `mp3_bitrate` as user-configurable settings

**Date:** 2026-05-06
**Context:** `mp3_bitrate` affects audio quality vs. file size tradeoff. `condition_on_previous_chunks` affects voice consistency across long texts. Different use cases benefit from different values.
**Decision:** Expose both as configurable settings. `Mp3Bitrate` defaults to 192 (high quality), options: 64/128/192. `ConditionOnPreviousChunks` defaults to `true` (enabled). Both are sent in the TTS request body.
**Affects:** `Settings.cs`, `SettingsViewModel`, `FishAudioApi.PostTtsAsync()`, `SettingsView.xaml`.

---

## DD-010: Random voice when VoiceId is empty

**Date:** 2026-04-27
**Context:** When no `reference_id` is sent in the TTS request, Fish Audio uses a random/default voice. This still costs credit (deducted asynchronously, ~60s delay).
**Decision:** When `VoiceId` is empty, show a localized "Random Voice" title in the display area instead of blank. `FishAudioApi.PostTtsAsync` omits `reference_id` from the body entirely (does not send empty string).
**Affects:** `SettingsViewModel.ApplyCachedVoice()`, `FishAudioApi.PostTtsAsync()`.

---

## DD-011: TextBox focus behavior — click blank space to unfocus

**Date:** 2026-05-06
**Context:** WPF TextBoxes do not lose focus when clicking blank space (unlike HTML inputs). This means the page input TextBox keeps focus after typing, preventing `LostFocus` bindings from firing.
**Decision:** Add a `PreviewMouseLeftButtonDown` handler on the UserControl root. Only clear focus when a TextBox currently holds keyboard focus AND the click target is not inside any interactive control (TextBox, PasswordBox, ButtonBase, ComboBox, Slider, Thumb). This prevents interfering with ComboBox selection and other controls. Page input also supports Enter key to commit immediately via `InputBindings`.
**Affects:** `SettingsView.xaml.cs` (`HasInteractiveAncestor`), page input TextBox `InputBindings`.

---

## DD-012: API Key commit on validation success

**Date:** 2026-05-06
**Context:** `OnPropertyChanged` no longer auto-saves `ApiKey` to `_settings` (see DD-005). But `Main.PlayAudioAsync` checks `Settings.ApiKey` (the `_settings` object) to guard TTS calls. Without committing the key on validation, `_settings.ApiKey` remains empty/stale.
**Decision:** `ConfirmApiKeyAsync` is the sole writer of `_settings.ApiKey`. On valid result → write key and save. On invalid result → write empty and save. `Dispose` does not touch `_settings.ApiKey` (see DD-003).
**Affects:** `SettingsViewModel.ConfirmApiKeyAsync()`.

---

## DD-013: API Key validation via explicit confirm button, simplified state

**Date:** 2026-05-06
**Context:** Auto-validation on text change caused spurious API calls due to WPF LostFocus behavior. The original 5-state `ApiKeyState` enum was only consumed as a binary "valid vs. not valid" distinction — the intermediate states were transient error messages.
**Decision:** Replace `ApiKeyState` enum with a single `bool IsApiKeyValid` and a transient `bool IsValidatingApiKey` (for button disable). Validation only runs when the user clicks the confirm button (checkmark icon). Editing the API Key draft does not clear the applied status by itself; the confirm action clears the old applied balance/status and then revalidates the submitted draft. Format errors are shown inline via `ApiKeyStatusText`; API errors are shown via snackbar (same as refresh button). The confirm button does not display latency — only the refresh button does.
**Affects:** `SettingsViewModel.ConfirmApiKeyCommand`, `IsApiKeyValid`, `IsValidatingApiKey`, `OnPropertyChanged` ApiKey handler.

---

## DD-014: Pagination centering with Grid layout

**Date:** 2026-05-06
**Context:** The search pagination used a `DockPanel` with Prev docked left and Next docked right. When boundary buttons collapsed (Prev on page 1, Next on last page), the remaining Fill area changed size, causing the center "page / total" control to shift horizontally.
**Decision:** Replace `DockPanel` with a 3-column `Grid` using `*` / `Auto` / `*` sizing. The two `*` columns split remaining space equally, guaranteeing the center `Auto` column stays at the midpoint regardless of Prev/Next visibility.
**Affects:** `SettingsView.xaml` pagination section.

---

## DD-015: Preview stop icon instead of pause

**Date:** 2026-05-06
**Context:** The preview button showed a pause icon (&#xE769;, double bars) when audio was playing. However, the actual behavior is stop (audio restarts from the beginning on next play), not pause (which would resume from the current position).
**Decision:** Replace the pause glyph with a filled square (Border element) to match the actual stop behavior. Both display area and search result preview buttons use a Grid containing a play TextBlock icon and a stop Border, toggled via DataTrigger on preview state.
**Affects:** `SettingsView.xaml` display area preview button, search result DataTemplate preview button.

---

## DD-016: Voice ID format validation before API call

**Date:** 2026-05-06
**Context:** Voice IDs follow the same 32-hex format as API Keys. Calling `GET /model/{id}` with a malformed ID always returns 404, wasting a network round-trip.
**Decision:** Check `Settings.IsValidVoiceIdFormat(id)` before calling `GetModelAsync`. On format failure, show localized "Voice ID format incorrect" error inline (`VoiceIdError`) and skip the API call. The regex is shared with `IsValidApiKeyFormat` — both use `HexId32Regex`.
**Affects:** `SettingsViewModel.SubmitVoiceIdAsync()`, `Settings.IsValidVoiceIdFormat()`.

---

## DD-017: Page input — LostFocus commit and numeric-only restriction

**Date:** 2026-05-06
**Context:** The page input TextBox only committed on Enter key. Users who typed a page number and clicked elsewhere expected the navigation to happen, but the value was silently discarded. The TextBox also accepted non-digit characters, which always failed validation.
**Decision:** Add a `LostFocus` handler that invokes `CommitPageInputCommand` (same as Enter). Add a `PreviewTextInput` handler that rejects non-digit characters. `CommitPageInputCommand` already handles out-of-range values by reverting to the current page.
**Affects:** `SettingsView.xaml` PageInputBox, `SettingsView.xaml.cs` (`PageInputBox_PreviewTextInput`, `PageInputBox_LostFocus`).

---

## DD-018: Persistent UI text must use DynamicResource, not GetTranslation

**Date:** 2026-05-06
**Context:** STranslate supports runtime language switching. `DynamicResource` bindings in XAML auto-update when the resource dictionary changes, but strings obtained via `_context.GetTranslation()` or `Application.Current.TryFindResource()` in value converters are captured at call time and become stale. This caused two bugs: the "Random Voice" title and the latency mode display names (Quality First / Balanced / Low Latency) did not update when the language changed.
**Decision:** All user-visible text that persists on screen must use `DynamicResource` in XAML. For conditional text (e.g., "Random Voice" shown only when `CachedVoiceId` is null), use `DataTrigger` + `DynamicResource`. For enum-to-display mappings (e.g., latency mode "normal" → "Quality First"), use `DataTrigger` per value with `DynamicResource` instead of a value converter. Transient text (error messages, snackbar) may use `GetTranslation()` since it's re-triggered on user action.
**Affects:** `SettingsView.xaml` (voice title, latency ComboBox). Removed `LatencyDisplayConverter.cs`.

---

## DD-019: Enter key commits focused settings inputs

**Date:** 2026-05-06
**Context:** The settings page uses several input boxes with adjacent confirm buttons. Users expect Enter to execute the same action while focus remains in the input.
**Decision:** Add Enter `KeyBinding`s to every input with a confirm action: API Key runs `ConfirmApiKeyCommand`, voice search runs `SearchVoicesCommand`, voice ID runs `SubmitVoiceIdCommand`, and page input keeps `CommitPageInputCommand`.
**Affects:** `SettingsView.xaml` input bindings.

---

## DD-020: API Key validation status is an inline state, not raw text

**Date:** 2026-05-06
**Context:** Showing a red `"..."` while waiting for the credit API response looked like an error and did not explain what was happening. After success, the balance changed, but the success state was not explicit enough.
**Decision:** Track API Key validation display state separately from the error message. Waiting uses a neutral inline "waiting for response" message, success uses a green checkmark and localized "verified and applied" message, and format/empty errors remain red inline text. Persistent waiting/success text comes from XAML `DynamicResource`; transient error text may still come from `GetTranslation()`. Startup validation success uses the same status so users can confirm the applied key loaded from config.
**Affects:** `SettingsViewModel.ApiKeyStatusKind`, `ConfirmApiKeyAsync()`, `SettingsView.xaml`, language resource dictionaries.

---

## DD-021: Voice tab initial height is stabilized

**Date:** 2026-05-06
**Context:** Before any search or by-ID submission, both voice selection tabs only show one input row, but their surrounding layout produced slightly different vertical spacing. Switching tabs made the following "Model" section move, which felt unstable.
**Decision:** Place both tab panels in a shared fixed-minimum-height container so their initial one-row states occupy the same vertical layout space. Search results, result count, errors, and pagination still expand the area only when they have real content.
**Affects:** `SettingsView.xaml` voice selection tab panels.

---

## DD-022: Draft API Key edits do not clear applied account state

**Date:** 2026-05-07
**Context:** Users expect the currently applied API Key balance/status to remain visible while they edit a new draft in the input box. Clearing the balance on focus or blur makes the UI look like it lost the applied configuration even though no submit happened.
**Decision:** API Key draft edits are inert until the user explicitly confirms. The applied balance/status remains visible while editing, and only the submit path clears the old applied state before revalidating the draft. This keeps the visible account info tied to the last applied key instead of the transient input.
**Affects:** `SettingsViewModel.OnPropertyChanged(ApiKey)`, `ConfirmApiKeyAsync()`, `ApplyPendingCreditAsync()`, `FetchCreditAsync()`.

---

## DD-023: Build script includes an optional regression test step

**Date:** 2026-05-07
**Context:** The repository already has a small executable regression test project under `tests/`, but the main build script did not expose a single command that ran both the package build and the regression checks in one pass.
**Decision:** Add a `-Test` switch to `build.ps1`. When supplied, the script still performs the normal build/package/`.spkg` verification flow, then runs the regression test project with the same build configuration. This keeps the default build unchanged while giving contributors one documented command for build plus regression verification.
**Affects:** `build.ps1`, `README.md`, localized README files, `CLAUDE.md`, `AGENTS.md`, `.claude/rules/completion-workflow.md`.

---

## DD-024: Cover image cache uses the plugin cache directory

**Date:** 2026-05-07
**Context:** Fish Audio search results return `cover_image` paths from a public CDN. Loading the same voices repeatedly re-fetches the same cover images, while STranslate exposes `PluginCacheDirectoryPath` for files that can be recreated and removed with the plugin cache.
**Decision:** Cache cover images under `PluginCacheDirectoryPath\cover_images` as `<voiceId>.jpg`. The cache scans existing files into an in-memory voice ID index for fast lookup. On a hit, the UI uses the local file URI; on a miss, it shows the CDN URL immediately and creates the cache in the background. The settings page shows cache size with adaptive units and clears only the `cover_images` cache.
**Affects:** `CoverImageCacheService`, `SettingsViewModel`, `SettingsView.xaml`, language resources, README files.

---

## DD-025: Cache cleanup is an explicit text action

**Date:** 2026-05-07
**Context:** The cache cleanup action was initially shown as a trash icon-only button. In practice, that reduced discoverability because users had to infer that the icon cleared the plugin's cover image cache.
**Decision:** Show cache cleanup as a normal localized text button using `STranslate_Plugin_Tts_FishAudio_ClearCache` via `DynamicResource`. The command and cache behavior stay unchanged; only the visible affordance changes from icon-only to explicit text.
**Affects:** `SettingsView.xaml`, settings view regression tests, README files.

---

## DD-026: Cover image cache size is measured from disk

**Date:** 2026-05-07
**Context:** Cache cleanup is best-effort. If a `.jpg` file under `cover_images` is temporarily locked or recreated outside the in-memory index, showing the cached byte counter can report `0 B` while files still exist on disk.
**Decision:** Calculate displayed cache usage by rescanning actual `cover_images/*.jpg` files and rebuilding the in-memory voice ID index on each size query. Cleanup still attempts to delete all files in the cache directory, then refreshes the index from disk so any remaining `.jpg` files keep contributing to the displayed usage.
**Affects:** `CoverImageCacheService`, cover image cache regression tests, README files.

---

## DD-027: Cache cleanup has a busy state with timeout recovery

**Date:** 2026-05-07
**Context:** Clearing `cover_images` can take visible time when files are locked or storage is slow. Without a processing state, repeated clicks can queue duplicate cleanup work; with a naive disabled state, a stuck cleanup path could leave the button unusable.
**Decision:** Run cache cleanup in the background, expose `IsClearingCoverImageCache`, and disable the command while active. The button shows a rotating circular busy indicator. If cleanup does not complete within 10 seconds, restore the button and show a localized retry message; late completion from the old cleanup may refresh cache size but must not unlock a newer cleanup operation.
**Affects:** `SettingsViewModel`, `SettingsView.xaml`, language resources, settings view regression tests, README files.

---

## DD-028: GitHub Actions publishes tagged releases from the matching changelog section

**Date:** 2026-05-07
**Context:** The repository needs a repeatable release path that builds and tests the Windows package, then publishes a GitHub Release with notes limited to the version being shipped.
**Decision:** Trigger the release workflow on pushed `v*` tags, run `build.ps1 -Clean -Configuration Release -Test`, extract only the matching `CHANGELOG.md` section into a temporary release notes file, publish the root `.spkg` with `softprops/action-gh-release`, and use the tag name itself as the GitHub Release title.
**Affects:** `.github/workflows/dotnet.yml`, `CHANGELOG.md`, `CLAUDE.md`, `AGENTS.md`.

---

## DD-029: README documentation follows the user setup workflow

**Date:** 2026-05-07
**Context:** The previous README files were concise but did not guide new users through installation, API Key setup, credit purchase, voice selection, and per-setting behavior in the order they encounter the plugin.
**Decision:** Structure all user-facing README files around Quick Start, Configuration, Emotion Markup, FAQ, and Build sections. The Simplified Chinese README is the source structure, and the four localized README files mirror it. All README files share the same screenshots stored under `docs/images`; root README uses `docs/images/...`, while localized README files under `docs/` use `images/...`.
**Affects:** `README.md`, `docs/README_*.md`, `docs/images`.
