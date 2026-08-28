using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Combat;

namespace VortexArena.Modes.Mole
{
    /// <summary>Puts a hammer in BOTH of the local player's hands and keeps it there, tinted in the
    /// player's team colour.
    /// <para>⚠️ The hammer is an ITEM, not a weapon: this mode is <c>weaponSource:"none"</c> (§10.5), so
    /// nothing else grants, holsters or fires. Handing it out through <c>WeaponDefinition</c> instead
    /// would open the damage path this whole family is built without.</para>
    /// <para>⚠️ It cannot be dropped: the hand is re-filled the moment its claim goes missing. A child
    /// who lets go of the grip must not end up watching the game with empty hands — and there is nothing
    /// else to pick up in this arena.</para>
    /// <para>Lives on the mode HUD prefab because that object's LIFETIME is exactly right: it exists
    /// while the mode's match does and dies with it. A hand-placed scene object would have to be
    /// remembered in every mole arena.</para></summary>
    [DisallowMultipleComponent]
    public sealed class MoleHammerGranter : MonoBehaviour
    {
        [Tooltip("Balyoz eşya tanımı — uzak elde bu tanımın netItemId'si çizilir.")]
        [SerializeField] private PropDefinition hammerDefinition;

        [Tooltip("Ele verilecek balyoz prefabı (kökü kabza ucu olmalı).")]
        [SerializeField] private GameObject hammerPrefab;

        [Tooltip("Kırmızı takımın balyoz rengi.")]
        [SerializeField] private Color redColor = new Color(0.86f, 0.22f, 0.20f);

        [Tooltip("Mavi takımın balyoz rengi.")]
        [SerializeField] private Color blueColor = new Color(0.20f, 0.42f, 0.90f);

        [Tooltip("Takımsızken (lobi/izleyici) balyoz rengi.")]
        [SerializeField] private Color neutralColor = new Color(0.75f, 0.72f, 0.66f);

        private Transform _left;
        private Transform _right;

        /// <summary>Team the hammers are currently painted in; <see cref="Team.Neutral"/> forces the
        /// first paint.</summary>
        private Team _paintedTeam = Team.Neutral;

        private bool _painted;

        private void Awake()
        {
            if (hammerDefinition == null || hammerPrefab == null)
            {
                Debug.LogError("[MoleHammerGranter] Balyoz tanımı ya da prefabı atanmamış — " +
                               "oyuncunun eline balyoz gelmeyecek.", this);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            HeldItems.Release(this);
            DestroyHand(ref _left);
            DestroyHand(ref _right);
            _painted = false;
        }

        private void Update()
        {
            EnsureHand(false);
            EnsureHand(true);
            ApplyTeamTint();
        }

        /// <summary>Makes sure the hand holds OUR hammer; rebuilds it when the claim is gone (dropped,
        /// destroyed, or taken over by another writer that has since released it).</summary>
        private void EnsureHand(bool rightHand)
        {
            Transform slot = rightHand ? _right : _left;
            HeldItems.Slot held = rightHand ? HeldItems.RightHand : HeldItems.LeftHand;

            if (slot != null && held.Owner == this && held.Instance == slot)
            {
                return;
            }

            DestroyHand(ref slot);
            if (rightHand)
            {
                _right = null;
            }
            else
            {
                _left = null;
            }

            OVRInput.Controller hand = rightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            Transform anchor = WeaponGranter.ResolveHandAnchor(hand);
            if (anchor == null)
            {
                // No rig yet (scene still loading) — try again next frame.
                return;
            }

            GameObject instance = Instantiate(hammerPrefab, anchor);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            if (!HeldItems.Report(this, rightHand, hammerDefinition, instance.transform,
                    GripSocketKind.Primary, hand))
            {
                // Another writer owns this hand right now; drop ours rather than leave an orphan in it.
                Destroy(instance);
                return;
            }

            if (rightHand)
            {
                _right = instance.transform;
            }
            else
            {
                _left = instance.transform;
            }

            _painted = false;
        }

        private static void DestroyHand(ref Transform slot)
        {
            if (slot == null)
            {
                return;
            }

            Destroy(slot.gameObject);
            slot = null;
        }

        /// <summary>The hammer's colour is the rule reminder: "smash the mole that matches your hammer".
        /// Repainted when the admin moves the player to the other team (§5.2).</summary>
        private void ApplyTeamTint()
        {
            Team team = ArenaCombat.LocalTeam;
            if (_painted && team == _paintedTeam)
            {
                return;
            }

            _paintedTeam = team;
            _painted = true;

            Color color = team == Team.Red ? redColor : team == Team.Blue ? blueColor : neutralColor;
            Paint(_left, color);
            Paint(_right, color);
        }

        private static void Paint(Transform hammer, Color color)
        {
            if (hammer == null)
            {
                return;
            }

            var renderers = hammer.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].material.color = color;
            }
        }
    }
}
