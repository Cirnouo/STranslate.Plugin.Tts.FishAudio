# Fish Audio TTS user guide

[Back to overview](../../README.en.md) · [简体中文](README.md) · [繁體中文](README.zh-TW.md) · **English** · [日本語](README.ja.md) · [한국어](README.ko.md)

[Get started](#installation) · [Choose and manage voices](#voices) · [Models and costs](#playback-and-cost) · [Adjust speech](#configuration) · [Formats and networking](#audio-formats) · [Troubleshoot](#troubleshooting)

## 1. Get started

<a id="installation"></a>
### Install

In STranslate on Windows, open **Settings → Plugins → Marketplace**, search for **Fish Audio TTS**, and install it.

<a id="first-setup"></a>
### Complete a first reading

Search, ID lookup and public-sample previews work without a Key, but still need a connection. Speech synthesis and credit queries require a valid Fish Audio API Key.

1. Create a key on the [official API Key page](https://fish.audio/app/api-keys) and paste it into **API Key** in the plugin. It saves automatically. Keep it private and do not add whitespace or newlines.
2. In **Search**, find a voice, preview it, then click **Apply**. You can also leave the voice unset for a random voice.
3. Choose the synthesis model and check its [cost conditions](#playback-and-cost). For zero spending, confirm official eligibility and explicitly select `s2.1-pro-free` before any test reading.
4. Return to STranslate and read a short sentence with **Fish Audio TTS** selected.

### Optional: credits, manual installation and updates

Click credit refresh if you want to check your balance.

For manual installation, download `STranslate.Plugin.Tts.FishAudio.spkg` from [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases), then import it through **Settings → Plugins → Install**.

Prefer stable STranslate; the verified compatibility baseline is v2.0.10. After an update, check your saved model, format and networking settings. See the [changelog](../../CHANGELOG.md) for changes.

The interface follows the host: Simplified Chinese, Traditional Chinese, English, Japanese, Korean, Russian or Turkish. There is no plugin language selector. Interface language differs from the languages a model can read; see the [official documentation](https://docs.fish.audio).

## 2. Choose and manage voices

<a id="voices"></a>
### Find a voice through three entry points

A voice selects timbre; a model selects the synthesis engine. A voice's community “Model ID” identifies that voice asset, not an engine such as `s2.1-pro`.

| Tab | Action |
|---|---|
| **Search** | Enter a name, then click search or press Enter. |
| **By ID** | Copy an ID from the [official community](https://fish.audio/app/discovery) or a card title. Paste the ID alone, not a URL. Click the magnifier or press Enter to look it up, then click **Apply** on its result. |
| **Likes** | Open locally saved voices; preview or apply after their details load. |

| To do this | Action |
|---|---|
| Hear a sample | Hover over a voice card and click Play on its avatar. During loading or playback, click the square to cancel or stop; it stays available after the pointer leaves. The avatar remains visible beneath the translucent control. After playback ends, Play appears only while the card is hovered. Preview plays an existing public sample without applying the voice or synthesizing your text. |
| Use the voice for reading | Click the tick, **Apply**. To return to random voice, click the cross, **Clear**. Your model and liked voices remain. Random voice does not mean free. |
| Save it for later | Click Like or Unlike to save or remove a voice. Removing it does not clear the voice currently in use. A random voice has no fixed ID to save. |
| Copy its Voice ID | Click the **card title**, not the author or description. |

Open **Search adjustments** inside the search field to choose **All languages** or toggle one or more language tiles. All languages clears specific choices; clearing the last specific restores All. Selecting all ten presets still filters to those presets. Fish Audio determines how selected languages combine. Choose **Relevance first**, **Most used first**, or **Newest first**. **Apply** closes the options and immediately refreshes page one using the current search text, even if nothing changed. **Reset** only restores the option drafts to All languages + Relevance first; **Close** discards drafts. Search or Enter uses the applied options, and changing pages keeps the last successful search. Options last only for the current settings session. If a refresh fails, the previous results and options remain.

Search, By ID, Likes and the selected voice use the same language labels in a separate single-line row: a flag plus a name in the interface language. Wider cards show more labels, with no fixed limit of three; click `+N` to open exactly the remaining N languages in a popup. Names stay visible; only a first label whose full name cannot fit is shortened with an ellipsis (hover for its full name). Missing language metadata hides the row. Labels come from Fish Audio voice metadata, not a fixed synthesis-language whitelist, speaker nationality or a complete account of the engine's language capabilities. Unknown language codes keep their text with a generic icon.

The lower-right date uses your system time zone (`yyyy-MM-dd`); hover for the complete timestamp and UTC offset. Missing or invalid dates show `—`. An empty description hides its entire row without leaving a gap.

Likes are saved on this computer, survive restart and do not synchronize with your Fish Audio account. You can save up to 300; remove one before adding another at the limit. Details and previews still need a connection. A failed lookup does not remove a saved liked voice.

Newly liked voices appear first. Unlike and like a voice again to return it to the front; the order survives restart. Adding a Like from a later page keeps you on that page. Return to page one to see the newest addition.

In Search and Likes, use `‹` / `›` to change pages. The page field accepts digits only and immediately clamps values above the current accessible maximum. Enter a different page and leave the field to navigate, or press Enter; an unchanged page does not reload. Search totals may be approximate (`≈`), unknown (`?`) or limited (`≤`), so some pages may be unavailable. Search with a more specific name if you cannot go further. The count-row arrow hides/shows the list without losing your place; finish page edits before collapsing.

Once a sample is playing from any card, Apply/Clear, Like/Unlike, queries, paging, panel changes and Key edits keep it playing. A matching card that appears again shows current progress and can Stop, even if sample information is temporarily missing. Playback ends with the sample, manual Stop, another preview, a playback failure or closing the plugin. While a sample is still loading, changing selection or the Key, or removing its source card, cancels that loading operation; click Preview again when needed.

## 3. Models and costs

<a id="playback-and-cost"></a>
### Confirm the model and your eligibility

Account registration, credits, capabilities and billing follow the [official documentation](https://docs.fish.audio) and [API pricing](https://docs.fish.audio/developer-guide/models-pricing/pricing-and-rate-limits).

**If spending must be zero, confirm free eligibility first.**

If spending must be zero, check your account under the [official promotion conditions](https://fish.audio/blog/s2-1-pro-free-api/), then explicitly select `s2.1-pro-free`. If you require zero spending and cannot confirm eligibility, use only search and previews; do not synthesize. A random voice, usable key, “free” in a name or unchanged balance is insufficient by itself. Paid models require credits and follow the official API pricing linked above; deductions can lag, and the plugin has no hard spending limit.

| Time | Available models and default |
|---|---|
| Through 30 November 2026 (UTC) | `s2.1-pro-free`, `drama-3`, `s2.1-pro`, `s2-pro`, `s1`; a new configuration defaults to free. |
| From 1 December 2026 (UTC) | Free is hidden; the remaining models stay available. Drama 3 is first in the list, but the default is `s2.1-pro`. |

Valid saved selections survive reordering; an unavailable selection changes to the current default. After the free period, that default, `s2.1-pro`, is a paid model. **Recheck the selected model after updates or promotion expiry, before reading.** Clicking the free-model card selects free; dismissing it remembers the dismissal. The card does not change account eligibility.

S1 remains selectable. The [official notice](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live) announces retirement on 2026-12-31, after which S1 requests are served by S2.1-Pro. Check pricing and plugin updates near that date; a saved choice is not a permanent service guarantee.

### Stop, replace and check credits

Stopping in STranslate or starting new speech cancels the previous playback. New speech waits for the old output to be released. Closing or reloading the plugin also stops its playback. Running speech uses its starting settings; later changes apply next time.

**Stopping does not guarantee zero charges.**

The provider may already have generated or charged for audio. A disconnect, timeout or playback error does not automatically trigger another synthesis or a model switch; decide whether to retry after fixing the cause. Balance updates can lag; an old balance is not a cost safeguard. Server queues, model generation and networking affect the start; free service has no fixed first-audio guarantee.

## 4. Adjust speech

<a id="configuration"></a>
### Four everyday controls

Edits save automatically. These are new-configuration defaults; change one setting at a time to compare the result.

| Setting | Default | Range | Effect |
|---|---|---|---|
| Speed | 1.0 | 0.5–2.0 | Makes speech slower or faster. |
| Volume | 0 dB | -10–+10 dB | Changes synthesis gain; 0 dB means no gain change, not mute. |
| Expressiveness (Temperature) | 0.7 | 0–1 | Higher is more varied, lower more consistent; zero is valid. |
| Diversity (Top P) | 0.7 | 0.05–1 | Adjusts sampling diversity; this is the plugin's allowed range. |

All three methods retain 0.01 precision:

- Click a slider track or drag its thumb, or hover over its slider or resting number for about 350 ms, then use the mouse wheel to adjust by 0.01.
- For an exact value, click the right-aligned number and type in the centered input; its unit stays outside the input. Enter or clicking elsewhere saves; Esc cancels. Invalid, empty or out-of-range input restores the previous value.
- Use a decimal point, or your region's decimal comma.
### Other options

| Option | Default and use |
|---|---|
| Normalize Loudness | On. Shown only for `s2.1-pro-free`, `s2.1-pro` and `s2-pro`. Other models hide it while keeping the value; returning restores it. |
| Latency | Quality First; Balanced and Low Latency are alternatives. Compare results, without expecting a fixed start time. |
| Text Normalization | Off; can turn numbers, units and similar content into more readable text. |
| Context Conditioning | On; uses earlier content within this synthesis for consistency, not audio from previous readings. |

Long text can start playing before the full synthesis finishes. Emotion, pronunciation and speaker markup depend on model support; complex or unclosed markup can delay the start. See the [official documentation](https://docs.fish.audio) for syntax. The plugin is not a complete markup editor.

## 5. Choose formats and configure networking

<a id="audio-formats"></a>
### PCM or MP3?

| Format | When to choose it |
|---|---|
| PCM | New-configuration default. Avoids compressed decoding but uses more network traffic. |
| MP3 | Try it on a slower connection or to reduce traffic. Choose 64 / 128 / 192 kbps; the default is 192. Higher bitrate needs more bandwidth. |

Older configurations generally retain MP3. PCM hides the entire bitrate setting; switching back to MP3 restores its saved value without changing other speech parameters. WAV and Opus are not selectable. Format alone cannot guarantee first-word speed; the server, connection and output device also matter.

<a id="network-and-proxy"></a>
### Check the two connections separately

**A working voice list does not establish that synthesis can connect.** Liked voice details and previews also need networking; saved liked voices are not offline audio.

| Feature | Network settings used |
|---|---|
| Search, voice details, credits, preview samples | STranslate host HTTP networking. |
| Speech synthesis | Windows system default networking/proxy; the connection must permit WebSocket. |

There is no separate proxy switch or address field in the plugin. If Fish Audio is unreachable or search works while speech fails, check host networking and Windows system networking/proxy separately, make sure the proxy service is running and the firewall permits WebSocket, then check model/account permissions. Retry manually after fixing the cause. Do not delete settings or liked voices to repair a temporary connection failure.

## 6. Troubleshoot and recover

<a id="troubleshooting"></a>
### Keys, connection and playback

| Symptom | Next action |
|---|---|
| Search fails after entering a Key | Copy a valid key from the official page without extra whitespace/newlines, or clear the key to browse public voices. Synthesis and credits still require a valid key. |
| Changing the Key stops a lookup or loading | Old queries and previews still loading are canceled; an already playing sample continues. Existing voices, selection and likes stay. Later queries use the new key; start a new lookup or preview when needed. |
| Key editing is temporarily locked | Wait for the current speech or credit request to finish. |
| Credits unavailable | Check the key, account permissions and host connection, then refresh. |
| Search works, speech fails | Check [both connections](#network-and-proxy), then model availability and account permissions. |
| Long wait before audio | Check system networking and output device; compare PCM/MP3. Model generation and free-service queues may still dominate. |
| Silent or interrupted audio | Check Windows output selection, system/app volume and device connection. Fix the cause, then decide whether to retry manually. Volume 0 dB is not mute. |
| Charges after stopping / balance unchanged | Stopping does not reverse provider charges. Wait and refresh, then check the official bill; see [cost limits](#playback-and-cost). |

<a id="voices-favorites-and-saving"></a>

### Voices, liked voices and saving

| Symptom | Next action |
|---|---|
| ID paste rejected | Copy the ID alone without a URL or extra text. Look it up after pasting, then apply. |
| ID lookup error disappears | Errors clear after five seconds, or when you edit the ID or start another lookup. Clearing does not mean the lookup succeeded. Check the ID or connection, look it up again, then apply the result. |
| ID found but selection unchanged | Click the result's tick, **Apply**. Preview and Like do not apply it. |
| Cannot add a liked voice | At 300, remove one first. Otherwise retry saving; if the error repeats, keep your existing liked voices and report the error and steps. |
| A liked voice shows only its ID | Check the host connection and reopen Likes. Its saved ID remains; remove it if you no longer want it. |
| Copying a title fails | Wait briefly and try the title again. If it still fails, copy the ID from the voice's [official community page](https://fish.audio/app/discovery). |
| A number returns to its old value | Enter a value within the [table's range](#configuration) and press Enter. Esc cancels an edit. |
| A save error repeats | Reopen settings to check the saved value. If the problem persists, report the error and steps. Keep your configuration. |
| Cards missing / no more pages | Expand the list first; if search is limited, use a more specific name. |
| Preview fails / no sample | Check host networking and look up the voice again. An unavailable sample is not replaced by synthesized audio. |

### Less frequent settings questions

- Loudness and bitrate follow the model and PCM/MP3 choice. Return to a supported choice to restore their saved values.
- An old Top P of zero becomes 0.05; Temperature zero remains valid. No reset is needed.
- **Clear cache** removes covers only, keeping selection and liked voices. Downloading covers again afterward is expected.

Do not delete settings or liked voices to repair a temporary connection failure. For unresolved problems, open an [issue](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/issues) with plugin/host/Windows versions, steps, model, format, network conditions and sanitized errors. Exclude API Keys, complete signed URLs, proxy passwords and private text. Development and contribution guidance is in [CONTRIBUTING](../../CONTRIBUTING.md).
