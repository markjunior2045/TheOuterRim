"""validate_map.py — check the map graph in data/map/ and draw review overlays (PLAN T0.5).

Usage (from the repository root):
    python tools/validate_map.py
    python tools/validate_map.py --review C:\\TheOuterRimProject\\RawGameFiles\\sorted --out artifacts/map-review

Checks every layout in every data/map/*.json file:
  - ids are unique; every edge endpoint is a space, patrol stack or port of its piece;
  - every port of a piece is used by exactly one edge;
  - joining pieces left to right (R1-L1, R2-L2) gives a connected graph (all spaces reachable);
  - every patrol stack leads to exactly one space (R-5.3), and each faction has one spawn space;
  - the two Core Worlds spaces are not adjacent (R-5.4);
  - any order of the six tiles also gives a connected graph (random map, R-3.3).
With --review, draws each piece with its spaces, contact spaces and edges on the original art,
so the owner can compare the graph with the board. Exit code 1 on any error.
"""
from __future__ import annotations

import argparse
import itertools
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FORMAT = "outer-rim-map/0.1"
PORT_PAIRS = (("R1", "L1"), ("R2", "L2"))


def load(map_dir: Path):
    files = sorted(map_dir.glob("*.json"))
    pieces, layouts, errors = {}, [], []
    for f in files:
        d = json.loads(f.read_text(encoding="utf-8"))
        if d.get("format") != FORMAT:
            errors.append(f"{f.name}: format {d.get('format')!r}, expected {FORMAT}")
        for p in d["pieces"]:
            if p["id"] in pieces:
                errors.append(f"duplicate piece id {p['id']}")
            pieces[p["id"]] = dict(p, pack=d["pack"])
        layouts += [dict(l, pack=d["pack"]) for l in d.get("layouts", [])]
    return pieces, layouts, errors


def check_pieces(pieces) -> list[str]:
    errors, seen = [], set()
    for p in pieces.values():
        nodes = {s["id"] for s in p["spaces"]} | {s["id"] for s in p["patrol_stacks"]}
        for i in nodes:
            if i in seen:
                errors.append(f"duplicate node id {i}")
            seen.add(i)
        planets = {s["id"] for s in p["spaces"] if s["type"] in ("planet", "core_worlds")}
        for c in p["contact_spaces"]:
            if c["planet"] not in planets:
                errors.append(f"{p['id']}: contact space on unknown planet {c['planet']}")
        port_use = Counter()
        for a, b in p["edges"]:
            for e in (a, b):
                if e in p["ports"]:
                    port_use[e] += 1
                elif e not in nodes:
                    errors.append(f"{p['id']}: edge endpoint {e} is not a node or port")
        for port in p["ports"]:
            if port_use[port] != 1:
                errors.append(f"{p['id']}: port {port} used by {port_use[port]} edges (expected 1)")
    return errors


def assemble(pieces, order):
    """Return (adjacency, node info) for pieces placed left to right."""
    adj = defaultdict(set)
    info = {}
    port_node = {}
    for pid in order:
        p = pieces[pid]
        for s in p["spaces"]:
            info[s["id"]] = dict(s, piece=pid)
        for s in p["patrol_stacks"]:
            info[s["id"]] = dict(s, type="patrol_stack", piece=pid)
        for a, b in p["edges"]:
            if a in p["ports"]:
                port_node[(pid, a)] = b
            elif b in p["ports"]:
                port_node[(pid, b)] = a
            else:
                adj[a].add(b)
                adj[b].add(a)
    for left, right in zip(order, order[1:]):
        for rp, lp in PORT_PAIRS:
            a, b = port_node.get((left, rp)), port_node.get((right, lp))
            if a and b:
                adj[a].add(b)
                adj[b].add(a)
    return adj, info


def reachable(adj, start, skip_stacks=True, info=None):
    seen, todo = {start}, [start]
    while todo:
        n = todo.pop()
        for m in adj[n]:
            if m not in seen and not (skip_stacks and info and info[m]["type"] == "patrol_stack"):
                seen.add(m)
                todo.append(m)
    return seen


def check_layout(pieces, layout) -> tuple[list[str], list[str]]:
    errors, report = [], []
    order = layout["pieces"]
    missing = [p for p in order if p not in pieces]
    if missing:
        return [f"layout {layout['id']}: unknown pieces {missing}"], report
    adj, info = assemble(pieces, order)
    spaces = [i for i, s in info.items() if s["type"] != "patrol_stack"]
    reach = reachable(adj, spaces[0], info=info)
    lost = sorted(set(spaces) - reach)
    if lost:
        errors.append(f"layout {layout['id']}: unreachable spaces {lost}")
    spawn = {}
    for i, s in info.items():
        if s["type"] != "patrol_stack":
            continue
        targets = [m for m in adj[i] if info[m]["type"] != "patrol_stack"]
        if len(targets) != 1:
            errors.append(f"layout {layout['id']}: stack {i} leads to {targets} (expected one space, R-5.3)")
        for f in s["factions"]:
            spawn[f] = targets[0] if targets else None
    for f in ("rebel", "imperial", "hutt", "syndicate"):
        if f not in spawn:
            errors.append(f"layout {layout['id']}: no patrol stack for {f}")
    core = [i for i, s in info.items() if s["type"] == "core_worlds"]
    for a, b in itertools.combinations(core, 2):
        if b in adj[a]:
            errors.append(f"layout {layout['id']}: Core Worlds {a} and {b} are adjacent (R-5.4)")
    types = Counter(info[i]["type"] for i in spaces)
    contacts = Counter()
    for pid in order:
        for c in pieces[pid]["contact_spaces"]:
            contacts[c["class"]["color"]] += 1
    edges = sum(1 for k, v in adj.items() if info[k]["type"] != "patrol_stack"
                for m in v if info[m]["type"] != "patrol_stack") // 2
    report.append(f"layout {layout['id']} ({layout['rule']}): " + ", ".join(f"{n} {t}" for t, n in sorted(types.items()))
                  + f"; {edges} space-to-space edges; contact spaces " + ", ".join(f"{n} {c}" for c, n in sorted(contacts.items())))
    report.append("  patrol spawn: " + ", ".join(f"{f} -> {spawn.get(f)}" for f in ("rebel", "syndicate", "imperial", "hutt")))
    return errors, report


def check_random(pieces, layout) -> list[str]:
    ends = layout["pieces"][0], layout["pieces"][-1]
    tiles = layout["pieces"][1:-1]
    for perm in itertools.permutations(tiles):
        order = [ends[0], *perm, ends[1]]
        adj, info = assemble(pieces, order)
        spaces = [i for i, s in info.items() if s["type"] != "patrol_stack"]
        if set(spaces) - reachable(adj, spaces[0], info=info):
            return [f"layout {layout['id']}: tile order {perm} leaves spaces unreachable (R-3.3)"]
    return []


def draw_review(pieces, sorted_dir: Path, out: Path):
    from PIL import Image, ImageDraw, ImageFont
    out.mkdir(parents=True, exist_ok=True)
    try:
        font = ImageFont.truetype("arial.ttf", 26)
    except OSError:
        font = ImageFont.load_default()
    colors = {"planet": (255, 80, 80), "navpoint": (0, 255, 255), "maelstrom": (255, 0, 255),
              "core_worlds": (255, 160, 0), "patrol_stack": (0, 255, 0)}
    for p in pieces.values():
        src = next(sorted_dir.rglob(p["image"] + ".png"), None)
        if src is None:
            print(f"review: image not found for {p['id']}")
            continue
        im = Image.open(src).convert("RGB")
        im.thumbnail((1800, 1800))
        w, h = im.size
        d = ImageDraw.Draw(im)
        xy = {}
        for s in p["spaces"] + p["patrol_stacks"]:
            xy[s["id"]] = (s["pos"][0] * w, s["pos"][1] * h, s.get("type", "patrol_stack"))
        for k, v in p["ports"].items():
            xy[k] = (v[0] * w, v[1] * h, "port")
        for a, b in p["edges"]:
            d.line([xy[a][:2], xy[b][:2]], fill=(255, 255, 0), width=5)
        for i, (x, y, t) in xy.items():
            r = 14 if t != "port" else 10
            d.ellipse([x - r, y - r, x + r, y + r], fill=colors.get(t, (255, 255, 255)), outline=(0, 0, 0), width=3)
            label = i.replace("nav-", "").replace("stack-", "stack ")
            d.text((x + 16, y + 8), label, fill=(255, 255, 255), font=font, stroke_width=3, stroke_fill=(0, 0, 0))
        for c in p["contact_spaces"]:
            x, y = c["pos"][0] * w, c["pos"][1] * h
            d.text((x - 40, y - 15), f"{c['class']['color']} ({c['planet']})", fill=(255, 255, 255), font=font,
                   stroke_width=3, stroke_fill=(0, 0, 0))
        im.save(out / f"{p['id']}.jpg", quality=88)
    print(f"review images in {out}")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--map-dir", default=str(ROOT / "data" / "map"))
    ap.add_argument("--review", help="sorted image folder, to draw overlays")
    ap.add_argument("--out", default=str(ROOT / "artifacts" / "map-review"))
    a = ap.parse_args(argv)
    pieces, layouts, errors = load(Path(a.map_dir))
    errors += check_pieces(pieces)
    for layout in layouts:
        e, r = check_layout(pieces, layout)
        errors += e
        print("\n".join(r))
        if not e:
            errors += check_random(pieces, layout)
    if a.review:
        draw_review(pieces, Path(a.review), Path(a.out))
    for e in errors:
        print("ERROR:", e)
    print("OK" if not errors else f"{len(errors)} error(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
