<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useThemeStore } from '@/theme/themeStore'
import { BACKGROUND_TINT_MIN, BACKGROUND_TINT_MAX, BACKGROUND_TINT_DEFAULT, getTheme, hasBackgroundTint } from '@/theme/themes'
import SliderSetting from './SliderSetting.vue'

// One slider for the settings page and the setup wizard: how emphasized the chosen
// theme's background color is (beige, green...), brightness kept (see tintTheme).
const { backgroundTint, themePreset } = storeToRefs(useThemeStore())

// Hidden for plain white/gray themes — nothing to tint there.
const isTintable = computed(() => {
  const theme = getTheme(themePreset.value)
  return !!theme && hasBackgroundTint(theme)
})

function formatTint(value: number): string {
  if (value === BACKGROUND_TINT_DEFAULT) return 'כמו בערכה'
  if (value === BACKGROUND_TINT_MIN) return 'ללא גוון'
  return `${value}%`
}
</script>

<template>
  <SliderSetting
    v-if="isTintable"
    label="עוצמת גוון הרקע"
    v-model="backgroundTint"
    :min="BACKGROUND_TINT_MIN"
    :max="BACKGROUND_TINT_MAX"
    :step="5"
    :format-value="formatTint"
    hint="כמה בולט צבע הרקע של ערכת הנושא (בז', ירוק וכו'). הבהירות לא משתנה, כך שהקריאה נשארת נוחה."
  />
</template>
