using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// <b>The only gate from game code to the network.</b> Never touch protocol DTOs, arena-space
    /// conversion or sequence numbers when writing a weapon/arrow/axe/bomb/trap — call these methods.
    ///
    /// <para><b>Why this class:</b> reporting a hit correctly needs four separate facts (pose must be
    /// converted to arena space, DIRECTION is not a point, the target must be a
    /// <see cref="RemoteHitBox"/>, the client decides damage). Buried inside <see cref="Weapon"/>,
    /// every new damage source would rediscover them — and one wrong rediscovery loses hits
    /// silently.</para>
    ///
    /// <para><b>You are not the authority:</b> these methods do not APPLY damage, only REPORT it.
    /// Health, death, score and phase come back from the server via <c>health_update</c>/
    /// <c>kill_event</c> (Docs/ArenaNet-Protokol.md §10.3). Never reduce health locally — the two
    /// sides would diverge.</para>
    ///
    /// <para><b>The two reports use TWO SEPARATE channels on purpose:</b>
    /// <see cref="ReportShot"/>/<see cref="ReportThrow"/> are <i>presentation</i> events (muzzle
    /// flash, sound, tracer) → UDP event channel, no reliability needed, a loss costs one effect
    /// (§6.4). <see cref="ReportHit"/> changes <i>authoritative state</i> (health, death, score) →
    /// stays on WS as <c>hit_report</c>. At 600 RPM shot events were drowning the authoritative
    /// channel; splitting frees it from shot noise entirely.</para>
    ///
    /// <para><b>All of these are silent no-ops without a connection.</b> Game code runs unchanged in
    /// a serverless editor session; no <c>if (connected)</c> around any call.</para>
    ///
    /// <para><b>One presentation job lives here: the hit marker</b> (<see cref="HitMarker"/>) — an X
    /// drawn for the shooter at every reported hit point. Same rationale as the gate itself: a new
    /// damage source gets it for free and "did I hit" never answers differently per damage source.
    /// Do not write your own marker.</para>
    /// </summary>
    public static class ArenaCombat
    {
        // Single reused instance instead of a DTO per hit: ArenaClient.Send converts to JSON INSIDE
        // the call (JsonUtility.ToJson is synchronous), so the object is free once send returns.
        // Array fields are allocated once too.
        // (No shot-event DTO here: the UDP channel uses its own pre-allocated buffer.)
        private static readonly HitReportMsg Hit = new HitReportMsg { hitPos = new float[3] };
        private static int _seq;

        /// <summary>Overlap buffer for <see cref="ReportAreaHit"/> — avoids a new array per blast.
        /// <para>⚠️ Sized well above the expected target count anyway: the query is narrowed to
        /// <see cref="ArenaLayers.AreaTargetMask"/> and skips triggers, but scenery still shares
        /// those layers with the hit boxes. A full buffer drops real targets, and the blast damages
        /// fewer players than it should.</para></summary>
        private static readonly Collider[] OverlapBuffer = new Collider[128];

        /// <summary>Blockers on ONE line from the blast centre to ONE target
        /// (<see cref="TryPenetrateArea"/>) — a separate buffer, since it is filled while
        /// <see cref="OverlapBuffer"/> is still being walked.</summary>
        private static readonly RaycastHit[] BlockerBuffer = new RaycastHit[32];

        /// <summary>One cover counts once even with several colliders on the line.</summary>
        private static readonly HashSet<int> AreaBlockersOnce = new HashSet<int>();

        private static readonly HashSet<int> AreaHitOnce = new HashSet<int>();

        /// <summary>⚠️ Separate set on purpose: a <c>netId</c> and a <c>playerId</c> collide numerically.</summary>
        private static readonly HashSet<int> AreaHitObjectsOnce = new HashSet<int>();

        // ------------------------------------------------------------------ state

        /// <summary>Local player's server id; <c>0</c> when not connected.</summary>
        public static int LocalPlayerId =>
            PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;

        /// <summary>Are we connected (do messages actually go out).</summary>
        public static bool IsConnected => ArenaClient.Instance != null && ArenaClient.Instance.IsConnected;

        /// <summary>Is the local player alive (server-authoritative, from <c>health_update</c>).</summary>
        public static bool IsAlive => PlayerCombatState.Instance == null || PlayerCombatState.Instance.IsAlive;

        /// <summary>Local player health (0..<see cref="ArenaProtocol.PLAYER_MAX_HP"/>).</summary>
        public static float LocalHp =>
            PlayerCombatState.Instance != null ? PlayerCombatState.Instance.Hp : ArenaProtocol.PLAYER_MAX_HP;

        /// <summary>Local player's team; <see cref="Team.Neutral"/> in teamless modes.</summary>
        public static Team LocalTeam =>
            PlayerCombatState.Instance != null ? PlayerCombatState.Instance.Team : Team.Neutral;

        /// <summary>
        /// <b>May the trigger be pulled.</b> Alive + phase Lobby/Live + (once connected) connection
        /// open + player INSIDE the arena (<see cref="IsOutsideArena"/>). Everything that fires MUST
        /// check this: a shot fired while dead or during countdown is rejected server-side anyway,
        /// but playing local sound/FX lies to the player.
        /// <para>Two gates, two questions: the first is <b>server-authoritative state</b> (health,
        /// phase), the second is a purely <b>local physical rule</b> with no protocol counterpart.</para>
        /// <para>A weaponless mode (<see cref="ModeRuntime.IsWeaponless"/>) is shut here too: no path
        /// puts a weapon in hand there, but the throwable reads the SAME gate and would otherwise stay
        /// live in a mode that is meant to have nothing to fire.</para>
        /// </summary>
        public static bool CanFire =>
            (PlayerCombatState.Instance == null || PlayerCombatState.Instance.CanFire) &&
            !IsOutsideArena && !ModeRuntime.IsWeaponless;

        /// <summary>Is the local player OUTSIDE the boundary's safe area — a fire gate.
        /// <para>Poking the weapon into an obstacle is already closed (§10.9), so stepping outside and
        /// shooting in is the remaining physical exploit. Fully LOCAL: no protocol counterpart, the
        /// server does not verify it.</para>
        /// <para>⚠️ Without a boundary (or without a plan) the gate stays OPEN (fail-open): locking
        /// the trigger without knowing the dimensions would mean nobody can fire in a scene whose
        /// dimensions file is unassigned.</para></summary>
        public static bool IsOutsideArena =>
            ArenaBoundary.Active != null && ArenaBoundary.Active.IsOutOfBounds;

        // ------------------------------------------------------------------- shot trace

        /// <summary>
        /// Probe distance BEHIND the muzzle (m): is the barrel body passing through an obstacle. About
        /// one rifle barrel — longer starts blocking legitimate shots taken against a corner, shorter
        /// misses a thin cover.
        /// </summary>
        private const float BarrelProbeMeters = 0.30f;

        /// <summary>Tracer length of a swallowed shot (m): the round dies at the muzzle.</summary>
        private const float BlockedTracerMeters = 0.05f;

        /// <summary>
        /// Ray result of a shot: how far it went, whether it hit something, whether an obstacle
        /// <b>swallowed</b> it.
        /// </summary>
        public readonly struct ShotTrace
        {
            /// <summary>An obstacle swallowed the round — <b>no damage is written to any target</b>.</summary>
            public readonly bool Blocked;

            /// <summary>The ray hit a collider (only meaningful when not <see cref="Blocked"/>).</summary>
            public readonly bool HasHit;

            /// <summary>Hit record; meaningless when <see cref="HasHit"/> is false.</summary>
            public readonly RaycastHit Hit;

            /// <summary>Distance the ray ACTUALLY travelled — tracer and <see cref="ReportShot"/> use it.</summary>
            public readonly float Distance;

            private ShotTrace(bool blocked, bool hasHit, RaycastHit hit, float distance)
            {
                Blocked = blocked;
                HasHit = hasHit;
                Hit = hit;
                Distance = distance;
            }

            internal static ShotTrace BlockedShot() =>
                new ShotTrace(true, false, default, BlockedTracerMeters);

            internal static ShotTrace HitShot(in RaycastHit hit) =>
                new ShotTrace(false, true, hit, hit.distance);

            internal static ShotTrace Miss(float range) =>
                new ShotTrace(false, false, default, range);
        }

        /// <summary>
        /// <b>The ray of a hitscan shot — everything that fires uses this.</b> Do not write your own
        /// <c>Physics.Raycast</c>: the obstacle rule lives here and any future arrow/axe/round gets it
        /// for free.
        ///
        /// <para><b>Why a plain raycast is not enough:</b> in Unity <b>a collider is never hit when
        /// the ray origin is inside it</b>. A player poking the muzzle into a crate would shoot
        /// through it and hit the player behind — pushing the muzzle tip past the far face of a thin
        /// wall is the same door (the origin is now beyond the wall). Only <b>testing the origin
        /// separately</b> catches both.</para>
        ///
        /// <para>When an obstacle swallows the shot the tracer ends at the muzzle and no
        /// <c>hit_report</c> is sent. ⚠️ <b>Triggered weapons normally never reach here:</b> their
        /// gate is <see cref="IsMuzzleBlocked"/> and it kills the trigger outright (no ammo spent, no
        /// sound/flash). This branch is a <b>second line of defence</b> for a damage source that has
        /// no trigger or does not know the gate.</para>
        ///
        /// <para>⚠️ The main ray stays <b>maskless</b> (remote hit boxes are on Default) but MUST
        /// discard triggers: <c>Queries Hit Triggers</c> is on in project settings and the ISDK grab
        /// volumes of scene weapons are triggers — otherwise a round fired past the bench stops in a
        /// grab volume.</para>
        /// </summary>
        /// <param name="muzzleWorld">World position of the muzzle tip.</param>
        /// <param name="direction">Round direction, <b>unit length</b> (distances rely on it).</param>
        /// <param name="range">Weapon range (m).</param>
        public static ShotTrace TraceShot(Vector3 muzzleWorld, Vector3 direction, float range)
        {
            if (IsMuzzleBlocked(muzzleWorld, direction))
            {
                return ShotTrace.BlockedShot();
            }

            return Physics.Raycast(muzzleWorld, direction, out RaycastHit hit, range,
                       ~0, QueryTriggerInteraction.Ignore)
                ? ShotTrace.HitShot(hit)
                : ShotTrace.Miss(range);
        }

        /// <summary>
        /// <b>Is the muzzle blocked by an obstacle</b> — the single test preventing shooting from
        /// behind a wall.
        ///
        /// <para>Two questions: <b>(1)</b> is the muzzle tip inside an obstacle, <b>(2)</b> does the
        /// barrel body (<see cref="BarrelProbeMeters"/> back) pass through one. The second covers thin
        /// cover: with the tip pushed past the far face it sits in open air, so the first test misses
        /// it.</para>
        ///
        /// <para>⚠️ The backward probe looks at the <c>Obstacle</c> mask ONLY: a maskless ray would
        /// catch the player's own hand/weapon and silently swallow legitimate shots.</para>
        ///
        /// <para><b>Two consumers, and the test must live in one place:</b> the trigger gate
        /// (<c>Weapon</c> — blocked means the trigger does nothing, no ammo spent) and
        /// <see cref="TraceShot"/> (second line of defence for trigger-less damage sources). Written
        /// twice, one would drift and the symptom would be "some weapons can shoot through walls".</para>
        /// </summary>
        /// <param name="muzzleWorld">World position of the muzzle tip.</param>
        /// <param name="direction">Round direction, <b>unit length</b>.</param>
        public static bool IsMuzzleBlocked(Vector3 muzzleWorld, Vector3 direction)
        {
            if (ObstacleVolumes.ContainsPoint(muzzleWorld))
            {
                return true;
            }

            int obstacleMask = ArenaLayers.ObstacleMask;
            return obstacleMask != 0 &&
                   Physics.Raycast(muzzleWorld - direction * BarrelProbeMeters, direction,
                       BarrelProbeMeters, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// <b>Trigger gate: is ANY part of the weapon touching an obstacle</b> (§10.9). Every
        /// triggered weapon must ask this before firing.
        ///
        /// <para><b>Why the muzzle test is not enough:</b> the muzzle is a POINT, the weapon is a
        /// VOLUME. A player can push the rifle behind a brick leaving only the muzzle tip in open air
        /// — firing without showing the body, invisible to a point test. The box test here asks about
        /// the drawn body as it is.</para>
        ///
        /// <para>The box is <b>oriented</b> (the weapon's own rotation): an axis-aligned box would
        /// cover twice the volume for a diagonally held rifle and would also cut legitimate shots
        /// taken beside cover.</para>
        ///
        /// <para>⚠️ <paramref name="bodyRoot"/> must be the weapon's <b>geometry root</b> (model), NOT
        /// the weapon prefab root: visuals that are not part of the weapon (grip frame) hang under the
        /// root and including them would make the weapon look far bigger.</para>
        /// </summary>
        /// <param name="bodyRoot">Weapon geometry root; <c>null</c> skips the box test.</param>
        /// <param name="localBounds">Weapon body bounds in <paramref name="bodyRoot"/> space.</param>
        /// <param name="muzzleWorld">World position of the muzzle tip.</param>
        /// <param name="direction">Round direction, <b>unit length</b>.</param>
        public static bool IsWeaponBlocked(Transform bodyRoot, in Bounds localBounds,
            Vector3 muzzleWorld, Vector3 direction)
        {
            // Muzzle gate first: one point + a short ray, cheaper than the box query.
            if (IsMuzzleBlocked(muzzleWorld, direction))
            {
                return true;
            }

            if (bodyRoot == null)
            {
                return false;
            }

            Vector3 scale = bodyRoot.lossyScale;
            var halfExtents = new Vector3(
                localBounds.extents.x * Mathf.Abs(scale.x),
                localBounds.extents.y * Mathf.Abs(scale.y),
                localBounds.extents.z * Mathf.Abs(scale.z));

            return ObstacleVolumes.OverlapsBox(bodyRoot.TransformPoint(localBounds.center),
                halfExtents, bodyRoot.rotation);
        }

        // ------------------------------------------------------------- target resolution

        /// <summary>
        /// Is there a NETWORK PLAYER behind a collision. Pass the collider the raycast hit; on
        /// <c>true</c> <paramref name="playerId"/> is that player and you MUST report damage with
        /// <see cref="ReportHit"/>. On <c>false</c> the target is not a network player (prop, wall).
        /// <para>Then ask <see cref="TryGetTargetNetId"/>: a network object (§10.10) resolves its
        /// <c>netId</c> from <c>NetObject</c> and takes damage through <see cref="ReportObjectHit"/>.
        /// Neither of the two = <b>there is no such thing as damage</b>: nothing on the client holds
        /// health, so that target takes none.</para>
        /// </summary>
        public static bool TryGetTargetPlayerId(Collider collider, out int playerId)
        {
            playerId = 0;
            if (collider == null)
            {
                return false;
            }

            // The hit box may sit on any child of the body — search upward.
            RemoteHitBox hitBox = collider.GetComponentInParent<RemoteHitBox>();
            if (hitBox == null || hitBox.PlayerId <= 0)
            {
                return false;
            }

            playerId = hitBox.PlayerId;
            return true;
        }

        /// <summary>Is there a NETWORK OBJECT behind a collision (§10.10); on <c>true</c> report damage
        /// with <see cref="ReportObjectHit"/>. Asked only after <see cref="TryGetTargetPlayerId"/>
        /// says no — a player and an object are never the same target.</summary>
        public static bool TryGetTargetNetId(Collider collider, out int netId)
        {
            netId = 0;
            if (collider == null)
            {
                return false;
            }

            // The collider may sit on any child of the object — search upward.
            NetObject netObject = collider.GetComponentInParent<NetObject>();
            if (netObject == null || netObject.NetId <= 0)
            {
                return false;
            }

            netId = netObject.NetId;
            return true;
        }

        /// <summary>Is this a headshot (is the collider a <see cref="RemoteHitBox.IsHead"/> box).
        /// APPLYING the head multiplier is your job: multiply the damage and pass it to
        /// <see cref="ReportHit"/> — the server applies the number you send verbatim (§10.3).</summary>
        public static bool IsHeadshot(Collider collider)
        {
            if (collider == null)
            {
                return false;
            }

            RemoteHitBox hitBox = collider.GetComponentInParent<RemoteHitBox>();
            return hitBox != null && hitBox.IsHead;
        }

        /// <summary>Hit zone of the collider; <c>HitZone.Body</c> (1× multiplier) if not a network
        /// player. APPLYING the zone multiplier is your job: multiply via
        /// <c>WeaponDefinition.GetZoneMultiplier</c> and pass it to <see cref="ReportHit"/> — the
        /// server applies the number you send verbatim (§10.3).</summary>
        public static HitZone GetHitZone(Collider collider)
        {
            if (collider == null)
            {
                return HitZone.Body;
            }

            RemoteHitBox hitBox = collider.GetComponentInParent<RemoteHitBox>();
            return hitBox != null ? hitBox.Zone : HitZone.Body;
        }

        // ---------------------------------------------------------------- reporting

        /// <summary>
        /// <b>§6.4: a shot was fired</b> — for the remote muzzle flash/sound/tracer. The server does
        /// NOT validate it, only relays it, and it has <b>nothing to do with damage</b> (that is a
        /// separate report: <see cref="ReportHit"/>). Calling both is normal — a shot both happens and
        /// hits.
        /// <para>
        /// <b>The muzzle POSITION is not sent</b> (and must not be): the tracer has to leave the
        /// muzzle of the weapon the RECEIVER draws. An absolute origin would produce a tracer offset
        /// from the drawn muzzle, since the receiver draws the weapon from an interpolated hand pose —
        /// consistency &gt; fidelity (§6.4).
        /// </para>
        /// </summary>
        /// <param name="worldDirection">World direction of the round (need not be normalized).</param>
        /// <param name="distanceMeters">Distance the ray ACTUALLY travelled: <c>hit.distance</c> on a
        /// hit, otherwise the weapon range. Tracer length comes from this.</param>
        /// <param name="netItemId"><c>netItemId</c> of the firing item (§6.6); <c>0</c> = unresolved
        /// (the remote side cannot find the presentation profile but still hears the event).</param>
        /// <param name="rightHand">Did the event come from the right hand (one bit on the wire — no
        /// "unknown").</param>
        public static void ReportShot(Vector3 worldDirection, float distanceMeters, byte netItemId, bool rightHand)
        {
            SendFireEvent(FireEventEntry.KIND_SHOT, worldDirection, distanceMeters, netItemId, rightHand);
        }

        /// <summary>
        /// <b>§6.4: an item was thrown</b> (bomb). Receivers simulate the same ballistics <b>locally</b>
        /// — deterministic since gravity is the only force, so no pose streaming is needed. Only
        /// direction + initial speed go on the wire; drift is cosmetic and ends with the blast.
        /// <para>Blast damage is NOT reported through this method but the existing way:
        /// <see cref="ReportAreaHit"/> (one <c>hit_report</c> per target).</para>
        /// </summary>
        /// <param name="worldDirection">Throw direction, world space (need not be normalized).</param>
        /// <param name="speedMetersPerSecond">Initial speed (m/s).</param>
        /// <param name="netItemId"><c>netItemId</c> of the thrown item (§6.6).</param>
        /// <param name="rightHand">Which hand it was thrown from.</param>
        public static void ReportThrow(Vector3 worldDirection, float speedMetersPerSecond, byte netItemId, bool rightHand)
        {
            SendFireEvent(FireEventEntry.KIND_THROW, worldDirection, speedMetersPerSecond, netItemId, rightHand);
        }

        /// <summary>
        /// Shared send path of both event kinds. <b>Silent no-op</b> without a channel (the class
        /// contract): weapons work unchanged in a serverless editor session.
        /// <para>Direction conversion is left to <see cref="ArenaSpace.WorldToArenaDirection"/> — the
        /// Net layer does not know arena space, converting is the caller's (this gate's) job.</para>
        /// </summary>
        private static void SendFireEvent(byte kind, Vector3 worldDirection, float magnitudeMeters,
            byte netItemId, bool rightHand)
        {
            UdpStateChannel channel = ArenaClient.Instance?.UdpChannel;
            if (channel == null)
            {
                return;
            }

            channel.SendFireEvent(kind, rightHand, netItemId,
                ArenaSpace.WorldToArenaDirection(worldDirection), magnitudeMeters);
        }

        /// <summary>
        /// <b>I damaged a network player</b> — the server validates and broadcasts <c>health_update</c>.
        /// <para>
        /// <b>YOU decide the damage</b> (§10.3): there is no weapon table on the server, the number you
        /// send is applied verbatim. Distance falloff, bow draw strength, head multiplier — all
        /// computed here and passed as one number. The server only validates state (is the phase Live,
        /// are shooter and target alive, is friendly fire on) and that the number is usable (NaN/∞/
        /// negative rejected).
        /// </para>
        /// <para>Never reduce health LOCALLY: the target's health comes back from the server. Not a
        /// preference but an absolute rule — no client component holds health.</para>
        /// <para><b>This method draws the hit marker</b> (<see cref="HitMarker"/>): an X at the hit
        /// point, visible only to the shooter. Do not call anything else. ⚠️ The marker says <i>a
        /// report was made</i>, not that damage was applied — the server may reject the hit (friendly
        /// fire off, phase not <c>playing</c>, target already dead; §10.3). Waiting for the
        /// authoritative result would delay the marker by a round trip and <c>health_update</c> does
        /// not carry WHERE the hit landed.</para>
        /// </summary>
        /// <param name="targetPlayerId">Id from <see cref="TryGetTargetPlayerId"/>.</param>
        /// <param name="worldHitPoint">World position of the hit (for FX/stats).</param>
        /// <param name="damage">Damage to apply — <b>must be positive and finite</b>.</param>
        /// <param name="weaponId">Kill feed label; may be empty (only the label is lost).</param>
        public static void ReportHit(int targetPlayerId, Vector3 worldHitPoint, float damage, string weaponId)
        {
            if (targetPlayerId <= 0)
            {
                return;
            }

            if (!float.IsFinite(damage) || damage <= 0f)
            {
                Debug.LogWarning($"[ArenaCombat] Geçersiz hasar ({damage}) — vuruş gönderilmedi. " +
                                 "Hasar pozitif ve sonlu olmalı (sunucu da reddeder).");
                return;
            }

            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return;
            }

            Hit.seq = ++_seq;
            // ⚠️ BOTH target fields are written on BOTH paths: Hit is one reused instance, so an
            // unwritten field leaks the previous hit's target and the server damages the wrong thing.
            Hit.targetPlayerId = targetPlayerId;
            Hit.targetNetId = 0;
            Hit.weaponId = weaponId ?? "";
            Hit.damage = damage;
            Write(Hit.hitPos, ArenaSpace.WorldToArena(worldHitPoint));
            client.Send(Hit);

            // Hit marker — deliberately AFTER the send: drawing an X for an unreported hit would lie
            // to the player (same rationale as CanFire). Runs only on the shooter's screen (this
            // method is called only on the client dealing damage) and has no wire counterpart.
            HitMarker.Shared.Play(worldHitPoint);
        }

        /// <summary>
        /// <b>I damaged a NETWORK OBJECT</b> (breakable cover, target board; §10.10) — the twin of
        /// <see cref="ReportHit"/> on the same <c>hit_report</c> message, only the target field differs.
        /// <para>The server answers with <c>object_state</c>, NOT <c>health_update</c>, and writes no
        /// score/kill: breaking an object is a world state, not a game event. Spawn protection and
        /// friendly fire do not gate it — an object has no team.</para>
        /// <para>Never break the object locally: the decision and the broken flag come from the server
        /// (<c>NetObject</c>).</para>
        /// </summary>
        /// <param name="targetNetId">Id from <see cref="TryGetTargetNetId"/>.</param>
        /// <param name="worldHitPoint">World position of the hit (for FX/stats).</param>
        /// <param name="damage">Damage to apply — <b>must be positive and finite</b>.</param>
        /// <param name="weaponId">Label; may be empty.</param>
        public static void ReportObjectHit(int targetNetId, Vector3 worldHitPoint, float damage, string weaponId)
        {
            if (targetNetId <= 0)
            {
                return;
            }

            if (!float.IsFinite(damage) || damage <= 0f)
            {
                Debug.LogWarning($"[ArenaCombat] Geçersiz hasar ({damage}) — vuruş gönderilmedi. " +
                                 "Hasar pozitif ve sonlu olmalı (sunucu da reddeder).");
                return;
            }

            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return;
            }

            Hit.seq = ++_seq;
            // ⚠️ BOTH target fields are written on BOTH paths: Hit is one reused instance, so an
            // unwritten field leaks the previous hit's target and the server damages the wrong thing.
            Hit.targetNetId = targetNetId;
            Hit.targetPlayerId = 0;
            Hit.weaponId = weaponId ?? "";
            Hit.damage = damage;
            Write(Hit.hitPos, ArenaSpace.WorldToArena(worldHitPoint));
            client.Send(Hit);

            // Same rationale as ReportHit: the marker says a report was made, not that damage landed.
            HitMarker.Shared.Play(worldHitPoint);
        }

        /// <summary>
        /// Shortcut for hitscan weapons: reports the hit and returns <c>true</c> when the raycast
        /// target is a network player.
        /// <para>
        /// <c>false</c> means the target is not a network PLAYER — it does NOT mean no damage: a network
        /// object (§10.10) is reported from here too and still returns <c>false</c>. The return value is
        /// only a PRESENTATION decision:
        /// <code>
        /// if (Physics.Raycast(muzzle.position, dir, out var hit, range))
        /// {
        ///     ArenaCombat.ReportImpact(hit);   // what the surface leaves behind
        ///     ArenaCombat.ReportRaycastHit(hit, damage, "ok");
        /// }
        /// </code>
        /// ⚠️ Choosing blood vs sparks is NOT the caller's job any more — that is what
        /// <see cref="ReportImpact"/> resolves from the surface.
        /// </para>
        /// </summary>
        public static bool ReportRaycastHit(in RaycastHit hit, float damage, string weaponId)
        {
            if (TryGetTargetPlayerId(hit.collider, out int playerId))
            {
                ReportHit(playerId, hit.point, damage, weaponId);
                return true;
            }

            // A network object takes damage too (§10.10) but is NOT a player: the report goes out and the
            // return value stays false, so every caller's blood/spark choice keeps its old meaning.
            if (TryGetTargetNetId(hit.collider, out int netId))
            {
                ReportObjectHit(netId, hit.point, damage, weaponId);
            }

            return false;
        }

        /// <summary>What the round LEAVES on what it hit: the surface's particles and sound
        /// (<see cref="SurfaceImpactFx"/>).
        /// <para>Sits next to <see cref="ReportHit"/> for the same reason: a new damage source (bow,
        /// axe, blast) gets impacts by calling one method, instead of each one growing its own
        /// prefab field and its own <c>Instantiate</c>. Purely local decoration — nothing here
        /// touches the wire.</para>
        /// <para>Independent of damage on purpose: it fires for scenery too, and a shot that hits a
        /// wall must look the same whether or not anything took damage.</para></summary>
        public static void ReportImpact(in RaycastHit hit)
        {
            SurfaceImpactFx.Shared.Play(hit);
        }

        /// <summary>
        /// <b>Area effect</b> (bomb, grenade, shockwave): reports a SEPARATE hit for EVERY network
        /// player AND network object (§10.10) in radius — there is no "area damage" message in the
        /// protocol (§10.3), an area effect means n <c>hit_report</c>s.
        /// <para>
        /// Damage falls off linearly with distance: <paramref name="damage"/> at the centre,
        /// <paramref name="damage"/> × <paramref name="edgeScale"/> at the edge. At most ONE hit per
        /// player (a body has several hit boxes) and one per object.
        /// </para>
        /// <para>⚠️ <b>Order with cover: absorb first, fall off second.</b> Breakable cover is
        /// subtracted from the CENTRE damage and the falloff then runs on the remainder — the blast
        /// spends its energy on the cover, and what survives travels the same curve it always would.
        /// Falling off first and subtracting after charges the cover against an already-reduced
        /// number, and a bomb that provably breaks a cover reaches nobody behind it
        /// (Docs/Gelistirici/Yapma-Listesi.md, "Alan hasarı ve siper").</para>
        /// <para>⚠️ <b>The local player is NOT in this list and cannot be</b>: player targets are found
        /// from <see cref="RemoteHitBox"/> colliders and the local rig carries none. Self damage goes
        /// through <see cref="ReportAreaSelfHit"/>.</para>
        /// </summary>
        /// <param name="layerMask">Layers to look for targets on; <c>0</c> means
        /// <see cref="ArenaLayers.AreaTargetMask"/>. Triggers never match.</param>
        /// <param name="requireLineOfSight">Make cover count: solid cover between the target and the
        /// centre blocks the blast, breakable cover only ABSORBS its remaining health
        /// (<see cref="TryPenetrateArea"/>). Off by default — a blast wrapping a corner is the old
        /// behaviour and callers that never asked must not change.</param>
        /// <returns>Number of PLAYERS a hit was reported for — network objects are reported but not
        /// counted, since callers read this value for player-facing feedback.</returns>
        public static int ReportAreaHit(Vector3 worldCenter, float radius, float damage, string weaponId,
            float edgeScale = 0.25f, int layerMask = 0, bool requireLineOfSight = false)
        {
            if (radius <= 0f || !float.IsFinite(damage) || damage <= 0f)
            {
                return 0;
            }

            AreaHitOnce.Clear();
            AreaHitObjectsOnce.Clear();

            // 0 = "the damageable layers", not "nothing": no caller ever wants a query that can match
            // no target at all, so the useless value is spent on the useful default.
            int mask = layerMask != 0 ? layerMask : ArenaLayers.AreaTargetMask;

            // ⚠️ Triggers are IGNORED: every damageable collider is solid (ArenaLayers.AreaTargetMask
            // states that invariant), while grab volumes, interaction and boundary volumes are not —
            // and those are what used to fill the buffer.
            int count = Physics.OverlapSphereNonAlloc(worldCenter, radius, OverlapBuffer, mask,
                QueryTriggerInteraction.Ignore);

            if (count >= OverlapBuffer.Length)
            {
                // An ERROR, not a warning: the buffer filling up means damage was NOT reported for
                // some targets — the blast silently did less than it should have.
                Debug.LogError($"[ArenaCombat] Alan etkisi tamponu doldu ({OverlapBuffer.Length}); " +
                               "yarıçapı küçült ya da layerMask ver — HEDEF ATLANMIŞ OLABİLİR.");
            }

            int reported = 0;
            for (int i = 0; i < count; i++)
            {
                Collider collider = OverlapBuffer[i];

                if (TryGetTargetPlayerId(collider, out int playerId))
                {
                    if (!AreaHitOnce.Add(playerId))
                    {
                        continue;
                    }

                    Vector3 playerPoint = collider.ClosestPoint(worldCenter);
                    float through = damage;

                    // ⚠️ The LOS ray goes to the collider's CENTRE, not to the closest point: the closest
                    // point sits ON the surface, and a wall touching the target would read as clear.
                    if (requireLineOfSight)
                    {
                        if (!TryPenetrateArea(worldCenter, collider.bounds.center, 0, out float absorbed))
                        {
                            continue;
                        }

                        through = damage - absorbed;
                    }

                    // ⚠️ Cover is subtracted from the CENTRE damage and falloff runs on what is left —
                    // never the other way round. Falloff first would charge the cover against an
                    // already-reduced number, so a 250 bomb would fail to reach past a 200 cover it
                    // demonstrably breaks (Yapma-Listesi, "Alan hasarı ve siper").
                    float applied = through > 0f
                        ? AreaFalloff(worldCenter, playerPoint, radius, through, edgeScale)
                        : 0f;

                    if (applied <= 0f)
                    {
                        continue;
                    }

                    ReportHit(playerId, playerPoint, applied, weaponId);
                    reported++;
                    continue;
                }

                if (!TryGetTargetNetId(collider, out int netId) || !AreaHitObjectsOnce.Add(netId))
                {
                    continue;
                }

                Vector3 point = collider.ClosestPoint(worldCenter);
                float throughToObject = damage;

                // ⚠️ The target's own netId is passed in: a breakable cover sits on the Obstacle layer
                // itself and would otherwise shadow itself into never taking damage.
                if (requireLineOfSight)
                {
                    if (!TryPenetrateArea(worldCenter, collider.bounds.center, netId, out float absorbed))
                    {
                        continue;
                    }

                    throughToObject = damage - absorbed;
                }

                // Same order as the player branch: subtract cover from the centre damage, then fall off.
                float appliedToObject = throughToObject > 0f
                    ? AreaFalloff(worldCenter, point, radius, throughToObject, edgeScale)
                    : 0f;

                if (appliedToObject <= 0f)
                {
                    continue;
                }

                ReportObjectHit(netId, point, appliedToObject, weaponId);
            }

            return reported;
        }

        /// <summary>
        /// <b>The blast damaged ME</b> — the local player's own share of an area effect.
        /// <para><b>Why this is not part of <see cref="ReportAreaHit"/>:</b> that one finds targets from
        /// <see cref="RemoteHitBox"/> colliders and <b>the local rig has none</b>, so self damage could
        /// never come out of it. Here the distance is measured to the player's own head
        /// (<see cref="WeaponGranter.TryResolveHead"/>, the same point <c>ObstacleViolationProbe</c>
        /// measures) and the falloff formula is shared with the area path.</para>
        /// <para>⚠️ <b>The friendly-fire switch is read on the CLIENT too</b> (§10.3, 5th gate): with it
        /// off the server rejects a self hit anyway, so sending would only print a rejection line per
        /// blast. The decision stays the server's — this is a noise gate, not a rule.</para>
        /// <para>Silent no-op with no connection, no rig or no player id (the class contract).</para>
        /// </summary>
        /// <param name="requireLineOfSight">Same cover rule as <see cref="ReportAreaHit"/>: solid cover
        /// blocks, breakable cover absorbs its remaining health — ducking behind your own crate saves
        /// you by exactly what the crate can still take.</param>
        /// <returns><c>true</c> if a hit was reported for the local player.</returns>
        public static bool ReportAreaSelfHit(Vector3 worldCenter, float radius, float damage,
            string weaponId, float edgeScale = 0.25f, bool requireLineOfSight = false)
        {
            if (radius <= 0f || !float.IsFinite(damage) || damage <= 0f || !ModeRuntime.FriendlyFire)
            {
                return false;
            }

            int playerId = LocalPlayerId;
            if (playerId <= 0 || !WeaponGranter.TryResolveHead(out Vector3 head))
            {
                return false;
            }

            if (Vector3.Distance(worldCenter, head) > radius)
            {
                return false;
            }

            float through = damage;

            if (requireLineOfSight)
            {
                if (!TryPenetrateArea(worldCenter, head, 0, out float absorbed))
                {
                    return false;
                }

                through = damage - absorbed;
            }

            // Same order as ReportAreaHit: cover comes off the centre damage, falloff runs on the rest.
            float applied = through > 0f
                ? AreaFalloff(worldCenter, head, radius, through, edgeScale)
                : 0f;

            if (applied <= 0f)
            {
                return false;
            }

            ReportHit(playerId, head, applied, weaponId);
            return true;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Linear falloff shared by both area paths: <paramref name="damage"/> at the centre,
        /// <c>damage × edgeScale</c> at the radius. ⚠️ One formula, or the thrower would take damage on
        /// a different curve than everyone else.</summary>
        private static float AreaFalloff(in Vector3 worldCenter, in Vector3 point, float radius,
            float damage, float edgeScale)
        {
            float t = Mathf.Clamp01(Vector3.Distance(worldCenter, point) / radius);
            return damage * Mathf.Lerp(1f, Mathf.Clamp01(edgeScale), t);
        }

        /// <summary>
        /// How much of an area effect reaches a target through the cover in front of it — the
        /// blast's answer to "is there something in the way" (Yemek-Kitabi §3 pattern).
        /// <para><b>Solid cover blocks completely; BREAKABLE cover only ABSORBS.</b> A breakable
        /// network object swallows its own remaining health and lets the rest through, so a bomb that
        /// beats a weakened cover still reaches the player behind it, and a broken one (rubble)
        /// swallows nothing at all. Anything that is not a breakable network object — arena geometry,
        /// a <c>maxHp 0</c> object — blocks the whole blast.</para>
        /// <para>⚠️ <c>Obstacle</c> mask ONLY: a maskless ray would stop on the target's own hit box,
        /// on the thrower's hands, on grab volumes — and swallow every hit. With no obstacle layer
        /// configured the gate stays OPEN (fail-open), like the other obstacle rules. The arena's
        /// OUTER walls/floor/ceiling are deliberately not on that layer
        /// (<see cref="ArenaLayers"/>), so they do not stop a blast.</para>
        /// <para>⚠️ A blast going off INSIDE a cover's collider does not see that cover (a ray never
        /// hits the collider it starts in) — it is then cover for nobody, which is the same answer
        /// the old line-of-sight gate gave.</para>
        /// <para>⚠️ The absorbed amount is the client's MIRROR of the cover's health
        /// (<c>NetObject.Hp</c>, last <c>object_state</c>). Damage is client-computed and applied by
        /// the server verbatim (§10.3), so two blasts inside one round trip both read the same
        /// health — the second over-penetrates by design rather than waiting for a reply.</para>
        /// </summary>
        /// <param name="ignoreNetId">The target's own <c>netId</c> — an object never shadows itself;
        /// <c>0</c> for a player target.</param>
        /// <param name="absorbed">Damage swallowed by breakable cover on the way; only meaningful
        /// when this returns <c>true</c>. ⚠️ Subtract it from the <b>centre</b> damage, then apply
        /// falloff to what is left — see the order note on <see cref="ReportAreaHit"/>.</param>
        /// <returns><c>false</c> = nothing gets through.</returns>
        private static bool TryPenetrateArea(Vector3 worldCenter, Vector3 targetPoint, int ignoreNetId,
            out float absorbed)
        {
            absorbed = 0f;

            int obstacleMask = ArenaLayers.ObstacleMask;
            if (obstacleMask == 0)
            {
                return true;
            }

            Vector3 delta = targetPoint - worldCenter;
            float distance = delta.magnitude;
            if (distance < 1e-4f)
            {
                return true;
            }

            int count = Physics.RaycastNonAlloc(new Ray(worldCenter, delta / distance), BlockerBuffer,
                distance, obstacleMask, QueryTriggerInteraction.Ignore);

            if (count >= BlockerBuffer.Length)
            {
                // Blocked, not let through: an unread blocker may be the solid one, and inventing
                // damage through a wall is worse than losing a blast.
                Debug.LogError($"[ArenaCombat] Görüş hattı tamponu doldu ({BlockerBuffer.Length}); " +
                               "patlama engellenmiş sayıldı — hedefe hasar GİTMEDİ.");
                return false;
            }

            AreaBlockersOnce.Clear();
            for (int i = 0; i < count; i++)
            {
                // The collider may sit on any child of the object — search upward, like the target
                // resolvers do.
                NetObject cover = BlockerBuffer[i].collider.GetComponentInParent<NetObject>();
                if (cover == null || cover.NetId <= 0)
                {
                    // Plain geometry: a wall is a wall.
                    return false;
                }

                if (cover.NetId == ignoreNetId || !AreaBlockersOnce.Add(cover.NetId))
                {
                    // The target itself, or a second collider of a cover already counted.
                    continue;
                }

                // A network object that cannot be broken is as solid as the wall next to it.
                if (cover.MaxHp <= 0f || cover.GetComponent<BreakableObject>() == null)
                {
                    return false;
                }

                if (cover.IsBroken)
                {
                    continue;
                }

                absorbed += Mathf.Max(0f, cover.Hp);
            }

            return true;
        }

        private static void Write(float[] target, in Vector3 value)
        {
            target[0] = value.x;
            target[1] = value.y;
            target[2] = value.z;
        }
    }
}
