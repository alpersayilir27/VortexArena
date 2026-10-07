using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>Marks the root of a weapon rack board (<c>VA_WeaponCanvas*</c>).</summary>
    /// <remarks>Lets <see cref="WeaponGranter"/>'s sweep hide the BOARD with its weapons; without it
    /// an empty board stays standing in modes that hand out weapons. Carries no logic on purpose.</remarks>
    [DisallowMultipleComponent]
    public class WeaponCanvas : MonoBehaviour
    {
    }
}
