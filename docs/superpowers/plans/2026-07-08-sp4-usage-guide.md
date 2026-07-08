# SP4 사용 가이드 정적 페이지 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax. The guide-authoring task (Task 1) MUST use the **frontend-design** skill for visual craft.

**Goal:** 자체 완결형 사용 가이드 `docs/index.html`(애니메이션 목업, 한/영)을 만들어 GitHub Pages(SignPath homepage)로 배포하고, 앱(모바일 PWA + PC 콘솔)에서 `/guide.html`로 링크한다.

**Architecture:** 정본 `docs/index.html`(인라인 CSS/JS, 외부 리소스 0). csproj가 이 파일을 앱 출력 `View/Htmls/guide.html`로 Content 링크 복사 → EmbedIO가 `/guide.html` 서빙. 모바일/host에서 링크. GitHub Pages는 `docs/`에서 배포.

**Tech Stack:** 순수 HTML/CSS/vanilla JS(프레임워크·빌드 툴 없음). 앱 측은 C#/.NET FW 4.8 + EmbedIO(기존). frontend-design 스킬로 시각 완성.

## Global Constraints

- 서드파티 `EmbedIO/` 수정 금지. 한글(UTF-8) 인코딩 훼손 금지. 새 NuGet·npm·빌드툴 금지.
- `docs/index.html`은 **자체 완결형**: 모든 CSS/JS 인라인, 외부 폰트/CDN/이미지 금지(이모지·인라인 SVG만). 로컬(`https://<lan-ip>:47600/guide.html`)과 Pages(`/agent-hub/`) 양쪽에서 경로 문제 없이 동작해야 하므로 절대경로 링크 회피(앱 다운로드/이슈 링크는 전체 URL 허용).
- 빌드: PowerShell msbuild `& "C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe" AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"` → 0 errors.
- 시각 품질은 헤드리스로 검증 불가 → **사용자 시각 검토가 최종 게이트**(Task 4). 자동 검증은 구조/자체완결성/콘텐츠 커버리지/빌드/링크 존재까지.
- 접근성: 자동재생 애니메이션은 일시정지/수동 이동 제공, `prefers-reduced-motion: reduce` 존중.
- 작업 브랜치: `feature/sp4-usage-guide`(생성됨, 스펙 커밋 존재). GitHub 저장소: `github.com/YOOJUNGYU/agent-hub` → Pages URL `https://yoojungyu.github.io/agent-hub/`.

---

### Task 1: `docs/index.html` 가이드 작성 (frontend-design)

**Files:** Create `docs/index.html`

**REQUIRED: frontend-design 스킬을 먼저 로드**해 팔레트/타이포/레이아웃 방침을 잡고 작성한다.

**요구 콘텐츠 (섹션, 순서):**
1. **헤더/히어로** — Agent Hub 소개 1~2문장, 다운로드(릴리스) 링크, 한/영 토글.
2. **접속** — 같은 와이파이, 콘솔 접속 주소를 폰 브라우저에 입력, 보안 경고 통과.
3. **인증서 등록 (가장 비중 크게)** — 왜 필요한지 + **Android**(설정 → 보안 → 기기 저장공간에서 설치 → **CA 인증서** → 경고 → 다운로드 파일 선택; "파일 직접 탭은 실패" 명시) / **iOS**(Safari 다운로드 → 프로파일 설치 → 인증서 신뢰 설정) 단계별 **애니메이션 목업**.
4. **기기 인증** — 폰 기기명 입력·요청 → PC 콘솔 승인 → 모니터 전환.
5. **세션 모니터/상세** — 카드(active/idle/ended) → 탭 → 활동 피드.
6. **웹 터미널(선택)** — PC 토글 ON → 폰에서 claude 실행/답변(위험 안내).
7. **질문 알림** — PC 훅 설치 → 입력 대기 시 폰 알림/배너 → 답변하기.
8. **푸터** — 이슈/문의 링크.

**요구 구현 방침:**
- **폰 목업 프레임 컴포넌트**: 가짜 폰 화면 안에 앱과 동일한 다크 팔레트(예: `#12141f`/`#181c2a`/`#7aa2ff`/배지색)로 각 단계 화면을 CSS로 재현. 단계 전환은 vanilla JS(다음/이전 + 자동재생 토글). Android/iOS 인증서 섹션은 OS 설정 화면을 단순 재현한 목업 프레임으로 단계 애니메이션.
- **한/영 토글**: 가이드 자체 미니 i18n(JS 객체 + `data-i18n` 유사 패턴 또는 두 언어 블록 토글). 앱 i18n과 독립(Pages에서도 동작해야 함).
- **반응형**: 데스크톱 = 목업 + 설명 나란히, 모바일 = 세로 스택. `max-width` 컨테이너, 가로 스크롤 없음.
- **자체 완결**: `<style>`/`<script>` 인라인. 외부 요청 0.
- 앱과 일관된 톤(다크, 라운드, 슬림). frontend-design으로 타이포 스케일·여백·색 대비(WCAG AA) 확보.

- [ ] **Step 1: frontend-design 스킬 로드 + 팔레트/레이아웃 방침 결정** (앱 기존 색과 일관).
- [ ] **Step 2: `docs/index.html` 작성** — 위 8개 섹션 + 폰 목업 + 단계 애니메이션 + 한/영 토글, 전부 인라인·자체완결.
- [ ] **Step 3: 자체완결성/구조 검증**
  - 외부 참조 0 확인: `grep -nE "https?://(cdn|fonts|unpkg|ajax)|src=\"http|href=\"http" docs/index.html` → 앱 릴리스/이슈 링크(github.com) 외 CDN/폰트/이미지 호스트 없음.
  - 필수 섹션 문자열 존재 확인(인증서/CA 인증서/기기 인증/터미널/알림, ko+en).
  - `prefers-reduced-motion` 미디어쿼리 존재, 애니메이션 일시정지/수동 이동 컨트롤 존재.
- [ ] **Step 4: 커밋**
```bash
git add docs/index.html
git commit -m "feat(sp4): 사용 가이드 정적 페이지(애니메이션 목업, 한/영, 자체완결)"
```

---

### Task 2: 앱에 링크 복사 + 모바일/PC 콘솔 링크

**Files:**
- Modify: `AgentHub/AgentHub.csproj` (docs/index.html → View/Htmls/guide.html Content 링크)
- Modify: `AgentHub/View/Htmls/index.html` (모바일 "사용법" 링크)
- Modify: `AgentHub/View/Htmls/host.html` (PC 콘솔 "사용 안내" 링크)
- Modify: `AgentHub/View/Htmls/js/i18n.js` (링크 라벨 ko/en)
- Modify: `AgentHub/View/Htmls/sw.js` (캐시 버전 상향)

- [ ] **Step 1: csproj Content 링크 복사**

`AgentHub/AgentHub.csproj` Content 그룹에 추가(프로젝트 밖 파일을 출력의 Htmls로 복사):
```xml
    <Content Include="..\docs\index.html">
      <Link>View\Htmls\guide.html</Link>
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
```

- [ ] **Step 2: 빌드 + 출력 복사 확인 (핵심 검증)**

PowerShell msbuild → 0 errors. **확인**: `install/Debug/View/Htmls/guide.html`가 존재하고 `docs/index.html`과 내용 동일한지.
> **폴백**: 만약 링크 Content가 출력의 `View\Htmls\guide.html`가 아니라 출력 루트로 복사되거나 누락되면(레거시 csproj의 프로젝트-밖 Link 복사 이슈), 대안으로 (a) `AgentHub/View/Htmls/guide.html`에 실제 사본을 두고 일반 Content로 등록 + `docs/index.html`은 별도 유지(동기화는 BeforeBuild 복사 타깃 `<Copy SourceFiles="$(ProjectDir)..\docs\index.html" DestinationFiles="$(ProjectDir)View\Htmls\guide.html" />`)로 자동화. 실측 결과에 따라 택1하고 report에 명시.

- [ ] **Step 3: 모바일 index.html 링크**

`index.html`의 인증서 footer(또는 헤더) 근처에 "사용법" 링크 추가(새 탭):
```html
      <a class="guide-link" href="/guide.html" target="_blank" rel="noopener" data-i18n="guide.link">📖 사용법 보기</a>
```

- [ ] **Step 4: PC 콘솔 host.html 링크**

`host.html` 헤더 또는 설정 탭에 "사용 안내" 링크:
```html
      <a class="guide-link" href="/guide.html" target="_blank" rel="noopener" data-i18n="guide.link">📖 사용 안내</a>
```

- [ ] **Step 5: i18n 라벨**

`js/i18n.js` ko: `'guide.link': '📖 사용법 보기'` / en: `'guide.link': '📖 User guide'` (양쪽 dict).

- [ ] **Step 6: sw 캐시 버전 상향** — `CACHE`를 다음 버전(`agent-hub-v9`)으로. (guide.html은 항상 네트워크로 두거나 ASSETS에 추가는 선택 — 자주 바뀌므로 캐시 제외 권장: sw fetch 예외에 `/guide.html` 추가 가능.)

- [ ] **Step 7: 검증** — `node --check` i18n.js → OK. PowerShell msbuild → 0 errors. `install/Debug/View/Htmls/guide.html` 존재. 모바일/host에 링크 존재(grep).

- [ ] **Step 8: 커밋**
```bash
git add AgentHub/AgentHub.csproj AgentHub/View/Htmls/index.html AgentHub/View/Htmls/host.html AgentHub/View/Htmls/js/i18n.js AgentHub/View/Htmls/sw.js
git commit -m "feat(sp4): guide.html 앱 번들 링크 + 모바일/PC 콘솔 사용법 링크"
```

---

### Task 3: GitHub Pages / SignPath 안내 문서

**Files:** Create `docs/README.md`

- [ ] **Step 1: `docs/README.md` 작성** — GitHub Pages 활성화 절차 + SignPath homepage 입력 안내:
```markdown
# Agent Hub 사용 가이드 (정적 사이트)

이 폴더(`docs/`)는 GitHub Pages로 배포되는 사용 가이드입니다. `index.html`이 진입점입니다.

## GitHub Pages 활성화 (최초 1회)
1. GitHub 저장소 → **Settings → Pages**
2. **Source**: `Deploy from a branch`
3. **Branch**: `main`, 폴더: `/docs` → **Save**
4. 몇 분 후 게시: **https://yoojungyu.github.io/agent-hub/**

## SignPath homepage URL
- SignPath 프로젝트 설정의 **homepage URL**에 위 주소를 입력합니다.

## 편집
- `docs/index.html`이 정본입니다. 앱 빌드 시 이 파일이 `View/Htmls/guide.html`로 복사되어 앱에서도 `/guide.html`로 열립니다. 수정은 이 파일만 하세요.
```

- [ ] **Step 2: 커밋**
```bash
git add docs/README.md
git commit -m "docs(sp4): GitHub Pages/SignPath 안내(docs/README)"
```

---

### Task 4: 검증 + 사용자 시각 검토 + 마무리

- [ ] **Step 1: 빌드 게이트** — PowerShell msbuild Restore+Build → 0 errors. `dotnet test` → 24/24(변화 없음 확인). `install/Debug/View/Htmls/guide.html` 존재.
- [ ] **Step 2: 자체완결/링크 최종 확인** — `docs/index.html`을 파일로 직접 열었을 때(그리고 `/guide.html`로 서빙 시) 외부 요청 없이 렌더되는지, 모바일/host 링크가 `/guide.html`로 연결되는지.
- [ ] **Step 3: 사용자 시각 검토(수동, 게이트)** — 브라우저(데스크톱+실제 폰)에서 `docs/index.html` 및 앱 `/guide.html` 열어 디자인 품질/단계 애니메이션/인증서 섹션 명확성/한·영 토글 확인. **품질이 성에 안 차면 여기서 프레임워크 전환 등 재작업 결정.**
- [ ] **Step 4: GitHub Pages 활성화 + SignPath 입력(사용자 수동)** — `docs/README.md` 절차대로.
- [ ] **Step 5: 브랜치 마무리** — `superpowers:finishing-a-development-branch`.

---

## Self-Review (계획 검증)

- **스펙 커버리지:** 가이드 콘텐츠(8섹션·인증서 강조·애니메이션 목업·한/영·자체완결)=Task1, 앱 링크 복사+모바일/PC 링크=Task2, Pages/SignPath 안내=Task3, 검증+사용자 시각 검토=Task4. 커버됨.
- **플레이스홀더:** 없음. 단 Task1의 가이드 HTML 전체 코드는 계획에 넣지 않고 **요구사항+frontend-design**으로 위임(디자인 산출물 특성상 적절 — WHAT/제약을 명시, HOW는 디자인 스킬).
- **리스크:** (1) 레거시 csproj의 프로젝트-밖 Link Content 복사 동작 → Task2 Step2에서 실측 + 폴백(BeforeBuild Copy 타깃) 명시. (2) 시각 품질은 헤드리스 검증 불가 → 사용자 검토 게이트(Task4 Step3)로 명확화, 재작업 저비용.
- **타입/링크 일관성:** `/guide.html` 경로, `guide.link` i18n 키, `docs/index.html` 정본이 Task 간 일치.
