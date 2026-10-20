"""Furniture for the pet-café office (original art). Same ids/sizes as the manifests they fill.

3/4 view: tops light, fronts darker, soft plum outline. Each function returns a Canvas.
"""
from draw import Canvas, mix

O = '#463a5c'                 # outline
WOOD = dict(top='#f6d9b2', front='#e2b386', shade='#c99264', light='#fff0d9', leg='#b98157')
CREAM, WHITE = '#fff8ec', '#ffffff'
PINK, PINK2 = '#ffb3c6', '#f48aa6'
MINT, MINT2 = '#a7ebd3', '#6fd0ae'
SKY, SKY2 = '#b9d8ff', '#86b5f5'
BUTTER, BUTTER2 = '#ffe49a', '#f7c95a'
LILAC, LILAC2 = '#d7c9ff', '#b29cf5'
LEAF, LEAF2, LEAF3 = '#8edc8a', '#5fbf6a', '#c7f0a8'
SCREEN_OFF, SCREEN_ON = '#7e88b0', '#d9f2ff'


def fin(c):
    c.outline(O)
    return c


# ── surfaces ─────────────────────────────────────────────────────────────
def desk_front():
    c = Canvas(48, 32)
    c.rect(1, 9, 46, 9, WOOD['top']); c.hline(1, 46, 9, WOOD['light'])
    c.rect(1, 18, 46, 9, WOOD['front']); c.hline(1, 46, 18, WOOD['shade'])
    for x0 in (3, 33):                                   # drawers with heart knobs
        c.rect(x0, 20, 12, 5, WOOD['top']); c.px(x0 + 5, 22, PINK2); c.px(x0 + 6, 22, PINK2)
    c.rect(2, 27, 3, 4, WOOD['leg']); c.rect(43, 27, 3, 4, WOOD['leg'])
    return fin(c)


def desk_side():
    c = Canvas(16, 64)
    c.rect(2, 13, 9, 46, WOOD['top']); c.vline(2, 13, 58, WOOD['light'])
    c.rect(11, 13, 3, 46, WOOD['front'])
    c.rect(4, 59, 2, 4, WOOD['leg']); c.rect(11, 59, 2, 4, WOOD['leg'])
    return fin(c)


def table_front():
    c = Canvas(48, 64)
    c.rect(1, 17, 46, 37, WOOD['top']); c.hline(1, 46, 17, WOOD['light'])
    for y in range(22, 52, 6):                              # plank seams
        c.hline(2, 45, y, mix(WOOD['top'], WOOD['front'], .45))
    c.rect(1, 54, 46, 5, WOOD['front']); c.hline(1, 46, 54, WOOD['shade'])
    c.rect(2, 59, 3, 5, WOOD['leg']); c.rect(43, 59, 3, 5, WOOD['leg'])
    return fin(c)


def coffee_table():
    c = Canvas(32, 32)
    c.ellipse(16, 15, 14.5, 8, WOOD['front'])
    c.ellipse(16, 13, 14.5, 8, WOOD['top'])
    c.ellipse(16, 11.5, 11, 5, WOOD['light'])
    c.rect(6, 22, 2, 7, WOOD['leg']); c.rect(24, 22, 2, 7, WOOD['leg'])
    return fin(c)


def small_table_front():
    c = Canvas(32, 32)
    c.rect(1, 13, 30, 9, MINT); c.hline(1, 30, 13, '#d4f7ea')
    c.rect(1, 22, 30, 4, MINT2)
    c.rect(3, 26, 2, 5, WOOD['leg']); c.rect(27, 26, 2, 5, WOOD['leg'])
    return fin(c)


def small_table_side():
    c = Canvas(16, 48)
    c.rect(3, 13, 8, 30, MINT); c.vline(3, 13, 42, '#d4f7ea')
    c.rect(11, 13, 2, 30, MINT2)
    c.rect(4, 43, 2, 4, WOOD['leg']); c.rect(10, 43, 2, 4, WOOD['leg'])
    return fin(c)


def counter():
    """상담실 reception counter: rounded pastel desk with a paw emblem."""
    c = Canvas(48, 32)
    c.rect(1, 9, 46, 8, CREAM); c.hline(1, 46, 9, WHITE)
    c.rect(1, 17, 46, 13, LILAC); c.hline(1, 46, 17, LILAC2)
    c.ellipse(24, 24, 3, 2.5, WHITE)                          # paw pad
    for dx in (-4, -1.5, 1.5, 4):
        c.ellipse(24 + dx, 20.5 if abs(dx) < 3 else 21.5, 1, 1, WHITE)
    return fin(c)


# ── seats ────────────────────────────────────────────────────────────────
def wooden_chair(view):
    c = Canvas(16, 32)
    if view == 'front':      # facing the viewer: backrest behind the seat
        c.rect(3, 8, 10, 11, WOOD['front']); c.rect(4, 9, 8, 3, PINK)        # backrest + cushion strip
        c.rect(2, 19, 12, 4, WOOD['top']); c.hline(2, 13, 19, WOOD['light'])
        c.rect(3, 23, 2, 6, WOOD['leg']); c.rect(11, 23, 2, 6, WOOD['leg'])
    elif view == 'back':     # backrest in front of the sitter
        c.rect(2, 17, 12, 4, WOOD['top'])
        c.rect(3, 21, 2, 8, WOOD['leg']); c.rect(11, 21, 2, 8, WOOD['leg'])
        c.rect(3, 12, 10, 10, WOOD['front']); c.rect(5, 14, 6, 2, WOOD['top'])
        c.px(7, 17, PINK2); c.px(8, 17, PINK2)                                 # little heart
    else:                    # side (facing right): backrest on the left
        c.rect(2, 8, 3, 13, WOOD['front']); c.vline(2, 8, 20, WOOD['shade'])
        c.rect(2, 19, 12, 3, WOOD['top']); c.rect(5, 18, 8, 1, PINK)
        c.rect(3, 22, 2, 7, WOOD['leg']); c.rect(11, 22, 2, 7, WOOD['leg'])
    return fin(c)


def cushioned_chair(view):
    c = Canvas(16, 16)
    if view == 'front':
        c.round_rect(2, 1, 12, 8, SKY); c.rect(3, 2, 10, 2, '#d8e9ff')
        c.round_rect(1, 8, 14, 5, SKY2); c.rect(2, 8, 12, 2, SKY)
    elif view == 'back':
        c.round_rect(1, 6, 14, 5, SKY2)
        c.round_rect(2, 5, 12, 9, SKY); c.rect(3, 6, 10, 1, '#d8e9ff')
    else:
        c.round_rect(1, 2, 4, 11, SKY2)
        c.round_rect(3, 8, 11, 5, SKY); c.hline(4, 13, 8, '#d8e9ff')
    c.rect(3, 13, 2, 2, WOOD['leg']); c.rect(11, 13, 2, 2, WOOD['leg'])
    return fin(c)


def cushioned_bench():
    """Cat-ear pouf."""
    c = Canvas(16, 16)
    c.tri(((2, 6), (3, 1), (6, 4)), PINK); c.tri(((10, 4), (13, 1), (14, 6)), PINK)
    c.ellipse(8, 9, 7, 5.5, PINK)
    c.ellipse(8, 7.5, 5.5, 3, '#ffd1dd')
    c.hline(4, 11, 13, PINK2)
    return fin(c)


def wooden_bench():
    c = Canvas(16, 16)
    c.ellipse(8, 6, 6.5, 3.5, WOOD['top']); c.ellipse(8, 5.5, 4.5, 2, WOOD['light'])
    c.rect(3, 9, 2, 6, WOOD['leg']); c.rect(11, 9, 2, 6, WOOD['leg']); c.rect(5, 12, 6, 1, WOOD['leg'])
    return fin(c)


def beanbag():
    c = Canvas(16, 16)
    c.ellipse(8, 9.5, 7, 5.5, LILAC)
    c.ellipse(8, 7, 5, 3.4, '#ebe3ff')
    c.ellipse(5, 11.5, 2, 1.2, LILAC2)
    return fin(c)


def sofa(view):
    if view == 'side':
        c = Canvas(16, 32)
        c.round_rect(1, 4, 5, 26, PINK2)                # backrest (left)
        c.round_rect(4, 7, 10, 21, PINK); c.vline(5, 8, 26, '#ffd1dd')
        c.round_rect(4, 3, 11, 5, PINK2); c.round_rect(4, 26, 11, 5, PINK2)  # arms
        return fin(c)
    c = Canvas(32, 16)
    if view == 'front':
        c.round_rect(2, 0, 28, 8, PINK2)
        c.round_rect(1, 7, 30, 7, PINK); c.hline(3, 28, 7, '#ffd1dd')
        c.round_rect(0, 4, 4, 10, PINK2); c.round_rect(28, 4, 4, 10, PINK2)
    else:
        c.round_rect(1, 3, 30, 5, PINK)
        c.round_rect(2, 6, 28, 8, PINK2); c.hline(4, 27, 7, PINK)
        c.round_rect(0, 2, 4, 11, PINK2); c.round_rect(28, 2, 4, 11, PINK2)
    return fin(c)


# ── electronics ──────────────────────────────────────────────────────────
def pc(state, frame=0):
    """Cat-ear monitor. state: off | on | back | side."""
    c = Canvas(16, 32)
    if state == 'side':
        c.rect(6, 11, 3, 11, '#e9e2f5'); c.rect(9, 12, 1, 9, SCREEN_OFF)
        c.rect(6, 22, 2, 4, '#d8cfe8'); c.rect(4, 26, 7, 2, '#d8cfe8')
        c.tri(((6, 11), (7, 8), (8, 11)), '#e9e2f5')
        return fin(c)
    c.tri(((2, 11), (3, 7), (6, 10)), CREAM); c.tri(((10, 10), (13, 7), (14, 11)), CREAM)
    c.round_rect(1, 10, 14, 12, CREAM)
    c.rect(7, 22, 2, 3, '#e5dccb'); c.rect(4, 25, 8, 2, '#e5dccb')
    if state == 'back':
        c.rect(3, 12, 10, 8, '#efe6d6'); c.px(7, 15, PINK2); c.px(8, 15, PINK2); c.px(7, 16, PINK2); c.px(8, 16, PINK2)
        return fin(c)
    c.tri(((3, 10), (3, 8), (5, 10)), PINK); c.tri(((11, 10), (13, 8), (13, 10)), PINK)
    c.rect(3, 12, 10, 8, SCREEN_OFF if state == 'off' else SCREEN_ON)
    if state == 'on':
        lines = [(4, 13, 5, '#7fa8ff'), (5, 15, 6, '#ff9db4'), (4, 17, 4, '#55c9a5'), (6, 18, 3, '#b29cf5')]
        for i, (x, y, w, col) in enumerate(lines):
            if (i + frame) % 4 != 3:
                c.hline(x, min(12, x + w - 1), y, col)
        c.px(4 + (frame * 3) % 8, 19, '#463a5c')                 # blinking cursor
    else:
        c.px(4, 13, '#9aa3c7')
    return fin(c)


def call_bell():
    c = Canvas(16, 16)
    c.ellipse(8, 10, 5.5, 4, BUTTER); c.ellipse(7, 8.5, 2, 1.5, '#fff3c4')
    c.rect(7, 4, 2, 2, BUTTER2)
    c.rect(2, 13, 12, 2, '#d8cfe8')
    return fin(c)


# ── walls ────────────────────────────────────────────────────────────────
BOOKS = [PINK2, SKY2, BUTTER2, MINT2, LILAC2, '#ff9f7a', '#7fa8ff']


def _books(c, x0, x1, base, seed):
    x, i = x0, seed
    while x <= x1 - 2:
        w = 2 if i % 3 else 3
        h = 7 + (i * 5) % 4
        if x + w - 1 > x1:
            break
        col = BOOKS[i % len(BOOKS)]
        c.rect(x, base - h, w, h, col); c.hline(x, x + w - 1, base - h + 2, mix(col, '#ffffff', .5))
        x += w + (1 if i % 4 == 0 else 0); i += 1


def bookshelf():
    c = Canvas(32, 16)
    _books(c, 2, 24, 12, 1)
    c.ellipse(28, 10, 2.5, 2.2, '#ffb760'); c.tri(((26, 9), (26, 6), (28, 8)), '#ffb760')  # cat figurine
    c.tri(((28, 8), (30, 6), (30, 9)), '#ffb760')
    c.rect(0, 12, 32, 3, WOOD['front']); c.hline(0, 31, 12, WOOD['light'])
    return fin(c)


def double_bookshelf():
    c = Canvas(32, 32)
    c.rect(0, 0, 32, 32, WOOD['shade']); c.rect(2, 2, 28, 28, mix(WOOD['shade'], '#ffffff', .25))
    _books(c, 3, 28, 14, 0); _books(c, 3, 22, 28, 3)
    c.ellipse(26, 26, 2.5, 2.5, LEAF)                                         # tiny plant
    c.rect(25, 27, 3, 2, PINK2)
    c.rect(2, 14, 28, 2, WOOD['front']); c.rect(2, 28, 28, 2, WOOD['front'])
    return fin(c)


def clock():
    """Cat clock with a tail pendulum."""
    c = Canvas(16, 32)
    c.tri(((3, 10), (4, 4), (7, 8)), O); c.tri(((9, 8), (12, 4), (13, 10)), O)
    c.ellipse(8, 13, 6, 5.5, '#55506d')
    c.ellipse(8, 13, 4.5, 4, WHITE)
    c.vline(8, 10, 13, O); c.hline(8, 10, 13, PINK2)
    c.px(5, 16, '#ff9db4'); c.px(11, 16, '#ff9db4')
    for (x, y) in ((8, 19), (8, 20), (9, 21), (9, 22), (10, 23), (10, 24)):
        c.px(x, y, '#55506d')
    return fin(c)


def whiteboard():
    c = Canvas(32, 32)
    c.rect(0, 2, 32, 24, '#d8cfe8'); c.rect(2, 4, 28, 20, WHITE)
    c.rect(4, 6, 6, 5, BUTTER); c.rect(12, 6, 6, 5, PINK); c.rect(4, 13, 6, 5, MINT)   # sticky notes
    c.hline(5, 8, 8, BUTTER2); c.hline(13, 16, 8, PINK2); c.hline(5, 8, 15, MINT2)
    c.ellipse(23, 13, 4, 3.5, WHITE)                                                    # doodled cat
    for (x, y) in ((20, 10), (21, 9), (22, 10), (24, 10), (25, 9), (26, 10)):
        c.px(x, y, '#7fa8ff')
    c.px(22, 13, '#7fa8ff'); c.px(24, 13, '#7fa8ff'); c.hline(19, 27, 16, '#7fa8ff')
    c.hline(20, 26, 20, SKY2)
    c.rect(4, 26, 24, 2, '#d8cfe8'); c.rect(8, 25, 3, 1, PINK2); c.rect(13, 25, 3, 1, SKY2)
    return fin(c)


def large_painting():
    c = Canvas(32, 32)
    c.rect(0, 2, 32, 26, WOOD['front']); c.rect(2, 4, 28, 22, '#cfe6ff')
    c.ellipse(24, 9, 3, 3, BUTTER)                                   # sun
    c.ellipse(9, 26, 12, 7, LEAF); c.ellipse(24, 27, 10, 6, LEAF2)   # hills
    c.rect(7, 15, 7, 5, CREAM); c.tri(((6, 15), (10, 11), (15, 15)), PINK2)  # little house
    c.rect(9, 17, 2, 3, WOOD['shade'])
    for x in range(0, 32):
        for y in (26, 27):
            if c.get(x, y) is None:
                c.px(x, y, WOOD['front'])
    return fin(c)


def small_painting(kind):
    c = Canvas(16, 32)
    c.rect(1, 6, 14, 18, WOOD['front']); c.rect(3, 8, 10, 14, PINK if kind == 'cat' else SKY)
    if kind == 'cat':   # orange cat portrait
        c.tri(((4, 14), (5, 10), (7, 13)), '#ffb760'); c.tri(((9, 13), (11, 10), (12, 14)), '#ffb760')
        c.ellipse(8, 16, 4, 3.5, '#ffb760')
        c.px(6, 16, O); c.px(10, 16, O); c.px(8, 17, '#ff7f9c')
    else:               # paw print poster
        c.ellipse(8, 17.5, 2.8, 2.3, WHITE)
        for (x, y) in ((5, 14), (7, 12.5), (9.5, 12.5), (11.5, 14)):
            c.ellipse(x + .5, y + .5, 1.1, 1.2, WHITE)
    return fin(c)


def hanging_plant():
    c = Canvas(16, 32)
    for (x0, x1) in ((4, 7), (11, 8)):
        step = 1 if x1 > x0 else -1
        for i, y in enumerate(range(1, 12)):
            c.px(x0 + (step * i) // 4, y, '#c9b39a')
    c.ellipse(8, 15, 4.5, 3.5, PINK)
    c.hline(4, 12, 14, PINK2)
    for (x, y0, y1) in ((4, 15, 23), (7, 17, 26), (11, 16, 21)):
        c.vline(x, y0, y1, LEAF2)
        for y in range(y0 + 1, y1, 2):
            c.px(x + (1 if y % 4 else -1), y, LEAF)
    c.ellipse(8, 12, 4, 2, LEAF)
    return fin(c)


# ── plants & decor ───────────────────────────────────────────────────────
def _pot(c, x, y, w, h, col=PINK, col2=PINK2):
    c.rect(x, y, w, h, col); c.hline(x - 1, x + w, y, col2); c.hline(x + 1, x + w - 2, y + h - 1, col2)


def plant():
    c = Canvas(16, 32)
    c.ellipse(8, 16, 6, 6, LEAF); c.ellipse(6, 14, 3, 2.5, LEAF3); c.ellipse(10, 18, 3, 2.5, LEAF2)
    _pot(c, 4, 22, 8, 7, BUTTER, BUTTER2)
    return fin(c)


def plant_2():
    c = Canvas(16, 32)
    for (x, top, col) in ((5, 9, LEAF2), (8, 6, LEAF), (11, 10, LEAF2)):
        c.rect(x - 1, top, 2, 22 - top, col); c.px(x - 1, top, None)
    c.ellipse(8, 6, 1.6, 1.6, PINK)                    # a flower
    _pot(c, 4, 22, 8, 7, SKY, SKY2)
    return fin(c)


def large_plant():
    c = Canvas(32, 48)
    for (cx, cy, rx, ry, col) in ((11, 16, 7, 6, LEAF2), (21, 14, 7, 6, LEAF), (16, 8, 6, 5, LEAF),
                                  (10, 25, 6, 5, LEAF), (22, 24, 6, 5, LEAF2), (16, 19, 6, 6, LEAF3)):
        c.ellipse(cx, cy, rx, ry, col)
    c.vline(16, 20, 35, LEAF2)
    _pot(c, 9, 35, 14, 11, LILAC, LILAC2)
    return fin(c)


def cactus():
    c = Canvas(16, 32)
    c.round_rect(5, 9, 6, 14, MINT2)
    c.round_rect(2, 13, 3, 6, MINT2); c.round_rect(11, 11, 3, 6, MINT2)
    c.px(6, 14, O); c.px(9, 14, O); c.px(5, 15, '#ff9db4'); c.px(10, 15, '#ff9db4')   # :)
    c.ellipse(8, 8.5, 1.6, 1.4, PINK)
    _pot(c, 4, 23, 8, 6)
    return fin(c)


def pot():
    c = Canvas(16, 16)
    for (x, y, col) in ((6, 4, PINK), (10, 5, BUTTER), (8, 2, LILAC)):
        c.ellipse(x, y, 1.8, 1.8, col)
    c.vline(6, 6, 8, LEAF2); c.vline(10, 7, 8, LEAF2); c.vline(8, 4, 8, LEAF2)
    _pot(c, 4, 9, 8, 6, MINT, MINT2)
    return fin(c)


def bin_():
    c = Canvas(16, 16)
    c.round_rect(4, 4, 8, 11, MINT); c.hline(3, 12, 4, MINT2)
    c.ellipse(8, 10.5, 1.5, 1.3, WHITE)
    for (x, y) in ((6, 8), (8, 7), (10, 8)):
        c.px(x, y, WHITE)
    return fin(c)


def coffee():
    c = Canvas(16, 16)
    c.round_rect(4, 6, 7, 7, WHITE); c.rect(11, 8, 2, 3, WHITE); c.px(11, 9, None)
    c.ellipse(7.5, 6.5, 3, 1.2, '#b98157')
    c.px(7, 6, '#fff0d9'); c.px(8, 6, '#fff0d9')            # latte heart
    c.hline(5, 9, 12, '#e8e2f0')
    return fin(c)


def cat_tower():
    c = Canvas(32, 48)
    c.rect(7, 12, 3, 32, '#e9d6b8'); c.rect(22, 22, 3, 22, '#e9d6b8')       # sisal posts
    for y in range(13, 44, 3):
        c.hline(7, 9, y, '#d4bb94')
    for y in range(23, 44, 3):
        c.hline(22, 24, y, '#d4bb94')
    c.round_rect(2, 9, 16, 4, LILAC); c.round_rect(15, 19, 15, 4, PINK)      # platforms
    c.round_rect(1, 42, 30, 5, LILAC2)
    c.ellipse(10, 6, 5, 3.5, '#ffb760'); c.tri(((6, 5), (6, 1), (9, 3)), '#ffb760')  # napping kitten
    c.tri(((11, 3), (13, 1), (14, 5)), '#ffb760')
    c.hline(8, 9, 6, O); c.hline(11, 12, 6, O)
    c.ellipse(24, 33, 2.5, 2.5, BUTTER)                                       # dangling toy
    c.vline(24, 23, 30, '#c9b39a')
    return fin(c)


def floor_lamp():
    c = Canvas(16, 32)
    c.tri(((3, 11), (8, 3), (13, 11)), BUTTER); c.hline(3, 13, 11, BUTTER2)
    c.vline(8, 12, 27, '#d8cfe8'); c.rect(5, 27, 7, 2, '#d8cfe8')
    return fin(c)


# id → (factory, png name). Existing ids keep their manifests; NEW ones get one written.
ASSETS = {
    'DESK/DESK_FRONT': desk_front, 'DESK/DESK_SIDE': desk_side,
    'TABLE_FRONT/TABLE_FRONT': table_front, 'COFFEE_TABLE/COFFEE_TABLE': coffee_table,
    'SMALL_TABLE/SMALL_TABLE_FRONT': small_table_front, 'SMALL_TABLE/SMALL_TABLE_SIDE': small_table_side,
    'WOODEN_CHAIR/WOODEN_CHAIR_FRONT': lambda: wooden_chair('front'),
    'WOODEN_CHAIR/WOODEN_CHAIR_BACK': lambda: wooden_chair('back'),
    'WOODEN_CHAIR/WOODEN_CHAIR_SIDE': lambda: wooden_chair('side'),
    'CUSHIONED_CHAIR/CUSHIONED_CHAIR_FRONT': lambda: cushioned_chair('front'),
    'CUSHIONED_CHAIR/CUSHIONED_CHAIR_BACK': lambda: cushioned_chair('back'),
    'CUSHIONED_CHAIR/CUSHIONED_CHAIR_SIDE': lambda: cushioned_chair('side'),
    'CUSHIONED_BENCH/CUSHIONED_BENCH': cushioned_bench, 'WOODEN_BENCH/WOODEN_BENCH': wooden_bench,
    'SOFA/SOFA_FRONT': lambda: sofa('front'), 'SOFA/SOFA_BACK': lambda: sofa('back'),
    'SOFA/SOFA_SIDE': lambda: sofa('side'),
    'PC/PC_FRONT_OFF': lambda: pc('off'), 'PC/PC_FRONT_ON_1': lambda: pc('on', 0),
    'PC/PC_FRONT_ON_2': lambda: pc('on', 1), 'PC/PC_FRONT_ON_3': lambda: pc('on', 2),
    'PC/PC_BACK': lambda: pc('back'), 'PC/PC_SIDE': lambda: pc('side'),
    'BOOKSHELF/BOOKSHELF': bookshelf, 'DOUBLE_BOOKSHELF/DOUBLE_BOOKSHELF': double_bookshelf,
    'CLOCK/CLOCK': clock, 'WHITEBOARD/WHITEBOARD': whiteboard,
    'LARGE_PAINTING/LARGE_PAINTING': large_painting,
    'SMALL_PAINTING/SMALL_PAINTING': lambda: small_painting('cat'),
    'SMALL_PAINTING_2/SMALL_PAINTING_2': lambda: small_painting('paw'),
    'HANGING_PLANT/HANGING_PLANT': hanging_plant,
    'PLANT/PLANT': plant, 'PLANT_2/PLANT_2': plant_2, 'LARGE_PLANT/LARGE_PLANT': large_plant,
    'CACTUS/CACTUS': cactus, 'POT/POT': pot, 'BIN/BIN': bin_, 'COFFEE/COFFEE': coffee,
    # new pieces for the 5-room office
    'COUNTER/COUNTER': counter, 'CALL_BELL/CALL_BELL': call_bell, 'BEANBAG/BEANBAG': beanbag,
    'CAT_TOWER/CAT_TOWER': cat_tower, 'FLOOR_LAMP/FLOOR_LAMP': floor_lamp,
}

# manifests for the new pieces: (name, category, w, h, footprintW, footprintH, bg, surf)
NEW = {
    'COUNTER': ('Counter', 'desks', 48, 32, 3, 2, 1, False),
    'CALL_BELL': ('Call Bell', 'misc', 16, 16, 1, 1, 0, True),
    'BEANBAG': ('Beanbag', 'chairs', 16, 16, 1, 1, 0, False),
    'CAT_TOWER': ('Cat Tower', 'decor', 32, 48, 2, 3, 2, False),
    'FLOOR_LAMP': ('Floor Lamp', 'decor', 16, 32, 1, 2, 1, False),
}
