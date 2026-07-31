---
name: backlog-board
description: >
  Use this skill when working on the team's backlog tracking workflow. Triggers on:
  'backlog board', 'status write-back', 'progress tracking'.
---

# Tracker Tooling

## Description

As a developer, I want the team's backlog to be visible and editable without hand-editing markdown,
so that status stays current and nobody has to remember a separate bookkeeping step.

This skill covers the backlog board itself: reading the backlog file, rendering epics and stories,
and writing status changes straight back to the file. The markdown file remains the single source of
truth — the board is a thin view and write layer over it.

Shared by US-01 (Backlog Board) and US-02 (Status Write-Back). One skill covers a feature area, not a
single story.

## Tasks

> One deliverable per task; each tagged with the acceptance criteria it satisfies.

1. **Backlog parser** — read the markdown file into epics, stories, tasks, and test cases, recording
   each item's line so writes can be surgical. *(→ AC1)*
2. **Board rendering** — show every epic and story with per-story task and test-case meters. *(→ AC1, AC2)*
3. **Status write-back** — swap exactly one status token on exactly one line, never re-rendering the
   whole file. *(→ AC3)*
4. **Summary regeneration** — refresh the roll-up block after each status change. *(→ AC4)*
5. **Tests** — parser, writer, and summary coverage including a byte-for-byte golden test. *(→ all AC)*

## Acceptance Criteria

- [ ] AC1: Every epic and story in the file appears on the board.
- [ ] AC2: Each story's badge matches the status recorded in the file.
- [ ] AC3: Changing a status alters exactly one line; all other bytes are unchanged.
- [ ] AC4: The roll-up summary is regenerated automatically after a status change.

## Technical Reference

The parser is line-oriented and tolerant: rows are located by content signature, and every editable
item records its exact line number so write-back never needs to re-render the document. Writes swap a
single token in place, preserving surrounding whitespace and the file's line endings — this is what
keeps version-control diffs readable.

## Notes / Gotchas

- Test-case tables appear in more than one column layout, and some stories use a `### Validation`
  heading instead of `### Test Cases`. The parser locates the status column by header name rather
  than by position.
- Never re-render the whole file. It reflows tables, disturbs the summary markers, and turns every
  diff into noise.
