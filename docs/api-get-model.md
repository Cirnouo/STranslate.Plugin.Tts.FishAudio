# Fish Audio API — 获取模型详情

## 端点

```
GET https://api.fish.audio/model/{id}
```

## 认证

| Header | 值 | 必需 |
|:--|:--|:--:|
| `Authorization` | `Bearer <API_KEY>` | 是 |

## 路径参数

| 参数 | 类型 | 说明 |
|:--|:--|:--|
| `id` | string | 模型 ID |

## 成功响应 (200)

返回与列表接口中 `items[]` 单条结构相同的 `ModelEntity` 对象。

```json
{
  "_id": "8d2c17a9b26d4d83888ea67a1ee565b2",
  "type": "tts",
  "title": "Valentino Narración Biblica Fer",
  "description": "A mature and authoritative male voice with a calm, spiritual tone...",
  "cover_image": "coverimage/8d2c17a9b26d4d83888ea67a1ee565b2",
  "train_mode": "fast",
  "state": "trained",
  "tags": ["male", "middle-aged", "narration", "calm", "serious"],
  "samples": [
    {
      "title": "Default Sample",
      "text": "Hermanos míos, recordemos las palabras del Señor...",
      "task_id": "40652a3ad6ea4ad88c791a9c3cb00401",
      "audio": "https://c97f3361a1c971323738e24f451a0225.r2.cloudflarestorage.com/fish-platform-data/task/40652a3ad6ea4ad88c791a9c3cb00401.mp3?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=...&X-Amz-Expires=3600&X-Amz-Signature=..."
    }
  ],
  "created_at": "2025-03-13T08:11:10.326000Z",
  "updated_at": "2025-03-13T08:11:10.326000Z",
  "languages": ["es"],
  "visibility": "public",
  "lock_visibility": false,
  "dmca_taken_down": false,
  "default_text": "Hermanos míos...",
  "quality": null,
  "like_count": 3976,
  "mark_count": 4314,
  "shared_count": 586,
  "task_count": 647418,
  "unliked": false,
  "liked": false,
  "marked": false,
  "author": {
    "_id": "1b82fb6a329c4462b67aa9ee0a42046f",
    "nickname": "Fernando Caicedo",
    "avatar": "avatars/1b82fb6a329c4462b67aa9ee0a42046f.jpg"
  }
}
```

### 与列表接口的差异

| 字段 | 列表接口 `/model` | 详情接口 `/model/{id}` |
|:--|:--|:--|
| `samples[].audio` | 公开 CDN 短链接 `https://platform.r2.fish.audio/task/xxx.mp3` | **签名 R2 URL**（含 `X-Amz-Signature`，约 1h 过期） |
| 其他字段 | 相同 | 相同 |

> 签名 URL 意味着详情接口的 `samples[].audio` 不适合长期缓存。
> 列表接口的公开 CDN 链接则可以直接缓存和播放。

## 错误响应

| 状态码 | 含义 | 响应体 |
|:--:|:--|:--|
| 404 | 模型不存在 | `{ "status": 404, "message": "Model not found" }` |
| 422 | 参数验证失败 | 验证错误数组 |

### 404 响应示例

```json
{
  "status": 404,
  "message": "Model not found"
}
```

## 不存在的模型 ID

当请求一个不存在的模型 ID 时（如随意输入的字符串），API 返回 404：

```
GET https://api.fish.audio/model/nonexistent_model_id_12345
```

```json
HTTP/1.1 404 Not Found

{
  "status": 404,
  "message": "Model not found"
}
```

插件在用户手动输入模型 ID 后，通过此接口验证模型是否存在。若返回 404 则弹出错误提示并清空当前模型缓存信息。

## 插件用途

1. **模型验证**：用户输入 `reference_id` 后（500ms 防抖），自动调用此接口验证模型是否存在
2. **模型详情展示**：在设置面板中显示所选模型的名称、封面、作者等信息
3. **试听**：通过 `samples[].audio` 播放模型样本音频
4. **本地缓存**：缓存模型详情，避免重复请求（注意签名 URL 有过期时间）

### 缓存策略建议

- 模型元数据（title/description/tags/cover_image）：可长期缓存
- `samples[].audio` URL：列表接口的 CDN 链接可长期缓存；详情接口的签名 URL 约 1h 过期
- 建议使用列表接口获取的 audio URL 进行缓存
