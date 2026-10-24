<div align="center">
  <a href="https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio"><img src="docs/assets/icon.svg" alt="Fish Audio TTS" width="80" height="80" /></a>
  <h1>Fish Audio TTS</h1>
  <p>为 <a href="https://github.com/ZGGSONG/STranslate">STranslate</a> 提供 <a href="https://fish.audio">Fish Audio</a> 语音合成。</p>
  <p>
    <img alt="License" src="https://img.shields.io/github/license/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Release" src="https://img.shields.io/github/v/release/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Downloads" src="https://img.shields.io/github/downloads/Cirnouo/STranslate.Plugin.Tts.FishAudio/total?style=flat-square" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" />
    <img alt="WPF" src="https://img.shields.io/badge/WPF-Plugin-blue?style=flat-square" />
  </p>
  <p><b>简体中文</b> · <a href="README.zh-TW.md">繁體中文</a> · <a href="README.en.md">English</a> · <a href="README.ja.md">日本語</a> · <a href="README.ko.md">한국어</a></p>
</div>

> [!WARNING]
> 大陆网络无法访问 `fish.audio` 服务。请参阅用户指南中的[网络与代理说明](docs/user-guide/README.md#network-and-proxy)。

> [!TIP]
> 使用 `.edu` 邮箱注册 Fish Audio 并完成学生认证，可免费领取 **5 美元余额**。[申请入口](https://fish.audio/students/)

> [!TIP]
> S2.1 Pro 模型限时免费至 **2026-11-30 UTC**，在插件内选用模型 `s2.1-pro-free` 进行调用。[活动说明](https://fish.audio/blog/s2-1-pro-free-api/)

> [!IMPORTANT]
> S1 将于 **2026-12-31** 退役。此后，使用模型 `s1` 的 API 请求将由 S2.1 Pro 以相同价格提供服务。请在此之前切换到 `s2.1-pro`。[官方说明](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live)

福利资格与免费活动条件以 Fish Audio 官方页面为准。

## 功能概览

- **声音发现**：按名称搜索、分页浏览和试听声音，可通过声音 ID 查询后应用，并将常用声音标记为喜欢。
- **流式朗读**：文本分段发送，音频持续接收与播放，长文本无需等待整段合成完成。
- **音频格式**：默认使用 PCM；网络较慢时可选择 MP3，并设置比特率。
- **合成控制**：可调整模型、语速、音量、响度归一化、表现力、多样性和延迟模式。
- **账户与缓存**：查看和刷新 API 余额，管理所有声音入口共用的封面图片缓存。
- **界面语言**：简体中文、繁體中文、English、日本語、한국어、Русский、Türkçe。

## 安装

### 插件市场（推荐）

1. 打开 STranslate，进入 **设置 → 插件 → 市场**。
2. 搜索 **Fish Audio TTS**，点击下载安装。

### 手动安装

1. 从 [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) 下载 `STranslate.Plugin.Tts.FishAudio.spkg`。
2. 在 STranslate 的 **设置 → 插件 → 安装** 中导入安装包。

## 开始使用

1. 在插件设置中填写 [Fish Audio API Key](https://fish.audio/app/api-keys)。
2. 搜索并试听声音，点击勾号执行 **应用**；也可通过声音 ID 的放大镜查询后应用，或从 **喜欢** 中选用。已应用的叉号可取消选择。
3. 选择合成模型及音频格式，按需要调整参数，然后在 STranslate 中朗读文本。

## 用户指南

[阅读完整用户指南 →](docs/user-guide/README.md)

详细指引覆盖安装、初次配置、声音选择、全部配置项、PCM / MP3、网络与代理，以及常见问题排查。

[配置说明](docs/user-guide/README.md#configuration) · [音频格式](docs/user-guide/README.md#audio-formats) · [网络与代理](docs/user-guide/README.md#network-and-proxy) · [常见问题](docs/user-guide/README.md#troubleshooting)

## 常见问题

**Q: 不设置声音会扣费吗？**

A: 是否扣费取决于所选合成模型。未设置声音时 Fish Audio 会使用随机声音生成音频；使用付费模型仍会消耗 API 余额，免费模型以官方活动条件为准。

**Q: 试听会扣费吗？**

A: 不会。插件试听播放的是声音自带的公开音频，不会调用 TTS 接口；因此未配置 API Key 时也能进行试听。

**Q: 为什么播放后余额没有马上变化？**

A: Fish Audio 的余额扣除可能存在延迟。播放完成后立即刷新余额时，可能会短暂看到旧余额。

**Q: 声音搜索必须先配置 API Key 吗？**

A: 声音搜索、通过 ID 查询和试听都可以在未配置 API Key 时使用；真正合成语音需要有效的 API Key，使用付费模型还需要足够的 API 余额。

**Q: 清理缓存会影响已选择的声音吗？**

A: 不会。清理缓存只删除声音封面图片缓存；声音 ID 和已选择声音信息仍保留。后续再次展示时会重新加载封面。

## Fish Audio 官方资源

账号、声音社区、模型能力、参数用法与计费规则，请参考 Fish Audio 官方页面。

[官网](https://fish.audio) · [API Key](https://fish.audio/app/api-keys) · [声音社区](https://fish.audio/app/discovery) · [官方文档](https://docs.fish.audio) · [API 计费](https://docs.fish.audio/developer-guide/models-pricing/pricing-and-rate-limits)

## 开发

请先阅读[开发与贡献指南](CONTRIBUTING.md)，了解环境准备、必读文档、构建测试和提交要求。

快速验证：在 Windows 上使用 .NET 10 SDK 执行：

```powershell
.\build.ps1 -Release -Clean -Test
```

安装包输出到仓库根目录 `STranslate.Plugin.Tts.FishAudio.spkg`。

## 反馈与贡献

[提交问题与建议](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/issues) · [更新日志](CHANGELOG.md) · [参与贡献](CONTRIBUTING.md)

## 许可证

[MIT License](LICENSE)
