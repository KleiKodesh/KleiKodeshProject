using System;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// There is no seforim database to read: the user has not chosen one, or the one they
    /// chose is no longer where it was.
    ///
    /// Its own file because it is thrown from more than one place — opening a connection and
    /// resolving a path — and caught by both orchestrators, which answer it very differently:
    /// the hosted app opens its setup wizard, the dev service reports the error.
    ///
    /// The message is diagnostic, for a log. Core does not phrase what the user reads.
    /// </summary>
    public sealed class SeforimDbUnavailableException : Exception
    {
        /// <summary>The path that was expected to hold the database, or null when none was
        /// ever configured. The two cases need different answers, so they stay distinguishable.</summary>
        public string? ExpectedPath { get; }

        private SeforimDbUnavailableException(string message, string? expectedPath)
            : base(message)
        {
            ExpectedPath = expectedPath;
        }

        /// <summary>No database has been chosen — a fresh install, not a broken one.</summary>
        public static SeforimDbUnavailableException NotConfigured() =>
            new SeforimDbUnavailableException("no seforim database is configured", null);

        /// <summary>A database was chosen but is not there any more — moved, deleted, or on a
        /// drive that is not connected.</summary>
        public static SeforimDbUnavailableException NotOnDisk(string expectedPath) =>
            new SeforimDbUnavailableException(
                "the configured seforim database is not on disk: " + expectedPath, expectedPath);
    }
}
