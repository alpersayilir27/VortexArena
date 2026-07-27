using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Birinci şahıs gövde avatarında (Movement SDK retarget karakteri) kafayı gizler:
    /// kamera kafanın tam içinde durduğu için mesh gizlenmezse kafatasının içi görünür.
    /// Gövde tek SkinnedMeshRenderer olduğundan renderer kapatılamaz — tek güvenli yol
    /// kafa kemiğini sıfıra yakın ölçeklemektir. Retargeter kemikleri her kare yazdığı
    /// için ölçek, yüksek execution order'lı LateUpdate ile EN SON basılır.
    /// </summary>
    [DefaultExecutionOrder(30000)]
    public class LocalAvatarHeadHider : MonoBehaviour
    {
        [Tooltip("Kafa kemiği; boş bırakılırsa altta adı 'Head' olan transform aranır.")]
        [SerializeField] private Transform headBone;
        [Tooltip("Kafa kemiğine uygulanan ölçek (0'a çok yakın: mesh görünmez, iskelet bozulmaz).")]
        [SerializeField] private float hiddenScale = 0.0001f;

        private void Awake()
        {
            if (headBone == null)
            {
                foreach (Transform t in GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Head")
                    {
                        headBone = t;
                        break;
                    }
                }
            }

            if (headBone == null)
            {
                Debug.LogWarning("[LocalAvatarHeadHider] 'Head' kemiği bulunamadı; kafa gizlenemeyecek.", this);
            }
        }

        private void LateUpdate()
        {
            if (headBone != null)
            {
                headBone.localScale = Vector3.one * hiddenScale;
            }
        }
    }
}
