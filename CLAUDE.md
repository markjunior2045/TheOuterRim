# CLAUDE.md — Outer Rim digital

Digital version of the *Star Wars: Outer Rim* board game, built in Unity for Windows. Personal, non-commercial. The rules core is plain C# with no `UnityEngine` references and is verified with `dotnet test`.

## Where things are

Repository root: `C:\TheOuterRimProject\TheOuterRim` (this folder).

| Path | Content |
|---|---|
| `docs/DECISIONS.md` | Structural decisions. A guide, not a rulebook. |
| `docs/PLAN.md` | Task plan (T0.1, T0.2, ...). Each task has a "Done when". |
| `docs/RULES.md` | Single source of truth for game rules. Rule IDs are `R-x.y`. |
| `docs/sources/` | Raw text of the rulebooks used to write `RULES.md`. |
| `src/OuterRim.Core/` | Rules core. The only copy of the core source. |
| `tests/OuterRim.Core.Tests/` | xUnit project that compiles the core source files directly. |
| `data/` | Game data (JSON). |
| `tools/` | Scripts. |
| `unity/` | Unity project (created in T0.4). |
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
- Build only: `dotnet build`
- Unity and Unity CLI commands: to be added in T0.4.

## Architecture rules

- `src/OuterRim.Core/` must never reference `UnityEngine`. Its `.asmdef` has `noEngineReferences: true`.
- Unity consumes the core as a local package (`file:../../src/OuterRim.Core` in `unity/Packages/manifest.json`). Do not copy core files into `unity/`.
- The tests compile the same files by glob; do not add a `.csproj` inside `src/`.
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
