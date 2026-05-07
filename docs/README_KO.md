<div align="center">
  <a href="https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio">
    <img src="images/icon.svg" alt="Fish Audio TTS" width="128" height="128" />
  </a>

  <h1>Fish Audio TTS</h1>

  <p>
    <a href="https://fish.audio">Fish Audio</a> 음성 합성 플러그인 for <a href="https://github.com/ZGGSONG/STranslate">STranslate</a>
  </p>

  <p>
    <img alt="License" src="https://img.shields.io/github/license/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Release" src="https://img.shields.io/github/v/release/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Downloads" src="https://img.shields.io/github/downloads/Cirnouo/STranslate.Plugin.Tts.FishAudio/total?style=flat-square" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" />
    <img alt="WPF" src="https://img.shields.io/badge/WPF-Plugin-blue?style=flat-square" />
  </p>

  <p>
    <a href="../README.md">简体中文</a> | <a href="README_TW.md">繁體中文</a> | <a href="README_EN.md">English</a> | <a href="README_JA.md">日本語</a> | <b>한국어</b>
  </p>
</div>

---

<div align="center">
  <img src="images/overview.png" alt="플러그인 개요" width="700" />
</div>


## 기능 개요

- **고품질 합성**: Fish Audio S2-Pro / S1 합성 모델을 지원하며 80개 이상의 언어를 다룹니다.
- **보이스 선택**: 이름으로 검색하거나 보이스 ID를 직접 입력할 수 있으며, 미리듣기, 선택, 지우기, 페이지 탐색을 지원합니다.
- **커버 캐시**: 보이스 `cover_image`를 자동으로 캐시해 반복 로드를 줄입니다. 설정에서 사용량을 확인하고 정리할 수 있습니다.
- **계정 확인**: API Key 검증 후 잔액과 "검증 및 적용됨" 상태를 표시해 설정이 적용되었는지 확인할 수 있습니다.
- **합성 제어**: 속도, 음량, 라우드니스 정규화, MP3 비트레이트, 표현력, 다양성, 지연 모드, 텍스트 정규화, 컨텍스트 연결을 지원합니다.
- **감정 마커**: S2-Pro의 `[laugh]` 또는 S1의 `(happy)` 같은 Fish Audio 감정 마커를 텍스트에 추가할 수 있습니다.
- **다국어 UI**: 简体中文, 繁體中文, English, 日本語, 한국어.

## 빠른 시작

### 1. 플러그인 설치

STranslate 플러그인 마켓에서 설치하는 방법을 우선 권장합니다. 마켓을 사용할 수 없다면 GitHub Release에서 `.spkg`를 다운로드해 수동 설치할 수 있습니다.

**방법 1: STranslate 플러그인 마켓**

1. STranslate를 엽니다.
2. **설정 -> 플러그인 -> 마켓**으로 이동합니다.
3. **Fish Audio TTS**를 검색하거나 찾아서 다운로드 및 설치합니다.
4. 설치 후 STranslate를 다시 시작하는 것을 권장합니다.

**방법 2: `.spkg` 수동 설치**

1. [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) 페이지로 이동합니다.
2. 최신 `STranslate.Plugin.Tts.FishAudio.spkg`를 다운로드합니다.
3. STranslate에서 **설정 -> 플러그인 -> 플러그인 설치**로 이동합니다.
4. 다운로드한 `.spkg` 파일을 선택하고 STranslate를 다시 시작합니다.

> [!TIP]
> `.spkg`는 본질적으로 ZIP 파일입니다. STranslate가 자동으로 압축을 풀고 로드하므로 직접 압축을 풀 필요가 없습니다.

### 2. API Key 받기

1. [Fish Audio API Keys](https://fish.audio/app/api-keys)에 로그인합니다.
2. API Key를 만들거나 복사합니다.

<!-- 스크린샷: images/fish-audio-api-keys.png
     내용: Fish Audio API Keys 페이지. API Key 생성/복사 위치를 강조하고 실제 API Key는 가리세요. -->
<div>
  <img src="images/fish-audio-api-keys.png" alt="Fish Audio API Keys 페이지" width="700" />
</div>

3. 플러그인 설정 페이지의 **API Key** 입력란에 붙여넣습니다.
4. 확인 버튼을 클릭하거나, 입력란에 포커스가 있을 때 `Enter`를 누릅니다.
5. **검증 및 적용됨**이 표시되고 계정 정보에 잔액이 보이면 현재 API Key가 플러그인에 적용된 것입니다.

<!-- 스크린샷: images/settings-account-api.png
     내용: 플러그인 설정 페이지의 계정 정보, API Key 입력란, 확인 버튼, 검증 및 적용됨 상태, 잔액. -->
<div>
  <img src="images/settings-account-api.png" alt="플러그인 계정 및 API Key 설정" width="360" />
</div>

### 3. API 잔액 구매

Fish Audio TTS는 Fish Audio API 잔액을 사용합니다. [Console -> Developer -> Billing -> Balance -> Purchase Credits](https://fish.audio/app/developers/billing/)에서 구매하거나 충전할 수 있습니다.

<!-- 스크린샷: images/fish-audio-billing.png
     내용: Fish Audio Billing/Balance/Purchase Credits 입구. 잔액 구매 또는 충전 위치를 강조하세요. -->
<div>
  <img src="images/fish-audio-billing.png" alt="Fish Audio API 잔액 구매 입구" width="700" />
</div>

> [!NOTE]
> 잔액 차감에는 지연이 있을 수 있습니다. 재생 직후 잔액을 새로고침하면 잠시 이전 잔액이 표시될 수 있습니다.

> [!TIP]
> `.edu` 이메일로 가입하고 학생 인증을 완료하면 Fish Audio 학생 크레딧을 받을 수 있습니다. 입구: [Fish Audio Students](https://fish.audio/students/).

### 4. 보이스 설정

보이스는 최종 읽기 음색을 결정합니다. 플러그인은 **검색**과 **ID로 설정** 두 가지 방법을 제공합니다.

> [!NOTE]
> 보이스를 설정하지 않아도 플러그인을 사용할 수 있습니다. 이 경우 플러그인은 Fish Audio에 `reference_id`를 보내지 않으며 Fish Audio가 무작위 보이스를 사용합니다. 무작위 보이스도 API 잔액을 소모합니다.

**방법 1: 이름으로 검색**

1. 보이스 영역의 **검색** 탭으로 전환합니다.
2. 입력란에 보이스 이름을 입력합니다.
3. 검색 아이콘을 클릭하거나 입력란에 포커스가 있을 때 `Enter`를 누릅니다.
4. 결과에서 보이스를 미리듣고, 마음에 드는 항목에서 **선택**을 클릭합니다.
5. 결과가 많으면 페이지 컨트롤로 페이지를 전환합니다.

<!-- 스크린샷: images/settings-voice-search.png
     내용: 검색 입력란, 검색 버튼, 검색 결과, 미리듣기 버튼, 선택 버튼, 페이지 컨트롤이 포함된 플러그인 보이스 검색 화면. -->
<div>
  <img src="images/settings-voice-search.png" alt="이름으로 보이스 검색 및 선택" width="450" />
</div>

**방법 2: 보이스 ID 사용**

1. Fish Audio 공식 사이트에서 대상 보이스 상세 페이지를 엽니다.
2. 펼침 메뉴에서 보이스 ID를 복사합니다.

<!-- 스크린샷: images/fish-audio-voice-id.png
     내용: Fish Audio 보이스 상세 페이지. 보이스 ID 위치를 강조하고 개인 계정 정보는 노출하지 마세요. -->
<div>
  <img src="images/fish-audio-voice-id.png" alt="Fish Audio 보이스 상세 페이지에서 보이스 ID 얻기" width="650" />
</div>

3. 플러그인 설정 페이지로 돌아가 **ID로 설정** 탭으로 전환합니다.
4. 보이스 ID를 붙여넣고 확인 버튼을 클릭하거나 입력란에 포커스가 있을 때 `Enter`를 누릅니다.
5. 플러그인이 ID를 검증하고 보이스 정보를 로드합니다.



<!-- 스크린샷: images/settings-voice-by-id.png
     내용: 보이스 ID 입력란, 붙여넣기 버튼, 확인 버튼이 포함된 ID 설정 화면. -->
<div>
  <img src="images/settings-voice-by-id.png" alt="보이스 ID로 보이스 설정" width="450" />
</div>

## 설정

플러그인 설정 페이지는 계정, 보이스, 모델, 오디오 출력, 운율, 생성 매개변수, 기타 영역으로 구성되어 있습니다.

### 계정 및 API

<!-- 스크린샷: images/settings-account-api.png
     내용: 계정 정보, API 주소, API Key 입력란, 검증 상태, 잔액, 새로고침 버튼. -->
<div>
  <img src="images/settings-account-api.png" alt="계정 및 API 설정" width="450" />
</div>

| 설정 항목 | 설명 |
| :-- | :-- |
| 계정 정보 | 현재 API Key에 해당하는 계정 잔액을 미국 달러 단위로 표시합니다. **새로고침**을 클릭하면 수동으로 갱신할 수 있습니다. |
| API 주소 | Fish Audio API 주소이며 현재 `https://api.fish.audio`로 고정되어 있습니다. |
| API Key | Fish Audio API 키입니다. 확인 버튼을 클릭하거나 `Enter`를 누른 뒤에만 검증 및 적용됩니다. |
| 검증 상태 | 서버 응답을 기다리는 동안 **응답 대기 중**을 표시합니다. 성공하면 **검증 및 적용됨**을 표시하고, 형식 오류는 입력란 옆에 표시됩니다. |

### 보이스

이름으로 보이스 검색

<!-- 스크린샷: images/settings-voice-search.png
     내용: 선택된 보이스 표시 영역 + 검색 탭. 커버, 제목, 작성자, 인기, 미리듣기, 선택, 지우기 동작을 표시. -->
<div>
  <img src="images/settings-voice-search.png" alt="보이스 검색 설정" width="450" />
</div>

ID로 보이스 가져오기

<!-- 스크린샷: images/settings-voice-by-id.png
     내용: ID 탭의 한 줄 입력란과 컨트롤. 검색 탭과 초기 높이가 일치하는 상태. -->
<div>
  <img src="images/settings-voice-by-id.png" alt="ID로 보이스 설정" width="450" />
</div>

| 설정 항목 | 설명 |
| :-- | :-- |
| 선택된 보이스 | 현재 적용된 보이스 정보를 표시합니다. 설정하지 않은 경우 **무작위 보이스**가 표시됩니다. |
| 검색 | 이름으로 Fish Audio 보이스를 검색하고, 미리듣기 후 선택할 수 있습니다. 검색 결과의 커버 이미지는 자동으로 캐시됩니다. |
| ID로 설정 | 이미 보이스 ID를 알고 있는 경우에 적합합니다. 보이스 ID는 32자의 소문자 16진수 문자열이어야 합니다. |
| 보이스 지우기 | 현재 보이스 설정을 지우고 무작위 보이스로 되돌립니다. |

### 모델 및 오디오 출력

<!-- 스크린샷: images/settings-model-audio.png
     내용: 합성 모델과 MP3 비트레이트 설정 카드. -->
<div>
  <img src="images/settings-model-audio.png" alt="모델 및 오디오 출력 설정" width="450" />
</div>

| 설정 항목 | 기본값 | 설명 |
| :-- | :--: | :-- |
| 합성 모델 | `s2-pro` | Fish Audio 합성 엔진을 선택합니다. `s2-pro`는 권장 옵션이며 80개 이상의 언어와 더 풍부한 감정 마커를 지원합니다. `s1`은 이전 모델 동작이 필요한 경우 사용할 수 있습니다. |
| MP3 비트레이트 | `192 kbps` | 출력 오디오 품질과 크기를 제어합니다. `64`, `128`, `192`를 선택할 수 있습니다. 비트레이트가 높을수록 보통 음질이 좋고 파일도 커집니다. |

### 운율

<!-- 스크린샷: images/settings-prosody.png
     내용: 속도, 음량, 라우드니스 정규화 설정 항목. -->
<div>
  <img src="images/settings-prosody.png" alt="운율 설정" width="450" />
</div>

| 설정 항목 | 기본값 | 설명 |
| :-- | :--: | :-- |
| 속도 | `1.0` | 읽기 속도를 제어하며 범위는 `0.5`부터 `2.0`까지입니다. `1.0`보다 작으면 느리고, 크면 빠릅니다. |
| 음량 | `0 dB` | 음량 오프셋을 제어하며 범위는 `-10 dB`부터 `+10 dB`까지이고 `0.1 dB` 정밀도를 지원합니다. |
| 라우드니스 정규화 | 켜짐 | `s2-pro` 모델에서만 표시되며 출력 라우드니스를 안정적으로 유지하는 데 도움이 됩니다. |

### 생성 매개변수

<!-- 스크린샷: images/settings-generation.png
     내용: 표현력, 다양성, 지연 모드, 텍스트 정규화, 컨텍스트 연결 설정 항목. -->
<div>
  <img src="images/settings-generation.png" alt="생성 매개변수 설정" width="450" />
</div>

| 설정 항목 | 기본값 | 설명 |
| :-- | :--: | :-- |
| 표현력 | `0.7` | 생성 샘플링 온도에 해당합니다. 값이 높을수록 표현 변화가 커지고, 낮을수록 출력이 안정적입니다. |
| 다양성 | `0.7` | `top_p`에 해당합니다. 값이 높을수록 샘플링 범위가 넓고, 낮을수록 결과가 수렴합니다. |
| 지연 모드 | 품질 우선 | 품질과 응답 속도 사이에서 선택합니다: 품질 우선, 균형, 저지연. |
| 텍스트 정규화 | 꺼짐 | 숫자, 단위 기호 등을 읽기에 더 적합한 텍스트로 자동 변환합니다. |
| 컨텍스트 연결 | 켜짐 | 이전 오디오를 컨텍스트로 사용해 긴 텍스트에서도 음성 일관성을 유지하는 데 도움을 줍니다. |

### 기타

<!-- 스크린샷: images/settings-misc-cache.png
     내용: 기타 영역의 캐시 사용량, 캐시 지우기 버튼, 처리 중 대기 표시. -->
<div>
  <img src="images/settings-misc-cache.png" alt="캐시 설정" width="450" />
</div>

| 설정 항목 | 설명 |
| :-- | :-- |
| 캐시 사용량 | 플러그인 캐시 디렉터리의 실제 `cover_images/*.jpg` 파일 크기를 스캔하고 B, KB, MB, GB 등의 단위로 자동 표시합니다. |
| 캐시 지우기 | 보이스 커버 이미지 캐시를 삭제합니다. 정리 중에는 버튼이 비활성화되고 회전 대기 표시가 나타나며, 완료 또는 시간 초과 후 다시 클릭할 수 있습니다. |

<details>
<summary><b>매개변수 목록</b> (클릭하여 펼치기)</summary>

| 매개변수 | 기본값 | 설명 |
| :-- | :--: | :-- |
| API Key | - | Fish Audio API 키, 필수입니다. |
| 보이스 ID | - (무작위 보이스) | 검색으로 선택하거나 직접 입력할 수 있습니다. 비어 있으면 무작위 보이스를 사용합니다. |
| 합성 모델 | `s2-pro` | `s2-pro` 또는 `s1`. |
| MP3 비트레이트 | `192 kbps` | `64`, `128`, `192`를 선택할 수 있습니다. |
| 속도 | `1.0` | 범위 `0.5`부터 `2.0`까지. |
| 음량 | `0 dB` | 범위 `-10 dB`부터 `+10 dB`까지. |
| 라우드니스 정규화 | 켜짐 | `s2-pro` 모델에서만 표시됩니다. |
| 표현력 | `0.7` | 범위 `0`부터 `1`까지. |
| 다양성 | `0.7` | 범위 `0`부터 `1`까지. |
| 지연 모드 | 품질 우선 | 품질 우선 / 균형 / 저지연. |
| 텍스트 정규화 | 꺼짐 | 숫자, 단위 기호 등을 읽기에 더 적합한 텍스트로 변환합니다. |
| 컨텍스트 연결 | 켜짐 | 이전 오디오를 컨텍스트로 사용해 음성 일관성을 유지합니다. |

</details>

## 감정 마커

Fish Audio는 텍스트 인라인 마커를 통해 감정을 제어합니다. 추가 API 매개변수는 필요하지 않습니다.

**S2-Pro**(권장)는 대괄호와 자연어 설명을 사용하며 텍스트 어디에나 배치할 수 있습니다:

```text
[angry] 이건 용납할 수 없어!
믿을 수 없어 [gasp] 정말 해냈구나 [laugh]
[whisper] 이건 비밀이야
```

**S1**은 소괄호와 고정 태그 세트를 사용하며 보통 문장 앞에 배치합니다:

```text
(happy) 오늘 날씨가 정말 좋다!
(sad)(whispering) 너무 보고 싶어.
```

## 자주 묻는 질문

**보이스를 설정하지 않으면 요금이 차감되나요?**

예. 보이스를 설정하지 않으면 Fish Audio가 무작위 보이스로 오디오를 생성하며 API 잔액이 차감됩니다.

**미리듣기는 요금이 차감되나요?**

아니요. 플러그인 미리듣기는 Fish Audio 보이스 항목에 포함된 공개 샘플 오디오를 재생할 뿐 TTS 합성 엔드포인트를 호출하지 않습니다. 그래서 API Key가 검증되지 않아도 미리듣기를 사용할 수 있습니다. 실제로 STranslate에서 플러그인으로 텍스트를 합성해 읽을 때만 유효한 API Key가 필요하고 잔액이 차감됩니다.

**재생 후 잔액이 바로 바뀌지 않는 이유는 무엇인가요?**

Fish Audio의 잔액 차감에는 지연이 있을 수 있습니다. 재생 직후 잔액을 새로고침하면 잠시 이전 잔액이 표시될 수 있습니다.

**보이스 검색 전에 API Key를 설정해야 하나요?**

보이스 검색, ID 조회, 미리듣기는 API Key가 검증되지 않아도 사용할 수 있습니다. 하지만 실제 음성 합성에는 유효한 API Key와 사용 가능한 잔액이 필요합니다.

**캐시를 지우면 선택된 보이스에 영향이 있나요?**

아니요. 캐시 지우기는 보이스 커버 이미지 캐시만 삭제합니다. 보이스 ID와 선택된 보이스 정보는 유지되며, 이후 다시 표시될 때 커버 이미지를 다시 로드합니다.

## 빌드

```powershell
# 표준 빌드 (Debug + .spkg 패키징)
.\build.ps1

# 클린 빌드
.\build.ps1 -Clean

# 클린 빌드 및 회귀 테스트 실행
.\build.ps1 -Clean -Test

# Release 빌드
.\build.ps1 -Configuration Release
```

빌드 결과물은 저장소 루트에 `STranslate.Plugin.Tts.FishAudio.spkg`로 출력됩니다.

<details>
<summary><b>환경 요구사항</b></summary>

- .NET 10.0 SDK
- Windows (WPF 프로젝트)

</details>

## 감사의 말

- [STranslate](https://github.com/ZGGSONG/STranslate) — 바로 사용할 수 있는 번역 및 OCR 도구
- [Fish Audio](https://fish.audio) — 음성 합성 API 제공업체
- [iNKORE WPF Modern UI](https://github.com/iNKORE-NET/UI.WPF.Modern) — WPF 모던 UI 컨트롤 라이브러리

## 라이선스

[MIT](../LICENSE)
