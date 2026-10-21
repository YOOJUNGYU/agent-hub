/**
 * Agent Hub transport (`vite build --mode hub`).
 *
 * The office is served by Agent Hub at /office/ with no pixel-agents server
 * behind it. Sprites come pre-decoded from the build (browserMock), and live
 * state comes from Agent Hub's /ws/agents socket, translated here into the
 * ServerMessages the office already renders:
 *
 *   sessions / office snapshot  → agentCreated / agentClosed / agentStatus
 *   office activity (per session, last few events)
 *        tool_use / tool_result → agentToolStart / agentToolDone (room + reading card)
 *        message / thinking     → agentThought (thought bubble)
 *        user_prompt            → agentToolsClear (new turn)
 *   elicit / permission / pendingAsk / pendingPermission → permission bubble + toast
 *   ask                         → waiting bubble ("Waiting for input")
 *
 * Clicking a character (focusAgent) opens that session's Agent Hub chat panel
 * (hub/hubUi.ts), where prompts, question answers and approvals go through
 * Agent Hub's own session screen.
 */
import {
  TRANSPORT_STATE_CONNECTED,
  TRANSPORT_STATE_CONNECTING,
  TRANSPORT_STATE_RECONNECTING,
} from '../../../core/src/constants.js';
import type { ClientMessage, ServerMessage } from '../../../core/src/messages.js';
import { mockAssetMessages } from '../browserMock.js';
import { attachChat, cleanAgentText, onHubMessage as chatOnHubMessage } from '../hub/chatPanel.js';
import type { HubEvent } from '../hub/hubTools.js';
import { detailOf, isShellTool, READING_TOOLS, SUBAGENT_TOOLS, toOfficeTool } from '../hub/hubTools.js';
import { dismissToasts, initHubTheme, openChat, setHubStatus, showAttention } from '../hub/hubUi.js';
import { compose, loadKit, moveRoom, resizeRoom } from '../hub/rooms.js';
import type { MessageTransport, TransportState } from './types.js';

/** Agent Hub SessionSummary (AgentHub/Common/Models/SessionSummary.cs), camelCase JSON. */
interface HubSession {
  id: string;
  engine?: string;
  title?: string;
  project?: string;
  cwd?: string;
  status?: string; // active | idle | ended
  working?: boolean;
  pendingAsk?: { question?: string } | null;
  pendingPermission?: { tool?: string; detail?: string } | null;
  /** Agent Hub can type into this session's console (running + hook-reported PID alive). */
  injectable?: boolean;
}

interface Tracked {
  id: number;
  session: HubSession;
  label: string;
  working: boolean;
  /** Event keys already translated (the current activity window only). */
  seen: Set<string>;
  openToolId: string | null;
  needsUser: boolean;
  /** Sub-characters (sub-agents and background shells) by their parent tool_use id. */
  subs: Map<string, { seen: Set<string>; openToolId: string | null; shell: boolean }>;
  /** Background task id → the tool_use that started it (task_done ends that sub-character). */
  bgTasks: Map<string, string>;
}

/** A live sub-agent of a session (office message `subagents`). */
interface HubSubagent {
  toolUseId: string;
  description?: string;
  agentType?: string;
  events: HubEvent[];
}

const TOKEN_KEY = 'agenthub.deviceToken'; // shared with Agent Hub's app.js (same origin)
const THOUGHT_MAX = 400;
const RECONNECT_MS = 3000;

export class HubTransport implements MessageTransport {
  private handlers: Array<(m: ServerMessage) => void> = [];
  private stateHandlers: Array<(s: TransportState) => void> = [];
  private _state: TransportState = TRANSPORT_STATE_CONNECTING;
  private readonly _ready: Promise<void>;
  private resolveReady!: () => void;
  private ws: WebSocket | null = null;
  private disposed = false;

  private bySession = new Map<string, Tracked>();
  private bySessionNumber = new Map<number, Tracked>();
  private nextId = 1;
  /** Office React side has mounted (webviewReady) — before that, keep state but emit nothing. */
  private officeReady = false;
  private lastSessions: HubSession[] = [];
  private lastActivity: Record<string, HubEvent[]> = {};
  private lastSubagents: Record<string, HubSubagent[]> = {};
  /** ?open=<sessionId>: open that chat once the session shows up (tray notification click). */
  private openOnLoad = new URLSearchParams(location.search).get('open');

  private readonly kitReady = loadKit();

  constructor() {
    initHubTheme();
    attachChat((msg) => {
      if (this.ws?.readyState === WebSocket.OPEN) this.ws.send(JSON.stringify(msg));
    });
    this._ready = new Promise((r) => (this.resolveReady = r));
    this.connect();
    // RoomOverlay asks to move a room (name plate dragged): recompose and re-send the layout.
    window.addEventListener('agenthub:moveRoom', (e) => {
      const { label, x, y } = (e as CustomEvent<{ label: string; x: number; y: number }>).detail;
      void this.kitReady.then((kit) => {
        if (kit && moveRoom(label, x, y)) this.emit({ type: 'layoutLoaded', layout: compose(kit) });
      });
    });
    // The host page (/host) relays the tray's "open this session" to an already-open window.
    window.addEventListener('message', (e) => {
      const d = e.data as { type?: string; sessionId?: string } | null;
      if (e.origin !== location.origin || d?.type !== 'agenthub:open' || !d.sessionId) return;
      const t = this.bySession.get(d.sessionId);
      if (t && this.officeReady) this.openSession(t);
      else this.openOnLoad = d.sessionId; // not here yet — open when it shows up
    });
    // …or to resize it (corner handle dragged).
    window.addEventListener('agenthub:resizeRoom', (e) => {
      const { label, w, h } = (e as CustomEvent<{ label: string; w: number; h: number }>).detail;
      void this.kitReady.then((kit) => {
        if (kit && resizeRoom(label, w, h)) this.emit({ type: 'layoutLoaded', layout: compose(kit) });
      });
    });
  }

  get state(): TransportState {
    return this._state;
  }
  get ready(): Promise<void> {
    return this._ready;
  }

  onMessage(handler: (m: ServerMessage) => void): () => void {
    this.handlers.push(handler);
    return () => {
      this.handlers = this.handlers.filter((h) => h !== handler);
    };
  }

  onStateChange(handler: (s: TransportState) => void): () => void {
    this.stateHandlers.push(handler);
    return () => {
      this.stateHandlers = this.stateHandlers.filter((h) => h !== handler);
    };
  }

  dispose(): void {
    this.disposed = true;
    this.ws?.close();
    this.handlers = [];
    this.stateHandlers = [];
  }

  /** Messages from the office UI. Only a few mean anything inside Agent Hub. */
  send(message: ClientMessage): void {
    switch (message.type) {
      case 'webviewReady':
        void this.onOfficeReady();
        break;
      case 'focusAgent': {
        const t = this.bySessionNumber.get(message.id as number);
        if (t) this.openSession(t);
        break;
      }
      default:
        break; // launching, terminals, hooks, settings: handled by Agent Hub itself
    }
  }

  // ── Socket ────────────────────────────────────────────────────────────────

  private setState(s: TransportState): void {
    if (this._state === s) return;
    this._state = s;
    for (const h of this.stateHandlers) h(s);
  }

  private connect(): void {
    if (this.disposed) return;
    const token = localStorage.getItem(TOKEN_KEY) ?? '';
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${proto}//${location.host}/ws/agents?token=${encodeURIComponent(token)}`);
    this.ws = ws;
    ws.onopen = () => {
      this.setState(TRANSPORT_STATE_CONNECTED);
      this.resolveReady();
      ws.send(JSON.stringify({ type: 'watchOffice' }));
    };
    ws.onclose = () => {
      if (this.disposed) return;
      this.setState(TRANSPORT_STATE_RECONNECTING);
      setHubStatus('연결 끊김 — 재연결 중…', false);
      setTimeout(() => this.connect(), RECONNECT_MS);
    };
    ws.onerror = () => ws.close();
    ws.onmessage = (ev) => {
      try {
        this.onHubMessage(JSON.parse(ev.data as string) as Record<string, unknown>);
      } catch (e) {
        console.error('[Hub] bad message', e);
      }
    };
  }

  private onHubMessage(m: Record<string, unknown>): void {
    chatOnHubMessage(m); // the chat panel needs activity, questions, permissions and results too
    switch (m.type) {
      case 'auth':
        if (m.status === 'approved') setHubStatus('연결됨', true);
        else setHubStatus('이 기기는 아직 승인되지 않았습니다 — Agent Hub 앱(/)에서 인증을 요청하세요', false);
        break;
      case 'office':
        this.lastActivity = (m.activity as Record<string, HubEvent[]>) ?? {};
        this.lastSubagents = (m.subagents as Record<string, HubSubagent[]>) ?? {};
        this.sync(m.sessions as HubSession[], this.lastActivity, this.lastSubagents);
        break;
      case 'sessions':
        this.sync(m.sessions as HubSession[], null);
        break;
      case 'elicit':
        this.attention(m.sessionId as string, '질문이 도착했습니다', true);
        break;
      case 'permission':
        this.attention(
          m.sessionId as string,
          `권한 승인 요청: ${String(m.tool ?? '')} ${String(m.detail ?? '')}`.trim(),
          true,
        );
        break;
      case 'ask': {
        const t = this.bySession.get(m.sessionId as string);
        if (t) this.emit({ type: 'agentStatus', id: t.id, status: 'waiting', awaitingInput: true });
        break;
      }
      default:
        break;
    }
  }

  // ── Translation ───────────────────────────────────────────────────────────

  private emit(msg: Record<string, unknown>): void {
    if (!this.officeReady) return;
    for (const h of this.handlers) h(msg as unknown as ServerMessage);
  }

  private async onOfficeReady(): Promise<void> {
    const kit = await this.kitReady;
    this.officeReady = true;
    // Sprites from the build; the office itself is the room kit, composed in the saved arrangement.
    for (const m of mockAssetMessages()) {
      if (m.type === 'settingsLoaded') continue;
      this.emit(m.type === 'layoutLoaded' && kit ? { ...m, layout: compose(kit) } : m);
    }
    this.emit({ type: 'providerCapabilities', readingTools: READING_TOOLS, subagentToolNames: SUBAGENT_TOOLS });
    this.emit({
      type: 'settingsLoaded',
      soundEnabled: false,
      lastSeenVersion: '1.5.0',
      extensionVersion: '1.5.0',
      watchAllSessions: true,
      alwaysShowLabels: true,
      ghostHeadlessAgents: false,
      hooksEnabled: false,
      hooksInfoShown: true,
      externalAssetDirectories: [],
      showAreas: true,
      bypassPermissions: false,
      showHostDirectories: false,
    });
    // No toolbar: launching agents, terminals and settings live in Agent Hub.
    this.emit({ type: 'officeAccess', readOnly: true });

    const tracked = [...this.bySession.values()];
    const names: Record<string, string> = {};
    const external: Record<string, boolean> = {};
    for (const t of tracked) {
      names[t.id] = t.label;
      external[t.id] = true;
    }
    this.emit({
      type: 'existingAgents',
      agents: tracked.map((t) => t.id),
      agentMeta: {},
      directoryNames: names,
      externalAgents: external,
    });
    // Replay the latest snapshot onto the fresh office.
    for (const t of tracked) {
      t.seen.clear();
      t.working = false;
      t.openToolId = null;
      t.needsUser = false;
      t.subs.clear();
      t.bgTasks.clear();
    }
    this.sync(this.lastSessions, this.lastActivity, this.lastSubagents);
  }

  private sync(
    sessions: HubSession[],
    activity: Record<string, HubEvent[]> | null,
    subagents: Record<string, HubSubagent[]> | null = null,
  ): void {
    this.lastSessions = sessions ?? [];
    // Only sessions you can talk to: still running, console reachable (hook-reported PID alive).
    const visible = this.lastSessions.filter((s) => s.status !== 'ended' && s.injectable === true);
    const keep = new Set(visible.map((s) => s.id));

    for (const [sid, t] of this.bySession) {
      if (keep.has(sid)) continue;
      this.emit({ type: 'agentClosed', id: t.id });
      this.bySession.delete(sid);
      this.bySessionNumber.delete(t.id);
      dismissToasts(sid);
    }

    for (const s of visible) {
      let t = this.bySession.get(s.id);
      if (!t) {
        t = {
          id: this.nextId++,
          session: s,
          label: labelOf(s),
          working: false,
          seen: new Set(),
          openToolId: null,
          needsUser: false,
          subs: new Map(),
          bgTasks: new Map(),
        };
        this.bySession.set(s.id, t);
        this.bySessionNumber.set(t.id, t);
        this.emit({ type: 'agentCreated', id: t.id, directoryName: t.label, isExternal: true });
        // Characters start out active; a session that is resting goes straight to 휴게실.
        if (s.working !== true) this.emit({ type: 'agentStatus', id: t.id, status: 'waiting' });
      }
      t.session = s;
      const early = this.earlyAttention.get(s.id);
      if (early) {
        this.earlyAttention.delete(s.id);
        this.attention(s.id, early.text, early.live);
      }

      const events = activity?.[s.id];
      if (events) this.applyActivity(t, events);
      if (subagents) this.applySubagents(t, subagents[s.id] ?? []);

      const working = s.working === true;
      if (working && !t.working) this.emit({ type: 'agentStatus', id: t.id, status: 'active' });
      if (!working && t.working) {
        this.closeTool(t);
        this.emit({ type: 'agentToolsClear', id: t.id });
        this.emit({ type: 'agentStatus', id: t.id, status: 'waiting' });
      }
      t.working = working;

      const needsUser = !!(s.pendingAsk || s.pendingPermission);
      if (needsUser && !t.needsUser) {
        const what = s.pendingPermission
          ? `권한 대기: ${s.pendingPermission.tool ?? ''} ${s.pendingPermission.detail ?? ''}`.trim()
          : `질문 대기: ${s.pendingAsk?.question ?? ''}`.trim();
        this.attention(s.id, what, false);
      }
      if (!needsUser && t.needsUser) {
        this.emit({ type: 'agentToolPermissionClear', id: t.id });
        dismissToasts(s.id);
      }
      t.needsUser = needsUser;
    }

    if (this.openOnLoad && this.officeReady) {
      const t = this.bySession.get(this.openOnLoad);
      if (t) {
        this.openOnLoad = null;
        this.openSession(t);
      }
    }
  }

  /** Translate the not-yet-seen part of a session's recent activity window. */
  private applyActivity(t: Tracked, events: HubEvent[]): void {
    const keys = events.map(eventKey);
    events.forEach((e, i) => {
      const key = keys[i];
      if (t.seen.has(key)) return;
      switch (e.kind) {
        case 'tool_use': {
          const toolId = e.toolId ?? key;
          // A sub-agent (Task/Agent) or a background shell becomes its own character next to its lead.
          if (e.toolName === 'Task' || e.toolName === 'Agent') {
            this.closeTool(t);
            this.spawnSub(t, toolId, `Subtask: ${detailOf(e)}`, e.background === true, false);
            if (!e.background) t.openToolId = toolId; // the lead waits for a foreground sub-agent
            break;
          }
          if (e.background && isShellTool(e.toolName)) {
            const cmd = e.target ?? detailOf(e);
            this.spawnSub(t, toolId, `Subtask: 🐚 셸 · ${cmd}`, true, true);
            this.emit({ type: 'subagentToolStart', id: t.id, parentToolId: toolId, toolId: `${toolId}:run`, status: `Running: ${cmd}` });
            break;
          }
          this.closeTool(t);
          const tool = toOfficeTool(e);
          this.emit({ type: 'agentToolStart', id: t.id, toolId: key, status: tool.status, toolName: tool.name });
          t.openToolId = key;
          break;
        }
        case 'tool_result': {
          const sub = e.toolId ? t.subs.get(e.toolId) : undefined;
          if (sub && e.taskId) {
            t.bgTasks.set(e.taskId, e.toolId!); // background: it lives on until task_done / its sub-agent finishes
            break;
          }
          if (sub && !sub.shell && e.toolId) this.clearSub(t, e.toolId); // foreground sub-agent returned
          this.closeTool(t);
          break;
        }
        case 'task_done': {
          const toolId = e.taskId ? t.bgTasks.get(e.taskId) : undefined;
          if (toolId) {
            t.bgTasks.delete(e.taskId!);
            this.clearSub(t, toolId);
          }
          break;
        }
        case 'message':
        case 'thinking': {
          // Reasoning with no tool in hand = planning (사무실) until the next tool starts.
          if (!t.openToolId && t.session.working) {
            this.emit({ type: 'agentToolStart', id: t.id, toolId: key, status: '생각 정리 중', toolName: 'Plan' });
            t.openToolId = key;
          }
          // Drop harness/system text that leaks into transcripts (tag blocks, environment notices).
          const text = cleanAgentText(e.text);
          if (text) {
            this.emit({
              type: 'agentThought',
              id: t.id,
              kind: e.kind === 'thinking' ? 'thinking' : 'text',
              text: text.length > THOUGHT_MAX ? text.slice(0, THOUGHT_MAX) + '…' : text,
            });
          }
          break;
        }
        case 'user_prompt':
          this.closeTool(t);
          this.emit({ type: 'agentToolsClear', id: t.id }); // new turn: fresh reading card
          break;
        default:
          break;
      }
    });
    t.seen = new Set(keys);
  }

  private spawnSub(t: Tracked, toolId: string, status: string, background: boolean, shell: boolean): void {
    if (t.subs.has(toolId)) return;
    t.subs.set(toolId, { seen: new Set(), openToolId: null, shell });
    this.emit({ type: 'agentToolStart', id: t.id, toolId, status, toolName: 'Agent', runInBackground: background });
  }

  private clearSub(t: Tracked, toolId: string): void {
    if (!t.subs.delete(toolId)) return;
    this.emit({ type: 'subagentClear', id: t.id, parentToolId: toolId });
  }

  /** Live sub-agents (from their own transcripts): their tools, thoughts, and when they finish. */
  private applySubagents(t: Tracked, subs: HubSubagent[]): void {
    const live = new Set(subs.map((x) => x.toolUseId));
    for (const [toolId, sub] of t.subs) {
      if (!sub.shell && !live.has(toolId) && sub.seen.size > 0) this.clearSub(t, toolId); // finished
    }
    for (const x of subs) {
      if (!x.toolUseId) continue;
      this.spawnSub(t, x.toolUseId, `Subtask: ${x.description ?? x.agentType ?? '서브에이전트'}`, true, false);
      const sub = t.subs.get(x.toolUseId)!;
      const keys = x.events.map(eventKey);
      x.events.forEach((e, i) => {
        if (sub.seen.has(keys[i])) return;
        if (e.kind === 'tool_use') {
          if (sub.openToolId) this.emit({ type: 'subagentToolDone', id: t.id, parentToolId: x.toolUseId, toolId: sub.openToolId });
          this.emit({ type: 'subagentToolStart', id: t.id, parentToolId: x.toolUseId, toolId: keys[i], status: toOfficeTool(e).status });
          sub.openToolId = keys[i];
        } else if (e.kind === 'tool_result' && sub.openToolId) {
          this.emit({ type: 'subagentToolDone', id: t.id, parentToolId: x.toolUseId, toolId: sub.openToolId });
          sub.openToolId = null;
        } else if (e.kind === 'message' || e.kind === 'thinking') {
          const text = cleanAgentText(e.text);
          if (text) {
            this.emit({
              type: 'agentThought', id: t.id, parentToolId: x.toolUseId,
              kind: e.kind === 'thinking' ? 'thinking' : 'text',
              text: text.length > THOUGHT_MAX ? text.slice(0, THOUGHT_MAX) + '…' : text,
            });
          }
        }
      });
      sub.seen = new Set(keys.length ? keys : ['∅']);
    }
  }

  private closeTool(t: Tracked): void {
    if (!t.openToolId) return;
    this.emit({ type: 'agentToolDone', id: t.id, toolId: t.openToolId });
    t.openToolId = null;
  }

  /** Questions/permissions that arrived before their session's first snapshot (it can take seconds). */
  private earlyAttention = new Map<string, { text: string; live: boolean }>();

  private attention(sessionId: string, text: string, live: boolean): void {
    const t = this.bySession.get(sessionId);
    if (!t) {
      this.earlyAttention.set(sessionId, { text, live }); // shown once the session shows up (sync)
      return;
    }
    this.emit({ type: 'agentToolPermission', id: t.id });
    showAttention(sessionId, t.label, text, () => this.openSession(t));
    if (live) t.needsUser = true;
  }

  private openSession(t: Tracked): void {
    openChat(t.session.id);
  }
}

/** Stable identity of an activity event across snapshots. Transcripts can repeat one tool_use over
 *  several records, so tools are keyed by their id; everything else by time + content. */
function eventKey(e: HubEvent, i: number): string {
  if (e.toolId && (e.kind === 'tool_use' || e.kind === 'tool_result')) return `${e.kind}|${e.toolId}`;
  return `${e.ts ?? i}|${e.kind}|${e.toolName ?? ''}|${e.summary ?? ''}|${e.taskId ?? ''}`;
}

/** Character label: project, plus where it runs (Codex / WSL). */
function labelOf(s: HubSession): string {
  let label = s.project && s.project !== '(unknown)' ? s.project : (s.title ?? s.id).slice(0, 24);
  if (s.engine === 'codex') label += ' · Codex';
  if (s.cwd?.startsWith('/')) label += ' · WSL';
  return label;
}
