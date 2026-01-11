using System;
using System.Diagnostics;

namespace UpdateCheckerLib
{
    internal static class ErrorLogger
    {
        [Conditional("DEBUG")]
        public static void Log(Exception ex, string context)
        {
            Debug.WriteLine("=======================================");
            Debug.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Debug.WriteLine("Context: " + context);
            Debug.WriteLine(ex.ToString());
            Debug.WriteLine("=======================================");

            if (Debugger.IsAttached)
                Debugger.Break();
        }

        [Conditional("DEBUG")]
        public static void Log(string message, string context)
        {
            Debug.WriteLine("=======================================");
            Debug.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Debug.WriteLine("Context: " + context);
            Debug.WriteLine(message);
            Debug.WriteLine("=======================================");
        }
    }
}
