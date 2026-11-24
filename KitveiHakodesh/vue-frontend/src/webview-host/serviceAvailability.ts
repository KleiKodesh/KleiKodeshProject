/**
 * Whether the KitveiHakodesh service can be reached right now, and if not, why.
 *
 * Every data op goes through the service (serviceClient.ts), so when it is unreachable the
 * whole app is affected. Rather than each feature failing with its own generic message, the
 * client records the failure here and ONE banner (ServiceUnavailableBanner.vue) says what is
 * wrong and offers "try again". Any later successful call clears it.
 *
 * The reasons come from the C# host (KitveiHakodeshServiceUnavailableReason, sent in the
 * relay's X-KHS-Unavailable header); the dev browser reports only NotRunning.
 */
import { ref } from 'vue'

export type ServiceUnavailableReason =
  | 'NotRunning'
  | 'NotInstalled'
  | 'InstallDeclined'
  | 'InstallFailed'
  | 'StartFailed'

const KNOWN_REASONS: readonly ServiceUnavailableReason[] = [
  'NotRunning', 'NotInstalled', 'InstallDeclined', 'InstallFailed', 'StartFailed',
]

/** null while the service answers; the reason once it has stopped answering. */
export const serviceUnavailableReason = ref<ServiceUnavailableReason | null>(null)

/** True while a "try again" is running. */
export const isRetryingService = ref(false)

export function parseUnavailableReason(header: string | null | undefined): ServiceUnavailableReason {
  return KNOWN_REASONS.find((r) => r === header) ?? 'NotRunning'
}

export function markServiceAvailable(): void {
  if (serviceUnavailableReason.value !== null) serviceUnavailableReason.value = null
}

export function markServiceUnavailable(reason: ServiceUnavailableReason): void {
  serviceUnavailableReason.value = reason
}

/**
 * Reasons that will not fix themselves by retrying the same request: the user has to act
 * (accept the install prompt) or something is broken. The client fails fast on these
 * instead of running its retry ladder.
 */
export function isPersistentReason(reason: ServiceUnavailableReason): boolean {
  return reason !== 'NotRunning'
}

/** Thrown by serviceClient when the service cannot be reached; carries the reason. */
export class ServiceUnavailableError extends Error {
  constructor(public readonly reason: ServiceUnavailableReason, detail?: string) {
    super(detail || `KitveiHakodesh service unavailable (${reason})`)
    this.name = 'ServiceUnavailableError'
  }
}

/**
 * "Try again": in the hosted app, ask the host to allow one more install / repair prompt and
 * bring the service up; then reload so every view refetches. The dev browser has no host to
 * ask - the dev server respawns the service - so it just reloads.
 */
export async function retryService(): Promise<void> {
  if (isRetryingService.value) return
  isRetryingService.value = true
  try {
    if (typeof window.__webviewAction === 'function') {
      const reply = (await window.__webviewAction('retryService')) as { ok?: boolean; reason?: string }
      if (!reply?.ok) {
        markServiceUnavailable(parseUnavailableReason(reply?.reason))
        return
      }
    }
    markServiceAvailable()
    window.location.reload()
  } catch {
    /* the banner stays; the user can try again */
  } finally {
    isRetryingService.value = false
  }
}
