#!/usr/bin/env python3
"""
sort_outer_rim.py — extraction v2, specific to the Star Wars: Outer Rim project.

Reads the two Tabletop Simulator mods used by the project and writes every
component into a sorted folder tree, by pack (base / expansion) and by type,
with stable names that become the component IDs in the project.

  [Mod 01]  Workshop 1790581987  primary source
  (Mod 02)  Workshop 2820783326  secondary: ambitions, names, expansion alternates

Needs tts_extract.py (v1) in the same folder, and Pillow, numpy and OpenCV:
  python -m pip install pillow numpy opencv-python

Usage (PowerShell, from the folder that holds both scripts):
  python sort_outer_rim.py
  python sort_outer_rim.py --out .\\sorted --tts-dir "C:\\...\\Tabletop Simulator"

Output (default .\\sorted):
  base/       base game components
  expansion/  Unfinished Business; expansion cards exist twice, in mod01/ and mod02/
  _review/    images to check the automatic matches by eye
  _unsorted/  single images that are not in the known table
  manifest.json, report.md, CREDITS.md

Everything about which sheet or token is what lives in the tables below.
If something lands in the wrong place, fix the table and run again.
Re-running only rewrites files whose content changed.
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import tts_extract as tx  # noqa: E402  (v1, same folder)
from PIL import Image, ImageFilter, ImageOps

try:
    import numpy as np
except ImportError:  # pragma: no cover
    sys.exit("numpy is required: python -m pip install numpy")

def _decode_with_opencv(path: Path) -> Image.Image | None:
    """Fallback for PNGs that Pillow rejects but libpng only warns about.

    Some (Mod 02) uploads (player boards 1, 2 and 4) carry an iCCP chunk with a
    bad CRC: Pillow refuses the file, OpenCV decodes the pixels fine.
    """
    try:
        import cv2
    except ImportError:
        return None
    arr = cv2.imdecode(np.fromfile(str(path), dtype=np.uint8), cv2.IMREAD_UNCHANGED)
    if arr is None:
        return None
    if arr.ndim == 2:
        return Image.fromarray(arr)
    if arr.shape[2] == 4:
        return Image.fromarray(cv2.cvtColor(arr, cv2.COLOR_BGRA2RGBA))
    return Image.fromarray(cv2.cvtColor(arr, cv2.COLOR_BGR2RGB))


MODS = {
    "mod01": {"workshop_id": "1790581987", "label": "[Mod 01]",
              "url": "https://steamcommunity.com/sharedfiles/filedetails/?id=1790581987"},
    "mod02": {"workshop_id": "2820783326", "label": "(Mod 02)",
              "url": "https://steamcommunity.com/sharedfiles/filedetails/?id=2820783326"},
}

REGIONS = ["navpoint", "cantonica-ord-mantell", "lothal-mon-calamari", "kessel-maelstrom",
           "tatooine-nal-hutta", "ryloth-naboo", "ring-of-kafrene-takodana", "core-worlds"]

# --------------------------------------------------------------------------
# Card sheets: (pack, category, deck).  Keys are the deck numbers shown in
# the preview images.  A tuple of keys means "any of these" (shared sheet).
# --------------------------------------------------------------------------
SHEETS = {
    "mod01": {
        40: ("base", "market", "luxury"),
        41: ("base", "market", "ship"),
        42: ("base", "market", "bounty"),
        43: ("base", "market", "job"),
        48: ("base", "market", "gear"),
        49: ("base", "market", "cargo"),
        45: ("base", "databank", None),
        46: ("base", "characters", None),
        47: ("base", "reference", None),
        50: ("base", "encounters", "navpoint"),
        51: ("base", "encounters", "lothal-mon-calamari"),
        52: ("base", "encounters", "ring-of-kafrene-takodana"),
        53: ("base", "encounters", "ryloth-naboo"),
        54: ("base", "encounters", "tatooine-nal-hutta"),
        55: ("base", "encounters", "cantonica-ord-mantell"),
        56: ("base", "encounters", "kessel-maelstrom"),
        (57, 58, 59, 60): ("base", "ship-sheets", "starter"),
        **{k: ("base", "ship-sheets", None) for k in range(61, 69)},
        69: ("base", "ai", "smuggler"),
        71: ("expansion", "ai", "set-1"),
        72: ("expansion", "ai", "set-2"),
        74: ("expansion", "characters", None),
        75: ("expansion", "encounters", "core-worlds"),
        76: ("expansion", "databank", None),
        77: ("expansion", "reference", None),
        78: ("expansion", "encounters", "kessel-maelstrom"),
        79: ("expansion", "encounters", "cantonica-ord-mantell"),
        80: ("expansion", "market", "bounty"),
        81: ("expansion", "market", "cargo"),
        82: ("expansion", "market", "gear"),
        83: ("expansion", "market", "job"),
        84: ("expansion", "market", "luxury"),
        85: ("expansion", "market", "ship"),
        86: ("expansion", "encounters", "lothal-mon-calamari"),
        87: ("expansion", "encounters", "navpoint"),
        88: ("expansion", "encounters", "ring-of-kafrene-takodana"),
        89: ("expansion", "encounters", "ryloth-naboo"),
        90: ("expansion", "encounters", "tatooine-nal-hutta"),
        **{k: ("expansion", "ambitions", None) for k in range(91, 99)},
        **{k: ("expansion", "ship-sheets", None) for k in range(99, 105)},
    },
    # (Mod 02) decks mix base and expansion. Used only as match candidates,
    # except ambitions, which are taken from here.
    "mod02": {
        620: ("mixed", "characters", None),
        621: ("expansion", "reference", None),
        624: ("mixed", "encounters", "lothal-mon-calamari"),
        627: ("mixed", "encounters", "navpoint"),
        637: ("expansion", "ambitions", None),
        638: ("mixed", "databank", None),
        639: ("mixed", "databank", None),
        2200: ("mixed", "encounters", "cantonica-ord-mantell"),
        2201: ("mixed", "encounters", "kessel-maelstrom"),
        2202: ("mixed", "encounters", "tatooine-nal-hutta"),
        2203: ("mixed", "encounters", "ryloth-naboo"),
        2204: ("mixed", "encounters", "ring-of-kafrene-takodana"),
        2205: ("mixed", "encounters", "core-worlds"),
        2206: ("mixed", "market", "bounty"),
        2208: ("mixed", "market", "gear"),
        2209: ("mixed", "market", "job"),
        2210: ("mixed", "market", "luxury"),
        2211: ("mixed", "market", "ship"),
        2212: ("mixed", "market", "cargo"),
        2220: ("mixed", "databank", None),   # starting cards (#90-#92)
        2221: ("mixed", "databank", None),
        2222: ("mixed", "databank", None),
        # 47, 69, 71, 72: identical to [Mod 01].  2194: card back.  2226: portraits.
    },
}

# --------------------------------------------------------------------------
# [Mod 01] single images, by the 8-character hash shown in the preview.
# (pack, folder, name).  Names marked "verify" were read from thumbnails.
# --------------------------------------------------------------------------
STANDEES = {
    "6e2038d7": ("base", "IG-88"), "d9fdad88": ("base", "Boba Fett"),
    "1127a324": ("base", "Doctor Aphra"), "28f9e372": ("base", "Jyn Erso"),
    "6e1b0b03": ("base", "Ketsu Onyo"), "e9d119e8": ("base", "Lando Calrissian"),
    "0b652d31": ("base", "Bossk"), "710a70ee": ("base", "Han Solo"),
    "2f14f968": ("expansion", "Cad Bane"), "5aa82f09": ("expansion", "Chewbacca"),
    "196e8110": ("expansion", "Dengar"), "223ba858": ("expansion", "Enfys Nest"),
    "5fa9154a": ("expansion", "Hera Syndulla"), "657f0397": ("expansion", "Hondo Ohnaka"),
    "419621a8": ("expansion", "Black Krrsantan"), "c3304043": ("expansion", "Maz Kanata"),
}

CONTACTS = {  # hash: (number, name)
    "0022cf78": (2, "4-LOM"), "9a2aaba7": (3, "Bib Fortuna"), "ac50b69b": (4, "Black Krrsantan"),
    "abd69c35": (5, "Black Sun Agent"), "2d6ffdff": (6, "BoShek"), "a9491857": (7, "Chewbacca"),
    "706f84fe": (8, "Dengar"), "7be06f92": (9, "Doctor Evazan"), "84a5f78d": (10, "Garindan"),
    "d3aa6a40": (11, "Grand Admiral Thrawn"), "37b846f5": (12, "Greedo"),
    "ec4a7743": (13, "Hera Syndulla"), "baf7b8b0": (14, "Hondo Ohnaka"),
    "c0fcfe1b": (15, "IG-RM Droid"), "66d00f1b": (16, "K-2SO"), "479063dd": (17, "Leia Organa"),
    "85d9019a": (18, "Lobot"), "0c9ec727": (19, "Maz Kanata"), "691d70b2": (20, "Ponda Baba"),
    "1657a4b4": (21, "Saw Gerrera"), "42b9e5ba": (22, "W1-LE"), "5f3e195a": (23, "Ziton Moj"),
    "18319f84": (24, "Ahsoka Tano"), "080a6244": (25, "Boba Fett"), "637db787": (26, "Bossk"),
    "396afdcd": (27, "Chopper"), "c73831ea": (28, "Doctor Aphra"), "4c54c49e": (29, "Enfys Nest"),
    "2dfa6ba7": (30, "Han Solo"), "03863eda": (31, "IG-88"), "9f48c819": (32, "Jyn Erso"),
    "04d1ae27": (33, "Ketsu Onyo"), "2a1c7b82": (34, "Lando Calrissian"),
}

PATROLS = {  # hash: (faction, level).  Hutt = green ring, Syndicate = yellow ring.
    "e2e1c7cf": ("hutt", 1), "ae7b03eb": ("hutt", 2), "42f10e82": ("hutt", 3), "bde22908": ("hutt", 4),
    "32699cf0": ("syndicate", 1), "f6cc2ce6": ("syndicate", 2), "0b05adaf": ("syndicate", 3), "0930d5c6": ("syndicate", 4),
    "052e3900": ("imperial", 1), "b9019eb1": ("imperial", 2), "15f6d03f": ("imperial", 3), "4516d43c": ("imperial", 4),
    "47d40c51": ("rebel", 1), "457462b7": ("rebel", 2), "46018846": ("rebel", 3), "2937930b": ("rebel", 4),
}

TOKENS = {  # hash: (pack, folder, name)
    "b3a5ad1e": ("base", "tokens", "credit-1000"),
    "58d8318a": ("base", "tokens", "credit-5000"),
    "5eb28085": ("base", "tokens", "credit-10000"),
    "23f04b1c": ("base", "tokens", "damage"),
    "2b78fbd6": ("base", "tokens", "goal"),
    "bd7cc565": ("base", "tokens", "reputation-hutt"),
    "fef377a1": ("base", "tokens", "reputation-imperial"),
    "bf53e6b1": ("base", "tokens", "reputation-rebel"),
    "567e3eea": ("base", "tokens", "reputation-syndicate"),
    "a9eaeaf9": ("base", "map", "endcap-empire-hutt"),
    "d2bd244d": ("base", "map", "endcap-rebel-syndicate"),
    "55938415": ("expansion", "map", "endcap-spinward-core"),
    "4ea8109c": ("expansion", "map", "endcap-trailing-core"),
    "1d158c94": ("expansion", "tokens", "debt-1"),
    "efb70fca": ("expansion", "tokens", "debt-2"),
    "8d0ab2a2": ("expansion", "tokens", "debt-3"),
    "161075ff": ("expansion", "tokens", "debt-4"),
    # Readable since the OpenCV fallback (bad iCCP CRC). Named by frame colour.
    "8ab23018": ("base", "player-boards", "player-board-green"),
    "97335e9a": ("base", "player-boards", "player-board-red"),
    "9fa48d9a": ("base", "player-boards", "player-board-yellow"),
    "d521160a": ("base", "player-boards", "player-board-white"),
    "dc47704f": ("base", "tokens", "dice"),
}

MAP_TILES = {  # [Mod 01] token nickname -> file name
    "Cantonica - Ord Mantell": "tile-cantonica-ord-mantell",
    "Lothal - Mon Calamari": "tile-lothal-mon-calamari",
    "Kessel": "tile-kessel",
    "Tatooine - Nal Hutta": "tile-tatooine-nal-hutta",
    "Ryloth - Naboo": "tile-ryloth-naboo",
    "The Ring of Kafrene - Takodama": "tile-ring-of-kafrene-takodana",
}

IGNORED_KINDS = {"model_texture"}  # table, cabinets, 3D models


# --------------------------------------------------------------------------
# Loading
# --------------------------------------------------------------------------

class Mod:
    def __init__(self, key: str, tts_dir: Path, extra_cache: list[Path]):
        self.key = key
        self.path = tx.find_mod_json(MODS[key]["workshop_id"], tts_dir)
        raw = self.path.read_text(encoding="utf-8")
        self.data = json.loads(raw)
        self.scan = tx.scan_mod(self.data, raw)
        self.cache = tx.ImageCache(tts_dir, extra_cache)
        self.face_keys: dict[str, set[int]] = defaultdict(set)
        self.objects: list[dict] = []   # objects with CustomImage
        for o in self.data.get("ObjectStates") or []:
            self._walk(o, [])
        backs = defaultdict(int)
        for sh in self.scan.sheets.values():
            if sh.back_url:
                backs[sh.back_url] += 1
        self.back_uses = backs
        self.cards_by_face: dict[str, list] = defaultdict(list)
        for c in self.scan.cards:
            self.cards_by_face[c.face_url].append(c)
        self._img_cache: dict[str, Image.Image | None] = {}

    def _walk(self, o: dict, path: list[str]):
        for k, v in (o.get("CustomDeck") or {}).items():
            try:
                self.face_keys[(v.get("FaceURL") or "").strip()].add(int(k))
            except ValueError:
                pass
        ci = o.get("CustomImage")
        if isinstance(ci, dict) and ci.get("ImageURL"):
            states = []
            for st in (o.get("States") or {}).values():
                sci = st.get("CustomImage") or {}
                if sci.get("ImageURL"):
                    states.append({"nickname": (st.get("Nickname") or "").strip(),
                                   "url": sci["ImageURL"].strip()})
            self.objects.append({
                "name": o.get("Name", ""), "nickname": (o.get("Nickname") or "").strip(),
                "path": " > ".join(path), "front": ci["ImageURL"].strip(),
                "back": (ci.get("ImageSecondaryURL") or "").strip(), "states": states,
            })
        nick = (o.get("Nickname") or "").strip()
        for key in ("ContainedObjects", "ChildObjects"):
            for c in o.get(key) or []:
                if isinstance(c, dict):
                    self._walk(c, path + ([nick] if nick else []))

    def file(self, url: str) -> Path | None:
        return self.cache.lookup(url)[0] if url else None

    def image(self, url: str) -> Image.Image | None:
        if url not in self._img_cache:
            p = self.file(url)
            img = None
            if p:
                try:
                    with Image.open(p) as im:
                        im.load()
                        img = im.copy()
                except Exception:
                    img = _decode_with_opencv(p)
            self._img_cache[url] = img
        return self._img_cache[url]

    def sheet_for(self, keyspec) -> list[tuple[str, object]]:
        keys = set(keyspec) if isinstance(keyspec, tuple) else {keyspec}
        return [(f, sh) for f, sh in self.scan.sheets.items() if self.face_keys.get(f, set()) & keys]


# --------------------------------------------------------------------------
# Cells and features
# --------------------------------------------------------------------------

def cells_of(mod: Mod, face: str, sh, inset: int):
    """Unique cells used on a sheet: [(index, copies, nicknames, front, back)]."""
    groups = defaultdict(list)
    for c in mod.cards_by_face.get(face, []):
        groups[c.index].append(c)
    front_sheet = mod.image(face)
    if front_sheet is None:
        return None
    per_card_back = sh.unique_back or (sh.num_w * sh.num_h == 1 and mod.back_uses.get(sh.back_url, 0) == 1)
    back_sheet = mod.image(sh.back_url) if (per_card_back and sh.back_url) else None
    out = []
    w, h = front_sheet.size
    for idx in sorted(groups):
        if idx >= sh.num_w * sh.num_h:
            continue
        front = front_sheet.crop(tx.cell_box(idx, sh.num_w, sh.num_h, w, h, inset))
        back = None
        if back_sheet is not None:
            if sh.num_w * sh.num_h == 1:
                back = back_sheet.copy()
            else:
                bw, bh = back_sheet.size
                back = back_sheet.crop(tx.cell_box(idx, sh.num_w, sh.num_h, bw, bh, inset))
        nicks = sorted({c.nickname for c in groups[idx] if c.nickname})
        out.append((idx, len(groups[idx]), nicks, front, back))
    return out


def edge_feature(img: Image.Image, size=(64, 96)) -> np.ndarray:
    g = ImageOps.equalize(ImageOps.grayscale(img.convert("RGB")))
    g = g.resize((size[0] * 4, size[1] * 4), Image.BILINEAR).filter(ImageFilter.GaussianBlur(5))
    a = np.asarray(g.resize(size, Image.BILINEAR), float)
    gx = np.diff(a, axis=1)[:-1, :]
    gy = np.diff(a, axis=0)[:, :-1]
    return np.hypot(gx, gy).ravel()


try:
    import cv2
except ImportError:  # falls back to the (weaker) edge correlation
    cv2 = None

MATCH_METHOD = "orb" if cv2 is not None else "edges"
# Flag thresholds for the review: (minimum score, minimum margin).
FLAG = {"orb": (150, 0.0), "edges": (0.5, 0.05)}


def _orb_desc(img: Image.Image, width: int = 600):
    g = ImageOps.grayscale(img.convert("RGB"))
    g = g.resize((width, max(1, int(g.height * width / g.width))), Image.BILINEAR)
    a = cv2.createCLAHE(2.0, (8, 8)).apply(np.asarray(g))
    return cv2.ORB_create(1500).detectAndCompute(a, None)


def _orb_score(da, db, bf) -> int:
    """Number of keypoint matches consistent with one homography (RANSAC).
    Robust to scan differences, scale and 180-degree rotation."""
    (ka, a), (kb, b) = da, db
    if a is None or b is None or len(a) < 2 or len(b) < 2:
        return 0
    good = [p[0] for p in bf.knnMatch(a, b, k=2)
            if len(p) == 2 and p[0].distance < 0.75 * p[1].distance]
    if len(good) < 8:
        return len(good)
    src = np.float32([ka[g.queryIdx].pt for g in good])
    dst = np.float32([kb[g.trainIdx].pt for g in good])
    _, mask = cv2.findHomography(src, dst, cv2.RANSAC, 8.0)
    return int(mask.sum()) if mask is not None else 0


def _score_matrix(items_a, items_b):
    if MATCH_METHOD == "orb":
        da = [_orb_desc(im) for _, im in items_a]
        db = [_orb_desc(im) for _, im in items_b]
        bf = cv2.BFMatcher(cv2.NORM_HAMMING)
        return np.array([[_orb_score(x, y, bf) for y in db] for x in da], float)

    def prep(feats):
        # Remove each side's own mean (the shared frame layout of that scan).
        M = np.array(feats)
        M = M - M.mean(axis=0)
        return M / (np.linalg.norm(M, axis=1, keepdims=True) + 1e-9)
    A = prep([edge_feature(im) for _, im in items_a])
    S = A @ prep([edge_feature(im) for _, im in items_b]).T
    return np.maximum(S, A @ prep([edge_feature(im.rotate(180)) for _, im in items_b]).T)


def match(items_a: list, items_b: list, allow_rotation: bool = True):
    """Greedy one-to-one match.  items: [(id, image)].
    Returns {id_a: (id_b, score, margin)}.  With OpenCV, score is the number
    of geometrically consistent keypoint matches and margin is
    1 - second_best / best; without it, an edge correlation is used."""
    if not items_a or not items_b:
        return {}
    S = _score_matrix(items_a, items_b)
    result, used_a, used_b = {}, set(), set()
    for flat in np.argsort(-S, axis=None):
        i, j = divmod(int(flat), S.shape[1])
        if i in used_a or j in used_b:
            continue
        row = np.sort(S[i])[::-1]
        if MATCH_METHOD == "orb":
            margin = 1.0 - (row[1] / row[0]) if len(row) > 1 and row[0] > 0 else (1.0 if row[0] > 0 else 0.0)
        else:
            margin = float(row[0] - row[1]) if len(row) > 1 else 1.0
        result[items_a[i][0]] = (items_b[j][0], float(S[i, j]), float(margin))
        used_a.add(i)
        used_b.add(j)
        if len(used_a) == len(items_a) or len(used_b) == len(items_b):
            break
    return result


# --------------------------------------------------------------------------
# Writer
# --------------------------------------------------------------------------

class Writer:
    def __init__(self, root: Path):
        self.root = root
        self.manifest: list[dict] = []
        self.written = 0
        self.ids: set[str] = set()

    def save(self, img: Image.Image, rel: str, **meta):
        path = self.root / rel
        data = tx.png_bytes(img)
        self.written += tx.write_if_changed(path, data)
        rec = {"file": rel.replace("\\", "/"), "size": list(img.size), "sha256": tx.sha256_bytes(data)}
        rec.update(meta)
        self.manifest.append(rec)
        return rec


def card_folder(pack, category, deck, version=None):
    parts = [pack, category] + ([deck] if deck else []) + ([version] if version else [])
    return "/".join(parts)


def card_id(pack, category, deck, idx_label):
    cat = {"encounters": "encounter", "characters": "character", "ship-sheets": "ship",
           "ambitions": "ambition"}.get(category, category)
    return "-".join([pack, cat] + ([deck] if deck else []) + [idx_label])


# --------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------

def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Sort Outer Rim components from the two TTS mods.")
    ap.add_argument("--out", default="sorted")
    ap.add_argument("--tts-dir")
    ap.add_argument("--cache-dir", action="append", help="extra image folder (repeatable)")
    ap.add_argument("--inset", type=int, default=2)
    args = ap.parse_args(argv)

    tts_dir = tx.find_tts_dir(args.tts_dir)
    extra = [Path(d) for d in (args.cache_dir or [])]
    m1, m2 = Mod("mod01", tts_dir, extra), Mod("mod02", tts_dir, extra)
    out = Path(args.out)
    W = Writer(out)
    notes: list[str] = []
    review: dict[str, list] = defaultdict(list)

    # ---- 1. [Mod 01] card sheets -----------------------------------------
    cards01: dict[tuple, list] = defaultdict(list)   # (category, deck) -> [(id, pack, img, rec)]
    backs_done: set[str] = set()
    for keyspec, (pack, category, deck) in SHEETS["mod01"].items():
        found = m1.sheet_for(keyspec)
        if not found:
            notes.append(f"[Mod 01] sheet {keyspec}: not found in the mod")
            continue
        for face, sh in found:
            cells = cells_of(m1, face, sh, args.inset)
            if cells is None:
                notes.append(f"[Mod 01] sheet {keyspec} ({category}): image missing from cache")
                continue
            dual = pack == "expansion" and category in ("market", "encounters", "databank",
                                                         "characters", "reference")
            folder = card_folder(pack, category, deck, "mod01" if dual else None)
            # Databank numbers shared by several different cards get a, b, c...
            num_total = defaultdict(int)
            for _, _, nk, _, _ in cells:
                if nk and nk[0].isdigit():
                    num_total[int(nk[0])] += 1
            num_seen = defaultdict(int)
            for idx, copies, nicks, front, back in cells:
                if category == "databank" and nicks and nicks[0].isdigit():
                    num = int(nicks[0])
                    num_seen[num] += 1
                    label = f"{num:03d}" + (chr(96 + num_seen[num]) if num_total[num] > 1 else "")
                elif category in ("ambitions", "ship-sheets") and sh.num_w * sh.num_h == 1:
                    label = f"s{sh.deck_key}"
                else:
                    label = f"{idx:02d}"
                cid = card_id(pack, category, deck, label)
                n = 2
                while cid in W.ids:
                    cid = card_id(pack, category, deck, f"{label}-{n}")
                    n += 1
                W.ids.add(cid)
                meta = dict(id=cid, pack=pack, category=category, deck=deck, side="front",
                            source="mod01", sheet=sh.deck_key, cell=idx, copies=copies,
                            nickname=", ".join(nicks), source_url=face)
                if category == "ambitions":
                    # Ambitions are re-numbered from (Mod 02) below.
                    cards01[(category, deck)].append((cid, pack, front, back, meta))
                    continue
                if category == "ship-sheets":
                    cards01[(category, deck or "")].append((cid, pack, front, back, meta))
                    continue
                rec = W.save(front, f"{folder}/{cid}.png", **meta)
                if back is not None:
                    W.save(back, f"{folder}/{cid}-back.png", **{**meta, "side": "back",
                                                               "source_url": sh.back_url})
                cards01[(category, deck)].append((cid, pack, front, back, rec))
            # Shared deck back, once per folder.
            if sh.back_url and not (sh.unique_back or sh.num_w * sh.num_h == 1) \
                    and folder not in backs_done:
                bimg = m1.image(sh.back_url)
                if bimg is not None:
                    W.save(bimg, f"{folder}/_back.png", id=f"{folder}/_back", pack=pack,
                           category=category, deck=deck, side="deck-back", source="mod01",
                           source_url=sh.back_url)
                    backs_done.add(folder)

    # ---- 2. (Mod 02) candidates ------------------------------------------
    cand02: dict[tuple, list] = defaultdict(list)  # (category, deck) -> [(key, img, back, meta)]
    for keyspec, (pack, category, deck) in SHEETS["mod02"].items():
        for face, sh in m2.sheet_for(keyspec):
            cells = cells_of(m2, face, sh, args.inset)
            if cells is None:
                notes.append(f"(Mod 02) sheet {keyspec} ({category}): image missing from cache")
                continue
            for idx, copies, nicks, front, back in cells:
                cand02[(category, deck)].append(
                    (f"{sh.deck_key}:{idx}", front, back,
                     dict(sheet=sh.deck_key, cell=idx, source_url=face, back_url=sh.back_url)))

    # ---- 3. Expansion alternates from (Mod 02) ----------------------------
    def save_alternates(category, deck, group01):
        cands = cand02.get((category, deck), [])
        if not cands:
            return
        if category == "reference":   # (Mod 02) has only the expansion card
            group01 = [g for g in group01 if g[1] == "expansion"]
        res = match([(c[0], c[2]) for c in group01], [(k, im) for k, im, _, _ in cands])
        by_key = {k: (im, b, meta) for k, im, b, meta in cands}
        for cid, pack, front, back, _ in group01:
            if pack != "expansion" or cid not in res:
                continue
            key, score, margin = res[cid]
            im, b, meta = by_key[key]
            folder = card_folder(pack, category, deck, "mod02")
            info = dict(id=cid, pack=pack, category=category, deck=deck, side="front",
                        source="mod02", match_score=round(score, 3),
                        match_margin=round(margin, 3), **meta)
            W.save(im, f"{folder}/{cid}.png", **info)
            if b is not None:
                W.save(b, f"{folder}/{cid}-back.png", **{**info, "side": "back"})
            review[f"{category}-{deck or 'all'}"].append((cid, front, im, score, margin))

    for (category, deck), group in cards01.items():
        if category in ("market", "encounters", "characters", "reference"):
            save_alternates(category, deck, group)
    db = cards01.get(("databank", None), [])
    if db:
        save_alternates("databank", None, db)

    # ---- 4. Ambitions: (Mod 02) is primary, [Mod 01] copies matched -------
    amb02 = cand02.get(("ambitions", None), [])
    amb01 = cards01.get(("ambitions", None), [])
    if amb02:
        res = match([(k, im) for k, im, _, _ in amb02], [(c[0], c[2]) for c in amb01])
        by01 = {c[0]: c for c in amb01}
        for n, (key, im, b, meta) in enumerate(amb02):
            cid = f"expansion-ambition-{n:02d}"
            W.save(im, f"expansion/ambitions/mod02/{cid}.png", id=cid, pack="expansion",
                   category="ambitions", side="front", source="mod02", **meta)
            if key in res:
                k01, score, margin = res[key]
                _, _, f1, b1, meta1 = by01[k01]
                W.save(f1, f"expansion/ambitions/mod01/{cid}.png",
                       **{**meta1, "id": cid, "match_score": round(score, 3)})
                review["ambitions"].append((cid, f1, im, score, margin))
        ab = m2.image(next(iter(sh.back_url for f, sh in m2.sheet_for(637)), ""))
        if ab is not None:
            W.save(ab, "expansion/ambitions/_back.png", id="expansion/ambitions/_back",
                   pack="expansion", category="ambitions", side="deck-back", source="mod02")

    # ---- 5. Ship sheets: names from (Mod 02) tokens -----------------------
    ship_tokens = []
    for o in m2.objects:
        if o["name"] != "Custom_Token" or not o["nickname"] or o["nickname"].startswith("Player"):
            continue
        img = m2.image(o["front"])
        if img is None or img.width < 1.6 * img.height:
            continue
        ship_tokens.append((o["nickname"], img, o["states"]))
    ships01 = [c for (cat, _), g in cards01.items() if cat == "ship-sheets" for c in g]
    res = match([(c[0], c[2]) for c in ships01], [(n, im) for n, im, _ in ship_tokens], False)
    states_of = {n: st for n, _, st in ship_tokens}
    seen_names: set[str] = set()
    for cid, pack, front, back, meta in ships01:
        name, back_name, score = None, None, 0.0
        if cid in res and res[cid][1] >= (40 if MATCH_METHOD == "orb" else 0.4):
            name, score = res[cid][0], res[cid][1]
            st = states_of.get(name) or []
            back_name = st[0]["nickname"] if st else None
        base = f"{pack}-ship-{tx.slug(name)}" if name else cid
        while base in seen_names:
            base += "-2"
        seen_names.add(base)
        folder = f"{pack}/ship-sheets"
        info = {**meta, "id": base, "name": name, "flipped_name": back_name,
                "match_score": round(score, 3)}
        W.save(front, f"{folder}/{base}.png", **info)
        if back is not None:
            W.save(back, f"{folder}/{base}-back.png", **{**info, "side": "back"})
        review["ship-names"].append((f"{base}", front, None, score, 0.0))

    # ---- 6. Single images --------------------------------------------------
    contact02 = []  # (class, image, sha8)
    for o in m2.objects:
        p = o["path"]
        for cls in ("White", "Green", "Yellow", "Orange"):
            if p.endswith(f"{cls} Contacts"):
                img, f = m2.image(o["front"]), m2.file(o["front"])
                if img is not None:
                    contact02.append((cls.lower(), img, tx.sha256_bytes(f.read_bytes())[:8]))
    c02_by_hash = {h: c for c, _, h in contact02}

    unsorted = []
    for o in m1.objects:
        kind = {"Custom_Tile": "tile", "Custom_Token": "token", "Figurine_Custom": "figurine",
                "Custom_Dice": "dice"}.get(o["name"], o["name"])
        f = m1.file(o["front"])
        front = m1.image(o["front"])
        if front is None:
            if kind not in IGNORED_KINDS:
                notes.append(f"[Mod 01] {kind} '{o['nickname']}': image missing or unreadable")
            continue
        h = tx.sha256_bytes(f.read_bytes())[:8]
        back = m1.image(o["back"]) if o["back"] else None
        if back is not None and m1.file(o["back"]) == f:
            back = None

        def put(rel_base, **meta):
            meta.setdefault("source", "mod01")
            meta.setdefault("hash", h)
            W.save(front, f"{rel_base}.png", side="front", source_url=o["front"], **meta)
            if back is not None:
                W.save(back, f"{rel_base}-back.png", side="back", source_url=o["back"], **meta)

        if h in STANDEES:
            pack, name = STANDEES[h]
            sid = f"{pack}-standee-{tx.slug(name)}"
            put(f"{pack}/standees/{sid}", id=sid, pack=pack, category="standees", name=name)
        elif h in CONTACTS:
            num, name = CONTACTS[h]
            pack = "expansion" if num >= 24 else "base"
            cls = "orange" if num >= 24 else c02_by_hash.get(h)
            how = "table" if num >= 24 else ("exact" if cls else None)
            if cls is None and contact02:
                r = match([(h, front)], [(i, im) for i, (_, im, _) in enumerate(contact02)], False)
                if h in r:
                    cls = contact02[r[h][0]][0]
                    how = f"visual {r[h][1]:.2f}"
            sid = f"{pack}-contact-{num:02d}-{tx.slug(name)}"
            put(f"{pack}/contacts/{sid}", id=sid, pack=pack, category="contacts",
                number=num, name=name, contact_class=cls, class_source=how)
        elif h in PATROLS:
            fac, lvl = PATROLS[h]
            sid = f"base-patrol-{fac}-{lvl}"
            put(f"base/patrols/{sid}", id=sid, pack="base", category="patrols",
                faction=fac, level=lvl)
        elif h in TOKENS:
            pack, folder, name = TOKENS[h]
            sid = f"{pack}-{name}"
            put(f"{pack}/{folder}/{sid}", id=sid, pack=pack, category=folder)
        elif o["nickname"] in MAP_TILES:
            sid = f"base-map-{MAP_TILES[o['nickname']]}"
            put(f"base/map/{sid}", id=sid, pack="base", category="map")
        elif kind in IGNORED_KINDS:
            continue
        else:
            sid = f"{kind}_{tx.slug(o['nickname'] or 'unnamed')}_{h}"
            put(f"_unsorted/{sid}", id=sid, pack=None, category="unsorted",
                nickname=o["nickname"], path=o["path"])
            unsorted.append(f"{kind} '{o['nickname']}' ({h}) at '{o['path'] or 'table'}'")

    # Dice and the four player boards come from [Mod 01] (TOKENS above). (Mod 02)
    # was the fallback while Pillow could not read them; its boards are two
    # near-identical blue variants, so they are no longer extracted.

    # ---- 7. Review images, manifest, report, credits ----------------------
    rev_dir = out / "_review"
    rev_dir.mkdir(parents=True, exist_ok=True)
    low = []
    for group, pairs in sorted(review.items()):
        tiles = []
        for cid, a, b, score, margin in pairs:
            pair = Image.new("RGB", (400, 300), (60, 60, 60))
            for i, im in enumerate([a, b]):
                if im is None:
                    continue
                t = im.convert("RGB")
                t.thumbnail((195, 295))
                pair.paste(t, (i * 205, 0))
            min_s, min_m = FLAG[MATCH_METHOD]
            flag = "  CHECK" if (b is not None and (score < min_s or margin < min_m)) else ""
            if flag:
                low.append(f"{group}: {cid} (score {score:.2f}, margin {margin:.2f})")
            tiles.append((pair, [cid[-30:], f"score {score:.2f}  margin {margin:.2f}{flag}"]))
        title = (f"{group}: [Mod 01] left, (Mod 02) right" if group != "ship-names"
                 else "ship sheets and the (Mod 02) name they matched")
        tx._grid_image(tiles, title, 6, 400, 300).save(rev_dir / f"{group}.jpg", quality=85)

    W.manifest.sort(key=lambda r: r["file"])
    mbytes = json.dumps({"tool": "sort_outer_rim.py", "manifest_version": 1, "files": W.manifest},
                        indent=2, ensure_ascii=False, sort_keys=True).encode("utf-8")
    W.written += tx.write_if_changed(out / "manifest.json", mbytes)

    counts = defaultdict(int)
    for r in W.manifest:
        counts["/".join(r["file"].split("/")[:-1])] += 1
    lines = ["# Outer Rim sort report", "", f"- Files: {len(W.manifest)}",
             f"- Match method: {MATCH_METHOD}" + ("" if MATCH_METHOD == "orb" else
                                                 " (install opencv-python for reliable matches)"), "",
             "## Files per folder", "", "| Folder | Files |", "|---|---|"]
    lines += [f"| {k} | {v} |" for k, v in sorted(counts.items())]
    lines += ["", "## Matches to check", ""]
    lines += [f"- {x}" for x in low] or ["None flagged."]
    lines += ["", "## Not sorted (unknown hash; add them to the tables)", ""]
    lines += [f"- {x}" for x in unsorted] or ["None."]
    lines += ["", "## Notes", ""] + ([f"- {x}" for x in notes] or ["None."])
    lines += ["", "Expansion Ring of Kafrene/Takodana has 11 cards: one replaces base "
              "Takodana #10 (RULES R-3.17). Identify it during transcription."]
    W.written += tx.write_if_changed(out / "report.md", ("\n".join(lines) + "\n").encode("utf-8"))

    credits = ["# CREDITS", "",
               "Component images were extracted from these Tabletop Simulator Workshop mods:", ""]
    for k, info in MODS.items():
        title = (m1 if k == "mod01" else m2).data.get("SaveName", "")
        credits.append(f"- {info['label']} {title} — {info['url']}")
    credits += ["", "Thanks to the mod authors for the scans. Star Wars: Outer Rim is "
                "© Fantasy Flight Games and © Lucasfilm Ltd. This is a personal, "
                "non-commercial project.", ""]
    W.written += tx.write_if_changed(out / "CREDITS.md", "\n".join(credits).encode("utf-8"))

    print("\n".join(lines))
    print(f"\nOutput: {out.resolve()}  ({W.written} file(s) written or updated)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
