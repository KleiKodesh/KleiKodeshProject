# halachic-units

Halachic unit converter. Singleton route `/midot`. Converts between biblical, Talmudic, and modern units across six measurement systems: length, area, volume, weight, coins, and time. Supports multiple halachic opinions for the Talmudic-to-metric conversion factor.

**HalachicUnitsPage.vue** — the full converter UI. System selector, opinion selector, from/to unit pickers, numeric input, result display, metric hint, and a step-by-step conversion explanation with source attribution. Shows a one-time disclaimer before first use (persisted via `settingsStore.midotDisclaimerAccepted`).

**halachicUnits.ts** — all conversion logic. `convert(value, from, to, opinion, useRounded)` is the core function. `toMetric` returns the metric equivalent of a Talmudic value. `formatResult` formats numbers for display. `explainConversion` returns a human-readable calculation string and the authoritative source for the conversion. Import from here for any unit math — do not duplicate conversion logic elsewhere.

**units/** — unit definitions organized by measurement system. Each file exports a record of unit name → `Unit` object with an `anchor` value (in base-unit space), system tag, and optional source/disputed fields. `index.ts` re-exports everything. `types.ts` defines the `Unit`, `UnitSource`, `OpinionKey`, and `MetricFactors` types. The opinion factors (Naeh, Chazon Ish, etc.) live in `index.ts` as `ALL_OPINIONS`.
