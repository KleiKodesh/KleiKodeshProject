<!-- Path data copied from @iconify-prerendered/vue-fluent's IconBook20Filled. -->

<!--
  The book icon mirrored for RTL.

  The flip is an SVG `transform` in the markup, not a CSS `transform: scaleX(-1)`.
  A CSS transform belongs to the rendered element, so it is lost the moment the SVG
  is serialized and drawn to a canvas — which is exactly what the native tab strip
  does to turn these into favicons, leaving the book facing the wrong way there
  while looking correct in the page.

  Two constraints shape the markup, both learned the hard way:

  - The transform sits on an inner <g>, never on the root <svg>. On the root it
    moves the element within its own viewport, so a serialized copy rasterizes to a
    clipped sliver — and it looks perfectly fine inline, which is what makes that
    variant such a trap.

  - There is exactly ONE <svg> here. Nesting the base icon's own <svg> inside a
    wrapper breaks color: theme.css has a global `svg { color: var(--text-secondary) }`,
    which applies directly to the inner element and beats the color inherited from
    the outer one, so `currentColor` resolves to grey no matter what the consumer
    sets. Hence the path is inlined rather than the component reused.
-->
<template>
  <svg aria-hidden="true" role="img" width="1em" height="1em" viewBox="0 0 20 20">
    <g transform="translate(20 0) scale(-1 1)">
      <path
        fill="currentColor"
        d="M6 2a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h9.5a.5.5 0 0 0 0-1H6a1 1 0 0 1-1-1h10a1 1 0 0 0 1-1V4a2 2 0 0 0-2-2zm.75 2.5h6.5a.75.75 0 0 1 0 1.5h-6.5a.75.75 0 0 1 0-1.5"
      />
    </g>
  </svg>
</template>
