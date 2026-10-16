using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The lock plate of ONE weapon slot on a zombie board: a warm backplate behind the
    /// weapon, a dark overlay + lock icon + "DALGA N" in front while it is locked, and a short red
    /// pulse when its wave arrives.</summary>
    /// <remarks>Added at RUNTIME by <see cref="ZombieWeaponUnlocks"/> to the weapon the board's
    /// <c>WeaponFrame</c> sits on; the board prefab is never touched (unpacking a board instance is
    /// forbidden) and no other mode pays for it.
    /// <para>⚠️ ONE world-space Canvas per slot with the UI default material: no new shader (a
    /// Shader.Find-only shader is stripped from the build) and no realtime light.</para>
    /// <para>⚠️ The dark arena is why the backplate exists at all and why it stays after unlocking:
    /// without it the weapon is a black silhouette on a black board.</para></remarks>
    internal class ZombieWeaponLockView : MonoBehaviour
    {
        /// <summary>Canvas units per metre — the canvas is authored in "pixels" and scaled down, so the
        /// font sizes below read like the rest of the UI kit.</summary>
        private const float UnitsPerMeter = 1000f;

        /// <summary>Gap (m) between the weapon's box and the front overlay.</summary>
        private const float FrontGapMeters = 0.01f;

        /// <summary>Gap (m) between the weapon's box and the backplate.</summary>
        /// <remarks>⚠️ Kept small: at 1 cm the backplate lands ON the board's slot plate face (the
        /// weapon hangs ~1 cm off it) and the transparent plate z-fights it — visible shimmer in the
        /// headset.</remarks>
        private const float BackplateGapMeters = 0.003f;

        /// <summary>Plate margin around the weapon's measured face (ratio of its size).</summary>
        private const float PlateMargin = 0.12f;

        private const float OverlayFadeSeconds = 0.6f;
        private const float PulseSeconds = 2f;

        /// <summary>Lock icon. ⚠️ The theme has no padlock sprite; <c>X</c> is the set's "you cannot"
        /// glyph and the wave number next to it carries the "yet" part.</summary>
        private const string LockIconName = "X";

        private static readonly List<ZombieWeaponLockView> Views = new List<ZombieWeaponLockView>();

        /// <summary>Every live view, in creation order.</summary>
        public static IReadOnlyList<ZombieWeaponLockView> All => Views;

        private Weapon _weapon;
        private int _unlockWave = 1;

        private RectTransform _front;
        private Image _overlay;
        private Image _lockIcon;
        private TMP_Text _label;

        /// <summary>Seconds left of the unlock animation; <c>&lt;= 0</c> = idle (Update returns at once,
        /// which is the whole point — a board holds one per slot).</summary>
        private float _animLeft;

        /// <summary><c>weaponId</c> of the slot, or empty when the weapon has no definition.</summary>
        public string WeaponId =>
            _weapon != null && _weapon.Definition != null ? _weapon.Definition.WeaponId : string.Empty;

        /// <summary>Label for the toast: the definition's display name, else the bare id upper-cased.</summary>
        public string DisplayName
        {
            get
            {
                if (_weapon == null || _weapon.Definition == null)
                {
                    return string.Empty;
                }

                string display = _weapon.Definition.DisplayName;
                return string.IsNullOrEmpty(display)
                    ? Girdap.Upper(_weapon.Definition.WeaponId)
                    : Girdap.Upper(display);
            }
        }

        /// <summary>The wave this slot opens at (1 = open from the start).</summary>
        public int UnlockWave => _unlockWave;

        /// <summary>Builds the plates around <paramref name="frame"/>'s weapon. Called right after
        /// <c>AddComponent</c>, before the first <see cref="Apply"/>.</summary>
        public void Bind(WeaponFrame frame)
        {
            _weapon = GetComponent<Weapon>();
            if (_weapon == null)
            {
                return;
            }

            _unlockWave = ZombieArsenal.Live.UnlockWave(WeaponId);
            Build(frame);
        }

        private void OnEnable()
        {
            if (!Views.Contains(this))
            {
                Views.Add(this);
            }
        }

        private void OnDisable()
        {
            Views.Remove(this);
        }

        /// <summary>Locks or opens the slot for <paramref name="wave"/> with NO animation — the state
        /// after a scene load, a mode change or a late join.</summary>
        public void Apply(int wave)
        {
            _animLeft = 0f;
            SetLocked(_unlockWave > Mathf.Max(1, wave));
        }

        /// <summary>Opens the slot with the unlock animation: the overlay fades, the label turns into
        /// "AÇILDI" and pulses red, then the whole front group hides.</summary>
        public void PlayUnlock()
        {
            if (_front == null)
            {
                return;
            }

            _front.gameObject.SetActive(true);
            _animLeft = PulseSeconds;

            if (_label != null)
            {
                _label.text = "AÇILDI";
            }
        }

        private void Update()
        {
            if (_animLeft <= 0f)
            {
                return;
            }

            _animLeft -= Time.deltaTime;
            float elapsed = PulseSeconds - _animLeft;

            if (_overlay != null)
            {
                Color color = _overlay.color;
                color.a = OverlayAlpha * Mathf.Clamp01(1f - elapsed / OverlayFadeSeconds);
                _overlay.color = color;
            }

            // Two beats over the pulse; alpha never reaches 0 so the word stays readable between them.
            float beat = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(elapsed * Mathf.PI * 2f));
            if (_label != null)
            {
                _label.color = new Color(Kan.BloodHi.r, Kan.BloodHi.g, Kan.BloodHi.b, beat);
            }

            if (_lockIcon != null)
            {
                _lockIcon.enabled = false;
            }

            if (_animLeft <= 0f)
            {
                _animLeft = 0f;
                SetLocked(false);
            }
        }

        private void SetLocked(bool locked)
        {
            if (_front != null)
            {
                _front.gameObject.SetActive(locked);
            }

            if (!locked)
            {
                return;
            }

            if (_overlay != null)
            {
                _overlay.color = OverlayColor;
            }

            if (_lockIcon != null)
            {
                _lockIcon.enabled = _lockIcon.sprite != null;
            }

            if (_label != null)
            {
                _label.text = $"DALGA {_unlockWave}";
                _label.color = Kan.BloodHi;
            }
        }

        // ------------------------------------------------------------------- palette

        /// <summary>Overlay weight — enough to read "not yours yet" without hiding which gun it is.</summary>
        private const float OverlayAlpha = 0.65f;

        private static Color OverlayColor =>
            new Color(Kan.PlateB.r, Kan.PlateB.g, Kan.PlateB.b, OverlayAlpha);

        /// <summary>Backplate: bone, very faint — a silhouette holder, not a panel.</summary>
        private static Color BackplateColor =>
            new Color(Kan.Bone.r, Kan.Bone.g, Kan.Bone.b, 0.12f);

        // --------------------------------------------------------------------- build

        /// <summary>Measures the weapon in the BOARD's frame (so the plates sit parallel to the board
        /// face) and builds the one canvas.</summary>
        private void Build(WeaponFrame frame)
        {
            Transform board = ResolveBoardFacing(frame);
            Quaternion facing = board != null ? board.rotation : transform.rotation;

            if (!MeasureInFacing(facing, frame, out Vector3 centerWorld, out Vector3 sizeMeters))
            {
                return;
            }

            // The RectTransform is created UP FRONT: adding a Canvas to a plain Transform swaps the
            // component out, and a Transform reference cached before that no longer points at it.
            var canvasGo = new GameObject("ZombieWeaponLock", typeof(RectTransform));
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.SetParent(transform, false);
            canvasRect.position = centerWorld;

            // ⚠️ The canvas +Z must point AWAY from the player: UI is visible from its −Z side, and the
            // board's front face is its local −Z, i.e. the player stands where the board looks from.
            canvasRect.rotation = facing;

            // World scale is forced to 1/UnitsPerMeter whatever the board instance's scale is (the
            // boards are placed scaled), so the plates and the label keep the same physical size.
            float parentScale = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
            canvasRect.localScale = Vector3.one / (UnitsPerMeter * parentScale);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            float width = sizeMeters.x * (1f + PlateMargin * 2f) * UnitsPerMeter;
            float height = sizeMeters.y * (1f + PlateMargin * 2f) * UnitsPerMeter;
            float backDepth = (sizeMeters.z * 0.5f + BackplateGapMeters) * UnitsPerMeter;
            float frontDepth = (sizeMeters.z * 0.5f + FrontGapMeters) * UnitsPerMeter;
            var plateSize = new Vector2(width, height);

            canvasRect.sizeDelta = plateSize;

            BuildBackplate(canvasRect, plateSize, backDepth);
            BuildFront(canvasRect, plateSize, frontDepth, Mathf.Min(width, height));
        }

        private void BuildBackplate(RectTransform parent, Vector2 size, float depth)
        {
            Image plate = NewImage("Backplate", parent);

            // +Z = behind the weapon: the model occludes the plate, which is exactly what makes the
            // silhouette readable instead of hiding it.
            Center(plate.rectTransform, size, depth);
            plate.color = BackplateColor;
        }

        private void BuildFront(RectTransform parent, Vector2 size, float frontDepth, float minSide)
        {
            var frontGo = new GameObject("Front", typeof(RectTransform));
            _front = (RectTransform)frontGo.transform;
            _front.SetParent(parent, false);
            Center(_front, size, -frontDepth);

            _overlay = NewImage("Overlay", _front);
            Center(_overlay.rectTransform, size, 0f);
            _overlay.color = OverlayColor;

            float icon = minSide * 0.3f;
            _lockIcon = NewImage("Lock", _front);
            _lockIcon.sprite = Girdap.Icon(LockIconName);
            _lockIcon.color = Kan.BloodHi;
            _lockIcon.preserveAspect = true;
            Center(_lockIcon.rectTransform, new Vector2(icon, icon), 0f);
            _lockIcon.rectTransform.anchoredPosition = new Vector2(0f, icon * 0.6f);
            _lockIcon.enabled = _lockIcon.sprite != null;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(_front, false);
            _label = labelGo.AddComponent<TextMeshProUGUI>();
            _label.font = Girdap.Font(GirdapFont.RubikWetPaint);
            _label.fontSize = minSide * 0.22f;
            _label.color = Kan.BloodHi;
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.raycastTarget = false;
            Center((RectTransform)labelGo.transform, new Vector2(size.x, minSide * 0.4f), 0f);
            ((RectTransform)labelGo.transform).anchoredPosition = new Vector2(0f, -icon * 0.6f);

            _front.gameObject.SetActive(false);
        }

        private static Image NewImage(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Centre anchors + explicit size. ⚠️ Not a stretched rect: the plates are offset along
        /// Z, and on a stretched rect that offset is derived from the margins instead of being set.</summary>
        private static void Center(RectTransform rect, Vector2 size, float z)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition3D = new Vector3(0f, 0f, z);
        }

        /// <summary>The board instance the slot belongs to; its rotation is what the plates align to.
        /// Falls back to the weapon itself for a weapon standing on its own.</summary>
        private Transform ResolveBoardFacing(WeaponFrame frame)
        {
            var canvasMarker = GetComponentInParent<WeaponCanvas>();
            if (canvasMarker != null)
            {
                return canvasMarker.transform;
            }

            return frame != null ? frame.transform : transform;
        }

        /// <summary>Weapon renderer bounds expressed in <paramref name="facing"/>'s axes (world metres),
        /// so width/height come out parallel to the board face.</summary>
        /// <remarks>⚠️ The frame's own subtree is skipped: the frame art is a selection volume fitted to
        /// the weapon and would inflate the plates.</remarks>
        private bool MeasureInFacing(Quaternion facing, WeaponFrame frame,
            out Vector3 centerWorld, out Vector3 sizeMeters)
        {
            centerWorld = transform.position;
            sizeMeters = Vector3.zero;

            Quaternion toFacing = Quaternion.Inverse(facing);
            Bounds box = default;
            bool any = false;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null ||
                    (frame != null && renderer.transform.IsChildOf(frame.transform)))
                {
                    continue;
                }

                Bounds local = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? local.min.x : local.max.x,
                        (corner & 2) == 0 ? local.min.y : local.max.y,
                        (corner & 4) == 0 ? local.min.z : local.max.z);

                    Vector3 point = toFacing * renderer.transform.TransformPoint(offset);
                    if (!any)
                    {
                        box = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        box.Encapsulate(point);
                    }
                }
            }

            if (!any)
            {
                return false;
            }

            centerWorld = facing * box.center;
            sizeMeters = box.size;
            return true;
        }

        /// <summary>Removes the plates and the component — the mode is over, and the weapon must look
        /// exactly as the board authored it again.</summary>
        public void Teardown()
        {
            Views.Remove(this);

            if (_front != null && _front.parent != null)
            {
                Destroy(_front.parent.gameObject);
            }

            _front = null;
            _overlay = null;
            _lockIcon = null;
            _label = null;
            Destroy(this);
        }

        private void OnDestroy()
        {
            Views.Remove(this);
        }
    }
}
