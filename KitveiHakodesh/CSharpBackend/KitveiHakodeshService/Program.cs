using KitveiHakodeshService.Catalog;
using KitveiHakodeshService.Common;
using KitveiHakodeshService.Dictionary;
using KitveiHakodeshService.HebrewBooks;
using KitveiHakodeshService.Http;
using KitveiHakodeshService.Ipc;
using KitveiHakodeshService.LocalFiles;
using KitveiHakodeshService.SeforimDb;
using KitveiHakodeshService.UserSettings;

// KitveiHakodesh service — the clean, native (.NET 10 / AOT) data front-door.
// It serves a semantic {op,args} MessagePack RPC over TWO transports in front of one
// shared Dispatcher:
//   • a named pipe ("KitveiHakodesh")            — Ipc/PipeServer (the original method,
//     intended for the installed Windows-service path);
//   • a loopback HTTP host (http://127.0.0.1:N)  — Http/HttpHostServer, which any local
//     app can spawn and talk to directly. Dev (the Vite plugin) uses this one.
// A spawner passes KHS_OWNER_PID so the service self-cleans (releases the port, deletes
// its discovery file) when its owner exits; Windows-service installation comes later.

var builder = Host.CreateApplicationBuilder(args);

// One shared HttpClient for outbound fetches (HebrewBooks PDF download). A single long-lived
// instance is the recommended pattern; a generous timeout covers large scanned-book PDFs, and a
// UA header keeps download.hebrewbooks.org from treating us as a bot.
builder.Services.AddSingleton(_ =>
{
    var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("KitveiHakodesh/1.0");
    return client;
});
builder.Services.AddSingleton<DocumentLocatorService>();
builder.Services.AddSingleton<HebrewBooksService>();
builder.Services.AddSingleton<DictionaryService>();
builder.Services.AddSingleton<SeforimDbService>();
builder.Services.AddSingleton<FullTextSearchService>();
builder.Services.AddSingleton<CatalogTocSearchService>();
builder.Services.AddSingleton<UserSettingsService>();
builder.Services.AddSingleton<HttpHostState>();
builder.Services.AddSingleton<LocalFileGrants>();
builder.Services.AddSingleton<KitveiHakodeshService.Pdf.WordConversionService>();
builder.Services.AddSingleton<Dispatcher>();
builder.Services.AddHostedService<PipeServer>();
builder.Services.AddHostedService<HttpHostServer>();
builder.Services.AddHostedService<OwnerWatcher>();
builder.Services.AddHostedService<FtsIndexingStarter>();
builder.Services.AddHostedService<CatalogTocIndexingStarter>();
builder.Services.AddHostedService<IdleMemoryTrimmer>();
// Watches the seforim DB WHILE the service runs and rebuilds both indexes on an
// in-place DB change (startup uses the one-shot stamp check; this covers live updates).
builder.Services.AddHostedService<DbChangeWatcher>();

var host = builder.Build();
host.Run();
