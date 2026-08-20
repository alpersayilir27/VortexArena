// ⚠️ Never add `using System;` here: this file uses UnityEngine.Random and System has its own
// Random → name clash. System.Environment is called fully qualified.
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Audio;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Combat
{
    /// <summary>Shot presentation for REMOTE players: listens to the server's 20 Hz event channel
    /// (<c>NetEvents.OnRemoteFireEvent</c>, §6.4/6.5), plays muzzle flash + spatial audio and draws
    /// the trail through <see cref="ShotTracer"/>. Our own shot NEVER arrives here (the channel
    /// filters the shooter, §6.5); local shot FX stays in Weapon/WeaponAudio.
    /// <para>⚠️ The event carries NO muzzle POSITION, on purpose (§6.4): the tracer must leave the
    /// muzzle of the gun the RECEIVER draws. An absolute position on the wire would start the
    /// tracer offset from the drawn muzzle, because the receiver draws the gun from an interpolated
    /// hand pose — more faithful to the shooter, worse to the eye. CONSISTENCY BEATS FIDELITY. So
    /// the origin is resolved locally: held item visual (<c>RemoteAvatar.GetHeldItemVisual</c>) →
    /// its <c>Muzzle</c> child → hand pose → head pose.</para>
    /// <para>Never placed in a scene: self-bootstraps and goes DontDestroyOnLoad. FX nodes are
    /// created lazily in an 8-slot round-robin pool (<c>WeaponCatalog.RemoteShotFxPrefab</c>, else
    /// a plain AudioSource fallback); the tracer pool lives in <see cref="ShotTracer"/>. The admin
    /// spectator receives the same events and sees the whole VISUAL presentation identically — no
    /// role split for flash, tracer or recoil. The only difference is audio LEVEL: shots of players
    /// not being watched in POV are attenuated
    /// (<see cref="SpectatorAudioFocus"/>).</para></summary>
    public class RemoteShotFx : MonoBehaviour
    {
        /// <summary>FX nodes in the pool (max simultaneously live shot effects).</summary>
        private const int PoolSize = 8;

        /// <summary>Beyond this distance (m) no sound is played, only the flash.</summary>
        private const float MaxAudibleDistanceMeters = 40f;

        /// <summary>Attenuation applied to shots outside the spectator focus
        /// (<see cref="SpectatorAudioFocus"/>). ⚠️ NOT zero: full silence would deafen the POV and
        /// the operator would lose "something is happening over there"; attenuation keeps that
        /// information while the watched player still dominates.</summary>
        private const float UnfocusedVolumeScale = 0.3f;

        private const float FallbackMaxDistanceMeters = 60f;

        private const int ParticlesPerShot = 14;

        /// <summary>Name of the muzzle child in the item prefab (WeaponKitBuilder creates it).</summary>
        private const string MuzzleChildName = "Muzzle";

        private const float AvatarScanIntervalSeconds = 0.5f;

        private const float PruneIntervalSeconds = 5f;
        private const float PlayerStaleSeconds = 10f;

        /// <summary>Pending event cap; on overflow the oldest is played NOW, never dropped.</summary>
        private const int MaxPendingEvents = 128;

        /// <summary>Longest an event may wait before playback (ms). A cap so a broken tick→time
        /// mapping cannot hold it forever; more than twice INTERP_DELAY_MS, so it never triggers in
        /// a healthy session.</summary>
        private const int MaxPlaybackLeadMs = 250;

        public static RemoteShotFx Instance { get; private set; }

        /// <summary>Spectator audio focus: WHICH player's shots stand out. <c>null</c> = no focus,
        /// every shot plays at full volume by distance (player clients, non-POV spectator modes and
        /// the unset state); <c>n</c> = that <c>playerId</c> full volume, everyone else attenuated
        /// by <see cref="UnfocusedVolumeScale"/>.
        /// <para>⚠️ Focus ATTENUATES, never mutes: silencing would deafen the operator to the rest
        /// of the arena.</para>
        /// <para>⚠️ Core cannot see App (the asmdef graph flows down), so APP writes this value —
        /// <c>AdminSpectatorCamera</c>, from the operator's current mode/selection. It is the only
        /// writer; a second one would leave "who is attenuated" unanswerable.</para>
        /// <para>⚠️ Focus is for POV only: in top-down/free mode a shot sound answers "where is the
        /// fight", so nobody is attenuated there.</para></summary>
        public static int? SpectatorAudioFocus { get; set; }

        private sealed class FxNode
        {
            public Transform Root;
            public AudioSource Source;
            public ParticleSystem Particles;
        }

        /// <summary>Per-player presentation state: avatar, per-hand resolved muzzle, tracer
        /// counter. The event path runs 53-160/s, so everything is CACHED here — a scene scan or
        /// GetComponentsInChildren per event is unacceptable.</summary>
        private sealed class PlayerFx
        {
            public RemoteAvatar Avatar;

            public Transform MuzzleL;
            public Transform MuzzleR;

            // The muzzle cache KEY is the item instance itself (the root GetHeldItemVisual
            // returns): a changed item changes the reference, so the cache invalidates itself and a
            // stale muzzle can never be used. A FAILED lookup is cached under the same key
            // (Muzzle=null), otherwise every shot would rescan the hierarchy for nothing.
            public Transform MuzzleSourceL;
            public Transform MuzzleSourceR;

            /// <summary>Has the "item has no Muzzle" warning been logged once for this player.</summary>
            public bool MuzzleWarned;

            /// <summary>Shots received from this player (tracerEveryNthRound counter).</summary>
            public int ShotCount;

            public float LastEventTime;
        }

        /// <summary>Event not yet due (playAtMs is on the Environment.TickCount axis).</summary>
        private struct PendingShot
        {
            public RemoteFireEvent evt;
            public int playAtMs;
        }

        private readonly FxNode[] _pool = new FxNode[PoolSize];
        private int _nextNode;

        // ⚠️ MAIN THREAD ONLY, no lock: NetEvents.OnRemoteFireEvent is raised on the main thread
        // (UdpStateChannel hands it over through _mainThreadActions) and Update drains it. Never
        // add a path that writes here from the network thread.
        private readonly List<PendingShot> _pending = new List<PendingShot>();

        private readonly Dictionary<int, PlayerFx> _players = new Dictionary<int, PlayerFx>();
        private readonly List<int> _pruneScratch = new List<int>();

        /// <summary>Endpoints of the regenerated pellet spread (<see cref="BuildScatter"/>). One
        /// buffer is enough: events play one at a time on the main thread.</summary>
        private Vector3[] _scatterPoints;

        private float _nextAvatarScan;
        private float _nextPrune;

        // One "not in catalog" warning per netItemId (keep the log clean).
        private readonly HashSet<byte> _warnedItemIds = new HashSet<byte>();
        private bool _warnedNoPrefab;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[RemoteShotFx]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RemoteShotFx>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // A second copy destroys itself.
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Persistent singleton: subscribe in Awake/OnDestroy, not OnEnable/OnDisable, so
            // server events are not missed while the object is disabled.
            NetEvents.OnRemoteFireEvent += HandleFireEvent;
            NetEvents.OnDisconnected += HandleDisconnected;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnRemoteFireEvent -= HandleFireEvent;
            NetEvents.OnDisconnected -= HandleDisconnected;
            Instance = null;
        }

        /// <summary>Pending events are DROPPED on disconnect: the old session's tracers must not
        /// play after reconnecting, and the server tick axis may restart from zero, which makes the
        /// computed playback times meaningless.</summary>
        private void HandleDisconnected()
        {
            _pending.Clear();
        }

        /// <summary>Plays due shot events, preserving order and compacting in one pass.</summary>
        private void Update()
        {
            int count = _pending.Count;
            if (count == 0)
            {
                return;
            }

            // TickCount difference via int subtraction: survives the ~24.9 day wrap.
            int now = System.Environment.TickCount;

            int write = 0;
            for (int i = 0; i < count; i++)
            {
                PendingShot shot = _pending[i];
                if (now - shot.playAtMs >= 0)
                {
                    PlayFireEvent(shot.evt);
                    continue;
                }

                if (write != i)
                {
                    _pending[write] = shot;
                }

                write++;
            }

            _pending.RemoveRange(write, count - write);
        }

        /// <summary>TIMING of an incoming shot event (§6.5): cheap rejects happen here, then the
        /// event waits until its own <c>serverTick</c> is due on the receiver's interpolation clock
        /// (<c>RemotePlayerRegistry.TryGetPlaybackTimeMs</c>).
        /// <para>Remote poses are deliberately drawn <c>INTERP_DELAY_MS</c> behind, but the server
        /// broadcasts the snapshot (0x02) and the event batch (0x04) on the SAME tick. Playing on
        /// arrival would fire flash/sound/tracer from where the hand was 100 ms ago (~20 cm off at
        /// 2 m/s). So we wait until the avatar's hand REACHES that tick.</para>
        /// <para>The 20 Hz batching therefore ADDS NO perceived latency: a ≤50 ms batch wait
        /// dissolves inside the 100 ms interpolation buffer.</para>
        /// <para>With no mapping (no snapshot yet, or a tick too old for the ring) the event plays
        /// IMMEDIATELY: a late tracer is acceptable, a lost one is not.</para>
        /// <para>⚠️ Sampling a historical pose is NOT needed and that door stays shut: the origin
        /// is the muzzle of the gun DRAWN this frame, not a wire position (§6.4). Playing at the
        /// right moment already makes the drawn muzzle that tick's muzzle.</para></summary>
        private void HandleFireEvent(RemoteFireEvent evt)
        {
            // Cheap rejects BEFORE queuing so an event that will not play never waits.
            // KIND_THROW belongs to a later phase and is silently skipped here.
            if (evt.kind != FireEventEntry.KIND_SHOT)
            {
                return;
            }

            if (evt.arenaDirection.sqrMagnitude < 1e-6f)
            {
                return;
            }

            int now = System.Environment.TickCount;

            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null || !registry.TryGetPlaybackTimeMs(evt.serverTick, out int playAtMs) ||
                now - playAtMs >= 0)
            {
                // No mapping, or already due → waiting is pointless.
                PlayFireEvent(evt);
                return;
            }

            if (playAtMs - now > MaxPlaybackLeadMs)
            {
                playAtMs = now + MaxPlaybackLeadMs;
            }

            if (_pending.Count >= MaxPendingEvents)
            {
                // Cap reached: play the oldest and remove it. NEVER dropped silently — a missing
                // tracer is undiagnosable, one playing 100 ms early is visible and harmless.
                PendingShot oldest = _pending[0];
                _pending.RemoveAt(0);
                PlayFireEvent(oldest.evt);
            }

            _pending.Add(new PendingShot { evt = evt, playAtMs = playAtMs });
        }

        /// <summary>PRESENTATION of one remote shot (§6.5): origin, muzzle flash, spatial audio,
        /// tracer. Timing belongs to <see cref="HandleFireEvent"/>; only due events reach here.</summary>
        private void PlayFireEvent(in RemoteFireEvent evt)
        {
            Vector3 worldDir = ArenaToWorldDirection(evt.arenaDirection);
            if (worldDir.sqrMagnitude < 1e-6f)
            {
                return;
            }

            worldDir = worldDir.normalized;

            float now = Time.unscaledTime;
            PlayerFx fx = GetOrCreatePlayer(evt.playerId);
            fx.LastEventTime = now;
            fx.ShotCount++;

            ItemDefinition item = ResolveItem(evt.itemId);

            // Recoil fires BEFORE origin resolution: the early return below (no pose at all) drops
            // the rest of the visuals but is no reason for the gun not to kick. Non-weapon items
            // have no recoil.
            if (item is WeaponDefinition recoilWeapon)
            {
                RemoteAvatar avatar = ResolveAvatar(fx, evt.playerId, now);
                if (avatar != null)
                {
                    avatar.ApplyShotRecoil(evt.rightHand, recoilWeapon);
                }
            }

            if (!TryResolveOrigin(fx, evt, now, out Vector3 origin))
            {
                // No muzzle, hand or head pose yet: we do not know where to put the effect.
                return;
            }

            WeaponCatalog catalog = WeaponCatalog.Load();
            FxNode node = TakeNode(catalog);
            if (node != null && node.Root != null)
            {
                node.Root.SetPositionAndRotation(origin, Quaternion.LookRotation(worldDir));

                if (node.Particles != null)
                {
                    node.Particles.Emit(ParticlesPerShot);
                }

                // The audio/flash profile is weapon-specific; a non-weapon item just skips it,
                // the tracer below is still drawn.
                PlayShotSound(node, item as WeaponDefinition, origin, evt.playerId);
            }

            DrawTracer(fx, item, origin, worldDir, evt.magnitude);
            PrunePlayers(now);
        }

        // --------------------------------------------------------------------- tracer

        /// <summary>Draws the trail. For KIND_SHOT <c>magnitude</c> is the hit DISTANCE (m), so the
        /// endpoint is <c>origin + dir × distance</c> (§6.4).
        /// <para>⚠️ Not drawn for every round: <c>TracerEveryNthRound</c> is counted PER PLAYER — a
        /// shared counter would scatter tracers across random players under heavy fire and make
        /// "who is shooting" unreadable.</para>
        /// <para>⚠️ A shotgun spread is REGENERATED here (<c>BuildScatter</c>): the wire carries
        /// one direction + one distance (§6.4 — per-pellet events would announce the same shot 9
        /// times and fill the per-packet limit on a single trigger), yet what a distant player sees
        /// is the shotgun's identity: a cone, not one thin line. The missing data is COSMETIC and
        /// the receiver can produce it; damage already travels as a separate <c>hit_report</c> per
        /// pellet from the shooter's client.</para></summary>
        private void DrawTracer(PlayerFx fx, ItemDefinition item, Vector3 origin, Vector3 worldDir, float distanceMeters)
        {
            if (item == null)
            {
                // Unresolved item → no look parameters. An invented tracer means the wrong
                // calibre/colour; flash + sound is enough.
                return;
            }

            int everyNth = item.TracerEveryNthRound;
            if (everyNth < 1 || fx.ShotCount % everyNth != 0)
            {
                return;
            }

            int count = BuildScatter(item as WeaponDefinition, origin, worldDir, distanceMeters);
            if (count < 1)
            {
                return;
            }

            // The pool is SHARED with the shooter's own trail (ShotTracer.Shared): both look up
            // the same ItemDefinition, so there is no reason to split it. Smoke is emitted inside
            // this single call — there is no separate step (see the ShotTracer summary).
            ShotTracer.Shared.Play(
                origin,
                _scatterPoints,
                count,
                item.TracerColor,
                item.TracerWidth,
                item.TracerLifetime);
        }

        /// <summary>Writes the spread endpoints into <see cref="_scatterPoints"/> and returns the
        /// count. A normal (or non-weapon) item writes a single point.
        /// <para>The FIRST pellet comes FROM THE WIRE (direction + distance): that is the ray the
        /// shooter really fired and it is untouched. The rest are generated from the
        /// <c>BaseSpreadDegrees</c> cone with the shooter's own distribution
        /// (<c>insideUnitCircle</c>, same formula as <see cref="Weapon"/>), so the fan ANGLE is the
        /// weapon's own figure, not an invented visual constant. ⚠️ Bloom and the two-hand factor
        /// are not on the wire, so the cone is drawn slightly narrower than the shooter's: narrow
        /// is the safe side, drawing it wide throws pellets behind walls.</para>
        /// <para>⚠️ Generated pellet distances are MEASURED BY RAY, never copied from the wire
        /// distance: the receiver loads the same arena and sees the same geometry. Copying would
        /// cut the whole cone at the shooter's hit distance (a flat disc) on a close hit, and send
        /// nine 26 m lines THROUGH walls on a miss. The ray goes through
        /// <see cref="ArenaCombat.TraceShot"/> — a hand-written <c>Physics.Raycast</c> loses the
        /// obstacle rule and the trigger filter.</para>
        /// <para>⚠️ The fan is NOT identical to the shooter's screen and does not need to be:
        /// pellet scatter is random on the shooter too. Matching it would require per-pellet
        /// direction + distance on the wire; consistency is sought at the ORIGIN, not here
        /// (§6.4).</para></summary>
        private int BuildScatter(WeaponDefinition weapon, Vector3 origin, Vector3 worldDir, float distanceMeters)
        {
            int pellets = weapon != null ? weapon.PelletCount : 1;
            pellets = Mathf.Clamp(pellets, 1, ShotTracer.MaxScatterLines);

            if (_scatterPoints == null || _scatterPoints.Length < ShotTracer.MaxScatterLines)
            {
                // Allocated once at the CAP size: the weapon changes from event to event, so
                // resizing per weapon would mean an allocation per shot.
                _scatterPoints = new Vector3[ShotTracer.MaxScatterLines];
            }

            _scatterPoints[0] = origin + worldDir * distanceMeters;
            if (pellets == 1)
            {
                return 1;
            }

            // Spread plane from two axes perpendicular to worldDir. ⚠️ Vector3.up cannot be the
            // unconditional reference: aiming straight up/down makes the cross product zero and
            // collapses the cone.
            Vector3 reference = Mathf.Abs(worldDir.y) > 0.99f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(reference, worldDir).normalized;
            Vector3 up = Vector3.Cross(worldDir, right);

            float spread = weapon.BaseSpreadDegrees;
            float range = weapon.Range > 0f ? weapon.Range : distanceMeters;

            int count = 1;
            for (int p = 1; p < pellets; p++)
            {
                Vector2 scatter = Random.insideUnitCircle * spread;
                Vector3 direction = Quaternion.AngleAxis(scatter.x, up) *
                                    Quaternion.AngleAxis(scatter.y, right) *
                                    worldDir;

                ArenaCombat.ShotTrace trace = ArenaCombat.TraceShot(origin, direction, range);
                _scatterPoints[count++] = origin + direction * trace.Distance;
            }

            return count;
        }

        // --------------------------------------------------------------------- origin

        /// <summary>Start point of the effect/tracer (§6.4): drawn muzzle → hand pose → head pose.
        /// False if none exists.</summary>
        private bool TryResolveOrigin(PlayerFx fx, in RemoteFireEvent evt, float now, out Vector3 origin)
        {
            origin = default;

            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;

            // Declared up front: if the call short-circuits (no registry) the compiler would treat
            // them as unassigned below.
            Pose headArena = Pose.identity;
            Pose handLArena = Pose.identity;
            Pose handRArena = Pose.identity;
            bool hasPose = registry != null &&
                           registry.GetInterpolatedPose(evt.playerId, out headArena, out handLArena, out handRArena);

            Vector3 handWorld = default;
            bool hasHand = false;
            if (hasPose)
            {
                Pose handArena = evt.rightHand ? handRArena : handLArena;
                // Exact zero = never-filled pose (interpolation returned identity); a hand really
                // sitting at the arena origin is not possible in practice.
                if (handArena.position.sqrMagnitude > 1e-6f)
                {
                    handWorld = ArenaSpace.ArenaToWorld(handArena.position);
                    hasHand = true;
                }
            }

            Transform muzzle = ResolveMuzzle(fx, evt, now);
            if (muzzle != null)
            {
                origin = muzzle.position;
                return true;
            }

            if (hasHand)
            {
                origin = handWorld;
                return true;
            }

            if (hasPose)
            {
                origin = ArenaSpace.ArenaToWorld(headArena.position);
                return true;
            }

            return false;
        }

        /// <summary>Finds the <c>Muzzle</c> child of the item instance DRAWN in that hand (cached
        /// per hand; refreshes itself when the instance changes).
        /// <para>The instance comes from the contract API
        /// (<see cref="RemoteAvatar.GetHeldItemVisual"/>), NOT from a scene search: only asking for
        /// the instance the receiver actually instantiated guarantees the tracer leaves the DRAWN
        /// muzzle (§6.4). With dual pistols <c>rightHand</c> picks the right one; in a two-handed
        /// grip (GRIP_LINKED) there is a single instance and either hand returns it.</para>
        /// <para>⚠️ The muzzle itself is still found BY NAME (scoped to one item instance) —
        /// renaming <c>Muzzle</c> in the prefab silently drops the presentation to the hand pose,
        /// so a single warning per player is logged (the event path runs 53-160/s, no
        /// spam).</para></summary>
        private Transform ResolveMuzzle(PlayerFx fx, in RemoteFireEvent evt, float now)
        {
            RemoteAvatar avatar = ResolveAvatar(fx, evt.playerId, now);
            if (avatar == null)
            {
                return null;
            }

            Transform itemVisual = avatar.GetHeldItemVisual(evt.rightHand);
            if (itemVisual == null)
            {
                return null; // no item drawn in that hand → fall back to the hand pose
            }

            // Cache key is the instance itself: same instance → same muzzle, different instance →
            // the item changed and we search again.
            Transform source = evt.rightHand ? fx.MuzzleSourceR : fx.MuzzleSourceL;
            if (source == itemVisual)
            {
                return evt.rightHand ? fx.MuzzleR : fx.MuzzleL;
            }

            Transform found = FindMuzzle(itemVisual);

            if (evt.rightHand)
            {
                fx.MuzzleSourceR = itemVisual;
                fx.MuzzleR = found;
            }
            else
            {
                fx.MuzzleSourceL = itemVisual;
                fx.MuzzleL = found;
            }

            if (found == null && !fx.MuzzleWarned)
            {
                fx.MuzzleWarned = true;
                Debug.LogWarning(
                    $"[RemoteShotFx] Oyuncu {evt.playerId}: elindeki '{itemVisual.name}' örneğinde " +
                    $"'{MuzzleChildName}' çocuğu yok — atış efekti/tracer el pozundan çıkacak.");
            }

            return found;
        }

        // GetComponentsInChildren is NOT used: it allocates an array. Manual recursion allocates
        // nothing and runs once per item change. The FIRST match wins — an item has one muzzle.
        private static Transform FindMuzzle(Transform parent)
        {
            int count = parent.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == MuzzleChildName)
                {
                    return child;
                }

                Transform found = FindMuzzle(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary><c>playerId</c> → <see cref="RemoteAvatar"/>. There is no static avatar
        /// registry and their spawner lives in App (Core cannot see it), so the index is built by a
        /// scene scan — never more often than <see cref="AvatarScanIntervalSeconds"/>, since
        /// FindObjectsByType allocates and the event path runs 53-160/s.</summary>
        private RemoteAvatar ResolveAvatar(PlayerFx fx, int playerId, float now)
        {
            if (fx.Avatar != null)
            {
                return fx.Avatar;
            }

            if (now < _nextAvatarScan)
            {
                return null;
            }

            _nextAvatarScan = now + AvatarScanIntervalSeconds;

            RemoteAvatar[] avatars = FindObjectsByType<RemoteAvatar>(FindObjectsSortMode.None);
            for (int i = 0; i < avatars.Length; i++)
            {
                RemoteAvatar avatar = avatars[i];
                if (avatar == null)
                {
                    continue;
                }

                PlayerFx target = GetOrCreatePlayer(avatar.PlayerId);
                if (target.Avatar != avatar)
                {
                    target.Avatar = avatar;
                    // New avatar = new hierarchy: clear the muzzle cache explicitly.
                    target.MuzzleL = null;
                    target.MuzzleR = null;
                    target.MuzzleSourceL = null;
                    target.MuzzleSourceR = null;
                }
            }

            return fx.Avatar;
        }

        // ---------------------------------------------------------------- player records

        private PlayerFx GetOrCreatePlayer(int playerId)
        {
            if (!_players.TryGetValue(playerId, out PlayerFx fx))
            {
                fx = new PlayerFx();
                _players.Add(playerId, fx);
            }

            return fx;
        }

        /// <summary>Drops records (tracer counter included) of players whose avatar is gone and
        /// who have sent no event for a while. Preferred over subscribing to the registry's
        /// <c>OnRemoteLeft</c> so the presentation does not depend on the player LIFECYCLE: the
        /// counter is cleaned even if an avatar was never spawned.</summary>
        private void PrunePlayers(float now)
        {
            if (now < _nextPrune)
            {
                return;
            }

            _nextPrune = now + PruneIntervalSeconds;
            _pruneScratch.Clear();

            foreach (KeyValuePair<int, PlayerFx> kv in _players)
            {
                if (kv.Value.Avatar == null && now - kv.Value.LastEventTime > PlayerStaleSeconds)
                {
                    _pruneScratch.Add(kv.Key);
                }
            }

            for (int i = 0; i < _pruneScratch.Count; i++)
            {
                _players.Remove(_pruneScratch[i]);
            }
        }

        // ---------------------------------------------------------------------- helpers

        /// <summary>Converts an arena-space DIRECTION to world. Arena space coincides with world
        /// space (<see cref="ArenaSpace"/>) so this is the identity; the helper exists to keep "this
        /// value came from ARENA space" visible at the call sites.</summary>
        private static Vector3 ArenaToWorldDirection(Vector3 arenaDirection)
        {
            return ArenaSpace.ArenaToWorld(arenaDirection);
        }

        /// <summary>Resolves the event's <c>itemId</c> (§6.6) to an item definition. That byte is
        /// the only source of the presentation profile, so the event is self-sufficient even if the
        /// snapshot's <c>itemL/itemR</c> bytes are lost (§6.4).</summary>
        private ItemDefinition ResolveItem(byte itemId)
        {
            NetItemCatalog catalog = NetItemCatalog.Load();
            ItemDefinition def = catalog != null ? catalog.FindByNetItemId(itemId) : null;
            if (def == null)
            {
                WarnUnknownItem(itemId);
            }

            return def;
        }

        /// <summary>With no definition (non-weapon/uncatalogued item) or no/distant listener only
        /// the flash remains; a player outside the spectator focus is attenuated, not muted.</summary>
        private static void PlayShotSound(FxNode node, WeaponDefinition def, Vector3 worldPos, int playerId)
        {
            if (def == null || node.Source == null || def.FireClips == null || def.FireClips.Length == 0)
            {
                return;
            }

            Camera listener = Camera.main;
            if (listener == null)
            {
                return;
            }

            if ((listener.transform.position - worldPos).sqrMagnitude >
                MaxAudibleDistanceMeters * MaxAudibleDistanceMeters)
            {
                return;
            }

            AudioClip clip = def.FireClips[Random.Range(0, def.FireClips.Length)];
            if (clip == null)
            {
                return;
            }

            // Operator's local weapon level, independent of the spectator focus below.
            float channel = AudioMix.Weapons;
            if (channel <= 0f)
            {
                return; // muted: flash and tracer are still drawn
            }

            // Spectator focus (written by App): players outside it are attenuated, never muted —
            // flash and tracer are drawn for everyone anyway, and attenuated audio keeps the
            // "there is a fight over there" information audible.
            int? focus = SpectatorAudioFocus;
            float volume = def.FireVolume;
            if (focus.HasValue && focus.Value != playerId)
            {
                volume *= UnfocusedVolumeScale;
            }

            volume *= channel;

            node.Source.pitch = def.FirePitchBase + Random.Range(-def.FirePitchJitter, def.FirePitchJitter);
            node.Source.PlayOneShot(clip, volume);
        }

        private FxNode TakeNode(WeaponCatalog catalog)
        {
            FxNode node = _pool[_nextNode];
            if (node == null || node.Root == null)
            {
                node = CreateNode(catalog);
                _pool[_nextNode] = node;
            }

            _nextNode = (_nextNode + 1) % PoolSize;
            return node;
        }

        private FxNode CreateNode(WeaponCatalog catalog)
        {
            GameObject prefab = catalog != null ? catalog.RemoteShotFxPrefab : null;
            GameObject go;

            if (prefab != null)
            {
                // Lives under our DDOL root, so the pool survives scene changes.
                go = Instantiate(prefab, transform);
            }
            else
            {
                if (!_warnedNoPrefab)
                {
                    _warnedNoPrefab = true;
                    Debug.LogWarning(
                        "[RemoteShotFx] WeaponCatalog.RemoteShotFxPrefab atanmadı — parçacıksız sade ses düğümü kullanılacak.");
                }

                go = new GameObject("[RemoteShotFxNode]");
                go.transform.SetParent(transform, false);

                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.maxDistance = FallbackMaxDistanceMeters;
            }

            return new FxNode
            {
                Root = go.transform,
                Source = go.GetComponentInChildren<AudioSource>(true),
                Particles = go.GetComponentInChildren<ParticleSystem>(true),
            };
        }

        private void WarnUnknownItem(byte itemId)
        {
            if (!_warnedItemIds.Add(itemId))
            {
                return;
            }

            Debug.LogWarning(
                $"[RemoteShotFx] netItemId {itemId} NetItemCatalog'da yok — atış yalnız flaş olarak " +
                "oynatılır (ses ve tracer atlanır).");
        }
    }
}
