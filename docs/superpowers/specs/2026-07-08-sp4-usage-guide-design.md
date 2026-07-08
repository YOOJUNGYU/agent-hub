# SP4 (+SP5) — 사용 가이드 정적 페이지 (애니메이션 목업 + GitHub Pages)

- 작성일: 2026-07-08
- 상태: 설계 확정 대기 (사용자 리뷰 예정)
- 선행: SP1·SP2·SP3(완료). 본 문서는 계획상 SP4(사용 가이드)와 SP5(정적 홈페이지)를 **하나로 합침**.

## 1. 목적

모바일 접속·**인증서 등록(복잡, 비중 큼)**·기기 인증·세션 모니터·터미널·질문 알림 사용법을 **애니메이션 예시 화면**으로 설명하는 **자체 완결형 정적 페이지**를 만든다. 이 페이지를:
- 앱(모바일 PWA + PC 콘솔)에서 "사용법" 링크로 열 수 있게 하고,
- **GitHub Pages**로 배포해 **SignPath homepage URL**(`https://yoojungyu.github.io/agent-hub/`)로 쓴다.

## 2. 확정된 결정 (브레인스토밍)

- **예시 화면 = 앱 UI 재현 애니메이션 목업**: 실제 스크린샷 캡처는 헤드리스로 불가하므로, 가짜 폰 프레임 안에서 앱과 동일한 다크 테마·배지 스타일로 단계가 CSS/JS 전환되는 목업.
- **호스팅 = `docs/` 폴더(main 브랜치)** GitHub Pages.
- **단일 소스, 두 배포 타깃**: 정본 `docs/index.html`(자체 완결형: 인라인 CSS/JS, 외부 리소스 0). csproj가 이 파일을 앱 출력의 `View/Htmls/guide.html`로 **링크 복사** → 로컬에서 `/guide.html`로 서빙. 중복/드리프트 없음.
- GitHub Pages 활성화(리포 설정)와 SignPath homepage URL 입력은 **사용자 수동**(안내 제공).

## 3. 아키텍처 / 배포

```
docs/index.html  (정본, 자체 완결형)
  ├─ GitHub Pages (Settings → Pages → Source: main /docs) → https://yoojungyu.github.io/agent-hub/  → SignPath homepage
  └─ csproj <Content Include="..\docs\index.html" Link="View\Htmls\guide.html" CopyToOutputDirectory=Always>
       → install/Debug/View/Htmls/guide.html → EmbedIO가 /guide.html 로 서빙(로컬/LAN)
          ├─ 모바일 PWA index.html: "사용법" 링크 → /guide.html
          └─ PC 콘솔 host.html: "사용 안내" 링크 → /guide.html
```

- 자체 완결형이어야 로컬(`https://<lan-ip>:47600/guide.html`)과 Pages(`/agent-hub/`) 양쪽에서 경로 문제 없이 동작(모든 링크/자산 인라인 또는 상대경로 회피).
- 가이드는 공개 콘텐츠 — 기기 인증 게이트 없음(index.html처럼 정적 서빙).

## 4. 콘텐츠 구성 (섹션)

애니메이션 목업 폰 프레임 + 단계 내비게이션(자동 전환 또는 다음/이전). 각 섹션:

1. **접속** — 같은 와이파이, 콘솔의 접속 주소를 폰 브라우저에 입력, 보안 경고 통과("고급 → 이동").
2. **인증서 등록 (가장 비중 크게, 복잡)** — 왜 필요한지(자체 서명·PWA 설치·경고 제거) + **Android**(설정 → 보안 → 기기 저장공간에서 설치 → **CA 인증서** 선택 → 경고 → 다운로드 파일 선택; 파일 직접 탭은 실패함을 명시) / **iOS**(Safari 다운로드 → 프로파일 설치 → 인증서 신뢰 설정) **단계별 애니메이션**.
3. **기기 인증** — 폰에서 기기 이름 입력·요청 → PC 콘솔 기기 관리에서 승인 → 폰이 모니터로 전환.
4. **세션 모니터 / 상세** — 세션 카드(active/idle/ended), 탭 → 실시간 활동 피드(스크롤 동작 포함).
5. **웹 터미널(선택)** — PC에서 토글 ON → 폰에서 터미널로 claude 실행/답변(위험 안내 포함).
6. **질문 알림** — PC에서 훅 설치 → claude 입력 대기 시 폰 알림/배너 → 답변하기.

- **한/영 토글** — 앱과 동일하게 간단한 언어 전환(가이드 자체 내장, 앱 i18n과 독립적인 미니 사전).
- 상단에 다운로드/설치 링크(릴리스), 하단에 문의(이슈) 링크.

## 5. 시각/구현 방침

- 구현 시 **frontend-design 스킬**로 시각 완성도 확보(앱의 다크 팔레트·라운드·배지와 일관).
- 순수 정적: `<script>`는 인라인 vanilla JS(단계 전환/언어 토글/애니메이션 제어), 외부 폰트/CDN 금지(자체 완결). 이모지·인라인 SVG로 아이콘.
- 반응형: 데스크톱에서는 폰 프레임 + 설명 나란히, 모바일에서는 세로 스택.
- 접근성: 자동재생 애니메이션은 일시정지/수동 이동 가능, `prefers-reduced-motion` 존중.

## 6. GitHub Pages / SignPath 안내 (사용자 수동)

- `docs/README.md` 또는 리포 안내에: GitHub → Settings → Pages → Source `main` / 폴더 `/docs` 선택 → 몇 분 후 `https://yoojungyu.github.io/agent-hub/` 게시.
- SignPath 프로젝트의 homepage URL에 위 주소 입력.
- (Pages 활성화·SignPath 입력은 GitHub/SignPath 웹 UI 작업이라 사용자가 수행 — 스펙/플랜은 파일과 명확한 안내만 제공.)

## 7. 테스트 / 검증

- `node --check` 불필요(HTML). 대신 브라우저에서 열어 렌더 확인(사용자 수동), 단계 전환·언어 토글·인증서 섹션 동작.
- 링크 유효성: 앱 빌드 후 `install/Debug/View/Htmls/guide.html` 존재 확인, 모바일/host에서 `/guide.html` 링크 존재.
- 빌드 게이트: PowerShell msbuild 0 errors(csproj Content 링크 정상).
- Pages: 로컬에서 `docs/index.html`을 직접 열어도 동작(자체 완결) 확인.

## 8. 범위 밖 / 한계

- 실제 스크린샷/GIF: 헤드리스 캡처 불가 → 애니메이션 목업으로 대체(추후 사용자가 실사로 교체 가능).
- GitHub Pages 활성화·SignPath 입력: 웹 UI 수동 작업.
- 가이드 콘텐츠의 앱 i18n 통합: 가이드는 자체 미니 사전(앱 번들 밖 Pages에서도 동작해야 하므로).

## 9. 변경/신규 파일 (요약)

- 신규: `docs/index.html`(정본 가이드), 필요 시 `docs/README.md`(Pages/SignPath 안내).
- 수정: `AgentHub/AgentHub.csproj`(`..\docs\index.html`을 `View\Htmls\guide.html`로 Content 링크 복사), `AgentHub/View/Htmls/index.html`(모바일 "사용법" 링크), `AgentHub/View/Htmls/host.html`(PC 콘솔 "사용 안내" 링크), `js/i18n.js`(링크 라벨 키), `sw.js`(캐시 버전 상향 — 로컬 guide 캐시 원하면 ASSETS 추가는 선택).
- 서드파티 `EmbedIO/` 미수정. 새 NuGet 없음.
