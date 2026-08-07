namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Kavrama noktasının hangi el için olduğu: kabza mı, ön kabza mı.
    /// <para>
    /// ⚠️ <b>Serialize edilen enum</b> — Unity sayısal indeks saklar. Yeni değer yalnız SONA
    /// eklenir; başa/ortaya ekleme kayıtlı değerleri sessizce kaydırır (Primary olan Secondary
    /// olur ve kavrama yanlış alandan okunur).
    /// </para>
    /// </summary>
    public enum GripSocketKind
    {
        /// <summary>Ana el (tetik eli).</summary>
        Primary,

        /// <summary>Ön kabza — yalnız <see cref="ItemHoldMode.TwoHand"/>'de anlamlı.</summary>
        Secondary
    }
}
