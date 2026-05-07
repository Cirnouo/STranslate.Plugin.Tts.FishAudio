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

## 功能概覽

- **高品質合成**: 支援 Fish Audio S2-Pro / S1 合成模型，涵蓋 80+ 種語言。
- **聲音選擇**: 可透過名稱搜尋聲音，也可直接輸入聲音 ID；支援試聽、選擇、清除和分頁瀏覽。
- **合成控制**: 支援語速、音量、響度歸一化、MP3 比特率、表現力、多樣性、延遲模式、文字規範化和上下文關聯。
- **情緒標記**: 可在文字中加入 Fish Audio 情緒標記，例如 S2-Pro 的 `[laugh]` 或 S1 的 `(happy)`。
- **多語言 UI**: 簡體中文、繁體中文、English、日本語、한국어。

## 快速開始

### 安裝

建議優先使用 STranslate 外掛市場安裝。

**STranslate 外掛市場**

1. 開啟 STranslate。
2. 進入 **設定 -> 外掛 -> 市場**。
3. 搜尋或找到 **Fish Audio TTS**，點擊下載安裝。

**手動安裝**

1. 前往 [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) 頁面。
2. 下載最新版本的 `STranslate.Plugin.Tts.FishAudio.spkg`。
3. 在 STranslate 中進入 **設定 -> 外掛 -> 安裝**。
4. 選擇下載的 `.spkg` 檔案。

### 設定 API Key

1. 進入並登入 [Fish Audio API Keys](https://fish.audio/app/api-keys) 頁面。
2. 點擊 **建立新的金鑰**。
3. 在外掛設定頁的 **API Key** 輸入框中貼上。
4. 點擊確認按鈕或按 `Enter` 套用。
5. 看到 **已驗證並套用**，並且帳戶資訊顯示餘額後，表示目前 API Key 已被外掛使用。

<!-- 截圖: images/fish-audio-api-keys.png
     內容: Fish Audio API Keys 頁面，突出建立/複製 API Key 的位置；請遮擋真實 API Key。 -->
<div>
  <img src="images/fish-audio-api-keys.png" alt="Fish Audio API Keys 頁面" width="700" />
</div>

### 購買 API 餘額

Fish Audio TTS 會消耗 Fish Audio API 餘額。可以在 [控制台 > 開發者 > 帳單 > 餘額 > 購買積分](https://fish.audio/app/developers/billing/) 購買或儲值。

<!-- 截圖: images/fish-audio-billing.png
     內容: Fish Audio Billing/Balance/Purchase Credits 入口，突出餘額購買或儲值位置。 -->
<div>
  <img src="images/fish-audio-billing.png" alt="Fish Audio API 餘額購買入口" width="700" />
</div>

> [!TIP]
> 使用 `.edu` 信箱註冊並完成學生認證，可免費領取 5 美元餘額。入口: [Fish Audio Students](https://fish.audio/students/)。

### 設定聲音

聲音決定最終朗讀時使用的音色。外掛提供 **搜尋** 和 **透過 ID** 兩種設定方式。

> [!NOTE]
> 不設定聲音也可以使用外掛。此時外掛不會向 Fish Audio 傳送 `reference_id`，Fish Audio 會使用隨機聲音；隨機聲音同樣會消耗 API 餘額。

**透過名稱搜尋**

1. 切換到聲音區域的 **搜尋** 標籤頁。
2. 在輸入框中輸入聲音名稱。
3. 點擊搜尋圖示或按 `Enter` 進行搜尋。
4. 在結果中試聽聲音，確認後點擊 **選擇**。
5. 如果結果很多，可以使用分頁控制項切換頁面。

**透過聲音 ID**

1. 在 Fish Audio 官網開啟目標聲音的詳情頁。
2. 從展開選單中複製聲音 ID。
3. 回到外掛設定頁，切換到 **透過 ID** 標籤頁。
4. 貼上聲音 ID，點擊確認按鈕或按 `Enter` 套用。
5. 外掛會驗證並載入這個聲音的資訊。

<!-- 截圖: images/fish-audio-voice-id.png
     內容: Fish Audio 聲音詳情頁，突出網址列或頁面中聲音 ID 的位置；請不要暴露個人帳戶資訊。 -->
<div>
  <img src="images/fish-audio-voice-id.png" alt="從 Fish Audio 聲音詳情頁取得聲音 ID" width="550" />
</div>

## 設定說明

<details>
<summary><b>參數一覽</b>（點擊展開）</summary>

| 參數 | 預設值 | 說明 |
| :-- | :--: | :-- |
| API Key | - | Fish Audio API 金鑰，必填。 |
| 聲音 ID | 隨機聲音 | 可透過搜尋選擇或手動輸入。為空時使用隨機聲音。 |
| 合成模型 | `s2-pro` | `s2-pro` 或 `s1`。 |
| MP3 比特率 | `192 kbps` | 可選 `64`、`128`、`192`。 |
| 語速 | `1.0` | 範圍 `0.5` 到 `2.0`。 |
| 音量 | `0 dB` | 範圍 `-10 dB` 到 `+10 dB`。 |
| 響度歸一化 | 開啟 | 僅 `s2-pro` 模型時顯示。 |
| 表現力 | `0.7` | 範圍 `0` 到 `1`。 |
| 多樣性 | `0.7` | 範圍 `0` 到 `1`。 |
| 延遲模式 | 品質優先 | 品質優先 / 平衡 / 低延遲。 |
| 文字規範化 | 關閉 | 將數字、單位符號等轉換為更適合朗讀的文字。 |
| 上下文關聯 | 開啟 | 使用前序音訊作為上下文以保持聲音一致性。 |

</details>

## 情緒標記

Fish Audio 透過文字內嵌標記控制情緒，不需要額外 API 參數。

**S2-Pro**（推薦）使用方括號和自然語言描述，可放在文字任意位置：

```text
[angry] 這不可接受！
我不敢相信 [gasp] 你真的做到了 [laugh]
[whisper] 這是一個秘密
```

**S1** 使用圓括號和固定標籤集，通常放在句首：

```text
(happy) 今天天氣真好！
(sad)(whispering) 我很想你。
```

## 常見問題

**Q: 不設定聲音會扣費嗎？**

A: 會。未設定聲音時 Fish Audio 會使用隨機聲音生成音訊，仍然會消耗 API 餘額。

**Q: 試聽會扣費嗎？**

A: 不會。外掛試聽播放的是聲音自帶的公開音訊，不會呼叫 TTS 介面；因此未驗證 API Key 時也能試聽。

**Q: 為什麼播放後餘額沒有馬上變化？**

A: Fish Audio 的餘額扣除可能存在延遲。播放完成後立即重新整理餘額時，可能會短暫看到舊餘額。

**Q: 聲音搜尋必須先設定 API Key 嗎？**

A: 聲音搜尋、透過 ID 查詢和試聽都可以在未驗證 API Key 時使用；但真正合成語音需要有效 API Key 和可用餘額。

**Q: 清理快取會影響已選擇的聲音嗎？**

A: 不會。清理快取只會刪除聲音封面圖片快取；聲音 ID 和已選擇聲音資訊仍會保留。後續再次展示時會重新載入封面。

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

建置產物輸出到倉庫根目錄 `STranslate.Plugin.Tts.FishAudio.spkg`。

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
