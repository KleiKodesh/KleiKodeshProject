import { computed, type ComputedRef, type Ref } from 'vue'
import { useElementSize } from '@vueuse/core'
import { APP_NAV_ITEMS, APP_NAV_SETTINGS_ITEM } from './appNavItems'
import { showPopOutButton } from '@/webview-host/bridge'

/**
 * Geometry the fit arithmetic mirrors from the rail's CSS (AppNavSidebar) - a 32px button
 * on a 6px gap, inside a panel that keeps 6px of padding and a 1px border for itself.
 * Change any of them there, change them here.
 */
const NAV_BUTTON_HEIGHT = 32
const NAV_ITEM_GAP = 6
/**
 * What the panel spends on itself before any button gets a pixel: its 6px padding top and
 * bottom plus the 1px border on each. Subtracted from the measured border box to get the
 * room the buttons actually share.
 */
const NAV_PANEL_VERTICAL_CHROME = 2 * 6 + 2 * 1

/**
 * Which of the rail's buttons fit its height, and which must collapse into its "more"
 * flyout.
 *
 * A rail too short for all its buttons does not scroll them away invisibly (its scrollbar
 * is hidden): the tail that no longer fits collapses into a "more" button. The rail is one
 * flat column (the destinations, workspaces, then its own controls), and whatever the
 * height cuts off, from the bottom up, moves into the flyout in that same order. The
 * one button that never collapses is hide-rail: it is the ONLY way to close the rail, so
 * it keeps the floor, with the more button directly above it standing in for the tail.
 *
 * The fit is computed from the panel's measured content height, never observed off the
 * buttons themselves, so nothing has to render before it can be counted and the answer
 * never disagrees with itself mid-frame. What makes the arithmetic one line is that every
 * child of the panel sits on the same 38px pitch - the rail's spacer included, which costs
 * one extra flex gap even at zero height, so n buttons need exactly n*38. It is also why
 * the rail's bottom cluster is spaced like the rest rather than packed tight.
 */
export function useAppNavSidebarOverflow(
  navPanelEl: Ref<HTMLElement | null>,
  isSplitViewButtonVisible: ComputedRef<boolean>,
) {
  // Measured as the BORDER box, then the panel's own chrome is taken off, rather than
  // asking for the content box directly. Both give the same steady-state number, but
  // useElementSize seeds its refs from `offsetHeight` on mount and only switches to the
  // observed box when the ResizeObserver first fires - so a content-box read is 14px too
  // large for that first frame, which is a whole slot. A rail one button too long for one
  // frame hides that button behind a scrollbar that is deliberately invisible, and the
  // button is not in the flyout either, because the arithmetic still counts it as fitting.
  // Reading the box the seed already speaks and subtracting the chrome ourselves makes the
  // first frame agree with every frame after it.
  const { height: navPanelBorderBoxHeight } = useElementSize(navPanelEl, undefined, {
    box: 'border-box',
  })

  /** The room the panel's flex children actually share: its border box less padding and border. */
  const navPanelContentHeight = computed(() =>
    Math.max(0, navPanelBorderBoxHeight.value - NAV_PANEL_VERTICAL_CHROME),
  )

  // Every COLLAPSIBLE button, in visual order - hide-rail is deliberately not among them
  // (see above), so it has no key at all. The conditional ones (split view, pop-out) are
  // simply absent when unavailable, so visible and collapsed are always read off one list.
  // Destinations - settings included - are keyed by their labels, because the label IS the
  // routing key; the buttons that are not destinations get names of their own.
  const railButtonKeys = computed<string[]>(() => [
    ...APP_NAV_ITEMS.map((item) => item.label),
    'workspaces',
    ...(isSplitViewButtonVisible.value ? ['split-view'] : []),
    ...(showPopOutButton ? ['pop-out'] : []),
    APP_NAV_SETTINGS_ITEM.label,
  ])

  /**
   * How many buttons the measured height has room for - never fewer than the two the rail
   * always renders: the more button and the hide button below it.
   *
   * That floor is not cosmetic. Below it the rail would still render both (the hide button
   * is pinned, the more button appears precisely because everything collapsed), and with
   * the panel's scrollbar hidden the second of them is pushed out of sight - which for a
   * rail whose hide button is the ONLY way to close it means a rail nobody can close. The
   * two overflow instead, which is recoverable: the panel scrolls.
   */
  const railSlotCount = computed(() =>
    Math.max(2, Math.floor(navPanelContentHeight.value / (NAV_BUTTON_HEIGHT + NAV_ITEM_GAP))),
  )

  // The +1 is the pinned hide-rail button, which needs a slot but has no key.
  // Zero height = not measured yet (first render): assume room for everything rather than
  // flashing a fully collapsed rail for a frame.
  const hasNavOverflow = computed(
    () =>
      navPanelContentHeight.value > 0 && railSlotCount.value < railButtonKeys.value.length + 1,
  )

  // The more button and the pinned hide-rail button take the last two slots for
  // themselves; everything past the ones before them collapses.
  const visibleRailKeys = computed(() => {
    if (!hasNavOverflow.value) return railButtonKeys.value
    return railButtonKeys.value.slice(0, Math.max(0, railSlotCount.value - 2))
  })

  function railButtonVisible(key: string) {
    return visibleRailKeys.value.includes(key)
  }

  /** The destinations still on the rail - always a prefix of APP_NAV_ITEMS, order kept. */
  const visibleNavItems = computed(() =>
    APP_NAV_ITEMS.filter((item) => railButtonVisible(item.label)),
  )

  /** The collapsed tail, in rail order - what the flyout renders. */
  const overflowedRailKeys = computed(() =>
    railButtonKeys.value.slice(visibleRailKeys.value.length),
  )

  return { hasNavOverflow, railButtonVisible, visibleNavItems, overflowedRailKeys }
}
