using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Combat
{
    /// <summary>The ONE place that puts a weapon in the player's hand, fed by two sources; the mode
    /// rule (<see cref="ModeWeaponSource"/>) decides which one runs. <list type="number">
    /// <item><b>RandomGrant</b> (§10.5 <c>weaponSource:"random"</c>): SWEEPS scene weapons away and
    /// gives every hand holding grip a random loadout weapon; releasing DESTROYS it and pressing
    /// again yields a NEW one (<see cref="WeaponGrantKind.Disposable"/>).</item> <item><b>Frame</b>
    /// (<see cref="WeaponFrame"/>, <see cref="ModeWeaponSource.WeaponCanvas"/>): the weapon stays
    /// frozen in its frame and the player SELECTS it from up to <c>maxGrabDistance</c>
    /// (<see cref="SelectWeapon"/>); grip summons a CLONE, releasing only HIDES it, so the same
    /// instance returns with the same ammo (<see cref="WeaponGrantKind.Persistent"/>). Two-handed:
    /// ONE clone per player; one-handed: one clone PER HAND (dual wield).</item> </list>
    /// <para>⚠️ There is NO component that places weapons in the scene and none is written — with
    /// <c>WeaponCanvas</c>, placement is an ARENA decision made when the map is designed. This
    /// class only knows the TAKING path; an empty scene simply gives nothing and that is not an
    /// error.</para>
    /// <para>Both paths live here because this class already resolves the rig/hand anchor, polls
    /// grip, revokes on death and cleans up on scene change. A second singleton would create a
    /// second rig discovery path — two searches can find different rigs on different frames and the
    /// weapon would jump between hands (see <see cref="ResolveHandAnchor"/>).</para>
    /// <para>Self-bootstrapping singleton (<see cref="PlayerCombatState"/> pattern): a scene
    /// component would add a manual setup step to every new arena.</para>
    /// <para>Both paths close themselves for the admin spectator: <c>AdminSpectator</c> disables
    /// the rig, so <see cref="OVRCameraRig"/> is not found (the search skips inactive objects) → no
    /// hand anchor → no weapon. The sweep ignores role: a scene weapon must not stand on the
    /// spectator's screen in FFA either.</para></summary>
    public class WeaponGranter : MonoBehaviour
    {
        /// <summary>Grip threshold on the analog <c>PrimaryHandTrigger</c>; a half press does not
        /// count as held, so the weapon does not flicker.</summary>
        private const float GripThreshold = 0.55f;

        private const float RigRescanSeconds = 1f;

        public static WeaponGranter Instance { get; private set; }

#if UNITY_EDITOR
        /// <summary>EDITOR ONLY (dev sandbox): hand out the loadout IN ORDER instead of randomly,
        /// one weapon per grip press, so every weapon can be reviewed deterministically.
        /// <para>⚠️ Production behaviour is UNCHANGED: only <c>DevSession</c> writes this flag, it
        /// never ships, and the mode rule stays random — randomness is a game design decision in
        /// FFA and is not bent for testing convenience.</para></summary>
        public static bool SequentialGrant { get; set; }

        private int _sequentialIndex = -1;
#endif

        private Weapon _grantedLeft;
        private Weapon _grantedRight;

        /// <summary>Weapon selected from a frame — ONE per player, reset per map (see
        /// <see cref="HandleSceneLoaded"/>).</summary>
        private WeaponDefinition _selected;

        /// <summary>PERSISTENT clone of the selected weapon, PER HAND: released it is hidden, not
        /// destroyed — that is where "the same weapon returns with the same ammo" comes from.
        /// <para>⚠️ Two slots does NOT mean two clones: a second clone exists only when the selected
        /// weapon is ONE-handed (dual wield). A two-handed weapon has a single clone that MOVES
        /// between slots when hands swap — recreating it would reset the magazine.</para></summary>
        private Weapon _summonedLeft;
        private Weapon _summonedRight;

        /// <summary>Secondary-grip LATCH: which hand is bound to which weapon's front socket.
        /// <para>⚠️ ESTABLISHING the link and MAINTAINING it are different rules, and this field is
        /// that distinction. Establishing checks distance
        /// (<see cref="Weapon.IsHandOnSecondaryGrip"/>, exactly what the socket sphere promises);
        /// maintaining checks the grip button ONLY. A distance check on maintenance would break the
        /// link while the player still holds grip: the two-handed solver does not MOVE the weapon
        /// toward the second hand, it only aims (<see cref="ItemGripSolver"/>), so the socket-to-
        /// controller gap equals |hand spacing − weapon grip spacing| and exceeds the ~0.10 m
        /// acceptance radius as soon as the player extends or pulls in their arms.</para></summary>
        private Weapon _secondaryLatchWeapon;
        private OVRInput.Controller _secondaryLatchHand = OVRInput.Controller.None;

        private OVRCameraRig _rig;
        private float _nextRigScanAt;
        private bool _aliveSubscribed;

        /// <summary>Objects HIDDEN by the sweep, kept so a rule change can restore them. On scene
        /// change the references die (Unity null) and the list is rebuilt.</summary>
        private readonly List<GameObject> _hiddenObjects = new List<GameObject>();

        private readonly List<WeaponDefinition> _pool = new List<WeaponDefinition>();

        private bool _swept;
        private bool _loadoutWarned;
        private bool _summonPrefabWarned;

        /// <summary>Last applied weapon source; used only to reset frame state ON TRANSITION
        /// (see <see cref="ApplyRules"/>).</summary>
        private bool _appliedRandomGrant;

        /// <summary>Last applied "the mode distributes weapons" state
        /// (<see cref="ModeDistributesWeapons"/>).
        /// <para>⚠️ <see cref="_appliedRandomGrant"/> alone is NOT enough: going from a staged arena
        /// into an FFA match keeps the source at <c>random</c> and only this flag changes. Untracked,
        /// the weapon the player picked from a rack during staging would carry into the match and
        /// silently break the "mode hands out a random weapon" rule.</para></summary>
        private bool _appliedModeDistributes;

        // ------------------------------------------------------------- hands / selection gate

        /// <summary>Weapon in the LEFT hand right now, whatever put it there — frame clone, random
        /// grant, or a scene weapon grabbed by hand; <c>null</c> = the hand is empty.</summary>
        public static Weapon LeftHandWeapon => HeldWeaponIn(OVRInput.Controller.LTouch);

        /// <summary>Weapon in the RIGHT hand right now — see <see cref="LeftHandWeapon"/>.</summary>
        public static Weapon RightHandWeapon => HeldWeaponIn(OVRInput.Controller.RTouch);

        /// <summary>May the player SELECT a weapon from a frame right now: only with BOTH hands
        /// empty. Read by <see cref="WeaponFrame"/> (ISDK candidate gate + aim ray) and by
        /// <see cref="SelectWeapon"/>.
        /// <para>⚠️ This is an END-OF-FRAME SNAPSHOT (<see cref="LateUpdate"/>), never a live query,
        /// and that is the whole reason it is a field. One grip press SELECTS at the frame and
        /// SUMMONS the held clone on the SAME frame, and Unity does not order ISDK's select against
        /// this component's <c>Update</c>. Queried live, the summon could fill the hand first and
        /// close the gate on the very frame the player is selecting: the ray appears, the press does
        /// nothing, and the player stays locked to their first weapon forever. Describing the
        /// previous frame's end instead, a released grip reopens selection before the next press is
        /// judged — swapping at the rack costs one grip release, which is the intended
        /// cost.</para></summary>
        public static bool CanSelectWeapon { get; private set; } = true;

        /// <summary>The weapon a hand holds, scanned from <see cref="Weapon.Active"/> so EVERY
        /// source counts — a stowed frame clone is inactive and drops out of that list by itself.
        /// <para>⚠️ An unresolved main hand (Editor session, no controller) counts for BOTH hands: a
        /// full hand we cannot name is still a full hand, and the gate built on this must not open
        /// on it.</para></summary>
        private static Weapon HeldWeaponIn(OVRInput.Controller hand)
        {
            for (int i = 0; i < Weapon.Active.Count; i++)
            {
                Weapon weapon = Weapon.Active[i];
                if (weapon == null || !weapon.IsHeld)
                {
                    continue;
                }

                if (weapon.MainHand == hand || weapon.MainHand == OVRInput.Controller.None)
                {
                    return weapon;
                }
            }

            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[WeaponGranter]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<WeaponGranter>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Static, so with domain reload disabled the field initializer does not run again.
            CanSelectWeapon = true;

            // Persistent singleton: subscribe in Awake/OnDestroy so rule/scene events are not
            // missed while the object is disabled (PlayerCombatState pattern).
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ModeRuntime.Changed += HandleRulesChanged;
            NetEvents.OnCountdown += HandleCountdown;
            ApplyRules();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            ModeRuntime.Changed -= HandleRulesChanged;
            NetEvents.OnCountdown -= HandleCountdown;

            if (_aliveSubscribed && PlayerCombatState.Instance != null)
            {
                PlayerCombatState.Instance.AliveChanged -= HandleAliveChanged;
            }

            _aliveSubscribed = false;
            Instance = null;
        }

        private void Update()
        {
            if (Instance != this)
            {
                return;
            }

            // PlayerCombatState bootstraps after scene load, so it may not exist during Awake
            // (same lazy subscription pattern as Weapon.TrySubscribeAlive).
            if (!_aliveSubscribed)
            {
                TrySubscribeAlive();
            }

            // The frame path runs when the mode's source is the rack OR the player has SELECTED
            // one (only possible in the free playground: racks are hidden in a running match).
            // ⚠️ RevokeAll is unconditional: in the free playground both paths are open, so at the
            // moment of selection the player may still hold a randomly granted weapon and would
            // otherwise carry two. On the frame source it is already a no-op.
            if (!IsRandomGrant || _selected != null)
            {
                RevokeAll();
                TickFrameSummon();
                return;
            }

            // The sweep happens on scene load; if the rules arrived AFTER the scene (late join)
            // it is compensated here. ⚠️ No sweep in the free playground (see SweepScene).
            if (!_swept && ModeDistributesWeapons)
            {
                SweepScene();
            }

            if (!CanHoldWeapon)
            {
                RevokeAll();
                return;
            }

            OVRCameraRig rig = ResolveRig();
            if (rig == null)
            {
                RevokeAll();
                return;
            }

            // ⚠️ Each hand is also told about the OTHER hand's weapon: if that one is two-handed
            // this hand gets no second weapon, it becomes the front-grip candidate (see TickHand).
            TickHand(OVRInput.Controller.LTouch, rig.leftHandAnchor, ref _grantedLeft, _grantedRight);
            TickHand(OVRInput.Controller.RTouch, rig.rightHandAnchor, ref _grantedRight, _grantedLeft);
        }

        /// <summary>Takes the <see cref="CanSelectWeapon"/> snapshot once both grant paths and every
        /// grab have settled for this frame — see that property for why the gate may not be read
        /// live.</summary>
        private void LateUpdate()
        {
            if (Instance != this)
            {
                return;
            }

            CanSelectWeapon = LeftHandWeapon == null && RightHandWeapon == null;
        }

        // ------------------------------------------------------------------- rules

        private static bool IsRandomGrant => ModeRuntime.Weapons == ModeWeaponSource.RandomGrant;

        /// <summary>No match is set up — the player is in the free playground (lobby profile,
        /// §10.7).
        /// <para>⚠️ This does NOT mean "we are in the lobby SCENE": selecting an arena from the
        /// lobby STAGES it, so everyone moves to the arena while the type stays <c>lobby</c> and
        /// the rules keep the lobby profile (a match starts only with <c>start_match</c>). The free
        /// playground is usually an arena WITH weapon racks.</para>
        /// <para>⚠️ Read from the RULE, not from <c>modeId</c> (§10.5: no <c>if (modeId ==
        /// "lobby")</c> chain on the client). The lobby profile is uniquely identified by
        /// <c>random</c> + <c>fireWhilePaused</c>: a running FFA match is also <c>random</c> but
        /// has no free fire.</para></summary>
        private static bool IsFreePlayground => IsRandomGrant && ModeRuntime.FireWhilePaused;

        /// <summary>The MODE hands out weapons, so scene racks must be hidden: source is
        /// <c>random</c> AND a match is set up (FFA). The free playground is excluded.</summary>
        private static bool ModeDistributesWeapons => IsRandomGrant && !IsFreePlayground;

        /// <summary>May the player hold a weapon right now: must be CALIBRATED.
        /// <para>⚠️ DEATH IS NOT A GATE HERE and is not added back. A dead player keeps the weapon
        /// in hand and may take one from a frame: in a round-based mode death parks the player in
        /// the ghost state for the whole regroup + countdown (no revive), so taking the gun away
        /// would mean minutes of empty hands and a second walk to the rack every round.</para>
        /// <para><b>Damage stays impossible and is not re-checked here.</b> The trigger is closed
        /// by <c>PlayerCombatState.CanFire</c> (ALIVE + phase/<c>fireWhilePaused</c>), and even if
        /// a round left the muzzle the server drops <c>hit_report</c> outside <c>playing</c>
        /// (§10.3) — holding is a PRESENTATION state, the damage gate is elsewhere.</para>
        /// <para>Uncalibrated stays closed: such a player can fire in NO phase (§10.6,
        /// <c>PlayerCombatState.CanFire</c>) and cannot revive either, so the gun would never come
        /// alive in their hands.</para>
        /// <para>⚠️ BOTH delivery paths go through this single gate (random grant and frame clone)
        /// — closing one and leaving the other open would break the rule per mode. The third path,
        /// frame SELECTION, is closed separately in <see cref="WeaponFrame.Filter"/>.</para>
        /// <para>Calibration is POLLED per frame rather than subscribed: this loop already runs
        /// every frame, so the weapon leaves the hand on the frame the state
        /// breaks.</para></summary>
        private static bool CanHoldWeapon => CalibrationState.IsCalibrated;

        private void HandleRulesChanged() => ApplyRules();

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // New scene = new weapons, new rig.
            _swept = false;
            _hiddenObjects.Clear();
            _rig = null;

            // ⚠️ Granted weapons are parked under the DDOL root (see Grant): they do not die on a
            // scene change and must be destroyed explicitly — nulling the reference would leave the
            // old scene's weapon in hand.
            RevokeAll();

            // ⚠️ The frame selection resets PER MAP: "the same weapon all round" is scoped to one
            // map. Carrying it over would let the player use a weapon that map does not have.
            _selected = null;
            DestroySummoned();

            ApplyRules();
        }

        /// <summary>The single apply point for a rule change: sweep on entering RandomGrant,
        /// restore and clear hands on leaving.</summary>
        private void ApplyRules()
        {
            // Reset the frame side when the SOURCE changed, so two sources never fill a hand at
            // once. ⚠️ The condition is deliberate: ModeRuntime.Changed can be raised repeatedly
            // with the same source (every load_match), and resetting unconditionally would silently
            // refill a half magazine mid-match.
            bool random = IsRandomGrant;
            bool distributes = ModeDistributesWeapons;

            if (random != _appliedRandomGrant || distributes != _appliedModeDistributes)
            {
                _appliedRandomGrant = random;
                _appliedModeDistributes = distributes;
                _selected = null;
                DestroySummoned();
            }

            if (distributes)
            {
                SweepScene();
                return;
            }

            RestoreScene();
            RevokeAll();
        }

        // ----------------------------------------------------------------- scene sweep

        /// <summary>Hides weapons standing in the scene: if the mode hands out weapons, scene
        /// instances must not look takeable.
        /// <para>⚠️ Runs only with a match SET UP (<see cref="ModeDistributesWeapons"/>), never
        /// while there is none. Staging an arena from the lobby keeps the lobby rule shape
        /// (<c>random</c>), so a gate of "is the source random" alone would hide the racks before
        /// the match and leave the player with neither a weapon nor free fire — the whole point of
        /// the lobby profile. In the free playground both paths stay open: pick from a rack, or get
        /// a random loadout weapon.</para>
        /// <para>⚠️ Base zones are NOT here and are not added back — strip visibility depends on
        /// the team mode, not the weapon source, and belongs to
        /// <see cref="Arena.BaseZoneVisibility"/>.</para>
        /// <para>Granted weapons are EXEMPT (<see cref="Weapon.IsGranted"/>): the sweep looks for
        /// <c>Weapon</c> components and a granted instance is one.</para></summary>
        private void SweepScene()
        {
            _swept = true;

            Weapon[] weapons = FindObjectsByType<Weapon>(FindObjectsSortMode.None);
            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon weapon = weapons[i];
                if (weapon == null || weapon.IsGranted || !weapon.gameObject.activeSelf)
                {
                    continue;
                }

                weapon.gameObject.SetActive(false);
                _hiddenObjects.Add(weapon.gameObject);
            }
        }

        /// <summary>Undoes the sweep (the rule left RandomGrant). After a scene change the list is
        /// already empty; dead references are skipped via Unity null.</summary>
        private void RestoreScene()
        {
            for (int i = 0; i < _hiddenObjects.Count; i++)
            {
                if (_hiddenObjects[i] != null)
                {
                    _hiddenObjects[i].SetActive(true);
                }
            }

            _hiddenObjects.Clear();
            _swept = false;
        }

        // ------------------------------------------------------------ granting weapons

        /// <summary>One frame of a hand's state: grip held → a weapon in hand, otherwise none.
        /// <para>⚠️ If the OTHER hand holds a TWO-HANDED weapon this hand gets NO second weapon; it
        /// becomes the front-grip candidate (grip held + controller inside the socket). Otherwise
        /// the player would hold two rifles in FFA with no way to steady either.</para></summary>
        private void TickHand(OVRInput.Controller hand, Transform anchor, ref Weapon granted,
            Weapon otherHandWeapon)
        {
            if (anchor == null)
            {
                Revoke(ref granted);
                return;
            }

            bool gripHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, hand) >= GripThreshold;

            if (IsTwoHandHeld(otherHandWeapon))
            {
                Revoke(ref granted);
                otherHandWeapon.SetSecondaryHand(ResolveSecondaryHand(otherHandWeapon, hand, gripHeld));
                return;
            }

            if (!gripHeld)
            {
                Revoke(ref granted);
                return;
            }

            // Still in hand (and not externally destroyed) — nothing to do.
            if (granted != null)
            {
                return;
            }

            granted = Grant(hand);
        }

        /// <summary>Gives a loadout weapon to a hand. Repeats are NOT prevented: in a two-weapon
        /// pool a "never the same twice" rule would turn randomness into round-robin.</summary>
        private Weapon Grant(OVRInput.Controller hand)
        {
            WeaponDefinition definition = PickFromLoadout();
            if (definition == null || definition.Prefab == null)
            {
                WarnMissingLoadout(definition);
                return null;
            }

            // ⚠️ Instantiated under this singleton (the DDOL root), NOT under the hand anchor;
            // Weapon.ApplyCanonicalGrip drives its pose every frame — same pattern as the
            // Persistent clone. As an anchor child, the world pose written by the two-handed solver
            // would collide with the parent transform.
            GameObject instance = Instantiate(definition.Prefab, transform, false);
            instance.name = definition.Prefab.name;

            // First-frame pose: the canonical grip takes over in LateUpdate; without this the
            // weapon would appear at the world origin for one frame.
            if (TryResolvePalm(hand, out Pose palm))
            {
                SolveInitialGrip(definition, hand, palm,
                    out Vector3 position, out Quaternion rotation);
                instance.transform.SetPositionAndRotation(position, rotation);
            }

            var weapon = instance.GetComponent<Weapon>();
            if (weapon == null)
            {
                Debug.LogWarning($"[WeaponGranter] '{definition.name}' prefabında Weapon bileşeni yok; silah verilemedi.");
                Destroy(instance);
                return null;
            }

            DetachFromPhysicsAndGrab(instance);

            // No need to hide the frame here: WeaponFrame listens to HeldChanged, which GrantTo
            // raises below — the rule lives in one place.
            weapon.GrantTo(hand, WeaponGrantKind.Disposable);
            return weapon;
        }

        /// <summary>A disposable weapon sits FIXED in the hand: grab and physics paths are closed.
        /// Otherwise it could be grabbed by the other hand or from a distance, fall out under
        /// gravity, or catch the player's own ray with its colliders.
        /// <para>⚠️ The frame clone's counterpart is <see cref="PrepareSummonedClone"/>, which
        /// applies the SAME grab gate but leaves colliders alone. Second-hand sensing is the
        /// granter's job on both paths (<see cref="Weapon.SetSecondaryHand"/>).</para></summary>
        private static void DetachFromPhysicsAndGrab(GameObject instance)
        {
            var interactables = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < interactables.Length; i++)
            {
                MonoBehaviour behaviour = interactables[i];
                // ISDK's grab surface: Grabbable + controller line + hand line. Weapon itself and
                // audio/FX stay ENABLED. ⚠️ Both lines are closed: the ISDK rig picks which one
                // runs, and missing one would leave a granted weapon grabbable.
                if (behaviour is Grabbable || behaviour is GrabInteractable ||
                    behaviour is DistanceGrabInteractable ||
                    behaviour is HandGrabInteractable || behaviour is DistanceHandGrabInteractable)
                {
                    behaviour.enabled = false;
                }
            }

            var bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i].isKinematic = true;
                bodies[i].detectCollisions = false;
                bodies[i].useGravity = false;
            }

            var colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        /// <summary>Picks a weapon from the active mode's loadout (definitions without a prefab are
        /// excluded). Random normally; sequential in the dev sandbox
        /// (<see cref="SequentialGrant"/>).</summary>
        private WeaponDefinition PickFromLoadout()
        {
            _pool.Clear();

            GameCatalog catalog = Resources.Load<GameCatalog>("GameCatalog");
            ModeDefinition mode = catalog != null ? catalog.FindMode(ModeRuntime.ModeId) : null;
            WeaponDefinition[] loadout = mode != null ? mode.Loadout : null;
            if (loadout == null)
            {
                return null;
            }

            for (int i = 0; i < loadout.Length; i++)
            {
                if (loadout[i] != null && loadout[i].Prefab != null)
                {
                    _pool.Add(loadout[i]);
                }
            }

            if (_pool.Count == 0)
            {
                return null;
            }

#if UNITY_EDITOR
            if (SequentialGrant)
            {
                // Wraps on the pool length, so the index stays valid if the loadout changed
                // between Play sessions.
                _sequentialIndex = (_sequentialIndex + 1) % _pool.Count;
                return _pool[_sequentialIndex];
            }
#endif

            return _pool[Random.Range(0, _pool.Count)];
        }

        private void WarnMissingLoadout(WeaponDefinition definition)
        {
            if (_loadoutWarned)
            {
                return;
            }

            _loadoutWarned = true;
            Debug.LogWarning(definition == null
                ? $"[WeaponGranter] '{ModeRuntime.ModeId}' modunun loadout'u boş (ya da katalogda yok); " +
                  "rastgele silah verilemiyor — ModeDefinition.loadout'a prefablı WeaponDefinition ekle."
                : $"[WeaponGranter] '{definition.name}' tanımının prefabı yok; rastgele silah verilemiyor.");
        }

        private void Revoke(ref Weapon granted)
        {
            if (granted != null)
            {
                Destroy(granted.gameObject);
            }

            granted = null;
        }

        private void RevokeAll()
        {
            Revoke(ref _grantedLeft);
            Revoke(ref _grantedRight);
        }

        // -------------------------------------------------------------- framed weapon

        /// <summary>Called by <see cref="WeaponFrame"/>: selects the player's weapon for this map.
        /// <para>Selecting the same weapon again does NOTHING — the clone and its magazine are
        /// kept, otherwise ammo would silently refill every time the player aimed at the frame. A
        /// different weapon destroys the old clone; the new one is born FULL on the next
        /// grip.</para>
        /// <para>⚠️ Selection needs BOTH hands EMPTY (<see cref="CanSelectWeapon"/>): with a weapon
        /// already in hand the free hand may neither pick a new one nor draw a ray at a frame. The
        /// gate is repeated here even though <see cref="WeaponFrame.Filter"/> already drops the
        /// frame from ISDK's candidate list — this is the only public entry point, and a caller
        /// that is not a frame would otherwise walk straight past the rule.</para>
        /// <para>⚠️ The gate MUST stay a snapshot and must never be rewritten as a live "is a weapon
        /// in hand" check — see <see cref="CanSelectWeapon"/> for the frame-ordering trap that
        /// would otherwise lock the player to their first weapon forever.</para></summary>
        public static void SelectWeapon(WeaponDefinition definition)
        {
            if (Instance == null || definition == null || !CanSelectWeapon)
            {
                return;
            }

            Instance.ApplySelection(definition);
        }

        private static bool IsTwoHandHeld(Weapon weapon)
        {
            return weapon != null && weapon.IsHeld &&
                   weapon.Definition != null && weapon.Definition.IsTwoHanded;
        }

        /// <summary>One frame of the front-grip link: is this hand holding
        /// <paramref name="weapon"/>'s secondary socket.
        /// <para>Two separate rules: ESTABLISHING requires the controller inside the socket's
        /// acceptance radius (<see cref="Weapon.IsHandOnSecondaryGrip"/> — the rule lives on the
        /// weapon and the sphere is drawn with the same figure); MAINTAINING requires only that
        /// grip stays pressed, with no distance or angle test.</para>
        /// <para>⚠️ NEVER restore a distance gate on maintenance. The two-handed solver does not
        /// move the weapon toward the second hand, it only aims (<see cref="ItemGripSolver"/>), so
        /// the socket-to-controller gap equals |hand spacing − weapon grip spacing| and exceeds the
        /// acceptance radius during normal aiming, leaning and turning — the link would break while
        /// the player still holds grip.</para>
        /// <para>The latch is bound to the weapon INSTANCE: destroying it and granting a new one
        /// (release + press) drops the match, so the new weapon's front grip must be taken at the
        /// socket again and the latch never leaks between weapons.</para></summary>
        private OVRInput.Controller ResolveSecondaryHand(Weapon weapon, OVRInput.Controller hand, bool gripHeld)
        {
            bool latched = _secondaryLatchWeapon == weapon && _secondaryLatchHand == hand;
            bool linked = gripHeld && weapon != null &&
                          (latched || weapon.IsHandOnSecondaryGrip(hand));

            if (linked)
            {
                _secondaryLatchWeapon = weapon;
                _secondaryLatchHand = hand;
                return hand;
            }

            // Only releases its OWN latch; another hand's or weapon's latch is not our business.
            if (latched)
            {
                _secondaryLatchWeapon = null;
                _secondaryLatchHand = OVRInput.Controller.None;
            }

            return OVRInput.Controller.None;
        }

        private void ApplySelection(WeaponDefinition definition)
        {
            if (_selected == definition)
            {
                return;
            }

            // ⚠️ No "may they take it" gate here (see SelectWeapon): a selection always applies.
            // The held clone is destroyed and, with grip still pressed, the NEW clone is born next
            // frame — swapping at the rack costs a single frame gap.
            _selected = definition;
            DestroySummoned();
        }

        /// <summary>One frame of the frame path: grip held → the selected weapon's clone is in
        /// hand, otherwise stowed.
        /// <para>Clone count follows the selected weapon's hold mode. Two-handed (rifle): ONE clone
        /// per player — the first pressing hand wins, with both pressed the weapon stays in the
        /// hand that already holds it (else the right), so swapping does not flicker it between
        /// hands; if the free hand's grip is on the front socket, that hand is the second hand.
        /// One-handed (pistol): each hand gets its OWN clone — two instances, two magazines, and
        /// "the same weapon returns with the same ammo" applies per hand.</para>
        /// <para>⚠️ On death the clone is stowed but the SELECTION is KEPT: the revived player
        /// returns with the same weapon (refilled by <see cref="HandleAliveChanged"/>). Clearing
        /// the selection would force a walk back to the frame after every death.</para></summary>
        private void TickFrameSummon()
        {
            if (!CanHoldWeapon)
            {
                StowAllSummoned();
                return;
            }

            if (ResolveRig() == null)
            {
                StowAllSummoned();
                return;
            }

            bool leftHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch) >= GripThreshold;
            bool rightHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch) >= GripThreshold;

            if (!leftHeld && !rightHeld)
            {
                StowAllSummoned();
                return;
            }

            if (_selected == null)
            {
                // Nothing selected from a frame yet: grip does nothing (and logs nothing — this
                // is NORMAL at match start).
                return;
            }

            if (_selected.IsTwoHanded)
            {
                TickTwoHandSummon(leftHeld, rightHeld);
                return;
            }

            TickOneHandSummon(OVRInput.Controller.LTouch, leftHeld);
            TickOneHandSummon(OVRInput.Controller.RTouch, rightHeld);
        }

        /// <summary>Two-handed selection, one clone: pick the main hand; the free hand is the
        /// front-grip candidate.</summary>
        private void TickTwoHandSummon(bool leftHeld, bool rightHeld)
        {
            OVRInput.Controller hand;
            if (leftHeld && rightHeld)
            {
                hand = _summonedLeft != null ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            }
            else
            {
                hand = leftHeld ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            }

            OVRInput.Controller other = Opposite(hand);

            // On a hand swap the SAME instance moves between slots: recreating it would reset ammo.
            Weapon weapon = GetSummoned(hand);
            if (weapon == null)
            {
                weapon = GetSummoned(other);
                SetSummoned(other, null);
            }

            if (weapon == null)
            {
                weapon = CreateSummoned();
                if (weapon == null)
                {
                    return;
                }
            }

            SetSummoned(hand, weapon);
            SummonTo(weapon, hand);

            // ⚠️ The front grip is written AFTER SummonTo: GrantTo clears the second hand, so the
            // reverse order would erase the value on the same frame.
            bool otherGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, other) >= GripThreshold;
            weapon.SetSecondaryHand(ResolveSecondaryHand(weapon, other, otherGrip));
        }

        private void TickOneHandSummon(OVRInput.Controller hand, bool gripHeld)
        {
            if (!gripHeld)
            {
                StowSummoned(hand);
                return;
            }

            Weapon weapon = GetSummoned(hand);
            if (weapon == null)
            {
                weapon = CreateSummoned();
                if (weapon == null)
                {
                    return;
                }

                SetSummoned(hand, weapon);
            }

            SummonTo(weapon, hand);
        }

        private void SummonTo(Weapon weapon, OVRInput.Controller hand)
        {
            if (weapon.GrantedHand == hand && weapon.gameObject.activeSelf)
            {
                return; // already in that hand
            }

            // First-frame pose: the clone is parked under the DDOL root, so without this it would
            // appear at the world origin for one frame (ApplyCanonicalGrip takes over in LateUpdate).
            if (TryResolvePalm(hand, out Pose palm))
            {
                SolveInitialGrip(_selected, hand, palm,
                    out Vector3 position, out Quaternion rotation);
                weapon.transform.SetPositionAndRotation(position, rotation);
            }

            weapon.gameObject.SetActive(true);
            weapon.GrantTo(hand, WeaponGrantKind.Persistent);
        }

        private Weapon GetSummoned(OVRInput.Controller hand)
        {
            return hand == OVRInput.Controller.LTouch ? _summonedLeft : _summonedRight;
        }

        private void SetSummoned(OVRInput.Controller hand, Weapon weapon)
        {
            if (hand == OVRInput.Controller.LTouch)
            {
                _summonedLeft = weapon;
            }
            else
            {
                _summonedRight = weapon;
            }
        }

        private static OVRInput.Controller Opposite(OVRInput.Controller hand)
        {
            return hand == OVRInput.Controller.LTouch
                ? OVRInput.Controller.RTouch
                : OVRInput.Controller.LTouch;
        }

        /// <summary>Creates the persistent clone from the selected definition's prefab.
        /// <para>⚠️ The clone parks under <see cref="transform"/> (the DDOL root), NEVER under the
        /// hand anchor: <c>Weapon.ApplyCanonicalGrip</c> drives its pose. Under the anchor it would
        /// die with the rig on a scene change and break the "one instance that hides and returns"
        /// rule.</para>
        /// <para>The clone's <see cref="WeaponFrame"/> GameObject is destroyed — the frame belongs
        /// to the SOURCE weapon in the scene, never to a held one.</para></summary>
        private Weapon CreateSummoned()
        {
            if (_selected == null || _selected.Prefab == null)
            {
                // Warn once: this runs on EVERY frame grip is held, so unconditional logging would
                // drown the console.
                if (!_summonPrefabWarned)
                {
                    _summonPrefabWarned = true;
                    Debug.LogWarning($"[WeaponGranter] '{(_selected != null ? _selected.name : "(seçim yok)")}' " +
                                     "tanımının prefabı yok; çerçeveden seçilen silah çağrılamıyor.");
                }

                return null;
            }

            GameObject instance = Instantiate(_selected.Prefab, transform, false);
            instance.name = _selected.Prefab.name;

            var weapon = instance.GetComponent<Weapon>();
            if (weapon == null)
            {
                Debug.LogWarning($"[WeaponGranter] '{_selected.name}' prefabında Weapon bileşeni yok; " +
                                 "çerçeve silahı çağrılamadı.");
                Destroy(instance);
                return null;
            }

            var frame = instance.GetComponentInChildren<WeaponFrame>(true);
            if (frame != null)
            {
                frame.DetachForClone();
                Destroy(frame.gameObject);
            }

            PrepareSummonedClone(instance);
            return weapon;
        }

        /// <summary>Prepares the clone for the hand: physics and ALL grabbing are disabled.
        /// <para>⚠️ Grab components are deliberately NOT enabled: on a granted weapon the granter's
        /// grip poll is the ONLY source of second-hand sensing
        /// (<see cref="Weapon.SetSecondaryHand"/>). Leaving the ISDK line open too would make one
        /// hand "holding" from two sources, write <c>GRIP_LINKED</c> by two paths, and leave the
        /// state half-set when one releases.</para>
        /// <para>Nothing to do for the front-grip indicator: <see cref="Weapon"/> drives it and only
        /// on a held weapon.</para></summary>
        private static void PrepareSummonedClone(GameObject instance)
        {
            var behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour is Grabbable || behaviour is GrabInteractable ||
                    behaviour is HandGrabInteractable ||
                    behaviour is DistanceGrabInteractable || behaviour is DistanceHandGrabInteractable)
                {
                    behaviour.enabled = false;
                }
            }

            var bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                // Kinematic, no gravity: the canonical grip drives the pose and physics racing it
                // would drop the weapon.
                bodies[i].isKinematic = true;
                bodies[i].useGravity = false;
            }
        }

        /// <summary>Takes a hand's clone away: HIDES it, never destroys, so ammo and state
        /// survive.</summary>
        private void StowSummoned(OVRInput.Controller hand)
        {
            Weapon weapon = GetSummoned(hand);
            if (weapon == null)
            {
                return;
            }

            weapon.Revoke();
            weapon.gameObject.SetActive(false);
        }

        private void StowAllSummoned()
        {
            StowSummoned(OVRInput.Controller.LTouch);
            StowSummoned(OVRInput.Controller.RTouch);
        }

        private void DestroySummoned()
        {
            if (_summonedLeft != null)
            {
                Destroy(_summonedLeft.gameObject);
                _summonedLeft = null;
            }

            if (_summonedRight != null)
            {
                Destroy(_summonedRight.gameObject);
                _summonedRight = null;
            }
        }

        private void TrySubscribeAlive()
        {
            if (_aliveSubscribed || PlayerCombatState.Instance == null)
            {
                return;
            }

            PlayerCombatState.Instance.AliveChanged += HandleAliveChanged;
            _aliveSubscribed = true;
        }

        /// <summary>A revived player returns with a FULL magazine.
        /// <para>⚠️ <c>Weapon.HandleAliveChanged</c> is NOT enough: it requires <c>IsHeld</c>, and a
        /// clone that was stowed while dead (grip released, rig unresolved) is DISABLED — it has
        /// unsubscribed and would never refill, so the player would return with the previous life's
        /// half magazine. This path reaches the clone whatever state it is in.</para></summary>
        private void HandleAliveChanged(bool alive)
        {
            if (alive)
            {
                RefillSummoned();
            }
        }

        /// <summary>Every countdown (§10.1 <c>phaseReason:"countdown"</c>) refills the held weapon.
        /// <para>⚠️ <see cref="HandleAliveChanged"/> alone is NOT enough: a player who SURVIVES the
        /// round never revives, so they would enter the next round half loaded. Gating on the
        /// countdown keeps it mode-agnostic — every round in a round-based mode, once at match start
        /// elsewhere (where it is a no-op anyway).</para>
        /// <para><see cref="Weapon.RefillFull"/> is idempotent; calling it every countdown second is
        /// harmless (an ongoing reload is cancelled, which is correct).</para></summary>
        private void HandleCountdown(CountdownMsg msg)
        {
            RefillSummoned();
        }

        private void RefillSummoned()
        {
            if (_summonedLeft != null)
            {
                _summonedLeft.RefillFull();
            }

            if (_summonedRight != null)
            {
                _summonedRight.RefillFull();
            }
        }

        // -------------------------------------------------------- hand resolution (ISDK)

        /// <summary>Resolves the OVR controller from the interactor that raised a pointer event.
        /// <c>evt.Data</c> is the interactor itself by default; the BB controller rig builds the
        /// interactor↔IController mapping through <see cref="InteractorControllerDecorator"/>.
        /// Returns <see cref="OVRInput.Controller.None"/> when unresolved (the Editor fallback
        /// mark).
        /// <para>⚠️ The ONE place hands are resolved, never copied: it has two consumers
        /// (<see cref="Weapon"/>, <see cref="WeaponFrame"/>) and copies drift. Same reason as rig
        /// discovery: hand and anchor resolve through one gate.</para></summary>
        public static OVRInput.Controller ResolveController(in PointerEvent evt)
        {
            if (evt.Data is IInteractorView view &&
                InteractorControllerDecorator.TryGetControllerForInteractor(view, out IController controller))
            {
                return ToOvrController(controller.Handedness);
            }

            // Fallback: with no decorator, look for a ControllerRef in the interactor hierarchy.
            Component dataComponent = evt.Data as Component;
            if (dataComponent != null)
            {
                ControllerRef controllerRef = dataComponent.GetComponentInParent<ControllerRef>();
                if (controllerRef != null)
                {
                    return ToOvrController(controllerRef.Handedness);
                }
            }

            return OVRInput.Controller.None;
        }

        /// <summary>Resolves the hand from the interactor's GameObject — the SAME path as
        /// <see cref="ResolveController(in PointerEvent)"/>. This second signature exists because
        /// ISDK filters (<c>IGameObjectFilter.Filter</c>) receive a GameObject, not an event.</summary>
        public static OVRInput.Controller ResolveControllerFromGameObject(GameObject interactorGameObject)
        {
            if (interactorGameObject == null)
            {
                return OVRInput.Controller.None;
            }

            var view = interactorGameObject.GetComponent<IInteractorView>();
            if (view != null &&
                InteractorControllerDecorator.TryGetControllerForInteractor(view, out IController controller))
            {
                return ToOvrController(controller.Handedness);
            }

            ControllerRef controllerRef = interactorGameObject.GetComponentInParent<ControllerRef>();
            if (controllerRef != null)
            {
                return ToOvrController(controllerRef.Handedness);
            }

            return OVRInput.Controller.None;
        }

        private static OVRInput.Controller ToOvrController(Handedness handedness)
        {
            return handedness == Handedness.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        }

        // ----------------------------------------------------------------- helpers

        /// <summary>The ONE way to resolve a hand anchor: granted weapons (<see cref="Grant"/>) and
        /// grabbed ones (<c>Weapon.ApplyCanonicalGrip</c>) both come through here.
        /// <para>⚠️ NEVER write a second rig discovery path: two searches can find different rigs
        /// on different frames (scene change, a rig the spectator disabled) and the weapon would
        /// jump between hands. With no rig this returns <c>null</c> and the caller does
        /// nothing.</para>
        /// <para>Also <c>null</c> for <see cref="OVRInput.Controller.None"/>: silently falling back
        /// to the right hand would show the weapon in the wrong hand.</para></summary>
        public static Transform ResolveHandAnchor(OVRInput.Controller hand)
        {
            if (hand != OVRInput.Controller.LTouch && hand != OVRInput.Controller.RTouch)
            {
                return null;
            }

            OVRCameraRig rig = Instance != null ? Instance.ResolveRig() : null;
            if (rig == null)
            {
                return null;
            }

            return hand == OVRInput.Controller.LTouch ? rig.leftHandAnchor : rig.rightHandAnchor;
        }

        /// <summary>The two body-height references — EYE and FLOOR world Y; <c>false</c> with no
        /// rig. The floor is the rig's tracking space: with tracking origin <c>Stage</c> that
        /// plane's local Y is zero, i.e. the player's physical floor. Together they answer "how far
        /// above the floor is this player's eye" and feed every height-scaled measurement (see
        /// <see cref="WeaponReloadGesture"/>).
        /// <para>⚠️ Rig discovery is again <see cref="ResolveRig"/> — no second search path
        /// (rationale in <see cref="ResolveHandAnchor"/>).</para></summary>
        public static bool TryResolveEyeAndFloor(out float eyeY, out float floorY)
        {
            eyeY = 0f;
            floorY = 0f;

            OVRCameraRig rig = Instance != null ? Instance.ResolveRig() : null;
            if (rig == null || rig.centerEyeAnchor == null || rig.trackingSpace == null)
            {
                return false;
            }

            eyeY = rig.centerEyeAnchor.position.y;
            floorY = rig.trackingSpace.position.y;
            return true;
        }

        /// <summary>The hand's PALM pose (<see cref="ResolveHandAnchor"/> +
        /// <see cref="HandGripPivot"/>); <c>false</c> when the rig or hand cannot be resolved.
        /// <para>⚠️ The only place the palm point is computed; every consumer (canonical grip, frame
        /// distance, front socket) goes through it. The palm is the anchor itself today and the grip
        /// record is in anchor space — a second path would put the offset in two places.</para>
        /// </summary>
        public static bool TryResolvePalm(OVRInput.Controller hand, out Pose palm)
        {
            Transform anchor = ResolveHandAnchor(hand);
            if (anchor == null)
            {
                palm = default;
                return false;
            }

            palm = HandGripPivot.Resolve(anchor, HandGripPivot.IsRight(hand));
            return true;
        }

        /// <summary>First-frame pose of a granted/summoned weapon: the SAME solver and source as
        /// <c>Weapon.ApplyCanonicalGrip</c> (the anchor-space record in the definition,
        /// <see cref="ItemGripSolver"/>).
        /// <para>⚠️ Both places MUST run the same formula: a different measurement here would make
        /// the weapon appear in one pose and jump to another on the next frame.</para>
        /// <para>⚠️ There is no secondary hand this frame (<c>hasSecondary: false</c>) but the
        /// signature still needs one, since the front-grip record is per hand. The OPPOSITE of the
        /// main hand is passed — that is the hand that would wrap the front grip.</para></summary>
        private static void SolveInitialGrip(ItemDefinition definition,
            OVRInput.Controller hand, in Pose palm, out Vector3 position, out Quaternion rotation)
        {
            bool mainHandRight = HandGripPivot.IsRight(hand);
            bool secondaryRight = !mainHandRight;

            ItemGripSolver.Solve(definition, mainHandRight, secondaryRight, palm, false, Vector3.zero,
                0f, out position, out rotation);
        }

        /// <summary>Finds the active rig, retrying once a second. For the admin spectator the rig
        /// is disabled, so this returns null permanently.</summary>
        private OVRCameraRig ResolveRig()
        {
            if (_rig != null && _rig.isActiveAndEnabled)
            {
                return _rig;
            }

            if (Time.time < _nextRigScanAt)
            {
                return null;
            }

            _nextRigScanAt = Time.time + RigRescanSeconds;
            _rig = FindFirstObjectByType<OVRCameraRig>();
            return _rig;
        }
    }
}
