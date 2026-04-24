# Desktop Layout Plan

## Summary

This plan preserves the current mobile/android-style app shell while adding an opt-in desktop layout mode. The desktop mode will use `dockview` via `dockview-vue` for a true docking layout with a main panel for the core workspace (`book-view`, `search`, `pdf-view`) and a side panel for navigation/widget pages (`books-fs`, `dictionary`, `midot`, `settings`, `hebrewbooks`, etc.).

Because the desktop layout is structurally different, it should be a separate shell instead of a responsive variant of the existing page.

## Current code findings

- `src/App.vue` is the application shell. It currently renders:
  - `AppTitleBar` as the top title/tool bar
  - `AppPageView` as the page content area
- `AppPageView.vue` dynamically loads page components by `tabStore.activeTab.route`.
- The app currently uses a global tab model in `src/stores/tabStore.ts` with:
  - multiple normal tabs for `/book-view`, `/search`, and other routes
  - singleton routes for navigation pages like `/settings`, `/books`, `/dictionary`, etc.
- The theme system uses CSS variables via `src/theme/themes.ts` and `src/stores/settingsStore.ts`.
- `BookViewPage.vue` already has an internal pane layout:
  - main content + optional bottom commentary panel via `BookViewSplitPane`
  - side toolbar placement logic for top/right/left/bottom toolbar positions
- There is no existing desktop docking framework in the codebase.

## Recommended architecture

### 1. Add a desktop layout mode setting

- Add a new `settingsStore` field, for example: `layoutMode: 'mobile' | 'desktop'`
- Add a settings control in `SettingsGeneralPane.vue` or a dedicated layout section.
- Keep the default mode as `mobile` to preserve current behavior.

### 2. Create separate app shells

- Keep the current shell as `MobileShell` (or maintain `AppTitleBar` + `AppPageView`).
- Add a new `DesktopShell` component in `src/components/layout/`.
- In `App.vue`, choose the shell based on `settingsStore.layoutMode`.

### 3. Desktop shell structure

A desktop shell should include:

- `DesktopTitleBar` or enhanced `AppTitleBar`
  - current page title
  - title for active main content
  - global actions (theme toggle, tab actions, panel toggle)
- `DesktopSideNav`
  - navigation pages in a side dock
  - visible by default in desktop layout
  - can use singleton page rendering for `books-fs`, `dictionary`, `midot`, `settings`, `hebrewbooks`, `workspaces`, etc.
- `DesktopMainPanel`
  - main workspace area for `book-view` and `search`
  - support multiple main tabs or panes if desired
- placeholder for a future bottom dock
  - support `commentary` tabs as a later stage
  - this can initially be a `DesktopBottomPanel` stub or a `BottomDockPlaceholder`

### 4. Mapping pages to desktop panels

- Main panel:
  - `/book-view`
  - `/search`
  - `/pdf-view`
- Side panel:
  - `/books`
  - `/dictionary`
  - `/midot`
  - `/settings`
  - `/hebrewbooks`
  - `/workspaces`
  - homepage can remain unused or show an overview widget if desired

### 5. Preserve current tab state model

- Keep the current `tabStore` for main pages, especially book/search tabs.
- Treat side panel pages as singleton dock widgets, not user-closeable tab content by default.
- This retains the existing persistence and page loading logic while separating desktop navigation.

## Theme compatibility and docking package evaluation

- The app uses CSS variables for colors and theme state.
- `dockview-vue` is the chosen package, so the evaluation now focuses on its Vue bindings and CSS override support.
- If it supports:
  - custom class names
  - CSS variables
  - style overrides
  then it can be adapted without downloading the source.
- If the package has hardcoded styles and no theme hooks, then source customization or a fork may be required.
- Given the current app architecture, the safest path is:
  1. install `dockview-vue`
  2. implement it as a desktop-only wrapper around the desktop shell
  3. style it with app CSS variables and optionally override its default stylesheet
  4. only if it proves too rigid, fall back to a custom desktop shell using CSS grid/flex

## Implementation phases

### Phase 1: Desktop mode scaffolding

1. Install `dockview-vue` and import its stylesheet.
2. Add `layoutMode` to `src/stores/settingsStore.ts` and localStorage persistence.
3. Add a settings control in `src/components/settings/SettingsGeneralPane.vue`.
4. Create `src/components/layout/DesktopShell.vue`.
5. Update `src/App.vue` to render either mobile or desktop shell.
6. Create `src/components/layout/DesktopSideNav.vue` and `src/components/layout/DesktopMainPanel.vue`.
7. Add a desktop title bar variant or extend `AppTitleBar.vue`.

### Phase 2: Desktop panel behavior

1. Make side panel pages render in the desktop dock.
2. Make main panel pages render as the primary workspace.
3. Keep book/search page loading logic intact.
4. Add a desktop navigation state so side page selection does not conflict with main page tabs.
5. Add transition/animation if needed for panel switching.

### Phase 3: Bottom commentary panel design (future)

1. Design a bottom dock area for commentary tabs.
2. Update `CommentaryView` to support separate commentary tabs in the bottom dock.
3. Implement a `DesktopBottomPanel.vue` that can host multiple commentary tabs.
4. Keep the current `BookViewSplitPane` behavior during the transition.

## Folder and file refactor suggestions

Suggested structure:

- `src/components/layout/`
  - `AppTitleBar.vue`
  - `AppPageView.vue`
  - `DesktopShell.vue`
  - `DesktopSideNav.vue`
  - `DesktopMainPanel.vue`
  - `DesktopTitleBar.vue`
- `src/components/layout/common/`
  - shared components such as `SideNavItem`, `DockPane`, `PanelHeader`
- `src/components/app-pages/`
  - existing main pages remain where they are
- `src/components/navigation-pages/`
  - perhaps group `books-fs`, `settings`, `dictionary` into a navigation page folder

This keeps mobile and desktop concerns separate while preserving current component boundaries.

## What should be done first

1. Implement desktop mode as an opt-in path, not a responsive override.
2. Keep the existing mobile layout unchanged.
3. Build the desktop shell around the current tab/page router.
4. Add desktop settings and persistence.
5. Separate desktop-specific components from shared components.

## Risks and recommendations

- A full docking system is large; start with static panels and later add drag/dock behavior.
- Do not migrate the commentary view immediately; reserve that for stage 2.
- Keep the current `book-view` page as the main-panel entry point to minimize refactor risk.
- Avoid forcing native route changes for desktop navigation; use the existing `tabStore` plus a desktop selection model.

## Conclusion

The most practical path is to add a dedicated desktop shell that reuses the app’s page-loading and theme system. Use `dockview-vue` as the first desktop engine, applying your theme through CSS variables and style overrides. If it proves too rigid, keep the desktop shell isolation and fall back to a custom docking wrapper later.

If you want, I can next draft the exact component structure and props for `DesktopShell.vue` and `DesktopSideNav.vue`.