# 코드 서명 설정 (SignPath, 오픈소스 무료)

Agent Hub 설치 파일(`AgentHub-win-Setup.exe`)을 코드 서명하여 Windows SmartScreen "알 수 없는 게시자" 경고를 줄이기 위한 절차입니다. 워크플로(`.github/workflows/release.yml`)에는 **서명 단계가 이미 추가**되어 있고, 아래 시크릿/변수가 설정되면 릴리스 시 자동으로 서명합니다. (설정 전에는 자동으로 건너뛰어 미서명으로 릴리스됩니다.)

> 서명 인증서는 로컬에서 즉시·무료로 만들 수 없습니다(자체서명은 SmartScreen에 도움 안 됨). SignPath Foundation은 **공개 오픈소스 프로젝트에 한해 무료**로 서명 인증서와 클라우드 서명을 제공합니다.

## 1단계 — SignPath 가입 및 Foundation 신청
1. https://signpath.io 에서 계정을 만듭니다.
2. **SignPath Foundation**(오픈소스 무료 프로그램)에 프로젝트를 신청합니다. 검토에 수일이 걸릴 수 있습니다.
   - 조건: 공개 저장소(현재 `github.com/YOOJUNGYU/agent-hub` = Public ✓), OSI 라이선스 권장.
   - (팁) 저장소에 `LICENSE` 파일이 있으면 승인에 유리합니다. 아직 없다면 라이선스를 추가하세요.

## 2단계 — SignPath 조직/프로젝트 구성
승인되면 SignPath 포털에서:
1. **Organization** 확인 → `Organization ID`(GUID) 기록.
2. **Project** 생성 → slug를 `agent-hub`로.
3. **Artifact Configuration** 생성 — 서명 대상이 단일 exe(`*Setup.exe`)임을 정의(단일 파일 서명 템플릿).
4. **Signing Policy** 생성 → slug 예 `release-signing`. (테스트용 `test-signing`도 가능)

## 3단계 — GitHub 연동
1. SignPath **GitHub App**을 저장소(`YOOJUNGYU/agent-hub`)에 설치합니다.
2. SignPath 포털에서 CI 연동용 **API Token**을 발급합니다.

## 4단계 — GitHub 시크릿/변수 등록
저장소 **Settings → Secrets and variables → Actions**에서:

**Secret (Secrets 탭):**
- `SIGNPATH_API_TOKEN` = 발급한 API 토큰

**Variable (Variables 탭):**
- `SIGNPATH_ORGANIZATION_ID` = 조직 GUID
- `SIGNPATH_PROJECT_SLUG` = `agent-hub`
- `SIGNPATH_POLICY_SLUG` = `release-signing` (2단계에서 만든 정책 slug)

## 5단계 — 릴리스
평소처럼 태그를 푸시하면 됩니다:
```bash
git tag v0.0.3
git push origin v0.0.3
```
`SIGNPATH_API_TOKEN`이 설정돼 있으면 워크플로가 **빌드 → 패키징 → SignPath 서명 → 릴리스 게시** 순으로 진행합니다.

## 참고
- SignPath Foundation 인증서는 조직 검증(OV) 방식입니다. 서명 후 게시자가 표시되지만, SmartScreen "평판"은 다운로드가 쌓이며 개선됩니다(즉시 100% 무경고는 EV 인증서에서만).
- 워크플로의 서명 단계 입력값(action 버전/슬러그)은 실제 SignPath 프로젝트 구성에 맞춰 미세 조정이 필요할 수 있습니다. `.github/workflows/release.yml`의 "SignPath" 단계를 참고하세요.
- 유료로 더 빠르고 안정적인 서명을 원하면 **Azure Trusted Signing**(~$10/월)도 Velopack이 지원합니다(`vpk pack --azureTrustedSign*`).
