using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace VortexArena.App
{
    /// <summary>
    /// Sahnenin <see cref="EventSystem"/>'inde hangi girdi modülünün konuşacağını seçer:
    /// XR aygıtı etkinken ISDK <c>PointableCanvasModule</c> (kumanda ışını + parmakla dokunma),
    /// aygıt yokken <c>InputSystemUIInputModule</c> (fare). İkisi de aynı objede durur, her an
    /// <b>yalnız biri</b> etkindir.
    ///
    /// <para>
    /// <b>Neden tam olarak biri:</b> <see cref="EventSystem"/> her karede modül listesini gezer ve
    /// <c>ShouldActivateModule()</c> diyen <b>İLK</b> modülü seçip yalnız onun <c>Process()</c>'ini
    /// çağırır. İkisi birden etkinse hangisinin kazandığı kayıt (OnEnable) sırasına düşer — yani
    /// sahne yeniden serialize edildiğinde sessizce değişebilen bir şeye. Biri kapalıysa seçim
    /// belirlenimlidir.
    /// </para>
    /// <para>
    /// <b>Ölçüt platform DEĞİL, XR aygıtının etkin olmasıdır.</b> <c>RuntimePlatform.Android</c>
    /// editörde her zaman yanlıştır: Quest Link ile editörde oynarken kumanda ışını UI'a hiç
    /// ulaşmaz, arayüz kımıldamayan bir tabelaya döner. <see cref="XRSettings.isDeviceActive"/>
    /// bunu doğru ayırır (Link, Meta XR Simulator ve cihaz üstü build'de true; HMD'siz masaüstünde
    /// false). Android yine de ayrıca sorulur: cihazda XR başlatılamadıysa bile fare modülüne
    /// düşmenin anlamı yok — orada fare yoktur.
    /// </para>
    /// <para>
    /// <b>Neden her karede yoklanır:</b> Link oturum ortasında bağlanıp kopabilir ve XR yığını ilk
    /// karede hazır olmayabilir. Yoklama iki bool okumasıdır; yazma <b>yalnız karar değişince</b>
    /// yapılır, yani modüller boşuna kapatılıp açılmaz.
    /// </para>
    /// <para>
    /// ⚠️ <b>Yazma sırası kayıtsız değildir: önce kaybeden kapatılır, sonra kazanan açılır.</b>
    /// ISDK'nın <c>PointableCanvasModule</c>'ü "exclusive mode" ile gelir — etkin modül oyken
    /// <c>UpdateModule()</c>'de aynı objedeki diğer modülleri KAPATIR. Ters sırada yazılsaydı
    /// masaüstü modülü açıldığı karede ISDK tarafından geri kapatılır ve fare ölürdü.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-100)] // EventSystem ilk modülünü seçmeden önce karar verilmiş olsun
    public class InputModuleAutoSwitch : MonoBehaviour
    {
        [Tooltip("XR aygıtı etkinken kullanılacak modül (ISDK PointableCanvasModule).")]
        [SerializeField] private BaseInputModule vrModule;

        [Tooltip("XR aygıtı yokken kullanılacak modül (InputSystemUIInputModule — fare).")]
        [SerializeField] private BaseInputModule desktopModule;

        /// <summary>Uygulanmış karar; <c>null</c> = henüz hiç uygulanmadı.</summary>
        private bool? _vrActive;

        private void Awake()
        {
            Apply(ShouldUseVr());
        }

        private void Update()
        {
            Apply(ShouldUseVr());
        }

        private static bool ShouldUseVr()
        {
            return XRSettings.isDeviceActive || Application.platform == RuntimePlatform.Android;
        }

        private void Apply(bool vr)
        {
            if (_vrActive.HasValue && _vrActive.Value == vr)
            {
                return; // karar değişmedi — modüllere dokunma
            }

            _vrActive = vr;

            // Sıra önemli (bkz. sınıf notu): önce kaybeden susturulur.
            BaseInputModule loser = vr ? desktopModule : vrModule;
            BaseInputModule winner = vr ? vrModule : desktopModule;

            if (loser != null)
            {
                loser.enabled = false;
            }

            if (winner != null)
            {
                winner.enabled = true;
            }

            if (winner == null)
            {
                // Sessizce ölmesin: modül bağlanmamışsa arayüz tıklanmaz ve sebebi görünmez.
                Debug.LogError($"[InputModuleAutoSwitch] {(vr ? "vrModule" : "desktopModule")} " +
                               "bağlanmamış — bu sahnede arayüz tıklanamaz.", this);
                return;
            }

            Debug.Log($"[InputModuleAutoSwitch] Girdi modülü: {winner.GetType().Name} " +
                      $"(XR aygıtı {(XRSettings.isDeviceActive ? "etkin" : "yok")}).", this);
        }
    }
}
