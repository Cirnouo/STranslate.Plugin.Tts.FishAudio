<div align="center">
  <a href="https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio">
    <img src="docs/images/icon.svg" alt="Fish Audio TTS" width="128" height="128" />
  </a>

  <h1>Fish Audio TTS</h1>

  <p>
    <a href="https://fish.audio">Fish Audio</a> 语音合成插件 for <a href="https://github.com/ZGGSONG/STranslate">STranslate</a>
  </p>

  <p>
    <img alt="License" src="https://img.shields.io/github/license/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Release" src="https://img.shields.io/github/v/release/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Downloads" src="https://img.shields.io/github/downloads/Cirnouo/STranslate.Plugin.Tts.FishAudio/total?style=flat-square" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" />
    <img alt="WPF" src="https://img.shields.io/badge/WPF-Plugin-blue?style=flat-square" />
  </p>
</div>

---

<div align="center">
  <img src="docs/images/overview.png" alt="插件总览" width="700" />
</div>

## 功能特性

- **高质量合成** — 基于 Fish Audio S2-Pro / S1 引擎，支持 80+ 种语言
- **模型搜索** — 在设置面板中搜索、浏览、试听并选择语音模型，支持分页
- **手动输入验证** — 直接输入模型 ID 自动验证并加载模型信息
- **情绪标记** — 通过文本内联标记控制语音情绪（S2-Pro: `[laugh]`，S1: `(happy)`）
- **韵律控制** — 语速（0.5x ~ 2.0x）、音量（±10 dB，0.1 dB 精度）、响度归一化
- **生成参数** — 表现力、多样性、延迟模式（质量优先 / 平衡 / 低延迟优先）、文本规范化
- **账户信息** — 实时显示剩余余额、API 延迟指示
- **多语言 UI** — 简体中文、繁體中文、English、日本語、한국어

## 安装

1. 前往 [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) 页面下载最新 `.spkg` 文件
2. 在 STranslate 中进入 **设置 → 插件 → 安装插件**
3. 选择下载的 `.spkg` 文件，重启 STranslate

> [!TIP]
> `.spkg` 本质是 ZIP 文件，STranslate 会自动解压加载。

## 前置条件

- [STranslate](https://github.com/ZGGSONG/STranslate) 最新版本
- Fish Audio API Key（[获取地址](https://fish.audio/app/api-keys)）
- Fish Audio 账户余额 > 0

## 配置说明

<details>
<summary><b>参数一览</b>（点击展开）</summary>

| 参数 | 默认值 | 说明 |
| :-- | :--: | :-- |
| API Key | — | Fish Audio API 密钥（必填） |
| 模型 ID | — | 语音模型 ID，可通过搜索选择或手动输入 |
| 合成引擎 | `s2-pro` | `s2-pro`（推荐）或 `s1` |
| 语速 | `1.0` | 0.5 ~ 2.0 |
| 音量 | `0 dB` | -10 ~ +10 dB，0.1 dB 精度 |
| 响度归一化 | 开启 | 仅 S2-Pro 引擎时显示 |
| 表现力 | `0.7` | 0 ~ 1，越高越多样 |
| 多样性 | `0.7` | 0 ~ 1 |
| 延迟模式 | 质量优先 | 质量优先 / 平衡 / 低延迟优先 |
| 文本规范化 | 关闭 | 数字→文字等自动转换 |

</details>

### 截图

<details>
<summary><b>界面截图</b>（点击展开）</summary>

#### 账户与 API

<img src="docs/images/account_and_api.png" alt="账户与 API" width="450" />

#### 模型选择与搜索

<div> 
    <img src="docs/images/model_selection.png" alt="模型选择" width="450" />
</div>

<div>
    <img src="docs/images/model_search.png" alt="模型搜索" width="450" />
</div>

### 语音合成引擎

<img src="docs/images/engine.png" alt="语音合成引擎" width="450" />

#### 韵律控制

<img src="docs/images/prosody.png" alt="韵律控制" width="450" />

#### 生成参数

<img src="docs/images/generation.png" alt="生成参数" width="450" />

</details>

## 情绪标记

Fish Audio 通过文本内联标记控制情绪，无需额外 API 参数：

**S2-Pro**（推荐）— 方括号 + 自然语言描述，可放在文本任意位置：
```
[angry] 这不可接受！
我不敢相信 [gasp] 你真的做到了 [laugh]
[whisper] 这是一个秘密
```

**S1** — 圆括号 + 固定标签集，放在句首：
```
(happy) 今天天气真好！
(sad)(whispering) 我很想你。
```

## 构建

```powershell
# 标准构建（Debug + .spkg 打包）
.\build.ps1

# 清理后构建
.\build.ps1 -Clean

# Release 构建
.\build.ps1 -Configuration Release
```

构建产物输出到仓库根目录 `STranslate.Plugin.Tts.FishAudio.spkg`。

<details>
<summary><b>环境要求</b></summary>

- .NET 10.0 SDK
- Windows（WPF 项目）

</details>

## 致谢

- [STranslate](https://github.com/ZGGSONG/STranslate) — 即用即走的翻译和 OCR 工具
- [Fish Audio](https://fish.audio) — 语音合成 API 提供商
- [iNKORE WPF Modern UI](https://github.com/iNKORE-NET/UI.WPF.Modern) — WPF 现代 UI 控件库

## 许可证

[MIT](LICENSE)
