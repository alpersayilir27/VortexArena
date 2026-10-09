using System.Collections;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Net;

namespace VortexArena.Core.GripCapture
{
    /// <summary>
    /// Play-mode grip capture: the operator holds the weapon's clone with one real hand while the
    /// other hand's controller presses a slot button, and the tracked finger pose — clamped to the
    /// item's surface — is written to the buffer.
    /// <para>⚠️ Editor-only assembly, so <c>AssetDatabase</c>/<c>EditorPrefs</c> are used directly.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class GripCaptureSession : MonoBehaviour, IAuthoringSession
    {
        private const float RotateSpeed = 90f;
        private const float Deadzone = 0.15f;
        private const float ForwardDistance = 0.40f;
        private const float DownDistance = 0.15f;
        private const float HapticSeconds = 0.08f;
        private const float HapticGap = 0.06f;
        private const float HapticFrequency = 0.5f;
        private const float HapticAmplitude = 0.6f;

        /// <summary>The rig needs a moment before the head pose means anything.</summary>
        private const float HeadSettleSeconds = 0.25f;

        private readonly GripCaptureRig _rig = new GripCaptureRig();
        private readonly GripCaptureHud _hud = new GripCaptureHud();
        private readonly GripCaptureHand _leftHand = new GripCaptureHand(false);
        private readonly GripCaptureHand _rightHand = new GripCaptureHand(true);

        private ItemDefinition _definition;
        private GripCaptureItem _item;
        private GripCaptureBuffer _buffer;
        private string _definitionGuid;
        private bool _blocked;
        private bool _ready;
        private bool _restored;
        private float _elapsed;

        private void Awake()
        {
            // ⚠️ In Awake, not Start: the game's AfterSceneLoad bootstraps read this flag, and a
            // singleton installed once cannot be taken back.
            AuthoringSession.Active = true;

            if (!_rig.Build(transform) || !_hud.Build(transform))
            {
                return;
            }

            _definition = GripCaptureSettings.LoadTarget();
            if (_definition == null)
            {
                Fail("Eşya seçilmedi — Inspector'dan seç, Play'i yeniden başlat.");
                return;
            }

            if (_definition.Prefab == null)
            {
                Fail($"Eşyanın prefab'ı yok ({_definition.DisplayName}) — Inspector'dan ata, Play'i " +
                     "yeniden başlat.");
                return;
            }

            _hud.SetItem(_definition.DisplayName, _definition.IsTwoHanded);
            ResolveBuffer();

            _item = new GripCaptureItem();
            if (!_item.Build(_definition.Prefab, transform))
            {
                Fail($"Eşya kopyası kurulamadı ({_definition.DisplayName}).");
                return;
            }

            if (!_leftHand.Build(transform) || !_rightHand.Build(transform))
            {
                Fail("Hayalet eller kurulamadı.");
                return;
            }

            _ready = true;
            RefreshSlots();
        }

        private void LateUpdate()
        {
            _elapsed += Time.deltaTime;
            _rig.Tick();
            _hud.SetMultimodal(_rig.MultimodalStatus());

            if (!_rig.Ready)
            {
                _hud.Tick(_rig.Head);
                return;
            }

            if (!_ready)
            {
                PlaceHudOnly();
                _hud.Tick(_rig.Head);
                return;
            }

            if (!_item.Placed)
            {
                TryFirstPlacement();
            }
            else
            {
                HandleRotation();
            }

            // autoSyncTransforms is off: without this the clamp queries the item's previous pose.
            Physics.SyncTransforms();

            _leftHand.Tick(_rig.Left, _item);
            _rightHand.Tick(_rig.Right, _item);

            _hud.SetHand(false, _leftHand.Valid, _leftHand.Touching, _leftHand.Penetrating,
                _leftHand.PalmInside);
            _hud.SetHand(true, _rightHand.Valid, _rightHand.Touching, _rightHand.Penetrating,
                _rightHand.PalmInside);

            HandleCaptureInput();
            _hud.Tick(_rig.Head);
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }

        private void ResolveBuffer()
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(_definition, out _definitionGuid, out long _);

            GripCaptureBuffer existing = GripCaptureBuffer.Load();
            if (existing == null)
            {
                _buffer = new GripCaptureBuffer { definitionGuid = _definitionGuid };
                return;
            }

            if (existing.definitionGuid == _definitionGuid)
            {
                _buffer = existing;
                return;
            }

            // ⚠️ Capturing is blocked, not silently merged: the buffer holds ONE item's slots and
            // overwriting it would drop someone else's unapplied session.
            _blocked = true;
            _hud.SetError($"Uygulanmamış eski yakalama var ({NameOfGuid(existing.definitionGuid)}) — " +
                          "Kavrama Stüdyosu'ndan uygula ya da sil, sonra Play'i yeniden başlat.");
        }

        private static string NameOfGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return "bilinmeyen eşya";
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            var definition = string.IsNullOrEmpty(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

            return definition != null ? definition.DisplayName : "bilinmeyen eşya";
        }

        private void Fail(string message)
        {
            _ready = false;
            _blocked = true;
            _hud.SetError(message);
            Debug.LogError($"[GripCapture] {message}");
        }

        /// <summary>Panel placement when there is no item (an error is on screen).</summary>
        private void PlaceHudOnly()
        {
            if (_elapsed < HeadSettleSeconds || !HeadPoseValid())
            {
                return;
            }

            Vector3 forward = Flat(_rig.Head.forward);
            _hud.Place(_rig.Head.position + forward * ForwardDistance + Vector3.down * DownDistance,
                _rig.Head);
        }

        private void TryFirstPlacement()
        {
            if (_elapsed < HeadSettleSeconds || !HeadPoseValid())
            {
                return;
            }

            PlaceInFront();
        }

        /// <summary>The head anchor sits at the origin until the first tracking update arrives.</summary>
        private bool HeadPoseValid()
        {
            return _rig.Head != null && _rig.Head.position.sqrMagnitude > 1e-4f;
        }

        private void PlaceInFront()
        {
            Vector3 forward = Flat(_rig.Head.forward);
            Vector3 pivot = _rig.Head.position + forward * ForwardDistance +
                            Vector3.down * DownDistance;

            // LookRotation of the flat forward so a weapon's barrel points away from the operator.
            _item.Place(pivot, Quaternion.LookRotation(forward, Vector3.up));
            _hud.Place(_item.PivotWorld, _rig.Head);

            RestoreFrozenGhosts();
        }

        private void HandleRotation()
        {
            if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.LTouch) ||
                OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch))
            {
                PlaceInFront();
                return;
            }

            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch) +
                            OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);

            if (stick.magnitude < Deadzone)
            {
                return;
            }

            if (stick.magnitude > 1f)
            {
                stick = stick.normalized;
            }

            float step = RotateSpeed * Time.deltaTime;
            _item.Rotate(stick.x * step, Vector3.up);

            // Negative: a positive rotation about +right tips the front DOWN, and the stick pushed
            // forward should raise it.
            _item.Rotate(-stick.y * step, Flat(_rig.Head.right));
        }

        private void HandleCaptureInput()
        {
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch))
            {
                Capture(GripSocketKind.Primary, true, OVRInput.Controller.LTouch);
            }
            else if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch))
            {
                Capture(GripSocketKind.Secondary, true, OVRInput.Controller.LTouch);
            }
            else if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            {
                Capture(GripSocketKind.Primary, false, OVRInput.Controller.RTouch);
            }
            else if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
            {
                Capture(GripSocketKind.Secondary, false, OVRInput.Controller.RTouch);
            }
        }

        private void Capture(GripSocketKind kind, bool rightHand, OVRInput.Controller controller)
        {
            string handLabel = rightHand ? "Sağ el" : "Sol el";
            string slotLabel = kind == GripSocketKind.Secondary ? "2. kavrama" : "1. kavrama";

            if (_blocked || _buffer == null)
            {
                Reject(controller, "Yakalama engelli — önce eski yakalamayı uygula ya da sil.");
                return;
            }

            if (!_item.Placed)
            {
                Reject(controller, "Eşya henüz önüne gelmedi — kafanı kaldır, bir saniye bekle.");
                return;
            }

            GripCaptureHand hand = rightHand ? _rightHand : _leftHand;
            if (!hand.Valid)
            {
                Reject(controller, $"{handLabel} izlenmiyor — elini kumandasız tut, kameraya göster.");
                return;
            }

            if (!hand.TryRecord(_item, out ItemGripPose pose))
            {
                Reject(controller, $"{handLabel} kaydı alınamadı — tekrar dene.");
                return;
            }

            _buffer.Put(kind, rightHand, pose);
            _buffer.Save();

            hand.CreateFrozenGhost(kind, hand.Wrist, hand.ClampedJoints, _item.Root);

            string message = $"{handLabel} · {slotLabel} yakalandı ({hand.TouchCount} parmak temas)";
            _hud.SetLastAction(message, true);
            RefreshSlots();
            Debug.Log($"[GripCapture] {message} — {_definition.DisplayName}");

            StartCoroutine(Pulse(controller, 1));
        }

        private void Reject(OVRInput.Controller controller, string reason)
        {
            _hud.SetLastAction(reason, false);
            StartCoroutine(Pulse(controller, 2));
        }

        private IEnumerator Pulse(OVRInput.Controller controller, int count)
        {
            for (int i = 0; i < count; i++)
            {
                OVRInput.SetControllerVibration(HapticFrequency, HapticAmplitude, controller);
                yield return new WaitForSeconds(HapticSeconds);
                OVRInput.SetControllerVibration(0f, 0f, controller);

                if (i + 1 < count)
                {
                    yield return new WaitForSeconds(HapticGap);
                }
            }
        }

        private void RestoreFrozenGhosts()
        {
            if (_restored || _buffer == null || _item == null || _item.Root == null)
            {
                return;
            }

            _restored = true;

            for (int i = 0; i < _buffer.entries.Count; i++)
            {
                GripCaptureEntry entry = _buffer.entries[i];
                GripCaptureHand hand = entry.rightHand ? _rightHand : _leftHand;

                // Recomposition is symmetric with the record: item.position + item.rotation * p,
                // never TransformPoint (weapon roots are scaled).
                Quaternion anchorRotation = _item.Rotation;
                Vector3 anchorPosition = _item.Position + anchorRotation * entry.pose.position;

                var wrist = new Pose(anchorPosition + anchorRotation * entry.pose.wristPosition,
                    anchorRotation * entry.pose.wristRotation);

                Quaternion[] joints = HandPoseLibrary.BuildJointRotations(entry.pose.fingerJoints,
                    entry.rightHand);

                hand.CreateFrozenGhost(entry.kind, wrist, joints, _item.Root);
            }
        }

        private void RefreshSlots()
        {
            SetSlot(GripSocketKind.Primary, true);
            SetSlot(GripSocketKind.Primary, false);
            SetSlot(GripSocketKind.Secondary, true);
            SetSlot(GripSocketKind.Secondary, false);

            bool secondary = _buffer != null && _buffer.HasKind(GripSocketKind.Secondary);
            bool primary = _buffer != null && _buffer.HasKind(GripSocketKind.Primary);

            if (secondary)
            {
                _hud.SetForecast("Çıkışta: ÇİFT kavramalı", false);
            }
            else if (primary)
            {
                _hud.SetForecast("Çıkışta: TEK kavramalı — 2. kavrama silinecek",
                    _definition != null && _definition.IsTwoHanded);
            }
            else
            {
                _hud.SetForecast("Çıkışta: değişiklik yok", false);
            }
        }

        private void SetSlot(GripSocketKind kind, bool rightHand)
        {
            string state = "—";
            if (_buffer != null && _buffer.Has(kind, rightHand))
            {
                state = "YENİ";
            }
            else if (_definition != null && _definition.HasGrip(kind, rightHand))
            {
                state = "kayıtlı";
            }

            _hud.SetSlot(kind, rightHand, state);
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        }
    }
}
