using UnityEngine;

namespace MPBlocks.Core
{
    /// <summary>
    /// The name this machine's player wants to use.
    ///
    /// Stored in PlayerPrefs so it survives a restart, and overridable from the command line
    /// with <c>-mp-name &lt;text&gt;</c> — handy for launching three players on one machine.
    ///
    /// The name is only a *request*: the server decides what actually gets stored, exactly like
    /// every other value.
    /// </summary>
    public static class LocalPlayerName
    {
        public const string PrefKey = "mpblocks.playerName";

        /// <summary>Long enough for a real name, short enough to read on screen.</summary>
        public const int MaxLength = 20;

        /// <summary>The saved name, or an empty string.</summary>
        public static string Get() => Sanitize(PlayerPrefs.GetString(PrefKey, string.Empty));

        /// <summary>Remembers a name for next time.</summary>
        public static void Set(string value)
        {
            PlayerPrefs.SetString(PrefKey, Sanitize(value));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// What to submit when this player connects: command line first, then the saved name,
        /// then empty (the server will generate "Player N").
        /// </summary>
        public static string Resolve()
        {
            var fromCli = FromCommandLine();
            if (!string.IsNullOrEmpty(fromCli)) return fromCli;

            return Get();
        }

        /// <summary>Strips control characters and caps the length.</summary>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var builder = new System.Text.StringBuilder(MaxLength);

            foreach (char c in value)
            {
                if (builder.Length >= MaxLength) break;
                if (c == '\n' || c == '\r' || c == '\t') continue;
                if (char.IsControl(c)) continue;
                builder.Append(c);
            }

            return builder.ToString().Trim();
        }

        private static string FromCommandLine()
        {
            try
            {
                var args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "-mp-name") return Sanitize(args[i + 1]);
                }
            }
            catch
            {
                // ignored
            }

            return string.Empty;
        }
    }
}
