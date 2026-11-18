<script setup lang="ts">
import { ref, onMounted, defineAsyncComponent } from 'vue'
import { restoreLocalFile } from '@/webview-host/bridge'
import { usePaneNavigation } from '@/composables/usePaneNavigation'

// The HTML viewer reads its page off the (stand-in) tab, exactly as in a real tab.
const HtmlViewPage = defineAsyncComponent(() => import('@/features/html-view/HtmlViewPage.vue'))

const props = defineProps<{ filePath: string }>()
const navigation = usePaneNavigation()
const failed = ref(false)
const ready = ref(false)

onMounted(async () => {
  const restored = await restoreLocalFile(props.filePath)
  if (!restored?.url) { failed.value = true; return }
  // Always run the Otzaria add-in bridge: an add-in needs it, and on a plain page it
  // stays inert (see useOtzariaAddinBridge).
  navigation.updateActiveTab({
    localFilePath: props.filePath,
    localFileVirtualUrl: restored.url,
    isOtzariaAddin: true,
  })
  ready.value = true
})
</script>

<template>
  <div v-if="failed" class="custom-widget-error">{{ $t('layout.widgets.fileMissing', { path: filePath }) }}</div>
  <HtmlViewPage v-else-if="ready" />
  <div v-else class="custom-widget-error">{{ $t('layout.widgets.loading') }}</div>
</template>

<style scoped>
.custom-widget-error { padding: 16px; font-size: 13px; color: var(--text-secondary); word-break: break-all; }
</style>
