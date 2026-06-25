using System;
using System.Net;

namespace UpdateCheckerLib
{
    /// <summary>
    /// Thrown when an update check or download fails after all retry attempts.
    /// Carries structured details for display in user-facing error messages.
    /// </summary>
    internal class UpdateException : Exception
    {
        /// <summary>HTTP status code, if the failure was an HTTP error. Null for network/other errors.</summary>
        public HttpStatusCode? StatusCode { get; }

        /// <summary>Number of attempts that were made before giving up.</summary>
        public int Attempts { get; }

        /// <summary>The URL that was being requested when the failure occurred.</summary>
        public string Url { get; }

        public UpdateException(string message, string url, int attempts, HttpStatusCode? statusCode = null, Exception inner = null)
            : base(message, inner)
        {
            Url        = url;
            Attempts   = attempts;
            StatusCode = statusCode;
        }

        /// <summary>
        /// Builds a user-facing detail string (Hebrew) with all available context.
        /// </summary>
        public string ToUserMessage()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Message);

            if (StatusCode.HasValue)
                sb.AppendLine($"קוד שגיאת שרת: {(int)StatusCode.Value} ({StatusCode.Value})");

            if (Attempts > 1)
                sb.AppendLine($"מספר ניסיונות: {Attempts}");

            if (!string.IsNullOrEmpty(Url))
                sb.AppendLine($"כתובת: {Url}");

            if (InnerException != null)
                sb.AppendLine($"פרטים טכניים: {InnerException.Message}");

            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Thrown when the GitHub API check fails (as opposed to the asset download).
    /// </summary>
    internal class UpdateCheckException : Exception
    {
        /// <summary>The GitHub API URL that was queried.</summary>
        public string Url { get; }

        public UpdateCheckException(string message, string url, Exception inner = null)
            : base(message, inner)
        {
            Url = url;
        }

        /// <summary>
        /// Builds a user-facing detail string (Hebrew) with all available context.
        /// </summary>
        public string ToUserMessage()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Message);

            if (InnerException != null)
                sb.AppendLine($"פרטים טכניים: {InnerException.Message}");

            if (!string.IsNullOrEmpty(Url))
                sb.AppendLine($"כתובת: {Url}");

            return sb.ToString().TrimEnd();
        }
    }
}
