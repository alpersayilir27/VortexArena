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
    /// Delta yalnız <b>ELİN GÖRSELİ</b> için gerekir: ön kabzayı saran sentetik elin bileği kaydın
    /// (anchor) deltası kadar ötesine kilitlenir (<c>HandGripPoser</c>), stüdyodaki hayalet el kökten
    /// aynı deltayla ötelenerek çizilir (<c>GripPoseStudio</c>). Yani delta yanlışsa/ölçülmemişse
    /// bozulan şey silahın yönü DEĞİL, elin silaha göre birkaç santim/derece kaymış görünmesidir.
    /// </para>
    /// <para>
    /// <b>Delta üç basamakta çözülür</b> (<see cref="ResolveAnchorToWrist"/>): canlı ölçüm
    /// (<see cref="HandGripPoser.TryGetAnchorToWrist"/>) → ölçülmüş sabit
    /// (<see cref="HandGripConvention.AnchorToWrist"/>) → kimlik. Delta kumanda sürümlü el
    /// pozlarından türediği için deterministiktir: aynı kumanda, aynı SDK, aynı sonuç — yani her
    /// başlıkta AYNI değer ölçülür.
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
        /// Kumanda anchor'ından ISDK bileğine delta (anchor uzayında, metre): canlı ölçüm varsa o,
        /// yoksa ölçülmüş sabit, o da yoksa kimlik.
        /// <para>
        /// ⚠️ Kimliğe düşmek silahı bozmaz (kayıt anchor uzayında): yalnız ön kabzadaki sentetik el
        /// ya da stüdyodaki hayalet el, kumandanın tam üstünde ve onunla aynı eksende çizilir — açık
        /// bir bozulma değil, kabul edilmiş bir yaklaşıklık.
        /// </para>
        /// </summary>
        public static Pose ResolveAnchorToWrist(bool rightHand)
        {
            return HandGripPoser.TryGetAnchorToWrist(rightHand, out Pose live)
                ? live
                : HandGripConvention.AnchorToWrist(rightHand);
        }

        /// <summary>
        /// Bir DÜNYA anchor pozundan aynı elin BİLEK pozunu üretir (<c>wrist = anchor ∘ delta</c>).
        /// <para>Ön kabza kilidi (<c>HandGripPoser</c>) buradan geçer: kayıt anchor'ı söyler, sentetik
        /// ele ise bilek verilir. Elle bileşim (<c>TransformPoint</c> DEĞİL): delta metredir.</para>
        /// </summary>
        public static Pose WristFromAnchor(bool rightHand, in Pose anchorWorld)
        {
            Pose delta = ResolveAnchorToWrist(rightHand);
            return new Pose(
                anchorWorld.position + anchorWorld.rotation * delta.position,
                anchorWorld.rotation * delta.rotation);
        }

    }
}
