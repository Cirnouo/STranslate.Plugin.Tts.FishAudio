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


## 功能概览

- **高质量合成**: 支持 Fish Audio S2-Pro / S1 合成模型，覆盖 80+ 种语言。
- **声音选择**: 可通过名称搜索声音，也可直接输入声音 ID；支持试听、选择、清空和分页浏览。
- **封面缓存**: 自动缓存声音 `cover_image`，减少重复加载；可在设置页查看占用并清理。
- **账户确认**: 验证 API Key 后显示余额和“已验证并应用”状态，便于确认配置是否生效。
- **合成控制**: 支持语速、音量、响度归一化、MP3 比特率、表现力、多样性、延迟模式、文本规范化和上下文关联。
- **情绪标记**: 可在文本中加入 Fish Audio 情绪标记，例如 S2-Pro 的 `[laugh]` 或 S1 的 `(happy)`。
- **多语言 UI**: 简体中文、繁體中文、English、日本語、한국어。

## 快速开始

### 1. 安装插件

推荐优先使用 STranslate 插件市场安装；如果插件市场暂时无法访问，也可以从 GitHub Release 手动安装 `.spkg`。

**方式一: STranslate 插件市场**

1. 打开 STranslate。
2. 进入 **设置 -> 插件 -> 市场**。
3. 搜索或找到 **Fish Audio TTS**，点击下载安装。
4. 安装后建议重启 STranslate。

**方式二: 手动安装 `.spkg`**

1. 前往 [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) 页面。
2. 下载最新版本的 `STranslate.Plugin.Tts.FishAudio.spkg`。
3. 在 STranslate 中进入 **设置 -> 插件 -> 安装插件**。
4. 选择下载的 `.spkg` 文件，重启 STranslate。

> [!TIP]
> `.spkg` 本质是 ZIP 文件，STranslate 会自动解压并加载，不需要手动解压。

### 2. 获取 API Key

1. 登录 [Fish Audio API Keys](https://fish.audio/app/api-keys)。
2. 创建或复制一个 API Key。

<!-- 截图: docs/images/fish-audio-api-keys.png
     内容: Fish Audio API Keys 页面，突出创建/复制 API Key 的位置；请遮挡真实 API Key。 -->
<div>
  <img src="docs/images/fish-audio-api-keys.png" alt="Fish Audio API Keys 页面" width="700" />
</div>

3. 在插件设置页的 **API Key** 输入框中粘贴。
4. 点击确认按钮，或在输入框聚焦时按 `Enter`。
5. 看到 **已验证并应用**，并且账户信息显示余额后，说明当前 API Key 已经被插件使用。

<!-- 截图: docs/images/settings-account-api.png
     内容: 插件设置页中的账户信息、API Key 输入框、确认按钮、已验证并应用状态和余额。 -->
<div>
  <img src="docs/images/settings-account-api.png" alt="插件账户和 API Key 设置" width="360" />
</div>

### 3. 购买 API 余额

Fish Audio TTS 会消耗 Fish Audio API 余额。可以在 [Console -> Developer -> Billing -> Balance -> Purchase Credits](https://fish.audio/app/developers/billing/) 购买或充值。

<!-- 截图: docs/images/fish-audio-billing.png
     内容: Fish Audio Billing/Balance/Purchase Credits 入口，突出余额购买或充值位置。 -->
<div>
  <img src="docs/images/fish-audio-billing.png" alt="Fish Audio API 余额购买入口" width="700" />
</div>

> [!NOTE]
> 余额扣除可能存在延迟。播放完成后立即刷新余额时，可能短暂显示旧余额。

> [!TIP]
> 使用 `.edu` 邮箱注册并完成学生认证，可获得 Fish Audio 提供的学生额度。入口: [Fish Audio Students](https://fish.audio/students/)。

### 4. 设置声音

声音决定最终朗读时使用的音色。插件提供 **搜索** 和 **通过 ID** 两种设置方式。

> [!NOTE]
> 不设置声音也可以使用插件。此时插件不会向 Fish Audio 发送 `reference_id`，Fish Audio 会使用随机声音；随机声音同样会消耗 API 余额。

**方法一: 通过名称搜索**

1. 切换到声音区域的 **搜索** 标签页。
2. 在输入框中输入声音名称。
3. 点击搜索图标，或在输入框聚焦时按 `Enter`。
4. 在结果中试听声音，确认后点击 **选择**。
5. 如果结果很多，可以使用分页控件切换页面。

<!-- 截图: docs/images/settings-voice-search.png
     内容: 插件声音搜索界面，包含搜索输入框、搜索按钮、搜索结果、试听按钮、选择按钮和分页。 -->
<div>
  <img src="docs/images/settings-voice-search.png" alt="通过名称搜索并选择声音" width="450" />
</div>

**方法二: 通过声音 ID**

1. 在 Fish Audio 官网打开目标声音的详情页。
2. 从展开菜单栏中复制声音 ID。

<!-- 截图: docs/images/fish-audio-voice-id.png
     内容: Fish Audio 声音详情页，突出地址栏或页面中声音 ID 的位置；请不要暴露个人账户信息。 -->
<div>
  <img src="docs/images/fish-audio-voice-id.png" alt="从 Fish Audio 声音详情页获取声音 ID" width="650" />
</div>

3. 回到插件设置页，切换到 **通过 ID** 标签页。
4. 粘贴声音 ID，点击确认按钮，或在输入框聚焦时按 `Enter`。
5. 插件会验证并加载这个声音的信息。



<!-- 截图: docs/images/settings-voice-by-id.png
     内容: 插件通过 ID 设置声音的界面，包含声音 ID 输入框、粘贴按钮和确认按钮。 -->
<div>
  <img src="docs/images/settings-voice-by-id.png" alt="通过声音 ID 设置声音" width="450" />
</div>

## 配置说明

插件设置页按使用流程分为账户、声音、模型、音频输出、韵律、生成参数和其他几个区域。

### 账户与 API

<!-- 截图: docs/images/settings-account-api.png
     内容: 插件账户信息、API 地址、API Key 输入框、确认状态、余额和刷新按钮。 -->
<div>
  <img src="docs/images/settings-account-api.png" alt="账户与 API 配置" width="450" />
</div>

| 配置项 | 说明 |
| :-- | :-- |
| 账户信息 | 显示当前 API Key 对应账户的余额，单位为美元。点击 **刷新** 可手动更新余额。 |
| API 地址 | Fish Audio API 地址，当前固定为 `https://api.fish.audio`。 |
| API Key | Fish Audio API 密钥。只有点击确认按钮或按 `Enter` 后才会验证并应用。 |
| 验证状态 | 等待服务器返回时显示 **等待响应中**；成功后显示 **已验证并应用**；格式错误会在输入框旁提示。 |

### 声音

通过名称搜索声音

<!-- 截图: docs/images/settings-voice-search.png
     内容: 已选声音展示区 + 搜索标签页，展示封面、标题、作者、热度、试听、选择和清空动作。 -->
<div>
  <img src="docs/images/settings-voice-search.png" alt="声音搜索配置" width="450" />
</div>

通过 ID 获取声音

<!-- 截图: docs/images/settings-voice-by-id.png
     内容: 通过 ID 标签页的一行输入框和控件，展示与搜索标签页一致的初始高度。 -->
<div>
  <img src="docs/images/settings-voice-by-id.png" alt="通过 ID 配置声音" width="450" />
</div>

| 配置项 | 说明 |
| :-- | :-- |
| 已选声音 | 显示当前应用的声音信息。没有设置时显示 **随机声音**。 |
| 搜索 | 通过声音名称搜索 Fish Audio 声音，可试听后选择。搜索结果中的封面图会自动缓存。 |
| 通过 ID | 适合已经知道声音 ID 的场景。声音 ID 必须是 32 位小写十六进制字符。 |
| 清空声音 | 清除当前声音设置，恢复为随机声音。 |

### 模型与音频输出

<!-- 截图: docs/images/settings-model-audio.png
     内容: 合成模型和 MP3 比特率两个配置卡片。 -->
<div>
  <img src="docs/images/settings-model-audio.png" alt="模型与音频输出配置" width="450" />
</div>

| 配置项 | 默认值 | 说明 |
| :-- | :--: | :-- |
| 合成模型 | `s2-pro` | 选择 Fish Audio 合成引擎。`s2-pro` 为推荐选项，支持 80+ 种语言和更丰富的情绪标记；`s1` 可用于需要旧模型行为的场景。 |
| MP3 比特率 | `192 kbps` | 控制输出音频质量和体积，可选 `64`、`128`、`192`。比特率越高，音质通常越好，文件也更大。 |

### 韵律

<!-- 截图: docs/images/settings-prosody.png
     内容: 语速、音量、响度归一化三个配置项。 -->
<div>
  <img src="docs/images/settings-prosody.png" alt="韵律配置" width="450" />
</div>

| 配置项 | 默认值 | 说明 |
| :-- | :--: | :-- |
| 语速 | `1.0` | 控制朗读速度，范围 `0.5` 到 `2.0`。小于 `1.0` 更慢，大于 `1.0` 更快。 |
| 音量 | `0 dB` | 控制音量偏移，范围 `-10 dB` 到 `+10 dB`，支持 `0.1 dB` 精度。 |
| 响度归一化 | 开启 | 仅在 `s2-pro` 模型下显示，用于让输出响度更稳定。 |

### 生成参数

<!-- 截图: docs/images/settings-generation.png
     内容: 表现力、多样性、延迟模式、文本规范化、上下文关联配置项。 -->
<div>
  <img src="docs/images/settings-generation.png" alt="生成参数配置" width="450" />
</div>

| 配置项 | 默认值 | 说明 |
| :-- | :--: | :-- |
| 表现力 | `0.7` | 对应生成采样温度。数值越高，表达变化越明显；数值越低，输出更稳定。 |
| 多样性 | `0.7` | 对应 `top_p`。数值越高，采样范围越宽；数值越低，结果更收敛。 |
| 延迟模式 | 质量优先 | 在质量和响应速度之间取舍：质量优先、平衡、低延迟。 |
| 文本规范化 | 关闭 | 自动将数字、单位符号等内容转换为更适合朗读的文本。 |
| 上下文关联 | 开启 | 使用前序音频作为上下文，帮助长文本保持声音一致性。 |

### 其他

<!-- 截图: docs/images/settings-misc-cache.png
     内容: 其他区域中的缓存占用、清理缓存按钮和处理中等待提示。 -->
<div>
  <img src="docs/images/settings-misc-cache.png" alt="缓存配置" width="450" />
</div>

| 配置项 | 说明 |
| :-- | :-- |
| 缓存占用 | 扫描插件缓存目录下 `cover_images/*.jpg` 的真实文件大小，并自动使用 B、KB、MB、GB 等单位显示。 |
| 清理缓存 | 删除声音封面缓存。清理过程中按钮会禁用并显示旋转等待提示，完成或超时后恢复可点击。 |

<details>
<summary><b>参数一览</b>（点击展开）</summary>

| 参数 | 默认值 | 说明 |
| :-- | :--: | :-- |
| API Key | - | Fish Audio API 密钥，必填。 |
| 声音 ID | -（随机声音） | 可通过搜索选择或手动输入。为空时使用随机声音。 |
| 合成模型 | `s2-pro` | `s2-pro` 或 `s1`。 |
| MP3 比特率 | `192 kbps` | 可选 `64`、`128`、`192`。 |
| 语速 | `1.0` | 范围 `0.5` 到 `2.0`。 |
| 音量 | `0 dB` | 范围 `-10 dB` 到 `+10 dB`。 |
| 响度归一化 | 开启 | 仅 `s2-pro` 模型时显示。 |
| 表现力 | `0.7` | 范围 `0` 到 `1`。 |
| 多样性 | `0.7` | 范围 `0` 到 `1`。 |
| 延迟模式 | 质量优先 | 质量优先 / 平衡 / 低延迟。 |
| 文本规范化 | 关闭 | 将数字、单位符号等转换为更适合朗读的文本。 |
| 上下文关联 | 开启 | 使用前序音频作为上下文以保持声音一致性。 |

</details>

## 情绪标记

Fish Audio 通过文本内联标记控制情绪，无需额外 API 参数。

**S2-Pro**（推荐）使用方括号和自然语言描述，可放在文本任意位置：

```text
[angry] 这不可接受！
我不敢相信 [gasp] 你真的做到了 [laugh]
[whisper] 这是一个秘密
```

**S1** 使用圆括号和固定标签集，通常放在句首：

```text
(happy) 今天天气真好！
(sad)(whispering) 我很想你。
```

## 常见问题

**不设置声音会扣费吗？**

会。未设置声音时 Fish Audio 会使用随机声音生成音频，仍然会消耗 API 余额。

**试听会扣费吗？**

不会。插件试听播放的是 Fish Audio 声音条目自带的公开样本音频，不会调用 TTS 合成接口；因此未验证 API Key 时也能进行试听。只有实际让 STranslate 使用插件合成朗读文本时，才需要有效 API Key 并消耗余额。

**为什么播放后余额没有马上变化？**

Fish Audio 的余额扣除可能存在延迟。播放完成后立即刷新余额时，可能会短暂看到旧余额。

**声音搜索必须先配置 API Key 吗？**

声音搜索、通过 ID 查询和试听都可以在未验证 API Key 时使用；但真正合成语音需要有效 API Key 和可用余额。

**清理缓存会影响已选择的声音吗？**

不会。清理缓存只删除声音封面图片缓存；声音 ID 和已选择声音信息仍保留。后续再次展示时会重新加载封面。

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
