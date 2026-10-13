using KitveiHakodesh.Core.WordDocuments;

namespace KitveiHakodeshService.Pdf;

/// <summary>
/// Transport wrapper over Core's <see cref="WordDocumentRenderer"/> (Word → PDF, Office-free
/// OOXML → HTML fallback, render cache). What stays here: DI lifetime, the log hookup, the
/// cache location ("convert-cache" next to the service exe, portable with the install) and the
/// KHS_DISABLE_WORD=1 test seam.
/// </summary>
public sealed class WordConversionService
{
    private readonly WordDocumentRenderer _renderer;

    public WordConversionService(ILogger<WordConversionService> logger)
    {
        _renderer = new WordDocumentRenderer(
            Path.Combine(AppContext.BaseDirectory, "convert-cache"),
            wordDisabled: Environment.GetEnvironmentVariable("KHS_DISABLE_WORD") is "1" or "true",
            log: (level, message, ex) =>
            {
                switch (level)
                {
                    case WordDocumentRenderLogLevel.Warning: logger.LogWarning(ex, "{Message}", message); break;
                    case WordDocumentRenderLogLevel.Debug: logger.LogDebug(ex, "{Message}", message); break;
                    default: logger.LogInformation(ex, "{Message}", message); break;
                }
            });
    }

    /// <summary>Abort an in-flight render of <paramref name="sourcePath"/>; true when one was cancelled.</summary>
    public bool Cancel(string sourcePath) => _renderer.Cancel(sourcePath);

    /// <summary>Render to a cached PDF (Word) or HTML (fallback). Throws when no route can render it.</summary>
    public Task<(string path, bool isHtml)> RenderAsync(string sourcePath, CancellationToken ct) =>
        _renderer.RenderAsync(sourcePath, ct);
}
