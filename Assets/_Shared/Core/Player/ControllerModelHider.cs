using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Rig'in GÖRSEL temsillerini ayıklar: <b>oyuncu gözlükte yalnız kendi karakterinin ellerini
    /// görür</b> — kumanda modeli çizilmez, rig'in ISDK el görselleri ("hayalet el") çizilmez,
    /// gövde/kol hiç yoktur.
    /// <para>
    /// ⚠️ <b>Oyuncunun gördüğü el buradan GELMEZ:</b> o el <see cref="LocalBodyAvatar"/>'ın gövde
    /// meshinden kesilmiş el meshidir. Bu bileşenin tek işi onun ÜSTÜNE binen rig görsellerini
    /// susturmaktır — ikisi birden çizilirse oyuncu iç içe geçmiş iki el görür.
    /// </para>
    /// <para>
    /// <b>İki ayrı susturma yolu vardır ve hangisinin seçildiği sonucu değiştirir:</b>
    /// <list type="bullet">
    /// <item><b>Obje kapatılır</b> (<c>SetActive(false)</c>): kumanda modelleri
    /// (<see cref="OVRControllerHelper"/>) ve mesafeli kavramanın hayalet el reticle'ları. Bunlar
    /// saf görseldir, kapatmakla kaybedilen bir şey yoktur.</item>
    /// <item><b>Yalnız Renderer'ı kapatılır</b>: oyuncunun kendi el görselleri
    /// (<see cref="drivenHandVisuals"/>). Obje AÇIK bırakılır çünkü <c>HandVisual</c> kapanırsa el
    /// iskeleti <b>sürülmeyi bırakır</b> ve o iskeletten ölçüm alan
    /// <see cref="HandGripCalibrationProbe"/> sessizce bind pozunu ölçmeye başlar. Çizim
    /// bakımından iki yol da aynı sonucu verir; fark yalnız iskeletin canlı kalmasıdır.</item>
    /// </list>
    /// </para>
    /// <para>
    /// ⚠️ <b>ADLAR NEREDEYSE AYNI — yalnız kelime SIRASI farklı.</b> Rig'de iki ayrı aile var:
    /// <list type="bullet">
    /// <item><c>OVRHandVisualLeft</c> / <c>OVRHandVisualRight</c> — <b>oyuncunun eli</b>, etkileşim
    /// rig'inin doğrudan çocuğu. Renderer'ı kapatılır, kendisi açık kalır.</item>
    /// <item><c>OVRLeftHandVisual</c> / <c>OVRRightHandVisual</c> — mesafeli kavrama hayaleti,
    /// <c>…/DistanceHandGrabInteractor/Visuals/…Reticle/…Synthetic/</c> altında. Objesi kapatılır.</item>
    /// </list>
    /// Karıştırmanın bedeli görünmez: iki el de zaten çizilmez, ama gerçek el KAPANDIĞI için
    /// kalibrasyon probu yanlış ölçer. Bu yüzden eşleşme <b>tam ad</b> iledir (içerir DEĞİL):
    /// "contains" iki aileyi de yakalardı.
    /// </para>
    /// <para>
    /// <b>Neden isim deseni DEĞİL bileşen tipi (kapatılanlar için):</b> önceki sürüm ada bakıyordu
    /// (<c>questController_animrig</c>, …) ve deseni iki kez tutmadı. Ölçüldü ki
    /// <c>questController_animrig</c> 24 objeyle eşleşiyor ama <b>hiçbiri aktif değil</b> — o
    /// Quest 1 / Rift S varyantı. Quest 3'te aktif olan varyant <c>MetaQuestTouchPlus_Left/Right</c>
    /// ve desene HİÇ uymuyordu. GameObject adı donanım varyantına göre değişir; <b>bileşen tipi
    /// değişmez</b>. Ad yalnız "hangi el BİZİM" sorusunda kullanılır ve o soruyu tip cevaplayamaz
    /// (altı objenin de bileşeni aynı) — bu yüzden ad tutmazsa
    /// <see cref="WarnNoDrivenHandVisual"/> uyarır.
    /// </para>
    /// <para>
    /// ⚠️ <b>DOKUNULMAYANLAR</b> — kavrama/etkileşim bunlara bağlıdır, kapatılırsa oyun kırılır:
    /// <c>SyntheticHand</c>, <c>OVRHand</c>, interactor'lar, <c>HandSphereMap</c>. Bu yüzden tarama
    /// iki tiple SINIRLI tutulur, "eli andıran her şey" süpürülmez.
    /// </para>
    /// <para>
    /// Her karede yeniden gizlenir: bu görseller kumanda bırakılıp tutulduğunda Meta tarafından
    /// yeniden AKTİFLEŞTİRİLİYOR — tek seferlik gizleme kalıcı olmuyor. Aynısı el görsellerinin
    /// Renderer'ı için de uygulanır: obje geri açıldığında ISDK'nın renderer'ı da tazelemesi
    /// ihtimaline karşı kapatma her kare tekrarlanır (kapalı bir renderer'ı tekrar kapatmak bedava).
    /// </para>
    /// </summary>
    public class ControllerModelHider : MonoBehaviour
    {
        /// <summary>
        /// Kapatılacak ikinci tip: ISDK'nın el görseli.
        /// <para>⚠️ Tip <b>doğrudan yazılamaz</b> (<c>Oculus.Interaction.Input.HandVisual</c>):
        /// yazmak Core asmdef'ine bir <c>Oculus.Interaction</c> referansı eklemeyi gerektirirdi.
        /// Bunun yerine <see cref="MonoBehaviour"/> taranıp tip ADI karşılaştırılır — bu ad
        /// GameObject adının aksine donanım varyantına göre değişmez.</para>
        /// </summary>
        private const string HandVisualTypeName = "HandVisual";

        /// <summary>
        /// Tüm rig'i yeniden tarama aralığı (sn). ⚠️ <b>Her kare taranmaz:</b> rig yüzlerce bileşen
        /// taşıyor ve tüm alt ağacı her karede gezmek Quest'te ölçülebilir bir maliyettir. Yeni bir
        /// görsel ancak rig yeniden kurulunca ortaya çıkar (insan zaman ölçeğinde nadir); Meta'nın
        /// bırak-tut'ta yaptığı şey ise yeni obje üretmek değil BİLİNEN objeyi geri açmaktır — o da
        /// aşağıda her kare kapatılıyor.
        /// </summary>
        private const float RescanIntervalSeconds = 0.5f;

        [Tooltip("Rig kökünün adı. Bulunamazsa OVRCameraRig tipinden aranır — bu alan yalnız hızlandırıcıdır.")]
        [SerializeField] private string rigRootName = "VA_CameraRig";

        [Tooltip("OYUNCUNUN KENDİ elleri: bu adlardaki el görselleri ÇİZİLMEZ ama objeleri açık " +
                 "kalır, çünkü iskeletlerinden kalibrasyon probu ölçüm alıyor (tam ad eşleşmesi). " +
                 "Benzer adlı hayalet eller için sınıf açıklamasına bak.")]
        [SerializeField] private string[] drivenHandVisuals =
        {
            "OVRHandVisualLeft",
            "OVRHandVisualRight",
        };

        private Transform rigRoot;
        private readonly List<MonoBehaviour> scanBuffer = new List<MonoBehaviour>(256);

        /// <summary>Objesi tümden kapatılacak görsel kökler (kumanda modelleri + hayalet eller).</summary>
        private readonly List<GameObject> targets = new List<GameObject>(16);

        /// <summary>Yalnız çizimi kesilecek el görselleri — objeleri açık kalır (gerekçe sınıf
        /// açıklamasında). Renderer'lar bir kez toplanır, her kare yeniden aranmaz.</summary>
        private readonly List<Renderer> drivenHandRenderers = new List<Renderer>(8);

        /// <summary>Zaten loglanmışlar: gizleme her kare TEKRARLANIR ama log bir kez basılır.</summary>
        private readonly HashSet<GameObject> logged = new HashSet<GameObject>();

        private float rescanTimer = float.NegativeInfinity;

        /// <summary>"Sürülen el görseli bulunamadı" uyarısı oturum başına bir kez.</summary>
        private static bool warnedNoDrivenHandVisual;

        private void LateUpdate()
        {
            if (rigRoot == null)
            {
                GameObject go = string.IsNullOrEmpty(rigRootName) ? null : GameObject.Find(rigRootName);
                if (go == null)
                {
                    // İsim tutmadı: rig prefabı yeniden adlandırılmış ya da sahnede başka bir adla
                    // duruyor olabilir. Kimliği ADI değil BİLEŞENİ belirler — tipten ara.
                    OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
                    go = rig != null ? rig.gameObject : null;
                }

                if (go == null)
                {
                    return; // rig henüz sahnede değil — sonraki karede tekrar denenir
                }

                rigRoot = go.transform;
                targets.Clear();
                drivenHandRenderers.Clear();
                rescanTimer = float.NegativeInfinity; // yeni rig: hemen tara
            }

            if (Time.unscaledTime - rescanTimer >= RescanIntervalSeconds)
            {
                rescanTimer = Time.unscaledTime;
                Rescan();
            }

            // ⚠️ Gizleme her kare TEKRARLANIR: Meta bu görselleri kumanda bırakılıp tutulduğunda
            // geri açıyor. "Bir kez gizlediysem bir daha bakmam" kısayolu tam da bu yüzden yanlıştır —
            // görsel geri gelir ve sessizce görünür kalırdı.
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                GameObject target = targets[i];
                if (target == null)
                {
                    targets.RemoveAt(i);
                    continue;
                }

                if (!target.activeSelf)
                {
                    continue;
                }

                target.SetActive(false);

                if (logged.Add(target))
                {
                    string parentName = target.transform.parent != null ? target.transform.parent.name : "(kök)";
                    Debug.Log($"[ControllerModelHider] Gizlendi: '{target.name}' ({parentName} altında).", this);
                }
            }

            // Oyuncunun kendi el görselleri: obje AÇIK kalır, yalnız çizim kesilir.
            for (int i = drivenHandRenderers.Count - 1; i >= 0; i--)
            {
                Renderer renderer = drivenHandRenderers[i];
                if (renderer == null)
                {
                    drivenHandRenderers.RemoveAt(i);
                    continue;
                }

                if (renderer.enabled)
                {
                    renderer.enabled = false;
                }
            }
        }

        /// <summary>Rig altındaki gizlenecek görselleri yeniden bulur (tek geçişte iki tip birden).
        /// <para>Oyuncunun kendi elleri ayrı listeye alınır: objeleri kapatılmaz, yalnız
        /// Renderer'ları toplanır.</para></summary>
        private void Rescan()
        {
            rigRoot.GetComponentsInChildren(true, scanBuffer);

            int handVisualsSeen = 0;
            int handVisualsDriven = 0;

            for (int i = 0; i < scanBuffer.Count; i++)
            {
                MonoBehaviour mb = scanBuffer[i];
                if (mb == null)
                {
                    continue;
                }

                bool isHandVisual = mb.GetType().Name == HandVisualTypeName;
                if (!isHandVisual && !(mb is OVRControllerHelper))
                {
                    continue;
                }

                GameObject target = mb.gameObject;

                if (isHandVisual)
                {
                    handVisualsSeen++;
                    if (IsPlayerHand(target.name))
                    {
                        handVisualsDriven++;
                        CollectDrivenRenderers(target);
                        continue; // oyuncunun kendi eli: objesi açık kalır, yalnız çizimi kesilir
                    }
                }

                if (!targets.Contains(target))
                {
                    targets.Add(target);
                }
            }

            if (handVisualsSeen > 0 && handVisualsDriven == 0)
            {
                WarnNoDrivenHandVisual();
            }
        }

        /// <summary>Bir el görselinin altındaki tüm Renderer'ları çizim-kesme listesine alır.
        /// <para>Alt ağaç taranır çünkü <c>HandVisual</c> bileşeni ile mesh'i taşıyan obje aynı
        /// olmak zorunda değil.</para></summary>
        private void CollectDrivenRenderers(GameObject handVisual)
        {
            Renderer[] renderers = handVisual.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && !drivenHandRenderers.Contains(renderers[i]))
                {
                    drivenHandRenderers.Add(renderers[i]);
                }
            }
        }

        /// <summary>Bu el görseli oyuncunun KENDİ eli mi — <b>tam ad</b> eşleşmesi (gerekçe sınıf
        /// açıklamasında: hayalet ellerin adı yalnız kelime sırasıyla ayrılıyor).</summary>
        private bool IsPlayerHand(string objectName)
        {
            if (drivenHandVisuals == null)
            {
                return false;
            }

            for (int i = 0; i < drivenHandVisuals.Length; i++)
            {
                if (string.Equals(drivenHandVisuals[i], objectName, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Rig'de el görseli var ama hiçbiri listeyle eşleşmedi → hepsi tümden kapatıldı.
        /// <para>⚠️ <b>Oyunun görüntüsü bundan etkilenmez</b> (oyuncunun elleri zaten
        /// <see cref="LocalBodyAvatar"/>'dan geliyor); kaybedilen şey el iskeletinin sürülmesidir ve
        /// tek kurbanı <see cref="HandGripCalibrationProbe"/>'dur. Bu yüzden hata değil UYARI:
        /// sessiz kalmak probu sessizce bind pozu ölçer hâle getirirdi.</para>
        /// </summary>
        private void WarnNoDrivenHandVisual()
        {
            if (warnedNoDrivenHandVisual)
            {
                return;
            }

            warnedNoDrivenHandVisual = true;
            Debug.LogWarning(
                "[ControllerModelHider] Rig'de el görseli bulundu ama hiçbiri 'Driven Hand " +
                "Visuals' ile eşleşmedi — hepsi tümden kapatıldı. Oyunun görüntüsü değişmez, ama " +
                "el iskeleti artık sürülmediği için HandGripCalibrationProbe bind pozunu ölçer. " +
                "Meta SDK'sı objeleri yeniden adlandırmış olabilir: rig altındaki gerçek adlara " +
                "bakıp listeyi güncelle (beklenen: OVRHandVisualLeft / OVRHandVisualRight).", this);
        }
    }
}
