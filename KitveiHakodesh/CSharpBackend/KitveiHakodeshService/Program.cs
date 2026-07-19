using KitveiHakodeshService.Catalog;
using KitveiHakodeshService.Common;
using KitveiHakodeshService.Dictionary;
using KitveiHakodeshService.HebrewBooks;
using KitveiHakodeshService.Ipc;
using KitveiHakodeshService.LocalFiles;
using KitveiHakodeshService.SefroimDb;
using KitveiHakodeshService.UserSettings;

// KitveiHakodesh service — the clean, native (.NET 10 / AOT) data front-door.
// It exposes one named pipe ("KitveiHakodesh") speaking a semantic {op,args} RPC
// envelope. In dev it runs as a console process spawned by the Vite dev plugin;
// Windows-service installation is added in a later step.

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<DocumentLocatorService>();
builder.Services.AddSingleton<HebrewBooksService>();
builder.Services.AddSingleton<DictionaryService>();
builder.Services.AddSingleton<SeforimDbService>();
builder.Services.AddSingleton<FullTextSearchService>();
builder.Services.AddSingleton<CatalogTocSearchService>();
builder.Services.AddSingleton<UserSettingsService>();
builder.Services.AddSingleton<Dispatcher>();
builder.Services.AddHostedService<PipeServer>();
builder.Services.AddHostedService<FtsIndexingStarter>();
builder.Services.AddHostedService<CatalogTocIndexingStarter>();
builder.Services.AddHostedService<IdleMemoryTrimmer>();
// Watches the seforim DB WHILE the service runs and rebuilds both indexes on an
// in-place DB change (startup uses the one-shot stamp check; this covers live updates).
builder.Services.AddHostedService<DbChangeWatcher>();

var host = builder.Build();
host.Run();
