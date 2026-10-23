<div align="center">
  <a href="https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio"><img src="assets/icon.svg" alt="Fish Audio TTS" width="80" height="80" /></a>
  <h1>Fish Audio TTS</h1>
  <p><a href="https://github.com/ZGGSONG/STranslate">STranslate</a>에서 <a href="https://fish.audio">Fish Audio</a> 음성 합성을 사용할 수 있습니다.</p>
  <p>
    <img alt="License" src="https://img.shields.io/github/license/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Release" src="https://img.shields.io/github/v/release/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Downloads" src="https://img.shields.io/github/downloads/Cirnouo/STranslate.Plugin.Tts.FishAudio/total?style=flat-square" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" />
    <img alt="WPF" src="https://img.shields.io/badge/WPF-Plugin-blue?style=flat-square" />
  </p>
  <p><a href="../README.md">简体中文</a> · <a href="README_TW.md">繁體中文</a> · <a href="README_EN.md">English</a> · <a href="README_JA.md">日本語</a> · <b>한국어</b></p>
</div>

> [!TIP]
> `.edu` 이메일로 Fish Audio에 가입하고 학생 인증을 완료하면 **미화 5달러 크레딧**을 무료로 받을 수 있습니다. [신청하기](https://fish.audio/students/)

> [!TIP]
> S2.1 Pro는 **2026-11-30 UTC**까지 한시적으로 무료입니다. 플러그인에서 `s2.1-pro-free`를 선택하세요. [프로모션 안내](https://fish.audio/blog/s2-1-pro-free-api/)

> [!IMPORTANT]
> S1은 **2026년 12월 31일**에 서비스가 종료됩니다. 이후 모델 `s1`을 사용하는 API 요청은 같은 요금으로 S2.1 Pro에서 처리됩니다. 그 전에 `s2.1-pro`로 전환하세요. [공식 안내](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live)

자격과 무료 프로모션 조건은 Fish Audio 공식 페이지를 따릅니다.

## 기능 소개

- **보이스 찾기**: 이름 검색, 페이지 탐색, 미리 듣기와 ID 조회 후 적용을 지원하며, 별로 자주 쓰는 보이스를 즐겨찾기에 저장합니다.
- **스트리밍 읽기**: 텍스트를 나누어 전송하면서 음성을 받아 재생하므로 긴 텍스트 전체의 합성을 기다릴 필요가 없습니다.
- **오디오 형식**: 기본값은 PCM이며, 느린 네트워크에서는 MP3와 비트레이트를 선택할 수 있습니다.
- **합성 제어**: 모델, 속도, 음량, 라우드니스 정규화, 표현력, 다양성, 지연 모드를 조정합니다.
- **계정 및 캐시**: API 잔액 확인·새로 고침과 모든 보이스 진입점이 공유하는 표지 이미지 캐시 관리를 지원합니다.
- **인터페이스 언어**: 简体中文, 繁體中文, English, 日本語, 한국어.

## 설치

### 플러그인 마켓 (권장)

1. STranslate에서 **설정 → 플러그인 → 마켓**을 엽니다.
2. **Fish Audio TTS**를 검색하여 다운로드하고 설치합니다.

### 수동 설치

1. [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases)에서 `STranslate.Plugin.Tts.FishAudio.spkg`를 받습니다.
2. STranslate의 **설정 → 플러그인 → 설치**에서 패키지를 가져옵니다.

## 시작하기

1. 플러그인 설정에 [Fish Audio API Key](https://fish.audio/app/api-keys)를 입력합니다.
2. 검색하고 미리 들은 뒤 체크로 **적용**합니다. ID의 돋보기로 조회하거나 **즐겨찾기**에서 고를 수 있습니다. 적용된 X 아이콘으로 선택을 해제합니다.
3. 합성 모델과 오디오 형식을 고르고 필요에 따라 조정한 다음 STranslate에서 텍스트를 읽습니다.

## 사용자 가이드

[전체 사용자 가이드 읽기 →](user-guide/README_KO.md)

설치, 초기 설정, 보이스 선택, 모든 설정 항목, PCM / MP3, 네트워크와 프록시, 문제 해결을 자세히 설명합니다.

[설정 안내](user-guide/README_KO.md#configuration) · [오디오 형식](user-guide/README_KO.md#audio-formats) · [네트워크 및 프록시](user-guide/README_KO.md#network-and-proxy) · [문제 해결](user-guide/README_KO.md#troubleshooting)

## 자주 묻는 질문

**Q: 보이스를 설정하지 않아도 요금이 부과되나요?**

A: 선택한 합성 모델에 따라 다릅니다. 보이스가 없으면 Fish Audio가 임의 보이스로 합성하며, 유료 모델은 API 크레딧을 사용합니다. 무료 모델은 공식 프로모션 조건을 따릅니다.

**Q: 미리 듣기에 크레딧이 사용되나요?**

A: 아니요. 보이스에 포함된 공개 오디오를 재생하며 TTS API를 호출하지 않습니다. API Key 없이도 미리 들을 수 있습니다.

**Q: 재생 후 잔액이 바로 바뀌지 않는 이유는 무엇인가요?**

A: Fish Audio의 차감 반영이 늦을 수 있습니다. 재생 직후 새로 고치면 잠시 이전 잔액이 보일 수 있습니다.

**Q: 보이스를 검색하려면 API Key가 필요한가요?**

A: 이름 검색, ID 조회, 미리 듣기는 API Key 없이 사용할 수 있습니다. 실제 음성 합성에는 유효한 API Key가 필요하며 유료 모델에는 충분한 API 잔액도 필요합니다.

**Q: 캐시를 지우면 선택한 보이스에도 영향이 있나요?**

A: 아니요. 보이스 표지 이미지 캐시만 삭제합니다. 보이스 ID와 선택 정보는 유지되며 다음 표시 때 표지를 다시 불러옵니다.

## Fish Audio 공식 자료

계정, 보이스 커뮤니티, 모델 기능, 매개변수 사용법과 요금은 Fish Audio 공식 페이지를 참고하세요.

[공식 사이트](https://fish.audio) · [API Key](https://fish.audio/app/api-keys) · [보이스 커뮤니티](https://fish.audio/app/discovery) · [공식 문서](https://docs.fish.audio) · [API 요금](https://docs.fish.audio/developer-guide/models-pricing/pricing-and-rate-limits)

## 개발

환경 준비, 필수 문서, 빌드·테스트와 제출 요건은 [개발 및 기여 가이드](../CONTRIBUTING.md)를 먼저 읽어 주세요.

Windows와 .NET 10 SDK에서 다음 명령으로 검증합니다.

```powershell
.\build.ps1 -Release -Clean -Test
```

패키지는 저장소 루트의 `STranslate.Plugin.Tts.FishAudio.spkg`에 출력됩니다.

## 피드백 및 기여

[문제 및 제안 제출](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/issues) · [변경 기록](../CHANGELOG.md) · [기여하기](../CONTRIBUTING.md)

## 라이선스

[MIT License](../LICENSE)
