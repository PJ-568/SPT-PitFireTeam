using System;
using System.Diagnostics;
using UnityEngine;

namespace friendlySAIN.Modules
{
    internal class Logger
    {
        public Logger()
        {
        }

        public static void LogInfo(string message)
        {
#if DEBUG
            friendlySAIN.Log.LogInfo($"[{Time.time}] " + message);
#endif
        }

        public static void LogTrace(string message)
        {
#if DEBUG
            var stackTrace = new StackTrace();
            friendlySAIN.Log.LogDebug($"[{Time.time}] {message}\nStackTrace:\n{stackTrace}");
#endif
        }

        public static void LogError(string message)
        {
            friendlySAIN.Log.LogError(message);
        }
        public static void LogError(Exception error)
        {
            friendlySAIN.Log.LogError(error);
        }
    }
}
