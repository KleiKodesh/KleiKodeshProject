<script setup lang="ts">
/**
 * One widget, in a window of its own (DetachedWindow - the same system a commentary
 * panel uses). The window's title bar, close button, size and place are the host's;
 * this is only what goes inside.
 */
import { provide, reactive, computed, ref, watch } from 'vue'
import DetachedWindow from '@/components/DetachedWindow.vue'
import { PANE_NAVIGATION_KEY, type PaneNavigation } from '@/composables/usePaneNavigation'
import { useAppShellPane } from '@/composables/useAppShellPane'
import { showToast } from '@/composables/useToast'
import { t } from '@/i18n'
import type { Tab } from '@/stores/tabStore'
import { useWidgetWindowStore, widgetWindowSize, widgetFitsContent, type WidgetWindowState } from './widgetWindowStore'
import { findWidgetItem } from './widgetItems'

const props = defineProps<{ win: WidgetWindowState }>()
const store = useWidgetWindowStore()
// removeCustom closes a widget's window before dropping it, so an open window always has its item.
const item = computed(() => findWidgetItem(props.win.id)!)
const size = widgetWindowSize(props.win.id)
const fitContent = widgetFitsContent(props.win.id)

// The tool pages were written as tab pages: they read "their" tab (route, saved query)
// and write its title. Inside a widget they get a private stand-in tab, so they behave
// as the active page and never touch the user's real tabs. Anything that opens a real
// document (a book link in the dictionary) still goes to pane 1.
const pane = useAppShellPane(1)
const widgetTab = reactive<Tab>({ id: `widget:${props.win.id}`, title: item.value.text, route: item.value.route })
const navigation: PaneNavigation = {
  updateActiveTab: (patch) => { Object.assign(widgetTab, patch, { route: item.value.route }) },
  openTab: pane.openTab,
  openOrUpdateActiveTab: (patch, openInNewTab) => pane.openOrUpdateActiveTab(patch, openInNewTab),
  openBookTarget: pane.openBookTarget,
  navigateToDestination: pane.navigateToDestination,
  switchTab: pane.switchTab,
  get activeTabId() { return widgetTab.id },
  get activeTab() { return widgetTab },
  get tabs() { return pane.tabs.value },
}
provide(PANE_NAVIGATION_KEY, navigation)

// Opening a widget that is already open brings its window to the front.
const detachedWindow = ref<InstanceType<typeof DetachedWindow> | null>(null)
watch(() => props.win.focusRequest, () => detachedWindow.value?.focus())

function onBlocked() {
  store.close(props.win.id)
  showToast(t('layout.widgets.windowBlocked'), { variant: 'error' })
}

</script>

<template>
  <DetachedWindow
    ref="detachedWindow"
    :name="`widget-${win.id}`"
    :title="item.text"
    :width="size.width"
    :height="size.height"
    :fit-content="fitContent"
    @closed="store.close(win.id)"
    @blocked="onBlocked"
  >
    <section class="widget-body" :class="{ 'widget-body--fit': fitContent }" :aria-label="item.text" :data-widget="win.id">
      <component :is="item.page" v-bind="item.props" />
    </section>
  </DetachedWindow>
</template>

<style scoped>
.widget-body {
  flex: 1;
  min-height: 0;
  position: relative;
  overflow: auto;
  display: flex;
  flex-direction: column;
  background: var(--bg-primary);
}
.widget-body > :deep(*) { flex: 1; min-height: 0; }
/* Natural size: the window takes it (DetachedWindow fitContent). */
.widget-body--fit { flex: none; overflow: visible; }
.widget-body--fit > :deep(*) { flex: none; }
/* PageCard switches to its edge-to-edge look by WINDOW width (@media); a widget's
   window is narrow, but the card's frame is still wasted room inside it. */
.widget-body :deep(.page-card-outer) { max-width: 100%; padding: 0; }
.widget-body :deep(.page-card) { border: none; border-radius: 0; background: var(--bg-primary); }
</style>
