using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// HUD'ın alt ortasındaki <b>maç kontrol şeridi</b>: üç ikon düğme (▶ BAŞLAT ·
    /// ⏸/▶ DURAKLAT-DEVAM · ■ İPTAL) ve son komutun durum satırı.
    ///
    /// <para><b>Görünüm prefabtan gelir</b> (<c>AdminHud.prefab</c> → <c>MatchBar</c>); bu sınıf
    /// yalnız davranıştır: düğmeleri bağlar, faz değiştikçe boyar ve pasifleştirir.</para>
    ///
    /// <para><b>Neden ayrı bir bileşen:</b> düğmeler HUD'ın <b>kalıcı</b> katmanında durur (panel
    /// kapalıyken de basılabilmeleri gerekir), ama <b>seçim durumu tercihler panelinde yaşar</b> —
    /// mod/harita/süre/limit/geri sayım ve "lobi açık mı" oradadır. Tek doğruluk kaynağı odur; bu
    /// şerit başlatmayı <see cref="AdminPreferencesPanel.StartSelectedMatch"/> ile ona sorar,
    /// kendi seçim alanını TUTMAZ (ikinci bir kaynak sessizce sapardı).</para>
    ///
    /// <para>⚠️ Düğmelerin pasifleşmesi yalnız bir <b>arayüz kapısıdır</b>: operatörü boşuna
    /// tıklatmamak içindir. Otorite sunucudadır ve aynı kurallar orada da uygulanır — bu yüzden
    /// veri henüz yokken (<see cref="AdminRoster"/> null) düğmeler AÇIK bırakılır, son sözü sunucu
    /// söyler.</para>
    ///
    /// <para>Tazeleme olay güdümlüdür; ek olarak <see cref="RefreshInterval"/> ile bir emniyet tiki
    /// koşar: panelin lobi bayrağı sahne komutundan sonra güncellendiği için tek bir olay karesi
    /// yanlış boyanmış bir düğme bırakabilir.</para>
    /// </summary>
    public class AdminMatchControls : MonoBehaviour
    {
        /// <summary>Faz/seçim değişimini kaçırmamak için emniyet tazeleme aralığı (sn) —
        /// <see cref="AdminHud"/> ile aynı ritim.</summary>
        private const float RefreshInterval = 0.25f;

        [Header("Seçim kaynağı")]
        [Tooltip("Mod/harita/süre seçimi ve lobi durumu bu panelde yaşar; BAŞLAT ona sorar. " +
                 "Boşsa Awake'te aynı canvas'ta aranır.")]
        [SerializeField] private AdminPreferencesPanel preferences;

        [Header("Düğmeler")]
        [SerializeField] private Button startButton;
        [SerializeField] private Image startIcon;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Image pauseIcon;
        [SerializeField] private Button abortButton;
        [SerializeField] private Image abortIcon;

        [Header("İkonlar")]
        [Tooltip("DURAKLAT/DEVAM ET tek düğmedir: koşan maçta pauseSprite, operatörün duraklattığı " +
                 "maçta playSprite gösterir.")]
        [SerializeField] private Sprite playSprite;
        [SerializeField] private Sprite pauseSprite;

        [Header("Durum")]
        [Tooltip("Son komutun insan okuyabilir sonucu (AdminCommands.Status).")]
        [SerializeField] private TextMeshProUGUI statusText;

        private float _nextRefresh;
        private bool _dirty = true;

        private void Awake()
        {
            if (preferences == null)
            {
                // ⚠️ Unity'nin sahte-null'u yüzünden `??=` KULLANILMAZ: yıkılmış bir bileşen
                // referansı `??=` için "dolu" görünür ve arama hiç yapılmazdı.
                preferences = transform.root.GetComponentInChildren<AdminPreferencesPanel>(true);
            }

            WireButtons();
        }

        /// <summary>
        /// Prefabtaki düğmelere davranışı bağlar. <b>Prefabta kalıcı (persistent) onClick kaydı
        /// YOKTUR</b> (<see cref="AdminHud"/>'daki bağlama ile aynı gerekçe): komutlar koşulludur
        /// (lobi açıkken BAŞLAT reddeder, duraklat/devam faza göre farklı komut gönderir) ve
        /// inspector'dan bağlanan bir kayıt o koşulları atlardı.
        /// </summary>
        private void WireButtons()
        {
            Wire(startButton, StartMatch);
            Wire(pauseButton, TogglePause);
            Wire(abortButton, AdminCommands.AbortMatch);
        }

        private static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private void OnEnable()
        {
            AdminCommands.StatusChanged += MarkDirty;
            NetEvents.OnConnectionStateChanged += HandleConnectionState;
            NetEvents.OnReturnToLobby += HandleOpenSceneChanged;
            NetEvents.OnLoadMatch += HandleOpenSceneChanged;
            NetEvents.OnConnected += HandleWelcome;

            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed += MarkDirty;
            }
        }

        private void OnDisable()
        {
            AdminCommands.StatusChanged -= MarkDirty;
            NetEvents.OnConnectionStateChanged -= HandleConnectionState;
            NetEvents.OnReturnToLobby -= HandleOpenSceneChanged;
            NetEvents.OnLoadMatch -= HandleOpenSceneChanged;
            NetEvents.OnConnected -= HandleWelcome;

            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed -= MarkDirty;
            }
        }

        private void Update()
        {
            bool tick = Time.unscaledTime >= _nextRefresh;
            if (tick)
            {
                _nextRefresh = Time.unscaledTime + RefreshInterval;
            }

            if (_dirty || tick)
            {
                _dirty = false;
                Refresh();
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void HandleConnectionState(ArenaConnectionState state)
        {
            _dirty = true;
        }

        private void HandleOpenSceneChanged(ReturnToLobbyMsg msg)
        {
            _dirty = true;
        }

        private void HandleOpenSceneChanged(LoadMatchMsg msg)
        {
            _dirty = true;
        }

        private void HandleWelcome(WelcomeMsg msg)
        {
            _dirty = true;
        }

        // ------------------------------------------------------------------ eylemler

        /// <summary>Maçı panelde seçili ayarlarla başlatır. Panel yoksa komut GÖNDERİLMEZ: mod ve
        /// harita orada seçiliyor, buradan uydurulacak bir varsayılan yok.</summary>
        private void StartMatch()
        {
            if (preferences == null)
            {
                AdminCommands.Note("Tercihler paneli yok; maç başlatılamadı.");
                return;
            }

            preferences.StartSelectedMatch();
        }

        /// <summary>
        /// Faz <c>playing</c> ise duraklatır, operatörün duraklattığı maçta ise sürdürür.
        /// <para>Karar YEREL bir bayrağa değil sunucudan gelen faza bakar: çoklu admin var
        /// (CLAUDE.md) ve duraklatmayı başkası da yapmış olabilir. Yerel bayrak tutulsaydı iki
        /// ekran birbirine ters düşerdi.</para>
        /// </summary>
        private static void TogglePause()
        {
            if (IsOperatorPaused)
            {
                AdminCommands.ResumeMatch();
            }
            else
            {
                AdminCommands.PauseMatch();
            }
        }

        /// <summary>Maç koşuyor mu (§10.1: tek cevap <c>phase == playing</c>).</summary>
        private static bool IsMatchLive
        {
            get
            {
                AdminRoster roster = AdminRoster.Instance;
                return roster != null && roster.Phase == ArenaProtocol.PHASE_PLAYING;
            }
        }

        /// <summary>Maç OPERATÖR tarafından duraklatılmış mı. Modun/geri sayımın duraklaması
        /// buraya girmez — sunucu onları <c>resume_match</c> ile kaldırtmaz (§5.2).</summary>
        private static bool IsOperatorPaused
        {
            get
            {
                AdminRoster roster = AdminRoster.Instance;
                return roster != null &&
                       roster.Phase == ArenaProtocol.PHASE_PAUSED &&
                       roster.PhaseReason == ArenaProtocol.PAUSE_REASON_OPERATOR;
            }
        }

        // ------------------------------------------------------------------ tazeleme

        /// <summary>Üç düğmeyi ve durum satırını yürürlükteki faza göre boyar. ⚠️ Tüm alanlar
        /// null-güvenli okunur: prefabta bağlanmayan öge sessizce çizilmez, şeridin geri kalanı
        /// çalışmaya devam eder.</summary>
        private void Refresh()
        {
            AdminRoster roster = AdminRoster.Instance;

            // BAŞLAT: panel yoksa kapı açık bırakılır (sunucu zaten reddeder).
            bool canStart = preferences == null || preferences.CanStartMatch;
            if (startButton != null)
            {
                startButton.interactable = canStart;
            }

            if (startIcon != null)
            {
                startIcon.color = canStart ? UiKit.Good : UiKit.Faint;
            }

            // DURAKLAT/DEVAM: tek düğme, ikonu ve komutu fazdan gelir.
            bool paused = IsOperatorPaused;
            bool live = IsMatchLive;
            if (pauseButton != null)
            {
                pauseButton.interactable = paused || live;
            }

            if (pauseIcon != null)
            {
                Sprite icon = paused ? playSprite : pauseSprite;
                if (icon != null)
                {
                    pauseIcon.sprite = icon;
                }

                pauseIcon.color = paused ? UiKit.Good : live ? UiKit.Title : UiKit.Faint;
            }

            // İPTAL her fazdan lobiye döndürür (<c>abort_match</c>, §10.1) — yani maç kuruluyken
            // ya da bitmişken anlamlıdır. Zaten lobide bekleniyorsa iptal edilecek bir şey yok.
            bool inLobby = roster != null &&
                           roster.Phase == ArenaProtocol.PHASE_PAUSED &&
                           (string.IsNullOrEmpty(roster.PhaseReason) ||
                            roster.PhaseReason == ArenaProtocol.PAUSE_REASON_LOBBY);
            if (abortButton != null)
            {
                abortButton.interactable = !inLobby;
            }

            if (abortIcon != null)
            {
                abortIcon.color = !inLobby ? UiKit.Bad : UiKit.Faint;
            }

            if (statusText != null)
            {
                statusText.text = AdminCommands.Status;
            }
        }
    }
}
