<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { documentIcon } from '@/utils/documentIcons'
import UnreachableBadge from '@/layout/UnreachableBadge.vue'
import { faviconUrl, loadWebsites } from './websitesList'
import { cachedWebsiteIconSource } from './cachedWebsiteIcon'
import { useWebsiteUnreachable } from './websiteReachability'

/** A site's favicon; the globe icon while it loads or when the site has none. A red broken-link badge sits on it while the site can't be reached. */
const props = defineProps<{ url: string; size?: number }>()

const failed = ref(false)
/** The <img> source: a shipped icon as is, a remote one through the service's icon cache. */
const source = ref('')
const unreachable = useWebsiteUnreachable(() => props.url)
// Re-resolved when the address changes, including when the list loads and supplies the real icon.
watch(
  () => faviconUrl(props.url),
  async (icon) => {
    failed.value = false
    source.value = ''
    if (!/^https?:/i.test(icon)) {
      source.value = icon
      return
    }
    const resolved = await cachedWebsiteIconSource(icon)
    if (faviconUrl(props.url) !== icon) return // a newer address took over meanwhile
    if (resolved) source.value = resolved
    else failed.value = true
  },
  { immediate: true },
)
const globe = documentIcon('globe')
// History cards can render before the list (which holds the icon addresses) has loaded.
onMounted(() => void loadWebsites())
</script>

<template>
  <span class="website-favicon-wrap">
    <img
      v-if="!failed && source"
      :src="source"
      :width="size ?? 24"
      :height="size ?? 24"
      class="website-favicon"
      alt=""
      referrerpolicy="no-referrer"
      @error="failed = true"
    >
    <component :is="globe.icon24" v-else :style="{ color: globe.color, width: `${size ?? 24}px`, height: `${size ?? 24}px` }" />
    <UnreachableBadge v-if="unreachable" />
  </span>
</template>

<style scoped>
.website-favicon-wrap {
  position: relative;
  display: inline-flex;
  flex-shrink: 0;
}

.website-favicon {
  object-fit: contain;
  flex-shrink: 0;
}
</style>
