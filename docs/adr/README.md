# Architecture decision records

An ADR records one rule that holds across phases, sessions and PRs. Read the index before you design or review a change. If a change breaks an ADR, write a new ADR that supersedes it first.

## When to write one

- The rule applies to more than one phase or more than one area of the code.
- People or sessions have had to explain it more than once.
- Breaking it would make two parts of the game disagree (for example, the screen and a card).

Do not write an ADR for a number or for a choice inside one phase. Those stay in the design docs, in each phase's "Decisions" list. The design docs number their decisions D1, D2 … again in each phase, so "D10" alone is not clear. ADR numbers are project-wide and never reused.

## Rules for an ADR

- One screen. Status, date, decision, why, consequences, where.
- Numbers stay in the design docs. An ADR links to the section; it does not copy the value.
- "Where" names files and symbols, with the commit they were read at.
- Status is Proposed, Accepted, or Superseded by NNNN. Do not delete a superseded ADR.
- File name: `NNNN-short-name.md`. Add a line to the index below and to the index in `CLAUDE.md`.

## Template

```markdown
# NNNN. Title as a rule

- Status: Accepted
- Date: YYYY-MM-DD
- Source: link to the design-doc section or decision

## Decision
## Why
## Consequences
## Where
Read at <commit>.
```

## Index

| # | Rule | Status |
|---|---|---|
| [0001](0001-open-world-game.md) | The game is the open world; the Observatory scenes move to their own repo | Accepted |
| [0002](0002-career-decides.md) | The career decides every outcome; the scene only draws it | Accepted |
| [0003](0003-closed-form-combat.md) | Combat resolves in closed form at any warp: one kill draw a missile, at impact | Accepted |
| [0004](0004-combat-rng.md) | Combat draws from its own random stream | Accepted |
| [0005](0005-one-bearing-rule.md) | One bearing rule for the rows, the cards and the tracers | Accepted |
| [0006](0006-chase-warp-cap.md) | In a chase, the warp follows the time to the next event | Accepted |
| [0007](0007-both-shells.md) | Every career surface works in both UI shells | Accepted |
| [0008](0008-chase-not-saved.md) | A chase is not saved | Accepted |
| [0009](0009-thrust-not-g.md) | Thrust, not g: every part is mass | Accepted |
