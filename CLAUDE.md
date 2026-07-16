# CLAUDE.md

> **응답 언어(최우선 규칙):** 사용자에게 보내는 모든 응답은 **무조건 항상 한글**로 작성한다. 예외 없음. (코드·명령어·경로·식별자는 원문 유지 가능하나 설명 문장은 한글.)

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

> 출처: [andrej-karpathy-skills](https://github.com/multica-ai/andrej-karpathy-skills) — Andrej Karpathy의 LLM 코딩 관찰에 기반한 4대 원칙.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.

---

## Project-specific (Agent Hub)

프로젝트 개요는 [README.md](./README.md)를 참고. 이 저장소에서 위 원칙을 적용할 때 특히 유의할 점:

- **사용 가이드 동기화(필수)**: 사용자에게 보이는 기능/동작(모바일·PC 콘솔 UI, 인증서·터미널·알림·프롬프트 흐름, 기본값 등)을 추가·변경하면 **같은 작업에서 `docs/index.html` 사용 가이드도 반드시 갱신**한다. 가이드는 GitHub Pages이자 앱 `/guide.html`의 단일 소스다. 기능 변경 PR/커밋에 가이드 갱신이 누락되면 미완성으로 간주한다.
- **네임스페이스**: 자체 코드의 루트 네임스페이스는 `AgentHub`. 새 코드도 `AgentHub.*`를 따른다.
- **서드파티 코드**: `EmbedIO/`는 서드파티 라이브러리다. 내부 `EmbedIO` 네임스페이스와 소스는 **수정하지 않는다**(원칙 3: Surgical Changes).
- **인코딩 주의**: C# 소스와 리소스에 **한글(UTF-8)** 문자열이 포함되어 있다. 문자열 일괄 치환 시 인코딩을 훼손하지 않는 방식(Edit 도구 또는 바이트 단위 처리)을 사용한다. 텍스트를 다른 코드페이지로 재인코딩해 저장하지 말 것.
- **빌드/검증** (원칙 4): 변경 후 아래로 빌드가 통과하는지 확인한다.
  ```powershell
  msbuild AgentHub.sln /t:Restore
  msbuild AgentHub.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"
  ```
  산출물: `install/Debug/AgentHub.exe`.
