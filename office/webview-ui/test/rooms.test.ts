import assert from 'node:assert/strict';

import { test } from 'vitest';

import { compose, loadKit, resizeRoom } from '../src/hub/rooms.js';

// A 5×4 room: overhang row, top wall, one floor row, bottom wall — and a door (1) in the bottom wall.
const W = 0;
const V = 255;
const F = 7;
const room = {
  label: 'A',
  color: 'white',
  desc: '',
  w: 5,
  h: 4,
  tiles: [V, V, V, V, V, W, W, W, W, W, W, F, F, F, W, W, 1, W, W, W],
  colors: new Array(20).fill(null),
  furniture: [
    { type: 'PLANT', col: 2, row: 2 },
    { type: 'PAINTING', col: 4, row: 1 }, // on the right wall: moves with it
  ],
  x: 0,
  y: 0,
};

test('rooms grow by repeating the last inner column/row, never shrink below design', async () => {
  globalThis.fetch = (async () => ({
    json: async () => ({ hallway: { floor: 1, color: {} }, gap: 1, top: 0, rooms: [room], pets: [] }),
  })) as unknown as typeof fetch;
  const kit = await loadKit();
  assert.ok(kit);

  assert.equal(resizeRoom('A', 3, 3), false); // smaller than designed: stays
  assert.equal(resizeRoom('A', 7, 5), true);
  const r = kit.rooms[0];
  assert.equal(r.w, 7);
  assert.equal(r.h, 5);
  const row = (i: number) => r.tiles.slice(i * 7, i * 7 + 7);
  assert.deepEqual(row(1), [W, W, W, W, W, W, W]); // top wall carries on
  assert.deepEqual(row(2), [W, F, F, F, F, F, W]); // floor widened, right wall moved out
  assert.deepEqual(row(3), [W, F, F, F, F, F, W]); // a new floor row above the bottom wall
  assert.deepEqual(row(4), [W, 1, W, W, W, W, W]); // bottom wall (with its door) moved down
  assert.deepEqual(
    r.furniture.map((f) => [f.type, f.col, f.row]),
    [
      ['PLANT', 2, 2],
      ['PAINTING', 6, 1],
    ],
  );
  assert.equal(compose(kit).cols, 7);
});
