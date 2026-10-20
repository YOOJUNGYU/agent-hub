"""Tiny pixel canvas: fill shapes, then auto-outline. Colours are '#rrggbb' strings or None (transparent)."""


def rgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def mix(a, b, t):
    """Blend two '#rrggbb' colours (t=0 → a, t=1 → b)."""
    ca, cb = rgb(a), rgb(b)
    return '#%02x%02x%02x' % tuple(round(x + (y - x) * t) for x, y in zip(ca, cb))


def grey(v):
    return '#%02x%02x%02x' % (v, v, v)


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.p = [[None] * w for _ in range(h)]

    def px(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.p[y][x] = c

    def get(self, x, y):
        return self.p[y][x] if 0 <= x < self.w and 0 <= y < self.h else None

    def rect(self, x, y, w, h, c):
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                self.px(xx, yy, c)

    def hline(self, x0, x1, y, c):
        for x in range(x0, x1 + 1):
            self.px(x, y, c)

    def vline(self, x, y0, y1, c):
        for y in range(y0, y1 + 1):
            self.px(x, y, c)

    def ellipse(self, cx, cy, rx, ry, c):
        """Filled ellipse centred on a pixel-corner grid (cx, cy may be .5)."""
        for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
            for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
                dx, dy = (x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry
                if dx * dx + dy * dy <= 1.0:
                    self.px(x, y, c)

    def round_rect(self, x, y, w, h, c, r=1):
        self.rect(x, y, w, h, c)
        for (cx, cy) in ((x, y), (x + w - 1, y), (x, y + h - 1), (x + w - 1, y + h - 1)):
            if r >= 1:
                self.px(cx, cy, None)

    def tri(self, pts, c):
        """Filled triangle (integer vertices)."""
        (x0, y0), (x1, y1), (x2, y2) = pts
        minx, maxx = min(x0, x1, x2), max(x0, x1, x2)
        miny, maxy = min(y0, y1, y2), max(y0, y1, y2)
        def side(ax, ay, bx, by, px_, py_):
            return (bx - ax) * (py_ - ay) - (by - ay) * (px_ - ax)
        for y in range(miny, maxy + 1):
            for x in range(minx, maxx + 1):
                d1 = side(x0, y0, x1, y1, x + .5, y + .5)
                d2 = side(x1, y1, x2, y2, x + .5, y + .5)
                d3 = side(x2, y2, x0, y0, x + .5, y + .5)
                neg = d1 < 0 or d2 < 0 or d3 < 0
                pos = d1 > 0 or d2 > 0 or d3 > 0
                if not (neg and pos):
                    self.px(x, y, c)

    def outline(self, c, diagonal=False):
        """Draw c on every transparent pixel touching an opaque one."""
        adds = []
        nb = [(1, 0), (-1, 0), (0, 1), (0, -1)]
        if diagonal:
            nb += [(1, 1), (1, -1), (-1, 1), (-1, -1)]
        for y in range(self.h):
            for x in range(self.w):
                if self.p[y][x] is None and any(
                        self.get(x + dx, y + dy) not in (None, c) for dx, dy in nb):
                    adds.append((x, y))
        for x, y in adds:
            self.p[y][x] = c

    def shift(self, dx, dy):
        out = Canvas(self.w, self.h)
        for y in range(self.h):
            for x in range(self.w):
                if self.p[y][x] is not None:
                    out.px(x + dx, y + dy, self.p[y][x])
        self.p = out.p
        return self

    def flip(self):
        self.p = [row[::-1] for row in self.p]
        return self

    def paste(self, other, ox, oy):
        for y in range(other.h):
            for x in range(other.w):
                if other.p[y][x] is not None:
                    self.px(ox + x, oy + y, other.p[y][x])

    def rgba(self):
        return [[(*rgb(c), 255) if c else (0, 0, 0, 0) for c in row] for row in self.p]
