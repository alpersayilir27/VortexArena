namespace VortexArena.Core.Player
{
    /// <summary>
    /// <see cref="WorldScaleTuner"/>'ın gözler arası mesafeyi hangi yoldan daralttığı.
    /// ⚠️ Serialize edilir — yeni değer SONA eklenir.
    /// </summary>
    public enum WorldScaleMethod
    {
        /// <summary>
        /// Rig'in ölçeği küçültülür, kafanın dünya konumu <c>TrackingSpace</c> ile geri düzeltilir.
        /// Göz ayrımının ata ölçeğinden etkilenmesine bağlıdır — etkilenmiyorsa hiçbir şey değişmez.
        /// </summary>
        RigScale = 0,

        /// <summary>
        /// Render'dan hemen önce iki gözün view matrisi doğrudan yeniden yazılır: göz konumları
        /// kafa merkezine doğru çekilir. Rig'e hiç dokunmaz. Tek belirsizlik URP'nin XR geçişinin
        /// kameranın stereo matrislerini mi yoksa kendi <c>XRPass</c> değerlerini mi kullandığıdır.
        /// </summary>
        ViewMatrix = 1,
    }
}
