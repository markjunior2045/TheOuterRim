"""build_loc.py — build the English localization file of a pack from its transcriptions (PLAN T2.2).

Usage (from the repository root):
    python tools/build_loc.py                 # base pack
    python tools/build_loc.py --pack expansion

Card data files carry no player-facing text (DECISIONS §3); abilities point at keys such as
"text.0" and the loader resolves them as "<card id>.text.0". This script flattens every printed
string of every transcribed card into such keys:

    base-market-gear-00.name    -> "IQA-11 Blaster Rifle"
    base-market-gear-00.text.0  -> "Gain +1 {GROUND_COMBAT}."

Icons stay as {PLACEHOLDERS}; the interface replaces them with sprites. The file is regenerated,
never edited by hand; Portuguese and Spanish files are translations of it.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SKIP = {"image", "image_back", "verify", "notes", "errata", "format", "source", "orientation", "role",
        "certain", "secret", "number", "variant", "kind", "card_type", "deck_icon", "faction", "color",
        "type", "databank_card", "droid", "pips", "value", "count", "cost", "distance", "level", "marker"}
FORMAT = "outer-rim-loc/0.1"


def card_id(card: dict) -> str | None:
    if "image" in card:
        return card["image"]
    sides = card.get("sides") or []
    return sides[0].get("image") if sides else None


def flatten(value, prefix: str, out: dict):
    if isinstance(value, str):
        if value.strip():
            out[prefix] = value
    elif isinstance(value, list):
        for i, v in enumerate(value):
            flatten(v, f"{prefix}.{i}", out)
    elif isinstance(value, dict):
        for k, v in value.items():
            if k not in SKIP and v not in (None, "", [], {}):
                flatten(v, f"{prefix}.{k}", out)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--pack", default="base")
    a = ap.parse_args(argv)
    src = ROOT / "data" / "transcription"
    out: dict[str, str] = {}
    for f in sorted(src.rglob(f"{a.pack}-*.json")):
        data = json.loads(f.read_text(encoding="utf-8"))
        for card in data.get("cards", []):
            cid = card_id(card)
            if not cid:
                print(f"skip: card without image in {f.name}", file=sys.stderr)
                continue
            body = {k: v for k, v in card.items() if k not in ("image",)}
            flatten(body, cid, out)
    dest = ROOT / "data" / "packs" / a.pack / "loc" / "en.json"
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps({"format": FORMAT, "pack": a.pack, "language": "en", "strings": out},
                               ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"{len(out)} strings -> {dest.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
