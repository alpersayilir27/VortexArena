using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Combat
{
    /// <summary>The weapon's FRAME: the scene weapon sits frozen inside it and never leaves; aiming
    /// from ≤<see cref="maxGrabDistance"/> m and pressing grip puts a CLONE in the player's hand
    /// (built and owned by <see cref="WeaponGranter"/>).
    /// <para>Exists only while the weapon is frozen in the scene: however it ends up held (granted
    /// or grabbed) the frame hides, and returns on release —
    /// <see cref="HandleHeldChanged"/>.</para>
    /// <para>Lives on the ROOT of the <c>VA_WeaponFrame</c> prefab, which is a CHILD of every
    /// <c>WPN_*</c> prefab. ⚠️ It reads the weapon it represents from its PARENT
    /// (<c>GetComponentInParent&lt;Weapon&gt;()</c>); there is NO separate
    /// <see cref="WeaponDefinition"/> field here and none is added — two places to write the same
    /// weapon means the frame can show one gun and hand out another.</para>
    /// <para>The gate is ISDK's own extension point: this component is an
    /// <see cref="IGameObjectFilter"/> registered in the <c>_interactorFilters</c> list of the
    /// frame's distance-grab components — <see cref="Filter"/> applies the distance rule, ISDK
    /// keeps the selection sensing.</para>
    /// <para>⚠️ BOTH distance-grab components are carried and both are listened to:
    /// <see cref="DistanceGrabInteractable"/> (controller line) and
    /// <see cref="DistanceHandGrabInteractable"/> (hand line). The ISDK rig decides which one runs,
    /// based on <c>OVRManager.controllerDrivenHandPosesType</c>; keeping only one would silently
    /// make the weapon ungrabbable on every change of that switch (<c>Docs/Sistem-Ozeti.md</c>
    /// §7).</para>
    /// <para>⚠️ The hand line's <c>Hand Alignment</c> is <c>None</c> in the prefab and stays that
    /// way. <c>AlignOnGrab</c> would lock the synthetic wrist to the grabbed object
    /// (<c>SyntheticHand.LockWristPose</c>), sticking the player's hand to the scene weapon while a
    /// CLONE is what they actually hold. The frame is a SELECTION trigger, not a grab target: it
    /// must drive nothing about the hand.</para></summary>
    public class WeaponFrame : MonoBehaviour, IGameObjectFilter
    {
        /// <summary>Shader lookup chain for the aim ray material (first hit wins).</summary>
        // ⚠️ Same chain as ShotTracer, "Sprites/Default" first: it is in Always Included Shaders,
        // so it survives the build. A Shader.Find-only shader no material references is STRIPPED
        // and the ray silently disappears on device.
        private static readonly string[] ShaderCandidates =
        {
            "Sprites/Default",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
        };

        // Fail-open warning once per SESSION, not per instance: every scene weapon has a frame.
        private static bool _warnedFailOpen;

        // Same for the "no frame art" warning: if it is missing it is missing on EVERY weapon
        // (they all come from the same prefab).
        private static bool _warnedNoFrameArt;

        [Header("Görünüm")]
        [Tooltip("Çerçeve görselini açar. Sahnedeki WPN_* ÖRNEĞİ üstünde override edilir — " +
                 "çerçevesiz durması istenen silahlarda kapatılır.")]
        [SerializeField] private bool isFrameVisible = true;

        [Tooltip("Çerçevenin KENDİ nişan ışınını çizer. VARSAYILAN KAPALI: aynı geri bildirimi " +
                 "ISDK'nın mesafe-kavrama göstergesi (tüp + reticle) zaten veriyor, ikisi birden " +
                 "çizilince oyuncu elinde iki ışın görür.")]
        [SerializeField] private bool isRayVisible;

        [Tooltip("Prefabdaki PASİF görsel kökü. Altındaki çerçeve MODELİ çalışma anında silahın " +
                 "ölçüsüne oturtulur (konum/dönüş/ölçek buradan yazılır).")]
        [SerializeField] private Transform frameVisual;

        [Tooltip("Silahın seçilebildiği en uzak mesafe (m) — AVUÇ ile çerçeve merkezi arası. " +
                 "⚠️ ISDK'nın kendi mesafe-kavrama konisi 5 m'de biter: bunun üstüne çıkmak işe " +
                 "yaramaz, silah aday bile olmaz.")]
        [SerializeField] private float maxGrabDistance = 4f;

        [Tooltip("Nişan ışınının rengi (alfa dahil).")]
        [SerializeField] private Color rayColor = new Color(0.35f, 0.7f, 1f, 0.9f);

        [Tooltip("Nişan ışınının kalınlığı (m).")]
        [SerializeField] private float rayWidth = 0.006f;

        [Tooltip("Silahın sınırlarına eklenen pay (m) — çerçeve silaha yapışık durmasın.")]
        [SerializeField] private float framePadding = 0.06f;

        [Header("Referanslar")]
        [Tooltip("Çerçevenin KENDİ mesafe-kavrama bileşeni (kumanda hattı). Boşsa GetComponent ile çözülür.")]
        [SerializeField] private DistanceGrabInteractable distanceGrab;

        [Tooltip("Çerçevenin KENDİ mesafe-kavrama bileşeni (el hattı). Boşsa GetComponent ile " +
                 "çözülür. İkisi birden tutulur — hangisinin koşacağını ISDK rig'i seçer.")]
        [SerializeField] private DistanceHandGrabInteractable distanceHandGrab;

        [Tooltip("Nişan alınacak hacim — silahın sınırlarına göre ÇALIŞMA ANINDA boyutlanır. " +
                 "Boşsa GetComponent ile çözülür.")]
        [SerializeField] private BoxCollider grabCollider;

        // Hovering interactors: id = PointerEvent.Identifier (Unhover/Cancel match key),
        // ctl = resolved OVR controller (None = unresolved → no ray).
        private readonly List<(int id, OVRInput.Controller ctl)> _hovering =
            new List<(int, OVRInput.Controller)>();

        private Weapon _weapon;

        /// <summary>Frame centre in the WEAPON's local space (computed once in Awake; the source
        /// weapon is frozen so it never changes).</summary>
        private Vector3 _centerLocal;

        private LineRenderer _rayLeft;
        private LineRenderer _rayRight;
        private Material _lineMaterial;
        private bool _warnedNoShader;
        private bool _detached;

        private void Awake()
        {
            _weapon = GetComponentInParent<Weapon>();
            if (_weapon == null)
            {
                // A frame alone is meaningless: only the parent weapon says what it represents.
                // Staying silent would leave an inert frame in the scene, expensive to diagnose.
                Debug.LogWarning($"[WeaponFrame] '{name}' bir Weapon'ın altında değil; çerçeve " +
                                 "kapatıldı. VA_WeaponFrame prefabı WPN_* prefabının ÇOCUĞU olmalı.", this);
                enabled = false;
                return;
            }

            // ⚠️ Subscribe in Awake/OnDestroy, NOT OnEnable/OnDisable: this handler disables the
            // frame's own GameObject, so unsubscribing in OnDisable would lose the "released"
            // signal and the frame would never come back.
            _weapon.HeldChanged += HandleHeldChanged;

            if (distanceGrab == null)
            {
                distanceGrab = GetComponent<DistanceGrabInteractable>();
            }

            if (distanceHandGrab == null)
            {
                distanceHandGrab = GetComponent<DistanceHandGrabInteractable>();
            }

            if (distanceGrab == null && distanceHandGrab == null)
            {
                // ⚠️ An ERROR, not a warning: with no grab component the frame is visible but the
                // weapon can NEVER be taken, which reads as "grabbing is broken" while the real
                // cause is a missing prefab component. Configure All Build Elements fixes it.
                Debug.LogError($"[WeaponFrame] '{name}' üzerinde ne DistanceGrabInteractable ne " +
                               "DistanceHandGrabInteractable var; bu çerçeveden silah alınamaz. " +
                               "Tools > VortexArena > Build > Configure All Build Elements çalıştırılmalı.", this);
            }

            if (grabCollider == null)
            {
                grabCollider = GetComponent<BoxCollider>();
            }

            FreezeSource();

            // Measured ONCE and feeds both consumers (frame art + aim volume).
            bool measured = MeasureWeaponBounds(out Bounds local);
            _centerLocal = measured ? local.center : Vector3.zero;

            SizeGrabCollider(measured, local);
            BuildFrameVisual(measured, local);
        }

        private void OnEnable()
        {
            // ⚠️ Subscribing to both does NOT double-count: only one interactor group runs per
            // frame (see the class summary), and distinct Identifiers would keep them apart anyway.
            if (distanceGrab != null)
            {
                distanceGrab.WhenPointerEventRaised += HandlePointerEvent;
            }

            if (distanceHandGrab != null)
            {
                distanceHandGrab.WhenPointerEventRaised += HandlePointerEvent;
            }
        }

        private void OnDisable()
        {
            if (distanceGrab != null)
            {
                distanceGrab.WhenPointerEventRaised -= HandlePointerEvent;
            }

            if (distanceHandGrab != null)
            {
                distanceHandGrab.WhenPointerEventRaised -= HandlePointerEvent;
            }

            // ISDK's Unhover/Cancel no longer reaches us; do not leave rays on.
            _hovering.Clear();
            HideRay(_rayLeft);
            HideRay(_rayRight);
        }

        private void OnDestroy()
        {
            if (_weapon != null)
            {
                _weapon.HeldChanged -= HandleHeldChanged;
            }

            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
                _lineMaterial = null;
            }
        }

        /// <summary>The frame exists only while the weapon is FROZEN in the scene; it hides
        /// however the weapon becomes held and returns on release.
        /// <para>Bound to <see cref="Weapon.HeldChanged"/>, not to call sites: several paths put a
        /// weapon in hand (<see cref="WeaponGranter"/>'s two modes + direct grab) and adding "hide
        /// the frame" to each would be a step silently forgotten by the next path.</para>
        /// <para>⚠️ Disable, do NOT destroy: the same instance must come back with its frame.
        /// Disabling also collects the rays and the ISDK subscription through <c>OnDisable</c>.</para>
        /// </summary>
        private void HandleHeldChanged(bool held)
        {
            gameObject.SetActive(!held);
        }

        private void Update()
        {
            if (_weapon == null || _detached || !isRayVisible || !WeaponGranter.CanSelectWeapon)
            {
                // Collect any live ray, otherwise it freezes on screen — the toggle may have been
                // cleared at runtime (Editor tinkering), or a hand may have filled mid-hover, and a
                // full hand must see no selection ray at all.
                HideRay(_rayLeft);
                HideRay(_rayRight);
                return;
            }

            TickRay(OVRInput.Controller.LTouch, ref _rayLeft);
            TickRay(OVRInput.Controller.RTouch, ref _rayRight);
        }

        /// <summary>Called by <see cref="WeaponGranter"/> while preparing a clone: hides the frame
        /// art and rays. The granter destroys the frame object anyway; since <c>Destroy</c> is
        /// deferred to end of frame, this stops the frame flashing in hand meanwhile.</summary>
        public void DetachForClone()
        {
            _detached = true;
            _hovering.Clear();
            HideRay(_rayLeft);
            HideRay(_rayRight);

            if (frameVisual != null)
            {
                frameVisual.gameObject.SetActive(false);
            }
        }

        // ---------------------------------------------------------------- freezing the source

        /// <summary>Nails the framed weapon in place: physics off, all NEAR grab paths closed —
        /// a framed weapon is selected FROM A DISTANCE ONLY. The secondary-grip indicator needs no
        /// handling: <see cref="Weapon"/> draws it only on a HELD weapon.
        /// <para>⚠️ The scan covers the parent weapon's tree and SKIPS the frame's own subtree: the
        /// frame's <see cref="Grabbable"/> and <see cref="Rigidbody"/> ARE the distance selection,
        /// and disabling them makes the weapon unobtainable. (<see cref="FrozenGrabTransformer"/>
        /// keeps the frame still, not a disabled component.)</para></summary>
        private void FreezeSource()
        {
            Rigidbody[] bodies = _weapon.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (IsUnderFrame(bodies[i].transform))
                {
                    continue;
                }

                bodies[i].isKinematic = true;
                bodies[i].useGravity = false;
            }

            MonoBehaviour[] behaviours = _weapon.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || IsUnderFrame(behaviour.transform))
                {
                    continue;
                }

                // ⚠️ Controller and hand lines are closed TOGETHER: leaving one open lets the
                // frozen scene weapon be grabbed directly, bypassing the frame.
                if (behaviour is Grabbable || behaviour is GrabInteractable ||
                    behaviour is HandGrabInteractable || behaviour is DistanceHandGrabInteractable)
                {
                    behaviour.enabled = false;
                }
            }
        }

        private bool IsUnderFrame(Transform candidate)
        {
            return candidate != null && candidate.IsChildOf(transform);
        }

        // ------------------------------------------------------------------------ frame art

        /// <summary>Fits the frame ART (under <see cref="frameVisual"/>) to the weapon's renderer
        /// bounds: turns its plane onto the weapon's two largest axes, scales to cover them and
        /// centres it.
        /// <para>⚠️ Done at runtime, not by hand (same reason as <see cref="SizeGrabCollider"/>):
        /// the frame is ONE prefab sitting under weapons of different sizes, so the WEAPON states
        /// the measurement, not the prefab.</para>
        /// <para>⚠️ <see cref="frameVisual"/> is INACTIVE in the prefab and only enabled here when
        /// <see cref="isFrameVisible"/>. <c>RemoteAvatar.SterilizeVisual</c> strips MonoBehaviours
        /// from the copy but does NOT disable GameObjects (and no <c>Awake</c> runs there), so
        /// starting active would show the frame on remote-held weapons and on the local
        /// clone.</para>
        /// <para>Both sides match their two largest axes (large→large), so a weapon lying down or
        /// standing up is framed correctly. Depth scale takes the SMALLER of the two: a frame that
        /// stretched in thickness as well would look like sagging profile.</para></summary>
        private void BuildFrameVisual(bool measured, Bounds local)
        {
            if (!measured)
            {
                // No renderers (or all under the frame): nothing to fit; the ray still has a
                // target (the weapon root is used as the centre).
                return;
            }

            if (!isFrameVisible || frameVisual == null)
            {
                return;
            }

            // ⚠️ Measure from the BASE pose: the fit below expects the unscaled box. Without the
            // reset a second call (or a leftover prefab scale) would compound and grow the frame.
            frameVisual.localPosition = Vector3.zero;
            frameVisual.localRotation = Quaternion.identity;
            frameVisual.localScale = Vector3.one;

            if (!MeasureFrameArtBounds(out Bounds art) || art.size.sqrMagnitude < 1e-8f)
            {
                WarnNoFrameArt();
                return;
            }

            frameVisual.gameObject.SetActive(true);

            // Plane axes: the two largest on each side, sorted large → small.
            LargestTwoAxes(local.size, out int weaponA, out int weaponB);
            OrderBySize(local.size, ref weaponA, ref weaponB);
            int weaponNormal = 3 - weaponA - weaponB;

            LargestTwoAxes(art.size, out int artA, out int artB);
            OrderBySize(art.size, ref artA, ref artB);
            int artNormal = 3 - artA - artB;

            // Rotation maps the art's (plane normal, long edge) onto the weapon's. Weapon axes go
            // into the frame visual's PARENT space: the two roots need not share a space.
            Transform parent = frameVisual.parent;
            Vector3 targetNormal = ToParentDirection(parent, AxisVector(weaponNormal));
            Vector3 targetUp = ToParentDirection(parent, AxisVector(weaponA));

            Quaternion rotation =
                Quaternion.LookRotation(targetNormal, targetUp) *
                Quaternion.Inverse(Quaternion.LookRotation(AxisVector(artNormal), AxisVector(artA)));

            // Scale is written in the ART's own axes (localScale applies before rotation).
            var scale = Vector3.one;
            scale[artA] = (local.size[weaponA] + framePadding * 2f) / Mathf.Max(art.size[artA], 1e-4f);
            scale[artB] = (local.size[weaponB] + framePadding * 2f) / Mathf.Max(art.size[artB], 1e-4f);
            scale[artNormal] = Mathf.Min(scale[artA], scale[artB]);

            // Put the art's MEASURED centre on the weapon's centre. The art pivot need not be
            // centred, so the offset is undone after rotation+scale.
            Vector3 targetCenter = parent.InverseTransformPoint(_weapon.transform.TransformPoint(local.center));

            frameVisual.localRotation = rotation;
            frameVisual.localScale = scale;
            frameVisual.localPosition = targetCenter - rotation * Vector3.Scale(scale, art.center);
        }

        private static void OrderBySize(in Vector3 size, ref int major, ref int minor)
        {
            if (size[minor] > size[major])
            {
                (major, minor) = (minor, major);
            }
        }

        private static Vector3 AxisVector(int axis)
        {
            var v = Vector3.zero;
            v[axis] = 1f;
            return v;
        }

        private Vector3 ToParentDirection(Transform parent, in Vector3 weaponLocalDirection)
        {
            Vector3 world = _weapon.transform.TransformDirection(weaponLocalDirection);
            return parent != null ? parent.InverseTransformDirection(world).normalized : world.normalized;
        }

        /// <summary>Measures the frame art bounds in <see cref="frameVisual"/>'s LOCAL space.
        /// ⚠️ Uses the corners of <c>localBounds</c>, NOT the world AABB (<c>Renderer.bounds</c>):
        /// a rotated model's world box is larger than the model and would oversize the frame.</summary>
        private bool MeasureFrameArtBounds(out Bounds local)
        {
            local = new Bounds();
            bool any = false;

            Renderer[] renderers = frameVisual.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Bounds rendererLocal = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? rendererLocal.min.x : rendererLocal.max.x,
                        (corner & 2) == 0 ? rendererLocal.min.y : rendererLocal.max.y,
                        (corner & 4) == 0 ? rendererLocal.min.z : rendererLocal.max.z);

                    Vector3 point = frameVisual.InverseTransformPoint(
                        renderer.transform.TransformPoint(offset));

                    if (!any)
                    {
                        local = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        local.Encapsulate(point);
                    }
                }
            }

            return any;
        }

        /// <summary>Warns once when the art root has no Renderer. Logged because the frame then
        /// silently never draws while the weapon is still selectable — the feature looks alive but
        /// is invisible. Usually the art was dropped from the prefab.</summary>
        private static void WarnNoFrameArt()
        {
            if (_warnedNoFrameArt)
            {
                return;
            }

            _warnedNoFrameArt = true;
            Debug.LogWarning("[WeaponFrame] Görsel kökünün altında Renderer yok — çerçeve " +
                             "çizilmeyecek (silah yine seçilebilir). VA_WeaponFrame prefabında " +
                             "FrameVisual altındaki çerçeve modeli duruyor mu?");
        }

        /// <summary>
        /// Fits the aim volume to the weapon bounds (plus <see cref="framePadding"/>).
        /// <para>⚠️ At runtime because <c>VA_WeaponFrame</c> is ONE prefab under weapons of
        /// different sizes: a fixed box would be too wide on a short gun (swallowing its neighbour)
        /// and too narrow on a long one. The WEAPON states the measurement.</para>
        /// <para>This box is the ONLY input to <see cref="DistanceGrabInteractable"/>'s candidate
        /// test: ISDK distance grab does no <c>Physics.Raycast</c>, it cone-tests the rigidbody's
        /// colliders directly. So no layer/mask setup is needed and the box MAY be a trigger (it is:
        /// weapons have no physical collision in free-roam).</para>
        /// <para>Unmeasured (no renderers) leaves the prefab box untouched — a rough target beats
        /// none.</para></summary>
        private void SizeGrabCollider(bool measured, Bounds local)
        {
            if (grabCollider == null || !measured)
            {
                return;
            }

            // The weapon's local box goes into the FRAME's local space (the two need not match),
            // so all eight corners are transformed and re-boxed.
            Bounds inFrame = default;
            bool any = false;

            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z);

                Vector3 inFrameSpace = transform.InverseTransformPoint(_weapon.transform.TransformPoint(point));

                if (!any)
                {
                    inFrame = new Bounds(inFrameSpace, Vector3.zero);
                    any = true;
                }
                else
                {
                    inFrame.Encapsulate(inFrameSpace);
                }
            }

            grabCollider.center = inFrame.center;
            grabCollider.size = inFrame.size + Vector3.one * (framePadding * 2f);
        }

        /// <summary>Measures the weapon's renderers in the WEAPON's local space (frame art
        /// excluded). ⚠️ Uses <c>localBounds</c> corners, NOT the world AABB: a rotated weapon's
        /// world box is much larger than the weapon.</summary>
        private bool MeasureWeaponBounds(out Bounds local)
        {
            local = new Bounds();
            bool any = false;

            Renderer[] renderers = _weapon.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || IsUnderFrame(renderer.transform))
                {
                    continue;
                }

                Bounds rendererLocal = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? rendererLocal.min.x : rendererLocal.max.x,
                        (corner & 2) == 0 ? rendererLocal.min.y : rendererLocal.max.y,
                        (corner & 4) == 0 ? rendererLocal.min.z : rendererLocal.max.z);

                    Vector3 point = _weapon.transform.InverseTransformPoint(
                        renderer.transform.TransformPoint(offset));

                    if (!any)
                    {
                        local = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        local.Encapsulate(point);
                    }
                }
            }

            return any;
        }

        private static void LargestTwoAxes(Vector3 size, out int axisA, out int axisB)
        {
            int smallest = 0;
            if (size.y < size[smallest])
            {
                smallest = 1;
            }

            if (size.z < size[smallest])
            {
                smallest = 2;
            }

            axisA = smallest == 0 ? 1 : 0;
            axisB = smallest == 2 ? 1 : 2;
        }

        // --------------------------------------------------------------------- ISDK gate

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Hover:
                    AddHover(evt);
                    break;

                case PointerEventType.Select:
                    // Select = "this weapon is mine now": WeaponGranter builds/hides the clone.
                    // This component has nothing to do with the held weapon; the source stays framed.
                    WeaponGranter.SelectWeapon(_weapon != null ? _weapon.Definition : null);
                    _hovering.Clear();
                    HideRay(_rayLeft);
                    HideRay(_rayRight);
                    break;

                case PointerEventType.Unhover:
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    RemoveHover(evt.Identifier);
                    break;
            }
        }

        private void AddHover(in PointerEvent evt)
        {
            for (int i = 0; i < _hovering.Count; i++)
            {
                if (_hovering[i].id == evt.Identifier)
                {
                    return;
                }
            }

            _hovering.Add((evt.Identifier, WeaponGranter.ResolveController(evt)));
        }

        private void RemoveHover(int identifier)
        {
            for (int i = 0; i < _hovering.Count; i++)
            {
                if (_hovering[i].id != identifier)
                {
                    continue;
                }

                _hovering.RemoveAt(i);
                return;
            }
        }

        /// <summary>ISDK gate: may this interactor select the weapon right now — CALIBRATED, BOTH
        /// hands empty, and within <see cref="maxGrabDistance"/>.
        /// <para>⚠️ FAIL-OPEN when the hand/anchor cannot be resolved: this is a FEEL gate, not a
        /// safety gate. An Editor session cannot resolve a controller, and failing closed would
        /// make the weapon unselectable in the Editor, i.e. untestable.</para></summary>
        public bool Filter(GameObject interactorGameObject)
        {
            // ⚠️ An uncalibrated player cannot take a weapon (§10.6), and the gate lives HERE
            // rather than on the selection path: a frame dropped from the candidate list draws no
            // ray/reticle either. On the selection side the player would aim, press grip and see
            // nothing. Delivery is also closed by WeaponGranter.CanHoldWeapon; this gate does not
            // replace it, it gives the player correct feedback.
            if (!CalibrationState.IsCalibrated)
            {
                return false;
            }

            // ⚠️ A weapon in EITHER hand closes the frame for BOTH: the free hand may not pick a
            // second weapon. Dropping the frame from the candidate list is what makes it silent —
            // no ray, no reticle, nothing to aim at — which is the point: the player must not see
            // an offer they cannot take. Reads an end-of-frame SNAPSHOT; never rewrite it as a live
            // "is a weapon in hand" query (see WeaponGranter.CanSelectWeapon: grip selects and
            // summons on the same frame, and a live check would lock the player to their first
            // weapon forever). Swapping at the rack costs one grip release.
            if (!WeaponGranter.CanSelectWeapon)
            {
                return false;
            }

            OVRInput.Controller hand = WeaponGranter.ResolveControllerFromGameObject(interactorGameObject);
            if (hand == OVRInput.Controller.None)
            {
                WarnFailOpen();
                return true;
            }

            if (!WeaponGranter.TryResolvePalm(hand, out Pose palm))
            {
                return true; // no rig → the gate is meaningless
            }

            return (palm.position - FrameCenterWorld).sqrMagnitude <= maxGrabDistance * maxGrabDistance;
        }

        // ----------------------------------------------------------------------- aim ray

        /// <summary>World position of the frame centre (ray target and distance reference).</summary>
        private Vector3 FrameCenterWorld =>
            _weapon != null ? _weapon.transform.TransformPoint(_centerLocal) : transform.position;

        /// <summary>Draws a ray from the hovering hand's PALM to the frame centre — only while in
        /// range.
        /// <para>⚠️ Off by default (<see cref="isRayVisible"/>): ISDK's distance grab draws its own
        /// indicator (tube + reticle) and enabling both shows the player two rays.</para>
        /// <para>⚠️ ISDK's indicator does not lie out of range, so turning ours off costs nothing:
        /// its candidate list filters every entry through <c>CanBeSelectedBy</c>, i.e. through
        /// <see cref="Filter"/>, so an out-of-range frame is not even a candidate.</para>
        /// <para>The distance test is still repeated here because <see cref="Filter"/> is FAIL-OPEN
        /// when the hand cannot be resolved (Editor session).</para></summary>
        private void TickRay(OVRInput.Controller hand, ref LineRenderer ray)
        {
            if (!IsHovering(hand))
            {
                HideRay(ray);
                return;
            }

            // Measured from the PALM, the same source as Filter, or the ray would die a few
            // centimetres off the gate's range.
            if (!WeaponGranter.TryResolvePalm(hand, out Pose palm))
            {
                HideRay(ray);
                return;
            }

            Vector3 center = FrameCenterWorld;
            if ((palm.position - center).sqrMagnitude > maxGrabDistance * maxGrabDistance)
            {
                HideRay(ray);
                return;
            }

            ray = ray != null ? ray : CreateRay(hand);
            if (ray == null)
            {
                return;
            }

            ray.SetPosition(0, palm.position);
            ray.SetPosition(1, center);
            ray.enabled = true;
        }

        private bool IsHovering(OVRInput.Controller hand)
        {
            for (int i = 0; i < _hovering.Count; i++)
            {
                if (_hovering[i].ctl == hand)
                {
                    return true;
                }
            }

            return false;
        }

        private LineRenderer CreateRay(OVRInput.Controller hand)
        {
            Material material = EnsureLineMaterial();
            if (material == null)
            {
                return null;
            }

            var go = new GameObject(hand == OVRInput.Controller.LTouch ? "[FrameRay_L]" : "[FrameRay_R]");
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            // World space: one end is on the HAND, the other on the frame.
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.startWidth = rayWidth;
            line.endWidth = rayWidth;
            line.startColor = rayColor;
            line.endColor = rayColor;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            line.enabled = false;

            return line;
        }

        private static void HideRay(LineRenderer ray)
        {
            if (ray != null && ray.enabled)
            {
                ray.enabled = false;
            }
        }

        // ----------------------------------------------------------------------- helpers

        /// <summary>Material SHARED by both aim rays (colour comes from LineRenderer vertex color).
        /// The frame itself is art carrying its own material; this is for the rays only.</summary>
        private Material EnsureLineMaterial()
        {
            if (_lineMaterial != null)
            {
                return _lineMaterial;
            }

            for (int i = 0; i < ShaderCandidates.Length; i++)
            {
                Shader shader = Shader.Find(ShaderCandidates[i]);
                if (shader != null)
                {
                    _lineMaterial = new Material(shader) { name = "M_WeaponFrame(runtime)" };
                    return _lineMaterial;
                }
            }

            if (!_warnedNoShader)
            {
                _warnedNoShader = true;
                Debug.LogWarning(
                    "[WeaponFrame] Nişan ışını için shader bulunamadı (Sprites/Default dahil) — " +
                    "silah yine seçilebilir ve çerçeve görünür, ama nişan ışını çizilmez. " +
                    "Graphics Settings > Always Included Shaders listesini kontrol et.", this);
            }

            return null;
        }

        /// <summary>Logs the fail-open once. Logged because with an unresolved hand the distance
        /// gate is fully disabled and the weapon can be selected from across the arena — the feature
        /// looks alive but does nothing. EXPECTED in an Editor session (no controller); on device it
        /// means the rig's <c>InteractorControllerDecorator</c> setup is missing.</summary>
        private static void WarnFailOpen()
        {
            if (_warnedFailOpen)
            {
                return;
            }

            _warnedFailOpen = true;
            Debug.LogWarning("[WeaponFrame] Interactor'dan el çözülemedi — mesafe kapısı AÇIK " +
                             "bırakıldı (silah her mesafeden seçilebilir). Editörde normaldir; " +
                             "başlıkta görülüyorsa rig'in InteractorControllerDecorator kurulumuna bak.");
        }
    }
}
