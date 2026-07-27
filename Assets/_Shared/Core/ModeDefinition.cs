using System;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Protocol;

namespace VortexArena.Core
{
    /// <summary>
    /// Oyun modu tanımı: modId + varsayılan kural parametreleri + uyumlu haritalar.
    /// <para>
    /// <see cref="ModeId"/> protokol anahtarıdır ("tdm") — admin <c>start_match{modeId}</c>
    /// gönderir, sunucu bunu kendi <c>IGameMode</c> kayıtlarıyla eşler. Kural OTORİTESİ
    /// SUNUCUDADIR; buradaki roundSeconds/scoreLimit yalnız arayüz/ön izleme değerleridir.
    /// </para>
    /// <see cref="HudPrefab"/> mod UI prefabıdır (Modes/&lt;Mod&gt;/UI/ altında); Core mod
    /// assembly'lerine referans vermez, bu yüzden alan tipi düz <c>GameObject</c>'tir.
    /// </summary>
    [CreateAssetMenu(fileName = "Mode", menuName = "VortexArena/Mode Definition")]
    public class ModeDefinition : ScriptableObject
    {
        [Header("Kimlik")]
        [Tooltip("Protokol anahtarı — sunucudaki IGameMode.ModeId ile birebir aynı.")]
        [SerializeField] private string modeId = "";
        [SerializeField] private string displayName = "";

        [Header("Varsayılan kurallar (otorite sunucudadır)")]
        [SerializeField] private int roundSeconds = 300;
        [SerializeField] private int scoreLimit = 30;

        [Header("Mod şekli — YALNIZ ÖNİZLEME (§10.5; otorite sunucudadır)")]
        [Tooltip("Takım kipi. Gerçek maçta load_match.rules.teamMode kazanır.")]
        [SerializeField] private ModeTeamMode teamMode = ModeTeamMode.TwoTeams;
        [Tooltip("Skor hangi kanala yazılır (takım skoru / bireysel skor).")]
        [SerializeField] private ModeScoreKind scoring = ModeScoreKind.Team;
        [Tooltip("Açıksa takım arkadaşı vurulabilir.")]
        [SerializeField] private bool friendlyFire;
        [Tooltip("Canlanma şartı: kendi tabanına gir / sabit dur.")]
        [SerializeField] private ModeReviveAnchor revive = ModeReviveAnchor.OwnBase;
        [Tooltip("Silah kaynağı — tümüyle istemci sunumu, sunucuda karşılığı yok.")]
        [SerializeField] private ModeWeaponSource weapons = ModeWeaponSource.Rack;
        [Tooltip("Ölüm → en erken canlanma süresi (sn). 0 GEÇERLİDİR = anında canlanma; " +
                 "varsayılan protokoldeki RESPAWN_DELAY'dir.")]
        [SerializeField] private float respawnDelay = ArenaProtocol.RESPAWN_DELAY;

        [Header("İçerik")]
        [Tooltip("Bu modun oynanabildiği haritalar; boş bırakılırsa katalogdaki tüm uyumlu haritalar.")]
        [SerializeField] private MapDefinition[] maps = Array.Empty<MapDefinition>();
        [Tooltip("Modun silah seti.")]
        [SerializeField] private WeaponDefinition[] loadout = Array.Empty<WeaponDefinition>();
        [Tooltip("Mod HUD prefabı (Modes/<Mod>/UI/); maç sahnesine App tarafından eklenir.")]
        [SerializeField] private GameObject hudPrefab;

        /// <summary>Protokol anahtarı ("tdm").</summary>
        public string ModeId => modeId;

        /// <summary>Arayüzde gösterilen ad.</summary>
        public string DisplayName => displayName;

        /// <summary>Varsayılan raund süresi (saniye).</summary>
        public int RoundSeconds => roundSeconds;

        /// <summary>Varsayılan skor limiti.</summary>
        public int ScoreLimit => scoreLimit;

        // ---- Mod şekli (§10.5) — YALNIZ ÖNİZLEME/EDİTÖR ----
        // Sunucusuz editör oturumunda (dev penceresinin sentetik maçı) ModeRuntime bunları okur;
        // gerçek bir load_match geldiği anda sunucunun değerleri bunları EZER. Sözleşme
        // roundSeconds/scoreLimit ile aynıdır: buradaki sayılar arayüz içindir, otorite değil.

        /// <summary>Önizleme: takım kipi.</summary>
        public ModeTeamMode TeamMode => teamMode;

        /// <summary>Önizleme: skor kanalı.</summary>
        public ModeScoreKind Scoring => scoring;

        /// <summary>Önizleme: dost ateşi açık mı.</summary>
        public bool FriendlyFire => friendlyFire;

        /// <summary>Önizleme: canlanma şartı.</summary>
        public ModeReviveAnchor Revive => revive;

        /// <summary>Önizleme: silah kaynağı.</summary>
        public ModeWeaponSource Weapons => weapons;

        /// <summary>Önizleme: canlanma gecikmesi (sn). <b><c>0</c> geçerlidir</b> (anında canlanma);
        /// alan hiç girilmemiş asset'lerde C# başlangıcı (<c>RESPAWN_DELAY</c>) geçerli olur.</summary>
        public float RespawnDelay => Mathf.Max(0f, respawnDelay);

        /// <summary>Modun oynanabildiği haritalar (boş = katalogdaki tüm uyumlu haritalar).</summary>
        public MapDefinition[] Maps => maps;

        /// <summary>Modun silah seti.</summary>
        public WeaponDefinition[] Loadout => loadout;

        /// <summary>Mod HUD prefabı (atanmamış olabilir).</summary>
        public GameObject HudPrefab => hudPrefab;
    }
}
