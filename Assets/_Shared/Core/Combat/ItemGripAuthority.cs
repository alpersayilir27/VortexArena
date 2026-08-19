using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Kumanda ANCHOR'ı ile ISDK BİLEĞİ arasındaki köprü — yalnız <b>görsel elin</b> işi.
    /// <para>
    /// <b>Kavrama kaydı ANCHOR uzayındadır</b> (<see cref="ItemGripPose"/>): eşyanın dünya pozunu
    /// çözen taraf (<see cref="ItemGripSolver"/>) da tel (§6.6) de elin ANCHOR pozunu bilir, kayıt
    /// aynı uzaydadır — silahın duruşu için hiçbir yerde delta ölçülmez ve rig'i olmayan izleyici
    /// (admin gözlemci) silahı oyuncuyla birebir aynı çizer.
    /// </para>
    /// <para>
    /// Delta yalnız <b>ELİN GÖRSELİ</b> için gerekir: sentetik elin bileği her karede buraya
    /// kilitlenir (<c>HandGripPoser</c>) ve stüdyodaki hayalet el kökten aynı deltayla ötelenerek
    /// çizilir (<c>GripPoseStudio</c>) — iki uç <b>aynı</b> sayıyı okuduğu için tezgâhta görülen el
    /// ile oyunda görülen el kurgu gereği aynıdır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Delta ÖLÇÜLMEZ, TANIMLANIR</b> (<see cref="HandPoseLibrary.AnchorToWrist"/>) ve buraya
    /// bir ölçüm basamağı GERİ EKLENMEZ. Bilek kilitlenmeseydi Meta'nın kumandadan sentezlediği
    /// "doğal" el pozundan gelirdi; o poz bizim hiçbir yerde bilmediğimiz bir ofset taşıyor, silah
    /// ise anchor'dan konumlanıyor — iki ayrı referans, tezgâh ile oyunun ayrışması demekti ve
    /// kapatmanın tek yolu o ofseti başlıkta ölçüp koda yapıştırmaktı. Ofsetin sahibi biz olunca
    /// ölçülecek bir şey kalmıyor.
    /// </para>
    /// <para>
    /// <b>Delta SLOT BAŞINA yazılabilir</b> (<see cref="ItemGripPose.Wrist"/>): elin silahın üstünde
    /// nasıl duracağı kabzadan kabzaya değişir (kimi ön kabza yandan, kimi alttan tutulur), oysa
    /// silahın kumandaya göre yeri değişmez. Yazılmamış slot paylaşılan tanıma düşer — yani bu
    /// sınıf hâlâ tek kapıdır, yalnız cevabı artık "kavraması yazılmışsa onun, değilse ortak olan"dır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ölçek tuzağı burada YOKTUR:</b> kayıt zaten ölçeksiz metredir
    /// (<see cref="ItemGripPose"/>), yani hiçbir yerde eşyanın görsel ölçeğiyle (<c>WPN_*</c>
    /// kökleri 0.8) çarpılmaz ve ölçekli/ölçeksiz bileşim ayrımı yapmak gerekmez.
    /// </para>
    /// <para>
    /// ⚠️ <b>Sol ve sağ AYRI çözülür.</b> Delta el başına ölçülür; kabza simetrik olmadığı için
    /// kayıt da el başınadır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Protokolde karşılığı YOKTUR ve tel formatı değişmez</b> (§6.6): duruş telde gitmiyor,
    /// iki uç da aynı tanımdan aynı kaydı okuyor; delta yalnız yerel elin görselini sürer.
    /// </para>
    /// </summary>
    public static class ItemGripAuthority
    {
        /// <summary>
        /// Kumanda anchor'ından ISDK bileğine delta (anchor uzayında, metre) — tek kaynak
        /// <see cref="HandPoseLibrary.AnchorToWrist"/>.
        /// <para>⚠️ Bu ara katman <b>gereksiz görünse de duruyor</b>: deltayı okuyan iki taraf var
        /// (yerel bilek kilidi ve stüdyo) ve ikisinin de aynı kapıdan geçmesi, ileride ofsetin
        /// tanımı değiştiğinde bir tarafın unutulmamasının garantisi.</para>
        /// </summary>
        public static Pose ResolveAnchorToWrist(bool rightHand)
        {
            return HandPoseLibrary.AnchorToWrist(rightHand);
        }

        /// <summary>
        /// Bir kavrama kaydının kendi el yerleşimi — yazılmamışsa paylaşılan tanım
        /// (<see cref="ResolveAnchorToWrist(bool)"/>).
        /// <para>⚠️ <b>Düşme sessizdir ve öyle kalmalı:</b> el yerleşimi kavrama kaydından sonra
        /// eklendi, yani diskteki her eski kayıt bu yolla bugünkü elini aynen koruyor. Burada uyarı
        /// basmak, hiçbir şeyi bozulmamış 13 silahın hepsi için konsola satır atmak olurdu; eksik
        /// yerleşim zaten stüdyoda görülüyor.</para>
        /// </summary>
        public static Pose ResolveAnchorToWrist(in ItemGripPose grip, bool rightHand)
        {
            return grip.HasWrist ? grip.Wrist : ResolveAnchorToWrist(rightHand);
        }

        /// <summary>
        /// Bir DÜNYA anchor pozundan aynı elin BİLEK pozunu üretir (<c>wrist = anchor ∘ delta</c>).
        /// <para>Bilek kilitlerinin ikisi de (<c>HandGripPoser</c>) buradan geçer: kayıt anchor'ı
        /// söyler, sentetik ele ise bilek verilir. Elle bileşim (<c>TransformPoint</c> DEĞİL): delta
        /// metredir.</para>
        /// </summary>
        /// <param name="anchorToWrist">Kullanılacak delta — slotun kendi yerleşimi ya da paylaşılan
        /// tanım (<c>ResolveAnchorToWrist</c>).</param>
        public static Pose WristFromAnchor(in Pose anchorWorld, in Pose anchorToWrist)
        {
            return new Pose(
                anchorWorld.position + anchorWorld.rotation * anchorToWrist.position,
                anchorWorld.rotation * anchorToWrist.rotation);
        }
    }
}
