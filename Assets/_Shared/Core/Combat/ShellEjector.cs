using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Ateş anında kovan fırlatır: <see cref="Weapon.Fired"/> olayına abone olur ve kovanı
    /// <see cref="CasingPool.Shared"/>'dan ister.
    /// <para>⚠️ Havuz ve süre işleyişi bilerek BU BİLEŞENDE DEĞİL. Silah örneği her kavra/bırak
    /// döngüsünde yok edildiği için havuzu burada tutmak, o an açık olan kovanların sahnede
    /// kalıcılaşması demekti (gerekçenin tamamı <see cref="CasingPool"/> sınıf özetinde). Bu
    /// bileşen yalnız <b>nereden, ne kadar kuvvetle</b> sorusunu cevaplar.</para>
    /// </summary>
    [RequireComponent(typeof(Weapon))]
    public class ShellEjector : MonoBehaviour
    {
        // ⚠️ "casingPrefab" ve "ejectPoint" alan adları WeaponKitBuilder tarafından ADA GÖRE
        // bağlanıyor (BindFields) — yeniden adlandırılırsa araç sessizce boş bağlar ve kovan
        // hiç çıkmaz.
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
