<div align="center">
  <a href="https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio"><img src="assets/icon.svg" alt="Fish Audio TTS" width="80" height="80" /></a>
  <h1>Fish Audio TTS</h1>
  <p>為 <a href="https://github.com/ZGGSONG/STranslate">STranslate</a> 提供 <a href="https://fish.audio">Fish Audio</a> 語音合成。</p>
  <p>
    <img alt="License" src="https://img.shields.io/github/license/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Release" src="https://img.shields.io/github/v/release/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Downloads" src="https://img.shields.io/github/downloads/Cirnouo/STranslate.Plugin.Tts.FishAudio/total?style=flat-square" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" />
    <img alt="WPF" src="https://img.shields.io/badge/WPF-Plugin-blue?style=flat-square" />
  </p>
  <p><a href="../README.md">简体中文</a> · <b>繁體中文</b> · <a href="README_EN.md">English</a> · <a href="README_JA.md">日本語</a> · <a href="README_KO.md">한국어</a></p>
</div>

> [!WARNING]
> 中國大陸網路無法存取 `fish.audio` 服務。請參閱使用指南中的[網路與代理說明](user-guide/README_TW.md#network-and-proxy)。

> [!TIP]
> 使用 `.edu` 電子郵件註冊 Fish Audio 並完成學生認證，可免費領取 **5 美元餘額**。[申請入口](https://fish.audio/students/)

> [!TIP]
> S2.1 Pro 模型限時免費至 **2026-11-30 UTC**，在外掛中選用模型 `s2.1-pro-free` 進行呼叫。[活動說明](https://fish.audio/blog/s2-1-pro-free-api/)

> [!IMPORTANT]
> S1 將於 **2026-12-31** 退役。此後，使用模型 `s1` 的 API 請求將由 S2.1 Pro 以相同價格提供服務。請在此之前切換至 `s2.1-pro`。[官方說明](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live)

福利資格與免費活動條件以 Fish Audio 官方頁面為準。

## 功能概覽

- **聲音探索**：依名稱搜尋、分頁瀏覽及試聽聲音，可透過聲音 ID 查詢後套用，並以星號收藏常用聲音。
- **串流朗讀**：分段傳送文字，持續接收與播放音訊，長文無須等待整段合成完成。
- **音訊格式**：預設使用 PCM；網路較慢時可選擇 MP3 並設定位元率。
- **合成控制**：可調整模型、語速、音量、響度歸一化、表現力、多樣性及延遲模式。
- **帳戶與快取**：查看與重新整理 API 餘額，管理所有聲音入口共用的封面圖片快取。
- **介面語言**：简体中文、繁體中文、English、日本語、한국어、Русский、Türkçe。

## 安裝

### 外掛市集（建議）

1. 開啟 STranslate，進入 **設定 → 外掛 → 市集**。
2. 搜尋 **Fish Audio TTS**，下載並安裝。

### 手動安裝

1. 從 [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) 下載 `STranslate.Plugin.Tts.FishAudio.spkg`。
2. 在 STranslate 的 **設定 → 外掛 → 安裝** 匯入安裝包。

## 開始使用

1. 在外掛設定中填入 [Fish Audio API Key](https://fish.audio/app/api-keys)。
2. 搜尋並試聽聲音，按勾號執行 **套用**；也可透過聲音 ID 的放大鏡查詢後套用，或從 **收藏夾** 中選用。已套用的叉號可取消選擇。
3. 選擇合成模型與音訊格式，依需要調整參數，再於 STranslate 朗讀文字。

## 使用指南

[閱讀完整使用指南 →](user-guide/README_TW.md)

詳細指南涵蓋安裝、首次設定、聲音選擇、全部設定項目、PCM / MP3、網路與代理及常見問題排解。

[設定說明](user-guide/README_TW.md#configuration) · [音訊格式](user-guide/README_TW.md#audio-formats) · [網路與代理](user-guide/README_TW.md#network-and-proxy) · [常見問題](user-guide/README_TW.md#troubleshooting)

## 常見問題

**Q: 不設定聲音會扣費嗎？**

A: 是否扣費取決於選用的合成模型。未設定聲音時 Fish Audio 會使用隨機聲音產生音訊；付費模型仍會消耗 API 餘額，免費模型以官方活動條件為準。

**Q: 試聽會扣費嗎？**

A: 不會。外掛試聽播放聲音附帶的公開音訊，不會呼叫 TTS 介面；因此未設定 API Key 也能試聽。

**Q: 為何播放後餘額沒有立刻變化？**

A: Fish Audio 的扣款可能延遲。播放完成後立刻重新整理，可能暫時顯示舊餘額。

**Q: 搜尋聲音前必須設定 API Key 嗎？**

A: 搜尋、透過 ID 查詢與試聽皆可在未設定 API Key 時使用；實際合成需要有效的 API Key，使用付費模型也需要足夠 API 餘額。

**Q: 清理快取會影響已選聲音嗎？**

A: 不會。清理僅刪除聲音封面圖片快取；聲音 ID 與已選聲音資訊仍會保留，之後顯示時會重新載入封面。

## Fish Audio 官方資源

帳戶、聲音社群、模型能力、參數用法與計費規則，請參閱 Fish Audio 官方頁面。

[官網](https://fish.audio) · [API Key](https://fish.audio/app/api-keys) · [聲音社群](https://fish.audio/app/discovery) · [官方文件](https://docs.fish.audio) · [API 計費](https://docs.fish.audio/developer-guide/models-pricing/pricing-and-rate-limits)

## 開發

請先閱讀[開發與貢獻指南](../CONTRIBUTING.md)，了解環境準備、必讀文件、建置測試與提交要求。

快速驗證：在 Windows 上使用 .NET 10 SDK 執行：

```powershell
.\build.ps1 -Release -Clean -Test
```

安裝包輸出至儲存庫根目錄 `STranslate.Plugin.Tts.FishAudio.spkg`。

## 回饋與貢獻

[提交問題與建議](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/issues) · [更新紀錄](../CHANGELOG.md) · [參與貢獻](../CONTRIBUTING.md)

## 授權條款

[MIT License](../LICENSE)
