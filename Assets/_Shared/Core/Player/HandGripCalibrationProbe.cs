using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Geliştirici aracı: <see cref="HandGripConvention"/>'daki <b>tahmini</b> anchor anatomisinin
    /// KESİN değerini cihazda ölçer ve doğrudan koda yapıştırılabilir biçimde loglar.
    /// <para>
    /// <b>Kaynak neden BB rig'inin kendi eli:</b> oyuncu kumandayı tutarken kendi ellerini doğru
    /// yerde görüyor — yani ISDK'nın kumandadan sürdüğü el iskeleti, aradığımız "anchor'a göre el
    /// nerede duruyor" sorusunun CANLI ve doğru cevabıdır. Ölçüm o iskeletin bileğinden alınır
    /// (<c>OVRHandVisualLeft/Right → OculusHand_* → b_*_wrist</c>).
    /// </para>
    /// <para>
    /// ⚠️ <b>Denenip elenen iki yol</b> (tekrar denenmesin): (1) <c>OVRInput.Controller.LHand/RHand</c>
    /// kumanda tutulurken elin pozunu verir ama <b>multimodal</b> (eşzamanlı el+kumanda) ister —
    /// projede kapalı, <c>GetControllerIsInHandState</c> hep "el yok" döner. (2) Mesafeli kavrama
    /// önizlemesindeki kopyalar (<c>OVRLeftHandVisual</c>/<c>OVRRightHandVisual</c>)
    /// <see cref="ControllerModelHider"/> tarafından kapatılır; onların kemikleri sürülmez.
    /// </para>
    /// <para>
    /// ⚠️ <b>O iki ad yalnız kelime SIRASIYLA ayrılır ve karıştırmak sessizce bind pozu ölçtürür:</b>
    /// ölçümün kaynağı <c>OVRHandVisual<i>Left</i></c>, elenen kopya ise <c>OVR<i>Left</i>HandVisual</c>.
    /// Kaynağı açık tutan şey <see cref="ControllerModelHider"/>'ın "Driven Hand Visuals"
    /// listesidir: o listedeki el görseline hiç dokunulmaz (oyuncunun gördüğü el odur), yani
    /// iskeleti sürülmeye devam eder. Bu probe'un çalışması için gizleyiciyi kapatmak GEREKMEZ.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ön koşul: <c>OVRManager.controllerDrivenHandPosesType</c> <c>None</c> OLMAMALI</b>
    /// (<c>VA_CameraRig</c> prefabında <c>Natural</c>). <c>None</c> iken kumanda tutulurken el
    /// verisi hiç üretilmez, iskelet bind pozunda kalır ve prob <b>hatasız ama yanlış</b> bir
    /// sabit basar.
    /// </para>
    /// <para>
    /// <b>Kullanımı:</b> <c>VA_CameraRig</c> prefabında durur, iki kumanda da normal tutulurken bir
    /// kez log basıp kendini kapatır; çıkan iki satır <see cref="HandGripConvention"/> içindeki
    /// tahmini sabitlerin yerine yapıştırılır.
    /// </para>
    /// </summary>
    public class HandGripCalibrationProbe : MonoBehaviour
    {
        /// <summary>Ortalamaya girecek geçerli kare sayısı — el titremesi sönsün diye birden çok.</summary>
        private const int RequiredSamples = 30;

        /// <summary>Bu süre boyunca hiç geçerli kare gelmezse ölçüm yapılamıyor demektir (sn).</summary>
        private const float NoDataTimeoutSeconds = 10f;

        /// <summary>Kareler arası sapma bunu aşarsa ölçüm oturmamıştır (derece).</summary>
        private const float UnstableAngleDegrees = 5f;

        private OVRCameraRig _rig;
        private Transform _leftWrist, _leftMiddle, _leftThumb;
        private Transform _rightWrist, _rightMiddle, _rightThumb;

        private int _sampleCount;
        private float _elapsed;

        // Yön ortalaması: aradığımız şey zaten iki yön vektörü, quaternion ortalamasına gerek yok.
        private Vector3 _leftFingerSum, _leftPalmSum, _rightFingerSum, _rightPalmSum;

        private Quaternion _leftFirst = Quaternion.identity, _rightFirst = Quaternion.identity;
        private float _leftMaxDeviation, _rightMaxDeviation;

        private void Update()
        {
            _elapsed += Time.deltaTime;

            if (!EnsureReferences() ||
                !TryReadAnchorBasis(false, out Quaternion leftBasis) ||
                !TryReadAnchorBasis(true, out Quaternion rightBasis))
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
            }
            else
            {
                _leftMaxDeviation = Mathf.Max(_leftMaxDeviation, Quaternion.Angle(_leftFirst, leftBasis));
                _rightMaxDeviation = Mathf.Max(_rightMaxDeviation, Quaternion.Angle(_rightFirst, rightBasis));
            }

            // Baz bir LookRotation'dır: yerel +Z = parmak yönü, +Y = avuç normali.
            _leftFingerSum += leftBasis * Vector3.forward;
            _leftPalmSum += leftBasis * Vector3.up;
            _rightFingerSum += rightBasis * Vector3.forward;
            _rightPalmSum += rightBasis * Vector3.up;

            _sampleCount++;
            if (_sampleCount >= RequiredSamples)
            {
                Report();
                enabled = false;
            }
        }

        /// <summary>Rig ve el kemikleri bir kez bulunur; sahne kurulumu geç tamamlanabilir.</summary>
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

        /// <summary>
        /// Bileği rig altında ADIYLA arar. ⚠️ Yalnız <b>etkin</b> olan kabul edilir: aynı adlı kemik
        /// rig'de birden çok yerde var (mesafeli kavrama önizlemesi, hand-only etkileşimciler) ve
        /// kapalı olanların kemikleri sürülmez — kapalı bir kopyayı ölçmek bind pozunu ölçmek olurdu.
        /// </summary>
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

        /// <summary>
        /// Elin anatomik bazını ANCHOR uzayında verir — <see cref="HandGripConvention"/>'ın
        /// beklediği şey tam olarak budur.
        /// <para>Anatomi ölçümü <see cref="HandGripConvention.TryMeasureBoneBasis"/>'e devredilir:
        /// sol/sağ çapraz çarpım kuralının projede tek uygulaması orasıdır.</para>
        /// </summary>
        private bool TryReadAnchorBasis(bool rightHand, out Quaternion anchorBasis)
        {
            anchorBasis = Quaternion.identity;

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
            return true;
        }

        private void Report()
        {
            float maxDeviation = Mathf.Max(_leftMaxDeviation, _rightMaxDeviation);
            string stability = maxDeviation > UnstableAngleDegrees
                ? $"⚠️ Kareler arası sapma {maxDeviation:F1}° — ölçüm oturmamış, DEĞERLER GÜVENİLMEZ. " +
                  "Elleri sabit tutup tekrar ölç."
                : $"Kareler arası sapma {maxDeviation:F1}° (kararlı).";

            Debug.Log(
                $"[HandGripCalibrationProbe] Sol: fingerDir={Format(_leftFingerSum)} palmNormal={Format(_leftPalmSum)}\n" +
                $"[HandGripCalibrationProbe] Sağ: fingerDir={Format(_rightFingerSum)} palmNormal={Format(_rightPalmSum)}\n" +
                $"{stability} {_sampleCount} kare ortalandı. " +
                "Bu değerleri HandGripConvention'daki tahmini anchor sabitlerinin yerine yaz.", this);
        }

        /// <summary>Toplamı normalize edip doğrudan koda yapıştırılabilir biçimde yazar.
        /// <para>⚠️ Biçimlendirme <b>InvariantCulture</b> ile yapılır: Türkçe yerelde ondalık
        /// ayırıcı virgül olur ve basılan satır derlenmeyen bir C# ifadesine dönüşürdü — oysa bu
        /// logun tek işi kopyalanıp koda yapıştırılmaktır.</para></summary>
        private static string Format(Vector3 sum)
        {
            Vector3 direction = sum.sqrMagnitude > 1e-8f ? sum.normalized : Vector3.zero;
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "new Vector3({0:F3}f, {1:F3}f, {2:F3}f)",
                direction.x, direction.y, direction.z);
        }
    }
}
