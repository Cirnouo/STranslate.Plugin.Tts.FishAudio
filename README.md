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

  <p>
    <b>简体中文</b> | <a href="docs/README_TW.md">繁體中文</a> | <a href="docs/README_EN.md">English</a> | <a href="docs/README_JA.md">日本語</a> | <a href="docs/README_KO.md">한국어</a>
  </p>
</div>

---

<div align="center">
  <img src="docs/images/overview.png" alt="插件总览" width="700" />
</div>

## 功能特性

- **高质量合成** — 基于 Fish Audio S2-Pro / S1 模型，支持 80+ 种语言
- **声音搜索** — 在设置面板中搜索、浏览、试听并选择语音声音，支持分页
- **声音试听** — 带进度环的试听按钮，支持播放中切换，状态按声音 ID 同步
- **封面缓存** — 声音封面图缓存到 STranslate 插件缓存目录，支持查看占用并一键清理
- **手动输入验证** — 直接输入声音 ID 验证并加载声音信息
- **情绪标记** — 通过文本内联标记控制语音情绪（S2-Pro: `[laugh]`，S1: `(happy)`）
- **韵律控制** — 语速（0.5x ~ 2.0x）、音量（±10 dB，0.1 dB 精度）、响度归一化
- **生成参数** — 表现力、多样性、延迟模式（质量优先 / 平衡 / 低延迟）、文本规范化、上下文关联
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

> [!NOTE]
> API 余额购买/充值入口：[控制台 > 开发者 > 账单 > 余额 > 购买积分](https://fish.audio/app/developers/billing/)。余额扣除可能存在延迟，播放完成后立即刷新余额可能短暂显示旧值。

> [!TIP]
> 使用 `.edu` 邮箱注册并完成学生认证，可免费获得 5 美元额度，入口：[Fish Audio Students](https://fish.audio/students/)。

## 配置说明

<details>
<summary><b>参数一览</b>（点击展开）</summary>

| 参数 | 默认值 | 说明 |
| :-- | :--: | :-- |
| API Key | — | Fish Audio API 密钥（必填） |
| 声音 ID | —（随机声音） | 语音声音 ID，可通过搜索选择或手动输入。为空时使用随机声音（系统默认音色） |
| 合成模型 | `s2-pro` | `s2-pro`（推荐）或 `s1` |
| 语速 | `1.0` | 0.5 ~ 2.0 |
| 音量 | `0 dB` | -10 ~ +10 dB，0.1 dB 精度 |
| 响度归一化 | 开启 | 仅 S2-Pro 模型时显示 |
| MP3 比特率 | `192 kbps` | 可选 64 / 128 / 192 kbps |
| 表现力 | `0.7` | 0 ~ 1，越高越多样 |
| 多样性 | `0.7` | 0 ~ 1 |
| 延迟模式 | 质量优先 | 质量优先 / 平衡 / 低延迟 |
| 文本规范化 | 关闭 | 数字→文字等自动转换 |
| 上下文关联 | 开启 | 使用前序音频作为上下文以保持声音一致性 |
| 封面缓存 | 自动 | `cover_image` 以 `<声音 ID>.jpg` 缓存在插件缓存目录，可在“其他”中清理 |

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

# 清理后构建并运行回归测试
.\build.ps1 -Clean -Test

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
