import type { Book } from './Book'

export type PageType = 'homepage' | 'kezayit-landing' | 'bookview' | 'pdfview' | 'hebrewbooks-view' | 'search' | 'settings' | 'hebrewbooks' | 'kezayit-search';

export interface BookState {
    bookId: number;
    bookTitle: string;
    initialLineIndex?: number; // Line index (0 to totalLines-1) representing which line should be at the top of the viewport. Set by TOC selection or saved from scroll position
    isTocOpen?: boolean; // Whether TOC overlay is open
    showBottomPane?: boolean; // Whether bottom pane of split view is visible
    hasConnections?: boolean; // Whether book has any connections (targum, reference, commentary, or other)
    selectedLineIndex?: number; // Currently selected line index for commentary
    commentaryGroupIndex?: number; // Currently selected commentary group index
    commentaryTargetBookId?: number; // targetBookId of currently selected commentary for persistence
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

export interface Tab {
    id: number;
    title: string;
    isActive: boolean;
    currentPage: PageType;
    bookState?: BookState;
    pdfState?: PdfState;
}
