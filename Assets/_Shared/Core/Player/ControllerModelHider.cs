using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Rig'in GÖRSEL temsillerini ayıklar: <b>oyuncu gözlükte yalnız rig'in sentetik ellerini
    /// görür</b> — kumanda modeli çizilmez, mesafeli kavramanın hayalet elleri çizilmez, <b>işaret
    /// ışını (ray) çizilmez</b>, gövde/kol hiç yoktur.
    /// <para>
    /// ⚠️ <b>Işında kapatılan şey INTERACTOR değil onun <c>Visuals</c> düğümüdür.</b>
    /// (<c>ControllerRayInteractor</c> ve <c>HandRayInteractor</c> altında.) Interactor'ın kendisi
    /// ayakta kalır: ISDK'nın UI işaretleme yolu (<c>PointableCanvasModule</c>) ona bağlı ve bu
    /// bileşenin işi davranış kapatmak değil GÖRSEL susturmaktır. Arena tarafında ışının çizecek bir
    /// hedefi yok — silah çerçevesi kendi göstergesini çiziyor, kavrama yakın/mesafeli grab ile
    /// oluyor — yani çizilen ışın oyuncunun ekranında yalnız gürültüdür.
    /// </para>
    /// <para>
    /// ⚠️ <b>Oyuncunun gördüğü el buradan GELMEZ ve buraya DOKUNULMAZ:</b> o el ISDK'nın sentetik
    /// elidir (<c>OVRHandVisualLeft</c>/<c>OVRHandVisualRight</c> → <c>SyntheticHandData</c>) ve
    /// kendi <c>HandVisual</c>'ı tarafından sürülür. Bu bileşenin tek işi onun ÜSTÜNE binen ikinci
    /// görselleri susturmaktır — hepsi birden çizilirse oyuncu iç içe geçmiş eller ve elinde
    /// duran bir kumanda modeli görür.
    /// </para>
    /// <para>
    /// ⚠️ <b>Sentetik elin görünmesi bu bileşene DEĞİL, tek bir rig ayarına bağlıdır:</b>
    /// <c>OVRManager.controllerDrivenHandPosesType</c> (<c>VA_CameraRig</c> prefabında
    /// <c>Natural</c>). <c>None</c> olursa kumanda tutulurken el verisi hiç üretilmez,
    /// <c>HandVisual</c> mesh'ini kendi kapatır ve oyuncu HİÇBİR el görmez — bu bileşende hiçbir
    /// şey değişmemiş olsa bile.
    /// </para>
    /// <para>
    /// ⚠️ <b>ADLAR NEREDEYSE AYNI — yalnız kelime SIRASI farklı.</b> Rig'de iki ayrı el ailesi var:
    /// <list type="bullet">
    /// <item><c>OVRHandVisualLeft</c> / <c>OVRHandVisualRight</c> — <b>oyuncunun gördüğü el</b>,
    /// etkileşim rig'inin doğrudan çocuğu. Hiç dokunulmaz.</item>
    /// <item><c>OVRLeftHandVisual</c> / <c>OVRRightHandVisual</c> — mesafeli kavrama hayaleti,
    /// <c>…/DistanceHandGrabInteractor/Visuals/…Reticle/…Synthetic/</c> altında. Objesi
    /// kapatılır.</item>
    /// </list>
    /// İkisinin de bileşen tipi aynıdır (<c>HandVisual</c>), yani tip onları ayıramaz; ayıran tek
    /// şey addır. Bu yüzden eşleşme <b>tam ad</b> iledir (içerir DEĞİL): "contains" iki aileyi de
    /// yakalar, gerçek eller de kapatılır ve <b>oyuncu ellerini tümden kaybeder</b>. Ad listesi
    /// hiçbir şeyle eşleşmezse <see cref="ErrorNoDrivenHandVisual"/> bunu açıkça bildirir.
    /// </para>
    /// <para>
    /// <b>Neden isim deseni DEĞİL bileşen tipi (kapatılanlar için):</b> <c>questController_animrig</c>
    /// gibi bir ad deseni 24 objeyle eşleşiyor ama <b>hiçbiri aktif değil</b> — o Quest 1 / Rift S
    /// varyantı. Quest 3'te aktif olan varyant <c>MetaQuestTouchPlus_Left/Right</c> ve desene HİÇ
    /// uymuyor. GameObject adı donanım varyantına göre değişir; <b>bileşen tipi değişmez</b>. Ad
    /// yalnız "hangi el OYUNCUNUN" sorusunda kullanılır ve o soruyu tip cevaplayamaz (altı objenin
    /// de bileşeni aynı).
    /// </para>
    /// <para>
    /// ⚠️ <b>DOKUNULMAYANLAR</b> — kavrama/etkileşim bunlara bağlıdır, kapatılırsa oyun kırılır:
    /// <c>SyntheticHand</c>, <c>OVRHand</c>, interactor'lar, <c>HandSphereMap</c>. Bu yüzden tarama
    /// iki tiple SINIRLI tutulur, "eli andıran her şey" süpürülmez.
    /// </para>
    /// <para>
    /// Her karede yeniden gizlenir: bu görseller kumanda bırakılıp tutulduğunda Meta tarafından
    /// yeniden AKTİFLEŞTİRİLİYOR — tek seferlik gizleme kalıcı olmuyor.
    /// </para>
    /// </summary>
    public class ControllerModelHider : MonoBehaviour
    {
        /// <summary>
        /// Taranacak ikinci tip: ISDK'nın el görseli.
        /// <para>⚠️ Tip <b>doğrudan yazılamaz</b> (<c>Oculus.Interaction.Input.HandVisual</c>):
        /// yazmak Core asmdef'ine bir <c>Oculus.Interaction</c> referansı eklemeyi gerektirirdi.
        /// Bunun yerine <see cref="MonoBehaviour"/> taranıp tip ADI karşılaştırılır — bu ad
        /// GameObject adının aksine donanım varyantına göre değişmez.</para>
        /// </summary>
        private const string HandVisualTypeName = "HandVisual";

        /// <summary>
        /// Taranacak üçüncü tip: ISDK'nın işaret ışını interactor'ı (hem kumanda hem el dalında
        /// <b>aynı</b> tiptir — <c>ControllerRayInteractor</c> ve <c>HandRayInteractor</c> objeleri
        /// ayrı ama bileşenleri aynı, yani tek tip ikisini de yakalar).
        /// <para>⚠️ Tip yine ADIYLA aranır: doğrudan yazmak Core asmdef'ine
        /// <c>Oculus.Interaction</c> referansı eklemek olurdu.</para>
        /// </summary>
        private const string RayInteractorTypeName = "RayInteractor";

        /// <summary>
        /// Işın interactor'ının altındaki görsel kapsayıcı — kapatılan tek şey budur.
        /// <para>⚠️ Interactor'ın KENDİSİ kapatılmaz (sınıf açıklaması): görsel gürültüyü almak için
        /// davranışı söküp atmak, ileride bir dünya arayüzüne işaret etmek gerektiğinde sebebi
        /// bulunamayacak bir kayıp olurdu.</para>
        /// </summary>
        private const string RayVisualsNodeName = "Visuals";

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

        [Tooltip("OYUNCUNUN GÖRDÜĞÜ eller: bu adlardaki el görsellerine HİÇ dokunulmaz (tam ad " +
                 "eşleşmesi). Benzer adlı hayalet eller için sınıf açıklamasına bak.")]
        [SerializeField] private string[] drivenHandVisuals =
        {
            "OVRHandVisualLeft",
            "OVRHandVisualRight",
        };

        private Transform rigRoot;
        private readonly List<MonoBehaviour> scanBuffer = new List<MonoBehaviour>(256);

        /// <summary>Objesi tümden kapatılacak görsel kökler (kumanda modelleri + hayalet eller).</summary>
        private readonly List<GameObject> targets = new List<GameObject>(16);

        /// <summary>Zaten loglanmışlar: gizleme her kare TEKRARLANIR ama log bir kez basılır.</summary>
        private readonly HashSet<GameObject> logged = new HashSet<GameObject>();

        private float rescanTimer = float.NegativeInfinity;

        /// <summary>"Oyuncunun eli bulunamadı" hatası oturum başına bir kez.</summary>
        private static bool erroredNoDrivenHandVisual;

        /// <summary>"Işının görsel düğümü bulunamadı" uyarısı oturum başına bir kez.</summary>
        private static bool warnedNoRayVisuals;

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
        }

        /// <summary>Rig altındaki gizlenecek görselleri yeniden bulur (tek geçişte iki tip birden).
        /// <para>Oyuncunun gördüğü el görselleri listeye HİÇ alınmaz — ne objesine ne
        /// Renderer'ına dokunulur.</para></summary>
        private void Rescan()
        {
            rigRoot.GetComponentsInChildren(true, scanBuffer);

            int handVisualsSeen = 0;
            int handVisualsDriven = 0;
            int raysSeen = 0;
            int rayVisualsFound = 0;

            for (int i = 0; i < scanBuffer.Count; i++)
            {
                MonoBehaviour mb = scanBuffer[i];
                if (mb == null)
                {
                    continue;
                }

                string typeName = mb.GetType().Name;
                bool isHandVisual = typeName == HandVisualTypeName;
                bool isRayInteractor = typeName == RayInteractorTypeName;
                if (!isHandVisual && !isRayInteractor && !(mb is OVRControllerHelper))
                {
                    continue;
                }

                GameObject target = mb.gameObject;

                if (isRayInteractor)
                {
                    // Kapatılan interactor DEĞİL, altındaki görsel kapsayıcı.
                    raysSeen++;
                    Transform visuals = mb.transform.Find(RayVisualsNodeName);
                    if (visuals == null)
                    {
                        continue;
                    }

                    rayVisualsFound++;
                    target = visuals.gameObject;
                }
                else if (isHandVisual)
                {
                    handVisualsSeen++;
                    if (IsPlayerHand(target.name))
                    {
                        handVisualsDriven++;
                        continue; // oyuncunun gördüğü el: hiç dokunulmaz
                    }
                }

                if (!targets.Contains(target))
                {
                    targets.Add(target);
                }
            }

            if (handVisualsSeen > 0 && handVisualsDriven == 0)
            {
                ErrorNoDrivenHandVisual();
            }

            if (raysSeen > 0 && rayVisualsFound == 0)
            {
                WarnNoRayVisuals();
            }
        }

        /// <summary>Bu el görseli oyuncunun gördüğü el mi — <b>tam ad</b> eşleşmesi (gerekçe sınıf
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
        /// Rig'de el görseli var ama hiçbiri listeyle eşleşmedi → <b>hepsi kapatıldı, oyuncu
        /// ellerini tümden kaybetti.</b>
        /// <para>⚠️ Uyarı değil HATA: oyuncunun ekranında çizilen tek el bunlar, yani belirtisi
        /// "izleme bozuk / eller kayboldu" diye okunur — oysa tek eksik, Meta SDK'sının
        /// değiştirdiği bir GameObject adıdır.</para>
        /// </summary>
        private void ErrorNoDrivenHandVisual()
        {
            if (erroredNoDrivenHandVisual)
            {
                return;
            }

            erroredNoDrivenHandVisual = true;
            Debug.LogError(
                "[ControllerModelHider] Rig'de el görseli bulundu ama hiçbiri 'Driven Hand " +
                "Visuals' ile eşleşmedi — hepsi hayalet sayılıp kapatıldı, yani OYUNCU ELLERİNİ " +
                "GÖRMEYECEK. Meta SDK'sı objeleri yeniden adlandırmış olabilir: rig altındaki " +
                "gerçek adlara bakıp listeyi güncelle (beklenen: OVRHandVisualLeft / " +
                "OVRHandVisualRight). ⚠️ Listeye benzer adlı OVRLeftHandVisual/OVRRightHandVisual " +
                "yazma — onlar mesafeli kavrama hayaletidir ve gizli kalmalıdır.", this);
        }

        /// <summary>
        /// Rig'de ışın interactor'ı var ama hiçbirinin altında görsel kapsayıcı bulunamadı →
        /// <b>ışın çizilmeye devam eder.</b>
        /// <para>⚠️ Hata değil UYARI: oyun çalışır, yalnız oyuncu elinden çıkan bir çizgi görür.
        /// Sebebi tek bir şey olabilir — ISDK düğümü yeniden adlandırmıştır; bakılacak yer
        /// <see cref="RayVisualsNodeName"/>.</para>
        /// </summary>
        private void WarnNoRayVisuals()
        {
            if (warnedNoRayVisuals)
            {
                return;
            }

            warnedNoRayVisuals = true;
            Debug.LogWarning(
                $"[ControllerModelHider] Rig'de ışın interactor'ı bulundu ama hiçbirinin altında " +
                $"'{RayVisualsNodeName}' düğümü yok — işaret ışını GİZLENEMEDİ ve oyuncunun " +
                "elinden çıkan çizgi görünmeye devam edecek. ISDK düğümü yeniden adlandırmış " +
                "olabilir: interactor'ın altındaki gerçek görsel düğüm adına bakıp sabiti güncelle.",
                this);
        }
    }
}
