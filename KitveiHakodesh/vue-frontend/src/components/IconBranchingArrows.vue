<!--
  One stem splitting into three arrowheads — one up, two down.

  Drawn as a single outline rather than three separate arrows so the junction reads
  as one shape branching, which is the idea it stands for in the toolbar: one work
  with several parallel versions of its text.

  Stroked (not filled) with round joins, matching the Fluent regular icons it sits
  beside; `currentColor` so the toolbar's active/inactive colouring drives it — see
  the global `svg { color: ... }` note in theme.css, which is why the stroke must be
  currentColor and not a fixed value.
-->
<template>
  <svg
    xmlns="http://www.w3.org/2000/svg"
    viewBox="0 0 24 24"
    width="1em"
    height="1em"
    fill="none"
    stroke="currentColor"
    stroke-width="1.6"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <!-- Up arrow: stem from the junction, head at the top. -->
    <path d="M12 3.4v7.2" />
    <path d="M8.9 6.5 12 3.4l3.1 3.1" />

    <!-- Left branch: junction down to the lower-left, head at its end. -->
    <path d="M12 10.6 5.2 17.4" />
    <path d="M5.2 13.1v4.3h4.3" />

    <!-- Right branch: junction down to the lower-right, head at its end. -->
    <path d="m12 10.6 6.8 6.8" />
    <path d="M18.8 13.1v4.3h-4.3" />
  </svg>
</template>
