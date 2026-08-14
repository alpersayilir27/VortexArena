using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Audio
{
    /// <summary>
    /// Haritadan bağımsız duyuru/geri bildirim seslerini çalan <b>tek</b> yer ("rakip elendi",
    /// "öldün", maç başladı/bitti). Klipler <see cref="GameSoundBank"/>'ten gelir; hangi olayın
    /// hangi sesi tetiklediği burada durur, sahnede ya da HUD'da değil.
    /// <para>
    /// <b>Kendini önyükleyen kalıcı tekildir; sahneye bileşen KONMAZ</b> — sesin çalması için
    /// arena kurulumunda bir adım olsaydı yeni her arena onu unutabilirdi.
    /// </para>
    /// <para>
    /// Ses <b>2D</b>'dir: duyuru oyuncunun kafasının içinde duyulur, arenada bir yeri yoktur.
    /// </para>
    /// <para>
    /// Oyuncuya özel sesler (öldürme/ölüm/canlanma) yerel oyuncu kimliği çözülemiyorsa —
    /// admin gözlemci ya da henüz bağlanmamış istemci — sessizce atlanır; faz sesleri
    /// (maç başladı, geri sayım) operatörde de çalar. <b>Maç sonucu duyurusu oyuncuya özel
    /// DEĞİLDİR</b> ("kırmızı takım kazandı"): herkeste aynı çalar, admin dahil.
    /// </para>
    /// <para>
    /// Yeni bir ses eklemek: <see cref="GameSoundId"/>'ye SONA bir değer +
    /// <see cref="GameSoundBank"/>'e bir alan + tetikleyen yerde <see cref="Play"/>.
    /// </para>
    /// <para>
    /// <b>Moda/haritaya göre değişen</b> sesler bankadan DEĞİL
    /// <see cref="ModeAudioRegistry"/>'den gelir ve <see cref="PlayModeEvent"/> ile çalar.
    /// Aynı an için ikisi de doluysa <b>kayıt bankayı ezer</b> — sesler üst üste binmesin.
    /// </para>
    /// <para>
    /// <b>Duyurular tek bir kanaldan SIRAYLA çalar</b> (bankadan da kayıttan da gelseler): bunlar
    /// konuşma replikleridir, üst üste binen ikisi birden anlaşılmaz olur. Kanal doluyken gelen
    /// duyuru öncekini KESMEZ, kuyruğa girer ve sırası gelince çalar — bkz. <see cref="Announce"/>.
    /// İstisna <see cref="IsInstant"/>'tır: anlamı zamanlamasında olan işaretler (geri sayım
    /// bip'i) beklemez.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class GameAudio : MonoBehaviour
    {
        /// <summary>
        /// Kuyrukta en fazla kaç duyuru bekleyebilir; taşarsa <b>yeni gelen düşer</b>.
        /// <para>Eski olanı atmak yanlış olurdu: sıradaki duyuru zaten olmuş bir olayı anlatıyor
        /// (öldürme, tur bitişi) ve onu atlamak oyuncuyu bilgisiz bırakır. Tavanın kendisi bir
        /// emniyettir — normal maçta kuyruk ikiyi geçmez.</para>
        /// </summary>
        private const int AnnouncementQueueLimit = 4;

        /// <summary>
        /// Bir duyurunun kuyrukta bekleyebileceği en uzun süre (sn); geçerse <b>hiç çalmaz</b>.
        /// <para>Gerekçe: geç çalan duyuru yanlış duyurudur — "rakip elendi" repliğini turlar arası
        /// toplanmanın ortasında duymak oyuncuyu o an olan bir şey sanmaya iter.</para>
        /// </summary>
        private const float AnnouncementTtlSeconds = 4f;

        /// <summary>İki replik arasındaki nefes payı (sn): bitişik çalan iki cümle tek cümle gibi
        /// duyuluyor.</summary>
        private const float AnnouncementGapSeconds = 0.1f;

        /// <summary>Sırasını bekleyen tek bir duyuru: klibi, seviyesi ve <b>bayatlama anı</b>.
        /// <para>Seviye kuyruğa girerken hesaplanır (o anki <see cref="MasterVolume"/> ile): duyuru
        /// olayın anına aittir, sırada beklerken operatörün çevirdiği bir düğmeye değil.</para>
        /// </summary>
        private readonly struct PendingAnnouncement
        {
            public readonly AudioClip Clip;
            public readonly float Volume;
            public readonly float ExpiresAt;

            public PendingAnnouncement(AudioClip clip, float volume, float expiresAt)
            {
                Clip = clip;
                Volume = volume;
                ExpiresAt = expiresAt;
            }
        }

        public static GameAudio Instance { get; private set; }

        private AudioSource _source;
        private float _masterVolume = 1f;

        /// <summary>
        /// Sırasını bekleyen duyurular (FIFO). <b>Sıra = geliş sırasıdır</b> ve doğrusu budur:
        /// sunucu olayları zaten nedensel sırada yolluyor (önce <c>kill_event</c>, sonra o ölümün
        /// bitirdiği turun <c>match_state</c>'i), WS sırayı koruyor. Öncelik tablosu yazmak bu
        /// sırayı ikinci bir yerden tekrar tarif etmek olurdu.
        /// </summary>
        private readonly Queue<PendingAnnouncement> _announcements = new Queue<PendingAnnouncement>();

        /// <summary>Duyuru kanalının boşalacağı an (<see cref="Time.unscaledTime"/>).
        /// <para>Kanalın meşguliyeti <c>AudioSource.isPlaying</c>'den DEĞİL klip uzunluğundan
        /// ölçülür: aynı kaynakta anlık işaretler de (bip) çalıyor ve <c>isPlaying</c> onları da
        /// sayardı — o zaman her bip sıradaki repliği geciktirirdi.</para></summary>
        private float _channelFreeAt;

        /// <summary>Son bilinen faz — <c>match_state</c> tekrar tekrar geldiği için sesin yalnız
        /// GEÇİŞTE çalması buna bağlı.</summary>
        private string _lastPhase = "";

        /// <summary>Bir önceki <c>match_state</c>'in kalan süresi; <c>-1</c> = henüz örnek yok.
        /// Uyarı sesi eşiğin GEÇİLDİĞİ örnekte çalsın diye tutulur.</summary>
        private float _lastTimeRemaining = -1f;

        /// <summary>Süre uyarısı bu tur/maç için çaldı mı — her <c>playing</c> geçişinde sıfırlanır.</summary>
        private bool _warningFired;

        /// <summary>
        /// Son roster (§5.3) — <b>tek tüketicisi öldürülen oyuncunun takımıdır</b>
        /// (<see cref="IsTeammate"/>). <c>kill_event</c> takım taşımaz ve taşımayacak: takım zaten
        /// <c>lobby_state</c> ile geliyor, ikinci bir kanal ikinci bir doğruluk kaynağı olurdu.
        /// <para>⚠️ Roster her değişimde tam olarak yeniden yayınlanıyor (takım değişimi dahil),
        /// yani burada tutulan kopya bayatlamaz — bir sonraki <c>set_team</c> onu tazeler.</para>
        /// </summary>
        private LobbyStateMsg _roster;

        /// <summary>Tüm duyuru seslerinin ortak çarpanı (0..1).</summary>
        public float MasterVolume
        {
            get => _masterVolume;
            set => _masterVolume = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Sesi çalar. Tekil henüz yoksa ya da klip atanmamışsa sessizce hiçbir şey olmaz —
        /// çağıran tarafın kontrol yazması gerekmez.
        /// </summary>
        public static void Play(GameSoundId id, float volumeScale = 1f)
        {
            if (Instance != null)
            {
                Instance.PlayInternal(id, volumeScale);
            }
        }

        /// <summary>
        /// Moda/haritaya özel sesi çalar: <see cref="ModeAudioRegistry"/>'de aktif mod + aktif
        /// sahne için o tetikleyiciye yazılmış kural varsa kliplerinden biri rastgele seçilir.
        /// <para>Dönüş <c>false</c> = o an için kural/klip yok (çağıran isterse ortak bankaya
        /// düşebilir). Tekil henüz yoksa da <c>false</c> döner.</para>
        /// </summary>
        public static bool PlayModeEvent(ModeAudioEvent trigger)
        {
            return Instance != null &&
                   TryResolve(trigger, out ModeAudioRegistry.Rule rule) &&
                   Instance.PlayRule(rule);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[GameAudio]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<GameAudio>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.spatialize = false;

            // Kalıcı tekiliz: obje devre dışı bırakılsa bile olay kaçmasın diye Awake/OnDestroy'da
            // abone oluruz (PlayerCombatState deseni).
            NetEvents.OnKillEvent += HandleKillEvent;
            NetEvents.OnRespawn += HandleRespawn;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnCountdown += HandleCountdown;
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnDisconnected += HandleDisconnected;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnKillEvent -= HandleKillEvent;
            NetEvents.OnRespawn -= HandleRespawn;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnCountdown -= HandleCountdown;
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnDisconnected -= HandleDisconnected;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;

            Instance = null;
        }

        private void PlayInternal(GameSoundId id, float volumeScale)
        {
            GameSoundBank bank = GameSoundBank.Load();
            if (bank == null || _source == null)
            {
                return;
            }

            AudioClip clip = bank.Clip(id);
            if (clip == null)
            {
                return;
            }

            float volume = Mathf.Clamp01(bank.Volume * _masterVolume * Mathf.Max(0f, volumeScale));

            if (IsInstant(id))
            {
                _source.PlayOneShot(clip, volume);
                return;
            }

            Announce(clip, volume);
        }

        /// <summary>
        /// Bu ses bir duyuru (replik) DEĞİL, <b>anlamı zamanlamasında olan anlık bir işaret</b> mi?
        /// Anlık olanlar kuyruğa girmez, sıra beklemez ve bekleyeni geciktirmez.
        /// <para>
        /// ⚠️ <b>Varsayılan "duyuru"dur</b> (<c>false</c>): <see cref="GameSoundId"/>'ye sona
        /// eklenen yeni bir ses kendiliğinden kuyruğa girer. Tersi olsaydı yeni her replik, o gün
        /// kimse fark etmeden bir öncekini keserdi.
        /// </para>
        /// <para>
        /// Ölçüt "kısa mı" değil <b>"geç çalarsa yalan olur mu"</b>dur: geri sayımın bip'i saniyenin
        /// kendisidir, bir replik bitene kadar beklerse yanlış saniyeyi işaretler. İhlal uyarısı da
        /// operatörün <i>şu anda</i> bakması gereken yeri söyler.
        /// </para>
        /// </summary>
        private static bool IsInstant(GameSoundId id)
        {
            return id == GameSoundId.CountdownTick || id == GameSoundId.AdminViolation;
        }

        /// <summary>
        /// Duyuruyu kanala verir: kanal boşsa hemen çalar, doluysa <b>kuyruğa girer</b> —
        /// çalmakta olanı ASLA kesmez.
        /// <para>
        /// ⚠️ <b>Kesmemek bilinçli bir karardır:</b> duyurular nedensel bir zincir anlatıyor
        /// ("rakip elendi" → "tur sona erdi, mevzilerinize dönün") ve zincirin ilk halkasını kesmek
        /// oyuncuya <i>neden</i> turun bittiğini hiç söylememek demek. Son duyuruyu kazandıran eski
        /// kural 1v1'de tam bunu yapıyordu: sunucu ölümü işledikten ~100 ms sonra turu kapatıyor,
        /// öldürme repliği daha ilk hecesindeyken kesiliyordu.
        /// </para>
        /// <para>
        /// Dönüş <c>false</c> = ses hiç çalmayacak (klip yok ya da kuyruk taştı); çağıran isterse
        /// yedeğine düşebilir.
        /// </para>
        /// </summary>
        private bool Announce(AudioClip clip, float volume)
        {
            if (clip == null || _source == null)
            {
                return false;
            }

            float now = Time.unscaledTime;
            if (_announcements.Count == 0 && now >= _channelFreeAt)
            {
                StartAnnouncement(clip, volume, now);
                return true;
            }

            if (_announcements.Count >= AnnouncementQueueLimit)
            {
                return false;
            }

            _announcements.Enqueue(new PendingAnnouncement(clip, volume, now + AnnouncementTtlSeconds));
            return true;
        }

        /// <summary>Duyuruyu başlatır ve kanalı klip boyunca (+ nefes payı) meşgul işaretler.</summary>
        private void StartAnnouncement(AudioClip clip, float volume, float now)
        {
            _source.PlayOneShot(clip, volume);
            _channelFreeAt = now + clip.length + AnnouncementGapSeconds;
        }

        /// <summary>
        /// Kuyruk pompası: kanal boşaldığında sıradaki repliği başlatır, bayatlamışları atar.
        /// <para>⚠️ Kuyruk yalnız burada ilerler — obje devre dışı bırakılırsa duyurular durur
        /// (bugün kimse bırakmıyor: tekil kendi kalıcı objesini kuruyor).</para>
        /// </summary>
        private void Update()
        {
            if (_announcements.Count == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < _channelFreeAt)
            {
                return;
            }

            while (_announcements.Count > 0)
            {
                PendingAnnouncement next = _announcements.Dequeue();
                if (next.Clip == null || now > next.ExpiresAt)
                {
                    continue;
                }

                StartAnnouncement(next.Clip, next.Volume, now);
                return;
            }
        }

        /// <summary>
        /// Kuyruğu boşaltır ve çalmakta olan repliği susturur — <b>oturum bittiğinde</b>.
        /// <para>Kesmenin meşru olduğu tek yer burasıdır: bağlantı kopunca sıradaki duyuru artık
        /// var olmayan bir maçı anlatıyor.</para>
        /// </summary>
        private void ClearAnnouncements()
        {
            _announcements.Clear();
            _channelFreeAt = 0f;

            if (_source != null)
            {
                _source.Stop();
            }
        }

        private void HandleKillEvent(KillEventMsg msg)
        {
            int local = ArenaCombat.LocalPlayerId;
            if (msg == null || local <= 0)
            {
                return;
            }

            if (msg.victimId == local)
            {
                Play(GameSoundId.LocalDeath);
                return;
            }

            // killerId == victimId: intihar/çevresel ölüm (§10.9) — öldüren yoktur, duyuru da yok.
            if (msg.killerId == local && msg.killerId != msg.victimId)
            {
                Play(IsTeammate(msg.victimId)
                    ? GameSoundId.TeammateEliminated
                    : GameSoundId.EnemyEliminated);
            }
        }

        /// <summary>
        /// Öldürülen oyuncu yerel oyuncunun TAKIM ARKADAŞI mı — dost ateşi açıkken (§10.5
        /// <c>set_friendly_fire</c>) duyurunun hangisi olacağını bu belirler.
        /// <para>
        /// ⚠️ Soru <b>"dost ateşi açık mı"</b> DEĞİLDİR: dost ateşi kapalıyken sunucu takımdaş
        /// hasarını zaten yazmaz, yani böyle bir <c>kill_event</c> hiç doğmaz. Kapıyı
        /// <see cref="ModeRuntime.FriendlyFire"/>'a bağlamak, operatör anahtarı ile olayın geliş
        /// anı arasındaki her sapmada duyuruyu yanlış tarafa düşürürdü.
        /// </para>
        /// <para>
        /// ⚠️ <b>Bilinmiyorsa "rakip" denir</b> (takımsız mod, yerel takım henüz
        /// <see cref="Team.Neutral"/>, kurban roster'da yok): olmayan bir dost ateşini duyurmak,
        /// gerçek bir öldürmeyi sessiz bırakmaktan daha yanıltıcıdır.
        /// </para>
        /// </summary>
        private bool IsTeammate(int victimId)
        {
            if (ModeRuntime.IsTeamless)
            {
                return false;
            }

            Team local = ArenaCombat.LocalTeam;
            return local != Team.Neutral && RosterTeam(victimId) == local;
        }

        /// <summary>
        /// Roster'daki oyuncunun takımı; kayıt yoksa <see cref="Team.Neutral"/>.
        /// <para>Protokoldeki <c>"red"</c>/<c>"blue"</c> dışındaki her değer (boş dahil)
        /// <see cref="Team.Neutral"/>'dır — takımsız modda sunucu takımları TEMİZLER (§10.5).</para>
        /// </summary>
        private Team RosterTeam(int playerId)
        {
            if (_roster?.players == null)
            {
                return Team.Neutral;
            }

            for (int i = 0; i < _roster.players.Length; i++)
            {
                PlayerInfo info = _roster.players[i];
                if (info == null || info.playerId != playerId)
                {
                    continue;
                }

                if (string.Equals(info.team, "red", StringComparison.OrdinalIgnoreCase))
                {
                    return Team.Red;
                }

                return string.Equals(info.team, "blue", StringComparison.OrdinalIgnoreCase)
                    ? Team.Blue
                    : Team.Neutral;
            }

            return Team.Neutral;
        }

        private void HandleLobbyState(LobbyStateMsg msg)
        {
            if (msg != null && msg.players != null)
            {
                _roster = msg;
            }
        }

        /// <summary>Kopuşta roster düşer: yeni oturumda oyuncu kimlikleri baştan dağıtılır ve
        /// bayat bir kopya kurbanı yanlış takımda gösterirdi. Bekleyen duyurular da düşer —
        /// artık var olmayan bir maçı anlatıyorlar.</summary>
        private void HandleDisconnected()
        {
            _roster = null;
            ClearAnnouncements();
        }

        /// <summary>
        /// Yeni bir maç kuruldu (operatör <c>start_match</c>): önceki maçın duyuruları düşer.
        /// <para>
        /// ⚠️ <b>Koşan bir maçın üstüne basılan "başlat" da buradan geçer</b> ve asıl gerekçe odur:
        /// biten turun repliği ("tur sona erdi, mevzilerinize dönün") sırada beklerken ya da
        /// çalarken operatör maçı yeniden kurabiliyor — o replik artık var olmayan bir turu
        /// anlatır. Faz kapısı (<see cref="IsRoundEnd"/>) bunu yakalayamaz: ses zaten <b>daha
        /// önce</b>, turun gerçekten bittiği anda doğmuştur.
        /// </para>
        /// <para>
        /// Faz geçmişi de sıfırlanır: yeni maçın ilk <c>match_state</c>'i her zaman bir duraklama
        /// olduğu için (<c>loading</c>) bu, maç başlangıcı sesini geciktirmez — yalnız iki AYRI
        /// maçın fazları arasında bir geçiş okunmasını yapısal olarak imkânsız kılar.
        /// </para>
        /// </summary>
        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            ResetForNewMatch();
        }

        /// <summary>Lobiye dönüldü (§10.7) — maç yok, bekleyen duyurunun anlatacağı bir şey de yok.</summary>
        private void HandleReturnToLobby(ReturnToLobbyMsg msg)
        {
            ResetForNewMatch();
        }

        /// <summary>Duyuru kanalını ve faz geçmişini yeni bir maça hazırlar.</summary>
        private void ResetForNewMatch()
        {
            ClearAnnouncements();
            _lastPhase = "";
            _lastTimeRemaining = -1f;
            _warningFired = false;
        }

        private void HandleRespawn(RespawnMsg msg)
        {
            int local = ArenaCombat.LocalPlayerId;
            if (msg != null && local > 0 && msg.playerId == local)
            {
                Play(GameSoundId.LocalRespawn);
            }
        }

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            string phase = msg.phase ?? "";
            bool playing = string.Equals(phase, ArenaProtocol.PHASE_PLAYING, StringComparison.Ordinal);

            if (!string.Equals(phase, _lastPhase, StringComparison.Ordinal))
            {
                // ⚠️ İlk mesajda (_lastPhase boş) ses çalınmaz: koşan bir maça sonradan bağlanan
                // başlık "maç başladı" duymamalı.
                bool started = _lastPhase.Length > 0 && playing;
                bool roundEnded = IsRoundEnd(_lastPhase, phase, msg.phaseReason);
                _lastPhase = phase;

                if (started)
                {
                    // Tur tabanlı modda her tur buradan geçer → uyarı her tur yeniden kurulur.
                    _warningFired = false;
                    _lastTimeRemaining = -1f;

                    // Moda/haritaya özel giriş sesi ortak bankayı EZER (üst üste binmesin).
                    if (!PlayModeEvent(ModeAudioEvent.RoundStart))
                    {
                        Play(GameSoundId.MatchStart);
                    }
                }
                else if (roundEnded)
                {
                    // Ortak bankada karşılığı YOKTUR: tur bitişi tur tabanlı modlara özgüdür,
                    // kuralı olmayan modda sessiz kalması doğrudur.
                    PlayModeEvent(ModeAudioEvent.RoundEnd);
                }
            }

            if (playing)
            {
                TickTimeWarning(msg.timeRemaining);
            }
        }

        /// <summary>
        /// Bu geçiş bir tur bitişi mi: <c>playing</c> → <c>paused</c> + <c>phaseReason == "mode"</c>.
        /// <para>
        /// ⚠️ <b>Ölçüt <c>modeId</c> DEĞİL fazdır</b> — istemcide <c>if (modeId == "tournament")</c>
        /// zinciri yazılmaz (§10.5). "Mod duraklatma istedi" çekirdeğin tek tur-arası sinyalidir;
        /// hangi modun bunu kullandığını <see cref="ModeAudioRegistry"/>'deki kural söyler, kod değil.
        /// </para>
        /// <para>
        /// ⚠️ <c>modeState</c> <b>ayrıştırılmaz</b> (<c>"regroup:2/6"</c>): serbest bir stringdir ve
        /// çekirdek onu yorumlamaz (§10.1) — modun yazdığı metni değiştirmesi sesi susturmamalı.
        /// </para>
        /// <para>
        /// ⚠️ <b>Gerekçeyi aramanın asıl sebebi operatördür:</b> duraklamanın tek kaynağı mod
        /// değil. Koşan maçın üstüne <c>start_match</c> (gerekçe <c>loading</c>), elle duraklatma
        /// (<c>operator</c>), <c>abort_match</c>/<c>return_to_lobby</c> (<c>lobby</c>) — üçü de
        /// <c>playing</c> → <c>paused</c> geçişidir ve hiçbirinde tur DOĞAL yoldan bitmemiştir.
        /// Yalnız faza bakan bir kapı üçünde de "tur sona erdi" derdi.
        /// </para>
        /// <para>
        /// İlk <c>match_state</c>'te (<paramref name="previousPhase"/> boş) tetiklenmez: turlar
        /// arasında bağlanan başlık kaçırdığı turun duyurusunu duymamalı.
        /// </para>
        /// </summary>
        private static bool IsRoundEnd(string previousPhase, string phase, string phaseReason)
        {
            return string.Equals(previousPhase, ArenaProtocol.PHASE_PLAYING, StringComparison.Ordinal) &&
                   string.Equals(phase, ArenaProtocol.PHASE_PAUSED, StringComparison.Ordinal) &&
                   string.Equals(phaseReason, ArenaProtocol.PAUSE_REASON_MODE, StringComparison.Ordinal);
        }

        /// <summary>
        /// Kalan süre uyarı eşiğini geçtiğinde sesi bir kez çalar. Süre <b>sunucu otoritesidir</b>
        /// ve <c>match_state</c> ile 1 Hz gelir; burada yalnız okunur, istemcide sayaç işletilmez.
        /// </summary>
        private void TickTimeWarning(float timeRemaining)
        {
            float previous = _lastTimeRemaining;
            _lastTimeRemaining = timeRemaining;

            // İlk örnekte eşik "geçilmiş" sayılmaz: son saniyelerinde bir maça bağlanan başlık
            // durduk yere "son 5 saniye" duymamalı.
            if (_warningFired || previous < 0f)
            {
                return;
            }

            if (!TryResolveWarning(out ModeAudioRegistry.Rule rule))
            {
                return;
            }

            // 1 Hz örneklemede eşik saniyesi ~N.0 olarak gelir; yarım saniyelik pay o örneği
            // kaçırıp uyarıyı bir saniye geç çalmamak içindir.
            float threshold = rule.WarningSeconds + 0.5f;
            if (previous > threshold && timeRemaining <= threshold)
            {
                _warningFired = true;
                PlayRule(rule);
            }
        }

        /// <summary>
        /// Süre uyarısının kuralı: önce tur, sonra maç. <b>Modun tur tabanlı olup olmadığını
        /// KAYIT söyler</b>, <c>modeState</c> değil — modun ara durumunu çekirdek yorumlamaz
        /// (Docs/ArenaNet-Protokol.md §10.1).
        /// </summary>
        private static bool TryResolveWarning(out ModeAudioRegistry.Rule rule)
        {
            return TryResolve(ModeAudioEvent.RoundEndWarning, out rule) ||
                   TryResolve(ModeAudioEvent.MatchEndWarning, out rule);
        }

        /// <summary>Aktif mod + aktif sahne için kuralı çözer.</summary>
        private static bool TryResolve(ModeAudioEvent trigger, out ModeAudioRegistry.Rule rule)
        {
            ModeAudioRegistry registry = ModeAudioRegistry.Load();
            if (registry == null)
            {
                rule = null;
                return false;
            }

            return registry.TryResolve(trigger, ModeRuntime.ModeId,
                SceneManager.GetActiveScene().name, out rule);
        }

        /// <summary>
        /// Kuralın kliplerinden birini duyuru kanalına verir; klip yoksa (ya da kuyruk taştıysa)
        /// <c>false</c>.
        /// <para>⚠️ Kayıttan gelen duyurunun bankadan gelene karşı bir <b>ayrıcalığı yoktur</b>:
        /// ikisi de aynı kuyruğa, geliş sırasıyla girer (<see cref="Announce"/>). "Kayıt bankayı
        /// ezer" kuralı bir SEÇİM kuralıdır — aynı AN için iki klip varsa hangisinin çalacağını
        /// söyler, çalmakta olanı kesme yetkisi değildir.</para>
        /// <para>Dönüş <c>true</c> = "çalacak", ille de "şu an çalıyor" değil: sırası gelince
        /// çalar. Çağıranın yedeğe düşme kararı için aradaki fark önemsizdir, ikisi de sesin
        /// duyulacağı anlamına gelir.</para>
        /// </summary>
        private bool PlayRule(ModeAudioRegistry.Rule rule)
        {
            AudioClip clip = rule != null ? rule.PickClip() : null;
            return clip != null && Announce(clip, Mathf.Clamp01(rule.Volume * _masterVolume));
        }

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            _lastPhase = ArenaProtocol.PHASE_FINISHED;

            // ⚠️ Sonuç duyurusu oyuncuya DEĞİL maça aittir ("kırmızı takım kazandı"), bu yüzden
            // dinleyene göre değişmez ve yerel oyuncu kimliği aranmaz: admin gözlemcide de çalar.
            if (!string.IsNullOrEmpty(msg.winnerTeam))
            {
                if (string.Equals(msg.winnerTeam, "red", StringComparison.OrdinalIgnoreCase))
                {
                    Play(GameSoundId.TeamRedWon);
                }
                else if (string.Equals(msg.winnerTeam, "blue", StringComparison.OrdinalIgnoreCase))
                {
                    Play(GameSoundId.TeamBlueWon);
                }

                return;
            }

            if (msg.winnerPlayerId > 0)
            {
                // Bireysel skorlu mod (ffa): kazanan bir OYUNCU'dur, takım duyurusunun karşılığı
                // yok — o modun sonuç sesi maç sonu ekranından okunur.
                return;
            }

            Play(GameSoundId.MatchDraw);
        }

        private void HandleCountdown(CountdownMsg msg)
        {
            if (msg != null && msg.seconds > 0)
            {
                Play(GameSoundId.CountdownTick);
            }
        }
    }
}
