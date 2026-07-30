namespace VortexArena.Core.Combat
{
    /// <summary>
    /// YEREL oyuncunun o an elinde ne tuttuğunun tek buluşma noktası (§6.2 poz paketindeki
    /// <c>itemL</c>/<c>itemR</c>/<c>gripFlags</c> baytlarının kaynağı).
    /// <para>
    /// <b>YAZAR:</b> <c>Weapon</c> / <c>WeaponGranter</c> (Core.Combat) — eşya ele girdiğinde ya da
    /// bırakıldığında <see cref="Report"/> çağrılır.
    /// <b>OKUYAN:</b> <c>PlayerPoseTracker</c> (App) — 20 Hz poz paketine bu üç baytı koyar.
    /// </para>
    /// <para>
    /// <b>Bu seam neden var:</b> App katmanı "elde ne var" sorusunu cevaplamak için sahnedeki silah
    /// listesini yeniden keşfetmek (GetComponentsInChildren / grab olaylarına abone olmak) zorunda
    /// kalmasın. Bilgiyi zaten bilen taraf onu bir kez bildirir.
    /// </para>
    /// <para>
    /// ⚠️ Bu sınıf <b>hiçbir şey göndermez</b> ve hiçbir kural uygulamaz — yalnız son bildirilen
    /// durumu tutar. Ağa yazma tek kapıdan (<c>PlayerPoseTracker</c> poz döngüsü) olur; buraya
    /// gönderme eklenirse eşya durumu poz ile aynı pakette gitmez ve iki ayrı doğruluk kaynağı doğar.
    /// </para>
    /// </summary>
    public static class HeldItems
    {
        /// <summary>Sol eldeki eşyanın <c>netItemId</c>'si; <c>0</c> = el boş.</summary>
        public static byte Left { get; private set; }

        /// <summary>Sağ eldeki eşyanın <c>netItemId</c>'si; <c>0</c> = el boş.</summary>
        public static byte Right { get; private set; }

        /// <summary>
        /// İki el AYNI eşyayı tutuyor (<c>FLAG_GRIP_LINKED</c>). ⚠️ "Aynı id iki slotta" tek başına
        /// bunu ifade etmez — çift tabanca meşru bir durumdur (§6.6).
        /// </summary>
        public static bool GripLinked { get; private set; }

        /// <summary>Ana el sağ mı (<c>FLAG_PRIMARY_RIGHT</c>). Yalnız <see cref="GripLinked"/> iken anlamlı.</summary>
        public static bool PrimaryRight { get; private set; }

        /// <summary>Yerel elde tutma durumunu bildirir (yazan taraf: <c>Weapon</c>/<c>WeaponGranter</c>).</summary>
        public static void Report(byte left, byte right, bool gripLinked, bool primaryRight)
        {
            Left = left;
            Right = right;
            GripLinked = gripLinked;
            PrimaryRight = primaryRight;
        }

        /// <summary>
        /// Durumu sıfırlar (iki el boş). Sahne/harita geçişinde çağrılır: aksi hâlde eski sahnenin
        /// silahı yeni sahnede "elde" bildirilmeye devam eder.
        /// </summary>
        public static void Clear()
        {
            Left = 0;
            Right = 0;
            GripLinked = false;
            PrimaryRight = false;
        }
    }
}
