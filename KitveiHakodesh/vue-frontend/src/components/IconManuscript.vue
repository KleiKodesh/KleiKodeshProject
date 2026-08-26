<!--
  A manuscript scroll — rolled at the top and bottom, with ruled lines of text between.

  Stands for the book's alternate versions in the toolbar: the reader is choosing which
  manuscript's text to read, so the scroll says "a witness to this text" more directly
  than an arrow or a book does.

  Stroked (not filled) with round caps and joins, at the 1.6 weight the Fluent regular
  icons beside it use, so it sits evenly in the toolbar row. `currentColor` throughout —
  theme.css pins `svg { color: ... }`, so a hard-coded stroke would ignore the toolbar's
  active/inactive colouring.
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
    <!-- Top roll: the curled edge the scroll is wound around. -->
    <path d="M6 3.5h10.5a2 2 0 0 1 0 4H6a2 2 0 0 1 0-4Z" />

    <!-- The two side edges of the open sheet, from the top roll to the bottom roll. -->
    <path d="M6 7.5v9" />
    <path d="M18 7.5v9" />

    <!-- Bottom roll. -->
    <path d="M7.5 16.5H18a2 2 0 0 1 0 4H7.5a2 2 0 0 1 0-4Z" />

    <!-- Ruled lines of text, inset from both edges and shortened on the last line. -->
    <path d="M9 10.6h6" />
    <path d="M9 13.4h4" />
  </svg>
</template>
