// Yaklaşık maliyet (keşif): pozes from the ministry catalogue, measurement lines, discipline summary, repricing, Excel.
"use strict";

function estimateForHost(e) {
  return {
    title: e.title ?? "", preparedBy: e.preparedBy ?? "", date: e.date ?? "", priceSource: e.priceSource ?? "", kdvRate: e.kdvRate ?? 0,
    items: e.items.map(i => ({ id: i.id, pozNo: i.pozNo ?? "", description: i.description ?? "", unit: i.unit ?? "", unitPrice: i.unitPrice ?? 0 })),
    lines: e.lines.map(l => ({
      id: l.id, itemId: l.itemId, period: 1, description: l.description ?? "", count: l.count ?? 1,
      width: l.width ?? null, length: l.length ?? null, height: l.height ?? null, fixed: l.fixed ?? null, deduct: !!l.deduct, source: l.source ?? "",
    })),
  };
}

App.register({
  id: "est", title: "Yaklaşık maliyet", icon: "file", group: "Modüller", keywords: "keşif yaklaşık maliyet metraj poz icmal birim fiyat",
  tab: "poz",

  mount(el) {
    this.el = el;
    for (const [k, v] of Object.entries({ poz: "Pozlar", metraj: "Metraj", icmal: "İcmal" }))
      App.command({ title: "Yaklaşık maliyet: " + v, hint: "Keşif", icon: "file", keywords: "keşif " + v, run: () => { this.tab = k; App.go("est"); } });
  },

  get est() { return App.project?.data.estimate; },

  show() {
    const el = clear(this.el);
    el.append(h("div", { class: "page-head" }, h("div", { class: "grow" }, h("h1", null, "Yaklaşık maliyet"),
      h("div", { class: "sub" }, "Bakanlık birim fiyat kataloğundan pozlar, metraj ve icmal. Birim fiyatlar %25 kâr ve genel gideri içerir, KDV hariçtir."))));
    const body = el.appendChild(h("div"));
    if (App.requireProject(body, "Yaklaşık maliyet")) return;
    const e = this.est;
    const tabs = [["poz", "Pozlar", e.items.length], ["metraj", "Metraj", e.lines.length], ["icmal", "İcmal"]];
    body.append(h("div", { class: "tabs" }, tabs.map(([k, label, n]) =>
      h("button", { class: "tab" + (this.tab === k ? " active" : ""), onclick: () => { this.tab = k; this.show(); } }, label, n ? h("span", { class: "badge" }, n) : null))));
    const host = body.appendChild(h("div"));
    ({ poz: this.renderItems, metraj: this.renderLines, icmal: this.renderSummary })[this.tab].call(this, host);
  },

  // ---- Pozlar ----
  renderItems(el) {
    const e = this.est, t = () => App.touch();
    const used = id => e.lines.filter(l => l.itemId === id).length;
    const tbody = h("tbody");
    const render = () => clear(tbody).append(e.items.length ? e.items.map((it, i) => h("tr", null,
      h("td", null, textField(it, "pozNo", null, { bare: true, onChange: t })),
      h("td", { style: { width: "45%" } }, textField(it, "description", null, { bare: true, onChange: t })),
      h("td", null, textField(it, "unit", null, { bare: true, onChange: t })),
      h("td", null, numField(it, "unitPrice", null, { bare: true, cls: "num", onChange: t })),
      h("td", { class: "num faint" }, used(it.id) || ""),
      h("td", null, h("button", {
        class: "btn ghost icon", title: "Sil", onclick: async () => {
          const n = used(it.id);
          if (n && !await confirmBox("Pozu sil", `Bu poza bağlı ${n} metraj satırı da silinecek.`, "Sil", true)) return;
          e.items.splice(i, 1); e.lines = e.lines.filter(l => l.itemId !== it.id); render(); t();
        },
      }, icon("trash")))))
      : h("tr", null, h("td", { colspan: 6, class: "empty" }, "Henüz poz yok. Sağdaki katalogdan arayıp ekleyin.")));
    render();

    const results = h("div", { class: "list" });
    const q = { text: "", source: e.priceSource || null };
    const search = debounce(async () => {
      try {
        const rows = await rpc("catalog.search", { q: q.text, source: q.source || null, max: 60 });
        clear(results).append(rows.length ? rows.map(r => h("div", { class: "list-item" },
          h("div", { class: "grow" }, h("div", { class: "title", title: r.spec ? `${r.description}\n\n${r.spec}` : r.description }, `${r.pozNo} · ${r.description}`),
            h("div", { class: "meta" }, `${money(r.unitPrice)} / ${r.unit || "?"} · ${r.source}`)),
          btn("Ekle", () => {
            e.items.push({ id: uid(), pozNo: r.pozNo, description: r.description, unit: r.unit, unitPrice: r.unitPrice });
            if (!e.priceSource) e.priceSource = r.source;
            render(); t(); toast(`${r.pozNo} eklendi`);
          }, { cls: "sm", icon: "plus" })))
          : h("div", { class: "empty small" }, "Katalogda sonuç yok."));
      } catch (err) { fail(err); }
    }, 200);
    const sourceHost = h("div");
    rpc("catalog.info").then(info => {
      sourceHost.append(selectField(q, "source", "Fiyat listesi", [{ value: null, label: "Tümü" }, ...info.sources.map(s => ({ value: s.source, label: `${s.source} (${s.count.toLocaleString(TR)})` }))], { onChange: search }));
    }).catch(fail);

    el.append(h("div", { class: "split-wide" },
      h("div", { class: "card" },
        h("div", { class: "card-head" }, h("h3", null, "Keşif pozları"),
          btn("Elle ekle", () => { e.items.push({ id: uid(), pozNo: "", description: "", unit: "", unitPrice: null }); render(); t(); }, { cls: "sm", icon: "plus" })),
        h("div", { class: "table-wrap" }, h("table", { class: "t" },
          h("thead", null, h("tr", null, h("th", null, "Poz no"), h("th", null, "Tanım"), h("th", null, "Birim"), h("th", { class: "num" }, "Birim fiyat (₺)"), h("th", { class: "num", title: "Metraj satırı" }, "Sat."), h("th"))),
          tbody))),
      h("div", { class: "card" },
        h("div", { class: "card-head" }, h("h3", null, "Birim fiyat kataloğu")),
        h("div", { class: "card-body stack" },
          sourceHost,
          textField(q, "text", null, { bare: true, placeholder: "Poz no veya tanım ara (ör. 25.305 polipropilen)", onChange: search }),
          results,
          h("div", { class: "faint small" }, "Katalog Ayarlar sayfasından Bakanlık listesi indirilerek doldurulur.")))));
    search();
  },

  // ---- Metraj ----
  renderLines(el) {
    const e = this.est, t = () => App.touch();
    if (!e.items.length) { el.append(note("Metraj girmek için önce Pozlar sekmesinden poz ekleyin.", "warn")); return; }
    const filter = { item: this.lineItem ?? "all" };
    const itemOpts = e.items.map(i => ({ value: i.id, label: `${i.pozNo} ${i.description}`.trim() || "(adsız poz)" }));
    const unitOf = id => e.items.find(i => i.id === id)?.unit ?? "";
    const tbody = h("tbody");
    const totalCell = h("td", { class: "num" });
    const render = () => {
      const rows = e.lines.filter(l => filter.item === "all" || l.itemId === filter.item);
      clear(tbody).append(rows.length ? rows.map(l => {
        const qCell = h("td", { class: "num" }, fmt(lineQty(l), 3));
        const upd = () => { qCell.textContent = fmt(lineQty(l), 3); renderTotal(); t(); };
        const unitCell = h("td", { class: "faint" }, unitOf(l.itemId));
        const sel = selectField(l, "itemId", null, itemOpts, { bare: true, onChange: () => { unitCell.textContent = unitOf(l.itemId); upd(); } });
        sel.style.maxWidth = "260px";
        return h("tr", null,
          h("td", null, sel),
          h("td", null, textField(l, "description", null, { bare: true, onChange: t })),
          ["count", "width", "length", "height", "fixed"].map(k => h("td", null, numField(l, k, null, { bare: true, cls: "num", onChange: upd }))),
          h("td", null, h("input", { type: "checkbox", checked: !!l.deduct, title: "Düşülecek miktar (−)", onchange: ev => { l.deduct = ev.target.checked; upd(); } })),
          qCell, unitCell,
          h("td", { class: "faint small", title: l.source }, l.source ? "çizim" : ""),
          h("td", null, h("button", { class: "btn ghost icon", onclick: () => { e.lines.splice(e.lines.indexOf(l), 1); render(); t(); } }, icon("x"))));
      }) : h("tr", null, h("td", { colspan: 13, class: "empty" }, "Bu filtrede metraj satırı yok.")));
      renderTotal();
    };
    const renderTotal = () => {
      if (filter.item === "all") { totalCell.textContent = ""; return; }
      totalCell.textContent = fmt(e.lines.filter(l => l.itemId === filter.item).reduce((a, l) => a + lineQty(l), 0), 3) + " " + unitOf(filter.item);
    };
    const add = () => {
      e.lines.push({ id: uid(), itemId: filter.item !== "all" ? filter.item : e.lines.at(-1)?.itemId ?? e.items[0].id, description: "", count: 1, width: null, length: null, height: null, fixed: null, deduct: false, source: "" });
      render(); t();
      tbody.lastChild?.querySelector("td:nth-child(2) input")?.focus();
    };
    const itemFilter = selectField(filter, "item", null, [{ value: "all", label: "Tümü" }, ...itemOpts], { bare: true, onChange: v => { this.lineItem = v === "all" ? null : v; render(); } });
    itemFilter.style.maxWidth = "340px";
    el.append(h("div", { class: "card" },
      h("div", { class: "card-head" }, h("div", { class: "row grow" }, h("span", { class: "label" }, "Poz"), itemFilter),
        btn("Çizimden al", () => App.go("cad"), { cls: "sm", icon: "drawing", title: "Çizim sayfasında Metraj panelini açın ve hedef olarak Yaklaşık maliyeti seçin" }),
        btn("Satır ekle", add, { cls: "primary sm", icon: "plus" })),
      h("div", { class: "table-wrap" }, h("table", { class: "t" },
        h("thead", null, h("tr", null, ["Poz", "Açıklama", "Adet", "En", "Boy", "Yük.", "Miktar (doğrudan)", "(−)", "Miktar", "Birim", "Kaynak", ""].map((x, i) =>
          h("th", { class: i >= 2 && i <= 8 && i !== 7 ? "num" : null }, x)))),
        tbody,
        h("tfoot", null, h("tr", { class: "total" }, h("td", { colspan: 8 }, filter.item === "all" ? "Toplamı görmek için poz seçin" : "Seçili poz toplamı"), totalCell, h("td", { colspan: 3 }))))),
      h("div", { class: "card-body faint small" }, "Miktar = Adet × En × Boy × Yükseklik (boş boyutlar 1 sayılır). \"Miktar (doğrudan)\" doluysa boyutların yerine o kullanılır.")));
    render();
  },

  // ---- İcmal ----
  async renderSummary(el) {
    const e = this.est, t = () => App.touch();
    const resultHost = h("div", { class: "stack" });
    const recalc = debounce(async () => {
      let r;
      try { r = await rpc("est.compute", { estimate: estimateForHost(e) }); } catch (err) { clear(resultHost).append(note(err.message, "bad")); return; }
      this.lastResult = r;
      clear(resultHost).append(
        h("div", { class: "stats" },
          r.groups.map(g => stat(g.discipline, money(g.amount), null)),
          stat("Toplam (KDV hariç)", money(r.total), null),
          stat(`KDV %${fmt(e.kdvRate, 0)}`, money(r.kdv), null),
          stat("Yaklaşık maliyet", money(r.grandTotal), null, "hero")),
        r.warnings.length > 0 && h("div", { class: "stack" }, r.warnings.slice(0, 12).map(w => note(w, "warn")), r.warnings.length > 12 && h("div", { class: "faint small" }, `… ${r.warnings.length - 12} uyarı daha`)),
        h("div", { class: "table-wrap" }, h("table", { class: "t" },
          h("thead", null, h("tr", null, ["Poz no", "İmalatın cinsi", "Birim", "Miktar", "Birim fiyat", "Tutar"].map((x, i) => h("th", { class: i >= 3 ? "num" : null }, x)))),
          h("tbody", null, r.rows.map(x => h("tr", null, h("td", null, x.pozNo), h("td", { title: x.description }, x.description.length > 90 ? x.description.slice(0, 90) + "…" : x.description),
            h("td", null, x.unit), h("td", { class: "num" }, fmt(x.quantity, 3)), h("td", { class: "num" }, money(x.unitPrice)), h("td", { class: "num" }, money(x.amount)))),
            h("tr", { class: "total" }, h("td", { colspan: 5 }, "Toplam (KDV hariç)"), h("td", { class: "num" }, money(r.total)))))));
    }, 150);

    const repriceHost = h("div");
    rpc("catalog.info").then(info => {
      const v = { source: info.sources[0]?.source ?? null };
      repriceHost.append(info.sources.length ? h("div", { class: "row wrap" },
        selectField(v, "source", null, info.sources.map(s => ({ value: s.source, label: s.source })), { bare: true }),
        btn("Bu listeyle yeniden fiyatla", async () => {
          try {
            const preview = await rpc("est.reprice", { estimate: estimateForHost(e), source: v.source, apply: false });
            const changed = preview.changes.filter(c => c.newPrice != null && c.newPrice !== c.oldPrice);
            const missing = preview.changes.filter(c => c.newPrice == null);
            if (!await confirmBox("Yeniden fiyatla", `${v.source}: ${changed.length} pozun fiyatı değişecek` + (missing.length ? `, ${missing.length} poz bu listede yok ve eski fiyatını koruyacak` : "") + ".", "Uygula")) return;
            for (const c of preview.changes) {
              const it = e.items.find(i => i.id === c.id);
              if (it && c.newPrice != null) { it.unitPrice = c.newPrice; if (!it.unit && c.newUnit) it.unit = c.newUnit; }
            }
            e.priceSource = v.source; t(); this.show(); toast(`${changed.length} poz yeniden fiyatlandı`);
          } catch (err) { fail(err); }
        }, { cls: "sm", icon: "refresh" }))
        : h("div", { class: "faint small" }, "Katalog boş: Ayarlar'dan Bakanlık listesi indirin."));
    }).catch(fail);

    el.append(h("div", { class: "grid g2", style: { marginBottom: "16px" } },
      h("div", { class: "card" }, h("div", { class: "card-head" }, h("h3", null, "Keşif bilgileri")),
        h("div", { class: "card-body fields" },
          textField(e, "title", "İşin adı", { placeholder: App.project.data.name, onChange: t }),
          textField(e, "preparedBy", "Hazırlayan", { onChange: t }),
          textField(e, "date", "Tarih", { type: "date", onChange: t }),
          numField(e, "kdvRate", "KDV oranı", { unit: "%", onChange: () => { t(); recalc(); } }),
          h("div", { class: "field" }, h("label", null, "Fiyat kaynağı"), h("div", null, e.priceSource || "—")))),
      h("div", { class: "card" }, h("div", { class: "card-head" }, h("h3", null, "Çıktı ve aktarım")),
        h("div", { class: "card-body stack" },
          h("div", { class: "row wrap" },
            btn("Excel", async () => { try { await rpc("est.export", { estimate: estimateForHost(e), name: e.title || App.project.data.name }); } catch (err) { fail(err); } }, { cls: "sm primary", icon: "excel" }),
            btn("PDF", () => this.pdf(), { cls: "sm", icon: "pdf" }),
            btn("Pozları hakedişe aktar", () => this.toJob(), { cls: "sm", icon: "receipt" })),
          h("div", { class: "label" }, "Başka ayın fiyatlarıyla güncelle"),
          repriceHost))));
    el.append(h("div", { class: "card" }, h("div", { class: "card-head" }, h("h3", null, "İcmal")), h("div", { class: "card-body" }, resultHost)));
    recalc();
  },

  pdf() {
    const r = this.lastResult, e = this.est;
    if (!r) { toast("Önce icmali hesaplayın", "bad"); return; }
    printReport("Yaklaşık Maliyet", h("div", { class: "stack" },
      h("p", null, `${e.title || App.project.data.name} · ${e.date ?? ""} · Fiyat kaynağı: ${e.priceSource || "—"}`),
      h("table", { class: "t" }, h("thead", null, h("tr", null, ["İş grubu", "Tutar"].map((x, i) => h("th", { class: i ? "num" : null }, x)))),
        h("tbody", null, r.groups.map(g => h("tr", null, h("td", null, g.discipline), h("td", { class: "num" }, money(g.amount)))),
          h("tr", { class: "total" }, h("td", null, "Toplam (KDV hariç)"), h("td", { class: "num" }, money(r.total))),
          h("tr", null, h("td", null, `KDV %${fmt(e.kdvRate, 0)}`), h("td", { class: "num" }, money(r.kdv))),
          h("tr", { class: "total" }, h("td", null, "Yaklaşık maliyet"), h("td", { class: "num" }, money(r.grandTotal))))),
      h("table", { class: "t" }, h("thead", null, h("tr", null, ["Poz no", "İmalatın cinsi", "Birim", "Miktar", "Birim fiyat", "Tutar"].map((x, i) => h("th", { class: i >= 3 ? "num" : null }, x)))),
        h("tbody", null, r.rows.map(x => h("tr", null, h("td", null, x.pozNo), h("td", null, x.description), h("td", null, x.unit),
          h("td", { class: "num" }, fmt(x.quantity, 3)), h("td", { class: "num" }, money(x.unitPrice)), h("td", { class: "num" }, money(x.amount)))))),
      h("p", { class: "small" }, "Birim fiyatlar yüklenici kârı ve genel giderleri (%25) içerir, KDV hariçtir.")), { landscape: true });
  },

  // The contract usually takes the estimate's pozes; prices stay editable there (bid prices may differ).
  async toJob() {
    const e = this.est, j = App.project.data.job;
    const missing = e.items.filter(i => !j.items.some(x => x.pozNo && x.pozNo === i.pozNo));
    if (!missing.length) { toast("Tüm pozlar hakedişte zaten var"); return; }
    if (!await confirmBox("Pozları hakedişe aktar", `${missing.length} poz hakediş poz listesine eklenecek (birim fiyatlarıyla). Sözleşme birim fiyatları farklıysa hakedişte düzeltin.`, "Aktar")) return;
    for (const i of missing) j.items.push({ id: uid(), pozNo: i.pozNo, description: i.description, unit: i.unit, unitPrice: i.unitPrice });
    App.touch(); toast(`${missing.length} poz hakedişe eklendi`);
  },
});
