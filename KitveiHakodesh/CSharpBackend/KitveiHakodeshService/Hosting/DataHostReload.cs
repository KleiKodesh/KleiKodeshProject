namespace KitveiHakodeshService.Hosting;

/// <summary>
/// "Restart the data host, not the service."
///
/// A change that the data host resolves once at construction (the additional libraries, the
/// database path) used to stop the WHOLE Windows service, relying on the next client to start
/// it again. That is fragile: a LocalSystem service that stops itself stays Stopped until
/// something starts it, and a client that cannot (or does not) leaves the user with a dead
/// product. Now the service process stays up: <see cref="UserSessionBinding"/> stops only the
/// data host, builds a fresh one in the same process, and the SCM never sees the service stop.
///
/// The requester marks the reload, then stops the data host through its own lifetime.
/// Anything that stops the data host WITHOUT a mark (the installer's "shutdown" op, a failed
/// build) still ends the service, exactly as before. In a console instance (dev, portable) the
/// mark is never consumed: the process exits and its spawner starts a new one, as always.
/// </summary>
public static class DataHostReload
{
    private static int _requested;

    /// <summary>Marks the next data-host stop as "rebuild the host in this process".</summary>
    public static void Request() => Interlocked.Exchange(ref _requested, 1);

    /// <summary>True once per <see cref="Request"/>.</summary>
    public static bool Consume() => Interlocked.Exchange(ref _requested, 0) == 1;

    /// <summary>Marks the reload, then stops the data host that owns <paramref name="lifetime"/>.</summary>
    public static void RequestAndStop(IHostApplicationLifetime lifetime)
    {
        Request();
        lifetime.StopApplication();
    }
}
