/**
 * "바닥 꾸미기" — the button under the zoom controls and its popover: pick the floor
 * the rooms stand on (solid / wood / tile × colour). Each swatch is a live preview
 * drawn by the same code as the office (vectorScene.drawBackdrop).
 */
import { useEffect, useRef, useState } from 'react';

import {
  drawBackdrop,
  FLOOR_COLORS,
  FLOOR_STYLES,
  type FloorChoice,
  type FloorStyle,
  getFloor,
  setFloor,
} from './vectorScene.js';

const SW_W = 64;
const SW_H = 44;

function Swatch({ choice, selected, name, onPick }: { choice: FloorChoice; selected: boolean; name: string; onPick: () => void }) {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const cv = ref.current;
    const c = cv?.getContext('2d');
    if (!cv || !c) return;
    const dpr = window.devicePixelRatio || 1;
    cv.width = SW_W * dpr;
    cv.height = SW_H * dpr;
    drawBackdrop(c, { ox: 0, oy: 0, z: 1.6 * dpr }, cv.width, cv.height, choice);
  }, [choice]);
  return (
    <button type="button" className={`hub-floor-swatch ${selected ? 'on' : ''}`} onClick={onPick} aria-pressed={selected}>
      <canvas ref={ref} style={{ width: SW_W, height: SW_H }} />
      <span>{name}</span>
    </button>
  );
}

export function FloorPicker() {
  const [open, setOpen] = useState(false);
  const [current, setCurrent] = useState<FloorChoice>(getFloor);
  const [tab, setTab] = useState<FloorStyle>(current.style);
  const rootRef = useRef<HTMLDivElement>(null);

  // close on an outside press or Esc
  useEffect(() => {
    if (!open) return;
    const onDown = (e: PointerEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false);
    window.addEventListener('pointerdown', onDown);
    window.addEventListener('keydown', onKey);
    return () => {
      window.removeEventListener('pointerdown', onDown);
      window.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const pick = (f: FloorChoice) => {
    setFloor(f);
    setCurrent(f);
  };

  return (
    <div ref={rootRef} className="hub-floor">
      <button
        type="button"
        className={`hub-floor-btn ${open ? 'on' : ''}`}
        title="바닥 꾸미기"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
      >
        🎨
      </button>
      {open && (
        <div className="hub-floor-pop" role="dialog" aria-label="바닥 꾸미기">
          <div className="hub-floor-title">바닥 꾸미기</div>
          <div className="hub-floor-tabs" role="tablist">
            {FLOOR_STYLES.map((s) => (
              <button
                key={s.style}
                type="button"
                role="tab"
                aria-selected={tab === s.style}
                className={tab === s.style ? 'on' : ''}
                onClick={() => setTab(s.style)}
              >
                {s.name}
              </button>
            ))}
          </div>
          <div className="hub-floor-grid">
            {FLOOR_COLORS[tab].map((c) => (
              <Swatch
                key={c.key}
                name={c.name}
                choice={{ style: tab, color: c.key }}
                selected={current.style === tab && current.color === c.key}
                onPick={() => pick({ style: tab, color: c.key })}
              />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
