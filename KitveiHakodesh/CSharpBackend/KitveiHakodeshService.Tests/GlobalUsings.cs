// The catalog engine these harnesses exercise moved into KitveiHakodesh.Core, where it is one
// implementation shared by the service and the hosted app. The old names are kept here rather
// than rewritten through ~6 files, because they are what the harnesses' own prose calls them.
global using CatalogTocIndex = KitveiHakodesh.Core.SeforimDbCatalog.SeforimDbCatalogIndex;
global using CatalogTocHit = KitveiHakodesh.Core.SeforimDbCatalog.CatalogTocHit;
global using CatalogTocTextRules = KitveiHakodesh.Core.SeforimDbCatalog.SeforimDbCatalogTextNormalizer;
