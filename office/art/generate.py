"""Regenerate every office sprite from code (original Agent Hub art).

    python3 office/art/generate.py            # writes into office/webview-ui/public/assets
    python3 office/art/generate.py --preview DIR   # also writes enlarged contact sheets to DIR

Manifests of existing furniture are left as they are (ids, sizes, footprints); new
furniture gets its manifest written here. Output is deterministic.
"""
import json
import os
import shutil
import sys

import characters
import furniture
import tiles
from draw import Canvas, grey
from pixpng import upscale, write

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, '..', 'webview-ui', 'public', 'assets')


def save(canvas, *parts):
    path = os.path.join(ASSETS, *parts)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    write(path, canvas.rgba())


def carpet(variant):
    """4x4 marching-squares sheet (case = NW1|NE2|SE4|SW8 at (case%4, case//4)).
    Dark = main colour, light = accent (the engine colourises the two tones)."""
    main, accent = grey(110), grey(230)
    sheet = Canvas(64, 64)
    for case in range(16):
        c = Canvas(16, 16)
        quads = {(0, 0): case & 1, (1, 0): case & 2, (1, 1): case & 4, (0, 1): case & 8}
        for y in range(16):
            for x in range(16):
                if not quads[(x // 8, y // 8)]:
                    continue
                # distance to the carpet's edge inside this junction tile
                qx, qy = x // 8, y // 8
                open_x = not quads[(1 - qx, qy)]
                open_y = not quads[(qx, 1 - qy)]
                if (open_x and (x == (7 if qx == 0 else 8))) or (open_y and (y == (7 if qy == 0 else 8))):
                    continue                      # rounded 1px inset where the rug ends
                border = (open_x and (x == (6 if qx == 0 else 9))) or (open_y and (y == (6 if qy == 0 else 9)))
                if variant == 0:
                    v = accent if border or (x % 4 == 1 and y % 4 == 1) else main
                elif variant == 1:
                    v = accent if border or (x + y) % 6 == 0 else main
                else:
                    v = accent if border or ((x // 2) + (y // 2)) % 2 == 0 and x % 8 in (3, 4) else main
                c.px(x, y, v)
        sheet.paste(c, (case % 4) * 16, (case // 4) * 16)
    return sheet


def main():
    preview = sys.argv[sys.argv.index('--preview') + 1] if '--preview' in sys.argv else None

    for i, spec in enumerate(characters.CHARS):
        save(characters.sheet(spec), 'characters', f'char_{i}.png')

    for i, f in enumerate(tiles.floors()):
        save(f, 'floors', f'floor_{i}.png')
    save(tiles.walls(), 'walls', 'wall_0.png')

    for i in range(3):
        save(carpet(i), 'carpets', f'carpet_{i}.png')

    pets_dir = os.path.join(ASSETS, 'pets')
    for old in os.listdir(pets_dir):
        if old not in tiles.PETS:
            shutil.rmtree(os.path.join(pets_dir, old))
    for kind, p in tiles.PETS.items():
        save(tiles.pet_sheet(kind), 'pets', kind, 'pet.png')
        with open(os.path.join(pets_dir, kind, 'manifest.json'), 'w') as fh:
            json.dump({'id': kind, 'name': p['name']}, fh, indent=2)
            fh.write('\n')

    for key, factory in furniture.ASSETS.items():
        folder, name = key.split('/')
        save(factory(), 'furniture', folder, f'{name}.png')
    for fid, (name, cat, w, h, fw, fh_, bg, surf) in furniture.NEW.items():
        with open(os.path.join(ASSETS, 'furniture', fid, 'manifest.json'), 'w') as fh:
            json.dump({'id': fid, 'name': name, 'category': cat, 'type': 'asset', 'canPlaceOnWalls': False,
                       'canPlaceOnSurfaces': surf, 'backgroundTiles': bg, 'width': w, 'height': h,
                       'footprintW': fw, 'footprintH': fh_}, fh, indent=2)
            fh.write('\n')

    if preview:
        os.makedirs(preview, exist_ok=True)
        sheet = Canvas(112 * 3, 96 * 2)
        for i, spec in enumerate(characters.CHARS):
            sheet.paste(characters.sheet(spec), (i % 3) * 112, (i // 3) * 96)
        write(os.path.join(preview, 'characters.png'), upscale(sheet.rgba(), 4))
        items = [f() for f in furniture.ASSETS.values()]
        W = 320
        board, x, y, rowh = Canvas(W, 200), 2, 2, 0
        for it in items:
            if x + it.w > W:
                x, y, rowh = 2, y + rowh + 3, 0
            board.paste(it, x, y)
            x += it.w + 3; rowh = max(rowh, it.h)
        write(os.path.join(preview, 'furniture.png'), upscale(board.rgba(), 3))
        t = Canvas(16 * 9 + 70, 128)
        for i, f in enumerate(tiles.floors()):
            t.paste(f, i * 16, 0)
        t.paste(tiles.walls(), 16 * 9 + 4, 0)
        t.paste(tiles.pet_sheet('hamster'), 0, 20); t.paste(carpet(0), 100, 20)
        write(os.path.join(preview, 'tiles.png'), upscale(t.rgba(), 3))
    print('generated', len(furniture.ASSETS), 'furniture sprites,', len(characters.CHARS), 'characters')


if __name__ == '__main__':
    main()
