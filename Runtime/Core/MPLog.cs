using System;
using UnityEngine;

namespace MPBlocks.Core
{
    /// <summary>
    /// Structured logger for MPBlocks. Every line carries the stable "[MP]" prefix so
    /// tooling (tools/mp-verify.sh) and agents can grep it.
    ///
    /// Format: [MP] LEVEL EVENT key=value key=value
    /// </summary>
    public static class MPLog
    {
        public const string Prefix = "[MP]";

        public static void Info(string evt) => Write(LogType.Log, evt, null);
        public static void Info(string evt, params (string key, object value)[] fields)
            => Write(LogType.Log, evt, fields);

        public static void Warn(string evt) => Write(LogType.Warning, evt, null);
        public static void Warn(string evt, params (string key, object value)[] fields)
            => Write(LogType.Warning, evt, fields);

        public static void Error(string evt) => Write(LogType.Error, evt, null);
        public static void Error(string evt, params (string key, object value)[] fields)
            => Write(LogType.Error, evt, fields);

        private static void Write(LogType type, string evt, (string key, object value)[] fields)
        {
            var level = type switch
            {
                LogType.Log => "INFO",
                LogType.Warning => "WARN",
                _ => "ERROR",
            };

            var line = $"{Prefix} {level} {evt}";
            if (fields != null)
            {
                foreach (var (key, value) in fields)
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    line += $" {key}={Format(value)}";
                }
            }

            switch (type)
            {
                case LogType.Warning: Debug.LogWarning(line); break;
                case LogType.Error:
                case LogType.Exception: Debug.LogError(line); break;
                default: Debug.Log(line); break;
            }
        }

        private static string Format(object value) => value switch
        {
            null => "null",
            float f => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    /// <summary>Stable log event names. Automation greps these; do not rename.</summary>
    public static class MPEvent
    {
        public const string ServerReady = "SERVER_READY";
        public const string ClientConnecting = "CLIENT_CONNECTING";
        public const string ClientJoined = "CLIENT_JOINED";
        public const string ClientLeft = "CLIENT_LEFT";
        public const string ServerStopped = "SERVER_STOPPED";
        public const string StateHashMatch = "STATE_HASH_MATCH";
        public const string StateHashMismatch = "STATE_HASH_MISMATCH";
        public const string AuthViolation = "AUTH_VIOLATION";
        public const string NoNetworkManager = "NO_NETWORKMANAGER";
        public const string ConfigError = "CONFIG_ERROR";
        public const string ValueSetByRequest = "VALUE_SET_BY_REQUEST";
        public const string EventReceived = "EVENT_RECEIVED";
        public const string SessionIdOk = "SESSION_ID_OK";
        public const string SessionIdMismatch = "SESSION_ID_MISMATCH";
        public const string LocalPlayerReady = "LOCAL_PLAYER_READY";
    }
}
