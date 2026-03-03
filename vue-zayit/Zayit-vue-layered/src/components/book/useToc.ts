import { bookTocService } from '@/data/services/bookTocService';
import type { TocEntry } from '@/data/types/BookToc';

export function useToc() {
    return {
        // TOC building
        buildTocFromFlat: (tocEntriesFlat: TocEntry[]) => bookTocService.buildTocFromFlat(tocEntriesFlat),
        findTocEntryByLineIndex: (tocEntries: TocEntry[], lineIndex: number) =>
            bookTocService.findTocEntryByLineIndex(tocEntries, lineIndex),
        flattenTocTree: (tocEntries: TocEntry[]) => bookTocService.flattenTocTree(tocEntries),
        getTocPath: (entry: TocEntry) => bookTocService.getTocPath(entry),
    };
}
