/**
 * CUSTOM: search input for the outline (תוכן עניינים) side panel.
 *
 * Mirrors the BookView TOC side-panel search so both panels behave identically:
 * typing filters the table of contents down to a FLAT, ranked list of matches
 * (best first), and clearing the query restores the normal nested tree.
 *
 * This is a self-contained port of the Vue app's `SegmentSearchTree`
 * (vue-frontend/src/utils/segmentSearchTree.ts) — deliberately duplicated rather
 * than imported, so `public/pdfjs/` stays decoupled from the Vue build output.
 * The scoring algorithm below must stay behaviourally identical to that file;
 * when one changes, change both. See CUSTOMIZATIONS.md.
 *
 * ── Algorithm (identical to segmentSearchTree.ts) ─────────────────────────────
 *
 * A "segment" is one node's text in the ancestor chain. A node at depth 3 has
 * three segments: grandparent text, parent text, own text. Query words are
 * matched across those segments rather than against a flat concatenated string,
 * so hierarchy boundaries are respected.
 *
 * Pass 1 — score every node. Walk query words left to right; each word must
 *   match at or after the segment the previous word matched in (ordered
 *   subsequence — order matters, adjacency does not). Within a segment, the
 *   first token that starts with the word wins (prefix match). Score = sum of
 *   intra-segment token distances for consecutive word pairs in the same
 *   segment, plus a x10 penalty per segment boundary crossed. Lower is better;
 *   a word with no match anywhere scores Infinity and is excluded.
 *   Two attempts: first requiring the LAST word to match a token exactly (so
 *   "פרק ל" does not surface "פרק לא" when a real "פרק ל" entry exists); if
 *   that yields nothing, retry with prefix matching on all words. A token with
 *   a trailing Talmud-page suffix ("ד." / "ד:") counts as exact for the bare
 *   word ("ד").
 *
 * Pass 2 — bond detection. In the best result, consecutive word pairs that
 *   landed in the SAME segment are "bonded"; any result that splits a bonded
 *   pair across segments is dropped. Query "פרק ד" bonds, so "פרק א / פסוק ד"
 *   is rejected.
 *
 * Pass 3 — ancestry dedup. If a node matched, all its descendants are
 *   suppressed — the ancestor result already covers them.
 *
 * Results are capped at 100, matching the Vue TreeView's filtered-list limit.
 */

(function () {
  'use strict';

  // ─── Tokenizer (port of tokenizeSegmentText) ────────────────────────────────

  var _wordCharMemo = new Map();
  var _WORD_CHAR_RE = /[\p{L}\p{N}]/u;

  /**
   * Tokenize one node's text into lowercase tokens. Keeps "." and ":" attached
   * to a preceding letter/digit so Talmud page references like "דף י." and
   * "דף י:" survive as single tokens.
   */
  function tokenizeSegmentText(text) {
    var s = text.toLowerCase();
    var tokens = [];
    var token = '';
    var prevIsWord = false;
    var i = 0;
    while (i < s.length) {
      var code = s.charCodeAt(i);

      // Surrogate pair — classify the full code point. A "." after the pair saw
      // a lone low surrogate as its previous char (never a word char), so
      // prevIsWord stays false.
      if (
        code >= 0xd800 &&
        code <= 0xdbff &&
        i + 1 < s.length &&
        s.charCodeAt(i + 1) >= 0xdc00 &&
        s.charCodeAt(i + 1) <= 0xdfff
      ) {
        var pair = s.slice(i, i + 2);
        if (_WORD_CHAR_RE.test(pair)) {
          token += pair;
        } else if (token) {
          tokens.push(token);
          token = '';
        }
        prevIsWord = false;
        i += 2;
        continue;
      }

      var isWord;
      if (
        (code >= 0x05d0 && code <= 0x05ea) || // Hebrew letters (incl. finals)
        (code >= 48 && code <= 57) || // 0-9
        (code >= 97 && code <= 122) || // a-z
        (code >= 65 && code <= 90) // A-Z (defensive — input is lowercased)
      ) {
        isWord = true;
      } else if (code < 0x80) {
        isWord = false;
      } else {
        var memo = _wordCharMemo.get(code);
        if (memo === undefined) {
          memo = _WORD_CHAR_RE.test(s[i]);
          _wordCharMemo.set(code, memo);
        }
        isWord = memo;
      }

      if (isWord || ((code === 46 || code === 58) && prevIsWord)) {
        token += s[i];
      } else if (token) {
        tokens.push(token);
        token = '';
      }
      prevIsWord = isWord;
      i++;
    }
    if (token) {
      tokens.push(token);
    }
    return tokens;
  }

  // ─── Search tree (port of SegmentSearchTree) ────────────────────────────────

  var SEGMENT_CROSSING_PENALTY = 10;

  /**
   * @param {Array<{id:number, parentId:number|null, text:string}>} nodes
   *        The FULL node list including ancestors, so segment chains resolve.
   */
  function SegmentSearchTree(nodes) {
    /** segments[nodeId] = array of token arrays, root→leaf. */
    this.segments = new Map();
    /** parentId per node — for ancestry dedup in Pass 3. */
    this.parentIds = new Map();
    /** "root · parent · node" per node, for rendering the path subtitle. */
    this.displayPaths = new Map();
    this._build(nodes);
  }

  SegmentSearchTree.prototype._build = function (nodes) {
    var byId = new Map();
    for (var i = 0; i < nodes.length; i++) {
      byId.set(nodes[i].id, nodes[i]);
    }
    var segCache = new Map();
    var displayCache = new Map();

    function getSegments(id) {
      var cached = segCache.get(id);
      if (cached) {
        return cached;
      }
      var node = byId.get(id);
      if (!node) {
        return [];
      }
      var parentSegs = node.parentId != null ? getSegments(node.parentId) : [];
      var result = parentSegs.concat([tokenizeSegmentText(node.text)]);
      segCache.set(id, result);
      return result;
    }

    function getDisplay(id) {
      var cached = displayCache.get(id);
      if (cached !== undefined) {
        return cached;
      }
      var node = byId.get(id);
      if (!node) {
        return '';
      }
      var parent = node.parentId != null ? getDisplay(node.parentId) : '';
      var result = parent ? parent + ' · ' + node.text : node.text;
      displayCache.set(id, result);
      return result;
    }

    for (var j = 0; j < nodes.length; j++) {
      var node = nodes[j];
      this.segments.set(node.id, getSegments(node.id));
      this.displayPaths.set(node.id, getDisplay(node.id));
      this.parentIds.set(node.id, node.parentId);
    }
  };

  /**
   * Score one node against the query words. Returns { score, segIndices };
   * score Infinity means no match.
   */
  SegmentSearchTree.prototype._score = function (nodeId, words, lastWordExact) {
    var segs = this.segments.get(nodeId);
    if (!segs) {
      return { score: Infinity, segIndices: [] };
    }

    var segIndices = []; // segment index where each query word matched
    var tokenIndices = []; // token index within that segment
    var segFrom = 0; // ordered subsequence: next word matches at segFrom or later

    for (var wi = 0; wi < words.length; wi++) {
      var w = words[wi];
      var requireExact = lastWordExact && wi === words.length - 1;
      var found = false;

      for (var si = segFrom; si < segs.length; si++) {
        var seg = segs[si];
        for (var ti = 0; ti < seg.length; ti++) {
          var tok = seg[ti];
          // A token matches exactly when it equals the query word, or when it
          // is the word followed by a single Talmud-page suffix ("." or ":").
          var isTalmudSuffix =
            tok.length === w.length + 1 &&
            (tok.endsWith('.') || tok.endsWith(':')) &&
            tok.startsWith(w);
          var isExact = tok === w || isTalmudSuffix;
          var matches = requireExact ? isExact : tok.startsWith(w);
          if (matches) {
            segIndices.push(si);
            tokenIndices.push(ti);
            segFrom = si; // next word may match the same or a later segment
            found = true;
            break;
          }
        }
        if (found) {
          break;
        }
      }

      if (!found) {
        return { score: Infinity, segIndices: [] };
      }
    }

    var score = 0;
    for (var i = 1; i < words.length; i++) {
      if (segIndices[i] === segIndices[i - 1]) {
        score += tokenIndices[i] - tokenIndices[i - 1];
      } else {
        score += (segIndices[i] - segIndices[i - 1]) * SEGMENT_CROSSING_PENALTY;
      }
    }

    return { score: score, segIndices: segIndices };
  };

  /**
   * @param {Array} nodes candidate nodes to match against
   * @param {string} query raw query string
   * @param {number} limit max results
   * @returns {Array} matched nodes, best first
   */
  SegmentSearchTree.prototype.search = function (nodes, query, limit) {
    if (limit === undefined) {
      limit = Infinity;
    }
    var words = query
      .trim()
      .toLowerCase()
      .split(/\s+/)
      .filter(Boolean);
    if (!words.length) {
      return [];
    }

    var self = this;
    function scoreAll(lastWordExact) {
      var out = [];
      for (var i = 0; i < nodes.length; i++) {
        var r = self._score(nodes[i].id, words, lastWordExact);
        if (r.score !== Infinity) {
          out.push({ node: nodes[i], score: r.score, segIndices: r.segIndices });
        }
      }
      return out;
    }

    // Pass 1 — exact-last-word first, fall back to all-prefix.
    var scored = scoreAll(true);
    if (!scored.length) {
      scored = scoreAll(false);
    }
    if (!scored.length) {
      return [];
    }

    scored.sort(function (a, b) {
      return a.score - b.score;
    });

    // Pass 2 — bond detection.
    var best = scored[0];
    var bonded = [];
    for (var i = 0; i < words.length - 1; i++) {
      bonded.push(best.segIndices[i] === best.segIndices[i + 1]);
    }

    var filtered = scored.filter(function (entry) {
      for (var k = 0; k < bonded.length; k++) {
        if (bonded[k] && entry.segIndices[k] !== entry.segIndices[k + 1]) {
          return false;
        }
      }
      return true;
    });

    // Pass 3 — ancestry dedup.
    var matchedIds = new Set(
      filtered.map(function (entry) {
        return entry.node.id;
      }),
    );
    var deduplicated = filtered.filter(function (entry) {
      var parentId = self.parentIds.get(entry.node.id);
      if (parentId === undefined) {
        parentId = null;
      }
      while (parentId !== null) {
        if (matchedIds.has(parentId)) {
          return false;
        }
        var next = self.parentIds.get(parentId);
        parentId = next === undefined ? null : next;
      }
      return true;
    });

    var count = limit === Infinity ? deduplicated.length : limit;
    return deduplicated.slice(0, count).map(function (entry) {
      return entry.node;
    });
  };

  // ─── Outline panel wiring ───────────────────────────────────────────────────

  var SIDEBAR_VIEW_OUTLINE = 2; // SidebarView.OUTLINE
  var RESULT_LIMIT = 100; // matches the Vue TreeView filtered-list cap

  /**
   * ABBYY FineReader and some other PDF producers store Hebrew outline titles
   * with trailing punctuation ("מח.") encoded in visual/LTR order as LEADING
   * punctuation (".מח"). This is a data problem, not a rendering one — the
   * characters really are in that order in the string, so the `direction: rtl`
   * rule on `.treeItem > a` cannot fix it. Detect the pattern (punctuation
   * followed by a Hebrew letter) and move the punctuation to the end.
   *
   * Ported from the Vue app's `normalizeOutlineTitle`
   * (src/features/pdf-viewer/usePdfViewPageTracking.ts), which applies the same
   * correction to the titlebar breadcrumb. Keep the two in sync.
   */
  // NOTE: `.` (not `[\s\S]`) is deliberate — it matches the Vue original exactly,
  // leaving titles that contain a newline untouched rather than reordering across
  // the line break.
  var LEADING_PUNCT_RE = /^([.,:;!?()[\]{}"']+)(\p{Script=Hebrew}.*)$/u;

  function normalizeOutlineTitle(raw) {
    var trimmed = raw.trim();
    var match = trimmed.match(LEADING_PUNCT_RE);
    return match && match[1] !== undefined && match[2] !== undefined
      ? match[2] + match[1]
      : trimmed;
  }

  /**
   * Read #outlinesView's nested `div.treeItem > a` DOM into a flat node list.
   * Nesting is `div.treeItem > div.treeItems > div.treeItem`, so an item's
   * parent is the nearest ancestor `.treeItem`. Each node keeps a reference to
   * its source anchor so the flat list can reuse PDF.js's own click handler.
   */
  function indexOutline(container) {
    var items = container.querySelectorAll('.treeItem');
    var nodes = [];
    var idByItem = new Map();

    for (var i = 0; i < items.length; i++) {
      idByItem.set(items[i], i + 1); // ids are 1-based; 0 is falsy
    }

    for (var j = 0; j < items.length; j++) {
      var item = items[j];
      var anchor = item.querySelector(':scope > a');
      if (!anchor) {
        continue;
      }
      var parent = item.parentElement;
      while (parent && parent !== container && !parent.classList.contains('treeItem')) {
        parent = parent.parentElement;
      }
      var parentId =
        parent && parent !== container && idByItem.has(parent) ? idByItem.get(parent) : null;
      nodes.push({
        id: idByItem.get(item),
        parentId: parentId,
        // Corrected form, so search matches what the user actually sees: a query
        // for "מח." must find a title stored as ".מח".
        text: normalizeOutlineTitle(anchor.textContent || ''),
        anchor: anchor,
        item: item,
      });
    }
    return nodes;
  }

  /**
   * Resolve one outline destination to a 1-based page number.
   *
   * Mirrors `resolveDestToPageNumber` in the Vue app's usePdfViewPageTracking.
   * Uses `getPageIndex()` rather than PDF.js's own `cachedPageNumber()` — the
   * latter only answers once every page is loaded, which never happens here
   * because this app sets `disableAutoFetch: true`. That is exactly why PDF.js's
   * built-in "current outline item" button is permanently disabled in this
   * viewer (`_dispatchEvent` resolves its capability to false under
   * disableAutoFetch), and why this feature cannot just reuse it.
   */
  function resolveDestPage(pdfDocument, dest) {
    if (dest === null || dest === undefined) {
      return Promise.resolve(null);
    }
    var explicitPromise =
      typeof dest === 'string' ? pdfDocument.getDestination(dest) : Promise.resolve(dest);

    return explicitPromise
      .then(function (explicitDest) {
        if (!explicitDest || !explicitDest.length) {
          return null;
        }
        var ref = explicitDest[0];
        if (ref === null || ref === undefined) {
          return null;
        }
        if (typeof ref === 'number') {
          return ref + 1;
        }
        return pdfDocument.getPageIndex(ref).then(
          function (zeroBased) {
            return zeroBased + 1;
          },
          function () {
            return null;
          },
        );
      })
      .catch(function () {
        return null;
      });
  }

  /**
   * Walk the outline tree in the same order `indexOutline()` walks the DOM, so
   * result[i] is the page number for the i-th `.treeItem`.
   *
   * Both walks are breadth-first over the same structure — `PDFOutlineViewer.render`
   * builds the DOM with a BFS queue, and `querySelectorAll('.treeItem')` returns
   * document order. Those differ for nested trees, so instead of assuming, this
   * matches each entry to its DOM row by destination hash (see buildPageIndex).
   */
  function flattenOutline(outline) {
    var flat = [];
    var queue = [{ items: outline }];
    while (queue.length > 0) {
      var level = queue.shift();
      for (var i = 0; i < level.items.length; i++) {
        var item = level.items[i];
        flat.push(item);
        if (item.items && item.items.length > 0) {
          queue.push({ items: item.items });
        }
      }
    }
    return flat;
  }

  function initOutlineSearch() {
    var content = document.getElementById('viewsManagerContent');
    var outlinesView = document.getElementById('outlinesView');
    if (!content || !outlinesView) {
      return;
    }

    // Flat results container — a sibling of #outlinesView inside the scroll
    // area, so it inherits the same scrolling and theming. It borrows the
    // `treeView` class (without `withNesting`) so rows are styled exactly like
    // real outline rows, just without indentation.
    var resultsView = document.createElement('div');
    resultsView.id = 'outlineSearchResults';
    resultsView.className = 'treeView hidden';
    content.append(resultsView);

    var searchBar = document.createElement('div');
    searchBar.id = 'outlineSearchBar';
    searchBar.className = 'hidden';
    var searchInner = document.createElement('div');
    searchInner.id = 'outlineSearchInner';
    var input = document.createElement('input');
    input.id = 'outlineSearchInput';
    input.type = 'search';
    input.placeholder = 'חיפוש...'; // חיפוש...
    input.title = 'חיפוש בתוכן הענינים'; // חיפוש בתוכן העניינים
    input.setAttribute(
      'aria-label',
      'חיפוש בתוכן הענינים',
    );
    searchInner.append(input);
    searchBar.append(searchInner);
    // Sits below the scrollable content, like BookView's .toc-search.
    content.after(searchBar);

    var tree = null;
    var nodes = null;
    var outlineCount = 0;
    var currentView = -1;

    // ── Current-entry tracking ────────────────────────────────────────────────
    // Highlights the outline row for the page the user is on, and keeps it
    // scrolled into view — the same relationship BookView's line view has with
    // its TOC. Built once per document, then consulted on every page change.
    //
    // `pageIndex` is [{ page, item }] sorted by page ascending; the active entry
    // for page N is the LAST one with page <= N, matching BookView's
    // `getActiveTocEntry` and the Vue breadcrumb's `findActiveOutlineEntry`.
    var pageIndex = null;
    var pageIndexToken = 0; // guards against a stale async build landing late
    var activeItem = null;

    function buildPageIndex() {
      var app = window.PDFViewerApplication;
      var pdfDocument = app && app.pdfDocument;
      if (!pdfDocument || !pdfDocument.getOutline) {
        return;
      }
      var token = ++pageIndexToken;

      pdfDocument
        .getOutline()
        .then(function (outline) {
          if (token !== pageIndexToken || !outline || !outline.length) {
            return null;
          }
          var flat = flattenOutline(outline);
          return Promise.all(
            flat.map(function (item) {
              return resolveDestPage(pdfDocument, item.dest);
            }),
          ).then(function (pages) {
            if (token !== pageIndexToken) {
              return null;
            }
            // Map each entry to its rendered row via the destination hash PDF.js
            // put in the anchor's href — robust against DOM/tree walk order
            // differing, and against entries whose dest failed to resolve.
            var linkService = app.pdfLinkService;
            var byHash = new Map();
            var anchors = outlinesView.querySelectorAll('.treeItem > a');
            for (var a = 0; a < anchors.length; a++) {
              var href = anchors[a].getAttribute('href');
              if (href && !byHash.has(href)) {
                byHash.set(href, anchors[a].parentNode);
              }
            }
            var entries = [];
            for (var i = 0; i < flat.length; i++) {
              if (pages[i] == null || !linkService) {
                continue;
              }
              var hash;
              try {
                hash = linkService.getDestinationHash(flat[i].dest);
              } catch (e) {
                continue;
              }
              var row = byHash.get(hash);
              if (row) {
                entries.push({ page: pages[i], item: row });
              }
            }
            entries.sort(function (x, y) {
              return x.page - y.page;
            });
            return entries;
          });
        })
        .then(function (entries) {
          if (token !== pageIndexToken || !entries) {
            return;
          }
          pageIndex = entries;
          syncCurrentEntry(app.page);
        })
        .catch(function () {
          // Tracking is a nicety — a failure here must never break the panel.
        });
    }

    /** Highlight the outline row covering `pageNumber`, and reveal it. */
    function syncCurrentEntry(pageNumber) {
      if (!pageIndex || !pageIndex.length || typeof pageNumber !== 'number') {
        return;
      }
      var found = null;
      for (var i = pageIndex.length - 1; i >= 0; i--) {
        if (pageIndex[i].page <= pageNumber) {
          found = pageIndex[i].item;
          break;
        }
      }
      if (!found || found === activeItem) {
        return;
      }
      activeItem = found;
      revealCurrentEntry();
    }

    /**
     * Mark the current row as PDF.js's SELECTED item, expand every ancestor so
     * it is visible, and scroll it into view — but only while the tree is
     * actually on screen. Scrolling a hidden panel is wasted work, and doing it
     * while search results are showing would fight the user.
     *
     * This deliberately reuses `_updateCurrentTreeItem`, PDF.js's own selected-
     * item bookkeeping, rather than keeping a parallel highlight class. It holds
     * `_currentTreeItem` internally and clears the previous row when a new one
     * is set, so page tracking and a manual click can never leave two rows
     * marked. The ancestor walk mirrors `_scrollToCurrentTreeItem`, but scrolls
     * with `block: 'nearest'` (PDF.js uses 'center', which yanks the list on
     * every page turn).
     */
    function revealCurrentEntry() {
      if (!activeItem || inResults() || currentView !== SIDEBAR_VIEW_OUTLINE) {
        return;
      }
      var app = window.PDFViewerApplication;
      var outlineViewer = app && app.pdfOutlineViewer;

      // Expand ancestors, recursively, so the row is actually visible.
      var node = activeItem.parentNode;
      while (node && node !== outlinesView) {
        if (node.classList && node.classList.contains('treeItem')) {
          var toggler = node.firstElementChild;
          if (toggler && toggler.classList.contains('treeItemToggler')) {
            toggler.classList.remove('treeItemsHidden');
          }
        }
        node = node.parentNode;
      }

      if (outlineViewer && typeof outlineViewer._updateCurrentTreeItem === 'function') {
        outlineViewer._updateCurrentTreeItem(activeItem);
      } else {
        // Defensive fallback if the private method is renamed upstream.
        var previous = outlinesView.querySelector('.treeItem.selected');
        if (previous) {
          previous.classList.remove('selected');
        }
        activeItem.classList.add('selected');
      }

      activeItem.scrollIntoView({ block: 'nearest' });
    }

    function invalidateIndex() {
      tree = null;
      nodes = null;
    }

    /**
     * Rewrite mangled titles in PDF.js's own outline DOM, in place.
     *
     * The Vue app already corrects these for the titlebar breadcrumb, but the
     * sidebar tree renders `item.title` verbatim, so entries stored as ".מח"
     * displayed with the punctuation at the start. Fixing the DOM (rather than
     * patching PDFOutlineViewer.render) keeps this additive and upgrade-safe,
     * and means the visible tree and the flat search results agree.
     *
     * Only the anchor's own text node is touched — PDF.js appends nothing else
     * to these anchors, and `.treeItems` children live in sibling elements.
     */
    function normalizeOutlineDom() {
      var anchors = outlinesView.querySelectorAll('.treeItem > a');
      for (var i = 0; i < anchors.length; i++) {
        var current = anchors[i].textContent || '';
        var fixed = normalizeOutlineTitle(current);
        if (fixed !== current) {
          anchors[i].textContent = fixed;
        }
      }
    }

    function ensureIndex() {
      if (tree) {
        return;
      }
      nodes = indexOutline(outlinesView);
      tree = new SegmentSearchTree(nodes);
    }

    /**
     * Return to the nested tree.
     *
     * Focus is dropped rather than carried over: the results list and the tree
     * have unrelated orderings, so keeping an index would land the ring on an
     * arbitrary row. The tree keeps its own `.selected` marker for the entry the
     * user actually opened, which is the meaningful position indicator.
     */
    function showTree() {
      resultsView.replaceChildren();
      resultsView.classList.add('hidden');
      outlinesView.classList.remove('outlineSearchActive');
      focusedIndex = -1;
      paintFocus(); // clears any ring left in either container
      // Returning from results to the tree: bring the current entry back into
      // view, since revealCurrentEntry() was suppressed while results showed.
      revealCurrentEntry();
    }

    // ── Keyboard navigation ───────────────────────────────────────────────────
    // Ported from the Vue app's `useListKeys` (src/composables/useListKeyNav.ts),
    // which drives the BookView TOC list: Up/Down move, Home/End jump to the
    // ends, Enter/Space activate. As in BookView, the focus ring is drawn on the
    // row rather than by moving real DOM focus, so the caret stays in the input
    // and the user can keep typing to refine the query while arrowing results.

    var focusedIndex = -1;

    /** True when the flat search results are showing (vs. the nested tree). */
    function inResults() {
      return !resultsView.classList.contains('hidden');
    }

    /**
     * The rows currently navigable, in visual order.
     *
     * Flat results: every row. Tree: only rows that are actually VISIBLE —
     * PDF.js collapses a subtree with `.treeItemsHidden` on the toggler, which
     * hides the sibling `.treeItems` via CSS, so those rows must be skipped or
     * arrowing would step through invisible entries. `offsetParent === null` is
     * the cheap, layout-accurate test for that (the container itself is
     * displayed whenever this runs).
     */
    function navItems() {
      if (inResults()) {
        return resultsView.querySelectorAll('a[data-nav-item]');
      }
      var all = outlinesView.querySelectorAll('.treeItem > a');
      var visible = [];
      for (var i = 0; i < all.length; i++) {
        if (all[i].offsetParent !== null) {
          visible.push(all[i]);
        }
      }
      return visible;
    }

    function paintFocus() {
      // Clear across BOTH containers: switching between tree and results, or
      // collapsing the row that held focus, must not strand a stale ring.
      var stale = document.querySelectorAll('#outlinesView .is-focused, #outlineSearchResults .is-focused');
      for (var s = 0; s < stale.length; s++) {
        stale[s].classList.remove('is-focused');
      }
      var items = navItems();
      if (focusedIndex >= 0 && focusedIndex < items.length) {
        items[focusedIndex].classList.add('is-focused');
      }
    }

    function moveTo(index) {
      var items = navItems();
      if (!items.length) {
        return;
      }
      focusedIndex = Math.max(0, Math.min(items.length - 1, index));
      paintFocus();
      // `block: 'nearest'` scrolls only when the row is actually out of view, so
      // arrowing through visible rows does not jolt the list.
      items[focusedIndex].scrollIntoView({ block: 'nearest' });
    }

    /**
     * Open the focused row, keeping the caret in the query box.
     *
     * The rows are real `<a href>` elements, so activating one — synthetically
     * or by mouse — makes the browser focus that anchor, which yanks focus out
     * of the input and ends keyboard navigation after a single Enter. Restore
     * it afterwards: once synchronously, and once more after a frame because
     * `goToDestination()` scrolls the page and can move focus asynchronously.
     */
    function activateFocused() {
      var items = navItems();
      var el = items[focusedIndex];
      if (!el) {
        return;
      }
      el.click(); // same delegation path as a mouse click
      refocusInput();
    }

    /**
     * Seed the ring from the CURRENT entry (the row for the page being viewed)
     * the first time the user arrows in the tree, instead of jumping to row 0.
     *
     * Arrowing is a continuation of "where am I", not a fresh traversal from the
     * top — a 500-page book would otherwise need hundreds of presses to get back
     * to the reading position. Returns true when it consumed the keypress, so
     * that first press only reveals the current row and the next one moves off
     * it.
     *
     * Results mode is excluded: renderResults() already seeds index 0, which is
     * the best match and the right starting point there.
     */
    function seedFocusFromCurrent() {
      if (inResults() || focusedIndex >= 0 || !activeItem) {
        return false;
      }
      // The current row may be inside a collapsed branch; expanding it (and
      // scrolling it into view) is exactly what revealCurrentEntry() does, and
      // it must run BEFORE navItems() so the row is actually navigable.
      revealCurrentEntry();
      var currentAnchor = activeItem.querySelector(':scope > a');
      var items = navItems();
      for (var i = 0; i < items.length; i++) {
        if (items[i] === currentAnchor) {
          focusedIndex = i;
          paintFocus();
          items[i].scrollIntoView({ block: 'nearest' });
          return true;
        }
      }
      return false;
    }

    /**
     * Keep the caret in the outline panel after opening an entry.
     *
     * Two separate things steal focus, at two different times:
     *
     *  1. The row itself. These are real `<a href>` elements, so activating one
     *     focuses it synchronously.
     *  2. `PDFLinkService.goToDestination()`, which registers a one-shot
     *     `textlayerrendered` listener and calls
     *     `evt.source.textLayer.div.focus()` when the destination page renders.
     *     That is ASYNCHRONOUS and unbounded — on a cold page it can land
     *     hundreds of ms later, long after any rAF we might wait for.
     *
     * So a single deferred re-focus is not enough. Instead, watch for focus
     * leaving the panel and pull it back for a short window after activation,
     * then stop. `focusin` (unlike `focus`) bubbles, so one document-level
     * listener catches both cases.
     */
    var reclaimUntil = 0;

    function refocusInput() {
      input.focus({ preventScroll: true });
      // Cover the async textlayerrendered focus. 1s is comfortably longer than a
      // page render but short enough that it can never fight a later, genuine
      // focus change by the user.
      reclaimUntil = performance.now() + 1000;
    }

    document.addEventListener(
      'focusin',
      function (e) {
        if (performance.now() > reclaimUntil) {
          return;
        }
        var el = e.target;
        // Only reclaim from the things activation is known to focus — never from
        // somewhere the user deliberately clicked (another toolbar field, say).
        if (el === input || !stealsFocusOnActivate(el)) {
          return;
        }
        input.focus({ preventScroll: true });
      },
      true,
    );

    function stealsFocusOnActivate(el) {
      if (!el || !el.closest) {
        return false;
      }
      // The outline row itself, or the destination page's text layer.
      return (
        (el.tagName === 'A' && !!el.closest('#outlinesView, #outlineSearchResults')) ||
        !!el.closest('.textLayer') ||
        el.classList.contains('textLayer')
      );
    }

    /**
     * The collapse toggler for the focused tree row, or null if it has no
     * children. PDF.js puts `.treeItemToggler` as the first child of the
     * `.treeItem`, before the anchor.
     */
    function focusedToggler() {
      if (inResults()) {
        return null;
      }
      var items = navItems();
      var el = items[focusedIndex];
      if (!el || !el.parentNode) {
        return null;
      }
      return el.parentNode.querySelector(':scope > .treeItemToggler');
    }

    /**
     * Expand or collapse the focused tree row.
     * `.treeItemsHidden` on the toggler is PDF.js's collapsed state.
     * Returns true when it actually changed something.
     */
    function setExpanded(expand) {
      var toggler = focusedToggler();
      if (!toggler) {
        return false;
      }
      var collapsed = toggler.classList.contains('treeItemsHidden');
      if (expand === collapsed) {
        toggler.classList.toggle('treeItemsHidden', !expand);
        // Collapsing can remove rows below the focused one; the focused row
        // itself always survives, so re-derive its index rather than assume.
        var items = navItems();
        for (var i = 0; i < items.length; i++) {
          if (items[i].classList.contains('is-focused')) {
            focusedIndex = i;
            break;
          }
        }
        return true;
      }
      return false;
    }

    /** Move focus to the focused row's parent row, if it has one. */
    function focusParent() {
      var items = navItems();
      var el = items[focusedIndex];
      if (!el) {
        return;
      }
      var parentItem = el.parentNode ? el.parentNode.parentNode : null;
      while (parentItem && parentItem !== outlinesView && !parentItem.classList.contains('treeItem')) {
        parentItem = parentItem.parentNode;
      }
      if (!parentItem || parentItem === outlinesView) {
        return;
      }
      var parentAnchor = parentItem.querySelector(':scope > a');
      for (var i = 0; i < items.length; i++) {
        if (items[i] === parentAnchor) {
          moveTo(i);
          return;
        }
      }
    }

    function renderResults(matches) {
      var fragment = document.createDocumentFragment();
      for (var i = 0; i < matches.length; i++) {
        var node = matches[i];
        var row = document.createElement('div');
        row.className = 'treeItem';
        // Clone the original anchor so PDF.js's own href/style survive, then
        // delegate the click to the real anchor — that keeps `_bindLink`'s
        // handler (goToDestination + selected-item tracking) as the single
        // source of truth for navigation.
        var anchor = document.createElement('a');
        anchor.href = node.anchor.href;
        anchor.textContent = node.text;
        anchor.style.fontWeight = node.anchor.style.fontWeight;
        anchor.style.fontStyle = node.anchor.style.fontStyle;
        anchor.dataset.outlineNodeId = String(node.id);
        anchor.dataset.navItem = ''; // keyboard-nav target (see moveTo/activate)
        row.append(anchor);

        // Ancestor path subtitle, mirroring the Vue panel's result rows.
        var path = tree.displayPaths.get(node.id);
        if (path && path !== node.text) {
          var parentPath = path.slice(0, path.length - node.text.length).replace(/\s*·\s*$/, '');
          if (parentPath) {
            var sub = document.createElement('span');
            sub.className = 'outlineSearchPath';
            sub.textContent = parentPath;
            anchor.append(sub);
          }
        }
        fragment.append(row);
      }
      resultsView.replaceChildren(fragment);
      resultsView.classList.remove('hidden');
      resultsView.scrollTop = 0;
      outlinesView.classList.add('outlineSearchActive');
      // Every keystroke rebuilds the rows, so the old index is meaningless.
      // Pre-focus the top hit: it is the best match, so Enter alone opens it.
      focusedIndex = matches.length ? 0 : -1;
      paintFocus();
    }

    function applyFilter() {
      var query = input.value;
      if (!query.trim()) {
        showTree();
        return;
      }
      ensureIndex();
      if (!nodes || !nodes.length) {
        showTree();
        return;
      }
      renderResults(tree.search(nodes, query, RESULT_LIMIT));
    }

    resultsView.addEventListener('click', function (e) {
      var anchor = e.target.closest ? e.target.closest('a[data-outline-node-id]') : null;
      if (!anchor || !nodes) {
        return;
      }
      e.preventDefault();
      var id = Number(anchor.dataset.outlineNodeId);
      for (var i = 0; i < nodes.length; i++) {
        if (nodes[i].id === id) {
          nodes[i].anchor.click(); // reuse PDF.js's own bound handler
          break;
        }
      }
      // Move the ring to the clicked row so a following Enter/Arrow continues
      // from there rather than from wherever the ring happened to be.
      var clicked = navItems();
      for (var j = 0; j < clicked.length; j++) {
        if (clicked[j] === anchor) {
          focusedIndex = j;
          paintFocus();
          break;
        }
      }
      refocusInput(); // clicking an <a> focuses it — keep the caret in the box
    });

    // Same treatment for clicks in the real tree. PDF.js's own handler runs on
    // the anchor (it is bound via element.onclick, so this listener does not
    // interfere); we only sync the ring and reclaim focus afterwards. Skip
    // toggler clicks — those expand/collapse and should not move the ring.
    outlinesView.addEventListener('click', function (e) {
      if (!input || searchBar.classList.contains('hidden')) {
        return;
      }
      var target = e.target;
      if (!target || !target.closest || target.classList.contains('treeItemToggler')) {
        return;
      }
      var anchor = target.closest('.treeItem > a');
      if (!anchor || !outlinesView.contains(anchor)) {
        return;
      }
      var items = navItems();
      for (var i = 0; i < items.length; i++) {
        if (items[i] === anchor) {
          focusedIndex = i;
          paintFocus();
          break;
        }
      }
      refocusInput();
    });

    input.addEventListener('input', applyFilter);
    input.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && input.value) {
        // Clear the query rather than letting Escape bubble out and close the
        // sidebar — matches the search-input affordance users expect.
        e.stopPropagation();
        input.value = '';
        applyFilter();
        return;
      }

      // Navigation drives whichever list is showing — the flat results while a
      // query is active, otherwise the nested tree.
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        if (seedFocusFromCurrent()) {
          return; // first press only lands the ring on the current entry
        }
        moveTo(focusedIndex < 0 ? 0 : focusedIndex + 1);
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        if (seedFocusFromCurrent()) {
          return;
        }
        moveTo(focusedIndex <= 0 ? 0 : focusedIndex - 1);
      } else if (e.key === 'Home') {
        // Only hijack Home/End when they would otherwise do nothing useful —
        // with a query typed, the caret is already at one end of a short string,
        // so list-jumping is the more useful binding (as in BookView).
        e.preventDefault();
        moveTo(0);
      } else if (e.key === 'End') {
        e.preventDefault();
        moveTo(navItems().length - 1);
      } else if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
        // Tree only — the flat results have no hierarchy to walk.
        // The outline is RTL, so ArrowLeft goes DEEPER (expand) and ArrowRight
        // goes back toward the root (collapse), mirroring the chevron direction
        // in BookView's RTL tree.
        if (inResults() || focusedIndex < 0) {
          return;
        }
        var expand = e.key === 'ArrowLeft';
        e.preventDefault();
        if (!setExpanded(expand)) {
          // Already in that state: collapse-when-collapsed climbs to the parent,
          // which is the conventional tree behaviour.
          if (!expand) {
            focusParent();
          }
        } else {
          paintFocus();
        }
      } else if (e.key === ' ' || e.key === 'Spacebar') {
        // Toggle expand/collapse on the focused tree row, mirroring BookView's
        // tree (TreeNode.vue: Space toggles a parent, selects a leaf).
        //
        // Only bound when the query box is EMPTY — otherwise Space has to stay
        // typable, since queries are multi-word ("פסחים דף ד"). That is also why
        // this cannot simply mirror BookView unconditionally: there the list is
        // not a text field, here it is.
        if (inResults() || input.value !== '' || focusedIndex < 0) {
          return;
        }
        e.preventDefault();
        var spaceToggler = focusedToggler();
        if (spaceToggler) {
          setExpanded(spaceToggler.classList.contains('treeItemsHidden'));
          paintFocus();
        } else {
          activateFocused(); // leaf — Space opens it, as in BookView
        }
      } else if (e.key === 'Enter') {
        if (focusedIndex >= 0) {
          e.preventDefault();
          activateFocused();
        }
      }
    });

    // Autofocus is driven from here rather than from a single event, because
    // visibility depends on TWO inputs — the active view and outlineCount — and
    // either one can be the last to arrive. Notably, the "default to the outline"
    // customization switches the view BEFORE the outline loads, so
    // sidebarviewchanged fires while outlineCount is still 0 (bar correctly
    // hidden, no focus) and it is outlineloaded that reveals the bar. Focusing on
    // the hidden→visible transition covers both orderings, and the wasVisible
    // guard keeps it to the transition only — so unrelated events (a page change
    // re-dispatching sidebarviewchanged, say) can't steal focus back from the
    // outline list while the user is arrowing through it.
    var wasVisible = false;

    function updateVisibility() {
      var show = currentView === SIDEBAR_VIEW_OUTLINE && outlineCount > 0;
      searchBar.classList.toggle('hidden', !show);
      if (!show && input.value) {
        input.value = '';
        showTree();
      }
      if (show && !wasVisible) {
        // The bar is revealed by removing .hidden above; focus() needs the element
        // to be laid out, so defer a frame rather than focusing a display:none input.
        requestAnimationFrame(function () {
          if (!searchBar.classList.contains('hidden')) {
            input.focus({ preventScroll: true });
          }
          // revealCurrentEntry() no-ops while the panel is hidden, so opening it
          // is the point at which the current row must be scrolled into view.
          revealCurrentEntry();
        });
      }
      wasVisible = show;
    }

    function waitForApp() {
      var app = window.PDFViewerApplication;
      if (!app || !app.eventBus) {
        setTimeout(waitForApp, 100);
        return;
      }

      app.eventBus._on('outlineloaded', function (evt) {
        outlineCount = evt.outlineCount || 0;
        input.value = '';
        // _finishRendering() appends the tree BEFORE dispatching this event, so
        // the DOM is present and can be corrected in place here.
        normalizeOutlineDom();
        invalidateIndex();
        showTree();
        updateVisibility();
        // The rendered rows exist now, so destinations can be mapped onto them.
        // This also covers the "when the file loads" case: buildPageIndex()
        // syncs against the current page as soon as it resolves.
        if (outlineCount > 0) {
          buildPageIndex();
        }
      });

      app.eventBus._on('pagechanging', function (evt) {
        syncCurrentEntry(evt.pageNumber);
      });

      app.eventBus._on('sidebarviewchanged', function (evt) {
        currentView = evt.view;
        updateVisibility(); // handles autofocus on the hidden→visible transition
      });

      // The outline tree is rebuilt on every document load; drop the stale index.
      app.eventBus._on('documentloaded', function () {
        outlineCount = 0;
        input.value = '';
        invalidateIndex();
        // Invalidate the page index too — bumping the token makes any in-flight
        // build from the previous document discard its result.
        pageIndexToken++;
        pageIndex = null;
        activeItem = null;
        showTree();
        updateVisibility();
      });
    }
    waitForApp();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initOutlineSearch);
  } else {
    initOutlineSearch();
  }
})();
