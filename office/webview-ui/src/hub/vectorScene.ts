/* eslint-disable pixel-agents/no-inline-colors -- this module is the artwork; its palette lives here */
/**
 * Agent Hub's office renderer — smooth vector graphics instead of pixel sprites.
 *
 * Same world as the pixel engine (16-unit tiles, OfficeState positions, mapOffset
 * centring), so every DOM overlay (plates, labels, bubbles, links) still lines up.
 * Floors, walls, furniture, the cat/dog characters and pets are drawn with canvas
 * paths at the current zoom, so nothing is blocky at any size.
 */
import type { OfficeState } from '../office/engine/officeState.js';
import { getCatalogEntry } from '../office/layout/furnitureCatalog.js';
import { mapOffset } from '../office/projection.js';
import type { Character, Pet } from '../office/types.js';
import { CharacterState, Direction, TILE_SIZE, TileType } from '../office/types.js';

type Ctx = CanvasRenderingContext2D;
const T = TILE_SIZE;

// ── palette ─────────────────────────────────────────────────────────────────

interface Paint {
  floor: string;
  floor2: string;
}
const ROOM_PAINT: Record<string, { light: Paint; dark: Paint }> = {
  자료실: { light: { floor: '#f6e2c4', floor2: '#efd3ab' }, dark: { floor: '#4a3f3a', floor2: '#433832' } },
  사무실: { light: { floor: '#dde8ff', floor2: '#cbdcff' }, dark: { floor: '#2f3a5c', floor2: '#2a3453' } },
  상담실: { light: { floor: '#ebe3ff', floor2: '#ddd0ff' }, dark: { floor: '#3b3460', floor2: '#352e57' } },
  작업실: { light: { floor: '#d9f5ea', floor2: '#c6eedd' }, dark: { floor: '#28463f', floor2: '#233f38' } },
  휴게실: { light: { floor: '#ffe3ea', floor2: '#ffd2de' }, dark: { floor: '#4f3443', floor2: '#47303c' } },
};
const THEME = {
  light: {
    hall2: '#ece6dc', wallTop: '#ffffff', wallFace: '#e6e9f4', wallEdge: '#c9cfe2',
    shadow: 'rgba(44, 51, 84, 0.13)', outline: 'rgba(44, 51, 84, 0.22)', screen: '#a9c4ff', screenOff: '#5b6584',
    wood: '#e7c08f', woodDark: '#cf9e66', woodLight: '#f5dcb6',
  },
  dark: {
    hall2: '#20253d', wallTop: '#3a4166', wallFace: '#2b3150', wallEdge: '#1b2036',
    shadow: 'rgba(0, 0, 0, 0.3)', outline: 'rgba(0, 0, 0, 0.35)', screen: '#8fb2ff', screenOff: '#3a4160',
    wood: '#a7835e', woodDark: '#8a6845', woodLight: '#c09a73',
  },
};
type Theme = (typeof THEME)['light'];

// cats & dogs — same cast as office/art/characters.py, drawn smooth
const CAST = [
  { kind: 'cat', fur: '#ffb35c', fur2: '#ef8f35', belly: '#fff3df', hood: '#7ea6ff', mark: 'tabby', eye: '#3a2f4a' },
  { kind: 'cat', fur: '#fbfbff', fur2: '#dfe3f0', belly: '#ffffff', hood: '#7fdcbe', mark: '', eye: '#4a7df0' },
  { kind: 'cat', fur: '#4f4a66', fur2: '#3a3650', belly: '#6d6888', hood: '#ffd66b', mark: '', eye: '#ffd447' },
  { kind: 'dog', ears: 'point', fur: '#f2a25e', fur2: '#d6823e', belly: '#fff6ea', hood: '#ff92a8', mark: 'muzzle', eye: '#3a2f4a' },
  { kind: 'dog', ears: 'flop', fur: '#fffaf2', fur2: '#efe3d2', belly: '#ffffff', hood: '#c4b2ff', mark: 'muzzle', eye: '#3a2f4a', earc: '#efd5bb' },
  { kind: 'dog', ears: 'flop', fur: '#c08a62', fur2: '#9a6744', belly: '#ecd0b1', hood: '#9fdc82', mark: 'muzzle', eye: '#2c2238', earc: '#7f5136' },
];

function theme(): Theme {
  return document.documentElement.getAttribute('data-theme') === 'dark' ? THEME.dark : THEME.light;
}
function isDark(): boolean {
  return document.documentElement.getAttribute('data-theme') === 'dark';
}

// ── helpers (all coordinates in world units; `v` maps them to device pixels) ───────

export interface View {
  ox: number;
  oy: number;
  z: number;
}
function rr(c: Ctx, v: View, x: number, y: number, w: number, h: number, r: number): void {
  const X = v.ox + x * v.z;
  const Y = v.oy + y * v.z;
  const W = w * v.z;
  const H = h * v.z;
  const R = Math.min(r * v.z, W / 2, H / 2);
  c.beginPath();
  c.moveTo(X + R, Y);
  c.arcTo(X + W, Y, X + W, Y + H, R);
  c.arcTo(X + W, Y + H, X, Y + H, R);
  c.arcTo(X, Y + H, X, Y, R);
  c.arcTo(X, Y, X + W, Y, R);
  c.closePath();
}
function box(c: Ctx, v: View, x: number, y: number, w: number, h: number, r: number, fill: string, stroke?: string): void {
  rr(c, v, x, y, w, h, r);
  c.fillStyle = fill;
  c.fill();
  if (stroke) {
    c.strokeStyle = stroke;
    c.lineWidth = Math.max(1, v.z * 0.45);
    c.stroke();
  }
}
function ell(c: Ctx, v: View, x: number, y: number, rx: number, ry: number, fill: string, stroke?: string): void {
  c.beginPath();
  c.ellipse(v.ox + x * v.z, v.oy + y * v.z, rx * v.z, ry * v.z, 0, 0, Math.PI * 2);
  c.fillStyle = fill;
  c.fill();
  if (stroke) {
    c.strokeStyle = stroke;
    c.lineWidth = Math.max(1, v.z * 0.45);
    c.stroke();
  }
}
function tri(c: Ctx, v: View, pts: Array<[number, number]>, fill: string, stroke?: string): void {
  c.beginPath();
  pts.forEach(([x, y], i) => (i ? c.lineTo(v.ox + x * v.z, v.oy + y * v.z) : c.moveTo(v.ox + x * v.z, v.oy + y * v.z)));
  c.closePath();
  c.fillStyle = fill;
  c.fill();
  if (stroke) {
    c.strokeStyle = stroke;
    c.lineWidth = Math.max(1, v.z * 0.45);
    c.lineJoin = 'round';
    c.stroke();
  }
}
function line(c: Ctx, v: View, x1: number, y1: number, x2: number, y2: number, color: string, width: number): void {
  c.beginPath();
  c.moveTo(v.ox + x1 * v.z, v.oy + y1 * v.z);
  c.lineTo(v.ox + x2 * v.z, v.oy + y2 * v.z);
  c.strokeStyle = color;
  c.lineWidth = width * v.z;
  c.lineCap = 'round';
  c.stroke();
}

// ── backdrop: the floor the rooms stand on (user's pick: solid / wood / tile × colour) ──

export type FloorStyle = 'solid' | 'wood' | 'tile';
export interface FloorChoice {
  style: FloorStyle;
  color: string;
}
export const FLOOR_STYLES: Array<{ style: FloorStyle; name: string }> = [
  { style: 'solid', name: '단색' },
  { style: 'wood', name: '마루' },
  { style: 'tile', name: '타일' },
];
export const FLOOR_COLORS: Record<FloorStyle, Array<{ key: string; name: string; hex: string }>> = {
  solid: [
    { key: 'cream', name: '크림', hex: '#f3efe8' },
    { key: 'sky', name: '하늘', hex: '#e3edfb' },
    { key: 'mint', name: '민트', hex: '#e0f3ea' },
    { key: 'lilac', name: '라벤더', hex: '#ebe5fa' },
    { key: 'peach', name: '복숭아', hex: '#fbe6dc' },
    { key: 'gray', name: '그레이', hex: '#e7e9ef' },
  ],
  wood: [
    { key: 'maple', name: '메이플', hex: '#f0d9b3' },
    { key: 'oak', name: '오크', hex: '#dfb784' },
    { key: 'cherry', name: '체리', hex: '#d29a74' },
    { key: 'walnut', name: '월넛', hex: '#a7795a' },
    { key: 'white', name: '화이트워시', hex: '#ece5da' },
    { key: 'gray', name: '그레이', hex: '#cfc9c2' },
  ],
  tile: [
    { key: 'white', name: '화이트', hex: '#f7f7f9' },
    { key: 'gray', name: '그레이', hex: '#dde0e8' },
    { key: 'sky', name: '하늘', hex: '#d8e6fa' },
    { key: 'mint', name: '민트', hex: '#d3eee2' },
    { key: 'pink', name: '핑크', hex: '#f8dbe3' },
    { key: 'terra', name: '테라코타', hex: '#e9bfa3' },
  ],
};
const FLOOR_KEY = 'agenthub.office.floor';
const DEFAULT_FLOOR: FloorChoice = { style: 'wood', color: 'maple' };

function loadFloor(): FloorChoice {
  try {
    const f = JSON.parse(localStorage.getItem(FLOOR_KEY) ?? 'null') as FloorChoice | null;
    if (f && FLOOR_COLORS[f.style]?.some((x) => x.key === f.color)) return f;
  } catch {
    /* default */
  }
  return DEFAULT_FLOOR;
}
let floor = loadFloor();

export function getFloor(): FloorChoice {
  return floor;
}
export function setFloor(f: FloorChoice): void {
  floor = f;
  try {
    localStorage.setItem(FLOOR_KEY, JSON.stringify(f));
  } catch {
    /* lasts for this page */
  }
}

/** Blend two #rrggbb colours (t = share of b). */
function mix(a: string, b: string, t: number): string {
  const p = (h: string, i: number) => parseInt(h.slice(1 + i * 2, 3 + i * 2), 16);
  const ch = (i: number) => Math.round(p(a, i) * (1 - t) + p(b, i) * t).toString(16).padStart(2, '0');
  return `#${ch(0)}${ch(1)}${ch(2)}`;
}

const PAT_RES = 4; // pattern pixels per world unit — crisp up to 4× zoom, scaled beyond
const patterns = new Map<string, CanvasPattern | string>();

/** The backdrop paint for a choice: a flat colour or a repeating wood/tile pattern (world-unit scale PAT_RES). */
function backdropPaint(c: Ctx, f: FloorChoice, dark: boolean): CanvasPattern | string {
  const key = `${f.style}:${f.color}:${dark}`;
  const hit = patterns.get(key);
  if (hit) return hit;
  const raw = FLOOR_COLORS[f.style].find((x) => x.key === f.color)?.hex ?? '#eeeeee';
  const base = dark ? mix(raw, '#1b2036', 0.74) : raw;
  const ink = dark ? '#000000' : '#3a3046';
  let paint: CanvasPattern | string = base;
  if (f.style !== 'solid') {
    const uw = f.style === 'wood' ? 96 : 32; // world units per repeat
    const uh = f.style === 'wood' ? 16 : 32;
    const cv = document.createElement('canvas');
    cv.width = uw * PAT_RES;
    cv.height = uh * PAT_RES;
    const g = cv.getContext('2d');
    if (g) {
      g.scale(PAT_RES, PAT_RES);
      g.fillStyle = base;
      g.fillRect(0, 0, uw, uh);
      if (f.style === 'wood') {
        // two staggered rows of planks, each plank its own tone, a little grain, soft seams
        const rows: number[][] = [
          [0, 56, 96],
          [-16, 24, 80],
        ];
        const tones = [0, 0.05, 0.02, 0.07];
        rows.forEach((seams, ri) => {
          const y = ri * 8;
          for (let i = 0; i < seams.length - 1; i++) {
            const x0 = Math.max(0, seams[i]);
            const x1 = seams[i + 1];
            g.fillStyle = mix(base, ink, tones[(i + ri * 2) % tones.length]);
            g.fillRect(x0, y, x1 - x0, 8);
            g.fillStyle = mix(base, ink, 0.1);
            for (const gy of [2.2, 5.1]) g.fillRect(x0 + 3 + ((i * 7 + ri * 5) % 9), y + gy, (x1 - x0) * 0.45, 0.25);
          }
          if (ri === 1) {
            g.fillStyle = mix(base, ink, tones[2]); // same plank as 0–24: it wraps round the repeat
            g.fillRect(80, y, 16, 8);
          }
          g.fillStyle = mix(base, ink, 0.22);
          g.fillRect(0, y + 7.6, uw, 0.4);
          for (const sx of seams) if (sx >= 0 && sx < uw) g.fillRect(sx, y, 0.45, 8);
        });
      } else {
        // 2×2 tiles: a faint checker, a highlight on each tile's upper edge, grout lines
        g.fillStyle = mix(base, '#ffffff', dark ? 0.04 : 0.35);
        g.fillRect(0, 0, 16, 16);
        g.fillRect(16, 16, 16, 16);
        g.fillStyle = mix(base, '#ffffff', dark ? 0.08 : 0.5);
        for (const [tx, ty] of [
          [0, 0],
          [16, 0],
          [0, 16],
          [16, 16],
        ])
          g.fillRect(tx + 1.2, ty + 1.2, 13.6, 0.6);
        g.fillStyle = mix(base, ink, dark ? 0.3 : 0.16);
        for (const p of [0, 16]) {
          g.fillRect(p, 0, 0.7, uh);
          g.fillRect(0, p, uw, 0.7);
        }
      }
      paint = c.createPattern(cv, 'repeat') ?? base;
    }
  }
  patterns.set(key, paint);
  return paint;
}

/** Fill a canvas area with the chosen floor, aligned to the world grid (v maps world → device px). */
export function drawBackdrop(c: Ctx, v: View, w: number, h: number, f: FloorChoice = floor, dark = isDark()): void {
  const paint = backdropPaint(c, f, dark);
  if (typeof paint !== 'string') paint.setTransform(new DOMMatrix([v.z / PAT_RES, 0, 0, v.z / PAT_RES, v.ox, v.oy]));
  c.fillStyle = paint;
  c.fillRect(0, 0, w, h);
}

// ── floors & walls ───────────────────────────────────────────────────────────

function drawFloor(c: Ctx, v: View, os: OfficeState, th: Theme): void {
  const L = os.getLayout();
  const dark = isDark();
  for (let r = 0; r < L.rows; r++) {
    for (let col = 0; col < L.cols; col++) {
      const t = L.tiles[r * L.cols + col];
      if (t === TileType.VOID || t === TileType.WALL) continue;
      const label = L.areaTiles?.[r * L.cols + col];
      if (!label) continue; // hallway: the backdrop floor (drawBackdrop) shows through
      const p = label ? ROOM_PAINT[label]?.[dark ? 'dark' : 'light'] : undefined;
      const x = col * T;
      const y = r * T;
      c.fillStyle = p ? p.floor : th.hall2;
      c.fillRect(v.ox + x * v.z, v.oy + y * v.z, T * v.z + 1, T * v.z + 1);
      // gentle per-room pattern
      const alt = p ? p.floor2 : th.hall2;
      c.fillStyle = alt;
      if (label === '자료실') {
        c.fillRect(v.ox + x * v.z, v.oy + (y + T - 1) * v.z, T * v.z + 1, v.z); // long planks
        if ((col + r * 3) % 4 === 0) c.fillRect(v.ox + (x + 8) * v.z, v.oy + y * v.z, v.z * 0.8, T * v.z);
      } else if (label === '사무실') {
        c.fillRect(v.ox + x * v.z, v.oy + (y + T - 0.6) * v.z, T * v.z + 1, 0.6 * v.z); // soft grid
        c.fillRect(v.ox + (x + T - 0.6) * v.z, v.oy + y * v.z, 0.6 * v.z, T * v.z + 1);
      } else if (label === '상담실') {
        ell(c, v, x + 8, y + 8, 1.1, 1.1, alt); // polka dots
      } else if (label === '작업실') {
        if ((col + r) % 2 === 0) c.fillRect(v.ox + x * v.z, v.oy + y * v.z, T * v.z + 1, T * v.z + 1); // checker
      } else if (label === '휴게실') {
        if (r % 2 === 0) c.fillRect(v.ox + x * v.z, v.oy + (y + 7) * v.z, T * v.z + 1, 2 * v.z); // stripes
      }
    }
  }
}

function isWall(os: OfficeState, col: number, row: number): boolean {
  const L = os.getLayout();
  if (col < 0 || row < 0 || col >= L.cols || row >= L.rows) return false;
  return L.tiles[row * L.cols + col] === TileType.WALL;
}

const WALL_H = 10; // how far a wall rises above its tile (world units)

function drawWall(c: Ctx, v: View, os: OfficeState, col: number, row: number, th: Theme): void {
  const x = col * T;
  const y = row * T;
  const south = isWall(os, col, row + 1);
  const top = y - WALL_H;
  // front face (only where the wall's south side is open)
  if (!south) {
    c.fillStyle = th.wallFace;
    c.fillRect(v.ox + x * v.z, v.oy + (top + T * 0.55) * v.z, T * v.z + 1, (T + WALL_H - T * 0.55) * v.z);
    c.fillStyle = th.shadow;
    c.fillRect(v.ox + x * v.z, v.oy + (y + T) * v.z, T * v.z + 1, 2.2 * v.z); // contact shadow on the floor
    c.fillStyle = th.wallEdge;
    c.fillRect(v.ox + x * v.z, v.oy + (y + T - 1.2) * v.z, T * v.z + 1, 1.2 * v.z); // skirting
  }
  // top face
  c.fillStyle = th.wallTop;
  c.fillRect(v.ox + x * v.z, v.oy + top * v.z, T * v.z + 1, (south ? T + 0.5 : T * 0.55) * v.z + 1);
  // soft edges where the wall run ends
  c.fillStyle = th.wallEdge;
  if (!isWall(os, col - 1, row)) c.fillRect(v.ox + x * v.z, v.oy + top * v.z, 0.8 * v.z, (T + WALL_H) * v.z);
  if (!isWall(os, col + 1, row)) c.fillRect(v.ox + (x + T - 0.8) * v.z, v.oy + top * v.z, 0.8 * v.z, (T + WALL_H) * v.z);
  if (!isWall(os, col, row - 1)) c.fillRect(v.ox + x * v.z, v.oy + top * v.z, T * v.z + 1, 0.8 * v.z);
}

// ── furniture ────────────────────────────────────────────────────────────────

const BOOK_COLORS = ['#ff8fa8', '#7ea6ff', '#ffd66b', '#7fdcbe', '#b8a2ff', '#ffa77a'];

function drawFurniture(c: Ctx, v: View, type: string, col: number, row: number, th: Theme, on: boolean, t: number): void {
  const base = type.split(':')[0];
  const mirrored = type.endsWith(':left');
  const entry = getCatalogEntry(base);
  const fw = entry?.footprintW ?? 1;
  const fh = entry?.footprintH ?? 1;
  const x = col * T;
  const y = row * T;
  const W = fw * T;
  const H = fh * T;
  const o = th.outline;
  const shadow = (w: number, cx = x + W / 2, cy = y + H - 1) => ell(c, v, cx, cy, w / 2, 2, th.shadow);

  switch (base.replace(/_(FRONT|BACK|SIDE|ON_\d|OFF)$/, '').replace(/_(FRONT|BACK|SIDE)$/, '')) {
    case 'TABLE': // big reading/work table
      shadow(W - 2);
      box(c, v, x + 1, y + 13, W - 2, H - 16, 3, th.wood, o);
      box(c, v, x + 1, y + H - 5, W - 2, 3, 1.5, th.woodDark);
      return;
    case 'DESK':
      shadow(W - 2);
      box(c, v, x + 1, y + 7, W - 2, 10, 2.5, th.wood, o);
      box(c, v, x + 2, y + 17, W - 4, 9, 2, th.woodDark);
      box(c, v, x + 5, y + 19, 10, 4, 1.5, th.woodLight);
      box(c, v, x + W - 15, y + 19, 10, 4, 1.5, th.woodLight);
      return;
    case 'COUNTER':
      shadow(W - 2);
      box(c, v, x + 1, y + 8, W - 2, 8, 3, '#ffffff', o);
      box(c, v, x + 1, y + 15, W - 2, 13, 3, '#c9b8ff', o);
      ell(c, v, x + W / 2, y + 22, 3, 2.4, '#ffffff');
      return;
    case 'SMALL_TABLE':
    case 'COFFEE_TABLE':
      shadow(W - 4);
      ell(c, v, x + W / 2, y + H / 2 + 2, W / 2 - 2, H / 2 - 4, th.woodDark);
      ell(c, v, x + W / 2, y + H / 2, W / 2 - 2, H / 2 - 4, th.wood, o);
      return;
    case 'PC': {
      const side = base.includes('SIDE');
      const back = base.includes('BACK');
      const glow = on || base.includes('_ON_');
      if (side) {
        box(c, v, x + (mirrored ? 9 : 4), y + 9, 3, 12, 1.5, '#e9e4f5', o);
        return;
      }
      box(c, v, x + 2, y + 6, 12, 10, 2.5, '#fff8ec', o); // cat-ear monitor
      tri(c, v, [[x + 3, y + 7], [x + 4, y + 3], [x + 7, y + 6]], '#fff8ec', o);
      tri(c, v, [[x + 9, y + 6], [x + 12, y + 3], [x + 13, y + 7]], '#fff8ec', o);
      if (!back) {
        box(c, v, x + 3.5, y + 7.5, 9, 7, 1.5, glow ? th.screen : th.screenOff);
        if (glow) {
          const k = Math.floor(t * 2) % 3;
          line(c, v, x + 5, y + 9.5, x + 9 + k, y + 9.5, '#ffffff', 0.8);
          line(c, v, x + 5, y + 11.5, x + 8 + ((k + 1) % 3), y + 11.5, '#ff9db4', 0.8);
        }
      }
      box(c, v, x + 7, y + 16, 2, 3, 0.5, '#e6dccb');
      box(c, v, x + 4.5, y + 18.5, 7, 1.6, 0.8, '#e6dccb');
      return;
    }
    case 'WOODEN_CHAIR':
    case 'CUSHIONED_CHAIR':
    case 'WOODEN_BENCH': {
      const cushion = base.startsWith('CUSHIONED') ? '#9cbcff' : th.wood;
      const front = base.includes('FRONT');
      const backSide = base.includes('BACK');
      const ySeat = y + H - 9;
      shadow(10, x + 8, y + H - 1.5);
      if (base.includes('SIDE')) {
        const bx = mirrored ? x + 11 : x + 2;
        box(c, v, bx, ySeat - 8, 3, 11, 1.5, th.woodDark, o); // backrest
        box(c, v, x + 2, ySeat, 12, 4, 2, cushion, o);
      } else if (backSide) {
        box(c, v, x + 2, ySeat, 12, 4, 2, cushion, o);
        box(c, v, x + 3, ySeat - 5, 10, 7, 2, th.woodDark, o); // backrest in front of the sitter
      } else {
        if (front) box(c, v, x + 3, ySeat - 7, 10, 7, 2, th.woodDark, o);
        box(c, v, x + 2, ySeat, 12, 4, 2, cushion, o);
      }
      line(c, v, x + 4, ySeat + 4, x + 4, y + H - 2, th.woodDark, 1.2);
      line(c, v, x + 12, ySeat + 4, x + 12, y + H - 2, th.woodDark, 1.2);
      return;
    }
    case 'CUSHIONED_BENCH': // cat-ear pouf
      shadow(12, x + 8, y + 14);
      tri(c, v, [[x + 2.5, y + 6], [x + 3.5, y + 1.5], [x + 6.5, y + 4.5]], '#ffb3c6', o);
      tri(c, v, [[x + 9.5, y + 4.5], [x + 12.5, y + 1.5], [x + 13.5, y + 6]], '#ffb3c6', o);
      ell(c, v, x + 8, y + 9, 6.5, 5, '#ffb3c6', o);
      ell(c, v, x + 8, y + 7.5, 4.5, 2.4, '#ffd3de');
      return;
    case 'BEANBAG':
      shadow(12, x + 8, y + 14);
      ell(c, v, x + 8, y + 9.5, 6.5, 5, '#c9b8ff', o);
      ell(c, v, x + 8, y + 7.5, 4.5, 2.5, '#e6dcff');
      return;
    case 'SOFA': {
      shadow(W - 2);
      const pink = '#ff9db4';
      const pinkL = '#ffc2d1';
      if (base.includes('SIDE')) {
        box(c, v, x + (mirrored ? 9 : 2), y + 2, 5, H - 4, 3, pink, o);
        box(c, v, x + 3, y + 5, 10, H - 10, 3, pinkL, o);
      } else {
        box(c, v, x + 1, y + (base.includes('BACK') ? 5 : 1), W - 2, 6, 3, pink, o);
        box(c, v, x + 2, y + (base.includes('BACK') ? 2 : 6), W - 4, 7, 3, pinkL, o);
      }
      return;
    }
    case 'DOUBLE_BOOKSHELF':
    case 'BOOKSHELF': {
      const shelves = base === 'DOUBLE_BOOKSHELF' ? 2 : 1;
      const top = y + H - (shelves === 2 ? 26 : 12);
      box(c, v, x + 1, top, W - 2, H - (top - y) - 1, 2, th.woodDark, o);
      for (let s = 0; s < shelves; s++) {
        const by = top + 2 + s * 12;
        let bx = x + 3;
        let i = col + s * 3;
        while (bx < x + W - 5) {
          const bw = 2.2 + (i % 3) * 0.6;
          const bh = 7 + (i % 4);
          box(c, v, bx, by + 9 - bh, bw, bh, 0.6, BOOK_COLORS[i % BOOK_COLORS.length]);
          bx += bw + 0.6;
          i++;
        }
        box(c, v, x + 2, by + 9, W - 4, 1.4, 0.4, th.wood);
      }
      return;
    }
    case 'WHITEBOARD':
      box(c, v, x + 1, y + 4, W - 2, H - 9, 2, '#ffffff', o);
      box(c, v, x + 4, y + 7, 6, 5, 1, '#ffe08a');
      box(c, v, x + 12, y + 7, 6, 5, 1, '#ffb3c6');
      box(c, v, x + 4, y + 14, 6, 5, 1, '#a7ebd3');
      line(c, v, x + 20, y + 10, x + 27, y + 10, '#7ea6ff', 0.9);
      line(c, v, x + 20, y + 14, x + 25, y + 14, '#7ea6ff', 0.9);
      return;
    case 'CLOCK': {
      ell(c, v, x + 8, y + 12, 5.5, 5.5, '#4f4a66', o);
      tri(c, v, [[x + 3.5, y + 9], [x + 4.5, y + 4.5], [x + 7, y + 7.5]], '#4f4a66');
      tri(c, v, [[x + 9, y + 7.5], [x + 11.5, y + 4.5], [x + 12.5, y + 9]], '#4f4a66');
      ell(c, v, x + 8, y + 12, 4, 4, '#ffffff');
      const a = (t / 4) % (Math.PI * 2);
      line(c, v, x + 8, y + 12, x + 8 + Math.cos(a) * 3, y + 12 + Math.sin(a) * 3, '#4f4a66', 0.6);
      line(c, v, x + 8, y + 12, x + 8, y + 9.5, '#ff7f9c', 0.6);
      return;
    }
    case 'LARGE_PAINTING':
    case 'SMALL_PAINTING':
    case 'SMALL_PAINTING_2': {
      const big = base === 'LARGE_PAINTING';
      const fx = x + 1.5;
      const fy = y + (big ? 3 : 6);
      const fw2 = W - 3;
      const fh2 = big ? 20 : 16;
      box(c, v, fx, fy, fw2, fh2, 1.5, th.woodDark, o);
      box(c, v, fx + 1.5, fy + 1.5, fw2 - 3, fh2 - 3, 1, big ? '#cfe6ff' : base === 'SMALL_PAINTING' ? '#ffd0dc' : '#d6e6ff');
      if (big) {
        ell(c, v, fx + fw2 - 6, fy + 6, 2.5, 2.5, '#ffd66b');
        ell(c, v, fx + 8, fy + fh2 - 2, 9, 4, '#8edc8a');
      } else if (base === 'SMALL_PAINTING') {
        ell(c, v, fx + fw2 / 2, fy + fh2 / 2 + 1, 3.5, 3, '#ffb35c'); // cat portrait
        tri(c, v, [[fx + fw2 / 2 - 3.5, fy + fh2 / 2], [fx + fw2 / 2 - 2.5, fy + 4], [fx + fw2 / 2 - 0.5, fy + fh2 / 2 - 1.5]], '#ffb35c');
        tri(c, v, [[fx + fw2 / 2 + 0.5, fy + fh2 / 2 - 1.5], [fx + fw2 / 2 + 2.5, fy + 4], [fx + fw2 / 2 + 3.5, fy + fh2 / 2]], '#ffb35c');
      } else {
        ell(c, v, fx + fw2 / 2, fy + fh2 / 2 + 1.5, 2.6, 2.2, '#ffffff'); // paw print
        for (const [dx, dy] of [[-3, -1.5], [-1, -3.2], [1.2, -3.2], [3.2, -1.5]])
          ell(c, v, fx + fw2 / 2 + dx, fy + fh2 / 2 + dy, 1, 1.1, '#ffffff');
      }
      return;
    }
    case 'HANGING_PLANT':
      line(c, v, x + 5, y + 2, x + 8, y + 10, '#c9b39a', 0.5);
      line(c, v, x + 11, y + 2, x + 8, y + 10, '#c9b39a', 0.5);
      ell(c, v, x + 8, y + 13, 4, 3, '#ffb3c6', o);
      ell(c, v, x + 8, y + 10.5, 4, 2, '#7fcf86');
      line(c, v, x + 5, y + 13, x + 4, y + 21, '#5fbf6a', 0.9);
      line(c, v, x + 11, y + 13, x + 12, y + 19, '#5fbf6a', 0.9);
      return;
    case 'PLANT':
    case 'PLANT_2':
    case 'LARGE_PLANT':
    case 'CACTUS':
    case 'POT': {
      const big = base === 'LARGE_PLANT';
      const potW = big ? 14 : base === 'POT' ? 8 : 8;
      const potH = big ? 10 : base === 'POT' ? 6 : 7;
      const cx = x + W / 2;
      const bottom = y + H - 1.5;
      shadow(potW + 2, cx, bottom);
      if (base === 'CACTUS') {
        box(c, v, cx - 3, bottom - potH - 13, 6, 14, 3, '#6fd0ae', o);
        box(c, v, cx - 7, bottom - potH - 9, 3, 6, 1.5, '#6fd0ae', o);
        ell(c, v, cx, bottom - potH - 13, 1.6, 1.4, '#ff8fa8');
      } else if (base === 'POT') {
        ell(c, v, cx - 2, bottom - potH - 3, 1.8, 1.8, '#ff8fa8');
        ell(c, v, cx + 2, bottom - potH - 2, 1.8, 1.8, '#ffd66b');
        ell(c, v, cx, bottom - potH - 5, 1.8, 1.8, '#b8a2ff');
      } else {
        const r = big ? 9 : 6;
        ell(c, v, cx - r * 0.45, bottom - potH - r, r * 0.8, r * 0.75, '#5fbf6a', o);
        ell(c, v, cx + r * 0.45, bottom - potH - r * 1.1, r * 0.8, r * 0.75, '#7fcf86', o);
        ell(c, v, cx, bottom - potH - r * 1.6, r * 0.75, r * 0.7, '#9be0a0', o);
      }
      const potColor = big ? '#c9b8ff' : base === 'PLANT_2' ? '#9cbcff' : base === 'POT' ? '#a7ebd3' : '#ffd66b';
      box(c, v, cx - potW / 2, bottom - potH, potW, potH, 2, potColor, o);
      return;
    }
    case 'CAT_TOWER':
      shadow(W - 4);
      box(c, v, x + 7, y + 10, 3, H - 14, 1, '#e9d6b8', o);
      box(c, v, x + 21, y + 20, 3, H - 24, 1, '#e9d6b8', o);
      box(c, v, x + 2, y + 8, 16, 4, 2, '#c9b8ff', o);
      box(c, v, x + 15, y + 18, 15, 4, 2, '#ffb3c6', o);
      box(c, v, x + 1, y + H - 6, W - 2, 5, 2.5, '#b8a2ff', o);
      ell(c, v, x + 10, y + 6, 4.5, 3, '#ffb35c', o); // a napping kitten
      tri(c, v, [[x + 6.5, y + 5], [x + 7, y + 1.5], [x + 9, y + 4]], '#ffb35c');
      tri(c, v, [[x + 11, y + 4], [x + 13, y + 1.5], [x + 13.5, y + 5]], '#ffb35c');
      return;
    case 'FLOOR_LAMP':
      shadow(8, x + 8, y + H - 2);
      line(c, v, x + 8, y + 12, x + 8, y + H - 3, '#d8cfe8', 1);
      tri(c, v, [[x + 3, y + 12], [x + 8, y + 4], [x + 13, y + 12]], '#ffe08a', o);
      if (isDark()) ell(c, v, x + 8, y + 16, 7, 5, 'rgba(255, 224, 138, 0.18)');
      return;
    case 'COFFEE':
      box(c, v, x + 4.5, y + 6, 6.5, 6.5, 2, '#ffffff', o);
      ell(c, v, x + 7.75, y + 6.5, 2.8, 1, '#b98157');
      return;
    case 'CALL_BELL':
      ell(c, v, x + 8, y + 10, 4.5, 3.5, '#ffd66b', o);
      box(c, v, x + 2.5, y + 12.5, 11, 2, 1, '#d8cfe8');
      return;
    case 'BIN':
      box(c, v, x + 4, y + 4, 8, 10, 2.5, '#a7ebd3', o);
      return;
    default:
      box(c, v, x + 1, y + 1, W - 2, H - 2, 3, th.wood, o);
  }
}

// ── characters ───────────────────────────────────────────────────────────────

function drawCharacter(c: Ctx, v: View, ch: Character, t: number, selected: boolean): void {
  const p = CAST[ch.palette % CAST.length];
  const o = 'rgba(58, 47, 74, 0.55)';
  const sitting = ch.state === CharacterState.TYPE;
  const walking = ch.state === CharacterState.WALK;
  const scale = ch.isSubagent ? 1.05 : 1.3;
  const x = ch.x;
  const step = walking ? Math.sin(t * 12 + ch.id) : 0;
  const bob = walking ? Math.abs(step) * -1.2 : Math.sin(t * 2 + ch.id) * 0.3;
  const gy = ch.y - 7; // engine anchors characters at the feet
  const y = gy + (sitting ? 2 : 0) + bob;
  const s = (n: number) => n * scale;
  const dir = ch.dir;
  const alpha = ch.matrixEffect ? Math.min(1, ch.matrixEffectTimer / 0.3) : 1;
  c.save();
  c.globalAlpha = ch.matrixEffect === 'despawn' ? 1 - alpha : ch.matrixEffect === 'spawn' ? alpha : 1;
  if (ch.hueShift) c.filter = `hue-rotate(${ch.hueShift}deg)`;

  ell(c, v, x, gy + 7, s(5.5), s(1.8), 'rgba(44, 51, 84, 0.16)'); // ground shadow
  if (selected) ell(c, v, x, gy + 7, s(7.5), s(2.8), 'rgba(106, 141, 255, 0.35)');

  // tail
  if (dir !== Direction.DOWN || p.kind === 'cat') {
    const tx = dir === Direction.LEFT ? x + s(5) : x - s(5);
    const wag = Math.sin(t * 4 + ch.id) * 1.5;
    c.beginPath();
    c.moveTo(v.ox + tx * v.z, v.oy + (y + s(1)) * v.z);
    c.quadraticCurveTo(v.ox + (tx + (dir === Direction.LEFT ? 3 : -3)) * v.z, v.oy + (y - s(3)) * v.z, v.ox + (tx + wag) * v.z, v.oy + (y - s(7)) * v.z);
    c.strokeStyle = p.fur2;
    c.lineWidth = s(2.2) * v.z;
    c.lineCap = 'round';
    c.stroke();
  }
  // legs / feet
  if (!sitting) {
    ell(c, v, x - s(2.2), y + s(5.2) + (step > 0 ? -1 : 0), s(1.8), s(1.4), p.fur2);
    ell(c, v, x + s(2.2), y + s(5.2) + (step < 0 ? -1 : 0), s(1.8), s(1.4), p.fur2);
  }
  // body (hoodie)
  box(c, v, x - s(4.6), y - s(3.5), s(9.2), s(8.5), s(4), p.hood, o);
  if (dir === Direction.DOWN) box(c, v, x - s(2.2), y - s(0.5), s(4.4), s(3), s(1.5), 'rgba(255,255,255,0.35)'); // pocket
  // paws
  const reading = sitting && /^(Read|Grep|Glob|WebFetch|WebSearch)$/.test(ch.currentTool ?? '');
  if (reading && dir !== Direction.UP) {
    box(c, v, x - s(4), y - s(2.5), s(8), s(5), s(1), '#ff9db4', o); // a book
    box(c, v, x - s(3.2), y - s(2), s(3), s(4), s(0.6), '#ffffff');
    box(c, v, x + s(0.2), y - s(2), s(3), s(4), s(0.6), '#ffffff');
  } else if (sitting && dir !== Direction.UP) {
    const tap = Math.sin(t * 14 + ch.id) > 0 ? 0.8 : 0;
    ell(c, v, x - s(2.5), y - s(0.5) - tap, s(1.5), s(1.2), p.fur);
    ell(c, v, x + s(2.5), y - s(0.5) - (0.8 - tap), s(1.5), s(1.2), p.fur);
  } else if (dir !== Direction.UP) {
    ell(c, v, x - s(4.8), y + s(1) + step * 0.6, s(1.5), s(1.3), p.fur);
    ell(c, v, x + s(4.8), y + s(1) - step * 0.6, s(1.5), s(1.3), p.fur);
  }
  // head
  const hx = x + (dir === Direction.RIGHT ? s(0.8) : dir === Direction.LEFT ? -s(0.8) : 0);
  const hy = y - s(9);
  // ears behind the head
  if (p.kind === 'cat' || p.ears === 'point') {
    tri(c, v, [[hx - s(5.5), hy - s(1)], [hx - s(4.2), hy - s(7.5)], [hx - s(0.8), hy - s(4.2)]], p.fur, o);
    tri(c, v, [[hx + s(0.8), hy - s(4.2)], [hx + s(4.2), hy - s(7.5)], [hx + s(5.5), hy - s(1)]], p.fur, o);
    if (dir !== Direction.UP) {
      tri(c, v, [[hx - s(4.4), hy - s(2)], [hx - s(3.8), hy - s(5.6)], [hx - s(2), hy - s(3.6)]], '#ffb3c6');
      tri(c, v, [[hx + s(2), hy - s(3.6)], [hx + s(3.8), hy - s(5.6)], [hx + s(4.4), hy - s(2)]], '#ffb3c6');
    }
  }
  ell(c, v, hx, hy, s(6.2), s(5.6), p.fur, o);
  if (p.ears === 'flop') {
    const ec = p.earc ?? p.fur2;
    if (dir === Direction.DOWN || dir === Direction.UP) {
      ell(c, v, hx - s(5.6), hy + s(0.5), s(1.8), s(3.8), ec, o);
      ell(c, v, hx + s(5.6), hy + s(0.5), s(1.8), s(3.8), ec, o);
    } else {
      ell(c, v, hx + (dir === Direction.RIGHT ? -s(2.5) : s(2.5)), hy + s(0.3), s(1.9), s(4), ec, o);
    }
  }
  if (p.mark === 'tabby') {
    for (const dx of [-1.8, 0, 1.8]) line(c, v, hx + s(dx), hy - s(5), hx + s(dx), hy - s(3.2), p.fur2, s(0.9));
  }
  // face
  const blink = Math.sin(t * 0.9 + ch.id * 7) > 0.985;
  if (dir === Direction.DOWN) {
    if (p.mark === 'muzzle') ell(c, v, hx, hy + s(2.2), s(3.2), s(2.2), p.belly);
    for (const ex of [-2.4, 2.4]) {
      if (blink) line(c, v, hx + s(ex) - s(0.8), hy + s(0.3), hx + s(ex) + s(0.8), hy + s(0.3), p.eye, s(0.6));
      else {
        ell(c, v, hx + s(ex), hy + s(0.2), s(0.95), s(1.2), p.eye);
        ell(c, v, hx + s(ex) + s(0.3), hy - s(0.3), s(0.35), s(0.35), '#ffffff');
      }
    }
    ell(c, v, hx - s(4), hy + s(1.9), s(1.1), s(0.6), 'rgba(255, 143, 168, 0.6)');
    ell(c, v, hx + s(4), hy + s(1.9), s(1.1), s(0.6), 'rgba(255, 143, 168, 0.6)');
    ell(c, v, hx, hy + s(1.6), s(0.8), s(0.55), p.kind === 'dog' ? '#4a3c55' : '#ff7f9c');
  } else if (dir !== Direction.UP) {
    const f = dir === Direction.RIGHT ? 1 : -1;
    if (p.kind === 'dog') ell(c, v, hx + f * s(5.2), hy + s(1.8), s(2.2), s(1.7), p.mark === 'muzzle' ? p.belly : p.fur, o);
    ell(c, v, hx + f * s(6.6), hy + s(1.3), s(0.7), s(0.55), p.kind === 'dog' ? '#4a3c55' : '#ff7f9c');
    if (blink) line(c, v, hx + f * s(2.4), hy + s(0.3), hx + f * s(3.8), hy + s(0.3), p.eye, s(0.6));
    else ell(c, v, hx + f * s(3), hy + s(0.2), s(0.9), s(1.15), p.eye);
    ell(c, v, hx + f * s(2.6), hy + s(2.2), s(1), s(0.55), 'rgba(255, 143, 168, 0.6)');
  }
  c.restore();
}

function drawPet(c: Ctx, v: View, pet: Pet, t: number, th: Theme): void {
  const hamster = pet.petType === 0;
  const hop = Math.abs(Math.sin(t * 8 + pet.x)) * 0.8;
  const x = pet.x;
  const y = pet.y - 4 - hop;
  ell(c, v, x, pet.y - 1, 4, 1.3, th.shadow);
  if (hamster) {
    ell(c, v, x - 2.6, y - 3.6, 1.3, 1.3, '#ffb3c6');
    ell(c, v, x + 2.6, y - 3.6, 1.3, 1.3, '#ffb3c6');
    ell(c, v, x, y, 4.4, 3.6, '#f7c28c', th.outline);
    ell(c, v, x, y + 1.2, 2.6, 1.9, '#fff4e2');
  } else {
    ell(c, v, x, y, 3.8, 3.5, '#ffe066', th.outline);
    tri(c, v, [[x + 2.8, y - 0.3], [x + 4.8, y + 0.4], [x + 2.8, y + 1.1]], '#ff9f43');
  }
  ell(c, v, x - 1.3, y - 0.8, 0.5, 0.6, '#3a2f4a');
  ell(c, v, x + 1.3, y - 0.8, 0.5, 0.6, '#3a2f4a');
}

/** The roster card's portrait: the same character, front-facing, head and shoulders. */
export function drawMugshot(c: Ctx, palette: number, hueShift: number, side: number): void {
  const ch = { id: 0, palette, hueShift, dir: Direction.DOWN, state: CharacterState.IDLE, x: 0, y: 0 } as Character;
  const z = side / 22;
  c.clearRect(0, 0, side, side);
  drawCharacter(c, { ox: side / 2, oy: side / 2 + 20 * z, z }, ch, 0, false);
}

// ── frame ────────────────────────────────────────────────────────────────────

/** Draw the whole office; returns the map offset (device px) the overlays use. */
export function renderHubFrame(
  c: Ctx,
  width: number,
  height: number,
  os: OfficeState,
  zoom: number,
  panX: number,
  panY: number,
): { offsetX: number; offsetY: number } {
  c.clearRect(0, 0, width, height);
  const L = os.getLayout();
  const { offsetX, offsetY } = mapOffset(width, height, L.cols, L.rows, zoom, panX, panY);
  const v: View = { ox: offsetX, oy: offsetY, z: zoom };
  const th = theme();
  const t = performance.now() / 1000;

  drawBackdrop(c, v, width, height);
  drawFloor(c, v, os, th);

  // Z-sorted drawables: walls, furniture, characters, pets (by their base line)
  const items: Array<{ z: number; draw: () => void }> = [];
  for (let r = 0; r < L.rows; r++) {
    for (let col = 0; col < L.cols; col++) {
      if (L.tiles[r * L.cols + col] === TileType.WALL) items.push({ z: (r + 1) * T - 0.01, draw: () => drawWall(c, v, os, col, r, th) });
    }
  }
  const chars = os.getCharacters();
  const busy = chars.filter((ch) => ch.isActive && ch.state === CharacterState.TYPE);
  for (const f of L.furniture) {
    const entry = getCatalogEntry(f.type.split(':')[0]);
    const fh = entry?.footprintH ?? 1;
    const wallItem = entry?.canPlaceOnWalls === true;
    const on = !wallItem && busy.some((ch) => Math.abs(ch.x - (f.col + 0.5) * T) < T * 1.6 && Math.abs(ch.y - (f.row + fh) * T) < T * 2.2);
    items.push({
      z: wallItem ? (f.row + fh) * T + 0.02 : (f.row + fh) * T - 0.5,
      draw: () => drawFurniture(c, v, f.type, f.col, f.row, th, on, t),
    });
  }
  for (const ch of chars) {
    items.push({ z: ch.y + T / 2, draw: () => drawCharacter(c, v, ch, t, os.selectedAgentId === ch.id || os.hoveredAgentId === ch.id) });
  }
  for (const pet of os.pets) items.push({ z: pet.y, draw: () => drawPet(c, v, pet, t, th) });
  items.sort((a, b) => a.z - b.z);
  for (const it of items) it.draw();
  return { offsetX, offsetY };
}
