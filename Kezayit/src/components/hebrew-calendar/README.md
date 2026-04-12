# hebrew-calendar

Hebrew calendar feature. Singleton route `/hebrew-calendar`, title `לוח שנה`.

## Files

`HebrewCalendarPage.vue` — page shell with two tabs: "לוח" (weekly calendar) and "זמני היום" (zmanim). Owns the `useZmanim` instance so the active city flows into both tabs. "היום" button scrolls the list back to the current week.

`WeeklyCalendarList.vue` — infinite virtual list of weeks (±2 years, ~208 weeks). Each week renders 7 day rows showing: Hebrew date (gematriya), Gregorian date, parasha (on Shabbat), holidays, candle lighting time, and havdalah time. Uses `@tanstack/vue-virtual` with `measureElement` for dynamic row heights. Starts scrolled to today's week via `scrollToIndex` on mount.

`useWeeklyCalendar.ts` — builds the full week array. Calls `HebrewCalendar.calendar` once for the entire ±2-year range with `sedrot`, `candlelighting`, and `havdalahMins: 50`. Events are indexed by date string then distributed into each day. Location is resolved via `Location.lookup` (for known cities) or a manual `Location` constructor. Exposes `weeks` (computed, reactive to city) and `todayWeekIndex` (always 104).

`useZmanim.ts` — location detection and daily zmanim. Tries `navigator.geolocation` on init, finds nearest city by distance, falls back to Jerusalem. Manual city selection persisted to IDB at key `zmanim.city`. Calculates 15 zmanim using `Zmanim` from `@hebcal/core`.

`ZmanimPanel.vue` — zmanim list with a location button that opens a scrollable city picker (20 cities). Shows a fallback hint when geolocation is unavailable.

## Patterns

- All `@hebcal/core` calls use `il: true` (Israel mode) when the city is in Israel.
- `CandleLightingEvent.eventTimeStr` and `HavdalahEvent.eventTimeStr` give the pre-formatted time string directly — no manual formatting needed.
- Niqqud is stripped from all rendered Hebrew strings via `replace(/[\u0591-\u05C7]/g, '')`.
- `renderGematriya()` on an `HDate` returns the full Hebrew date; split by space to get day/month/year tokens.
- The weekly list never re-fetches — the full range is computed once and cached by Vue's `computed`.
