using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Geliştirici aracı: <see cref="HandGripConvention"/>'daki <b>tahmini</b> anchor anatomisinin
    /// KESİN değerini cihazda ölçer ve yapıştırılabilir biçimde loglar.
    /// <para>
    /// <b>Nasıl ölçüyor:</b> kumanda elde tutulurken Meta'nın runtime'ı hem kumandanın
    /// (<c>LTouch/RTouch</c>) hem de o kumandayı tutan ELİN (<c>LHand/RHand</c>) pozunu veriyor —
    /// <c>OVRCameraRig</c> "controller in hand" anchor'ını böyle kuruyor. İkisinin farkı
    /// anchor→OVR bileği dönüşünün TAMAMIDIR; OVR bileğinin bilinen anatomik yönleri bu dönüşle
    /// çarpılınca anchor uzayındaki parmak yönü ve avuç normali çıkar.
    /// </para>
    /// <para>
    /// <b>Kullanımı:</b> bileşen <c>VA_CameraRig</c> prefabına eklenir, iki kumanda da elde
    /// tutulurken bir kez çalıştırılır, konsola düşen iki satır <see cref="HandGripConvention"/>
    /// içindeki tahmini sabitlerin yerine yapıştırılır — sonra bileşen prefabdan silinebilir.
    /// </para>
    /// <para>⚠️ Ölçüm tek bir log basıp kendini kapatır: 20 Hz'lik bir log seli konsolu kullanılmaz
    /// hâle getirirdi ve ortalama zaten birkaç kareyle oturuyor.</para>
    /// </summary>
    public class HandGripCalibrationProbe : MonoBehaviour
    {
        /// <summary>Ortalamaya girecek geçerli kare sayısı — el titremesi sönsün diye birden çok.</summary>
        private const int RequiredSamples = 30;

        /// <summary>Bu süre boyunca hiç geçerli kare gelmezse ölçüm yapılamıyor demektir (sn).</summary>
        private const float NoDataTimeoutSeconds = 10f;

        /// <summary>Kareler arası sapma bunu aşarsa ölçüm oturmamıştır (derece).</summary>
        private const float UnstableAngleDegrees = 5f;

        private int _sampleCount;
        private float _elapsed;

        // Ortalama için toplam; sonda normalize edilir (quaternion ortalaması yerine yön ortalaması:
        // aradığımız şey zaten iki yön vektörü).
        private Vector3 _leftFingerSum;
        private Vector3 _leftPalmSum;
        private Vector3 _rightFingerSum;
        private Vector3 _rightPalmSum;

        // Kararlılık denetimi: ilk örnekle sonrakiler arasındaki en büyük açı farkı.
        private Quaternion _leftFirstAnchorToWrist = Quaternion.identity;
        private Quaternion _rightFirstAnchorToWrist = Quaternion.identity;
        private float _leftMaxDeviation;
        private float _rightMaxDeviation;

        private void Update()
        {
            _elapsed += Time.deltaTime;

            if (!TryReadAnchorToWrist(false, out Quaternion leftAnchorToWrist) ||
                !TryReadAnchorToWrist(true, out Quaternion rightAnchorToWrist))
            {
                // Kumanda elde algılanmıyorsa LHand/RHand pozu güvenilmez — o kare tümden atlanır.
                if (_sampleCount == 0 && _elapsed >= NoDataTimeoutSeconds)
                {
                    Debug.LogWarning(
                        "[HandGripCalibrationProbe] Kumanda elde algılanmadı (Controller-in-hand " +
                        "durumu yok), ölçüm yapılamadı. İki kumandayı da normal şekilde tutarak " +
                        "tekrar dene; el izleme kapalıysa bu veri hiç gelmez.", this);
                    enabled = false;
                }

                return;
            }

            if (_sampleCount == 0)
            {
                _leftFirstAnchorToWrist = leftAnchorToWrist;
                _rightFirstAnchorToWrist = rightAnchorToWrist;
            }
            else
            {
                _leftMaxDeviation = Mathf.Max(
                    _leftMaxDeviation, Quaternion.Angle(_leftFirstAnchorToWrist, leftAnchorToWrist));
                _rightMaxDeviation = Mathf.Max(
                    _rightMaxDeviation, Quaternion.Angle(_rightFirstAnchorToWrist, rightAnchorToWrist));
            }

            _leftFingerSum += leftAnchorToWrist * HandGripConvention.OvrWristFingerDirection(false);
            _leftPalmSum += leftAnchorToWrist * HandGripConvention.OvrWristPalmNormal(false);
            _rightFingerSum += rightAnchorToWrist * HandGripConvention.OvrWristFingerDirection(true);
            _rightPalmSum += rightAnchorToWrist * HandGripConvention.OvrWristPalmNormal(true);

            _sampleCount++;
            if (_sampleCount >= RequiredSamples)
            {
                Report();
                enabled = false;
            }
        }

        /// <summary>
        /// Kumanda anchor'ından OVR bileğine dönüş. Kumanda elde DEĞİLSE <c>false</c>:
        /// <c>LHand/RHand</c> pozu o durumda gerçek bir bileği temsil etmiyor.
        /// </summary>
        private static bool TryReadAnchorToWrist(bool rightHand, out Quaternion anchorToWrist)
        {
            anchorToWrist = Quaternion.identity;

            OVRInput.Hand hand = rightHand ? OVRInput.Hand.HandRight : OVRInput.Hand.HandLeft;
            if (OVRInput.GetControllerIsInHandState(hand) != OVRInput.ControllerInHandState.ControllerInHand)
            {
                return false;
            }

            OVRInput.Controller controller = rightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            OVRInput.Controller wrist = rightHand ? OVRInput.Controller.RHand : OVRInput.Controller.LHand;

            anchorToWrist = Quaternion.Inverse(OVRInput.GetLocalControllerRotation(controller))
                            * OVRInput.GetLocalControllerRotation(wrist);
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
