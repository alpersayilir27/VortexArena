using System.Reflection;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEngine;

namespace VortexArena.Core.GripCapture
{
    /// <summary>
    /// The tool's own OVR rig + ISDK hand data sources, built from the SDK prefabs at runtime.
    /// <para>⚠️ Everything is instantiated under an INACTIVE holder and configured there: OVRManager
    /// and the data sources read their fields in Awake, so a field set after activation is ignored
    /// (symptom: multimodal never turns on, hands stay disconnected).</para>
    /// </summary>
    public sealed class GripCaptureRig
    {
        private const string RigPrefabPath =
            "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

        private const string HandsPrefabPath =
            "Packages/com.meta.xr.sdk.interaction.ovr/Runtime/Prefabs/Hands/OVRHandsDataSource.prefab";

        private Hand[] _hands;
        private bool _multimodalRequested;

        public OVRCameraRig Rig { get; private set; }

        public Transform Head { get; private set; }

        public IHand Left { get; private set; }

        public IHand Right { get; private set; }

        /// <summary>Rig built, OVRManager initialized and the head pose readable.</summary>
        public bool Ready => Rig != null && Head != null && OVRManager.OVRManagerinitialized;

        /// <summary>Builds the rig and the hand data sources; <c>false</c> when an SDK asset is
        /// missing.</summary>
        public bool Build(Transform parent)
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            var handsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandsPrefabPath);

            if (rigPrefab == null)
            {
                Debug.LogError($"[GripCapture] OVR rig prefab'ı yüklenemedi: {RigPrefabPath}");
                return false;
            }

            if (handsPrefab == null)
            {
                Debug.LogError($"[GripCapture] ISDK el veri kaynağı yüklenemedi: {HandsPrefabPath}");
                return false;
            }

            var holder = new GameObject("Rig");
            holder.transform.SetParent(parent, false);
            holder.SetActive(false);

            GameObject rigObject = Object.Instantiate(rigPrefab, holder.transform);
            rigObject.name = "OVRCameraRig";
            Rig = rigObject.GetComponent<OVRCameraRig>();
            if (Rig == null)
            {
                Debug.LogError("[GripCapture] OVRCameraRig bileşeni prefab'da bulunamadı.");
                return false;
            }

            ConfigureManager(rigObject);

            GameObject handsObject = Object.Instantiate(handsPrefab, holder.transform);
            handsObject.name = "OVRHandsDataSource";
            WireHands(holder.transform, handsObject);

            holder.SetActive(true);

            // centerEyeAnchor is assigned in OVRCameraRig.Awake, so it is only readable once active.
            Head = Rig.centerEyeAnchor;
            if (Head == null)
            {
                Debug.LogError("[GripCapture] Kafa çapası (centerEyeAnchor) çözülemedi.");
                return false;
            }

            return true;
        }

        /// <summary>Per-frame bookkeeping: multimodal request + which <c>Hand</c> is which side.</summary>
        public void Tick()
        {
            if (!_multimodalRequested && OVRManager.OVRManagerinitialized)
            {
                _multimodalRequested = true;
                bool enabled = OVRInput.EnableSimultaneousHandsAndControllers();
                Debug.Log($"[GripCapture] Eşzamanlı el+kumanda isteği: {enabled} · donanım desteği: " +
                          $"{OVRManager.IsMultimodalHandsControllersSupported()}");
            }

            ResolveHandedness();
        }

        /// <summary>Which input combination is live right now — HUD text.</summary>
        public string MultimodalStatus()
        {
            bool leftController = OVRInput.IsControllerConnected(OVRInput.Controller.LTouch);
            bool rightController = OVRInput.IsControllerConnected(OVRInput.Controller.RTouch);
            bool leftHand = Left != null && Left.IsConnected;
            bool rightHand = Right != null && Right.IsConnected;

            if (leftController && rightHand)
            {
                return "Sol kumanda + sağ el";
            }

            if (rightController && leftHand)
            {
                return "Sağ kumanda + sol el";
            }

            if (leftController && rightController)
            {
                return "İki kumanda";
            }

            if (leftHand && rightHand)
            {
                return "İki el";
            }

            return "—";
        }

        private static void ConfigureManager(GameObject rigObject)
        {
            var manager = rigObject.GetComponentInChildren<OVRManager>(true);
            if (manager == null)
            {
                Debug.LogError("[GripCapture] OVRManager bulunamadı — eşzamanlı el+kumanda açılamaz.");
                return;
            }

            manager.launchSimultaneousHandsControllersOnStartup = true;
            manager.SimultaneousHandsAndControllersEnabled = true;
            manager.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.None;
            manager.isInsightPassthroughEnabled = false;

            // The public property writes through to the runtime; the private field is what Awake
            // applies, so it is set directly.
            FieldInfo origin = typeof(OVRManager).GetField("_trackingOriginType",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (origin != null)
            {
                origin.SetValue(manager, OVRManager.TrackingOrigin.FloorLevel);
            }
            else
            {
                Debug.LogWarning("[GripCapture] _trackingOriginType alanı bulunamadı — izleme " +
                                 "başlangıcı SDK varsayılanında kalıyor.");
            }

            var emulator = rigObject.GetComponentInChildren<OVRHeadsetEmulator>(true);
            if (emulator != null)
            {
                emulator.opMode = OVRHeadsetEmulator.OpMode.Off;
            }
        }

        private void WireHands(Transform holder, GameObject handsObject)
        {
            var rigRefObject = new GameObject("OVRCameraRigRef");
            rigRefObject.transform.SetParent(holder, false);
            var rigRef = rigRefObject.AddComponent<OVRCameraRigRef>();
            rigRef.InjectAllOVRCameraRigRef(Rig, false);

            var transformerObject = new GameObject("TrackingToWorldTransformerOVR");
            transformerObject.transform.SetParent(holder, false);
            var transformer = transformerObject.AddComponent<TrackingToWorldTransformerOVR>();
            transformer.InjectAllTrackingToWorldTransformerOVR(rigRef);

            FieldInfo rigRefField = typeof(FromOVRHandDataSource).GetField("_cameraRigRef",
                BindingFlags.Instance | BindingFlags.NonPublic);

            var sources = handsObject.GetComponentsInChildren<FromOVRHandDataSource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                // ⚠️ The serialized field, not only the interface property: Awake re-reads the field
                // and would overwrite an injected property with null.
                rigRefField?.SetValue(sources[i], rigRef);
                sources[i].InjectTrackingToWorldTransformer(transformer);
            }

            if (rigRefField == null)
            {
                Debug.LogWarning("[GripCapture] FromOVRHandDataSource._cameraRigRef alanı bulunamadı " +
                                 "— el verisi gelmeyebilir.");
            }

            _hands = handsObject.GetComponentsInChildren<Hand>(true);
            if (_hands.Length < 2)
            {
                Debug.LogError("[GripCapture] El veri kaynağında iki Hand bileşeni bulunamadı.");
                return;
            }

            // Prefab order (Left, Right) until Handedness can be read from live data.
            Left = _hands[0];
            Right = _hands[1];
        }

        private void ResolveHandedness()
        {
            if (_hands == null)
            {
                return;
            }

            for (int i = 0; i < _hands.Length; i++)
            {
                if (!_hands[i].IsConnected)
                {
                    continue;
                }

                if (_hands[i].Handedness == Handedness.Left)
                {
                    Left = _hands[i];
                }
                else
                {
                    Right = _hands[i];
                }
            }
        }
    }
}
