using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>Dev tool: measures the hand's two anchor-space terms on device — the hand ANATOMY
    /// (finger direction + palm normal) and <see cref="HandGripPivot"/>'s palm OFFSET (wrist position
    /// relative to the anchor).</summary>
    /// <remarks>
    /// Both come from the same sampling run: measured separately they would capture different hand
    /// poses and the constants would be mutually inconsistent.
    /// <para><b>Why the BB rig's own hand is the source:</b> the player sees their hands in the right
    /// place while holding a controller, so ISDK's controller-driven hand skeleton is the LIVE, correct
    /// answer to "where is the hand relative to the anchor". Measured at that skeleton's wrist
    /// (<c>OVRHandVisualLeft/Right → OculusHand_* → b_*_wrist</c>).</para>
    /// <para>⚠️ <b>Two dead ends, do not retry:</b> (1) <c>OVRInput.Controller.LHand/RHand</c> gives the
    /// hand pose while holding a controller but needs <b>multimodal</b> (simultaneous hand+controller),
    /// disabled in this project — <c>GetControllerIsInHandState</c> always returns "no hand".
    /// (2) The distance-grab preview copies (<c>OVRLeftHandVisual</c>/<c>OVRRightHandVisual</c>) are
    /// disabled by <see cref="ControllerModelHider"/>; their bones are not driven.</para>
    /// <para>⚠️ <b>Those two names differ only by word ORDER and mixing them silently measures the bind
    /// pose:</b> the source is <c>OVRHandVisual<i>Left</i></c>, the discarded copy is
    /// <c>OVR<i>Left</i>HandVisual</c>. The source stays enabled because of
    /// <see cref="ControllerModelHider"/>'s "Driven Hand Visuals" list. Disabling the hider is NOT
    /// needed for this probe.</para>
    /// <para>⚠️ <b>Precondition: <c>OVRManager.controllerDrivenHandPosesType</c> must NOT be
    /// <c>None</c></b> (<c>Natural</c> in the <c>VA_CameraRig</c> prefab). At <c>None</c> no hand data
    /// is produced while holding a controller, the skeleton stays in bind pose and the probe prints an
    /// <b>error-free but wrong</b> constant.</para>
    /// <para>⚠️ <b>The anatomy half is a CROSS-CHECK, not a paste target any more:</b>
    /// <see cref="HandGripConvention.AnchorBasis"/> measures the same quantity at runtime from the
    /// synthetic hand's own skeleton. Pasting a device reading over the last-resort constants there
    /// would give the remote wrist a second, slightly different description of the hand the player is
    /// already holding. What the reading is still good for: if it disagrees with the runtime basis by
    /// more than the hand's own tremor, the skeleton being read is not the one being drawn.</para>
    /// <para><b>Usage:</b> lives in the <c>VA_CameraRig</c> prefab; with both controllers held normally
    /// it logs once and disables itself. The palm OFFSET lines are still pasted straight into
    /// <see cref="HandGripPivot"/>.</para>
    /// </remarks>
    public class HandGripCalibrationProbe : MonoBehaviour
    {
        /// <summary>Valid frames to average — several, so hand tremor cancels out.</summary>
        private const int RequiredSamples = 30;

        /// <summary>No valid frame for this long means measurement is impossible (s).</summary>
        private const float NoDataTimeoutSeconds = 10f;

        /// <summary>Frame-to-frame deviation above this means the measurement never settled (degrees).</summary>
        private const float UnstableAngleDegrees = 5f;

        /// <summary>POSITION counterpart of the same check (m). A separate threshold is needed because the
        /// palm offset is a few centimetres: a run that looks stable in degrees may have drifted
        /// completely in position.</summary>
        private const float UnstablePositionMeters = 0.01f;

        private OVRCameraRig _rig;
        private Transform _leftWrist, _leftMiddle, _leftThumb;
        private Transform _rightWrist, _rightMiddle, _rightThumb;

        private int _sampleCount;
        private float _elapsed;

        // Direction averaging: we only want two direction vectors, no quaternion averaging needed.
        private Vector3 _leftFingerSum, _leftPalmSum, _rightFingerSum, _rightPalmSum;

        // Wrist position delta in ANCHOR space — the source of HandGripPivot.PalmOffset.
        private Vector3 _leftOffsetSum, _rightOffsetSum;

        private Quaternion _leftFirst = Quaternion.identity, _rightFirst = Quaternion.identity;
        private float _leftMaxDeviation, _rightMaxDeviation;

        private Vector3 _leftFirstOffset, _rightFirstOffset;
        private float _leftMaxOffsetDeviation, _rightMaxOffsetDeviation;

        private void Update()
        {
            _elapsed += Time.deltaTime;

            if (!EnsureReferences() ||
                !TryReadAnchorSample(false, out Quaternion leftBasis, out Vector3 leftOffset) ||
                !TryReadAnchorSample(true, out Quaternion rightBasis, out Vector3 rightOffset))
            {
                if (_sampleCount == 0 && _elapsed >= NoDataTimeoutSeconds)
                {
                    Debug.LogWarning(
                        "[HandGripCalibrationProbe] Ölçüm yapılamadı: BB rig'inin kumandadan sürülen " +
                        "el iskeleti bulunamadı ya da sürülmüyor (OVRHandVisualLeft/Right → " +
                        "OculusHand_* → b_*_wrist).\n" +
                        "⚠️ EN OLASI SEBEP: OVRManager > Controller Driven Hand Poses Type = None " +
                        "(VA_CameraRig prefabında Natural olmalı) — kapalıyken kumanda tutulurken " +
                        "el verisi hiç üretilmez. İkinci sebep: o el görseli KAPALI ve kapalı " +
                        "iskeletin kemikleri SÜRÜLMEZ; oyuncunun kendi el görselinin objesi " +
                        "normalde açık tutulur, kapanmışsa adı ControllerModelHider'ın 'Driven " +
                        "Hand Visuals' listesinden düşmüş demektir (liste tam ad eşleştirir). " +
                        "⚠️ Listeye benzer adlı OVRLeftHandVisual/" +
                        "OVRRightHandVisual'ı YAZMA — onlar mesafeli kavrama hayaletidir, bind " +
                        "pozunda dururlar. (Kapalı iskeleti ölçmek bind pozunu ölçmek olurdu; bu " +
                        "yüzden burada sessizce devam edilmiyor.)", this);
                    enabled = false;
                }

                return;
            }

            if (_sampleCount == 0)
            {
                _leftFirst = leftBasis;
                _rightFirst = rightBasis;
                _leftFirstOffset = leftOffset;
                _rightFirstOffset = rightOffset;
            }
            else
            {
                _leftMaxDeviation = Mathf.Max(_leftMaxDeviation, Quaternion.Angle(_leftFirst, leftBasis));
                _rightMaxDeviation = Mathf.Max(_rightMaxDeviation, Quaternion.Angle(_rightFirst, rightBasis));
                _leftMaxOffsetDeviation = Mathf.Max(_leftMaxOffsetDeviation,
                    Vector3.Distance(_leftFirstOffset, leftOffset));
                _rightMaxOffsetDeviation = Mathf.Max(_rightMaxOffsetDeviation,
                    Vector3.Distance(_rightFirstOffset, rightOffset));
            }

            // The basis is a LookRotation: local +Z = finger direction, +Y = palm normal.
            _leftFingerSum += leftBasis * Vector3.forward;
            _leftPalmSum += leftBasis * Vector3.up;
            _rightFingerSum += rightBasis * Vector3.forward;
            _rightPalmSum += rightBasis * Vector3.up;

            _leftOffsetSum += leftOffset;
            _rightOffsetSum += rightOffset;

            _sampleCount++;
            if (_sampleCount >= RequiredSamples)
            {
                Report();
                enabled = false;
            }
        }

        /// <summary>Resolves rig and hand bones once; scene setup may complete late.</summary>
        private bool EnsureReferences()
        {
            if (_rig == null)
            {
                _rig = FindFirstObjectByType<OVRCameraRig>();
                if (_rig == null)
                {
                    return false;
                }
            }

            if (_leftWrist == null)
            {
                ResolveHand("b_l_wrist", "b_l_middle1", "b_l_thumb1",
                    ref _leftWrist, ref _leftMiddle, ref _leftThumb);
            }

            if (_rightWrist == null)
            {
                ResolveHand("b_r_wrist", "b_r_middle1", "b_r_thumb1",
                    ref _rightWrist, ref _rightMiddle, ref _rightThumb);
            }

            return _leftWrist != null && _leftMiddle != null && _leftThumb != null &&
                   _rightWrist != null && _rightMiddle != null && _rightThumb != null;
        }

        /// <summary>Finds the wrist under the rig BY NAME. ⚠️ Only an <b>active</b> one is accepted: the
        /// same bone name exists in several places in the rig (distance-grab preview, hand-only
        /// interactors) and disabled ones are not driven — measuring a disabled copy would measure the
        /// bind pose.</summary>
        private void ResolveHand(
            string wristName, string middleName, string thumbName,
            ref Transform wrist, ref Transform middle, ref Transform thumb)
        {
            Transform[] all = _rig.GetComponentsInChildren<Transform>(false);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name != wristName || !all[i].gameObject.activeInHierarchy)
                {
                    continue;
                }

                Transform candidateMiddle = FindChild(all[i], middleName);
                Transform candidateThumb = FindChild(all[i], thumbName);
                if (candidateMiddle == null || candidateThumb == null)
                {
                    continue;
                }

                wrist = all[i];
                middle = candidateMiddle;
                thumb = candidateThumb;
                return;
            }
        }

        private static Transform FindChild(Transform root, string name)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == name)
                {
                    return children[i];
                }
            }

            return null;
        }

        /// <summary>Returns both anchor-space measurements at once: the anatomical BASIS (cross-checked
        /// against <see cref="HandGripConvention.AnchorBasis"/>) and the wrist POSITION delta (wanted by
        /// <see cref="HandGripPivot.PalmOffset"/>).</summary>
        /// <remarks>Read in the same frame from the same source; sampled separately they would capture
        /// different hand poses and the two constants would be inconsistent.
        /// <para>The anatomy measurement is delegated to
        /// <see cref="HandGripConvention.TryMeasureBoneBasis"/>, the single implementation of the
        /// left/right cross-product rule.</para></remarks>
        private bool TryReadAnchorSample(bool rightHand, out Quaternion anchorBasis, out Vector3 anchorOffset)
        {
            anchorBasis = Quaternion.identity;
            anchorOffset = Vector3.zero;

            Transform wrist = rightHand ? _rightWrist : _leftWrist;
            Transform middle = rightHand ? _rightMiddle : _leftMiddle;
            Transform thumb = rightHand ? _rightThumb : _leftThumb;
            Transform anchor = rightHand ? _rig.rightHandAnchor : _rig.leftHandAnchor;

            if (anchor == null || !HandGripConvention.TryMeasureBoneBasis(
                    wrist, middle, thumb, rightHand, out Quaternion wristLocalBasis))
            {
                return false;
            }

            anchorBasis = Quaternion.Inverse(anchor.rotation) * wrist.rotation * wristLocalBasis;

            // ⚠️ NOT InverseTransformPoint: the offset is in METRES and must not be shrunk even if the
            // rig's scale is not 1 (HandGripPivot applies it under the same rule).
            anchorOffset = Quaternion.Inverse(anchor.rotation) * (wrist.position - anchor.position);
            return true;
        }

        private void Report()
        {
            float maxDeviation = Mathf.Max(_leftMaxDeviation, _rightMaxDeviation);
            string stability = maxDeviation > UnstableAngleDegrees
                ? $"⚠️ Kareler arası açı sapması {maxDeviation:F1}° — ölçüm oturmamış, DEĞERLER GÜVENİLMEZ. " +
                  "Elleri sabit tutup tekrar ölç."
                : $"Kareler arası açı sapması {maxDeviation:F1}° (kararlı).";

            float maxOffsetDeviation = Mathf.Max(_leftMaxOffsetDeviation, _rightMaxOffsetDeviation);
            string offsetStability = maxOffsetDeviation > UnstablePositionMeters
                ? $"⚠️ Kareler arası konum sapması {maxOffsetDeviation * 100f:F1} cm — avuç ofseti " +
                  "oturmamış, DEĞERLER GÜVENİLMEZ."
                : $"Kareler arası konum sapması {maxOffsetDeviation * 100f:F1} cm (kararlı).";

            Debug.Log(
                $"[HandGripCalibrationProbe] Sol: fingerDir={Format(_leftFingerSum)} palmNormal={Format(_leftPalmSum)}\n" +
                $"[HandGripCalibrationProbe] Sağ: fingerDir={Format(_rightFingerSum)} palmNormal={Format(_rightPalmSum)}\n" +
                $"{stability} {_sampleCount} kare ortalandı. " +
                "Bu iki satır KARŞILAŞTIRMA içindir, koda yazılmaz: anchor anatomisini " +
                "HandGripConvention.AnchorBasis zaten çalışma anında ölçüyor.\n" +
                $"[HandGripCalibrationProbe] public static readonly Vector3 LeftPalmOffset = {FormatPoint(_leftOffsetSum)};\n" +
                $"[HandGripCalibrationProbe] public static readonly Vector3 RightPalmOffset = {FormatPoint(_rightOffsetSum)};\n" +
                $"{offsetStability} Bu iki satırı HandGripPivot'taki tahmini ofsetlerin yerine yaz.", this);
        }

        /// <summary>Normalises the direction sum and formats it ready to paste into code.</summary>
        private static string Format(Vector3 sum)
        {
            Vector3 direction = sum.sqrMagnitude > 1e-8f ? sum.normalized : Vector3.zero;
            return FormatVector(direction);
        }

        /// <summary>Formats the AVERAGE of the position sum. ⚠️ NOT normalised like
        /// <see cref="Format"/>: this is a measure in metres, not a direction — normalising would
        /// inflate a 3 cm palm offset to 1 m.</summary>
        private string FormatPoint(Vector3 sum)
        {
            return FormatVector(_sampleCount > 0 ? sum / _sampleCount : Vector3.zero);
        }

        /// <summary>Formats a vector ready to paste into code.</summary>
        /// <remarks>⚠️ Uses <b>InvariantCulture</b>: in a Turkish locale the decimal separator becomes a
        /// comma and the printed line would turn into a C# expression that does not compile — while the
        /// sole purpose of this log is to be copy-pasted into code.</remarks>
        private static string FormatVector(Vector3 value)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "new Vector3({0:F3}f, {1:F3}f, {2:F3}f)",
                value.x, value.y, value.z);
        }
    }
}
