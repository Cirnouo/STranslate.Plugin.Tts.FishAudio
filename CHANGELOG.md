# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Added
- 所有带确认语义的设置页输入框支持聚焦时按 Enter 执行确认：API Key 确认、声音名称搜索、声音 ID 提交；分页输入保留 Enter 提交
- API Key 验证新增内联等待/成功状态：等待响应中使用中性提示，验证成功显示"已验证并应用"
- `build.ps1` 新增 `-Test` 选项，可在构建和打包后顺序执行回归测试
- 新增本地 `.claude/rules/` 项目规则文件（本地配置，不纳入 Git 跟踪）
- 新增 API Key 状态流行为测试，覆盖启动验证成功提示和编辑草稿不清空已应用状态
- 新增声音封面图本地缓存：`cover_image` 以 `<声音 ID>.jpg` 保存到插件缓存目录 `cover_images`
- 设置页"其他"新增缓存占用显示和"清理缓存"按钮
- 新增封面缓存行为测试，覆盖命中、未命中创建、清理和大小格式化

### Changed
- 账户余额显示在数值后追加美元符号
- 声音搜索按钮改为仅显示放大镜图标，并通过悬停提示显示"搜索"
- 声音名称搜索占位文本改为"输入声音名称"
- 固定声音选择两个入口的初始高度，避免切换"搜索"/"通过 ID"时下方"模型"分组上下跳动
- 启动时已保存 API Key 验证成功后同样显示"已验证并应用"
- API Key 输入框编辑或失焦不再清空已应用提示和账户余额；只有触发确认提交时才清空旧状态并使用提交内容重新验证
- 搜索结果和已选声音封面图优先使用本地缓存，未命中时先显示 CDN 图片并在后台创建缓存
- 清理缓存入口由垃圾桶图标按钮改为显式文本按钮，提高可发现性
- README 更新为当前设置项和回归测试命令，推荐使用 `.\build.ps1 -Clean -Test`

## [1.0.2] - 2026-05-06

### Added
- 声音试听系统：带圆形进度环的试听按钮，使用 MediaPlayer 追踪真实播放进度
- 试听状态按声音 ID 同步，展示区和搜索结果中的同一声音始终显示相同的播放/停止状态和进度
- 支持试听中途切换：点击另一个声音的试听按钮即可无缝切换
- 声音选择区采用选项卡布局：搜索（默认）和通过 ID 两种入口二选一
- 通过 ID 面板：输入声音 ID 后点击"设为声音"按钮进行显式验证，支持粘贴按钮
- 展示区显示声音描述（description），限制两行高度
- 搜索结果卡片显示声音描述
- 搜索分页支持可编辑的页码输入，回车跳转到指定页
- 显示搜索结果总数（首次搜索前隐藏结果数和分页控件）
- API Key 粘贴按钮和确认按钮：输入框右侧新增从剪贴板粘贴和确认验证的图标按钮
- API Key 格式验证：点击确认按钮时检测 32 位十六进制格式，格式正确后调用 API 验证；格式错误内联显示，API 错误通过 Snackbar 显示（与刷新按钮一致）
- API Key 持久化仅通过确认按钮：验证通过写入配置，验证失败写入空值；Dispose 不再处理 API Key
- 声音搜索和通过 ID 查询不再依赖有效 API Key（无有效 Key 时使用 dummy token）
- 通过 ID 设置声音时增加 32 位十六进制格式校验，格式不正确时跳过 API 调用
- 新增可配置项：MP3 比特率（64/128/192 kbps）和上下文关联（condition_on_previous_chunks）
- 搜索分页页码输入框限制为纯数字输入，支持失焦提交（LostFocus 触发跳转）
- 延迟文本临时显示：刷新后显示 4 秒自动隐藏，带延迟图标
- 上一页/下一页按钮在边界页自动隐藏
- 点击空白区域可使文本框失焦（WPF 行为修正）
- `docs/DESIGN_DECISIONS.md`：采用 ADR 格式记录各业务行为的设计原因
- 5 语言新增多个本地化 key（Tab_Search, Tab_ById, Selected, NoResults, Prev, Next, SetVoice, ApiKey_InvalidFormat, ApiKey_Invalid, Paste, Confirm, VoiceId_InvalidFormat, ConditionOnPreviousChunks 等）

### Changed
- **术语重命名**：用户界面中"模型"→"声音"，"引擎"→"模型"
  - 分组标题：模型 → 声音，语音合成引擎 → 模型
  - 当前模型 → 已选声音，模型 ID → 声音 ID，搜索模型 → 搜索声音
  - 合成引擎 → 合成模型，随机模型 → 随机声音
  - 未找到该模型 → 未找到该声音
  - 响度归一化描述：仅 S2-Pro 引擎 → 仅 S2-Pro 模型
- 展示区布局重构：头像(48px) + 名称/描述/作者 + 试听按钮 + 清除按钮
- 声音 ID 输入从原来的内联 TextBox + 500ms 防抖自动查询改为独立面板 + 显式提交，去除冗余标签；确认按钮改为勾号图标
- 声音 ID 输入框字体改为系统默认（去除 Consolas），与搜索输入框保持视觉一致
- API Key 状态机从 5 状态枚举简化为单一 `bool IsApiKeyValid`，中间状态仅作临时报错提示
- API Key 验证从每次修改自动触发改为用户点击确认按钮时触发；确认按钮不再显示延迟
- 试听按钮停止图标从暂停（双竖线）改为终止（填充方块），匹配实际行为
- 分页控件从 DockPanel 改为 Grid 布局，页码指示器始终居中
- 搜索面板从可折叠改为选项卡切换（始终可见）
- 延迟模式选项文本：低延迟优先 → 低延迟（zh-cn/zh-tw/ko）；改用 XAML DynamicResource DataTrigger 替代 LatencyDisplayConverter，确保语言切换时实时更新
- 随机声音标题改用 XAML DynamicResource DataTrigger 替代 ViewModel GetTranslation，确保语言切换时实时更新
- 表现力描述更新：添加"越低一致性越强"
- 文本规范化描述更新：自动将数字、单位符号等内容转换为可读的文本
- 上下文关联描述更新：使用前序音频作为上下文以保持声音一致性（对齐 API 文档）
- 合成模型描述：S2-Pro 推荐 → 推荐 S2-Pro
- 音量配置项去除冗余描述
- API Key 不再在每次字符输入时自动保存，改为通过验证状态机控制
- 启动时仅在 API Key 格式有效时才发起信用额度检查
- CachedVoiceInfo（原 CachedModelInfo）新增 Description 字段
- VoiceSearchItem（原 ModelSearchItem）新增 Description、IsBeingPreviewed、PreviewProgress 字段，继承 ObservableObject
- SettingsViewModel 移除旧的 PlaySampleCommand/IsPlayingSample，改用基于 MediaPlayer 的试听系统
- 5 语言 README 和 plugin.json 描述更新为新术语
- CLAUDE.md / AGENTS.md 新增 Terminology 段落和更新的 Key Behaviors
- **内部重命名**：Settings 配置字段和 ViewModel 属性与新术语对齐
  - `Settings.ReferenceId` → `Settings.VoiceId`
  - `Settings.CachedModel` (CachedModelInfo) → `Settings.CachedVoice` (CachedVoiceInfo)
  - ViewModel: `CachedModelId/Title/Description/CoverUrl/Author/SampleUrl/TaskCount` → `CachedVoice*`
  - ViewModel: `ApplyCachedModel()` → `ApplyCachedVoice()`
- 新增配置迁移机制：`Settings.Migrate()` 在 `Main.Init()` 中检测旧字段名并自动迁移

### Removed
- `ApiKeyState` 枚举（5 状态），改用单一 `bool IsApiKeyValid`
- SettingsViewModel 中的 `EvaluateApiKeyFormat()`、`GetApiKeyStatusText()`、`CommitValidApiKey()` 方法
- `LatencyDisplayConverter`（被 XAML DataTrigger + DynamicResource 取代）
- SettingsViewModel 中的 _suppressModelLookup / _modelLookupCts / LookupModelAsync（被显式提交取代）
- IsSearchPanelVisible / ToggleSearchPanelCommand（被选项卡模式取代）
- PlaySampleCommand / IsPlayingSample（被试听系统取代）

## [1.0.1] - 2026-04-29

### Added
- 未设置模型 ID 时，当前模型卡片显示本地化的"随机模型"标题，替代空白状态
- 5 语言新增 `RandomModel` 本地化 key（zh-cn/zh-tw/en/ja/ko）
- 延迟模式显示名本地化（质量优先 / 平衡 / 低延迟优先），替代硬编码中文
- 当前模型卡片显示使用次数（TaskCount）
- 当前模型卡片增加试听按钮和清除按钮
- 模型 ID 输入框旁增加搜索图标按钮
- 模型搜索结果显示模型 ID、作者和使用次数
- README 全部 5 个语言版本新增 API 余额购买入口说明和 billing URL
- docs/api-tts.md 新增省略 reference_id 的实测结果和余额扣除延迟说明

### Changed
- 当前模型卡片布局重构：信息区（封面+文本）移至 Description，操作按钮移至右侧
- CachedModelInfo 精简：移除冗余字段 Id、AuthorAvatar、SampleText，新增 TaskCount
- ApplyCachedModel 判断条件从 Id 改为 Title
- CachedModelId 取自 ReferenceId 而非 CachedModelInfo.Id
- 清除按钮和使用次数可见性绑定从 CachedModelTitle 改为 CachedModelId
- 搜索结果卡片按钮增加 MinHeight/Padding 统一样式
- 刷新按钮与延迟文本布局调整（按钮在前，延迟在后）
- 截图更新为最新 UI
- plugin.json 版本号更新至 1.0.1

### Removed
- SettingsViewModel 中硬编码的 GetLatencyDisplayName 方法（改用 LatencyDisplayConverter + 本地化资源）
- FishAudioApi.BuildAvatarUrl 方法（未使用）
- ModelSearchItem 中的 SampleText、AuthorAvatar 字段（未使用）

## [1.0.0] - 2026-04-27

### Added
- Fish Audio TTS 语音合成（MP3 192kbps）
- S2-Pro / S1 引擎选择
- 模型搜索、浏览、试听与选择（分页，每页 6 条）
- 手动输入模型 ID 自动验证并获取模型信息（500ms 防抖）
- 韵律控制（语速、音量 0.1dB 精度、响度归一化）
- 生成参数调节（表现力、多样性、延迟模式、文本规范化）
- 延迟模式中文显示（质量优先 / 平衡 / 低延迟优先）
- 账户余额显示与 API 延迟检测
- 本地缓存选中模型信息
- 响度归一化仅在 S2-Pro 引擎时显示
- 5 种语言界面支持（zh-cn/zh-tw/en/ja/ko）
