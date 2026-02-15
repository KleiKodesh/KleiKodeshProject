import type { Book } from './Book'

export type PageType = 'homepage' | 'openfile' | 'bookview' | 'pdfview' | 'hebrewbooks-view' | 'search' | 'settings' | 'hebrewbooks' | 'kezayit-search' | 'workspaces';

export interface BookState {
    bookId: number;
    bookTitle: string;
    initialLineIndex?: number; // Line index (0 to totalLines-1) representing which line should be at the top of the viewport. Set by TOC selection or saved from scroll position
    lineOffset?: number; // Pixel offset of the line from viewport top - for accurate restoration (negative = line is scrolled up)
    shouldHighlight?: boolean; // Whether to highlight the initial line (for search result navigation)
    isTocOpen?: boolean; // Whether TOC overlay is open
    isFirstTocOpen?: boolean; // Whether this is the first time opening TOC (for full-width vs compact display)
    showBottomPane?: boolean; // Whether bottom pane of split view is visible
    hasConnections?: boolean; // Whether book has any connections (targum, reference, commentary, or other)
    selectedLineIndex?: number; // Currently selected line index for commentary
    commentaryFilterConnectionTypeId?: number; // Selected connection type filter for commentary (undefined = show all)
    commentaryPositionsByFilter?: Record<string, { groupIndex: number; targetBookId?: number; scrollPosition: number }>; // Position per filter for persistence when switching
    commentaryPositions?: Record<string, { groupIndex: number; targetBookId?: number }>; // Legacy position storage (simple format)
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
    hasSearched: boolean; // Whether a search has been executed
    highlightTerms?: string; // Terms to highlight when navigating from search results to book
    highlightSnippet?: string; // Snippet to use for background highlighting
    results?: any[]; // Search results to persist across tab switches
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
