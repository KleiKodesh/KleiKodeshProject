<template>
    <div v-if="showItem"
         @click.stop="handleClick"
         class="flex-row flex-center-start hover-bg c-pointer dropdown-item">
        <component :is="iconComponent"
                   class="diacritics-icon"
                   :class="stateClass" />
        <span class="dropdown-label">{{ label }}</span>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useTabStore } from '../stores/tabStore'
import DiacriticsFullIcon from './icons/DiacriticsFullIcon.vue'
import DiacriticsNikkudOnlyIcon from './icons/DiacriticsNikkudOnlyIcon.vue'
import DiacriticsNoneIcon from './icons/DiacriticsNoneIcon.vue'

const tabStore = useTabStore()

const showItem = computed(() => {
    return tabStore.activeTab?.currentPage === 'bookview'
})

const diacriticsState = computed(() => {
    return tabStore.activeTab?.bookState?.diacriticsState || 0
})

const stateClass = computed(() => {
    if (diacriticsState.value === 1) return 'state-1'
    if (diacriticsState.value === 2) return 'state-2'
    return ''
})

const iconComponent = computed(() => {
    if (diacriticsState.value === 1) return DiacriticsNikkudOnlyIcon // Nikkud only
    if (diacriticsState.value === 2) return DiacriticsNoneIcon       // No diacritics
    return DiacriticsFullIcon  // Full diacritics (nikkud + cantillation)
})

const label = computed(() => {
    if (diacriticsState.value === 0) return 'הסר טעמים'
    if (diacriticsState.value === 1) return 'הסר גם ניקוד'
    return 'שחזר טעמים וניקוד'
})



const handleClick = () => {
    tabStore.toggleDiacritics()
}
</script>

<style scoped>
.diacritics-icon {
    flex-shrink: 0;
    width: 20px;
    height: 20px;
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text-primary);
}

.diacritics-icon :deep(svg) {
    fill: currentColor;
}

.dropdown-label {
    font-size: 14px;
    color: var(--text-primary);
    white-space: nowrap;
}

.dropdown-item:hover .diacritics-icon {
    opacity: 1;
}

.diacritics-icon.state-1 :deep(svg) {
    fill: #ff8c00;
}

.diacritics-icon.state-2 :deep(svg) {
    fill: #ff4500;
}
</style>
