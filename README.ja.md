<div align="center">
  <a href="https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio"><img src="docs/assets/icon.svg" alt="Fish Audio TTS" width="80" height="80" /></a>
  <h1>Fish Audio TTS</h1>
  <p><a href="https://github.com/ZGGSONG/STranslate">STranslate</a> で <a href="https://fish.audio">Fish Audio</a> の音声合成を利用できます。</p>
  <p>
    <img alt="License" src="https://img.shields.io/github/license/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Release" src="https://img.shields.io/github/v/release/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Downloads" src="https://img.shields.io/github/downloads/Cirnouo/STranslate.Plugin.Tts.FishAudio/total?style=flat-square" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" />
    <img alt="WPF" src="https://img.shields.io/badge/WPF-Plugin-blue?style=flat-square" />
  </p>
  <p><a href="README.md">简体中文</a> · <a href="README.zh-TW.md">繁體中文</a> · <a href="README.en.md">English</a> · <b>日本語</b> · <a href="README.ko.md">한국어</a></p>
</div>

> [!TIP]
> `.edu` メールアドレスで Fish Audio に登録し、学生認証を完了すると、**5 米ドル分のクレジット**を無料で受け取れます。[申請ページ](https://fish.audio/students/)

> [!TIP]
> S2.1 Pro は **2026-11-30 UTC** まで期間限定で無料です。プラグインで `s2.1-pro-free` を選択してください。[キャンペーン詳細](https://fish.audio/blog/s2-1-pro-free-api/)

> [!IMPORTANT]
> S1 は **2026年12月31日** に提供を終了します。それ以降、モデル `s1` を指定した API リクエストは、同じ料金で S2.1 Pro によって処理されます。それまでに `s2.1-pro` に切り替えてください。[公式案内](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live)

対象資格と無料キャンペーンの条件は Fish Audio 公式ページをご確認ください。

## 機能概要

- **ボイスを探す**：名前検索、ページ送り、試聴、ID 照会後の適用に対応し、星でよく使うボイスをお気に入りに保存できます。
- **ストリーミング読み上げ**：テキストを分割送信しながら音声を受信・再生するため、長文全体の合成完了を待つ必要がありません。
- **音声形式**：既定は PCM。低速な回線では MP3 とビットレートを選択できます。
- **合成の調整**：モデル、速度、音量、ラウドネス正規化、表現力、多様性、レイテンシを設定できます。
- **アカウントとキャッシュ**：API 残高の確認・更新と、すべてのボイス入口で共有するカバー画像キャッシュの管理に対応します。
- **表示言語**：简体中文、繁體中文、English、日本語、한국어、Русский、Türkçe。

## インストール

### プラグインマーケット（推奨）

1. STranslate の **設定 → プラグイン → マーケット** を開きます。
2. **Fish Audio TTS** を検索し、ダウンロードしてインストールします。

### 手動インストール

1. [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) から `STranslate.Plugin.Tts.FishAudio.spkg` をダウンロードします。
2. STranslate の **設定 → プラグイン → インストール** からパッケージを読み込みます。

## 使い始める

1. プラグイン設定に [Fish Audio API Key](https://fish.audio/app/api-keys) を入力します。
2. 検索・試聴後、チェックで **適用** します。ID の虫眼鏡で照会するか、**お気に入り** から選べます。適用中のクロスで選択を解除できます。
3. 合成モデルと音声形式を選び、必要に応じて調整してから STranslate で読み上げます。

## ユーザーガイド

[完全なユーザーガイドを読む →](docs/user-guide/README.ja.md)

インストール、初期設定、ボイス選択、全設定項目、PCM / MP3、ネットワークとプロキシ、問題の対処方法を説明します。

[設定一覧](docs/user-guide/README.ja.md#configuration) · [音声形式](docs/user-guide/README.ja.md#audio-formats) · [ネットワークとプロキシ](docs/user-guide/README.ja.md#network-and-proxy) · [問題の対処](docs/user-guide/README.ja.md#troubleshooting)

## よくある質問

**Q: ボイス未設定でも課金されますか？**

A: 選択した合成モデルによります。未設定の場合はランダムなボイスで合成され、 有料モデルでは API クレジットを消費します。無料モデルには公式キャンペーンの条件が適用されます。

**Q: 試聴でクレジットを消費しますか？**

A: いいえ。ボイスに付属する公開音声を再生し、TTS API は呼び出しません。API Key 未設定でも試聴できます。

**Q: 再生後すぐに残高が変わらないのはなぜですか？**

A: Fish Audio の残高反映には遅延があり、再生直後の更新では古い残高が一時的に表示されることがあります。

**Q: ボイス検索には API Key が必要ですか？**

A: 検索、ID での照会、試聴は API Key 未設定でも利用できます。音声合成には有効な API Key が必要で、有料モデルでは十分な API 残高も必要です。

**Q: キャッシュ削除は選択中のボイスに影響しますか？**

A: いいえ。削除するのはボイスのカバー画像だけです。ボイス ID と選択情報は保持され、画像は次の表示時に再取得されます。

## Fish Audio 公式リソース

アカウント、ボイスコミュニティ、モデルの機能、パラメーター、料金は Fish Audio 公式ページをご確認ください。

[公式サイト](https://fish.audio) · [API Key](https://fish.audio/app/api-keys) · [ボイスコミュニティ](https://fish.audio/app/discovery) · [公式ドキュメント](https://docs.fish.audio) · [API 料金](https://docs.fish.audio/developer-guide/models-pricing/pricing-and-rate-limits)

## 開発

環境準備、必読文書、ビルド・テスト、提出要件は[開発・貢献ガイド](CONTRIBUTING.md)をご覧ください。

Windows と .NET 10 SDK で次を実行すると検証できます。

```powershell
.\build.ps1 -Release -Clean -Test
```

パッケージはリポジトリ直下の `STranslate.Plugin.Tts.FishAudio.spkg` に出力されます。

## フィードバックと貢献

[問題・提案を報告](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/issues) · [更新履歴](CHANGELOG.md) · [開発への参加](CONTRIBUTING.md)

## ライセンス

[MIT License](LICENSE)
