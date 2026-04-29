# Fish Audio API — 文本转语音 (TTS)

## 端点

```
POST https://api.fish.audio/v1/tts
```

## 认证

| Header | 值 | 必需 |
|:--|:--|:--:|
| `Authorization` | `Bearer <API_KEY>` | 是 |
| `Content-Type` | `application/json` | 是 |
| `model` | `s2-pro` 或 `s1` | 是 |

> `model` 是自定义 HTTP header，不在请求体中。推荐使用 `s2-pro`。

## 请求体 (JSON)

### 必填字段

| 字段 | 类型 | 说明 |
|:--|:--|:--|
| `text` | string | 待合成的文本（必填） |

### 语音模型

| 字段 | 类型 | 默认值 | 说明 |
|:--|:--|:--:|:--|
| `reference_id` | string/null | `null` | 语音模型 ID。不传则使用系统默认音色 |

> 实测：请求体完全省略 `reference_id` 时，`model: s2-pro` 仍可成功合成语音。

### 韵律控制 (`prosody` 对象)

| 字段 | 类型 | 默认值 | 范围 | 说明 |
|:--|:--|:--:|:--:|:--|
| `prosody.speed` | number | `1.0` | 0.5 ~ 2.0 | 语速倍率 |
| `prosody.volume` | number | `0` | — | 音量调整 (dB)，正值加大、负值减小 |
| `prosody.normalize_loudness` | bool | `true` | — | 响度归一化（仅 S2-Pro） |

### 音频输出

| 字段 | 类型 | 默认值 | 可选值 | 说明 |
|:--|:--|:--:|:--|:--|
| `format` | string | `"mp3"` | `wav` / `pcm` / `mp3` / `opus` | 输出音频格式 |
| `sample_rate` | int/null | `null` | 8000/16000/24000/32000/44100 | 采样率，null 时按格式默认 |
| `mp3_bitrate` | int | `128` | 64 / 128 / 192 | MP3 比特率 (kbps) |
| `opus_bitrate` | int | `-1000` | -1000/24/32/48/64 | Opus 比特率，-1000=自动 |

### 生成参数

| 字段 | 类型 | 默认值 | 范围 | 说明 |
|:--|:--|:--:|:--:|:--|
| `temperature` | number | `0.7` | 0 ~ 1 | 表现力控制，越高越多样 |
| `top_p` | number | `0.7` | 0 ~ 1 | 核采样多样性 |
| `chunk_length` | int | `300` | 100 ~ 300 | 文本分块长度（字符） |
| `normalize` | bool | `true` | — | 文本规范化（数字→文字等） |
| `latency` | string | `"normal"` | `low`/`normal`/`balanced` | 延迟-质量权衡 |
| `max_new_tokens` | int | `1024` | — | 每分块最大音频 token 数 |
| `repetition_penalty` | number | `1.2` | — | 重复惩罚，>1.0 减少重复 |
| `min_chunk_length` | int | `50` | 0 ~ 100 | 最小分块字符数 |
| `condition_on_previous_chunks` | bool | `true` | — | 利用前文音频保持一致性 |
| `early_stop_threshold` | number | `1.0` | 0 ~ 1 | 批处理早停阈值 |

### 完整请求示例

```json
{
  "text": "你好，这是一条 Fish Audio 语音合成测试。",
  "reference_id": "54a5170264694bfc8e9ad98df7bd89c3",
  "format": "mp3",
  "mp3_bitrate": 128,
  "sample_rate": 44100,
  "temperature": 0.7,
  "top_p": 0.7,
  "prosody": {
    "speed": 1.0,
    "volume": 0,
    "normalize_loudness": true
  },
  "chunk_length": 200,
  "normalize": true,
  "latency": "normal",
  "max_new_tokens": 1024,
  "repetition_penalty": 1.2
}
```

## 成功响应 (200)

**返回原始二进制音频数据**（不是 JSON），使用 `Transfer-Encoding: chunked`。

- Content-Type: 取决于请求的 `format`（如 `audio/mpeg` for mp3）
- 直接将响应体写入文件即可播放
- 响应体不包含音频 URL；TTS 结果不是 CDN/R2 链接

> 这是与 Vocu API 的核心差异：Vocu 返回 JSON 含音频 URL，Fish Audio 直接返回二进制音频流。

### 实测成功响应：省略 `reference_id`

请求条件：

- Header: `model: s2-pro`
- Body: 省略 `reference_id`
- Body: `format=mp3`, `mp3_bitrate=128`, `latency=normal`
- Text: `测试。`

响应：

| 项 | 值 |
|:--|:--|
| HTTP 状态 | `200` |
| `Content-Type` | `audio/mpeg` |
| `Transfer-Encoding` | `chunked` |
| `Content-Length` | 无 |
| 响应体 | MP3 二进制数据 |
| 实测大小 | `14209` bytes |
| MP3 起始字节 | `ff fb 90 c4 ...` |

### 余额扣除时机

实测一次省略 `reference_id` 的短文本 TTS 后，余额接口存在延迟更新：

| 查询时机 | `credit` |
|:--|:--|
| TTS 前 | `14.761455` |
| TTS 后约 3 秒 | `14.761455` |
| TTS 后约 60 秒 | `14.761320` |

结论：省略 `reference_id` 的 TTS 不是免费调用；余额扣除可能异步写入。插件若在播放完成后立即刷新余额，可能短时间内读到扣费前的旧余额。

## 错误响应

所有错误均返回 JSON：

| 状态码 | 含义 | 实测响应体 |
|:--:|:--|:--|
| 401 | 未认证 | `{ "status": 401, "message": "Missing \`Authorization\` header" }` |
| 402 | 余额不足 | `{ "status": 402, "message": "Insufficient Balance" }` |
| 422 | 参数验证失败 | `[{ "loc": [...], "type": "...", "msg": "..." }]` |
| 429 | 速率限制 | `{ "status": 429, "message": "..." }` （文档记录，未实测） |

## 情绪标记

Fish Audio 不通过 API 字段控制情绪，而是通过**文本内联标记**：

### S2-Pro（推荐）
使用 `[方括号]` + 自然语言描述，可放在文本任意位置：
```
我不敢相信 [gasp] 你真的做到了 [laugh]
[whisper] 这是一个秘密
[angry] 这不可接受！
```

### S1
使用 `(圆括号)` + 固定标签集（64+ 种），必须放在**句首**：
```
(happy) 今天天气真好！
(sad)(whispering) 我很想你。
```

## 模型对比

| 模型 | 情绪语法 | 多说话人 | 语言数 | 说明 |
|:--|:--|:--:|:--:|:--|
| `s2-pro` | `[自然语言]` | 支持 | 80+ | 推荐，最新 |
| `s1` | `(固定标签)` | 不支持 | 13 | 旧款 |

## 插件实现要点

1. **响应处理**：成功时为二进制流，错误时为 JSON。需先检查状态码
2. **二进制下载**：使用 `IHttpService.PostAsBytesAsync` 获取 `byte[]`，再通过 `IAudioPlayer.PlayAsync(byte[], ct)` 直接播放
3. **固定格式**：插件固定使用 `mp3` 格式、`192` kbps 比特率，不暴露给用户配置
4. **错误处理**：401/402/422 的响应体是 JSON，可解析 `message` 字段显示给用户
