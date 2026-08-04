using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Geliştirici aracı: anchor uzayındaki <b>tahmini</b> iki sabiti cihazda ölçer ve doğrudan
    /// koda yapıştırılabilir biçimde loglar — <see cref="HandGripConvention"/>'ın el ANATOMİSİ
    /// (parmak yönü + avuç normali) ve <see cref="HandGripPivot"/>'un avuç OFSETİ (bileğin anchor'a
    /// göre konumu). İkisi aynı örneklemeden çıkar: ayrı ölçülselerdi biri elin bir duruşunu,
    /// öteki başkasını yakalar ve sabitler birbiriyle tutarsız kalırdı.
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
    /// kez log basıp kendini kapatır; çıkan satırlar <see cref="HandGripConvention"/> ve
    /// <see cref="HandGripPivot"/> içindeki tahmini sabitlerin yerine yapıştırılır.
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

        /// <summary>Aynı kontrolün KONUM karşılığı (m). Ayrı bir eşik gerekiyor çünkü avuç ofseti
        /// birkaç santimlik bir ölçü: derece cinsinden kararlı görünen bir örnekleme, konumda
        /// tümüyle kaymış olabilir.</summary>
        private const float UnstablePositionMeters = 0.01f;

        private OVRCameraRig _rig;
        private Transform _leftWrist, _leftMiddle, _leftThumb;
        private Transform _rightWrist, _rightMiddle, _rightThumb;

        private int _sampleCount;
        private float _elapsed;

        // Yön ortalaması: aradığımız şey zaten iki yön vektörü, quaternion ortalamasına gerek yok.
        private Vector3 _leftFingerSum, _leftPalmSum, _rightFingerSum, _rightPalmSum;

        // Bileğin ANCHOR uzayındaki konum farkı — HandGripPivot.PalmOffset'in kaynağı.
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

            // Baz bir LookRotation'dır: yerel +Z = parmak yönü, +Y = avuç normali.
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
        /// Elin ANCHOR uzayındaki iki ölçüsünü birden verir: anatomik BAZ
        /// (<see cref="HandGripConvention"/>'ın beklediği) ve bileğin KONUM farkı
        /// (<see cref="HandGripPivot.PalmOffset"/>'in beklediği).
        /// <para>İkisi aynı karede ve aynı kaynaktan okunur: ayrı ayrı örneklenselerdi biri elin
        /// bir duruşunu, öteki başka bir duruşunu yakalar ve iki sabit birbiriyle tutarsız çıkardı.</para>
        /// <para>Anatomi ölçümü <see cref="HandGripConvention.TryMeasureBoneBasis"/>'e devredilir:
        /// sol/sağ çapraz çarpım kuralının projede tek uygulaması orasıdır.</para>
        /// </summary>
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

            // ⚠️ InverseTransformPoint DEĞİL: ofset METREdir, rig'in ölçeği 1 olmasa bile
            // küçültülmemeli (HandGripPivot da aynı kuralla uyguluyor).
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
                "Bu değerleri HandGripConvention'daki tahmini anchor sabitlerinin yerine yaz.\n" +
                $"[HandGripCalibrationProbe] public static readonly Vector3 LeftPalmOffset = {FormatPoint(_leftOffsetSum)};\n" +
                $"[HandGripCalibrationProbe] public static readonly Vector3 RightPalmOffset = {FormatPoint(_rightOffsetSum)};\n" +
                $"{offsetStability} Bu iki satırı HandGripPivot'taki tahmini ofsetlerin yerine yaz.", this);
        }

        /// <summary>Yön toplamını normalize edip doğrudan koda yapıştırılabilir biçimde yazar.</summary>
        private static string Format(Vector3 sum)
        {
            Vector3 direction = sum.sqrMagnitude > 1e-8f ? sum.normalized : Vector3.zero;
            return FormatVector(direction);
        }

        /// <summary>Konum toplamının ORTALAMASINI yazar. ⚠️ <see cref="Format"/> gibi normalize
        /// EDİLMEZ: bu bir yön değil metre cinsinden bir ölçüdür, birim uzunluğa çekmek 3 cm'lik
        /// avuç ofsetini 1 m'ye şişirirdi.</summary>
        private string FormatPoint(Vector3 sum)
        {
            return FormatVector(_sampleCount > 0 ? sum / _sampleCount : Vector3.zero);
        }

        /// <summary>
        /// Vektörü doğrudan koda yapıştırılabilir biçimde yazar.
        /// <para>⚠️ Biçimlendirme <b>InvariantCulture</b> ile yapılır: Türkçe yerelde ondalık
        /// ayırıcı virgül olur ve basılan satır derlenmeyen bir C# ifadesine dönüşürdü — oysa bu
        /// logun tek işi kopyalanıp koda yapıştırılmaktır.</para>
        /// </summary>
        private static string FormatVector(Vector3 value)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "new Vector3({0:F3}f, {1:F3}f, {2:F3}f)",
                value.x, value.y, value.z);
        }
    }
}
