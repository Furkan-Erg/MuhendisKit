import 'element-plus/dist/index.css'

import { LIBREDWG_PARSER_WORKER_FILE } from '@mlightcad/cad-simple-viewer'
import { i18n } from '@mlightcad/cad-viewer'
import { AcDbDatabaseConverterManager, AcDbFileType } from '@mlightcad/data-model'
import { AcDbLibreDwgConverter } from '@mlightcad/libredwg-converter'
import { createApp } from 'vue'

import App from './App.vue'

// DWG reading through LibreDWG (GPL-3.0, compatible with the app's AGPL-3.0).
AcDbDatabaseConverterManager.instance.register(
  AcDbFileType.DWG,
  new AcDbLibreDwgConverter({ convertByEntityType: false, useWorker: true, parserWorkerUrl: `./assets/${LIBREDWG_PARSER_WORKER_FILE}` })
)

const app = createApp(App)
app.use(i18n)
app.mount('#app')
document.getElementById('loader')?.remove()
