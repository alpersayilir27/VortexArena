using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Hayalet gövdeyi (ölü/kalibresiz avatarda çizilen İKİNCİ model) karakterin canlı
    /// iskeletinden sürer — Unity'nin humanoid retarget'ı ile, çalışma anında.
    /// <para>
    /// ⚠️ <b>Neden ikinci bir Movement SDK retargeter'ı DEĞİL:</b> iskelet blob'u
    /// <c>RemoteSkeletonRegistry.TryTakeBlob</c> ile <b>tüketilir</b> (aynı kareyi iki kez
    /// oynatmamak için) — iki retargeter aynı blob için yarışırdı. Üstüne hayalet modelin kendi
    /// MSDK retarget config'i gerekirdi. Bu köprü ağ yoluna HİÇ dokunmaz: zaten uygulanmış olan
    /// karakter pozunu okur, hayalete giydirir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Execution order yüksektir ve öyle kalmalı:</b> okunacak şey SDK'nın o kare
    /// <b>uygulanmış</b> pozudur. <c>CharacterRetargeter</c> ve
    /// <see cref="ArenaNetCharacterBehaviour"/> kendi <c>LateUpdate</c>'lerinde yazıyor; daha
    /// erken koşan bir sürücü bir kare bayat gövde çizerdi (<c>LocalBodyAvatar</c>'ın 30000'i
    /// aynı gerekçeyle konuldu).
    /// </para>
    /// <para>
    /// ⚠️ <b>Animator EKLENMEZ.</b> Köprü <see cref="HumanPoseHandler"/>'ı doğrudan Avatar +
    /// kök ile kurar. Kaynak karaktere Animator takmak, MSDK'nın yazdığı kemiklerin üstüne
    /// ikinci bir sürücü koymak olurdu.
    /// </para>
    /// <para>
    /// Görünmezken hiç koşmaz: <see cref="RemoteAvatar"/> bu bileşenin <c>enabled</c>'ını
    /// hayalet durumuyla birlikte açıp kapatır.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(30100)]
    public class GhostPoseDriver : MonoBehaviour
    {
        [Header("Kaynak — ağdan sürülen karakter")]
        [Tooltip("Karakter FBX'inin humanoid Avatar'ı (Ch15_nonPBR: Rig = Humanoid).")]
        [SerializeField] private Avatar sourceAvatar;

        [Tooltip("Retargeter'ın yazdığı iskeletin kökü (ArenaNetCharacterBehaviour'un oturttuğu kök).")]
        [SerializeField] private Transform sourceRoot;

        [Header("Hedef — hayalet gövde")]
        [Tooltip("Hayalet modelin humanoid Avatar'ı. Model değiştirmek YALNIZ bu iki alanı değiştirmektir.")]
        [SerializeField] private Avatar ghostAvatar;

        [SerializeField] private Transform ghostRoot;

        [Tooltip("Hayaletin ölçeği kaynağın ölçeğini izlesin mi. Boy blob'un 0. ekleminden " +
                 "geliyor ve SDK onu kaynağın localScale'ine yazıyor; kapatılırsa hayalet " +
                 "oyuncunun gerçek boyunda görünmez.")]
        [SerializeField] private bool matchScale = true;

        private HumanPoseHandler _sourceHandler;
        private HumanPoseHandler _ghostHandler;

        // Kas dizisi ilk GetHumanPose'ta bir kez ayrılır; alan olarak tutulduğu için kare
        // başına çöp üretmez.
        private HumanPose _pose;

        private bool _ready;

        private void Awake()
        {
            _ready = TrySetup();
        }

        private void OnDestroy()
        {
            // ⚠️ HumanPoseHandler yönetilmeyen bellek tutar — Dispose edilmezse her uzak oyuncu
            // örneğinde sızar (avatar başka bir oyuncuya devredilirken de yeniden kurulmaz,
            // örnek ömrü boyunca tek handler yeter).
            _sourceHandler?.Dispose();
            _ghostHandler?.Dispose();
            _sourceHandler = null;
            _ghostHandler = null;
            _ready = false;
        }

        private bool TrySetup()
        {
            if (!IsUsable(sourceAvatar, sourceRoot, "kaynak") ||
                !IsUsable(ghostAvatar, ghostRoot, "hayalet"))
            {
                return false;
            }

            _sourceHandler = new HumanPoseHandler(sourceAvatar, sourceRoot);
            _ghostHandler = new HumanPoseHandler(ghostAvatar, ghostRoot);
            return true;
        }

        /// <summary>
        /// Alan bağı + Avatar geçerliliği. ⚠️ <b>Hata basar ve kendini kapatır:</b> sessiz kalsaydı
        /// hayalet gövde T-pozunda donar ve bu sahada "ağ bozuk" diye okunurdu — oysa tek eksik
        /// bir prefab bağıdır.
        /// </summary>
        private bool IsUsable(Avatar avatar, Transform root, string role)
        {
            if (avatar == null || root == null)
            {
                Debug.LogError($"[GhostPoseDriver] {role} alanları boş — hayalet gövde " +
                               "sürülemeyecek (T-pozunda donar).", this);
                return false;
            }

            if (!avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[GhostPoseDriver] {role} Avatar'ı humanoid değil " +
                               $"('{avatar.name}') — FBX importer'da Rig = Humanoid olmalı.", this);
                return false;
            }

            return true;
        }

        private void LateUpdate()
        {
            if (!_ready)
            {
                return;
            }

            // Kas uzayında oku → farklı gövde oranlarına kendiliğinden uyar (humanoid retarget'ın
            // tek varlık sebebi bu; iki iskeletin kemik adları/oranları aynı olmak zorunda değil).
            _sourceHandler.GetHumanPose(ref _pose);

            // HumanPose'un gövde konumu KÖKE GÖREdir: iki kök üst üste oturmadan poz doğru yere
            // uygulanmaz.
            ghostRoot.SetPositionAndRotation(sourceRoot.position, sourceRoot.rotation);
            if (matchScale)
            {
                ghostRoot.localScale = sourceRoot.localScale;
            }

            _ghostHandler.SetHumanPose(ref _pose);
        }
    }
}
