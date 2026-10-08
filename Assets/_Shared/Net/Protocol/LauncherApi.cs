// Also linked into the nullable-enabled launcher; Unity and the server compile it without nullable.
#nullable disable
using System;

namespace VortexArena.Protocol
{
    /// <summary>Operator launcher HTTP endpoints on CONTROL_PORT, loopback only (§13).</summary>
    public static class LauncherApi
    {
        public const string STATUS_PATH = "/launcher/status";
        public const string RECORDING_PATH = "/launcher/recording";
        public const string SHUTDOWN_PATH = "/launcher/shutdown";
    }

    /// <summary>Body of <c>POST /launcher/recording</c>.</summary>
    [Serializable]
    public class LauncherRecordingRequest
    {
        public bool on;
    }

    /// <summary>Answer of <c>GET /launcher/status</c> and <c>POST /launcher/recording</c>.</summary>
    [Serializable]
    public class LauncherStatus
    {
        public int protocolVersion;
        public string venue;

        /// <summary>Same values as <see cref="MatchInfo"/> (§5.3).</summary>
        public string phase;
        public string phaseReason;
        public string modeId;
        public string sceneName;

        public int playerCount;
        public int adminCount;
        public LauncherRecordingStatus recording;
    }

    [Serializable]
    public class LauncherRecordingStatus
    {
        /// <summary>Operator turned recording on; files open at match start.</summary>
        public bool armed;

        /// <summary>A file is open right now (armed in the lobby = armed &amp;&amp; !active).</summary>
        public bool active;

        /// <summary>Open file name without folder; empty when none.</summary>
        public string file;

        public long elapsedMs;

        /// <summary>Full path of the replay folder.</summary>
        public string directory;
    }
}
