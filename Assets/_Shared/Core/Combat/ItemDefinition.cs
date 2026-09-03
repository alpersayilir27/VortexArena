using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Common base of everything holdable (weapon, bomb).
    /// <para>
    /// <b>Deliberately NARROW: only what the network and remote drawing need</b> (<c>netItemId</c>,
    /// prefab, hold mode, grip poses). <i>Behaviour</i> fields — damage/magazine/range/fuse — do NOT
    /// belong here; they live in the derived class (<see cref="WeaponDefinition"/>). Rationale:
    /// <c>RemoteAvatar</c> draws the item in a remote player's hand without knowing what it DOES.
    /// This is the presentation counterpart of the Net layer's "contains no game knowledge" rule; a
    /// wider base would make the remote drawing path depend on game rules unnoticed.
    /// </para>
    /// <para>
    /// <b>Pose does not go on the wire</b> (Docs/ArenaNet-Protokol.md §6.6): the item's placement
    /// relative to the hand comes from the grip fields here, i.e. from each client's APK; its
    /// rotation is always the main controller's (<see cref="ItemGripSolver"/>). The precondition is a
    /// canonical grip — free grip (arbitrary offset) means a wrong pose on the remote side.
    /// </para>
    /// </summary>
    public abstract class ItemDefinition : ScriptableObject
    {
        [Header("Kimlik")]
        [SerializeField] private string displayName = "";

        // ⚠️ NEVER add [Range(1,255)]. Unity's Range drawer draws an IntSlider that silently clamps 0
        // to min (1) AND dirties the asset — every definition opened in the Inspector would become
        // netItemId=1, i.e. all weapons would collide. Bounds are checked in HasNetItemId and the real
        // protection is the editor guard (runs on every Configure All Build Elements sync).
        [Tooltip("Ağ kimliği (1-255; 0 = atanmamış). Snapshot'ta bu bayt gider; katalog dizi " +
                 "indeksi DEĞİLDİR — elle, kararlı verilir. Çakışmayı bekçi yakalar.")]
        [SerializeField] private int netItemId = 0;

        [Header("Sunum")]
        [Tooltip("Eşya prefabı (loadout kurulumları + uzak çizim için).")]
        [SerializeField] private GameObject prefab;

        [Tooltip("OneHand (tabanca/bomba) / TwoHand (tüfek).")]
        [SerializeField] private ItemHoldMode holdMode = ItemHoldMode.OneHand;

        // ⚠️ THREE INDEPENDENT axes, deliberately not one "item type" enum: the first item that breaks
        // the pattern (grabbed from a distance but physical; grabbed up close but cloned) would split
        // the single enum into a matrix. Each is a separate question with its own consumer.
        // ⚠️ The first value of each is the one an asset written BEFORE these fields existed reads
        // (Unity stores the numeric index and a missing field deserializes to 0) — those defaults ARE
        // today's weapon behaviour, so the enum order is a contract, not cosmetics.
        [Header("Alma yolu / örnekleme / bırakma")]
        [Tooltip("Eşya ele NASIL gelir. ⚠️ Varsayılan DistanceGrab'dır: bu alan eklenmeden önce " +
                 "kaydedilmiş her tanım onu okur, yani silahların bugünkü davranışı korunur. " +
                 "DistanceGrab DIŞINDA bir yol seçildiyse prefabda mesafeli kavrama bileşeni " +
                 "BULUNMAMALI — kurulum bekçisi bunu hata olarak bildirir.")]
        [SerializeField] private ItemGrabPath grabPath = ItemGrabPath.DistanceGrab;

        [Tooltip("Ele gelen NE. PerViewerClone: sahnedeki asıl donuk durur, ele kopya gelir " +
                 "(varsayılan; silah/bomba). WorldSingle: tek örnek vardır, ele o objenin kendisi " +
                 "gelir ve sahiplik devredilir — bu eşyanın tel baytı 0 kalır, uzak el objeyi kendi " +
                 "ağ nesnesi örneğinden çizer.")]
        [SerializeField] private ItemInstancing instancing = ItemInstancing.PerViewerClone;

        [Tooltip("Bırakınca ne olur. Return: yerine döner (varsayılan; silah). Physics: rigidbody " +
                 "serbest kalır (bomba, fırlatılan prop).")]
        [SerializeField] private ItemReleaseMode releaseMode = ItemReleaseMode.Return;

        // ⚠️ ALL FOUR RECORDS ARE IN THE SAME SPACE: each is the hand's CONTROLLER ANCHOR position
        // local to the ITEM (item → anchor; ItemGripPose — the anchor record has no rotation, the
        // weapon is always aligned with the controller). Written in one direction only; describing a
        // second space would only produce sign errors. Anchor = the hand pose on the wire = the pose
        // the solver knows, so no reader has to measure a delta.
        // ⚠️ The record's SECOND half is the hand's VISUAL (wrist placement + finger rig) and never
        // affects the item's pose: the hand may sit sideways/below while the weapon stays aligned with
        // the controller.
        // ⚠️ The record is PER HAND: grips are not symmetric, so the two controllers land on different
        // places of the item — one record mirrored would push the left hand inside the weapon.
        // ⚠️ Records are authored in the studio (editor), not captured with the headset.
        // The RADII here are not part of the pose but a GATE size: they say where a grip is accepted,
        // not how the item sits in the hand.
        [Header("Kavrama (kanonik)")]
        [Tooltip("SAĞ elin kumanda anchor'ının ana kabzadaki pozu (eşyaya göre yerel) + riglenmiş parmak duruşu.")]
        [SerializeField] private ItemGripPose primaryGripRight;

        [Tooltip("SOL elin kumanda anchor'ının ana kabzadaki pozu (eşyaya göre yerel) + riglenmiş parmak duruşu.")]
        [SerializeField] private ItemGripPose primaryGripLeft;

        [Tooltip("SAĞ elin kumanda anchor'ının ön kabzadaki pozu — yalnız TwoHand'de anlamlı.")]
        [SerializeField] private ItemGripPose secondaryGripRight;

        [Tooltip("SOL elin kumanda anchor'ının ön kabzadaki pozu — yalnız TwoHand'de anlamlı.")]
        [SerializeField] private ItemGripPose secondaryGripLeft;

        // ⚠️ NO [Range] — the same trap as the netItemId warning above: the Range drawer silently
        // clamps to its own defaults AND dirties the asset, so every definition opened in the
        // Inspector gets committed with a different radius. The lower bound is applied in the property.
        // ⚠️ There is NO radius for the PRIMARY grip: the weapon arrives in the main hand by being
        // granted/summoned, the player never has to move a hand onto it — a measure with no reader
        // goes stale.
        [Tooltip("Ön kabza SOKETİNİN yarıçapı (m): boş elin kumanda anchor'ı bu kürenin içindeyken grip'e " +
                 "basılınca ikinci el ön kabzaya bağlanır; oyuncunun gördüğü küre de tam bu yarıçapla " +
                 "çizilir (0.10 = 20 cm çap). Yalnız TwoHand'de anlamlı. Silah başına ayarlanır.")]
        [SerializeField] private float secondaryGripRadius = 0.10f;

        // ⚠️ No SEPARATE field for the finger pose, and none is added: the pose is PART of the grip
        // record (ItemGripPose.fingerJoints), i.e. it lives per slot. A separate field would keep "this
        // hand's pose" and "this hand's fingers" in two places, one updated and the other forgotten —
        // whereas the hand wrapping the foregrip and the hand on the trigger are different by
        // definition.

        // ⚠️ Cache per slot ([kind, hand] = 4 entries): both converting the rigged pose into an ISDK
        // joint array and reducing it to curl ratios for the humanoid hand allocate, and both paths are
        // read PER FRAME (HandGripPoser / RemoteHandPoser). NOT serialized: derived data — a second
        // copy in the asset would draw the old pose in game once the rig changed and the cache was
        // forgotten. Editor write gates drop the cache (InvalidateGripCache).
        [NonSerialized] private Quaternion[][] _gripJointCache;
        [NonSerialized] private HandPoseProfile[] _gripCurlCache;
        [NonSerialized] private bool[] _gripCurlResolved;

        // Tracer appearance lives in the base because the base's criterion is "what the network AND
        // REMOTE DRAWING need", and a tracer is exactly remote drawing data: the side drawing a remote
        // shot (RemoteShotFx) knows nothing but the event's itemId — it must draw the trail without
        // knowing damage, magazine or range. A presentation parameter, NOT a behaviour field.
        [Header("Tracer (uzak sunum)")]
        [Tooltip("Mermi izinin rengi (alfa dahil).")]
        [SerializeField] private Color tracerColor = new Color(1f, 0.85f, 0.4f, 0.9f);

        [Tooltip("Mermi izinin baş kalınlığı (metre); kuyruk bunun küçük bir oranıdır.")]
        [SerializeField] private float tracerWidth = 0.02f;

        // ⚠️ This duration starts at ARRIVAL, not at the shot: the streak flies muzzle → impact at a
        // fixed speed (ShotTracer), so flight time comes from distance and is NOT part of this field;
        // what it covers is arrival → completely gone (ShotTracer.FadeAlphaAt). A separate "fade
        // duration" field is deliberately absent — with two numbers, which one cuts the other (a fade
        // longer than the lifetime truncates the trail) would be a silent trap.
        [Tooltip("İz parçasının hedefe VARIŞINDAN tümden sönmesine kadar geçen süre (saniye). " +
                 "Namludan hedefe uçuş süresi mesafeden gelir, bu süreye dahil değildir; " +
                 "iz bu süre boyunca sönerek kaybolur, sonunda pat diye kapanmaz.")]
        [SerializeField] private float tracerLifetime = 0.1f;

        // ⚠️ NOT every round gets a tracer. Two reasons:
        // (a) Real weapons work that way too — drawing every round looks like a laser beam and exposes
        //     the shooter's position more than it should.
        // (b) Budget: at full auto 16 players produce ~160 shots/s, a third of that ~53/s. The real
        //     cost is GC/draw calls, not BYTES — the wire already carries 9 B per event.
        [Tooltip("Kaçta bir mermiye tracer çizilir. 1 = her mermi, 0/negatif = tracer kapalı.")]
        [SerializeField] private int tracerEveryNthRound = 3;

        /// <summary>Name shown in the UI.</summary>
        public string DisplayName => displayName;

        /// <summary>
        /// Item id on the wire (§6.6). <c>0</c> is RESERVED for "empty hand", so an unassigned
        /// definition is invalid — checked via <see cref="HasNetItemId"/>.
        /// </summary>
        public byte NetItemId => (byte)netItemId;

        /// <summary>
        /// Is an id assigned. ⚠️ The real protection is not this property but the editor guard (the net
        /// item catalog pass of the <c>Configure All Build Elements</c> sync): a colliding/missing id
        /// does not break compilation, it shows up in the field as "the wrong item was drawn in their
        /// hand".
        /// </summary>
        public bool HasNetItemId => netItemId >= 1 && netItemId <= 255;

        /// <summary>Item prefab (may be unassigned).</summary>
        public GameObject Prefab => prefab;

        /// <summary>How many hands it is held with.</summary>
        public ItemHoldMode HoldMode => holdMode;

        /// <summary>Is it two-handed (shortcut).</summary>
        public bool IsTwoHanded => holdMode == ItemHoldMode.TwoHand;

        /// <summary>How the item gets into the hand (<see cref="ItemGrabPath"/>). Consumers:
        /// <c>WeaponFrame</c> (distance), <c>WristHolster</c> (wrist), <c>NetObjectGrabBridge</c>
        /// (socket) — and the editor guard that compares this rule against the prefab.</summary>
        public ItemGrabPath GrabPath => grabPath;

        /// <summary>Whether the hand gets a clone or the object itself (<see cref="ItemInstancing"/>).</summary>
        public ItemInstancing Instancing => instancing;

        /// <summary>What happens on release (<see cref="ItemReleaseMode"/>).</summary>
        public ItemReleaseMode ReleaseMode => releaseMode;

        /// <summary>Single instance, ownership handed over (shortcut).
        /// <para>⚠️ Also the gate that keeps this item's <c>itemL</c>/<c>itemR</c> byte at <b>0</b>
        /// (§6.6) and stops the remote avatar building a second copy — one object must not exist both
        /// as a network instance and as a byte-driven clone.</para></summary>
        public bool IsWorldSingle => instancing == ItemInstancing.WorldSingle;

        /// <summary>
        /// The <b>authored</b> record of the requested grip point for the requested hand.
        /// <para>⚠️ When the requested hand is unauthored it <b>falls back to the OTHER hand's record</b>
        /// (<c>default</c> if neither exists): a weapon authored for one hand should be held in an
        /// approximate but sane pose in the other, rather than snapped to the origin. ⚠️ The fallback is
        /// for <b>reading</b> only — "is it authored" is answered by <see cref="HasGrip"/> and that does
        /// NOT fall back, otherwise a missing hand would never appear in any report.</para>
        /// </summary>
        public ItemGripPose GetGrip(GripSocketKind kind, bool rightHand)
        {
            bool secondary = kind == GripSocketKind.Secondary;
            ItemGripPose own = secondary
                ? (rightHand ? secondaryGripRight : secondaryGripLeft)
                : (rightHand ? primaryGripRight : primaryGripLeft);

            if (own.IsAuthored)
            {
                return own;
            }

            ItemGripPose other = secondary
                ? (rightHand ? secondaryGripLeft : secondaryGripRight)
                : (rightHand ? primaryGripLeft : primaryGripRight);

            return other.IsAuthored ? other : default;
        }

        /// <summary>Is this grip point authored <b>for this hand</b> (no fallback to the other hand —
        /// rationale in <see cref="GetGrip"/>).</summary>
        public bool HasGrip(GripSocketKind kind, bool rightHand)
        {
            if (kind == GripSocketKind.Secondary)
            {
                return (rightHand ? secondaryGripRight : secondaryGripLeft).IsAuthored;
            }

            return (rightHand ? primaryGripRight : primaryGripLeft).IsAuthored;
        }

        /// <summary>
        /// This slot's rigged finger pose as the joint array the <b>local synthetic hand</b> (ISDK)
        /// expects — <c>SyntheticHand.OverrideAllJoints</c> form. A slot with unrigged fingers falls
        /// back to the idle array.
        /// <para>The pose is PART of the record: the hand on the primary grip is on the trigger, the one
        /// wrapping the foregrip is closed — the two do not fit into a single "item pose" field.</para>
        /// <para>⚠️ The returned array is <b>CACHED and SHARED</b> (read per frame): the caller does NOT
        /// modify it, only reads.</para>
        /// </summary>
        public Quaternion[] GripJointRotations(GripSocketKind kind, bool rightHand)
        {
            _gripJointCache ??= new Quaternion[4][];

            int slot = GripSlot(kind, rightHand);
            Quaternion[] cached = _gripJointCache[slot];
            if (cached != null)
            {
                return cached;
            }

            ItemGripPose grip = GetGrip(kind, rightHand);
            cached = grip.HasFingers
                ? HandPoseLibrary.BuildJointRotations(grip.fingerJoints, rightHand)
                : HandPoseLibrary.IdleJointRotations(rightHand);

            _gripJointCache[slot] = cached;
            return cached;
        }

        /// <summary>
        /// The same slot for the <b>remote avatar's humanoid (Mixamo) hand</b>: curl ratio per finger,
        /// MEASURED from the rigged pose (<see cref="HandPoseLibrary.MeasureCurl"/>).
        /// <para>⚠️ Raw joint rotations cannot be written onto a humanoid bone (the two skeletons' axes
        /// differ — the project's learned rule); this ratio is the bridge. The ratio is <b>not stored in
        /// the asset</b>: derived data would be a second source of truth.</para>
        /// </summary>
        public HandPoseProfile GripFingerCurl(GripSocketKind kind, bool rightHand)
        {
            _gripCurlCache ??= new HandPoseProfile[4];
            _gripCurlResolved ??= new bool[4];

            int slot = GripSlot(kind, rightHand);
            if (_gripCurlResolved[slot])
            {
                return _gripCurlCache[slot];
            }

            ItemGripPose grip = GetGrip(kind, rightHand);
            HandPoseProfile profile = grip.HasFingers
                ? HandPoseLibrary.MeasureCurl(grip.fingerJoints, rightHand)
                : HandPoseProfile.Idle;

            _gripCurlCache[slot] = profile;
            _gripCurlResolved[slot] = true;
            return profile;
        }

        /// <summary>Cache slot: [grip point, hand] → <c>0..3</c>.</summary>
        private static int GripSlot(GripSocketKind kind, bool rightHand)
        {
            return (kind == GripSocketKind.Secondary ? 2 : 0) + (rightHand ? 1 : 0);
        }

        /// <summary>
        /// Drops the derived finger caches — called whenever a record changes.
        /// <para>⚠️ All four slots drop: clearing one hand's record can make the other hand fall back to
        /// it (<see cref="GetGrip"/>), so refreshing a single slot would leave its neighbour stale.</para>
        /// </summary>
        private void InvalidateGripCache()
        {
            _gripJointCache = null;
            _gripCurlCache = null;
            _gripCurlResolved = null;
        }

        /// <summary>
        /// The <b>ITEM's</b> local position relative to the main hand anchor (metres): <c>itemPos =
        /// palm.pos + palm.rot * this</c>; rotation is always the anchor's own
        /// (<see cref="ItemGripSolver"/>).
        /// <para>Derivation: the record is the anchor's position relative to the item, and this is its
        /// inverse (just a sign — the item is aligned with the controller, so no other transform). Zero
        /// for an unauthored record: the item sits right on the controller.</para>
        /// </summary>
        public Vector3 PrimaryGripPosition(bool rightHand)
        {
            return -GetGrip(GripSocketKind.Primary, rightHand).position;
        }

        /// <summary>
        /// The primary grip point's local position <b>relative to the ITEM</b> (metres) — read by the
        /// side resolving the item's world pose (<see cref="ItemGripSolver"/>).
        /// <para>⚠️ <b>Not a separate field but the record itself:</b> the record already answers "where
        /// on the item the controller is". A second serialized field would keep the same point in two
        /// places, one updated and the other forgotten.</para>
        /// </summary>
        public Vector3 PrimaryGripPointOnItem(bool rightHand)
        {
            return GetGrip(GripSocketKind.Primary, rightHand).position;
        }

        /// <summary>
        /// Is the foregrip record authored <b>for at least one hand</b> (meaningful only when
        /// <see cref="IsTwoHanded"/>; always <c>false</c> for one-handed items). EVERY path reading the
        /// foregrip (<c>Weapon</c>'s socket and gate, <c>HandGripPoser</c>, <c>RemoteAvatar</c>) checks
        /// this first.
        /// <para>⚠️ <b>An unauthored foregrip IS THE ITEM ROOT:</b> <see cref="GetGrip"/> returns
        /// <c>default</c> (zero pose) when neither hand exists, so <see cref="SecondaryGripPosition"/>
        /// gives the root. On most weapons that point sits right at the main hand's wrist — with the gate
        /// open the socket sphere would appear over the main hand, the second hand could not hold "the
        /// grip", and it would show up not as an error but as "the indicator is in the wrong place".
        /// Hence an unauthored foregrip <b>does not exist</b>: no socket is drawn, no second hand binds,
        /// and <c>Weapon</c> warns once. The only place records are authored is the studio
        /// (<c>Kavrama Pozu Stüdyosu</c>).</para>
        /// </summary>
        public bool HasSecondaryGrip =>
            IsTwoHanded && (secondaryGripRight.IsAuthored || secondaryGripLeft.IsAuthored);

        /// <summary>
        /// The foregrip point's local position <b>relative to the ITEM</b> (metres) — meaningful only
        /// when <see cref="HasSecondaryGrip"/> (unauthored returns zero = the item root; the caller
        /// checks that gate first). The second hand's world target is
        /// <c>item.position + item.rotation * this</c> (⚠️ NOT <c>TransformPoint</c>: the measure is in
        /// metres and must not grow with the item's visual scale).
        /// </summary>
        public Vector3 SecondaryGripPosition(bool rightHand)
        {
            return GetGrip(GripSocketKind.Secondary, rightHand).position;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Writes the grip into the matching field — <b>the studio's only write gate</b> (exists to keep
        /// the fields private; a second writer would mean describing which of the four fields is which
        /// hand a second time).
        /// <para>⚠️ Does NOT call <c>EditorUtility.SetDirty</c>/<c>SaveAssets</c>: the caller usually
        /// writes several fields in a row and wants one Undo/dirty step.</para>
        /// </summary>
        /// <param name="anchorInItem">Controller anchor's local position relative to the ITEM (metres, unscaled).</param>
        /// <param name="wristInAnchor">Hand model's local pose relative to the controller anchor (metres,
        /// unscaled) — carries the hand sitting sideways/below relative to the weapon.</param>
        /// <param name="fingerJoints">Finger joints rigged for that slot (may be empty — the hand then
        /// stays in its idle pose).</param>
        public void EditorSetGrip(GripSocketKind kind, bool rightHand, in Vector3 anchorInItem,
            in Pose wristInAnchor, HandJointRotation[] fingerJoints)
        {
            ItemGripPose capture = ItemGripPose.From(anchorInItem, wristInAnchor, fingerJoints);
            InvalidateGripCache();

            if (kind == GripSocketKind.Secondary)
            {
                if (rightHand)
                {
                    secondaryGripRight = capture;
                }
                else
                {
                    secondaryGripLeft = capture;
                }

                return;
            }

            if (rightHand)
            {
                primaryGripRight = capture;
            }
            else
            {
                primaryGripLeft = capture;
            }
        }

        /// <summary>
        /// Returns a grip record to <b>unauthored</b> (<c>authored = false</c>) — the delete direction of
        /// the same gate as <see cref="EditorSetGrip"/>.
        /// <para>⚠️ Zeroing the pose is NOT enough: a zero pose is a valid grip
        /// (<see cref="ItemGripPose"/>), so the "all zero = unauthored" shortcut would be silently wrong
        /// here — the flag itself is cleared so the read path can fall back to the other hand's record
        /// and tools can report the missing grip.</para>
        /// </summary>
        public void EditorClearGrip(GripSocketKind kind, bool rightHand)
        {
            ItemGripPose empty = default;
            InvalidateGripCache();

            if (kind == GripSocketKind.Secondary)
            {
                if (rightHand)
                {
                    secondaryGripRight = empty;
                }
                else
                {
                    secondaryGripLeft = empty;
                }

                return;
            }

            if (rightHand)
            {
                primaryGripRight = empty;
            }
            else
            {
                primaryGripLeft = empty;
            }
        }

        /// <summary>
        /// Drops the derived finger caches on every change coming from the Inspector (or an
        /// Undo/Revert).
        /// <para>⚠️ The write gates already drop the cache; this gate closes the paths that BYPASS them
        /// (Undo, prefab revert, hand-editing the asset). Since grip fields are visible in the Inspector,
        /// "only the studio writes" is a habit, not a contract.</para>
        /// </summary>
        private void OnValidate()
        {
            InvalidateGripCache();
        }
#endif

        /// <summary>
        /// Foregrip socket radius (metres): while an empty hand's controller ANCHOR is inside this
        /// sphere, a grip press binds the second hand to the foregrip
        /// (<c>Weapon.IsHandOnSecondaryGrip</c>) and the socket sphere the player sees is drawn with
        /// exactly this radius (visual = acceptance volume) — meaningful only when
        /// <see cref="IsTwoHanded"/>.
        /// <para>⚠️ <b>The 1 cm floor must stay:</b> a zero (or negative) radius makes the foregrip
        /// mathematically ungrabbable — in the field that shows up NOT as an error but as "the second
        /// hand does not hold", which is expensive to diagnose. An unset/zeroed asset keeps working
        /// thanks to it.</para>
        /// </summary>
        public float SecondaryGripRadius => Mathf.Max(0.01f, secondaryGripRadius);

        /// <summary>Colour of the tracer drawn for a remote shot.</summary>
        public Color TracerColor => tracerColor;

        /// <summary>Tracer width (metres).</summary>
        public float TracerWidth => tracerWidth;

        /// <summary>Tracer lifetime (seconds).</summary>
        public float TracerLifetime => tracerLifetime;

        /// <summary>
        /// Draw a tracer every Nth round (<c>1</c> = every round, <c>0</c>/negative = off).
        /// <para>⚠️ A <b>playtest setting</b>, and it lives here in the SO: the right number is found by
        /// eye in the field. Drawing every round looks like a laser beam, exposes position too much and
        /// eats the draw/GC budget under heavy fire (the real cost is draw calls, not bytes).</para>
        /// </summary>
        public int TracerEveryNthRound => tracerEveryNthRound;
    }
}
