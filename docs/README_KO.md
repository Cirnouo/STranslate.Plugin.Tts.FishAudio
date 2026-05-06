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

## 기능

- **고품질 합성** — Fish Audio S2-Pro / S1 모델 기반, 80개 이상의 언어 지원
- **보이스 검색** — 설정 패널에서 보이스 검색, 탐색, 미리듣기 및 선택, 페이지네이션 지원
- **보이스 미리듣기** — 진행률 링이 있는 미리듣기 버튼, 재생 중 전환 지원, 보이스 ID로 상태 동기화
- **커버 캐시** — 보이스 커버 이미지를 STranslate 플러그인 캐시에 저장하고, 사용량은 `cover_images`의 실제 `.jpg` 파일을 스캔해 계산하며 정리 중에는 회전 대기 표시로 반복 클릭을 방지
- **수동 입력 검증** — 보이스 ID를 직접 입력하면 검증 후 보이스 정보 로드
- **감정 마커** — 텍스트 인라인 마커로 음성 감정 제어 (S2-Pro: `[laugh]`, S1: `(happy)`)
- **운율 제어** — 속도 (0.5x~2.0x), 음량 (±10 dB, 0.1 dB 정밀도), 라우드니스 정규화
- **생성 매개변수** — 표현력, 다양성, 지연 모드 (품질 우선 / 균형 / 저지연), 텍스트 정규화, 컨텍스트 연결
- **계정 정보** — 잔액 및 API 지연 실시간 표시
- **다국어 UI** — 简体中文, 繁體中文, English, 日本語, 한국어

## 설치

1. [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) 페이지에서 최신 `.spkg` 파일 다운로드
2. STranslate에서 **설정 → 플러그인 → 플러그인 설치**로 이동
3. 다운로드한 `.spkg` 파일을 선택하고 STranslate 재시작

> [!TIP]
> `.spkg`는 본질적으로 ZIP 파일입니다. STranslate가 자동으로 압축을 풀고 로드합니다.

## 사전 요구사항

- [STranslate](https://github.com/ZGGSONG/STranslate) 최신 버전
- Fish Audio API Key ([여기에서 발급](https://fish.audio/app/api-keys))
- Fish Audio 계정 잔액 > 0

> [!NOTE]
> API 잔액 구매/충전: [콘솔 > 개발자 > 청구 > 잔액 > 크레딧 구매](https://fish.audio/app/developers/billing/). 잔액 차감이 지연될 수 있습니다. 재생 직후 잔액을 새로고침하면 이전 값이 일시적으로 표시될 수 있습니다.

> [!TIP]
> `.edu` 이메일로 가입하고 학생 인증을 완료하면 미화 5달러 무료 크레딧을 받을 수 있습니다. 입구: [Fish Audio Students](https://fish.audio/students/).

## 설정

<details>
<summary><b>매개변수 목록</b> (클릭하여 펼치기)</summary>

| 매개변수 | 기본값 | 설명 |
| :-- | :--: | :-- |
| API Key | — | Fish Audio API 키 (필수) |
| 보이스 ID | — (무작위 보이스) | 보이스 ID, 검색으로 선택하거나 직접 입력. 비어 있으면 무작위 보이스 (시스템 기본 음성) 사용 |
| 합성 모델 | `s2-pro` | `s2-pro` (권장) 또는 `s1` |
| 속도 | `1.0` | 0.5~2.0 |
| 음량 | `0 dB` | -10~+10 dB, 0.1 dB 정밀도 |
| 라우드니스 정규화 | 켜짐 | S2-Pro 모델 사용 시에만 표시 |
| MP3 비트레이트 | `192 kbps` | 64 / 128 / 192 kbps |
| 표현력 | `0.7` | 0~1, 높을수록 다양 |
| 다양성 | `0.7` | 0~1 |
| 지연 모드 | 품질 우선 | 품질 우선 / 균형 / 저지연 |
| 텍스트 정규화 | 꺼짐 | 숫자를 텍스트로 자동 변환 등 |
| 컨텍스트 연결 | 켜짐 | 이전 오디오를 컨텍스트로 사용해 음성 일관성 유지 |
| 커버 캐시 | 자동 | `cover_image`를 `<보이스 ID>.jpg`로 플러그인 캐시에 저장합니다. 사용량은 `cover_images/*.jpg`를 스캔해 계산하며, "기타"의 "캐시 지우기" 버튼으로 정리할 수 있고 처리 중에는 대기 표시가 나타납니다 |

</details>

## 감정 마커

Fish Audio는 텍스트 인라인 마커를 통해 감정을 제어합니다. 추가 API 매개변수가 필요하지 않습니다:

**S2-Pro** (권장) — 대괄호 + 자연어 설명, 텍스트 어디에나 배치 가능:
```
[angry] 이건 용납할 수 없어!
믿을 수 없어 [gasp] 정말 해냈구나 [laugh]
[whisper] 이건 비밀이야
```

**S1** — 소괄호 + 고정 태그 세트, 문장 앞에 배치:
```
(happy) 오늘 날씨가 정말 좋다!
(sad)(whispering) 너무 보고 싶어.
```

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
