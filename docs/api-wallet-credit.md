# Fish Audio API — 查询账户余额

## 端点

```
GET https://api.fish.audio/wallet/self/api-credit
```

## 认证

| Header | 值 | 必需 |
|:--|:--|:--:|
| `Authorization` | `Bearer <API_KEY>` | 是 |

## 查询参数

| 参数 | 类型 | 默认值 | 说明 |
|:--|:--|:--:|:--|
| `check_free_credit` | bool | `false` | 是否检查免费额度 |
| `team_id` | string/null | `null` | 团队 ID（可选） |

> 路径中的 `{user_id}` 使用 `self` 即表示当前认证用户。

## 成功响应 (200)

```json
{
  "_id": "945251492f2343699a0ea5b027c411c6",
  "user_id": "1f651f170506433f9fc78abe23c74ff6",
  "credit": "0",
  "created_at": "2026-04-15T09:23:04.277000Z",
  "updated_at": "2026-04-15T09:23:04.277000Z",
  "has_phone_sha256": false,
  "has_free_credit": null
}
```

### 字段说明

| 字段 | 类型 | 说明 |
|:--|:--|:--|
| `_id` | string | 钱包记录 ID |
| `user_id` | string | 用户 ID |
| `credit` | **string** | 剩余额度（注意是字符串，非数字） |
| `created_at` | datetime | 创建时间 (ISO 8601) |
| `updated_at` | datetime | 更新时间 (ISO 8601) |
| `has_phone_sha256` | bool | 是否已绑定手机 |
| `has_free_credit` | bool/null | 是否有免费额度 |

## 错误响应

| 状态码 | 说明 | 响应体 |
|:--:|:--|:--|
| 401 | 未认证 | `{ "status": 401, "message": "..." }` |
| 422 | 参数验证失败 | 验证错误数组 |

## 插件用途

用于在设置面板中显示用户的 API 余额，以及验证 API Key 是否有效（连接测试）。

### 注意事项

- `credit` 字段是**字符串类型**，在显示前需适当格式化
- 该接口同时可用作 API Key 有效性验证和延迟测试
- 测试账户余额为 `"0"` 时，TTS 请求会返回 402
