using System;

namespace VortexArena.Net
{
    /// <summary>
    /// StreamingAssets/arena.json içeriği — keşif zincirinin son halkası
    /// (elle girilen IP > beacon > arena.json). ServerDiscovery yükler.
    /// </summary>
    [Serializable]
    public class ArenaStaticConfig
    {
        public string serverIp = "";
        public int serverPort = 0; // 0/eksik → ArenaProtocol.CONTROL_PORT
    }
}
