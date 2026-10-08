using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>
    /// Plays a <c>.vxr</c> match recording back through the LIVE parse paths (§12.4): a <c>Text</c>
    /// record goes to the same dispatcher a WS message does, a <c>Datagram</c> record to the same
    /// <see cref="UdpStateChannel"/> entry point the socket uses. Nothing is sent and nothing
    /// connects.
    /// <para><b>No file I/O and no threads here</b> — the source is a <c>byte[]</c> and the caller
    /// drives <see cref="Tick"/>. That is what keeps a web player possible later (plan §7).</para>
    /// <para>The playback clock is installed as the <see cref="NetClock"/> override, so every
    /// registry interpolates and ages samples on recording time instead of wall time.</para>
    /// </summary>
    public sealed class ReplayPlayer
    {
        /// <summary>Playback speed bounds; <c>Time.timeScale</c> carries them (phase 3 renders off
        /// the same clock).</summary>
        public const float MinSpeed = 0.25f;

        public const float MaxSpeed = 8f;

        /// <summary>Clock base so that <c>now - INTERP_DELAY_MS</c> stays positive at time 0 —
        /// ⚠️ a negative "now" makes the interpolation window land before every sample and the first
        /// seconds of the recording draw nothing.</summary>
        private const int ClockBaseMs = 10000;

        /// <summary>How much of the datagram stream before a seek target is still fed: the interp
        /// rings need recent samples, while older ones would only spam fire FX.</summary>
        private const int DatagramPrerollMs = 1000;

        private readonly List<ReplayRecord> _records = new List<ReplayRecord>();

        /// <summary>Replay posts IMMEDIATELY: we are already on the main thread and ordering against
        /// the datagram stream must be preserved.</summary>
        private readonly NetMessageDispatcher _dispatcher;

        private int _nextIndex;
        private float _speed = 1f;
        private bool _unknownTypeWarned;

        public ReplayPlayer()
        {
            _dispatcher = new NetMessageDispatcher(action => action());
        }

        /// <summary>Listing metadata (§12.3); null when the meta JSON was unreadable.</summary>
        public ReplayMeta Meta { get; private set; }

        public ReplayHeader Header { get; private set; }

        /// <summary>Recording length; for an unfinalized file the last complete record's time.</summary>
        public int DurationMs { get; private set; }

        /// <summary>Playback position.</summary>
        public int CurrentMs { get; private set; }

        public bool IsLoaded { get; private set; }

        /// <summary>Everything has been fed and the clock reached the end.</summary>
        public bool IsEnded { get; private set; }

        /// <summary>Turkish load error for the screen; empty when fine.</summary>
        public string Error { get; private set; } = "";

        public bool HasError => Error.Length > 0;

        /// <summary>Speed multiplier; the owner mirrors it into <c>Time.timeScale</c>.</summary>
        public float Speed
        {
            get => _speed;
            set => _speed = Mathf.Clamp(value, MinSpeed, MaxSpeed);
        }

        /// <summary>
        /// Holds the clock while something outside must catch up — today the arena scene load the
        /// first <c>load_match</c> starts. Without it the recording would keep streaming into a
        /// scene that is not there yet and the first seconds of the match would be lost.
        /// </summary>
        public Func<bool> HoldClock;

        public event Action OnLoaded;
        public event Action OnEnded;
        public event Action<string> OnError;

        // ------------------------------------------------------------------- load

        /// <summary>Parses header + meta + every record into memory; false leaves <see cref="Error"/>
        /// filled.</summary>
        public bool Load(byte[] bytes)
        {
            Unload();

            if (bytes == null || bytes.Length < ReplayFile.HEADER_SIZE)
            {
                return Fail("Kayıt dosyası okunamadı (boş ya da çok küçük).");
            }

            ReplayHeader header;
            string metaJson;

            using (var reader = new BinaryReader(new MemoryStream(bytes, false), Encoding.UTF8))
            {
                if (!ReplayFile.TryReadHeader(reader, out header, out metaJson, out string headerError))
                {
                    return Fail(headerError);
                }

                if (header.protocolVersion != ArenaProtocol.PROTOCOL_VERSION)
                {
                    // §12.4: the records ARE §5/§6 bytes and the wire layout changes between
                    // versions — reading an old one with a new parser silently yields garbage poses.
                    return Fail($"Kayıt v{header.protocolVersion} ile alınmış, bu sürüm " +
                                $"v{ArenaProtocol.PROTOCOL_VERSION} oynatabilir.");
                }

                // A truncated tail is valid (§12.3): whatever was read stays playable.
                while (ReplayFile.TryReadRecord(reader, out ReplayRecord record))
                {
                    if (record.kind == ReplayRecordKind.End)
                    {
                        break;
                    }

                    _records.Add(record);
                    DurationMs = (int)record.timeMs;
                }
            }

            if (_records.Count == 0)
            {
                return Fail("Kayıtta oynatılacak veri yok.");
            }

            Header = header;
            if (header.IsFinalized && header.durationMs > 0)
            {
                DurationMs = (int)header.durationMs;
            }

            if (!string.IsNullOrEmpty(metaJson))
            {
                try
                {
                    Meta = JsonUtility.FromJson<ReplayMeta>(metaJson);
                }
                catch (Exception e)
                {
                    // Meta is for listing only — a broken one must not block playback.
                    Debug.LogWarning($"[ReplayPlayer] Kayıt meta'sı okunamadı: {e.Message}");
                }
            }

            IsLoaded = true;
            NetClock.SetOverride(() => ClockBaseMs + CurrentMs);
            OnLoaded?.Invoke();
            return true;
        }

        /// <summary>Drops the recording and gives the live clock back.</summary>
        public void Unload()
        {
            NetClock.ClearOverride();
            _records.Clear();
            _nextIndex = 0;
            CurrentMs = 0;
            DurationMs = 0;
            Meta = null;
            Header = default;
            IsLoaded = false;
            IsEnded = false;
            Error = "";
            _unknownTypeWarned = false;
        }

        private bool Fail(string message)
        {
            Error = string.IsNullOrEmpty(message) ? "Kayıt dosyası açılamadı." : message;
            Debug.LogError($"[ReplayPlayer] {Error}");
            OnError?.Invoke(Error);
            return false;
        }

        // ---------------------------------------------------------------- playback

        /// <summary>
        /// Advances the playback clock by one frame and feeds everything that came due.
        /// <paramref name="deltaSeconds"/> is SCALED engine time: pause (<c>timeScale</c> 0) and
        /// speed therefore reach FX, physics and animators and the clock identically.
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (!IsLoaded || IsEnded || deltaSeconds <= 0f)
            {
                return;
            }

            if (HoldClock != null && HoldClock())
            {
                return;
            }

            int target = Mathf.Min(DurationMs, CurrentMs + Mathf.RoundToInt(deltaSeconds * 1000f));
            if (!FeedUntil(target, int.MinValue))
            {
                return; // a scene load started; the clock waits on that record
            }

            if (_nextIndex >= _records.Count && CurrentMs >= DurationMs)
            {
                IsEnded = true;
                OnEnded?.Invoke();
            }
        }

        /// <summary>Jumps to an absolute position; backwards rewinds and re-feeds from the start.</summary>
        public void Seek(int targetMs)
        {
            if (!IsLoaded)
            {
                return;
            }

            targetMs = Mathf.Clamp(targetMs, 0, DurationMs);

            if (targetMs < CurrentMs)
            {
                ResetFedState();
            }

            IsEnded = false;

            // ⚠️ Cues are suppressed at their source across the jump (ReplayMode.Seeking): the
            // stretch is fed in one frame, and muting the listener would not stop lines that queue.
            ReplayMode.Seeking = true;
            try
            {
                FeedUntil(targetMs, targetMs - DatagramPrerollMs);
            }
            finally
            {
                ReplayMode.Seeking = false;
            }
        }

        public void SeekBy(int deltaMs)
        {
            Seek(CurrentMs + deltaMs);
        }

        /// <summary>Back to the beginning (same rewind path as a backward seek).</summary>
        public void Restart()
        {
            if (!IsLoaded)
            {
                return;
            }

            ResetFedState();
            IsEnded = false;
        }

        /// <summary>
        /// Rewind reset: the recording is re-fed from 0, so everything a record built must go.
        /// <para>The LIVE disconnect reset is reused deliberately — every registry, the admin roster,
        /// the object table and the pending shot FX already clear themselves there, and in replay
        /// that path shows nothing (the connection card is not installed, §12.4). The scene STAYS
        /// loaded; the re-fed <c>load_match</c> stages the world again.</para>
        /// </summary>
        private void ResetFedState()
        {
            NetEvents.RaiseDisconnected();
            _dispatcher.ResetRosterVersion();
            ArenaClient.Instance?.UdpChannel?.ResetForReplay();

            _nextIndex = 0;
            CurrentMs = 0;
        }

        /// <summary>
        /// Feeds every record up to <paramref name="targetMs"/>. Datagrams older than
        /// <paramref name="datagramFromMs"/> are SKIPPED (seek: only the last second is needed to
        /// refill the interpolation rings, older ones would replay their fire events too).
        /// </summary>
        /// <returns>False when the clock was parked on a record (scene load).</returns>
        private bool FeedUntil(int targetMs, int datagramFromMs)
        {
            while (_nextIndex < _records.Count && _records[_nextIndex].timeMs <= (uint)targetMs)
            {
                ReplayRecord record = _records[_nextIndex++];

                // The ingest stamp is the record's own time, not the frame's: a batch fed in one
                // frame must still land spread out on the playback clock.
                CurrentMs = (int)record.timeMs;

                if (record.kind == ReplayRecordKind.Text)
                {
                    FeedText(record.payload);
                }
                else if (record.kind == ReplayRecordKind.Datagram && record.timeMs >= (uint)Mathf.Max(0, datagramFromMs))
                {
                    FeedDatagram(record.payload);
                }

                if (HoldClock != null && HoldClock())
                {
                    return false;
                }
            }

            CurrentMs = targetMs;
            return true;
        }

        private void FeedText(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return;
            }

            try
            {
                string json = Encoding.UTF8.GetString(payload);
                var envelope = JsonUtility.FromJson<MsgEnvelope>(json);
                if (envelope == null || string.IsNullOrEmpty(envelope.type))
                {
                    return;
                }

                if (!_dispatcher.TryDispatch(envelope.type, json) && !_unknownTypeWarned)
                {
                    // Once only: a recording full of an unknown type would flood the console.
                    _unknownTypeWarned = true;
                    Debug.LogWarning($"[ReplayPlayer] Kayıttaki '{envelope.type}' mesajı bu sürümde " +
                                     "işlenmiyor; yok sayıldı.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ReplayPlayer] Kayıt metni işlenemedi: {e.Message}");
            }
        }

        private void FeedDatagram(byte[] payload)
        {
            UdpStateChannel channel = ArenaClient.Instance != null ? ArenaClient.Instance.UdpChannel : null;
            if (channel == null || payload == null || payload.Length == 0)
            {
                return;
            }

            try
            {
                channel.HandleDatagram(payload, NetClock.NowMs);
            }
            catch (Exception e)
            {
                // Same isolation as the live receive loop: one bad datagram must not stop playback.
                Debug.LogWarning($"[ReplayPlayer] Kayıttaki datagram düşürüldü: {e.Message}");
            }
        }
    }
}
