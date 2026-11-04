#!/usr/bin/env python3
"""
tts_extract.py — extract images from a Tabletop Simulator mod.

Generic: works with any TTS mod, not only Outer Rim.

What it does
------------
1. Finds the mod JSON (by Workshop ID or by path).
2. Walks every object in the save, including objects nested in bags,
   decks, alternate states and child objects.
3. Lists every image URL the mod references and checks whether each one
   is in the local TTS image cache.
4. Slices card sheets into one image per card, using the grid declared in
   the mod (NumWidth x NumHeight) and each card's CardID.
5. Copies single-image objects (tiles, tokens, boards) as they are.
6. Writes a manifest (JSON) and a human-readable report (Markdown).

Commands
--------
  inventory   Read only. Reports what the mod references and what is
              present / missing in the cache. Writes nothing to disk
              except the report, if --out is given.
  extract     Does everything above and writes the images.

Examples (Windows, PowerShell)
------------------------------
  python tts_extract.py inventory --mod 1790581987
  python tts_extract.py extract --mod 1790581987 --label mod01 `
      --out C:\\TheOuterRimProject\\RawGameFiles\\tts

  # Explicit paths, if auto-detection fails:
  python tts_extract.py extract `
      --mod "C:\\Users\\me\\Documents\\My Games\\Tabletop Simulator\\Mods\\Workshop\\1790581987.json" `
      --tts-dir "C:\\Users\\me\\Documents\\My Games\\Tabletop Simulator" `
      --label mod01 --out C:\\TheOuterRimProject\\RawGameFiles\\tts

Output layout (inside --out/<label>/)
-------------------------------------
  sheets/      original card sheets, copied untouched from the cache
  cards/       one file per card face:  <deck>-<index>_<name>.png
  backs/       card backs (one per sheet, or one per card if UniqueBack)
  images/      single-image objects (tiles, tokens, boards)
  manifest.json
  report.md

Re-running is safe: files that already exist with the same content are
left untouched, and the manifest is written in a stable order.

Requires Python 3.9+ and Pillow (pip install pillow).
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import re
import shutil
import sys
import urllib.request
from dataclasses import dataclass, field, asdict
from pathlib import Path

try:
    from PIL import Image
except ImportError:  # pragma: no cover
    sys.exit("Pillow is required: pip install pillow")

# Large card sheets can exceed Pillow's decompression-bomb guard.
Image.MAX_IMAGE_PIXELS = None

IMAGE_EXTS = (".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif")
URL_RE = re.compile(r"https?://[^\s\"'<>\\]+", re.IGNORECASE)

# Object names TTS uses for single cards and for card decks.
CARD_NAMES = {"Card", "CardCustom"}
DECK_NAMES = {"Deck", "DeckCustom"}

# Steam moved its cloud host; caches may hold either form of the same URL.
HOST_ALIASES = [
    ("http://cloud-3.steamusercontent.com/", "https://steamusercontent-a.akamaihd.net/"),
    ("https://cloud-3.steamusercontent.com/", "https://steamusercontent-a.akamaihd.net/"),
]


# --------------------------------------------------------------------------
# Locating TTS folders
# --------------------------------------------------------------------------

def windows_documents_dir() -> Path | None:
    """Real 'Documents' folder on Windows, honouring OneDrive redirection."""
    if os.name != "nt":
        return None
    try:
        import ctypes
        from ctypes import wintypes

        class GUID(ctypes.Structure):
            _fields_ = [("Data1", wintypes.DWORD), ("Data2", wintypes.WORD),
                        ("Data3", wintypes.WORD), ("Data4", ctypes.c_ubyte * 8)]

        # FOLDERID_Documents {FDD39AD0-238F-46AF-ADB4-6C85480369C7}
        fid = GUID(0xFDD39AD0, 0x238F, 0x46AF,
                   (ctypes.c_ubyte * 8)(0xAD, 0xB4, 0x6C, 0x85, 0x48, 0x03, 0x69, 0xC7))
        out = ctypes.c_wchar_p()
        if ctypes.windll.shell32.SHGetKnownFolderPath(ctypes.byref(fid), 0, None,
                                                      ctypes.byref(out)) == 0:
            path = Path(out.value)
            ctypes.windll.ole32.CoTaskMemFree(out)
            return path
    except Exception:
        pass
    return None


def candidate_tts_dirs() -> list[Path]:
    home = Path.home()
    docs = windows_documents_dir()
    cands = []
    if docs:
        cands.append(docs / "My Games" / "Tabletop Simulator")
    cands += [
        home / "Documents" / "My Games" / "Tabletop Simulator",
        home / "OneDrive" / "Documents" / "My Games" / "Tabletop Simulator",
        home / "OneDrive" / "Documentos" / "My Games" / "Tabletop Simulator",
        # Mods stored in the game folder (TTS option "Mod Save Location").
        Path(r"C:\Program Files (x86)\Steam\steamapps\common\Tabletop Simulator\Tabletop Simulator_Data"),
        # Linux / macOS
        home / ".local" / "share" / "Tabletop Simulator",
        home / "Library" / "Tabletop Simulator",
    ]
    seen, unique = set(), []
    for c in cands:
        key = str(c).lower()
        if key not in seen:
            seen.add(key)
            unique.append(c)
    return unique


def find_tts_dir(explicit: str | None) -> Path:
    if explicit:
        p = Path(explicit)
        if (p / "Mods").is_dir():
            return p
        if p.name.lower() == "mods" and p.is_dir():
            return p.parent
        sys.exit(f"No 'Mods' folder found under: {p}")
    for c in candidate_tts_dirs():
        if (c / "Mods").is_dir():
            return c
    tried = "\n  ".join(str(c) for c in candidate_tts_dirs())
    sys.exit("Could not find the Tabletop Simulator folder. Tried:\n  "
             f"{tried}\nUse --tts-dir to point at it.")


def find_mod_json(mod: str, tts_dir: Path | None) -> Path:
    p = Path(mod)
    if p.suffix.lower() == ".json" and p.is_file():
        return p
    if mod.isdigit() and tts_dir:
        for sub in ("Workshop", "Saves"):
            cand = tts_dir / "Mods" / sub / f"{mod}.json"
            if cand.is_file():
                return cand
        # Some setups keep workshop mods one level down.
        hits = list((tts_dir / "Mods").rglob(f"{mod}.json"))
        if hits:
            return hits[0]
    sys.exit(f"Mod JSON not found for '{mod}'. Pass the full path with --mod.")


def list_workshop_mods(tts_dir: Path) -> list[tuple[str, str]]:
    """(id, name) for every mod the local workshop index knows about."""
    info = tts_dir / "Mods" / "Workshop" / "WorkshopFileInfos.json"
    out = []
    if info.is_file():
        try:
            for entry in json.loads(info.read_text(encoding="utf-8")):
                path = entry.get("Directory", "")
                mod_id = Path(path).stem if path else ""
                out.append((mod_id, entry.get("Name", "")))
        except Exception:
            pass
    if not out:
        for f in sorted((tts_dir / "Mods" / "Workshop").glob("*.json")):
            if f.name == "WorkshopFileInfos.json":
                continue
            try:
                name = json.loads(f.read_text(encoding="utf-8")).get("SaveName", "")
            except Exception:
                name = "(unreadable)"
            out.append((f.stem, name))
    return out


# --------------------------------------------------------------------------
# Cache lookup
# --------------------------------------------------------------------------

def cache_key(url: str) -> str:
    """TTS names cached files after the URL with non-alphanumerics removed."""
    return re.sub(r"[^A-Za-z0-9]", "", url)


def url_variants(url: str) -> list[str]:
    url = url.strip()
    variants = [url]
    for old, new in HOST_ALIASES:
        if url.startswith(old):
            variants.append(new + url[len(old):])
        if url.startswith(new):
            variants.append(old + url[len(new):])
    if url.startswith("http://"):
        variants.append("https://" + url[7:])
    elif url.startswith("https://"):
        variants.append("http://" + url[8:])
    seen, uniq = set(), []
    for v in variants:
        if v not in seen:
            seen.add(v)
            uniq.append(v)
    return uniq


class ImageCache:
    def __init__(self, tts_dir: Path | None, extra_dirs: list[Path]):
        self.by_stem: dict[str, Path] = {}
        dirs = []
        if tts_dir:
            dirs += [tts_dir / "Mods" / "Images", tts_dir / "Mods" / "Images Raw"]
        dirs += extra_dirs
        for d in dirs:
            if d.is_dir():
                for f in d.iterdir():
                    # Skip TTS raw textures (.rawt) and anything Pillow cannot read.
                    if f.is_file() and f.suffix.lower() in IMAGE_EXTS:
                        self.by_stem.setdefault(f.stem, f)

    def lookup(self, url: str) -> tuple[Path | None, str]:
        """Returns (path, how) where how is 'exact', 'fuzzy' or 'missing'."""
        for v in url_variants(url):
            hit = self.by_stem.get(cache_key(v))
            if hit:
                return hit, "exact"
        # Fallback: match on the URL's last path segment, if distinctive.
        tail = cache_key(url.rstrip("/").split("/")[-1])
        if len(tail) >= 12:
            matches = [p for s, p in self.by_stem.items() if s.endswith(tail)]
            if len(matches) == 1:
                return matches[0], "fuzzy"
        return None, "missing"


# --------------------------------------------------------------------------
# Walking the save
# --------------------------------------------------------------------------

@dataclass
class SheetInfo:
    deck_key: int
    face_url: str
    back_url: str
    num_w: int
    num_h: int
    unique_back: bool
    back_is_hidden: bool
    name: str = ""  # unique short name used for output files


@dataclass
class CardInfo:
    card_id: int
    deck_key: int
    index: int
    nickname: str
    description: str
    guid: str
    path: str  # container chain, e.g. "Market Decks > Cargo"
    face_url: str = ""  # sheet this card is cut from ("" if unresolved)


@dataclass
class ImageRef:
    kind: str       # tile, token, board, model_texture, ...
    url: str
    nickname: str
    path: str
    field: str


@dataclass
class ModScan:
    sheets: dict[str, SheetInfo] = field(default_factory=dict)   # by face URL
    cards: list[CardInfo] = field(default_factory=list)
    images: list[ImageRef] = field(default_factory=list)
    all_urls: set[str] = field(default_factory=set)
    object_count: int = 0


def _children(obj: dict):
    for key in ("ContainedObjects", "ChildObjects"):
        for child in obj.get(key) or []:
            if isinstance(child, dict):
                yield child
    states = obj.get("States")
    if isinstance(states, dict):
        for child in states.values():
            if isinstance(child, dict):
                yield child


def _register_custom_deck(scan: ModScan, custom_deck: dict) -> dict[int, str]:
    """Registers sheets and returns this object's {deck key: face URL}.
    Deck keys are local to each object, so the same key can mean different
    sheets in different decks; cards are therefore bound to a face URL."""
    local: dict[int, str] = {}
    for key, d in (custom_deck or {}).items():
        try:
            dk = int(key)
        except ValueError:
            continue
        face = (d.get("FaceURL") or "").strip()
        if not face:
            continue
        local[dk] = face
        scan.sheets.setdefault(face, SheetInfo(
            deck_key=dk,
            face_url=face,
            back_url=(d.get("BackURL") or "").strip(),
            num_w=int(d.get("NumWidth") or 10),
            num_h=int(d.get("NumHeight") or 7),
            unique_back=bool(d.get("UniqueBack")),
            back_is_hidden=bool(d.get("BackIsHidden")),
        ))
    return local


def _walk(scan: ModScan, obj: dict, path: list[str], seen_guids: set,
          inherited: dict[int, str] | None = None):
    scan.object_count += 1
    name = obj.get("Name", "")
    nick = (obj.get("Nickname") or "").strip()
    here = path + ([nick] if nick else [])

    decks = dict(inherited or {})
    if obj.get("CustomDeck"):
        decks.update(_register_custom_deck(scan, obj["CustomDeck"]))

    if name in CARD_NAMES and obj.get("CardID") is not None:
        cid = int(obj["CardID"])
        guid = obj.get("GUID", "")
        key = (guid, cid, " > ".join(path))
        if key not in seen_guids:
            seen_guids.add(key)
            scan.cards.append(CardInfo(
                card_id=cid, deck_key=cid // 100, index=cid % 100,
                nickname=nick, description=(obj.get("Description") or "").strip(),
                guid=guid, path=" > ".join(path),
                face_url=decks.get(cid // 100, ""),
            ))

    ci = obj.get("CustomImage")
    if isinstance(ci, dict):
        kind = {"Custom_Tile": "tile", "Custom_Token": "token",
                "Custom_Board": "board", "Figurine_Custom": "figurine"}.get(name, name or "image")
        for fld in ("ImageURL", "ImageSecondaryURL"):
            url = (ci.get(fld) or "").strip()
            if url:
                scan.images.append(ImageRef(kind, url, nick, " > ".join(path), fld))

    cm = obj.get("CustomMesh")
    if isinstance(cm, dict):
        for fld in ("DiffuseURL", "NormalURL"):
            url = (cm.get(fld) or "").strip()
            if url:
                scan.images.append(ImageRef("model_texture", url, nick, " > ".join(path), fld))

    for child in _children(obj):
        _walk(scan, child, here, seen_guids, decks)


def scan_mod(data: dict, raw_text: str) -> ModScan:
    scan = ModScan()
    seen: set = set()
    for obj in data.get("ObjectStates") or []:
        if isinstance(obj, dict):
            _walk(scan, obj, [], seen)
    # Catch-all: URLs in scripts, UI XML, notes, etc.
    for m in URL_RE.finditer(raw_text):
        scan.all_urls.add(m.group(0).rstrip(".,);]"))
    scan.cards.sort(key=lambda c: (c.deck_key, c.index, c.nickname, c.guid))
    return scan


# --------------------------------------------------------------------------
# Output helpers
# --------------------------------------------------------------------------

def slug(text: str, max_len: int = 60) -> str:
    text = re.sub(r"[^\w\s-]", "", text, flags=re.UNICODE).strip().lower()
    text = re.sub(r"[\s_-]+", "-", text)
    return text[:max_len].strip("-") or "unnamed"


def sha256_bytes(b: bytes) -> str:
    return hashlib.sha256(b).hexdigest()


def write_if_changed(path: Path, data: bytes) -> bool:
    """Writes only when content differs. Returns True if written."""
    if path.is_file() and path.read_bytes() == data:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return True


def png_bytes(img: Image.Image) -> bytes:
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=False)
    return buf.getvalue()


def cell_box(index: int, num_w: int, num_h: int, width: int, height: int, inset: int = 0):
    """Pixel box of one grid cell. `inset` trims pixels on every side, to
    drop the 1-2 px of neighbouring cards that JPEG compression bleeds in."""
    col, row = index % num_w, index // num_w
    x0 = round(col * width / num_w)
    x1 = round((col + 1) * width / num_w)
    y0 = round(row * height / num_h)
    y1 = round((row + 1) * height / num_h)
    if inset and (x1 - x0) > 4 * inset and (y1 - y0) > 4 * inset:
        x0, y0, x1, y1 = x0 + inset, y0 + inset, x1 - inset, y1 - inset
    return x0, y0, x1, y1


def download(url: str, dest_dir: Path) -> Path | None:
    dest_dir.mkdir(parents=True, exist_ok=True)
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "tts-extract/1.0"})
        with urllib.request.urlopen(req, timeout=30) as r:
            data = r.read()
        img = Image.open(io.BytesIO(data))
        ext = "." + (img.format or "png").lower().replace("jpeg", "jpg")
        out = dest_dir / (cache_key(url) + ext)
        write_if_changed(out, data)
        return out
    except Exception:
        return None


# --------------------------------------------------------------------------
# Main work
# --------------------------------------------------------------------------

def run(args) -> int:
    tts_dir = None
    if args.mod.isdigit() or args.tts_dir:
        tts_dir = find_tts_dir(args.tts_dir)
    elif not args.no_cache:
        try:
            tts_dir = find_tts_dir(None)
        except SystemExit:
            tts_dir = None

    if args.command == "list":
        if not tts_dir:
            tts_dir = find_tts_dir(args.tts_dir)
        print(f"TTS folder: {tts_dir}\n")
        for mod_id, name in list_workshop_mods(tts_dir):
            print(f"  {mod_id:>12}  {name}")
        return 0

    mod_path = find_mod_json(args.mod, tts_dir)
    raw_text = mod_path.read_text(encoding="utf-8")
    data = json.loads(raw_text)
    label = args.label or slug(data.get("SaveName") or mod_path.stem)

    extra = [Path(d) for d in (args.cache_dir or [])]
    out_root = Path(args.out) / label if args.out else None
    dl_dir = (out_root / "_downloads") if out_root else None
    if dl_dir:
        extra.append(dl_dir)
    cache = ImageCache(None if args.no_cache else tts_dir, extra)

    scan = scan_mod(data, raw_text)

    # Resolve every URL the mod uses as an image.
    image_urls = set(scan.sheets) | {s.back_url for s in scan.sheets.values() if s.back_url}
    image_urls |= {i.url for i in scan.images}
    other_urls = sorted(scan.all_urls - image_urls)

    resolved: dict[str, tuple[Path | None, str]] = {}
    for url in sorted(image_urls):
        hit, how = cache.lookup(url)
        if not hit and args.download and dl_dir:
            for variant in url_variants(url):
                got = download(variant, dl_dir)
                if got:
                    # Store under the URL the mod uses, so later runs find it.
                    final = got.with_name(cache_key(url) + got.suffix)
                    if final != got:
                        got.replace(final)
                    hit, how = final, "downloaded"
                    break
        resolved[url] = (hit, how)

    manifest = {
        "tool": "tts_extract.py",
        "manifest_version": 1,
        "mod": {
            "label": label,
            "save_name": data.get("SaveName", ""),
            "json_path": str(mod_path),
            "json_sha256": sha256_bytes(raw_text.encode("utf-8")),
        },
        "sheets": [], "cards": [], "backs": [], "images": [],
        "missing": [], "warnings": [], "other_urls": other_urls,
    }

    written = 0
    if args.command == "extract" and out_root:
        out_root.mkdir(parents=True, exist_ok=True)

    # --- card sheets -----------------------------------------------------
    # Unique output names per sheet (deck keys can repeat across decks).
    taken: set[str] = set()
    for sh in sorted(scan.sheets.values(), key=lambda s: (s.deck_key, s.face_url)):
        base, n = str(sh.deck_key), 1
        sh.name = base
        while sh.name in taken:
            n += 1
            sh.name = f"{base}.{n}"
        taken.add(sh.name)

    cards_by_face: dict[str, list[CardInfo]] = {}
    unresolved: list[CardInfo] = []
    for c in scan.cards:
        if c.face_url in scan.sheets:
            cards_by_face.setdefault(c.face_url, []).append(c)
        else:
            unresolved.append(c)

    for face_url, sh in sorted(scan.sheets.items(), key=lambda kv: (kv[1].deck_key, kv[1].name)):
        src, how = resolved.get(face_url, (None, "missing"))
        entry = {"sheet": sh.name, "deck_key": sh.deck_key, "face_url": face_url, "back_url": sh.back_url,
                 "grid": [sh.num_w, sh.num_h], "unique_back": sh.unique_back,
                 "cache": str(src) if src else None, "lookup": how,
                 "card_count": len(cards_by_face.get(face_url, []))}
        if not src:
            manifest["missing"].append({"url": face_url, "used_by": f"sheet {sh.name}"})
            manifest["sheets"].append(entry)
            continue

        with Image.open(src) as im:
            im.load()
            sheet = im.convert("RGBA") if im.mode in ("P", "LA") else im.copy()
        w, h = sheet.size
        entry.update({"size": [w, h],
                      "cell_size": [round(w / sh.num_w), round(h / sh.num_h)]})
        manifest["sheets"].append(entry)

        if args.command == "extract":
            sheet_out = out_root / "sheets" / f"{sh.name}{src.suffix.lower()}"
            written += write_if_changed(sheet_out, src.read_bytes())

        # Cards in this sheet. Several cards may share an index (duplicates).
        used_names: dict[str, int] = {}
        for c in cards_by_face.get(face_url, []):
            if c.index >= sh.num_w * sh.num_h:
                manifest["warnings"].append(
                    f"card {c.card_id} '{c.nickname}' is outside the {sh.num_w}x{sh.num_h} grid of sheet {sh.name}")
                continue
            box = cell_box(c.index, sh.num_w, sh.num_h, w, h, args.inset)
            base = f"{sh.name}-{c.index:02d}_{slug(c.nickname)}"
            n = used_names.get(base, 0)
            used_names[base] = n + 1
            fname = base + (f"_{n + 1}" if n else "") + ".png"
            crop = sheet.crop(box)
            data_png = png_bytes(crop)
            rec = {"id": fname[:-4], "file": f"cards/{fname}", "card_id": c.card_id,
                   "deck_key": sh.deck_key, "index": c.index, "nickname": c.nickname,
                   "description": c.description, "guid": c.guid, "path": c.path,
                   "box": list(box), "size": list(crop.size),
                   "sha256": sha256_bytes(data_png), "source_url": face_url}
            manifest["cards"].append(rec)
            if args.command == "extract":
                written += write_if_changed(out_root / rec["file"], data_png)

        # Backs.
        if sh.back_url:
            bsrc, bhow = resolved.get(sh.back_url, (None, "missing"))
            if not bsrc:
                manifest["missing"].append({"url": sh.back_url,
                                            "used_by": f"back of sheet {sh.name}"})
            else:
                with Image.open(bsrc) as bim:
                    bim.load()
                    back = bim.copy()
                if sh.unique_back:
                    bw, bh = back.size
                    for c in cards_by_face.get(face_url, []):
                        if c.index >= sh.num_w * sh.num_h:
                            continue
                        crop = back.crop(cell_box(c.index, sh.num_w, sh.num_h, bw, bh, args.inset))
                        data_png = png_bytes(crop)
                        rel = f"backs/{sh.name}-{c.index:02d}_{slug(c.nickname)}.png"
                        manifest["backs"].append({"file": rel, "deck_key": sh.deck_key,
                                                  "index": c.index, "size": list(crop.size),
                                                  "sha256": sha256_bytes(data_png),
                                                  "source_url": sh.back_url})
                        if args.command == "extract":
                            written += write_if_changed(out_root / rel, data_png)
                else:
                    rel = f"backs/{sh.name}{bsrc.suffix.lower()}"
                    raw = bsrc.read_bytes()
                    manifest["backs"].append({"file": rel, "deck_key": sh.deck_key,
                                              "size": list(back.size),
                                              "sha256": sha256_bytes(raw),
                                              "source_url": sh.back_url})
                    if args.command == "extract":
                        written += write_if_changed(out_root / rel, raw)

    # --- single images (tiles, tokens, boards, textures) -----------------
    seen_img: set[str] = set()
    for ref in sorted(scan.images, key=lambda r: (r.kind, r.nickname, r.url, r.field)):
        src, how = resolved.get(ref.url, (None, "missing"))
        if not src:
            manifest["missing"].append({"url": ref.url,
                                        "used_by": f"{ref.kind} '{ref.nickname}' ({ref.field})"})
            continue
        if ref.url in seen_img:
            continue
        seen_img.add(ref.url)
        raw = src.read_bytes()
        try:
            with Image.open(src) as im:
                size = list(im.size)
        except Exception:
            size = None
        side = "-back" if ref.field == "ImageSecondaryURL" else ""
        name = f"{ref.kind}_{slug(ref.nickname or 'unnamed')}{side}_{sha256_bytes(raw)[:8]}"
        rel = f"images/{name}{src.suffix.lower()}"
        manifest["images"].append({"file": rel, "kind": ref.kind, "nickname": ref.nickname,
                                   "path": ref.path, "field": ref.field, "size": size,
                                   "sha256": sha256_bytes(raw), "source_url": ref.url,
                                   "lookup": how})
        if args.command == "extract":
            written += write_if_changed(out_root / rel, raw)

    # Cards whose deck key has no sheet in their own or their deck's CustomDeck.
    for c in unresolved:
        card_label = f"'{c.nickname}'" if c.nickname else "(unnamed)"
        manifest["warnings"].append(
            f"card {c.card_id} {card_label} at '{c.path or 'table'}' has no matching sheet (deck key {c.deck_key})")
    manifest["warnings"] = sorted(set(manifest["warnings"]))

    # Deduplicate the missing list for a stable manifest.
    uniq = {(m["url"], m["used_by"]) for m in manifest["missing"]}
    manifest["missing"] = [{"url": u, "used_by": b} for u, b in sorted(uniq)]

    if args.command == "preview":
        out = Path(args.out) if args.out else Path(".")
        files = make_previews(scan, resolved, manifest, out, label)
        for f in files:
            print(f"Preview written: {f}")
        return 0

    report = build_report(manifest, scan, resolved, tts_dir)
    if out_root:
        out_root.mkdir(parents=True, exist_ok=True)
        written += write_if_changed(out_root / "report.md", report.encode("utf-8"))
        if args.command == "extract":
            mbytes = json.dumps(manifest, indent=2, ensure_ascii=False, sort_keys=True).encode("utf-8")
            written += write_if_changed(out_root / "manifest.json", mbytes)

    print(report)
    if out_root:
        print(f"\nOutput: {out_root}  ({written} file(s) written or updated)")
    return 0


def _grid_image(tiles: list[tuple[Image.Image | None, list[str]]], title: str,
                cols: int, thumb_w: int, thumb_h: int) -> Image.Image:
    """Contact sheet: one thumbnail per tile with up to 3 caption lines."""
    from PIL import ImageDraw, ImageFont
    try:
        font = ImageFont.truetype("arial.ttf", 15)
        big = ImageFont.truetype("arial.ttf", 22)
    except Exception:
        font = big = ImageFont.load_default()
    pad, cap = 12, 58
    rows = max(1, (len(tiles) + cols - 1) // cols)
    W = pad + cols * (thumb_w + pad)
    H = 50 + rows * (thumb_h + cap + pad)
    sheet = Image.new("RGB", (W, H), (245, 245, 245))
    d = ImageDraw.Draw(sheet)
    d.text((pad, 12), title, fill=(0, 0, 0), font=big)
    for i, (img, lines) in enumerate(tiles):
        x = pad + (i % cols) * (thumb_w + pad)
        y = 50 + (i // cols) * (thumb_h + cap + pad)
        d.rectangle([x - 1, y - 1, x + thumb_w, y + thumb_h], outline=(180, 180, 180))
        if img is not None:
            t = img.convert("RGBA")
            t.thumbnail((thumb_w, thumb_h), Image.LANCZOS)
            bg = Image.new("RGB", t.size, (60, 60, 60))
            bg.paste(t, mask=t.split()[3])
            sheet.paste(bg, (x + (thumb_w - t.width) // 2, y + (thumb_h - t.height) // 2))
        else:
            d.text((x + 10, y + thumb_h // 2), "MISSING", fill=(200, 0, 0), font=font)
        for j, line in enumerate(lines[:3]):
            d.text((x, y + thumb_h + 4 + j * 17), line[:30], fill=(0, 0, 0), font=font)
    return sheet


def make_previews(scan: ModScan, resolved: dict, manifest: dict,
                  out_dir: Path, label: str) -> list[Path]:
    """Writes <label>_preview_sheets.jpg (first card of every sheet) and
    <label>_preview_images.jpg (every single image: tiles, tokens, ...)."""
    out_dir.mkdir(parents=True, exist_ok=True)
    written = []

    by_face: dict[str, list[CardInfo]] = {}
    for c in scan.cards:
        by_face.setdefault(c.face_url, []).append(c)
    tiles = []
    for face, sh in sorted(scan.sheets.items(), key=lambda kv: (kv[1].deck_key, kv[1].name)):
        cards = sorted(by_face.get(face, []), key=lambda c: c.index)
        src, _ = resolved.get(face, (None, "missing"))
        img = None
        if src and cards:
            with Image.open(src) as im:
                im.load()
                w, h = im.size
                img = im.crop(cell_box(cards[0].index, sh.num_w, sh.num_h, w, h))
        names = [c.nickname for c in cards if c.nickname]
        tiles.append((img, [f"sheet {sh.name}  {sh.num_w}x{sh.num_h}",
                            f"{len(cards)} cards  {cards[0].path[-14:] if cards and cards[0].path else ''}",
                            names[0] if names else ""]))
    if tiles:
        p = out_dir / f"{label}_preview_sheets.jpg"
        _grid_image(tiles, f"{label} - first card of each sheet", 10, 180, 280).save(p, quality=88)
        written.append(p)

    tiles, seen = [], set()
    for ref in sorted(scan.images, key=lambda r: (r.kind, r.nickname, r.url)):
        if ref.field not in ("ImageURL", "DiffuseURL") or ref.url in seen:
            continue
        seen.add(ref.url)
        src, _ = resolved.get(ref.url, (None, "missing"))
        img = None
        if src:
            try:
                with Image.open(src) as im:
                    im.load()
                    img = im.copy()
            except Exception:
                img = None
        tiles.append((img, [f"{ref.kind}", ref.nickname or "(no name)",
                            f"{t_hash(src)}" if src else ""]))
    if tiles:
        p = out_dir / f"{label}_preview_images.jpg"
        _grid_image(tiles, f"{label} - single images (tiles, tokens, boards, ...)", 12, 150, 150).save(p, quality=88)
        written.append(p)
    return written


def t_hash(path: Path) -> str:
    """Same 8-char hash used in the extracted file names under images/."""
    return sha256_bytes(path.read_bytes())[:8]


def build_report(m: dict, scan: ModScan, resolved: dict, tts_dir: Path | None) -> str:
    present = sum(1 for p, _ in resolved.values() if p)
    fuzzy = sum(1 for _, how in resolved.values() if how == "fuzzy")
    downloaded = sum(1 for _, how in resolved.values() if how == "downloaded")
    lines = [
        f"# Extraction report — {m['mod']['label']}",
        "",
        f"- Mod: {m['mod']['save_name'] or '(no name)'}",
        f"- JSON: `{m['mod']['json_path']}`",
        f"- TTS folder: `{tts_dir}`" if tts_dir else "- TTS folder: (not used)",
        f"- Objects walked: {scan.object_count}",
        f"- Image URLs referenced: {len(resolved)} "
        f"(found: {present}, fuzzy match: {fuzzy}, downloaded: {downloaded}, "
        f"missing: {len(resolved) - present})",
        f"- Other URLs (scripts, notes, models): {len(m['other_urls'])}",
        "",
        "## Card sheets",
        "",
        "| Sheet | Grid | Sheet size | Cell size | Cards | Status |",
        "|---|---|---|---|---|---|",
    ]
    for s in m["sheets"]:
        size = "x".join(map(str, s.get("size", []))) or "—"
        cell = "x".join(map(str, s.get("cell_size", []))) or "—"
        status = "ok" if s["cache"] else "**missing**"
        if s.get("lookup") == "fuzzy":
            status = "ok (fuzzy)"
        lines.append(f"| {s['sheet']} | {s['grid'][0]}x{s['grid'][1]} | {size} | "
                     f"{cell} | {s['card_count']} | {status} |")

    if m["cards"]:
        widths = sorted(c["size"][0] for c in m["cards"])
        lines += ["", "## Cards", "",
                  f"- Card faces: {len(m['cards'])}",
                  f"- Card width in pixels: min {widths[0]}, "
                  f"median {widths[len(widths) // 2]}, max {widths[-1]}",
                  f"- Named cards: {sum(1 for c in m['cards'] if c['nickname'])}"]

    kinds: dict[str, int] = {}
    for i in m["images"]:
        kinds[i["kind"]] = kinds.get(i["kind"], 0) + 1
    if kinds:
        lines += ["", "## Single images", ""]
        lines += [f"- {k}: {v}" for k, v in sorted(kinds.items())]

    if m["missing"]:
        lines += ["", "## Missing", "",
                  "Not in the local cache. Open the mod in TTS (and take objects out "
                  "of their bags) to cache them, or re-run with `--download`.", ""]
        lines += [f"- {x['used_by']}: {x['url']}" for x in m["missing"][:200]]
        if len(m["missing"]) > 200:
            lines.append(f"- ... and {len(m['missing']) - 200} more (see manifest.json)")
    else:
        lines += ["", "## Missing", "", "Nothing missing."]
    if m.get("warnings"):
        lines += ["", "## Warnings", ""] + [f"- {w}" for w in m["warnings"]]
    return "\n".join(lines) + "\n"


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Extract images from a Tabletop Simulator mod.")
    ap.add_argument("command", choices=["list", "inventory", "extract", "preview"],
                    help="list: show local mods; inventory: report only; extract: write images; "
                         "preview: write overview images (first card of each sheet, all single images)")
    ap.add_argument("--mod", default="", help="Workshop ID (e.g. 1790581987) or path to the mod JSON")
    ap.add_argument("--tts-dir", help="Tabletop Simulator data folder (the one containing 'Mods')")
    ap.add_argument("--out", help="Output root; files go to <out>/<label>/")
    ap.add_argument("--label", help="Short name for this mod in the output (e.g. mod01)")
    ap.add_argument("--cache-dir", action="append",
                    help="Extra folder with cached images (repeatable)")
    ap.add_argument("--download", action="store_true",
                    help="Download images missing from the cache (needs --out)")
    ap.add_argument("--no-cache", action="store_true", help="Ignore the TTS image cache")
    ap.add_argument("--inset", type=int, default=0,
                    help="Trim N pixels from every side of each card (JPEG edge bleed). Default 0")
    args = ap.parse_args(argv)
    if args.command != "list" and not args.mod:
        ap.error("--mod is required for inventory, extract and preview")
    if args.command == "extract" and not args.out:
        ap.error("--out is required for extract")
    return run(args)


if __name__ == "__main__":
    sys.exit(main())
