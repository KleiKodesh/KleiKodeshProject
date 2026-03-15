/** Strip ASCII and Hebrew quote chars, normalize for search comparison */
export const normalize = (s: string) => s.replace(/["'״׳]/g, '').toLowerCase()
