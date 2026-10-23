# Fish Audio TTS user guide

[Back to overview](../README_EN.md) · [简体中文](README.md) · [繁體中文](README_TW.md) · **English** · [日本語](README_JA.md) · [한국어](README_KO.md)

This guide covers installation, operation and settings in the STranslate plugin. For account registration, key creation, credits, model capabilities and billing, use the [official documentation](https://docs.fish.audio), [API key page](https://fish.audio/app/api-keys) and [API pricing](https://docs.fish.audio/developer-guide/models-pricing/pricing-and-rate-limits).

[Installation](#installation) · [First setup](#first-setup) · [Voices](#voices) · [Configuration](#configuration) · [Audio formats](#audio-formats) · [Network and proxy](#network-and-proxy) · [Playback and cost](#playback-and-cost) · [Troubleshooting](#troubleshooting)

<a id="installation"></a>
## Installation and updates

Use STranslate on Windows. In **Settings → Plugins → Marketplace**, find **Fish Audio TTS** and install it.

For manual installation, download `STranslate.Plugin.Tts.FishAudio.spkg` from [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases), then import it through **Settings → Plugins → Install**. Prefer the current stable STranslate release. The plugin uses SDK 1.0.15; its verified compatibility baseline is STranslate v2.0.10. Ordinary installation does not require setting up the .NET SDK; that is for source builds.

After updating, check your saved model, audio format and networking settings. Keep your configuration when diagnosing ordinary connection problems. See the [changelog](../../CHANGELOG.md) for version changes.

<a id="first-setup"></a>
## First setup

1. Open the plugin settings and paste a key created on the official page into **API Key**. Edits save automatically; there is no confirmation step. Do not add whitespace/newlines or share the key.
2. Refresh your credits if needed. Startup tries silently; before a result is available, the row says **Click refresh**. Credit requests and speech temporarily lock the API Key input and release it afterward.
3. Search and preview voices, then click the card's tick (**Apply**). Alternatively, look up an ID on **By ID** and apply its result, or choose from **Favorites**. You may also leave the voice unset for a random voice.
4. Check the synthesis model and choose PCM or MP3 before reading a short text in STranslate. If spending must be zero, read the [cost constraints](#playback-and-cost) before synthesis; a test request cannot establish eligibility.

Search, lookup by ID and previews work without an API Key. Synthesis and credit queries require a usable key. The plugin accepts legacy 32-character lowercase hexadecimal keys and newer `sk-fish-` keys followed by 43 permitted characters. Passing the local shape check does not establish authentication or sufficient credits.

Without a configured key, voice list/detail requests use `dummy`; once configured, they use the entered key, including Favorites loading, startup voice refresh and preview-link recovery. A rejected key is not retried with `dummy`. Sample and cover downloads carry no key. Changing the key cancels old queries while retaining displayed Search/By-ID results, Favorites and selected voice information; subsequent lookups use the new key. The shared in-memory metadata cache isolates entries by credential; another credential's entries are not reused.

The interface follows STranslate's language automatically: Simplified Chinese, Traditional Chinese, English, Japanese or Korean. There is no separate plugin language selector. Interface language and the language of text being read are independent; consult official model language support.

API Key and By ID place their Paste icons inside the right edge of each input, with text space reserved. Pasting a key still saves automatically. Pasting an ID only fills the input; click its magnifier or press Enter to look it up, then apply the result separately.

The Voice ID field accepts partial input of up to 32 ASCII lowercase hexadecimal characters (`0–9`, `a–f`); lookup still requires all 32. Invalid typed characters are blocked. Both Ctrl+V and the embedded Paste button reject an entire invalid paste and preserve the existing input. Paste the ID alone: uppercase letters, whitespace, URLs and overlength text are rejected, with no trimming, ID extraction, lowercase conversion or truncation.

<a id="voices"></a>
## Search, selection and previews

The tabs are **Search**, **By ID**, then **Favorites**. The active tab keeps its accent when hovered or pressed. The selected voice and Search/By ID/favorite cards synchronize applied state by voice ID. Unapplied cards show a tick; cards matching the applied ID show a cross. Their tooltips and accessible actions are Apply and Clear. These are uniform borderless icon controls with hover and keyboard-focus feedback. A cross on any matching card clears the selected voice. Lookup, preview and favorites remain independent of selection.

The selected card always shows Cross/Clear while a voice is selected. Matching browse cards also show Cross, including ID-only placeholders where Clear remains available; only an unapplied placeholder's Apply waits for valid metadata. Failed settings saves preserve the previous selection and card state.

On any selected voice, Search, By ID or Favorites card that has a voice ID, clicking the title copies that ID to the clipboard. A successful copy shows a floating confirmation; a failed copy shows an error. Author names and descriptions remain ordinary text without this copy action. Copying does not apply or preview the voice or change favorites.

On **Search**, enter a name and press Enter or click search. Editing the input does not immediately submit it. A successful new query applies its results and returns to page one. Preview plays an existing public sample; the tick (**Apply**) selects the voice. The selected card and matching browsing cards share preview state. Preparation shows a spinner and cancel square; actual playback then shows output progress. Click again during loading/playback to stop; switching voice also stops the old preview.

Search results first appear expanded. An icon-only toggle sits at the right of the result-count row, with no separate text header. It shows/hides the hints, cards and pagination below; the count row stays visible. Query, results, page and selection remain, and the current settings view retains expansion across tabs/new searches. Finish a page edit with Enter or restore it before collapsing, since leaving the input can submit the edited page.

Search and favorite paging use bare `‹` / `›` chevrons with the exact tooltip/accessibility actions **Prev** and **Next** rather than visible words or rectangular buttons. Page input commits with Enter or focus loss. Search requests six raw records and filters non-TTS items. `≈` means approximate total, `?` unknown, and `≤` an accessible page upper bound. A provider window may expose only part of the total. Without a bound, only visited pages and the allowed next page are reachable; failed/empty pages are not skipped automatically.

On **By ID**, enter the full 32-character lowercase hexadecimal ID and click the magnifier or press Enter. This only loads one preview/star/selection-icon card without changing selection. Use its tick (**Apply**) when unapplied; an already-applied ID instead shows a cross (**Clear**) that cancels selection. Editing the input or starting another lookup removes the old card. By ID has no paging/fold control. Obtain IDs from the [official community](https://fish.audio/app/discovery); a voice-asset “Model ID” differs from an engine such as `s2.1-pro`. A cross on the selected card or any matching result restores random voice while retaining model, favorites and billing rules.

### Favorites

The Favorites tab has a star icon. The selected/applied voice card also shows the shared favorite star whenever it has a voice ID. Use it to add/remove that voice from the same ID list used by Search, By ID and Favorites; all matching stars synchronize. Bookmarking does not change selection, and a random voice has no fixed ID to bookmark.

An outlined five-point star means unsaved; a filled gold star means favorite. Click to toggle it. Matching cards in Search, By ID and Favorites stay synchronized by voice ID. Starring and applying are independent; removing a favorite does not clear the selected voice. **Favorites**, to the right of By ID, persists only an ordered JSON array of voice IDs in `favorite-voices.json` inside the host-provided settings directory for this plugin. Favorite metadata is not stored in the main settings, and favorites do not synchronize with a Fish Audio account. IDs survive restart; display information comes from the shared in-memory cache or a voice detail lookup on a cache miss.

The list starts empty and holds up to 300 voices. At the limit, a message asks you to remove one before adding another; no entry is evicted. Stars/list publish only after the file is saved successfully; a failed save preserves their old state. Favorites has six cards per page, exact local counts, previous/next and a page field committed with Enter or focus loss. Successful removal updates immediately. If the last page becomes empty while other entries remain, the list moves to the preceding valid page. An empty list shows only its empty-state hint, with no result count, collapse control or pager. Removing the last entry shows that hint even if the list was collapsed; storage errors are reported separately. While detail is unavailable, a placeholder remains bookmarked and removable, with Apply unavailable until valid metadata loads. Network/not-found errors never delete its saved ID automatically.

When Favorites is nonempty, its count row has an independent icon-only toggle on the right. The count remains visible while hints, cards and pagination collapse. It starts expanded and keeps independent view-local state across tab/list changes. Finish page edits before collapsing. Search, By ID, Favorites and preview detail refresh share a bounded in-memory metadata cache; it does not survive restart or contain audio. Cover images retain their existing shared persistent cache; clearing covers does not remove favorite IDs. Sample URLs still pass the expiry policy below even on a metadata-cache hit. Changing favorite page, removing the originating favorite or replacing an ID result stops its preview; late responses cannot restore a favorite or restart old playback.

Preview never calls TTS. A usable sample URL can be reused without fetching detail on every play. Signed URLs refresh within thirty seconds of expiry, or conservatively when signature timing is missing/invalid. A sample 403/404 allows at most one detail refresh and one retry; an earlier expiry refresh consumes that same budget. Not all URLs are signed, and an unsigned URL is not guaranteed permanent. A missing sample or refresh failure does not trigger synthesized replacement audio.

Search, By ID and Favorites share the existing persistent cover-image cache by voice ID under `cover_images` in the host's plugin-specific cache directory. Favorites does not make a second image copy. Local hits display first; misses load and cache a cover again. **Clear cache** deletes only these images, never the favorite-ID file in the separate settings directory or shared voice metadata. After clearing, existing cards use their canonical remote cover source; a later search, By-ID lookup or favorite-page load can download and cache covers again.

There is no reusable audio cache across plays. Every preview fetches the sample through host HTTP into that play's memory buffer, released on stop, end or failure. Selected-voice caching stores metadata and the sample URL; the cover cache stores images. Preview progress represents output playback position, not download progress.

The shared metadata cache holds up to 300 entries and evicts the least recently used. Its explicit fifteen-minute client TTL uses a monotonic clock. It survives plugin reinitialization within the same application lifetime, not application restart. Remote search/detail responses populate it; bookmarking does not renew TTL or replace newer metadata. Preview expiry and sample 403/404 bypass this cache for fresh detail while retaining the one-refresh budget. The TTL is not a sample-URL validity guarantee.

<a id="configuration"></a>
## Configuration

Editable settings save automatically. Defaults below apply to new configurations; legacy format migration is explained afterward. A voice selects timbre, a synthesis model selects the engine, and an API Key authenticates the account.

To enter an exact Speed, Volume, Expressiveness (Temperature) or Diversity (Top P), click the number beside its slider. It becomes an input with the same fixed bounds; units such as `dB` remain outside. Leaving the input or pressing Enter validates and saves the value and updates the slider; Esc cancels. Empty, malformed, non-finite or out-of-range input restores the committed value. All four settings display and save two decimal places. Dragging, wheel adjustment and direct input share 0.01 precision, preserving values such as Top P `0.82` and Volume `1.03 dB`.

Hover over a slider or its resting numeric text to adjust the value with the mouse wheel. Each notch changes any of the four settings by 0.01, stopping at the allowed bounds. Tracks, thumbs and resting numeric text receive this action; adjacent units do not, and the page does not scroll during adjustment. Wheel input over a slider shows the same current-value bubble as dragging; it disappears after a brief idle interval or pointer leave. Once the number becomes an input, wheeling over that input leaves its value unchanged and scrolls the page normally.

Numeric editing accepts ASCII digits `0–9`, at most one decimal separator, and a leading minus for Volume only. You can use `.`; `,` is also accepted when it is the current culture's decimal separator. Speed, Temperature and Top P allow four characters, one integer digit and two fractional digits. Volume allows six characters, two integer digits and two fractional digits, including `-10.00`. Invalid typing is not displayed; invalid paste is rejected whole while preserving the draft. Incomplete drafts remain editable, with range validation on commit.

| Setting | Default | Choices and effect |
|---|---|---|
| API Key | Empty | Credential for synthesis and credits; paste is supported. |
| Voice ID | Empty / random voice | Use the tick (**Apply**) on a Search, By ID or Favorites card; omission does not mean free. |
| Favorites | Empty list | Separate file stores ordered IDs only; up to 300, six per page, stars add/remove, no account sync. |
| Synthesis model | `s2.1-pro-free` during promotion, then `s2.1-pro` | Availability/default policy below. |
| Audio format | PCM | PCM / MP3; older configurations generally retain MP3. |
| MP3 bitrate | 192 kbps | 64 / 128 / 192; the entire setting is hidden under PCM and restored with its value under MP3. |
| Speed | 1.0 | 0.5–2.0; dragging, wheel and direct entry all use 0.01 precision. |
| Volume | 0 dB | -10–+10 dB; dragging, wheel and direct entry all use 0.01 precision. Synthesis gain, not a mute switch. |
| Normalize Loudness | On | Supports S2-family models; shown/sent only for the three allowed models, hidden elsewhere with its value retained. |
| Expressiveness | 0.7 | 0–1; dragging, wheel and direct entry all use 0.01 precision. Higher is more varied, lower more consistent. |
| Diversity | 0.7 | Plugin range 0.05–1; dragging, wheel and direct entry all use 0.01 precision. Top P sampling control. |
| Latency | Quality First (`normal`) | Quality First / Balanced (`balanced`) / Low Latency (`low`); no fixed first-word guarantee. |
| Text Normalization | Off | Converts numbers, units and similar content to readable text. |
| Context Conditioning | On | Uses earlier chunks of this synthesis, not audio from previous requests. |
| Credit refresh | Silent startup attempt | Manual refresh displays latency; successful speech refreshes silently. Not a spending cap. |
| Cover cache | Automatic | Shows disk usage and **Clear cache**; voice ID, metadata and favorites remain. |
| Free-model promotion card | Not dismissed | Click selects free; closing remembers dismissal. It does not change eligibility. |
| Interface language | Host language | Five automatic locales; no separate plugin selector. |

Normalize Loudness is shown and `normalize_loudness` sent only for `s2.1-pro-free`, `s2.1-pro` and `s2-pro`. It has no effect for S1. Drama 3 support is unconfirmed, so the current client capability policy hides the setting; this is not a claim of proven server incompatibility. Hidden values are retained and the request field omitted, then restored when selecting an allowed model.

Before **2026-12-01T00:00:00Z**, the ordered model list is `s2.1-pro-free`, `drama-3`, `s2.1-pro`, `s2-pro`, `s1`, with free as default. At and after that instant, free is hidden and the remaining list starts with Drama 3, but the default remains `s2.1-pro`. First list item and default are independent. Valid saved selections survive reordering; unavailable selections normalize to the current default. The promotion follows [official conditions](https://fish.audio/blog/s2-1-pro-free-api/).

S1 remains selectable. The [official WS page](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live) announces retirement on 2026-12-31, after which S1 requests are served by S2.1-Pro. Check official pricing and plugin updates near that date; an old saved model is not a permanent service guarantee.

Main settings use SchemaVersion=2. Migration preserves valid fields. Unversioned/version-1 settings default missing or invalid audio format to MP3 and retain valid bitrate; new settings default to PCM. Favorite IDs use a separate file, not a metadata field in main settings, and introduce no new settings schema. Format changes retain other parameters and the hidden MP3 bitrate; model changes retain hidden loudness values. When loading settings, invalid numeric ranges revert to defaults; valid numbers round to display precision rather than slider increments. Future/duplicate version markers protect the main host settings file; do not lower the version manually to force saving.

Legacy streaming proxy mode/address values no longer apply; ordinary canonical saves remove those obsolete fields within schema 2.

Top P's 0.05 floor is a plugin compatibility policy, not an assertion about the official API minimum. A stored zero moves to 0.05; negative, non-finite or greater-than-one values revert to 0.7. Expressiveness (Temperature) still accepts zero and retains its 0–1 range.

<a id="audio-formats"></a>
## PCM and MP3

PCM uses 44.1 kHz, 16-bit mono and avoids compressed decoding, at about 88,200 bytes per second. MP3 uses continuing decoding; 64/128/192 kbps reduces network traffic, with higher bitrate requiring more bandwidth. Try MP3 for slower links or PCM to avoid compressed processing. Server queues, network transit, device buffering and initial silence affect end-to-end start. Neither format guarantees a fixed first-word time.

Long input is sent lazily in segments while audio is received and played. Ordinary text can begin before the full synthesis finishes. Complex or unclosed emotion/phoneme markup may delay a safe flush. The plugin preserves text order and is not a complete markup editor; model-specific emotion, pronunciation and speaker syntax belongs in the [official documentation](https://docs.fish.audio). WAV/Opus are not selectable.

<a id="network-and-proxy"></a>
## Network and system connection

Formal speech uses native WebSocket and always follows the system default proxy. The plugin has no separate proxy switch, mode or address setting. Voice search/detail, credits and preview samples continue to use STranslate host HTTP settings. Both paths must work; a successful HTTP request does not establish WebSocket connectivity.

If search works but speech fails, check Windows system networking/proxy, the availability of your system proxy service and firewall support for WebSocket, alongside model availability and account permissions. There is no proxy field to configure inside the plugin. Retry manually after fixing the connection; the plugin does not switch models or start another synthesis automatically.

<a id="playback-and-cost"></a>
## Playback, stopping and cost

Use the plugin through STranslate's read-aloud feature. New speech cancels previous speech and waits for old output cleanup. Host stop, initialization retirement and closing also cancel the owned playback. A running request uses the settings captured at its start; later edits apply next time. Normal completion waits for received real audio to finish playing.

Cancellation stops the client; it does not establish that the provider generated nothing or charged nothing. Disconnects, timeouts and decode errors do not cause an automatic second synthesis or switch to a paid model, HTTP or another transport. Startup normalization of an expired model can choose the current default, so check the selection after updates or promotion expiry.

If spending must be strictly zero, synthesize only after current official conditions establish eligibility and you explicitly select `s2.1-pro-free`. If eligibility cannot be confirmed, stay with search/previews and do not synthesize. A random voice, valid-looking key, unchanged balance or “free” in a name is insufficient on its own. Paid models require credits; deductions can lag and the plugin has no hard spending limit. Free service has no fixed first-audio latency guarantee.

<a id="troubleshooting"></a>
## Troubleshooting

| Symptom | Check / action |
|---|---|
| Loudness missing / old Top P becomes 0.05 | Loudness follows the client model list and retains its hidden value. Top P zero moves to the compatibility floor; Temperature zero remains valid. |
| Key format error | Copy again from the official page; remove whitespace/newlines. Do not confuse key and voice ID. |
| Search works, speech fails | Check host HTTP and system WebSocket networking separately, plus model availability/account permissions. |
| Large total but no more pages | The provider window may be smaller; refine the name instead of inferring jumpable pages from total. |
| Cards disappear after collapse | Expand results again; query/page/selection remain. |
| ID found but selection unchanged | Click the result card's tick (**Apply**); lookup, preview and star do not apply automatically. |
| Cannot add a favorite / placeholder only | Check the 300 limit, file-save feedback and voice-detail connectivity. Failed detail never deletes its ID; it remains removable. |
| Preview unavailable/fails | Check host networking; the voice may lack a sample or its URL may expire. Query detail again. No TTS replacement occurs. |
| Slow initial playback | Check system networking and output device; compare PCM/MP3. Free queues/model generation can still dominate. |
| Silent/interrupted output | Check Windows output selection, system/app volume and device connection; retry manually after fixing. |
| Balance unchanged | Wait and refresh manually; old balance does not prove free use. |
| Covers reload after cleanup | Expected: only image files were deleted; voice settings and favorites remain. |

For unresolved problems, open an [issue](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/issues) with plugin/host/Windows versions, steps, model/format and system-network conditions and sanitized errors. Exclude API Keys, full signed URLs, proxy passwords and private text. For development, see [CONTRIBUTING](../../CONTRIBUTING.md).
