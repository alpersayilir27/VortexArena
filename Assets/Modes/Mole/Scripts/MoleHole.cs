using UnityEngine;
using VortexArena.Net;

namespace VortexArena.Modes.Mole
{
    /// <summary>A hole in the ground: the mole rises out of it, waits, gets squashed, goes back down.
    /// Stage and colour come from the server (<c>stage</c> + <c>s</c>, §10.5); the MOVEMENT is played
    /// locally.
    /// <para>⚠️ The mole is NOT a network object — it is this object's presentation child. So the prefab
    /// carries NO <c>NetObjectBody</c> / <c>NetObjectPoseSender</c>: the rise would then be driven by the
    /// animation AND by the wire at once, i.e. one mole in two places.</para>
    /// <para>⚠️ The rise must finish INSIDE the server's up-window (§10.5), otherwise a mole the server
    /// already took down is still climbing on screen — and unhittable moles look like a broken hammer.</para>
    /// <para><b>The mole needs a collider</b> — that is what the hammer touches
    /// (<see cref="MoleHammer"/>), and it may sit anywhere under this object, so a real model brings
    /// its own. Make it a TRIGGER: a solid mole shoves the player's real body around in free
    /// roam.</para>
    /// <para><b>Prefab shape — swapping in a real model:</b> the rising pivot (<c>Mole</c>) carries the
    /// collider and the timing, and its <c>Model</c> child carries ONLY the look. Replacing the mole
    /// means replacing <c>Model</c>: line the new model's feet up with the pivot's origin (the mole
    /// stands at ground level when raised) and its head with the old head's height, then drag the
    /// renderers that should take the team colour into <see cref="teamRenderers"/>. Nothing else moves
    /// — the pivot is what rises, not the model.</para>
    /// <para>⚠️ <b><c>Model</c>'s own transform must stay at zero</b> (position, rotation, scale 1): it is
    /// the swap point, not a placement. An offset there moves the mole away from its hole in EVERY
    /// arena at once, and the mole is below the floor while hidden — so the mistake shows up as holes
    /// that look misplaced rather than as a mole in the wrong spot. Place the model's parts inside
    /// <c>Model</c>, never by nudging <c>Model</c> itself.</para></summary>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public sealed class MoleHole : MonoBehaviour
    {
        [Tooltip("Yükselip inen köstebek kökü (collider ve zamanlama burada; görsel 'Model' " +
                 "çocuğundadır). Boşsa 'Mole' adlı çocuk aranır.")]
        [SerializeField] private Transform mole;

        [Tooltip("Takım rengini ALACAK görseller — gövde, kafa, pençeler. Göz ve burun gibi sabit " +
                 "renkli parçalar buraya KONMAZ. Boşsa köstebeğin altındaki tüm görseller boyanır.")]
        [SerializeField] private Renderer[] teamRenderers;

        [Tooltip("Köstebeğin delikten yükseldiği mesafe (m).")]
        [SerializeField] private float riseHeight = 0.4f;

        [Tooltip("Yükselme/inme süresi (sn). ⚠️ Sunucunun havada kalma penceresinden KISA olmalı.")]
        [SerializeField] private float riseSeconds = 0.25f;

        [Tooltip("Ezilince köstebeğin indiği dikey ölçek oranı.")]
        [SerializeField] private float squashScale = 0.3f;

        [SerializeField] private Color redColor = new Color(0.86f, 0.22f, 0.20f);
        [SerializeField] private Color blueColor = new Color(0.20f, 0.42f, 0.90f);

        private NetObject _net;

        /// <summary>Material INSTANCES of the team-coloured renderers. Instances on purpose: writing the
        /// shared asset would repaint every hole in the scene at once.</summary>
        private Material[] _teamMaterials;

        private Vector3 _restLocalPosition;
        private Vector3 _restLocalScale;

        /// <summary>How far out the mole is, 0..1.</summary>
        private float _height;

        /// <summary>Pop counter from the payload; the value a <c>whack</c> must carry back.</summary>
        public int Nonce { get; private set; } = -1;

        /// <summary>Mole colour from the payload (<c>red</c>/<c>blue</c>); empty while the hole is idle.
        /// Only the tint reads it — the hitter's own team is compared on the SERVER.</summary>
        private string _color = "";

        public int NetId => _net != null ? _net.NetId : 0;

        public bool IsUp => _net != null && _net.Stage == MoleKinds.StageUp;

        private void Awake()
        {
            _net = GetComponent<NetObject>();

            if (mole == null)
            {
                Transform found = transform.Find("Mole");
                mole = found != null ? found : transform;
            }

            // A swapped-in model loses the hand-picked list; painting everything is a better default
            // than painting nothing (the player must be able to tell the two colours apart).
            if (teamRenderers == null || teamRenderers.Length == 0)
            {
                teamRenderers = mole.GetComponentsInChildren<Renderer>(true);
            }

            _restLocalPosition = mole.localPosition;
            _restLocalScale = mole.localScale;

            _teamMaterials = new Material[teamRenderers.Length];
            for (int i = 0; i < teamRenderers.Length; i++)
            {
                if (teamRenderers[i] != null)
                {
                    _teamMaterials[i] = teamRenderers[i].material;
                }
            }
        }

        private void OnEnable()
        {
            _net.StateChanged += HandleStateChanged;
            ReadPayload();
            ApplyImmediate();
        }

        private void OnDisable()
        {
            _net.StateChanged -= HandleStateChanged;
        }

        /// <summary>Payload is re-read on EVERY state, including the snapshot a late joiner gets — that is
        /// where a standing mole's colour and counter come from (§10.5).</summary>
        private void HandleStateChanged(NetObject net, NetStateOrigin origin)
        {
            ReadPayload();

            // A snapshot must not animate: a late joiner would watch a mole climb that has been standing
            // for a second already (§10.10 origin).
            if (origin == NetStateOrigin.Snapshot)
            {
                ApplyImmediate();
            }
        }

        private void ReadPayload()
        {
            Nonce = _net.TryGetPayloadValue(MoleKinds.PayloadNonce, out string nonce) &&
                    int.TryParse(nonce, out int parsed)
                ? parsed
                : -1;

            _color = _net.TryGetPayloadValue(MoleKinds.PayloadColor, out string color) ? color : "";

            if (_teamMaterials == null)
            {
                return;
            }

            Color tint = _color == MoleKinds.ColorBlue ? blueColor : redColor;
            for (int i = 0; i < _teamMaterials.Length; i++)
            {
                if (_teamMaterials[i] != null)
                {
                    _teamMaterials[i].color = tint;
                }
            }
        }

        /// <summary>Jumps straight to the state's pose (no animation) — used on enable and on snapshots.</summary>
        private void ApplyImmediate()
        {
            _height = _net.Stage == MoleKinds.StageHidden ? 0f : 1f;
            ApplyPose();
        }

        private void Update()
        {
            float target = _net.Stage == MoleKinds.StageHidden ? 0f : 1f;
            _height = Mathf.MoveTowards(_height, target, Time.deltaTime / Mathf.Max(0.01f, riseSeconds));
            ApplyPose();
        }

        /// <summary>⚠️ The mole is switched OFF while down instead of just being lowered: the free-roam
        /// floor is flat (there is no physical pit), so a mole sitting below y=0 would hang in plain
        /// sight under the ground wherever the floor mesh does not cover it.
        /// <para>Its collider goes with it, so a hidden mole cannot be hit at all — no second gate
        /// needed for that case.</para></summary>
        private void ApplyPose()
        {
            bool visible = _height > 0.001f;
            if (mole.gameObject.activeSelf != visible)
            {
                mole.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            mole.localPosition = _restLocalPosition + Vector3.up * (riseHeight * _height);

            float squash = _net.Stage == MoleKinds.StageSquashed ? Mathf.Max(0.05f, squashScale) : 1f;
            mole.localScale = new Vector3(
                _restLocalScale.x,
                _restLocalScale.y * squash,
                _restLocalScale.z);
        }
    }
}
