// Mekanik ve elektrik hesaplar: one panel per calculator; the math runs in MuhendisKit.Core.
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
// Lookup tables a calculator's form loads from the host once (e.g. the rain regulation's C and Manning tables).
const CALC_TABLES = {};
// Several line values in one field: whitespace or ';' separated, Turkish decimal comma allowed.
const parseList = s => String(s ?? "").split(/[\s;]+/).map(parseNum).filter(x => isFinite(x) && x > 0);
async function cityList() { return (CALC_TABLES.cities ??= await rpc("calc.cities")).cities; }
/** Province dropdown (tesisat.org climate table); onChange gets the chosen city object or null. */
async function citySelect(obj, key, label, onChange, filter = () => true) {
  const list = (await cityList()).filter(filter);
  return selectField(obj, key, label, [{ value: null, label: "Seçilmedi" }, ...list.map(c => ({ value: c.name, label: c.name }))], {
    onChange: v => onChange(list.find(c => c.name === v) ?? null), hint: "Kaynak: tesisat.org iklim verileri (Düzce listede yok)",
  });
}

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
    id: "fire", title: "Yangın suyu deposu", icon: "flame", keywords: "yangın dolabı hidrant sprinkler yağmurlama depo bykhy yönetmelik",
    desc: "Binaların Yangından Korunması Hakkında Yönetmelik md. 92, 94, 95 ve Ek-8/A, 8/B, 8/C'ye göre yangın suyu debisi ve depo hacmi (ön hesap).",
    formula: "Yağmurlama: Q = yoğunluk (mm/dak) × koruma alanı (m²)   [Ek-8/B]\n+ dolap ve hidrant ilaveleri   [Ek-8/C]\nV = ΣQ × süre (düşük 30, orta 60, yüksek 90 dk)   [md. 92]\nYalnız dolap: 100 l/dak (yarı-sert) veya 400 l/dak (yassı) × süre   [md. 92 (6), 94]\nYalnız hidrant: en az 1900 l/dak × 90 dk   [md. 92 (7), 95]",
    init: () => ({ hazard: "orta1", sprinkler: true, dry: false, height: 10, cabinets: true, cabinetType: "yari-sert", hydrant: false }),
    form(v, go) {
      const check = (key, label) => h("label", { class: "check" }, h("input", { type: "checkbox", checked: !!v[key], onchange: e => { v[key] = e.target.checked; go(true); } }), label);
      return h("div", { class: "fields" },
        selectField(v, "hazard", "Bina tehlike sınıfı (md. 19)", [
          ["dusuk", "Düşük tehlike"], ["orta1", "Orta tehlike-1"], ["orta2", "Orta tehlike-2"], ["orta3", "Orta tehlike-3"], ["orta4", "Orta tehlike-4"],
          ["yuksek1", "Yüksek tehlike-1"], ["yuksek2", "Yüksek tehlike-2"], ["yuksek3", "Yüksek tehlike-3"], ["yuksek4", "Yüksek tehlike-4"],
        ].map(([value, label]) => ({ value, label })), { onChange: go }),
        check("sprinkler", "Yağmurlama (sprinkler) sistemi"),
        v.sprinkler && [
          selectField(v, "dry", "Yağmurlama sistemi", [{ value: false, label: "Islak / ön uyarılı" }, { value: true, label: "Kuru / değişken" }], { onChange: go }),
          numField(v, "height", "En alt ve en üst başlık arası yükseklik", { unit: "m", hint: "Ek-8/A tablo karşılaştırması için", onChange: go }),
        ],
        check("cabinets", "Yangın dolabı"),
        v.cabinets && selectField(v, "cabinetType", "Dolap tipi", [{ value: "yari-sert", label: "Yarı-sert hortum (TS EN 671-1)" }, { value: "yassi", label: "Yassı hortum (TS EN 671-2)" }], { onChange: go }),
        check("hydrant", "Hidrant sistemi"));
    },
    compute: v => rpc("calc.fireWater", { input: { hazard: v.hazard, sprinkler: !!v.sprinkler, drySprinkler: !!v.dry, sprinklerHeightM: v.sprinkler ? v.height : null, cabinets: !!v.cabinets, cabinetType: v.cabinetType, hydrant: !!v.hydrant } }),
    view(r) {
      return [
        h("div", { class: "stats" },
          stat("Toplam debi", fmt(r.flowLpm, 0), "l/dak"),
          stat("Süre", fmt(r.minutes, 0), "dk"),
          stat("Debi × süre", fmt(r.volumeM3, 1), "m³"),
          r.tableVolumeM3 != null && stat("Ek-8/A tablo", fmt(r.tableVolumeM3, 0), "m³"),
          stat("Tasarım depo hacmi", fmt(r.designVolumeM3, 1), "m³", "hero")),
        h("div", { class: "table-wrap" }, h("table", { class: "t" },
          h("thead", null, h("tr", null, ["Bileşen", "Debi (l/dak)", "Süre (dk)", "Hacim (m³)", "Kaynak"].map((x, i) => h("th", { class: i && i < 4 ? "num" : null }, x)))),
          h("tbody", null, r.parts.map(p => h("tr", null, h("td", null, p.name), h("td", { class: "num" }, fmt(p.flowLpm, 0)),
            h("td", { class: "num" }, fmt(p.minutes, 0)), h("td", { class: "num" }, fmt(p.volumeM3, 1)), h("td", { class: "faint small" }, p.source)))))),
        r.requirements.length > 0 && h("div", { class: "stack" }, h("div", { class: "label" }, "Yönetmelik şartları"), r.requirements.map(x => note(x))),
        r.notes.map(x => note(x)),
      ];
    },
    summary: (r, v) => `${v.hazard}: ${fmt(r.flowLpm, 0)} l/dak, depo ${fmt(r.designVolumeM3, 1)} m³`,
  },
  {
    id: "radiator", title: "Radyatör seçimi", icon: "thermo", keywords: "radyatör panel dilim seçim en 442 dt50 ısı verimi",
    desc: "Mahal ısı yükü ve sistem sıcaklıklarına göre katalogda aranacak ΔT50 (75/65/20 °C) ısı verimi; EN 442 üs formülü.",
    formula: "ΔTlm = (Tg − Td) / ln((Tg − Ti) / (Td − Ti))\nf = (ΔTlm / 49,83)^n\nQ50 = Q / f    (katalogdaki ΔT50 değeri en az bu kadar olmalı)",
    init: () => ({ q: 1500, tg: 70, td: 55, ti: 20, n: 1.3, per: null }),
    form(v, go) {
      return h("div", { class: "fields" },
        numField(v, "q", "Mahal ısı yükü", { unit: "W", hint: "Isı kaybı hesabından", onChange: go }),
        numField(v, "tg", "Gidiş suyu", { unit: "°C", onChange: go }),
        numField(v, "td", "Dönüş suyu", { unit: "°C", onChange: go }),
        numField(v, "ti", "Oda sıcaklığı", { unit: "°C", onChange: go }),
        numField(v, "n", "Radyatör üssü n", { hint: "Üretici kataloğundan; girilmezse 1,3 kabul edilmiştir", onChange: go }),
        numField(v, "per", "Katalog ΔT50 verimi (isteğe bağlı)", { unit: "W / m, dilim ya da adet", onChange: go }));
    },
    compute: v => rpc("calc.radiator", { input: { heatW: v.q, supplyC: v.tg, returnC: v.td, roomC: v.ti, exponent: v.n ?? 1.3, catalogPerUnitW: v.per } }),
    view(r) {
      return [
        h("div", { class: "stats" },
          stat("ΔTlm", fmt(r.deltaTLog, 2), "K"),
          stat("Düzeltme katsayısı f", fmt(r.factor, 3)),
          stat("Gereken ΔT50 verimi", fmt(r.requiredNominalW, 0), "W", "hero"),
          r.units != null && stat("Gereken miktar", fmt(r.units, 2), "m / dilim / adet")),
        r.warning && note(r.warning, "warn"),
      ];
    },
    summary: (r, v) => `${fmt(v.q, 0)} W, ${fmt(v.tg, 0)}/${fmt(v.td, 0)}/${fmt(v.ti, 0)} °C → ΔT50 ${fmt(r.requiredNominalW, 0)} W`,
  },
  {
    id: "booster", title: "Hidrofor", icon: "droplet", keywords: "hidrofor pompa basınç tank genleşme membran kullanma suyu zon",
    desc: "Hidrofor devreye girme/çıkma basıncı ve membranlı tank hacmi; Bakanlık Makina Tesisatı Genel Teknik Şartnamesi basınç ölçütleriyle kontrol.",
    formula: "p_giriş = H / 10,2 + kayıplar + musluk akma basıncı (şartname: 1 bar)\np_çıkış = p_giriş + Δp\nVd = Q · (3600 / z) / 4      V = Vd · (p_çıkış + 1) / (p_çıkış − p_giriş)\nŞartname: statik basınç 1–4 bar, en çok 60 m'de (20 kat) bir zon",
    init: () => ({ q: 3, h: 15, loss: 0.5, tap: 1, dp: 1, z: 20, pumps: 1, below: 0 }),
    form(v, go) {
      return h("div", { class: "fields" },
        numField(v, "q", "Pik debi", { unit: "m³/h", hint: "Eş zamanlı kullanım debisi", onChange: go }),
        numField(v, "h", "Hidrofor – en üst musluk kot farkı", { unit: "m", onChange: go }),
        numField(v, "loss", "Boru, sayaç ve armatür kayıpları", { unit: "bar", hint: "Şartname: su sayacında en çok 0,5 bar", onChange: go }),
        numField(v, "tap", "En üst muslukta akma basıncı", { unit: "bar", hint: "Şartname: normal musluklarda 1 bar", onChange: go }),
        numField(v, "dp", "Devreye girme–çıkma farkı", { unit: "bar", onChange: go }),
        numField(v, "z", "Saatte en çok kalkış", { hint: "Pompa motoru üreticisinden", onChange: go }),
        numField(v, "pumps", "Asıl pompa sayısı", { onChange: go }),
        numField(v, "below", "Hidrofor altındaki en alt musluk derinliği", { unit: "m", onChange: go }));
    },
    compute: v => rpc("calc.booster", { input: { flowM3h: v.q, heightM: v.h, lossBar: v.loss, tapPressureBar: v.tap, differentialBar: v.dp, startsPerHour: v.z, pumps: v.pumps, lowestFixtureBelowM: v.below } }),
    view(r) {
      return [
        h("div", { class: "stats" },
          stat("Devreye girme", fmt(r.cutInBar, 2), "bar"),
          stat("Devreden çıkma", fmt(r.cutOutBar, 2), "bar"),
          stat("Pompa basma yüksekliği", fmt(r.pumpHeadM, 1), "mSS"),
          stat("Çekilen hacim", fmt(r.drawdownL, 0), "L"),
          stat("Tank hacmi", fmt(r.tankVolumeL, 0), "L", "hero"),
          stat("En alt muslukta statik", fmt(r.maxStaticAtLowestBar, 2), "bar", r.maxStaticAtLowestBar > 4 ? "bad" : null)),
        r.warnings.map(w => note(w, "warn")),
      ];
    },
    summary: (r, v) => `${fmt(v.q, 2)} m³/h, H ${fmt(v.h, 0)} m → ${fmt(r.cutInBar, 1)}/${fmt(r.cutOutBar, 1)} bar, tank ${fmt(r.tankVolumeL, 0)} L`,
  },
  {
    id: "rain", title: "Yağmur suyu debisi ve boru çapı", icon: "droplet", keywords: "yağmur suyu rasyonel yöntem akış katsayısı kanal boru manning parsel",
    desc: "Rasyonel yöntemle yağmur suyu debisi ve kısmi dolu Manning ile boru/kanal çapı; Yağmursuyu Toplama, Depolama ve Deşarj Sistemleri Hakkında Yönetmelik (2017) EK-1.",
    formula: "r = 166,7 · i   (l/s·ha; i mm/dak)   [denklem 1.2]\nQ = C · r · A   (l/s; A ha)   [denklem 1.8]\nQ = (1/n) · A · R^(2/3) · S^(1/2)   (kısmi dolu daire kesit)\nSınırlar: hız 0,5–5 m/s, doluluk ≤ %90; beton kanalda Çizelge 1.4 eğimleri",
    init: () => ({ area: 1000, areaType: "apartman", c: 0.7, city: null, i: null, material: "beton", slope: 1, fill: 0.9 }),
    async form(v, go) {
      CALC_TABLES.rain ??= await rpc("calc.rainTables");
      const t = CALC_TABLES.rain;
      const at = t.areaTypes.find(a => a.id === v.areaType);
      return h("div", { class: "fields" },
        numField(v, "area", "Drenaj alanı", { unit: "m²", onChange: go }),
        selectField(v, "areaType", "Alan tipi (Çizelge 1.3)", t.areaTypes.map(a => ({ value: a.id, label: `${a.name} (${fmt(a.cMin, 2)}–${fmt(a.cMax, 2)})` })), {
          onChange: () => { const a = t.areaTypes.find(x => x.id === v.areaType); v.c = a.cMax; go(true); },
        }),
        numField(v, "c", "Akış katsayısı C", { hint: at ? `Tablo aralığı ${fmt(at.cMin, 2)}–${fmt(at.cMax, 2)}; varsayılan üst sınır` : null, onChange: go }),
        await citySelect(v, "city", "İl (yağmur verimi)", () => go(true), c => c.rainYieldLpsHa != null),
        !v.city && numField(v, "i", "Yağış şiddeti i", { unit: "mm/dak", hint: "İl seçmezseniz yerel MGM verisinden girin", onChange: go }),
        selectField(v, "material", "Boru malzemesi (Çizelge 1.5)", t.materials.map(m => ({ value: m.id, label: `${m.name} (n = ${fmt(m.manning, 3)})` })), { onChange: go }),
        numField(v, "slope", "Eğim", { unit: "%", hint: "Bina yatay boruları: şartnameye göre en az %1", onChange: go }),
        numField(v, "fill", "En büyük doluluk oranı", { hint: "Yönetmelik: %90'a kadar", onChange: go }));
    },
    validate: v => v.i == null && !v.city ? "İl seçin ya da yağış şiddetini girin (yerel MGM verisi)." : null,
    async compute(v) {
      const city = v.city ? (await cityList()).find(c => c.name === v.city) : null;
      return rpc("calc.rain", { input: { areaM2: v.area, c: v.c, intensityMmMin: v.i ?? 0, yieldLpsHa: city?.rainYieldLpsHa ?? null, material: v.material, slopePercent: v.slope, maxFilling: v.fill } });
    },
    view(r) {
      return [
        h("div", { class: "stats" },
          stat("Yağmur verimi r", fmt(r.yieldLpsHa, 1), "l/s·ha"),
          stat("Debi Q", fmt(r.flowLps, 2), "l/s"),
          stat("Önerilen iç çap", r.suggested ? "Ø" + r.suggested + " mm" : "Uygun çap yok", null, r.suggested ? "hero" : "bad")),
        sizingTable(r.rows.map(x => ({ ...x, name: x.diameterMm })), [
          { label: "İç çap (mm)", get: x => "Ø" + x.diameterMm },
          { label: "Kapasite (l/s)", num: true, get: x => fmt(x.capacityLps, 1) },
          { label: "Doluluk", num: true, get: x => x.filling != null ? "%" + fmt(x.filling * 100, 0) : "—" },
          { label: "Hız (m/s)", num: true, get: x => x.velocity != null ? fmt(x.velocity, 2) : "—" },
          { label: "Not", get: x => x.warning ? h("span", { class: "faint small" }, x.warning) : "" },
          { label: "", get: fitBadge },
        ], r.suggested),
        note("Çaplar iç çap olarak değerlendirilir; plastik borularda katalog iç çapını kontrol edin.", ""),
      ];
    },
    summary: (r, v) => `A ${fmt(v.area, 0)} m², C ${fmt(v.c, 2)}, i ${fmt(v.i, 2)} mm/dak → Q ${fmt(r.flowLps, 1)} l/s, ${r.suggested ? "Ø" + r.suggested : "uygun çap yok"}`,
  },
  {
    id: "gravity", title: "Yağmur / pis su borusu (doluluk)", icon: "droplet", keywords: "yağmur suyu pis su atık su boru çapı doluluk oranı eğim din 1986 kanalizasyon",
    desc: "Doluluk oranına göre yatay yağmur ve pis su borusu çapı (DIN 1986-100 yöntemi; tesisat.org aracıyla aynı sonuçları verir). Her satır bir hat.",
    formula: "Prandtl–Colebrook, kb = 1,0 mm, ν = 1,31·10⁻⁶ m²/s, kısmi dolu kesitin hidrolik çapıyla\nDoluluk: yağmur suyu h/d = 0,7, pis su h/d = 0,5\nPis su debisi Q = 0,5 × yük birimi\nHız v = Q / (π/4 · (0,975·DN)²)",
    init: () => ({ rain: true, slope: 100, values: "2 5 10 20" }),
    form(v, go) {
      return h("div", { class: "fields" },
        selectField(v, "rain", "Tesisat", [{ value: true, label: "Yağmur suyu (debi, l/s)" }, { value: false, label: "Pis su (yük birimi)" }], { onChange: () => go(true) }),
        selectField(v, "slope", "Eğim", [20, 25, 30, 33, 40, 50, 60, 67, 70, 80, 100, 125, 150, 175, 200].map(a => ({ value: a, label: `1:${a} (%${fmt(100 / a, 2)})` })), { onChange: go }),
        textField(v, "values", v.rain ? "Hat debileri (l/s)" : "Hat yük birimleri", { placeholder: "Boşluk ya da noktalı virgülle ayırın: 2 5 10,5", onChange: go }));
    },
    validate: v => parseList(v.values).length ? null : "En az bir hat değeri girin.",
    compute: v => rpc("calc.gravityPipes", { rain: v.rain, slopeA: v.slope, values: parseList(v.values) }),
    view(r, v) {
      return [
        h("div", { class: "table-wrap" }, h("table", { class: "t" },
          h("thead", null, h("tr", null, ["Hat", v.rain ? "Debi (l/s)" : "Yük birimi", "Debi (l/s)", "Çap", "Kapasite (l/s)", "Hız (m/s)"].map((x, i) => h("th", { class: i ? "num" : null }, x)))),
          h("tbody", null, r.map((l, i) => h("tr", null, h("td", null, i + 1), h("td", { class: "num" }, fmt(l.input, 2)), h("td", { class: "num" }, fmt(l.flowLps, 2)),
            h("td", { class: "num" }, l.dn ? h("b", null, "Ø" + l.dn) : h("span", { class: "badge bad" }, "Hidrolik hesap gerekir")),
            h("td", { class: "num" }, l.capacityLps != null ? fmt(l.capacityLps, 1) : "—"), h("td", { class: "num" }, l.velocity != null ? fmt(l.velocity, 2) : "—")))))),
        note("Kaynak yöntem: tesisat.org Doluluk Oranına Göre Yağmursuyu ve Atık Su Boru Çapı Hesabı (izinle; 600 örnekle karşılaştırıldı)."),
      ];
    },
    summary: (r, v) => `${v.rain ? "Yağmur" : "Pis su"} 1:${v.slope}: ` + r.map(l => l.dn ? "Ø" + l.dn : "hidrolik").join(", "),
  },
  {
    id: "fixture", title: "Atık su borusu (sarfiyat birimi)", icon: "droplet", keywords: "atık su pis su sarfiyat birimi vitrifiye boru çapı kolon yatay",
    desc: "Vitrifiye sarfiyat birimlerinin toplamına göre yatay boru ve kolon çapı (tesisat.org atık su tablosu).",
    formula: "Hat SB = Σ (adet × vitrifiye SB)\nYatay boru: pürüz katsayısı ve eğime göre tablo kapasitesi ≥ SB olan en küçük çap\nKolon: Ø70 ≤ 40, Ø100 ≤ 150, Ø125 ≤ 400, Ø150 ≤ 900, Ø200 ≤ 2200 SB",
    init: () => ({ horizontal: true, rough: 0.35, slope: 100, values: "", counts: {} }),
    async form(v, go) {
      const fixtures = CALC_TABLES.fixtures ??= await rpc("calc.fixtures");
      const total = () => fixtures.reduce((a, f) => a + (v.counts[f.name] ?? 0) * f.sb, 0);
      const totalCell = h("b", null, fmt(total(), 1));
      const counter = h("div", { class: "table-wrap", style: { maxHeight: "260px" } }, h("table", { class: "t" },
        h("thead", null, h("tr", null, h("th", null, "Vitrifiye"), h("th", { class: "num" }, "SB"), h("th", { class: "num" }, "Adet"))),
        h("tbody", null, fixtures.map(f => h("tr", null, h("td", null, f.name), h("td", { class: "num" }, fmt(f.sb, 0)),
          h("td", null, numField(v.counts, f.name, null, { bare: true, cls: "num", placeholder: "0", onChange: () => { totalCell.textContent = fmt(total(), 1); } })))))));
      return h("div", { class: "stack" },
        h("div", { class: "fields" },
          selectField(v, "horizontal", "Boru", [{ value: true, label: "Yatay" }, { value: false, label: "Düşey (kolon)" }], { onChange: () => go(true) }),
          v.horizontal && selectField(v, "rough", "Pürüz katsayısı", [{ value: 0.35, label: "0,35" }, { value: 0.25, label: "0,25" }], { onChange: go }),
          v.horizontal && selectField(v, "slope", "Eğim", [{ value: 100, label: "1:100" }, { value: 50, label: "1:50" }], { onChange: go }),
          textField(v, "values", "Hat sarfiyat birimleri", { placeholder: "Boşlukla ayırın: 12 40 150", onChange: go })),
        h("details", null, h("summary", null, "Vitrifiye sayımından sarfiyat birimi"), counter,
          h("div", { class: "row" }, h("span", null, "Toplam: "), totalCell, h("span", null, " SB"),
            btn("Hat olarak ekle", () => { v.values = (v.values + " " + fmt(total(), 1).replace(".", "")).trim(); v.counts = {}; go(true); }, { cls: "sm", icon: "plus" }))));
    },
    validate: v => parseList(v.values).length ? null : "En az bir hat sarfiyat birimi girin (ya da vitrifiye sayımından ekleyin).",
    compute: v => rpc("calc.fixtureUnits", { horizontal: v.horizontal, roughness: v.rough, slopeA: v.slope, values: parseList(v.values) }),
    view(r) {
      return [
        h("div", { class: "table-wrap" }, h("table", { class: "t" },
          h("thead", null, h("tr", null, ["Hat", "SB", "Çap", "Tablo kapasitesi (SB)", "Debi (l/s)"].map((x, i) => h("th", { class: i ? "num" : null }, x)))),
          h("tbody", null, r.map((l, i) => h("tr", null, h("td", null, i + 1), h("td", { class: "num" }, fmt(l.sb, 1)),
            h("td", { class: "num" }, l.dn ? h("b", null, "Ø" + l.dn) : h("span", { class: "badge bad" }, "Hidrolik hesap gerekir")),
            h("td", { class: "num" }, l.capacitySb != null ? fmt(l.capacitySb, 0) : "—"), h("td", { class: "num" }, l.capacityLps != null ? fmt(l.capacityLps, 2) : "—")))))),
        note("Kaynak: tesisat.org Atık Su Boru Çapı Hesabı tabloları (izinle)."),
      ];
    },
    summary: (r, v) => `${v.horizontal ? "Yatay" : "Kolon"}: ` + r.map(l => `${fmt(l.sb, 0)} SB → ${l.dn ? "Ø" + l.dn : "hidrolik"}`).join(", "),
  },
  {
    id: "chimney", title: "Baca çapı (pratik)", icon: "flame", keywords: "baca çapı kesit kazan doğal çekiş kombi",
    desc: "Doğal çekişli bacalarda yakıt, yükseklik ve kazan kapasitesine göre yaklaşık baca kesiti (tesisat.org pratik baca hesabı). Ön boyutlandırma içindir.",
    formula: "Fb = k · Q / √H   (Q kcal/h, H m, Fb cm²)\nD = √(4 · Fb / π)   (cm)\nDoğalgazda 30–70 kW için; 70 kW ve üzeri TS 2165 (DIN 4705) / TS EN 13384-1",
    init: () => ({ kw: 50, h: 10, group: "Gaz Yakıtlı", fuel: "Doğalgaz" }),
    async form(v, go) {
      const fuels = CALC_TABLES.chimney ??= await rpc("calc.chimneyFuels");
      if (!fuels[v.group]?.[v.fuel]) v.fuel = Object.keys(fuels[v.group])[0];
      return h("div", { class: "fields" },
        numField(v, "kw", "Kazan kapasitesi", { unit: "kW", hint: v.kw ? `${fmt(v.kw * 859.845, 0)} kcal/h` : null, onChange: () => go(true) }),
        numField(v, "h", "Bina (baca) yüksekliği", { unit: "m", onChange: go }),
        selectField(v, "group", "Yakıt sistemi", Object.keys(fuels).map(g => ({ value: g, label: g })), { onChange: () => go(true) }),
        selectField(v, "fuel", "Yakıt", Object.entries(fuels[v.group]).map(([f, k]) => ({ value: f, label: `${f} (k = ${fmt(k, 3)})` })), { onChange: go }));
    },
    compute: v => rpc("calc.chimney", { input: { capacityKcalh: v.kw * 859.845, heightM: v.h, fuelGroup: v.group, fuel: v.fuel } }),
    view(r) {
      return [
        h("div", { class: "stats" },
          stat("Katsayı k", fmt(r.k, 3)),
          stat("Kesit Fb", fmt(r.sectionCm2, 0), "cm²"),
          stat("Baca iç çapı", fmt(r.diameterCm, 1), "cm", "hero")),
        r.warning && note(r.warning, "warn"),
      ];
    },
    summary: (r, v) => `${fmt(v.kw, 0)} kW, H ${fmt(v.h, 1)} m, ${v.fuel} → Ø${fmt(r.diameterCm, 1)} cm`,
  },
  {
    id: "vdrop", title: "Gerilim düşümü ve kesit", icon: "bolt", keywords: "elektrik kablo kesit gerilim düşümü linye kolon",
    desc: "Aktif güç ile omik gerilim düşümü; her standart kesit için % düşüm ve sınırı sağlayan en küçük kesit.",
    formula: "Monofaze: %e = 200 · L · P / (k · S · U²)\nTrifaze:  %e = 100 · L · P / (k · S · U²)\n(P W, L tek yön m, S mm², U V; k bakır 56, alüminyum 35 m/Ωmm²)",
    init: () => ({ phase: "mono", kw: 3, l: 25, u: 230, material: "cu", limit: 1.5 }),
    form(v, go) {
      return h("div", { class: "fields" },
        selectField(v, "phase", "Faz", [{ value: "mono", label: "Monofaze" }, { value: "tri", label: "Trifaze" }], { onChange: () => { v.u = v.phase === "tri" ? 400 : 230; go(true); } }),
        numField(v, "kw", "Güç (talep gücü)", { unit: "kW", onChange: go }),
        numField(v, "l", "Hat uzunluğu (tek yön)", { unit: "m", onChange: go }),
        numField(v, "u", v.phase === "tri" ? "Fazlar arası gerilim" : "Faz-nötr gerilimi", { unit: "V", onChange: go }),
        selectField(v, "material", "İletken", [{ value: "cu", label: "Bakır (k = 56)" }, { value: "al", label: "Alüminyum (k = 35)" }], { onChange: go }),
        selectField(v, "limit", "İzin verilen düşüm", [{ value: 1.5, label: "%1,5 aydınlatma / priz" }, { value: 3, label: "%3 motor" }, { value: 5, label: "%5" }], {
          onChange: go, hint: "Yapı bağlantı kutusundan sonra; Elektrik İç Tesisleri Yönetmeliği md. 57 (EMO aktarımı, resmi metinle doğrulanmadı)",
        }));
    },
    compute: v => rpc("calc.voltageDrop", { input: { phase: v.phase, powerKw: v.kw, lengthM: v.l, voltageV: v.u, material: v.material, limitPercent: v.limit } }),
    view(r) {
      return [
        h("div", { class: "stats" }, stat("Önerilen kesit", r.suggested != null ? fmt(r.suggested, 1) + " mm²" : "300 mm² yetmiyor", null, r.suggested != null ? "hero" : "bad")),
        sizingTable(r.rows.map(x => ({ ...x, name: x.sectionMm2 })), [
          { label: "Kesit (mm²)", get: x => fmt(x.sectionMm2, 1) },
          { label: "Düşüm (%)", num: true, get: x => fmt(x.dropPercent, 3) },
          { label: "Düşüm (V)", num: true, get: x => fmt(x.dropV, 2) },
          { label: "", get: fitBadge },
        ], r.suggested),
        note("Kesit ayrıca akım taşıma kapasitesine ve koruma elemanına göre kontrol edilmelidir; bu hesap yalnız gerilim düşümüne bakar.", "warn"),
      ];
    },
    summary: (r, v) => `${v.phase === "tri" ? "3~" : "1~"} ${fmt(v.kw, 2)} kW, ${fmt(v.l, 0)} m, %${fmt(v.limit, 1)} → ${r.suggested != null ? fmt(r.suggested, 1) + " mm²" : "uygun kesit yok"}`,
  },
  {
    id: "power", title: "Akım ve güç", icon: "bolt", keywords: "elektrik akım güç kva kvar cosfi sigorta",
    desc: "Aktif güçten (veya akımdan) akım, görünür ve reaktif güç.",
    formula: "Monofaze: I = P / (U · cosφ)\nTrifaze:  I = P / (√3 · U · cosφ)\nS = P / cosφ      Q = S · sinφ",
    init: () => ({ phase: "tri", mode: "p", kw: 10, a: 16, u: 400, cos: 0.85 }),
    form(v, go) {
      return h("div", { class: "fields" },
        selectField(v, "phase", "Faz", [{ value: "mono", label: "Monofaze" }, { value: "tri", label: "Trifaze" }], { onChange: () => { v.u = v.phase === "tri" ? 400 : 230; go(true); } }),
        selectField(v, "mode", "Giriş", [{ value: "p", label: "Aktif güçten" }, { value: "i", label: "Akımdan" }], { onChange: () => go(true) }),
        v.mode === "p" ? numField(v, "kw", "Aktif güç", { unit: "kW", onChange: go }) : numField(v, "a", "Akım", { unit: "A", onChange: go }),
        numField(v, "u", v.phase === "tri" ? "Fazlar arası gerilim" : "Faz-nötr gerilimi", { unit: "V", onChange: go }),
        numField(v, "cos", "Güç katsayısı cosφ", { onChange: go }));
    },
    compute: v => rpc("calc.power", { input: { phase: v.phase, voltageV: v.u, cosPhi: v.cos, powerKw: v.mode === "p" ? v.kw : null, currentA: v.mode === "i" ? v.a : null } }),
    view(r) {
      return h("div", { class: "stats" },
        stat("Akım", fmt(r.currentA, 2), "A", "hero"),
        stat("Aktif güç", fmt(r.powerKw, 3), "kW"),
        stat("Görünür güç", fmt(r.apparentKva, 3), "kVA"),
        stat("Reaktif güç", fmt(r.reactiveKvar, 3), "kVAr"));
    },
    summary: (r, v) => `${v.phase === "tri" ? "3~" : "1~"} ${fmt(r.powerKw, 2)} kW, cosφ ${fmt(v.cos, 2)} → ${fmt(r.currentA, 1)} A`,
  },
  {
    id: "comp", title: "Kompanzasyon", icon: "bolt", keywords: "elektrik reaktif güç kondansatör kompanzasyon cosfi",
    desc: "Güç katsayısını hedef değere çıkarmak için gereken kondansatör gücü.",
    formula: "Qc = P · (tanφ₁ − tanφ₂)\nAkımdaki azalma = 1 − cosφ₁ / cosφ₂",
    init: () => ({ kw: 100, cos1: 0.75, cos2: 0.95 }),
    form(v, go) {
      return h("div", { class: "fields" },
        numField(v, "kw", "Aktif güç", { unit: "kW", onChange: go }),
        numField(v, "cos1", "Mevcut cosφ₁", { onChange: go }),
        numField(v, "cos2", "Hedef cosφ₂", { onChange: go }));
    },
    compute: v => rpc("calc.compensation", { input: { powerKw: v.kw, cosPhi1: v.cos1, cosPhi2: v.cos2 } }),
    view(r) {
      return h("div", { class: "stats" },
        stat("Kondansatör gücü", fmt(r.capacitorKvar, 2), "kVAr", "hero"),
        stat("tanφ₁ / tanφ₂", `${fmt(r.tan1, 3)} / ${fmt(r.tan2, 3)}`),
        stat("Görünür güç", `${fmt(r.apparentBeforeKva, 1)} → ${fmt(r.apparentAfterKva, 1)}`, "kVA"),
        stat("Akımdaki azalma", fmt(r.currentReductionPercent, 1), "%"));
    },
    summary: (r, v) => `${fmt(v.kw, 1)} kW, cosφ ${fmt(v.cos1, 2)} → ${fmt(v.cos2, 2)}: ${fmt(r.capacitorKvar, 1)} kVAr`,
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
  id: "calc", title: "Mekanik ve elektrik hesaplar", icon: "calc", group: "Modüller", keywords: "hesap makinesi elektrik yangın",
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
    el.append(h("div", { class: "page-head" }, h("div", { class: "grow" }, h("h1", null, "Mekanik ve elektrik hesaplar"),
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
    const renderForm = async () => {
      try { clear(formHost).append(await c.form(v, rebuild => { if (rebuild === true) renderForm(); go(); })); }
      catch (e) { clear(formHost).append(note(e.message, "bad")); }
    };
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

  async pdf(c, v) {
    const r = this.results[c.id];
    if (!r) { toast("Önce hesabı tamamlayın", "bad"); return; }
    const inputs = h("div", { class: "card card-pad" }, h("h3", null, "Girdiler"), await c.form(clone(v), () => { }));
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
    const outsideField = numField(st, "outsideTemp", "Dış hesap sıcaklığı", { unit: "°C", onChange: changed });
    const cityHost = h("div", { class: "fields" }, numField(st, "insideTemp", "İç hesap sıcaklığı", { unit: "°C", onChange: changed }), outsideField);
    cityList().then(async list => {
      // A new heat-loss sheet takes the province from the project's location text.
      if (st.city == null && st.outsideTemp == null) {
        const loc = App.project?.data.info?.location ?? "";
        const hit = list.find(c => loc.toLocaleLowerCase(TR).includes(c.name.toLocaleLowerCase(TR)));
        if (hit) { st.city = hit.name; st.outsideTemp = hit.winterDesignC; changed(); }
      }
      cityHost.prepend(await citySelect(st, "city", "İl", city => {
        if (city) { st.outsideTemp = city.winterDesignC; outsideField.querySelector("input").value = String(city.winterDesignC).replace(".", ","); }
        changed();
      }));
    }).catch(fail);

    const newRoom = () => ({ id: uid(), name: `Mahal ${st.rooms.length + 1}`, volume: null, n: null, increment: 0, insideTemp: null,
      surfaces: [{ id: uid(), name: "Dış duvar", area: null, u: null, dt: null }] });

    const recalc = debounce(async () => {
      clear(resultHost);
      if (st.outsideTemp == null) { resultHost.append(note("İl seçin ya da dış hesap sıcaklığını girin.", "warn")); return; }
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
        renderChain(res);
      } catch (e) { resultHost.append(note(e.message, "bad")); }
    }, 200);

    // ---- Isıtma zinciri: room loads → radiators → pipes → pump ----
    // 80/60 °C: the ministry's heating specification (MGTTS-03) rates boilers at this regime. Pipe limits are the same
    // defaults as the pipe calculator and come from the project specification; the local-loss share is an assumption.
    const ch = st.chain ??= { supplyC: 80, returnC: 60, exponent: 1.3, perUnit: null, material: "celik", maxV: 1.0, maxR: 150, length: null, local: 0.3, equipment: 20 };
    const chainForm = h("div");
    const chainResult = h("div", { class: "stack" });
    const chainHost = h("div", { class: "card" }, h("div", { class: "card-head" }, icon("pipe"), h("h3", null, "Isıtma tesisatı zinciri"),
      h("span", { class: "faint small" }, "Isı kaybı → radyatör → boru → pompa")), h("div", { class: "card-body stack" }, chainForm, chainResult));
    let lastRooms = null;
    const chainChanged = debounce(() => { if (App.project) App.touch(); renderChain(lastRooms); }, 200);
    chainForm.append(h("div", { class: "fields" },
      numField(ch, "supplyC", "Gidiş suyu", { unit: "°C", hint: "Bakanlık ısıtma şartnamesi: 80/60 °C rejimi", onChange: chainChanged }),
      numField(ch, "returnC", "Dönüş suyu", { unit: "°C", onChange: chainChanged }),
      numField(ch, "exponent", "Radyatör üssü n", { hint: "Katalogdan; 1,3 kabul", onChange: chainChanged }),
      numField(ch, "perUnit", "Katalog ΔT50 verimi (isteğe bağlı)", { unit: "W / m, dilim", onChange: chainChanged }),
      selectField(ch, "material", "Boru malzemesi", PIPE_MATERIALS, { onChange: chainChanged }),
      numField(ch, "maxV", "Azami hız", { unit: "m/s", hint: "Şartnameye göre", onChange: chainChanged }),
      numField(ch, "maxR", "Azami basınç kaybı", { unit: "Pa/m", hint: "Şartnameye göre", onChange: chainChanged }),
      numField(ch, "length", "Kritik devre boyu (gidiş + dönüş)", { unit: "m", onChange: chainChanged }),
      numField(ch, "local", "Yerel kayıp oranı", { hint: "Sürekli kaybın oranı; 0,3 = %30 kabul", onChange: chainChanged }),
      numField(ch, "equipment", "Kazan, vana vb. kayıplar", { unit: "kPa", hint: "Üretici verilerinden", onChange: chainChanged })));
    async function renderChain(res) {
      lastRooms = res;
      clear(chainResult);
      if (!res?.length) { chainResult.append(h("div", { class: "faint small" }, "Mahal ısı kayıpları hesaplanınca zincir burada çıkar.")); return; }
      let r;
      try {
        r = await rpc("calc.heatingChain", { input: {
          rooms: res.map((x, i) => ({ name: x.name, heatW: x.totalW, roomC: st.rooms[i]?.insideTemp ?? null })),
          supplyC: ch.supplyC ?? 0, returnC: ch.returnC ?? 0, roomC: st.insideTemp ?? 20, exponent: ch.exponent ?? 1.3, catalogPerUnitW: ch.perUnit,
          material: ch.material, maxVelocity: ch.maxV ?? 0, maxPressureDrop: ch.maxR ?? 0, criticalLengthM: ch.length ?? 0, localLossFactor: ch.local ?? 0, equipmentKPa: ch.equipment ?? 0,
        } });
      } catch (e) { chainResult.append(note(e.message, "bad")); return; }
      const pipe = p => p.size ? `${p.size}` : "uygun çap yok";
      chainResult.append(
        h("div", { class: "stats" },
          stat("Toplam yük", fmt(r.totalHeatW / 1000, 2), "kW"),
          stat("Toplam debi", fmt(r.main.flowM3h, 3), "m³/h"),
          stat("Ana boru", pipe(r.main), null, r.main.size ? null : "bad"),
          stat("Pompa", `${fmt(r.pump.flowM3h, 2)} m³/h · ${fmt(r.pump.headM, 2)} mSS`, null, "hero")),
        h("div", { class: "table-wrap" }, h("table", { class: "t" },
          h("thead", null, h("tr", null, ["Mahal", "Isı yükü (W)", "Radyatör ΔT50 (W)", ch.perUnit ? "Miktar" : null, "Debi (m³/h)", "Branşman", "Hız (m/s)", "R (Pa/m)"]
            .filter(Boolean).map((x, i) => h("th", { class: i ? "num" : null }, x)))),
          h("tbody", null, r.rooms.map(x => h("tr", null, h("td", null, x.name), h("td", { class: "num" }, fmt(x.heatW, 0)), h("td", { class: "num" }, h("b", null, fmt(x.requiredNominalW, 0))),
            ch.perUnit ? h("td", { class: "num" }, fmt(x.units, 2)) : null,
            h("td", { class: "num" }, fmt(x.pipe.flowM3h, 3)), h("td", { class: "num" }, pipe(x.pipe)),
            h("td", { class: "num" }, x.pipe.velocity != null ? fmt(x.pipe.velocity, 2) : "—"), h("td", { class: "num" }, x.pipe.pressureDropPaM != null ? fmt(x.pipe.pressureDropPaM, 0) : "—")))))),
        h("div", { class: "formula" }, `Pompa basma yüksekliği = R·L + yerel kayıplar + ekipman = ${fmt(r.pump.pipeLossKPa, 2)} + ${fmt(r.pump.localLossKPa, 2)} + ${fmt(r.pump.equipmentKPa, 2)} = ${fmt(r.pump.headKPa, 2)} kPa ≈ ${fmt(r.pump.headM, 2)} mSS`),
        r.notes.map(n => note(n)));
    }

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
        note("İl seçilince dış hesap sıcaklığı tesisat.org iklim tablosundan gelir; yön ve işletme artırımları mühendis tarafından girilir."),
        cityHost)));
    panel.append(roomsHost);
    panel.append(h("div", null, btn("Mahal ekle", () => { st.rooms.push(newRoom()); renderRooms(); changed(); }, { cls: "primary", icon: "plus" })));
    panel.append(h("div", { class: "card" }, h("div", { class: "card-head" }, h("h3", null, "Sonuç")), h("div", { class: "card-body" }, resultHost)));
    panel.append(chainHost);
    panel.append(h("div", { class: "card card-pad stack" }, h("h3", null, "Yöntem"), h("div", { class: "formula" }, c.formula)));
    renderChain(null);
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
