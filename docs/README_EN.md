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
- **Synthesis controls**: Supports speed, volume, loudness normalization, MP3 bitrate, expressiveness, diversity, latency mode, text normalization, and context conditioning.
- **Emotion markup**: Add Fish Audio emotion markers in text, such as `[laugh]` for S2-Pro or `(happy)` for S1.
- **Multilingual UI**: Simplified Chinese, Traditional Chinese, English, Japanese, Korean.

## Quick Start

### Installation

Installing from the STranslate plugin marketplace is recommended.

**STranslate Plugin Marketplace**

1. Open STranslate.
2. Go to **Settings -> Plugins -> Marketplace**.
3. Search for or find **Fish Audio TTS**, then download and install it.

**Manual install**

1. Open the [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) page.
2. Download the latest `STranslate.Plugin.Tts.FishAudio.spkg`.
3. In STranslate, go to **Settings -> Plugins -> Install**.
4. Select the downloaded `.spkg` file.

### Get an API Key

1. Sign in to [Fish Audio API Keys](https://fish.audio/app/api-keys).
2. Create or copy an API Key.

<!-- Screenshot: images/fish-audio-api-keys.png
     Content: Fish Audio API Keys page, highlighting where to create or copy an API Key. Hide any real API Key. -->
<div>
  <img src="images/fish-audio-api-keys.png" alt="Fish Audio API Keys page" width="700" />
</div>

3. Paste it into the plugin settings **API Key** input.
4. Click the confirm button, or press `Enter` while the input is focused.
5. When **Verified and applied** appears and the account balance is shown, the current API Key is being used by the plugin.

### Buy API Credits

Fish Audio TTS consumes Fish Audio API credits. You can purchase or top up credits at [Console > Developer > Billing > Balance > Purchase Credits](https://fish.audio/app/developers/billing/).

<!-- Screenshot: images/fish-audio-billing.png
     Content: Fish Audio Billing/Balance/Purchase Credits entry, highlighting where to buy or top up credits. -->
<div>
  <img src="images/fish-audio-billing.png" alt="Fish Audio API credit purchase entry" width="700" />
</div>

> [!TIP]
> Register with a `.edu` email and complete student verification to receive 5 USD in credits. Entry: [Fish Audio Students](https://fish.audio/students/).

### Set a Voice

The voice determines the timbre used for the final reading. The plugin offers **Search** and **By ID**.

> [!NOTE]
> The plugin can be used without setting a voice. In that case it does not send `reference_id` to Fish Audio, and Fish Audio uses a random voice. Random voices still consume API credits.

**Search by Name**

1. Switch to the **Search** tab in the voice section.
2. Enter a voice name in the input.
3. Click the search icon, or press `Enter` while the input is focused.
4. Preview voices in the results, then click **Select** on the one you want.
5. If there are many results, use the pagination control to switch pages.

**By Voice ID**

1. Open the target voice detail page on Fish Audio.
2. Copy the voice ID from the expanded menu.
3. Return to the plugin settings page and switch to the **By ID** tab.
4. Paste the voice ID, then click the confirm button or press `Enter` while the input is focused.
5. The plugin validates the ID and loads the voice information.

<!-- Screenshot: images/fish-audio-voice-id.png
     Content: Fish Audio voice detail page, highlighting where to copy the voice ID. Do not expose account information. -->
<div>
  <img src="images/fish-audio-voice-id.png" alt="Get a voice ID from a Fish Audio voice page" width="650" />
</div>

## Configuration

<details>
<summary><b>Parameter Reference</b> (click to expand)</summary>

| Setting | Default | Description |
| :-- | :--: | :-- |
| API Key | - | Fish Audio API key, required. |
| Voice ID | Random Voice | Search to select or enter manually. When empty, a random voice is used. |
| Synthesis model | `s2-pro` | `s2-pro` or `s1`. |
| MP3 bitrate | `192 kbps` | Available values: `64`, `128`, `192`. |
| Speed | `1.0` | Range `0.5` to `2.0`. |
| Volume | `0 dB` | Range `-10 dB` to `+10 dB`. |
| Loudness normalization | On | Shown only for the `s2-pro` model. |
| Expressiveness | `0.7` | Range `0` to `1`. |
| Diversity | `0.7` | Range `0` to `1`. |
| Latency mode | Quality first | Quality first / Balanced / Low latency. |
| Text normalization | Off | Converts numbers, unit symbols, and similar content into text that is easier to read aloud. |
| Context conditioning | On | Uses previous audio as context to help keep voice consistency across long texts. |

</details>

## Emotion Markup

Fish Audio controls emotion through inline markers in the text, with no extra API parameters required.

**S2-Pro** (recommended) uses square brackets and natural-language descriptions. It can appear anywhere in the text:

```text
[angry] This is unacceptable!
I can't believe [gasp] you actually did it [laugh]
[whisper] This is a secret
```

**S1** uses parentheses and a fixed tag set, usually placed at the start of a sentence:

```text
(happy) The weather is great today!
(sad)(whispering) I miss you.
```

## FAQ

**Q: Does it charge if I don't set a voice?**

A: Yes. When no voice is set, Fish Audio generates audio with a random voice and still consumes API credits.

**Q: Does preview playback charge credits?**

A: No. Preview playback uses the voice's public audio and does not call the TTS API, so it also works without a verified API Key.

**Q: Why doesn't the balance change immediately after playback?**

A: Fish Audio's balance deduction may be delayed. Refreshing the balance immediately after playback may briefly show the old balance.

**Q: Do I need to configure an API Key before voice search?**

A: Voice search, voice lookup by ID, and preview playback can all be used without a verified API Key. Real synthesis still requires a valid API Key and available balance.

**Q: Will clearing the cache affect the selected voice?**

A: No. Clearing the cache only removes voice cover image cache files. The voice ID and selected voice information remain intact, and the cover is reloaded the next time it is shown.

## Build

```powershell
# Standard build (Debug + .spkg package)
.\build.ps1

# Clean build
.\build.ps1 -Clean

# Clean build and run regression tests
.\build.ps1 -Clean -Test

# Release build
.\build.ps1 -Configuration Release
```

Build output is written to `STranslate.Plugin.Tts.FishAudio.spkg` in the repository root.

<details>
<summary><b>Environment Requirements</b></summary>

- .NET 10.0 SDK
- Windows (WPF project)

</details>

## Credits

- [STranslate](https://github.com/ZGGSONG/STranslate) - a ready-to-use translation and OCR tool
- [Fish Audio](https://fish.audio) - the speech synthesis API provider
- [iNKORE WPF Modern UI](https://github.com/iNKORE-NET/UI.WPF.Modern) - a modern UI control library for WPF

## License

[MIT](LICENSE)
