"""The office as a kit of five movable rooms (writes office-rooms.json).

    자료실  reading files / searching / the web      사무실  planning, thinking, delegating
    상담실  waiting for you (question / permission)   작업실  editing code, running commands
    휴게실  idle between turns

Each room is a self-contained block — its own walls, doors and furniture — that
the office (src/hub/rooms.ts) places on a hallway floor. Users drag rooms by their
name plates; the hallway around them keeps every arrangement connected.

Block grid: row 0 = wall-item overhang (VOID), row 1 = top wall, last row = bottom
wall, col 0 / last col = side walls. Doors are wall tiles turned into floor.

Also checks the default arrangement: every seat reachable, all floor one region.
"""
import glob
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, '..', 'webview-ui', 'public', 'assets')

WALL, VOID = 0, 255
WALL_COLOR = {'h': 250, 's': 22, 'b': 18, 'c': -25}
DOOR_COLOR = {'h': 40, 's': 30, 'b': 28, 'c': -30}
HALL_FLOOR, HALL_COLOR = 1, {'h': 38, 's': 26, 'b': 34, 'c': -38}     # pattern 0: soft plain
GAP = 1                                                                # hallway between rooms
TOP = 1   # hallway row above everything: a room's wall-item overhang row always connects through it


def room(label, color, floor, fcolor, w, h, desc, doors):
    """A blank room block w x h (incl. walls + overhang row)."""
    tiles, colors = [], []
    for r in range(h):
        for c in range(w):
            if r == 0:
                tiles.append(VOID); colors.append(None)
            elif r == 1 or r == h - 1 or c == 0 or c == w - 1:
                tiles.append(WALL); colors.append(WALL_COLOR)
            else:
                tiles.append(floor); colors.append(fcolor)
    for c, r in doors:
        tiles[r * w + c] = HALL_FLOOR; colors[r * w + c] = DOOR_COLOR
    return {'label': label, 'color': color, 'desc': desc, 'w': w, 'h': h,
            'tiles': tiles, 'colors': colors, 'furniture': []}


def put(rm, t, c, r):
    rm['furniture'].append({'type': t, 'col': c, 'row': r})


def reading_table(rm, tc, tr, pcs=False):
    put(rm, 'TABLE_FRONT', tc, tr)
    if pcs:
        put(rm, 'PC_SIDE', tc, tr); put(rm, 'PC_SIDE', tc, tr + 2)
        put(rm, 'PC_SIDE:left', tc + 2, tr); put(rm, 'PC_SIDE:left', tc + 2, tr + 2)
    put(rm, 'WOODEN_CHAIR_SIDE', tc - 1, tr); put(rm, 'WOODEN_CHAIR_SIDE', tc - 1, tr + 2)
    put(rm, 'WOODEN_CHAIR_SIDE:left', tc + 3, tr); put(rm, 'WOODEN_CHAIR_SIDE:left', tc + 3, tr + 2)


def build():
    H = 12  # 9 interior rows
    lib = room('자료실', '#ffb547', 7, {'h': 32, 's': 55, 'b': 18, 'c': -35}, 15, H,
               '파일을 읽고 코드·파일을 검색하고 웹 자료를 찾아보는 곳',
               [(c, H - 1) for c in (6, 7, 8)] + [(14, r) for r in (8, 9)] + [(0, r) for r in (6, 7)])
    for c in (1, 3, 5, 9, 11):
        put(lib, 'DOUBLE_BOOKSHELF', c, 0)
    put(lib, 'CLOCK', 7, 0); put(lib, 'HANGING_PLANT', 8, 0)
    reading_table(lib, 3, 3); reading_table(lib, 9, 3)
    put(lib, 'COFFEE', 4, 5); put(lib, 'POT', 10, 5)
    put(lib, 'PLANT', 1, 9); put(lib, 'FLOOR_LAMP', 13, 9)

    plan = room('사무실', '#6a8dff', 4, {'h': 212, 's': 50, 'b': 24, 'c': -30}, 15, H,
                '도구 없이 생각·답변을 정리하고, 할 일을 계획하고, 서브에이전트에게 일을 맡기는 곳',
                [(0, r) for r in (8, 9, 10)] + [(14, r) for r in (8, 9, 10)] + [(c, H - 1) for c in (5, 6, 7)])
    put(plan, 'WHITEBOARD', 1, 0); put(plan, 'WHITEBOARD', 4, 0); put(plan, 'CLOCK', 7, 0)
    put(plan, 'LARGE_PAINTING', 9, 0); put(plan, 'SMALL_PAINTING', 12, 0)
    for dc in (2, 6, 10):
        for dr in (3, 7):
            put(plan, 'DESK_FRONT', dc, dr); put(plan, 'PC_FRONT_OFF', dc + 1, dr)
            put(plan, 'CUSHIONED_CHAIR_BACK', dc + 1, dr + 2)
    put(plan, 'PLANT_2', 13, 9)

    wait = room('상담실', '#b29cf5', 6, {'h': 262, 's': 42, 'b': 24, 'c': -30}, 13, H,
                '질문에 대한 답이나 권한 승인을 기다리며 당신을 부르는 곳 — 여기 있는 친구를 클릭해 답해 주세요',
                [(0, r) for r in (8, 9, 10)] + [(c, H - 1) for c in (5, 6, 7)])
    put(wait, 'SMALL_PAINTING_2', 2, 0); put(wait, 'CLOCK', 6, 0); put(wait, 'SMALL_PAINTING', 10, 0)
    put(wait, 'COUNTER', 4, 3); put(wait, 'CALL_BELL', 5, 3)
    for c in (4, 5, 6):
        put(wait, 'CUSHIONED_CHAIR_BACK', c, 5)
    put(wait, 'SOFA_FRONT', 8, 8); put(wait, 'BEANBAG', 3, 9); put(wait, 'PLANT', 11, 2); put(wait, 'POT', 1, 2)

    work = room('작업실', '#2fc7a1', 3, {'h': 158, 's': 42, 'b': 20, 'c': -30}, 23, H,
                '코드를 고치고 파일을 쓰고 명령을 실행하는 곳',
                [(c, 1) for c in (6, 7, 8)] + [(c, 1) for c in (13, 14, 15)] + [(22, r) for r in (6, 7, 8)]
                + [(0, r) for r in (8, 9)])
    put(work, 'WHITEBOARD', 1, 0); put(work, 'BOOKSHELF', 10, 1); put(work, 'SMALL_PAINTING_2', 18, 0)
    for tc in (3, 9, 15):
        reading_table(work, tc, 3, pcs=True)
    put(work, 'BIN', 1, 10); put(work, 'CACTUS', 21, 8); put(work, 'LARGE_PLANT', 19, 7); put(work, 'COFFEE', 16, 6)

    rest = room('휴게실', '#ff7896', 2, {'h': 348, 's': 48, 'b': 24, 'c': -30}, 13, H,
                '턴을 마치고 쉬는 중 — 다음 지시를 기다리는 친구들',
                [(c, 1) for c in (5, 6, 7)] + [(0, r) for r in (6, 7, 8)])
    put(rest, 'LARGE_PAINTING', 3, 0); put(rest, 'HANGING_PLANT', 7, 0); put(rest, 'SMALL_PAINTING', 9, 0)
    put(rest, 'SOFA_FRONT', 4, 4); put(rest, 'SOFA_SIDE', 3, 5); put(rest, 'COFFEE_TABLE', 4, 5)
    put(rest, 'SOFA_SIDE:left', 6, 5); put(rest, 'SOFA_BACK', 4, 7); put(rest, 'COFFEE', 4, 5)
    put(rest, 'CAT_TOWER', 9, 2); put(rest, 'BEANBAG', 2, 9); put(rest, 'BEANBAG', 8, 9)
    put(rest, 'CUSHIONED_BENCH', 10, 7); put(rest, 'FLOOR_LAMP', 11, 5); put(rest, 'PLANT_2', 1, 2)

    # default arrangement (tile coords of each block's top-left), hallway gap between blocks
    lib['x'], lib['y'] = 0, 0
    plan['x'], plan['y'] = lib['w'] + GAP, 0
    wait['x'], wait['y'] = plan['x'] + plan['w'] + GAP, 0
    work['x'], work['y'] = 0, H + GAP
    rest['x'], rest['y'] = work['w'] + GAP, H + GAP
    return {
        'version': 1,
        'hallway': {'floor': HALL_FLOOR, 'color': HALL_COLOR},
        'gap': GAP,
        'top': TOP,
        'rooms': [lib, plan, wait, work, rest],
        'pets': [{'id': 'pet-hamster', 'petType': 0}, {'id': 'pet-chick', 'petType': 1}],
    }


def compose(kit):
    """Python twin of src/hub/rooms.ts compose() — used to check the default arrangement."""
    cols = max(r['x'] + r['w'] for r in kit['rooms'])
    rows = max(r['y'] + r['h'] for r in kit['rooms']) + TOP
    tiles = [kit['hallway']['floor']] * (cols * rows)
    area = [None] * (cols * rows)
    furniture = []
    for i, rm in enumerate(kit['rooms']):
        for r in range(rm['h']):
            for c in range(rm['w']):
                t = rm['tiles'][r * rm['w'] + c]
                if t == VOID:
                    continue
                k = (rm['y'] + TOP + r) * cols + rm['x'] + c
                tiles[k] = t
                if t not in (WALL, kit['hallway']['floor']):
                    area[k] = rm['label']
        for j, f in enumerate(rm['furniture']):
            furniture.append({'uid': f'r{i}-{j}', 'type': f['type'], 'col': rm['x'] + f['col'],
                              'row': rm['y'] + TOP + f['row']})
    return {'cols': cols, 'rows': rows, 'tiles': tiles, 'areaTiles': area, 'furniture': furniture}


def check(d):
    fp = {}
    for m in glob.glob(os.path.join(ASSETS, 'furniture', '*', 'manifest.json')):
        def walk(n, cat=None, bg=0):
            cat = n.get('category', cat)
            bg = n.get('backgroundTiles', bg)          # group values apply to their members
            if 'members' in n:
                for ch in n['members']:
                    walk(ch, cat, bg)
            else:
                fp[n['id']] = (n['footprintW'], n['footprintH'], bg, cat)
        walk(json.load(open(m)))
    blocked, seats = set(), []
    for f in d['furniture']:
        w, h, bg, cat = fp[f['type'].split(':')[0]]
        for dr in range(h):
            for dc in range(w):
                t = (f['col'] + dc, f['row'] + dr)
                if dr >= bg:
                    blocked.add(t)
                if cat == 'chairs' and dr >= bg:
                    seats.append(t)
    cols, rows = d['cols'], d['rows']
    def floor(c, r):
        return 0 <= c < cols and 0 <= r < rows and d['tiles'][r * cols + c] not in (WALL, VOID)
    open_ = {(c, r) for r in range(rows) for c in range(cols) if floor(c, r) and (c, r) not in blocked}
    nb = lambda c, r: [(c + 1, r), (c - 1, r), (c, r + 1), (c, r - 1)]
    trapped = [s for s in seats if not any(n in open_ for n in nb(*s))]
    start = next(iter(open_)); seen = {start}; q = [start]
    while q:
        for n in nb(*q.pop()):
            if n in open_ and n not in seen:
                seen.add(n); q.append(n)
    per_room = {}
    for s in seats:
        lab = d['areaTiles'][s[1] * cols + s[0]]
        per_room[lab] = per_room.get(lab, 0) + 1
    print(f"{cols}x{rows} | seats per room: {per_room} | trapped: {trapped} | open {len(open_)} reachable {len(seen)}")
    return not trapped and len(seen) == len(open_)


if __name__ == '__main__':
    kit = build()
    for old in glob.glob(os.path.join(ASSETS, 'default-layout-*.json')):
        os.remove(old)
    with open(os.path.join(ASSETS, 'office-rooms.json'), 'w', encoding='utf-8') as fh:
        json.dump(kit, fh, ensure_ascii=False)
    sys.exit(0 if check(compose(kit)) else 1)
