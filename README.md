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

## 1 功能概览

- **高质量合成**: 支持 Fish Audio s2.1-pro-free / s2.1-pro / s2-pro / s1 合成模型，覆盖 80+ 种语言
- **声音选择**: 可通过名称搜索声音，也可直接输入声音 ID；支持试听、选择、清空和分页浏览
- **合成控制**: 支持语速、音量、响度归一化、MP3 比特率、表现力、多样性、延迟模式、文本规范化和上下文关联
- **情绪标记**: 可在文本中加入 Fish Audio 情绪标记，例如 S2-Pro 的 `[laugh]` 或 S1 的 `(happy)`
- **声音社区**: 可浏览 Fish Audio 创意社区中的公开声音，并结合插件内搜索、试听和 ID 设置快速应用喜欢的音色
- **本地化**: 简体中文、繁體中文、English、日本語、한국어

## 2 快速开始

### 2.1 安装

推荐优先使用 STranslate 插件市场安装

**STranslate 插件市场**

1. 打开 STranslate
2. 进入 **设置 -> 插件 -> 市场**
3. 搜索或找到 **Fish Audio TTS**，点击下载安装

**手动安装**

1. 前往 [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) 页面
2. 下载最新版本的 `STranslate.Plugin.Tts.FishAudio.spkg`
3. 在 STranslate 中进入 **设置 -> 插件 -> 安装**
4. 选择下载的 `.spkg` 文件

### 2.2 获取并设置 API Key

> [!TIP]
> 使用 `.edu` 邮箱注册 Fish Audio 并完成学生认证，可免费领取 5 美元余额。入口: [Fish Audio 学生免费积分](https://fish.audio/students/)。

使用电子邮件注册并登录 Fish Audio，进入 [开发者 > API 密钥](https://fish.audio/app/api-keys) 页面，点击 **创建新的密钥**，并复制此密钥

<!-- 截图: docs/images/fish-audio-api-keys.png
     内容: Fish Audio API Keys 页面，突出创建/复制 API Key 的位置；请遮挡真实 API Key。 -->
<div>
  <img src="docs/images/fish-audio-api-keys.png" alt="Fish Audio API Keys 页面" width="700" />
</div>

在插件设置页的 **API Key** 输入框中粘贴

<div>
  <img src="docs/images/plugin-api-key-input.png" alt="STranslate Fish Audio Plugin API Key 输入框" width="360" />
</div>

### 2.3 购买 API 余额

> [!TIP]
> S2.1 Pro 模型限时免费至 `2026-08-31`（UTC 全日可用），在插件内选用模型 `s2.1-pro-free` 进行调用。详情入口：[Fish Audio S2.1 Pro：面向开发者的免费文字转语音 API](https://fish.audio/blog/s2-1-pro-free-api/)

Fish Audio TTS 会消耗 Fish Audio API 余额。可以在 [开发者 > 控制台](https://fish.audio/app/developers/billing/) 页面中购买

<!-- 截图: docs/images/fish-audio-billing.png
     内容: Fish Audio Billing/Balance/Purchase Credits 入口，突出余额购买或充值位置。 -->
<div>
  <img src="docs/images/fish-audio-billing.png" alt="Fish Audio API 余额购买入口" width="700" />
</div>

### 2.4 获取声音

声音决定最终朗读时使用的音色。

Fish Audio 的声音社区收录了创作者公开分享的声音。你可以在官网按名称、角色或语言浏览并试听，确认风格后再回到插件中搜索同名声音，或复制声音 ID 直接应用。入口：[创意 > 发现](https://fish.audio/app/discovery)

<div>
  <img src="docs/images/fish-audio-voice-community.png" alt="Fish Audio 声音社区" width="700" />
</div>

### 2.5 设置声音

插件提供 **通过名称搜索声音** 和 **通过 ID 直接设置** 两种设置方式

#### 2.5.1 通过名称搜索声音

在插件的 **搜索** 标签页输入声音名称，点击搜索图标或按 `Enter` 查询；结果列表支持试听和分页，确认后点击 **选择** 即可应用。

<div>
  <img src="docs/images/plugin-voice-search.png" alt="STranslate Fish Audio Plugin 声音搜索" width="360" />
</div>

#### 2.5.2 通过 ID 直接设置

在声音社区中打开目标声音详情页，点击页面操作区的更多菜单（`...`），选择 `复制模型 ID`。Fish Audio 当前菜单项仍使用“模型 ID”命名；复制到的就是插件所需的声音 ID。

<!-- 截图: docs/images/fish-audio-voice-id.png
     内容: Fish Audio 声音详情页，突出地址栏或页面中声音 ID 的位置；请不要暴露个人账户信息。 -->
<div>
  <img src="docs/images/fish-audio-voice-id.png" alt="从 Fish Audio 声音详情页获取声音 ID" width="550" />
</div>

回到插件的 **通过 ID** 标签页，粘贴 ID 并点击确认按钮；插件会加载声音信息并应用该声音。

<div>
  <img src="docs/images/plugin-voice-id.png" alt="STranslate Fish Audio Plugin" width="360" />
</div>

## 3 配置说明
<details>
<summary><b>参数一览</b>（点击展开）</summary>

| 参数 |    默认值     | 说明                                                                     |
| :-- |:----------:|:-----------------------------------------------------------------------|
| API Key |     -      | Fish Audio API 密钥，必填                                                   |
| 声音 ID |    随机声音    | 可通过搜索选择或手动输入。为空时使用随机声音                                                 |
| 合成模型 | 免费期内 `s2.1-pro-free`，到期后 `s2.1-pro` | 2026-08-31 UTC 全日可选 `s2.1-pro-free`、`s2.1-pro`、`s2-pro`、`s1`；2026-09-01 UTC 起隐藏免费模型 |
| MP3 比特率 | `192 kbps` | 可选 `64`、`128`、`192`                                                    |
| 语速 |   `1.0`    | 范围 `0.5` 到 `2.0`                                                       |
| 音量 |   `0 dB`   | 范围 `-10 dB` 到 `+10 dB`                                                 |
| 响度归一化 |     开启     | `s2.1-pro-free`、`s2.1-pro`、`s2-pro` 可用；`s1` 时显示为禁用                     |
| 表现力 |   `0.7`    | 范围 `0` 到 `1`                                                           |
| 多样性 |   `0.7`    | 范围 `0` 到 `1`                                                           |
| 延迟模式 |    质量优先    | 质量优先 / 平衡 / 低延迟                                                        |
| 文本规范化 |     关闭     | 将数字、单位符号等转换为更适合朗读的文本                                                   |
| 上下文关联 |     开启     | 使用同一次合成音频的前序片段保持声音一致性，不会使用之前生成的其他音频                                      |

</details>

## 4 高级用法

### 4.1 情感控制

Fish Audio 通过文本内联标记控制情绪，无需额外 API 参数；把标记直接写进待朗读文本即可。常见用途包括改变句子的情绪、语气强弱，或插入笑声、叹气、停顿等拟声效果。

**S2 系列模型**（推荐）使用方括号和自然语言描述。句子级情绪通常放在句首，语气或音效标记可以放在需要生效的位置：

```text
[angry] 这不可接受！
我不敢相信 [gasp] 你真的做到了 [laugh]
[whisper] 这是一个秘密
```

**S1** 使用圆括号和固定标签集，通常放在句首：

```text
(happy) 今天天气真好！
(sad)(whispering) 我很想你
```

建议每句话只使用一个主要情绪，避免在很短的文本里堆叠过多标记。完整标签列表、组合方式和排查建议请参考官方文档：[Emotion Control](https://docs.fish.audio/developer-guide/core-features/emotions)。

### 4.2 细粒度控制

细粒度控制用于指定某个词、字或短语的准确发音，适合人名、品牌名、多音字、专业术语或日语音高需要精确控制的场景。用 `<|phoneme_start|>` 和 `<|phoneme_end|>` 包裹目标发音，其他标点和普通文本保持在标签外。

英文使用 CMU Arpabet，通常替换一个单词：

```text
I am an <|phoneme_start|>EH1 N JH AH0 N IH1 R<|phoneme_end|>.
```

中文使用带声调数字的拼音，一个音节一个标签：

```text
我是一个<|phoneme_start|>gong1<|phoneme_end|><|phoneme_start|>cheng2<|phoneme_end|><|phoneme_start|>shi1<|phoneme_end|>。
```

日文使用 OpenJTalk 风格的罗马字音素和音高数字，通常包裹较短词组：

```text
<|phoneme_start|>ha0shi1ga0<|phoneme_end|>見えます。
```

如需系统学习格式、符号表和生成工具，请参考官方文档：[Fine-grained Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control)、[English Phoneme Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/english)、[Chinese Phoneme Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/chinese)、[Japanese Phoneme Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/japanese)。

## 常见问题

**Q: 不设置声音会扣费吗？**

A: 会。未设置声音时 Fish Audio 会使用随机声音生成音频，仍然会消耗 API 余额

**Q: 试听会扣费吗？**

A: 不会。插件试听播放的是声音自带的公开音频，不会调用 TTS 接口；因此未配置 API Key 时也能进行试听

**Q: 为什么播放后余额没有马上变化？**

A: Fish Audio 的余额扣除可能存在延迟。播放完成后立即刷新余额时，可能会短暂看到旧余额

**Q: 声音搜索必须先配置 API Key 吗？**

A: 声音搜索、通过 ID 查询和试听都可以在未配置 API Key 时使用；但真正合成语音需要格式正确、可用且有余额的 API Key

**Q: 清理缓存会影响已选择的声音吗？**

A: 不会。清理缓存只删除声音封面图片缓存；声音 ID 和已选择声音信息仍保留。后续再次展示时会重新加载封面

## 构建

```powershell
# 推荐：Release 构建 + 回归测试，完成后清理中间产物
.\build.ps1 -Release -Clean -Test

# Debug 构建（默认配置；构建前清理中间产物）
.\build.ps1

# Debug 构建完成后清理中间产物
.\build.ps1 -Clean

# Debug 构建和回归测试完成后清理中间产物
.\build.ps1 -Clean -Test
```

构建产物输出到仓库根目录 `STranslate.Plugin.Tts.FishAudio.spkg`

不传 `-Release` 时默认执行 Debug 构建；`-Release` 是唯一的构建配置开关。普通构建会在开始前清理仓库的 `bin`、`obj`、`.artifacts`，插件项目的 `bin`、`obj`、`.artifacts`，以及测试项目的 `obj`、`bin`；`-Clean` 将同一清理推迟到构建及可选回归测试结束后（失败时也会执行），且不会删除仓库根目录中已生成的 `.spkg`。`-CleanOnly` 只执行清理，不进行构建。

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
