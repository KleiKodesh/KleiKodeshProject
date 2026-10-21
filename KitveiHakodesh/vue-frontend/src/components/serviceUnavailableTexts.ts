import type { ServiceUnavailableReason } from '@/webview-host/serviceAvailability'
import { t } from '@/i18n'

/** UI texts of ServiceUnavailableBanner.vue, one entry per reason. */
export const SERVICE_UNAVAILABLE_TEXTS: Record<ServiceUnavailableReason, { title: string; body: string }> = {
  NotRunning: {
    title: t('components.service.notRunningTitle'),
    body: t('components.service.notRunningBody'),
  },
  NotInstalled: {
    title: t('components.service.notInstalledTitle'),
    body: t('components.service.notInstalledBody'),
  },
  InstallDeclined: {
    title: t('components.service.installDeclinedTitle'),
    body: t('components.service.installDeclinedBody'),
  },
  InstallFailed: {
    title: t('components.service.installFailedTitle'),
    body: t('components.service.installFailedBody'),
  },
  StartFailed: {
    title: t('components.service.startFailedTitle'),
    body: t('components.service.startFailedBody'),
  },
}

/** The banner button's label per reason. */
export const SERVICE_ACTION_LABELS: Record<ServiceUnavailableReason, string> = {
  NotRunning: t('components.service.retry'),
  NotInstalled: t('components.service.install'),
  InstallDeclined: t('components.service.retry'),
  InstallFailed: t('components.service.retry'),
  StartFailed: t('components.service.repair'),
}

export const SERVICE_RETRYING_LABEL = t('components.service.retrying')

/** Banner texts for a seforim DB left mid-update by Otzaria (hot journal beside the file). */
export const SEFORIM_DB_UPDATE_INTERRUPTED_TEXTS = {
  title: t('components.service.dbInterruptedTitle'),
  body: t('components.service.dbInterruptedBody'),
}

/** Banner texts when no seforim DB (the Otzaria library) exists at the configured path. */
export const SEFORIM_DB_MISSING_TEXTS = {
  title: t('components.service.dbMissingTitle'),
  body: t('components.service.dbMissingBody'),
}

/** Banner texts when the seforim DB file exists but cannot be opened. */
export const SEFORIM_DB_UNREADABLE_TEXTS = {
  title: t('components.service.dbUnreadableTitle'),
  body: t('components.service.dbUnreadableBody'),
}

export const SEFORIM_DB_PICK_LABEL = t('components.service.pickLibrary')
