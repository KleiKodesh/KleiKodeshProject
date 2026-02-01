<template>
    <div ref="dropdownContainer"
         class="touch-dropdown-container">
        <slot name="trigger"
              :toggle="toggle"
              :isOpen="isOpen" />

        <transition name="dropdown-fade">
            <div v-if="isOpen"
                 class="touch-dropdown-menu"
                 :class="{ 'dropdown-above': showAbove }"
                 @click.stop>
                <slot name="content"
                      :close="close" />
            </div>
        </transition>
    </div>
</template>

<script setup lang="ts">
import { ref, nextTick, onMounted } from 'vue'
import { useClickOutside } from '../../composables/useClickOutside'

interface Props {
    disabled?: boolean
    closeOnItemClick?: boolean
}

const props = withDefaults(defineProps<Props>(), {
    disabled: false,
    closeOnItemClick: true
})

const emit = defineEmits<{
    'open': []
    'close': []
    'toggle': [isOpen: boolean]
}>()

const isOpen = ref(false)
const showAbove = ref(false)
const dropdownContainer = ref<HTMLElement>()

// Use touch-friendly click outside
useClickOutside(dropdownContainer, () => {
    if (isOpen.value) {
        close()
    }
})

const toggle = () => {
    if (props.disabled) return

    if (isOpen.value) {
        close()
    } else {
        open()
    }
}

const open = async () => {
    if (props.disabled) return

    isOpen.value = true
    emit('open')
    emit('toggle', true)

    // Check if dropdown should appear above trigger
    await nextTick()
    checkDropdownPosition()
}

const close = () => {
    isOpen.value = false
    showAbove.value = false
    emit('close')
    emit('toggle', false)
}

const checkDropdownPosition = () => {
    if (!dropdownContainer.value) return

    const rect = dropdownContainer.value.getBoundingClientRect()
    const viewportHeight = window.innerHeight
    const spaceBelow = viewportHeight - rect.bottom
    const spaceAbove = rect.top

    // Show above if there's more space above and not enough below
    showAbove.value = spaceAbove > spaceBelow && spaceBelow < 200
}

// Handle item clicks
const handleItemClick = () => {
    if (props.closeOnItemClick) {
        close()
    }
}

// Expose methods for parent components
defineExpose({
    isOpen,
    open,
    close,
    toggle
})

// Listen for window resize to reposition dropdown
onMounted(() => {
    window.addEventListener('resize', checkDropdownPosition)
    window.addEventListener('scroll', checkDropdownPosition)

    return () => {
        window.removeEventListener('resize', checkDropdownPosition)
        window.removeEventListener('scroll', checkDropdownPosition)
    }
})
</script>

<style scoped>
.touch-dropdown-container {
    position: relative;
    display: inline-block;
}

.touch-dropdown-menu {
    position: absolute;
    top: 100%;
    left: 0;
    right: 0;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 0.375rem;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
    z-index: 1000;
    max-height: 300px;
    overflow-y: auto;
    -webkit-overflow-scrolling: touch;
    overscroll-behavior: contain;
    min-width: 200px;
}

.touch-dropdown-menu.dropdown-above {
    top: auto;
    bottom: 100%;
}

/* Touch-friendly dropdown items */
.touch-dropdown-menu :deep(.dropdown-item) {
    min-height: 44px;
    padding: 12px 16px;
    display: flex;
    align-items: center;
    gap: 12px;
    cursor: pointer;
    touch-action: manipulation;
    transition: background-color 0.1s ease;
    border-bottom: 1px solid var(--border-color);
}

.touch-dropdown-menu :deep(.dropdown-item:last-child) {
    border-bottom: none;
}

/* Touch-friendly hover/active states */
@media (hover: hover) {
    .touch-dropdown-menu :deep(.dropdown-item:hover) {
        background: var(--hover-bg);
    }
}

.touch-dropdown-menu :deep(.dropdown-item:active) {
    background: var(--active-bg);
    transform: scale(0.98);
}

/* Dropdown animations */
.dropdown-fade-enter-active,
.dropdown-fade-leave-active {
    transition: opacity 0.15s ease, transform 0.15s ease;
}

.dropdown-fade-enter-from {
    opacity: 0;
    transform: translateY(-8px);
}

.dropdown-fade-leave-to {
    opacity: 0;
    transform: translateY(-8px);
}

.dropdown-fade-enter-to,
.dropdown-fade-leave-from {
    opacity: 1;
    transform: translateY(0);
}

/* For dropdowns that appear above */
.dropdown-above.dropdown-fade-enter-from,
.dropdown-above.dropdown-fade-leave-to {
    transform: translateY(8px);
}
</style>