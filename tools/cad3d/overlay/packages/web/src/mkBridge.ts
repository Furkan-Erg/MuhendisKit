// MühendisKit bridge (AGPL-3.0, part of the MühendisKit modifications to Chili3D).
// The MühendisKit window hosts this app in an iframe and talks to it with postMessage:
//   parent → iframe { mk: "cad3d", id, cmd, args }; iframe → parent { mk: "cad3d-reply", id, ok, result | error }
// It exposes Chili3D's own AI tool catalog so Claude (through MühendisKit's MCP server) can model in 3D.
import { buildSystemPrompt, buildTools, flattenSystem } from "@chili3d/ai";
import { Config, type IApplication, type IDocument, PubSub, VisualNode } from "@chili3d/core";

interface Args {
    name?: string;
    args?: Record<string, unknown>;
    code?: string;
    ids?: string[];
    format?: string;
    url?: string;
}
type Handler = (args: Args) => unknown;

function post(msg: Record<string, unknown>) {
    window.parent.postMessage(msg, "*");
}

async function ensureDocument(app: IApplication): Promise<IDocument> {
    return app.activeView?.document ?? (await app.newDocument("Model"));
}

function toBase64(bytes: Uint8Array): string {
    let s = "";
    for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
    return btoa(s);
}

// The in-app chat panel opens with every editor; inside MühendisKit Claude works through MCP, so start with it closed
// (the ribbon's AI button still opens it).
function hideChatPanel() {
    setTimeout(() => {
        if (document.querySelector("chili-ai-chat")?.isConnected) PubSub.default.pub("toggleChatPanel");
    }, 0);
}

export function startMkBridge(app: IApplication) {
    if (window.parent === window) return;
    if (Config.instance.language !== "tr-TR") Config.instance.language = "tr-TR";
    hideChatPanel();
    const tools = () => buildTools();
    const handlers: Record<string, Handler> = {
        guide: () => flattenSystem(buildSystemPrompt()),
        tools: () => tools().map((t) => ({ name: t.name, description: t.description, parameters: t.parameters })),
        async call(a) {
            await ensureDocument(app);
            const tool = tools().find((t) => t.name === a.name);
            if (!tool) throw new Error(`Bilinmeyen 3B aracı: ${a.name}`);
            const r = await tool.handler(a.args ?? {});
            return typeof r === "string" ? { content: r } : r;
        },
        // Free-form JavaScript with the Chili3D application object, like the macro plugin.
        async macro(a) {
            await ensureDocument(app);
            const fn = new Function("app", `return (async () => {\n${a.code}\n})()`);
            return await fn(app);
        },
        async newDocument(a) {
            const doc = await app.newDocument(String(a.name ?? "Model"));
            hideChatPanel();
            return { name: doc.name };
        },
        formats: () => ({ import: app.dataExchange.importFormats(), export: app.dataExchange.exportFormats() }),
        // Exports nodes (all top-level nodes by default) to bytes; the host writes the file.
        async export(a) {
            const doc = await ensureDocument(app);
            const ids: string[] | undefined = a.ids;
            const nodes = doc.modelManager
                .findNodes((n) => (ids?.length ? ids.includes(n.id) : n.parent === doc.modelManager.rootNode))
                .filter((n): n is VisualNode => n instanceof VisualNode);
            if (!nodes.length) throw new Error("Dışa aktarılacak geometri yok.");
            const parts = await app.dataExchange.export(String(a.format), nodes);
            if (!parts) throw new Error("Dışa aktarılacak geometri yok.");
            const bytes = new Uint8Array(await new Blob(parts).arrayBuffer());
            return { base64: toBase64(bytes) };
        },
        async import(a) {
            const doc = await ensureDocument(app);
            const res = await fetch(String(a.url));
            if (!res.ok) throw new Error(`Dosya okunamadı (${res.status}).`);
            const file = new File([await res.blob()], String(a.name));
            await app.dataExchange.import(doc, [file]);
            return { ok: true };
        },
    };

    window.addEventListener("message", async (e) => {
        const m = e.data;
        if (!m || m.mk !== "cad3d" || e.source !== window.parent) return;
        try {
            const h = handlers[m.cmd];
            if (!h) throw new Error(`Bilinmeyen komut: ${m.cmd}`);
            post({ mk: "cad3d-reply", id: m.id, ok: true, result: await h(m.args ?? {}) });
        } catch (err) {
            post({ mk: "cad3d-reply", id: m.id, ok: false, error: err instanceof Error ? err.message : String(err) });
        }
    });
    post({ mk: "cad3d-event", type: "ready" });
}
