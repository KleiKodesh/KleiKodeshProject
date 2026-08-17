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
    /** "root · parent · node" per node — the label of a search result row. */
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
    // Add button, sharing the search row — the panel's single always-visible
    // editing affordance, matching Acrobat/PDF-XChange's "new bookmark" header
    // button: one click adds an entry pointing at the CURRENT page, name
    // pre-selected for typing. Everything else is direct manipulation on the
    // rows (context menu, double-click rename, drag to move) — no edit mode.
    var addButton = document.createElement('button');
    addButton.id = 'outlineAddButton';
    addButton.type = 'button';
    addButton.title = 'הוספת סימנייה לעמוד הנוכחי (Ctrl+B)';
    addButton.setAttribute('aria-label', 'הוספת סימנייה לעמוד הנוכחי');
    searchInner.append(input, addButton);
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
            // href → queue of rows in document order (see consumption below).
            var byHash = new Map();
            var anchors = outlinesView.querySelectorAll('.treeItem > a');
            for (var a = 0; a < anchors.length; a++) {
              var href = anchors[a].getAttribute('href');
              if (!href) {
                continue;
              }
              var queueForHref = byHash.get(href);
              if (!queueForHref) {
                queueForHref = [];
                byHash.set(href, queueForHref);
              }
              queueForHref.push(anchors[a].parentNode);
            }
            // Stamp each row with its resolved page (tracking + edit targets)
            // AND its index into the BFS-flattened original outline (tocSrc).
            // The save path uses tocSrc to copy everything the editor does not
            // model — precise/named destinations, URL actions, bold/italic,
            // color, closed state — verbatim from the original item, so an
            // edit to ONE entry no longer degrades every other entry.
            //
            // Rows are matched to items by expected href — the URL for link
            // items, the destination hash otherwise (exactly what PDF.js's
            // _bindLink assigns) — via a QUEUE per href, consumed in order, so
            // duplicate destinations map to distinct rows instead of the first
            // row swallowing every occurrence.
            var stamped = 0;
            for (var i = 0; i < flat.length; i++) {
              var key = null;
              if (flat[i].url) {
                key = flat[i].url;
              } else if (linkService && flat[i].dest) {
                try {
                  key = linkService.getDestinationHash(flat[i].dest);
                } catch (e) {
                  key = null;
                }
              }
              if (key == null) {
                continue;
              }
              var rowQueue = byHash.get(key);
              var row = rowQueue && rowQueue.length ? rowQueue.shift() : null;
              if (!row) {
                continue;
              }
              row.dataset.tocSrc = String(i);
              if (pages[i] != null && !row.dataset.tocPage) {
                row.dataset.tocPage = String(pages[i]);
              }
              stamped++;
            }
            return stamped;
          });
        })
        .then(function (stamped) {
          if (token !== pageIndexToken || stamped === null) {
            return;
          }
          rebuildPageIndexFromDom();
          syncCurrentEntry(app.page);
        })
        .catch(function () {
          // Tracking is a nicety — a failure here must never break the panel.
        });
    }

    /**
     * Rebuild the sorted page index from `data-toc-page` in the DOM.
     *
     * Cheap (one querySelectorAll + sort), and the single place the index is
     * produced — so an edit only has to stamp/remove rows and call this, rather
     * than re-resolving any destinations.
     */
    function rebuildPageIndexFromDom() {
      var items = outlinesView.querySelectorAll('.treeItem');
      var entries = [];
      for (var i = 0; i < items.length; i++) {
        var page = Number(items[i].dataset.tocPage);
        if (page > 0) {
          entries.push({ page: page, item: items[i] });
        }
      }
      entries.sort(function (x, y) {
        return x.page - y.page;
      });
      pageIndex = entries;
      // The previously-current row may have been deleted.
      if (activeItem && !outlinesView.contains(activeItem)) {
        activeItem = null;
      }
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
     * Stamp --toc-depth on every row. The BookView-style CSS keeps rows full
     * panel width and indents INSIDE them (padding = depth*10px + the 24px
     * chevron column), so each row must know its depth; PDF.js's structural
     * container margins are zeroed. Runs on every tree (re)build and edit —
     * one cheap O(N) pass.
     */
    function stampDepths() {
      var items = outlinesView.querySelectorAll('.treeItem');
      for (var i = 0; i < items.length; i++) {
        var depth = 0;
        var node = items[i].parentElement;
        while (node && node !== outlinesView) {
          if (node.classList.contains('treeItems')) {
            depth++;
          }
          node = node.parentElement;
        }
        items[i].style.setProperty('--toc-depth', String(depth));
      }
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
        if (renaming) {
          return; // the contentEditable anchor must keep focus while renaming
        }
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
        // The FULL ancestor path is the row's label — "root · parent · node" on
        // one line, exactly what BookView's TreeView renders while filtering
        // (`filter ? displayPaths.get(node.id) : node.text`). A result is only
        // meaningful in context: "פרק ד" alone is ambiguous across a book, and
        // the search itself matches across the whole ancestor chain, so the row
        // must show the same chain it matched against.
        anchor.textContent = tree.displayPaths.get(node.id) || node.text;
        anchor.style.fontWeight = node.anchor.style.fontWeight;
        anchor.style.fontStyle = node.anchor.style.fontStyle;
        anchor.dataset.outlineNodeId = String(node.id);
        anchor.dataset.navItem = ''; // keyboard-nav target (see moveTo/activate)
        row.append(anchor);
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

    // ── Editing ───────────────────────────────────────────────────────────────
    // Add / rename / delete / re-nest outline entries, including building one
    // from nothing on a PDF that has no TOC.
    //
    // The DOM is the model. PDF.js's rendered tree is already the source of
    // truth for search (indexOutline), tracking (data-toc-page) and keyboard
    // nav, and normalizeOutlineDom already rewrites it in place — so editing
    // the same DOM keeps every one of those features working with no parallel
    // data structure to keep in sync. Rows keep PDF.js's exact shape
    // (`.treeItem > a`, `.treeItems`, `.treeItemToggler`) so the styling and
    // collapse behaviour come for free.
    //
    // Edits live in the DOM until save (SaveDocument writes them into the
    // PDF); `outlineDirty` feeds _hasChanges() so the viewer knows the
    // document is modified. There is NO edit mode: rows navigate on click and
    // are edited by direct manipulation (context menu, double-click rename,
    // drag to move), the convention every PDF editor converged on.
    var outlineDirty = false;
    var renaming = false;
    var renamingAnchor = null; // the contentEditable anchor while renaming
    // True when PDF.js's own _finishRendering bound the container's toggler
    // click listener (it only does so when the outline rendered WITH nesting).
    // When it did not — flat or empty original outline — togglers created by
    // the editor (ensureToggler) would be click-dead without our fallback.
    var pdfjsTogglerHandling = false;

    function markDirty() {
      outlineDirty = true;
    }

    /**
     * XFA documents: the worker's SaveDocument skips the outline block for
     * isPureXfa, so edits would silently discard on save. With no edit mode to
     * disable, every interaction trigger checks this instead.
     */
    function editingDisabled() {
      var app = window.PDFViewerApplication;
      return !!(app && app.pdfDocument && app.pdfDocument.isPureXfa);
    }

    /** The `.treeItem` behind the keyboard ring, or null. */
    function focusedRow() {
      if (inResults()) {
        return null;
      }
      var items = navItems();
      var anchor = items[focusedIndex];
      return anchor ? anchor.parentNode : null;
    }

    /** Put the ring on a specific row and scroll it into view. */
    function focusRow(row) {
      var anchor = row && row.querySelector(':scope > a');
      if (!anchor) {
        return;
      }
      var items = navItems();
      for (var i = 0; i < items.length; i++) {
        if (items[i] === anchor) {
          focusedIndex = i;
          paintFocus();
          items[i].scrollIntoView({ block: 'nearest' });
          return;
        }
      }
    }

    /** Runs after every mutation: mark dirty and re-derive the dependent state. */
    function afterEdit() {
      markDirty();
      stampDepths(); // structure may have changed (indent/outdent/drag/add)
      invalidateIndex(); // search index is rebuilt lazily on the next query
      rebuildPageIndexFromDom();
      paintFocus();
      pushOutlineToTransport();
      notifyHost();
      // Current-entry tracking is deliberately NOT re-synced here — it scrolls,
      // which would yank the list mid-edit. The next pagechanging picks it up.
    }

    /**
     * Serialize the tree DOM to [{title, page, items}] and hand it to the
     * WorkerTransport, where the patched saveDocument() picks it up and the
     * patched SaveDocument worker handler writes it into the PDF's incremental
     * update (see CUSTOMIZATIONS.md). Titles are the DISPLAYED text — i.e.
     * after the mangled-title correction — so saving also persists that fix.
     *
     * Pages come from data-toc-page. A row whose destination never resolved has
     * no stamp; inherit the nearest previous row's page so the entry stays
     * navigable rather than being dropped.
     */
    function serializeOutlineDom() {
      var lastPage = 1;
      function walk(container) {
        var out = [];
        var rows = container.querySelectorAll(':scope > .treeItem');
        for (var i = 0; i < rows.length; i++) {
          var row = rows[i];
          var anchor = row.querySelector(':scope > a');
          if (!anchor) {
            continue;
          }
          var page = Number(row.dataset.tocPage);
          if (!(page > 0)) {
            page = lastPage;
          }
          lastPage = page;
          var kids = row.querySelector(':scope > .treeItems');
          var entry = {
            title: (anchor.textContent || '').trim(),
            page: page,
            items: kids ? walk(kids) : [],
          };
          // Index into the original outline (BFS order) — lets the save path
          // preserve this entry's original destination/action/styling verbatim.
          // Absent on user-created rows.
          var src = Number(row.dataset.tocSrc);
          if (Number.isInteger(src) && src >= 0) {
            entry.src = src;
          }
          out.push(entry);
        }
        return out;
      }
      return walk(outlinesView);
    }

    function pushOutlineToTransport() {
      // Between documentloaded (which resets state) and the new document's
      // outlineloaded, the DOM still shows the PREVIOUS document's tree — a
      // blur-committed rename landing in that window must not serialize the
      // old tree into the new document's transport.
      if (searchBar.dataset.outlineLoaded !== '1') {
        return;
      }
      var app = window.PDFViewerApplication;
      var pdfDocument = app && app.pdfDocument;
      if (pdfDocument && pdfDocument._transport) {
        pdfDocument._transport.editedOutline = serializeOutlineDom();
      }
    }

    // Fallback expand/collapse for togglers PDF.js never wired: its container
    // listener is bound in _finishRendering only when the outline rendered
    // WITH nesting, so on a flat/empty original outline the togglers created
    // by editing (ensureToggler) would be click-dead. When PDF.js's listener
    // IS bound, this stays out of the way entirely (both firing would cancel
    // each other out — stopPropagation does not stop same-element listeners).
    outlinesView.addEventListener('click', function (e) {
      if (pdfjsTogglerHandling) {
        return;
      }
      var target = e.target;
      if (!target || !target.classList || !target.classList.contains('treeItemToggler')) {
        return;
      }
      e.preventDefault();
      e.stopPropagation();
      target.classList.toggle('treeItemsHidden');
    });

    // ── Host (Vue app) integration ────────────────────────────────────────────
    // The viewer lives in an iframe that the host destroys or navigates on tab
    // switches, so unsaved edits must be pushed OUT eagerly — after every edit,
    // not on teardown (there is no reliable teardown moment). The host stores
    // the snapshot per tab and rehydrates via setState() when the tab returns.
    //
    //   window.__khOutlineHostNotify — assigned by the host on iframe load;
    //     called with {dirty, outline} after every edit and after a completed
    //     save ({dirty:false}).
    //   window.__khOutlineEditor — {getState, setState, isDirty} for the host.
    //   window.__khSuppressUnloadPrompt — set by the host just before a
    //     Vue-initiated navigation/teardown. The _hasChanges wrapper returns
    //     false while set, so PDF.js's own beforeunload prompt stays quiet for
    //     navigations whose state the host has already preserved. Without this,
    //     switching between two PDF tabs (same iframe, src change) pops the
    //     browser's native English prompt — and cancelling it desyncs the
    //     iframe from the already-switched Vue tab state.

    function notifyHost() {
      if (typeof window.__khOutlineHostNotify === 'function') {
        try {
          window.__khOutlineHostNotify({
            dirty: outlineDirty,
            outline: serializeOutlineDom(),
          });
        } catch (e) {
          // Host callback failures must never break the editor.
        }
      }
    }

    /**
     * Rebuild the tree DOM from a host snapshot ([{title, page, items}]).
     * Used when a tab returns after its iframe was torn down mid-edit. The
     * rebuilt rows carry data-toc-page and no href (same as freshly added
     * rows), so navigation goes through the goToPage fallback below and the
     * next save writes exactly this tree.
     */
    function rebuildFromState(outline) {
      closeContextMenu();
      clearDrag();
      outlinesView.replaceChildren();
      var total = 0;
      function build(items, container) {
        for (var i = 0; i < items.length; i++) {
          var item = items[i];
          var row = createRow(String(item.title || ''), Number(item.page) || 1);
          // Restore the original-outline index so a save after rehydration
          // still preserves this entry's destination/action/styling.
          if (Number.isInteger(item.src) && item.src >= 0) {
            row.dataset.tocSrc = String(item.src);
          }
          container.append(row);
          total++;
          if (Array.isArray(item.items) && item.items.length > 0) {
            ensureToggler(row);
            build(item.items, ensureItemsContainer(row));
          }
        }
      }
      build(outline, outlinesView);
      stampDepths();
      outlineCount = total;
      var outlineMenuButton = document.getElementById('outlinesViewMenu');
      if (outlineMenuButton) {
        outlineMenuButton.disabled = false;
      }
      outlineDirty = true;
      invalidateIndex();
      rebuildPageIndexFromDom();
      pushOutlineToTransport();
      updateVisibility();
    }

    window.__khOutlineEditor = {
      getState: function () {
        return { dirty: outlineDirty, outline: serializeOutlineDom() };
      },
      setState: function (state) {
        if (state && Array.isArray(state.outline)) {
          rebuildFromState(state.outline);
        }
      },
      isDirty: function () {
        return outlineDirty;
      },
    };

    // PDF.js marks a `.treeItem` as having children with a `.treeItemToggler`
    // first child plus a sibling `.treeItems` container; `.withNesting` on the
    // view is what makes the toggler visible at all.
    function ensureToggler(item) {
      if (!item.querySelector(':scope > .treeItemToggler')) {
        var toggler = document.createElement('div');
        toggler.className = 'treeItemToggler';
        item.prepend(toggler);
      }
      outlinesView.classList.add('withNesting');
    }

    function ensureItemsContainer(item) {
      var container = item.querySelector(':scope > .treeItems');
      if (!container) {
        container = document.createElement('div');
        container.className = 'treeItems';
        item.append(container);
      }
      return container;
    }

    /** Drop the toggler + empty container once an item has no children left. */
    function cleanupItem(item) {
      var container = item.querySelector(':scope > .treeItems');
      if (container && !container.querySelector(':scope > .treeItem')) {
        container.remove();
        var toggler = item.querySelector(':scope > .treeItemToggler');
        if (toggler) {
          toggler.remove();
        }
      }
    }

    /**
     * A new row. It has no PDF destination — nothing to resolve — so its target
     * page is carried in `data-toc-page`, which is also what the page index
     * reads. The anchor deliberately has NO href: PDF.js only binds navigation
     * to anchors it created, and an empty href would push a history entry.
     */
    function createRow(title, page) {
      var row = document.createElement('div');
      row.className = 'treeItem';
      row.dataset.tocPage = String(page);
      row.dataset.tocNew = '1';
      var anchor = document.createElement('a');
      anchor.textContent = title;
      anchor.dataset.navItem = '';
      row.append(anchor);
      return row;
    }

    /**
     * Add an entry pointing at the CURRENT page (the Acrobat convention).
     * `reference` — insert after this row (same level); `asChild` — insert as
     * its last child instead. No reference: after the ring row, else after the
     * current-entry row, else appended at the end. The new row drops straight
     * into rename with the placeholder name selected.
     */
    function addItem(reference, asChild) {
      if (inResults()) {
        // Adding into the hidden tree while results are showing would be
        // invisible — leave search first, then insert relative to something
        // the user can see.
        input.value = '';
        applyFilter();
      }
      var app = window.PDFViewerApplication;
      var page = (app && app.page) || 1;
      var row = createRow('פריט חדש', page);
      var target = reference || focusedRow() || activeItem;
      if (asChild && target) {
        ensureToggler(target);
        ensureItemsContainer(target).append(row);
        var toggler = target.querySelector(':scope > .treeItemToggler');
        if (toggler) {
          toggler.classList.remove('treeItemsHidden'); // reveal the new child
        }
      } else if (target && target.parentNode && outlinesView.contains(target)) {
        target.after(row); // sibling, at the target's level
      } else {
        outlinesView.append(row);
      }
      afterEdit();
      focusRow(row);
      startRename(row); // a new row is always named immediately
    }

    /**
     * Delete the focused row, PROMOTING its children into its place rather than
     * discarding the subtree — losing a whole branch to one keystroke is a much
     * worse mistake than leaving entries at the wrong depth.
     */
    function deleteItem(targetRow) {
      var row = targetRow || focusedRow();
      if (!row || !outlinesView.contains(row)) {
        return;
      }
      // Where the ring should land after the delete: next sibling row, else
      // previous, else the parent — so consecutive deletes need no re-picking.
      var focusNext = row.nextElementSibling;
      while (focusNext && !focusNext.classList.contains('treeItem')) {
        focusNext = focusNext.nextElementSibling;
      }
      if (!focusNext) {
        focusNext = row.previousElementSibling;
        while (focusNext && !focusNext.classList.contains('treeItem')) {
          focusNext = focusNext.previousElementSibling;
        }
      }
      var host = row.parentNode;
      if (!focusNext && host && host.classList && host.classList.contains('treeItems')) {
        focusNext = host.parentNode;
      }
      var kids = row.querySelector(':scope > .treeItems');
      var firstPromoted = null;
      if (kids) {
        var children = Array.prototype.slice.call(kids.querySelectorAll(':scope > .treeItem'));
        firstPromoted = children[0] ?? null;
        for (var i = children.length - 1; i >= 0; i--) {
          row.after(children[i]);
        }
      }
      row.remove();
      if (host && host.classList && host.classList.contains('treeItems') && host.parentNode) {
        cleanupItem(host.parentNode);
      }
      focusedIndex = -1;
      afterEdit();
      var target = firstPromoted ?? focusNext;
      if (target && outlinesView.contains(target)) {
        focusRow(target);
      }
    }

    function startRename(targetRow) {
      var row = targetRow || focusedRow();
      var anchor = row && row.querySelector(':scope > a');
      if (!anchor || renaming) {
        return;
      }
      var original = anchor.textContent || '';
      renaming = true;
      renamingAnchor = anchor;
      anchor.contentEditable = 'true';
      anchor.classList.add('outlineRenaming');
      anchor.focus();
      var range = document.createRange();
      range.selectNodeContents(anchor);
      var selection = window.getSelection();
      selection.removeAllRanges();
      selection.addRange(range);

      function finish(commit) {
        if (!renaming) {
          return;
        }
        renaming = false;
        renamingAnchor = null;
        anchor.contentEditable = 'false';
        anchor.classList.remove('outlineRenaming');
        anchor.removeEventListener('keydown', onKey);
        anchor.removeEventListener('blur', onBlur);
        var text = (anchor.textContent || '').replace(/\s+/g, ' ').trim();
        var committed = commit && text ? text : original;
        anchor.textContent = committed;
        // Only an ACTUAL change dirties the document. A canceled or unchanged
        // rename must not — marking dirty arms the full outline rewrite on
        // save and the unsaved-changes alert, for a no-op.
        if (committed !== original) {
          afterEdit();
        }
        input.focus({ preventScroll: true });
      }
      function onKey(e) {
        if (e.key === 'Enter') {
          e.preventDefault();
          finish(true);
        } else if (e.key === 'Escape') {
          e.preventDefault();
          e.stopPropagation(); // do not also clear the query / close the sidebar
          finish(false);
        }
      }
      function onBlur() {
        finish(true);
      }
      anchor.addEventListener('keydown', onKey);
      anchor.addEventListener('blur', onBlur);
    }

    /** Nest a row under the row above it, at the same level. */
    function indentItem(targetRow) {
      var row = targetRow || focusedRow();
      if (!row) {
        return;
      }
      var previous = row.previousElementSibling;
      while (previous && !previous.classList.contains('treeItem')) {
        previous = previous.previousElementSibling;
      }
      if (!previous) {
        return; // nothing above at this level to nest under
      }
      ensureToggler(previous);
      ensureItemsContainer(previous).append(row);
      var toggler = previous.querySelector(':scope > .treeItemToggler');
      if (toggler) {
        toggler.classList.remove('treeItemsHidden'); // reveal what we just moved
      }
      afterEdit();
      focusRow(row);
    }

    /** Move a row out to its parent's level, just after the parent. */
    function outdentItem(targetRow) {
      var row = targetRow || focusedRow();
      if (!row) {
        return;
      }
      var container = row.parentNode;
      if (!container || !container.classList || !container.classList.contains('treeItems')) {
        return; // already at the top level
      }
      var owner = container.parentNode;
      if (!owner) {
        return;
      }
      owner.after(row);
      cleanupItem(owner);
      afterEdit();
      focusRow(row);
    }

    /** Move a row up/down among its visible siblings (Alt+↑/↓). */
    function moveItem(targetRow, direction) {
      var row = targetRow || focusedRow();
      if (!row) {
        return;
      }
      if (direction < 0) {
        var previous = row.previousElementSibling;
        while (previous && !previous.classList.contains('treeItem')) {
          previous = previous.previousElementSibling;
        }
        if (!previous) {
          return;
        }
        previous.before(row);
      } else {
        var next = row.nextElementSibling;
        while (next && !next.classList.contains('treeItem')) {
          next = next.nextElementSibling;
        }
        if (!next) {
          return;
        }
        next.after(row);
      }
      afterEdit();
      focusRow(row);
    }

    /**
     * Re-point a row at the CURRENT page ("עדכון יעד לעמוד הנוכחי" — the
     * set-destination-to-current-view every PDF editor offers). The row
     * becomes editor-owned: original dest/action no longer apply, so the src
     * stamp and PDF.js's bound navigation are dropped and it navigates via
     * data-toc-page like an added row.
     */
    function retargetItem(targetRow) {
      var row = targetRow || focusedRow();
      var anchor = row && row.querySelector(':scope > a');
      if (!anchor) {
        return;
      }
      var app = window.PDFViewerApplication;
      row.dataset.tocPage = String((app && app.page) || 1);
      row.dataset.tocNew = '1'; // shows the "modified, unsaved" edge marker
      delete row.dataset.tocSrc;
      anchor.removeAttribute('href');
      anchor.onclick = null;
      afterEdit();
      focusRow(row);
    }

    // ── Context menu — the hub for row actions (the PDF Expert/Acrobat way) ──
    var contextMenu = null;
    var contextMenuRow = null;

    function closeContextMenu() {
      if (contextMenu) {
        contextMenu.remove();
        contextMenu = null;
        contextMenuRow = null;
      }
      if (hoverMenuButton && hoverMenuButton.isConnected) {
        hoverMenuButton.remove();
        hoverRow = null;
      }
    }

    var MENU_ITEMS = [
      { key: 'rename', label: 'שינוי שם', hint: 'F2' },
      { key: 'retarget', label: 'עדכון יעד לעמוד הנוכחי', hint: '' },
      { key: 'add', label: 'הוספת פריט אחרי', hint: '' },
      { key: 'addChild', label: 'הוספת תת־פריט', hint: '' },
      { key: 'delete', label: 'מחיקה', hint: 'Del' },
    ];

    function openContextMenu(row, clientX, clientY) {
      closeContextMenu();
      contextMenuRow = row;
      focusRow(row); // menu target and ring agree — actions hit what you see
      var menu = document.createElement('div');
      menu.id = 'outlineContextMenu';
      menu.setAttribute('role', 'menu');
      for (var i = 0; i < MENU_ITEMS.length; i++) {
        var item = MENU_ITEMS[i];
        var button = document.createElement('button');
        button.type = 'button';
        button.className = 'outlineMenuItem';
        button.dataset.action = item.key;
        button.setAttribute('role', 'menuitem');
        var label = document.createElement('span');
        label.textContent = item.label;
        button.append(label);
        if (item.hint) {
          var hint = document.createElement('span');
          hint.className = 'outlineMenuHint';
          hint.textContent = item.hint;
          button.append(hint);
        }
        menu.append(button);
      }
      document.body.append(menu);
      // Clamp to the viewport; in RTL open toward the left of the cursor.
      var rect = menu.getBoundingClientRect();
      var x = Math.max(4, Math.min(clientX, window.innerWidth - rect.width - 4));
      var y = Math.max(4, Math.min(clientY, window.innerHeight - rect.height - 4));
      menu.style.left = x + 'px';
      menu.style.top = y + 'px';
      contextMenu = menu;

      menu.addEventListener('click', function (e) {
        var button = e.target.closest ? e.target.closest('.outlineMenuItem') : null;
        if (!button) {
          return;
        }
        var action = button.dataset.action;
        var target = contextMenuRow;
        closeContextMenu();
        if (!target || !outlinesView.contains(target)) {
          return;
        }
        if (action === 'rename') {
          startRename(target);
        } else if (action === 'retarget') {
          retargetItem(target);
        } else if (action === 'add') {
          addItem(target, false);
        } else if (action === 'addChild') {
          addItem(target, true);
        } else if (action === 'delete') {
          deleteItem(target);
          input.focus({ preventScroll: true });
        }
      });
    }

    outlinesView.addEventListener('contextmenu', function (e) {
      if (dragState && dragState.started) {
        e.preventDefault();
        clearDrag();
        return;
      }
      var target = e.target;
      if (!target || !target.closest) {
        return;
      }
      var anchor = target.closest('.treeItem > a');
      if (!anchor || inResults() || editingDisabled()) {
        return;
      }
      e.preventDefault();
      e.stopPropagation();
      if (renaming) {
        return; // finish the rename first
      }
      openContextMenu(anchor.parentNode, e.clientX, e.clientY);
    });

    // Any interaction elsewhere dismisses the menu.
    document.addEventListener('pointerdown', function (e) {
      if (contextMenu && !(e.target && contextMenu.contains(e.target))) {
        closeContextMenu();
      }
    }, true);
    document.addEventListener('keydown', function (e) {
      if (e.key !== 'Escape') {
        return;
      }
      if (dragState && dragState.started) {
        e.stopPropagation();
        clearDrag(); // nulls dragState — the eventual pointerup drops nothing
        return;
      }
      if (contextMenu) {
        e.stopPropagation();
        closeContextMenu();
        input.focus({ preventScroll: true });
      }
    }, true);
    content.addEventListener('scroll', function () {
      closeContextMenu();
      if (hoverMenuButton.isConnected) {
        hoverMenuButton.remove();
        hoverRow = null;
      }
    }, true);

    // ── Hover ⋯ — one floating button, repositioned onto the hovered row ─────
    // A single element (not one per row) so the tree DOM PDF.js owns stays
    // untouched and there is zero per-row cost at 4,000+ entries.
    var hoverMenuButton = document.createElement('button');
    hoverMenuButton.id = 'outlineRowMenuButton';
    hoverMenuButton.type = 'button';
    // The ⋯ glyph is a masked SVG icon in viewer-custom.css, matching PDF.js's
    // own toolbar buttons — no text content.
    hoverMenuButton.title = 'פעולות';
    hoverMenuButton.setAttribute('aria-label', 'פעולות פריט');
    var hoverRow = null;

    outlinesView.addEventListener('mouseover', function (e) {
      if (renaming || dragState || inResults() || editingDisabled()) {
        return;
      }
      var target = e.target;
      var anchor = target && target.closest ? target.closest('.treeItem > a') : null;
      if (!anchor) {
        return;
      }
      hoverRow = anchor.parentNode;
      var rect = anchor.getBoundingClientRect();
      hoverMenuButton.style.top = rect.top + 'px';
      // Span the row's full height. It has to be set here rather than in CSS:
      // the button is position:fixed on document.body (so the PDF.js-owned tree
      // DOM stays untouched), so it has no layout relationship to the row and
      // cannot inherit its height. Rows are min-height:28px but grow when a long
      // title wraps, so read it off the rect every time rather than hardcoding.
      hoverMenuButton.style.height = rect.height + 'px';
      // RTL: rows start at the right; park the button at the row's LEFT edge
      // (the visual end) so it never covers the title's first words.
      hoverMenuButton.style.left = Math.max(0, rect.left) + 'px';
      if (!hoverMenuButton.isConnected) {
        document.body.append(hoverMenuButton);
      }
    });
    outlinesView.addEventListener('mouseleave', function (e) {
      // Moving onto the ⋯ button itself IS a DOM mouseleave of the outline
      // (the button is a body child positioned over the row) — keep it, or it
      // strobes on/off under the pointer and clicks fall through to the row.
      if (e.relatedTarget === hoverMenuButton) {
        return;
      }
      if (hoverMenuButton.isConnected && !contextMenu) {
        hoverMenuButton.remove();
        hoverRow = null;
      }
    });
    hoverMenuButton.addEventListener('mouseleave', function (e) {
      if (!contextMenu && !(e.relatedTarget && outlinesView.contains(e.relatedTarget))) {
        hoverMenuButton.remove();
        hoverRow = null;
      }
    });
    hoverMenuButton.addEventListener('click', function (e) {
      if (hoverRow && outlinesView.contains(hoverRow)) {
        var rect = hoverMenuButton.getBoundingClientRect();
        openContextMenu(hoverRow, rect.left, rect.bottom + 2);
      }
      e.stopPropagation();
    });

    // ── Drag & drop — reorder and nest in one gesture (the Acrobat way) ──────
    // Pointer-based rather than HTML5 DnD for full control of the insertion
    // line. Dragging starts from anywhere on a row after a 5px threshold, so
    // plain clicks still navigate. Drop zones per hovered row: top quarter =
    // before it, bottom quarter = after it, middle = INTO it (last child).
    var dragState = null; // { row, started, startX, startY, suppressClick }
    var dropIndicator = null;
    var dropTarget = null; // { row, mode: 'before' | 'after' | 'into' }

    function ensureDropIndicator() {
      if (!dropIndicator) {
        dropIndicator = document.createElement('div');
        dropIndicator.id = 'outlineDropIndicator';
        document.body.append(dropIndicator);
      }
      return dropIndicator;
    }

    function clearDrag() {
      if (dropIndicator) {
        dropIndicator.remove();
        dropIndicator = null;
      }
      if (dragState && dragState.row) {
        dragState.row.classList.remove('outlineDragging');
      }
      outlinesView.classList.remove('outlineDropInto');
      if (dropTarget && dropTarget.row) {
        dropTarget.row.classList.remove('outlineDropIntoRow');
      }
      dragState = null;
      dropTarget = null;
    }

    function updateDropTarget(e) {
      var element = document.elementFromPoint(e.clientX, e.clientY);
      var anchor = element && element.closest ? element.closest('#outlinesView .treeItem > a') : null;
      if (dropTarget && dropTarget.row) {
        dropTarget.row.classList.remove('outlineDropIntoRow');
      }
      dropTarget = null;
      if (!anchor) {
        if (dropIndicator) {
          dropIndicator.style.display = 'none';
        }
        return;
      }
      var row = anchor.parentNode;
      // Never drop a row into or beside its own subtree.
      if (row === dragState.row || dragState.row.contains(row)) {
        if (dropIndicator) {
          dropIndicator.style.display = 'none';
        }
        return;
      }
      var rect = anchor.getBoundingClientRect();
      var ratio = (e.clientY - rect.top) / Math.max(1, rect.height);
      var mode = ratio < 0.25 ? 'before' : ratio > 0.75 ? 'after' : 'into';
      dropTarget = { row: row, mode: mode };
      var indicator = ensureDropIndicator();
      if (mode === 'into') {
        indicator.style.display = 'none';
        row.classList.add('outlineDropIntoRow');
      } else {
        indicator.style.display = 'block';
        indicator.style.top = (mode === 'before' ? rect.top : rect.bottom) - 1 + 'px';
        // RTL: the line spans from the row's indented start (right) leftward.
        indicator.style.left = rect.left + 'px';
        indicator.style.width = rect.width + 'px';
      }
      // Auto-scroll near the container's edges.
      var containerRect = content.getBoundingClientRect();
      if (e.clientY < containerRect.top + 24) {
        content.scrollTop -= 8;
      } else if (e.clientY > containerRect.bottom - 24) {
        content.scrollTop += 8;
      }
    }

    function completeDrop() {
      var row = dragState.row;
      var target = dropTarget;
      if (!target || !target.row || !outlinesView.contains(target.row) || !outlinesView.contains(row)) {
        return false;
      }
      var oldHost = row.parentNode;
      var oldNext = row.nextSibling;
      if (target.mode === 'into') {
        ensureToggler(target.row);
        ensureItemsContainer(target.row).append(row);
        var toggler = target.row.querySelector(':scope > .treeItemToggler');
        if (toggler) {
          toggler.classList.remove('treeItemsHidden');
        }
      } else if (target.mode === 'before') {
        target.row.before(row);
      } else {
        // 'after': when the target has visible children, dropping "after" it
        // visually means "before its first child" — otherwise the row would
        // jump below the whole subtree the user was aiming just under.
        var kids = target.row.querySelector(':scope > .treeItems');
        var expanded =
          kids &&
          !(target.row.querySelector(':scope > .treeItemToggler') || {}).classList?.contains(
            'treeItemsHidden',
          );
        if (kids && expanded && kids.querySelector(':scope > .treeItem')) {
          kids.prepend(row);
        } else {
          target.row.after(row);
        }
      }
      if (row.parentNode === oldHost && row.nextSibling === oldNext) {
        // Positional no-op (dropped right back where it was) — don't dirty
        // the document for it, same as an unchanged rename.
        focusRow(row);
        return true;
      }
      if (oldHost && oldHost !== row.parentNode && oldHost.classList && oldHost.classList.contains('treeItems') && oldHost.parentNode) {
        cleanupItem(oldHost.parentNode);
      }
      afterEdit();
      focusRow(row);
      return true;
    }

    outlinesView.addEventListener('pointerdown', function (e) {
      if (e.button !== 0 || renaming || inResults() || editingDisabled()) {
        return;
      }
      var target = e.target;
      if (!target || !target.closest || target.classList.contains('treeItemToggler')) {
        return;
      }
      var anchor = target.closest('.treeItem > a');
      if (!anchor) {
        return;
      }
      clearDrag(); // a lost pointerup must not leave an orphaned gesture
      dragState = {
        row: anchor.parentNode,
        started: false,
        startX: e.clientX,
        startY: e.clientY,
        pointerId: e.pointerId,
      };
    });

    document.addEventListener('pointermove', function (e) {
      if (!dragState) {
        return;
      }
      if ((e.buttons & 1) === 0) {
        clearDrag(); // primary button no longer down — the pointerup was lost
        return;
      }
      if (!dragState.started) {
        var dx = e.clientX - dragState.startX;
        var dy = e.clientY - dragState.startY;
        if (dx * dx + dy * dy < 25) {
          return; // below the 5px threshold — still a click
        }
        dragState.started = true;
        dragState.row.classList.add('outlineDragging');
        closeContextMenu();
        if (hoverMenuButton.isConnected) {
          hoverMenuButton.remove();
        }
      }
      e.preventDefault();
      updateDropTarget(e);
    });

    document.addEventListener('pointerup', function (e) {
      if (!dragState) {
        return;
      }
      var started = dragState.started;
      if (started) {
        completeDrop();
        // The pointerup is followed (same task queue) by a click on whatever
        // the pointer is over — swallow it so the drop doesn't also navigate.
        // Removed on a 0-timeout rather than {once:true}: a once-listener that
        // no click ever consumes (pointer released outside the window, or a
        // programmatic drop) would linger and eat the NEXT unrelated click.
        var suppressClick = function (ev) {
          ev.stopPropagation();
          ev.preventDefault();
        };
        document.addEventListener('click', suppressClick, true);
        setTimeout(function () {
          document.removeEventListener('click', suppressClick, true);
        }, 0);
      }
      clearDrag();
    });

    document.addEventListener('pointercancel', clearDrag);

    // ── Double-click renames (the Calibre way; Acrobat's click-pause-click is
    // the same idea). The first click of the pair navigates — harmless, the
    // rename then starts in place.
    outlinesView.addEventListener('dblclick', function (e) {
      var target = e.target;
      var anchor = target && target.closest ? target.closest('.treeItem > a') : null;
      if (!anchor || renaming || inResults() || editingDisabled()) {
        return;
      }
      e.preventDefault();
      e.stopPropagation();
      startRename(anchor.parentNode);
    });

    // While renaming, a caret-positioning click INSIDE the text being renamed
    // must not reach PDF.js's element.onclick — goToDestination would scroll
    // the page and its async textLayer focus would blur-commit mid-edit.
    outlinesView.addEventListener(
      'click',
      function (e) {
        if (renaming && renamingAnchor && e.target && renamingAnchor.contains(e.target)) {
          e.stopPropagation();
        }
      },
      true,
    );

    addButton.addEventListener('click', function () {
      if (!editingDisabled()) {
        addItem(null, false);
      }
    });

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

    // Navigation for rows PDF.js did not create: freshly added entries and
    // rows rebuilt from a host snapshot have no href and no bound onclick, so
    // clicking them would otherwise do nothing. Route them through goToPage
    // using the row's data-toc-page (page-level — the same fidelity their
    // saved destination will have).
    outlinesView.addEventListener('click', function (e) {
      if (renaming) {
        return;
      }
      var target = e.target;
      if (!target || !target.closest) {
        return;
      }
      var anchor = target.closest('.treeItem > a');
      if (!anchor || anchor.getAttribute('href')) {
        return; // PDF.js-bound rows keep their own handler
      }
      var page = Number(anchor.parentNode.dataset.tocPage);
      if (!(page > 0)) {
        return;
      }
      e.preventDefault();
      var app = window.PDFViewerApplication;
      if (app && app.pdfLinkService) {
        app.pdfLinkService.goToPage(page);
      }
      var outlineViewer = app && app.pdfOutlineViewer;
      if (outlineViewer && typeof outlineViewer._updateCurrentTreeItem === 'function') {
        outlineViewer._updateCurrentTreeItem(anchor.parentNode);
      }
      refocusInput();
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

      // Editing shortcuts, PDF-editor conventions: Ctrl+B adds a bookmark for
      // the current page; F2 renames the ring row; Delete removes it (only
      // with an empty query, like Space — the key must stay typable);
      // Alt+arrows move the ring row (↑/↓ among siblings, ←/→ indent/outdent
      // in this RTL tree).
      if ((e.ctrlKey || e.metaKey) && (e.key === 'b' || e.key === 'B')) {
        e.preventDefault();
        if (!editingDisabled()) {
          addItem(null, false);
        }
        return;
      }
      if (e.key === 'F2') {
        e.preventDefault();
        if (!editingDisabled()) {
          startRename();
        }
        return;
      }
      if (e.key === 'Delete' && input.value === '' && !inResults() && focusedIndex >= 0) {
        e.preventDefault();
        if (!editingDisabled()) {
          deleteItem();
        }
        return;
      }
      if (e.altKey && !inResults() && focusedIndex >= 0 && !editingDisabled()) {
        if (e.key === 'ArrowUp' || e.key === 'ArrowDown') {
          e.preventDefault();
          moveItem(null, e.key === 'ArrowUp' ? -1 : 1);
          return;
        }
        if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
          // RTL: Alt+← nests deeper (like plain ← expands), Alt+→ un-nests.
          e.preventDefault();
          if (e.key === 'ArrowLeft') {
            indentItem();
          } else {
            outdentItem();
          }
          return;
        }
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
      // Shown whenever the outline view is active and a document is loaded —
      // even with zero entries, so a TOC can be created from scratch on a PDF
      // that has none (the edit toggle is the affordance for that).
      var app = window.PDFViewerApplication;
      var hasDocument = !!(app && app.pdfDocument);
      var show = currentView === SIDEBAR_VIEW_OUTLINE && (outlineCount > 0 || hasDocument);
      // XFA documents: the worker's SaveDocument skips the outline block for
      // isPureXfa (their save path is XFA-specific), so editing would silently
      // discard on save — hide the affordance instead.
      var isXfa = !!(hasDocument && app.pdfDocument.isPureXfa);
      addButton.classList.toggle('hidden', isXfa);
      outlinesView.classList.toggle('outlineNoEdit', isXfa);
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

      // Let outline edits participate in the viewer's dirty state. _hasChanges()
      // drives both the beforeunload alert and the toolbar save indicator; a
      // runtime wrap means no viewer.mjs patch and survives PDF.js's own logic
      // changing. (The onBeforeUnload listener is bound to the app object, so
      // this wrapper is what it calls.)
      var originalHasChanges = app._hasChanges;
      if (typeof originalHasChanges === 'function') {
        app._hasChanges = function () {
          // Suppressed only around Vue-initiated teardowns, whose state the
          // host has already snapshotted — see the host-integration comment.
          if (window.__khSuppressUnloadPrompt) {
            return false;
          }
          return outlineDirty || originalHasChanges.call(app);
        };
      }

      // A COMPLETED save resolves the dirty state. viewer.mjs's patched
      // _triggerDownload dispatches this only after the file was actually
      // written (or the fallback anchor download fired) — a cancelled save
      // dialog dispatches nothing, so the edits correctly stay dirty.
      document.addEventListener('kh-save-complete', function () {
        outlineDirty = false;
        notifyHost();
      });

      app.eventBus._on('outlineloaded', function (evt) {
        outlineCount = evt.outlineCount || 0;
        input.value = '';
        // _finishRendering() appends the tree BEFORE dispatching this event, so
        // the DOM is present and can be corrected in place here.
        normalizeOutlineDom();
        stampDepths();
        // withNesting at THIS point can only have come from PDF.js's own render
        // (edits haven't happened yet) — it marks that PDF.js bound its
        // container-level toggler listener. See the fallback listener above.
        pdfjsTogglerHandling = outlinesView.classList.contains('withNesting');
        invalidateIndex();
        showTree();
        updateVisibility();
        // The rendered rows exist now, so destinations can be mapped onto them.
        // This also covers the "when the file loads" case: buildPageIndex()
        // syncs against the current page as soon as it resolves.
        if (outlineCount > 0) {
          buildPageIndex();
        }
        // A PDF with no outline: PDF.js disables the תוכן עניינים view in the
        // selector (onTreeLoaded sets button.disabled = !count), which would
        // make creating one from scratch impossible. Re-enable it — this
        // handler registers after ViewsManager's, so it runs after the disable.
        if (outlineCount === 0) {
          var outlineMenuButton = document.getElementById('outlinesViewMenu');
          if (outlineMenuButton) {
            outlineMenuButton.disabled = false;
          }
        }
        // Signal that outline processing (including the empty-outline dance
        // above) has settled — PDF.js re-enables the view buttons early during
        // document setup, so button state alone is not a reliable readiness
        // probe. Read by tests and available to the host app.
        searchBar.dataset.outlineLoaded = '1';
      });

      app.eventBus._on('pagechanging', function (evt) {
        syncCurrentEntry(evt.pageNumber);
      });

      app.eventBus._on('sidebarviewchanged', function (evt) {
        currentView = evt.view;
        updateVisibility(); // handles autofocus on the hidden→visible transition
      });

      // ── Dismiss on outside click / window blur ──────────────────────────────
      // Port of the Vue app's `useDropdownClose` (src/composables/useDropdownClose.ts),
      // which closes BookView's TOC side panel. Stock PDF.js never dismisses this
      // panel — `Sidebar` binds the toggle button's click and nothing else — so it
      // stays open until explicitly toggled, unlike every other panel in the app.
      //
      // Like BookView, this applies ONLY when the panel FLOATS OVER the page, never
      // when it sits side by side with it. Both apps have the two modes:
      //
      //   BookView  — `sidePanelIsOverlay` (shell width < 520px) picks the overlay
      //               component; BookViewSidePanel.vue guards the close with it.
      //   PDF.js    — viewer.css: above 840px `#viewerContainer` is pushed by
      //               `--viewsManager-width` (side by side); at ≤840px the
      //               `inset-inline-start: 0 !important` override stops the push,
      //               so the panel overlaps the page.
      //
      // Side by side, the panel takes no space from the content and dismissing it
      // on any stray click in the page would be hostile — that is a docked column,
      // not a popover. The media query is the single source of truth for which mode
      // is live, so it is queried rather than duplicated as a JS constant.
      //
      // `pointerdown` (not `click`) matches useDropdownClose's underlying
      // onClickOutside, and fires before a click can act on whatever was hit.
      var sidebarEl = document.getElementById('viewsManager');
      var toggleButtonEl = document.getElementById('viewsManagerToggleButton');
      var overlayModeQuery = window.matchMedia('(max-width: 840px)');

      // `app.viewsManager` — the ViewsManager instance (viewer.mjs assigns it in
      // `_initializeViewerComponents`). NOT `app.pdfSidebar`: that was the name in
      // older PDF.js, and it does not exist in this build, so calls on it would
      // silently no-op.
      function sidebarIsOpen() {
        return !!(app.viewsManager && app.viewsManager.isOpen);
      }

      /** True only while the panel floats OVER the page (see the note above). */
      function sidebarIsOverlay() {
        return overlayModeQuery.matches;
      }

      function closeSidebar() {
        if (sidebarIsOpen()) {
          app.viewsManager.close();
        }
      }

      document.addEventListener(
        'pointerdown',
        function (e) {
          if (!sidebarIsOpen() || !sidebarIsOverlay() || !sidebarEl) {
            return;
          }
          var target = e.target;
          if (!target || !target.nodeType) {
            return;
          }
          if (sidebarEl.contains(target)) {
            return;
          }
          // The toggle button closes the panel through its own click handler.
          // Closing here too would let that click immediately REOPEN it — the
          // toggle-button race useDropdownClose guards with `justClosed`.
          if (toggleButtonEl && toggleButtonEl.contains(target)) {
            return;
          }
          // Editor UI this panel owns is rendered on document.body, outside the
          // sidebar subtree — a click there is not "outside".
          if (target.closest && target.closest('#outlineContextMenu, #outlineRowMenuButton')) {
            return;
          }
          // An in-progress rename commits FIRST, then the panel closes — clicking
          // away is a deliberate "I'm done here", so it should do what clicking
          // away from the anchor alone does (commit) and then dismiss.
          //
          // It has to be committed explicitly rather than left to the anchor's own
          // blur handler. This listener is on `pointerdown` (capture), which runs
          // BEFORE the blur, and `finish()` ends by calling `input.focus()` — which
          // pulls focus back INTO the panel we are about to close. Committing here
          // makes the order deterministic: text saved, focus settled, then close.
          if (renaming && renamingAnchor) {
            renamingAnchor.blur(); // fires onBlur → finish(true)
          }
          closeSidebar();
        },
        true,
      );

      // Focus leaving the window (switching apps, or into another WebView frame)
      // dismisses too, matching useDropdownClose's closeOnBlur. The timeout lets
      // document.activeElement settle first, exactly as the Vue version does.
      window.addEventListener('blur', function () {
        setTimeout(function () {
          if (!sidebarIsOpen() || !sidebarIsOverlay()) {
            return;
          }
          var active = document.activeElement;
          var movedIntoFrame =
            active instanceof HTMLIFrameElement || active instanceof HTMLObjectElement;
          if (!document.hasFocus() || movedIntoFrame) {
            // As with the outside click: commit an in-progress rename before
            // dismissing. Losing the window mid-rename must not discard the text.
            if (renaming && renamingAnchor) {
              renamingAnchor.blur();
            }
            closeSidebar();
          }
        }, 0);
      });

      // The outline tree is rebuilt on every document load; drop the stale
      // index and reset the editor. Bound to 'documentinit' — NOT
      // 'documentloaded', which only fires after getDownloadInfo() resolves,
      // i.e. after the FULL file downloads; under this app's
      // disableAutoFetch:true that is late or never for an in-place open(),
      // which left the previous document's edit state (dirty flag, etc.)
      // dangling. 'documentinit' fires right after setInitialView, per
      // document, regardless of download progress.
      app.eventBus._on('documentinit', function () {
        // The host sets __khSuppressUnloadPrompt on the OUTGOING document's
        // window before navigating — but an iframe's WindowProxy keeps its JS
        // identity across navigations, so on a REUSED iframe (pdf→pdf tab
        // switch) the flag written during teardown is still set when the NEXT
        // document initializes here. Left set, it silences _hasChanges for the
        // life of that document — annotation edits would close/unload without
        // a prompt. Clear it: a new document starting means any suppression
        // window is over.
        window.__khSuppressUnloadPrompt = false;
        outlineCount = 0;
        input.value = '';
        invalidateIndex();
        // Invalidate the page index too — bumping the token makes any in-flight
        // build from the previous document discard its result.
        pageIndexToken++;
        pageIndex = null;
        activeItem = null;
        // Edits belong to the previous document — reset the editor entirely.
        outlineDirty = false;
        closeContextMenu();
        clearDrag();
        // A rename active when the document switches never gets its blur (the
        // focused anchor is removed with the old tree, which fires no blur) —
        // without this reset `renaming` would stay true for the session,
        // disabling startRename and the focusin reclaim permanently.
        renaming = false;
        renamingAnchor = null;
        pdfjsTogglerHandling = false;
        delete searchBar.dataset.outlineLoaded;
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
