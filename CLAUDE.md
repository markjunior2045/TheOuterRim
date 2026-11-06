# CLAUDE.md — Outer Rim digital

Digital version of the *Star Wars: Outer Rim* board game, built in Unity for Windows. Personal, non-commercial. The rules core is plain C# with no `UnityEngine` references and is verified with `dotnet test`.

## Where things are

Repository root: `C:\TheOuterRimProject\TheOuterRim` (this folder).

| Path | Content |
|---|---|
| `docs/DECISIONS.md` | Structural decisions. A guide, not a rulebook. |
| `docs/ROADMAP.md` | Short roadmap: stages and status. |
| `docs/PLAN.md` | Task plan (T0.1, T0.2, ...). Each task has a "Done when". |
| `docs/RULES.md` | Single source of truth for game rules. Rule IDs are `R-x.y`. |
| `docs/MAP_FORMAT.md` | Map graph format (`data/map/`). |
| `docs/DATA_FORMAT.md` | Game data format: packs, cards, abilities (`data/packs/`, `data/schema/registry.json`). |
| `docs/EFFECTS.md` | Inventory of card objects, timings and effect primitives (T2.1). Sets the card data format. |
| `docs/REVIEW.md` | Interpretations awaiting the owner's review. Add an item whenever a reading is a judgement call. |
| `docs/sources/` | Raw text of the rulebooks used to write `RULES.md`. |
| `src/OuterRim.Core/` | Rules core. The only copy of the core source. |
| `tests/OuterRim.Core.Tests/` | xUnit project that compiles the core source files directly. |
| `data/` | Game data (JSON). |
| `tools/` | Scripts, and the V0 terminal runner (`tools/OuterRim.Terminal/`, compiles the core sources like the tests). |
| `unity/` | Unity project (Unity 6.6, URP). Client scripts in `unity/Assets/Scripts/`, scenes built from code. |
| `artifacts/` | .NET build output. Git-ignored. |

Read `docs/DECISIONS.md` and `docs/PLAN.md` before starting a task.

## External folders (outside the repository)

Siblings of this repository, in `C:\TheOuterRimProject\`:

- `RawGameFiles\` — raw game files (source for image extraction).
- `Other\` — reference material, not used by the build.

Rules for both: never version them, never copy them into the repository, never move or rename them, never modify them. Treat them as read-only input. Only processed assets and manifests go in the repository.

## Commands

Run from the repository root.

- Run all tests: `dotnet test`
- Regenerate the golden save after an intended save format change (bump `SaveSerializer.Format` first): set `OUTERRIM_UPDATE_GOLDEN=1` and run `dotnet test`
- Build only: `dotnet build`
- V0, seeded games between random bots with every invariant checked (T3.1). Exit code 0 when every game was sound:
  `dotnet run --project tools/OuterRim.Terminal -c Release -- --games 1000 --seed 1`
  One game with its full command and event log: `dotnet run --project tools/OuterRim.Terminal -- --seed 42`
  (`--help` lists the options: players, round limit, parallel jobs, rejected-command check.)
- Unity (editor 6000.6.8f1, project in `unity/`). The Unity Hub must be running and signed in, or the Editor cannot download packages.
  - Unity tests with a pass/fail exit code (uses the open Editor if one has the project, otherwise batch mode): `python tools/unity_tests.py [--mode editmode|playmode|all] [--batch]`
  - Start an Editor without a window, for the commands below (in the background; stop it with `unity command editor quit`):
    `"C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Unity.exe" -batchmode -projectPath unity -logFile <file>`
  - With an Editor open on the project (run from `unity/`): `unity command script recompile`, then `unity command script recompile_status --result-only` (compiler errors with file and line); `unity command console get`; `unity command scene get_hierarchy --result-only`; `unity command editor invoke_menu --path "Outer Rim/Build Boot Scene"` (scenes are built from code by `Assets/Scripts/Editor/SceneBuilder.cs`); `unity command test run --mode all`. `unity command` with no argument lists every command.
- Install the Python tool dependencies: `pip install -r tools/requirements.txt`
- Prepare reading copies of card images for a transcription batch (written to the git-ignored `artifacts/transcription-work/`; the source folder is never modified):
  `python tools/prepare_transcription_images.py --sorted C:\TheOuterRimProject\RawGameFiles\sorted --deck base/market/gear [--only 0-4]`
- Validate the game data packs: `python tools/validate_data.py`; its tests: `python -m unittest discover -s tools/tests`
- Rebuild a pack's English strings from the transcriptions (never edit `loc/en.json` by hand): `python tools/build_loc.py [--pack base]`
- Validate the map graph and draw review overlays (written to the git-ignored `artifacts/map-review/`):
  `python tools/validate_map.py --review C:\TheOuterRimProject\RawGameFiles\sorted`
- Transcribe a batch of cards into `data/transcription/` (format in `docs/TRANSCRIPTION_FORMAT.md`): Claude Code command `/transcribe <deck> [range]`, for example `/transcribe base/market/gear 0-9`.

## Architecture rules

- `src/OuterRim.Core/` must never reference `UnityEngine`. Its `.asmdef` has `noEngineReferences: true`.
- Unity consumes the core as a local package (`file:../../src/OuterRim.Core` in `unity/Packages/manifest.json`). Do not copy core files into `unity/`.
- The tests compile the same files by glob; do not add a `.csproj` inside `src/`.
- Tests that must also pass under Unity are NUnit 3 and live in `tests/OuterRim.Core.SharedTests/` (a local Unity package, listed in `testables`); `tests/OuterRim.Core.NUnitTests/` runs them under `dotnet test`. The rest of the core tests are xUnit.
- Unity's client code lives in `unity/Assets/Scripts/` (`OuterRim.Client` assembly). It reads the state and sends commands through `GameSession`; it never changes the state itself.
- Build output goes to `artifacts/`. Nothing under `src/` or `unity/` may contain `bin/` or `obj/`.
- Core code must compile under Unity: C# 9 and .NET Standard 2.1 APIs only. Implicit usings are off, so every core file declares its own `using`s.
- The core receives commands, returns events, uses seeded randomness and has serializable state.

## Conventions

- Everything in the repository is English: documents, code, identifiers, file names. Conversations with the owner are in Brazilian Portuguese. Player-facing text goes in localization files.
- Plain Git, no LFS. One branch per task (`task/tX-Y-short-name`), at least one commit per task.
- A task is done when its tests pass, new behaviour cites rule IDs (`R-x.y`) in code comments and test names, and docs are updated if a decision was made.
- Rule ambiguity is marked `[?]` in `RULES.md` and asked about, not guessed.
- Cards and rules have stable IDs. Data files are JSON with a format version.
- Stay inside the task's scope. If the plan needs to change, update `docs/PLAN.md`.
