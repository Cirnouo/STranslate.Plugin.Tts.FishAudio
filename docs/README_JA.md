<div align="center">
  <a href="https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio">
    <img src="images/icon.svg" alt="Fish Audio TTS" width="128" height="128" />
  </a>

  <h1>Fish Audio TTS</h1>

  <p>
    <a href="https://fish.audio">Fish Audio</a> 音声合成プラグイン for <a href="https://github.com/ZGGSONG/STranslate">STranslate</a>
  </p>

  <p>
    <img alt="License" src="https://img.shields.io/github/license/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Release" src="https://img.shields.io/github/v/release/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Downloads" src="https://img.shields.io/github/downloads/Cirnouo/STranslate.Plugin.Tts.FishAudio/total?style=flat-square" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" />
    <img alt="WPF" src="https://img.shields.io/badge/WPF-Plugin-blue?style=flat-square" />
  </p>

  <p>
    <a href="../README.md">简体中文</a> | <a href="README_TW.md">繁體中文</a> | <a href="README_EN.md">English</a> | <b>日本語</b> | <a href="README_KO.md">한국어</a>
  </p>
</div>

---

<div align="center">
  <img src="images/overview.png" alt="プラグイン概要" width="700" />
</div>

## 機能

- **高品質合成** — Fish Audio S2-Pro / S1 モデル搭載、80 以上の言語に対応
- **ボイス検索** — 設定パネルでボイスを検索・閲覧・試聴・選択、ページネーション対応
- **ボイス試聴** — プログレスリング付き試聴ボタン、再生中の切り替え対応、ボイス ID で状態同期
- **カバーキャッシュ** — ボイスのカバー画像を STranslate のプラグインキャッシュに保存し、使用量は `cover_images` 内の実際の `.jpg` ファイルをスキャンして計算、設定から明示的な削除ボタンで削除可能
- **手動入力検証** — ボイス ID を直接入力すると検証しボイス情報を読み込み
- **感情マーカー** — テキスト内のインラインマーカーで音声の感情を制御（S2-Pro: `[laugh]`、S1: `(happy)`）
- **韻律制御** — 速度（0.5x〜2.0x）、音量（±10 dB、0.1 dB 精度）、ラウドネス正規化
- **生成パラメータ** — 表現力、多様性、レイテンシモード（品質優先 / バランス / 低レイテンシ）、テキスト正規化、コンテキスト連携
- **アカウント情報** — 残高とAPI レイテンシをリアルタイム表示
- **多言語 UI** — 簡体字中国語、繁体字中国語、English、日本語、한국어

## インストール

1. [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) ページから最新の `.spkg` ファイルをダウンロード
2. STranslate で **設定 → プラグイン → プラグインのインストール** を開く
3. ダウンロードした `.spkg` ファイルを選択し、STranslate を再起動

> [!TIP]
> `.spkg` は本質的に ZIP ファイルです。STranslate が自動的に展開して読み込みます。

## 前提条件

- [STranslate](https://github.com/ZGGSONG/STranslate) 最新バージョン
- Fish Audio API Key（[取得はこちら](https://fish.audio/app/api-keys)）
- Fish Audio アカウント残高 > 0

> [!NOTE]
> API 残高の購入・チャージ：[コンソール > 開発者 > 請求 > 残高 > クレジット購入](https://fish.audio/app/developers/billing/)。残高の差し引きに遅延が生じる場合があります。再生直後に残高を更新すると古い値が表示されることがあります。

> [!TIP]
> `.edu` メールアドレスで登録して学生認証を完了すると、5 米ドル分の無料クレジットを受け取れます。入口：[Fish Audio Students](https://fish.audio/students/)。

## 設定

<details>
<summary><b>パラメータ一覧</b>（クリックで展開）</summary>

| パラメータ | デフォルト | 説明 |
| :-- | :--: | :-- |
| API Key | — | Fish Audio API キー（必須） |
| ボイス ID | —（ランダムボイス） | ボイス ID、検索で選択または手動入力。空の場合はランダムボイス（システムデフォルト音声）を使用 |
| 合成モデル | `s2-pro` | `s2-pro`（推奨）または `s1` |
| 速度 | `1.0` | 0.5〜2.0 |
| 音量 | `0 dB` | -10〜+10 dB、0.1 dB 精度 |
| ラウドネス正規化 | オン | S2-Pro モデル使用時のみ表示 |
| MP3 ビットレート | `192 kbps` | 64 / 128 / 192 kbps |
| 表現力 | `0.7` | 0〜1、高いほど多様 |
| 多様性 | `0.7` | 0〜1 |
| レイテンシモード | 品質優先 | 品質優先 / バランス / 低レイテンシ |
| テキスト正規化 | オフ | 数字からテキストへの自動変換など |
| コンテキスト連携 | オン | 前の音声をコンテキストとして使い、声の一貫性を保つ |
| カバーキャッシュ | 自動 | `cover_image` を `<ボイス ID>.jpg` としてプラグインキャッシュに保存。使用量は `cover_images/*.jpg` をスキャンして計算し、「その他」の「キャッシュをクリア」ボタンで削除可能 |

</details>

## 感情マーカー

Fish Audio はテキスト内のインラインマーカーで感情を制御します。追加の API パラメータは不要です：

**S2-Pro**（推奨）— 角括弧 + 自然言語の説明、テキストの任意の位置に配置可能：
```
[angry] これは許せない！
信じられない [gasp] 本当にやったんだ [laugh]
[whisper] これは秘密だよ
```

**S1** — 丸括弧 + 固定タグセット、文頭に配置：
```
(happy) 今日はいい天気ですね！
(sad)(whispering) あなたがとても恋しいです。
```

## ビルド

```powershell
# 標準ビルド（Debug + .spkg パッケージング）
.\build.ps1

# クリーンビルド
.\build.ps1 -Clean

# クリーンビルドして回帰テストを実行
.\build.ps1 -Clean -Test

# Release ビルド
.\build.ps1 -Configuration Release
```

ビルド成果物はリポジトリルートに `STranslate.Plugin.Tts.FishAudio.spkg` として出力されます。

<details>
<summary><b>環境要件</b></summary>

- .NET 10.0 SDK
- Windows（WPF プロジェクト）

</details>

## 謝辞

- [STranslate](https://github.com/ZGGSONG/STranslate) — すぐに使える翻訳・OCR ツール
- [Fish Audio](https://fish.audio) — 音声合成 API プロバイダー
- [iNKORE WPF Modern UI](https://github.com/iNKORE-NET/UI.WPF.Modern) — WPF モダン UI コントロールライブラリ

## ライセンス

[MIT](../LICENSE)
