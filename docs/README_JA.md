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

## 機能概要

- **高品質合成**: Fish Audio S2-Pro / S1 合成モデルに対応し、80 以上の言語をカバーします。
- **ボイス選択**: 名前で検索することも、ボイス ID を直接入力することもできます。試聴、選択、クリア、ページ送りに対応します。
- **合成制御**: 速度、音量、ラウドネス正規化、MP3 ビットレート、表現力、多様性、レイテンシモード、テキスト正規化、コンテキスト連携を設定できます。
- **感情マーカー**: S2-Pro の `[laugh]` や S1 の `(happy)` など、Fish Audio の感情マーカーをテキストに追加できます。
- **多言語 UI**: 簡体字中国語、繁体字中国語、English、日本語、한국어。

## クイックスタート

### インストール

STranslate のプラグインマーケットからインストールする方法を推奨します。

**STranslate プラグインマーケット**

1. STranslate を開きます。
2. **設定 -> プラグイン -> マーケット** に移動します。
3. **Fish Audio TTS** を検索または見つけて、ダウンロードしてインストールします。

**手動インストール**

1. [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) ページを開きます。
2. 最新の `STranslate.Plugin.Tts.FishAudio.spkg` をダウンロードします。
3. STranslate で **設定 -> プラグイン -> インストール** を開きます。
4. ダウンロードした `.spkg` ファイルを選択します。

### API Key を取得

1. [Fish Audio API Keys](https://fish.audio/app/api-keys) にログインします。
2. API Key を作成またはコピーします。

<!-- スクリーンショット: images/fish-audio-api-keys.png
     内容: Fish Audio API Keys ページ。API Key の作成/コピー位置を強調し、実際の API Key は隠してください。 -->
<div>
  <img src="images/fish-audio-api-keys.png" alt="Fish Audio API Keys ページ" width="700" />
</div>

3. プラグイン設定ページの **API Key** 入力欄に貼り付けます。
4. 確認ボタンをクリックするか、入力欄にフォーカスした状態で `Enter` を押します。
5. **検証済み・適用済み** が表示され、アカウント情報に残高が表示されれば、現在の API Key がプラグインで使用されています。

### API クレジットを購入

Fish Audio TTS は Fish Audio API クレジットを消費します。[コンソール > 開発者 > 請求 > 残高 > クレジットを購入](https://fish.audio/app/developers/billing/) で購入またはチャージできます。

<!-- スクリーンショット: images/fish-audio-billing.png
     内容: Fish Audio Billing/Balance/Purchase Credits の入口。クレジット購入またはチャージ位置を強調してください。 -->
<div>
  <img src="images/fish-audio-billing.png" alt="Fish Audio API クレジット購入入口" width="700" />
</div>

> [!TIP]
> `.edu` メールアドレスで登録して学生認証を完了すると、5 米ドル分のクレジットを受け取れます。入口: [Fish Audio Students](https://fish.audio/students/)。

### ボイスを設定

ボイスは読み上げ時の音色を決定します。プラグインには **検索** と **ID 指定** の 2 つの設定方法があります。

> [!NOTE]
> ボイスを設定しなくてもプラグインは使用できます。この場合、プラグインは Fish Audio に `reference_id` を送信せず、Fish Audio がランダムボイスを使用します。ランダムボイスでも API クレジットは消費されます。

**名前で検索**

1. ボイスセクションの **検索** タブに切り替えます。
2. 入力欄にボイス名を入力します。
3. 検索アイコンをクリックするか、入力欄にフォーカスした状態で `Enter` を押します。
4. 結果からボイスを試聴し、決まったら **選択** をクリックします。
5. 結果が多い場合は、ページ送りコントロールでページを切り替えます。

**ボイス ID を使う**

1. Fish Audio 公式サイトで対象ボイスの詳細ページを開きます。
2. 展開メニューからボイス ID をコピーします。
3. プラグイン設定ページに戻り、**ID 指定** タブに切り替えます。
4. ボイス ID を貼り付け、確認ボタンをクリックするか、入力欄にフォーカスした状態で `Enter` を押します。
5. プラグインが ID を検証し、ボイス情報を読み込みます。

<!-- スクリーンショット: images/fish-audio-voice-id.png
     内容: Fish Audio ボイス詳細ページ。ボイス ID の位置を強調し、個人アカウント情報は表示しないでください。 -->
<div>
  <img src="images/fish-audio-voice-id.png" alt="Fish Audio ボイス詳細ページからボイス ID を取得" width="650" />
</div>

## 設定

<details>
<summary><b>パラメータ一覧</b>（クリックで展開）</summary>

| パラメータ | デフォルト | 説明 |
| :-- | :--: | :-- |
| API Key | - | Fish Audio API キー。必須です。 |
| ボイス ID | ランダムボイス | 検索で選択、または手動入力できます。空の場合はランダムボイスを使用します。 |
| 合成モデル | `s2-pro` | `s2-pro` または `s1`。 |
| MP3 ビットレート | `192 kbps` | `64`、`128`、`192` から選択できます。 |
| 速度 | `1.0` | 範囲は `0.5` から `2.0`。 |
| 音量 | `0 dB` | 範囲は `-10 dB` から `+10 dB`。 |
| ラウドネス正規化 | オン | `s2-pro` モデルでのみ表示されます。 |
| 表現力 | `0.7` | 範囲は `0` から `1`。 |
| 多様性 | `0.7` | 範囲は `0` から `1`。 |
| レイテンシモード | 品質優先 | 品質優先 / バランス / 低レイテンシ。 |
| テキスト正規化 | オフ | 数字、単位記号などを読み上げに適したテキストへ自動変換します。 |
| コンテキスト連携 | オン | 前の音声をコンテキストとして使用し、長文でも声の一貫性を保ちやすくします。 |

</details>

## 感情マーカー

Fish Audio はテキスト内のインラインマーカーで感情を制御します。追加の API パラメータは不要です。

**S2-Pro**（推奨）は角括弧と自然言語の説明を使い、テキストの任意の位置に置けます。

```text
[angry] これは受け入れられない！
信じられない [gasp] 君が本当にやり遂げた [laugh]
[whisper] これは秘密です
```

**S1** は丸括弧と固定タグセットを使い、通常は文頭に置きます。

```text
(happy) 今日はとてもいい天気！
(sad)(whispering) とても会いたい。
```

## よくある質問

**Q: ボイスを設定しないと課金されますか？**

A: はい。ボイス未設定時は Fish Audio がランダムボイスで音声を生成し、それでも API クレジットを消費します。

**Q: 試聴でも課金されますか？**

A: いいえ。プラグインの試聴はボイスに付属する公開音声を再生するだけで、TTS API は呼び出しません。そのため、API Key を検証していなくても試聴できます。

**Q: 再生後に残高がすぐ変わらないのはなぜですか？**

A: Fish Audio の残高差し引きには遅延がある場合があります。再生直後に残高を更新すると、しばらく古い残高が表示されることがあります。

**Q: ボイス検索の前に API Key を設定する必要がありますか？**

A: ボイス検索、ID による検索、試聴はいずれも API Key の検証なしで使用できます。ただし、実際の音声合成には有効な API Key と利用可能な残高が必要です。

**Q: キャッシュを削除すると選択済みのボイスに影響しますか？**

A: いいえ。キャッシュを削除してもボイスのカバー画像キャッシュだけが消えます。ボイス ID と選択済みボイス情報はそのままで、次回表示時にカバーが再読み込みされます。

## ビルド

```powershell
# 標準ビルド（Debug + .spkg パッケージ）
.\build.ps1

# クリーンビルド
.\build.ps1 -Clean

# クリーンビルドと回帰テスト実行
.\build.ps1 -Clean -Test

# Release ビルド
.\build.ps1 -Configuration Release
```

ビルド成果物はリポジトリのルートに `STranslate.Plugin.Tts.FishAudio.spkg` として出力されます。

<details>
<summary><b>環境要件</b></summary>

- .NET 10.0 SDK
- Windows（WPF プロジェクト）

</details>

## 謝辞

- [STranslate](https://github.com/ZGGSONG/STranslate) - すぐに使える翻訳・OCR ツール
- [Fish Audio](https://fish.audio) - 音声合成 API の提供元
- [iNKORE WPF Modern UI](https://github.com/iNKORE-NET/UI.WPF.Modern) - WPF 向けモダン UI コントロールライブラリ

## ライセンス

[MIT](../LICENSE)
