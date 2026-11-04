"""Prepare reading copies of card images for a transcription batch (PLAN.md T2.8).

Reads the sorted extraction output and writes downscaled JPG copies into a
work folder (default: <repo>/artifacts/transcription-work, which is git-ignored).
The source folder is never modified.

- Every card face is resized to 1000 px high.
- Encounter cards also get their bottom half rotated 180 degrees and placed
  beside the card, so text printed upside down (secrets, some assets) reads
  normally.
- A legend image with the six market deck backs is written once, so deck
  icons inside card text can be identified.

Usage:
  python tools/prepare_transcription_images.py --sorted C:\\TheOuterRimProject\\RawGameFiles\\sorted --deck base/market/gear
  python tools/prepare_transcription_images.py --sorted ... --deck base/encounters/navpoint --only 0-4
"""
import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw

HEIGHT = 1000
MARKET_DECKS = ["bounty", "cargo", "gear", "job", "luxury", "ship"]


def parse_range(text):
    if not text:
        return None
    lo, _, hi = text.partition("-")
    return range(int(lo), int(hi or lo) + 1)


def card_index(path):
    """Trailing number of names like base-market-gear-07 or base-databank-012a."""
    stem = path.stem
    digits = ""
    for part in reversed(stem.split("-")):
        num = "".join(ch for ch in part if ch.isdigit())
        if num and part[: len(num)] == num:
            digits = num
            break
    return int(digits) if digits else None


def resize(img):
    w, h = img.size
    return img.resize((round(w * HEIGHT / h), HEIGHT), Image.LANCZOS)


def encounter_copy(img):
    card = resize(img)
    w, h = card.size
    bottom = card.crop((0, h // 2, w, h)).rotate(180)
    out = Image.new("RGB", (w * 2 + 10, h), "white")
    out.paste(card, (0, 0))
    out.paste(bottom, (w + 10, 0))
    return out


def write_legend(sorted_root, out_dir):
    target = out_dir / "_legend-market-decks.jpg"
    if target.exists():
        return target
    tiles = []
    for name in MARKET_DECKS:
        back = sorted_root / "base" / "market" / name / "_back.png"
        if back.exists():
            tiles.append((name, Image.open(back).convert("RGB").resize((300, 470))))
    if not tiles:
        return None
    sheet = Image.new("RGB", (300 * len(tiles), 500), "white")
    draw = ImageDraw.Draw(sheet)
    for i, (name, tile) in enumerate(tiles):
        sheet.paste(tile, (i * 300, 0))
        draw.text((i * 300 + 10, 478), f"{name} -> {{DECK_{name.upper()}}}", fill="black")
    sheet.save(target, quality=85)
    return target


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sorted", required=True, type=Path, help="root of the sorted extraction output")
    ap.add_argument("--deck", required=True, help="deck folder relative to --sorted, e.g. base/market/gear")
    ap.add_argument("--out", type=Path, default=Path(__file__).resolve().parent.parent / "artifacts" / "transcription-work")
    ap.add_argument("--only", help="index range to include, e.g. 0-9")
    ap.add_argument("--include-backs", action="store_true", help="also copy *-back images (characters, ship sheets)")
    args = ap.parse_args()

    src = args.sorted / args.deck
    if not src.is_dir():
        sys.exit(f"deck folder not found: {src}")
    out = args.out / args.deck.replace("/", "-").replace("\\", "-")
    out.mkdir(parents=True, exist_ok=True)
    legend = write_legend(args.sorted, args.out)

    wanted = parse_range(args.only)
    is_encounter = "encounters" in Path(args.deck).parts
    written = []
    for path in sorted(src.glob("*.png")):
        if path.name.startswith("_"):
            continue
        if path.stem.endswith("-back") and not args.include_backs:
            continue
        idx = card_index(path)
        if wanted is not None and (idx is None or idx not in wanted):
            continue
        img = Image.open(path).convert("RGB")
        copy = encounter_copy(img) if is_encounter else resize(img)
        target = out / (path.stem + ".jpg")
        copy.save(target, quality=85)
        written.append(target)

    if legend:
        print(f"legend: {legend}")
    for t in written:
        print(t)
    print(f"{len(written)} image(s) in {out}")


if __name__ == "__main__":
    main()
