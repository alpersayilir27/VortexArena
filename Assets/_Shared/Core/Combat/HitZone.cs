namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bir vuruş kutusunun gövde bölgesi — <b>hasar çarpanının tek kaynağı</b>.
    /// <para>
    /// Model CS2'dir: kafa 4×, gövde ve KOLLAR 1×, karın/leğen 1.25×, bacak 0.75×. Çarpanın
    /// sayısal karşılığı burada değil <see cref="WeaponDefinition.GetZoneMultiplier"/>'dadır —
    /// denge sayıları silah başına değişir, bölge listesi değişmez.
    /// </para>
    /// <para>
    /// ⚠️ <b>Serialize edilen enum: yeni değer SONA eklenir.</b> Unity sayısal indeks saklar;
    /// başa/ortaya eklemek <c>RemoteAvatar.prefab</c>'daki kutuların bölgesini sessizce kaydırır
    /// (aynı kural <c>Team</c> için de yazılı).
    /// </para>
    /// <para>
    /// ⚠️ <b><see cref="Body"/> sıfırdır ve öyle kalmalı:</b> atanmamış (ya da bileşeni yeni
    /// eklenmiş) bir kutu en zararsız değere düşsün — çarpanı 1×, yani hiçbir sürpriz hasar yok.
    /// </para>
    /// </summary>
    public enum HitZone
    {
        /// <summary>Göğüs ve kollar — çarpan 1× (referans hasar).</summary>
        Body,

        /// <summary>Kafa — çarpan <c>WeaponDefinition.HeadshotMultiplier</c> (CS2: 4×).</summary>
        Head,

        /// <summary>Karın/leğen — çarpan <c>WeaponDefinition.StomachMultiplier</c> (CS2: 1.25×).</summary>
        Stomach,

        /// <summary>Bacaklar — çarpan <c>WeaponDefinition.LegMultiplier</c> (CS2: 0.75×).</summary>
        Leg,
    }
}
