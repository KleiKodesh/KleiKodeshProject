<script setup lang="ts">
import { useClipboard } from '@vueuse/core'
import SettingRow from './SettingRow.vue'
import { openUrlInSystemBrowser } from '@/webview-host/bridge'
import { useIsGithubReachable } from '@/composables/useIsGithubReachable'

// "Report a problem": opens the GitHub new-issue page. When GitHub is blocked (common behind
// content filters) the support email is shown instead - never both.

const GITHUB_NEW_ISSUE_URL = 'https://github.com/KleiKodesh/KleiKodeshProject/issues/new/choose'
const SUPPORT_EMAIL = 'kleikodeshproject@gmail.com'

const { isGithubReachable } = useIsGithubReachable()
const { copy: copySupportEmail, copied: isSupportEmailCopied } = useClipboard({ legacy: true })
</script>

<template>
  <!-- ── דיווח על בעיה ── -->
  <div data-section="section-report-problem" :data-section-label="$t('settings.reportProblem.section')">
    <div id="section-report-problem" class="section-label">{{ $t('settings.reportProblem.section') }}</div>

    <SettingRow :label="$t('settings.reportProblem.label')">
      <div class="report-problem">
        <span v-if="isGithubReachable === null" class="report-problem-text">{{ $t('settings.reportProblem.checking') }}</span>
        <button
          v-else-if="isGithubReachable"
          class="settings-btn"
          @click="openUrlInSystemBrowser(GITHUB_NEW_ISSUE_URL)"
        >
          {{ $t('settings.reportProblem.open') }}
        </button>
        <template v-else>
          <span class="report-problem-text">{{ $t('settings.reportProblem.emailNote') }}</span>
          <span class="report-problem-email" dir="ltr">{{ SUPPORT_EMAIL }}</span>
          <button class="settings-btn" @click="copySupportEmail(SUPPORT_EMAIL)">
            {{ isSupportEmailCopied ? $t('settings.reportProblem.copied') : $t('settings.reportProblem.copy') }}
          </button>
        </template>
      </div>
    </SettingRow>
  </div>
</template>

<style scoped>
.report-problem {
  width: 100%;
}

.report-problem-text {
  font-size: 12px;
  color: var(--text-secondary);
}

.report-problem-email {
  user-select: text;
  font-size: 13px;
}
</style>
