using System;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>Mode-specific look of the player's own hands: one glove mesh per hand plus its materials.</summary>
    /// <remarks>
    /// The meshes must carry the rig hand's bone order and bindposes, so they come from the glove importer, never
    /// from an FBX. Material slot 1, when present, is the team-tinted strap band (<see cref="LocalGloves"/>).
    /// </remarks>
    [CreateAssetMenu(fileName = "GloveSkin", menuName = "VortexArena/Glove Skin")]
    public sealed class GloveSkin : ScriptableObject
    {
        [Tooltip("Sol el mesh'i — Tools > VortexArena > Avatars > Eldiven Mesh'ini İçe Aktar üretir (kemik sırası " +
                 "ve bindpose'lar rig'in OpenXR elininkidir). FBX'ten gelen mesh atanmaz: el parçalanmış çizilir.")]
        [SerializeField] private Mesh left;

        [Tooltip("Sağ el mesh'i — sol elle aynı kural.")]
        [SerializeField] private Mesh right;

        [Tooltip("Alt-mesh sırasıyla materyaller. Slot 1 varsa takım rengiyle boyanan bileklik kayışıdır.")]
        [SerializeField] private Material[] materials = Array.Empty<Material>();

        /// <summary>Left hand glove mesh.</summary>
        public Mesh Left => left;

        /// <summary>Right hand glove mesh.</summary>
        public Mesh Right => right;

        /// <summary>Materials in submesh order; slot 1 is the team band when present.</summary>
        public Material[] Materials => materials;
    }
}
