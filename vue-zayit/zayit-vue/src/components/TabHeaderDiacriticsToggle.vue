<template>
    <button v-if="showButton"
            @click.stop="handleClick"
            :class="['flex-center c-pointer diacritics-toggle-btn', stateClass]"
            :title="title">
        <component :is="iconComponent"
                   class="diacritics-icon" />
    </button>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useTabStore } from '../stores/tabStore'
import DiacriticsFullIcon from './icons/DiacriticsFullIcon.vue'
import DiacriticsNikkudOnlyIcon from './icons/DiacriticsNikkudOnlyIcon.vue'
import DiacriticsNoneIcon from './icons/DiacriticsNoneIcon.vue'

const tabStore = useTabStore()

const showButton = computed(() => {
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

const title = computed(() => {
    if (diacriticsState.value === 0) return 'הסר טעמים'
    if (diacriticsState.value === 1) return 'הסר גם ניקוד'
    return 'שחזר טעמים וניקוד'
})

const handleClick = () => {
    tabStore.toggleDiacritics()
}
</script>

<style scoped>
.diacritics-toggle-btn:hover {
    background: var(--hover-bg);
    transform: scale(1.05);
}

.diacritics-icon {
    width: 16px;
    height: 16px;
    transition: opacity 0.2s ease;
}

.diacritics-toggle-btn.state-1 .diacritics-icon :deep(svg) {
    fill: #ff8c00;
}

.diacritics-toggle-btn.state-2 .diacritics-icon :deep(svg) {
    fill: #ff4500;
}
</style>