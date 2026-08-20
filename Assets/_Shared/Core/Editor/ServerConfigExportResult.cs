using System;
using System.Collections.Generic;

namespace VortexArena.Core.Editor
{
    /// <summary><see cref="ServerConfigExporter.Export"/> result: dialog text from the menu,
    /// machine readable report from automation (MCP / batch).</summary>
    /// <remarks>Plain public fields, not properties: Unity serialization and reflection based
    /// automation both read fields without trouble.</remarks>
    [Serializable]
    public class ServerConfigExportResult
    {
        /// <summary>Absolute path of the written <c>Server/config/maps.json</c>.</summary>
        public string MapsPath;

        /// <summary>Map rows written to maps.json (skipped/duplicate excluded).</summary>
        /// <remarks>No weapon field: the server keeps no weapon table.</remarks>
        public int MapCount;

        /// <summary>Validation warnings; empty list = clean export. Also logged.</summary>
        public List<string> Warnings = new List<string>();

        /// <summary>One line summary for Debug.Log / dialog title.</summary>
        public string Summary;
    }
}
