import type { Book } from './Book'

export type PageType = 'homepage' | 'openfile' | 'bookview' | 'pdfview' | 'hebrewbooks-view' | 'search' | 'settings' | 'hebrewbooks' | 'kezayit-search' | 'workspaces';

export interface BookState {
    bookId: number;
    bookTitle: string;
    initialLineIndex?: number; // Line index (0 to totalLines-1) representing which line should be at the top of the viewport. Set by TOC selection or saved from scroll position
    lineOffset?: number; // Pixel offset of the line from viewport top - for accurate restoration (negative = line is scrolled up)
    scrollTop?: number; // Raw scroll position in pixels - Layer 1 for fast restoration
    shouldHighlight?: boolean; // Whether to highlight the initial line (for search result navigation)
    isTocOpen?: boolean; // Whether TOC overlay is open
    isFirstTocOpen?: boolean; // Whether this is the first time opening TOC (for full-width vs compact display)
    showBottomPane?: boolean; // Whether bottom pane of split view is visible
    hasConnections?: boolean; // Whether book has any connections (targum, reference, commentary, or other)
    selectedLineIndex?: number; // Currently selected line index for commentary
    selectedTocEntryId?: number; // If a TOC line was clicked, store the TOC entry ID to load all its lines' links
    commentaryFilterConnectionTypeId?: number; // Selected connection type filter for commentary (undefined = show all)
    defaultCommentaryBookId?: number; // Book's default commentary (from book definition, used on first load)
    currentCommentaryBookId?: number; // Currently selected commentary book ID (updated by scroll observer)
    currentCommentaryGroupName?: string; // Currently selected commentary group name (for precise matching when same book appears multiple times)
    diacriticsState?: number; // 0 = show all, 1 = hide cantillation, 2 = hide nikkud too
    isLineDisplayInline?: boolean; // false = block display, true = inline display
    originalHtml?: string; // Store original HTML for diacritics restoration
    isSearchOpen?: boolean; // Whether search overlay is open
    showAltToc?: boolean; // Whether to display alt TOC entries (default: true)
}

export interface PdfState {
    fileName: string;
    fileUrl: string;
    filePath?: string; // Original file path for persistence
    source?: string; // Source of the PDF (e.g., 'hebrewbook', 'file')
    bookId?: string; // Hebrew book ID for session restoration
    bookTitle?: string; // Hebrew book title for display
    isLoading?: boolean; // Loading state for virtual URL recreation
}

export interface SearchState {
    searchQuery: string; // Current search text
    scrollPosition: number; // Scroll position in results list (legacy - for non-virtual scroll)
    firstVisibleItemIndex?: number; // First visible item index for virtual scroll restoration
    itemOffset?: number; // Pixel offset of first visible item from viewport top
    hasSearched: boolean; // Whether a search has been executed
    highlightTerms?: string; // Terms to highlight when navigating from search results to book
    highlightSnippet?: string; // Snippet to use for background highlighting
    // Note: results are NOT stored here - they're loaded from cache to save memory
}

export interface Tab {
    id: number;
    title: string;
    isActive: boolean;
    currentPage: PageType;
    bookState?: BookState;
    pdfState?: PdfState;
    searchState?: SearchState;
}
