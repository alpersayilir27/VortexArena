using System;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Protocol;

namespace VortexArena.Core
{
    /// <summary>
    /// Game mode definition: modId + default rule parameters + compatible maps.
    /// <para>
    /// <see cref="ModeId"/> is the protocol key ("tdm") — admin sends <c>start_match{modeId}</c>
    /// and the server matches it against its own <c>IGameMode</c> registrations. RULE AUTHORITY
    /// LIVES ON THE SERVER; roundSeconds/scoreLimit here are UI/preview values only.
    /// </para>
    /// <see cref="HudPrefab"/> is the mode UI prefab (under Modes/&lt;Mode&gt;/UI/); Core does not
    /// reference mode assemblies, which is why the field type is a plain <c>GameObject</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "Mode", menuName = "VortexArena/Mode Definition")]
    public class ModeDefinition : ScriptableObject
    {
        [Header("Kimlik")]
        [Tooltip("Protokol anahtarı — sunucudaki IGameMode.ModeId ile birebir aynı.")]
        [SerializeField] private string modeId = "";
        [SerializeField] private string displayName = "";
        [Tooltip("Oyun tipi — operatörün ilk seçimi. Bugünkü tüm arenalar Hızlı Savaş'tır.")]
        // Default QuickBattle: existing assets resolve correctly WITHOUT being touched.
        [SerializeField] private GameType gameType = GameType.QuickBattle;
        [Tooltip("Bu tanım bir MAÇ modu değil, LOBİ profilidir (§10.7): sunucuda IGameMode " +
                 "karşılığı yoktur, start_match ile başlatılamaz. Yalnız lobide silah loadout'u " +
                 "ve HUD çözmek için katalogda durur; admin mod seçicisinde gösterilmez.")]
        [SerializeField] private bool lobbyProfile;

        [Header("Varsayılan kurallar (otorite sunucudadır)")]
        [SerializeField] private int roundSeconds = 300;
        [Tooltip("Varsayılan skor limiti; -1 = sınırsız. 0 = modun skor limiti YOK (sunucudaki mod " +
                 "limiti okumaz, yalnız süre biter) — admin'de satır kilitlenir, limit gönderilmez.")]
        [SerializeField] private int scoreLimit = 120;

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
        [SerializeField] private ModeWeaponSource weapons = ModeWeaponSource.WeaponCanvas;
        [Tooltip("Ölüm → en erken canlanma süresi (sn). 0 GEÇERLİDİR = anında canlanma; " +
                 "varsayılan protokoldeki RESPAWN_DELAY'dir.")]
        [SerializeField] private float respawnDelay = ArenaProtocol.RESPAWN_DELAY;
        [Tooltip("YALNIZ takım kipi 'None' iken anlamlıdır: açıksa takımsız oyuncular TEK ortak " +
                 "taraftır (ortak oyun), kapalıysa herkes tek başınadır (FFA). Gerçek maçta " +
                 "load_match.rules.allies kazanır.")]
        [SerializeField] private bool allies;
        [Tooltip("Açıksa yedek mermi silahın yedek şarjör sayısıyla SINIRLIDIR; kapalıysa sonsuzdur " +
                 "(gösterge 30/∞). Gerçek maçta load_match.rules.limitedReserve kazanır.")]
        [SerializeField] private bool limitedReserve;

        [Header("İçerik")]
        [Tooltip("Bu modun oynanabildiği haritalar; boş bırakılırsa katalogdaki tüm uyumlu haritalar.")]
        [SerializeField] private MapDefinition[] maps = Array.Empty<MapDefinition>();
        [Tooltip("Modun silah seti.")]
        [SerializeField] private WeaponDefinition[] loadout = Array.Empty<WeaponDefinition>();
        [Tooltip("Mod HUD prefabı (Modes/<Mod>/UI/); maç sahnesine App tarafından eklenir.")]
        [SerializeField] private GameObject hudPrefab;
        [Tooltip("Moda özel maç sonu ekranı — MatchResultOverlay.prefab'ın VARYANTI olmalı " +
                 "(Modes/<Mod>/UI/). Boşsa genel ekran çizilir.")]
        [SerializeField] private GameObject resultScreenPrefab;

        [Header("Oyuncu gövdesi (opsiyonel)")]
        [Tooltip("Mavi takım ve takımsız oyuncunun gövdesi; boşsa varsayılan gövde (Ch15) çizilir — " +
                 "TAKIMSIZ modda boş bırakılırsa varsayılan iki gövde (Ch15/Ch18) sahne tohumundan " +
                 "rastgele dağıtılır. Kök SkeletonPoseMirror taşıyan, Tools > VortexArena > Avatars > " +
                 "Mod Gövdesi Kur (seçili FBX) ile kurulmuş prefab olmalı.")]
        [SerializeField] private GameObject bodyPrefab;
        [Tooltip("Kırmızı takımın gövdesi; boşsa varsayılan kırmızı gövde (Ch18) çizilir. " +
                 "⚠️ İki takımın gövdesi belirgin biçimde farklı model olmalı — takım modelden okunur.")]
        [SerializeField] private GameObject redBodyPrefab;
        [Tooltip("Takımsız modda ikinci gövde. Doluysa oyuncular kadro sırasına göre bodyPrefab ile " +
                 "bu gövde arasında dönüşümlü dağıtılır (eşit sayı). Takımlı modda kullanılmaz.")]
        [SerializeField] private GameObject altBodyPrefab;

        /// <summary>Protocol key ("tdm").</summary>
        public string ModeId => modeId;

        /// <summary>Name shown in the UI.</summary>
        public string DisplayName => displayName;

        /// <summary>Game type the round type belongs to (§11).</summary>
        public GameType GameType => gameType;

        /// <summary>
        /// Is this a lobby profile (§10.7)? When <c>true</c> this definition is <b>not a startable
        /// mode</b>: it has no <c>IGameMode</c> counterpart on the server and <c>start_match</c> rejects it.
        /// <para>The single reason it exists in the catalog: the lobby's weapon loadout (and HUD, if any)
        /// is resolved via <c>GameCatalog.FindMode(ModeRuntime.ModeId)</c>. The admin UI looks at this
        /// flag to hide the mode from the picker — so the operator is not shown a button that would be
        /// silently rejected on every press.</para>
        /// </summary>
        public bool IsLobbyProfile => lobbyProfile;

        /// <summary>Default round duration (seconds).</summary>
        public int RoundSeconds => roundSeconds;

        /// <summary>Default score limit.</summary>
        public int ScoreLimit => scoreLimit;

        /// <summary>Does the server's mode read a score limit at all? <c>scoreLimit == 0</c> = time-only
        /// mode; the admin locks the limit row instead of offering a knob that does nothing.</summary>
        public bool HasScoreLimit => scoreLimit != 0;

        // ---- Mode shape (§10.5) — PREVIEW/EDITOR ONLY ----
        // In a serverless editor session (the dev window's synthetic match) ModeRuntime reads these;
        // the moment a real load_match arrives the server's values OVERWRITE them. The contract is the
        // same as for roundSeconds/scoreLimit: the numbers here are for the UI, not authority.

        /// <summary>Preview: team mode.</summary>
        public ModeTeamMode TeamMode => teamMode;

        /// <summary>Preview: score channel.</summary>
        public ModeScoreKind Scoring => scoring;

        /// <summary>Preview: whether friendly fire is on.</summary>
        public bool FriendlyFire => friendlyFire;

        /// <summary>Preview: are the teamless players one shared side (§10.5 <c>allies</c>)?</summary>
        public bool Allies => allies;

        /// <summary>Preview: revive condition.</summary>
        public ModeReviveAnchor Revive => revive;

        /// <summary>Preview: weapon source.</summary>
        public ModeWeaponSource Weapons => weapons;

        /// <summary>Preview: is the spare ammo limited (§10.5 <c>limitedReserve</c>)?</summary>
        public bool LimitedReserve => limitedReserve;

        /// <summary>Preview: respawn delay (s). <b><c>0</c> is valid</b> (instant revive);
        /// on assets where the field was never entered the C# initializer (<c>RESPAWN_DELAY</c>) applies.</summary>
        public float RespawnDelay => Mathf.Max(0f, respawnDelay);

        /// <summary>Maps the mode can be played on (empty = every compatible map in the catalog).</summary>
        public MapDefinition[] Maps => maps;

        /// <summary>The mode's weapon set.</summary>
        public WeaponDefinition[] Loadout => loadout;

        /// <summary>Mode HUD prefab (may be unassigned).</summary>
        public GameObject HudPrefab => hudPrefab;

        /// <summary>Mode's own match-end screen, a <c>MatchResultOverlay</c> variant; null = generic
        /// screen. A <c>GameObject</c> for the same reason as <see cref="HudPrefab"/>: the overlay
        /// type lives in App, which Core does not reference.</summary>
        public GameObject ResultScreenPrefab => resultScreenPrefab;

        /// <summary>Body of the blue/teamless player; null = default body — in a TEAMLESS mode null
        /// also means the two default bodies are split by the scene's <c>bodySeed</c>
        /// (<c>RemotePlayerSpawner</c>). A <c>GameObject</c> for the same reason as
        /// <see cref="HudPrefab"/>.</summary>
        public GameObject BodyPrefab => bodyPrefab;

        /// <summary>Body of the red team; null = default red body (rationale in <see cref="BodyPrefab"/>).</summary>
        public GameObject RedBodyPrefab => redBodyPrefab;

        /// <summary>Second body of a TEAMLESS mode; null = every teamless player gets
        /// <see cref="BodyPrefab"/> (unless that is null too — see there). The roster order decides who
        /// gets which (see <c>RemotePlayerSpawner</c>), so the split is identical on every
        /// client.</summary>
        public GameObject AltBodyPrefab => altBodyPrefab;
    }
}
