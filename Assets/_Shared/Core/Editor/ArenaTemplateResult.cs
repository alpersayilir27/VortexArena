using System;
using System.Collections.Generic;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <see cref="ArenaTemplateWizard.Create"/> sonucu. Sihirbaz HİÇBİR durumda exception
    /// fırlatmaz: hata olursa <see cref="Success"/> <c>false</c> ve <see cref="Error"/> dolu döner
    /// (MCP / batch otomasyonundan güvenle çağrılabilsin).
    /// </summary>
    [Serializable]
    public class ArenaTemplateResult
    {
        /// <summary>Arena üretildi mi.</summary>
        public bool Success;

        /// <summary>Başarısızlık nedeni (başarılıysa boş).</summary>
        public string Error;

        /// <summary>Üretilen sahnenin asset yolu.</summary>
        public string ScenePath;

        /// <summary>Üretilen <c>MapDefinition</c> asset yolu.</summary>
        public string MapAssetPath;

        /// <summary>Elle rötuş gerektiren noktalar (sanat geçişi, NavMesh, vb.).</summary>
        public List<string> Warnings = new List<string>();

        /// <summary>Tek satırlık özet (pencere/konsol için).</summary>
        public string Summary;
    }
}
