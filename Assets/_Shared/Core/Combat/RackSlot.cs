using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bir raf gözü: silahın duracağı yer (<see cref="anchor"/>) ve isteğe bağlı olarak
    /// hangi silahın duracağı (<see cref="weapon"/>).
    /// <para>
    /// <b>Neden ikisi ayrı:</b> silahın raf üzerinde nereye konacağı bir SAHNE/sanat kararıdır
    /// (raf modeline göre değişir), hangi silahın verileceği ise bir MOD kararıdır
    /// (<c>ModeDefinition.loadout</c>). <see cref="weapon"/> boş bırakılırsa göz, modun
    /// loadout'undan sırayla doldurulur — böylece moda silah eklemek sahneyi hiç açmadan işler.
    /// </para>
    /// </summary>
    [Serializable]
    public class RackSlot
    {
        [Tooltip("Silahın konumlanacağı boş Transform. Boşsa göz atlanır.")]
        public Transform anchor;

        [Tooltip("Bu gözde DURACAK silah. Boş bırakılırsa modun loadout'undan sırayla alınır.")]
        public WeaponDefinition weapon;
    }
}
