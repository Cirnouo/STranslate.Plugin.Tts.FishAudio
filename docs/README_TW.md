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

- **高品質合成**: 支援 Fish Audio S2-Pro / S1 合成模型，覆蓋 80+ 種語言。
- **聲音選擇**: 可透過名稱搜尋聲音，也可直接輸入聲音 ID；支援試聽、選擇、清除和分頁瀏覽。
- **封面快取**: 自動快取聲音 `cover_image`，減少重複載入；可在設定頁查看占用並清理。
- **帳戶確認**: 驗證 API Key 後顯示餘額和「已驗證並套用」狀態，方便確認設定是否生效。
- **合成控制**: 支援語速、音量、響度歸一化、MP3 比特率、表現力、多樣性、延遲模式、文字規範化和上下文關聯。
- **情緒標記**: 可在文字中加入 Fish Audio 情緒標記，例如 S2-Pro 的 `[laugh]` 或 S1 的 `(happy)`。
- **多語言 UI**: 簡體中文、繁體中文、English、日本語、한국어。

## 快速開始

### 1. 安裝外掛

建議優先使用 STranslate 外掛市場安裝；如果外掛市場暫時無法存取，也可以從 GitHub Release 手動安裝 `.spkg`。

**方式一: STranslate 外掛市場**

1. 開啟 STranslate。
2. 進入 **設定 -> 外掛 -> 市場**。
3. 搜尋或找到 **Fish Audio TTS**，點擊下載安裝。
4. 安裝後建議重新啟動 STranslate。

**方式二: 手動安裝 `.spkg`**

1. 前往 [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) 頁面。
2. 下載最新版本的 `STranslate.Plugin.Tts.FishAudio.spkg`。
3. 在 STranslate 中進入 **設定 -> 外掛 -> 安裝外掛**。
4. 選擇下載的 `.spkg` 檔案，重新啟動 STranslate。

> [!TIP]
> `.spkg` 本質是 ZIP 檔案，STranslate 會自動解壓並載入，不需要手動解壓。

### 2. 取得 API Key

1. 登入 [Fish Audio API Keys](https://fish.audio/app/api-keys)。
2. 建立或複製一個 API Key。

<!-- 截圖: images/fish-audio-api-keys.png
     內容: Fish Audio API Keys 頁面，突出建立/複製 API Key 的位置；請遮擋真實 API Key。 -->
<div>
  <img src="images/fish-audio-api-keys.png" alt="Fish Audio API Keys 頁面" width="700" />
</div>

3. 在外掛設定頁的 **API Key** 輸入框中貼上。
4. 點擊確認按鈕，或在輸入框聚焦時按 `Enter`。
5. 看到 **已驗證並套用**，且帳戶資訊顯示餘額後，表示目前 API Key 已被外掛使用。

<!-- 截圖: images/settings-account-api.png
     內容: 外掛設定頁中的帳戶資訊、API Key 輸入框、確認按鈕、已驗證並套用狀態和餘額。 -->
<div>
  <img src="images/settings-account-api.png" alt="外掛帳戶和 API Key 設定" width="360" />
</div>

### 3. 購買 API 餘額

Fish Audio TTS 會消耗 Fish Audio API 餘額。可以在 [Console -> Developer -> Billing -> Balance -> Purchase Credits](https://fish.audio/app/developers/billing/) 購買或儲值。

<!-- 截圖: images/fish-audio-billing.png
     內容: Fish Audio Billing/Balance/Purchase Credits 入口，突出餘額購買或儲值位置。 -->
<div>
  <img src="images/fish-audio-billing.png" alt="Fish Audio API 餘額購買入口" width="700" />
</div>

> [!NOTE]
> 餘額扣除可能存在延遲。播放完成後立即重新整理餘額時，可能短暫顯示舊餘額。

> [!TIP]
> 使用 `.edu` 信箱註冊並完成學生認證，可獲得 Fish Audio 提供的學生額度。入口: [Fish Audio Students](https://fish.audio/students/)。

### 4. 設定聲音

聲音決定最終朗讀時使用的音色。外掛提供 **搜尋** 和 **透過 ID** 兩種設定方式。

> [!NOTE]
> 不設定聲音也可以使用外掛。此時外掛不會向 Fish Audio 傳送 `reference_id`，Fish Audio 會使用隨機聲音；隨機聲音同樣會消耗 API 餘額。

**方法一: 透過名稱搜尋**

1. 切換到聲音區域的 **搜尋** 標籤頁。
2. 在輸入框中輸入聲音名稱。
3. 點擊搜尋圖示，或在輸入框聚焦時按 `Enter`。
4. 在結果中試聽聲音，確認後點擊 **選擇**。
5. 如果結果很多，可以使用分頁控制項切換頁面。

<!-- 截圖: images/settings-voice-search.png
     內容: 外掛聲音搜尋介面，包含搜尋輸入框、搜尋按鈕、搜尋結果、試聽按鈕、選擇按鈕和分頁。 -->
<div>
  <img src="images/settings-voice-search.png" alt="透過名稱搜尋並選擇聲音" width="450" />
</div>

**方法二: 透過聲音 ID**

1. 在 Fish Audio 官網開啟目標聲音的詳情頁。
2. 從展開選單中複製聲音 ID。

<!-- 截圖: images/fish-audio-voice-id.png
     內容: Fish Audio 聲音詳情頁，突出聲音 ID 的位置；請不要暴露個人帳戶資訊。 -->
<div>
  <img src="images/fish-audio-voice-id.png" alt="從 Fish Audio 聲音詳情頁取得聲音 ID" width="650" />
</div>

3. 回到外掛設定頁，切換到 **透過 ID** 標籤頁。
4. 貼上聲音 ID，點擊確認按鈕，或在輸入框聚焦時按 `Enter`。
5. 外掛會驗證並載入這個聲音的資訊。



<!-- 截圖: images/settings-voice-by-id.png
     內容: 外掛透過 ID 設定聲音的介面，包含聲音 ID 輸入框、貼上按鈕和確認按鈕。 -->
<div>
  <img src="images/settings-voice-by-id.png" alt="透過聲音 ID 設定聲音" width="450" />
</div>

## 設定說明

外掛設定頁按使用流程分為帳戶、聲音、模型、音訊輸出、韻律、生成參數和其他幾個區域。

### 帳戶與 API

<!-- 截圖: images/settings-account-api.png
     內容: 外掛帳戶資訊、API 位址、API Key 輸入框、確認狀態、餘額和重新整理按鈕。 -->
<div>
  <img src="images/settings-account-api.png" alt="帳戶與 API 設定" width="450" />
</div>

| 設定項 | 說明 |
| :-- | :-- |
| 帳戶資訊 | 顯示目前 API Key 對應帳戶的餘額，單位為美元。點擊 **重新整理** 可手動更新餘額。 |
| API 位址 | Fish Audio API 位址，目前固定為 `https://api.fish.audio`。 |
| API Key | Fish Audio API 金鑰。只有點擊確認按鈕或按 `Enter` 後才會驗證並套用。 |
| 驗證狀態 | 等待伺服器返回時顯示 **等待回應中**；成功後顯示 **已驗證並套用**；格式錯誤會在輸入框旁提示。 |

### 聲音

透過名稱搜尋聲音

<!-- 截圖: images/settings-voice-search.png
     內容: 已選聲音展示區 + 搜尋標籤頁，展示封面、標題、作者、熱度、試聽、選擇和清除動作。 -->
<div>
  <img src="images/settings-voice-search.png" alt="聲音搜尋設定" width="450" />
</div>

透過 ID 取得聲音

<!-- 截圖: images/settings-voice-by-id.png
     內容: 透過 ID 標籤頁的一行輸入框和控制項，展示與搜尋標籤頁一致的初始高度。 -->
<div>
  <img src="images/settings-voice-by-id.png" alt="透過 ID 設定聲音" width="450" />
</div>

| 設定項 | 說明 |
| :-- | :-- |
| 已選聲音 | 顯示目前套用的聲音資訊。沒有設定時顯示 **隨機聲音**。 |
| 搜尋 | 透過聲音名稱搜尋 Fish Audio 聲音，可試聽後選擇。搜尋結果中的封面圖會自動快取。 |
| 透過 ID | 適合已經知道聲音 ID 的情境。聲音 ID 必須是 32 位小寫十六進位字元。 |
| 清除聲音 | 清除目前聲音設定，恢復為隨機聲音。 |

### 模型與音訊輸出

<!-- 截圖: images/settings-model-audio.png
     內容: 合成模型和 MP3 比特率兩個設定卡片。 -->
<div>
  <img src="images/settings-model-audio.png" alt="模型與音訊輸出設定" width="450" />
</div>

| 設定項 | 預設值 | 說明 |
| :-- | :--: | :-- |
| 合成模型 | `s2-pro` | 選擇 Fish Audio 合成引擎。`s2-pro` 為推薦選項，支援 80+ 種語言和更豐富的情緒標記；`s1` 可用於需要舊模型行為的情境。 |
| MP3 比特率 | `192 kbps` | 控制輸出音訊品質和體積，可選 `64`、`128`、`192`。比特率越高，音質通常越好，檔案也更大。 |

### 韻律

<!-- 截圖: images/settings-prosody.png
     內容: 語速、音量、響度歸一化三個設定項。 -->
<div>
  <img src="images/settings-prosody.png" alt="韻律設定" width="450" />
</div>

| 設定項 | 預設值 | 說明 |
| :-- | :--: | :-- |
| 語速 | `1.0` | 控制朗讀速度，範圍 `0.5` 到 `2.0`。小於 `1.0` 更慢，大於 `1.0` 更快。 |
| 音量 | `0 dB` | 控制音量偏移，範圍 `-10 dB` 到 `+10 dB`，支援 `0.1 dB` 精度。 |
| 響度歸一化 | 開啟 | 僅在 `s2-pro` 模型下顯示，用於讓輸出響度更穩定。 |

### 生成參數

<!-- 截圖: images/settings-generation.png
     內容: 表現力、多樣性、延遲模式、文字規範化、上下文關聯設定項。 -->
<div>
  <img src="images/settings-generation.png" alt="生成參數設定" width="450" />
</div>

| 設定項 | 預設值 | 說明 |
| :-- | :--: | :-- |
| 表現力 | `0.7` | 對應生成取樣溫度。數值越高，表達變化越明顯；數值越低，輸出更穩定。 |
| 多樣性 | `0.7` | 對應 `top_p`。數值越高，取樣範圍越寬；數值越低，結果更收斂。 |
| 延遲模式 | 品質優先 | 在品質和回應速度之間取捨：品質優先、平衡、低延遲。 |
| 文字規範化 | 關閉 | 自動將數字、單位符號等內容轉換為更適合朗讀的文字。 |
| 上下文關聯 | 開啟 | 使用前序音訊作為上下文，幫助長文字保持聲音一致性。 |

### 其他

<!-- 截圖: images/settings-misc-cache.png
     內容: 其他區域中的快取占用、清理快取按鈕和處理中等待提示。 -->
<div>
  <img src="images/settings-misc-cache.png" alt="快取設定" width="450" />
</div>

| 設定項 | 說明 |
| :-- | :-- |
| 快取占用 | 掃描外掛快取目錄下 `cover_images/*.jpg` 的真實檔案大小，並自動使用 B、KB、MB、GB 等單位顯示。 |
| 清理快取 | 刪除聲音封面快取。清理過程中按鈕會停用並顯示旋轉等待提示，完成或逾時後恢復可點擊。 |

<details>
<summary><b>參數一覽</b>（點擊展開）</summary>

| 參數 | 預設值 | 說明 |
| :-- | :--: | :-- |
| API Key | - | Fish Audio API 金鑰，必填。 |
| 聲音 ID | -（隨機聲音） | 可透過搜尋選擇或手動輸入。為空時使用隨機聲音。 |
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

Fish Audio 透過文字內聯標記控制情緒，無需額外 API 參數。

**S2-Pro**（推薦）使用方括號和自然語言描述，可放在文字任意位置：

```text
[angry] 這不可接受！
我不敢相信 [gasp] 你真的做到了 [laugh]
[whisper] 這是一個祕密
```

**S1** 使用圓括號和固定標籤集，通常放在句首：

```text
(happy) 今天天氣真好！
(sad)(whispering) 我很想你。
```

## 常見問題

**不設定聲音會扣費嗎？**

會。未設定聲音時 Fish Audio 會使用隨機聲音生成音訊，仍然會消耗 API 餘額。

**試聽會扣費嗎？**

不會。外掛試聽播放的是 Fish Audio 聲音條目自帶的公開樣本音訊，不會呼叫 TTS 合成介面；因此未驗證 API Key 時也能進行試聽。只有實際讓 STranslate 使用外掛合成朗讀文字時，才需要有效 API Key 並消耗餘額。

**為什麼播放後餘額沒有馬上變化？**

Fish Audio 的餘額扣除可能存在延遲。播放完成後立即重新整理餘額時，可能會短暫看到舊餘額。

**聲音搜尋必須先設定 API Key 嗎？**

聲音搜尋、透過 ID 查詢和試聽都可以在未驗證 API Key 時使用；但真正合成語音需要有效 API Key 和可用餘額。

**清理快取會影響已選擇的聲音嗎？**

不會。清理快取只刪除聲音封面圖片快取；聲音 ID 和已選聲音資訊仍保留。後續再次展示時會重新載入封面。

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

- [STranslate](https://github.com/ZGGSONG/STranslate) — 即用即走的翻譯和 OCR 工具
- [Fish Audio](https://fish.audio) — 語音合成 API 提供商
- [iNKORE WPF Modern UI](https://github.com/iNKORE-NET/UI.WPF.Modern) — WPF 現代 UI 控制項庫

## 授權條款

[MIT](../LICENSE)
