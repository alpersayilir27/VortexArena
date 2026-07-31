using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Meta'nın kamera rigine kendiliğinden koyduğu GÖRSEL temsilleri kapatır: fiziksel Touch
    /// kontrolcüsünün 3D modeli ve "controller-driven" sentetik el overlay'i. Proje oyuncuya kendi
    /// gövde avatarını gösterdiği için bunların hiçbiri istenmiyor.
    /// <para>
    /// <b>Neden isim deseni DEĞİL bileşen tipi:</b> önceki sürüm ada bakıyordu
    /// (<c>questController_animrig</c>, <c>OVRLeftHandVisual</c>…) ve deseni iki kez tutmadı.
    /// Ölçüldü ki <c>questController_animrig</c> 24 objeyle eşleşiyor ama <b>hiçbiri aktif değil</b>
    /// — o Quest 1 / Rift S varyantı. Quest 3'te aktif olan varyant <c>MetaQuestTouchPlus_Left/Right</c>
    /// → <c>oculus_controller_l/r_MeshX</c> ve desene HİÇ uymuyor, yani kumanda modeli çiziliyordu.
    /// GameObject adı donanım varyantına göre değişir; <b>bileşen tipi değişmez</b>.
    /// </para>
    /// <para>
    /// ⚠️ <b>DOKUNULMAYANLAR</b> — kavrama/etkileşim bunlara bağlıdır, kapatılırsa oyun kırılır:
    /// <c>SyntheticHand</c>, <c>OVRHand</c>, interactor'lar, retiküller (<c>ReticleIconHand</c> vb.),
    /// <c>HandSphereMap</c>. Bu yüzden tarama iki tiple SINIRLI tutulur, "eli andıran her şey"
    /// süpürülmez.
    /// </para>
    /// <para>
    /// Güvenlik ölçüldü: <c>OVRControllerHelper</c> ve <c>HandVisual</c> taşıyan objelerin altında
    /// yalnız görsel bileşenler var (<c>BuildingBlock</c>, <c>OVRControllerHelper</c>,
    /// <c>HandVisual</c>, <c>MaterialPropertyBlockEditor</c>) — o objeleri kapatmak etkileşimi bozmaz.
    /// </para>
    /// <para>
    /// Her karede (LateUpdate) yeniden taranır: bu görseller kontrolcü bırakılıp tutulduğunda Meta
    /// tarafından yeniden AKTİFLEŞTİRİLİYOR — tek seferlik gizleme kalıcı olmuyor.
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

        private Transform rigRoot;
        private readonly List<MonoBehaviour> scanBuffer = new List<MonoBehaviour>(256);

        /// <summary>Bulunmuş görsel kökler — her kare bunların yalnız <c>activeSelf</c>'i bakılır.</summary>
        private readonly List<GameObject> targets = new List<GameObject>(16);

        /// <summary>Zaten loglanmışlar: gizleme her kare TEKRARLANIR ama log bir kez basılır.</summary>
        private readonly HashSet<GameObject> logged = new HashSet<GameObject>();

        private float rescanTimer = float.NegativeInfinity;

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

            // ⚠️ Gizleme her kare TEKRARLANIR: Meta bu görselleri kontrolcü bırakılıp tutulduğunda
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

        /// <summary>Rig altındaki görsel kökleri yeniden bulur (tek geçişte iki tip birden).</summary>
        private void Rescan()
        {
            rigRoot.GetComponentsInChildren(true, scanBuffer);
            for (int i = 0; i < scanBuffer.Count; i++)
            {
                MonoBehaviour mb = scanBuffer[i];
                if (mb == null)
                {
                    continue;
                }

                if (!(mb is OVRControllerHelper) && mb.GetType().Name != HandVisualTypeName)
                {
                    continue;
                }

                GameObject target = mb.gameObject;
                if (!targets.Contains(target))
                {
                    targets.Add(target);
                }
            }
        }
    }
}
