using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>Explicit surface override for one object: what a round leaves here is decided by this
    /// component, not by the material.
    /// <para>Exists for the cases the material lookup cannot serve: a multi-material mesh, or an
    /// object that shares a material with others but must sound different. It is the EXCEPTION —
    /// mapping the material in <see cref="SurfaceLibrary"/> covers whole arenas at once, while this
    /// covers one object and has to be remembered.</para>
    /// <para>Searched UPWARDS from the collider (same shape as <c>RemoteHitBox</c>), so it may sit on
    /// a parent while the collider lives on a child.</para>
    /// <para>No runtime behaviour: it is read only when a round lands.</para></summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceTag : MonoBehaviour
    {
        [Tooltip("Bu objeye (ve altındaki collider'lara) uygulanacak yüzey. Boşsa override yok — " +
                 "çözüm materyale düşer.")]
        [SerializeField] private SurfaceDefinition surface;

        public SurfaceDefinition Surface => surface;
    }
}
