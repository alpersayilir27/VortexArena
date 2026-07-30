namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Kavrama soketinin hangi el için olduğu (<see cref="GripSocketMarker"/>).
    /// <para>
    /// ⚠️ <b>Serialize edilen enum</b> — Unity sayısal indeks saklar. Yeni değer yalnız SONA
    /// eklenir; başa/ortaya ekleme sahnelerdeki işaretçilerin türünü sessizce kaydırır (Primary
    /// işaretçisi Secondary olur ve SO'ya yanlış alan yazılır).
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
