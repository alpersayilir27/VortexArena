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
    /// Oyuncuya özel sesler (öldürme/ölüm/kazanma) yerel oyuncu kimliği çözülemiyorsa —
    /// admin gözlemci ya da henüz bağlanmamış istemci — sessizce atlanır; faz sesleri
    /// (maç başladı, geri sayım) operatörde de çalar.
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
                Play(GameSoundId.EnemyEliminated);
            }
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
            }

            if (playing)
            {
                TickTimeWarning(msg.timeRemaining);
            }
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

            int local = ArenaCombat.LocalPlayerId;
            if (local <= 0)
            {
                // Admin gözlemci: kazanan "kendisi" olmadığı için kaybetme sesi çalmamalı.
                return;
            }

            bool won;
            if (!string.IsNullOrEmpty(msg.winnerTeam))
            {
                won = string.Equals(msg.winnerTeam, TeamWire(ArenaCombat.LocalTeam),
                    StringComparison.OrdinalIgnoreCase);
            }
            else if (msg.winnerPlayerId > 0)
            {
                won = msg.winnerPlayerId == local;
            }
            else
            {
                // Berabere / kazanan yok → duyuru yok.
                return;
            }

            Play(won ? GameSoundId.MatchWin : GameSoundId.MatchLose);
        }

        private void HandleCountdown(CountdownMsg msg)
        {
            if (msg != null && msg.seconds > 0)
            {
                Play(GameSoundId.CountdownTick);
            }
        }

        /// <summary>Enum'u protokoldeki takım anahtarına çevirir; takımsız = boş string.</summary>
        private static string TeamWire(Team team)
        {
            switch (team)
            {
                case Team.Red: return "red";
                case Team.Blue: return "blue";
                default: return "";
            }
        }
    }
}
