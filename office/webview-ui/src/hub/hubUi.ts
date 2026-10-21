/**
 * Agent Hub chrome around the office (plain DOM, outside React so the office
 * code stays untouched): a top bar, the per-session chat panel and attention
 * toasts.
 *
 * The chat panel is Agent Hub's own session screen — index.html?embed=1&session=ID
 * (activity feed, prompt box, question answers, permission approval) — framed
 * from the same origin, so there is one implementation of every reply flow.
 * Styles: `.hub-*` in index.css.
 */
import { mountChat, onChatClose, showSession, showTerminal } from './chatPanel.js';

let root: HTMLDivElement | null = null;
let panel: HTMLDivElement | null = null;
let statusEl: HTMLSpanElement | null = null;
let toasts: HTMLDivElement | null = null;
let openSessionId: string | null = null;

function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className: string,
  parent?: HTMLElement,
): HTMLElementTagNameMap[K] {
  const e = document.createElement(tag);
  e.className = className;
  parent?.appendChild(e);
  return e;
}

function ensureUi(): void {
  if (root) return;
  root = el('div', 'hub-root', document.body);

  const bar = el('div', 'hub-bar', root);
  bar.hidden = window.top !== window; // inside the console its top bar carries brand + status
  el('span', 'hub-brand', bar).textContent = 'Agent Hub';
  statusEl = el('span', 'hub-status', bar);

  toasts = el('div', 'hub-toasts', root);

  panel = el('div', 'hub-panel', root);
  panel.hidden = true;
  panel.style.width = `${savedPanelWidth()}px`;
  makeResizable(panel);
  mountChat(el('div', 'hub-chat', panel));
  onChatClose(closeChat);
  window.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && openSessionId) closeChat();
  });
}

/** Theme shared with the Agent Hub console and chat (localStorage 'agenthub.theme', light by
 *  default). The console sits in the same origin, so its toggle reaches us as a storage event. */
const THEME_KEY = 'agenthub.theme';
function applyTheme(value: string | null): void {
  document.documentElement.setAttribute('data-theme', value === 'dark' ? 'dark' : 'light');
}
export function initHubTheme(): void {
  try {
    applyTheme(localStorage.getItem(THEME_KEY));
  } catch {
    applyTheme(null);
  }
  window.addEventListener('storage', (e) => {
    if (e.key === THEME_KEY) applyTheme(e.newValue);
  });
}

/** Framed by the Agent Hub console (/host): the console's top bar shows our status instead. */
const framed = window.top !== window;

// ── Chat panel width: drag its left edge (splitter); remembered per browser ──
const PANEL_WIDTH_KEY = 'agenthub.chatWidth';
const PANEL_MIN_PX = 320;
const PANEL_DEFAULT_PX = 480;

function savedPanelWidth(): number {
  const v = Number(localStorage.getItem(PANEL_WIDTH_KEY));
  return Number.isFinite(v) && v >= PANEL_MIN_PX ? v : PANEL_DEFAULT_PX;
}

function makeResizable(p: HTMLDivElement): void {
  const grip = el('div', 'hub-panel-grip', p);
  grip.title = '끌어서 너비 조절';
  grip.addEventListener('pointerdown', (e) => {
    e.preventDefault();
    // The framed chat would swallow pointer events mid-drag — cover it while dragging.
    const shield = el('div', 'hub-drag-shield', root!);
    const move = (ev: PointerEvent) => {
      const w = Math.min(window.innerWidth - 120, Math.max(PANEL_MIN_PX, window.innerWidth - ev.clientX));
      p.style.width = `${w}px`;
    };
    const up = () => {
      shield.remove();
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      try {
        localStorage.setItem(PANEL_WIDTH_KEY, String(parseInt(p.style.width, 10)));
      } catch {
        /* not persisted — width lasts for this page */
      }
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  });
}

export function setHubStatus(text: string, ok: boolean): void {
  ensureUi();
  if (framed) {
    parent.postMessage({ type: 'agenthub:officeStatus', text, ok }, location.origin);
    return;
  }
  statusEl!.textContent = text;
  statusEl!.classList.toggle('ok', ok);
}

/** Open (or switch) the chat panel to this session. */
export function openChat(sessionId: string): void {
  ensureUi();
  if (openSessionId !== sessionId) {
    showSession(sessionId);
    openSessionId = sessionId;
  }
  panel!.hidden = false;
  dismissToasts(sessionId);
}

/** Show a hidden terminal right after launching (its session appears in the office shortly). */
export function openTerminal(ptyId: string, title: string): void {
  ensureUi();
  showTerminal(ptyId, title);
  openSessionId = null;
  panel!.hidden = false;
}

export function closeChat(): void {
  if (!panel) return;
  panel.hidden = true;
  showSession(null);
  openSessionId = null;
}

export function isChatOpen(sessionId: string): boolean {
  return openSessionId === sessionId && !!panel && !panel.hidden;
}

/** A session needs the user (question / permission / input). Toast + OS notification when hidden. */
export function showAttention(sessionId: string, title: string, text: string, onOpen: () => void): void {
  ensureUi();
  if (isChatOpen(sessionId)) return; // already looking at it — the panel shows the form
  dismissToasts(sessionId);
  const t = el('button', 'hub-toast', toasts!);
  t.dataset.session = sessionId;
  el('b', '', t).textContent = title;
  el('span', '', t).textContent = text;
  t.addEventListener('click', () => {
    t.remove();
    onOpen();
  });
  if (document.hidden && 'Notification' in window && Notification.permission === 'granted') {
    try {
      const n = new Notification(`Agent Hub · ${title}`, { body: text, tag: `office-${sessionId}` });
      n.onclick = () => {
        window.focus();
        onOpen();
      };
    } catch {
      /* notifications unavailable (e.g. embedded browser) — the toast still shows */
    }
  }
}

export function dismissToasts(sessionId: string): void {
  toasts?.querySelectorAll<HTMLElement>(`.hub-toast[data-session="${CSS.escape(sessionId)}"]`).forEach((t) => t.remove());
}

// ── New agent (Claude Code / Codex in a new console on the PC, Windows or WSL) ──

interface LaunchOptions {
  distros: string[];
  recent: Array<{ cwd: string; env: 'wsl' | 'windows'; project: string }>;
}

const TOKEN_KEY = 'agenthub.deviceToken'; // same origin as Agent Hub's app.js
let launcher: HTMLDivElement | null = null;

function api(path: string, init?: RequestInit): Promise<Response> {
  const headers = new Headers(init?.headers);
  headers.set('X-Device-Token', localStorage.getItem(TOKEN_KEY) ?? '');
  return fetch(path, { ...init, headers });
}

export async function openLauncher(): Promise<void> {
  ensureUi();
  launcher?.remove();
  const back = el('div', 'hub-modal-back', root!);
  launcher = back;
  const box = el('form', 'hub-modal', back);
  el('h2', 'hub-modal-title', box).textContent = '새 에이전트';
  const close = () => {
    back.remove();
    launcher = null;
  };
  back.addEventListener('click', (e) => {
    if (e.target === back) close();
  });

  let opts: LaunchOptions = { distros: [], recent: [] };
  try {
    const r = await api('/api/agents/launch-options');
    if (r.ok) opts = (await r.json()) as LaunchOptions;
  } catch {
    /* options are suggestions only — the form still works */
  }

  const field = (label: string): HTMLLabelElement => {
    const l = el('label', 'hub-field', box);
    el('span', 'hub-field-label', l).textContent = label;
    return l;
  };
  const engine = el('select', 'hub-input', field('엔진'));
  for (const [v, t] of [
    ['claude', 'Claude Code'],
    ['codex', 'Codex'],
  ]) {
    const o = el('option', '', engine);
    o.value = v;
    o.textContent = t;
  }
  const where = el('select', 'hub-input', field('실행 환경'));
  const win = el('option', '', where);
  win.value = 'windows|';
  win.textContent = 'Windows';
  for (const d of opts.distros) {
    const o = el('option', '', where);
    o.value = `wsl|${d}`;
    o.textContent = `WSL · ${d}`;
  }
  const cwd = el('input', 'hub-input', field('작업 폴더'));
  cwd.placeholder = '비우면 홈 폴더';
  // 최근 작업 폴더(이 환경, 최대 10개) — 눌러서 바로 고른다. 같은 폴더에서 반복해 띄우는 경우가 대부분.
  const recentBox = el('div', 'hub-recent', box);
  const fillRecent = () => {
    recentBox.replaceChildren();
    const env = where.value.startsWith('wsl') ? 'wsl' : 'windows';
    const rows = opts.recent.filter((x) => x.env === env).slice(0, 10);
    recentBox.hidden = rows.length === 0;
    if (rows.length === 0) return;
    el('div', 'hub-recent-title', recentBox).textContent = '최근 작업 폴더';
    for (const r of rows) {
      const b = el('button', 'hub-recent-item', recentBox);
      b.type = 'button';
      b.title = r.cwd;
      b.classList.toggle('on', cwd.value === r.cwd);
      el('span', 'hub-recent-name', b).textContent = `📁 ${r.project || r.cwd}`;
      el('span', 'hub-recent-path', b).textContent = r.cwd;
      b.addEventListener('click', () => {
        cwd.value = r.cwd;
        fillRecent();
        prompt.focus();
      });
    }
  };
  where.addEventListener('change', fillRecent);
  cwd.addEventListener('input', fillRecent);
  const prompt = el('textarea', 'hub-input', field('첫 프롬프트 (선택)'));
  prompt.rows = 3;
  prompt.placeholder = '예: README를 읽고 이 프로젝트 구조를 요약해줘';
  fillRecent(); // after `prompt` exists — a picked folder moves focus there
  const msg = el('p', 'hub-modal-msg', box);
  const actions = el('div', 'hub-modal-actions', box);
  const cancel = el('button', 'hub-btn', actions);
  cancel.type = 'button';
  cancel.textContent = '취소';
  cancel.addEventListener('click', close);
  const go = el('button', 'hub-btn primary', actions);
  go.type = 'submit';
  go.textContent = '실행';

  box.addEventListener('submit', async (e) => {
    e.preventDefault();
    const [env, distro] = where.value.split('|');
    go.disabled = true;
    msg.textContent = '실행 중…';
    try {
      const r = await api('/api/agents/launch', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ engine: engine.value, env, distro, cwd: cwd.value, prompt: prompt.value }),
      });
      const res = (await r.json()) as { ok: boolean; message?: string; pty?: string };
      if (res.ok) {
        close();
        showNotice('새 에이전트를 시작했어요 — 곧 캐릭터가 출근합니다 🐾');
        if (res.pty) openTerminal(res.pty, `${engine.value === 'codex' ? 'Codex' : 'Claude Code'} · ${where.selectedOptions[0]?.textContent ?? ''}`);
      } else {
        msg.textContent = res.message ?? '실행하지 못했습니다.';
        go.disabled = false;
      }
    } catch {
      msg.textContent = 'Agent Hub에 연결하지 못했습니다.';
      go.disabled = false;
    }
  });
  engine.focus();
}

/** A short, self-dismissing note (no session attached). */
function showNotice(text: string): void {
  ensureUi();
  const t = el('div', 'hub-toast note', toasts!);
  el('span', '', t).textContent = text;
  setTimeout(() => t.remove(), 6000);
}

