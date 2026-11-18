# hebrew-calendar

Hebrew calendar widget (features/widgets): opens in a floating window, not a tab. Two views — monthly grid and weekly detail — switchable from the header. City selection for zmanim is persisted to localStorage.

**HebrewCalendarPage.vue** — orchestrator. Owns the view-mode toggle, city picker, and routes between `MonthlyView` and `WeeklyView`.

**MonthlyView.vue** — monthly grid showing Hebrew dates in gematriya, holidays, and the weekly parasha. Navigates by Gregorian month but displays Hebrew month labels alongside.

**WeeklyView.vue** — week view with a full day-by-day breakdown: holidays, parasha, candle lighting, havdalah, omer count, and all daily learning cycles. Also shows zmanim for the selected city.

**DayRow.vue** — single day row used inside the weekly view.

**CalendarHeader.vue** — navigation header shared by both views: prev/next buttons, today button, and the Hebrew + Gregorian period label.

**calendarTypes.ts** — TypeScript types for `CalendarDay`, `CalendarWeek`, and `City`. Import types from here.

**useMonthlyView.ts** — month navigation state. Keeps Gregorian and Hebrew month/year in sync. Use `jumpToHebrew(year, month)` to navigate to a specific Hebrew month.

**useWeeklyView.ts** — week navigation state and data. Builds a full `CalendarWeek` for the current week using `@hebcal/core`, including all events, candle lighting times, and daily learning via `hebrewCalendarLearning.ts`. Rebuilds automatically when the city changes. Requesting a week that includes a molad event causes `@hebcal/core/molad.js` to run, which requires `Temporal.ZonedDateTime` — covered by the conditional polyfill in `src/stubs/temporal-polyfill-stub.ts`.

**useZmanim.ts** — city selection and the day's printed zmanim (`calcDayZmanim`, used by `useWeeklyView`). Tries geolocation first, falls back to Jerusalem. Persists the selected city to localStorage via `KEYS.SETTINGS_ZMANIM_CITY`. Exports `CITIES` (the full city list) and `useZmanim` (the composable for the city picker UI).

Zmanim follow the real Itim Labina luach (itimlabina.co.il) of each city:

- **itimLabinaLuachot.ts** — data: for each Itim Labina luach, which zman id their printed view shows first for each of the 14 rows (sea-level netz instead of the uncomputable visible netz), the definitions of those zmanim, and which luach each city follows.
- **itimLabinaZmanim.ts** — `itimLabinaDayZmanim(city, date)`: evaluates those definitions on top of `kosherZmanim/` (a verified port of KosherKotlin's ItimLabinaCalculator) and returns each row's instant and printed string. Also used by the home page's next-zman bar.
- **itimLabinaTimeFormat.ts** — prints a time exactly like their client (15-second rounding, `.`/`:` minute marks, `candle1`), in the city's time zone.
- **cityClimates.ts** — per-city monthly temperatures (WorldClim 2.1, CC BY 4.0) that drive the refraction; the city coordinates in `calendarTypes.ts` are Itim Labina's own locations where they have one.
