# Fish Audio API — 列出/搜索模型

## 端点

```
GET https://api.fish.audio/model
```

## 认证

| Header | 值 | 必需 |
|:--|:--|:--:|
| `Authorization` | `Bearer <API_KEY>` | 是 |

## 查询参数

| 参数 | 类型 | 默认值 | 说明 |
|:--|:--|:--:|:--|
| `page_size` | int | `10` | 每页结果数（最小 1） |
| `page_number` | int | `1` | 页码（从 1 开始） |
| `title` | string/null | `null` | 按标题模糊搜索 |
| `tag` | string/string[] | `null` | 按标签筛选（可多选） |
| `self` | bool | `false` | 仅返回当前用户的模型 |
| `author_id` | string/null | `null` | 按作者 ID 筛选（`self=true` 时忽略） |
| `language` | string/string[] | `null` | 按语言筛选 (`zh`/`en`/`ja` 等) |
| `title_language` | string/string[] | `null` | 按标题语言筛选 |
| `sort_by` | string | `"score"` | 排序字段：`score`/`task_count`/`created_at` |

## 成功响应 (200)

```json
{
  "total": 1734711,
  "items": [
    {
      "_id": "54a5170264694bfc8e9ad98df7bd89c3",
      "type": "tts",
      "title": "丁真",
      "description": "This is a youthful male voice with a gentle, sincere...",
      "cover_image": "coverimage/54a5170264694bfc8e9ad98df7bd89c3",
      "train_mode": "fast",
      "state": "trained",
      "tags": ["male", "young", "social-media", "friendly", "calm"],
      "samples": [
        {
          "title": "Default Sample",
          "text": "大家好，我是丁真...",
          "task_id": "5f55a97cbbf84d059f8249c489c90fce",
          "audio": "https://platform.r2.fish.audio/task/5f55a97cbbf84d059f8249c489c90fce.mp3"
        }
      ],
      "created_at": "2024-06-30T09:20:09.288000Z",
      "updated_at": "2024-06-30T09:20:09.288000Z",
      "languages": ["zh"],
      "visibility": "public",
      "lock_visibility": false,
      "dmca_taken_down": false,
      "default_text": "大家好，我是丁真...",
      "quality": null,
      "like_count": 5360,
      "mark_count": 4124,
      "shared_count": 3682,
      "task_count": 396651,
      "unliked": false,
      "liked": false,
      "marked": false,
      "author": {
        "_id": "5c401ff6d0f1421288c1aeee4271903d",
        "nickname": "Katze Hi",
        "avatar": "avatars/5c401ff6d0f1421288c1aeee4271903d"
      }
    }
  ]
}
```

### ModelEntity 字段说明

| 字段 | 类型 | 说明 |
|:--|:--|:--|
| `_id` | string | 模型 ID（用作 TTS 的 `reference_id`） |
| `type` | string | `"tts"` 或 `"svc"` |
| `title` | string | 模型名称 |
| `description` | string | 模型描述 |
| `cover_image` | string | 封面图**相对路径**（见下方 URL 构建） |
| `train_mode` | string | `"fast"` 或 `"full"` |
| `state` | string | `created`/`training`/`trained`/`failed` |
| `tags` | string[] | 标签列表 |
| `samples` | SampleEntity[] | 试听样本列表 |
| `languages` | string[] | 支持语言 |
| `visibility` | string | `public`/`unlist`/`private` |
| `default_text` | string | 默认演示文本 |
| `like_count` | int | 点赞数 |
| `mark_count` | int | 收藏数 |
| `task_count` | int | 使用次数 |
| `liked` | bool | 当前用户是否已点赞 |
| `marked` | bool | 当前用户是否已收藏 |
| `author` | AuthorEntity | 作者信息 |

### SampleEntity 字段说明

| 字段 | 类型 | 说明 |
|:--|:--|:--|
| `title` | string | 样本标题 |
| `text` | string | 样本文本内容 |
| `task_id` | string | 任务 ID |
| `audio` | string | 音频 URL（**完整 URL**，可直接播放） |

> 列表接口中 `samples[].audio` 返回的是公开 CDN 短链接（如 `https://platform.r2.fish.audio/task/xxx.mp3`），
> 而详情接口 `/model/{id}` 返回的是带签名的 R2 URL（含 `X-Amz-Signature`，有效期约 1 小时）。

### AuthorEntity 字段说明

| 字段 | 类型 | 说明 |
|:--|:--|:--|
| `_id` | string | 作者 ID |
| `nickname` | string | 作者昵称 |
| `avatar` | string | 头像**相对路径**（见下方 URL 构建） |

## 图片 URL 构建

API 返回的 `cover_image` 和 `author.avatar` 是**相对路径**，需拼接 CDN 基址：

```
CDN 基址: https://public-platform.r2.fish.audio/
```

### 示例

| 相对路径 | 完整 URL |
|:--|:--|
| `coverimage/54a5170264694bfc8e9ad98df7bd89c3` | `https://public-platform.r2.fish.audio/coverimage/54a5170264694bfc8e9ad98df7bd89c3` |
| `avatars/5c401ff6d0f1421288c1aeee4271903d` | `https://public-platform.r2.fish.audio/avatars/5c401ff6d0f1421288c1aeee4271903d` |

### Cloudflare Image Resizing（可选）

支持通过 CDN 内联缩放图片，减少带宽：

```
https://public-platform.r2.fish.audio/cdn-cgi/image/width=128,format=auto/coverimage/{model_id}
```

实测数据：
- 原图 cover_image: **338 KB** (PNG)
- width=128 缩放: **12 KB** (auto format)
- 原图 avatar: **113 KB** (JPEG)
- width=64 缩放: **1.4 KB** (auto format)

## 搜索示例

### 按标题搜索
```
GET /model?title=丁真&page_size=10
```
实测返回 37 个匹配结果。

### 按语言筛选
```
GET /model?language=zh&page_size=10&sort_by=score
```
实测返回 415,704 个中文模型。

### 自有模型
```
GET /model?self=true&page_size=10
```
测试账户无自建模型，返回空列表。

## 错误响应

| 状态码 | 说明 |
|:--:|:--|
| 422 | 参数验证失败 |

## 插件用途

1. **模型搜索**：用户在设置面板中搜索和浏览公共语音模型
2. **模型选择**：展示模型封面、名称、作者、标签，用户选中后填入 `reference_id`
3. **试听**：通过 `samples[].audio` URL 直接播放模型样本
4. **本地缓存**：缓存模型列表和图片，减少重复请求
