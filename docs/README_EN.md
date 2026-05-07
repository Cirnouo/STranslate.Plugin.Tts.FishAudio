<div align="center">
  <a href="https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio">
    <img src="images/icon.svg" alt="Fish Audio TTS" width="128" height="128" />
  </a>

  <h1>Fish Audio TTS</h1>

  <p>
    <a href="https://fish.audio">Fish Audio</a> Text-to-Speech plugin for <a href="https://github.com/ZGGSONG/STranslate">STranslate</a>
  </p>

  <p>
    <img alt="License" src="https://img.shields.io/github/license/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Release" src="https://img.shields.io/github/v/release/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Downloads" src="https://img.shields.io/github/downloads/Cirnouo/STranslate.Plugin.Tts.FishAudio/total?style=flat-square" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" />
    <img alt="WPF" src="https://img.shields.io/badge/WPF-Plugin-blue?style=flat-square" />
  </p>

  <p>
    <a href="../README.md">简体中文</a> | <a href="README_TW.md">繁體中文</a> | <b>English</b> | <a href="README_JA.md">日本語</a> | <a href="README_KO.md">한국어</a>
  </p>
</div>

---

<div align="center">
  <img src="images/overview.png" alt="Plugin overview" width="700" />
</div>


## Feature Overview

- **High-quality synthesis**: Supports Fish Audio S2-Pro / S1 synthesis models and 80+ languages.
- **Voice selection**: Search by name or enter a voice ID directly; supports preview, selection, clearing, and paginated browsing.
- **Cover cache**: Automatically caches voice `cover_image` files to reduce repeated loading; usage and cleanup are available in settings.
- **Account confirmation**: After API Key verification, the plugin shows the balance and "Verified and applied" state so users can confirm the key is active.
- **Synthesis controls**: Supports speed, volume, loudness normalization, MP3 bitrate, expressiveness, diversity, latency mode, text normalization, and context conditioning.
- **Emotion markup**: Add Fish Audio emotion markers in text, such as `[laugh]` for S2-Pro or `(happy)` for S1.
- **Multilingual UI**: Simplified Chinese, Traditional Chinese, English, Japanese, Korean.

## Quick Start

### 1. Install the Plugin

Installing from the STranslate plugin marketplace is recommended. If the marketplace is unavailable, download the `.spkg` from GitHub Releases and install it manually.

**Method 1: STranslate Plugin Marketplace**

1. Open STranslate.
2. Go to **Settings -> Plugins -> Marketplace**.
3. Search for or find **Fish Audio TTS**, then install it.
4. Restart STranslate after installation.

**Method 2: Manual `.spkg` Install**

1. Open the [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) page.
2. Download the latest `STranslate.Plugin.Tts.FishAudio.spkg`.
3. In STranslate, go to **Settings -> Plugins -> Install Plugin**.
4. Select the downloaded `.spkg` file and restart STranslate.

> [!TIP]
> `.spkg` is essentially a ZIP file. STranslate extracts and loads it automatically; you do not need to unzip it manually.

### 2. Get an API Key

1. Sign in to [Fish Audio API Keys](https://fish.audio/app/api-keys).
2. Create or copy an API Key.

<!-- Screenshot: images/fish-audio-api-keys.png
     Content: Fish Audio API Keys page, highlighting where to create/copy an API Key. Hide any real API Key. -->
<div>
  <img src="images/fish-audio-api-keys.png" alt="Fish Audio API Keys page" width="700" />
</div>

3. Paste it into the plugin settings **API Key** input.
4. Click the confirm button, or press `Enter` while the input is focused.
5. When **Verified and applied** appears and the account balance is shown, the current API Key is being used by the plugin.

<!-- Screenshot: images/settings-account-api.png
     Content: Plugin account info, API Key input, confirm button, verified/applied state, and balance. -->
<div>
  <img src="images/settings-account-api.png" alt="Plugin account and API Key settings" width="360" />
</div>

### 3. Buy API Credits

Fish Audio TTS consumes Fish Audio API credits. You can purchase or top up credits at [Console -> Developer -> Billing -> Balance -> Purchase Credits](https://fish.audio/app/developers/billing/).

<!-- Screenshot: images/fish-audio-billing.png
     Content: Fish Audio Billing/Balance/Purchase Credits entry, highlighting where to buy or top up credits. -->
<div>
  <img src="images/fish-audio-billing.png" alt="Fish Audio API credit purchase entry" width="700" />
</div>

> [!NOTE]
> Credit deduction can be delayed. Refreshing the balance immediately after playback may briefly show the old balance.

> [!TIP]
> Register with a `.edu` email and complete student verification to receive Fish Audio student credits. Entry: [Fish Audio Students](https://fish.audio/students/).

### 4. Set a Voice

The voice determines the final timbre used for reading aloud. The plugin provides two methods: **Search** and **By ID**.

> [!NOTE]
> The plugin can be used without setting a voice. In that case it does not send `reference_id` to Fish Audio, and Fish Audio uses a random voice. Random voices still consume API credits.

**Method 1: Search by Name**

1. Switch to the **Search** tab in the voice section.
2. Enter a voice name in the input.
3. Click the search icon, or press `Enter` while the input is focused.
4. Preview voices in the results, then click **Select** on the one you want.
5. If there are many results, use the pagination control to switch pages.

<!-- Screenshot: images/settings-voice-search.png
     Content: Plugin voice search UI with search input, search button, results, preview button, select button, and pagination. -->
<div>
  <img src="images/settings-voice-search.png" alt="Search and select a voice by name" width="450" />
</div>

**Method 2: Use a Voice ID**

1. Open the target voice detail page on Fish Audio.
2. Copy the voice ID from the expanded menu.

<!-- Screenshot: images/fish-audio-voice-id.png
     Content: Fish Audio voice detail page, highlighting where to copy the voice ID. Do not expose account information. -->
<div>
  <img src="images/fish-audio-voice-id.png" alt="Get a voice ID from a Fish Audio voice page" width="650" />
</div>

3. Return to the plugin settings page and switch to the **By ID** tab.
4. Paste the voice ID, then click the confirm button or press `Enter` while the input is focused.
5. The plugin validates the ID and loads the voice information.



<!-- Screenshot: images/settings-voice-by-id.png
     Content: Plugin By ID voice setup UI with voice ID input, paste button, and confirm button. -->
<div>
  <img src="images/settings-voice-by-id.png" alt="Set a voice by voice ID" width="450" />
</div>

## Configuration

The settings page is organized by workflow: account, voice, model, audio output, prosody, generation parameters, and misc.

### Account and API

<!-- Screenshot: images/settings-account-api.png
     Content: Plugin account info, API URL, API Key input, verification state, balance, and refresh button. -->
<div>
  <img src="images/settings-account-api.png" alt="Account and API settings" width="450" />
</div>

| Setting | Description |
| :-- | :-- |
| Account info | Shows the balance for the current API Key in USD. Click **Refresh** to update it manually. |
| API URL | Fish Audio API endpoint, currently fixed to `https://api.fish.audio`. |
| API Key | Fish Audio API key. It is verified and applied only after clicking the confirm button or pressing `Enter`. |
| Verification state | Shows **Waiting for response** while waiting for the server; shows **Verified and applied** on success; format errors appear next to the input. |

### Voice

Search voices by name

<!-- Screenshot: images/settings-voice-search.png
     Content: Selected voice display + Search tab, showing cover, title, author, popularity, preview, select, and clear actions. -->
<div>
  <img src="images/settings-voice-search.png" alt="Voice search settings" width="450" />
</div>

Get voices by ID

<!-- Screenshot: images/settings-voice-by-id.png
     Content: By ID tab with one input row and controls, showing initial height matching the Search tab. -->
<div>
  <img src="images/settings-voice-by-id.png" alt="Configure voice by ID" width="450" />
</div>

| Setting | Description |
| :-- | :-- |
| Selected voice | Shows the currently applied voice information. When no voice is set, it shows **Random Voice**. |
| Search | Search Fish Audio voices by name, preview them, then select one. Result cover images are cached automatically. |
| By ID | Useful when you already know the voice ID. Voice IDs must be 32 lowercase hexadecimal characters. |
| Clear voice | Clears the current voice setting and returns to random voice. |

### Model and Audio Output

<!-- Screenshot: images/settings-model-audio.png
     Content: Synthesis model and MP3 bitrate setting cards. -->
<div>
  <img src="images/settings-model-audio.png" alt="Model and audio output settings" width="450" />
</div>

| Setting | Default | Description |
| :-- | :--: | :-- |
| Synthesis model | `s2-pro` | Selects the Fish Audio synthesis engine. `s2-pro` is recommended and supports 80+ languages plus richer emotion markers; `s1` is available for cases that need the older model behavior. |
| MP3 bitrate | `192 kbps` | Controls output quality and size. Available values: `64`, `128`, `192`. Higher bitrate usually means better quality and larger files. |

### Prosody

<!-- Screenshot: images/settings-prosody.png
     Content: Speed, volume, and loudness normalization settings. -->
<div>
  <img src="images/settings-prosody.png" alt="Prosody settings" width="450" />
</div>

| Setting | Default | Description |
| :-- | :--: | :-- |
| Speed | `1.0` | Controls reading speed, from `0.5` to `2.0`. Lower than `1.0` is slower; higher than `1.0` is faster. |
| Volume | `0 dB` | Controls volume offset, from `-10 dB` to `+10 dB`, with `0.1 dB` precision. |
| Loudness normalization | On | Shown only for the `s2-pro` model. Helps keep output loudness stable. |

### Generation Parameters

<!-- Screenshot: images/settings-generation.png
     Content: Expressiveness, diversity, latency mode, text normalization, and context conditioning settings. -->
<div>
  <img src="images/settings-generation.png" alt="Generation parameter settings" width="450" />
</div>

| Setting | Default | Description |
| :-- | :--: | :-- |
| Expressiveness | `0.7` | Corresponds to generation sampling temperature. Higher values create more expressive variation; lower values make output more stable. |
| Diversity | `0.7` | Corresponds to `top_p`. Higher values broaden sampling; lower values make results more constrained. |
| Latency mode | Quality | Balances quality and response speed: Quality, Balanced, Low Latency. |
| Text normalization | Off | Converts numbers, unit symbols, and similar content into text that is easier to read aloud. |
| Context conditioning | On | Uses previous audio as context to help long text keep voice consistency. |

### Misc

<!-- Screenshot: images/settings-misc-cache.png
     Content: Cache usage, clear cache button, and busy indicator in the Misc section. -->
<div>
  <img src="images/settings-misc-cache.png" alt="Cache settings" width="450" />
</div>

| Setting | Description |
| :-- | :-- |
| Cache usage | Scans actual files under `cover_images/*.jpg` in the plugin cache directory and displays the size using B, KB, MB, GB, and other adaptive units. |
| Clear cache | Deletes cached voice cover images. During cleanup, the button is disabled and shows a rotating busy indicator; it becomes clickable again after completion or timeout. |

<details>
<summary><b>Parameter Reference</b> (click to expand)</summary>

| Parameter | Default | Description |
| :-- | :--: | :-- |
| API Key | - | Fish Audio API key, required. |
| Voice ID | - (Random Voice) | Search to select or enter manually. When empty, a random voice is used. |
| Synthesis model | `s2-pro` | `s2-pro` or `s1`. |
| MP3 bitrate | `192 kbps` | Available values: `64`, `128`, `192`. |
| Speed | `1.0` | Range `0.5` to `2.0`. |
| Volume | `0 dB` | Range `-10 dB` to `+10 dB`. |
| Loudness normalization | On | Shown only for the `s2-pro` model. |
| Expressiveness | `0.7` | Range `0` to `1`. |
| Diversity | `0.7` | Range `0` to `1`. |
| Latency mode | Quality | Quality / Balanced / Low Latency. |
| Text normalization | Off | Converts numbers, unit symbols, and similar content into text that is easier to read aloud. |
| Context conditioning | On | Uses previous audio as context to keep voice consistency. |

</details>

## Emotion Markup

Fish Audio controls emotion through inline text markers. No extra API parameters are needed.

**S2-Pro** (recommended) uses square brackets and natural language descriptions, and can be placed anywhere in the text:

```text
[angry] This is unacceptable!
I can't believe [gasp] you actually did it [laugh]
[whisper] This is a secret
```

**S1** uses parentheses and a fixed tag set, usually placed at the beginning of a sentence:

```text
(happy) What a beautiful day!
(sad)(whispering) I miss you so much.
```

## FAQ

**Will it cost credits if I do not set a voice?**

Yes. When no voice is set, Fish Audio uses a random voice to synthesize audio, and API credits are still consumed.

**Do previews cost credits?**

No. Plugin previews play the public sample audio attached to Fish Audio voice entries and do not call the TTS synthesis endpoint. This is why previews also work before an API Key is verified. Only actual STranslate text-to-speech synthesis requires a valid API Key and consumes credits.

**Why does the balance not change immediately after playback?**

Fish Audio credit deduction can be delayed. Refreshing the balance immediately after playback may briefly show the old balance.

**Do I need to configure an API Key before voice search?**

Voice search, By ID lookup, and previews can be used before API Key verification. Actual speech synthesis requires a valid API Key and available credits.

**Will clearing the cache affect the selected voice?**

No. Clearing the cache only deletes cached cover images. The voice ID and selected voice information remain saved; covers are loaded again when displayed later.

## Build

```powershell
# Standard build (Debug + .spkg packaging)
.\build.ps1

# Clean build
.\build.ps1 -Clean

# Clean build and run regression tests
.\build.ps1 -Clean -Test

# Release build
.\build.ps1 -Configuration Release
```

Build output goes to the repository root as `STranslate.Plugin.Tts.FishAudio.spkg`.

<details>
<summary><b>Requirements</b></summary>

- .NET 10.0 SDK
- Windows (WPF project)

</details>

## Acknowledgements

- [STranslate](https://github.com/ZGGSONG/STranslate) — A ready-to-use translation and OCR tool
- [Fish Audio](https://fish.audio) — Text-to-speech API provider
- [iNKORE WPF Modern UI](https://github.com/iNKORE-NET/UI.WPF.Modern) — Modern UI control library for WPF

## License

[MIT](../LICENSE)
