using System;

namespace VortexArena.Core
{
    /// <summary>
    /// <b>Henüz başlamamış</b> maçın seçili modu (§5.3 <c>selection_state</c>) — yalnız SUNUM için.
    /// <para>
    /// ⚠️ <see cref="ModeRuntime"/> ile karıştırılmaz: orası <b>koşan</b> maçın kurallarıdır ve
    /// otoritesi <c>load_match.rules</c>'tur. Burası "admin panelinde ne seçili" bilgisidir; hiçbir
    /// kuralı, HUD'u ya da loadout'u değiştirmez — maç türü ancak <c>start_match</c> ile değişir.
    /// </para>
    /// <para>
    /// <b>Neden var:</b> lobide beklerken (ve admin bir arenayı sahnelediğinde) taban şeritlerinin
    /// görünüp görünmeyeceği seçili moda bağlı — takımlı modda (TDM/turnuva) şeritler gerekli,
    /// takımsızda (FFA) yanıltıcı. Aktif kural o sırada lobi profili olduğu için bu bilgi başka
    /// hiçbir yerden okunamıyor. Tek tüketicisi <c>Arena.BaseZoneVisibility</c>'dir.
    /// </para>
    /// <para>
    /// ⚠️ Buraya <b>tüketicisi olmayan alan eklenmez</b>: seçimin geri kalanı (harita, süre, limit)
    /// operatöre aittir ve <c>admin_state</c> ile yalnız adminlere gider.
    /// </para>
    /// </summary>
    public static class ModeSelection
    {
        /// <summary>Sunucu seçimi hiç bildirdi mi. <c>false</c> = eski sunucu (ya da bağlantı yok):
        /// tüketici aktif kurala düşer, çünkü "bilinmiyor" ile "takımsız" aynı şey değildir.</summary>
        public static bool HasValue { get; private set; }

        /// <summary>Seçili mod kimliği (açılışta <c>"lobby"</c>). Yalnız teşhis/loglama içindir —
        /// davranış <see cref="IsTeamless"/>'tan okunur (istemcide <c>if (modeId == …)</c> zinciri
        /// yazılmaz, §10.5).</summary>
        public static string ModeId { get; private set; } = "";

        /// <summary>Seçili mod takımsız mı (<c>teamMode:"none"</c>).</summary>
        public static bool IsTeamless { get; private set; }

        /// <summary>Seçim gerçekten değiştiğinde tetiklenir (aynı değerin tekrarında SESSİZ —
        /// mesaj her bağlantıda ve her seçim komutunda gelebilir).</summary>
        public static event Action Changed;

        /// <summary>Telden gelen seçimi uygular. <paramref name="teamMode"/> §10.5 sözlüğüdür;
        /// <c>"none"</c> dışındaki her değer (boş dahil) takımlı sayılır — bilinmeyen değerin
        /// varsayılana düşmesi kuralı.</summary>
        public static void Apply(string modeId, string teamMode)
        {
            Set(true, modeId ?? "", string.Equals(teamMode, "none", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Bağlantı koptu / oturum bitti: "bilinmiyor"a döner ki tüketici yine aktif
        /// kurala düşsün.</summary>
        public static void Reset()
        {
            Set(false, "", false);
        }

        private static void Set(bool hasValue, string modeId, bool teamless)
        {
            bool changed = hasValue != HasValue || modeId != ModeId || teamless != IsTeamless;

            HasValue = hasValue;
            ModeId = modeId;
            IsTeamless = teamless;

            if (changed)
            {
                Changed?.Invoke();
            }
        }
    }
}
