<script setup lang="ts">
import SettingRow from './SettingRow.vue'
import { openUrlInSystemBrowser } from '@/webview-host/bridge'

// "Report a problem": the GitHub new-issue link (opens in the system browser) and the support
// email for users whose filter blocks GitHub. Both stay selectable for copy-paste.

const GITHUB_NEW_ISSUE_URL = 'https://github.com/KleiKodesh/KleiKodeshProject/issues/new/choose'
const SUPPORT_EMAIL = 'kleikodeshproject@gmail.com'
</script>

<template>
  <!-- ── דיווח על בעיה ── -->
  <div data-section="section-report-problem" :data-section-label="$t('settings.reportProblem.section')">
    <div id="section-report-problem" class="section-label">{{ $t('settings.reportProblem.section') }}</div>

    <SettingRow :label="$t('settings.reportProblem.label')">
      <div class="report-problem">
        <a
          class="report-problem-link"
          dir="ltr"
          :href="GITHUB_NEW_ISSUE_URL"
          @click.prevent="openUrlInSystemBrowser(GITHUB_NEW_ISSUE_URL)"
        >{{ GITHUB_NEW_ISSUE_URL }}</a>
        <span class="report-problem-text">{{ $t('settings.reportProblem.emailNote') }}</span>
        <span class="report-problem-link" dir="ltr">{{ SUPPORT_EMAIL }}</span>
      </div>
    </SettingRow>
  </div>
</template>

<style scoped>
.report-problem {
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.report-problem-text {
  font-size: 12px;
  color: var(--text-secondary);
}

.report-problem-link {
  user-select: text;
  font-size: 13px;
  word-break: break-all;
}
</style>
