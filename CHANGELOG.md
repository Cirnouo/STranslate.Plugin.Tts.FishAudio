# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

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
