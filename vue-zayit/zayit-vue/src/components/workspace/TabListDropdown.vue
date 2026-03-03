<template>
  <transition name="slide">
    <div v-if="isVisible && visibleTabs.length > 0"
         class="tab-dropdown">
      <template v-for="tab in visibleTabs"
                :key="tab.id">
        <div :class="['flex-row bar c-pointer tab-item', { active: tab.isActive }]"
             @click="selectTab(tab.id)">
          <div></div> <!-- spacer -->
          <span class="center-text ellipsis">{{ tab.title }}</span>
          <div class="justify-end">
            <button @click.stop="closeTabById(tab.id)"
                    class="flex-center c-pointer">
              <Icon icon="fluent:dismiss-16-regular"
                    class="small-icon" />
            </button>
          </div>
        </div>
      </template>
    </div>
  </transition>
</template>

<script setup lang="ts">
import { Icon } from '@iconify/vue';
import { useTabListDropdown } from '@/components/workspace/useTabListDropdown';

const {
  isVisible,
  visibleTabs,
  toggle,
  close,
  selectTab,
  closeTabById
} = useTabListDropdown();

defineExpose({ toggle, close, isVisible });
</script>

<style scoped>
.tab-dropdown {
  position: absolute;
  width: 100%;
  max-height: 40vh;
  /* Maximum height is 40% of viewport */
  overflow-y: auto;
  /* Enable vertical scrolling if content exceeds max-height */
  /* scrollbar-gutter: stable; Reserve space for scrollbar to prevent layout shift */
  z-index: 1000;
  /* Stack above other content */
  background-color: var(--bg-secondary);
  border-bottom: 1px solid var(--border-color);
  /* Bottom border separator */
  box-shadow: 0 0.25rem 0.5rem rgba(0, 0, 0, 0.1);
  /* Subtle shadow for depth */
}

.tab-dropdown .bar {
  border-right: 3.5px solid transparent;
  /* placeholder so content doesnt shift */
}

.tab-dropdown .bar:hover {
  background: var(--hover-bg);
}

.tab-dropdown .bar.active {
  border-right: 3.5px solid var(--accent-color);
  color: var(--accent-color);
}

.tab-item>div {
  flex: 1 1 0%;
}
</style>
