// Ayarlar: theme, TS 825 limit table, material library, hakediş data sources, Claude (MCP), about.
"use strict";

App.register({
  id: "settings", title: "Ayarlar", icon: "settings", bottom: true, keywords: "ayar tema evds katalog malzeme sınır değer ts 825 claude mcp",

  mount(el) { this.el = el; },

  async show() {
    const st = App.settings;
    const save = () => App.saveSettings();
    const el = clear(this.el);
    el.append(h("div", { class: "page-head" }, h("div", { class: "grow" }, h("h1", null, "Ayarlar"),
      h("div", { class: "sub" }, "Bu bilgisayardaki tüm projeler için geçerlidir."))));

    // Theme
    el.append(h("div", { class: "card", style: { marginBottom: "16px" } },
      h("div", { class: "card-head" }, h("h2", null, "Görünüm")),
      h("div", { class: "card-body fields" },
        selectField(st, "theme", "Tema", [{ value: "system", label: "Windows ile aynı" }, { value: "light", label: "Açık" }, { value: "dark", label: "Koyu" }], {
          onChange: v => {
            applyTheme(v); save();
            Cad2d.call("theme", { theme: effectiveTheme() }).catch(() => { });
          },
        }))));

    // TS 825 U table (seeded from TS825_DEFAULTS on first use)
    const lim = uLimits();
    for (const z of ZONES) lim.values[z] ??= {};
    el.append(h("div", { class: "card", style: { marginBottom: "16px" } },
      h("div", { class: "card-head" }, h("h2", null, "TS 825:2024 tavsiye U değerleri"),
        btn("Varsayılana dön", async () => {
          if (!await confirmBox("U değerlerini sıfırla", "Tablodaki değişiklikleriniz silinip varsayılan değerler yüklenecek.", "Sıfırla", true)) return;
          lim.values = clone(TS825_DEFAULTS); lim.highGlazing = clone(TS825_HIGH_GLAZING); lim.source = TS825_SOURCE;
          save(); this.show();
        }, { cls: "sm ghost" })),
      h("div", { class: "card-body stack" },
        note("Varsayılan değerler belediyenin gösterdiği tesisat.org TS 825 programından alındı; orada \"TS 825 tavsiye U değeri\" olarak geçer. Değerler W/m²K'dir ve değiştirilebilir; boş bırakılan için kontrol yapılmaz."),
        h("div", { class: "table-wrap" }, h("table", { class: "t" },
          h("thead", null, h("tr", null, h("th", null, "Bölge"), LIMIT_KEYS.map(k => h("th", { class: "num" }, k.label)), h("th", { class: "num" }, "Cam g"))),
          h("tbody", null, ZONES.map(z => h("tr", null, h("td", null, z + ". bölge"),
            LIMIT_KEYS.map(k => h("td", null, numField(lim.values[z], k.key, null, { bare: true, cls: "num", placeholder: "—", onChange: save }))),
            h("td", null, textField(lim.values[z], "g", null, { bare: true, placeholder: "—", onChange: save }))))))),
        h("div", { class: "grid g2" },
          numField(lim.highGlazing, "factor", "Cam oranı ≥ %60: opak eleman katsayısı", { onChange: save, hint: "Duvar, çatı ve döşeme değerleri bununla çarpılır (0,75 = %25 daha küçük)." }),
          numField(lim.highGlazing, "pencere", "Cam oranı ≥ %60: pencere U", { unit: "W/m²K", onChange: save })),
        textField(lim, "source", "Kaynak", { placeholder: "ör. TS 825:2024, Tablo …", onChange: save }))));

    // Materials
    const matBody = h("tbody");
    const mats = () => (st.materials ??= clone(DEFAULT_MATERIALS));
    const renderMats = () => clear(matBody).append(mats().map((m, i) => h("tr", null,
      h("td", null, textField(m, "name", null, { bare: true, onChange: save })),
      h("td", null, m.fixedR != null ? h("span", { class: "faint small" }, `R = ${fmt(m.fixedR, 2)}`) : numField(m, "lambda", null, { bare: true, cls: "num", onChange: save })),
      h("td", null, numField(m, "mu", null, { bare: true, cls: "num", onChange: save })),
      h("td", null, h("button", { class: "btn ghost icon", onclick: () => { mats().splice(i, 1); renderMats(); save(); } }, icon("x"))))));
    renderMats();
    el.append(h("div", { class: "card", style: { marginBottom: "16px" } },
      h("div", { class: "card-head" }, h("h2", null, "Malzeme kütüphanesi"),
        btn("Ekle", () => { mats().push({ name: "", lambda: null, mu: 1 }); renderMats(); save(); }, { cls: "sm", icon: "plus" }),
        btn("Varsayılana dön", async () => {
          if (!await confirmBox("Kütüphaneyi sıfırla", "Eklediğiniz malzemeler silinecek.", "Sıfırla", true)) return;
          st.materials = clone(DEFAULT_MATERIALS); renderMats(); save();
        }, { cls: "ghost sm" })),
      h("div", { class: "table-wrap", style: { maxHeight: "360px" } }, h("table", { class: "t" },
        h("thead", null, h("tr", null, h("th", null, "Malzeme"), h("th", { class: "num" }, "λ (W/mK)"), h("th", { class: "num" }, "μ"), h("th"))), matBody)),
      h("div", { class: "card-body faint small" }, "Varsayılan değerler tipik değerlerdir (TS EN ISO 10456 ve yaygın beyan değerleri). Projede üreticinin beyan ettiği λ değerini kullanın.")));

    // Hakediş sources
    const catalogHost = h("div");
    const renderCatalog = async () => {
      let info = { count: 0, sources: [] };
      try { info = await rpc("catalog.info"); } catch (e) { fail(e); }
      clear(catalogHost).append(info.sources.length ? h("table", { class: "t" }, h("tbody", null, info.sources.map(x => h("tr", null,
        h("td", null, x.source), h("td", { class: "num" }, x.count.toLocaleString(TR) + " poz"),
        h("td", null, h("button", { class: "btn ghost icon", onclick: async () => { await rpc("catalog.remove", { source: x.source }).catch(fail); renderCatalog(); } }, icon("x")))))))
        : h("div", { class: "faint small" }, "Katalog boş."));
    };
    // Ministry lists: yearly book (January) or a monthly update, downloaded and read by the host.
    const csbDialog = async () => {
      let list;
      try { list = await rpc("catalog.csbList"); } catch (e) { fail(e); return; }
      const v = { key: `${list[0].year}-${list[0].month}` };
      const rayic = h("input", { type: "checkbox" });
      const status = h("div", { class: "faint small" });
      let busy = false;
      const go = async () => {
        if (busy) return;
        busy = true; ok.disabled = true;
        App.onCatalogProgress = m => { status.textContent = m; };
        const [year, month] = v.key.split("-").map(Number);
        try {
          const r = await rpc("catalog.csbImport", { year, month, includeRayic: rayic.checked });
          closeOverlay(); toast(`${r.count.toLocaleString(TR)} poz eklendi (${r.source})`); renderCatalog();
        } catch (e) { fail(e); status.textContent = ""; }
        finally { busy = false; ok.disabled = false; App.onCatalogProgress = null; }
      };
      const ok = btn("İndir ve ekle", go, { cls: "primary", icon: "download" });
      modal("Bakanlık birim fiyatları", h("div", { class: "stack" },
        selectField(v, "key", "Liste", list.map(r => ({ value: `${r.year}-${r.month}`, label: `${r.label} · ${r.month === 1 ? "yıllık kitap" : "aylık güncel liste"}` }))),
        h("label", { class: "check" }, rayic, "Rayiç listelerini de ekle (malzeme, işçilik, makine)"),
        h("p", { class: "faint small" }, "yfk.csb.gov.tr'deki PDF listeleri indirilip okunur; ilk seferde birkaç dakika sürebilir. Aylık listelerde tanım ve tarifler aynı yılın kitabından tamamlanır."),
        status), [btn("Vazgeç", closeOverlay), ok]);
    };
    el.append(h("div", { class: "grid g2", style: { marginBottom: "16px" } },
      h("div", { class: "card" }, h("div", { class: "card-head" }, h("h2", null, "Birim fiyat kataloğu"),
        h("div", { class: "row" },
          btn("Bakanlıktan indir", csbDialog, { cls: "sm primary", icon: "download" }),
          btn("Excel'den içe aktar", async () => {
            try { const r = await rpc("catalog.import"); if (r) { toast(`${r.count.toLocaleString(TR)} poz eklendi (${r.source})`); renderCatalog(); } } catch (e) { fail(e); }
          }, { cls: "sm", icon: "excel" }))),
        h("div", { class: "card-body stack" },
          h("p", { class: "muted small" }, "Çevre, Şehircilik ve İklim Değişikliği Bakanlığı'nın inşaat, mekanik ve elektrik birim fiyatlarını yıllık kitaptan ya da aylık güncel listelerden indirin. Başka kurumların listeleri Excel ya da CSV olarak eklenebilir (\"Poz No\" ve \"Birim Fiyat\" sütunları otomatik bulunur)."),
          catalogHost)),
      h("div", { class: "card" }, h("div", { class: "card-head" }, h("h2", null, "Endeks (TCMB EVDS)")),
        h("div", { class: "card-body stack" },
          textField(st, "evdsKey", "EVDS API anahtarı", { type: "password", onChange: save, hint: "evds3.tcmb.gov.tr üzerinden ücretsiz alınır; bu bilgisayarda saklanır." }),
          textField(st, "evdsSeries", "Seri kodu", { placeholder: "TP.TUFE1YI.T1", onChange: save, hint: "Yİ-ÜFE genel endeksi (TÜİK)." }),
          h("div", null, h("a", { onclick: () => rpc("url.open", { url: "https://evds3.tcmb.gov.tr/" }) }, "EVDS sitesini aç"))))));
    renderCatalog();

    // Claude / MCP
    el.append(this.mcpCard());

    // About
    el.append(h("div", { class: "card" },
      h("div", { class: "card-head" }, h("h2", null, "Hakkında")),
      h("div", { class: "card-body stack" },
        h("p", null, `MühendisKit ${App.info.version ?? ""}. Özgür yazılımdır: GNU Affero Genel Kamu Lisansı 3.0 (AGPL-3.0) ile dağıtılır.`),
        h("table", { class: "t" }, h("thead", null, h("tr", null, h("th", null, "Bileşen"), h("th", null, "Kullanım"), h("th", null, "Lisans"))),
          h("tbody", null, [
            ["MLightCAD (cad-viewer)", "2B çizim editörü", "MIT"], ["LibreDWG (libredwg-web)", "DWG okuma", "GPL-3.0"],
            ["Chili3D", "3B katı modelleme", "AGPL-3.0"], ["Open CASCADE Technology", "3B geometri çekirdeği", "LGPL-2.1"],
            ["ACadSharp", "DWG/DXF dönüştürme, metraj", "MIT"], ["ClosedXML", "Excel çıktısı", "MIT"], ["three.js", "Görüntüleme", "MIT"],
          ].map(r => h("tr", null, r.map(c => h("td", null, c)))))),
        h("div", { class: "row" },
          btn("Veri klasörü", () => rpc("shell.open", { path: App.info.dataDir }).catch(fail), { cls: "sm", icon: "folder" }),
          btn("Projeler klasörü", () => rpc("shell.open", { path: App.info.projectsDir }).catch(fail), { cls: "sm", icon: "folder" })))));
  },

  mcpCard() {
    const host = h("div", { class: "card-body stack" }, h("div", { class: "faint small" }, "Kontrol ediliyor…"));
    const card = h("div", { class: "card", style: { marginBottom: "16px" } },
      h("div", { class: "card-head" }, h("h2", null, "Claude ile kullanım (MCP)")), host);
    const row = (title, ok, text, action) => h("div", { class: "row", style: { padding: "6px 0" } },
      h("span", { class: "badge " + (ok ? "ok" : "") }, ok ? "Bağlı" : "Bağlı değil"),
      h("div", { class: "grow" }, h("div", null, h("b", null, title)), h("div", { class: "faint small" }, text)),
      action);
    const run = async (button, fn) => {
      button.disabled = true;
      try { await fn(); } catch (e) { fail(e); } finally { button.disabled = false; }
    };
    rpc("mcp.info").then(info => {
      const codeBtn = btn(info.codeConfigured ? "Yeniden bağla" : "Claude Code'a ekle", () => run(codeBtn, async () => {
        await rpc("mcp.configureCode");
        toast("Claude Code'a eklendi. Yeni bir Claude Code oturumunda kullanılabilir.");
        this.show();
      }), { cls: "sm" + (info.codeConfigured ? "" : " primary") });
      const deskBtn = btn(info.desktopConfigured ? "Yeniden bağla" : "Claude Desktop'a ekle", () => run(deskBtn, async () => {
        if (!info.desktopConfigured && !await confirmBox("Claude Desktop'a ekle",
          "Claude Desktop yapılandırmasına \"muhendiskit\" MCP sunucusu eklenecek. Diğer ayarlarınız korunur (önceki hali .bak olarak saklanır).", "Ekle")) return;
        await rpc("mcp.configureDesktop");
        toast("Eklendi. Claude Desktop'u tamamen kapatıp yeniden açın (görev çubuğu simgesinden Çıkış).");
        this.show();
      }), { cls: "sm" + (info.desktopConfigured ? "" : " primary") });
      clear(host).append(
        h("p", { class: "muted" }, "Claude, MühendisKit'i bir MCP sunucusu üzerinden kullanabilir: hesap yapar, çizer, katı model oluşturur, hakediş satırı girer. Yaptığı her şey açık pencerede görünür. Her bilgisayarda bir kez bağlamanız yeterli."),
        row("Claude Code", info.codeConfigured, "Terminaldeki Claude Code (kullanıcı geneli)", codeBtn),
        row("Claude Desktop", info.desktopConfigured, info.desktopConfigPaths.join(" · "), deskBtn),
        h("details", null, h("summary", { class: "faint small", style: { cursor: "pointer" } }, "Elle kurulum"),
          h("div", { class: "stack", style: { marginTop: "8px" } },
            h("div", { class: "row" }, h("input", { class: "input mono small", readOnly: true, value: info.claudeCodeCommand }),
              btn("Kopyala", () => { navigator.clipboard.writeText(info.claudeCodeCommand); toast("Kopyalandı"); }, { cls: "sm", icon: "copy" })),
            h("div", { class: "faint small" }, "MCP sunucusu: " + info.serverPath + (info.serverExists ? "" : " (bulunamadı!)")))));
    }).catch(e => clear(host).append(note(e.message, "bad")));
    return card;
  },
});
