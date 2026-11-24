import { ref, onMounted } from 'vue'

// Many users sit behind content filters that block GitHub. Probe it once when the caller
// mounts. A filter often answers with its own block page instead of dropping the connection,
// so an opaque no-cors fetch would wrongly succeed: probe the CORS-enabled API instead and
// require a real OK answer. Network error, block page or timeout = unreachable.

const GITHUB_PROBE_URL = 'https://api.github.com/zen'
const GITHUB_PROBE_TIMEOUT_MS = 5000

export function useIsGithubReachable() {
  /** null while the probe is still running. */
  const isGithubReachable = ref<boolean | null>(null)

  onMounted(async () => {
    try {
      const response = await fetch(GITHUB_PROBE_URL, {
        cache: 'no-store',
        signal: AbortSignal.timeout(GITHUB_PROBE_TIMEOUT_MS),
      })
      isGithubReachable.value = response.ok
    } catch {
      isGithubReachable.value = false
    }
  })

  return { isGithubReachable }
}
