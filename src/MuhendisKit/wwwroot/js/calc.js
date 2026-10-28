// Mekanik hesaplar: one panel per calculator; the math runs in MuhendisKit.Core.
"use strict";

const PIPE_MATERIALS = [
  { value: "celik", label: "Çelik boru (EN 10255 orta seri)" },
  { value: "bakir", label: "Bakır boru (EN 1057)" },
  { value: "ppr", label: "PP-R boru PN20 (SDR 6)" },
];

function sizingTable(rows, cols, suggested) {
  return h("div", { class: "table-wrap" }, h("table", { class: "t" },
    h("thead", null, h("tr", null, cols.map(c => h("th", { class: c.num ? "num" : null }, c.label)))),
    h("tbody", null, rows.map(r => h("tr", { class: r.name === suggested ? "fits" : null },
      cols.map(c => h("td", { class: c.num ? "num" : null }, c.get(r))))))));
}
const fitBadge = r => r.fits ? h("span", { class: "badge ok" }, "Uygun") : h("span", { class: "badge" }, "—");

const CALCS = [
  {
    id: "heatloss", title: "Isı kaybı (oda bazında)", icon: "thermo", keywords: "ts 2164 ısı kaybı kalorifer radyatör oda",
    desc: "TS 2164 yapısında: iletimle kayıp + artırımlar + hava değişimiyle kayıp.",
    formula: "Q₀ = Σ A·U·ΔT\nQ_art = Q₀ · Z%\nQ_hava = 0,34 · n · V · (Ti − Td)\nQ = Q₀ + Q_art + Q_hava   [W]",
    custom: true,
  },
  {
    id: "pipe", title: "Boru çapı (sıcak su)", icon: "pipe", keywords: "boru çap kalorifer debi darcy colebrook basınç kaybı",
    desc: "Darcy–Weisbach ve Colebrook–White ile her boru çapı için hız ve basınç kaybı hesaplanır.",
    formula: "V̇ = Q / (ρ·cp·ΔT)\nR = f/d · ρv²/2   (Colebrook: 1/√f = −2 log(ε/3,7d + 2,51/(Re√f)))",
    init: () => ({ material: "celik", mode: "heat", kw: 50, dt: 20, flow: 2, temp: 70, maxV: 1.0, maxR: 150 }),
    form(v, go) {
      return h("div", { class: "fields" },
        selectField(v, "material", "Boru malzemesi", PIPE_MATERIALS, { onChange: go }),
        selectField(v, "mode", "Giriş", [{ value: "heat", label: "Isı yükünden" }, { value: "flow", label: "Debiden" }], { onChange: () => go(true) }),
        v.mode === "heat" ? [
          numField(v, "kw", "Isı yükü", { unit: "kW", onChange: go }),
          numField(v, "dt", "Gidiş–dönüş farkı", { unit: "K", onChange: go }),
        ] : numField(v, "flow", "Debi", { unit: "m³/h", onChange: go }),
        numField(v, "temp", "Su sıcaklığı (ortalama)", { unit: "°C", onChange: go }),
        numField(v, "maxV", "Azami hız", { unit: "m/s", hint: "Şartnameye göre girin", onChange: go }),
        numField(v, "maxR", "Azami basınç kaybı", { unit: "Pa/m", hint: "Şartnameye göre girin", onChange: go }));
    },
    async compute(v) {
      const flow = v.mode === "heat" ? await rpc("calc.pipeFlow", { kw: v.kw, dt: v.dt, temp: v.temp }) : v.flow;
      const r = await rpc("calc.pipe", { input: { material: v.material, flowM3h: flow, tempC: v.temp, maxVelocity: v.maxV, maxPressureDrop: v.maxR } });
      return { ...r, flow };
    },
    view(r) {
      return [
        h("div", { class: "stats" },
          stat("Debi", fmt(r.flow, 3), "m³/h"),
          stat("Önerilen çap", r.suggested ?? "Uygun çap yok", null, r.suggested ? "hero" : "bad")),
        sizingTable(r.rows.map(x => ({ ...x, name: x.name })), [
          { label: "Çap", get: x => x.name },
          { label: "İç çap (mm)", num: true, get: x => fmt(x.innerDiameterMm, 1) },
          { label: "Hız (m/s)", num: true, get: x => fmt(x.velocity, 2) },
          { label: "Re", num: true, get: x => fmt(x.reynolds, 0) },
          { label: "f", num: true, get: x => fmt(x.friction, 4) },
          { label: "R (Pa/m)", num: true, get: x => fmt(x.pressureDropPerM, 1) },
          { label: "", get: fitBadge },
        ], r.suggested),
      ];
    },
    summary: (r, v) => `${fmt(r.flow, 3)} m³/h → ${r.suggested ?? "uygun çap yok"} (${PIPE_MATERIALS.find(m => m.value === v.material).label})`,
  },
  {
    id: "duct", title: "Kanal çapı (eşit sürtünme)", icon: "wind", keywords: "hava kanalı havalandırma klima eşdeğer çap dikdörtgen",
    desc: "Hedef sürtünme kaybına göre yuvarlak çap ve eşdeğer dikdörtgen kanal (Huebscher).",
    formula: "R = f/D · ρv²/2  →  D çözülür\nDe = 1,30 · (a·b)^0,625 / (a+b)^0,25",
    init: () => ({ flow: 1000, r: 1.0, aspect: 2, rough: 0.09 }),
    form(v, go) {
      return h("div", { class: "fields" },
        numField(v, "flow", "Hava debisi", { unit: "m³/h", onChange: go }),
        numField(v, "r", "Hedef sürtünme kaybı", { unit: "Pa/m", hint: "Şartnameye göre girin", onChange: go }),
        numField(v, "aspect", "Dikdörtgen en/boy oranı", { unit: "b/a", onChange: go }),
        numField(v, "rough", "Pürüzlülük", { unit: "mm", hint: "Galvaniz sac ≈ 0,09 mm", onChange: go }));
    },
    compute: v => rpc("calc.duct", { input: { flowM3h: v.flow, targetPressureDrop: v.r, roughnessMm: v.rough, aspectRatio: v.aspect } }),
    view(r) {
      return h("div", { class: "stats" },
        stat("Hesaplanan çap", fmt(r.roundDiameterMm, 0), "mm"),
        stat("Standart yuvarlak", "Ø" + fmt(r.standardDiameterMm, 0), "mm", "hero"),
        stat("Hız (standart)", fmt(r.velocity, 2), "m/s"),
        stat("Sürtünme (standart)", fmt(r.pressureDropPerM, 2), "Pa/m"),
        stat("Eşdeğer dikdörtgen", `${fmt(r.rectWidthMm, 0)} × ${fmt(r.rectHeightMm, 0)}`, "mm", "hero"),
        stat("Dikdörtgende hız", fmt(r.rectVelocity, 2), "m/s"));
    },
    summary: (r, v) => `${fmt(v.flow, 0)} m³/h, ${fmt(v.r, 2)} Pa/m → Ø${fmt(r.standardDiameterMm, 0)} veya ${fmt(r.rectWidthMm, 0)}×${fmt(r.rectHeightMm, 0)} mm`,
  },
  {
    id: "psychro", title: "Psikrometri", icon: "droplet", keywords: "nemli hava bağıl nem çiy noktası yaş termometre entalpi",
    desc: "Nemli hava özellikleri, ASHRAE Fundamentals 2017 bölüm 1 denklemleri.",
    formula: "W = 0,621945 · pw / (p − pw)\nh = 1,006 t + W (2501 + 1,86 t)   [kJ/kg]",
    init: () => ({ t: 25, mode: "rh", value: 50, altitude: 0 }),
    form(v, go) {
      const label = { rh: "Bağıl nem", wetbulb: "Yaş termometre", dewpoint: "Çiy noktası" };
      return h("div", { class: "fields" },
        numField(v, "t", "Kuru termometre", { unit: "°C", onChange: go }),
        selectField(v, "mode", "İkinci özellik", [{ value: "rh", label: "Bağıl nem" }, { value: "wetbulb", label: "Yaş termometre" }, { value: "dewpoint", label: "Çiy noktası" }], { onChange: () => go(true) }),
        numField(v, "value", label[v.mode], { unit: v.mode === "rh" ? "%" : "°C", onChange: go }),
        numField(v, "altitude", "Rakım", { unit: "m", onChange: go }));
    },
    compute: v => rpc("calc.psychro", { t: v.t, mode: v.mode, value: v.value, altitude: v.altitude ?? 0 }),
    view(r) {
      return h("div", { class: "stats" },
        stat("Bağıl nem", fmt(r.rh * 100, 1), "%", "hero"),
        stat("Özgül nem", fmt(r.w * 1000, 2), "g/kg"),
        stat("Entalpi", fmt(r.enthalpy, 2), "kJ/kg", "hero"),
        stat("Çiy noktası", fmt(r.dewPoint, 2), "°C"),
        stat("Yaş termometre", fmt(r.wetBulb, 2), "°C"),
        stat("Özgül hacim", fmt(r.volume, 4), "m³/kg"),
        stat("Yoğunluk", fmt(r.density, 4), "kg/m³"),
        stat("Buhar basıncı", fmt(r.vapourPressure, 0), "Pa"),
        stat("Atmosfer basıncı", fmt(r.pressure / 1000, 2), "kPa"));
    },
    summary: r => `${fmt(r.dryBulb, 1)} °C / %${fmt(r.rh * 100, 0)}: h = ${fmt(r.enthalpy, 1)} kJ/kg, W = ${fmt(r.w * 1000, 2)} g/kg, çiy ${fmt(r.dewPoint, 1)} °C`,
  },
  {
    id: "tank", title: "Kapalı genleşme tankı", icon: "tank", keywords: "genleşme deposu membranlı ts en 12828 emniyet ventili",
    desc: "TS EN 12828'e göre membranlı kapalı genleşme tankı hacmi.",
    formula: "Ve = e · Vs        Vwr = max(0,005 Vs; 3 L)\np0 = hst/10 + 0,2 bar     pe = psv − 0,5 bar (psv ≤ 5)\nVn = (Ve + Vwr) · (pe + 1) / (pe − p0)",
    init: () => ({ vs: 500, tmax: 80, hst: 10, psv: 3, tfill: 10 }),
    form(v, go) {
      return h("div", { class: "fields" },
        numField(v, "vs", "Tesisattaki su hacmi", { unit: "L", hint: "Kazan + boru + ısıtıcılar", onChange: go }),
        numField(v, "tmax", "En yüksek gidiş suyu sıcaklığı", { unit: "°C", onChange: go }),
        numField(v, "hst", "Statik yükseklik", { unit: "m", hint: "Tank ile en üst nokta arası", onChange: go }),
        numField(v, "psv", "Emniyet ventili açma basıncı", { unit: "bar", onChange: go }),
        numField(v, "tfill", "Dolum suyu sıcaklığı", { unit: "°C", onChange: go }));
    },
    compute: v => rpc("calc.tank", { input: { systemVolumeL: v.vs, maxTempC: v.tmax, staticHeightM: v.hst, safetyValveBar: v.psv, fillTempC: v.tfill } }),
    view(r) {
      return h("div", { class: "stats" },
        stat("Genleşme katsayısı e", fmt(r.expansionCoeff * 100, 2), "%"),
        stat("Genleşme hacmi Ve", fmt(r.expansionVolumeL, 1), "L"),
        stat("Su rezervi Vwr", fmt(r.waterReserveL, 1), "L"),
        stat("Ön basınç p0", fmt(r.prechargeBar, 2), "bar"),
        stat("Son basınç pe", fmt(r.finalPressureBar, 2), "bar"),
        stat("Gerekli hacim Vn", fmt(r.nominalVolumeL, 1), "L"),
        stat("Seçilecek tank", r.standardSizeL ? r.standardSizeL + " L" : "1000 L üstü", null, "hero"));
    },
    summary: (r, v) => `Vs = ${fmt(v.vs, 0)} L, ${fmt(v.tmax, 0)} °C → Vn = ${fmt(r.nominalVolumeL, 1)} L, seçim ${r.standardSizeL ?? ">1000"} L, p0 = ${fmt(r.prechargeBar, 1)} bar`,
  },
  {
    id: "gas", title: "Doğalgaz boru çapı", icon: "flame", keywords: "doğalgaz gaz renouard iç tesisat boru çapı basınç kaybı",
    desc: "Alçak basınçlı iç tesisat, Renouard (doğrusal) formülü. Hat başına hesaplanır.",
    formula: "ΔP = 23200 · ρr · L · Q^1,82 · D^−4,82\n(ΔP mbar, L m, Q m³/h, D mm; Q/D < 150 aralığında geçerli)",
    init: () => ({ material: "celik", mode: "kw", kw: 24, hu: 9.59, q: 2.5, l: 10, maxDp: null, rho: 0.6 }),
    form(v, go) {
      return h("div", { class: "fields" },
        selectField(v, "material", "Boru malzemesi", PIPE_MATERIALS.filter(m => m.value !== "ppr"), { onChange: go }),
        selectField(v, "mode", "Giriş", [{ value: "kw", label: "Cihaz gücünden" }, { value: "q", label: "Debiden" }], { onChange: () => go(true) }),
        v.mode === "kw" ? [
          numField(v, "kw", "Toplam cihaz gücü", { unit: "kW", onChange: go }),
          numField(v, "hu", "Alt ısıl değer", { unit: "kWh/m³", hint: "Dağıtım şirketinin değerini girin", onChange: go }),
        ] : numField(v, "q", "Gaz debisi", { unit: "m³/h", onChange: go }),
        numField(v, "l", "Eşdeğer hat boyu", { unit: "m", hint: "Fittings eşdeğer boyları dahil", onChange: go }),
        numField(v, "maxDp", "İzin verilen basınç kaybı", { unit: "mbar", hint: "Dağıtım şirketi şartnamesinden", onChange: go }),
        numField(v, "rho", "Bağıl yoğunluk ρr", { onChange: go }));
    },
    validate: v => v.maxDp == null ? "İzin verilen basınç kaybını girin (dağıtım şirketi şartnamesi)." : null,
    async compute(v) {
      const q = v.mode === "kw" ? v.kw / v.hu : v.q;
      const r = await rpc("calc.gas", { input: { material: v.material, flowM3h: q, lengthM: v.l, maxDropMbar: v.maxDp, relativeDensity: v.rho } });
      return { ...r, q };
    },
    view(r) {
      return [
        h("div", { class: "stats" },
          stat("Gaz debisi", fmt(r.q, 3), "m³/h"),
          stat("Önerilen çap", r.suggested ?? "Uygun çap yok", null, r.suggested ? "hero" : "bad")),
        sizingTable(r.rows, [
          { label: "Çap", get: x => x.name },
          { label: "İç çap (mm)", num: true, get: x => fmt(x.innerDiameterMm, 1) },
          { label: "ΔP (mbar)", num: true, get: x => fmt(x.dropMbar, 3) },
          { label: "Hız (m/s)", num: true, get: x => fmt(x.velocity, 2) },
          { label: "Q/D", num: true, get: x => h("span", { class: x.qOverD >= 150 ? "badge warn" : null }, fmt(x.qOverD, 2)) },
          { label: "", get: fitBadge },
        ], r.suggested),
      ];
    },
    summary: (r, v) => `${fmt(r.q, 2)} m³/h, L = ${fmt(v.l, 1)} m, ΔPmax ${fmt(v.maxDp, 2)} mbar → ${r.suggested ?? "uygun çap yok"}`,
  },
  {
    id: "units", title: "Birim çevirici", icon: "swap", keywords: "birim dönüştür kcal btu bar psi mmss cfm",
    desc: "Mekanik tesisatta sık kullanılan birimler.",
    custom: true,
  },
];

// Unit factors to the base unit of each quantity (temperature handled separately).
const UNITS = {
  "Güç / ısı yükü": { base: "W", u: { "W": 1, "kW": 1000, "kcal/h": 1.163, "Mcal/h": 1163, "BTU/h": 0.29307107, "Ton (soğutma)": 3516.853, "HP": 745.69987 } },
  "Basınç": { base: "Pa", u: { "Pa": 1, "kPa": 1000, "bar": 1e5, "mbar": 100, "mSS": 9806.65, "mmSS": 9.80665, "psi": 6894.757, "atm": 101325, "mmHg": 133.3224 } },
  "Debi (hacimsel)": { base: "m³/h", u: { "m³/h": 1, "m³/s": 3600, "L/s": 3.6, "L/min": 0.06, "L/h": 0.001, "cfm": 1.6990108, "gpm (US)": 0.22712471 } },
  "Enerji": { base: "kWh", u: { "kWh": 1, "MJ": 1 / 3.6, "kcal": 0.001163, "Mcal": 1.163, "BTU": 0.00029307107, "therm": 29.307107 } },
  "Uzunluk": { base: "m", u: { "mm": 0.001, "cm": 0.01, "m": 1, "km": 1000, "inç": 0.0254, "ft": 0.3048 } },
  "Alan": { base: "m²", u: { "mm²": 1e-6, "cm²": 1e-4, "m²": 1, "ft²": 0.09290304, "inç²": 0.00064516 } },
  "Hacim": { base: "m³", u: { "L": 0.001, "m³": 1, "ft³": 0.028316847, "galon (US)": 0.0037854118 } },
  "Isı iletim katsayısı": { base: "W/m²K", u: { "W/m²K": 1, "kcal/m²h°C": 1.163, "BTU/ft²h°F": 5.678263 } },
  "Sıcaklık": { temp: true, u: { "°C": 1, "K": 1, "°F": 1 } },
};
function convertTemp(v, from, to) {
  const c = from === "K" ? v - 273.15 : from === "°F" ? (v - 32) * 5 / 9 : v;
  return to === "K" ? c + 273.15 : to === "°F" ? c * 9 / 5 + 32 : c;
}

App.register({
  id: "calc", title: "Mekanik hesaplar", icon: "calc", group: "Modüller", keywords: "hesap makinesi",
  states: {},
  results: {},
  selected: "pipe",

  mount(el) {
    this.el = el;
    for (const c of CALCS)
      App.command({ title: c.title, hint: "Hesap", icon: c.icon, keywords: c.keywords, run: () => App.go("calc", c.id) });
  },

  show(id) {
    if (id) this.selected = id;
    const el = clear(this.el);
    el.append(h("div", { class: "page-head" }, h("div", { class: "grow" }, h("h1", null, "Mekanik hesaplar"),
      h("div", { class: "sub" }, "Sonuçlar girdikçe güncellenir. Projeye kaydettiğiniz hesaplar proje dosyasında saklanır."))));
    this.listEl = h("div", { class: "card list" });
    this.panelEl = h("div", { class: "stack" });
    el.append(h("div", { class: "split" }, h("div", { class: "stack" }, this.listEl, this.savedCard()), this.panelEl));
    this.renderList();
    this.renderPanel();
  },

  renderList() {
    clear(this.listEl).append(CALCS.map(c => h("button", {
      class: "list-item" + (c.id === this.selected ? " active" : ""),
      onclick: () => { this.selected = c.id; this.renderList(); this.renderPanel(); },
    }, icon(c.icon), h("div", { class: "grow" }, h("div", { class: "title" }, c.title)))));
  },

  savedCard() {
    const calcs = App.project?.data.calcs ?? [];
    if (!calcs.length) return null;
    return h("div", { class: "card" }, h("div", { class: "card-head" }, h("h3", null, "Projedeki hesaplar"), h("span", { class: "badge" }, calcs.length)),
      h("div", { class: "list" }, calcs.slice().reverse().slice(0, 12).map(x => h("div", { class: "list-item" },
        h("div", { class: "grow" }, h("div", { class: "title" }, x.title), h("div", { class: "meta", title: x.summary }, x.summary)),
        h("button", {
          class: "btn ghost icon", title: "Sil", onclick: () => {
            App.project.data.calcs = calcs.filter(c => c !== x); App.touch(); this.show();
          },
        }, icon("trash"))))));
  },

  renderPanel() {
    const c = CALCS.find(x => x.id === this.selected);
    const panel = clear(this.panelEl);
    if (c.id === "heatloss") return this.renderHeatLoss(panel, c);
    if (c.id === "units") return this.renderUnits(panel, c);

    const v = this.states[c.id] ??= c.init();
    const formHost = h("div");
    const resultHost = h("div", { class: "stack" });
    let seq = 0;
    const run = async () => {
      const my = ++seq;
      const msg = c.validate?.(v);
      if (msg) { clear(resultHost).append(note(msg, "warn")); this.results[c.id] = null; return; }
      try {
        const r = await c.compute(v);
        if (my !== seq) return;
        this.results[c.id] = r;
        clear(resultHost).append(c.view(r, v));
      } catch (e) {
        if (my !== seq) return;
        this.results[c.id] = null;
        clear(resultHost).append(note(e.message, "bad"));
      }
    };
    const go = debounce(run, 180);
    const renderForm = () => clear(formHost).append(c.form(v, rebuild => { if (rebuild === true) renderForm(); go(); }));
    renderForm();

    panel.append(h("div", { class: "card" },
      h("div", { class: "card-head" }, icon(c.icon), h("h2", null, c.title),
        btn("Projeye kaydet", () => this.saveToProject(c, v), { cls: "sm", icon: "save" }),
        btn("PDF", () => this.pdf(c, v), { cls: "sm", icon: "pdf" })),
      h("div", { class: "card-body stack" }, h("p", { class: "muted" }, c.desc), formHost)));
    panel.append(h("div", { class: "card" }, h("div", { class: "card-head" }, h("h3", null, "Sonuç")), h("div", { class: "card-body" }, resultHost)));
    panel.append(h("div", { class: "card card-pad stack" }, h("h3", null, "Yöntem"), h("div", { class: "formula" }, c.formula)));
    run();
  },

  saveToProject(c, v) {
    if (!App.project) { toast("Kaydetmek için önce bir proje açın", "bad"); return; }
    const r = this.results[c.id];
    if (!r) { toast("Kaydedilecek sonuç yok", "bad"); return; }
    App.project.data.calcs.push({ id: uid(), type: c.id, title: c.title, input: clone(v), summary: c.summary(r, v), date: new Date().toISOString() });
    App.touch();
    toast("Hesap projeye kaydedildi");
    this.show();
  },

  pdf(c, v) {
    const r = this.results[c.id];
    if (!r) { toast("Önce hesabı tamamlayın", "bad"); return; }
    const inputs = h("div", { class: "card card-pad" }, h("h3", null, "Girdiler"), c.form(clone(v), () => { }));
    printReport(c.title, h("div", { class: "stack" }, inputs, h("div", { class: "card card-pad" }, c.view(r, v)), h("div", { class: "formula" }, c.formula)));
  },

  // ---- Isı kaybı ----
  renderHeatLoss(panel, c) {
    const local = this.states.heatloss ??= { insideTemp: 20, outsideTemp: null, rooms: [] };
    const st = App.project?.data.heatloss ?? local;
    const changed = () => { if (App.project) App.touch(); recalc(); };
    const elements = App.project?.data.insulation.elements ?? [];
    const resultHost = h("div");
    const roomsHost = h("div", { class: "stack" });

    const newRoom = () => ({ id: uid(), name: `Mahal ${st.rooms.length + 1}`, volume: null, n: null, increment: 0, insideTemp: null,
      surfaces: [{ id: uid(), name: "Dış duvar", area: null, u: null, dt: null }] });

    const recalc = debounce(async () => {
      clear(resultHost);
      if (st.outsideTemp == null) { resultHost.append(note("Dış hesap sıcaklığını girin (TS 2164 / proje şartnamesi).", "warn")); return; }
      if (!st.rooms.length) return;
      const rooms = st.rooms.map(r => {
        const ti = r.insideTemp ?? st.insideTemp;
        return {
          name: r.name, volumeM3: r.volume ?? 0, airChangesPerHour: r.n ?? 0, insideTempC: ti, outsideTempC: st.outsideTemp,
          incrementPercent: r.increment ?? 0,
          surfaces: r.surfaces.map(x => ({ name: x.name, area: x.area ?? 0, u: x.u ?? 0, tempDiff: x.dt ?? (ti - st.outsideTemp) })),
        };
      });
      try {
        const res = await rpc("calc.heatloss", { rooms });
        this.results.heatloss = res;
        const total = res.reduce((a, r) => a + r.totalW, 0);
        resultHost.append(h("div", { class: "stack" },
          h("div", { class: "stats" }, stat("Toplam ısı kaybı", fmt(total / 1000, 2), "kW", "hero"), stat("Toplam", fmt(total * 0.86, 0), "kcal/h")),
          h("div", { class: "table-wrap" }, h("table", { class: "t" },
            h("thead", null, h("tr", null, ["Mahal", "İletim Q₀ (W)", "Artırım (W)", "Hava değişimi (W)", "Toplam (W)"].map((x, i) => h("th", { class: i ? "num" : null }, x)))),
            h("tbody", null, res.map(r => h("tr", null, h("td", null, r.name),
              [r.transmissionW, r.incrementW, r.infiltrationW, r.totalW].map(x => h("td", { class: "num" }, fmt(x, 0))))),
              h("tr", { class: "total" }, h("td", null, "Toplam"), h("td"), h("td"), h("td"), h("td", { class: "num" }, fmt(total, 0))))))));
      } catch (e) { resultHost.append(note(e.message, "bad")); }
    }, 200);

    const renderRooms = () => {
      clear(roomsHost).append(st.rooms.map((r, ri) => {
        const surfRows = h("tbody");
        const renderSurfaces = () => clear(surfRows).append(r.surfaces.map((x, si) => h("tr", null,
          h("td", null, textField(x, "name", null, { bare: true, onChange: changed })),
          h("td", null, numField(x, "area", null, { bare: true, cls: "num", onChange: changed })),
          h("td", null, h("div", { class: "row" }, numField(x, "u", null, { bare: true, cls: "num", onChange: changed }),
            elements.length > 0 && h("select", {
              class: "select", style: { width: "30px", padding: "4px 2px" }, title: "Isı yalıtımı sayfasındaki elemandan al",
              onchange: e => {
                const el = elements.find(q => q.id === e.target.value);
                if (el) { x.u = Number(uValue(el).toFixed(3)); if (x.name.startsWith("Dış duvar") || !x.name) x.name = el.name; renderSurfaces(); changed(); }
                e.target.value = "";
              },
            }, h("option", { value: "" }, "…"), elements.map(el => h("option", { value: el.id }, `${el.name} (U=${fmt(uValue(el), 3)})`))))),
          h("td", null, numField(x, "dt", null, { bare: true, cls: "num", placeholder: "Ti−Td", onChange: changed })),
          h("td", null, h("button", { class: "btn ghost icon", onclick: () => { r.surfaces.splice(si, 1); renderSurfaces(); changed(); } }, icon("x"))))));
        renderSurfaces();
        return h("div", { class: "card" },
          h("div", { class: "card-head" },
            textField(r, "name", null, { bare: true, onChange: changed }),
            h("button", { class: "btn ghost icon", title: "Mahali sil", onclick: () => { st.rooms.splice(ri, 1); renderRooms(); changed(); } }, icon("trash"))),
          h("div", { class: "card-body stack" },
            h("div", { class: "fields" },
              numField(r, "volume", "Hacim", { unit: "m³", onChange: changed }),
              numField(r, "n", "Hava değişimi", { unit: "1/h", hint: "Proje şartnamesi / TS 2164", onChange: changed }),
              numField(r, "increment", "Toplam artırım", { unit: "%", hint: "Yön + kesintili işletme vb.", onChange: changed }),
              numField(r, "insideTemp", "İç sıcaklık", { unit: "°C", placeholder: String(st.insideTemp ?? ""), hint: "Boşsa genel değer", onChange: changed })),
            h("div", { class: "table-wrap" }, h("table", { class: "t" },
              h("thead", null, h("tr", null, h("th", null, "Yüzey"), h("th", { class: "num" }, "Alan (m²)"), h("th", { class: "num" }, "U (W/m²K)"), h("th", { class: "num" }, "ΔT (K)"), h("th"))),
              surfRows)),
            h("div", null, btn("Yüzey ekle", () => { r.surfaces.push({ id: uid(), name: "", area: null, u: null, dt: null }); renderSurfaces(); }, { cls: "sm", icon: "plus" }))));
      }));
    };

    panel.append(h("div", { class: "card" },
      h("div", { class: "card-head" }, icon(c.icon), h("h2", null, c.title),
        App.project ? h("span", { class: "badge ok" }, "Projeye otomatik kaydediliyor") : h("span", { class: "badge warn" }, "Proje açık değil: kaydedilmez"),
        btn("PDF", () => this.heatLossPdf(st), { cls: "sm", icon: "pdf" })),
      h("div", { class: "card-body stack" }, h("p", { class: "muted" }, c.desc),
        note("Dış hesap sıcaklığı, yön ve işletme artırımları TS 2164 tablolarından mühendis tarafından girilir; program bu değerleri varsaymaz."),
        h("div", { class: "fields" },
          numField(st, "insideTemp", "İç hesap sıcaklığı", { unit: "°C", onChange: changed }),
          numField(st, "outsideTemp", "Dış hesap sıcaklığı", { unit: "°C", onChange: changed })))));
    panel.append(roomsHost);
    panel.append(h("div", null, btn("Mahal ekle", () => { st.rooms.push(newRoom()); renderRooms(); changed(); }, { cls: "primary", icon: "plus" })));
    panel.append(h("div", { class: "card" }, h("div", { class: "card-head" }, h("h3", null, "Sonuç")), h("div", { class: "card-body" }, resultHost)));
    panel.append(h("div", { class: "card card-pad stack" }, h("h3", null, "Yöntem"), h("div", { class: "formula" }, c.formula)));
    renderRooms();
    recalc();
  },

  heatLossPdf(st) {
    const res = this.results.heatloss;
    if (!res?.length) { toast("Önce hesabı tamamlayın", "bad"); return; }
    const body = h("div", { class: "stack" },
      h("p", null, `İç hesap sıcaklığı ${fmt(st.insideTemp, 1)} °C, dış hesap sıcaklığı ${fmt(st.outsideTemp, 1)} °C.`),
      st.rooms.map((r, i) => h("div", { class: "card card-pad stack" },
        h("h3", null, r.name),
        h("table", { class: "t" }, h("thead", null, h("tr", null, ["Yüzey", "Alan (m²)", "U (W/m²K)", "ΔT (K)", "Q (W)"].map((x, j) => h("th", { class: j ? "num" : null }, x)))),
          h("tbody", null, r.surfaces.map(x => {
            const dt = x.dt ?? ((r.insideTemp ?? st.insideTemp) - st.outsideTemp);
            return h("tr", null, h("td", null, x.name), h("td", { class: "num" }, fmt(x.area, 2)), h("td", { class: "num" }, fmt(x.u, 3)), h("td", { class: "num" }, fmt(dt, 1)), h("td", { class: "num" }, fmt((x.area ?? 0) * (x.u ?? 0) * dt, 0)));
          }))),
        h("div", null, `Q₀ = ${fmt(res[i].transmissionW, 0)} W · artırım %${fmt(r.increment ?? 0, 0)} = ${fmt(res[i].incrementW, 0)} W · hava değişimi (n = ${fmt(r.n ?? 0, 2)} 1/h, V = ${fmt(r.volume ?? 0, 1)} m³) = ${fmt(res[i].infiltrationW, 0)} W · `, h("b", null, `toplam ${fmt(res[i].totalW, 0)} W`)))),
      h("h2", null, `Bina toplamı: ${fmt(res.reduce((a, r) => a + r.totalW, 0) / 1000, 2)} kW`));
    printReport("Isı Kaybı Hesabı", body);
  },

  // ---- Birim çevirici ----
  renderUnits(panel, c) {
    const v = this.states.units ??= { cat: "Güç / ısı yükü", value: 1, from: "kW" };
    const out = h("div");
    const render = () => {
      const def = UNITS[v.cat];
      if (!(v.from in def.u)) v.from = Object.keys(def.u)[0];
      clear(out).append(h("table", { class: "t" }, h("tbody", null, Object.keys(def.u).map(u => {
        const r = def.temp ? convertTemp(v.value ?? 0, v.from, u) : (v.value ?? 0) * def.u[v.from] / def.u[u];
        return h("tr", { class: u === v.from ? "sel" : null }, h("td", null, u), h("td", { class: "num mono" }, fmtAuto(r, 6)));
      }))));
    };
    const fromHost = h("div");
    const renderFrom = () => clear(fromHost).append(selectField(v, "from", "Birim", Object.keys(UNITS[v.cat].u).map(u => ({ value: u, label: u })), { onChange: render }));
    panel.append(h("div", { class: "card" },
      h("div", { class: "card-head" }, icon(c.icon), h("h2", null, c.title)),
      h("div", { class: "card-body stack" },
        h("div", { class: "fields" },
          selectField(v, "cat", "Büyüklük", Object.keys(UNITS).map(k => ({ value: k, label: k })), { onChange: () => { v.from = Object.keys(UNITS[v.cat].u)[0]; renderFrom(); render(); } }),
          numField(v, "value", "Değer", { onChange: render }),
          fromHost),
        out)));
    renderFrom();
    render();
  },
});
