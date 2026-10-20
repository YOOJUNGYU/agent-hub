/**
 * The session chat panel (native — no iframe): activity feed, prompt box,
 * question answers and permission approval for one session, over the same
 * /ws/agents socket the office uses.
 *
 * Protocol (AgentHub/Server/Socket/AgentMonitorModule.cs):
 *   → watch {sessionId} / unwatch            ← activity {sessionId, events}
 *   → inject {sessionId, text}               ← injectResult {ok, reason}
 *   ← elicit {id, questions, sessionId}      → elicitAnswer {id, answers: {question: "label" | "a, b"}}
 *     expired question (session.pendingAsk) → pickerAnswer {sessionId, indices, text, optionCount}
 *   ← permission {id, tool, detail, sessionId} → permissionDecision {id, decision: allow | deny}
 *     expired permission (pendingPermission)  → permissionInject {sessionId, choice: allow | allowAlways | deny}
 */
import type { HubEvent } from './hubTools.js';
import { mountTerminal } from './terminalView.js';

type Send = (msg: Record<string, unknown>) => void;

interface Question {
  question: string;
  header?: string;
  multiSelect?: boolean;
  options?: Array<{ label: string; description?: string }>;
}
interface Session {
  id: string;
  title?: string;
  project?: string;
  cwd?: string;
  engine?: string;
  working?: boolean;
  injectable?: boolean;
  /** Hidden terminal Agent Hub launched this session in (터미널 tab). */
  pty?: string | null;
  pendingAsk?: { question: string; header?: string; multiSelect?: boolean; options?: string[]; questionCount?: number } | null;
  pendingPermission?: { tool?: string; detail?: string } | null;
}

let send: Send = () => {};
const sessions = new Map<string, Session>();
const liveAsk = new Map<string, { id: string; questions: Question[] }>();
const livePerm = new Map<string, { id: string; tool?: string; detail?: string }>();

let current: string | null = null;
let events: HubEvent[] = [];
let sending = false;
let answeredExpired = ''; // the expired question just answered (don't re-show it while the snapshot catches up)
let ui: {
  title: HTMLElement;
  sub: HTMLElement;
  feed: HTMLElement;
  cards: HTMLElement;
  input: HTMLTextAreaElement;
  sendBtn: HTMLButtonElement;
  note: HTMLElement;
  tabs: HTMLElement;
  main: HTMLElement;
  term: HTMLElement;
} | null = null;
let termPty: string | null = null; // the terminal shown in the 터미널 tab
let disposeTerm: (() => void) | null = null;
let tab: 'chat' | 'term' = 'chat';

let onClose: () => void = () => {};

export function attachChat(fn: Send): void {
  send = fn;
}

/** The panel owner (hubUi) closes the panel when the chat's ✕ is pressed. */
export function onChatClose(fn: () => void): void {
  onClose = fn;
}

/** Text that is the harness talking, not the agent (system notices, tag blocks). */
export function cleanAgentText(text: string | undefined): string {
  const t = (text ?? '').replace(/<([a-z_-]+)>[\s\S]*?<\/\1>/gi, '').trim();
  return /^#\s*(Environment update|System|Session)/i.test(t) ? '' : t;
}

function h<K extends keyof HTMLElementTagNameMap>(tag: K, cls = '', text?: string): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text !== undefined) e.textContent = text;
  return e;
}

/** Agent text with just **bold** and `code` shown as such — built as DOM nodes, never innerHTML. */
function rich(cls: string, text: string): HTMLDivElement {
  const e = h('div', cls);
  for (const part of text.split(/(\*\*[^*\n]+\*\*|`[^`\n]+`)/)) {
    if (part.startsWith('**') && part.endsWith('**') && part.length > 4) e.append(h('strong', '', part.slice(2, -2)));
    else if (part.startsWith('`') && part.endsWith('`') && part.length > 2) e.append(h('code', '', part.slice(1, -1)));
    else if (part) e.append(part);
  }
  return e;
}

/** Build the chat UI inside the panel body (once). */
export function mountChat(container: HTMLElement): void {
  container.replaceChildren();
  const head = h('div', 'chat-head');
  const titles = h('div', 'chat-titles');
  const title = h('div', 'chat-title');
  const sub = h('div', 'chat-sub');
  titles.append(title, sub);
  const close = h('button', 'chat-close', '✕');
  close.type = 'button';
  close.title = '닫기 (Esc)';
  close.onclick = () => onClose();
  head.append(titles, close);
  const feed = h('div', 'chat-feed');
  const cards = h('div', 'chat-cards');
  const composer = h('form', 'chat-composer');
  const input = h('textarea', 'chat-input');
  input.rows = 2;
  input.placeholder = '이 세션에 보낼 프롬프트… (Ctrl+Enter로 보내기)';
  const sendBtn = h('button', 'chat-send', '보내기');
  sendBtn.type = 'submit';
  const note = h('div', 'chat-note');
  note.hidden = true;
  composer.append(input, sendBtn);
  // 대화 | 터미널 tabs (the terminal tab exists when Agent Hub runs the session in its hidden terminal)
  const tabs = h('div', 'chat-tabs');
  const main = h('div', 'chat-main');
  main.append(feed, cards, note, composer);
  const term = h('div', 'chat-term');
  term.hidden = true;
  container.append(head, tabs, main, term);
  composer.addEventListener('submit', (e) => {
    e.preventDefault();
    doInject();
  });
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      doInject();
    }
  });
  ui = { title, sub, feed, cards, input, sendBtn, note, tabs, main, term };
}

function setTab(next: 'chat' | 'term'): void {
  tab = next;
  if (!ui) return;
  ui.main.hidden = tab !== 'chat';
  ui.term.hidden = tab !== 'term';
  if (tab === 'term' && termPty && !disposeTerm) disposeTerm = mountTerminal(ui.term, termPty);
  renderTabs();
}

function renderTabs(): void {
  if (!ui) return;
  ui.tabs.replaceChildren();
  ui.tabs.hidden = !termPty;
  const items: Array<['chat' | 'term', string]> = current ? [['chat', '💬 대화'], ['term', '🖥 터미널']] : [['term', '🖥 터미널']];
  for (const [key, label] of items) {
    const b = h('button', `chat-tab ${tab === key ? 'on' : ''}`, label);
    b.type = 'button';
    b.onclick = () => setTab(key);
    ui.tabs.append(b);
  }
}

function attachTerminal(pty: string | null): void {
  if (pty === termPty) return;
  disposeTerm?.();
  disposeTerm = null;
  ui?.term.replaceChildren();
  termPty = pty;
  if (tab === 'term' && pty) setTab('term');
}

/** A terminal on its own, before its Claude/Codex session shows up (e.g. a first-run trust prompt). */
export function showTerminal(ptyId: string, title: string): void {
  showSession(null);
  attachTerminal(ptyId);
  if (ui) {
    ui.title.textContent = title;
    ui.sub.textContent = '세션이 시작되는 중 — 첫 화면에 묻는 것이 있으면 여기서 답해 주세요';
  }
  setTab('term');
}

/** Show a session (or null to close). */
export function showSession(sessionId: string | null): void {
  if (current && current !== sessionId) send({ type: 'unwatch' });
  current = sessionId;
  events = [];
  sending = false;
  attachTerminal(sessionId ? (sessions.get(sessionId)?.pty ?? null) : null);
  if (sessionId) setTab('chat');
  if (!ui) return;
  ui.feed.replaceChildren(h('div', 'chat-loading', '불러오는 중…'));
  ui.input.value = '';
  if (sessionId) send({ type: 'watch', sessionId });
  render();
}

/** Every Agent Hub socket message passes through here (HubTransport forwards them). */
export function onHubMessage(m: Record<string, unknown>): void {
  switch (m.type) {
    case 'sessions':
    case 'office':
      for (const s of (m.sessions as Session[]) ?? []) sessions.set(s.id, s);
      if (current) render();
      break;
    case 'activity':
      if (m.sessionId === current) {
        events = (m.events as HubEvent[]) ?? [];
        if (sending && events.some((e) => e.kind === 'user_prompt' && e.text?.trim() === ui?.input.dataset.pending)) {
          sending = false;
          if (ui) ui.input.value = '';
        }
        renderFeed();
      }
      break;
    case 'elicit':
      liveAsk.set(m.sessionId as string, { id: m.id as string, questions: (m.questions as Question[]) ?? [] });
      if (m.sessionId === current) render();
      break;
    case 'permission':
      livePerm.set(m.sessionId as string, { id: m.id as string, tool: m.tool as string, detail: m.detail as string });
      if (m.sessionId === current) render();
      break;
    case 'injectResult':
    case 'pickerAnswerResult':
    case 'permissionInjectResult':
      if (m.sessionId === current) onResult(m.type as string, m.ok === true, m.reason as string | undefined);
      break;
    case 'answerBlocked':
      flash('clawd-on-desk가 같은 질문을 가로채고 있어요. clawd를 끄고 다시 답해 주세요.', true);
      break;
    default:
      break;
  }
}

const REASON: Record<string, string> = {
  engine: '이 엔진은 직접 입력을 지원하지 않아요.',
  nopid: '세션 창을 찾지 못했어요 (Agent Hub 훅이 연결된 세션만 입력할 수 있어요).',
  noconsole: '세션 콘솔에 연결하지 못했어요 — 창이 닫혔는지 확인해 주세요.',
  failed: '입력에 실패했어요. 다시 시도해 주세요.',
};

function onResult(type: string, ok: boolean, reason?: string): void {
  if (type === 'injectResult') {
    if (!ok) {
      sending = false;
      flash(REASON[reason ?? 'failed'] ?? REASON.failed, true);
    } else {
      flash('보냈어요 — 세션이 받으면 피드에 나타나요.', false);
    }
    render();
    return;
  }
  flash(ok ? '콘솔에 답을 입력했어요.' : (REASON[reason ?? 'failed'] ?? REASON.failed), !ok);
}

function flash(text: string, bad: boolean): void {
  if (!ui) return;
  ui.note.textContent = text;
  ui.note.classList.toggle('bad', bad);
  ui.note.hidden = false;
  window.setTimeout(() => {
    if (ui && ui.note.textContent === text) ui.note.hidden = true;
  }, 6000);
}

function doInject(): void {
  const s = current ? sessions.get(current) : undefined;
  const text = ui?.input.value.trim();
  if (!ui || !s || !text || sending) return;
  sending = true;
  ui.input.dataset.pending = text;
  send({ type: 'inject', sessionId: s.id, text });
  render();
  window.setTimeout(() => {
    if (sending) {
      sending = false;
      render();
    }
  }, 12000);
}

// ── rendering ──────────────────────────────────────────────────────────────

function render(): void {
  if (!ui || !current) return;
  const s = sessions.get(current);
  ui.title.textContent = s?.title || s?.project || current;
  if (s?.pty && s.pty !== termPty) attachTerminal(s.pty);
  renderTabs();
  const where = s?.cwd?.startsWith('/') ? 'WSL' : 'Windows';
  ui.sub.textContent = `${s?.project ?? ''} · ${s?.engine === 'codex' ? 'Codex' : 'Claude'} · ${where} · ${s?.working ? '작업 중' : '대기 중'}`;
  ui.input.disabled = !s?.injectable;
  ui.sendBtn.disabled = !s?.injectable || sending;
  ui.sendBtn.textContent = sending ? '보내는 중…' : '보내기';
  if (!s?.injectable) ui.input.placeholder = '이 세션은 입력을 받을 수 없어요 (Agent Hub 훅이 연결된 콘솔 세션만 가능)';
  renderCards(s);
}

const ICON: Record<string, string> = {
  Read: '📄', Grep: '🔍', Glob: '🗂️', WebFetch: '🌐', WebSearch: '🔎', Edit: '✏️', Write: '✏️', MultiEdit: '✏️',
  Bash: '⌨️', exec: '⌨️', shell: '⌨️', apply_patch: '✏️', Task: '🤝', Agent: '🤝', TodoWrite: '📝',
};

function renderFeed(): void {
  if (!ui) return;
  const nearBottom = ui.feed.scrollHeight - ui.feed.scrollTop - ui.feed.clientHeight < 80;
  ui.feed.replaceChildren();
  if (events.length === 0) ui.feed.append(h('div', 'chat-loading', '아직 활동이 없어요'));
  for (const e of events) {
    const text = cleanAgentText(e.text);
    switch (e.kind) {
      case 'user_prompt':
        if (text) ui.feed.append(h('div', 'chat-msg me', text));
        break;
      case 'message':
        if (text) ui.feed.append(rich('chat-msg agent', text));
        break;
      case 'thinking':
        if (text) ui.feed.append(h('div', 'chat-think', `💭 ${text}`));
        break;
      case 'tool_use': {
        const chip = h('div', 'chat-tool');
        chip.append(h('span', 'ico', ICON[e.toolName ?? ''] ?? '🧩'), h('span', 'sum', e.target || e.summary || e.toolName || ''));
        ui.feed.append(chip);
        break;
      }
      case 'tool_result':
        if (e.summary) ui.feed.append(h('div', 'chat-result', `↳ ${e.summary}`));
        break;
      default:
        break;
    }
  }
  if (nearBottom || ui.feed.dataset.first !== current) ui.feed.scrollTop = ui.feed.scrollHeight;
  ui.feed.dataset.first = current ?? '';
}

function renderCards(s: Session | undefined): void {
  if (!ui || !current) return;
  ui.cards.replaceChildren();
  const perm = livePerm.get(current);
  if (perm) ui.cards.append(permissionCard(perm.tool, perm.detail, (allow) => {
    send({ type: 'permissionDecision', id: perm.id, decision: allow ? 'allow' : 'deny' });
    livePerm.delete(current!);
    flash(allow ? '허용했어요.' : '거부했어요.', false);
    render();
  }));
  else if (s?.pendingPermission) ui.cards.append(expiredPermissionCard(s));

  const ask = liveAsk.get(current);
  if (ask && ask.questions.length) ui.cards.append(questionCard(ask.questions, (answers) => {
    send({ type: 'elicitAnswer', id: ask.id, answers });
    liveAsk.delete(current!);
    flash('답했어요.', false);
    render();
  }));
  else if (s?.pendingAsk && s.injectable) {
    const pa = s.pendingAsk;
    if ((pa.questionCount ?? 1) > 1) ui.cards.append(h('div', 'chat-card', '여러 문항 질문은 PC 콘솔에서 답해 주세요.'));
    else if (answeredExpired !== `${pa.header}|${pa.question}`) {
      const q: Question = { question: pa.question, header: pa.header, multiSelect: pa.multiSelect,
        options: (pa.options ?? []).map((label) => ({ label })) };
      ui.cards.append(questionCard([q], (_answers, picked) => {
        send({ type: 'pickerAnswer', sessionId: s.id, indices: picked.indices, text: picked.text, optionCount: q.options!.length });
        answeredExpired = `${pa.header}|${pa.question}`;
        render();
      }));
    }
  }
}

function permissionCard(tool: string | undefined, detail: string | undefined, decide: (allow: boolean) => void): HTMLElement {
  const card = h('div', 'chat-card perm');
  card.append(h('b', '', `🔐 ${tool ?? '도구'} 실행을 허용할까요?`));
  if (detail) card.append(h('pre', 'chat-detail', detail));
  const row = h('div', 'chat-actions');
  const no = h('button', 'chat-btn', '거부');
  const yes = h('button', 'chat-btn primary', '허용');
  no.type = yes.type = 'button';
  no.onclick = () => decide(false);
  yes.onclick = () => decide(true);
  row.append(no, yes);
  card.append(row);
  return card;
}

function expiredPermissionCard(s: Session): HTMLElement {
  const card = h('div', 'chat-card perm');
  card.append(h('b', '', `🔐 권한 대기 중 — 콘솔로 답하기: ${s.pendingPermission?.tool ?? ''}`));
  if (s.pendingPermission?.detail) card.append(h('pre', 'chat-detail', s.pendingPermission.detail));
  const row = h('div', 'chat-actions');
  for (const [choice, label] of [['deny', '거부'], ['allowAlways', '항상 허용'], ['allow', '허용']] as const) {
    const b = h('button', `chat-btn ${choice === 'allow' ? 'primary' : ''}`, label);
    b.type = 'button';
    b.onclick = () => send({ type: 'permissionInject', sessionId: s.id, choice });
    row.append(b);
  }
  card.append(row);
  return card;
}

/** Multi-step question form; submits {question: "label" | "a, b"} plus the picked indices/text (expired path). */
function questionCard(
  questions: Question[],
  submit: (answers: Record<string, string>, picked: { indices: number[]; text: string | null }) => void,
): HTMLElement {
  const card = h('div', 'chat-card ask');
  const answers: Record<string, string> = {};
  let step = 0;
  const draw = () => {
    card.replaceChildren();
    const q = questions[step];
    const opts = q.options ?? [];
    card.append(h('div', 'chat-q-head', `❓ ${q.header || '질문'}${questions.length > 1 ? ` (${step + 1}/${questions.length})` : ''}`));
    card.append(h('div', 'chat-q', q.question));
    const chosen = new Set<number>();
    const list = h('div', 'chat-opts');
    const other = h('textarea', 'chat-input other');
    other.rows = 2;
    other.placeholder = '직접 입력';
    other.hidden = true;
    const OTHER = -1;
    const items: Array<[number, string, string?]> = [...opts.map((o, i) => [i, o.label, o.description] as [number, string, string?]), [OTHER, '기타 (직접 입력)']];
    for (const [i, label, desc] of items) {
      const b = h('button', 'chat-opt');
      b.type = 'button';
      b.append(h('span', 'lbl', label));
      if (desc) b.append(h('span', 'desc', desc));
      b.onclick = () => {
        if (!q.multiSelect) {
          chosen.clear();
          list.querySelectorAll('.chat-opt').forEach((x) => x.classList.remove('on'));
        }
        if (chosen.has(i)) chosen.delete(i);
        else chosen.add(i);
        b.classList.toggle('on', chosen.has(i));
        other.hidden = !chosen.has(OTHER);
        if (!other.hidden) other.focus();
      };
      list.append(b);
    }
    card.append(list, other);
    const row = h('div', 'chat-actions');
    if (step > 0) {
      const back = h('button', 'chat-btn', '이전');
      back.type = 'button';
      back.onclick = () => {
        step--;
        draw();
      };
      row.append(back);
    }
    const next = h('button', 'chat-btn primary', step === questions.length - 1 ? '답하기' : '다음');
    next.type = 'button';
    next.onclick = () => {
      const labels = [...chosen].filter((i) => i !== OTHER).map((i) => opts[i].label);
      const custom = chosen.has(OTHER) ? other.value.trim() : '';
      if (custom) labels.push(custom);
      if (labels.length === 0) return;
      answers[q.question] = q.multiSelect ? labels.join(', ') : labels[0];
      if (step < questions.length - 1) {
        step++;
        draw();
        return;
      }
      submit(answers, { indices: [...chosen].filter((i) => i !== OTHER), text: custom || null });
    };
    row.append(next);
    card.append(row);
  };
  draw();
  return card;
}
