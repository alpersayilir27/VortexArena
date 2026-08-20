using System;

namespace VortexArena.Net
{
    /// <summary>
    /// The content of StreamingAssets/arena.json — the last link of the discovery chain
    /// (manually entered IP > beacon > arena.json). Loaded by ServerDiscovery.
    /// </summary>
    [Serializable]
    public class ArenaStaticConfig
    {
        public string serverIp = "";
        public int serverPort = 0; // 0/missing → ArenaProtocol.CONTROL_PORT
    }
}
