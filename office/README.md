# office — Agent Hub의 픽셀 오피스

Agent Hub 화면의 "오피스"(세션 = 캐릭터)를 만드는 웹 앱입니다. `npm run build:hub` 결과가
`AgentHub/View/Htmls/office/`로 들어가 Agent Hub 서버가 `/office/`로 서빙합니다(빌드 산출물은 git 미추적, CI가 빌드).

```bash
cd office               # npm 워크스페이스 루트(core/·webview-ui/가 node_modules를 공유)
npm ci
npm run build:hub      # → ../AgentHub/View/Htmls/office
npm test               # 단위 테스트
```

## 구성

| 경로 | 내용 |
|---|---|
| `webview-ui/` | React + Canvas 오피스. Agent Hub 연동은 `src/transport/hubTransport.ts`(세션 → 캐릭터 번역), `src/hub/`(대화 패널·알림·새 에이전트·테마) |
| `core/src/` | 메시지 타입·에셋 유틸 (webview-ui가 참조) |
| `webview-ui/src/hub/vectorScene.ts` | **Agent Hub 화면 렌더러** — 바닥·벽·가구·고양이/강아지 캐릭터·펫을 캔버스 벡터로 직접 그린다(픽셀 스프라이트를 쓰지 않음) |
| `art/` | **모든 스프라이트를 코드로 생성** — `python3 art/generate.py`(캐릭터·가구·바닥·벽·펫), `python3 art/layout.py`(5개 방 레이아웃 + 좌석 도달성 검사) |
| `eslint-rules/` | 픽셀 UI 린트 규칙 |

## 방의 의미 (`art/layout.py`, `src/office/engine/officeState.ts`)

| 방 | 언제 |
|---|---|
| 📚 자료실 | 파일 읽기·검색, 웹 조회·검색 |
| 🗂 사무실 | 도구 없이 생각·답변 중, 할 일 정리, 서브에이전트 지시 |
| 🔧 작업실 | 코드 수정, 명령 실행 |
| 🔔 상담실 | 질문·권한 승인 대기(사용자를 기다림) |
| ☕ 휴게실 | 턴 사이 휴식(휴게실 안에서만 돌아다님) |

## 출처와 라이선스

- 오피스 엔진(렌더러·캐릭터 FSM·레이아웃 에디터 등)은 **[pixel-agents](https://github.com/pixel-agents-hq/pixel-agents)** (MIT, © Pablo De Lucca)의
  `webview-ui`·`core`를 가져와 Agent Hub용으로 고친 것입니다 — 원본 라이선스는 [`LICENSE`](LICENSE).
- **그림은 전부 Agent Hub 오리지널**입니다. 캐릭터(고양이·강아지 6종), 가구, 바닥, 벽, 펫, 카펫 PNG는 `art/`의 코드가 생성합니다
  (pixel-agents의 원래 아트팩 이미지는 포함하지 않습니다).
- 픽셀 폰트 `webview-ui/public/fonts/FSPixelSansUnicode-Regular.ttf`는 pixel-agents에 포함된 것을 그대로 씁니다.
