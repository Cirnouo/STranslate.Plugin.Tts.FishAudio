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

- **고품질 합성**: Fish Audio S2-Pro / S1 합성 모델을 지원하며 80개 이상의 언어를 다룹니다
- **보이스 선택**: 이름으로 검색하거나 보이스 ID를 직접 입력할 수 있으며, 미리듣기, 선택, 지우기, 페이지 탐색을 지원합니다
- **합성 제어**: 속도, 음량, 라우드니스 정규화, MP3 비트레이트, 표현력, 다양성, 지연 모드, 텍스트 정규화, 컨텍스트 연결을 지원합니다
- **감정 마커**: S2-Pro의 `[laugh]` 또는 S1의 `(happy)` 같은 Fish Audio 감정 마커를 텍스트에 추가할 수 있습니다
- **현지화**: 简体中文, 繁體中文, English, 日本語, 한국어

## 빠른 시작

### 설치

STranslate 플러그인 마켓에서 설치하는 방법을 우선 권장합니다

**STranslate 플러그인 마켓**

1. STranslate를 엽니다
2. **설정 -> 플러그인 -> 마켓**으로 이동합니다
3. **Fish Audio TTS**를 검색하거나 찾아서 다운로드 및 설치합니다

**수동 설치**

1. [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) 페이지를 엽니다
2. 최신 `STranslate.Plugin.Tts.FishAudio.spkg`를 다운로드합니다
3. STranslate에서 **설정 -> 플러그인 -> 설치**를 엽니다
4. 다운로드한 `.spkg` 파일을 선택합니다

### API Key 받기

1. [Fish Audio API Keys](https://fish.audio/app/api-keys)에 로그인합니다
2. API Key를 만들거나 복사합니다

<!-- 스크린샷: images/fish-audio-api-keys.png
     내용: Fish Audio API Keys 페이지. API Key 생성/복사 위치를 강조하고 실제 API Key는 가리세요. -->
<div>
  <img src="images/fish-audio-api-keys.png" alt="Fish Audio API Keys 페이지" width="700" />
</div>

3. 플러그인 설정 페이지의 **API Key** 입력란에 붙여넣습니다
4. 확인 버튼을 클릭하거나, 입력란에 포커스가 있을 때 `Enter`를 누릅니다
5. **검증 및 적용됨**이 표시되고 계정 정보에 잔액이 보이면 현재 API Key가 플러그인에 적용된 것입니다

### API 크레딧 구매

Fish Audio TTS는 Fish Audio API 잔액을 사용합니다. [콘솔 > 개발자 > 청구 > 잔액 > 크레딧 구매](https://fish.audio/app/developers/billing/)에서 구매할 수 있습니다

<!-- 스크린샷: images/fish-audio-billing.png
     내용: Fish Audio Billing/Balance/Purchase Credits 입구. 잔액 구매 또는 충전 위치를 강조하세요. -->
<div>
  <img src="images/fish-audio-billing.png" alt="Fish Audio API 잔액 구매 입구" width="700" />
</div>

> [!NOTE]
> `.edu` 이메일로 가입하고 학생 인증을 완료하면 5달러 상당의 Fish Audio 학생 크레딧을 받을 수 있습니다. 입구: [Fish Audio Students](https://fish.audio/students/)

### 보이스 설정

보이스는 최종 읽기 음색을 결정합니다. 플러그인은 **검색**과 **ID 지정** 두 가지 방법을 제공합니다

> [!NOTE]
> 보이스를 설정하지 않아도 플러그인을 사용할 수 있습니다. 이 경우 플러그인은 Fish Audio에 `reference_id`를 보내지 않으며 Fish Audio가 무작위 보이스를 사용합니다. 무작위 보이스도 API 잔액을 소모합니다

**이름으로 검색**

1. 보이스 영역의 **검색** 탭으로 전환합니다
2. 입력란에 보이스 이름을 입력합니다
3. 검색 아이콘을 클릭하거나 입력란에 포커스가 있을 때 `Enter`를 누릅니다
4. 결과에서 보이스를 미리듣고, 마음에 드는 항목에서 **선택**을 클릭합니다
5. 결과가 많으면 페이지 컨트롤로 페이지를 전환합니다

**보이스 ID 사용**

1. Fish Audio 공식 사이트에서 대상 보이스 상세 페이지를 엽니다
2. 펼침 메뉴에서 보이스 ID를 복사합니다
3. 플러그인 설정 페이지로 돌아가 **ID 지정** 탭으로 전환합니다
4. 보이스 ID를 붙여넣고 확인 버튼을 클릭하거나 입력란에 포커스가 있을 때 `Enter`를 누릅니다
5. 플러그인이 ID를 검증하고 보이스 정보를 로드합니다

<!-- 스크린샷: images/fish-audio-voice-id.png
     내용: Fish Audio 보이스 상세 페이지. 보이스 ID 위치를 강조하고 개인 계정 정보는 노출하지 마세요. -->
<div>
  <img src="images/fish-audio-voice-id.png" alt="Fish Audio 보이스 상세 페이지에서 보이스 ID 얻기" width="650" />
</div>

## 설정

<details>
<summary><b>매개변수 목록</b> (클릭하여 펼치기)</summary>

| 매개변수 | 기본값 | 설명 |
| :-- | :--: | :-- |
| API Key | - | Fish Audio API 키, 필수입니다. |
| 보이스 ID | 무작위 보이스 | 검색으로 선택하거나 직접 입력할 수 있습니다. 비어 있으면 무작위 보이스를 사용합니다. |
| 합성 모델 | `s2-pro` | `s2-pro` 또는 `s1`. |
| MP3 비트레이트 | `192 kbps` | `64`, `128`, `192`를 선택할 수 있습니다. |
| 속도 | `1.0` | 범위 `0.5`부터 `2.0`까지. |
| 음량 | `0 dB` | 범위 `-10 dB`부터 `+10 dB`까지. |
| 라우드니스 정규화 | 켜짐 | `s2-pro` 모델에서만 표시됩니다. |
| 표현력 | `0.7` | 범위 `0`부터 `1`까지. |
| 다양성 | `0.7` | 범위 `0`부터 `1`까지. |
| 지연 모드 | 품질 우선 | 품질 우선 / 균형 / 저지연. |
| 텍스트 정규화 | 꺼짐 | 숫자, 단위 기호 등을 읽기에 더 적합한 텍스트로 자동 변환합니다. |
| 컨텍스트 연결 | 켜짐 | 이전 오디오를 컨텍스트로 사용해 긴 텍스트에서도 음성 일관성을 유지하는 데 도움을 줍니다. |

</details>

## 감정 마커

Fish Audio는 텍스트 안의 인라인 마커로 감정을 제어하며, 추가 API 파라미터는 필요하지 않습니다

**S2-Pro**(권장)는 대괄호와 자연어 설명을 사용하며 텍스트의 어느 위치에나 둘 수 있습니다

```text
[angry] 이건 받아들일 수 없어!
믿기지 않아 [gasp] 네가 정말 해냈다 [laugh]
[whisper] 이건 비밀이야
```

**S1**은 소괄호와 고정 태그 집합을 사용하며, 보통 문장 앞에 둡니다

```text
(happy) 오늘 날씨가 정말 좋다!
(sad)(whispering) 너무 보고 싶어
```

## 자주 묻는 질문

**Q: 보이스를 설정하지 않으면 요금이 청구되나요?**

A: 네. 보이스를 설정하지 않으면 Fish Audio가 무작위 보이스로 음성을 생성하며, 이 경우에도 API 잔액이 소모됩니다

**Q: 미리듣기도 과금되나요?**

A: 아니요. 플러그인의 미리듣기는 보이스에 포함된 공개 오디오를 재생할 뿐이며 TTS API를 호출하지 않습니다. 따라서 API Key를 검증하지 않아도 미리듣기를 사용할 수 있습니다

**Q: 재생 후 잔액이 바로 바뀌지 않는 이유는 무엇인가요?**

A: Fish Audio의 잔액 차감은 지연될 수 있습니다. 재생 직후 잔액을 새로고침하면 잠시 이전 잔액이 보일 수 있습니다

**Q: 보이스 검색 전에 API Key를 먼저 설정해야 하나요?**

A: 보이스 검색, ID로 조회, 미리듣기는 모두 유효한 API Key 없이 사용할 수 있습니다. 하지만 실제 음성 합성에는 유효한 API Key와 사용 가능한 잔액이 필요합니다

**Q: 캐시를 정리하면 선택한 보이스에 영향이 있나요?**

A: 아니요. 캐시 정리는 보이스 커버 이미지 캐시만 삭제합니다. 보이스 ID와 선택된 보이스 정보는 그대로 유지되며, 다음 표시 때 다시 로드됩니다

## 빌드

```powershell
# 표준 빌드 (Debug + .spkg 패키징)
.\build.ps1

# 정리 후 빌드
.\build.ps1 -Clean

# 정리 후 빌드 및 회귀 테스트 실행
.\build.ps1 -Clean -Test

# Release 빌드
.\build.ps1 -Configuration Release
```

빌드 산출물은 저장소 루트의 `STranslate.Plugin.Tts.FishAudio.spkg`로 출력됩니다

<details>
<summary><b>환경 요구 사항</b></summary>

- .NET 10.0 SDK
- Windows (WPF 프로젝트)

</details>

## 감사의 말

- [STranslate](https://github.com/ZGGSONG/STranslate) - 바로 사용할 수 있는 번역 및 OCR 도구
- [Fish Audio](https://fish.audio) - 음성 합성 API 제공자
- [iNKORE WPF Modern UI](https://github.com/iNKORE-NET/UI.WPF.Modern) - WPF용 모던 UI 컨트롤 라이브러리

## 라이선스

[MIT](../LICENSE)
