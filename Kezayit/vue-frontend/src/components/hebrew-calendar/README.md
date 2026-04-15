# hebrew-calendar

Hebrew calendar page. Singleton route `/hebrew-calendar`. Two views — monthly grid and weekly detail — switchable from the header. City selection for zmanim is persisted to localStorage.

**HebrewCalendarPage.vue** — orchestrator. Owns the view-mode toggle, city picker, and routes between `MonthlyView` and `WeeklyView`.

**MonthlyView.vue** — monthly grid showing Hebrew dates in gematriya, holidays, and the weekly parasha. Navigates by Gregorian month but displays Hebrew month labels alongside.

**WeeklyView.vue** — week view with a full day-by-day breakdown: holidays, parasha, candle lighting, havdalah, omer count, and all daily learning cycles. Also shows zmanim for the selected city.

**DayRow.vue** — single day row used inside the weekly view.

**CalendarHeader.vue** — navigation header shared by both views: prev/next buttons, today button, and the Hebrew + Gregorian period label.

**calendarTypes.ts** — TypeScript types for `CalendarDay`, `CalendarWeek`, and `City`. Import types from here.

**useMonthlyView.ts** — month navigation state. Keeps Gregorian and Hebrew month/year in sync. Use `jumpToHebrew(year, month)` to navigate to a specific Hebrew month.

**useWeeklyView.ts** — week navigation state and data. Builds a full `CalendarWeek` for the current week using `@hebcal/core`, including all events, candle lighting times, and daily learning via `hebrewLearning.ts`. Rebuilds automatically when the city changes.

**useZmanim.ts** — city selection and zmanim calculation. Tries geolocation first, falls back to Jerusalem. Persists the selected city to localStorage via `KEYS.SETTINGS_ZMANIM_CITY`. Exports `CITIES` (the full city list), `calcDayZmanim` (used by `useWeeklyView`), and `useZmanim` (the composable for the city picker UI).
