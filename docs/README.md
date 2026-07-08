# Agent Hub 사용 가이드 (정적 사이트)

이 폴더(`docs/`)는 GitHub Pages로 배포되는 **사용 가이드**입니다. 진입점은 `index.html`입니다.

## GitHub Pages 활성화 (최초 1회, 웹 UI)

1. GitHub 저장소 → **Settings → Pages**
2. **Source**: `Deploy from a branch`
3. **Branch**: `main`, 폴더: `/docs` → **Save**
4. 몇 분 후 게시됩니다: **https://yoojungyu.github.io/agent-hub/**

## SignPath homepage URL

- SignPath 프로젝트 설정의 **homepage URL**(필수 값)에 위 주소(`https://yoojungyu.github.io/agent-hub/`)를 입력합니다.

## 편집 방법 (중요)

- **`docs/index.html`이 정본(single source)입니다.** 자체 완결형(인라인 CSS/JS, 외부 리소스 없음)이라 파일 하나로 동작합니다.
- 앱 빌드 시 이 파일이 `install/Debug/View/Htmls/guide.html`로 **자동 복사**되어(csproj Content 링크), 앱에서도 `/guide.html`로 열립니다(모바일 PWA·PC 콘솔의 "사용법" 링크).
- 따라서 **수정은 `docs/index.html`만** 하면 GitHub Pages와 앱 양쪽에 반영됩니다.

## 참고

- 예시 화면은 앱 UI를 HTML/CSS로 재현한 애니메이션 목업입니다(실제 스크린샷 아님). 추후 실제 스크린샷/GIF로 교체할 수 있습니다.
- 가이드는 공개 콘텐츠이며 외부로 데이터를 전송하지 않습니다.
