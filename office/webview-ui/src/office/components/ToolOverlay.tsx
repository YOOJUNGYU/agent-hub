import { Fragment, useEffect, useRef, useState } from 'react';

import { Button } from '../../components/ui/Button.js';
import {
  BUBBLE_FADE_DURATION_SEC,
  BUBBLE_SITTING_OFFSET_PX,
  BUBBLE_VERTICAL_OFFSET_PX,
  CHARACTER_SITTING_OFFSET_PX,
  CONTEXT_CRITICAL_THRESHOLD,
  CONTEXT_DANGER_THRESHOLD,
  CONTEXT_GAUGE_BG,
  CONTEXT_GAUGE_COLOR_CRITICAL,
  CONTEXT_GAUGE_COLOR_DANGER,
  CONTEXT_GAUGE_COLOR_OK,
  CONTEXT_GAUGE_COLOR_WARN,
  CONTEXT_GAUGE_HEIGHT_PX,
  CONTEXT_GAUGE_WIDTH_PX,
  CONTEXT_WARN_THRESHOLD,
  OVERLAY_BUBBLE_Z_INDEX,
  OVERLAY_LABEL_SELECTED_Z_INDEX,
  OVERLAY_LABEL_Z_INDEX,
  REFERENCE_CARD_BG,
  REFERENCE_CARD_LINK,
  REFERENCE_CARD_MAX_ITEMS,
  REFERENCE_CARD_MAX_WIDTH_PX,
  REFERENCE_CARD_TEXT,
  ROOM_READING,
  TEAM_LEAD_COLOR,
  TEAM_ROLE_COLOR,
  THOUGHT_BUBBLE_BG,
  THOUGHT_BUBBLE_BORDER,
  THOUGHT_BUBBLE_MAX_WIDTH_PX,
  THOUGHT_BUBBLE_TEXT,
  THOUGHT_PEEK_MAX_WIDTH_PX,
  THOUGHT_PREVIEW_CHARS,
  TOOL_OVERLAY_VERTICAL_OFFSET,
  WAIT_TOOLS,
} from '../../constants.js';
import type { SubagentCharacter } from '../../hooks/useExtensionMessages.js';
import { isHubRuntime } from '../../runtime.js';
import { transport } from '../../transport/index.js';
import type { OfficeState } from '../engine/officeState.js';
import { overlayProjection } from '../projection.js';
import { getCachedSprite } from '../sprites/spriteCache.js';
import { BUBBLE_PERMISSION_SPRITE, BUBBLE_WAITING_SPRITE } from '../sprites/spriteData.js';
import type { ReferenceItem } from '../toolUtils.js';
import { parseReference } from '../toolUtils.js';
import type { ToolActivity } from '../types.js';
import { CharacterState } from '../types.js';

// Both turn-end states show the green checkmark bubble. A finished turn (Stop)
// shows ONLY the checkmark (the label falls through to its normal idle text);
// going idle waiting on the user (Notification(idle_prompt)) additionally
// surfaces this label. Driven by Character.waitingAwaitingInput.
const WAITING_INPUT_ACTIVITY_TEXT = isHubRuntime ? '입력 대기' : 'Waiting for input';
const NEEDS_APPROVAL_TEXT = isHubRuntime ? '승인 대기' : 'Needs approval';
const IDLE_TEXT = isHubRuntime ? '쉬는 중' : 'Idle';
const QUESTION_TEXT = '질문 대기';

interface ToolOverlayProps {
  officeState: OfficeState;
  agents: number[];
  agentTools: Record<number, ToolActivity[]>;
  subagentTools: Record<number, Record<string, ToolActivity[]>>;
  subagentCharacters: SubagentCharacter[];
  containerRef: React.RefObject<HTMLDivElement | null>;
  zoom: number;
  panRef: React.RefObject<{ x: number; y: number }>;
  onCloseAgent: (id: number) => void;
  alwaysShowOverlay: boolean;
}

/** Derive a short human-readable activity string from tools/status */
function getActivityText(
  agentId: number,
  agentTools: Record<number, ToolActivity[]>,
  isActive: boolean,
  bubbleType: 'permission' | 'waiting' | null,
  waitingAwaitingInput: boolean,
): string {
  if (bubbleType === 'permission') return NEEDS_APPROVAL_TEXT;
  // Only the idle case ("Waiting for input") gets a dedicated label. A finished
  // turn (Stop, waitingAwaitingInput=false) falls through so the checkmark alone
  // signals "done", same as the original behavior.
  if (bubbleType === 'waiting' && waitingAwaitingInput) return WAITING_INPUT_ACTIVITY_TEXT;

  const tools = agentTools[agentId];
  if (tools && tools.length > 0) {
    // Find the latest non-done tool
    const activeTool = [...tools].reverse().find((t) => !t.done);
    if (activeTool) {
      if (activeTool.permissionWait) return NEEDS_APPROVAL_TEXT;
      return activeTool.status;
    }
    // All tools done but agent still active (mid-turn) — keep showing last tool status
    if (isActive) {
      const lastTool = tools[tools.length - 1];
      if (lastTool) return lastTool.status;
    }
  }

  return IDLE_TEXT;
}

function getFuelColor(ratio: number): string {
  if (ratio >= CONTEXT_CRITICAL_THRESHOLD) return CONTEXT_GAUGE_COLOR_CRITICAL;
  if (ratio >= CONTEXT_DANGER_THRESHOLD) return CONTEXT_GAUGE_COLOR_DANGER;
  if (ratio >= CONTEXT_WARN_THRESHOLD) return CONTEXT_GAUGE_COLOR_WARN;
  return CONTEXT_GAUGE_COLOR_OK;
}

/** A speech-bubble sprite blitted into a DOM canvas. Bubbles live in the DOM
 *  (not the office canvas) so they can stack ABOVE the status label panels —
 *  canvas content can never out-paint the DOM overlay. */
function OverlayBubble({ cached, dpr }: { cached: HTMLCanvasElement; dpr: number }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    canvas.width = cached.width;
    canvas.height = cached.height;
    canvas.getContext('2d')?.drawImage(cached, 0, 0);
  }, [cached]);
  return (
    <canvas
      ref={canvasRef}
      className="block"
      style={{ width: cached.width / dpr, height: cached.height / dpr }}
    />
  );
}

export function ToolOverlay({
  officeState,
  agents,
  agentTools,
  subagentTools,
  subagentCharacters,
  containerRef,
  zoom,
  panRef,
  onCloseAgent,
  alwaysShowOverlay,
}: ToolOverlayProps) {
  const [, setTick] = useState(0);
  useEffect(() => {
    let rafId = 0;
    const tick = () => {
      setTick((n) => n + 1);
      rafId = requestAnimationFrame(tick);
    };
    rafId = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(rafId);
  }, []);

  const el = containerRef.current;
  if (!el) return null;
  // dpr is also needed to size the bubble canvases in CSS px (OverlayBubble).
  const dpr = window.devicePixelRatio || 1;
  const project = overlayProjection(
    officeState.getLayout(),
    el.getBoundingClientRect(),
    zoom,
    panRef.current,
    dpr,
  );

  const selectedId = officeState.selectedAgentId;
  const hoveredId = officeState.hoveredAgentId;

  // All character IDs
  const allIds = [...agents, ...subagentCharacters.map((s) => s.id)];

  return (
    <>
      {allIds.map((id) => {
        const ch = officeState.characters.get(id);
        if (!ch) return null;

        const isSelected = selectedId === id;
        const isHovered = hoveredId === id;
        const isSub = ch.isSubagent;

        // Speech bubble (permission dots / done checkmark) — rendered here in
        // the DOM, above the label panels (OVERLAY_BUBBLE_Z_INDEX over the label panels), for every
        // character regardless of label visibility. Mirrors the geometry the
        // canvas renderer used. The idle "Waiting for input" state speaks
        // through its label, not a bubble.
        let bubbleNode = null;
        if (ch.bubbleType && !(ch.bubbleType === 'waiting' && ch.waitingAwaitingInput)) {
          const cachedBubble = getCachedSprite(
            ch.bubbleType === 'permission' ? BUBBLE_PERMISSION_SPRITE : BUBBLE_WAITING_SPRITE,
            zoom,
          );
          const bubbleSittingOff = ch.state === CharacterState.TYPE ? BUBBLE_SITTING_OFFSET_PX : 0;
          const alpha =
            ch.bubbleType === 'waiting' && ch.bubbleTimer < BUBBLE_FADE_DURATION_SEC
              ? ch.bubbleTimer / BUBBLE_FADE_DURATION_SEC
              : 1;
          bubbleNode = (
            <div
              className="absolute -translate-x-1/2 pointer-events-none"
              style={{
                left: project.toScreenX(ch.x),
                // Bubble bottom sits one sprite pixel above the character's
                // anchor: project the anchor, then lift by the bubble's own
                // device-pixel height plus one zoomed pixel, in CSS px.
                top:
                  project.toScreenY(ch.y + bubbleSittingOff - BUBBLE_VERTICAL_OFFSET_PX) -
                  (cachedBubble.height + zoom) / dpr,
                opacity: alpha,
                zIndex: OVERLAY_BUBBLE_Z_INDEX,
              }}
            >
              {isHubRuntime ? (
                // Agent Hub: a soft round badge instead of the pixel speech-bubble sprite
                <span
                  className={`hub-badge ${ch.bubbleType === 'permission' ? 'hub-badge-wait' : 'hub-badge-done'}`}
                >
                  {ch.bubbleType === 'permission' ? '?' : '\u2713'}
                </span>
              ) : (
                <OverlayBubble cached={cachedBubble} dpr={dpr} />
              )}
            </div>
          );
        }

        // Thought bubble (what the agent is thinking / deciding) and, in the
        // reading room, the card of files / URLs / queries it is looking at.
        // Both render regardless of label visibility.
        const anchorSittingOff = ch.state === CharacterState.TYPE ? CHARACTER_SITTING_OFFSET_PX : 0;
        const anchorX = project.toScreenX(ch.x);
        const headY = project.toScreenY(ch.y + anchorSittingOff - TOOL_OVERLAY_VERTICAL_OFFSET);
        const expanded = isSelected || isHovered;
        // Agent Hub: the bubble and label open the chat too, not just the sprite (sub-agents → their lead).
        const openChat = () => transport.send({ type: 'focusAgent', id: officeState.terminalOwnerOf(id) });
        let thoughtNode = null;
        const thought = ch.thought;
        if (thought) {
          // Agent Hub: a one-line peek until hovered/selected, so neighbours' bubbles don't pile up.
          const peek = isHubRuntime && !expanded;
          const text =
            expanded || peek || thought.text.length <= THOUGHT_PREVIEW_CHARS
              ? thought.text
              : thought.text.slice(0, THOUGHT_PREVIEW_CHARS) + '\u2026';
          thoughtNode = (
            <div
              className={`absolute shadow-pixel ${isHubRuntime ? 'cursor-pointer' : 'pointer-events-none'}`}
              onClick={isHubRuntime ? openChat : undefined}
              style={{
                left: anchorX,
                top: headY - (isHubRuntime ? 30 : 34),
                transform: 'translate(-50%, -100%)',
                maxWidth: peek ? THOUGHT_PEEK_MAX_WIDTH_PX : THOUGHT_BUBBLE_MAX_WIDTH_PX,
                width: 'max-content',
                background: THOUGHT_BUBBLE_BG,
                color: THOUGHT_BUBBLE_TEXT,
                border: isHubRuntime ? '1px solid var(--color-border)' : `2px solid ${THOUGHT_BUBBLE_BORDER}`,
                borderRadius: isHubRuntime ? 12 : 10,
                padding: '3px 8px',
                fontSize: '18px',
                lineHeight: 1.05,
                whiteSpace: peek ? 'nowrap' : 'pre-wrap',
                overflow: peek ? 'hidden' : undefined,
                textOverflow: peek ? 'ellipsis' : undefined,
                wordBreak: 'break-word',
                opacity: expanded ? 1 : 0.92,
                zIndex: expanded ? OVERLAY_LABEL_SELECTED_Z_INDEX : OVERLAY_LABEL_Z_INDEX,
              }}
              data-testid="agent-thought"
              data-agent-id={id}
            >
              {thought.kind === 'thinking' ? '\u{1F4AD} ' : '\u{1F4AC} '}
              {text}
            </div>
          );
        }

        let referenceNode = null;
        if (ch.seatId && officeState.seatZone(ch.seatId) === ROOM_READING) {
          const sub = isSub ? subagentCharacters.find((s) => s.id === id) : undefined;
          const rows = sub ? subagentTools[sub.parentAgentId]?.[sub.parentToolId] : agentTools[id];
          const refs: ReferenceItem[] = [];
          for (const row of [...(rows ?? [])].reverse()) {
            const ref = parseReference(row.status);
            if (ref && !refs.some((r) => r.text === ref.text)) refs.push(ref);
            if (refs.length >= REFERENCE_CARD_MAX_ITEMS) break;
          }
          if (refs.length > 0) {
            referenceNode = (
              <div
                className="absolute shadow-pixel flex flex-col gap-2"
                style={{
                  left: anchorX + 14 * (zoom / dpr),
                  top: project.toScreenY(ch.y + anchorSittingOff),
                  transform: 'translateY(-100%)',
                  maxWidth: REFERENCE_CARD_MAX_WIDTH_PX,
                  width: 'max-content',
                  background: REFERENCE_CARD_BG,
                  color: REFERENCE_CARD_TEXT,
                  border: isHubRuntime ? '1px solid var(--color-border)' : `2px solid ${REFERENCE_CARD_TEXT}`,
                  borderRadius: isHubRuntime ? 10 : undefined,
                  padding: '3px 6px',
                  fontSize: '17px',
                  lineHeight: 1,
                  pointerEvents: 'auto',
                  zIndex: expanded ? OVERLAY_LABEL_SELECTED_Z_INDEX : OVERLAY_LABEL_Z_INDEX,
                }}
                data-testid="reference-card"
                data-agent-id={id}
              >
                {refs.map((r, i) => (
                  <div
                    key={r.text}
                    className="overflow-hidden text-ellipsis whitespace-nowrap"
                    style={{ opacity: i === 0 ? 1 : 0.6 }}
                    title={r.href ?? r.text}
                  >
                    {r.icon}{' '}
                    {r.href ? (
                      <a
                        href={r.href}
                        target="_blank"
                        rel="noopener noreferrer"
                        style={{ color: REFERENCE_CARD_LINK, textDecoration: 'underline' }}
                      >
                        {r.text}
                      </a>
                    ) : (
                      r.text
                    )}
                  </div>
                ))}
              </div>
            );
          }
        }
        const extrasNode = (
          <>
            {thoughtNode}
            {referenceNode}
          </>
        );

        // Only show the label for hovered or selected agents (unless
        // always-show is on) — the bubble renders regardless. Agent Hub: resting
        // agents keep quiet (휴게실 already says it) until hovered.
        const resting = isHubRuntime && !ch.isActive && ch.bubbleType !== 'permission';
        if ((!alwaysShowOverlay || resting) && !isSelected && !isHovered) {
          return (
            <Fragment key={id}>
              {bubbleNode}
              {extrasNode}
            </Fragment>
          );
        }

        // Position above character
        const sittingOffset = ch.state === CharacterState.TYPE ? CHARACTER_SITTING_OFFSET_PX : 0;
        const screenX = project.toScreenX(ch.x);
        const screenY = project.toScreenY(ch.y + sittingOffset - TOOL_OVERLAY_VERTICAL_OFFSET);

        // A "Done" agent (finished turn: waiting bubble without awaitingInput)
        // shows ONLY its floating green checkmark bubble, never the label panel
        // (the panel would cover the bubble). Render an empty positioned marker
        // so overlay counts stay stable and hover/select can still bring the
        // panel back. When always-show is off, the early return above already
        // keeps the panel hidden for idle agents.
        const isDone = ch.bubbleType === 'waiting' && !ch.waitingAwaitingInput;
        if (isDone && !isSelected && !isHovered) {
          return (
            <Fragment key={id}>
              {bubbleNode}
              {extrasNode}
              <div
                className="absolute"
                style={{ left: screenX, top: screenY, pointerEvents: 'none' }}
                data-testid="agent-overlay"
                data-agent-id={id}
              />
            </Fragment>
          );
        }

        // Get activity text
        const hasWaitingBubble = ch.bubbleType === 'waiting';
        const subHasPermission = isSub && ch.bubbleType === 'permission';
        let activityText: string;
        if (hasWaitingBubble && ch.waitingAwaitingInput) {
          // Idle, waiting on the user -> dedicated label. A finished turn (Stop)
          // shows only the checkmark and falls through to the normal idle text.
          activityText = WAITING_INPUT_ACTIVITY_TEXT;
        } else if (isSub) {
          if (subHasPermission) {
            activityText = NEEDS_APPROVAL_TEXT;
          } else {
            // Hover shows the subtask title; SELECTING the sub reveals its live
            // tool activity (watched sub-agents stream it via subagentToolStart).
            const sub = subagentCharacters.find((s) => s.id === id);
            const rows = sub ? subagentTools[sub.parentAgentId]?.[sub.parentToolId] : undefined;
            const activeRow =
              isSelected && rows ? [...rows].reverse().find((t) => !t.done) : undefined;
            activityText = activeRow?.status ?? (sub?.label || 'Subtask');
          }
        } else {
          activityText = getActivityText(
            id,
            agentTools,
            ch.isActive,
            ch.bubbleType,
            ch.waitingAwaitingInput ?? false,
          );
        }

        if (isHubRuntime && WAIT_TOOLS.has(ch.currentTool ?? '')) {
          activityText = QUESTION_TEXT;
        }

        // Determine dot color
        const tools = agentTools[id];
        const hasPermission = subHasPermission || tools?.some((t) => t.permissionWait && !t.done);
        const hasActiveTools = tools?.some((t) => !t.done);
        const isActive = ch.isActive;
        const hasWaiting = ch.bubbleType === 'waiting';

        let dotColor: string | null = null;
        if (hasPermission || hasWaiting) {
          dotColor = 'var(--color-status-permission)';
        } else if (isActive && hasActiveTools) {
          dotColor = 'var(--color-status-active)';
        }

        // Team info
        const teamRoleLabel = ch.isTeamLead ? 'LEAD' : ch.agentName || null;
        const hasExtraLines = !!(ch.directoryName || teamRoleLabel);

        // Context gauge. Every agent gets one — lead, teammate, adopted,
        // headless — as soon as it has taken a turn. Sub-agents never do: they
        // have no session of their own, so contextTokens stays 0.
        const contextRatio = ch.contextTokens / ch.maxContextTokens;
        const showContextGauge = !isSub && ch.contextTokens > 0;

        return (
          <Fragment key={id}>
            {bubbleNode}
            {extrasNode}
            <div
              className="absolute flex flex-col items-center -translate-x-1/2"
              style={{
                left: screenX,
                top: screenY - (hasExtraLines ? 34 : 28),
                pointerEvents: isSelected || isHubRuntime ? 'auto' : 'none',
                cursor: isHubRuntime ? 'pointer' : undefined,
                opacity: alwaysShowOverlay && !isSelected && !isHovered ? (isSub ? 0.5 : 0.75) : 1,
                zIndex: isSelected ? OVERLAY_LABEL_SELECTED_Z_INDEX : OVERLAY_LABEL_Z_INDEX,
              }}
              data-testid="agent-overlay"
              data-agent-id={id}
              onClick={isHubRuntime ? openChat : undefined}
            >
              <div className="flex items-center border-border px-8 pt-2 pb-4 gap-5 pixel-panel whitespace-nowrap max-w-2xs">
                {dotColor && (
                  <span
                    className={`w-6 h-6 rounded-full shrink-0 ${isActive && !hasPermission && !hasWaiting ? 'pixel-pulse' : ''}`}
                    style={{ background: dotColor }}
                  />
                )}
                <div className="flex flex-col gap-0 overflow-hidden">
                  {teamRoleLabel && (
                    <span
                      className="overflow-hidden text-ellipsis block leading-none"
                      style={{
                        fontSize: '18px',
                        color: ch.isTeamLead ? TEAM_LEAD_COLOR : TEAM_ROLE_COLOR,
                        fontWeight: ch.isTeamLead ? 'bold' : undefined,
                      }}
                    >
                      {teamRoleLabel}
                    </span>
                  )}
                  <span
                    className="overflow-hidden text-ellipsis block leading-none"
                    style={{
                      fontSize: isSub ? '20px' : '22px',
                      fontStyle: isSub ? 'italic' : undefined,
                    }}
                  >
                    {activityText}
                  </span>
                  {ch.directoryName && (
                    <span className="text-2xs leading-none overflow-hidden text-ellipsis block">
                      {ch.directoryName}
                    </span>
                  )}
                </div>
                {isSelected && !isSub && (
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={(e) => {
                      e.stopPropagation();
                      onCloseAgent(id);
                    }}
                    title="Close agent"
                    className="ml-2 shrink-0 leading-none"
                  >
                    ×
                  </Button>
                )}
              </div>
              {showContextGauge && (
                <div
                  style={{
                    width: CONTEXT_GAUGE_WIDTH_PX,
                    height: CONTEXT_GAUGE_HEIGHT_PX,
                    background: CONTEXT_GAUGE_BG,
                    marginTop: 2,
                  }}
                  title={`${Math.round(contextRatio * 100)}% context used (${(ch.contextTokens / 1000).toFixed(0)}k of ${(ch.maxContextTokens / 1000).toFixed(0)}k tokens)`}
                  data-testid="context-gauge"
                  data-context-pct={Math.round(contextRatio * 100)}
                >
                  <div
                    style={{
                      width: `${Math.min(contextRatio * 100, 100)}%`,
                      height: '100%',
                      background: getFuelColor(contextRatio),
                    }}
                  />
                </div>
              )}
            </div>
          </Fragment>
        );
      })}
    </>
  );
}
