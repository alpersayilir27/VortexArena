using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;

namespace VortexArena.Core.Player
{
    /// <summary>Paints the strap band of the local hand gloves in the local player's team colour.</summary>
    /// <remarks>
    /// Only the player sees these hands (others see the remote avatar), so the colour gives opponents
    /// nothing. The band is submesh 1 of the glove mesh, i.e. material slot 1; teamless modes keep
    /// <see cref="neutralBand"/>. Team colours come from <see cref="Girdap"/>, never from here.
    /// </remarks>
    public sealed class GloveTeamBand : MonoBehaviour
    {
        private const int BandSlot = 1;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Eldiven SkinnedMeshRenderer'ları (OVRHandVisual'ların OpenXR dalı, sol + sağ). Alt-mesh 1 bileklik kayışıdır.")]
        [SerializeField] private SkinnedMeshRenderer[] gloves;

        [Tooltip("Takımsız modda takılı kalan bileklik materyali; takım renkli kopyalar bundan üretilir.")]
        [SerializeField] private Material neutralBand;

        private Material _red;
        private Material _blue;
        private bool _painted;
        private Team _paintedTeam;

        private void OnEnable()
        {
            PlayerCombatState.LocalTeamChanged += HandleLocalTeamChanged;
            ModeRuntime.Changed += Apply;
            Apply();
        }

        private void OnDisable()
        {
            PlayerCombatState.LocalTeamChanged -= HandleLocalTeamChanged;
            ModeRuntime.Changed -= Apply;
        }

        private void OnDestroy()
        {
            if (_red != null)
            {
                Destroy(_red);
            }

            if (_blue != null)
            {
                Destroy(_blue);
            }
        }

        private void HandleLocalTeamChanged(Team _) => Apply();

        private void Apply()
        {
            if (neutralBand == null)
            {
                return;
            }

            var team = ModeRuntime.IsTeamless ? Team.Neutral : ArenaCombat.LocalTeam;
            if (_painted && team == _paintedTeam)
            {
                return;
            }

            _painted = true;
            _paintedTeam = team;
            Material band = team switch
            {
                Team.Red => _red != null ? _red : _red = Tinted(team, Girdap.Red),
                Team.Blue => _blue != null ? _blue : _blue = Tinted(team, Girdap.Blue),
                _ => neutralBand,
            };

            foreach (var glove in gloves)
            {
                if (glove == null)
                {
                    continue;
                }

                var mats = glove.sharedMaterials;
                if (mats.Length <= BandSlot)
                {
                    continue;
                }

                mats[BandSlot] = band;
                glove.sharedMaterials = mats;
            }
        }

        // Runtime copies keep the SRP Batcher path; a property block would drop it.
        private Material Tinted(Team team, Color color)
        {
            var mat = new Material(neutralBand) { name = neutralBand.name + "_" + team };
            mat.SetColor(BaseColorId, color);
            return mat;
        }
    }
}
