using Oculus.Interaction;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Hiçbir şey yapmayan ISDK <see cref="ITransformer"/>'ı: kavranan nesneyi <b>yerinde
    /// dondurur</b>. <see cref="WeaponFrame"/>'in durduğu kaynak silahta kullanılır.
    /// <para>
    /// ⚠️ <b>"Transformer koymamak" hareketsizlik DEĞİL, SERBEST hareket demektir:</b>
    /// <c>Grabbable.Start</c> tek ve çift el transformer'larının İKİSİ de boşsa kendisi bir
    /// <c>GrabFreeTransformer</c> üretip bağlar ("Create missing defaults"). Yani alanları boş
    /// bırakmak, çerçevedeki silahı en serbest hâline sokardı.
    /// </para>
    /// <para>
    /// <b>Neden "her karede pozu geri yaz" değil:</b> o yaklaşım ISDK'nın transformer'ıyla kare
    /// kare yarışır — hangisinin sonra yazdığı Unity'nin çağrı sırasına kalır ve silah titrer.
    /// Kaynağı hiç kıpırdatmamanın tek kesin yolu, kıpırdatacak kodu devreden çıkarmaktır.
    /// </para>
    /// </summary>
    public class FrozenGrabTransformer : MonoBehaviour, ITransformer
    {
        public void Initialize(IGrabbable grabbable)
        {
        }

        public void BeginTransform()
        {
        }

        public void UpdateTransform()
        {
        }

        public void EndTransform()
        {
        }
    }
}
