import { ref } from 'vue'

/** Set to true just before a reset/reload to block all interaction until the page reloads. */
export const resetting = ref(false)
