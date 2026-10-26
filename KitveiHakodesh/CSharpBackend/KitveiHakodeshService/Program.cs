using KitveiHakodeshService.Catalog;
using KitveiHakodeshService.Common;
using KitveiHakodeshService.Dictionary;
using KitveiHakodeshService.HebrewBooks;
using KitveiHakodeshService.Hosting;
using KitveiHakodeshService.Http;
using KitveiHakodeshService.Ipc;
using KitveiHakodeshService.LocalFiles;
using KitveiHakodeshService.SeforimDb;
using KitveiHakodeshService.UserSettings;

// KitveiHakodesh service — the clean, native (.NET 10 / AOT) data front-door, and the ONE
// process in the product that opens the seforim database and the search indexes.
// It serves a semantic {op,args} MessagePack RPC over a named pipe ("KitveiHakodesh") in
// front of one shared Dispatcher. Three ways to run:
//   • installed Windows service (LocalSystem, demand start) — the product. Binds to the first
//     user that connects, then runs the data host (see Hosting/UserSessionBinding).
//   • console process spawned by a host (the Vite dev plugin, or a hosted app that found no
//     installed service) — inherits the spawner's user; KHS_OWNER_PID ties it to the spawner;
//     also opens a loopback HTTP host for the dev browser.
//   • `--install` / `--uninstall` — SCM registration, run elevated by the installer.

internal static partial class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--install": return ServiceRegistration.Install();
                case "--uninstall": return ServiceRegistration.Uninstall();
            }
        }

        if (WindowsServiceMode.IsWindowsService)
        {
            SingleInstanceInstalledServiceWins.EndOtherCopies();
            var service = Host.CreateApplicationBuilder(args);
            service.Services.AddWindowsService(options => options.ServiceName = WindowsServiceMode.ServiceName);
            service.Services.AddHostedService<UserSessionBinding>();
            service.Build().Run();
            return 0;
        }

        if (!SingleInstanceInstalledServiceWins.ConsoleMayRun(out string reason))
        {
            Console.Error.WriteLine("Not starting: " + reason + ".");
            return 0;
        }

        BuildDataHost(args).Run();
        return 0;
    }

    /// <summary>The data host: every service, both transports (the HTTP host only outside
    /// service mode). Built once per process - or, in service mode, once the user is known.</summary>
    public static IHost BuildDataHost(string[] args)
    {
        // A full library update downloaded last session waits beside the live file; swap it in
        // now, before anything opens the database. Here, not in Main: the library lives in the
        // user's profile, which a service only knows once it is bound to a user.
        KitveiHakodesh.Core.SeforimLibraryDownload.SeforimLibraryStagedDatabase.PromoteIfStaged();

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
        builder.Services.AddSingleton<LocalFileIndexService>();
        builder.Services.AddSingleton<HebrewBooksService>();
        builder.Services.AddSingleton<DictionaryService>();
        builder.Services.AddSingleton<SeforimDbService>();
        builder.Services.AddSingleton<SearchExpansionService>();
        builder.Services.AddSingleton<FullTextSearchService>();
        builder.Services.AddSingleton<CatalogTocSearchService>();
        builder.Services.AddSingleton<UserSettingsService>();
        builder.Services.AddSingleton<HttpHostState>();
        builder.Services.AddSingleton<LocalFileGrants>();
        builder.Services.AddSingleton<KitveiHakodeshService.Pdf.WordConversionService>();
        builder.Services.AddSingleton<OwnerRegistry>();
        builder.Services.AddSingleton<Dispatcher>();
        builder.Services.AddHostedService<PipeServer>();
        if (!WindowsServiceMode.IsWindowsService)
        {
            // The loopback HTTP host is the dev browser's door; a Windows service has no dev
            // browser and should open no port.
            builder.Services.AddHostedService<HttpHostServer>();
        }
        // Stops the service (console or installed) once no program that uses it is running any
        // more; hosts restart it on demand (Http/OwnerWatcher).
        builder.Services.AddHostedService<OwnerWatcher>();
        builder.Services.AddHostedService<FtsIndexingStarter>();
        builder.Services.AddHostedService<CatalogTocIndexingStarter>();
        // The local file index too: built at start (after a short delay), not on the first search.
        builder.Services.AddHostedService<KitveiHakodeshService.LocalFiles.LocalFileIndexingStarter>();
        // Singleton too, so the diagnostics endpoint can trigger a trim on the same instance.
        builder.Services.AddSingleton<IdleMemoryTrimmer>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<IdleMemoryTrimmer>());
        // Grows the bundled HebrewBooks catalog in the background. Checks twice a day and almost
        // always finds nothing to do - the interval between actual walks is a stamp inside the
        // catalog file, not a schedule here.
        // Singleton too, so the owner watcher can see a walk in progress (Http/OwnerWatcher).
        builder.Services.AddSingleton<HebrewBooksCatalogUpdateService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<HebrewBooksCatalogUpdateService>());
        // Downloads Otzaria's library when none is installed, and resumes a pending download on
        // start. Singleton too, so the owner watcher can see a download in progress.
        builder.Services.AddSingleton<SeforimLibraryDownloadService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SeforimLibraryDownloadService>());
        // Watches the seforim DB WHILE the service runs and rebuilds both indexes on an
        // in-place DB change (startup uses the one-shot stamp check; this covers live updates).
        builder.Services.AddHostedService<DbChangeWatcher>();
        // Weekly self-update: check GitHub, download, launch the installer as the user. Only the
        // installed Windows service acts (UpdaterService returns at once in a console instance).
        builder.Services.AddSingleton<KitveiHakodeshService.KitveiHakodesh.Updater>();
        builder.Services.AddHostedService<KitveiHakodeshService.KitveiHakodesh.UpdaterService>();

        return builder.Build();
    }
}
