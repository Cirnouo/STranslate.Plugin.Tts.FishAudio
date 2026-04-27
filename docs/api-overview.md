# Fish Audio API 概览

本文档汇总插件使用的 Fish Audio API 端点、公共约定和实测结论。

## 基础信息

| 项目 | 值 |
|:--|:--|
| API 基址 | `https://api.fish.audio` |
| CDN 基址（图片/静态资源） | `https://public-platform.r2.fish.audio/` |
| 认证方式 | `Authorization: Bearer <API_KEY>` |
| API Key 获取 | [fish.audio/app/api-keys](https://fish.audio/app/api-keys) |
| 内容类型 | `application/json`（TTS 也支持 `application/msgpack`） |

## 插件使用的端点

| 端点 | 方法 | 用途 | 文档 |
|:--|:--:|:--|:--|
| `/v1/tts` | POST | 文本转语音 | [api-tts.md](api-tts.md) |
| `/wallet/self/api-credit` | GET | 查询账户余额 | [api-wallet-credit.md](api-wallet-credit.md) |
| `/model` | GET | 列出/搜索模型 | [api-list-models.md](api-list-models.md) |
| `/model/{id}` | GET | 获取模型详情 | [api-get-model.md](api-get-model.md) |

## 通用错误格式

所有 API 的错误响应均为 JSON：

```json
{ "status": <HTTP状态码>, "message": "<错误信息>" }
```

### 常见错误码

| 状态码 | 含义 | 实测 message |
|:--:|:--|:--|
| 401 | 未认证 / Key 无效 | `"Missing \`Authorization\` header"` |
| 402 | 余额不足 | `"Insufficient Balance"` |
| 404 | 资源不存在 | `"Model not found"` |
| 422 | 参数验证失败 | 数组格式，含 `loc`/`type`/`msg` |
| 429 | 速率限制 | （未实测） |

## 图片资源 URL 构建

API 返回的图片字段（`cover_image`、`author.avatar`）是**相对路径**：

```
完整 URL = CDN基址 + 相对路径
         = https://public-platform.r2.fish.audio/ + coverimage/{model_id}
```

### Cloudflare Image Resizing

在 CDN 路径中插入 `/cdn-cgi/image/width=W,format=auto/` 可缩放图片：

```
https://public-platform.r2.fish.audio/cdn-cgi/image/width=128,format=auto/coverimage/{model_id}
```

| 场景 | 建议宽度 | 典型大小 |
|:--|:--:|:--:|
| 模型列表缩略图 | 64~96 | ~5 KB |
| 模型详情封面 | 128~256 | ~12 KB |
| 作者头像 | 32~48 | ~1 KB |

## 音频资源 URL

| 来源 | URL 类型 | 是否可缓存 |
|:--|:--|:--:|
| 列表接口 `samples[].audio` | 公开 CDN 链接 `https://platform.r2.fish.audio/task/xxx.mp3` | 长期可缓存 |
| 详情接口 `samples[].audio` | 签名 R2 URL（含 `X-Amz-Expires=3600`） | 约 1h 过期 |
| TTS 响应 | 直接二进制流 | 不适用 |

## TTS 模型

| 模型 ID | 情绪语法 | 多说话人 | 语言数 | 说明 |
|:--|:--|:--:|:--:|:--|
| `s2-pro` | `[自然语言描述]` | 支持 | 80+ | **推荐** |
| `s1` | `(固定标签集)` | 不支持 | 13 | 旧款 |

## 实测延迟参考

以下数据为从中国大陆网络环境单次请求测量（仅供参考）：

| 端点 | 延迟 |
|:--|:--:|
| `/wallet/self/api-credit` | ~750ms |
| `/model` (list, 5条) | ~1900ms |
| `/model?title=丁真` (search) | ~830ms |
| `/model/{id}` (detail) | ~860ms |
| `/v1/tts` (402 error) | ~1400ms |

## 与 Vocu API 的核心差异

| 维度 | Vocu / 悟声 | Fish Audio |
|:--|:--|:--|
| TTS 响应 | JSON `{ audio: "url" }` | **原始二进制音频流** |
| 情绪控制 | API 字段（5 个滑块） | 文本内联标记 |
| 语速控制 | `speechRate` 字段 | `prosody.speed` 嵌套对象 |
| 模型选择 | `voiceId` + `promptId` | `reference_id` + `model` header |
| 站点 | 双站点 (vocu.ai / wusound.cn) | 单站点 (api.fish.audio) |
| 用户信息 | 完整（name/email/avatar/credits） | 仅余额 (`credit` 字符串) |
