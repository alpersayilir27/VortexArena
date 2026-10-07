using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;

namespace VortexArena.Core.Player
{
    /// <summary>The local player's hand gloves: the mode's glove skin and the strap band's team colour.</summary>
    /// <remarks>
    /// Only the player sees these hands (others see the remote avatar), so neither gives opponents anything.
    /// The skin follows the same mode as the bodies (<see cref="RemoteAvatar.ResolveBodyMode"/>); a mode
    /// without <see cref="ModeDefinition.GloveSkin"/> keeps the rig's own (tactical) glove. The band is material
    /// slot 1 of the worn look: teamless modes keep it as is, team modes get a tinted copy (<see cref="Girdap"/>).
    /// </remarks>
    public sealed class LocalGloves : MonoBehaviour
    {
        private const int BandSlot = 1;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Sol eldiven: OVRHandVisualLeft'in OpenXR dalındaki SkinnedMeshRenderer. Prefabdaki mesh ve " +
                 "materyaller varsayılan (taktik) eldivendir.")]
        [SerializeField] private SkinnedMeshRenderer leftGlove;

        [Tooltip("Sağ eldiven: OVRHandVisualRight'ın OpenXR dalındaki SkinnedMeshRenderer.")]
        [SerializeField] private SkinnedMeshRenderer rightGlove;

        private readonly Dictionary<(Material band, Team team), Material> _tinted = new Dictionary<(Material, Team), Material>();
        private Look _defaultLeft;
        private Look _defaultRight;
        private bool _dressed;
        private GloveSkin _skin;
        private Material _leftBand;  // slot 1 of the worn look before tinting; null = no band
        private Material _rightBand;
        private bool _painted;
        private Team _paintedTeam;

        private struct Look
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Bounds Bounds;
        }

        private void Awake()
        {
            _defaultLeft = Capture(leftGlove);
            _defaultRight = Capture(rightGlove);
        }

        private void OnEnable()
        {
            PlayerCombatState.LocalTeamChanged += HandleLocalTeamChanged;
            ModeRuntime.Changed += Apply;
            ModeSelection.Changed += Apply;
            Apply();
        }

        private void OnDisable()
        {
            PlayerCombatState.LocalTeamChanged -= HandleLocalTeamChanged;
            ModeRuntime.Changed -= Apply;
            ModeSelection.Changed -= Apply;
        }

        private void OnDestroy()
        {
            foreach (var mat in _tinted.Values)
            {
                Destroy(mat);
            }

            _tinted.Clear();
        }

        private void HandleLocalTeamChanged(Team _) => Apply();

        private void Apply()
        {
            Dress();
            PaintBand();
        }

        private void Dress()
        {
            ModeDefinition mode = RemoteAvatar.ResolveBodyMode(out _);
            GloveSkin skin = mode != null ? mode.GloveSkin : null;
            if (_dressed && skin == _skin)
            {
                return;
            }

            _dressed = true;
            _skin = skin;
            _painted = false; // fresh material arrays: the band has to be painted again
            _leftBand = Wear(leftGlove, _defaultLeft, skin != null ? skin.Left : null, skin);
            _rightBand = Wear(rightGlove, _defaultRight, skin != null ? skin.Right : null, skin);
        }

        private void PaintBand()
        {
            var team = ModeRuntime.IsTeamless ? Team.Neutral : ArenaCombat.LocalTeam;
            if (_painted && team == _paintedTeam)
            {
                return;
            }

            _painted = true;
            _paintedTeam = team;
            Paint(leftGlove, _leftBand, team);
            Paint(rightGlove, _rightBand, team);
        }

        private void Paint(SkinnedMeshRenderer glove, Material band, Team team)
        {
            if (glove == null || band == null)
            {
                return;
            }

            var mats = glove.sharedMaterials;
            mats[BandSlot] = team == Team.Red || team == Team.Blue ? Tinted(band, team) : band;
            glove.sharedMaterials = mats;
        }

        private static Look Capture(SkinnedMeshRenderer glove) => glove == null
            ? default
            : new Look { Mesh = glove.sharedMesh, Materials = glove.sharedMaterials, Bounds = glove.localBounds };

        // A skin without this hand's mesh (or without materials) leaves the hand on its default look.
        // Returns the look's band material (slot 1), null when it has none.
        private static Material Wear(SkinnedMeshRenderer glove, Look fallback, Mesh mesh, GloveSkin skin)
        {
            if (glove == null)
            {
                return null;
            }

            bool own = mesh != null && skin.Materials.Length > 0;
            var mats = own ? skin.Materials : fallback.Materials;
            glove.sharedMesh = own ? mesh : fallback.Mesh;
            glove.sharedMaterials = mats;
            glove.localBounds = own ? Grow(fallback.Bounds, glove, mesh) : fallback.Bounds;
            return mats.Length > BandSlot ? mats[BandSlot] : null;
        }

        // localBounds lives in root bone space: a longer skin (sleeve) would otherwise be culled while still on screen.
        private static Bounds Grow(Bounds bounds, SkinnedMeshRenderer glove, Mesh mesh)
        {
            int root = System.Array.IndexOf(glove.bones, glove.rootBone);
            var bindposes = mesh.bindposes;
            if (root < 0 || root >= bindposes.Length)
            {
                return bounds;
            }

            Bounds mb = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                bounds.Encapsulate(bindposes[root].MultiplyPoint3x4(corner));
            }

            return bounds;
        }

        // Runtime copies keep the SRP Batcher path; a property block would drop it.
        private Material Tinted(Material band, Team team)
        {
            if (!_tinted.TryGetValue((band, team), out var mat))
            {
                mat = new Material(band) { name = band.name + "_" + team };
                mat.SetColor(BaseColorId, team == Team.Red ? Girdap.Red : Girdap.Blue);
                _tinted[(band, team)] = mat;
            }

            return mat;
        }
    }
}
