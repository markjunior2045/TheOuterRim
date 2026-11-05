"""validate_data.py — validate content packs against data/schema/registry.json (PLAN T2.2).

Usage (from the repository root):
    python tools/validate_data.py                # every pack in data/packs/
    python tools/validate_data.py --pack base

Checks, for every pack manifest (data/packs/<id>/pack.json):
  - manifest format, id, version and that every dependency exists;
  - every card file has the data format version and belongs to the pack;
  - card ids are unique across all packs;
  - each card has exactly the fields its kind allows (registry "card_kinds"), with the right types;
  - every ability, effect, condition, timing, amount and selector is known to the registry,
    with all required parameters and no unknown ones ("unknown effect type" is an error);
  - space ids (destinations, "cannot buy on") exist on the pack's map or a dependency's map;
  - every ability text key exists in the pack's English localization;
  - consistency between sets: contact tokens per class match the contact spaces on the pack's map
    (R-12.4), and each faction has exactly one patrol token of each level 1 to 4 (R-11.3).
Exit code 1 on any error. The rules are documented in docs/DATA_FORMAT.md.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA_FORMAT = "outer-rim-data/0.1"
PACK_FORMAT = "outer-rim-pack/0.1"


class Validator:
    def __init__(self, registry: dict):
        self.r = registry
        self.errors: list[str] = []
        self.space_ids: set[str] = set()
        self.loc: dict[str, str] = {}

    def err(self, path: str, msg: str):
        self.errors.append(f"{path}: {msg}")

    # ---- generic typing --------------------------------------------------
    def check(self, value, typ: str, path: str):
        if typ.endswith("?"):
            typ = typ[:-1]
        if typ.startswith("nullable:"):
            if value is None:
                return
            typ = typ[len("nullable:"):]
        if typ.startswith("list:"):
            if not isinstance(value, list):
                return self.err(path, f"expected a list of {typ[5:]}")
            for i, v in enumerate(value):
                self.check(v, typ[5:], f"{path}[{i}]")
            return
        if typ.startswith("enum:"):
            if value not in typ[5:].split("|"):
                self.err(path, f"{value!r} not in {typ[5:]}")
            return
        simple = {"int": int, "bool": bool, "string": str, "object": dict}
        if typ in simple:
            ok = isinstance(value, simple[typ]) and not (typ == "int" and isinstance(value, bool))
            if not ok:
                self.err(path, f"expected {typ}, got {value!r}")
            return
        handler = {
            "effects": self.check_effects, "cond": self.check_cond, "amount": self.check_amount,
            "timing": self.check_timing, "ability": self.check_ability, "outcome": self.check_outcome,
            "space_id": self.check_space_id,
        }.get(typ)
        if handler:
            return handler(value, path)
        if typ == "event":
            if value not in self.r["events"]:
                self.err(path, f"unknown event {value!r}")
            return
        if typ == "custom_handler":
            if value not in self.r["custom_handlers"]:
                self.err(path, f"unknown custom handler {value!r}")
            return
        if typ in self.r["enums"]:
            if value not in self.r["enums"][typ]:
                self.err(path, f"{value!r} is not a valid {typ}")
            return
        if typ in self.r["types"]:
            return self.check_named(value, typ, path)
        if typ in self.r.get("field_types", {}):
            return self.check_params(value, self.r["field_types"][typ], path, typ)
        self.err(path, f"registry uses unknown type {typ!r}")

    def check_params(self, obj, spec: dict, path: str, what: str, skip=()):
        if not isinstance(obj, dict):
            return self.err(path, f"{what}: expected an object")
        for k, t in spec.items():
            if k not in obj:
                if not t.endswith("?") and not t.startswith("nullable:"):
                    self.err(path, f"{what}: missing {k!r}")
                elif t.startswith("nullable:"):
                    self.err(path, f"{what}: missing {k!r} (use null)")
                continue
            self.check(obj[k], t, f"{path}.{k}")
        for k in obj:
            if k not in spec and k not in skip:
                self.err(path, f"{what}: unknown field {k!r}")

    def check_named(self, value, typ: str, path: str):
        spec = self.r["types"][typ]
        if isinstance(value, str):
            enum = spec.get("enum")
            if value in spec.get("strings", []) or (enum and value in self.r["enums"][enum]):
                return
            return self.err(path, f"{value!r} is not a valid {typ}")
        if isinstance(value, dict) and len(value) == 1:
            (k, params), = value.items()
            if k in spec.get("objects", {}):
                return self.check_params(params, spec["objects"][k], f"{path}.{k}", f"{typ}.{k}")
        self.err(path, f"{value!r} is not a valid {typ}")

    # ---- card language ---------------------------------------------------
    def check_effects(self, value, path: str):
        if not isinstance(value, list):
            return self.err(path, "expected a list of effects")
        for i, e in enumerate(value):
            p = f"{path}[{i}]"
            if not isinstance(e, dict) or "op" not in e:
                self.err(p, "effect needs an 'op'")
                continue
            op = e["op"]
            if op not in self.r["effects"]:
                self.err(p, f"unknown effect type {op!r}")
                continue
            self.check_params(e, self.r["effects"][op], p, op, skip=("op",))

    def check_cond(self, value, path: str):
        if not isinstance(value, dict) or "cond" not in value:
            return self.err(path, "condition needs a 'cond'")
        c = value["cond"]
        if c not in self.r["conditions"]:
            return self.err(path, f"unknown condition {c!r}")
        self.check_params(value, self.r["conditions"][c], path, c, skip=("cond",))

    def check_amount(self, value, path: str):
        if isinstance(value, int) and not isinstance(value, bool):
            return
        objs = self.r["amount"]["objects"]
        if isinstance(value, dict) and len(value) == 1:
            (k, params), = value.items()
            if k in objs:
                return self.check_params(params, objs[k], f"{path}.{k}", f"amount.{k}")
        self.err(path, f"invalid amount {value!r}")

    def check_timing(self, value, path: str):
        if not isinstance(value, dict) or value.get("type") not in self.r["timings"]:
            return self.err(path, f"unknown timing {value!r}")
        self.check_params(value, self.r["timings"][value["type"]], path, f"timing.{value['type']}", skip=("type",))

    def check_outcome(self, value, path: str):
        self.check_params(value, self.r["outcome"], path, "outcome")

    def check_ability(self, value, path: str):
        self.check_params(value, self.r["ability_fields"], path, "ability")

    def check_space_id(self, value, path: str):
        if value not in self.space_ids:
            self.err(path, f"unknown space id {value!r}")

    def check_card(self, card: dict, path: str, card_id: str):
        kind = card.get("kind")
        if kind not in self.r["card_kinds"]:
            return self.err(path, f"unknown or unsupported card kind {kind!r}")
        spec = dict(self.r["card_fields"], **self.r["card_kinds"][kind])
        self.check_params(card, spec, path, f"card {kind}")
        for apath, ab in iter_abilities(card, path):
            key = ab.get("text") if isinstance(ab, dict) else None
            if key and f"{card_id}.{key}" not in self.loc:
                self.err(apath, f"text key {card_id}.{key} not in the English localization")


def iter_abilities(card: dict, path: str):
    """Abilities on the card and on each side of two-sided cards (characters, ship sheets)."""
    for i, ab in enumerate(card.get("abilities") or []):
        yield f"{path}.abilities[{i}]", ab
    for s, side in enumerate(card.get("sides") or []):
        for i, ab in enumerate(side.get("abilities") or [] if isinstance(side, dict) else []):
            yield f"{path}.sides[{s}].abilities[{i}]", ab
    # Encounter cards: secrets are abilities held by the player (R-15.82).
    for s, sec in enumerate(card.get("sections") or []):
        for v, var in enumerate(sec.get("variants") or [] if isinstance(sec, dict) else []):
            for i, ab in enumerate(var.get("abilities") or [] if isinstance(var, dict) else []):
                yield f"{path}.sections[{s}].variants[{v}].abilities[{i}]", ab


def load_json(path: Path, v: Validator):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        v.err(str(path.relative_to(ROOT)), "file not found")
    except json.JSONDecodeError as e:
        v.err(str(path.relative_to(ROOT)), f"invalid JSON: {e}")
    return None


def map_spaces(map_file: Path) -> set[str]:
    d = json.loads(map_file.read_text(encoding="utf-8"))
    return {s["id"] for p in d["pieces"] for s in p["spaces"]}


def check_consistency(v: Validator, pid: str, cards: list[dict], map_file: str | None):
    contacts = [c for c in cards if c.get("kind") == "contact"]
    if contacts and map_file:
        d = json.loads((ROOT / map_file).read_text(encoding="utf-8"))
        spaces: dict[str, int] = {}
        for p in d["pieces"]:
            for cs in p["contact_spaces"]:
                spaces[cs["class"]["color"]] = spaces.get(cs["class"]["color"], 0) + 1
        tokens: dict[str, int] = {}
        for c in contacts:
            tokens[c["class"]] = tokens.get(c["class"], 0) + 1
        for cls in sorted(set(spaces) | set(tokens)):
            if spaces.get(cls, 0) != tokens.get(cls, 0):
                v.err(f"pack {pid}", f"{tokens.get(cls, 0)} {cls} contact tokens but {spaces.get(cls, 0)} {cls} "
                                     f"contact spaces on the map (R-12.4)")
    patrols = [c for c in cards if c.get("kind") == "patrol"]
    if patrols:
        for f in v.r["enums"]["faction"]:
            levels = sorted(c.get("level") for c in patrols if c.get("faction") == f)
            if levels != [1, 2, 3, 4]:
                v.err(f"pack {pid}", f"{f} patrol levels {levels}, expected [1, 2, 3, 4] (R-11.3)")


def validate(packs_dir: Path, registry_file: Path, only: str | None = None) -> list[str]:
    v = Validator(json.loads(registry_file.read_text(encoding="utf-8")))
    manifests = {}
    for mf in sorted(packs_dir.glob("*/pack.json")):
        m = load_json(mf, v)
        if m:
            manifests[m.get("id")] = (mf, m)
    seen_ids: dict[str, str] = {}
    for pid, (mf, m) in manifests.items():
        rel = str(mf.relative_to(ROOT))
        if m.get("format") != PACK_FORMAT:
            v.err(rel, f"format {m.get('format')!r}, expected {PACK_FORMAT}")
        for k in ("id", "version", "depends", "cards", "localization"):
            if k not in m:
                v.err(rel, f"missing {k!r}")
        for dep in m.get("depends", []):
            if dep not in manifests:
                v.err(rel, f"depends on unknown pack {dep!r}")
        if only and pid != only:
            continue
        v.space_ids = set()
        for p in [pid] + list(m.get("depends", [])):
            if p in manifests and manifests[p][1].get("map"):
                v.space_ids |= map_spaces(ROOT / manifests[p][1]["map"])
        loc_file = m.get("localization", {}).get("en")
        loc = load_json(ROOT / loc_file, v) if loc_file else None
        v.loc = (loc or {}).get("strings", {})
        pack_cards: list[dict] = []
        for cf in m.get("cards", []):
            d = load_json(ROOT / cf, v)
            if not d:
                continue
            if d.get("format") != DATA_FORMAT:
                v.err(cf, f"format {d.get('format')!r}, expected {DATA_FORMAT}")
            if d.get("pack") != pid:
                v.err(cf, f"pack {d.get('pack')!r} in a file listed by pack {pid!r}")
            for i, card in enumerate(d.get("cards", [])):
                cid = card.get("id", f"#{i}")
                path = f"{cf}:{cid}"
                if cid in seen_ids:
                    v.err(path, f"duplicate card id (also in {seen_ids[cid]})")
                seen_ids[cid] = cf
                v.check_card(card, path, cid)
                pack_cards.append(card)
        check_consistency(v, pid, pack_cards, m.get("map"))
    return v.errors


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--packs", default=str(ROOT / "data" / "packs"))
    ap.add_argument("--registry", default=str(ROOT / "data" / "schema" / "registry.json"))
    ap.add_argument("--pack")
    a = ap.parse_args(argv)
    errors = validate(Path(a.packs), Path(a.registry), a.pack)
    for e in errors:
        print("ERROR:", e)
    print("OK" if not errors else f"{len(errors)} error(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
