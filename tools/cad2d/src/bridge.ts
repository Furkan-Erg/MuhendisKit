// Message bridge between the MühendisKit window (parent) and the MLightCAD editor in this iframe.
// Parent → iframe: { mk: 'cad2d', id, cmd, args }; iframe → parent: { mk: 'cad2d-reply', id, ok, result | error }
// and unsolicited events { mk: 'cad2d-event', type, ... }.
import {
  CAD_SCRIPT_API_SUMMARY,
  captureDrawingPreview,
  createDocManagerSession,
  executeCadScript,
  waitForDrawingSceneReady
} from '@mlightcad/cad-agent-core'
import { AcApDocManager } from '@mlightcad/cad-simple-viewer'
import { AcDbMText } from '@mlightcad/data-model'
import { nextTick } from 'vue'

type Handler = (args: Record<string, unknown>) => Promise<unknown> | unknown

const session = createDocManagerSession()

// cad-agent-core 1.7.7 drawText assigns `textHeight`, which AcDbMText doesn't have (its property is `height`),
// so script-drawn text ends up 0 high and invisible. Route the old name to the real one.
if (!Object.getOwnPropertyDescriptor(AcDbMText.prototype, 'textHeight')) {
  Object.defineProperty(AcDbMText.prototype, 'textHeight', {
    configurable: true,
    get(this: AcDbMText) { return this.height },
    set(this: AcDbMText, v: number) { this.height = v }
  })
}
const docs = () => AcApDocManager.instance

function post(msg: Record<string, unknown>) {
  window.parent?.postMessage(msg, '*')
}

function current() {
  const doc = docs().curDocument
  if (!doc?.database) throw new Error('Açık çizim yok.')
  return doc
}

function info() {
  const doc = docs().curDocument
  if (!doc?.database) return { open: false }
  return {
    open: true,
    title: doc.docTitle,
    fileName: doc.fileName,
    layers: doc.layerStore?.getLayers?.() ?? [],
    currentLayer: doc.layerStore?.getCurrentLayerName?.()
  }
}

export async function startBridge(setTheme: (t: 'light' | 'dark') => void) {
  const handlers: Record<string, Handler> = {
    async open(a) {
      const url = String(a.url), name = String(a.name ?? 'cizim.dxf')
      const res = await fetch(url)
      if (!res.ok) throw new Error(`Dosya okunamadı (${res.status}).`)
      const ok = await docs().openDocument(name, await res.arrayBuffer(), {} as never)
      if (!ok) throw new Error('Çizim açılamadı.')
      return info()
    },
    async new() {
      const ok = await docs().newDocument()
      if (!ok) throw new Error('Yeni çizim oluşturulamadı.')
      return info()
    },
    info,
    // ASCII DXF of the current drawing; the host writes it as .dxf or converts it to .dwg.
    dxf() {
      const doc = current()
      const out = doc.database.dxfOut(undefined, 6)
      return { title: doc.docTitle, dxf: typeof out === 'string' ? out : new TextDecoder().decode(out) }
    },
    async script(a) {
      return executeCadScript({ code: String(a.code), intent: a.intent ? String(a.intent) : undefined }, session)
    },
    api: () => CAD_SCRIPT_API_SUMMARY,
    async preview(a) {
      const r = await captureDrawingPreview(typeof a.longSide === 'number' ? a.longSide : undefined)
      if (!r.ok) throw new Error('Önizleme alınamadı: ' + r.reason)
      return r.dataUrl
    },
    // Fit to everything drawn (the scene's own bounds; the header extents lag behind edits).
    async fit() {
      await waitForDrawingSceneReady()
      docs().curView.zoomToFitDrawing()
      return null
    },
    async command(a) {
      await docs().executeCommandString(String(a.text))
      return info()
    },
    theme(a) {
      setTheme(a.theme === 'light' ? 'light' : 'dark')
      return null
    }
  }

  window.addEventListener('message', async e => {
    const m = e.data
    if (!m || m.mk !== 'cad2d' || e.source !== window.parent) return
    const h = handlers[m.cmd]
    try {
      if (!h) throw new Error('Bilinmeyen komut: ' + m.cmd)
      post({ mk: 'cad2d-reply', id: m.id, ok: true, result: await h(m.args ?? {}) })
    } catch (err) {
      post({ mk: 'cad2d-reply', id: m.id, ok: false, error: err instanceof Error ? err.message : String(err) })
    }
  })

  ;(window as unknown as { __mkDocs: unknown }).__mkDocs = docs   // for debugging from the host's DevTools

  // Start with an empty drawing from the ISO template so the editor is usable right away. The viewer
  // emits "create" before its props (template base URL) are applied, so wait a tick and retry once.
  await nextTick()
  if (!(await docs().newDocument().catch(() => false))) {
    await new Promise(r => setTimeout(r, 500))
    await docs().newDocument().catch(() => false)
  }
  post({ mk: 'cad2d-event', type: 'ready', info: info() })
}
