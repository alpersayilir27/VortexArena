using System;
using System.Collections.Generic;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <see cref="ServerConfigExporter.Export"/> sonucu — menüden çağrıldığında dialog metnini,
    /// otomasyondan (MCP / batch) çağrıldığında ise makine tarafından okunabilir raporu taşır.
    /// <para>
    /// Alanlar düz <c>public</c>'tir (property değil): hem Unity serileştiricisi hem de
    /// reflection tabanlı otomasyon araçları alanları sorunsuz okur.
    /// </para>
    /// </summary>
    [Serializable]
    public class ServerConfigExportResult
    {
        /// <summary>Yazılan <c>Server/config/weapons.json</c> dosyasının mutlak yolu.</summary>
        public string WeaponsPath;

        /// <summary>Yazılan <c>Server/config/maps.json</c> dosyasının mutlak yolu.</summary>
        public string MapsPath;

        /// <summary>weapons.json'a yazılan silah satırı sayısı (atlanan/yinelenen hariç).</summary>
        public int WeaponCount;

        /// <summary>maps.json'a yazılan harita satırı sayısı (atlanan/yinelenen hariç).</summary>
        public int MapCount;

        /// <summary>Doğrulama uyarıları; boş liste = temiz export. Konsola da yazılır.</summary>
        public List<string> Warnings = new List<string>();

        /// <summary>Tek satırlık özet (Debug.Log / dialog başlığı için).</summary>
        public string Summary;
    }
}
