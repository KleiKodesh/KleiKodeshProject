# hebrew-calendar

Hebrew calendar widget (features/widgets): opens in a floating window, not a tab. Two views — monthly grid and weekly detail — switchable from the header. City selection for zmanim is persisted to localStorage.

**HebrewCalendarPage.vue** — orchestrator. Owns the view-mode toggle, city picker, and routes between `MonthlyView` and `WeeklyView`.

**MonthlyView.vue** — monthly grid showing Hebrew dates in gematriya, holidays, and the weekly parasha. Navigates by Gregorian month but displays Hebrew month labels alongside.

**WeeklyView.vue** — week view with a full day-by-day breakdown: holidays, parasha, candle lighting, havdalah, omer count, and all daily learning cycles. Also shows zmanim for the selected city.

**DayRow.vue** — single day row used inside the weekly view.

**CalendarHeader.vue** — navigation header shared by both views: prev/next buttons, today button, and the Hebrew + Gregorian period label.

**calendarTypes.ts** — TypeScript types for `CalendarDay`, `CalendarWeek` and `CalendarZmanim`. Import types from here.

**useMonthlyView.ts** — month navigation state. Keeps Gregorian and Hebrew month/year in sync. Use `jumpToHebrew(year, month)` to navigate to a specific Hebrew month.

**useWeeklyView.ts** — week navigation state and data. Builds a full `CalendarWeek` for the current week using `@hebcal/core`, including all events, candle lighting times, and daily learning via `hebrewCalendarLearning.ts`. Rebuilds automatically when the city changes. Requesting a week that includes a molad event causes `@hebcal/core/molad.js` to run, which requires `Temporal.ZonedDateTime` — covered by the conditional polyfill in `src/stubs/temporal-polyfill-stub.ts`.

**useZmanim.ts** — place selection. Tries geolocation first (nearest listed place), falls back to Jerusalem. Persists the selected place id to localStorage via `KEYS.SETTINGS_ZMANIM_CITY` (a saved value that is not a valid id means Jerusalem).


**zmanimPlaces.ts** — the app's door to the `kleikodesh-zmanim` library (submodule `../kleikodesh-zmanim.js`, imported through the `kleikodesh-zmanim` alias in vite.config.ts, vitest.config.ts and tsconfig.app.json; LGPL-2.1; NREL SPA sun position, refraction from published physics, every zman defined from its cited halachic sources). Places are numeric ids; names come from the library in the UI language. `dayPrintedZmanim` feeds the weekly view, `dayTimedZmanim` the home page's next-zman bar. The library's data files ship beside the app in `zmanim/` (the `zmanimAssets` plugin in vite.config.ts serves them in dev and writes them into the build) and are read on first need. Definitions and rounding: see the library README.

Place data: contains data from GeoNames (geonames.org), licensed under CC BY 4.0 (details in the library README).
