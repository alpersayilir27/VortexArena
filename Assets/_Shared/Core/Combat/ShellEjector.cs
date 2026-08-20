using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Ejects a casing on fire: subscribes to <see cref="Weapon.Fired"/> and asks
    /// <see cref="CasingPool.Shared"/> for the casing.
    /// <para>⚠️ Pooling and lifetime are deliberately NOT here: the weapon instance is destroyed on
    /// every grab/release cycle, so a pool here would strand the live casings in the scene (full
    /// rationale in <see cref="CasingPool"/>). This component only answers "from where, with how
    /// much force".</para>
    /// </summary>
    [RequireComponent(typeof(Weapon))]
    public class ShellEjector : MonoBehaviour
    {
        // ⚠️ The field names "casingPrefab" and "ejectPoint" are bound BY NAME by WeaponKitBuilder
        // (BindFields) — renaming them makes the tool bind nothing silently and no casing is ever
        // ejected.
        [Tooltip("Kovan prefabı (Rigidbody + Collider taşır).")]
        [SerializeField] private GameObject casingPrefab;

        [Tooltip("Kovanın fırlatılacağı nokta (namlu değil, gövdenin yan/kapak tarafı).")]
        [SerializeField] private Transform ejectPoint;

        [SerializeField] private float ejectForceMin = 1.2f;
        [SerializeField] private float ejectForceMax = 2.0f;
        [SerializeField] private float ejectTorque = 6f;

        private Weapon weapon;

        private void Awake()
        {
            weapon = GetComponent<Weapon>();
        }

        private void OnEnable()
        {
            if (weapon != null)
                weapon.Fired += HandleFired;
        }

        private void OnDisable()
        {
            if (weapon != null)
                weapon.Fired -= HandleFired;
        }

        private void HandleFired()
        {
            if (casingPrefab == null || ejectPoint == null)
                return;

            CasingPool.Shared.Eject(casingPrefab, ejectPoint, ejectForceMin, ejectForceMax, ejectTorque);
        }
    }
}
