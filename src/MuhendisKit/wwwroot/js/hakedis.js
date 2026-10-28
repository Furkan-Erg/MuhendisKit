// Hakediş: contract, unit-price items, measurement lines (yeşil defter), payments and price difference.
"use strict";

function lineQty(l) {
  const q = l.fixed != null ? l.fixed : (l.count ?? 1) * (l.width ?? 1) * (l.length ?? 1) * (l.height ?? 1);
  return Math.round(q * (l.deduct ? -1 : 1) * 1000) / 1000;
}

// The host model uses non-nullable decimals; empty inputs become their neutral value.
function jobForHost(j) {
  return {
    owner: j.owner ?? "", contractor: j.contractor ?? "", contractNo: j.contractNo ?? "", tenderMonth: j.tenderMonth ?? "",
    contractAmount: j.contractAmount ?? 0, b: j.b ?? 0, pnDigits: j.pnDigits ?? 3, kdvRate: j.kdvRate ?? 0,
    deductions: j.deductions.map(d => ({ name: d.name ?? "", ratePercent: d.ratePercent ?? 0, base: d.base ?? "tutar" })),
    items: j.items.map(i => ({ id: i.id, pozNo: i.pozNo ?? "", description: i.description ?? "", unit: i.unit ?? "", unitPrice: i.unitPrice ?? 0 })),
    lines: j.lines.map(l => ({
      id: l.id, itemId: l.itemId, period: l.period ?? 1, description: l.description ?? "", count: l.count ?? 1,
      width: l.width ?? null, length: l.length ?? null, height: l.height ?? null, fixed: l.fixed ?? null, deduct: !!l.deduct, source: l.source ?? "",
    })),
    periods: j.periods.map(p => ({ no: p.no, date: p.date ?? "", month: p.month ?? "", allowanceMonth: p.allowanceMonth || null, note: p.note ?? "" })),
    indices: Object.fromEntries(Object.entries(j.indices).filter(([, v]) => v != null && v > 0)),
  };
}

const monthInput = (obj, key, label, onChange) => {
  const input = h("input", { class: "input", type: "month", value: obj[key] ?? "" });
  input.addEventListener("change", () => { obj[key] = input.value || null; onChange?.(); });
  return label ? h("div", { class: "field" }, h("label", null, label), input) : input;
};

App.register({
  id: "hak", title: "Hakediş", icon: "receipt", group: "Modüller", keywords: "metraj yeşil defter poz birim fiyat fiyat farkı icmal keşif",
  tab: "is",
  metrajPeriod: null,
  selectedPeriod: null,

  mount(el) {
    this.el = el;
    const tabs = { is: "İş bilgileri", poz: "Pozlar", metraj: "Metraj (yeşil defter)", hakedis: "Hakedişler", endeks: "Endeksler" };
    for (const [k, v] of Object.entries(tabs))
      App.command({ title: "Hakediş: " + v, hint: "Hakediş", icon: "receipt", keywords: "hakediş " + v, run: () => { this.tab = k; App.go("hak"); } });
    App.command({ title: "Yeni hakediş dönemi", hint: "Hakediş", icon: "plus", keywords: "hakediş ekle", run: () => { App.go("hak"); if (App.project) { this.tab = "hakedis"; this.addPeriod(); } } });
  },

  get job() { return App.project?.data.job; },

  show() {
    const el = clear(this.el);
    el.append(h("div", { class: "page-head" }, h("div", { class: "grow" }, h("h1", null, "Hakediş"),
      h("div", { class: "sub" }, "Poz, metraj, hakediş icmali ve fiyat farkı. Excel çıktısı resmi tablo düzenine yakındır."))));
    const body = el.appendChild(h("div"));
    if (App.requireProject(body, "Hakediş bilgileri")) return;
    const j = this.job;
    const tabs = [
      ["is", "İş bilgileri"], ["poz", "Pozlar", j.items.length], ["metraj", "Metraj", j.lines.length],
      ["hakedis", "Hakedişler", j.periods.length], ["endeks", "Endeksler", this.missingIndices().length ? "!" : null],
    ];
    body.append(h("div", { class: "tabs" }, tabs.map(([k, label, n]) =>
      h("button", { class: "tab" + (this.tab === k ? " active" : ""), onclick: () => { this.tab = k; this.show(); } },
        label, n != null && n !== 0 && h("span", { class: "badge" + (n === "!" ? " warn" : "") }, n)))));
    this.bodyEl = body.appendChild(h("div"));
    ({ is: this.renderJob, poz: this.renderItems, metraj: this.renderLines, hakedis: this.renderPeriods, endeks: this.renderIndices })[this.tab].call(this, this.bodyEl);
  },

  touch() { App.touch(); },

  // ---- İş bilgileri ----
  renderJob(el) {
    const j = this.job, t = () => this.touch();
    const dedBody = h("tbody");
    const renderDed = () => clear(dedBody).append(j.deductions.map((d, i) => h("tr", null,
      h("td", null, textField(d, "name", null, { bare: true, onChange: t })),
      h("td", null, numField(d, "ratePercent", null, { bare: true, cls: "num", onChange: t })),
      h("td", null, selectField(d, "base", null, [{ value: "tutar", label: "KDV hariç tutar" }, { value: "kdv", label: "KDV (tevkifat)" }, { value: "toplam", label: "KDV dahil tutar" }], { bare: true, onChange: t })),
      h("td", null, h("button", { class: "btn ghost icon", onclick: () => { j.deductions.splice(i, 1); renderDed(); t(); } }, icon("x"))))));
    renderDed();
    el.append(h("div", { class: "grid g2" },
      h("div", { class: "card" }, h("div", { class: "card-head" }, h("h3", null, "Sözleşme")),
        h("div", { class: "card-body fields" },
          textField(j, "owner", "İdare", { onChange: t }),
          textField(j, "contractor", "Yüklenici", { onChange: t }),
          textField(j, "contractNo", "Sözleşme no / tarihi", { onChange: t }),
          monthInput(j, "tenderMonth", "İhale tarihinin ayı (G0)", t),
          numField(j, "contractAmount", "Sözleşme bedeli", { unit: "₺", onChange: t }))),
      h("div", { class: "card" }, h("div", { class: "card-head" }, h("h3", null, "Fiyat farkı ve vergiler")),
        h("div", { class: "card-body stack" },
          h("div", { class: "fields" },
            numField(j, "b", "Sabit katsayı B", { hint: "Sözleşmedeki değer", onChange: t }),
            numField(j, "pnDigits", "Pn ondalık hane", { hint: "Uygulama esaslarına göre", onChange: t }),
            numField(j, "kdvRate", "KDV oranı", { unit: "%", onChange: t })),
          h("div", { class: "formula" }, "F = An × B × (Pn − 1)     Pn = Gn / G0\nGn: uygulama ayı ile ödenek ayı endekslerinden küçük olanı"),
          note("Geçici kararnamelerle gelen ek fiyat farkı düzenlemeleri (ör. ilave fiyat farkı) bu formüle dahil değildir; gerekiyorsa ayrıca hesaplayın.")))),
      h("div", { class: "card", style: { marginTop: "16px" } },
        h("div", { class: "card-head" }, h("h3", null, "Kesintiler"), btn("Kesinti ekle", () => { j.deductions.push({ name: "", ratePercent: null, base: "tutar" }); renderDed(); t(); }, { cls: "sm", icon: "plus" })),
        h("div", { class: "table-wrap" }, h("table", { class: "t" },
          h("thead", null, h("tr", null, h("th", null, "Kesinti"), h("th", { class: "num" }, "Oran (%)"), h("th", null, "Matrah"), h("th"))), dedBody)),
        h("div", { class: "card-body" }, h("span", { class: "faint small" }, "Oranları sözleşme ve güncel mevzuata göre kontrol edin (damga vergisi binde 9,48 = %0,948)."))));
  },

  // ---- Pozlar ----
  renderItems(el) {
    const j = this.job, t = () => this.touch();
    const tbody = h("tbody");
    const used = id => j.lines.filter(l => l.itemId === id).length;
    const render = () => clear(tbody).append(j.items.length ? j.items.map((it, i) => h("tr", null,
      h("td", null, textField(it, "pozNo", null, { bare: true, onChange: t })),
      h("td", { style: { width: "45%" } }, textField(it, "description", null, { bare: true, onChange: t })),
      h("td", null, textField(it, "unit", null, { bare: true, onChange: t })),
      h("td", null, numField(it, "unitPrice", null, { bare: true, cls: "num", onChange: t })),
      h("td", { class: "num faint" }, used(it.id) || ""),
      h("td", null, h("button", {
        class: "btn ghost icon", title: "Sil", onclick: async () => {
          const n = used(it.id);
          if (n && !await confirmBox("Pozu sil", `Bu poza bağlı ${n} metraj satırı da silinecek.`, "Sil", true)) return;
          j.items.splice(i, 1); j.lines = j.lines.filter(l => l.itemId !== it.id); render(); t();
        },
      }, icon("trash")))))
      : h("tr", null, h("td", { colspan: 6, class: "empty" }, "Henüz poz yok. Katalogdan arayın ya da elle ekleyin.")));
    render();

    const results = h("div", { class: "list" });
    const q = { text: "" };
    const search = debounce(async () => {
      try {
        const rows = await rpc("catalog.search", { q: q.text });
        clear(results).append(rows.length ? rows.map(r => h("div", { class: "list-item" },
          h("div", { class: "grow" }, h("div", { class: "title", title: r.spec ? `${r.description}

${r.spec}` : r.description }, `${r.pozNo} · ${r.description}`),
            h("div", { class: "meta" }, `${money(r.unitPrice)} / ${r.unit || "?"}${r.mountingPrice != null ? ` · montaj ${money(r.mountingPrice)}` : ""} · ${r.source}`)),
          btn("Ekle", () => {
            j.items.push({ id: uid(), pozNo: r.pozNo, description: r.description, unit: r.unit, unitPrice: r.unitPrice });
            render(); t(); toast(`${r.pozNo} eklendi`);
          }, { cls: "sm", icon: "plus" })))
          : h("div", { class: "empty small" }, "Katalogda sonuç yok."));
      } catch (e) { fail(e); }
    }, 200);

    el.append(h("div", { class: "split-wide" },
      h("div", { class: "card" },
        h("div", { class: "card-head" }, h("h3", null, "İşin pozları"),
          btn("Elle ekle", () => { j.items.push({ id: uid(), pozNo: "", description: "", unit: "", unitPrice: null }); render(); t(); }, { cls: "sm", icon: "plus" })),
        h("div", { class: "table-wrap" }, h("table", { class: "t" },
          h("thead", null, h("tr", null, h("th", null, "Poz no"), h("th", null, "Tanım"), h("th", null, "Birim"), h("th", { class: "num" }, "Birim fiyat (₺)"), h("th", { class: "num", title: "Metraj satırı" }, "Sat."), h("th"))),
          tbody))),
      h("div", { class: "card" },
        h("div", { class: "card-head" }, h("h3", null, "Birim fiyat kataloğu")),
        h("div", { class: "card-body stack" },
          textField(q, "text", null, { bare: true, placeholder: "Poz no veya tanım ara (ör. 25.120 radyatör)", onChange: search }),
          results,
          h("div", { class: "faint small" }, "Katalog Ayarlar sayfasından Bakanlık listesi indirilerek ya da Excel ile doldurulur.")))));
    search();
  },

  // ---- Metraj ----
  renderLines(el) {
    const j = this.job, t = () => this.touch();
    if (!j.items.length) { el.append(note("Metraj girmek için önce Pozlar sekmesinden poz ekleyin.", "warn")); return; }
    const periodNos = [...new Set([...j.periods.map(p => p.no), ...j.lines.map(l => l.period)])].sort((a, b) => a - b);
    if (!periodNos.length) periodNos.push(1);
    if (this.metrajPeriod != null && !periodNos.includes(this.metrajPeriod)) this.metrajPeriod = null;
    const filter = { period: this.metrajPeriod ?? "all", item: this.metrajItem ?? "all" };
    const tbody = h("tbody");
    const totalCell = h("td", { class: "num", colspan: 1 });
    const itemOpts = j.items.map(i => ({ value: i.id, label: `${i.pozNo} ${i.description}`.trim() || "(adsız poz)" }));
    const unitOf = id => j.items.find(i => i.id === id)?.unit ?? "";

    const render = () => {
      const rows = j.lines.filter(l => (filter.period === "all" || l.period === Number(filter.period)) && (filter.item === "all" || l.itemId === filter.item));
      clear(tbody).append(rows.length ? rows.map(l => {
        const qCell = h("td", { class: "num" }, fmt(lineQty(l), 3));
        const upd = () => { qCell.textContent = fmt(lineQty(l), 3); renderTotal(); t(); };
        const itemSel = selectField(l, "itemId", null, itemOpts, { bare: true, onChange: () => { unitCell.textContent = unitOf(l.itemId); upd(); } });
        itemSel.style.maxWidth = "240px";
        const unitCell = h("td", { class: "faint" }, unitOf(l.itemId));
        return h("tr", null,
          h("td", null, selectField(l, "period", null, periodNos.map(n => ({ value: n, label: n + "." })), { bare: true, onChange: upd })),
          h("td", null, itemSel),
          h("td", null, textField(l, "description", null, { bare: true, onChange: t })),
          ["count", "width", "length", "height", "fixed"].map(k => h("td", null, numField(l, k, null, { bare: true, cls: "num", onChange: upd }))),
          h("td", null, h("input", { type: "checkbox", checked: !!l.deduct, title: "Düşülecek miktar (−)", onchange: e => { l.deduct = e.target.checked; upd(); } })),
          qCell, unitCell,
          h("td", { class: "faint small", title: l.source }, l.source ? "çizim" : ""),
          h("td", null, h("button", { class: "btn ghost icon", onclick: () => { j.lines.splice(j.lines.indexOf(l), 1); render(); t(); } }, icon("x"))));
      }) : h("tr", null, h("td", { colspan: 14, class: "empty" }, "Bu filtrede metraj satırı yok.")));
      renderTotal();
    };
    const renderTotal = () => {
      if (filter.item === "all") { totalCell.textContent = ""; return; }
      const rows = j.lines.filter(l => l.itemId === filter.item && (filter.period === "all" || l.period === Number(filter.period)));
      totalCell.textContent = fmt(rows.reduce((a, l) => a + lineQty(l), 0), 3) + " " + unitOf(filter.item);
    };
    const add = () => {
      const period = filter.period === "all" ? periodNos.at(-1) : Number(filter.period);
      const last = j.lines.at(-1);
      j.lines.push({ id: uid(), itemId: filter.item !== "all" ? filter.item : last?.itemId ?? j.items[0].id, period, description: "", count: 1, width: null, length: null, height: null, fixed: null, deduct: false, source: "" });
      render(); t();
      tbody.lastChild?.querySelector("td:nth-child(3) input")?.focus();
    };

    el.append(h("div", { class: "card" },
      h("div", { class: "card-head" },
        h("div", { class: "row grow" },
          h("span", { class: "label" }, "Hakediş"),
          selectField(filter, "period", null, [{ value: "all", label: "Tümü" }, ...periodNos.map(n => ({ value: n, label: n + " nolu" }))], { bare: true, onChange: v => { this.metrajPeriod = v === "all" ? null : Number(v); render(); } }),
          h("span", { class: "label" }, "Poz"),
          (() => { const s = selectField(filter, "item", null, [{ value: "all", label: "Tümü" }, ...itemOpts], { bare: true, onChange: v => { this.metrajItem = v === "all" ? null : v; render(); } }); s.style.maxWidth = "320px"; return s; })()),
        btn("Satır ekle", add, { cls: "primary sm", icon: "plus" })),
      h("div", { class: "table-wrap" }, h("table", { class: "t" },
        h("thead", null, h("tr", null, ["Hak.", "Poz", "Açıklama", "Adet", "En", "Boy", "Yük.", "Miktar (doğrudan)", "(−)", "Miktar", "Birim", "Kaynak", ""].map((x, i) =>
          h("th", { class: i >= 3 && i <= 9 && i !== 8 ? "num" : null }, x)))),
        tbody,
        h("tfoot", null, h("tr", { class: "total" }, h("td", { colspan: 9 }, filter.item === "all" ? "Toplamı görmek için poz seçin" : "Seçili poz toplamı"), totalCell, h("td", { colspan: 3 }))))),
      h("div", { class: "card-body faint small" }, "Miktar = Adet × En × Boy × Yükseklik (boş boyutlar 1 sayılır). \"Miktar (doğrudan)\" doluysa boyutların yerine o kullanılır.")));
    render();
  },

  // ---- Hakedişler ----
  addPeriod() {
    const j = this.job;
    const no = Math.max(0, ...j.periods.map(p => p.no)) + 1;
    j.periods.push({ no, date: todayIso(), month: thisMonth(), allowanceMonth: null, note: "" });
    this.selectedPeriod = no;
    this.touch();
    this.show();
  },

  renderPeriods(el) {
    const j = this.job, t = () => this.touch();
    if (!j.periods.find(p => p.no === this.selectedPeriod)) this.selectedPeriod = j.periods.at(-1)?.no ?? null;
    const p = j.periods.find(x => x.no === this.selectedPeriod);
    const side = h("div", { class: "card" },
      h("div", { class: "card-head" }, h("h3", null, "Hakedişler"), btn("Yeni", () => this.addPeriod(), { cls: "sm primary", icon: "plus" })),
      j.periods.length ? h("div", { class: "list" }, j.periods.slice().sort((a, b) => a.no - b.no).map(x =>
        h("button", { class: "list-item" + (x.no === this.selectedPeriod ? " active" : ""), onclick: () => { this.selectedPeriod = x.no; this.show(); } },
          h("div", { class: "grow" }, h("div", { class: "title" }, `${x.no} Nolu Hakediş`), h("div", { class: "meta" }, fmtMonth(x.month))))))
        : h("div", { class: "empty small" }, "Henüz hakediş yok."));
    const main = h("div", { class: "stack" });
    el.append(h("div", { class: "split" }, side, main));
    if (!p) { main.append(h("div", { class: "card" }, h("div", { class: "empty" }, icon("receipt"), h("h3", null, "Hakediş dönemi ekleyin"), h("p", null, "Her dönem bir hakedişe karşılık gelir; metraj satırları dönemlere atanır.")))); return; }

    const summaryHost = h("div", { class: "stack" });
    const recalc = debounce(() => this.renderSummary(p, summaryHost), 150);
    main.append(h("div", { class: "card" },
      h("div", { class: "card-head" }, h("h2", null, `${p.no} Nolu Hakediş`),
        btn("Excel", () => this.exportExcel(p.no), { cls: "sm", icon: "excel" }),
        btn("PDF", () => this.pdf(p.no), { cls: "sm", icon: "pdf" }),
        h("button", {
          class: "btn ghost sm danger", onclick: async () => {
            const n = j.lines.filter(l => l.period === p.no).length;
            if (!await confirmBox("Hakedişi sil", n ? `${p.no} nolu hakediş ve bu döneme ait ${n} metraj satırı silinecek.` : `${p.no} nolu hakediş silinsin mi?`, "Sil", true)) return;
            j.periods = j.periods.filter(x => x !== p); j.lines = j.lines.filter(l => l.period !== p.no); t(); this.show();
          },
        }, icon("trash"))),
      h("div", { class: "card-body fields" },
        textField(p, "date", "Hakediş tarihi", { type: "date", onChange: t }),
        monthInput(p, "month", "Uygulama ayı (Gn)", () => { t(); recalc(); }),
        monthInput(p, "allowanceMonth", "Ödenek ayı (varsa)", () => { t(); recalc(); }),
        textField(p, "note", "Not", { onChange: t }))));
    main.append(summaryHost);
    recalc();
  },

  async renderSummary(p, host) {
    let s;
    try { s = await rpc("hak.period", { job: jobForHost(this.job), no: p.no }); }
    catch (e) { clear(host).append(note(e.message, "bad")); return; }
    clear(host).append(
      s.warnings.map(w => note(w, "warn")),
      h("div", { class: "stats" },
        stat("Bu hakediş tutarı", fmt(s.thisAmount, 2), "₺"),
        stat("Fiyat farkı", s.pn == null ? "—" : fmt(s.priceDiff, 2), "₺"),
        stat("KDV dahil", fmt(s.subtotal + s.kdv, 2), "₺"),
        stat("Ödenecek", fmt(s.payable, 2), "₺", "hero")),
      this.summaryTable(s),
      this.itemsTable(s));
  },

  summaryTable(s) {
    const j = this.job;
    const row = (k, v, cls) => h("tr", { class: cls }, h("td", null, k), h("td", { class: "num" }, v));
    return h("div", { class: "card" }, h("div", { class: "card-head" }, h("h3", null, "Hakediş icmali")),
      h("table", { class: "t" }, h("tbody", null,
        row("A — Sözleşme fiyatları ile yapılan işin tutarı (kümülatif)", money(s.cumAmount)),
        row("B — Önceki hakedişler toplamı", money(s.prevAmount)),
        row("C — Bu hakediş tutarı (A − B)", money(s.thisAmount), "total"),
        row(`Endeks: G0 = ${s.g0 == null ? "—" : fmt(s.g0, 2)} · Gn = ${s.gn == null ? "—" : fmt(s.gn, 2)} · Pn = ${s.pn == null ? "—" : fmt(s.pn, j.pnDigits ?? 3)} · B = ${fmt(j.b, 2)}`, ""),
        row("D — Fiyat farkı (C × B × (Pn − 1))", s.pn == null ? "hesaplanmadı" : money(s.priceDiff)),
        row("Kümülatif fiyat farkı", money(s.cumPriceDiff)),
        row("E — Toplam (C + D)", money(s.subtotal), "total"),
        row(`F — KDV (%${fmt(j.kdvRate, 0)})`, money(s.kdv)),
        row("G — KDV dahil tutar", money(s.subtotal + s.kdv), "total"),
        s.deductions.map(d => row(`Kesinti: ${d.name} (%${fmt(d.ratePercent, 3)})`, "−" + money(d.amount))),
        row("Yükleniciye ödenecek tutar", money(s.payable), "total"))));
  },

  itemsTable(s) {
    return h("div", { class: "card" }, h("div", { class: "card-head" }, h("h3", null, "Yapılan işler listesi")),
      h("div", { class: "table-wrap" }, h("table", { class: "t" },
        h("thead", null, h("tr", null, ["Poz no", "Tanım", "Birim", "Birim fiyat", "Önceki", "Bu dönem", "Toplam", "Bu dönem tutar", "Toplam tutar"].map((x, i) => h("th", { class: i >= 3 ? "num" : null }, x)))),
        h("tbody", null, s.items.length ? s.items.map(r => h("tr", null,
          h("td", { class: "mono small" }, r.pozNo), h("td", null, r.description), h("td", null, r.unit),
          h("td", { class: "num" }, fmt(r.unitPrice, 2)), h("td", { class: "num" }, fmt(r.prevQty, 3)), h("td", { class: "num" }, fmt(r.thisQty, 3)),
          h("td", { class: "num" }, fmt(r.cumQty, 3)), h("td", { class: "num" }, fmt(r.thisAmount, 2)), h("td", { class: "num" }, fmt(r.cumAmount, 2))))
          : h("tr", null, h("td", { colspan: 9, class: "empty" }, "Bu hakedişe kadar metraj yok."))))));
  },

  async exportExcel(no) {
    try {
      const path = await rpc("hak.export", { job: jobForHost(this.job), no, name: App.project.data.name });
      if (path) toast("Excel dosyası kaydedildi");
    } catch (e) { fail(e); }
  },

  async pdf(no) {
    let s;
    try { s = await rpc("hak.period", { job: jobForHost(this.job), no }); } catch (e) { fail(e); return; }
    const j = this.job;
    printReport(`${no} Nolu Hakediş`, h("div", { class: "stack" },
      h("p", null, [j.owner && "İdare: " + j.owner, j.contractor && "Yüklenici: " + j.contractor, j.contractNo && "Sözleşme: " + j.contractNo].filter(Boolean).join(" · ")),
      h("p", null, `Uygulama ayı: ${fmtMonth(s.month)}`),
      this.summaryTable(s), this.itemsTable(s)), { landscape: true, fileName: `${App.project.data.name} - ${no} Nolu Hakediş` });
  },

  // ---- Endeksler ----
  neededMonths() {
    const j = this.job;
    return [...new Set([j.tenderMonth, ...j.periods.flatMap(p => [p.month, p.allowanceMonth])].filter(Boolean))].sort();
  },
  missingIndices() {
    const j = this.job;
    return this.neededMonths().filter(m => !(j.indices[m] > 0));
  },

  renderIndices(el) {
    const j = this.job, t = () => this.touch();
    const months = [...new Set([...this.neededMonths(), ...Object.keys(j.indices)])].sort();
    const needed = new Set(this.neededMonths());
    const tbody = h("tbody", null, months.length ? months.map(m => {
      const role = [m === j.tenderMonth && "G0 (ihale ayı)", ...j.periods.filter(p => p.month === m).map(p => `${p.no}. hakediş`), ...j.periods.filter(p => p.allowanceMonth === m).map(p => `${p.no}. ödenek`)].filter(Boolean).join(", ");
      return h("tr", null, h("td", null, fmtMonth(m)), h("td", { class: "mono small faint" }, m), h("td", { class: "small muted" }, role || "—"),
        h("td", null, numField(j.indices, m, null, { bare: true, cls: "num" + (needed.has(m) && !(j.indices[m] > 0) ? " invalid" : ""), onChange: t })),
        h("td", null, !needed.has(m) && h("button", { class: "btn ghost icon", onclick: () => { delete j.indices[m]; t(); this.show(); } }, icon("x"))));
    }) : h("tr", null, h("td", { colspan: 5, class: "empty" }, "İhale ayını ve hakediş aylarını girdiğinizde gerekli aylar burada listelenir.")));
    const extra = { m: null };
    const fetchEvds = async () => {
      if (!App.settings.evdsKey) { toast("Ayarlar'dan EVDS API anahtarını girin", "bad"); return; }
      const years = months.map(m => Number(m.slice(0, 4)));
      const from = years.length ? Math.min(...years) : new Date().getFullYear() - 1;
      try {
        const r = await rpc("evds.fetch", { key: App.settings.evdsKey, series: App.settings.evdsSeries || null, from, to: new Date().getFullYear() });
        let n = 0;
        for (const m of months) if (r[m] != null) { j.indices[m] = r[m]; n++; }
        t(); this.show();
        toast(n ? `${n} ayın endeksi güncellendi` : "Gerekli aylar için EVDS'de değer bulunamadı", n ? "" : "bad");
      } catch (e) { fail(e); }
    };
    el.append(h("div", { class: "split-wide" },
      h("div", { class: "card" },
        h("div", { class: "card-head" }, h("h3", null, "Endeks değerleri"), btn("EVDS'den çek", fetchEvds, { cls: "sm", icon: "download" })),
        h("div", { class: "table-wrap" }, h("table", { class: "t" },
          h("thead", null, h("tr", null, h("th", null, "Ay"), h("th"), h("th", null, "Kullanıldığı yer"), h("th", { class: "num" }, "Endeks"), h("th"))), tbody)),
        h("div", { class: "card-body row" }, monthInput(extra, "m", null, () => { if (extra.m && !(extra.m in j.indices)) { j.indices[extra.m] = null; this.show(); } }),
          h("span", { class: "faint small" }, "Başka bir ay eklemek için seçin"))),
      h("div", { class: "card card-pad stack" },
        h("h3", null, "Endeks kaynağı"),
        h("p", { class: "muted" }, "Fiyat farkında sözleşmede belirtilen endeks kullanılır (genellikle TÜİK Yİ-ÜFE genel endeksi). Değerleri TÜİK bülteninden elle girebilir ya da TCMB EVDS'den çekebilirsiniz."),
        note(`EVDS seri kodu: ${App.settings.evdsSeries || "TP.TUFE1YI.T1"}. Çekilen değerleri bir kez TÜİK bülteniyle karşılaştırın; seri kodunu Ayarlar'dan değiştirebilirsiniz.`),
        this.missingIndices().length ? note(`Eksik endeks: ${this.missingIndices().map(fmtMonth).join(", ")}. Bu aylara ait hakedişlerde fiyat farkı hesaplanmaz.`, "warn") : note("Gerekli tüm endeksler girildi.", "ok"))));
  },

  // Called by the drawing viewer: adds quantities taken from a drawing as measurement lines.
});
