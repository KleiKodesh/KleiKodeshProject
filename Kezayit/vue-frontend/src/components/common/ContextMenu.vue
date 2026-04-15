<script setup lang="ts">
import { ref, computed, nextTick } from 'vue'
import { useDropdownClose } from '@/composables/useDropdownClose'

export interface ContextMenuItem {
  label: string
  action: () => void
}

const props = defineProps<{ items: ContextMenuItem[] }>()

const visible = ref(false)
const x = ref(0)
const y = ref(0)
const menuRef = ref<HTMLElement>()
const menuStyle = computed(() => ({ left: `${x.value}px`, top: `${y.value}px` }))

useDropdownClose(menuRef, () => {
  visible.value = false
})

async function show(event: MouseEvent) {
  event.preventDefault()
  x.value = event.clientX
  y.value = event.clientY
  visible.value = true
  await nextTick()
  if (menuRef.value) {
    const rect = menuRef.value.getBoundingClientRect()
    if (x.value + rect.width > window.innerWidth) x.value = window.innerWidth - rect.width - 4
    if (y.value + rect.height > window.innerHeight) y.value = window.innerHeight - rect.height - 4
  }
}

function hide() {
  visible.value = false
}

function runItem(item: ContextMenuItem) {
  item.action()
  hide()
}

defineExpose({ show, hide })
</script>

<template>
  <Teleport to="body">
    <div v-if="visible" ref="menuRef" class="context-menu" :style="menuStyle" @click.stop>
      <div v-for="item in items" :key="item.label" class="context-menu-item" @click="runItem(item)">
        {{ item.label }}
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.context-menu {
  position: fixed;
  z-index: 9999;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  box-shadow:
    0 2px 8px rgba(0, 0, 0, 0.12),
    0 8px 24px rgba(0, 0, 0, 0.08);
  min-width: 160px;
  direction: rtl;
}
.context-menu-item {
  padding: 8px 16px;
  cursor: pointer;
  font-size: 13px;
  text-align: right;
  border-bottom: 1px solid var(--border-color);
}
.context-menu-item:last-child {
  border-bottom: none;
}
.context-menu-item:hover {
  background: var(--bg-hover);
}
</style>
