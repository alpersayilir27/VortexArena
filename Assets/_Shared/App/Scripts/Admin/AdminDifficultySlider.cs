using System;
using TMPro;
using UnityEngine;
using VortexArena.Core.UI;
using VortexArena.Protocol;

using UiSlider = UnityEngine.UI.Slider;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// ZORLUK slider above the right end of the HUD match bar: re-tunes the <b>running</b> zombie
    /// match (§10.5) through <see cref="AdminCommands.SetDifficulty"/>.
    /// <para><b>Look comes from the prefab</b> (<c>GirdapUiBuilder.Hud</c>); this class is behaviour
    /// only. ⚠️ It lives on the HUD <b>ROOT</b> with the widget as a reference, not on the widget
    /// itself: a component on a deactivated object could never show it again.</para>
    /// <para>⚠️ Authority is the server: the handle draws the <c>diff</c> token of
    /// <c>match_state.modeState</c> and an operator move only sends a request — a rejected one snaps
    /// back on the next echo.</para>
    /// </summary>
    public class AdminDifficultySlider : MonoBehaviour
    {
        /// <summary><c>modeId</c> of the only mode reading the difficulty (§10.5). ⚠️ Must match the
        /// server's <c>ZombieMode.ModeId</c> literally.</summary>
        private const string ZombieModeId = "zombie";

        /// <summary><c>modeState</c> token carrying the LIVE difficulty (§10.5).</summary>
        private const string DifficultyKey = "diff";

        /// <summary>Refresh interval of the server reconcile (s) — the echo arrives with
        /// <c>match_state</c>, so there is nothing to redraw faster than that.</summary>
        private const float RefreshInterval = 0.25f;

        /// <summary>Grace after an operator move: the echo is one round trip away and reconciling in
        /// between would snap the handle back under the operator's hand.</summary>
        private const float EchoGrace = 1.5f;

        [Header("Zorluk kaydırıcısı")]
        [Tooltip("Parçanın tamamı — yalnız koşan zombi maçında açılır, lobide kapanır.")]
        [SerializeField] private GameObject group;

        [Tooltip("0=KOLAY, 1=NORMAL, 2=ZOR (wholeNumbers).")]
        [SerializeField] private UiSlider slider;

        [Tooltip("Durak etiketleri; koşan zorluğun etiketi vurgulanır.")]
        [SerializeField] private TextMeshProUGUI easyLabel;
        [SerializeField] private TextMeshProUGUI normalLabel;
        [SerializeField] private TextMeshProUGUI hardLabel;

        /// <summary>Difficulty currently drawn. Also the loop guard: an echo equal to this changes
        /// nothing and sends nothing.</summary>
        private string _shown = MatchDifficulty.Normal;

        /// <summary>True while the SERVER value is written into the handle — <c>onValueChanged</c>
        /// must not mistake the echo for an operator move and bounce it back.</summary>
        private bool _applying;

        private float _nextRefresh;
        private float _quietUntil;
        private bool _dirty = true;

        private void Awake()
        {
            if (slider != null)
            {
                // ⚠️ No persistent onValueChanged entry in the prefab: the echo guard lives here.
                slider.onValueChanged.RemoveAllListeners();
                slider.onValueChanged.AddListener(HandleValueChanged);
            }
        }

        private void OnEnable()
        {
            AdminSession.Changed += MarkDirty;
            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed += MarkDirty;
            }
        }

        private void OnDisable()
        {
            AdminSession.Changed -= MarkDirty;
            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed -= MarkDirty;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextRefresh)
            {
                // Also covers a roster that was not alive yet at OnEnable.
                _nextRefresh = Time.unscaledTime + RefreshInterval;
                _dirty = true;
            }

            if (!_dirty)
            {
                return;
            }

            _dirty = false;
            Refresh();
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void Refresh()
        {
            AdminRoster roster = AdminRoster.Instance;
            bool visible = roster != null && roster.ModeId == ZombieModeId && !InLobby(roster);

            if (group != null && group.activeSelf != visible)
            {
                group.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            // A state without the token (match still loading) must not drag the handle to a default.
            string server = ReadDifficulty(roster.ModeState);
            if (server != null && Time.unscaledTime >= _quietUntil)
            {
                Show(server);
                return;
            }

            Paint();
        }

        /// <summary>The widget is for a LIVE match; in the lobby there is nothing to re-tune (the
        /// next match's difficulty is picked in the preferences panel).</summary>
        private static bool InLobby(AdminRoster roster)
        {
            return roster.Phase == ArenaProtocol.PHASE_PAUSED &&
                   (string.IsNullOrEmpty(roster.PhaseReason) ||
                    roster.PhaseReason == ArenaProtocol.PAUSE_REASON_LOBBY);
        }

        private void HandleValueChanged(float raw)
        {
            if (_applying)
            {
                return;
            }

            string picked = DifficultyAt(Mathf.RoundToInt(raw));
            if (picked == _shown)
            {
                return;
            }

            _shown = picked;
            _quietUntil = Time.unscaledTime + EchoGrace;
            Paint();
            AdminCommands.SetDifficulty(picked);
        }

        private void Show(string difficulty)
        {
            _shown = difficulty;

            if (slider != null)
            {
                _applying = true;
                slider.value = IndexOf(difficulty);
                _applying = false;
            }

            Paint();
        }

        private void Paint()
        {
            Tint(easyLabel, _shown == MatchDifficulty.Easy);
            Tint(normalLabel, _shown == MatchDifficulty.Normal);
            Tint(hardLabel, _shown == MatchDifficulty.Hard);
        }

        private static void Tint(TextMeshProUGUI label, bool active)
        {
            if (label != null)
            {
                label.color = active ? Girdap.AccHi : Girdap.Muted;
            }
        }

        private static int IndexOf(string difficulty)
        {
            return difficulty == MatchDifficulty.Easy ? 0
                : difficulty == MatchDifficulty.Hard ? 2
                : 1;
        }

        private static string DifficultyAt(int index)
        {
            return index <= 0 ? MatchDifficulty.Easy
                : index >= 2 ? MatchDifficulty.Hard
                : MatchDifficulty.Normal;
        }

        /// <summary>Live difficulty from <c>modeState</c>; <c>null</c> = no token, so the caller keeps
        /// what it draws.</summary>
        private static string ReadDifficulty(string modeState)
        {
            if (string.IsNullOrEmpty(modeState))
            {
                return null;
            }

            string[] tokens = modeState.Split(';');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int sep = token.IndexOf(':');
                if (sep <= 0 || sep == token.Length - 1)
                {
                    continue;
                }

                if (!string.Equals(token.Substring(0, sep).Trim(), DifficultyKey,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                return MatchDifficulty.Normalize(token.Substring(sep + 1).Trim());
            }

            return null;
        }
    }
}
