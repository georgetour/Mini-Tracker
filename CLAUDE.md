# Mini Tracker — Project Instructions

## What this is

A tiny local web board over a `BACKLOG.yaml` index and one folder per user story — a minimal
Jira/Azure-DevOps-style view of epics, user stories, tasks, and test cases. Click a status and it's
**written through instantly** to the file — no save button, no database, no PowerShell. The files
stay the single source of truth; this tool is a thin UI + write layer over them.

Mini Tracker is standalone and generic: point it at any project's `BACKLOG.yaml` (existing, or a
brand-new one it creates from `templates/BACKLOG.template.yaml`), from any folder. Nothing here is
tied to a specific product or repo.

## Where the design source material lives

The UI's visual design (colors, typography, the status-pair color system) and the original
`BACKLOG.yaml`/`SKILL.md` conventions were designed against a real product backlog. That source
material — mockups, design tokens, the original spec — is kept locally in a gitignored `reference/`
folder (see `.gitignore`), never committed, never part of the public repo. If you have that folder
locally and a planning decision needs revisiting, `reference/README.md` explains what's there and
where it came from.

## Locked architecture decisions — do not relitigate without the user

These were settled over a long design discussion. If a "better" idea comes up (a database, a sync
button, PowerShell, React/Node), it was very likely already considered and rejected.

- **The files ARE the database.** No relational/NoSQL DB. Git is its history/backup. A database
  would just be a second, disagreeing copy of data the files already own.
- **`BACKLOG.yaml` is a thin index; each story owns a folder.** The index holds epics → stories
  (code, title, status, release, folder) and nothing else, so the board draws without opening a
  single story folder. Measured at 1000 stories: **35 ms versus 270 ms** when tasks and test cases
  were in the index. Each story's folder holds `SKILL.md` (prose), `tasks.yaml` and
  `test-cases.yaml` (state). The split is what makes the board scale — don't undo it by putting
  detail back in the index.
- **Anything with state you click lives in YAML; anything you read is markdown.** That is the whole
  rule. Tasks and test cases are data; descriptions and acceptance criteria are prose.
- **Whole-file writes, no surgical text editing.** The app is the only writer, so deserialize →
  mutate → serialize is enough. That deleted `BacklogWriter`, `BacklogGenerator` and `SummaryWriter`
  outright (~500 lines). Serialization is deterministic, so one status change is one line of diff.
- **Write-through, and Sync is an integrity check, not a save.** Every UI click writes immediately —
  no pending buffer, no localStorage. Sync re-reads and validates: YAML errors with a line and
  column, stories naming folders that aren't there, folders nobody references.
- **Tasks and test cases are replaced wholesale.** `PUT /api/story/{code}/tasks` takes the entire
  list, so add, edit, delete, reorder and toggle are one endpoint with no per-item id to drift out
  of step when a stale tab posts an index that has since moved.
- **No roll-up block in the file.** The status summary is computed live. A derived number stored
  beside its source is a materialised view that can disagree with it — which is what the old
  `STATUS-SUMMARY` block was, and why every write had to regenerate it.
- **Splitting storage costs integrity, and validation pays it back.** A story can name a missing
  folder, or a folder can go unreferenced — impossible when it was one file. `/api/validate` reports
  both, and `GET /api/board` returns 422 with the report rather than a blank board.
- **No PowerShell, anywhere.** The one CLI mode is
  `dotnet run --project src/MiniTracker.Api -- migrate <BACKLOG.md>`, which imports an old markdown
  backlog. Pure C#, so CI can run it on a Linux runner with zero shell dependency.
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
- **`SKILL.md` is prose, edited whole, and is exactly three sections:** Description, Tasks,
  Acceptance Criteria. Nothing else belongs in it — a `## Test Cases` table is the same data as
  `test-cases.yaml` written twice, which is the duplication the split storage exists to avoid, and
  technical detail belongs in your own design docs. The story page shows it rendered and offers a
  plain text editor over the raw markdown; the leading `# Title` is dropped for display only,
  because the page already shows it. Its `## Tasks` section is the refinement narrative,
  deliberately a different thing from the tickable `tasks.yaml` beside it. Two tests pin the shape.
- **An epic page lists its stories and nothing else.** No task meters, no expanded test cases —
  those belong to the story page, and repeating them would both duplicate that page and force the
  board to load detail it doesn't show. This UI rule is what makes the thin index sufficient.
- **Dynamic local config, not a hardcoded default path.** `tracker.config.json` (gitignored, lives
  next to the app) holds the logo path, backlog path, and skills folder path — all editable from the
  UI's **⚙ Configure** button and the logo slot, with changes taking effect immediately, no restart.
  A deploy-time override (`BacklogPath` via env/appsettings/CLI arg) always wins over the UI config and
  is never persisted into it.
- **First run shows a live demo, never an error.** With nothing configured and no `BACKLOG.yaml`
  found nearby, the app materializes a real, writable copy of `templates/BACKLOG.template.yaml`, plus
  the whole `templates/skills/` tree alongside it, and points itself at both (`BacklogPath` and
  `SkillsPath`, the latter at `skills/` itself since a story's folder is a bare name) — like a CMS's
  demo content. Pointing Configure at a real project replaces it for good.
- **`templates/BACKLOG.template.yaml`, `templates/skills/` and `templates/SKILL.template.md`** are the
  canonical starting points for a brand-new project — the same layout the app itself writes, ready to
  copy or auto-created by Configure when a given backlog path doesn't exist yet. `templates/skills/`
  ships a `README.md` explaining the folder layout to an agent; it is **one copy**, not one per
  story folder.
- **Header UI**: one global **+ Add** menu (New epic / New user story) sits left of **Configure** /
  Sync / Stage — a single add menu, never one button per thing you can create.
  "Configure" = set backlog path + skills folder path (creates the backlog file from the template if
  missing). "Sync" = reload-from-disk (not save — writes are already instant); "Stage" = optional
  `git add` on the backlog only, **never** auto-commit.
- **Every screen has a real URL, and C# owns the route table.** The path *is* the breadcrumb:
  `/` → `/core-application` → `/core-application/checkout-and-payment` mirrors Overview → Epic →
  User Story, with no `epic/` or `story/` filler. Slugs are generated in `Backlog/Slugs.cs` and
  travel in the board JSON — never recomputed in JavaScript, or the two would eventually disagree
  and produce a dead link. Plus `/releases`, `/releases/{tag}` and the form pages
  (`/add-epic`, `/add-story`, `/edit-epic`, `/configure`). `Program.cs` declares each route
  explicitly and 404s anything else — including `/epic/999` and `/story/US-999`, which it checks
  against the backlog. **No blanket `MapFallbackToFile`**: a catch-all answers 200 to every typo.
  What the server does *not* do is render each view — the browser already holds the board and swaps
  views with no request, which is what makes navigation instant. Server owns *which routes exist*;
  the client owns *the transition*.
- Because URLs are nested, **asset paths in `index.html` must be absolute** (`/app.js`, not
  `app.js`) — a relative path under `/story/US-01` resolves to `/story/app.js`.
- Form pages use real `<form>` markup in `index.html`, not markup assembled in JavaScript. The
  browser does constraint validation, and every endpoint validates again server-side: the JS check
  is only there to save a round trip.
- **Bind an image's `src` behind `x-if`, never `x-show`.** A hidden `<img>` still fetches its src;
  with no logo configured that was a live request for `/story/null`.
- **One navigation per screen size.** Up to 900px the sidebar collapses to a rail; 769–900px it is a
  drawer behind the hamburger; at 768px and below the drawer and hamburger are gone entirely and a
  bottom bar carries the destinations with the add action raised in the centre. Designed to 320px.
- **No multi-user, no cloud sync (yet).** Single local developer, single writer. If that ever changes,
  revisit concurrency and a real state store then — don't build it preemptively.

## Current build status

**Done** (whole suite green, verified end-to-end — run `dotnet test` for the count rather than
trusting a number written here, which goes stale within a day):

*Storage*
- `Backlog/YamlIndex.cs` — reads and writes `BACKLOG.yaml`; deterministic output, so one status
  change is one line of diff. Assigns slugs on every read
- `Backlog/StoryFolder.cs` — one story's folder: `SKILL.md`, `tasks.yaml`, `test-cases.yaml`.
  Refuses any folder name that would escape the skills root
- `Backlog/BacklogValidation.cs` — every issue names the file and the line it is on, plus the
  index↔folder integrity checks: missing folders (error), unreferenced folders (warning), unknown
  status words, releases not in the roadmap. The board is parsed into objects, which carry no
  position, so the line is found back in the text by the story's code
- `Backlog/YamlDiagnostic.cs` — turns a parse error into an edit. Where the mistake is recognisable
  (shell `'\''` escaping, a tab in the indentation, an unquoted colon, a repeated key) it prints the
  corrected line to paste over the original; otherwise the parser's own words plus a caret. Duplicate
  keys are rejected by both readers rather than silently keeping the last one
- `Backlog/PathSafety.cs` — the one place that decides whether a resolved path is inside a root.
  `NormaliseSeparators` first, because `\` is a separator on Windows and an ordinary filename
  character on Linux — so `folder: ..\outside` was harmless on one and a traversal on the other,
  from the same file in git
- `Backlog/Slugs.cs` — URL segments from titles; unique, accent-stripped, and kept off the app's own
  paths so an epic called "Configure" can't take `/configure`

*Services and API*
- `Services/BacklogService.cs` — the read/write gateway. Paths resolve fresh per call, writes are
  serialized behind a lock, and a status write never touches a story's files
- `Services/TrackerConfigService.cs` — local config, backlog-path resolution (deploy override →
  configured path → walk-up fallback → live demo), create-from-template, first-run demo
- `Program.cs` — `GET /api/board` (index only, 422 + report if it won't parse),
  `GET /api/story/{code}`, `PUT /api/story/{code}/tasks`, `PUT /api/story/{code}/test-cases`,
  `POST /api/story/{code}/status`, `GET /api/validate`, config and logo endpoints,
  `GET|POST /api/skill`, `POST /api/git/stage`; plus the `migrate` CLI mode

*Migration*
- `Backlog/Legacy/MarkdownBacklogParser.cs` — the original markdown reader, demoted. Nothing in the
  running app calls it; it exists so an old backlog can still be imported, and keeps its tests
- `Backlog/Legacy/MarkdownMigrator.cs` — `.md` → index + folders. Never overwrites an existing
  `BACKLOG.yaml` or a `SKILL.md`; reports where old skill files lived rather than copying blindly.
  The shipped templates were generated with it, so it is proven on real content

*Front end*
- `wwwroot/index.html` — every view as declarative markup; the epic page is a story list, the story
  page owns tasks and test cases with inline add / edit / delete
- `wwwroot/app.js` — one `Alpine.data('tracker')` component. `decorate()` shapes the index;
  `loadDetail()` fetches a story's folder only when that story opens
- Sync shows a validation banner: severity, message, the file and line it came from, and for a
  recognised YAML mistake the corrected line

*Build and CI*
- `.editorconfig` + `Directory.Build.props` — naming rules and .NET analyzers, enforced by the build
  (`EnforceCodeStyleInBuild`). Warnings become errors only under `ContinuousIntegrationBuild`, so
  half-finished code still builds locally but nothing untidy reaches `main`
- `.github/workflows/ci.yml` — restore, build, test on **Linux**. That is what catches the
  case-sensitivity and path-separator bugs a Windows-only run hides
- `.github/workflows/codeql.yml` — security analysis on push, PR and weekly. Explicit build rather
  than autobuild, because the runner image does not reliably carry .NET 9

**Not yet built** — pick up here:
1. **Warn when marking Done with unfinished tasks.** The board no longer loads task counts, so this
   check needs the story's folder — it only makes sense on the story page.
2. **Pagination** — measured at 1000 stories the board is ~35 ms server-side, but the browser still
   renders every row. An epic holding more than ~20–30 stories is a modelling smell rather than a
   scale problem, so page the board, not the epic.
3. **Dockerfile** — for eventual container deployment (Linux). Note: `templates/` is found by walking
   up from the working directory (same mechanism as `BacklogLocator`) — a container/publish build
   must ensure `templates/` ships alongside the app, or this needs revisiting.

## Testing

`dotnet test` must pass on a fresh clone with nothing else configured (CI included) — the suite ships
its own sample `BACKLOG.md` fixture (`tests/MiniTracker.Tests/Fixtures/BACKLOG.sample.md`), used by
the migration tests, so it's fully self-contained; no external repo or sibling clone is assumed.

Server-side validation is not optional: every endpoint validates again regardless of what the browser
already checked, and each rule has a rejection test written before its UI.

**Reproduce CI before claiming a change is good.** `-p:ContinuousIntegrationBuild=true` on
`dotnet test` does not force a recompile, so an incremental run reports success without the
analyzers having looked at the file you just edited — that shipped a broken build twice. Delete
`bin`/`obj`, then:

```bash
dotnet restore
dotnet build --no-restore -p:ContinuousIntegrationBuild=true
dotnet test  --no-build   -p:ContinuousIntegrationBuild=true
```

## Running it

```bash
dotnet run --project src/MiniTracker.Api
```

Works from any folder — see `README.md` for the Configure flow that points it at a real project.
Coming from a markdown backlog:

```bash
dotnet run --project src/MiniTracker.Api -- migrate path/to/BACKLOG.md
```
