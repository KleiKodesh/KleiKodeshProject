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

Zmanim are computed on the device with `kosherZmanim/` (a port of an earlier zmanim library / KosherZmanim, LGPL-2.1) using the standard NOAA calculator (`calculators/noaaCalculator.ts`, the calendar's default) and sea-level sunrise/sunset:

- **dayZmanim.ts** - `dayZmanim(city, date)`: each row's instant and printed time, using standard definitions (alot 16.1 deg, misheyakir 11.5 deg, shma/tfila GRA and MGA 72 minutes, chatzot, mincha gedola/ketana, plag GRA, tzeit 8.5 deg, chatzot halaila = solar midnight). Times are printed HH:MM with stringent rounding: starts round up, deadlines round down, chatzot to the nearest minute (the `ROUNDING` table). Also used by the home page's next-zman bar.

City list (`calendarTypes.ts`): Contains data from GeoNames (geonames.org), licensed under CC BY 4.0.
Some neighbourhood coordinates come from Wikidata (CC0). Where Wikidata has no elevation, it comes from the Open-Meteo elevation API (CC BY 4.0), based on the Copernicus DEM GLO-90 (produced using Copernicus WorldDEM-90 (c) DLR e.V. 2010-2014 and (c) Airbus Defence and Space GmbH 2014-2018, provided under COPERNICUS by the European Union and ESA; all rights reserved).
