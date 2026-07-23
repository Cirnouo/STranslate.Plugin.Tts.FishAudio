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

## 1 機能概要

- **高品質合成**: Fish Audio s2.1-pro-free / s2.1-pro / s2-pro / s1 合成モデルに対応し、80 以上の言語をカバーします
- **ボイス選択**: 名前で検索することも、ボイス ID を直接入力することもできます。試聴、選択、クリア、ページ送りに対応します
- **合成制御**: 速度、音量、ラウドネス正規化、MP3 ビットレート、表現力、多様性、レイテンシモード、テキスト正規化、コンテキスト連携を設定できます
- **感情マーカー**: S2-Pro の `[laugh]` や S1 の `(happy)` など、Fish Audio の感情マーカーをテキストに追加できます
- **ボイスコミュニティ**: Fish Audio のクリエイターコミュニティで公開されているボイスを閲覧し、プラグイン内検索、試聴、ID 設定で好みの音色をすばやく適用できます
- **ローカライズ**: 簡体字中国語、繁体字中国語、English、日本語、한국어

## 2 クイックスタート

### 2.1 インストール

STranslate のプラグインマーケットからインストールする方法を推奨します

**STranslate プラグインマーケット**

1. STranslate を開きます
2. **設定 -> プラグイン -> マーケット** に移動します
3. **Fish Audio TTS** を検索または見つけて、ダウンロードしてインストールします

**手動インストール**

1. [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) ページを開きます
2. 最新の `STranslate.Plugin.Tts.FishAudio.spkg` をダウンロードします
3. STranslate で **設定 -> プラグイン -> インストール** を開きます
4. ダウンロードした `.spkg` ファイルを選択します

### 2.2 API Key を取得して設定

> [!TIP]
> `.edu` メールアドレスで Fish Audio に登録して学生認証を完了すると、5 米ドル分のクレジットを受け取れます。入口: [Fish Audio Students](https://fish.audio/students/)。

メールアドレスで登録して Fish Audio にログインし、[開発者 > API Keys](https://fish.audio/app/api-keys) を開いて **Create new key** をクリックし、キーをコピーします

<!-- スクリーンショット: images/fish-audio-api-keys.png
     内容: Fish Audio API Keys ページ。API Key の作成/コピー位置を強調し、実際の API Key は隠してください。 -->
<div>
  <img src="images/fish-audio-api-keys.png" alt="Fish Audio API Keys ページ" width="700" />
</div>

プラグイン設定ページの **API Key** 入力欄に貼り付けます

<div>
  <img src="images/plugin-api-key-input.png" alt="STranslate Fish Audio Plugin API Key 入力欄" width="360" />
</div>

### 2.3 API クレジットを購入

> [!TIP]
> S2.1 Pro モデルは `2026-08-31` UTC 終了時まで期間限定で無料です。プラグイン内で `s2.1-pro-free` を選択して使用します。詳細: [Fish Audio S2.1 Pro: Free Text-to-Speech API for Developers](https://fish.audio/blog/s2-1-pro-free-api/)

Fish Audio TTS は Fish Audio API クレジットを消費します。[開発者 > コンソール](https://fish.audio/app/developers/billing/) で購入できます

<!-- スクリーンショット: images/fish-audio-billing.png
     内容: Fish Audio Billing/Balance/Purchase Credits の入口。クレジット購入またはチャージ位置を強調してください。 -->
<div>
  <img src="images/fish-audio-billing.png" alt="Fish Audio API クレジット購入入口" width="700" />
</div>

### 2.4 ボイスを取得

ボイスは読み上げ時の音色を決定します。

Fish Audio のボイスコミュニティには、クリエイターが公開したボイスが収録されています。公式サイトで名前、キャラクター、言語から閲覧・試聴し、気に入ったらプラグインで同名ボイスを検索するか、ボイス ID をコピーして直接適用できます。入口: [Creative > Discover](https://fish.audio/app/discovery)

<div>
  <img src="images/fish-audio-voice-community.png" alt="Fish Audio ボイスコミュニティ" width="700" />
</div>

### 2.5 ボイスを設定

プラグインには **名前でボイスを検索** と **ID で直接設定** の 2 つの設定方法があります

#### 2.5.1 名前でボイスを検索

プラグインの **検索** タブにボイス名を入力し、検索アイコンをクリックするか `Enter` を押します。結果リストでは試聴とページ送りができ、**選択** をクリックするとそのボイスを適用できます。

<div>
  <img src="images/plugin-voice-search.png" alt="STranslate Fish Audio Plugin ボイス検索" width="360" />
</div>

#### 2.5.2 ID で直接設定

ボイスコミュニティで対象ボイスの詳細ページを開き、ページ操作エリアのその他メニュー（`...`）をクリックして `モデル ID をコピー` を選びます。Fish Audio の現在のメニュー名はまだ「モデル ID」ですが、コピーされる値がプラグインに必要なボイス ID です。

<!-- スクリーンショット: images/fish-audio-voice-id.png
     内容: Fish Audio ボイス詳細ページ。ボイス ID の位置を強調し、個人アカウント情報は表示しないでください。 -->
<div>
  <img src="images/fish-audio-voice-id.png" alt="Fish Audio ボイス詳細ページからボイス ID を取得" width="550" />
</div>

プラグインの **ID 指定** タブに戻り、ID を貼り付けて確認ボタンをクリックします。プラグインがボイス情報を読み込み、そのボイスを適用します。

<div>
  <img src="images/plugin-voice-id.png" alt="STranslate Fish Audio Plugin" width="360" />
</div>

## 3 設定

<details>
<summary><b>パラメータ一覧</b>（クリックで展開）</summary>

| パラメータ | デフォルト | 説明 |
| :-- | :--: | :-- |
| API Key | - | Fish Audio API キー。必須です。 |
| ボイス ID | ランダムボイス | 検索で選択、または手動入力できます。空の場合はランダムボイスを使用します。 |
| 合成モデル | 無料期間中は `s2.1-pro-free`、終了後は `s2.1-pro` | 2026-08-31 UTC 終了時までは `s2.1-pro-free`、`s2.1-pro`、`s2-pro`、`s1` を選択可能。2026-09-01 UTC 以降は無料モデルを非表示。 |
| MP3 ビットレート | `192 kbps` | `64`、`128`、`192` から選択できます。 |
| 速度 | `1.0` | 範囲は `0.5` から `2.0`。 |
| 音量 | `0 dB` | 範囲は `-10 dB` から `+10 dB`。 |
| ラウドネス正規化 | オン | `s2.1-pro-free`、`s2.1-pro`、`s2-pro` で使用可能。`s1` では無効表示されます。 |
| 表現力 | `0.7` | 範囲は `0` から `1`。 |
| 多様性 | `0.7` | 範囲は `0` から `1`。 |
| レイテンシモード | 品質優先 | 品質優先 / バランス / 低レイテンシ。 |
| テキスト正規化 | オフ | 数字、単位記号などを読み上げに適したテキストへ自動変換します。 |
| コンテキスト連携 | オン | 同じ合成音声内の前のチャンクを使って声の一貫性を保ち、以前に生成した別の音声は参照しません。 |

</details>

## 4 上級者向け

### 4.1 感情制御

Fish Audio はテキスト内のインラインマーカーで感情を制御します。追加の API パラメータは不要です。読み上げるテキストに直接マーカーを書き込みます。文全体の感情、語気の強さ、笑い声・息をのむ音・ささやきなどの音声効果を指定する用途に向いています。

**S2 系列モデル**（推奨）は角括弧と自然言語の説明を使います。文全体の感情は通常文頭に置き、語気や効果音のマーカーは効かせたい位置に置けます：

```text
[angry] これは受け入れられない！
信じられない [gasp] 君が本当にやり遂げた [laugh]
[whisper] これは秘密です
```

**S1** は丸括弧と固定タグセットを使い、通常は文頭に置きます

```text
(happy) 今日はとてもいい天気！
(sad)(whispering) とても会いたい
```

1 文につき主要な感情は 1 つに抑え、短いテキストに多くのマーカーを詰め込みすぎないことを推奨します。完全なタグ一覧、組み合わせ方、トラブルシューティングは公式ドキュメントを参照してください：[Emotion Control](https://docs.fish.audio/developer-guide/core-features/emotions)。

### 4.2 細粒度制御

細粒度制御は、単語、文字、短いフレーズの正確な発音を指定するための機能です。人名、ブランド名、中国語の多音字、専門用語、日本語のピッチアクセントを正確に制御したい場合に使えます。対象の発音を `<|phoneme_start|>` と `<|phoneme_end|>` で囲み、通常の句読点や周囲のテキストはタグの外に置きます。

英語は CMU Arpabet を使い、通常は 1 単語を置き換えます：

```text
I am an <|phoneme_start|>EH1 N JH AH0 N IH1 R<|phoneme_end|>.
```

中国語は声調番号付きのピンインを使い、1 音節ごとに 1 つのタグを使います：

```text
我是一个<|phoneme_start|>gong1<|phoneme_end|><|phoneme_start|>cheng2<|phoneme_end|><|phoneme_start|>shi1<|phoneme_end|>。
```

日本語は OpenJTalk 形式のローマ字音素とピッチ番号を使い、通常は短い語句を囲みます：

```text
<|phoneme_start|>ha0shi1ga0<|phoneme_end|>見えます。
```

形式、記号表、生成ツールを詳しく確認する場合は公式ドキュメントを参照してください：[Fine-grained Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control)、[English Phoneme Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/english)、[Chinese Phoneme Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/chinese)、[Japanese Phoneme Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/japanese)。

## よくある質問

**Q: ボイスを設定しないと課金されますか？**

A: はい。ボイス未設定時は Fish Audio がランダムボイスで音声を生成し、それでも API クレジットを消費します

**Q: 試聴でも課金されますか？**

A: いいえ。プラグインの試聴はボイスに付属する公開音声を再生するだけで、TTS API は呼び出しません。そのため、API Key を設定していなくても試聴できます

**Q: 再生後に残高がすぐ変わらないのはなぜですか？**

A: Fish Audio の残高差し引きには遅延がある場合があります。再生直後に残高を更新すると、しばらく古い残高が表示されることがあります

**Q: ボイス検索の前に API Key を設定する必要がありますか？**

A: ボイス検索、ID による検索、試聴はいずれも API Key 未設定でも使用できます。ただし、実際の音声合成には形式が正しく利用可能な API Key と残高が必要です

**Q: キャッシュを削除すると選択済みのボイスに影響しますか？**

A: いいえ。キャッシュを削除してもボイスのカバー画像キャッシュだけが消えます。ボイス ID と選択済みボイス情報はそのままで、次回表示時にカバーが再読み込みされます

## ビルド

```powershell
# 推奨：Release ビルド + 回帰テスト、完了後に中間生成物を削除
.\build.ps1 -Release -Clean -Test

# Debug ビルド（既定の構成、中間生成物を事前に削除）
.\build.ps1

# Debug ビルド後に中間生成物を削除
.\build.ps1 -Clean

# Debug ビルドと回帰テストの完了後に中間生成物を削除
.\build.ps1 -Clean -Test
```

ビルド成果物はリポジトリのルートに `STranslate.Plugin.Tts.FishAudio.spkg` として出力されます

`-Release` を指定しない場合は Debug ビルドが既定であり、`-Release` が唯一のビルド構成スイッチです。通常のビルドでは、開始前にリポジトリの `bin`、`obj`、`.artifacts`、プラグインプロジェクトの `bin`、`obj`、`.artifacts`、テストプロジェクトの `obj`、`bin` を削除します。`-Clean` は同じ削除処理をビルドと任意の回帰テストの終了後まで延期し、失敗時にも実行します。また、リポジトリルートに既に生成された `.spkg` は削除しません。`-CleanOnly` はビルドせずに削除処理だけを実行します。

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
