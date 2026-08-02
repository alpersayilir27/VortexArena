using System;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Core
{
    /// <summary>
    /// Aktif maçın kurallarının (Docs/ArenaNet-Protokol.md §10.5) <b>tek okuma noktası</b>.
    /// <para>
    /// <b>Otorite sunucudadır:</b> değerler <c>load_match.rules</c> / <c>welcome.match.rules</c>
    /// ile gelir. İstemcide <c>if (modeId == "…")</c> zinciri YOKTUR — yeni mod eklemek istemci
    /// kodunu değiştirmez.
    /// </para>
    /// <para>
    /// <b>Neden tek nokta:</b> canlanma (<c>PlayerCombatState</c>), skor satırı (<c>ModeHudBase</c>),
    /// silah kaynağı ve admin takım kipi (<c>AdminRoster</c>) aynı bilgiyi ister. Dördü ayrı ayrı
    /// <c>load_match</c> dinlerse dördü ayrı ayrı bayatlar; tek okuma + tek besleme noktası bunu
    /// yapısal olarak imkânsız kılar.
    /// </para>
    /// <para>
    /// Durum ve olay <b>statiktir</b> (<c>AdminSelection</c> deseni): dinleyiciler beslemenin ne
    /// zaman kurulduğunu bilmek zorunda kalmasın. Besleme <see cref="ModeRuntimePump"/> tarafından
    /// yapılır ve o kendini önyükler.
    /// </para>
    /// </summary>
    public static class ModeRuntime
    {
        /// <summary>Admin arayüzünün de okuduğu katalog kaynak adı (uzantısız).</summary>
        private const string CatalogResourceName = "GameCatalog";

        /// <summary>Kurallar değiştiğinde (ana thread).</summary>
        public static event Action Changed;

        /// <summary>Kuralların hangi moda ait olduğu; hiç maç yüklenmediyse boş.</summary>
        public static string ModeId { get; private set; } = "";

        public static ModeTeamMode Teams { get; private set; } = ModeTeamMode.TwoTeams;

        public static ModeScoreKind Scoring { get; private set; } = ModeScoreKind.Team;

        /// <summary>true = takım arkadaşı vurulabilir. Karar sunucudadır; istemci yalnız gösterir.</summary>
        public static bool FriendlyFire { get; private set; }

        public static ModeReviveAnchor Revive { get; private set; } = ModeReviveAnchor.OwnBase;

        public static ModeWeaponSource Weapons { get; private set; } = ModeWeaponSource.WeaponCanvas;

        /// <summary>Ölüm → en erken canlanma süresi; sunucunun <c>respawn.delaySeconds</c>'ı ile
        /// aynı değerdir (ikisi de modun kuralından beslendiği için çakışmazlar).
        /// <para><b><c>0</c> geçerli bir değerdir</b> (anında canlanma) — varsayılana çekilmez.
        /// Alan telde hiç gelmezse DTO'nun kendi başlangıcı (<c>RESPAWN_DELAY</c>) geçerlidir,
        /// yani "yazılmadı" ile "sıfır yazıldı" birbirine karışmaz.</para></summary>
        public static float RespawnDelay { get; private set; } = ArenaProtocol.RESPAWN_DELAY;

        /// <summary>
        /// Faz <c>playing</c> değilken silah ateşlenebilir mi (§10.5 <c>fireWhilePaused</c>).
        /// Lobi türünde <c>true</c>: hedef atışı yapılır, namlu alevi herkese relay edilir — ama
        /// <b>hasar yine yoktur</b>, onu sunucu fazdan kapatır (§10.3).
        /// <para>⚠️ Bu alan sayesinde istemcide <c>if (modeId == "lobby")</c> zinciri doğmaz;
        /// "burada ateş edilir mi" sorusunun tek cevabı buradadır.</para>
        /// </summary>
        public static bool FireWhilePaused { get; private set; }

        /// <summary>Takımsız mod kısayolu — çağıranların enum karşılaştırmasını tekrarlamaması için.</summary>
        public static bool IsTeamless => Teams == ModeTeamMode.None;

        /// <summary>
        /// Sunucudan gelen kural şeklini uygular. <paramref name="info"/> <c>null</c> ise
        /// (kuralları taşımayan bir sunucu) katalog devralır — bkz. <see cref="ApplyFromCatalog"/>.
        /// </summary>
        public static void Apply(string modeId, ModeRulesInfo info)
        {
            if (info == null)
            {
                ApplyFromCatalog(modeId);
                return;
            }

            Set(modeId,
                ParseTeams(info.teamMode),
                ParseScoring(info.scoring),
                info.friendlyFire,
                ParseRevive(info.reviveAnchor),
                ParseWeapons(info.weaponSource),
                info.respawnDelay,
                info.fireWhilePaused);
        }

        /// <summary>
        /// Kurallar telde gelmediğinde (kuralları taşımayan bir sunucu) <see cref="ModeDefinition"/>
        /// önizleme değerlerini uygular; mod katalogda yoksa varsayılana döner.
        /// <para>
        /// ⚠ <b>Sapmada SUNUCU kazanır.</b> <see cref="ModeDefinition"/>'daki kural alanları yalnız
        /// arayüz/önizleme içindir — gerçek bir <c>load_match</c> geldiği anda bu değerler ezilir.
        /// (<c>roundSeconds</c>/<c>scoreLimit</c> için bugün de geçerli olan sözleşmenin aynısı.)
        /// </para>
        /// </summary>
        public static void ApplyFromCatalog(string modeId)
        {
            ModeDefinition mode = FindCatalogMode(modeId);
            if (mode == null)
            {
                Reset(modeId);
                return;
            }

            // Serbest atış ayrı bir SO alanı DEĞİL, lobi profilinin türevidir: iki alanı da elle
            // işaretlemek "lobi ama ateş kapalı" gibi anlamsız bir kombinasyonu mümkün kılardı.
            // Otorite yine sunucuda (rules.fireWhilePaused); burası yalnız kuralsız telin yedeği.
            Set(modeId, mode.TeamMode, mode.Scoring, mode.FriendlyFire,
                mode.Revive, mode.Weapons, mode.RespawnDelay, mode.IsLobbyProfile);
        }

        /// <summary>Varsayılana (takımlı TDM) döner — açık sahneye dönüşte ve bağlantı kopunca.</summary>
        public static void Reset(string modeId = "")
        {
            Set(modeId, ModeTeamMode.TwoTeams, ModeScoreKind.Team, false,
                ModeReviveAnchor.OwnBase, ModeWeaponSource.WeaponCanvas, ArenaProtocol.RESPAWN_DELAY, false);
        }

        // ---------------------------------------------------------------- iç işler

        private static void Set(string modeId, ModeTeamMode teams, ModeScoreKind scoring,
            bool friendlyFire, ModeReviveAnchor revive, ModeWeaponSource weapons, float respawnDelay,
            bool fireWhilePaused)
        {
            string id = modeId ?? "";
            // 0 korunur (anında canlanma); yalnız anlamsız negatif kırpılır.
            float delay = Mathf.Max(0f, respawnDelay);

            bool changed = id != ModeId || teams != Teams || scoring != Scoring ||
                           friendlyFire != FriendlyFire || revive != Revive || weapons != Weapons ||
                           fireWhilePaused != FireWhilePaused ||
                           !Mathf.Approximately(delay, RespawnDelay);

            ModeId = id;
            Teams = teams;
            Scoring = scoring;
            FriendlyFire = friendlyFire;
            Revive = revive;
            Weapons = weapons;
            RespawnDelay = delay;
            FireWhilePaused = fireWhilePaused;

            if (changed)
            {
                Changed?.Invoke();
            }
        }

        private static ModeDefinition FindCatalogMode(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
            {
                return null;
            }

            // Katalog admin arayüzüyle aynı yerden okunur (Assets/_Shared/Data/Resources/).
            // Bulunamazsa sessizce varsayılana düşülür: bu yol yalnız kurallar telde
            // gelmediğinde koşar, sahada kurallar her zaman sunucudan gelir.
            var catalog = Resources.Load<GameCatalog>(CatalogResourceName);
            return catalog != null ? catalog.FindMode(modeId) : null;
        }

        // Ayrıştırma kuralı (§10.5): BİLİNMEYEN/BOŞ DEĞER VARSAYILANA DÜŞER. Bu sayede yeni bir
        // kural değeri eklemek eski istemciyi kırmaz ve PROTOCOL_VERSION artmaz.

        private static ModeTeamMode ParseTeams(string value)
        {
            return Matches(value, "none") ? ModeTeamMode.None : ModeTeamMode.TwoTeams;
        }

        private static ModeScoreKind ParseScoring(string value)
        {
            return Matches(value, "player") ? ModeScoreKind.Player : ModeScoreKind.Team;
        }

        private static ModeReviveAnchor ParseRevive(string value)
        {
            if (Matches(value, "standstill"))
            {
                return ModeReviveAnchor.StandStill;
            }

            return Matches(value, "none") ? ModeReviveAnchor.None : ModeReviveAnchor.OwnBase;
        }

        private static ModeWeaponSource ParseWeapons(string value)
        {
            return Matches(value, "random") ? ModeWeaponSource.RandomGrant : ModeWeaponSource.WeaponCanvas;
        }

        private static bool Matches(string value, string expected)
        {
            return !string.IsNullOrEmpty(value) &&
                   string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
