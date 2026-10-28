<template>
  <MlCadViewer
    locale="tr"
    :theme="theme"
    :base-url="BASE_URL"
    :mode="AcEdOpenMode.Write"
    @create="onCreate"
  />
</template>

<script setup lang="ts">
import { AcEdOpenMode } from '@mlightcad/cad-simple-viewer'
import { MlCadViewer } from '@mlightcad/cad-viewer'
import { ref } from 'vue'

import { startBridge } from './bridge'

// Fonts and drawing templates; the host serves this origin from its local cache after the first download.
const BASE_URL = 'https://cdn.jsdelivr.net/gh/mlightcad/cad-data@main/'
const params = new URLSearchParams(location.search)
const theme = ref<'light' | 'dark'>(params.get('theme') === 'light' ? 'light' : 'dark')

const onCreate = async () => {
  await startBridge(t => (theme.value = t))
}
</script>
