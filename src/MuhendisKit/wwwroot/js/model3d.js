// Katı model (3B): Chili3D (OpenCascade kernel) in an iframe (wwwroot/cad3d), driven through the same bridge as 2B.
"use strict";

const Cad3d = new FrameBridge("cad3d");
const EXPORT_3D = [
  { value: ".step", label: "STEP (.step)" }, { value: ".iges", label: "IGES (.iges)" }, { value: ".brep", label: "BREP (.brep)" },
  { value: ".stl binary", label: "STL (.stl)" }, { value: ".obj", label: "OBJ (.obj)" },
];

App.register({
  id: "model3d", title: "Katı model (3B)", icon: "cube", group: "Çizim", full: true, keywords: "3d katı model solid step iges stl ekstrüzyon fillet",

  mount(el) {
    this.el = el;
    App.command({ title: "3B model: dosya içe aktar (STEP, IGES, STL…)", hint: "3B", icon: "cube", keywords: "step iges brep stl aç", run: () => { App.go("model3d"); this.importFile(); } });
    App.command({ title: "3B model: dışa aktar", hint: "3B", icon: "download", keywords: "step stl kaydet", run: () => { App.go("model3d"); this.exportDialog(); } });
    this.frame = h("iframe", { src: "cad3d/index.html", style: { border: 0, width: "100%", height: "100%", display: "block" } });
    Cad3d.attach(this.frame);
    el.append(h("div", { class: "cad-stage", style: { height: "100%" } },
      h("div", { class: "cad-toolbar" },
        btn("Yeni model", () => Cad3d.call("newDocument", { name: "Model" }).catch(fail), { cls: "sm", icon: "plus" }),
        btn("İçe aktar", () => this.importFile(), { cls: "sm", icon: "folder" }),
        btn("Dışa aktar", () => this.exportDialog(), { cls: "sm", icon: "download" }),
        h("div", { class: "grow" }),
        h("span", { class: "faint small" }, "Chili3D · OpenCascade çekirdeği")),
      h("div", { class: "cad-canvas", style: { background: "var(--surface)" } }, this.frame)));
  },

  async importFile() {
    try {
      const f = await rpc("file.pick", { filter: "3B model (*.step;*.stp;*.iges;*.igs;*.brep;*.stl)|*.step;*.stp;*.iges;*.igs;*.brep;*.stl" });
      if (!f) return;
      await Cad3d.call("import", { url: f.url, name: f.name });
      toast(`${f.name} içe aktarıldı`);
    } catch (e) { fail(e); }
  },

  exportDialog() {
    const v = { format: ".step" };
    modal("Dışa aktar", h("div", { class: "stack" },
      selectField(v, "format", "Biçim", EXPORT_3D),
      h("p", { class: "muted small" }, "Tüm model tek dosyaya aktarılır. STEP, SolidWorks ve diğer CAD programlarıyla açılabilir.")),
      [btn("Vazgeç", closeOverlay), btn("Kaydet", async () => {
        closeOverlay();
        try {
          const { base64 } = await Cad3d.call("export", { format: v.format });
          const ext = v.format.replace(" binary", "");
          const path = await rpc("file.saveBase64", { base64, name: "Model" + ext, filter: `${ext.slice(1).toUpperCase()} (*${ext})|*${ext}` });
          if (path) toast("Kaydedildi: " + path);
        } catch (e) { fail(e); }
      }, { cls: "primary" })]);
  },
});
