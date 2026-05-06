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

## 功能特性

- **高品質合成** — 基於 Fish Audio S2-Pro / S1 模型，支援 80+ 種語言
- **聲音搜尋** — 在設定面板中搜尋、瀏覽、試聽並選擇語音聲音，支援分頁
- **聲音試聽** — 帶進度環的試聽按鈕，支援播放中切換，狀態按聲音 ID 同步
- **手動輸入驗證** — 直接輸入聲音 ID 驗證並載入聲音資訊
- **情緒標記** — 透過文字內聯標記控制語音情緒（S2-Pro: `[laugh]`，S1: `(happy)`）
- **韻律控制** — 語速（0.5x ~ 2.0x）、音量（±10 dB，0.1 dB 精度）、響度歸一化
- **生成參數** — 表現力、多樣性、延遲模式（品質優先 / 平衡 / 低延遲優先）、文字規範化
- **帳戶資訊** — 即時顯示剩餘餘額、API 延遲指示
- **多語言 UI** — 簡體中文、繁體中文、English、日本語、한국어

## 安裝

1. 前往 [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) 頁面下載最新 `.spkg` 檔案
2. 在 STranslate 中進入 **設定 → 外掛 → 安裝外掛**
3. 選擇下載的 `.spkg` 檔案，重新啟動 STranslate

> [!TIP]
> `.spkg` 本質是 ZIP 檔案，STranslate 會自動解壓載入。

## 前置條件

- [STranslate](https://github.com/ZGGSONG/STranslate) 最新版本
- Fish Audio API Key（[取得位址](https://fish.audio/app/api-keys)）
- Fish Audio 帳戶餘額 > 0

> [!NOTE]
> API 餘額購買/儲值入口：[控制台 > 開發者 > 帳單 > 餘額 > 購買積分](https://fish.audio/app/developers/billing/)。餘額扣除可能存在延遲，播放完成後立即重新整理餘額可能短暫顯示舊值。

> [!TIP]
> 使用 `.edu` 信箱註冊並完成學生認證，可免費獲得 5 美元額度，入口：[Fish Audio Students](https://fish.audio/students/)。

## 設定說明

<details>
<summary><b>參數一覽</b>（點擊展開）</summary>

| 參數 | 預設值 | 說明 |
| :-- | :--: | :-- |
| API Key | — | Fish Audio API 金鑰（必填） |
| 聲音 ID | —（隨機聲音） | 語音聲音 ID，可透過搜尋選擇或手動輸入。為空時使用隨機聲音（系統預設音色） |
| 合成模型 | `s2-pro` | `s2-pro`（推薦）或 `s1` |
| 語速 | `1.0` | 0.5 ~ 2.0 |
| 音量 | `0 dB` | -10 ~ +10 dB，0.1 dB 精度 |
| 響度歸一化 | 開啟 | 僅 S2-Pro 模型時顯示 |
| 表現力 | `0.7` | 0 ~ 1，越高越多樣 |
| 多樣性 | `0.7` | 0 ~ 1 |
| 延遲模式 | 品質優先 | 品質優先 / 平衡 / 低延遲優先 |
| 文字規範化 | 關閉 | 數字→文字等自動轉換 |

</details>

## 情緒標記

Fish Audio 透過文字內聯標記控制情緒，無需額外 API 參數：

**S2-Pro**（推薦）— 方括號 + 自然語言描述，可放在文字任意位置：
```
[angry] 這不可接受！
我不敢相信 [gasp] 你真的做到了 [laugh]
[whisper] 這是一個祕密
```

**S1** — 圓括號 + 固定標籤集，放在句首：
```
(happy) 今天天氣真好！
(sad)(whispering) 我很想你。
```

## 建置

```powershell
# 標準建置（Debug + .spkg 打包）
.\build.ps1

# 清理後建置
.\build.ps1 -Clean

# 建置並執行回歸測試
.\build.ps1 -Test

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
