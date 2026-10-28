// Çizim (2B): the MLightCAD editor runs in an iframe (wwwroot/cad2d); files and quantities go through the host.
"use strict";

const UNIT_FACTORS = { mm: 0.001, cm: 0.01, m: 1 };
const UNIT_FROM_HEADER = { Millimeters: "mm", Centimeters: "cm", Meters: "m" };

function effectiveTheme() {
  const t = App.settings.theme;
  if (t === "light" || t === "dark") return t;
  return matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

// Promise-based messaging with an editor iframe (same protocol for cad2d and cad3d).
class FrameBridge {
  constructor(prefix) {
    this.prefix = prefix;
    this.seq = 0;
    this.pending = new Map();
    this.readyWaiters = [];
    this.isReady = false;
    this.onEvent = null;
    window.addEventListener("message", e => {
      const m = e.data;
      if (!this.frame || e.source !== this.frame.contentWindow || !m) return;
      if (m.mk === prefix + "-reply") {
        const p = this.pending.get(m.id);
        if (!p) return;
        this.pending.delete(m.id);
        m.ok ? p.resolve(m.result) : p.reject(new Error(m.error));
      } else if (m.mk === prefix + "-event") {
        if (m.type === "ready") { this.isReady = true; this.readyWaiters.splice(0).forEach(r => r()); }
        this.onEvent?.(m);
      }
    });
  }
  attach(frame) { this.frame = frame; this.isReady = false; }
  ready(timeoutMs = 60000) {
    if (this.isReady) return Promise.resolve();
    return new Promise((resolve, reject) => {
      const t = setTimeout(() => reject(new Error("Modül yüklenemedi (zaman aşımı).")), timeoutMs);
      this.readyWaiters.push(() => { clearTimeout(t); resolve(); });
    });
  }
  async call(cmd, args = {}) {
    await this.ready();
    return new Promise((resolve, reject) => {
      const id = ++this.seq;
      this.pending.set(id, { resolve, reject });
      this.frame.contentWindow.postMessage({ mk: this.prefix, id, cmd, args }, "*");
    });
  }
}

const Cad2d = new FrameBridge("cad2d");

App.register({
  id: "cad", title: "Çizim (2B)", icon: "drawing", group: "Çizim", full: true, keywords: "dwg dxf autocad çizim plan metraj blok katman",
  file: null,          // { path, name } of the drawing on disk, when there is one
  sideOpen: false,

  mount(el) {
    this.el = el;
    App.command({ title: "Çizim aç (DWG/DXF)", hint: "Çizim", icon: "folder", keywords: "dwg dxf aç", run: () => { App.go("cad"); this.open(); } });
    App.command({ title: "Yeni çizim", hint: "Çizim", icon: "plus", keywords: "dwg dxf yeni", run: () => { App.go("cad"); this.newDrawing(); } });
    App.command({ title: "Çizimden metraj çıkar", hint: "Çizim", icon: "ruler", keywords: "metraj blok sayımı boru uzunluğu", run: () => { App.go("cad"); this.toggleSide(true); } });

    this.titleEl = h("span", { class: "muted small" }, "Yeni çizim");
    this.frame = h("iframe", { src: `cad2d/index.html?theme=${effectiveTheme()}`, style: { border: 0, width: "100%", height: "100%", display: "block" }, allow: "clipboard-read; clipboard-write" });
    Cad2d.attach(this.frame);
    Cad2d.onEvent = m => { if (m.type === "ready") this.setTitle(); };
    this.side = h("div", { class: "cad-side", hidden: true });
    el.append(h("div", { class: "cad", style: { gridTemplateColumns: "minmax(0,1fr) auto" } },
      h("div", { class: "cad-stage" },
        h("div", { class: "cad-toolbar" },
          btn("Aç", () => this.open(), { cls: "sm", icon: "folder" }),
          btn("Yeni", () => this.newDrawing(), { cls: "sm", icon: "plus" }),
          btn("Kaydet", () => this.save(false), { cls: "sm", icon: "save" }),
          btn("Farklı kaydet", () => this.save(true), { cls: "sm ghost" }),
          h("div", { class: "grow row", style: { justifyContent: "center" } }, this.titleEl),
          btn("Metraj", () => this.toggleSide(), { cls: "sm", icon: "ruler" })),
        h("div", { class: "cad-canvas" }, this.frame)),
      this.side));
  },

  setTitle() {
    this.titleEl.textContent = this.file ? this.file.path : "Kaydedilmemiş çizim";
  },

  async open(path) {
    try {
      const f = await rpc("cad.pick", path ? { path } : {});
      if (!f) return;
      await Cad2d.call("open", { url: f.url, name: f.name });
      this.file = { path: f.path, name: f.name };
      this.setTitle();
      toast(`${f.name} açıldı`);
      if (this.sideOpen) this.runMetraj();
    } catch (e) { fail(e); }
  },

  async newDrawing() {
    try { await Cad2d.call("new"); this.file = null; this.setTitle(); } catch (e) { fail(e); }
  },

  async save(as) {
    try {
      const { dxf, title } = await Cad2d.call("dxf");
      const target = !as && this.file ? this.file.path : null;
      const path = await rpc("cad.save", { dxf, path: target, name: (this.file?.name ?? title ?? "Çizim").replace(/\.[^.]+$/, "") });
      if (!path) return;
      this.file = { path, name: path.split("\\").pop() };
      this.setTitle();
      toast(path.toLowerCase().endsWith(".dwg") ? "DWG olarak kaydedildi" : "DXF olarak kaydedildi");
    } catch (e) { fail(e); }
  },

  toggleSide(force) {
    this.sideOpen = force ?? !this.sideOpen;
    this.side.hidden = !this.sideOpen;
    if (this.sideOpen) this.runMetraj();
  },

  // Quantities from the drawing as it is now in the editor (DXF → ACadSharp analysis on the host).
  async runMetraj() {
    const side = clear(this.side);
    side.style.width = "340px";
    side.append(h("div", { class: "card-head" }, h("h3", null, "Çizimden metraj"),
      btn("Yenile", () => this.runMetraj(), { cls: "ghost sm", icon: "refresh" }),
      h("button", { class: "btn ghost icon", onclick: () => this.toggleSide(false) }, icon("x"))));
    const body = side.appendChild(h("div", { class: "cad-side-body" }, h("div", { class: "empty small" }, "Hesaplanıyor…")));
    let info;
    try {
      const { dxf } = await Cad2d.call("dxf");
      info = await rpc("cad.analyze", { dxf });
    } catch (e) { clear(body).append(note(e.message, "bad")); return; }

    const st = this.metrajState ??= { unit: null };
    st.unit = UNIT_FROM_HEADER[info.units] ?? st.unit ?? "mm";
    const picked = new Set();
    const rowsHost = h("div");
    const render = () => {
      const f = UNIT_FACTORS[st.unit];
      const items = [
        ...info.layers.filter(l => l.length > 1e-9).map(l => ({ key: "L:" + l.layer, kind: "length", name: l.layer, layer: l.layer, group: "Katman uzunlukları", description: `${l.layer} katmanı toplam uzunluk`, quantity: l.length * f, unit: "m", source: `Çizim: ${this.file?.name ?? "çizim"} / ${l.layer} uzunluk` })),
        ...info.layers.filter(l => l.closed > 0).map(l => ({ key: "A:" + l.layer, kind: "area", name: l.layer, layer: l.layer, group: "Kapalı alanlar", description: `${l.layer} katmanı kapalı alanlar (${l.closed} adet)`, quantity: l.closedArea * f * f, unit: "m²", source: `Çizim: ${this.file?.name ?? "çizim"} / ${l.layer} alan` })),
        ...info.blocks.map(b => ({ key: "B:" + b.block + "|" + b.layer, kind: "count", name: b.block, layer: b.layer, group: "Blok sayıları", description: `${b.block} bloğu (${b.layer})`, quantity: b.count, unit: "adet", source: `Çizim: ${this.file?.name ?? "çizim"} / blok ${b.block} (${b.layer})` })),
      ];
      this.metrajItems = items;
      const groups = [...new Set(items.map(i => i.group))];
      clear(rowsHost).append(items.length ? groups.map(g => [
        h("div", { class: "nav-group" }, g),
        items.filter(i => i.group === g).map(i => h("label", { class: "list-item", style: { cursor: "pointer" } },
          h("input", { type: "checkbox", checked: picked.has(i.key), onchange: e => { e.target.checked ? picked.add(i.key) : picked.delete(i.key); } }),
          h("div", { class: "grow" }, h("div", { class: "title", title: i.description }, i.description)),
          h("span", { class: "num small" }, `${fmt(i.quantity, i.unit === "adet" ? 0 : 2)} ${i.unit}`))),
      ]) : h("div", { class: "empty small" }, "Çizimde ölçülebilir çizgi veya blok yok."));
    };
    clear(body).append(
      h("div", { class: "card-body stack" },
        h("div", { class: "row" }, h("span", { class: "label grow" }, "Çizim birimi"),
          selectField(st, "unit", null, [{ value: "mm", label: "mm" }, { value: "cm", label: "cm" }, { value: "m", label: "m" }], { bare: true, onChange: render })),
        !UNIT_FROM_HEADER[info.units] && note("Çizimde birim tanımlı değil; doğru birimi seçin.", "warn"),
        h("div", { class: "faint small" }, `${info.entityCount.toLocaleString(TR)} nesne · ${info.version}`)),
      rowsHost,
      h("div", { class: "card-body row wrap" },
        h("button", { class: "btn ghost sm", onclick: () => { this.metrajItems.forEach(i => picked.add(i.key)); render(); } }, "Tümünü seç"),
        btn("Pozlarla eşleştir ve aktar", () => {
          const rows = this.metrajItems.filter(i => picked.has(i.key));
          if (!rows.length) { toast("Önce satır seçin", "bad"); return; }
          if (!App.project) { toast("Aktarmak için bir proje açın", "bad"); return; }
          this.transferDialog(rows);
        }, { cls: "primary", icon: "receipt" })));
    render();
  },

  // Drawing quantities → measurement lines of the estimate or of a hakediş period. Each row gets a poz (suggested from
  // the project's earlier choices and from the layer/block name); a row already taken from the same drawing source is
  // updated instead of added again, so re-measuring a revised drawing does not double the quantities.
  async transferDialog(rows) {
    const d = App.project.data;
    const v = { target: this.transferTarget ?? (d.estimate.items.length || !d.job.items.length ? "est" : "hak"), period: d.job.periods.at(-1)?.no ?? 1 };
    const host = h("div", { class: "stack" });
    const choice = {};
    const targetData = () => v.target === "est" ? d.estimate : d.job;
    const draw = async () => {
      const t = targetData();
      clear(host);
      if (!t.items.length) { host.append(note(v.target === "est" ? "Yaklaşık maliyette poz yok: önce Pozlar sekmesinden ekleyin." : "Hakedişte poz yok: önce Pozlar sekmesinden ekleyin.", "warn")); return; }
      let sugg = [];
      try {
        sugg = await rpc("takeoff.suggest", {
          rows: rows.map(r => ({ key: r.key, kind: r.kind, name: r.name, layer: r.layer, quantity: r.quantity })),
          items: t.items.map(i => ({ id: i.id, pozNo: i.pozNo ?? "", description: i.description ?? "", unit: i.unit ?? "", unitPrice: i.unitPrice ?? 0 })),
          saved: d.drawingMap,
        });
      } catch (e) { fail(e); }
      const opts = [{ value: null, label: "— aktarma —" }, ...t.items.map(i => ({ value: i.id, label: `${i.pozNo} ${i.description}`.slice(0, 80) + ` (${i.unit})` }))];
      host.append(h("div", { class: "table-wrap", style: { maxHeight: "50vh" } }, h("table", { class: "t" },
        h("thead", null, h("tr", null, h("th", null, "Çizimden"), h("th", { class: "num" }, "Miktar"), h("th", null, "Poz"))),
        h("tbody", null, rows.map(r => {
          const s = sugg.find(x => x.key === r.key);
          choice[r.key] ??= { itemId: s?.itemId ?? null };
          const sel = selectField(choice[r.key], "itemId", null, opts, { bare: true });
          sel.style.maxWidth = "360px";
          return h("tr", null, h("td", null, r.description), h("td", { class: "num" }, `${fmt(r.quantity, r.unit === "adet" ? 0 : 2)} ${r.unit}`),
            h("td", null, h("div", { class: "row" }, sel, s?.reason && h("span", { class: "badge" + (s.reason === "saved" ? " ok" : "") }, s.reason === "saved" ? "önceki seçim" : "öneri"))));
        })))));
    };
    const go = () => {
      const t = targetData();
      let added = 0, updated = 0;
      for (const r of rows) {
        const itemId = choice[r.key]?.itemId;
        if (!itemId) continue;
        const item = t.items.find(i => i.id === itemId);
        d.drawingMap[r.key] = item?.pozNo ?? "";
        const qty = Math.round(r.quantity * 1000) / 1000;
        const period = Number(v.period);
        const same = t.lines.find(l => l.source === r.source && l.itemId === itemId && (v.target === "est" || l.period === period));
        if (same) { same.fixed = qty; same.count = 1; same.width = same.length = same.height = null; updated++; continue; }
        const line = { id: uid(), itemId, description: r.description, count: 1, width: null, length: null, height: null, fixed: qty, deduct: false, source: r.source };
        if (v.target === "hak") line.period = period;
        t.lines.push(line); added++;
      }
      this.transferTarget = v.target;
      App.touch(); closeOverlay();
      toast(`${added} satır eklendi, ${updated} satır güncellendi (${v.target === "est" ? "yaklaşık maliyet" : `${v.period} nolu hakediş`})`);
    };
    const periodNos = d.job.periods.length ? d.job.periods.map(p => p.no) : [1];
    const head = h("div", { class: "fields" },
      selectField(v, "target", "Hedef", [{ value: "est", label: "Yaklaşık maliyet" }, { value: "hak", label: "Hakediş metrajı" }], { onChange: () => { Object.keys(choice).forEach(k => delete choice[k]); head.querySelector(".period-field").hidden = v.target !== "hak"; draw(); } }),
      (() => { const f = selectField(v, "period", "Hakediş", periodNos.map(n => ({ value: n, label: n + " nolu" }))); f.classList.add("period-field"); f.hidden = v.target !== "hak"; return f; })());
    modal("Çizimden metraja aktar", h("div", { class: "stack" }, head, host,
      h("div", { class: "faint small" }, "Pozun birimi miktarla uyumlu olmalı (uzunluk → m, alan → m², blok → adet). Seçimler projede saklanır; aynı katman bir dahaki sefere aynı poza önerilir.")),
      [btn("Vazgeç", closeOverlay), btn("Aktar", go, { cls: "primary" })], { wide: true });
    draw();
  },
});
