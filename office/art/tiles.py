"""Floors (16x16) and walls (64x128 bitmask sheet) — greyscale, colourised per tile by the
engine (Photoshop-style Colorize), so only luminance matters here. Plus the two pets."""
from draw import Canvas, grey

O = '#463a5c'


def _floor(fn):
    c = Canvas(16, 16)
    for y in range(16):
        for x in range(16):
            c.px(x, y, grey(fn(x, y)))
    return c


FLOORS = [
    lambda x, y: 214 - (3 if (x * 7 + y * 3) % 11 == 0 else 0),                         # 0 soft plain
    lambda x, y: 168 if y % 8 == 7 else (206 if (x + (y // 8) * 6) % 16 != 0 else 182),  # 1 wide planks
    lambda x, y: 222 if ((x // 8) + (y // 8)) % 2 == 0 else 196,                        # 2 big checker
    lambda x, y: 176 if x % 8 == 7 or y % 8 == 7 else 214,                              # 3 square tiles
    lambda x, y: 186 if (x + y) % 8 == 0 or (x - y) % 8 == 0 else 212,                  # 4 diamond lattice
    lambda x, y: 186 if (x % 4 == 1 and y % 4 == 1) else 216,                           # 5 polka-dot carpet
    lambda x, y: 170 if x % 4 == 3 else (208 if (y + (x // 4) * 5) % 12 else 186),      # 6 narrow planks
    lambda x, y: 220 if ((x // 4) + (y // 4)) % 2 == 0 else 204,                        # 7 small checker
    lambda x, y: 182 if (x % 8 == 0 and y % 2 == 0) or (y % 8 == 0 and x % 2 == 0) else 214,  # 8 dashed grid
]


def floors():
    return [_floor(f) for f in FLOORS]


# Wall piece for a 4-bit neighbour mask (N=1, E=2, S=4, W=8): 16x32, the tile is the bottom half.
CAP, CAP_HI, FACE, FACE_DOT, RAIL, PANEL, PANEL_LINE, BASE, EDGE = (
    226, 240, 214, 200, 236, 190, 172, 160, 120)


def wall_piece(mask):
    c = Canvas(16, 32)
    n, e, s, w = mask & 1, mask & 2, mask & 4, mask & 8
    for y in range(32):
        for x in range(16):
            if s or y < 8:
                v = CAP_HI if (y == 1 and not n) else CAP
            elif y == 31:
                v = BASE
            elif y >= 28:
                v = BASE + 12
            elif y >= 22:
                v = PANEL_LINE if (x % 8 == 3) or y == 22 else PANEL
            elif y == 21:
                v = RAIL
            else:  # wallpaper with tiny hearts-ish dots
                v = FACE_DOT if ((x + 2) % 6 == 0 and (y - 9) % 6 == 0) or ((x + 5) % 6 == 0 and (y - 12) % 6 == 0) else FACE
            c.px(x, y, grey(v))
    edge = grey(EDGE)
    if not n:
        c.hline(0, 15, 0, edge)
    if not s:
        c.hline(0, 15, 31, edge)
        c.hline(0, 15, 8, grey(CAP - 30))         # cap lip shadow
    if not w:
        c.vline(0, 0, 31, edge)
    if not e:
        c.vline(15, 0, 31, edge)
    return c


def walls():
    sheet = Canvas(64, 128)
    for m in range(16):
        sheet.paste(wall_piece(m), (m % 4) * 16, (m // 4) * 32)
    return sheet


# ── pets: a hamster and a chick (96x96: down walk3+idle3 / up walk3+idle3 / right walk 3x32) ──
PETS = {
    'hamster': dict(body='#f7c28c', belly='#fff4e2', ear='#ffb3c6', name='Hamster'),
    'chick': dict(body='#ffe066', belly='#fff3b0', ear='#ff9f43', name='Chick'),
}


def _pet_small(p, kind, view, phase, idle):
    c = Canvas(16, 32)
    bob = 0 if idle or phase != 1 else -1
    cy = 26 + bob
    if kind == 'hamster':
        c.ellipse(4.5, cy - 5, 1.6, 1.6, p['ear']); c.ellipse(11.5, cy - 5, 1.6, 1.6, p['ear'])
        c.ellipse(8, cy, 5.5, 4.6, p['body'])
        if view == 'down':
            c.ellipse(8, cy + 1.5, 3.2, 2.5, p['belly'])
            c.px(6, cy - 1, O); c.px(10, cy - 1, O); c.px(8, cy, '#ff7f9c')
            c.px(5, cy + 1, '#ff9db4'); c.px(11, cy + 1, '#ff9db4')
    else:
        c.ellipse(8, cy, 5, 4.6, p['body'])
        c.px(8, cy - 5, p['body']); c.px(9, cy - 6, p['body'])           # tuft
        if view == 'down':
            c.px(6, cy - 1, O); c.px(10, cy - 1, O)
            c.px(7, cy + 1, p['ear']); c.px(8, cy + 1, p['ear'])
            c.px(4, cy + 1, '#ffd0a0'); c.px(12, cy + 1, '#ffd0a0')
    step = {0: (0, 1), 1: (0, 0), 2: (1, 0)}[phase] if not idle else (0, 0)
    foot = p['ear']
    c.px(6, 31 - step[0], foot); c.px(10, 31 - step[1], foot)
    c.outline(O)
    return c


def _pet_side(p, kind, phase):
    c = Canvas(32, 32)
    bob = -1 if phase == 1 else 0
    cx, cy = 15, 25 + bob
    if kind == 'hamster':
        c.ellipse(cx - 1, cy, 7, 4.8, p['body']); c.ellipse(cx + 3, cy - 4.5, 1.6, 1.6, p['ear'])
        c.ellipse(cx + 2, cy + 1.5, 3, 2.2, p['belly'])
        c.px(cx + 4, cy - 1, O); c.px(cx + 6, cy, '#ff7f9c')
    else:
        c.ellipse(cx, cy, 5.5, 4.6, p['body']); c.px(cx, cy - 5, p['body'])
        c.px(cx + 3, cy - 1, O); c.px(cx + 6, cy, p['ear']); c.px(cx + 7, cy, p['ear'])
        c.ellipse(cx - 2, cy + 1, 2.5, 1.6, p['belly'])                       # wing
    legs = {0: (-3, 2), 1: (-1, 1), 2: (-2, 3)}[phase]
    for dx in legs:
        c.px(cx + dx, 31, p['ear'] if kind == 'hamster' else '#ff9f43')
    c.outline(O)
    return c


def pet_sheet(kind):
    p = PETS[kind]
    out = Canvas(96, 96)
    for row, view in enumerate(('down', 'up')):
        for i in range(3):
            out.paste(_pet_small(p, kind, view, i, False), i * 16, row * 32)
            out.paste(_pet_small(p, kind, view, i, True), 48 + i * 16, row * 32)
    for i in range(3):
        out.paste(_pet_side(p, kind, i), i * 32, 64)
    return out
