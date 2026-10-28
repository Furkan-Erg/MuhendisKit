// Isı yalıtımı: layered elements, U value, TS 825 limit check, Glaser condensation analysis.
"use strict";

// Surface resistances from TS EN ISO 6946 (horizontal flow 0.13, upward 0.10, downward 0.17; outside 0.04, ground 0).
const ELEMENT_KINDS = [
  { value: "duvar", label: "Dış duvar", rsi: 0.13, rse: 0.04, limit: "duvar" },
  { value: "cati", label: "Çatı / tavan (ısı akışı yukarı)", rsi: 0.10, rse: 0.04, limit: "cati" },
  { value: "doseme_hava", label: "Dış havaya açık döşeme", rsi: 0.17, rse: 0.04, limit: "doseme" },
  { value: "doseme_toprak", label: "Toprağa temas eden döşeme", rsi: 0.17, rse: 0, limit: "doseme" },
  { value: "duvar_toprak", label: "Toprağa temas eden duvar", rsi: 0.13, rse: 0, limit: "duvar" },
  { value: "pencere", label: "Pencere / dış kapı", window: true, limit: "pencere" },
];
const LIMIT_KEYS = [
  { key: "duvar", label: "Duvar" }, { key: "cati", label: "Çatı / tavan" },
  { key: "doseme", label: "Döşeme / taban" }, { key: "pencere", label: "Pencere" },
];
const ZONES = ["1", "2", "3", "4", "5", "6"];

// TS 825:2024 U values per climate zone (W/m²K) from the reference the municipality designated: tesisat.org's
// online TS 825 program, which lists them as "TS 825 Tavsiye U Değeri" (read 2026-10-28). Façades with ≥ 60 %
// glazing use 25 % lower opaque values and a 1.6 window value. Seeded into Ayarlar once; the engineer can edit them.
const TS825_SOURCE = "TS 825:2024 tavsiye U değerleri, tesisat.org/online/yalitim.html (belediyenin gösterdiği kaynak, 28.10.2026)";
const TS825_DEFAULTS = {
  "1": { duvar: 0.45, cati: 0.35, doseme: 0.40, pencere: 1.8, g: "≤ 0,45" },
  "2": { duvar: 0.40, cati: 0.30, doseme: 0.35, pencere: 1.8, g: "≤ 0,45" },
  "3": { duvar: 0.40, cati: 0.30, doseme: 0.35, pencere: 1.8, g: "≤ 0,45" },
  "4": { duvar: 0.35, cati: 0.25, doseme: 0.30, pencere: 1.8, g: "≥ 0,55" },
  "5": { duvar: 0.25, cati: 0.20, doseme: 0.25, pencere: 1.8, g: "≥ 0,55" },
  "6": { duvar: 0.25, cati: 0.20, doseme: 0.25, pencere: 1.8, g: "≥ 0,55" },
};
const TS825_HIGH_GLAZING = { factor: 0.75, pencere: 1.6 };

/** Settings' U table, filled with the defaults the first time (an engineer's own entries are never overwritten). */
function uLimits() {
  const st = App.settings;
  const lim = st.uLimits ??= { source: "", values: {} };
  const entered = Object.values(lim.values ?? {}).some(z => Object.values(z ?? {}).some(v => v != null && v !== ""));
  if (!lim.seeded && !entered) {
    lim.values = clone(TS825_DEFAULTS);
    lim.highGlazing = clone(TS825_HIGH_GLAZING);
    lim.source = TS825_SOURCE;
    lim.seeded = true;
    App.saveSettings();
  }
  lim.values ??= {};
  lim.highGlazing ??= {};
  return lim;
}

// Typical design values (TS EN ISO 10456 / common declared values). Projects must use the
// manufacturer's declared λ; the engineer edits this library under Ayarlar.
const DEFAULT_MATERIALS = [
  { name: "İç sıva (kireç-çimento harcı)", lambda: 0.87, mu: 20 },
  { name: "Dış sıva (çimento harcı)", lambda: 1.4, mu: 25 },
  { name: "Alçı sıva", lambda: 0.51, mu: 10 },
  { name: "Betonarme", lambda: 2.5, mu: 80 },
  { name: "Yatay delikli tuğla", lambda: 0.45, mu: 5 },
  { name: "Gazbeton (400 kg/m³)", lambda: 0.13, mu: 5 },
  { name: "EPS ısı yalıtım levhası", lambda: 0.040, mu: 60 },
  { name: "Grafitli EPS", lambda: 0.032, mu: 60 },
  { name: "XPS ısı yalıtım levhası", lambda: 0.035, mu: 100 },
  { name: "Taş yünü levha", lambda: 0.038, mu: 1 },
  { name: "Cam yünü", lambda: 0.037, mu: 1 },
  { name: "Poliüretan (PUR) levha", lambda: 0.025, mu: 50 },
  { name: "Şap (çimento esaslı)", lambda: 1.4, mu: 50 },
  { name: "Seramik kaplama", lambda: 1.3, mu: 200 },
  { name: "Ahşap", lambda: 0.13, mu: 50 },
  { name: "Alçı levha", lambda: 0.25, mu: 8 },
  { name: "Bitümlü su yalıtım örtüsü", lambda: 0.23, mu: 50000 },
  { name: "PE buhar kesici folyo", lambda: 0.33, mu: 100000 },
  { name: "Durgun hava boşluğu (≥25 mm)", lambda: null, mu: 1, fixedR: 0.18 },
];
const materials = () => App.settings.materials?.length ? App.settings.materials : DEFAULT_MATERIALS;
const kindOf = el => ELEMENT_KINDS.find(k => k.value === el.kind) ?? ELEMENT_KINDS[0];

function layerR(l) { return l.fixedR != null ? l.fixedR : l.lambda > 0 && l.thickness != null ? l.thickness / 100 / l.lambda : 0; }
function uValue(el) {
  if (kindOf(el).window) return el.fixedU ?? NaN;
  const r = (el.rsi ?? 0) + el.layers.reduce((a, l) => a + layerR(l), 0) + (el.rse ?? 0);
  return r > 0 ? 1 / r : NaN;
}
/** Recommended U for the element in this zone; façades with ≥ 60 % glazing (highGlazing) use the stricter set. */
function limitFor(el, zone, highGlazing = false) {
  const lim = uLimits(), key = kindOf(el).limit;
  const v = lim.values[zone]?.[key];
  if (v == null || v === "") return null;
  if (!highGlazing) return v;
  if (key === "pencere") return lim.highGlazing.pencere ?? null;
  const f = lim.highGlazing.factor;
  return f == null ? null : Math.round(v * f * 100) / 100;
}
// Saturation pressure as in TS EN ISO 13788 (same as Envelope.SaturationPressure).
const psatIso = t => t >= 0 ? 610.5 * Math.exp(17.269 * t / (237.3 + t)) : 610.5 * Math.exp(21.875 * t / (265.5 + t));

function toHostElement(el) {
  return {
    name: el.name, kind: el.kind, rsi: el.rsi ?? 0, rse: el.rse ?? 0,
    fixedU: kindOf(el).window ? (el.fixedU ?? 0) : null,
    layers: kindOf(el).window ? [] : el.layers.map(l => ({
      name: l.name, thicknessM: (l.thickness ?? 0) / 100, lambda: l.lambda ?? 0, mu: l.mu ?? 1, fixedR: l.fixedR ?? null,
    })),
  };
}

const LAYER_COLORS = ["#c9b79c", "#d98c6a", "#e8e1c4", "#9fb7c9", "#b9d4a8", "#e6c27a", "#c7a3c9", "#a7c4c2"];
function layerColor(l, i) {
  const n = trLower(l.name);
  if (/eps|xps|yün|poliüretan|pur|yalıtım/.test(n)) return "#f2d24b";
  if (/beton/.test(n)) return "#a9adb3";
  if (/tuğla/.test(n)) return "#cf7f5c";
  if (/sıva|harç|şap/.test(n)) return "#e3ddd0";
  if (/hava/.test(n)) return "#dfeefc";
  return LAYER_COLORS[i % LAYER_COLORS.length];
}

App.register({
  id: "ins", title: "Isı yalıtımı", icon: "wall", group: "Modüller", keywords: "ts 825 u değeri yoğuşma glaser mantolama",
  selectedId: null,

  mount(el) {
    this.el = el;
    App.command({ title: "Yeni yapı elemanı (duvar, çatı…)", hint: "Isı yalıtımı", icon: "wall", keywords: "ts 825 eleman", run: () => { App.go("ins"); this.addElement("duvar"); } });
    App.command({ title: "Isı yalıtım raporu (PDF)", hint: "Isı yalıtımı", icon: "pdf", keywords: "ts 825 rapor", run: () => { App.go("ins"); this.report(); } });
  },

  get data() { return App.project?.data.insulation; },

  show() {
    const el = clear(this.el);
    el.append(h("div", { class: "page-head" },
      h("div", { class: "grow" }, h("h1", null, "Isı yalıtımı"),
        h("div", { class: "sub" }, "Katman katman U değeri, TS 825 sınır kontrolü ve Glaser yöntemiyle yoğuşma analizi.")),
      App.project && btn("Rapor (PDF)", () => this.report(), { icon: "pdf" })));
    if (App.requireProject(el.appendChild(h("div")), "Yapı elemanları")) return;
    const d = this.data;
    if (!d.elements.find(e => e.id === this.selectedId)) this.selectedId = d.elements[0]?.id ?? null;

    this.sideEl = h("div", { class: "stack" });
    this.mainEl = h("div", { class: "stack" });
    el.lastChild.replaceWith(h("div", { class: "split" }, this.sideEl, this.mainEl));
    this.renderSide();
    this.renderMain();
  },

  renderSide() {
    const d = this.data;
    const changed = () => { App.touch(); this.renderMain(); };
    const limitsSet = ZONES.some(z => Object.values(uLimits().values[z] ?? {}).some(v => typeof v === "number"));
    clear(this.sideEl).append(
      h("div", { class: "card" },
        h("div", { class: "card-head" }, h("h3", null, "Elemanlar")),
        d.elements.length ? h("div", { class: "list" }, d.elements.map(e => {
          const u = uValue(e), lim = limitFor(e, d.zone, d.highGlazing);
          return h("button", { class: "list-item" + (e.id === this.selectedId ? " active" : ""), onclick: () => { this.selectedId = e.id; this.renderSide(); this.renderMain(); } },
            h("div", { class: "grow" }, h("div", { class: "title" }, e.name), h("div", { class: "meta" }, kindOf(e).label)),
            h("span", { class: "badge " + (lim == null ? "" : u <= lim ? "ok" : "bad") }, isFinite(u) ? "U " + fmt(u, 3) : "—"));
        })) : h("div", { class: "empty small" }, "Henüz eleman yok."),
        h("div", { class: "card-body row wrap" },
          ELEMENT_KINDS.filter(k => ["duvar", "cati", "doseme_toprak", "pencere"].includes(k.value)).map(k =>
            btn(k.label.split(" (")[0], () => this.addElement(k.value), { cls: "sm", icon: "plus" })))),
      h("div", { class: "card" },
        h("div", { class: "card-head" }, h("h3", null, "İklim ve bölge")),
        h("div", { class: "card-body stack" },
          selectField(d, "zone", "TS 825:2024 iklim bölgesi", [{ value: null, label: "Seçilmedi" }, ...ZONES.map(z => ({ value: z, label: z + ". bölge" }))], { onChange: () => { App.touch(); this.renderSide(); this.renderMain(); } }),
          h("label", { class: "check" }, h("input", { type: "checkbox", checked: !!d.highGlazing, onchange: e => { d.highGlazing = e.target.checked; App.touch(); this.renderSide(); this.renderMain(); } }),
            "Cephe cam oranı %60 veya üzeri"),
          !limitsSet && note(h("span", null, "Tavsiye U değerleri boş. ", h("a", { onclick: () => App.go("settings") }, "Ayarlar"), "'dan tabloyu doldurun."), "warn"),
          h("div", { class: "label" }, "Yoğuşma analizi koşulları"),
          h("div", { class: "grid g2" },
            numField(d.climate, "insideTemp", "İç sıcaklık", { unit: "°C", onChange: changed }),
            numField(d.climate, "insideRh", "İç bağıl nem", { unit: "%", onChange: changed }),
            numField(d.climate, "outsideTemp", "Dış sıcaklık", { unit: "°C", onChange: changed }),
            numField(d.climate, "outsideRh", "Dış bağıl nem", { unit: "%", onChange: changed })),
          h("div", { class: "hint faint small" }, "Dış koşullar, standarttaki ilgili ay/il değerlerinden girilir."))));
  },

  addElement(kind) {
    if (!App.project) return;
    const k = ELEMENT_KINDS.find(x => x.value === kind);
    const L = (name, thickness, insulation = false) => {
      const m = materials().find(x => x.name === name) ?? {};
      return { id: uid(), name, thickness, lambda: m.lambda ?? null, mu: m.mu ?? 1, fixedR: m.fixedR ?? null, insulation };
    };
    const templates = {
      duvar: [L("İç sıva (kireç-çimento harcı)", 2), L("Yatay delikli tuğla", 19), L("EPS ısı yalıtım levhası", 5, true), L("Dış sıva (çimento harcı)", 0.8)],
      cati: [L("Alçı sıva", 1), L("Betonarme", 14), L("Taş yünü levha", 10, true)],
      doseme_toprak: [L("Seramik kaplama", 1), L("Şap (çimento esaslı)", 5), L("XPS ısı yalıtım levhası", 5, true), L("Bitümlü su yalıtım örtüsü", 0.4), L("Betonarme", 15)],
    };
    const el = { id: uid(), name: k.label.split(" (")[0], kind, rsi: k.rsi ?? null, rse: k.rse ?? null, layers: templates[kind] ?? [], fixedU: k.window ? null : undefined };
    this.data.elements.push(el);
    this.selectedId = el.id;
    App.touch();
    this.show();
  },

  renderMain() {
    const main = clear(this.mainEl);
    const d = this.data;
    const el = d.elements.find(e => e.id === this.selectedId);
    if (!el) {
      main.append(h("div", { class: "card" }, h("div", { class: "empty" }, icon("wall"), h("h3", null, "Bir yapı elemanı ekleyin"),
        h("p", null, "Soldan duvar, çatı, döşeme veya pencere ekleyin. Katmanlar şablondan gelir, istediğiniz gibi değiştirin."))));
      return;
    }
    const kind = kindOf(el);
    const resultHost = h("div", { class: "stack" });
    const chartsHost = h("div", { class: "stack" });
    const changed = debounce(() => { App.touch(); this.evaluate(el, resultHost, chartsHost); this.renderSide(); }, 150);

    const head = h("div", { class: "card" },
      h("div", { class: "card-head" },
        textField(el, "name", null, { bare: true, onChange: changed }),
        h("button", {
          class: "btn ghost sm danger", onclick: async () => {
            if (!await confirmBox("Elemanı sil", `"${el.name}" silinsin mi?`, "Sil", true)) return;
            d.elements = d.elements.filter(x => x !== el); App.touch(); this.show();
          },
        }, icon("trash"), "Sil")),
      h("div", { class: "card-body stack" },
        h("div", { class: "fields" },
          selectField(el, "kind", "Eleman türü", ELEMENT_KINDS.map(k => ({ value: k.value, label: k.label })), {
            onChange: () => { const k = kindOf(el); if (!k.window) { el.rsi = k.rsi; el.rse = k.rse; } App.touch(); this.renderMain(); this.renderSide(); },
          }),
          kind.window ? numField(el, "fixedU", "Pencere/kapı U değeri (Uw)", { unit: "W/m²K", hint: "Üretici beyanı / etiket", onChange: changed }) : [
            numField(el, "rsi", "İç yüzey direnci Rsi", { unit: "m²K/W", onChange: changed }),
            numField(el, "rse", "Dış yüzey direnci Rse", { unit: "m²K/W", onChange: changed }),
          ])));
    main.append(head);

    if (!kind.window) {
      const tbody = h("tbody");
      const lib = materials();
      const datalist = h("datalist", { id: "mat-list" }, lib.map(m => h("option", { value: m.name })));
      const renderLayers = () => clear(tbody).append(el.layers.map((l, i) => {
        const rCell = h("td", { class: "num" }, fmt(layerR(l), 3));
        const upd = () => { rCell.textContent = fmt(layerR(l), 3); changed(); };
        const nameInput = textField(l, "name", null, { bare: true, onChange: upd });
        nameInput.setAttribute("list", "mat-list");
        nameInput.addEventListener("change", () => {
          const m = lib.find(x => x.name === l.name);
          if (m) { l.lambda = m.lambda; l.mu = m.mu; l.fixedR = m.fixedR ?? null; renderLayers(); changed(); }
        });
        return h("tr", null,
          h("td", null, h("span", { class: "layer-swatch", style: { background: layerColor(l, i) } })),
          h("td", { class: "name" }, nameInput),
          h("td", null, numField(l, "thickness", null, { bare: true, cls: "num", onChange: upd })),
          h("td", null, l.fixedR != null ? h("span", { class: "faint small" }, "R sabit") : numField(l, "lambda", null, { bare: true, cls: "num", onChange: upd })),
          h("td", null, numField(l, "mu", null, { bare: true, cls: "num", onChange: upd })),
          rCell,
          h("td", null, h("input", {
            type: "radio", name: "ins-layer", checked: !!l.insulation, title: "Gerekli kalınlık bu katman için hesaplanır",
            onchange: () => { el.layers.forEach(x => x.insulation = x === l); changed(); },
          })),
          h("td", { class: "row", style: { gap: "0" } },
            h("button", { class: "btn ghost icon", title: "Yukarı (içe)", disabled: i === 0, onclick: () => { [el.layers[i - 1], el.layers[i]] = [el.layers[i], el.layers[i - 1]]; renderLayers(); changed(); } }, icon("up")),
            h("button", { class: "btn ghost icon", title: "Aşağı (dışa)", disabled: i === el.layers.length - 1, onclick: () => { [el.layers[i + 1], el.layers[i]] = [el.layers[i], el.layers[i + 1]]; renderLayers(); changed(); } }, icon("down")),
            h("button", { class: "btn ghost icon", title: "Sil", onclick: () => { el.layers.splice(i, 1); renderLayers(); changed(); } }, icon("x"))));
      }));
      renderLayers();
      main.append(h("div", { class: "card" },
        h("div", { class: "card-head" }, h("h3", null, "Katmanlar"), h("span", { class: "muted small" }, "İçten dışa doğru")),
        datalist,
        h("div", { class: "table-wrap" }, h("table", { class: "t layers" },
          h("thead", null, h("tr", null, h("th"), h("th", null, "Malzeme"), h("th", { class: "num" }, "Kalınlık (cm)"), h("th", { class: "num" }, "λ (W/mK)"),
            h("th", { class: "num" }, "μ"), h("th", { class: "num" }, "R (m²K/W)"), h("th", { title: "Yalıtım katmanı" }, "Yal."), h("th"))),
          tbody)),
        h("div", { class: "card-body row" },
          btn("Katman ekle", () => { el.layers.push({ id: uid(), name: "", thickness: null, lambda: null, mu: 1, fixedR: null, insulation: false }); renderLayers(); changed(); }, { cls: "sm", icon: "plus" }),
          h("span", { class: "faint small" }, "λ değerleri tipik değerdir; projede üreticinin beyan ettiği değeri kullanın."))));
    }
    main.append(h("div", { class: "card" }, h("div", { class: "card-head" }, h("h3", null, "Sonuç")), h("div", { class: "card-body" }, resultHost)));
    main.append(chartsHost);
    this.evaluate(el, resultHost, chartsHost);
  },

  async evaluate(el, resultHost, chartsHost) {
    const d = this.data;
    const kind = kindOf(el);
    const c = d.climate;
    const climateOk = [c.insideTemp, c.insideRh, c.outsideTemp, c.outsideRh].every(x => x != null);
    const lim = limitFor(el, d.zone, d.highGlazing);
    const insIndex = el.layers.findIndex(l => l.insulation);
    let r;
    try {
      r = await rpc("ins.evaluate", {
        element: toHostElement(el),
        climate: climateOk ? { insideTemp: c.insideTemp, insideRh: c.insideRh / 100, outsideTemp: c.outsideTemp, outsideRh: c.outsideRh / 100 } : null,
        targetU: lim, insulationLayer: insIndex,
      });
    } catch (e) { clear(resultHost).append(note(e.message, "bad")); return; }
    el.lastResult = { u: r.u, condensation: r.glaser ? r.glaser.condensation.length > 0 : null, surface: r.glaser?.surfaceCondensation ?? null };

    const ok = lim != null && isFinite(r.u) ? r.u <= lim + 1e-9 : null;
    clear(resultHost).append(
      h("div", { class: "stats" },
        stat("U değeri", isFinite(r.u) ? fmt(r.u, 3) : "—", "W/m²K", ok == null ? "hero" : ok ? "ok" : "bad"),
        r.rTotal != null && stat("Toplam ısıl direnç", fmt(r.rTotal, 3), "m²K/W"),
        stat("Tavsiye U (" + (d.zone ? d.zone + ". bölge" : "bölge yok") + ")", lim != null ? fmt(lim, 3) : "Girilmedi", lim != null ? "W/m²K" : null),
        r.required != null && insIndex >= 0 && stat("Gerekli yalıtım", fmt(Math.ceil(r.required * 1000) / 10, 1), "cm",
          ok === false ? "bad" : null)),
      ok === true ? note(`U = ${fmt(r.u, 3)} ≤ ${fmt(lim, 3)} W/m²K: TS 825 tavsiye değerini sağlıyor.`, "ok")
        : ok === false ? note(`U = ${fmt(r.u, 3)} > ${fmt(lim, 3)} W/m²K: TS 825 tavsiye değerini sağlamıyor.` +
          (r.required != null && insIndex >= 0 ? ` "${el.layers[insIndex].name}" en az ${fmt(Math.ceil(r.required * 1000) / 10, 1)} cm olmalı.` : ""), "bad")
          : note(d.zone ? "Bu bölge ve eleman türü için tavsiye U değeri yok (Ayarlar)." : "U kontrolü için iklim bölgesini seçin."),
      !kind.window && insIndex < 0 && lim != null && h("div", { class: "hint faint small" }, "Gerekli kalınlık için katman tablosunda yalıtım katmanını işaretleyin."));

    clear(chartsHost);
    if (kind.window || !el.layers.length) return;
    if (!climateOk) {
      chartsHost.append(note("Yoğuşma analizi için soldaki iç ve dış iklim koşullarını girin.", "warn"));
      return;
    }
    if (r.glaserError) { chartsHost.append(note(r.glaserError, "bad")); return; }
    const g = r.glaser;
    const condText = g.condensation.length
      ? `Yapı bileşeni içinde yoğuşma var: ${g.condensation.map(p => `${p.interface}. arayüz (${fmt(p.x * 100, 1)} cm), ${fmt(p.rate, 3)} g/m²h`).join("; ")}. Kuruma dönemi hesabı ve buhar dengeleyici gerekebilir.`
      : "Yapı bileşeni içinde yoğuşma oluşmuyor.";
    chartsHost.append(
      h("div", { class: "card" },
        h("div", { class: "card-head" }, h("h3", null, "Glaser yoğuşma analizi")),
        h("div", { class: "card-body stack" },
          note(condText, g.condensation.length ? "bad" : "ok"),
          note(`İç yüzey sıcaklığı ${fmt(g.insideSurfaceTemp, 2)} °C, iç havanın çiy noktası ${fmt(g.insideDewPoint, 2)} °C: ` +
            (g.surfaceCondensation ? "yüzeyde yoğuşma riski var." : "yüzeyde yoğuşma yok."), g.surfaceCondensation ? "bad" : "ok"),
          h("div", { class: "grid g2" },
            h("div", null, h("div", { class: "label" }, "Sıcaklık dağılımı (kesit)"), this.sectionChart(el, g)),
            h("div", null, h("div", { class: "label" }, "Buhar basıncı (Glaser diyagramı, x = sd)"), this.glaserChart(el, g))),
          h("div", { class: "legend" },
            h("span", { style: { "--c": "var(--c-temp)" } }, "Sıcaklık"),
            h("span", { style: { "--c": "var(--c-psat)" } }, "Doyma basıncı ps"),
            h("span", { style: { "--c": "var(--c-p)" } }, "Kısmi buhar basıncı p")),
          h("div", { class: "table-wrap" }, h("table", { class: "t" },
            h("thead", null, h("tr", null, ["Arayüz", "Konum (cm)", "sd (m)", "θ (°C)", "ps (Pa)", "p (Pa)"].map((x, i) => h("th", { class: i ? "num" : null }, x)))),
            h("tbody", null, g.points.map((p, i) => h("tr", { class: g.condensation.some(c => c.interface === i) ? "sel" : null },
              h("td", null, i === 0 ? "İç yüzey" : i === g.points.length - 1 ? "Dış yüzey" : `${el.layers[i - 1].name} / ${el.layers[i].name}`),
              h("td", { class: "num" }, fmt(p.x * 100, 1)), h("td", { class: "num" }, fmt(p.sd, 2)), h("td", { class: "num" }, fmt(p.temp, 2)),
              h("td", { class: "num" }, fmt(p.saturationPressure, 0)), h("td", { class: "num" }, fmt(p.vapourPressure, 0))))))))));
  },

  // Cross-section with layer bands (to scale) and the temperature line.
  sectionChart(el, g) {
    const W = 460, H = 260, L = 44, R = 12, T = 12, B = 34;
    const total = g.points.at(-1).x || 1;
    const temps = g.points.map(p => p.temp);
    const tMin = Math.floor(Math.min(...temps) / 5) * 5, tMax = Math.ceil(Math.max(...temps) / 5) * 5 || 5;
    const X = x => L + (W - L - R) * x / total;
    const Y = t => T + (H - T - B) * (tMax - t) / ((tMax - tMin) || 1);
    const svg = s("svg", { class: "chart section-svg", viewBox: `0 0 ${W} ${H}` });
    el.layers.forEach((l, i) => {
      const x0 = X(g.points[i].x), x1 = X(g.points[i + 1].x);
      svg.append(s("rect", { class: "layer", x: x0, y: T, width: Math.max(0.5, x1 - x0), height: H - T - B, fill: layerColor(l, i), opacity: 0.55 }, s("title", null, l.name)));
    });
    for (let t = tMin; t <= tMax; t += 5) {
      svg.append(s("line", { class: "grid", x1: L, x2: W - R, y1: Y(t), y2: Y(t) }), s("text", { x: L - 6, y: Y(t) + 4, "text-anchor": "end" }, t));
    }
    svg.append(s("polyline", { class: "temp", points: g.points.map(p => `${X(p.x)},${Y(p.temp)}`).join(" ") }));
    svg.append(s("text", { x: L, y: H - 10 }, "İç"), s("text", { x: W - R, y: H - 10, "text-anchor": "end" }, "Dış"),
      s("text", { x: (L + W - R) / 2, y: H - 10, "text-anchor": "middle" }, `${fmt(total * 100, 1)} cm`));
    return svg;
  },

  // Classic Glaser diagram: x axis is the cumulative equivalent air thickness sd.
  glaserChart(el, g) {
    const W = 460, H = 260, L = 48, R = 12, T = 12, B = 34;
    const sdTotal = g.points.at(-1).sd || 1;
    // Saturation curve inside each layer: temperature is linear in sd within a homogeneous layer.
    const sat = [];
    for (let i = 0; i < g.points.length - 1; i++) {
      const a = g.points[i], b = g.points[i + 1];
      for (let k = 0; k <= 12; k++) {
        const f = k / 12;
        sat.push({ sd: a.sd + (b.sd - a.sd) * f, p: psatIso(a.temp + (b.temp - a.temp) * f) });
      }
    }
    const pMax = Math.ceil(Math.max(...sat.map(x => x.p), ...g.points.map(p => p.vapourPressure)) / 500) * 500;
    const X = v => L + (W - L - R) * v / sdTotal;
    const Y = p => T + (H - T - B) * (1 - p / pMax);
    const svg = s("svg", { class: "chart", viewBox: `0 0 ${W} ${H}` });
    g.points.forEach((p, i) => { if (i > 0 && i < g.points.length - 1) svg.append(s("line", { class: "grid", x1: X(p.sd), x2: X(p.sd), y1: T, y2: H - B })); });
    for (let p = 0; p <= pMax; p += pMax > 2000 ? 1000 : 500)
      svg.append(s("line", { class: "grid", x1: L, x2: W - R, y1: Y(p), y2: Y(p) }), s("text", { x: L - 6, y: Y(p) + 4, "text-anchor": "end" }, p));
    svg.append(s("line", { class: "axis", x1: L, x2: L, y1: T, y2: H - B }), s("line", { class: "axis", x1: L, x2: W - R, y1: H - B, y2: H - B }));
    svg.append(s("polyline", { class: "psat", points: sat.map(q => `${X(q.sd)},${Y(q.p)}`).join(" ") }));
    svg.append(s("polyline", { class: "pv", points: g.points.map(q => `${X(q.sd)},${Y(q.vapourPressure)}`).join(" ") }));
    for (const c of g.condensation) {
      const p = g.points[c.interface];
      svg.append(s("circle", { class: "cond", cx: X(p.sd), cy: Y(p.vapourPressure), r: 5 }, s("title", null, `Yoğuşma ${fmt(c.rate, 3)} g/m²h`)));
    }
    svg.append(s("text", { x: (L + W - R) / 2, y: H - 10, "text-anchor": "middle" }, `sd (m), toplam ${fmt(sdTotal, 2)} m`), s("text", { x: 4, y: T + 4 }, "Pa"));
    return svg;
  },

  async report() {
    if (!App.project) return;
    const d = this.data;
    if (!d.elements.length) { toast("Raporlanacak eleman yok", "bad"); return; }
    const c = d.climate;
    const climateOk = [c.insideTemp, c.insideRh, c.outsideTemp, c.outsideRh].every(x => x != null);
    const parts = [];
    const summary = [];
    for (const el of d.elements) {
      const lim = limitFor(el, d.zone, d.highGlazing);
      let r;
      try {
        r = await rpc("ins.evaluate", {
          element: toHostElement(el),
          climate: climateOk ? { insideTemp: c.insideTemp, insideRh: c.insideRh / 100, outsideTemp: c.outsideTemp, outsideRh: c.outsideRh / 100 } : null,
          targetU: lim, insulationLayer: el.layers.findIndex(l => l.insulation),
        });
      } catch (e) { fail(e); return; }
      const ok = lim == null ? null : r.u <= lim + 1e-9;
      summary.push(h("tr", null, h("td", null, el.name), h("td", null, kindOf(el).label), h("td", { class: "num" }, fmt(r.u, 3)),
        h("td", { class: "num" }, lim != null ? fmt(lim, 3) : "—"), h("td", null, ok == null ? "—" : ok ? "Uygun" : "Uygun değil"),
        h("td", null, r.glaser ? (r.glaser.condensation.length ? "Var" : "Yok") : "—")));
      parts.push(h("div", { class: "card card-pad stack" },
        h("h2", null, el.name + " · " + kindOf(el).label),
        kindOf(el).window ? h("p", null, `Uw = ${fmt(el.fixedU, 2)} W/m²K (üretici beyanı)`) :
          h("table", { class: "t" },
            h("thead", null, h("tr", null, ["Katman (içten dışa)", "d (cm)", "λ (W/mK)", "μ", "R (m²K/W)"].map((x, i) => h("th", { class: i ? "num" : null }, x)))),
            h("tbody", null,
              h("tr", null, h("td", null, "İç yüzey (Rsi)"), h("td"), h("td"), h("td"), h("td", { class: "num" }, fmt(el.rsi, 2))),
              el.layers.map(l => h("tr", null, h("td", null, l.name), h("td", { class: "num" }, fmt(l.thickness, 1)), h("td", { class: "num" }, l.fixedR != null ? "—" : fmt(l.lambda, 3)),
                h("td", { class: "num" }, fmt(l.mu, 0)), h("td", { class: "num" }, fmt(layerR(l), 3)))),
              h("tr", null, h("td", null, "Dış yüzey (Rse)"), h("td"), h("td"), h("td"), h("td", { class: "num" }, fmt(el.rse, 2))),
              h("tr", { class: "total" }, h("td", null, "Toplam R / U"), h("td"), h("td"), h("td"), h("td", { class: "num" }, `${fmt(r.rTotal, 3)} / ${fmt(r.u, 3)}`)))),
        h("p", null, lim != null ? `Tavsiye U (${d.zone}. bölge${d.highGlazing ? ", cam oranı ≥ %60" : ""}): ${fmt(lim, 3)} W/m²K → ${ok ? "sağlıyor" : "sağlamıyor"}.` : "Tavsiye U değeri yok."),
        r.glaser && h("div", { class: "grid g2" }, this.sectionChart(el, r.glaser), this.glaserChart(el, r.glaser)),
        r.glaser && h("p", null, r.glaser.condensation.length
          ? `Yoğuşma: ${r.glaser.condensation.map(p => `${fmt(p.x * 100, 1)} cm'de ${fmt(p.rate, 3)} g/m²h`).join(", ")}.`
          : "Yapı bileşeni içinde yoğuşma oluşmuyor.")));
    }
    printReport("Isı Yalıtım Hesap Raporu", h("div", { class: "stack" },
      h("p", null, `İklim bölgesi: ${d.zone ?? "seçilmedi"}. Yoğuşma koşulları: iç ${fmt(c.insideTemp, 1)} °C / %${fmt(c.insideRh, 0)}, dış ${fmt(c.outsideTemp, 1)} °C / %${fmt(c.outsideRh, 0)}.`),
      App.settings.uLimits?.source && h("p", { class: "small" }, "U değeri kaynağı: " + App.settings.uLimits.source),
      h("table", { class: "t" }, h("thead", null, h("tr", null, ["Eleman", "Tür", "U", "Tavsiye U", "Durum", "Yoğuşma"].map((x, i) => h("th", { class: i === 2 || i === 3 ? "num" : null }, x)))), h("tbody", null, summary)),
      parts));
  },
});
