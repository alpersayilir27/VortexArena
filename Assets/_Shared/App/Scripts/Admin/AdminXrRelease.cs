using UnityEngine;
using UnityEngine.XR.Management;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Admin rolünde XR'ı bırakır. Standalone'da XR açılışta otomatik başlar (editörde Quest
    /// Link ile player oynayabilmek için gerekli) ve başlayan OpenXR oturumu boştaki HMD'yi
    /// kapar — admin'in gözlemci kamerası gözlüğe çizilir. Rol admin çözüldüğü anda loader
    /// durdurulup deinitialize edilir: oturum serbest kalır, aynı PC'de koşan player süreci
    /// (editör ya da APK öncesi Link testi) HMD'yi alabilir.
    ///
    /// <para><b>Çağıranlar:</b> <c>AppBoot.Start</c> (build + Boot'tan Play) ve
    /// <c>DevSession.ApplySelection</c> (editörde açık sahneden Play). İki yol da rol
    /// çözüldükten hemen sonra çağırır; loader yoksa (init hiç olmamış ya da zaten bırakılmış)
    /// sessiz no-op olduğu için çift çağrı zararsızdır.</para>
    /// </summary>
    public static class AdminXrRelease
    {
        public static void Apply()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                return; // Android her zaman player — XR'a dokunulmaz.
            }

            if (!AppSession.RoleResolved || AppSession.Role != AppSession.RoleAdmin)
            {
                return;
            }

            XRManagerSettings manager = XRGeneralSettings.Instance != null
                ? XRGeneralSettings.Instance.Manager
                : null;

            if (manager == null || manager.activeLoader == null)
            {
                return;
            }

            manager.StopSubsystems();
            manager.DeinitializeLoader();
            Debug.Log("[AdminXrRelease] Rol admin — XR bırakıldı, HMD player sürecine kaldı.");
        }
    }
}
