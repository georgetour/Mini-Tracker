# Mini Tracker — Project Instructions

## What this is

A tiny local web board over a `BACKLOG.md` file — a minimal Jira/Azure-DevOps-style view of epics,
user stories, tasks, and test cases. Click a status and it's **written through instantly** to the
markdown file — no sync button, no database, no PowerShell. The markdown stays the single source of
truth; this tool is a thin UI + write layer over it.

Mini Tracker is standalone and generic: point it at any project's `BACKLOG.md` (existing, or a
brand-new one it creates from `templates/BACKLOG.template.md`), from any folder. Nothing here is tied
to a specific product or repo.

## Where the design source material lives

The UI's visual design (colors, typography, the status-pair color system) and the original
`BACKLOG.md`/`SKILL.md` conventions were designed against a real product backlog. That source
material — mockups, design tokens, the original spec — is kept locally in a gitignored `reference/`
folder (see `.gitignore`), never committed, never part of the public repo. If you have that folder
locally and a planning decision needs revisiting, `reference/README.md` explains what's there and
where it came from.

## Locked architecture decisions — do not relitigate without the user

These were settled over a long design discussion. If a "better" idea comes up (a database, a sync
button, PowerShell, React/Node), it was very likely already considered and rejected.

- **`BACKLOG.md` IS the database.** No relational/NoSQL DB. Git is its history/backup. A database
  would just be a second, disagreeing copy of data the file already owns.
- **Write-through, NO sync button.** Every UI click is an instant surgical write to `BACKLOG.md` — no
  pending buffer, no localStorage, no "did I save?" ambiguity.
- **No PowerShell, anywhere.** `SummaryWriter` (C#) regenerates the STATUS-SUMMARY roll-up in-process;
  exposed as a CLI (`dotnet run --project src/MiniTracker.Api -- sync-status`) so CI can run it on a
  Linux runner with zero shell dependency.
- **Surgical writes only.** A status change swaps exactly one status token on exactly one line — never
  re-render the whole file (would reflow tables, disturb the `<!-- STATUS-SUMMARY -->` markers, and
  turn every git diff to noise).
- **Plain HTML/CSS/JS frontend with Alpine.js, no build step** — no React, no Node, no bundler. One
  self-contained .NET project stays tiny. Alpine is **vendored** at
  `wwwroot/vendor/alpine-csp.min.js` (60 KB), not loaded from a CDN: the app must work offline and
  under a CSP that names no external script host.
- **Alpine's CSP build specifically**, paired with a real `Content-Security-Policy` header from
  `Program.cs` (no `unsafe-eval`, no `unsafe-inline` for scripts). It evaluates bindings without
  `new Function()`, so injected text can never become code. The trade is that markup expressions are
  property reads and method calls only — no template literals, arrow functions or inline assignment.
  Anything needing computation is precomputed in `decorate()`. Don't "simplify" by swapping in the
  standard Alpine build; that silently removes the guarantee the header is asserting.
- **Markup lives in `index.html`, never in JavaScript.** `app.js` holds state and logic and returns
  values; it does not assemble HTML. The single exception is `renderMarkdown()`, which escapes its
  input before any transform runs and rejects link schemes other than http/https/mailto/anchor.
- **Status colours are CSS classes (`.st-done`), never bound inline styles.** An inline style beats
  every class rule, which is exactly how hover states here broke twice.
- **`SKILL.md` is read-only reference**, linked from the story-detail page — this tool never parses or
  writes it, only `BACKLOG.md`.
- **Dynamic local config, not a hardcoded default path.** `tracker.config.json` (gitignored, lives
  next to the app) holds the logo path, backlog path, and skills folder path — all editable from the
  UI's **⚙ Configure** button and the logo slot, with changes taking effect immediately, no restart.
  A deploy-time override (`BacklogPath` via env/appsettings/CLI arg) always wins over the UI config and
  is never persisted into it.
- **First run shows a live demo, never an error.** With nothing configured and no `BACKLOG.md` found
  nearby, the app materializes a real, writable copy of `templates/BACKLOG.template.md`, plus its
  `templates/skills/` example files alongside it, and points itself at both (`BacklogPath` and
  `SkillsPath`) — like a CMS's demo content. Pointing Configure at a real project replaces it for good.
- **`templates/BACKLOG.template.md` and `templates/SKILL.template.md`** are the canonical starting
  points for a brand-new project — same structural conventions `BacklogParser` expects, ready to copy
  or auto-created by Configure when a given backlog path doesn't exist yet.
- **Header UI**: one global **+ Add** menu (New epic / New user story) sits left of **Configure** /
  Sync / Stage — a single add menu, never one button per thing you can create.
  "Configure" = set backlog path + skills folder path (creates the backlog file from the template if
  missing). "Sync" = reload-from-disk (not save — writes are already instant); "Stage" = optional
  `git add BACKLOG.md` only, **never** auto-commit.
- **Add / Configure are real pages at real URLs** (`/add-epic`, `/add-story`, `/edit-epic`,
  `/configure`) with real `<form>` markup in `index.html` — not markup assembled in JavaScript. The
  browser does constraint validation, and every endpoint validates again server-side: the JS check
  is only there to save a round trip.
- **One navigation per screen size.** Up to 900px the sidebar collapses to a rail; 769–900px it is a
  drawer behind the hamburger; at 768px and below the drawer and hamburger are gone entirely and a
  bottom bar carries the destinations with the add action raised in the centre. Designed to 320px.
- **No multi-user, no cloud sync (yet).** Single local developer, single writer. If that ever changes,
  revisit concurrency and a real state store then — don't build it preemptively.

## Current build status

**Done** (tests green, verified end-to-end):
- `Backlog/BacklogParser.cs` — parses epics → stories → tasks/test-cases, handles both test-case table
  shapes and the `### Validation` heading variant, captures the roadmap's declared versions, records
  line locators for surgical writes
- `Backlog/SummaryWriter.cs` — C# roll-up regenerator; golden test proves byte-for-byte fidelity
- `Backlog/BacklogWriter.cs` — surgical single-line/cell write-through (story status, task done/not,
  test-case status)
- `Services/TrackerConfigService.cs` — the local config (logo/backlog/skills paths), backlog-path
  resolution (deploy override → configured path → walk-up fallback → live demo), and create-from-
  template when Configure is pointed at a path that doesn't exist yet. First-run demo materialization
  also copies `templates/skills/` alongside the demo backlog and points `SkillsPath` at it, without
  clobbering an already-configured `SkillsPath`
- `Services/BacklogLocator.cs` — walk-up-from-cwd fallback so running from inside a project that
  already has a `BACKLOG.md` at its root needs zero config
- `Services/SkillFileResolver.cs` — resolves a story's recorded skill path safely under the configured
  skills folder (rejects anything that would escape it)
- `Services/BacklogService.cs` — read/write gateway; re-resolves its path fresh on every call (a
  Configure change takes effect with no restart), serializes writes behind a lock
- `Program.cs` — Minimal API: `GET /api/board`, `POST /api/story/{code}/status`,
  `POST /api/story/{code}/task/{taskId}`, `POST /api/story/{code}/testcase/{tcId}`,
  `POST /api/git/stage`, `GET /api/config`, `POST /api/config/backlog`, `POST /api/config/skills`,
  `POST /api/config/logo`, `GET /api/skill`; plus the `sync-status` CLI mode
- `wwwroot/index.html` — the board UI, story-detail page, click-to-change status pickers, task
  toggles, logo upload, the Configure modal, and a functional skill-file viewer — all instant
  write-through
- `templates/BACKLOG.template.md` / `templates/SKILL.template.md` — starter files, also used as the
  first-run demo content

- `Backlog/BacklogGenerator.cs` — text-in/text-out add, rename and delete for epics and stories,
  plus recording a skill path on a story that had none. Insertions are append-only at the insertion
  point so a diff shows purely added lines; deletions take their surrounding blank lines with them
  so repeated edits never grow a gap
- `wwwroot/index.html` — every view as declarative markup: board, epic, story detail, releases, the
  four form pages, the status picker and the delete `<dialog>`. Board and release views share one
  template because they are the same shape (a header plus story rows), fed by different data
- `wwwroot/app.js` — one `Alpine.data('tracker')` component: state, routing, API calls, and
  `decorate()`, which turns the server's board into the exact labels, class names and bar widths the
  markup binds to. Identifiers are assigned by the app, never typed. A story with no description
  gets its markdown file created from the template on demand, in whichever skills folder the
  backlog's other stories already use
- Verified in a real browser: all five views, both pickers, task toggles, all four forms, the
  description round-trip, both deletes, and 320 / 860 / 1280px layouts — with zero CSP violations

**Not yet built** — pick up here:
1. **Pagination** — measured at 1000 stories across 100 epics the board renders in roughly 400 ms,
   which is fine but is the ceiling. An epic holding more than ~20–30 stories is a modelling smell
   rather than a scale problem, so page the board, not the epic.
2. **Dockerfile** — for eventual container deployment (Linux). Nothing built yet. Note: `templates/`
   is currently found by walking up from the working directory (same mechanism as `BacklogLocator`) —
   a container/publish build must ensure `templates/` ships alongside the app, or this needs revisiting.

## Testing

`dotnet test` must pass on a fresh clone with nothing else configured (CI included) — the test suite
ships its own sample `BACKLOG.md` fixture (`tests/MiniTracker.Tests/Fixtures/BACKLOG.sample.md`), so
it's fully self-contained; no external repo or sibling clone is assumed anywhere in the test suite.

## Running it

```bash
dotnet run --project src/MiniTracker.Api
```
Works from any folder — see `README.md` for the Configure flow that points it at a real project.
