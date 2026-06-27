<div align="center">
  <a href="https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio">
    <img src="images/icon.svg" alt="Fish Audio TTS" width="128" height="128" />
  </a>

  <h1>Fish Audio TTS</h1>

  <p>
    <a href="https://fish.audio">Fish Audio</a> 語音合成外掛 for <a href="https://github.com/ZGGSONG/STranslate">STranslate</a>
  </p>

  <p>
    <img alt="License" src="https://img.shields.io/github/license/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Release" src="https://img.shields.io/github/v/release/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Downloads" src="https://img.shields.io/github/downloads/Cirnouo/STranslate.Plugin.Tts.FishAudio/total?style=flat-square" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" />
    <img alt="WPF" src="https://img.shields.io/badge/WPF-Plugin-blue?style=flat-square" />
  </p>

  <p>
    <a href="../README.md">简体中文</a> | <b>繁體中文</b> | <a href="README_EN.md">English</a> | <a href="README_JA.md">日本語</a> | <a href="README_KO.md">한국어</a>
  </p>
</div>

---

<div align="center">
  <img src="images/overview.png" alt="外掛總覽" width="700" />
</div>

## 1 功能概覽

- **高品質合成**: 支援 Fish Audio s2.1-pro-free / s2.1-pro / s2-pro / s1 合成模型，涵蓋 80+ 種語言
- **聲音選擇**: 可透過名稱搜尋聲音，也可直接輸入聲音 ID；支援試聽、選擇、清除和分頁瀏覽
- **合成控制**: 支援語速、音量、響度歸一化、MP3 比特率、表現力、多樣性、延遲模式、文字規範化和上下文關聯
- **情緒標記**: 可在文字中加入 Fish Audio 情緒標記，例如 S2-Pro 的 `[laugh]` 或 S1 的 `(happy)`
- **聲音社群**: 可瀏覽 Fish Audio 創意社群中的公開聲音，並結合外掛內搜尋、試聽和 ID 設定快速套用喜歡的音色
- **本地化**: 簡體中文、繁體中文、English、日本語、한국어

## 2 快速開始

### 2.1 安裝

建議優先使用 STranslate 外掛市場安裝

**STranslate 外掛市場**

1. 開啟 STranslate
2. 進入 **設定 -> 外掛 -> 市場**
3. 搜尋或找到 **Fish Audio TTS**，點擊下載安裝

**手動安裝**

1. 前往 [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) 頁面
2. 下載最新版本的 `STranslate.Plugin.Tts.FishAudio.spkg`
3. 在 STranslate 中進入 **設定 -> 外掛 -> 安裝**
4. 選擇下載的 `.spkg` 檔案

### 2.2 取得並設定 API Key

> [!TIP]
> 使用 `.edu` 信箱註冊 Fish Audio 並完成學生認證，可免費領取 5 美元餘額。入口: [Fish Audio 學生免費積分](https://fish.audio/students/)。

使用電子郵件註冊並登入 Fish Audio，進入 [開發者 > API 金鑰](https://fish.audio/app/api-keys) 頁面，點擊 **建立新的金鑰**，並複製此金鑰

<!-- 截圖: images/fish-audio-api-keys.png
     內容: Fish Audio API Keys 頁面，突出建立/複製 API Key 的位置；請遮擋真實 API Key。 -->
<div>
  <img src="images/fish-audio-api-keys.png" alt="Fish Audio API Keys 頁面" width="700" />
</div>

在外掛設定頁的 **API Key** 輸入框中貼上

<div>
  <img src="images/plugin-api-key-input.png" alt="STranslate Fish Audio Plugin API Key 輸入框" width="360" />
</div>

### 2.3 購買 API 餘額

> [!TIP]
> S2.1 Pro 模型限時免費至 `2026-07-24`，在外掛內選用模型 `s2.1-pro-free` 進行呼叫。詳情入口：[Fish Audio S2.1 Pro：面向開發者的免費文字轉語音 API](https://fish.audio/blog/s2-1-pro-free-api/)

Fish Audio TTS 會消耗 Fish Audio API 餘額。可以在 [開發者 > 控制台](https://fish.audio/app/developers/billing/) 頁面中購買

<!-- 截圖: images/fish-audio-billing.png
     內容: Fish Audio Billing/Balance/Purchase Credits 入口，突出餘額購買或儲值位置。 -->
<div>
  <img src="images/fish-audio-billing.png" alt="Fish Audio API 餘額購買入口" width="700" />
</div>

### 2.4 取得聲音

聲音決定最終朗讀時使用的音色。

Fish Audio 的聲音社群收錄了創作者公開分享的聲音。你可以在官網按名稱、角色或語言瀏覽並試聽，確認風格後再回到外掛中搜尋同名聲音，或複製聲音 ID 直接套用。入口：[創意 > 發現](https://fish.audio/app/discovery)

<div>
  <img src="images/fish-audio-voice-community.png" alt="Fish Audio 聲音社群" width="700" />
</div>

### 2.5 設定聲音

外掛提供 **透過名稱搜尋聲音** 和 **透過 ID 直接設定** 兩種設定方式

#### 2.5.1 透過名稱搜尋聲音

在外掛的 **搜尋** 標籤頁輸入聲音名稱，點擊搜尋圖示或按 `Enter` 查詢；結果列表支援試聽和分頁，確認後點擊 **選擇** 即可套用。

<div>
  <img src="images/plugin-voice-search.png" alt="STranslate Fish Audio Plugin 聲音搜尋" width="360" />
</div>

#### 2.5.2 透過 ID 直接設定

在聲音社群中開啟目標聲音詳情頁，點擊頁面操作區的更多選單（`...`），選擇 `複製模型 ID`。Fish Audio 目前選單項仍使用「模型 ID」命名；複製到的就是外掛所需的聲音 ID。

<!-- 截圖: images/fish-audio-voice-id.png
     內容: Fish Audio 聲音詳情頁，突出網址列或頁面中聲音 ID 的位置；請不要暴露個人帳戶資訊。 -->
<div>
  <img src="images/fish-audio-voice-id.png" alt="從 Fish Audio 聲音詳情頁取得聲音 ID" width="550" />
</div>

回到外掛的 **透過 ID** 標籤頁，貼上 ID 並點擊確認按鈕；外掛會載入聲音資訊並套用該聲音。

<div>
  <img src="images/plugin-voice-id.png" alt="STranslate Fish Audio Plugin" width="360" />
</div>

## 3 設定說明

<details>
<summary><b>參數一覽</b>（點擊展開）</summary>

| 參數 | 預設值 | 說明 |
| :-- | :--: | :-- |
| API Key | - | Fish Audio API 金鑰，必填。 |
| 聲音 ID | 隨機聲音 | 可透過搜尋選擇或手動輸入。為空時使用隨機聲音。 |
| 合成模型 | 免費期內 `s2.1-pro-free`，到期後 `s2.1-pro` | 免費期內可選 `s2.1-pro-free`、`s2.1-pro`、`s2-pro`、`s1`；2026-07-24 UTC 起隱藏免費模型。 |
| MP3 比特率 | `192 kbps` | 可選 `64`、`128`、`192`。 |
| 語速 | `1.0` | 範圍 `0.5` 到 `2.0`。 |
| 音量 | `0 dB` | 範圍 `-10 dB` 到 `+10 dB`。 |
| 響度歸一化 | 開啟 | `s2.1-pro-free`、`s2.1-pro`、`s2-pro` 可用；`s1` 時顯示為停用。 |
| 表現力 | `0.7` | 範圍 `0` 到 `1`。 |
| 多樣性 | `0.7` | 範圍 `0` 到 `1`。 |
| 延遲模式 | 品質優先 | 品質優先 / 平衡 / 低延遲。 |
| 文字規範化 | 關閉 | 將數字、單位符號等轉換為更適合朗讀的文字。 |
| 上下文關聯 | 開啟 | 使用同一次合成音訊的前序片段保持聲音一致性，不會使用先前產生的其他音訊。 |

</details>

## 4 進階用法

### 4.1 情感控制

Fish Audio 透過文字內嵌標記控制情緒，不需要額外 API 參數；把標記直接寫進待朗讀文字即可。常見用途包括改變句子的情緒、語氣強弱，或插入笑聲、嘆氣、停頓等擬聲效果。

**S2 系列模型**（推薦）使用方括號和自然語言描述。句子級情緒通常放在句首，語氣或音效標記可以放在需要生效的位置：

```text
[angry] 這不可接受！
我不敢相信 [gasp] 你真的做到了 [laugh]
[whisper] 這是一個秘密
```

**S1** 使用圓括號和固定標籤集，通常放在句首：

```text
(happy) 今天天氣真好！
(sad)(whispering) 我很想你
```

建議每句話只使用一個主要情緒，避免在很短的文字裡堆疊過多標記。完整標籤列表、組合方式和排查建議請參考官方文件：[Emotion Control](https://docs.fish.audio/developer-guide/core-features/emotions)。

### 4.2 細粒度控制

細粒度控制用於指定某個詞、字或短語的準確發音，適合人名、品牌名、多音字、專業術語或日語音高需要精確控制的場景。用 `<|phoneme_start|>` 和 `<|phoneme_end|>` 包裹目標發音，其他標點和普通文字保持在標籤外。

英文使用 CMU Arpabet，通常替換一個單詞：

```text
I am an <|phoneme_start|>EH1 N JH AH0 N IH1 R<|phoneme_end|>.
```

中文使用帶聲調數字的拼音，一個音節一個標籤：

```text
我是一個<|phoneme_start|>gong1<|phoneme_end|><|phoneme_start|>cheng2<|phoneme_end|><|phoneme_start|>shi1<|phoneme_end|>。
```

日文使用 OpenJTalk 風格的羅馬字音素和音高數字，通常包裹較短詞組：

```text
<|phoneme_start|>ha0shi1ga0<|phoneme_end|>見えます。
```

如需系統學習格式、符號表和生成工具，請參考官方文件：[Fine-grained Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control)、[English Phoneme Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/english)、[Chinese Phoneme Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/chinese)、[Japanese Phoneme Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/japanese)。

## 常見問題

**Q: 不設定聲音會扣費嗎？**

A: 會。未設定聲音時 Fish Audio 會使用隨機聲音生成音訊，仍然會消耗 API 餘額

**Q: 試聽會扣費嗎？**

A: 不會。外掛試聽播放的是聲音自帶的公開音訊，不會呼叫 TTS 介面；因此未設定 API Key 時也能試聽

**Q: 為什麼播放後餘額沒有馬上變化？**

A: Fish Audio 的餘額扣除可能存在延遲。播放完成後立即重新整理餘額時，可能會短暫看到舊餘額

**Q: 聲音搜尋必須先設定 API Key 嗎？**

A: 聲音搜尋、透過 ID 查詢和試聽都可以在未設定 API Key 時使用；但真正合成語音需要格式正確、可用且有餘額的 API Key

**Q: 清理快取會影響已選擇的聲音嗎？**

A: 不會。清理快取只會刪除聲音封面圖片快取；聲音 ID 和已選擇聲音資訊仍會保留。後續再次展示時會重新載入封面

## 建置

```powershell
# 標準建置（Debug + .spkg 打包）
.\build.ps1

# 清理後建置
.\build.ps1 -Clean

# 清理後建置並執行回歸測試
.\build.ps1 -Clean -Test

# Release 建置
.\build.ps1 -Configuration Release
```

建置產物輸出到倉庫根目錄 `STranslate.Plugin.Tts.FishAudio.spkg`

<details>
<summary><b>環境需求</b></summary>

- .NET 10.0 SDK
- Windows（WPF 專案）

</details>

## 致謝

- [STranslate](https://github.com/ZGGSONG/STranslate) - 即用即走的翻譯和 OCR 工具
- [Fish Audio](https://fish.audio) - 語音合成 API 提供商
- [iNKORE WPF Modern UI](https://github.com/iNKORE-NET/UI.WPF.Modern) - WPF 現代 UI 控制項庫

## 授權

[MIT](../LICENSE)
