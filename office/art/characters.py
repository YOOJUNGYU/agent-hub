"""Agent characters: six cats & dogs in hoodies (original art for Agent Hub's office).

Sheet: 112x96 = 7 frames (walk1-3, type1-2, read1-2) x 3 rows (down, up, right), 16x32 each.
Left-facing is the right row mirrored by the engine.
"""
from draw import Canvas

OUTLINE = '#463a5c'
EYE_DARK = '#2f2840'
BLUSH = '#ff9db4'
WHITE = '#ffffff'

CHARS = [
    # name, kind, ears, fur, fur shade, belly/muzzle, inner ear, eye, hoodie, hoodie shade, marking
    dict(name='cheese', kind='cat', ears='point', fur='#ffb760', fur2='#ec8f35', belly='#fff2dd',
         inner='#ffb6c4', eye=EYE_DARK, hood='#7fa8ff', hood2='#5d86ea', mark='tabby'),
    dict(name='snow', kind='cat', ears='point', fur='#fbfbff', fur2='#dde2f0', belly='#ffffff',
         inner='#ffb3c6', eye='#4a7df0', hood='#86e3c4', hood2='#55c9a5', mark=None),
    dict(name='coal', kind='cat', ears='point', fur='#55506d', fur2='#3e3953', belly='#77728f',
         inner='#ff9fb6', eye='#ffd447', hood='#ffd96e', hood2='#f1b93c', mark=None),
    dict(name='shiba', kind='dog', ears='point', fur='#f2a660', fur2='#d6833d', belly='#fff6ea',
         inner='#ffd5b5', eye=EYE_DARK, hood='#ff94a8', hood2='#ef6f88', mark='muzzle'),
    dict(name='mallang', kind='dog', ears='flop', fur='#fffaf3', fur2='#eee2d1', belly='#ffffff',
         inner='#f2d9c2', eye=EYE_DARK, hood='#c9b6ff', hood2='#a68cf4', mark='muzzle', earc='#f0d6bd'),
    dict(name='choco', kind='dog', ears='flop', fur='#c08a62', fur2='#9a6744', belly='#ecd0b1',
         inner='#e9c09c', eye=EYE_DARK, hood='#a2e283', hood2='#76c65a', mark='muzzle', earc='#7f5136'),
]


def _ears_front(c, s, oy):
    if s['ears'] == 'point':
        c.tri(((2, 11 + oy), (4, 5 + oy), (7, 10 + oy)), s['fur'])
        c.tri(((9, 10 + oy), (12, 5 + oy), (14, 11 + oy)), s['fur'])
        c.tri(((3, 10 + oy), (4, 7 + oy), (6, 10 + oy)), s['inner'])
        c.tri(((10, 10 + oy), (12, 7 + oy), (13, 10 + oy)), s['inner'])


def _ears_flop_front(c, s, oy):
    if s['ears'] == 'flop':
        c.ellipse(2.5, 14 + oy, 1.7, 3.6, s['earc'])
        c.ellipse(13.5, 14 + oy, 1.7, 3.6, s['earc'])


def _legs(c, s, oy, phase, sitting):
    if sitting:  # tucked under the chair: two little feet
        c.rect(5, 27 + oy, 2, 2, s['fur'])
        c.rect(9, 27 + oy, 2, 2, s['fur'])
        return
    lift = {0: (1, 0), 1: (0, 0), 2: (0, 1)}[phase]
    c.rect(5, 26 + oy - lift[0], 2, 3, s['fur2'])
    c.rect(9, 26 + oy - lift[1], 2, 3, s['fur2'])
    c.rect(5, 28 + oy - lift[0], 2, 1, s['fur'])
    c.rect(9, 28 + oy - lift[1], 2, 1, s['fur'])


def _body_front(c, s, oy):
    c.round_rect(4, 18 + oy, 8, 9, s['hood'])
    c.vline(11, 19 + oy, 25 + oy, s['hood2'])           # shade
    c.hline(6, 9, 23 + oy, s['hood2'])                  # kangaroo pocket
    c.px(7, 18 + oy, s['belly']); c.px(8, 18 + oy, s['belly'])  # neck


def _face_front(c, s, oy, blink=False):
    if s['mark'] == 'muzzle':
        c.ellipse(8, 16.6 + oy, 3.4, 2.2, s['belly'])
    if s['mark'] == 'tabby':
        for x in (6, 8, 10):
            c.px(x - 1 if x == 6 else x, 10 + oy, s['fur2'])
        c.px(7, 10 + oy, s['fur2']); c.px(7, 11 + oy, s['fur2']); c.px(8, 11 + oy, s['fur2'])
    for ex in (5, 10):
        if blink:
            c.px(ex, 15 + oy, s['eye']); c.px(ex + (1 if ex == 5 else -1), 15 + oy, s['eye'])
        else:
            c.px(ex, 14 + oy, s['eye']); c.px(ex, 15 + oy, s['eye'])
            if s['eye'] == EYE_DARK:
                c.px(ex, 14 + oy, '#5a4f73')
    c.px(4, 16 + oy, BLUSH); c.px(11, 16 + oy, BLUSH)
    nose = '#5b4a62' if s['kind'] == 'dog' else '#ff7f9c'
    c.px(7, 16 + oy, nose); c.px(8, 16 + oy, nose)
    c.px(6, 17 + oy, OUTLINE); c.px(9, 17 + oy, OUTLINE)


def _tail_front(c, s, oy):
    if s['kind'] == 'cat':
        for (x, y) in ((12, 24), (13, 24), (14, 23), (14, 22), (14, 21), (13, 20)):
            c.px(x, y + oy, s['fur'])
    elif s['ears'] == 'point':  # shiba curl
        for (x, y) in ((12, 21), (13, 20), (14, 20), (14, 19), (13, 18)):
            c.px(x, y + oy, s['fur'])
    else:
        c.px(12, 23 + oy, s['fur']); c.px(13, 22 + oy, s['fur'])


def front(s, pose, phase):
    c = Canvas(16, 32)
    sitting = pose != 'walk'
    oy = (1 if sitting else 0) + (-1 if pose == 'walk' and phase == 1 else 0)
    _tail_front(c, s, oy)
    _legs(c, s, oy, phase, sitting)
    _body_front(c, s, oy)
    # arms + paws
    if pose == 'walk':
        sw = {0: 1, 1: 0, 2: -1}[phase]
        c.rect(3, 19 + oy + sw, 1, 3, s['hood']); c.px(3, 22 + oy + sw, s['fur'])
        c.rect(12, 19 + oy - sw, 1, 3, s['hood']); c.px(12, 22 + oy - sw, s['fur'])
    elif pose == 'type':
        up = phase
        c.rect(4, 21 + oy - up, 2, 1, s['fur']); c.rect(10, 20 + oy + up, 2, 1, s['fur'])
    else:  # read: an open book held in front
        c.rect(4, 20 + oy, 8, 5, '#ff8fa8')
        c.rect(5, 20 + oy, 3, 4, WHITE); c.rect(8, 20 + oy, 3, 4, WHITE)
        c.hline(5, 6, 21 + oy, '#c9cfe4'); c.hline(9, 10, 21 + oy, '#c9cfe4')
        c.hline(5, 7, 22 + oy + phase, '#c9cfe4')
        c.px(3, 22 + oy, s['fur']); c.px(12, 22 + oy, s['fur'])
    _ears_front(c, s, oy)
    c.ellipse(8, 13.6 + oy, 6.1, 5.1, s['fur'])
    c.hline(4, 11, 18 + oy, s['fur2'])  # chin shade
    _ears_flop_front(c, s, oy)
    _face_front(c, s, oy, blink=(pose == 'read' and phase == 1))
    c.outline(OUTLINE)
    return c


def back(s, pose, phase):
    c = Canvas(16, 32)
    sitting = pose != 'walk'
    oy = (1 if sitting else 0) + (-1 if pose == 'walk' and phase == 1 else 0)
    _legs(c, s, oy, phase, sitting)
    c.round_rect(4, 18 + oy, 8, 9, s['hood'])
    c.vline(4, 19 + oy, 25 + oy, s['hood2'])
    c.ellipse(8, 19.5 + oy, 3, 1.6, s['hood2'])  # hood
    if pose == 'walk':
        sw = {0: 1, 1: 0, 2: -1}[phase]
        c.rect(3, 19 + oy - sw, 1, 3, s['hood']); c.rect(12, 19 + oy + sw, 1, 3, s['hood'])
    else:  # paws busy in front: elbows peek out
        c.px(3, 20 + oy + (phase if pose == 'type' else 0), s['hood'])
        c.px(12, 20 + oy + ((1 - phase) if pose == 'type' else 0), s['hood'])
    # tail
    if s['kind'] == 'cat':
        for (x, y) in ((8, 26), (9, 27), (10, 27), (11, 26), (12, 25), (12, 24)):
            c.px(x, y + oy, s['fur'])
    elif s['ears'] == 'point':
        for (x, y) in ((8, 24), (9, 23), (9, 22), (8, 22)):
            c.px(x, y + oy, s['fur'])
    else:
        c.px(8, 26 + oy, s['fur']); c.px(8, 27 + oy, s['fur'])
    _ears_front(c, s, oy)
    c.ellipse(8, 13.6 + oy, 6.1, 5.1, s['fur'])
    c.hline(4, 11, 17 + oy, s['fur2']); c.hline(5, 10, 18 + oy, s['fur2'])
    if s['mark'] == 'tabby':
        for y in (11, 13, 15):
            c.hline(6, 9, y + oy, s['fur2'])
    _ears_flop_front(c, s, oy)
    c.outline(OUTLINE)
    return c


def right(s, pose, phase):
    c = Canvas(16, 32)
    sitting = pose != 'walk'
    oy = (1 if sitting else 0) + (-1 if pose == 'walk' and phase == 1 else 0)
    # tail (behind)
    if s['kind'] == 'cat':
        for (x, y) in ((4, 23), (3, 22), (2, 21), (2, 20), (2, 19), (3, 18)):
            c.px(x, y + oy, s['fur'])
    elif s['ears'] == 'point':
        for (x, y) in ((4, 20), (3, 19), (3, 18), (4, 17)):
            c.px(x, y + oy, s['fur'])
    else:
        c.px(4, 22 + oy, s['fur']); c.px(3, 21 + oy, s['fur'])
    # legs
    if sitting:
        c.rect(7, 27 + oy, 4, 2, s['fur'])
    else:
        a, b = {0: (4, 10), 1: (6, 8), 2: (5, 9)}[phase]
        c.rect(a, 26 + oy, 2, 3, s['fur2']); c.rect(b, 26 + oy, 2, 3, s['fur2'])
        c.rect(a, 28 + oy, 2, 1, s['fur']); c.rect(b, 28 + oy, 2, 1, s['fur'])
    c.round_rect(5, 18 + oy, 7, 9, s['hood'])
    c.vline(5, 19 + oy, 25 + oy, s['hood2'])
    # arm
    if pose == 'walk':
        ax = {0: 9, 1: 8, 2: 7}[phase]
        c.rect(ax, 20 + oy, 2, 3, s['hood2']); c.px(ax + 1, 23 + oy, s['fur'])
    elif pose == 'type':
        c.rect(9, 21 + oy, 3, 1, s['hood2']); c.px(12, 21 + oy - phase, s['fur'])
    else:
        c.rect(10, 19 + oy, 3, 5, '#ff8fa8'); c.rect(11, 19 + oy, 2, 4, WHITE)
        c.hline(11, 12, 20 + oy + phase, '#c9cfe4')
        c.px(9, 21 + oy, s['fur'])
    # head
    if s['ears'] == 'point':
        c.tri(((5, 10 + oy), (6, 5 + oy), (9, 9 + oy)), s['fur'])
        c.tri(((6, 9 + oy), (6, 7 + oy), (8, 9 + oy)), s['inner'])
    c.ellipse(8.5, 13.6 + oy, 5.6, 5.1, s['fur'])
    if s['kind'] == 'dog':
        c.ellipse(13, 15.6 + oy, 2.2, 1.7, s['belly'] if s['mark'] == 'muzzle' else s['fur'])
        c.px(14, 15 + oy, '#5b4a62')
    else:
        c.px(14, 15 + oy, '#ff7f9c')
    c.hline(5, 11, 18 + oy, s['fur2'])
    if s['ears'] == 'flop':
        c.ellipse(5.5, 13.5 + oy, 1.7, 3.6, s['earc'])
    if s['mark'] == 'tabby':
        c.px(7, 9 + oy, s['fur2']); c.px(8, 10 + oy, s['fur2']); c.px(6, 11 + oy, s['fur2'])
    blink = pose == 'read' and phase == 1
    if blink:
        c.px(10, 15 + oy, s['eye']); c.px(11, 15 + oy, s['eye'])
    else:
        c.px(11, 14 + oy, s['eye']); c.px(11, 15 + oy, s['eye'])
    c.px(11, 16 + oy, BLUSH)
    c.outline(OUTLINE)
    return c


POSES = [('walk', 0), ('walk', 1), ('walk', 2), ('type', 0), ('type', 1), ('read', 0), ('read', 1)]


def sheet(s):
    out = Canvas(112, 96)
    for row, fn in enumerate((front, back, right)):
        for i, (pose, phase) in enumerate(POSES):
            out.paste(fn(s, pose, phase), i * 16, row * 32)
    return out
