const STATUSES = [
  {label:"Not Yet Started", emoji:"⬜", cls:"st-nys"},
  {label:"Under Review",    emoji:"🔍", cls:"st-rev"},
  {label:"Refined",         emoji:"✨", cls:"st-ref"},
  {label:"In Progress",     emoji:"🔄", cls:"st-prog"},
  {label:"Vendor Test",     emoji:"🧪", cls:"st-test"},
  {label:"Done",            emoji:"✅", cls:"st-done"},
  {label:"On Hold",         emoji:"⏸", cls:"st-hold"},
];
const TC_STATUSES = [
  {label:"Not Run", emoji:"⬜", cls:"st-nys"},
  {label:"Passed",  emoji:"✅", cls:"st-done"},
  {label:"Failed",  emoji:"❌", cls:"st-fail"},
];
const clsFor = (label, set=STATUSES) => (set.find(s=>s.label===label)||{cls:"st-nys"}).cls;

let board = null;
let route = {view:"board", code:null};
const app = document.getElementById("main");
const overlay = document.getElementById("overlay");

document.getElementById("btnSync").innerHTML =
  `<span class="ic" style="display:inline-flex"><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="23 4 23 10 17 10"></polyline><polyline points="1 20 1 14 7 14"></polyline><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path></svg></span><span class="lbl">Reload from file</span>`;
document.getElementById("btnStage").innerHTML =
  `<span class="ic" style="display:inline-flex"><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="6" cy="6" r="2.2"></circle><circle cx="6" cy="18" r="2.2"></circle><circle cx="18" cy="8" r="2.2"></circle><path d="M6 8.2v7.6"></path><path d="M18 10.2a6 6 0 0 1-6 6H8.5"></path></svg></span><span class="lbl">Stage in git</span>`;

let config = { backlogPath:null, skillsPath:null, logoPath:null, isDemo:false };

async function loadConfig(){
  try{
    config = await api("/api/config");
    renderLogo();
  }catch(e){ /* non-fatal — board already rendered independently */ }
}
function renderLogo(){
  const slot = el("logoSlot"), inner = el("logoInner");
  if(!slot || !inner) return;
  const has = !!config.logoPath;
  // With a logo set, the slot drops its dashed placeholder styling: someone's logo should be
  // shown exactly as they supplied it, never dimmed or framed by us.
  slot.classList.toggle("has-logo", has);
  slot.setAttribute("aria-label", has ? "Go to Overview" : "Set a logo");
  slot.setAttribute("title", has ? "Overview" : "Set a logo");
  // Cache-buster: the filename is stable, so the browser would otherwise show the previous image.
  inner.innerHTML = has
    ? `<img class="logoimg" src="${esc(config.logoPath)}?v=${Date.now()}" alt="">`
    : "+";
}

/* ---------- form pages ---------- */
const el = id => document.getElementById(id);

/** Maps a URL path to a page section. The board is everything else. */
const PAGES = {
  "/configure": "page-configure",
  "/add-epic":  "page-add-epic",
  "/add-story": "page-add-story",
  "/edit-epic": "page-edit-epic",
};

/** The epic /edit-epic is editing. Held here rather than in the URL so a stale link can't point
 *  the rename form at an epic that no longer exists. */
let editingEpic = null;

function showPage(path, { push = true, epicNumber = null } = {}){
  const sectionId = PAGES[path];
  el("pages").hidden = !sectionId;
  el("main").hidden = !!sectionId;
  Object.values(PAGES).forEach(id => { el(id).hidden = id !== sectionId; });

  if(sectionId){
    clearFormErrors(el(sectionId).querySelector("form"));
    if(sectionId === "page-configure") fillConfigForm();
    if(sectionId === "page-add-epic")  fillEpicForm();
    if(sectionId === "page-add-story") fillStoryForm();
    if(sectionId === "page-edit-epic" && !fillEditEpicForm(epicNumber)) return goBoard();
    // Move focus to the first control so keyboard users land inside the form.
    const first = el(sectionId).querySelector("input, select");
    if(first) first.focus();
  }
  if(push && location.pathname !== path) history.pushState({}, "", path);
  renderSidebar();
  syncMobileNav();
}

/** Hides every form page and puts the board back. Safe to call when no page is open.
 *  Deliberately does NOT touch the URL — the first render happens before the router has read
 *  location.pathname, so rewriting it here would discard a direct visit to /add-story. */
function leaveFormPages(){
  el("pages").hidden = true;
  el("main").hidden = false;
  Object.values(PAGES).forEach(id => { el(id).hidden = true; });
}

function goBoard({ push = true } = {}){
  route = { view:"board" };
  leaveFormPages();
  if(push && location.pathname !== "/") history.pushState({}, "", "/");
  render();
}

/** Routes the current URL. Called on load and on browser back/forward. */
function routeFromUrl({ push = false } = {}){
  PAGES[location.pathname] ? showPage(location.pathname, { push }) : goBoard({ push });
}
window.addEventListener("popstate", () => routeFromUrl());

/* ---------- form validation ---------- */
function clearFormErrors(form){
  if(!form) return;
  form.querySelectorAll(".field-err").forEach(p => { p.hidden = true; p.textContent = ""; });
  form.querySelectorAll("[aria-invalid]").forEach(i => i.removeAttribute("aria-invalid"));
  const summary = form.querySelector(".form-err");
  if(summary){ summary.hidden = true; summary.textContent = ""; }
}

function setFieldError(form, fieldId, message){
  const p = form.querySelector(`[data-err-for="${fieldId}"]`);
  const input = el(fieldId);
  if(p){ p.textContent = message; p.hidden = false; }
  if(input) input.setAttribute("aria-invalid", "true");
}

function setFormError(form, message){
  const summary = form.querySelector(".form-err");
  if(summary){ summary.textContent = message; summary.hidden = false; }
}

/**
 * Runs the browser's own constraint validation, then rewrites the messages so they say what to
 * do rather than what failed. Returns true when the form is good to send.
 */
function validate(form, custom = {}){
  clearFormErrors(form);
  let firstBad = null;

  for(const input of form.querySelectorAll("input, select")){
    let message = "";
    if(input.validity.valueMissing)      message = custom[input.id]?.required || "Fill this in.";
    else if(input.validity.patternMismatch) message = custom[input.id]?.pattern || "Use the format shown below the field.";
    else if(input.validity.rangeUnderflow || input.validity.rangeOverflow)
      message = custom[input.id]?.range || `Enter a number between ${input.min} and ${input.max}.`;
    else if(input.validity.tooLong)      message = `Keep this under ${input.maxLength} characters.`;
    else {
      const extra = custom[input.id]?.check?.(input.value.trim());
      if(extra) message = extra;
    }
    if(message){
      setFieldError(form, input.id, message);
      firstBad = firstBad || input;
    }
  }
  if(firstBad) firstBad.focus();
  return !firstBad;
}

async function api(path, method="GET", body){
  const res = await fetch(path, {method, headers:body?{"Content-Type":"application/json"}:{}, body:body?JSON.stringify(body):undefined});
  if(!res.ok) throw new Error(await res.text() || res.status);
  return res.json();
}
async function load(){
  board = await api("/api/board");
  // Epics start expanded so the whole backlog is visible at a glance; collapsing is opt-in.
  if(!sidebar.seeded){
    board.epics.forEach(e => sidebar.expandedEpics.add(e.number));
    sidebar.seeded = true;
  }
  render();
}
function toast(msg){ const t=document.getElementById("toast"); t.textContent=msg; t.classList.add("show"); setTimeout(()=>t.classList.remove("show"),1400); }
function esc(s){ return (s||"").replace(/[&<>"]/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;"}[c])); }
const allStories = () => board.epics.flatMap(e=>e.stories.map(s=>({...s, epic:e.number})));

/* ---------- shared helpers (styles copied verbatim from the design mockup) ---------- */
let sidebar = { collapsed:false, expandedEpics:new Set(), drawerOpen:false, seeded:false };
// False until the router has read the initial URL, so the first render can't overwrite it.
let booted = false;

/** Maps a status label to its --st-* token pair. st-fail is the one that breaks the pattern. */
function statusVars(label, set=STATUSES){
  const cls = clsFor(label, set);
  return cls === "st-fail"
    ? { bg:"var(--fail-bg)", fg:"var(--fail-fg)" }
    : { bg:`var(--${cls}-bg)`, fg:`var(--${cls}-fg)` };
}

/** Epic holding the highest-activity story — same rule the summary's 📍 marker uses. */
function currentEpicNumber(){
  const rank = {"In Progress":5,"Vendor Test":4,"Under Review":3,"Refined":2,"Not Yet Started":1,"Done":0,"On Hold":0};
  let best = -1, num = null;
  for(const e of board.epics){
    const r = Math.max(0, ...e.stories.map(s => rank[s.status.label] ?? 0));
    if(r > best){ best = r; num = e.number; }
  }
  return best >= 1 ? num : null;
}

const ICON_GRID = `<rect x="3" y="3" width="7" height="7" rx="1"></rect><rect x="14" y="3" width="7" height="7" rx="1"></rect><rect x="3" y="14" width="7" height="7" rx="1"></rect><rect x="14" y="14" width="7" height="7" rx="1"></rect>`;
const ICON_TAG  = `<path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z"></path><line x1="7" y1="7" x2="7.01" y2="7"></line>`;
const ICON_PENCIL = `<path d="M12 20h9"></path><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z"></path>`;
const ICON_TRASH  = `<polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path><line x1="10" y1="11" x2="10" y2="17"></line><line x1="14" y1="11" x2="14" y2="17"></line>`;

/** An icon + label action button. The label hides itself on narrow screens; the title stays. */
function actionBtn(attr, label, icon, extraClass = ""){
  return `<button class="actbtn ${extraClass}" ${attr} title="${label}" aria-label="${label}">
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${icon}</svg>
    <span class="lbl">${label}</span></button>`;
}

/* ---------- sidebar ---------- */
function renderSidebar(){
  const el = document.getElementById("sidebar");
  const mobile = window.innerWidth <= 900;
  // The drawer only exists in the 769–900px band, where there is no bottom bar to replace it.
  // Below that the sidebar is hidden outright, so a drawer left open on resize must not linger.
  if(window.innerWidth <= 768 && sidebar.drawerOpen) setDrawerState(false);
  const open = mobile ? sidebar.drawerOpen : !sidebar.collapsed;
  el.setAttribute("style", mobile
    ? `flex:none;box-sizing:border-box;position:fixed;top:59px;left:0;bottom:0;z-index:65;background:var(--surface);overflow-x:hidden;overflow-y:auto;transition:width .38s cubic-bezier(.22,.61,.36,1);will-change:width;border-right:1px solid var(--border);`
      + (open ? `width:min(300px,86vw);box-shadow:10px 0 30px rgba(10,20,40,.22);` : `width:0;box-shadow:none;`)
    : `flex:none;box-sizing:border-box;position:sticky;top:59px;align-self:stretch;max-height:calc(100vh - 59px);overflow-x:hidden;overflow-y:auto;transition:width .34s cubic-bezier(.22,.61,.36,1);will-change:width;border-right:1px solid var(--border);`
      + `width:${open ? "240px" : "64px"};`);
  el.innerHTML = open ? sidebarFullHtml() : railHtml();
}

function railHtml(){
  const isBoard = route.view === "board";
  const isRel = route.view === "releases" || route.view === "release";
  const btn = (active, attr, title, icon) => {
    const bd = active ? "var(--brand)" : "var(--border)";
    const bg = active ? "var(--st-prog-bg)" : "var(--surface)";
    const fg = active ? "var(--brand)" : "var(--muted)";
    return `<button class="mt-hb" ${attr} title="${title}" style="width:36px;height:36px;flex:none;border-radius:9px;border:1px solid ${bd};background:${bg};color:${fg};cursor:pointer;display:flex;align-items:center;justify-content:center">
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${icon}</svg></button>`;
  };
  let h = `<div style="width:64px;box-sizing:border-box;display:flex;flex-direction:column;align-items:center;gap:8px;padding:14px 0">`;
  h += `<button class="mt-hb" data-sb-toggle aria-label="Expand sidebar" aria-expanded="false" title="Expand sidebar" style="width:36px;height:36px;flex:none;border-radius:9px;border:1px solid var(--border);background:var(--surface);color:var(--muted);cursor:pointer;display:flex;align-items:center;justify-content:center">
    <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"></polyline></svg></button>`;
  h += btn(isBoard, `data-nav="board" aria-label="Overview"`, "Overview — all epics", ICON_GRID);
  h += btn(isRel, `data-nav="releases" aria-label="By release"`, "By release", ICON_TAG);
  h += `<div style="width:24px;height:1px;background:var(--border);margin:0"></div>`;
  const cur = currentEpicNumber();
  for(const e of board.epics){
    // isActive = viewing this epic; hl also covers "this epic owns the open story" (mockup's split).
    const isActive = route.view==="epic" && route.epicNumber===e.number;
    const hl = isActive || (route.view==="story" && e.stories.some(s=>s.code===route.code));
    h += `<button class="mt-hb" data-epic="${e.number}" title="${esc(e.title)} · ${e.stories.length} ${e.stories.length===1?"story":"stories"}" style="width:48px;height:42px;flex:none;border-radius:10px;background:${isActive?"var(--st-prog-bg)":"var(--surface)"};color:${hl?"var(--brand)":"var(--muted)"};border:1px solid ${hl?"var(--brand)":"var(--border)"};font-family:'IBM Plex Mono',monospace;cursor:pointer;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:0;position:relative">
      <span style="font-size:7.5px;font-weight:600;letter-spacing:.08em;opacity:.7;line-height:1">EPIC</span>
      <span style="font-size:13px;font-weight:700;line-height:1.1">${e.number}</span>
      ${e.number===cur?`<span style="position:absolute;top:-3px;right:-3px;width:8px;height:8px;border-radius:999px;background:var(--brand);border:1.5px solid var(--surface)"></span>`:""}
    </button>`;
  }
  return h + `</div>`;
}

function sidebarFullHtml(){
  const isBoard = route.view === "board";
  const isRel = route.view === "releases" || route.view === "release";
  const navRow = (active, attr, label, icon) => `<button class="mt-row" ${attr} style="display:flex;align-items:center;gap:10px;width:100%;text-align:left;padding:8px 10px;border:none;border-left:2px solid ${active?"var(--brand)":"transparent"};border-radius:0 9px 9px 0;background:${active?"var(--st-prog-bg)":"transparent"};cursor:pointer;font-family:'IBM Plex Sans',sans-serif;margin-bottom:4px">
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="${active?"var(--brand)":"var(--muted)"}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="flex:none">${icon}</svg>
      <span style="font-size:13px;font-weight:${active?700:600};color:${active?"var(--brand)":"var(--text)"}">${label}</span></button>`;

  // Below the mobile breakpoint the bottom bar already carries Overview and By release, so the
  // drawer drops them and is purely the epic browser — two menus offering the same two
  // destinations is just a choice made twice.
  const bottomBarOwnsNav = window.innerWidth <= 768;

  let h = `<div style="width:240px;box-sizing:border-box;padding:14px 10px;display:flex;flex-direction:column;gap:2px">`;
  h += `<div style="display:flex;justify-content:flex-start;align-items:center;gap:8px;padding-bottom:8px">
    <button class="mt-hb" data-sb-toggle aria-label="Collapse sidebar" aria-expanded="true" title="Collapse sidebar" style="width:36px;height:36px;flex:none;border-radius:9px;border:1px solid var(--border);background:var(--surface);color:var(--muted);cursor:pointer;display:flex;align-items:center;justify-content:center">
      <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"></polyline></svg></button></div>`;
  if(bottomBarOwnsNav){
    h += `<div style="font-size:10.5px;font-weight:700;letter-spacing:.08em;color:var(--faint);padding:2px 10px 8px">EPICS</div>`;
  }else{
    h += navRow(isBoard, `data-nav="board"`, "Overview", ICON_GRID);
    h += navRow(isRel, `data-nav="releases"`, "By release", ICON_TAG);
    h += `<div style="height:1px;background:var(--border);margin:2px 6px 8px"></div>`;
  }

  const cur = currentEpicNumber();
  for(const e of board.epics){
    const open = sidebar.expandedEpics.has(e.number);
    // The mockup keeps these separate: isActive tints the row and fills the chip; hl only
    // colours the accent and text, so opening a story highlights its epic without filling it.
    const isActive = route.view==="epic" && route.epicNumber===e.number;
    const hl = isActive || (route.view==="story" && e.stories.some(s=>s.code===route.code));
    h += `<div style="display:flex;align-items:stretch;margin-bottom:1px">
      <button class="mt-row" data-epic="${e.number}" style="flex:1;min-width:0;display:flex;align-items:center;gap:10px;text-align:left;padding:7px 8px;border:none;border-left:2px solid ${hl?"var(--brand)":"transparent"};border-radius:0 9px 9px 0;background:${isActive?"var(--st-prog-bg)":"transparent"};cursor:pointer;font-family:'IBM Plex Sans',sans-serif">
        <span style="width:24px;height:24px;border-radius:7px;background:${isActive?"var(--brand)":"var(--surface2)"};color:${isActive?"#fff":"var(--muted)"};border:1px solid ${isActive?"var(--brand)":"var(--border)"};font-family:'IBM Plex Mono',monospace;font-size:11.5px;font-weight:700;display:flex;align-items:center;justify-content:center;flex:none">${e.number}</span>
        <span style="flex:1;display:flex;flex-direction:column;gap:1px;min-width:0">
          <span style="display:flex;align-items:center;gap:6px"><span style="font-size:13px;font-weight:${hl?700:600};color:${hl?"var(--brand)":"var(--text)"};line-height:1.25;text-wrap:pretty">${esc(e.title)}</span>${e.number===cur?`<span style="font-size:8.5px;font-weight:700;letter-spacing:.05em;color:var(--brand);background:var(--st-prog-bg);border-radius:999px;padding:1px 5px;white-space:nowrap;flex:none">CURRENT</span>`:""}</span>
          <span style="font-size:10.5px;color:var(--faint)">${e.stories.length} ${e.stories.length===1?"story":"stories"}</span>
        </span>
      </button>
      <button class="mt-chev" data-toggle-epic="${e.number}" aria-label="Toggle stories" aria-expanded="${open}" style="width:26px;flex:none;border:none;background:none;color:var(--faint);cursor:pointer;font-size:10px;display:flex;align-items:center;justify-content:center">${open?"▾":"▸"}</button>
    </div>`;
    if(open && e.stories.length){
      h += `<div style="display:flex;flex-direction:column;margin:0 0 6px 21px;border-left:1px solid var(--border);padding-left:4px">`;
      for(const s of e.stories){
        const sActive = route.view==="story" && route.code===s.code;
        const v = statusVars(s.status.label);
        h += `<button class="mt-row" data-story="${s.code}" title="${esc(s.title)}" style="display:flex;align-items:center;gap:8px;width:100%;text-align:left;background:${sActive?"var(--st-prog-bg)":"transparent"};border:none;border-left:2px solid ${sActive?"var(--brand)":"transparent"};border-radius:0 7px 7px 0;padding:5.5px 9px;cursor:pointer;font-family:'IBM Plex Sans',sans-serif;margin-left:-5px">
          <span style="width:7px;height:7px;border-radius:2px;background:${v.bg};border:1px solid ${v.fg};flex:none"></span>
          <span style="font-family:'IBM Plex Mono',monospace;font-size:10px;font-weight:600;color:var(--muted);flex:none">${s.code}</span>
          <span style="font-size:12px;font-weight:${sActive?700:500};color:${sActive?"var(--brand)":"var(--text)"};line-height:1.25;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(s.title)}</span>
        </button>`;
      }
      h += `</div>`;
    }
  }
  return h + `</div>`;
}

/* ---------- shared view partials ---------- */
/* The rule and its accent span the full width, but only the label itself is clickable — an
   invisible hit area stretching across empty space is confusing to aim at. */
function epicHeaderHtml(e){
  return `<div class="epic-head">
    <button class="mt-epich" data-epic="${e.number}">
      <span class="epic-badge"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>EPIC</span>
      <span class="epic-num">${e.number}</span>
      <span class="epic-name">${esc(e.title)}</span>
    </button>
    <span class="epic-count">${e.stories.length} ${e.stories.length===1?"story":"stories"}</span>
  </div>`;
}

function storyRowHtml(s, context){
  const v = statusVars(s.status.label);
  return `<div class="story-row">
    <button class="mt-open" data-story="${s.code}" title="Open story">
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="row-mark"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>
      <span class="row-code">${s.code}</span>
      <span class="row-name">${esc(s.title)}</span>
    </button>
    ${context?`<span class="row-ctx">${esc(context)}</span>`:""}
    <span class="row-sp"></span>
    <button class="mt-badge row-badge" data-pick="story" data-code="${s.code}" style="background:${v.bg};color:${v.fg}"><span>${s.status.emoji}</span><span>${esc(s.status.label)}</span><span class="caret">▾</span></button>
  </div>`;
}

function epicBlockHtml(e){
  return `<div style="display:flex;flex-direction:column;gap:2px">${epicHeaderHtml(e)}`
    + (e.stories.length
        ? e.stories.map(s=>storyRowHtml(s)).join("")
        : `<p style="color:var(--faint);font-size:13px;padding:8px 4px 8px 24px;margin:0">No stories yet.</p>`)
    + `</div>`;
}

/* ---------- rendering ---------- */
function render(){
  overlay.innerHTML = "";
  // Any board navigation leaves a form page — otherwise the sidebar looks live but does nothing.
  // Once the app has started, that also means the URL should no longer name a form page.
  leaveFormPages();
  if(booted && PAGES[location.pathname]) history.pushState({}, "", "/");
  // Navigating from the drawer should close it, or the choice stays hidden behind the overlay.
  if(sidebar.drawerOpen && window.innerWidth <= 900){
    sidebar.drawerOpen = false;
    document.getElementById("scrim").classList.remove("show");
    document.getElementById("hamburger").setAttribute("aria-expanded", "false");
  }
  renderSidebar();
  syncMobileNav();
  if(route.view === "story")    return renderStory();
  if(route.view === "epic")     return renderEpic();
  if(route.view === "releases") return renderReleases();
  if(route.view === "release")  return renderRelease();
  renderBoard();
}

/** Groups stories by release tag, ordered by the roadmap; unscheduled last. */
function releaseGroups(){
  const order = board.roadmapVersions || [];
  const map = new Map();
  for(const e of board.epics)
    for(const s of e.stories){
      const key = s.release || "Unscheduled";
      if(!map.has(key)) map.set(key, []);
      map.get(key).push({story:s, epicName:e.title});
    }
  return [...map.keys()].sort((a,b)=>{
    if(a==="Unscheduled") return 1;
    if(b==="Unscheduled") return -1;
    const ia = order.indexOf(a), ib = order.indexOf(b);
    return (ia<0?999:ia) - (ib<0?999:ib);
  }).map(tag => ({tag, items:map.get(tag)}));
}

function releaseGroupHtml(g, clickable){
  const tag = `<span style="display:inline-flex;align-items:center;gap:5px;background:var(--surface2);color:var(--muted);border:1px solid var(--border);border-radius:5px;padding:2px 8px;font-family:'IBM Plex Mono',monospace;font-size:11px;font-weight:700;flex:none"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${ICON_TAG}</svg>${esc(g.tag)}</span>
    <span style="font-size:12.5px;color:var(--faint)">${g.items.length} ${g.items.length===1?"story":"stories"}</span>`;
  const head = clickable
    ? `<button class="mt-epich" data-release="${esc(g.tag)}" style="display:flex;align-items:center;gap:10px;padding:6px 4px 6px 12px;border:none;border-left:3px solid var(--brand);border-bottom:1px solid var(--border);background:none;cursor:pointer;font-family:'IBM Plex Sans',sans-serif;text-align:left">${tag}</button>`
    : `<div style="display:flex;align-items:center;gap:10px;padding:6px 4px 6px 12px;border-left:3px solid var(--brand);border-bottom:1px solid var(--border)">${tag}</div>`;
  return `<div style="display:flex;flex-direction:column;gap:2px">${head}`
    + g.items.map(i=>storyRowHtml(i.story, i.epicName)).join("") + `</div>`;
}

function renderReleases(){
  const groups = releaseGroups();
  app.innerHTML = `<section style="display:flex;flex-direction:column;gap:26px;max-width:720px">`
    + (groups.length ? groups.map(g=>releaseGroupHtml(g, true)).join("")
                     : `<p style="color:var(--faint);font-size:13px">No releases found.</p>`)
    + `</section>`;
}

function renderRelease(){
  const g = releaseGroups().find(x=>x.tag===route.release);
  if(!g){ route = {view:"releases"}; return renderReleases(); }
  app.innerHTML = breadcrumbHtml([
      {label:"Overview", attr:`data-nav="board"`},
      {label:"By release", attr:`data-nav="releases"`},
      {label:g.tag}
    ])
    + `<section style="display:flex;flex-direction:column;gap:16px;max-width:720px">${releaseGroupHtml(g, false)}</section>`;
}

/** Story cards on the epic view are always fully open — a card that both summarises and
 *  hides detail behind a click reads as ambiguous, so there is no collapse here. */
function storyCardHtml(s){
  const v = statusVars(s.status.label);
  const tDone = s.tasks.filter(t=>t.done).length;
  const cPass = s.testCases.filter(t=>t.status.label==="Passed").length;
  const cFail = s.testCases.filter(t=>t.status.label==="Failed").length;

  let h = `<div style="background:var(--surface);border:1px solid var(--border);border-radius:11px;box-shadow:var(--shadow);display:flex;flex-direction:column">
    <div style="padding:13px 15px 14px;display:flex;flex-direction:column;gap:9px">
      <div style="display:flex;align-items:center;gap:8px">
        <span style="font-family:'IBM Plex Mono',monospace;font-size:12px;font-weight:600;color:var(--muted);white-space:nowrap">${s.code}</span>
        <div style="flex:1"></div>
        ${s.release?`<span style="font-family:'IBM Plex Mono',monospace;font-size:10.5px;font-weight:600;color:var(--muted);border:1px solid var(--border);border-radius:5px;padding:1px 6px;background:var(--surface2);white-space:nowrap">${esc(s.release)}</span>`:""}
      </div>
      <div class="mt-open" data-story="${s.code}" title="Open story detail" style="font-size:14.5px;font-weight:600;line-height:1.3;text-wrap:pretty;cursor:pointer;width:fit-content">${esc(s.title)}</div>
      <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap">
        <button class="mt-badge" data-pick="story" data-code="${s.code}" style="display:flex;align-items:center;gap:7px;background:${v.bg};color:${v.fg};border:1px solid transparent;border-radius:999px;padding:3.5px 11px;font-family:'IBM Plex Sans',sans-serif;font-size:12.5px;font-weight:600;cursor:pointer;white-space:nowrap;flex:none"><span>${s.status.emoji}</span><span>${esc(s.status.label)}</span><span style="font-size:8.5px;opacity:.75">▾</span></button>
      </div>
      <div style="display:flex;gap:16px">
        <div style="flex:1;display:flex;flex-direction:column;gap:4px">
          <div style="display:flex;justify-content:space-between;font-size:11px;color:var(--muted)"><span>Tasks</span><span style="font-variant-numeric:tabular-nums;font-weight:600;white-space:nowrap">${tDone}/${s.tasks.length}</span></div>
          <div style="height:5px;border-radius:999px;background:var(--st-nys-bg);overflow:hidden"><div style="height:100%;width:${pct(tDone,s.tasks.length)}%;background:var(--brand);border-radius:999px"></div></div>
        </div>
        <div style="flex:1;display:flex;flex-direction:column;gap:4px">
          <div style="display:flex;justify-content:space-between;font-size:11px;color:var(--muted)"><span>Test cases</span><span style="font-variant-numeric:tabular-nums;font-weight:600;white-space:nowrap">${cPass}/${s.testCases.length}${cFail?`<span style="color:var(--fail-fg)"> · ${cFail} ✗</span>`:""}</span></div>
          <div style="height:5px;border-radius:999px;background:var(--st-nys-bg);overflow:hidden;display:flex"><div style="height:100%;width:${pct(cPass,s.testCases.length)}%;background:var(--st-done-fg)"></div><div style="height:100%;width:${pct(cFail,s.testCases.length)}%;background:var(--fail-fg)"></div></div>
        </div>
      </div>
    </div>`;

  h += `<div style="border-top:1px solid var(--border);background:var(--surface2);border-radius:0 0 11px 11px;padding:16px 18px;display:flex;flex-direction:column;gap:14px">
      <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:10px 28px">
        <div>
          <div style="font-size:11px;font-weight:700;letter-spacing:.08em;color:var(--faint);margin-bottom:8px">TASKS · ${tDone}/${s.tasks.length}</div>
          ${s.tasks.length ? s.tasks.map(t=>`
            <div class="lrow">
              <button class="mt-box" data-task="${t.id}" data-code="${s.code}" title="Toggle done" style="width:21px;height:21px;flex:none;border-radius:6px;border:1.5px solid ${t.done?"var(--st-done-fg)":"var(--border)"};background:${t.done?"var(--st-done-bg)":"var(--surface)"};color:${t.done?"var(--st-done-fg)":"transparent"};font-size:12px;font-weight:700;line-height:1;cursor:pointer;display:flex;align-items:center;justify-content:center">${t.done?"✓":""}</button>
              <span style="font-size:13px;color:${t.done?"var(--faint)":"var(--text)"};flex:1">${esc(t.text)}</span>
            </div>`).join("") : `<div style="font-size:12.5px;color:var(--faint);padding:6px 0">No tasks yet.</div>`}
        </div>
        <div>
          <div style="font-size:11px;font-weight:700;letter-spacing:.08em;color:var(--faint);margin-bottom:8px">TEST CASES · ${cPass}/${s.testCases.length}</div>
          ${s.testCases.length ? s.testCases.map(t=>{
            const tv = statusVars(t.status.label, TC_STATUSES);
            return `<div class="lrow">
              <button class="mt-badge" data-pick="tc" data-code="${s.code}" data-tc="${t.id}" style="display:flex;align-items:center;gap:5px;background:${tv.bg};color:${tv.fg};border:1px solid transparent;border-radius:999px;padding:2px 9px;font-family:'IBM Plex Sans',sans-serif;font-size:11.5px;font-weight:600;cursor:pointer;min-width:64px;justify-content:center;white-space:nowrap;flex:none">${esc(t.status.label)} <span style="font-size:8.5px;opacity:.75">▾</span></button>
              <span style="font-size:13px;flex:1">${esc(t.description)}</span>
            </div>`;
          }).join("") : `<div style="font-size:12.5px;color:var(--faint);padding:6px 0">No test cases yet.</div>`}
        </div>
      </div>
    </div>`;
  return h + `</div>`;
}

function renderEpic(){
  const e = board.epics.find(x=>x.number===route.epicNumber);
  if(!e){ route = {view:"board"}; return renderBoard(); }
  const isCur = e.number === currentEpicNumber();

  let h = breadcrumbHtml([
    {label:"Overview", attr:`data-nav="board"`},
    {label:`${e.number} — ${e.title}`}
  ]);

  h += `<section style="display:flex;flex-direction:column;gap:12px">
    <div style="display:flex;align-items:center;gap:10px 12px;flex-wrap:wrap;padding-left:12px;border-left:3px solid var(--brand)">
      <h2 style="margin:0;font-size:16px;font-weight:700">${e.number} — ${esc(e.title)}</h2>
      ${isCur?`<span title="Highest-activity epic, derived from story statuses" style="display:flex;align-items:center;gap:6px;background:var(--st-prog-bg);color:var(--st-prog-fg);border:1px solid var(--st-prog-fg);border-radius:999px;padding:2px 10px;font-size:11px;font-weight:700;letter-spacing:.06em;white-space:nowrap">
        <span style="width:6px;height:6px;border-radius:999px;background:var(--st-prog-fg);display:inline-block"></span><span>CURRENT</span></span>`:""}
      <span style="font-size:12.5px;color:var(--faint);white-space:nowrap">${e.stories.length} ${e.stories.length===1?"story":"stories"}</span>
      <div style="flex:1;min-width:0"></div>
      <div class="rowacts">
        ${actionBtn(`data-edit-epic="${e.number}"`, "Rename epic", ICON_PENCIL)}
        ${actionBtn(`data-del-epic="${e.number}"`, "Delete epic", ICON_TRASH, "danger")}
      </div>
    </div>`;

  // Cards are always open, so they stack full width rather than tiling in a grid.
  h += e.stories.length
    ? `<div style="display:flex;flex-direction:column;gap:12px;max-width:1000px">${e.stories.map(storyCardHtml).join("")}</div>`
    : `<div style="background:var(--surface2);border:1px dashed var(--border);border-radius:11px;padding:16px 18px;font-size:13px;color:var(--muted);max-width:1000px">No stories defined yet — candidate scope only.</div>`;

  app.innerHTML = h + `</section>`;
}

function breadcrumbHtml(parts){
  return `<nav aria-label="Breadcrumb" style="display:flex;align-items:center;gap:8px;font-size:12.5px;color:var(--muted);flex-wrap:wrap;margin-bottom:16px">`
    + parts.map((p,i)=>{
      const sep = i ? `<span aria-hidden="true" style="color:var(--faint)">›</span>` : "";
      return sep + (p.attr
        ? `<button class="mt-crumb" ${p.attr} style="background:none;border:none;color:var(--brand);font-family:'IBM Plex Sans',sans-serif;font-size:12.5px;font-weight:600;cursor:pointer;padding:0">${esc(p.label)}</button>`
        : `<span aria-current="page" style="font-weight:600;color:var(--text)">${esc(p.label)}</span>`);
    }).join("") + `</nav>`;
}

function renderBoard(){
  app.innerHTML = `<section style="display:flex;flex-direction:column;gap:26px;max-width:720px">`
    + board.epics.map(epicBlockHtml).join("") + `</section>`;
}

function renderStory(){
  const s = allStories().find(x=>x.code===route.code);
  if(!s){ route={view:"board"}; return renderBoard(); }
  const e = board.epics.find(x=>x.stories.some(y=>y.code===s.code));
  const v = statusVars(s.status.label);
  // Reset first: without this, opening a story with no description would inherit the previous
  // story's file path and the editor would save into the wrong file.
  skillDraft = { path: s.skillPath || null, original: "" };
  const tDone = s.tasks.filter(t=>t.done).length;
  const cPass = s.testCases.filter(t=>t.status.label==="Passed").length;
  const cFail = s.testCases.filter(t=>t.status.label==="Failed").length;

  let h = `<div style="max-width:860px;display:flex;flex-direction:column;gap:20px">`;

  h += breadcrumbHtml([
    {label:"Overview", attr:`data-nav="board"`},
    {label:`${e.number} — ${e.title}`, attr:`data-epic="${e.number}"`},
    {label:s.title}
  ]).replace(`margin-bottom:16px`, `margin-bottom:0`);

  h += `<div style="display:flex;align-items:center;gap:10px">
    <span style="font-family:'IBM Plex Mono',monospace;font-size:13px;font-weight:600;color:var(--muted)">${s.code}</span>
    ${s.release?`<span style="font-family:'IBM Plex Mono',monospace;font-size:10.5px;font-weight:600;color:var(--muted);border:1px solid var(--border);border-radius:5px;padding:1px 7px;background:var(--surface2)">${esc(s.release)}</span>`:""}
  </div>`;

  h += `<h1 style="margin:0;font-size:26px;font-weight:700;line-height:1.2;text-wrap:pretty">${esc(s.title)}</h1>`;

  // Status, then the two things you do to a story. Keeping Edit up here — beside the title rather
  // than inside the description panel — is the difference between finding it and hunting for it.
  h += `<div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap">
    <button class="mt-badge" data-pick="story" data-code="${s.code}" style="display:flex;align-items:center;gap:7px;background:${v.bg};color:${v.fg};border:1px solid transparent;border-radius:999px;padding:5px 13px;font-family:'IBM Plex Sans',sans-serif;font-size:13px;font-weight:600;cursor:pointer;white-space:nowrap"><span>${s.status.emoji}</span><span>${esc(s.status.label)}</span><span style="font-size:9px;opacity:.75">▾</span></button>
    <div style="flex:1;min-width:0"></div>
    <div class="rowacts">
      ${actionBtn(`data-skill-edit`, "Edit description", ICON_PENCIL)}
      ${actionBtn(`data-del-story="${s.code}"`, "Delete story", ICON_TRASH, "danger")}
    </div>
  </div>`;

  const testPct = pct(cPass, s.testCases.length), failPct = pct(cFail, s.testCases.length);
  h += `<div style="display:flex;gap:26px;flex-wrap:wrap;background:var(--surface);border:1px solid var(--border);border-radius:11px;box-shadow:var(--shadow);padding:16px 20px">
    <div style="min-width:150px;flex:1;display:flex;flex-direction:column;gap:5px">
      <div style="display:flex;justify-content:space-between;font-size:12px;color:var(--muted)"><span>Tasks</span><span style="font-variant-numeric:tabular-nums;font-weight:600">${tDone}/${s.tasks.length}</span></div>
      <div style="height:6px;border-radius:999px;background:var(--st-nys-bg);overflow:hidden"><div style="height:100%;width:${pct(tDone,s.tasks.length)}%;background:var(--brand);border-radius:999px"></div></div>
    </div>
    <div style="min-width:150px;flex:1;display:flex;flex-direction:column;gap:5px">
      <div style="display:flex;justify-content:space-between;font-size:12px;color:var(--muted)"><span>Test cases</span><span style="font-variant-numeric:tabular-nums;font-weight:600">${cPass}/${s.testCases.length}${cFail?`<span style="color:var(--fail-fg)"> · ${cFail} ✗</span>`:""}</span></div>
      <div style="height:6px;border-radius:999px;background:var(--st-nys-bg);overflow:hidden;display:flex"><div style="height:100%;width:${testPct}%;background:var(--st-done-fg)"></div><div style="height:100%;width:${failPct}%;background:var(--fail-fg)"></div></div>
    </div>
  </div>`;

  h += `<div id="skillCard" style="background:var(--surface);border:1px solid var(--border);border-radius:11px;box-shadow:var(--shadow);padding:20px 22px;display:flex;flex-direction:column;gap:14px">
    <div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap">
      <h2 style="margin:0;font-size:14px;font-weight:700;letter-spacing:.02em">Description</h2>
      <div style="flex:1;min-width:0"></div>
      <div id="skillActions" style="flex:none"></div>
    </div>
    <div id="skillBody" style="font-size:14px;line-height:1.65">${
      s.skillPath
        ? `<span style="color:var(--faint)">Loading…</span>`
        : `<div class="md-empty">
             <p>This story has no description yet. Mini Tracker keeps each description in its own
                markdown file so it lives with your project, not in a database.</p>
             <button class="btn primary sm" data-write-desc="${s.code}">Write a description</button>
           </div>`
    }</div>
  </div>`;

  h += `<div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:14px 26px;background:var(--surface);border:1px solid var(--border);border-radius:11px;box-shadow:var(--shadow);padding:20px 22px">
    <div>
      <div style="font-size:11px;font-weight:700;letter-spacing:.08em;color:var(--faint);margin-bottom:10px">TASKS · ${tDone}/${s.tasks.length}</div>
      ${s.tasks.length ? s.tasks.map(t=>`
        <div class="lrow">
          <button class="mt-box" data-task="${t.id}" title="Toggle done" style="width:21px;height:21px;flex:none;border-radius:6px;border:1.5px solid ${t.done?"var(--st-done-fg)":"var(--border)"};background:${t.done?"var(--st-done-bg)":"var(--surface)"};color:${t.done?"var(--st-done-fg)":"transparent"};font-size:12px;font-weight:700;line-height:1;cursor:pointer;display:flex;align-items:center;justify-content:center">${t.done?"✓":""}</button>
          <span style="font-size:13.5px;color:${t.done?"var(--faint)":"var(--text)"};flex:1">${esc(t.text)}</span>
        </div>`).join("")
        : `<div style="font-size:12.5px;color:var(--faint);padding:6px 0">No tasks yet.</div>`}
    </div>
    <div>
      <div style="font-size:11px;font-weight:700;letter-spacing:.08em;color:var(--faint);margin-bottom:10px">TEST CASES · ${cPass}/${s.testCases.length}</div>
      ${s.testCases.length ? s.testCases.map(t=>{
        const tv = statusVars(t.status.label, TC_STATUSES);
        return `<div class="lrow">
          <button class="mt-badge" data-pick="tc" data-code="${s.code}" data-tc="${t.id}" style="display:flex;align-items:center;gap:5px;background:${tv.bg};color:${tv.fg};border:1px solid transparent;border-radius:999px;padding:2px 9px;font-family:'IBM Plex Sans',sans-serif;font-size:11.5px;font-weight:600;cursor:pointer;min-width:64px;justify-content:center;white-space:nowrap;flex:none">${esc(t.status.label)} <span style="font-size:8.5px;opacity:.75">▾</span></button>
          <span style="font-size:13.5px;flex:1">${esc(t.description)}</span>
        </div>`;
      }).join("")
        : `<div style="font-size:12.5px;color:var(--faint);padding:6px 0">No test cases yet.</div>`}
    </div>
  </div>`;

  app.innerHTML = h + `</div>`;
  if(s.skillPath) loadSkill(s.skillPath);
}

/* ---------- SKILL.md viewer / editor ---------- */
let skillDraft = { path:null, original:"" };
// Set when a description was just created, so it opens ready to type in rather than showing an
// empty read view the person then has to click Edit on.
let openEditorOnLoad = false;

async function loadSkill(path){
  const body = el("skillBody"), actions = el("skillActions");
  try{
    const res = await fetch(`/api/skill?path=${encodeURIComponent(path)}`);
    const text = await res.text();
    if(!res.ok){
      body.innerHTML = `<span style="color:var(--faint)">${esc(text || "This description file could not be opened.")}</span>`;
      actions.innerHTML = "";
      return;
    }
    skillDraft = { path, original: text };
    if(openEditorOnLoad){ openEditorOnLoad = false; showSkillEdit(); }
    else showSkillRead();
  }catch{
    body.innerHTML = `<span style="color:var(--faint)">This description file could not be opened.</span>`;
    actions.innerHTML = "";
  }
}

/**
 * Renders markdown to HTML. Everything is escaped first, so the transforms below only ever see
 * safe text — a SKILL.md can never inject markup. Deliberately small: it covers what a SKILL.md
 * actually contains rather than trying to be a complete CommonMark implementation.
 */
function renderMarkdown(src){
  // Only these schemes may become a link. Without this check a SKILL.md containing
  // [x](javascript:…) would render a working script URL — escaping the text is not enough,
  // because the danger is in the href, not the characters.
  const safeHref = url => /^(https?:\/\/|mailto:|#|\/|\.{0,2}\/)/i.test(url) ? url : null;

  const inline = t => esc(t)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/(^|[^*])\*([^*\n]+)\*/g, '$1<em>$2</em>')
    .replace(/\[([^\]]+)\]\(([^)\s]+)\)/g, (m, text, url) => {
      const href = safeHref(url);
      return href ? `<a href="${href}" rel="noopener noreferrer" target="_blank">${text}</a>` : m;
    });

  const lines = String(src ?? "").replace(/\r\n?/g, "\n").split("\n");
  const out = [];
  let i = 0;

  // YAML frontmatter, shown as a quiet metadata block rather than as a stray "---" rule.
  if(lines[0] === "---"){
    const end = lines.indexOf("---", 1);
    if(end > 0){
      out.push(`<div class="md-meta">${lines.slice(1, end).map(l => esc(l)).join("<br>")}</div>`);
      i = end + 1;
    }
  }

  const flushList = (buf, ordered) => {
    if(!buf.length) return;
    out.push(`<${ordered?"ol":"ul"}>${buf.map(x => `<li>${inline(x)}</li>`).join("")}</${ordered?"ol":"ul"}>`);
    buf.length = 0;
  };

  let list = [], listOrdered = false, para = [];
  const flushPara = () => { if(para.length){ out.push(`<p>${inline(para.join(" "))}</p>`); para = []; } };

  for(; i < lines.length; i++){
    const line = lines[i];

    if(line.startsWith("```")){                                   // fenced code
      flushPara(); flushList(list, listOrdered);
      const body = [];
      for(i++; i < lines.length && !lines[i].startsWith("```"); i++) body.push(lines[i]);
      out.push(`<pre><code>${esc(body.join("\n"))}</code></pre>`);
      continue;
    }
    if(/^\|.*\|\s*$/.test(line)){                                  // table
      flushPara(); flushList(list, listOrdered);
      const rows = [];
      for(; i < lines.length && /^\|.*\|\s*$/.test(lines[i]); i++) rows.push(lines[i]);
      i--;
      const cells = r => r.trim().replace(/^\||\|$/g, "").split("|").map(c => c.trim());
      const isSep = r => /^[\s|:-]+$/.test(r);
      const head = cells(rows[0]);
      const bodyRows = rows.slice(isSep(rows[1] ?? "") ? 2 : 1).map(cells);
      out.push(`<div class="md-tablewrap"><table><thead><tr>${head.map(c => `<th>${inline(c)}</th>`).join("")}</tr></thead>`
        + `<tbody>${bodyRows.map(r => `<tr>${r.map(c => `<td>${inline(c)}</td>`).join("")}</tr>`).join("")}</tbody></table></div>`);
      continue;
    }

    const h = line.match(/^(#{1,6})\s+(.*)$/);
    if(h){ flushPara(); flushList(list, listOrdered);
      const lvl = Math.min(h[1].length + 1, 6);                    // h1 in the file is an h2 here
      out.push(`<h${lvl}>${inline(h[2])}</h${lvl}>`); continue; }

    if(/^\s*[-*]\s+/.test(line)){
      flushPara(); if(listOrdered) flushList(list, true);
      listOrdered = false; list.push(line.replace(/^\s*[-*]\s+/, "")); continue;
    }
    if(/^\s*\d+\.\s+/.test(line)){
      flushPara(); if(!listOrdered) flushList(list, false);
      listOrdered = true; list.push(line.replace(/^\s*\d+\.\s+/, "")); continue;
    }
    if(/^>\s?/.test(line)){
      flushPara(); flushList(list, listOrdered);
      out.push(`<blockquote>${inline(line.replace(/^>\s?/, ""))}</blockquote>`); continue;
    }
    if(/^(---|\*\*\*|___)\s*$/.test(line)){
      flushPara(); flushList(list, listOrdered); out.push("<hr>"); continue;
    }
    if(line.trim() === ""){ flushPara(); flushList(list, listOrdered); continue; }

    // A wrapped bullet: markdown lets a list item run onto the next line without indentation, and
    // that continuation belongs to the item. Treating it as a new paragraph is what dropped the
    // tail of long bullets below the list.
    if(list.length && !para.length){ list[list.length - 1] += " " + line.trim(); continue; }

    flushList(list, listOrdered);
    para.push(line.trim());
  }
  flushPara(); flushList(list, listOrdered);
  return out.join("");
}

/** The file backing this description, shown quietly — it is provenance, not a heading. */
function skillPathNote(){
  return skillDraft.path
    ? `<span class="mono" style="font-size:11.5px;color:var(--faint);overflow-wrap:anywhere">${esc(skillDraft.path)}</span>`
    : "";
}

function showSkillRead(){
  el("skillBody").innerHTML = `<div class="md">${renderMarkdown(skillDraft.original)}</div>`;
  el("skillActions").innerHTML = skillPathNote();
}

function showSkillEdit(){
  el("skillBody").innerHTML =
    `<textarea id="skillEditor" spellcheck="false" aria-label="Description, in markdown"
       style="width:100%;min-height:340px;resize:vertical;font-family:'IBM Plex Mono',monospace;font-size:12.5px;line-height:1.6;color:var(--text);background:var(--bg);border:1px solid var(--border);border-radius:9px;padding:14px 16px">${esc(skillDraft.original)}</textarea>
     <p class="hint">Plain markdown. Saved to <span class="mono">${esc(skillDraft.path)}</span>.</p>`;
  el("skillActions").innerHTML =
    `<span style="display:flex;gap:8px"><button class="btn ghost sm" data-skill-cancel>Cancel</button>
     <button class="btn primary sm" data-skill-save>Save</button></span>`;
  el("skillEditor").focus();
}

/**
 * Opens the editor for the story on screen. A story with no description yet gets its markdown file
 * created first — writing one should not require a detour through Configure or the file system.
 */
async function editDescription(code){
  if(skillDraft.path) return showSkillEdit();
  try{
    const res = await fetch(`/api/story/${encodeURIComponent(code)}/skill`, { method:"POST" });
    if(!res.ok) throw new Error(await res.text() || "The description file could not be created.");
    board = (await res.json()).board;
    // The story now carries a path, so the redraw loads the new file — and openEditorOnLoad makes
    // it land in the editor. Doing the load here as well would race with that one.
    openEditorOnLoad = true;
    render();
  }catch(err){
    toast(err.message || "The description file could not be created.");
  }
}

async function saveSkill(){
  const editor = el("skillEditor");
  const content = editor.value;
  const btn = document.querySelector("[data-skill-save]");
  btn.disabled = true;
  try{
    const res = await fetch("/api/skill", {
      method:"POST",
      headers:{ "Content-Type":"application/json" },
      body: JSON.stringify({ path: skillDraft.path, content })
    });
    if(!res.ok) throw new Error(await res.text() || "The file could not be saved.");
    skillDraft.original = content;
    showSkillRead();
    toast("Skill file saved");
  }catch(err){
    toast(err.message || "The file could not be saved.");
    btn.disabled = false;
  }
}

const pct = (a,b)=> b===0?0:Math.round(a/b*100);

async function viewSkill(path){
  try{
    const res = await fetch(`/api/skill?path=${encodeURIComponent(path)}`);
    const text = await res.text();
    if(!res.ok){ toast(text||"SKILL.md not found"); return; }
    modal(`<h2>${esc(path)}</h2><pre style="white-space:pre-wrap;max-height:60vh;overflow:auto;font-family:'IBM Plex Mono',monospace;font-size:12px;background:var(--surface2);border-radius:8px;padding:12px">${esc(text)}</pre>
      <div class="actions"><button class="btn ghost" onclick="overlay.innerHTML=''">Close</button></div>`);
  }catch(e){ toast("Failed to load SKILL.md"); }
}

/* ---------- interactions ---------- */
document.addEventListener("click", async (ev)=>{
  const t = ev.target.closest("[data-pick],[data-story],[data-task],[data-skill],[data-nav],[data-epic],[data-toggle-epic],[data-sb-toggle],[data-release],[data-toggle-story],[data-skill-edit],[data-skill-cancel],[data-skill-save],[data-write-desc],[data-del-story],[data-edit-epic],[data-del-epic]");
  if(ev.target.closest(".pop")) return;
  overlay.querySelectorAll(".pop").forEach(p=>p.remove());
  if(!t) return;

  if(t.dataset.writeDesc){ editDescription(t.dataset.writeDesc); return; }
  if(t.dataset.delStory){ deleteStory(t.dataset.delStory); return; }
  if(t.dataset.editEpic !== undefined){ showPage("/edit-epic", { epicNumber: +t.dataset.editEpic }); return; }
  if(t.dataset.delEpic !== undefined){ deleteEpic(+t.dataset.delEpic); return; }
  if(t.dataset.skillEdit !== undefined){ editDescription(route.code); return; }
  if(t.dataset.skillCancel !== undefined){ showSkillRead(); return; }
  if(t.dataset.skillSave !== undefined){ saveSkill(); return; }
  if(t.dataset.sbToggle !== undefined){ sidebar.collapsed = !sidebar.collapsed; renderSidebar(); return; }
  if(t.dataset.toggleEpic !== undefined){
    const n = +t.dataset.toggleEpic;
    sidebar.expandedEpics.has(n) ? sidebar.expandedEpics.delete(n) : sidebar.expandedEpics.add(n);
    renderSidebar(); return;
  }
  if(t.dataset.nav){ route = t.dataset.nav==="board" ? {view:"board"} : {view:"releases"}; render(); return; }
  if(t.dataset.release){ route = {view:"release", release:t.dataset.release}; render(); return; }
  if(t.dataset.skill){ ev.preventDefault(); viewSkill(t.dataset.skill); return; }
  if(t.dataset.story){ route={view:"story", code:t.dataset.story}; render(); return; }
  if(t.dataset.epic !== undefined){ route={view:"epic", epicNumber:+t.dataset.epic}; render(); return; }

  if(t.dataset.task){
    const code = t.dataset.code
      || allStories().find(s=>s.tasks.some(x=>x.id===t.dataset.task))?.code
      || route.code;
    const wasDone = allStories().find(s=>s.code===code)?.tasks.find(x=>x.id===t.dataset.task)?.done;
    try{ board = await api(`/api/story/${code}/task/${t.dataset.task}`, "POST", {done:!wasDone}); toast("Saved ✓"); render(); }
    catch(e){ toast("Error"); }
    return;
  }

  if(t.dataset.pick==="story"){
    const cur = allStories().find(s=>s.code===t.dataset.code)?.status.label;
    return openPicker(t, STATUSES, async (st)=>{
      board = await api(`/api/story/${t.dataset.code}/status`, "POST", {emoji:st.emoji, label:st.label}); toast("Saved ✓"); render();
    }, cur);
  }
  if(t.dataset.pick==="tc"){
    const cur = allStories().find(s=>s.code===t.dataset.code)
      ?.testCases.find(x=>x.id===t.dataset.tc)?.status.label;
    return openPicker(t, TC_STATUSES, async (st)=>{
      board = await api(`/api/story/${t.dataset.code}/testcase/${t.dataset.tc}`, "POST", {emoji:st.emoji, label:st.label}); toast("Saved ✓"); render();
    }, cur);
  }
});

/**
 * Status picker — markup copied from the mockup. The story variant carries a "SET STATUS"
 * caption and the status emoji; the test-case variant is narrower and drops both.
 */
function openPicker(anchor, set, onPick, currentLabel){
  const isTc = set === TC_STATUSES;
  const r = anchor.getBoundingClientRect();
  const pop = document.createElement("div");
  pop.className = "pop";
  pop.setAttribute("role", "listbox");
  pop.setAttribute("aria-label", "Set status");
  pop.setAttribute("style",
    `position:absolute;z-index:50;min-width:${isTc?"150px":"224px"};background:var(--surface);border:1px solid var(--border);`
    + `border-radius:${isTc?"10px":"11px"};box-shadow:0 8px 28px rgba(10,20,40,.22);padding:${isTc?"5px":"6px"};`);

  const rows = set.map((s,i)=>{
    const v = statusVars(s.label, set);
    const cur = s.label === currentLabel;
    return `<button class="mt-row" role="option" aria-selected="${cur}" data-i="${i}" style="display:flex;align-items:center;gap:${isTc?"8px":"9px"};width:100%;text-align:left;background:${cur?"var(--surface2)":"transparent"};border:none;border-radius:${isTc?"6px":"7px"};padding:${isTc?"5.5px 9px":"6.5px 10px"};font-family:'IBM Plex Sans',sans-serif;font-size:${isTc?"12.5px":"13px"};color:var(--text);cursor:pointer;white-space:nowrap">
      <span style="width:${isTc?"10px":"11px"};height:${isTc?"10px":"11px"};border-radius:${isTc?"3px":"4px"};background:${v.bg};border:1px solid ${v.fg};display:inline-block;flex:none"></span>
      ${isTc?"":`<span>${s.emoji}</span>`}<span style="flex:1">${s.label}</span>
      ${cur?`<span style="color:var(--brand);font-weight:700">✓</span>`:""}
    </button>`;
  }).join("");

  pop.innerHTML = isTc ? rows
    : `<div style="font-size:10.5px;font-weight:700;letter-spacing:.08em;color:var(--faint);padding:4px 10px 6px">SET STATUS</div>${rows}`;

  // Anchored to the badge; flipped left when it would overflow the viewport.
  const width = isTc ? 150 : 224;
  const left = Math.min(r.left + window.scrollX, window.scrollX + document.documentElement.clientWidth - width - 12);
  pop.style.left = Math.max(window.scrollX + 8, left) + "px";
  pop.style.top = (r.bottom + window.scrollY + 6) + "px";

  pop.querySelectorAll("button").forEach(b=> b.onclick = async ()=>{
    pop.remove();
    try{ await onPick(set[+b.dataset.i]); }catch(e){ toast("Error"); }
  });
  overlay.appendChild(pop);
}

/* ---------- header navigation ---------- */
function setAddMenu(open){
  el("addMenu").hidden = !open;
  el("btnAdd").setAttribute("aria-expanded", String(open));
}
el("btnAdd").addEventListener("click", (ev) => {
  ev.stopPropagation();
  setAddMenu(el("addMenu").hidden);
});
// Clicking anywhere else, or pressing Escape, closes the menu — standard menu behaviour.
document.addEventListener("click", (ev) => {
  if(!ev.target.closest(".addwrap")) setAddMenu(false);
});
document.addEventListener("keydown", (ev) => { if(ev.key === "Escape") setAddMenu(false); });

el("btnAddEpic").addEventListener("click",   () => { setAddMenu(false); showPage("/add-epic"); });
el("btnAddStory").addEventListener("click",  () => { setAddMenu(false); showPage("/add-story"); });
el("btnConfigure").addEventListener("click", () => showPage("/configure"));

/* ---------- mobile bottom navigation ---------- */
// The add menu is shared with the desktop pill; on mobile it re-anchors above the bottom bar.
el("mnavAdd").addEventListener("click", (ev) => {
  ev.stopPropagation();
  const opening = el("addMenu").hidden;
  el("addMenu").classList.toggle("mobile", true);
  setAddMenu(opening);
  el("mnavAdd").setAttribute("aria-expanded", String(opening));
});

document.querySelectorAll("[data-mnav]").forEach(btn => {
  btn.addEventListener("click", () => {
    setAddMenu(false);
    switch(btn.dataset.mnav){
      case "board":     goBoard(); break;
      case "releases":  route = { view:"releases" };
                        if(PAGES[location.pathname]) history.pushState({}, "", "/");
                        render(); break;
      case "configure": showPage("/configure"); break;
      case "reload":    load().then(() => toast("Reloaded from file")); break;
    }
  });
});

/** Marks the current destination in the bottom bar, mirroring the sidebar's active state. */
function syncMobileNav(){
  const path = location.pathname;
  const current = PAGES[path] === "page-configure" ? "configure"
    : (route.view === "releases" || route.view === "release") ? "releases"
    : PAGES[path] ? null : "board";
  document.querySelectorAll("[data-mnav]").forEach(b =>
    b.classList.toggle("active", b.dataset.mnav === current));
}
// With a logo set the slot behaves like any site logo and goes home; while it's still the empty
// "+" placeholder its only useful job is to take you somewhere you can set one.
el("logoSlot").addEventListener("click", () => config.logoPath ? goBoard() : showPage("/configure"));

// Cancel buttons and breadcrumbs inside the pages return to the board.
document.querySelectorAll('#pages [data-nav="board"]').forEach(b =>
  b.addEventListener("click", () => goBoard()));

/* ---------- Configure ---------- */
function fillConfigForm(){
  el("cfgBacklog").value = config.backlogPath || "";
  el("cfgSkills").value = config.skillsPath || "";
  el("cfgLogo").value = "";

  // Show the logo that's already set, so this page reflects reality rather than looking empty.
  const has = !!config.logoPath;
  el("logoCurrent").hidden = !has;
  if(has) el("logoCurrentImg").src = `${config.logoPath}?v=${Date.now()}`;
  el("logoHint").textContent = has
    ? "Choose a file to replace it. PNG, JPG, SVG or WebP, up to 2 MB."
    : "PNG, JPG, SVG or WebP, up to 2 MB. Shown in the top-left corner.";
}

el("logoRemove").addEventListener("click", async () => {
  const btn = el("logoRemove");
  btn.disabled = true;
  try{
    config = await api("/api/config/logo", "DELETE");
    renderLogo();
    fillConfigForm();
    toast("Logo removed");
  }catch(err){
    setFormError(el("configForm"), err.message || "The logo could not be removed.");
  }finally{
    btn.disabled = false;
  }
});

el("configForm").addEventListener("submit", async (ev) => {
  ev.preventDefault();
  const form = ev.currentTarget;
  if(!validate(form, {
    cfgBacklog: { check: v => v && !/\.md$/i.test(v) ? "Point this at a .md file, for example C:/projects/my-app/BACKLOG.md." : "" }
  })) return;

  const submit = form.querySelector('button[type="submit"]');
  const data = new FormData(form);
  const backlogPath = (data.get("backlogPath") || "").trim();
  const skillsPath  = (data.get("skillsPath")  || "").trim();
  const logo = data.get("logo");

  submit.disabled = true;
  try{
    if(backlogPath) config = await api("/api/config/backlog", "POST", { path: backlogPath });
    if(skillsPath)  config = await api("/api/config/skills",  "POST", { path: skillsPath });
    if(logo && logo.size > 0){
      const fd = new FormData();
      fd.append("logo", logo);
      const res = await fetch("/api/config/logo", { method:"POST", body: fd });
      if(!res.ok) throw new Error(await res.text() || "The logo could not be saved.");
      config = await res.json();
    }
    renderLogo();
    await load();
    goBoard();
    toast("Changes saved");
  }catch(err){
    setFormError(form, err.message || "Those settings could not be saved.");
  }finally{
    submit.disabled = false;
  }
});

/* ---------- Add epic ---------- */
/** Identifiers are assigned by the app, never typed — the same reasoning as a database key. */
function nextEpicNumber(){
  const used = board ? board.epics.map(e => e.number) : [];
  return used.length ? Math.max(...used) + 1 : 0;
}

function fillEpicForm(){
  el("epicTitle").value = "";
  el("epicNumberPreview").textContent = `Epic ${nextEpicNumber()}`;
}

el("epicForm").addEventListener("submit", async (ev) => {
  ev.preventDefault();
  const form = ev.currentTarget;
  if(!validate(form, { epicTitle: { required: "Give the epic a title." } })) return;

  const submit = form.querySelector('button[type="submit"]');
  const data = new FormData(form);
  submit.disabled = true;
  try{
    board = await api("/api/epic", "POST", {
      number: nextEpicNumber(),
      title: (data.get("title") || "").trim()
    });
    goBoard();
    toast("Epic added");
  }catch(err){
    setFormError(form, err.message || "The epic could not be added.");
  }finally{
    submit.disabled = false;
  }
});

/* ---------- Rename epic ---------- */
/** Returns false when there is nothing to rename, so the caller can fall back to the board. */
function fillEditEpicForm(epicNumber){
  const e = board && board.epics.find(x => x.number === (epicNumber ?? editingEpic));
  if(!e) return false;
  editingEpic = e.number;
  el("editEpicTitle").value = e.title;
  el("editEpicNumber").textContent = `Epic ${e.number}`;
  return true;
}

el("editEpicForm").addEventListener("submit", async (ev) => {
  ev.preventDefault();
  const form = ev.currentTarget;
  if(!validate(form, { editEpicTitle: { required: "Give the epic a title." } })) return;

  const submit = form.querySelector('button[type="submit"]');
  const data = new FormData(form);
  submit.disabled = true;
  try{
    board = await api(`/api/epic/${editingEpic}`, "POST", { title: (data.get("title") || "").trim() });
    route = { view:"epic", epicNumber: editingEpic };
    leaveFormPages();
    if(location.pathname !== "/") history.pushState({}, "", "/");
    render();
    toast("Epic renamed");
  }catch(err){
    setFormError(form, err.message || "The epic could not be renamed.");
  }finally{
    submit.disabled = false;
  }
});

/* ---------- deleting ---------- */
/**
 * Asks before deleting, using the <dialog> declared in index.html. Resolves true only when the
 * delete button was the one that closed it — Escape and Cancel both mean no.
 */
function confirmDelete({ title, body, okLabel }){
  const dlg = el("confirmDialog");
  el("confirmTitle").textContent = title;
  el("confirmBody").textContent = body;
  el("confirmOk").textContent = okLabel;
  el("confirmError").hidden = true;
  return new Promise(resolve => {
    dlg.addEventListener("close", () => resolve(dlg.returnValue === "delete"), { once:true });
    dlg.showModal();
  });
}

async function deleteStory(code){
  const s = allStories().find(x => x.code === code);
  if(!s) return;
  const ok = await confirmDelete({
    title: `Delete ${code}?`,
    body: `"${s.title}" and its tasks and test cases are removed from BACKLOG.md. Its description file is left on disk.`,
    okLabel: "Delete story"
  });
  if(!ok) return;
  try{
    board = await api(`/api/story/${encodeURIComponent(code)}`, "DELETE");
    route = { view:"board" };
    render();
    toast("Story deleted");
  }catch(err){ toast(err.message || "The story could not be deleted."); }
}

async function deleteEpic(number){
  const e = board.epics.find(x => x.number === number);
  if(!e) return;
  const count = e.stories.length;
  const ok = await confirmDelete({
    title: `Delete Epic ${number}?`,
    body: count
      ? `"${e.title}" and its ${count} ${count === 1 ? "story is" : "stories are"} removed from BACKLOG.md. This cannot be undone from here.`
      : `"${e.title}" is removed from BACKLOG.md. It has no stories.`,
    okLabel: count ? `Delete epic and ${count} ${count === 1 ? "story" : "stories"}` : "Delete epic"
  });
  if(!ok) return;
  try{
    board = await api(`/api/epic/${number}`, "DELETE");
    route = { view:"board" };
    render();
    toast("Epic deleted");
  }catch(err){ toast(err.message || "The epic could not be deleted."); }
}

/* ---------- Add user story ---------- */
function nextStoryCode(){
  const codes = board ? allStories().map(s => Number(s.code.replace("US-", ""))) : [];
  const next = codes.length ? Math.max(...codes) + 1 : 1;
  return "US-" + String(next).padStart(2, "0");
}

function fillStoryForm(){
  el("storyEpic").innerHTML = (board ? board.epics : [])
    .map(e => `<option value="${e.number}">${e.number} — ${esc(e.title)}</option>`).join("");
  el("storyTitle").value = "";
  el("storyRelease").value = "";
  el("storySkill").value = "";
  el("storyCodePreview").textContent = nextStoryCode();
}

el("storyForm").addEventListener("submit", async (ev) => {
  ev.preventDefault();
  const form = ev.currentTarget;
  if(!validate(form, {
    storyEpic:  { required: "Choose which epic this story belongs to." },
    storyTitle: { required: "Give the story a title." }
  })) return;

  const submit = form.querySelector('button[type="submit"]');
  const data = new FormData(form);
  submit.disabled = true;
  try{
    board = await api("/api/story", "POST", {
      epicNumber: Number(data.get("epicNumber")),
      code:       nextStoryCode(),
      title:      (data.get("title") || "").trim(),
      release:    (data.get("release") || "").trim(),
      skillPath:  (data.get("skillPath") || "").trim()
    });
    goBoard();
    toast("User story added");
  }catch(err){
    setFormError(form, err.message || "The story could not be added.");
  }finally{
    submit.disabled = false;
  }
});

document.getElementById("btnSync").onclick = ()=>{ load().then(()=>toast("Synced from BACKLOG.md ✓")); };
document.getElementById("btnStage").onclick = async ()=>{
  try{ await api("/api/git/stage","POST"); toast("Staged (git add) ✓"); }
  catch(e){ toast("Stage failed"); }
};
/* ---------- drawer (769–900px only) ---------- */
/** Flips the drawer flag and its chrome. Split from setDrawer so renderSidebar can force it
 *  closed without recursing back into a render. */
function setDrawerState(open){
  sidebar.drawerOpen = open;
  document.getElementById("scrim").classList.toggle("show", open);
  const h = document.getElementById("hamburger");
  h.setAttribute("aria-expanded", String(open));
  h.setAttribute("aria-label", open ? "Hide navigation" : "Show navigation");
}
function setDrawer(open){ setDrawerState(open); renderSidebar(); }
document.getElementById("hamburger").onclick = ()=> setDrawer(!sidebar.drawerOpen);
document.getElementById("scrim").onclick = ()=> setDrawer(false);
// Esc closes the drawer, matching what people expect from an overlay.
document.addEventListener("keydown", (ev)=>{ if(ev.key === "Escape" && sidebar.drawerOpen) setDrawer(false); });
// Crossing the breakpoint changes which sidebar mode applies, so repaint it.
window.addEventListener("resize", ()=>{ if(board) renderSidebar(); });

document.getElementById("btnTheme").onclick = ()=>{
  const cur = document.body.getAttribute("data-theme");
  const next = cur==="dark"?"light":cur==="light"?"dark":(matchMedia("(prefers-color-scheme:dark)").matches?"light":"dark");
  document.body.setAttribute("data-theme", next);
};

// Config loads after the board: first run materializes the demo (and its tracker.config.json entry)
// as a side effect of GET /api/board, so loading config first would race and show blank fields.
// loadConfig still runs even if the board load fails, so Configure remains usable either way.
load()
  .catch(e=> app.innerHTML = `<p style="color:var(--err)">Failed to load board: ${esc(e.message)}</p>`)
  .then(loadConfig)
  .then(()=> { routeFromUrl(); booted = true; });   // honour /configure, /add-epic, /add-story on a direct visit
