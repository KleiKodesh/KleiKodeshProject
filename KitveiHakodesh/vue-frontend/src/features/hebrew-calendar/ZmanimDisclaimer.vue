<script setup lang="ts">
// Always-visible note under every zmanim display: the times are approximate.
// Wording comes from the zmanim library itself (DISCLAIMER), in the UI language.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { DISCLAIMER } from 'kleikodesh-zmanim'

const { locale } = useI18n()
const lang = computed(() => (locale.value === 'en' ? 'en' : 'he'))
</script>

<template>
  <p class="zmanim-disclaimer" :dir="lang === 'he' ? 'rtl' : 'ltr'" :lang="lang">{{ DISCLAIMER[lang] }}</p>
</template>

<style scoped>
.zmanim-disclaimer {
  margin: 6px 2px 0;
  font-size: 11px;
  line-height: 1.35;
  color: var(--text-secondary);
  opacity: 0.85;
  text-align: start;
}
</style>
