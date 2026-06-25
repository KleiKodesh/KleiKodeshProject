using KitveiHakodeshService.Server;
using KitveiHakodeshService.Seforim;
using KitveiHakodeshService.Dictionary;
using KitveiHakodeshService.HebrewBooks;

var builder = WebApplication.CreateBuilder(args);

// ── Service registration ───────────────────────────────────────────────────

builder.Services.AddSingleton<SseManager>();
builder.Services.AddSingleton<SeforimDbManager>();
builder.Services.AddSingleton<SeforimFullTextSearch>();
builder.Services.AddSingleton<DictionaryDbManager>();
builder.Services.AddSingleton<HebrewBooksDbManager>();

// CORS — allow the Vite dev server and any localhost frontend to call this service.
// In production the allowed origin should be locked down to the specific app URL.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(
                "http://localhost:5173",   // Vite dev server
                "http://localhost:5174",   // Vite alt port
                "http://localhost:4173")   // Vite preview
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());        // Required for EventSource with credentials
});

var app = builder.Build();

app.UseCors();

// ── DB startup ─────────────────────────────────────────────────────────────
// Read the seforim DB path from configuration. In development, set SeforimDbPath
// in appsettings.Development.json or as an environment variable.
var seforimDbPath = app.Configuration["SeforimDbPath"] ?? "";
var seforimDb = app.Services.GetRequiredService<SeforimDbManager>();
seforimDb.Open(seforimDbPath);

// Kick off FTS index build in the background once the DB is ready.
if (seforimDb.IsReady)
{
    var fts = app.Services.GetRequiredService<SeforimFullTextSearch>();
    var sseManager = app.Services.GetRequiredService<SseManager>();
    fts.OnDbReady(seforimDbPath, sseManager);
}

// ── Routes ──────────────────────────────────────────────────────────────────
ApiEndpoints.Register(app);

app.Run();
