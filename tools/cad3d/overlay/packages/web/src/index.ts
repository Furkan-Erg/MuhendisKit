// Part of the Chili3d Project, under the AGPL-3.0 License.
// See LICENSE file in the project root for full license information.

import { AppBuilder } from "@chili3d/builder";
import { type IApplication, Logger } from "@chili3d/core";
import { Loading } from "./loading";
import { startMkBridge } from "./mkBridge";
import { parseStartupParams } from "./startupParams";

const loading = new Loading();
document.body.appendChild(loading);

async function handleApplicaionBuilt(app: IApplication) {
    document.body.removeChild(loading);

    const { plugins, fileUrl } = parseStartupParams(window.location.search);
    for (const plugin of plugins) {
        Logger.info(`loading plugin from: ${plugin}`);
        await app.pluginManager.loadFromUrl(plugin);
    }
    if (fileUrl) {
        Logger.info(`loading file from: ${fileUrl}`);
        await app.loadFileFromUrl(fileUrl);
    }
    // MühendisKit: open straight into an empty model and accept commands from the host window.
    if (!fileUrl && !app.activeView) await app.newDocument("Model");
    startMkBridge(app);
}

// prettier-ignore
new AppBuilder()
    .useIndexedDB()
    .useWasmOcc()
    .useParametric()
    .useThree()
    .useUI()
    .build()
    .then(handleApplicaionBuilt)
    .catch((err) => {
        alert(err.message);
    });
