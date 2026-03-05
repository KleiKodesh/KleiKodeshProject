<template>
    <button @click.stop="handleToggle"
            class="flex-center c-pointer touch-interactive"
            :title="pdfPageFilters ? 'ביטול פילטרים לעמודי PDF' : 'החלת פילטרים לעמודי PDF'">
        <Icon :icon="pdfPageFilters ? 'fluent:color-24-filled' : 'fluent:color-24-regular'" />
    </button>
</template>

<script setup lang="ts">
import { Icon } from '@iconify/vue'
import { useSettings } from '@/components/settings/useSettings'
import { setPdfPageFilters } from '@/utils/themes'

const { pdfPageFilters } = useSettings()

function handleToggle() {
    pdfPageFilters.value = !pdfPageFilters.value
    setPdfPageFilters(pdfPageFilters.value)
}
</script>
