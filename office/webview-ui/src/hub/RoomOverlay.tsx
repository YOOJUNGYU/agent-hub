/**
 * Room name plates (top-left of each room), room tooltips on hover, and moving a
 * room by dragging its plate (mouse or touch). Agent Hub mode only.
 */
import { useEffect, useRef, useState } from 'react';

import type { SubagentCharacter } from '../hooks/useExtensionMessages.js';
import type { OfficeState } from '../office/engine/officeState.js';
import { mapOffset, overlayProjection } from '../office/projection.js';
import type { ToolActivity } from '../office/types.js';
import { TILE_SIZE } from '../office/types.js';
import { canPlace, getKit, ROOM_EMOJI, type RoomBlock, roomRect } from './rooms.js';

interface Props {
  officeState: OfficeState;
  agents: number[];
  agentTools: Record<number, ToolActivity[]>;
  subagentCharacters: SubagentCharacter[];
  containerRef: React.RefObject<HTMLDivElement | null>;
  zoom: number;
  panRef: React.RefObject<{ x: number; y: number }>;
}

interface Drag {
  label: string;
  startX: number;
  startY: number;
  dx: number;
  dy: number;
}

export function RoomOverlay({ officeState, agents, agentTools, subagentCharacters, containerRef, zoom, panRef }: Props) {
  const [, setTick] = useState(0);
  const [hovered, setHovered] = useState<string | null>(null);
  const [drag, setDrag] = useState<Drag | null>(null);
  const dragRef = useRef<Drag | null>(null);

  useEffect(() => {
    let raf = 0;
    const loop = () => {
      setTick((n) => n + 1);
      raf = requestAnimationFrame(loop);
    };
    raf = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(raf);
  }, []);

  // Hover: which room is under the pointer (canvas gets the events; we only listen).
  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const onMove = (e: MouseEvent) => {
      const kit = getKit();
      if (!kit || dragRef.current) return;
      const layout = officeState.getLayout();
      const rect = el.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      const { offsetX, offsetY } = mapOffset(
        Math.round(rect.width * dpr),
        Math.round(rect.height * dpr),
        layout.cols,
        layout.rows,
        zoom,
        panRef.current.x,
        panRef.current.y,
      );
      const col = Math.floor(((e.clientX - rect.left) * dpr - offsetX) / zoom / TILE_SIZE);
      const row = Math.floor(((e.clientY - rect.top) * dpr - offsetY) / zoom / TILE_SIZE);
      const room = kit.rooms.find((r) => {
        const b = roomRect(kit, r);
        return col >= b.col && col < b.col + b.w && row >= b.row + 1 && row < b.row + b.h;
      });
      setHovered(room?.label ?? null);
    };
    const onLeave = () => setHovered(null);
    el.addEventListener('mousemove', onMove);
    el.addEventListener('mouseleave', onLeave);
    return () => {
      el.removeEventListener('mousemove', onMove);
      el.removeEventListener('mouseleave', onLeave);
    };
  }, [containerRef, officeState, zoom, panRef]);

  const kit = getKit();
  const el = containerRef.current;
  if (!kit || !el) return null;
  const dpr = window.devicePixelRatio || 1;
  const project = overlayProjection(officeState.getLayout(), el.getBoundingClientRect(), zoom, panRef.current, dpr);
  const tilePx = (TILE_SIZE * zoom) / dpr; // one tile in CSS px

  // Who is in which room (by the tile they stand on), and what they're doing.
  const layout = officeState.getLayout();
  const occupants = new Map<string, Array<{ name: string; doing: string }>>();
  const subLabel = new Map(subagentCharacters.map((s) => [s.id, s.label]));
  for (const id of [...agents, ...subagentCharacters.map((s) => s.id)]) {
    const ch = officeState.characters.get(id);
    if (!ch) continue;
    const label = layout.areaTiles?.[ch.tileRow * layout.cols + ch.tileCol];
    if (!label) continue;
    const tools = agentTools[id];
    const live = tools ? [...tools].reverse().find((t) => !t.done) ?? tools[tools.length - 1] : undefined;
    const name = ch.isSubagent ? `↳ ${subLabel.get(id) || '서브에이전트'}` : (ch.directoryName ?? `#${id}`);
    const doing = ch.isSubagent ? '' : live?.status ?? (ch.isActive ? '작업 중' : '쉬는 중');
    const list = occupants.get(label) ?? [];
    list.push({ name, doing });
    occupants.set(label, list);
  }

  const startDrag = (room: RoomBlock) => (e: React.PointerEvent) => {
    e.preventDefault();
    e.stopPropagation();
    (e.target as HTMLElement).setPointerCapture(e.pointerId);
    const d = { label: room.label, startX: e.clientX, startY: e.clientY, dx: 0, dy: 0 };
    dragRef.current = d;
    setDrag(d);
    setHovered(null);
  };
  const moveDrag = (e: React.PointerEvent) => {
    const d = dragRef.current;
    if (!d) return;
    const next = { ...d, dx: Math.round((e.clientX - d.startX) / tilePx), dy: Math.round((e.clientY - d.startY) / tilePx) };
    dragRef.current = next;
    setDrag(next);
  };
  const endDrag = () => {
    const d = dragRef.current;
    dragRef.current = null;
    setDrag(null);
    if (!d || (d.dx === 0 && d.dy === 0)) return;
    const room = kit.rooms.find((r) => r.label === d.label);
    if (room) {
      window.dispatchEvent(
        new CustomEvent('agenthub:moveRoom', { detail: { label: d.label, x: room.x + d.dx, y: room.y + d.dy } }),
      );
    }
  };

  // Sub-agents and background shells hold hands with their lead: a soft string between their hands.
  const HAND_Y = 6; // world px above the tile centre
  const links = subagentCharacters.flatMap((sc) => {
    const sub = officeState.characters.get(sc.id);
    const lead = officeState.characters.get(sc.parentAgentId);
    if (!sub || !lead) return [];
    const x1 = project.toScreenX(sub.x);
    const y1 = project.toScreenY(sub.y - HAND_Y);
    const x2 = project.toScreenX(lead.x);
    const y2 = project.toScreenY(lead.y - HAND_Y);
    const close = Math.hypot(sub.x - lead.x, sub.y - lead.y) < TILE_SIZE * 1.6;
    const mx = (x1 + x2) / 2;
    const my = (y1 + y2) / 2 - Math.min(60, Math.abs(x2 - x1) / 4 + 12); // a little sag-free arc
    return [{ id: sc.id, x1, y1, x2, y2, mx, my, close }];
  });

  return (
    <>
      <svg className="hub-links" width="100%" height="100%">
        {links.map((l) =>
          l.close ? (
            <line key={l.id} className="hub-link-hands" x1={l.x1} y1={l.y1} x2={l.x2} y2={l.y2} />
          ) : (
            <g key={l.id}>
              <path className="hub-link-string" d={`M ${l.x1} ${l.y1} Q ${l.mx} ${l.my} ${l.x2} ${l.y2}`} />
              <text className="hub-link-heart" x={(l.x1 + 2 * l.mx + l.x2) / 4} y={(l.y1 + 2 * l.my + l.y2) / 4 + 4}>
                ♥
              </text>
            </g>
          ),
        )}
      </svg>
      {kit.rooms.map((room) => {
        const b = roomRect(kit, room);
        // inside the room, just below the top wall's face
        const left = project.toScreenX((b.col + 1) * TILE_SIZE) + 4;
        const top = project.toScreenY((b.row + 2) * TILE_SIZE) + 4;
        const people = occupants.get(room.label) ?? [];
        const isHovered = hovered === room.label;
        return (
          <div key={room.label}>
            <div
              className={`hub-plate ${isHovered ? 'hover' : ''}`}
              style={{ left, top, ['--room' as string]: room.color }}
              title="끌어서 방 옮기기"
              onPointerDown={startDrag(room)}
              onPointerMove={moveDrag}
              onPointerUp={endDrag}
              onPointerCancel={endDrag}
              onMouseEnter={() => !dragRef.current && setHovered(room.label)}
            >
              <span className="hub-plate-emoji">{ROOM_EMOJI[room.label] ?? '🏠'}</span>
              {room.label}
              {people.length > 0 && <span className="hub-plate-count">{people.length}</span>}
            </div>
            {isHovered && !drag && (
              <div className="hub-room-tip" style={{ left, top: top + 30, ['--room' as string]: room.color }}>
                <b>
                  {ROOM_EMOJI[room.label]} {room.label}
                </b>
                <p>{room.desc}</p>
                {people.length === 0 ? (
                  <p className="muted">지금은 아무도 없어요</p>
                ) : (
                  <ul>
                    {people.map((p, i) => (
                      <li key={i}>
                        <span className="who">{p.name}</span>
                        {p.doing && <span className="doing">{p.doing}</span>}
                      </li>
                    ))}
                  </ul>
                )}
                <p className="muted">명패를 끌어 방을 옮길 수 있어요</p>
              </div>
            )}
          </div>
        );
      })}
      {drag &&
        (() => {
          const room = kit.rooms.find((r) => r.label === drag.label);
          if (!room) return null;
          const b = roomRect(kit, room);
          const ok = canPlace(room.label, room.x + drag.dx, room.y + drag.dy);
          return (
            <div
              className={`hub-room-ghost ${ok ? 'ok' : 'bad'}`}
              style={{
                left: project.toScreenX((b.col + drag.dx) * TILE_SIZE),
                top: project.toScreenY((b.row + 1 + drag.dy) * TILE_SIZE),
                width: b.w * tilePx,
                height: (b.h - 1) * tilePx,
              }}
            />
          );
        })()}
    </>
  );
}
