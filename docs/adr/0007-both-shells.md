# 0007. Every career surface works in both UI shells

- Status: Accepted
- Date: combat Phase 2 (the rule holds in every phase since)
- Source: [design-combat.md, Phase 2 "UI in both shells"](../design-combat.md#ui-in-both-shells) and each phase's build plan

## Decision

- The game has two UI shells: the UI Toolkit shell (the default) and the classic panel (IMGUI, and `-fowlegacyui`).
- Every career surface works in both: rows, cards and their answers, the yard, and the Jobs page actions.
- Rows come from shared functions (`Readouts`, `YardReadouts`), so both shells draw the same text.
- A surface that one shell cannot show says so in QA with a `SKIP` line, and names where the other shell covers it.

## Why

- The classic panel is still a supported way to play.
- One source of text keeps the two shells from drifting.

## Consequences

- A UI feature is not done until both shells show it.
- `-fowcareer` runs in both shells (`career` and `legacy` suites). During a PR, the `legacy` suite runs only when the classic shell is touched; before a merge, it always runs (see `CLAUDE.md`).

## Where

- `Scenes/OpenSystemScene.UI.cs`: `Readouts`, which both shells read.
- `Scenes/OpenSystemScene.Yard.cs`: `YardReadouts`.
- `Scenes/OpenSystemScene.Hud.cs`, `.CareerHud.cs` (toolkit) and `FogOfWarApp.cs` (classic panel).
- `CareerQA.*.cs`: the `SKIP` lines under `-fowlegacyui`.

Read at ef016bf (main).
