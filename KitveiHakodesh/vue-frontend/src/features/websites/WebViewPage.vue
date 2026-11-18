<script setup lang="ts">
import { computed, inject, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import MessageBanner from '@/components/MessageBanner.vue'
import { t } from '@/i18n'
import { claimSiteFrame, releaseSiteFrame, siteFrames } from './siteFrames'
import { loadWebsites, websiteForUrl, websitesRef } from './websitesList'

/**
 * A website tab. The site's iframe does NOT live here: it lives in SiteFrameLayer for as long as
 * the tab, so switching tabs keeps it alive. This page is the placeholder the layer positions the
 * iframe over (its CSS anchor), and it claims the tab's frame while shown and releases it when
 * the tab is switched away (siteFrames.ts). Only an address on the websites list is ever loaded.
 */
const paneNavigation = usePaneNavigation()
const paneId = inject<1 | 2>('paneId', 1)
const tabId = paneNavigation.activeTabId
const owner = Symbol('web-view-page')
const listLoaded = ref(false)
onMounted(() => void loadWebsites().then(() => (listLoaded.value = true)))

const webUrl = computed(() => paneNavigation.activeTab.webUrl ?? '')
const allowed = computed(() => !!webUrl.value && !!websiteForUrl(webUrl.value, websitesRef.value))
const frame = computed(() => siteFrames.get(tabId))

watch([allowed, webUrl, listLoaded], () => {
  if (listLoaded.value && allowed.value) claimSiteFrame(tabId, owner, webUrl.value, paneId)
}, { immediate: true })
onBeforeUnmount(() => releaseSiteFrame(tabId, owner))
</script>

<template>
  <div class="web-view-page">
    <div v-if="!webUrl" class="web-view-message">{{ t('websites.noSite') }}</div>
    <template v-else-if="listLoaded">
      <div v-if="allowed" class="web-view-anchor" :style="`anchor-name: --site-${tabId}`">
        <div v-if="frame?.loading" class="web-view-message">{{ t('websites.loading') }}</div>
      </div>
      <div v-else class="web-view-message">
        <MessageBanner :message="t('websites.notAllowedUrl', { url: webUrl })" :dismissible="false" />
      </div>
    </template>
  </div>
</template>

<style scoped>
.web-view-page {
  height: 100%;
  display: flex;
  flex-direction: column;
}
.web-view-anchor {
  flex: 1;
  display: flex;
}
.web-view-message {
  margin: auto;
  padding: 16px;
  color: var(--text-secondary);
}
</style>
