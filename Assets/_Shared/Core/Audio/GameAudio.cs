using System;
using UnityEngine;
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
            if (string.Equals(phase, _lastPhase, StringComparison.Ordinal))
            {
                return;
            }

            // ⚠️ İlk mesajda (_lastPhase boş) ses çalınmaz: koşan bir maça sonradan bağlanan
            // başlık "maç başladı" duymamalı.
            bool started = _lastPhase.Length > 0 &&
                           string.Equals(phase, ArenaProtocol.PHASE_PLAYING, StringComparison.Ordinal);

            _lastPhase = phase;

            if (started)
            {
                Play(GameSoundId.MatchStart);
            }
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
