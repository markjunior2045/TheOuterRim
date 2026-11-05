# MAP_FORMAT.md — outer-rim-map/0.1

Map geometry read from the board pieces (`PLAN.md` T0.5). One file per pack in `data/map/`: `base-map.json` (six tiles, two endcaps, standard layout) and `expansion-map.json` (Core Worlds endcaps and their layout). Checked by `tools/validate_map.py`.

```json
{
  "format": "outer-rim-map/0.1",
  "pack": "base",
  "notes": ["..."],
  "pieces": [ ... ],
  "layouts": [ { "id": "standard", "rule": "R-3.2", "pieces": ["endcap-rebel-syndicate", "...", "endcap-empire-hutt"] } ]
}
```

## Pieces

| Field | Content |
|---|---|
| `id` | stable piece id, e.g. `tile-kessel` |
| `image` | art file name without extension (`sorted/{pack}/map/`) |
| `spaces` | `[{ id, type, name, faction, pos }]`; `type` is `planet`, `navpoint`, `maelstrom` or `core_worlds` `[UB]`; `faction` is the planet's icon or `null` (R-5.5) |
| `contact_spaces` | `[{ planet, class: { color, pips }, pos }]`, from the pips under each portrait frame (R-12.3) |
| `patrol_stacks` | `[{ id, factions, pos }]`: where facedown patrol tokens wait (R-3.7). Not a space. |
| `ports` | `{ L1, L2, R1, R2 }`: where paths leave the piece (left upper, left lower, right upper, right lower) |
| `edges` | `[[a, b]]`, undirected paths between spaces, stacks and ports of this piece (R-5.2) |

`pos` is the approximate centre, normalised to the piece image (`x / width`, `y / height`), for drawing only. Rules never read it.

Space ids are stable and global: planets by name (`ord-mantell`), the Maelstrom as `maelstrom`, navpoints as `nav-{tile}-{n}` numbered left to right on their tile.

## Layouts

A layout lists pieces left to right. Neighbouring pieces join `R1`–`L1` and `R2`–`L2`; the spaces at both ends of a joined port pair are adjacent. Any order of the six tiles is valid (random map, R-3.3), and the validator checks all of them.

The space a patrol stack connects to is where that faction's patrols spawn (R-5.3, R-11.10) and where its level-1 patrol starts (R-3.5). On the Core Worlds endcaps two factions share one stack and one spawn space (R-3.6).

## Not in the graph

- Crossing between the two Core Worlds spaces is a card effect (R-5.24 to R-5.28), not an edge; they are never adjacent (R-5.4).
- Patrol direction ("clockwise", R-11.18) needs the order of spaces along the map. It is derived from the layout and `pos` when patrol movement is built (`PLAN.md` T1.8).
