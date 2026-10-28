// Shell: host bridge, DOM helpers, project state, navigation, command palette.
"use strict";

// ---- Host bridge (JSON-RPC over WebView2 postMessage) ----
const host = window.chrome?.webview;
let rpcSeq = 0;
const pending = new Map();
host?.addEventListener("message", e => {
  const m = e.data;
  if (m?.automation) { runAutomation(m.automation); return; }
  if (m?.catalogProgress) { App.onCatalogProgress?.(m.catalogProgress); return; }
  const p = pending.get(m?.id);
  if (!p) return;
  pending.delete(m.id);
  m.ok ? p.resolve(m.result) : p.reject(new Error(m.error));
});
function rpc(cmd, args = {}) {
  if (!host) return Promise.reject(new Error("Uygulama dışında açıldı."));
  return new Promise((resolve, reject) => {
    const id = ++rpcSeq;
    pending.set(id, { resolve, reject });
    host.postMessage({ id, cmd, args });
  });
}

// ---- DOM helpers ----
// Element.append flattens nested arrays and skips null/false, so views can be written as
// `el.append(cond && x, list.map(...))`. Only this page's own code runs here (editors live in iframes).
{
  const nativeAppend = Element.prototype.append;
  Element.prototype.append = function (...children) {
    return nativeAppend.apply(this, children.flat(Infinity).filter(c => c != null && c !== false)
      .map(c => c instanceof Node ? c : String(c)));
  };
}
function h(tag, attrs, ...children) {
  const el = document.createElement(tag);
  if (attrs) for (const [k, v] of Object.entries(attrs)) {
    if (v == null || v === false) continue;
    if (k === "class") el.className = v;
    else if (k === "style" && typeof v === "object")
      for (const [sk, sv] of Object.entries(v)) sk.startsWith("--") ? el.style.setProperty(sk, sv) : (el.style[sk] = sv);
    else if (k.startsWith("on")) el.addEventListener(k.slice(2).toLowerCase(), v);
    else if (k === "html") el.innerHTML = v;
    else if (k in el && k !== "list" && typeof v !== "string") el[k] = v;
    else el.setAttribute(k, v === true ? "" : v);
  }
  append(el, children);
  return el;
}
function append(el, children) {
  for (const c of children.flat(Infinity)) {
    if (c == null || c === false) continue;
    el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  }
  return el;
}
const $ = (sel, root = document) => root.querySelector(sel);
function clear(el) { el.replaceChildren(); return el; }

const SVGNS = "http://www.w3.org/2000/svg";
function s(tag, attrs, ...children) {
  const el = document.createElementNS(SVGNS, tag);
  if (attrs) for (const [k, v] of Object.entries(attrs)) if (v != null) el.setAttribute(k, v);
  for (const c of children.flat(Infinity)) if (c != null) el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  return el;
}

// Lucide-style line icons (24×24, stroke).
const ICONS = {
  home: '<path d="M3 10.5 12 3l9 7.5"/><path d="M5 9.5V21h14V9.5"/><path d="M10 21v-6h4v6"/>',
  calc: '<rect x="5" y="3" width="14" height="18" rx="2"/><path d="M8 7h8M8 11h2M12 11h2M16 11h0M8 15h2M12 15h2M8 18h2M12 18h2M16 15v3"/>',
  wall: '<rect x="3" y="4" width="18" height="16" rx="1"/><path d="M3 9h18M3 14h18M9 4v5M15 9v5M9 14v6"/>',
  receipt: '<path d="M5 3h14v18l-2.5-1.5L14 21l-2-1.5L10 21l-2.5-1.5L5 21z"/><path d="M9 8h6M9 12h6M9 16h3"/>',
  cube: '<path d="m12 3 8 4.5v9L12 21l-8-4.5v-9z"/><path d="m4 7.5 8 4.5 8-4.5M12 12v9"/>',
  drawing: '<path d="M3 21h18"/><path d="M5 17 15 7l3 3L8 20H5z"/><path d="m13 9 3 3"/>',
  settings: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  folder: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
  file: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/>',
  trash: '<path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/>',
  up: '<path d="m6 15 6-6 6 6"/>',
  down: '<path d="m6 9 6 6 6-6"/>',
  x: '<path d="M6 6l12 12M18 6 6 18"/>',
  check: '<path d="m5 12 5 5L20 7"/>',
  alert: '<path d="M12 3 2 20h20z"/><path d="M12 10v4M12 17h0"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7.5h0"/>',
  pipe: '<path d="M3 8h8a4 4 0 0 1 4 4v9"/><path d="M3 14h5a1 1 0 0 1 1 1v6"/><path d="M3 6v10M13 21h4"/>',
  wind: '<path d="M3 8h11a3 3 0 1 0-3-3"/><path d="M3 12h15a3 3 0 1 1-3 3"/><path d="M3 16h7"/>',
  droplet: '<path d="M12 3s6 6.5 6 11a6 6 0 0 1-12 0c0-4.5 6-11 6-11z"/>',
  tank: '<rect x="6" y="5" width="12" height="15" rx="6"/><path d="M12 2v3M6 12.5h12"/>',
  flame: '<path d="M12 3c1 4 5 5.5 5 10a5 5 0 0 1-10 0c0-2.5 1.5-4 2.5-5 0 2 1 3 2 3 0-3-1-5.5.5-8z"/>',
  thermo: '<path d="M14 14.8V5a2 2 0 1 0-4 0v9.8a4 4 0 1 0 4 0z"/><path d="M12 9v7"/>',
  swap: '<path d="M7 4 3 8l4 4"/><path d="M3 8h14"/><path d="m17 20 4-4-4-4"/><path d="M21 16H7"/>',
  excel: '<rect x="4" y="3" width="16" height="18" rx="2"/><path d="m9 8 6 8M15 8l-6 8"/>',
  pdf: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5M9 13h6M9 17h4"/>',
  save: '<path d="M5 3h11l3 3v13a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"/><path d="M7 3v5h8V3M7 21v-7h10v7"/>',
  ruler: '<path d="M3 17 17 3l4 4L7 21z"/><path d="m7 13 2 2M10 10l2 2M13 7l2 2"/>',
  area: '<path d="M4 18 8 5l12 4-3 11z"/>',
  layers: '<path d="m12 3 9 5-9 5-9-5z"/><path d="m3 13 9 5 9-5"/>',
  maximize: '<path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"/>',
  download: '<path d="M12 4v11M7 10l5 5 5-5M5 20h14"/>',
  search: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/>',
  link: '<path d="M10 14a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1"/><path d="M14 10a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1"/>',
  copy: '<rect x="8" y="8" width="13" height="13" rx="2"/><path d="M16 8V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h3"/>',
  refresh: '<path d="M20 11a8 8 0 0 0-14.5-4.5L3 9"/><path d="M3 4v5h5M4 13a8 8 0 0 0 14.5 4.5L21 15"/><path d="M21 20v-5h-5"/>',
};
function icon(name) {
  const el = document.createElementNS(SVGNS, "svg");
  el.setAttribute("viewBox", "0 0 24 24");
  el.setAttribute("class", "i");
  el.innerHTML = ICONS[name] ?? "";
  return el;
}

// ---- Numbers (Turkish formatting; inputs accept "1.234,5" and "1234.5") ----
const TR = "tr-TR";
function fmt(n, d = 2) {
  if (n == null || !isFinite(n)) return "—";
  return Number(n).toLocaleString(TR, { minimumFractionDigits: d, maximumFractionDigits: d });
}
function fmtAuto(n, sig = 4) {
  if (n == null || !isFinite(n)) return "—";
  const a = Math.abs(n);
  const d = a === 0 ? 0 : a >= 1000 ? 0 : a >= 100 ? 1 : a >= 10 ? 2 : a >= 1 ? 3 : Math.min(6, sig - Math.floor(Math.log10(a)) - 1);
  return fmt(n, Math.max(0, d));
}
const money = n => fmt(n, 2) + " ₺";
function parseNum(v) {
  if (typeof v === "number") return v;
  if (v == null) return NaN;
  let t = String(v).trim().replace(/\s/g, "");
  if (t === "") return NaN;
  if (t.includes(",")) t = t.replace(/\./g, "").replace(",", ".");
  return Number(t);
}
const trLower = t => String(t ?? "").toLocaleLowerCase(TR);
function debounce(fn, ms) {
  let t;
  return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); };
}
const uid = () => crypto.randomUUID().replace(/-/g, "");
const clone = o => JSON.parse(JSON.stringify(o));
const todayIso = () => new Date().toISOString().slice(0, 10);
const thisMonth = () => new Date().toISOString().slice(0, 7);
function fmtDate(d) {
  const x = d instanceof Date ? d : new Date(d);
  return isNaN(x) ? "" : x.toLocaleDateString(TR, { day: "numeric", month: "long", year: "numeric" });
}
function fmtMonth(m) {
  if (!m) return "—";
  const [y, mo] = m.split("-").map(Number);
  return new Date(y, mo - 1, 1).toLocaleDateString(TR, { month: "long", year: "numeric" });
}

// ---- Form controls bound to an object ----
// numField(obj, key, label, {unit, hint, onChange}) edits obj[key] as a number (null when empty).
function numField(obj, key, label, opt = {}) {
  const input = h("input", {
    class: "input" + (opt.cls ? " " + opt.cls : ""), inputmode: "decimal",
    value: obj[key] == null ? "" : String(obj[key]).replace(".", ","),
    placeholder: opt.placeholder ?? "",
  });
  input.addEventListener("input", () => {
    const n = parseNum(input.value);
    const ok = input.value.trim() === "" || isFinite(n);
    input.classList.toggle("invalid", !ok);
    if (!ok) return;
    obj[key] = input.value.trim() === "" ? null : n;
    opt.onChange?.(obj[key]);
  });
  if (opt.bare) return input;
  const control = opt.unit ? h("div", { class: "input-unit" }, input, h("span", { class: "unit" }, opt.unit)) : input;
  return h("div", { class: "field" }, label && h("label", null, label), control, opt.hint && h("div", { class: "hint" }, opt.hint));
}
function textField(obj, key, label, opt = {}) {
  const input = h(opt.multiline ? "textarea" : "input", {
    class: "input", value: obj[key] ?? "", placeholder: opt.placeholder ?? "", type: opt.type ?? "text",
    rows: opt.multiline ? 3 : null,
  });
  input.addEventListener("input", () => { obj[key] = input.value; opt.onChange?.(input.value); });
  if (opt.bare) return input;
  return h("div", { class: "field" }, label && h("label", null, label), input, opt.hint && h("div", { class: "hint" }, opt.hint));
}
function selectField(obj, key, label, options, opt = {}) {
  const val = x => String(x ?? "");
  const sel = h("select", { class: "select" },
    options.map(o => h("option", { value: val(o.value), selected: val(obj[key]) === val(o.value) }, o.label)));
  sel.addEventListener("change", () => {
    const o = options.find(x => val(x.value) === sel.value);
    obj[key] = o ? o.value : sel.value;
    opt.onChange?.(obj[key]);
  });
  if (opt.bare) return sel;
  return h("div", { class: "field" }, label && h("label", null, label), sel, opt.hint && h("div", { class: "hint" }, opt.hint));
}
function stat(k, v, unit, cls) {
  return h("div", { class: "stat" + (cls ? " " + cls : "") }, h("div", { class: "k" }, k), h("div", { class: "v" }, v, unit && h("small", null, unit)));
}
function note(text, kind = "", ic = kind === "ok" ? "check" : kind ? "alert" : "info") {
  return h("div", { class: "note " + kind }, icon(ic), h("div", null, text));
}
function btn(label, onclick, opt = {}) {
  return h("button", { class: "btn " + (opt.cls ?? ""), onclick, title: opt.title, disabled: opt.disabled },
    opt.icon && icon(opt.icon), label);
}

// ---- Toast / modal / confirm ----
function toast(text, kind = "") {
  const t = h("div", { class: "toast " + kind }, text);
  $("#toasts").append(t);
  setTimeout(() => t.remove(), kind === "bad" ? 6000 : 3200);
}
function fail(e) { console.error(e); toast(e?.message ?? String(e), "bad"); }

let overlayClose = null;
function showOverlay(content, onClose) {
  const ov = $("#overlay");
  clear(ov).append(content);
  ov.hidden = false;
  overlayClose = onClose ?? null;
  ov.onmousedown = e => { if (e.target === ov) closeOverlay(); };
}
function closeOverlay() {
  const ov = $("#overlay");
  if (ov.hidden) return;
  ov.hidden = true;
  clear(ov);
  const f = overlayClose; overlayClose = null;
  f?.();
}
function modal(title, body, actions = [], opt = {}) {
  const m = h("div", { class: "card modal" + (opt.wide ? " wide" : "") },
    h("div", { class: "card-head" }, h("h2", null, title), h("button", { class: "btn ghost icon", onclick: closeOverlay }, icon("x"))),
    h("div", { class: "card-body" }, body),
    actions.length > 0 && h("div", { class: "modal-foot" }, actions));
  showOverlay(m, opt.onClose);
  setTimeout(() => m.querySelector("input, select")?.focus(), 30);
  return m;
}
function confirmBox(title, text, okLabel = "Evet", danger = false) {
  return new Promise(resolve => {
    let answered = false;
    modal(title, h("p", null, text), [
      btn("Vazgeç", () => closeOverlay()),
      btn(okLabel, () => { answered = true; closeOverlay(); }, { cls: danger ? "primary danger" : "primary" }),
    ], { onClose: () => resolve(answered) });
  });
}

// ---- App ----
const App = {
  pages: [],
  pageById: {},
  current: null,
  project: null,       // { path, data }
  settings: {},
  info: {},
  commands: [],

  register(page) {
    this.pages.push(page);
    this.pageById[page.id] = page;
  },
  command(cmd) { this.commands.push(cmd); },

  async start() {
    applyTheme("system");
    try {
      [this.info, this.settings] = await Promise.all([rpc("app.info"), rpc("settings.load")]);
    } catch (e) { fail(e); }
    this.settings ??= {};
    applyTheme(this.settings.theme ?? "system");
    $("#version").textContent = "Sürüm " + (this.info.version ?? "?");
    this.buildNav();
    $("#search-btn").onclick = () => openPalette();
    $("#project-pill").onclick = () => this.go("home");
    document.addEventListener("keydown", e => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") { e.preventDefault(); openPalette(); }
      else if (e.key === "Escape") closeOverlay();
      else if (e.ctrlKey && e.key.toLowerCase() === "s") { e.preventDefault(); this.saveNow(); }
    });
    const start = this.info.startProject ?? this.settings.lastProject;
    if (start) await this.openProject(start, true);
    this.renderProjectPill();
    this.go("home");
    host?.postMessage({ automationReady: true });
  },

  buildNav() {
    const nav = clear($("#nav"));
    let group = null;
    for (const p of this.pages) {
      if (p.group && p.group !== group) { group = p.group; nav.append(h("div", { class: "nav-group" }, group)); }
      if (p.bottom) nav.append(h("div", { class: "nav-spacer" }));
      p.navEl = h("button", { class: "nav-item", onclick: () => this.go(p.id) }, icon(p.icon), p.title);
      nav.append(p.navEl);
      this.command({ title: p.title, hint: "Sayfa", icon: p.icon, keywords: p.keywords ?? "", run: () => this.go(p.id) });
    }
  },

  go(id, arg) {
    const page = this.pageById[id];
    if (!page) return;
    for (const p of this.pages) {
      p.navEl.classList.toggle("active", p === page);
      if (p.el) p.el.hidden = p !== page;
    }
    if (!page.el) {
      page.el = h("section", { class: "page" + (page.full ? " full" : "") });
      $("#pages").append(page.el);
      page.mount?.(page.el);
    }
    this.current = page;
    page.show?.(arg);
    $("#pages").scrollTop = 0;
  },

  // Re-render visible pages that depend on the project (after open/new/close).
  projectChanged() {
    this.renderProjectPill();
    for (const p of this.pages) p.projectChanged?.();
    this.current?.show?.();
  },

  renderProjectPill() {
    const pill = clear($("#project-pill"));
    pill.classList.toggle("none", !this.project);
    pill.append(h("span", { class: "dot" }), this.project ? this.project.data.name : "Proje açık değil");
    $("#save-state").textContent = "";
  },

  async openProject(path, quiet = false) {
    try {
      const r = await rpc("project.load", { path });
      this.setProject(r.path, r.data);
      if (!quiet) toast(`"${r.data.name}" açıldı`);
    } catch (e) {
      if (!quiet) fail(e);
      if (this.settings.lastProject === path) { this.settings.lastProject = null; this.saveSettings(); }
    }
  },
  setProject(path, data) {
    this.project = path ? { path, data: migrateProject(data) } : null;
    this.settings.lastProject = path;
    if (path) {
      const recent = (this.settings.recent ?? []).filter(p => p !== path);
      this.settings.recent = [path, ...recent].slice(0, 12);
    }
    this.saveSettings();
    this.projectChanged();
  },
  async newProject(name, extra = {}) {
    const data = migrateProject({ name, created: new Date().toISOString(), info: extra });
    const path = await rpc("project.save", { data });
    this.setProject(path, data);
    toast(`"${name}" oluşturuldu`);
  },
  closeProject() { this.setProject(null, null); },

  // Every edit calls touch(); the project is saved shortly after the last change.
  touch() {
    if (!this.project) return;
    $("#save-state").textContent = "Değişiklikler kaydediliyor…";
    this._save();
  },
  _save: debounce(() => App.saveNow(), 700),
  async saveNow() {
    if (!this.project) return;
    try {
      await rpc("project.save", { path: this.project.path, data: this.project.data });
      $("#save-state").textContent = "Kaydedildi · " + new Date().toLocaleTimeString(TR, { hour: "2-digit", minute: "2-digit" });
    } catch (e) {
      $("#save-state").textContent = "Kaydedilemedi";
      fail(e);
    }
  },
  saveSettings: debounce(() => rpc("settings.save", { data: App.settings }).catch(fail), 300),

  requireProject(container, what) {
    if (this.project) return false;
    clear(container).append(h("div", { class: "card" }, h("div", { class: "empty" },
      icon("folder"), h("h3", null, "Önce bir proje açın"),
      h("p", null, `${what} proje dosyasına kaydedilir. Yeni bir proje oluşturun ya da var olanı açın.`),
      h("div", { class: "row", style: { justifyContent: "center", marginTop: "14px" } },
        btn("Yeni proje", () => newProjectDialog(), { cls: "primary", icon: "plus" }),
        btn("Proje aç", () => openProjectDialog(), { icon: "folder" })))));
    return true;
  },
};

// Fills in fields added in later versions so old project files keep working.
function migrateProject(d) {
  d.version ??= 1;
  d.name ??= "Adsız proje";
  d.info ??= {};
  d.calcs ??= [];
  d.insulation ??= {};
  d.insulation.climate ??= { insideTemp: 20, insideRh: 65, outsideTemp: null, outsideRh: null };
  d.insulation.zone ??= null;
  d.insulation.elements ??= [];
  d.heatloss ??= { insideTemp: 20, outsideTemp: null, rooms: [] };
  d.job ??= {};
  const j = d.job;
  j.owner ??= d.info.owner ?? ""; j.contractor ??= ""; j.contractNo ??= ""; j.tenderMonth ??= "";
  j.contractAmount ??= 0; j.b ??= 0.9; j.pnDigits ??= 3; j.kdvRate ??= 20;
  j.deductions ??= [{ name: "Damga vergisi", ratePercent: 0.948, base: "tutar" }];
  j.items ??= []; j.lines ??= []; j.periods ??= []; j.indices ??= {};
  return d;
}

function newProjectDialog() {
  const v = { name: "", location: "", owner: "" };
  const create = async () => {
    if (!v.name.trim()) { toast("Proje adı girin", "bad"); return; }
    closeOverlay();
    try { await App.newProject(v.name.trim(), { location: v.location, owner: v.owner }); App.go("home"); }
    catch (e) { fail(e); }
  };
  const body = h("div", { class: "stack" },
    textField(v, "name", "Proje adı", { placeholder: "ör. Kadıköy Okul Binası Mekanik Tesisat" }),
    textField(v, "location", "Yer / şantiye", { placeholder: "İl, ilçe, adres" }),
    textField(v, "owner", "İdare / işveren"),
    h("div", { class: "hint faint small" }, "Proje, Belgeler\\MühendisKit\\Projeler klasörüne kaydedilir."));
  body.addEventListener("keydown", e => { if (e.key === "Enter") create(); });
  modal("Yeni proje", body, [btn("Vazgeç", closeOverlay), btn("Oluştur", create, { cls: "primary" })]);
}
async function openProjectDialog() {
  try {
    const r = await rpc("project.openDialog");
    if (r) { App.setProject(r.path, r.data); toast(`"${r.data.name}" açıldı`); }
  } catch (e) { fail(e); }
}

function applyTheme(theme) {
  if (theme === "light" || theme === "dark") document.documentElement.dataset.theme = theme;
  else delete document.documentElement.dataset.theme;
}

// ---- Command palette (Ctrl+K), in the spirit of AutoCAD's command line ----
function openPalette() {
  const input = h("input", { placeholder: "Komut, hesap veya sayfa ara…" });
  const list = h("div", { class: "palette-list" });
  let items = [], sel = 0;
  const all = () => [
    ...App.commands,
    { title: "Yeni proje", hint: "Proje", icon: "plus", run: newProjectDialog },
    { title: "Proje aç", hint: "Proje", icon: "folder", run: openProjectDialog },
    ...(App.project ? [{ title: "Projeyi kaydet", hint: "Ctrl+S", icon: "save", run: () => App.saveNow() }] : []),
  ];
  const render = () => {
    const q = trLower(input.value).split(/\s+/).filter(Boolean);
    items = all().filter(c => {
      const hay = trLower(c.title + " " + (c.keywords ?? "") + " " + (c.hint ?? ""));
      return q.every(w => hay.includes(w));
    }).slice(0, 40);
    sel = Math.min(sel, Math.max(0, items.length - 1));
    clear(list).append(items.length ? items.map((c, i) =>
      h("div", { class: "palette-item" + (i === sel ? " sel" : ""), onmousemove: () => { if (sel !== i) { sel = i; render(); } }, onclick: () => run(c) },
        icon(c.icon ?? "search"), c.title, h("span", { class: "ph" }, c.hint ?? ""))) : h("div", { class: "empty small" }, "Sonuç yok"));
  };
  const run = c => { closeOverlay(); c.run(); };
  input.addEventListener("input", () => { sel = 0; render(); });
  input.addEventListener("keydown", e => {
    if (e.key === "ArrowDown") { sel = Math.min(sel + 1, items.length - 1); render(); e.preventDefault(); }
    else if (e.key === "ArrowUp") { sel = Math.max(sel - 1, 0); render(); e.preventDefault(); }
    else if (e.key === "Enter" && items[sel]) run(items[sel]);
  });
  showOverlay(h("div", { class: "card palette" }, input, list));
  render();
  input.focus();
}

// ---- Printable reports → PDF ----
async function printReport(title, body, opt = {}) {
  const root = clear($("#print-root"));
  root.append(
    h("div", { class: "report-head" },
      h("h1", null, title),
      h("div", null, [App.project?.data.name, App.project?.data.info?.location].filter(Boolean).join(" · ")),
      h("div", { class: "small" }, fmtDate(new Date()))),
    body,
    h("div", { class: "report-foot" }, "MühendisKit ile hazırlanmıştır. Hesaplar, girilen değerlere ve belirtilen standart yöntemlerine dayanır; sorumluluk hesabı onaylayan mühendistedir."));
  try {
    const path = await rpc("pdf.save", { name: opt.fileName ?? title, landscape: !!opt.landscape });
    if (path) toast("PDF kaydedildi");
  } catch (e) { fail(e); }
  finally { clear(root); }
}

// ---- Automation (MCP server → host → here) ----
// Each handler gets the args object and returns a JSON-able value; errors go back to Claude as text.
const PROJECT_SECTIONS = ["info", "calcs", "insulation", "heatloss", "job"];
const AUTOMATION = {
  "ui.go": a => { App.go(a.page); return { page: a.page }; },
  "ui.state": () => ({ page: App.current?.id, project: App.project ? { path: App.project.path, name: App.project.data.name } : null }),
  "project.open": async a => { await App.openProject(a.path); if (!App.project) throw new Error("Proje açılamadı."); return { path: App.project.path, name: App.project.data.name }; },
  "project.create": async a => { await App.newProject(a.name, { location: a.location ?? "", owner: a.owner ?? "" }); return { path: App.project.path }; },
  "project.get": a => {
    if (!App.project) throw new Error("Açık proje yok.");
    const d = App.project.data;
    return a.section ? d[a.section] : { path: App.project.path, ...d };
  },
  "project.update": async a => {
    if (!App.project) throw new Error("Açık proje yok.");
    if (!PROJECT_SECTIONS.includes(a.section) && a.section !== "name") throw new Error("Bölüm şunlardan biri olmalı: name, " + PROJECT_SECTIONS.join(", "));
    App.project.data[a.section] = a.value;
    migrateProject(App.project.data);
    await App.saveNow();
    App.projectChanged();
    return { saved: App.project.path };
  },
  "hak.period": a => rpc("hak.period", { job: jobForHost(requireJob()), no: a.no }),
  "hak.export": a => rpc("hak.export", { job: jobForHost(requireJob()), no: a.no, name: App.project.data.name, path: a.path }),
  "cad2d.open": async a => { App.go("cad"); await App.pageById.cad.open(a.path); return Cad2d.call("info"); },
  "cad2d.save": async a => {
    App.go("cad");
    const { dxf } = await Cad2d.call("dxf");
    const path = await rpc("cad.save", { dxf, path: a.path });
    App.pageById.cad.file = { path, name: path.split(/[\\/]/).pop() }; App.pageById.cad.setTitle();
    return { path };
  },
  "cad2d.quantities": async () => { App.go("cad"); const { dxf } = await Cad2d.call("dxf"); return rpc("cad.analyze", { dxf }); },
  "cad3d.import": async a => {
    App.go("model3d");
    const f = await rpc("cad.pick", { path: a.path });
    return Cad3d.call("import", { url: f.url, name: f.name });
  },
  "cad3d.export": async a => {
    App.go("model3d");
    const { base64 } = await Cad3d.call("export", { format: a.format, ids: a.ids });
    return { path: await rpc("file.saveBase64", { base64, path: a.path }) };
  },
};
function requireJob() {
  if (!App.project) throw new Error("Açık proje yok.");
  return App.project.data.job;
}
async function runAutomation({ id, cmd, args }) {
  const reply = r => host.postMessage({ automationReply: id, ...r });
  try {
    let fn = AUTOMATION[cmd];
    // cad2d.<cmd> / cad3d.<cmd>: straight to the editor bridges (script, api, preview, fit, new, tools, call, guide, macro…)
    if (!fn && cmd.startsWith("cad2d.")) fn = a => { App.go("cad"); return Cad2d.call(cmd.slice(6), a); };
    if (!fn && cmd.startsWith("cad3d.")) fn = a => { App.go("model3d"); return Cad3d.call(cmd.slice(6), a); };
    if (!fn) throw new Error("Bilinmeyen otomasyon komutu: " + cmd);
    reply({ ok: true, result: (await fn(args ?? {})) ?? null });
  } catch (e) {
    reply({ ok: false, error: e?.message ?? String(e) });
  }
}
