// Ana sayfa: active project, modules, recent projects, data status.
"use strict";

App.register({
  id: "home", title: "Ana sayfa", icon: "home", keywords: "başlangıç proje",

  mount(el) { this.el = el; },

  async show() {
    const el = clear(this.el);
    const p = App.project;
    const hour = new Date().getHours();
    const greet = hour < 6 ? "İyi geceler" : hour < 12 ? "Günaydın" : hour < 18 ? "İyi günler" : "İyi akşamlar";
    el.append(h("div", { class: "page-head" },
      h("div", { class: "grow" }, h("h1", null, greet), h("div", { class: "sub" }, fmtDate(new Date()))),
      btn("Proje aç", openProjectDialog, { icon: "folder" }),
      btn("Yeni proje", newProjectDialog, { cls: "primary", icon: "plus" })));

    el.append(p ? this.projectCard(p) : h("div", { class: "card card-pad row", style: { marginBottom: "16px" } },
      h("div", { class: "grow" }, h("h2", null, "Açık proje yok"),
        h("p", { class: "muted" }, "Hesap makineleri ve çizim görüntüleyici proje olmadan da çalışır. Isı yalıtımı ve hakediş bir projeye kaydedilir.")),
      btn("Yeni proje", newProjectDialog, { cls: "primary", icon: "plus" })));

    const d = p?.data;
    const tiles = [
      { id: "calc", icon: "calc", title: "Mekanik ve elektrik hesaplar", desc: "Isı kaybı, radyatör, boru ve kanal çapı, genleşme tankı, doğalgaz, hidrofor, yağmur ve atık su, yangın suyu, gerilim düşümü, kompanzasyon.", foot: d ? `${d.calcs.length} kayıtlı hesap` : "Proje gerektirmez" },
      { id: "ins", icon: "wall", title: "Isı yalıtımı", desc: "Katman katman U değeri, TS 825 tavsiye U kontrolü, Glaser yoğuşma analizi.", foot: d ? `${d.insulation.elements.length} yapı elemanı` : "Proje gerekir" },
      { id: "est", icon: "file", title: "Yaklaşık maliyet", desc: "Bakanlık pozlarıyla keşif, metraj, iş grubu icmali, ay ay yeniden fiyatlama, Excel çıktısı.", foot: d ? `${d.estimate.items.length} poz` : "Proje gerekir" },
      { id: "hak", icon: "receipt", title: "Hakediş", desc: "Poz, metraj (yeşil defter), hakediş icmali, fiyat farkı ve Excel çıktısı.", foot: d ? `${d.job.items.length} poz · ${d.job.periods.length} hakediş` : "Proje gerekir" },
      { id: "cad", icon: "drawing", title: "Çizim (2B)", desc: "DWG/DXF açma ve çizme, katmanlar, ölçülendirme, çizimden metraj.", foot: "AutoCAD gerekmez" },
      { id: "model3d", icon: "cube", title: "Katı model (3B)", desc: "Taslak, ekstrüzyon, döndürme, boolean, fillet; STEP/IGES/STL.", foot: "OpenCascade çekirdeği" },
    ];
    el.append(h("div", { class: "grid g3", style: { marginBottom: "22px" } }, tiles.map(t =>
      h("button", { class: "card tile", onclick: () => App.go(t.id) },
        h("div", { class: "tile-icon" }, icon(t.icon)), h("h3", null, t.title), h("div", { class: "desc" }, t.desc), h("div", { class: "foot" }, t.foot)))));

    const recentCard = h("div", { class: "card" }, h("div", { class: "card-head" }, h("h2", null, "Projeler"),
      btn("Klasörü aç", () => rpc("shell.open", { path: App.info.projectsDir }).catch(fail), { cls: "ghost sm", icon: "folder" })));
    const statusCard = h("div", { class: "card" }, h("div", { class: "card-head" }, h("h2", null, "Veri durumu")));
    el.append(h("div", { class: "split-wide" }, recentCard, statusCard));

    try {
      const list = await rpc("projects.list", { extra: App.settings.recent ?? [] });
      recentCard.append(list.length ? h("div", { class: "list" }, list.slice(0, 10).map(x =>
        h("button", { class: "list-item" + (p?.path === x.path ? " active" : ""), onclick: () => App.openProject(x.path) },
          icon("file"), h("div", { class: "grow" }, h("div", { class: "title" }, x.name), h("div", { class: "meta" }, x.path)),
          h("span", { class: "faint small" }, fmtDate(x.modified)))))
        : h("div", { class: "empty" }, "Henüz proje yok."));
    } catch (e) { fail(e); }

    let catalog = { count: 0 };
    try { catalog = await rpc("catalog.info"); } catch { }
    const limits = uLimits().values;
    const limitCount = Object.values(limits).reduce((n, z) => n + Object.values(z ?? {}).filter(v => typeof v === "number").length, 0);
    const rows = [
      [limitCount > 0, "TS 825:2024 tavsiye U değerleri", limitCount > 0 ? `${limitCount} değer` : "Boş: Ayarlar'dan tabloyu doldurun", "settings"],
      [!!App.settings.evdsKey, "TCMB EVDS anahtarı", App.settings.evdsKey ? "Tanımlı: endeksler otomatik çekilebilir" : "Yok: endeksler elle girilir", "settings"],
      [catalog.count > 0, "Birim fiyat kataloğu", catalog.count > 0 ? `${catalog.count.toLocaleString(TR)} poz` : "Boş: Ayarlar'dan Bakanlık listesini indirin", "settings"],
    ];
    statusCard.append(h("div", { class: "list" }, rows.map(([ok, title, text, page]) =>
      h("button", { class: "list-item", onclick: () => App.go(page) },
        h("span", { class: "badge " + (ok ? "ok" : "warn") }, ok ? "Hazır" : "Eksik"),
        h("div", { class: "grow" }, h("div", { class: "title" }, title), h("div", { class: "meta" }, text))))));
  },

  projectCard(p) {
    const d = p.data;
    return h("div", { class: "card", style: { marginBottom: "16px" } },
      h("div", { class: "card-head" },
        h("div", { class: "grow" }, h("h2", null, d.name), h("div", { class: "small muted" }, [d.info.location, d.info.owner].filter(Boolean).join(" · ") || p.path)),
        btn("Klasörde göster", () => rpc("shell.reveal", { path: p.path }).catch(fail), { cls: "ghost sm", icon: "folder" }),
        btn("Farklı kaydet", async () => {
          try { const path = await rpc("project.saveAs", { data: d }); if (path) { App.setProject(path, d); toast("Kaydedildi"); } } catch (e) { fail(e); }
        }, { cls: "ghost sm", icon: "save" }),
        btn("Kapat", () => App.closeProject(), { cls: "ghost sm", icon: "x" })),
      h("div", { class: "card-body" }, h("div", { class: "fields" },
        textField(d, "name", "Proje adı", { onChange: () => { App.touch(); App.renderProjectPill(); } }),
        textField(d.info, "location", "Yer / şantiye", { onChange: () => App.touch() }),
        textField(d.info, "owner", "İdare / işveren", { onChange: () => App.touch() }),
        textField(d.info, "notes", "Notlar", { onChange: () => App.touch() }))));
  },
});
