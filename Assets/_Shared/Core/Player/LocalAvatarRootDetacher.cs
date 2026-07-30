using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Yerel gövde avatarını <see cref="Awake"/>'te sahne köküne AYIRIR ve dünya transformunu
    /// birime oturtur. Tek seferlik bir işlemdir — kare başına maliyeti yoktur.
    /// <para>
    /// <b>Neden gerekli:</b> Movement SDK'nın retarget çıktısı <b>DÜNYA UZAYINDADIR</b>.
    /// <c>SkeletonUtilities.GetPosesFromTheTracker</c> her kemiğe
    /// <c>OVRCameraRig.trackingSpace.localToWorldMatrix</c>'i baskılıyor; ardından
    /// <c>ConvertWorldToLocalPoseJob</c> KÖK eklemi ebeveynine göre yerelleştirmeden bırakıyor
    /// ve <c>ApplyPoseJob</c> onu <c>SetLocalPositionAndRotation</c> ile yazıyor. Bu projede kök
    /// eklem avatarın kendi transformudur; avatar kamera rig'inin ALTINDA dururken rig transformu
    /// bir kez <c>trackingSpace</c> üzerinden, bir kez de ebeveynlik üzerinden olmak üzere
    /// <b>iki kez</b> uygulanıyordu.
    /// </para>
    /// <para>
    /// Belirtisi: rig birimken (arena origin'inde, kalibrasyonsuz) sorun görünmez; kalibrasyon
    /// rig'e bir dönüşüm yazar yazmaz avatar oyuncudan tam <b>bir kalibrasyon ofseti</b> kadar
    /// uzağa oturur — arena etrafında dönmüş ve zemin düzeltmesi kadar yükselmiş, oyuncunun
    /// hareketlerini birebir yapan ayrı bir gövde gibi. Oyuncu kendi kollarını göremez, çünkü
    /// kollar da o kopyanın üstündedir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Avatar bu yüzden hareket eden hiçbir kökün altına konmaz.</b> Rig'in altında
    /// tutulup transformu her kare sıfırlanarak da düzeltilebilirdi, ama o çözüm retargeter'ın
    /// execution order'ına bağımlı olurdu (bizim sıfırlamamız <c>CharacterRetargeter.LateUpdate</c>'ten
    /// önce koşmak zorunda kalırdı) ve kare başına iş eklerdi.
    /// </para>
    /// <para>
    /// Ayırma <see cref="Awake"/>'te yapılır: <c>CharacterRetargeterConfig.Setup</c>
    /// <c>Start</c>'ta koşuyor ve ölçeği <c>transform.lossyScale</c>'den okuyor — ayırmanın
    /// ondan önce bitmesi gerekiyor.
    /// </para>
    /// <para>
    /// ⚠️ Avatar artık rig'in çocuğu OLMADIĞI için <b>rig'i kapatmak avatarı kapatmaz</b>:
    /// admin gözlemcisi (<c>AdminSpectator</c>) onu ayrıca kapatır.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class LocalAvatarRootDetacher : MonoBehaviour
    {
        private void Awake()
        {
            if (transform.parent != null)
            {
                // worldPositionStays: false — yerel değerler korunur, böylece ayırma sonrası
                // dünya transformu doğrudan yerel transform olur (prefabda birimdir).
                transform.SetParent(null, false);
            }

            // Yine de açıkça birime oturtulur: avatarın prefab içindeki yerel transformu
            // ileride kazara kaydırılırsa sessizce ikinci bir ofset üretmesin.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // ⚠️ localScale'e DOKUNULMAZ: onu her kare CharacterRetargeter.ApplyPose yazıyor
            // (oyuncunun boyuna göre kök ölçeği).
        }
    }
}
