// Core's seforim-DB row types, made visible project-wide under their plain names.
//
// These are not mirrors or aliases-for-convenience: Core's models ARE the wire types. They carry
// the same [MessagePackObject(keyAsPropertyName: true)] contract and the same property names, so
// the bytes the frontend receives are unchanged, and a change to a SELECT list in Core reaches
// the frontend with nothing in between to keep in step.
//
// They are declared GLOBALLY rather than per file because they appear in three places that would
// each need the same block — the row-returning queries, the RPC result envelopes that carry them,
// and the search service. The *Args / *Result envelopes themselves stay in this project: they
// shape RPC calls rather than data.

global using AltTocStructureRow = KitveiHakodesh.Core.SeforimDb.AltTocStructureRow;
global using BookIdRow = KitveiHakodesh.Core.SeforimDb.BookIdRow;
global using BookInfo = KitveiHakodesh.Core.SeforimDb.BookInfo;
global using BookMetadataRow = KitveiHakodesh.Core.SeforimDb.BookMetadataRow;
global using BookRow = KitveiHakodesh.Core.SeforimDb.BookRow;
global using BookSourceNameRow = KitveiHakodesh.Core.SeforimDb.BookSourceNameRow;
global using BookVersionRow = KitveiHakodesh.Core.SeforimDb.BookVersionRow;
global using CategoryRow = KitveiHakodesh.Core.SeforimDb.CategoryRow;
global using CommentaryLinkRow = KitveiHakodesh.Core.SeforimDb.CommentaryLinkRow;
global using CommentaryLinkGroupRow = KitveiHakodesh.Core.SeforimDb.CommentaryLinkGroupRow;
global using ConnectionTypeRow = KitveiHakodesh.Core.SeforimDb.ConnectionTypeRow;
global using DefaultCommentatorRow = KitveiHakodesh.Core.SeforimDb.DefaultCommentatorRow;
global using EnclosingTocPathRow = KitveiHakodesh.Core.SeforimDb.EnclosingTocPathRow;
global using LineBookRow = KitveiHakodesh.Core.SeforimDb.LineBookRow;
global using LineContentRow = KitveiHakodesh.Core.SeforimDb.LineContentRow;
global using LineIndexRow = KitveiHakodesh.Core.SeforimDb.LineIndexRow;
global using LineRefRow = KitveiHakodesh.Core.SeforimDb.LineRefRow;
global using LineRow = KitveiHakodesh.Core.SeforimDb.LineRow;
global using LinkTargetRow = KitveiHakodesh.Core.SeforimDb.LinkTargetRow;
global using RawLineRow = KitveiHakodesh.Core.SeforimDb.RawLineRow;
global using ReverseBookRow = KitveiHakodesh.Core.SeforimDb.ReverseBookRow;
global using ReverseLineRow = KitveiHakodesh.Core.SeforimDb.ReverseLineRow;
global using SchemaMetaValueRow = KitveiHakodesh.Core.SeforimDb.SchemaMetaValueRow;
global using SectionNavRow = KitveiHakodesh.Core.SeforimDb.SectionNavRow;
global using StaticFilterRow = KitveiHakodesh.Core.SeforimDb.StaticFilterRow;
global using TocEntryRow = KitveiHakodesh.Core.SeforimDb.TocEntryRow;
global using LineTocEntryRow = KitveiHakodesh.Core.SeforimDb.LineTocEntryRow;
global using TocEntryPathRow = KitveiHakodesh.Core.SeforimDb.TocEntryPathRow;
global using TocPathsForLinesResult = KitveiHakodesh.Core.SeforimDb.TocPathsForLinesResult;
global using TocPrefixRow = KitveiHakodesh.Core.SeforimDb.TocPrefixRow;
global using TocSectionRow = KitveiHakodesh.Core.SeforimDb.TocSectionRow;
global using TocTitleRow = KitveiHakodesh.Core.SeforimDb.TocTitleRow;
global using WordLinkAnchorRow = KitveiHakodesh.Core.SeforimDb.WordLinkAnchorRow;
global using WordLinkAnchorsResult = KitveiHakodesh.Core.SeforimDb.WordLinkAnchorsResult;
global using WordLinkTargetRow = KitveiHakodesh.Core.SeforimDb.WordLinkTargetRow;
global using WordLinkTargetsResult = KitveiHakodesh.Core.SeforimDb.WordLinkTargetsResult;
global using HebrewBook = KitveiHakodesh.Core.HebrewBooks.HebrewBook;
global using HebrewBooksEdition = KitveiHakodesh.Core.HebrewBooks.HebrewBooksEdition;
global using FtsHit = KitveiHakodesh.Core.SeforimDbFullTextSearch.FtsHit;
global using FtsIndexStatus = KitveiHakodesh.Core.SeforimDbFullTextSearch.FtsIndexStatus;
global using FtsSearchResult = KitveiHakodesh.Core.SeforimDbFullTextSearch.FtsSearchResult;
global using FtsStreamChunk = KitveiHakodesh.Core.SeforimDbFullTextSearch.FtsStreamChunk;
global using FileHit = KitveiHakodesh.Core.LocalFiles.FileHit;
global using LocateDocumentsResult = KitveiHakodesh.Core.LocalFiles.LocateDocumentsResult;
global using LocalFileIndexStatusResult = KitveiHakodesh.Core.LocalFiles.LocalFileIndexStatusResult;
global using CatalogTocSearchResult = KitveiHakodesh.Core.SeforimDbCatalog.CatalogTocSearchResult;
global using CatalogTitleSearchResult = KitveiHakodesh.Core.SeforimDbCatalog.CatalogTitleSearchResult;
global using CatalogTocStatus = KitveiHakodesh.Core.SeforimDbCatalog.CatalogTocStatus;
