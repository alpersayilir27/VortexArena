namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bir elin parmak duruşunun <b>adı</b> — kavrama kaydının (<see cref="ItemGripPose"/>) slot
    /// başına taşıdığı üç seçenekten biri.
    /// <para>
    /// Sayıları burada DEĞİL <see cref="HandPoseProfile"/>'da durur ve oradan tek kaynaktan
    /// dağıtılır (<see cref="HandGripPresets"/>): aynı beş oran hem yerel sentetik eli (ISDK), hem
    /// stüdyodaki hayalet eli, hem uzak avatarın Mixamo elini sürer. Stüdyoda görülen el ile oyunda
    /// görülen el bu yüzden aynıdır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Serialize EDİLİR</b> (<see cref="ItemGripPose.preset"/> asset'lerde yaşıyor): yeni
    /// değer <b>SONA</b> eklenir. Unity sayısal indeks sakladığı için başa/ortaya ekleme yazılmış
    /// bütün kavramaların duruşunu sessizce kaydırır.
    /// </para>
    /// </summary>
    public enum HandGripPreset
    {
        /// <summary>Boşta duran el: parmaklar hafif açık (anatomik dinlenme).</summary>
        Idle = 0,

        /// <summary>Tetiği olan el: işaret parmağı tetikte serbest, diğerleri kabzayı sarar.</summary>
        Firing = 1,

        /// <summary>Kabzayı/ön kabzayı saran el: beş parmak da kapanır.</summary>
        Grip = 2,
    }
}
