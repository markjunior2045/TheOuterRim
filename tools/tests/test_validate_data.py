"""Tests for tools/validate_data.py (PLAN T2.2: the validator rejects malformed cards and unknown effect types).

Run from the repository root:  python -m unittest discover -s tools/tests -v
"""
import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
import validate_data as vd  # noqa: E402

REGISTRY = ROOT / "data" / "schema" / "registry.json"

GOOD_CARD = {
    "id": "test-gear-00", "kind": "gear", "deck": "gear", "cost": 3000, "traits": ["weapon"],
    "patrol_icon": {"faction": "rebel", "distance": 3},
    "abilities": [
        {"text": "text.0", "timing": {"type": "passive"},
         "effects": [{"op": "stat.modify", "stat": "ground_combat", "by": 1}]},
        {"text": "text.1", "timing": {"type": "step", "step": "action"}, "optional": True,
         "condition": {"cond": "rep", "faction": "hutt", "in": ["pos", "neu"]},
         "cost": [{"op": "credits.spend", "amount": 1000}],
         "effects": [{"op": "test", "skill": ["stealth"],
                      "pass": [{"op": "credits.gain", "amount": {"up_to": {"max": 3000}}}],
                      "fail": [{"op": "damage.suffer", "target": "character", "amount": 2}]}]},
    ],
}
GOOD_CARGO = {
    "id": "test-cargo-00", "kind": "cargo", "deck": "cargo", "cost": 1000, "traits": [],
    "patrol_icon": None, "cannot_buy_on": "kessel", "destination": "kessel", "delivery_check": None,
    "reward": [{"op": "credits.gain", "amount": 5000}], "abilities": [],
}
LOC = {"test-gear-00.text.0": "Gain +1.", "test-gear-00.text.1": "Action: ...",
       "test-character-00.front.upper.0": "Gain +2 {HYPERDRIVE}."}
GOOD_CHARACTER = {
    "id": "test-character-00", "kind": "character", "ground_combat": 3, "health": 4, "skills": ["piloting"],
    "setup": {"databank": 91, "reputation": [{"faction": "hutt", "state": "neg"}]},
    "sides": [
        {"image": "test-character-00", "side": "front", "abilities": [
            {"text": "front.upper.0", "timing": {"type": "passive"},
             "effects": [{"op": "stat.modify", "stat": "hyperdrive", "by": 2}]}]},
        {"image": "test-character-00-back", "side": "back", "abilities": [
            {"timing": {"type": "goal", "kind": "personal",
                        "progress": {"count": {"event": "combat_won", "times": 3}}},
             "effects": [{"op": "fame.gain", "amount": 1}, {"op": "sheet.flip", "sheet": "character"}]}]},
    ],
}


class ValidateDataTests(unittest.TestCase):
    def run_with(self, cards, loc=LOC, depends=()):
        with tempfile.TemporaryDirectory(dir=ROOT / "artifacts") as tmp:
            tmp = Path(tmp)
            rel = tmp.relative_to(ROOT).as_posix()
            pack = tmp / "packs" / "test"
            (pack / "cards").mkdir(parents=True)
            (pack / "cards" / "c.json").write_text(json.dumps(
                {"format": vd.DATA_FORMAT, "pack": "test", "cards": cards}), encoding="utf-8")
            (pack / "en.json").write_text(json.dumps({"strings": loc}), encoding="utf-8")
            (pack / "pack.json").write_text(json.dumps({
                "format": vd.PACK_FORMAT, "id": "test", "version": "0.0.1", "depends": list(depends),
                "map": "data/map/base-map.json",
                "cards": [f"{rel}/packs/test/cards/c.json"],
                "localization": {"en": f"{rel}/packs/test/en.json"}}), encoding="utf-8")
            return vd.validate(tmp / "packs", REGISTRY)

    def assert_error(self, cards, fragment, **kw):
        errors = self.run_with(cards, **kw)
        self.assertTrue(any(fragment in e for e in errors), f"expected {fragment!r} in {errors}")

    def mutate(self, fn, base=GOOD_CARD):
        card = copy.deepcopy(base)
        fn(card)
        return [card]

    def test_good_cards_pass(self):
        self.assertEqual(self.run_with([GOOD_CARD, GOOD_CARGO]), [])

    def test_real_base_pack_passes(self):
        self.assertEqual(vd.validate(ROOT / "data" / "packs", REGISTRY, "base"), [])

    def test_unknown_effect_type(self):
        self.assert_error(self.mutate(lambda c: c["abilities"][0]["effects"].append({"op": "credits.steal", "amount": 1})),
                          "unknown effect type 'credits.steal'")

    def test_unknown_effect_nested_in_branch(self):
        self.assert_error(self.mutate(lambda c: c["abilities"][1]["effects"][0]["pass"].append({"op": "teleport"})),
                          "unknown effect type 'teleport'")

    def test_missing_required_parameter(self):
        self.assert_error(self.mutate(lambda c: c["abilities"][0]["effects"][0].pop("by")), "missing 'by'")

    def test_unknown_parameter(self):
        self.assert_error(self.mutate(lambda c: c["abilities"][0]["effects"][0].update(bonus=2)), "unknown field 'bonus'")

    def test_bad_enum_value(self):
        self.assert_error(self.mutate(lambda c: c["abilities"][0]["effects"][0].update(stat="speed")),
                          "'speed' is not a valid stat")

    def test_unknown_condition(self):
        self.assert_error(self.mutate(lambda c: c["abilities"][1].update(condition={"cond": "is_jedi"})),
                          "unknown condition 'is_jedi'")

    def test_unknown_timing(self):
        self.assert_error(self.mutate(lambda c: c["abilities"][0].update(timing={"type": "sometimes"})), "unknown timing")

    def test_bad_amount(self):
        self.assert_error(self.mutate(lambda c: c["abilities"][1]["cost"][0].update(amount="lots")), "invalid amount")

    def test_wrong_field_type(self):
        self.assert_error(self.mutate(lambda c: c.update(cost="3000")), "expected int")

    def test_missing_card_field(self):
        self.assert_error(self.mutate(lambda c: c.pop("deck")), "missing 'deck'")

    def test_unknown_card_field(self):
        self.assert_error(self.mutate(lambda c: c.update(rarity="rare")), "unknown field 'rarity'")

    def test_unknown_card_kind(self):
        self.assert_error(self.mutate(lambda c: c.update(kind="starship")), "card kind 'starship'")

    def test_unknown_space(self):
        self.assert_error(self.mutate(lambda c: c.update(destination="coruscant"), GOOD_CARGO), "unknown space id 'coruscant'")

    def test_missing_localization_key(self):
        self.assert_error([GOOD_CARD], "not in the English localization", loc={"test-gear-00.text.0": "x"})

    def test_duplicate_card_id(self):
        self.assert_error([GOOD_CARD, GOOD_CARD], "duplicate card id")

    def test_unknown_custom_handler(self):
        self.assert_error(self.mutate(lambda c: c["abilities"][0]["effects"].append({"op": "custom", "handler": "magic"})),
                          "unknown custom handler 'magic'")

    def test_two_sided_card_passes(self):
        self.assertEqual(self.run_with([GOOD_CHARACTER]), [])

    def test_unknown_effect_on_card_side(self):
        self.assert_error(self.mutate(lambda c: c["sides"][1]["abilities"][0]["effects"].append({"op": "jedi.mind_trick"}),
                                      GOOD_CHARACTER), "unknown effect type 'jedi.mind_trick'")

    def test_bad_goal_progress(self):
        self.assert_error(self.mutate(lambda c: c["sides"][1]["abilities"][0]["timing"].update(progress={"someday": {}}),
                                      GOOD_CHARACTER), "is not a valid goal_progress")

    def test_missing_text_key_on_card_side(self):
        self.assert_error([GOOD_CHARACTER], "test-character-00.front.upper.0 not in the English localization",
                          loc={"test-gear-00.text.0": "x"})

    def test_contact_classes_must_match_map(self):
        token = {"id": "test-contact-00", "kind": "contact", "class": "orange", "faction": None, "droid": False, "databank": 2}
        self.assert_error([token], "1 orange contact tokens but 0 orange contact spaces")

    def test_patrol_levels_per_faction(self):
        patrol = {"id": "test-patrol-00", "kind": "patrol", "faction": "hutt", "level": 1, "combat": 3, "reward": []}
        self.assert_error([patrol], "hutt patrol levels [1], expected [1, 2, 3, 4]")

    def test_unknown_dependency(self):
        self.assert_error([GOOD_CARD], "depends on unknown pack 'mandalorian'", depends=["mandalorian"])


if __name__ == "__main__":
    unittest.main()
