<script setup lang="ts">
import SettingRow from './SettingRow.vue'
import { openUrlInSystemBrowser } from '@/webview-host/bridge'
import { useIsGithubReachable } from '@/composables/useIsGithubReachable'

// "Report a problem" row (inside the Updates section): EITHER the GitHub new-issue link (opens in the system browser) OR, when
// GitHub is blocked (common behind content filters), the support email. Both selectable.

const GITHUB_NEW_ISSUE_URL = 'https://github.com/KleiKodesh/KleiKodeshProject/issues/new/choose'
const SUPPORT_EMAIL = 'kleikodeshproject@gmail.com'

const { isGithubReachable } = useIsGithubReachable()
</script>

<template>
  <SettingRow :label="$t('settings.reportProblem.label')">
    <div class="report-problem">
      <a
        v-if="isGithubReachable"
        class="report-problem-link"
        dir="ltr"
        :href="GITHUB_NEW_ISSUE_URL"
        @click.prevent="openUrlInSystemBrowser(GITHUB_NEW_ISSUE_URL)"
      >{{ GITHUB_NEW_ISSUE_URL }}</a>
      <template v-else-if="isGithubReachable === false">
        <span class="report-problem-text">{{ $t('settings.reportProblem.emailNote') }}</span>
        <span class="report-problem-link" dir="ltr">{{ SUPPORT_EMAIL }}</span>
      </template>
    </div>
  </SettingRow>
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
