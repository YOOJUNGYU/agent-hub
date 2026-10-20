/**
 * A hidden terminal Agent Hub owns (ConPTY, AgentHub/Server/Terminal/PtyHost.cs), shown in the chat
 * panel's 터미널 tab: output streams over /ws/term?id=…, keystrokes and resizes go back.
 */
import { FitAddon } from '@xterm/addon-fit';
import { Terminal } from '@xterm/xterm';

const TOKEN_KEY = 'agenthub.deviceToken';

function cssVar(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

/** Attach a terminal for `ptyId` inside `host`; returns a disposer. */
export function mountTerminal(host: HTMLElement, ptyId: string, onExit?: () => void): () => void {
  const term = new Terminal({
    // eslint-disable-next-line pixel-agents/pixel-font -- a terminal needs a monospace font
    fontFamily: "Consolas, 'D2Coding', 'Cascadia Mono', monospace",
    fontSize: 13,
    cursorBlink: true,
    scrollback: 5000,
    theme: {
      background: cssVar('--color-bg-dark'),
      foreground: cssVar('--color-text'),
      cursor: cssVar('--color-accent'),
      selectionBackground: cssVar('--color-btn-hover'),
    },
  });
  const fit = new FitAddon();
  term.loadAddon(fit);
  term.open(host);

  const token = localStorage.getItem(TOKEN_KEY) ?? '';
  const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const ws = new WebSocket(`${proto}//${location.host}/ws/term?id=${encodeURIComponent(ptyId)}&token=${encodeURIComponent(token)}`);
  ws.binaryType = 'arraybuffer';
  const send = (msg: Record<string, unknown>) => {
    if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(msg));
  };
  const resize = () => {
    try {
      fit.fit();
    } catch {
      return; // host hidden (other tab) — fit again when shown
    }
    send({ type: 'resize', cols: term.cols, rows: term.rows });
  };
  ws.onopen = resize;
  ws.onmessage = (e) => {
    if (typeof e.data === 'string') {
      const m = JSON.parse(e.data) as { type?: string };
      if (m.type === 'exit') {
        term.write('\r\n\x1b[2m— 세션이 종료됐어요 —\x1b[0m\r\n');
        onExit?.();
      } else if (m.type === 'gone') term.write('\x1b[2m이 터미널은 더 이상 없어요 (Agent Hub를 다시 시작했나요?)\x1b[0m\r\n');
      return;
    }
    term.write(new Uint8Array(e.data as ArrayBuffer));
  };
  const input = term.onData((data) => send({ type: 'input', data }));
  const ro = new ResizeObserver(() => resize());
  ro.observe(host);

  return () => {
    ro.disconnect();
    input.dispose();
    ws.close();
    term.dispose();
  };
}
