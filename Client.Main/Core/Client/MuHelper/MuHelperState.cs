using System;

namespace Client.Main.Core.Client
{
    /// <summary>
    /// Client-side MU Helper session: the configuration the server stored for the character (0xAE) and the
    /// running state confirmed by MuHelperStatusUpdate (0xBF 0x51). Updated on the main thread only.
    /// </summary>
    public static class MuHelperState
    {
        // The server only sends 0xAE when the character has saved data, so a stored blob is only valid for its owner.
        private static MuHelperConfig _config;
        private static string _owner;

        public static bool IsRunning { get; private set; }
        /// <summary>Zen of the last periodic charge, and the total charged since the helper was started.</summary>
        public static uint LastZenCost { get; private set; }
        public static ulong TotalZenCost { get; private set; }
        public static DateTime StartedAtUtc { get; private set; }

        /// <summary>Raised after a configuration or status update.</summary>
        public static event Action Changed;

        public static MuHelperConfig GetConfig(string characterName)
            => _config != null && _owner == characterName ? _config : new MuHelperConfig();

        public static void SetConfig(string characterName, MuHelperConfig config)
        {
            _owner = characterName;
            _config = config;
            Changed?.Invoke();
        }

        /// <summary>Applies MuHelperStatusUpdate: a money charge, or the started / stopped confirmation.</summary>
        public static void ApplyStatus(bool consumeMoney, uint money, bool paused)
        {
            if (consumeMoney)
            {
                LastZenCost = money;
                TotalZenCost += money;
            }
            else if (paused)
            {
                IsRunning = false;
            }
            else
            {
                IsRunning = true;
                StartedAtUtc = DateTime.UtcNow;
                LastZenCost = 0;
                TotalZenCost = 0;
            }

            Changed?.Invoke();
        }

        /// <summary>Stops the local session without waiting for the server (disconnect, death, map change).</summary>
        public static void MarkStopped()
        {
            if (!IsRunning)
                return;
            IsRunning = false;
            Changed?.Invoke();
        }
    }
}
