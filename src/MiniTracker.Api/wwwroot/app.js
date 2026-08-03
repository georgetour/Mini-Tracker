/* ============================================================================
   Mini Tracker — the browser half.

   One Alpine component. It holds state, talks to the API, and shapes the JSON
   into exactly what index.html binds to. It never builds HTML: the only place
   markup is produced is renderMarkdown(), for the one field that is markdown by
   definition, and that output is escaped before any transform touches it.

   Written against Alpine's CSP build, so expressions in the markup are property
   reads and method calls only. Anything that needs a template literal, a
   ternary chain or a computation is precomputed here as a plain value — which
   is why decorate() exists.
   ============================================================================ */

const STATUSES = [
  { label:"Not Yet Started", emoji:"⬜", cls:"st-nys"  },
  { label:"Under Review",    emoji:"🔍", cls:"st-rev"  },
  { label:"Refined",         emoji:"✨", cls:"st-ref"  },
  { label:"In Progress",     emoji:"🔄", cls:"st-prog" },
  { label:"Vendor Test",     emoji:"🧪", cls:"st-test" },
  { label:"Done",            emoji:"✅", cls:"st-done" },
  { label:"On Hold",         emoji:"⏸", cls:"st-hold" },
];
const TC_STATUSES = [
  { label:"Not Run", emoji:"⬜", cls:"st-nys"  },
  { label:"Passed",  emoji:"✅", cls:"st-done" },
  { label:"Failed",  emoji:"❌", cls:"st-fail" },
];
// The files store a plain word — "In Progress". The emoji is presentation and lives only here.
const CLASS_FOR = {}, EMOJI_FOR = {};
STATUSES.concat(TC_STATUSES).forEach(s => { CLASS_FOR[s.label] = s.cls; EMOJI_FOR[s.label] = s.emoji; });

/** Ranks statuses by how much attention an epic is getting — the rule the roll-up's 📍 uses. */
const ACTIVITY = { "In Progress":5, "Vendor Test":4, "Under Review":3, "Refined":2, "Not Yet Started":1, "Done":0, "On Hold":0 };

/** Form pages, by URL. Everything else is a board view, which has its own URL shape — see
 *  routePath() and readUrl(). Every screen is addressable, so the breadcrumb, the address bar and
 *  the browser's back button always agree with each other. */
const PAGES = { "/configure":"configure", "/add-epic":"add-epic", "/add-story":"add-story",
                "/edit-epic":"edit-epic", "/edit-story":"edit-story" };
const PATH_FOR = { configure:"/configure", "add-epic":"/add-epic", "add-story":"/add-story",
                   "edit-epic":"/edit-epic", "edit-story":"/edit-story" };

const pct = (a, b) => (b === 0 ? 0 : Math.round((a / b) * 100));
const width = n => "width:" + n + "%";
const plural = (n, one, many) => n + " " + (n === 1 ? one : many);

function esc(s){
  return String(s == null ? "" : s).replace(/[&<>"]/g, c => ({ "&":"&amp;", "<":"&lt;", ">":"&gt;", '"':"&quot;" }[c]));
}

async function api(path, method, body){
  const res = await fetch(path, {
    method: method || "GET",
    headers: body ? { "Content-Type":"application/json" } : {},
    body: body ? JSON.stringify(body) : undefined,
  });
  if(!res.ok) throw new Error((await res.text()) || String(res.status));
  return res.json();
}

/**
 * Turns the index into the shape the markup binds to: every label and class name is computed once,
 * here. The CSP build cannot format strings in an attribute, and even if it could, doing it per
 * binding would recompute on every keystroke elsewhere on the page.
 *
 * The board carries no tasks or test cases — those live in each story's folder and arrive from
 * /api/story/{code} only when that story is opened. That is what keeps this fast as the backlog
 * grows: nothing above the story page ever parses detail it does not render.
 */
function decorate(board){
  const epics = (board.epics || []).map(epic => {
    const stories = (epic.stories || []).map(story => Object.assign({}, story, {
      statusClass: CLASS_FOR[story.status] || "st-nys",
      emoji:       EMOJI_FOR[story.status] || "⬜",
      // A release is optional, so this slot is hidden rather than removed — see the row markup.
      releaseSlotClass: story.release ? "" : "empty",
    }));

    const count = stories.length;
    return Object.assign({}, epic, {
      stories,
      countLabel:  plural(count, "story", "stories"),
      optionLabel: epic.number + " — " + epic.title,
      railTitle:   epic.title + " · " + plural(count, "story", "stories"),
      activity:    Math.max(0, ...stories.map(s => ACTIVITY[s.status] || 0)),
      isCurrent:   false,
    });
  });

  // Exactly one epic is "current": the one holding the most advanced work.
  let best = 0, chosen = null;
  epics.forEach(e => { if(e.activity > best){ best = e.activity; chosen = e; } });
  if(chosen) chosen.isCurrent = true;

  return { project: board.project || "", epics, roadmap: board.roadmap || [] };
}

/**
 * Drops a leading "# Title" heading, which the story page already shows above the card. Only the
 * first heading and only when it is the first non-blank line — a SKILL.md that opens with prose
 * keeps everything, and no other heading is touched.
 */
function withoutLeadingTitle(markdown){
  const lines = markdown.replace(/\r\n/g, "\n").split("\n");
  let i = 0;

  // Step over YAML frontmatter first. These files open with it, so without this the search below
  // finds "---" instead of the heading and gives up — which is exactly what it did.
  if(lines[0] === "---"){
    const end = lines.indexOf("---", 1);
    if(end > 0) i = end + 1;
  }

  while(i < lines.length && lines[i].trim() === "") i++;
  if(i >= lines.length || !/^#\s+\S/.test(lines[i])) return markdown;

  lines.splice(i, 1);
  while(i < lines.length && lines[i].trim() === "") lines.splice(i, 1);
  return lines.join("\n");
}

/** Adds the display fields to one story's tasks and test cases, once per load. */
function decorateDetail(detail){
  const tasks = (detail.tasks || []).map(t => Object.assign({}, t));
  const testCases = (detail.testCases || []).map(t => Object.assign({}, t, {
    statusClass: CLASS_FOR[t.status] || "st-nys",
  }));
  return { tasks, testCases, loading: false, error: "" };
}

/**
 * Renders markdown to HTML. Everything is escaped first, so the transforms below only ever see
 * safe text — a description can never inject markup. Deliberately small: it covers what these
 * files actually contain rather than trying to be a complete CommonMark implementation.
 */
function renderMarkdown(src){
  // Only these schemes may become a link. Without this check a file containing [x](javascript:…)
  // would render a working script URL — escaping the text is not enough, because the danger is in
  // the href, not in the characters.
  const safeHref = url => (/^(https?:\/\/|mailto:|#|\/|\.{0,2}\/)/i.test(url) ? url : null);

  const inline = t => esc(t)
    .replace(/`([^`]+)`/g, "<code>$1</code>")
    .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
    .replace(/(^|[^*])\*([^*\n]+)\*/g, "$1<em>$2</em>")
    .replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (m, text, url) => {
      const href = safeHref(url);
      return href ? '<a href="' + href + '" rel="noopener noreferrer" target="_blank">' + text + "</a>" : m;
    });

  const lines = String(src == null ? "" : src).replace(/\r\n?/g, "\n").split("\n");
  const out = [];
  let i = 0;

  // YAML frontmatter, shown as a quiet metadata block rather than as a stray "---" rule.
  if(lines[0] === "---"){
    const end = lines.indexOf("---", 1);
    if(end > 0){
      out.push('<div class="md-meta">' + lines.slice(1, end).map(esc).join("<br>") + "</div>");
      i = end + 1;
    }
  }

  // "- [ ] AC1: …" and "- [x] …". Rendered as a real checkbox rather than the literal characters,
  // and deliberately not clickable: these belong to the description file, whereas the tick-boxes
  // elsewhere on the page are the story's tasks in tasks.yaml. Two different things.
  const TASK_ITEM = /^\[([ xX])\]\s+([\s\S]*)$/;

  let list = [], ordered = false, para = [];
  const flushList = () => {
    if(!list.length) return;
    const items = list.map(x => {
      const m = x.match(TASK_ITEM);
      if(!m) return "<li>" + inline(x) + "</li>";
      const done = m[1] !== " ";
      return '<li class="md-task"><span class="md-box' + (done ? " on" : "") + '" aria-hidden="true">'
        + (done ? "✓" : "") + "</span><span>" + inline(m[2]) + "</span></li>";
    });
    const tag = ordered ? "ol" : "ul";
    const cls = list.some(x => TASK_ITEM.test(x)) ? ' class="md-tasks"' : "";
    out.push("<" + tag + cls + ">" + items.join("") + "</" + tag + ">");
    list = [];
  };
  const flushPara = () => { if(para.length){ out.push("<p>" + inline(para.join(" ")) + "</p>"); para = []; } };

  for(; i < lines.length; i++){
    const line = lines[i];

    if(line.startsWith("```")){                                     // fenced code
      flushPara(); flushList();
      const body = [];
      for(i++; i < lines.length && !lines[i].startsWith("```"); i++) body.push(lines[i]);
      out.push("<pre><code>" + esc(body.join("\n")) + "</code></pre>");
      continue;
    }
    if(/^\|.*\|\s*$/.test(line)){                                   // table
      flushPara(); flushList();
      const rows = [];
      for(; i < lines.length && /^\|.*\|\s*$/.test(lines[i]); i++) rows.push(lines[i]);
      i--;
      const cells = r => r.trim().replace(/^\||\|$/g, "").split("|").map(c => c.trim());
      const isSep = r => /^[\s|:-]+$/.test(r);
      const head = cells(rows[0]);
      const body = rows.slice(isSep(rows[1] || "") ? 2 : 1).map(cells);
      out.push('<div class="md-tablewrap"><table><thead><tr>'
        + head.map(c => "<th>" + inline(c) + "</th>").join("") + "</tr></thead><tbody>"
        + body.map(r => "<tr>" + r.map(c => "<td>" + inline(c) + "</td>").join("") + "</tr>").join("")
        + "</tbody></table></div>");
      continue;
    }

    const h = line.match(/^(#{1,6})\s+(.*)$/);
    if(h){
      flushPara(); flushList();
      const lvl = Math.min(h[1].length + 1, 6);                     // an h1 in the file is an h2 here
      out.push("<h" + lvl + ">" + inline(h[2]) + "</h" + lvl + ">");
      continue;
    }
    if(/^\s*[-*]\s+/.test(line)){
      flushPara(); if(ordered) flushList();
      ordered = false; list.push(line.replace(/^\s*[-*]\s+/, "")); continue;
    }
    if(/^\s*\d+\.\s+/.test(line)){
      flushPara(); if(!ordered) flushList();
      ordered = true; list.push(line.replace(/^\s*\d+\.\s+/, "")); continue;
    }
    if(/^>\s?/.test(line)){
      flushPara(); flushList();
      out.push("<blockquote>" + inline(line.replace(/^>\s?/, "")) + "</blockquote>"); continue;
    }
    if(/^(---|\*\*\*|___)\s*$/.test(line)){
      flushPara(); flushList(); out.push("<hr>"); continue;
    }
    if(line.trim() === ""){ flushPara(); flushList(); continue; }

    // A wrapped bullet: markdown lets a list item run onto the next line without indentation, and
    // that continuation belongs to the item rather than starting a new paragraph.
    if(list.length && !para.length){ list[list.length - 1] += " " + line.trim(); continue; }

    flushList();
    para.push(line.trim());
  }
  flushPara(); flushList();
  return out.join("");
}

document.addEventListener("alpine:init", () => {
  Alpine.data("tracker", () => ({

    /* ------------------------------------------------------------ state -- */
    board: { project:"", epics: [], roadmap: [] },
    detail: { tasks: [], testCases: [], loading: false, error: "" },
    addingTask: false, addingTest: false, editingTask: -1, draftText: "",
    report: { show:false, title:"", cls:"", issues:[] },
    config: { backlogPath:null, skillsPath:null, logoPath:null },
    loadError: "",

    view: "board",          // board | epic | story | releases | release
    page: "",               // "" | configure | add-epic | add-story | edit-epic
    epicNumber: null,
    storyCode: null,
    releaseTag: null,
    editingEpic: null,
    seedEpic: null,          // which epic the add-story form should preselect, if reached from one

    winWidth: window.innerWidth,
    sidebarCollapsed: false,
    drawerOpen: false,
    expanded: [],
    addOpen: false,
    addMobile: false,

    picker: { open:false, options:[], narrow:false, cls:"", pos:"", current:"", kind:"", story:null, tc:null },
    skill:  { path:null, original:"", draft:"", html:"", editing:false, loading:false, saving:false, error:"" },
    confirm:{ title:"", body:"", okLabel:"Delete" },

    form: {}, err: {}, saving: false,
    logoStamp: Date.now(),
    toastText: "", toastOn: false, toastTimer: null,

    /* ------------------------------------------------------------- init -- */
    init(){
      window.addEventListener("resize", () => { this.winWidth = window.innerWidth; });
      window.addEventListener("popstate", () => this.readUrl());
      document.addEventListener("keydown", ev => {
        if(ev.key !== "Escape") return;
        this.addOpen = false;
        this.picker.open = false;
        if(this.drawerOpen) this.drawerOpen = false;
      });

      // The board first: on a first run the demo is materialised as a side effect of GET
      // /api/board, so asking for the config before that would race and come back empty.
      this.load()
        .catch(e => { this.loadError = "Failed to load the board: " + e.message; })
        .then(() => this.loadConfig())
        .then(() => this.readUrl());
    },

    async load(){
      this.board = decorate(await api("/api/board"));
      if(!this.expanded.length) this.expanded = this.board.epics.map(e => e.number);
    },

    async loadConfig(){
      try{ this.config = await api("/api/config"); }
      catch(e){ /* non-fatal: the board is already usable, and Configure can still be opened */ }
    },

    /**
     * Sync. Splitting storage across an index and one folder per story made it possible for the two
     * to disagree — a story naming a folder that is not there, a folder nobody references, a file
     * that will not parse. That class of bug was impossible when it was all one file, so this
     * button pays it back: it checks the whole backlog and says exactly what is wrong and where.
     */
    async reload(){
      try{
        const report = await api("/api/validate");
        const issues = report.issues || [];
        const errors = issues.filter(i => i.severity === "error").length;
        const warnings = issues.length - errors;

        this.report = {
          show: issues.length > 0,
          cls: report.ok ? "warn" : "bad",
          // The board is still on screen behind this — these are integrity problems, not a failure
          // to load. Saying "stopping the backlog loading" while it is plainly loaded reads as a
          // lie and makes people distrust the rest of the message.
          title: report.ok
            ? plural(warnings, "thing worth a look", "things worth a look")
            : plural(errors, "problem to fix", "problems to fix"),
          issues: issues.map(i => Object.assign({}, i, {
            sevClass: i.severity === "error" ? "sev-bad" : "sev-warn",
          })),
        };

        if(!report.ok) return this.toast("The backlog has errors");

        await this.load();
        if(this.view === "story") await this.loadDetail();
        this.toast(issues.length ? "Reloaded — with warnings" : "Reloaded from BACKLOG.yaml");
      }catch(e){
        this.toast("Could not read the backlog");
      }
    },

    dismissReport(){ this.report.show = false; },

    async stage(){
      try{ await api("/api/git/stage", "POST"); this.toast("Staged (git add)"); }
      catch(e){ this.toast("Stage failed"); }
    },

    /* --------------------------------------------------------- routing -- */
    /**
     * The URL for whatever is on screen — and it is exactly the breadcrumb:
     *   Overview › Core Application › Checkout and Payment
     *   /          core-application / checkout-and-payment
     * The slugs come from the board JSON. They are generated in C# (Slugs.cs) and never recomputed
     * here, so a link the browser builds and a URL the server resolves cannot drift apart.
     */
    routePath(){
      if(this.page) return PATH_FOR[this.page];
      if(this.view === "releases") return "/releases";
      if(this.view === "release")  return "/releases/" + encodeURIComponent(this.releaseTag);
      if(this.view === "epic"){
        const e = this.epic;
        return e ? "/" + e.slug : "/";
      }
      if(this.view === "story"){
        const e = this.storyEpic, s = this.story;
        return e && s ? "/" + e.slug + "/" + s.slug : "/";
      }
      return "/";
    },

    /** Reads the address bar into state. Runs on first load and on every back/forward, so it never
     *  pushes history of its own. A URL naming something that no longer exists falls back to the
     *  board and rewrites itself, so back doesn't bounce off a dead entry. */
    readUrl(){
      const path = location.pathname;

      const page = PAGES[path];
      if(page){
        // Both edit pages need something already selected. Reached cold — a bookmark, a refresh —
        // there is nothing to edit, so fall back rather than showing an empty form.
        if(page === "edit-epic" && this.editingEpic === null) return this.resetToBoard();
        if(page === "edit-story" && !this.storyCode) return this.resetToBoard();
        return this.openPage(page, false);
      }
      this.page = "";

      if(path === "/" || path === "") return this.show("board", false);
      if(path === "/releases") return this.show("releases", false);

      const parts = path.split("/").filter(Boolean).map(decodeURIComponent);

      if(parts[0] === "releases" && parts.length === 2){
        if(!this.releaseGroups().some(g => g.title === parts[1])) return this.resetToBoard();
        this.releaseTag = parts[1];
        return this.show("release", false);
      }

      // /{epic-slug} and /{epic-slug}/{story-slug} — the breadcrumb, read back.
      if(parts.length === 1 || parts.length === 2){
        const epic = this.epics.find(e => e.slug === parts[0]);
        if(!epic) return this.resetToBoard();

        if(parts.length === 1){
          this.epicNumber = epic.number;
          return this.show("epic", false);
        }

        const story = epic.stories.find(s => s.slug === parts[1]);
        if(!story) return this.resetToBoard();
        this.storyCode = story.code;
        this.show("story", false);
        this.loadDetail();
        return this.loadSkill();
      }

      return this.resetToBoard();
    },

    /** A URL naming something that is not there — a stale bookmark, a typo, a story since deleted.
     *  Landing silently on the Overview looks like the link worked, so say what happened. */
    resetToBoard(){
      const asked = location.pathname;
      history.replaceState({}, "", "/");
      this.show("board", false);
      if(asked && asked !== "/") this.toast("That page no longer exists — showing the Overview");
    },

    openPage(page, push){
      this.page = page;
      this.err = {};
      this.addOpen = false;
      this.drawerOpen = false;
      if(page === "configure") this.form = { backlogPath: this.config.backlogPath || "", skillsPath: this.config.skillsPath || "" };
      if(page === "add-epic")  this.form = { title:"" };
      // seedEpic is set when Add is reached from inside an epic, so the dropdown already names the
      // epic you were looking at rather than the first one on the board.
      if(page === "add-story") this.form = { epicNumber: String(this.seedEpic != null ? this.seedEpic
                                               : (this.board.epics.length ? this.board.epics[0].number : 0)),
                                             title:"", release:"", description:"" };
      this.seedEpic = null;
      if(page === "edit-epic") this.form = { title: this.epicOf(this.editingEpic) ? this.epicOf(this.editingEpic).title : "" };
      if(page === "edit-story") this.form = { title: this.story ? this.story.title : "",
                                              release: this.story ? this.story.release : "" };
      if(push !== false) this.navigate();
    },

    /** Leaves any form page and shows a board view. */
    show(view, push){
      this.view = view;
      this.page = "";
      this.addOpen = false;
      this.drawerOpen = false;
      this.picker.open = false;
      if(push !== false) this.navigate();
    },

    navigate(){
      const path = this.routePath();
      if(location.pathname !== path) history.pushState({}, "", path);
    },

    goBoard(push){ this.show("board", push); },
    goReleases(push){ this.show("releases", push); },
    goConfigure(){ this.openPage("configure"); },
    goAddEpic(){ this.openPage("add-epic"); },
    goAddStory(){ this.openPage("add-story"); },
    goAddStoryHere(){ this.seedEpic = this.epicNumber; this.openPage("add-story"); },
    goRenameEpic(){ this.editingEpic = this.epicNumber; this.openPage("edit-epic"); },
    goEditStory(){ if(this.story) this.openPage("edit-story"); },

    /** Cancel returns you to what you were editing, not to the Overview. Dumping someone at the
     *  top of the app because they changed their mind loses their place for no reason. */
    cancelEditEpic(){ this.openEpicByNumber(this.editingEpic); },
    cancelEditStory(){ this.show("story"); },

    openEpic(epic){ this.openEpicByNumber(epic.number); },
    openEpicByNumber(number, push){ this.epicNumber = number; this.show("epic", push); },
    openRelease(section){ this.releaseTag = section.title; this.show("release"); },

    openStory(story){
      this.storyCode = story.code;
      this.show("story");
      this.loadDetail();
      this.loadSkill();
    },

    goCrumb(c){
      if(c.to === "board") this.goBoard();
      else if(c.to === "releases") this.goReleases();
      else if(c.to === "epic") this.openEpicByNumber(c.epicNumber);
    },

    /* ------------------------------------------------------ derived UI -- */
    get epics(){ return this.board.epics; },
    get onFormPage(){ return this.page !== ""; },
    get isListView(){ return this.view === "board" || this.view === "releases" || this.view === "release"; },
    get isEpicView(){ return this.view === "epic"; },
    get isStoryView(){ return this.view === "story"; },
    get bottomBarOwnsNav(){ return this.winWidth <= 768; },
    get railed(){ return this.winWidth > 900 && this.sidebarCollapsed; },

    get sidebarClass(){
      const cls = [];
      if(this.railed) cls.push("collapsed");
      if(this.drawerOpen) cls.push("open");
      return cls.join(" ");
    },
    get scrimClass(){ return this.drawerOpen ? "show" : ""; },
    get drawerLabel(){ return this.drawerOpen ? "Hide navigation" : "Show navigation"; },
    toggleDrawer(){ this.drawerOpen = !this.drawerOpen; },
    closeDrawer(){ this.drawerOpen = false; },
    toggleSidebar(){ this.sidebarCollapsed = !this.sidebarCollapsed; },

    get boardNavClass(){ return this.view === "board" && !this.onFormPage ? "on" : ""; },
    get releasesNavClass(){ return (this.view === "releases" || this.view === "release") && !this.onFormPage ? "on" : ""; },
    mnavClass(which){
      if(this.onFormPage) return this.page === which ? "active" : "";
      if(which === "board") return this.view === "board" ? "active" : "";
      if(which === "releases") return this.view === "releases" || this.view === "release" ? "active" : "";
      return "";
    },

    toggleAdd(){ this.addMobile = false; this.addOpen = !this.addOpen; },
    toggleAddMobile(){ this.addMobile = true; this.addOpen = !this.addOpen; },
    closeAdd(){ this.addOpen = false; },
    get addMenuClass(){ return this.addMobile ? "mobile" : ""; },

    toggleTheme(){
      const cur = document.body.getAttribute("data-theme");
      const dark = matchMedia("(prefers-color-scheme:dark)").matches;
      document.body.setAttribute("data-theme", cur === "dark" ? "light" : cur === "light" ? "dark" : (dark ? "light" : "dark"));
    },

    /* ---------------------------------------------------------- sidebar -- */
    isEpicOpen(epic){ return this.expanded.indexOf(epic.number) !== -1; },
    chevron(epic){ return this.isEpicOpen(epic) ? "▾" : "▸"; },
    toggleEpic(epic){
      const i = this.expanded.indexOf(epic.number);
      if(i === -1) this.expanded.push(epic.number); else this.expanded.splice(i, 1);
    },
    /** An epic is highlighted when you are on it, or when the open story belongs to it. */
    epicIsHighlighted(epic){
      if(this.view === "epic" && this.epicNumber === epic.number) return true;
      return this.view === "story" && epic.stories.some(s => s.code === this.storyCode);
    },
    epicIsActive(epic){ return this.view === "epic" && this.epicNumber === epic.number; },
    epicRowClass(epic){
      const cls = [];
      if(this.epicIsHighlighted(epic)) cls.push("hl");
      if(this.epicIsActive(epic)) cls.push("on");
      return cls.join(" ");
    },
    railEpicClass(epic){ return this.epicRowClass(epic); },
    storyRowClass(story){ return this.view === "story" && this.storyCode === story.code ? "on" : ""; },

    /* ------------------------------------------------- list-view model -- */
    /** Board and release views are the same shape — a header plus story rows — so they share
     *  one template and differ only in what fills this list. */
    get sections(){
      if(this.view === "board"){
        return this.epics.map(e => ({
          key: "e" + e.number, kind: "epic", epicNumber: e.number, title: e.title,
          countLabel: e.countLabel, rows: e.stories.map(s => ({ s, ctx: "" })),
        }));
      }
      const groups = this.releaseGroups();
      if(this.view === "release"){
        const one = groups.filter(g => g.title === this.releaseTag);
        return one.map(g => Object.assign({}, g, { clickable:false }));
      }
      return groups;
    },

    /** Groups stories by release tag, ordered by the roadmap; unscheduled last. */
    releaseGroups(){
      const order = this.board.roadmap;
      const map = new Map();
      this.epics.forEach(e => e.stories.forEach(s => {
        const key = s.release || "Unscheduled";
        if(!map.has(key)) map.set(key, []);
        map.get(key).push({ s, ctx: e.title });
      }));
      const keys = Array.from(map.keys()).sort((a, b) => {
        if(a === "Unscheduled") return 1;
        if(b === "Unscheduled") return -1;
        const ia = order.indexOf(a), ib = order.indexOf(b);
        return (ia < 0 ? 999 : ia) - (ib < 0 ? 999 : ib);
      });
      return keys.map(tag => ({
        key: "r" + tag, kind: "release", title: tag, clickable: true,
        countLabel: plural(map.get(tag).length, "story", "stories"), rows: map.get(tag),
      }));
    },

    get stackClass(){ return this.view === "release" ? "tight" : ""; },
    get emptyListMessage(){ return this.view === "board" ? "No epics yet." : "No releases found."; },

    get crumbs(){
      if(this.view === "epic" && this.epic)
        return [{ label:"Overview", link:true, to:"board", first:true },
                { label: this.epic.number + " — " + this.epic.title, link:false }];
      if(this.view === "story" && this.story && this.storyEpic)
        return [{ label:"Overview", link:true, to:"board", first:true },
                { label: this.storyEpic.number + " — " + this.storyEpic.title, link:true, to:"epic", epicNumber: this.storyEpic.number },
                { label: this.story.title, link:false }];
      if(this.view === "release")
        return [{ label:"Overview", link:true, to:"board", first:true },
                { label:"By release", link:true, to:"releases" },
                { label: this.releaseTag, link:false }];
      return [];
    },
    get crumbClass(){ return this.view === "story" ? "flush" : ""; },

    /* -------------------------------------------------------- epic view -- */
    epicOf(number){ return this.epics.find(e => e.number === number) || null; },
    get epic(){ return this.epicOf(this.epicNumber); },
    get epicStories(){ return this.epic ? this.epic.stories : []; },
    get epicHeading(){ return this.epic ? this.epic.number + " — " + this.epic.title : ""; },
    get epicCountLabel(){ return this.epic ? this.epic.countLabel : ""; },
    get epicIsCurrent(){ return !!this.epic && this.epic.isCurrent; },
    get epicSectionClass(){ return ""; },
    get editingEpicLabel(){ return "Epic " + this.editingEpic; },

    /* ------------------------------------------------------ story view -- */
    storyByCode(code){
      // Every field on the detail page reads through this, so it stops at the first match rather
      // than walking the whole board each time.
      for(const e of this.epics){
        const s = e.stories.find(x => x.code === code);
        if(s) return s;
      }
      return null;
    },
    get story(){ return this.storyByCode(this.storyCode); },
    get storyEpic(){ return this.epics.find(e => e.stories.some(s => s.code === this.storyCode)) || null; },
    get storyTitle(){ return this.story ? this.story.title : ""; },
    get storyRelease(){ return this.story ? this.story.release : ""; },
    get storyStatusClass(){ return this.story ? this.story.statusClass : ""; },
    get storyStatusLabel(){ return this.story ? this.story.status : ""; },
    get storyEmoji(){ return this.story ? this.story.emoji : ""; },

    // Tasks and test cases come from the story's folder, not the board.
    get storyTasks(){ return this.detail.tasks; },
    get storyTestCases(){ return this.detail.testCases; },
    get doneCount(){ return this.detail.tasks.filter(t => t.done).length; },
    get tcPass(){ return this.detail.testCases.filter(t => t.status === "Passed").length; },
    get tcFail(){ return this.detail.testCases.filter(t => t.status === "Failed").length; },
    get detailTaskLabel(){ return this.doneCount + "/" + this.detail.tasks.length; },
    get detailTaskBar(){ return width(pct(this.doneCount, this.detail.tasks.length)); },
    get detailTcLabel(){ return this.tcPass + "/" + this.detail.testCases.length; },
    get detailTcFail(){ return this.tcFail; },
    get detailTcFailLabel(){ return " · " + this.tcFail + " ✗"; },
    get detailTcPassBar(){ return width(pct(this.tcPass, this.detail.testCases.length)); },
    get detailTcFailBar(){ return width(pct(this.tcFail, this.detail.testCases.length)); },
    get detailTasksHeading(){ return "TASKS · " + this.detailTaskLabel; },
    get detailTcHeading(){ return "TEST CASES · " + this.detailTcLabel; },

    boxClass(task){ return task.done ? "on" : ""; },
    taskMark(task){ return task.done ? "✓" : ""; },
    taskTextClass(task){ return task.done ? "done" : ""; },

    async loadDetail(){
      this.detail = { tasks: [], testCases: [], loading: true, error: "" };
      const code = this.storyCode;
      try{
        const d = await api("/api/story/" + encodeURIComponent(code));
        if(this.storyCode !== code) return;   // another story was opened while this was in flight
        this.detail = decorateDetail(d);
      }catch(e){
        if(this.storyCode !== code) return;
        this.detail = { tasks: [], testCases: [], loading: false,
                        error: e.message || "This story's files could not be read." };
      }
    },

    /* ----------------------------------------------------------- writes -- */
    async write(path, body, message){
      try{
        this.board = decorate(await api(path, "POST", body));
        this.toast(message);
      }catch(e){ this.toast(e.message || "That change could not be saved"); }
    },

    /* ----------------------------------------- tasks and test cases ------ */
    /** One call covers add, edit, delete, reorder and toggle: the client sends the list it wants
     *  the file to hold, so there is no per-item addressing to drift out of step. */
    async saveTasks(tasks, message){
      try{
        const d = await api("/api/story/" + encodeURIComponent(this.storyCode) + "/tasks", "PUT",
                            { tasks: tasks.map(t => ({ text: t.text, done: t.done })) });
        this.detail = decorateDetail(d);
        this.toast(message);
      }catch(e){ this.toast(e.message || "That change could not be saved."); }
    },

    async saveTestCases(cases, message){
      try{
        const d = await api("/api/story/" + encodeURIComponent(this.storyCode) + "/test-cases", "PUT",
                            { testCases: cases.map(c => ({ text: c.text, status: c.status })) });
        this.detail = decorateDetail(d);
        this.toast(message);
      }catch(e){ this.toast(e.message || "That change could not be saved."); }
    },

    async toggleTask(i){
      const tasks = this.detail.tasks.map(t => Object.assign({}, t));
      tasks[i].done = !tasks[i].done;
      await this.saveTasks(tasks, "Saved");
    },

    /** Long text would push the buttons off a phone screen, so the prompt quotes just enough of it
     *  to identify which row you are about to lose. */
    shorten(text){
      const t = (text || "").trim();
      return t.length > 90 ? t.slice(0, 90).trimEnd() + "…" : t;
    },

    async removeTask(i){
      const task = this.detail.tasks[i];
      if(!task) return;

      const ok = await this.ask("Delete this task?",
        "“" + this.shorten(task.text) + "” will be removed from tasks.yaml.", "Delete task");
      if(!ok) return;

      await this.saveTasks(this.detail.tasks.filter((_, j) => j !== i), "Task deleted");
    },

    async removeTestCase(i){
      const tc = this.detail.testCases[i];
      if(!tc) return;

      const ok = await this.ask("Delete this test case?",
        "“" + this.shorten(tc.text) + "” will be removed from test-cases.yaml.", "Delete test case");
      if(!ok) return;

      await this.saveTestCases(this.detail.testCases.filter((_, j) => j !== i), "Test case deleted");
    },

    /* --------------------------------------------- inline add and edit --- */
    // A task is one field with no URL of its own, so it is edited in place. Multi-field creates —
    // an epic, a story — still get a real page.
    startAddTask(){ this.cancelEdit(); this.addingTask = true; this.focusRef("newTask"); },
    startAddTest(){ this.cancelEdit(); this.addingTest = true; this.focusRef("newTest"); },
    cancelEdit(){ this.addingTask = false; this.addingTest = false; this.editingTask = -1; this.draftText = ""; },

    focusRef(name){
      this.$nextTick(() => { if(this.$refs[name]) this.$refs[name].focus(); });
    },

    startEditTask(i){
      this.cancelEdit();
      this.editingTask = i;
      this.draftText = this.detail.tasks[i].text;
      this.focusRef("editTask");
    },

    async commitEditTask(){
      const i = this.editingTask;
      if(i < 0) return;
      const text = this.draftText.trim();
      this.editingTask = -1;
      this.draftText = "";
      if(!text || text === this.detail.tasks[i].text) return;

      const tasks = this.detail.tasks.map(t => Object.assign({}, t));
      tasks[i].text = text;
      await this.saveTasks(tasks, "Task updated");
    },

    async commitAddTask(){
      const text = this.draftText.trim();
      if(!text) return this.cancelEdit();
      this.draftText = "";
      const tasks = this.detail.tasks.map(t => Object.assign({}, t));
      tasks.push({ text, done: false });
      await this.saveTasks(tasks, "Task added");
      this.focusRef("newTask");            // stay open so several can be typed in a row
    },

    async commitAddTest(){
      const text = this.draftText.trim();
      if(!text) return this.cancelEdit();
      this.draftText = "";
      const cases = this.detail.testCases.map(c => Object.assign({}, c));
      cases.push({ text, status: "Not Run" });
      await this.saveTestCases(cases, "Test case added");
      this.focusRef("newTest");
    },

    /* --------------------------------------------------- status picker -- */
    openStatusPicker(story, el){ this.openPicker(el, STATUSES, story.status, "story", story, null); },
    openTestPicker(i, el){ this.openPicker(el, TC_STATUSES, this.detail.testCases[i].status, "tc", null, i); },

    openPicker(el, options, current, kind, story, tc){
      const r = el.getBoundingClientRect();
      const w = options === TC_STATUSES ? 150 : 224;
      const max = window.scrollX + document.documentElement.clientWidth - w - 12;
      const left = Math.max(window.scrollX + 8, Math.min(r.left + window.scrollX, max));
      this.picker = {
        open: true, options, current, kind, story, tc,
        narrow: options === TC_STATUSES,
        cls: options === TC_STATUSES ? "narrow" : "",
        pos: "left:" + left + "px;top:" + (r.bottom + window.scrollY + 6) + "px",
      };
    },
    closePicker(){ this.picker.open = false; },
    isCurrentOption(option){ return option.label === this.picker.current; },
    optionClass(option){ return this.isCurrentOption(option) ? "on" : ""; },
    optionSquareClass(option){ return option.cls + (this.picker.narrow ? " md" : " lg"); },

    choose(option){
      const p = this.picker;
      this.picker.open = false;

      if(p.kind === "story"){
        this.write("/api/story/" + p.story.code + "/status", { status: option.label }, "Saved");
        return;
      }
      const cases = this.detail.testCases.map(c => Object.assign({}, c));
      cases[p.tc].status = option.label;
      this.saveTestCases(cases, "Saved");
    },

    /* ------------------------------------------------------ description -- */
    get skillReading(){ return !!this.skill.path && !this.skill.editing && !this.skill.loading && !this.skill.error; },

    async loadSkill(){
      const story = this.story;
      // The description is always <folder>/SKILL.md — created with the story, so there is no
      // "this story has no description" state left to handle.
      const path = story ? story.folder + "/SKILL.md" : null;
      this.skill = { path, original:"", draft:"", html:"", editing:false, loading:false, saving:false, error:"" };
      if(!path) return;

      this.skill.loading = true;
      try{
        const res = await fetch("/api/skill?path=" + encodeURIComponent(path));
        const text = await res.text();
        if(!res.ok) throw new Error(text || "This description could not be opened.");
        if(this.skill.path !== path) return;      // a different story was opened meanwhile
        this.skill.original = text;
        // The page already shows the story title above this card, and SKILL.md opens with the same
        // title as its H1 — so it appeared twice, one line apart. Dropped for display only; the
        // editor and the saved file keep it, because the file has to stand on its own on disk.
        this.skill.html = renderMarkdown(withoutLeadingTitle(text));
      }catch(e){
        this.skill.error = e.message || "This description could not be opened.";
      }finally{
        this.skill.loading = false;
      }
    },

    editDescription(){
      this.skill.draft = this.skill.original;
      this.skill.editing = true;
    },

    cancelDescription(){ this.skill.editing = false; this.skill.draft = ""; },

    async saveDescription(){
      this.skill.saving = true;
      try{
        const res = await fetch("/api/skill", {
          method: "POST",
          headers: { "Content-Type":"application/json" },
          body: JSON.stringify({ path: this.skill.path, content: this.skill.draft }),
        });
        if(!res.ok) throw new Error((await res.text()) || "The file could not be saved.");
        this.skill.original = this.skill.draft;
        this.skill.html = renderMarkdown(this.skill.draft);
        this.skill.editing = false;
        this.toast("Description saved");
      }catch(e){ this.toast(e.message || "The file could not be saved."); }
      finally{ this.skill.saving = false; }
    },

    /* --------------------------------------------------------- deleting -- */
    /** Resolves true only when the delete button closed the dialog — Escape and Cancel mean no. */
    ask(title, body, okLabel){
      this.confirm = { title, body, okLabel };
      const dlg = this.$refs.confirm;
      return new Promise(resolve => {
        dlg.addEventListener("close", () => resolve(dlg.returnValue === "delete"), { once:true });
        dlg.showModal();
      });
    },

    async askDeleteStory(){
      const s = this.story;
      if(!s) return;
      const ok = await this.ask("Delete " + s.code + "?",
        '"' + s.title + '" and its tasks and test cases are removed from BACKLOG.yaml. Its folder is deleted too.',
        "Delete story");
      if(!ok) return;
      try{
        this.board = decorate(await api("/api/story/" + encodeURIComponent(s.code), "DELETE"));
        this.goBoard();
        this.toast("Story deleted");
      }catch(e){ this.toast(e.message || "The story could not be deleted."); }
    },

    async askDeleteEpic(){
      const e = this.epic;
      if(!e) return;
      const n = e.stories.length;
      const ok = await this.ask("Delete Epic " + e.number + "?",
        n ? '"' + e.title + '" and its ' + plural(n, "story is", "stories are")
            + " removed from BACKLOG.yaml, along with " + (n === 1 ? "its folder" : "their folders")
            + ". This cannot be undone from here."
          : '"' + e.title + '" is removed from BACKLOG.yaml. It has no stories.',
        n ? "Delete epic and " + plural(n, "story", "stories") : "Delete epic");
      if(!ok) return;
      try{
        this.board = decorate(await api("/api/epic/" + e.number, "DELETE"));
        this.goBoard();
        this.toast("Epic deleted");
      }catch(err){ this.toast(err.message || "The epic could not be deleted."); }
    },

    /* ------------------------------------------------------------ forms -- */
    /** Identifiers are assigned by the app, never typed — the same reasoning as a database key. */
    get nextEpicNumber(){
      const used = this.epics.map(e => e.number);
      return used.length ? Math.max(...used) + 1 : 0;
    },
    get nextEpicLabel(){ return "Epic " + this.nextEpicNumber; },
    get nextStoryCode(){
      const nums = [];
      this.epics.forEach(e => e.stories.forEach(s => nums.push(Number(s.code.replace("US-", "")))));
      const next = nums.length ? Math.max(...nums) + 1 : 1;
      return "US-" + String(next).padStart(2, "0");
    },

    /** Client-side checks are a courtesy that saves a round trip. Every one of them is enforced
     *  again on the server, which is the check that actually counts. */
    require(field, message){
      const value = (this.form[field] || "").trim();
      if(!value) this.err[field] = message;
      return value;
    },

    async submitEpic(){
      this.err = {};
      const title = this.require("title", "Give the epic a title.");
      if(!title) return;
      this.saving = true;
      try{
        const number = this.nextEpicNumber;
        this.board = decorate(await api("/api/epic", "POST", { number, title }));
        // Open the epic just created, the same way adding a story opens the story. Returning to the
        // Overview meant the two add flows ended somewhere different for no reason, and left you to
        // find the thing you had just made.
        this.openEpicByNumber(number);
        this.toast("Epic added");
      }catch(e){ this.err.form = e.message || "The epic could not be added."; }
      finally{ this.saving = false; }
    },

    async submitRename(){
      this.err = {};
      const title = this.require("title", "Give the epic a title.");
      if(!title) return;
      this.saving = true;
      try{
        this.board = decorate(await api("/api/epic/" + this.editingEpic, "POST", { title }));
        this.openEpicByNumber(this.editingEpic);
        this.toast("Epic renamed");
      }catch(e){ this.err.form = e.message || "The epic could not be renamed."; }
      finally{ this.saving = false; }
    },

    async submitEditStory(){
      this.err = {};
      const title = this.require("title", "Give the story a title.");
      if(!title) return;

      const code = this.storyCode;
      this.saving = true;
      try{
        this.board = decorate(await api("/api/story/" + encodeURIComponent(code), "POST", {
          title, release: (this.form.release || "").trim(),
        }));
        // The slug follows the title, so the URL this story lives at has just changed. Re-open it
        // by code and let routePath() write the new address.
        this.storyCode = code;
        this.show("story");
        this.toast("Story updated");
      }catch(e){ this.err.form = e.message || "The story could not be updated."; }
      finally{ this.saving = false; }
    },

    async submitStory(){
      this.err = {};
      const title = this.require("title", "Give the story a title.");
      if(this.form.epicNumber === "" || this.form.epicNumber == null) this.err.epicNumber = "Choose which epic this story belongs to.";
      if(!title || this.err.epicNumber) return;
      this.saving = true;
      try{
        this.board = decorate(await api("/api/story", "POST", {
          epicNumber: Number(this.form.epicNumber),
          code: this.nextStoryCode,
          title,
          release: (this.form.release || "").trim(),
          description: (this.form.description || "").trim(),
        }));
        // Open the story that was just created rather than dropping back to the Overview — you
        // almost always want to keep working on the thing you just made.
        this.storyCode = this.board.epics
          .flatMap(e => e.stories).map(s => s.code)
          .sort((a, b) => Number(b.replace(/\D/g, "")) - Number(a.replace(/\D/g, "")))[0];
        this.show("story");
        this.loadDetail();
        this.loadSkill();
        this.toast("User story added");
      }catch(e){ this.err.form = e.message || "The story could not be added."; }
      finally{ this.saving = false; }
    },

    async saveConfig(){
      this.err = {};
      const backlog = (this.form.backlogPath || "").trim();
      // Must match TrackerConfigService.ValidateBacklogPath. This check only saves a round trip —
      // the server enforces the same rule — but when the two disagree the form blocks a path the
      // server would have accepted, which is worse than having no check at all.
      if(backlog && !/\.ya?ml$/i.test(backlog)){
        this.err.backlogPath = "Point this at a .yaml file, for example C:/projects/my-app/BACKLOG.yaml.";
        return;
      }
      const skills = (this.form.skillsPath || "").trim();
      const file = this.$refs.logo.files[0];

      this.saving = true;
      try{
        if(backlog) this.config = await api("/api/config/backlog", "POST", { path: backlog });
        if(skills)  this.config = await api("/api/config/skills",  "POST", { path: skills });
        if(file){
          const fd = new FormData();
          fd.append("logo", file);
          const res = await fetch("/api/config/logo", { method:"POST", body: fd });
          if(!res.ok) throw new Error((await res.text()) || "The logo could not be saved.");
          this.config = await res.json();
          this.logoStamp = Date.now();
        }
        await this.load();
        this.goBoard();
        this.toast("Changes saved");
      }catch(e){ this.err.form = e.message || "Those settings could not be saved."; }
      finally{ this.saving = false; }
    },

    /* ------------------------------------------------------------- logo -- */
    get hasLogo(){ return !!this.config.logoPath; },
    // The filename is stable, so without a cache-buster the browser keeps showing the old image.
    get logoSrc(){ return this.config.logoPath + "?v=" + this.logoStamp; },
    get logoClass(){ return this.hasLogo ? "has-logo" : ""; },
    get logoLabel(){ return this.hasLogo ? "Overview" : "Set a logo"; },
    get logoHint(){
      return this.hasLogo
        ? "Choose a file to replace it. PNG, JPG, SVG or WebP, up to 2 MB."
        : "PNG, JPG, SVG or WebP, up to 2 MB. Shown in the top-left corner.";
    },
    // With a logo set the slot behaves like any site logo and goes home; while it is still the
    // empty "+" placeholder its only useful job is to take you somewhere you can set one.
    /**
     * The logo is a real <a href="/">, so the browser handles ctrl-click, middle-click and
     * "open in new tab" natively. A plain left click is taken over here instead, because the
     * board is already loaded and swapping the view beats a full round trip.
     */
    logoNav(e){
      if(!e || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || e.button) return;
      e.preventDefault();
      this.goBoard();
    },

    async removeLogo(){
      try{
        this.config = await api("/api/config/logo", "DELETE");
        this.toast("Logo removed");
      }catch(e){ this.err.form = e.message || "The logo could not be removed."; }
    },

    /* ------------------------------------------------------------ toast -- */
    get toastClass(){ return this.toastOn ? "show" : ""; },
    toast(text){
      this.toastText = text;
      this.toastOn = true;
      clearTimeout(this.toastTimer);
      this.toastTimer = setTimeout(() => { this.toastOn = false; }, 1600);
    },
  }));
});
