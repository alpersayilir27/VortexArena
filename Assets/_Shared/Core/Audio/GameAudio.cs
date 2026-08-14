using System;
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
    /// </summary>
    [DisallowMultipleComponent]
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }

        private AudioSource _source;
        private float _masterVolume = 1f;

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

            _source.PlayOneShot(clip, Mathf.Clamp01(bank.Volume * _masterVolume * Mathf.Max(0f, volumeScale)));
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
        /// bayat bir kopya kurbanı yanlış takımda gösterirdi.</summary>
        private void HandleDisconnected()
        {
            _roster = null;
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
        /// Kuralın kliplerinden birini çalar; klip yoksa <c>false</c>.
        /// <para>⚠️ Çalmadan önce duyuru kanalı <b>susturulur</b>: bunlar konuşma replikleri ve
        /// üst üste binen iki replik ikisini birden anlaşılmaz kılar — son duyuru kazanır.
        /// (Bir istemcide aynı anda iki replik doğuramaz; bu, kuralı yapısal kılan bir emniyet.
        /// Aynı odada çalan İKİ İSTEMCİ'nin sesi buradan engellenemez.)</para>
        /// </summary>
        private bool PlayRule(ModeAudioRegistry.Rule rule)
        {
            AudioClip clip = rule != null ? rule.PickClip() : null;
            if (clip == null || _source == null)
            {
                return false;
            }

            _source.Stop();
            _source.PlayOneShot(clip, Mathf.Clamp01(rule.Volume * _masterVolume));
            return true;
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
