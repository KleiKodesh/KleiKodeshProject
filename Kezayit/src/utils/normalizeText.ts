const QUOTES = /["'״׳]/g

/** Normalize for search comparison: lowercase and strip quote characters */
export const normalize = (s: string) => s.toLowerCase().replace(QUOTES, '')
