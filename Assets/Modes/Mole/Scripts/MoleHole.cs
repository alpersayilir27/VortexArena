using System.Collections.Generic;
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
    /// already took down is still climbing on screen — and unhittable moles look like a broken hammer.</para></summary>
    [RequireComponent(typeof(NetObject))]
    [DisallowMultipleComponent]
    public sealed class MoleHole : MonoBehaviour
    {
        [Tooltip("Yükselip inen köstebek kökü. Boşsa 'Mole' adlı çocuk aranır.")]
        [SerializeField] private Transform mole;

        [Tooltip("Köstebeğin takım rengini alacak görsel. Boşsa köstebeğin altında aranır.")]
        [SerializeField] private Renderer moleRenderer;

        [Tooltip("Köstebeğin delikten yükseldiği mesafe (m).")]
        [SerializeField] private float riseHeight = 0.4f;

        [Tooltip("Yükselme/inme süresi (sn). ⚠️ Sunucunun havada kalma penceresinden KISA olmalı.")]
        [SerializeField] private float riseSeconds = 0.25f;

        [Tooltip("Ezilince köstebeğin indiği dikey ölçek oranı.")]
        [SerializeField] private float squashScale = 0.3f;

        [SerializeField] private Color redColor = new Color(0.86f, 0.22f, 0.20f);
        [SerializeField] private Color blueColor = new Color(0.20f, 0.42f, 0.90f);

        /// <summary>Every hole in the loaded scene — what <see cref="MoleHammer"/> searches instead of
        /// physics. A trigger would need a kinematic rigidbody on a hand-driven hammer, and teleporting
        /// hands miss triggers between frames.</summary>
        private static readonly List<MoleHole> _all = new List<MoleHole>();

        public static IReadOnlyList<MoleHole> All => _all;

        private NetObject _net;
        private Material _moleMaterial;

        private Vector3 _restLocalPosition;
        private Vector3 _restLocalScale;

        /// <summary>How far out the mole is, 0..1.</summary>
        private float _height;

        /// <summary>Pop counter from the payload; the value a <c>whack</c> must carry back.</summary>
        public int Nonce { get; private set; } = -1;

        /// <summary>Mole colour from the payload (<c>red</c>/<c>blue</c>); empty while the hole is idle.</summary>
        public string Color { get; private set; } = "";

        public int NetId => _net != null ? _net.NetId : 0;

        public bool IsUp => _net != null && _net.Stage == MoleKinds.StageUp;

        /// <summary>Where the hammer has to land — the mole itself, not the hole.</summary>
        public Vector3 HitPoint => mole != null ? mole.position : transform.position;

        private void Awake()
        {
            _net = GetComponent<NetObject>();

            if (mole == null)
            {
                Transform found = transform.Find("Mole");
                mole = found != null ? found : transform;
            }

            if (moleRenderer == null)
            {
                moleRenderer = mole.GetComponentInChildren<Renderer>(true);
            }

            _restLocalPosition = mole.localPosition;
            _restLocalScale = mole.localScale;

            if (moleRenderer != null)
            {
                _moleMaterial = moleRenderer.material;
            }
        }

        private void OnEnable()
        {
            _all.Add(this);
            _net.StateChanged += HandleStateChanged;
            ReadPayload();
            ApplyImmediate();
        }

        private void OnDisable()
        {
            _all.Remove(this);
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

            Color = _net.TryGetPayloadValue(MoleKinds.PayloadColor, out string color) ? color : "";

            if (_moleMaterial == null)
            {
                return;
            }

            _moleMaterial.color = Color == MoleKinds.ColorBlue ? blueColor : redColor;
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
        /// sight under the ground wherever the floor mesh does not cover it.</summary>
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
