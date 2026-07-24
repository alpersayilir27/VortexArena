using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Sahnedeki arena origin kaydı + dünya↔arena dönüşümü için statik yardımcı.
    /// Ağa giden/gelen tüm pozlar ARENA UZAYINDADIR; bu sınıf sahnedeki tek
    /// origin'e göre çeviriyi yapar. Lobby'de origin yok → dünya=arena (sahne
    /// kökü 0,0,0); arena sahnelerinde origin = ArenaBoundary transformu
    /// (OnEnable'da kendini kaydeder).
    /// </summary>
    public static class ArenaSpace
    {
        private static Transform _origin;

        /// <summary>Sahnede kayıtlı bir arena origin'i var mı.</summary>
        public static bool HasOrigin => _origin != null;

        /// <summary>
        /// Arena origin'ini kaydeder (ArenaBoundary.OnEnable çağırır).
        /// null yok sayılır; farklı bir origin gelirse üzerine yazılır.
        /// </summary>
        public static void SetOrigin(Transform t)
        {
            if (t == null)
            {
                return;
            }

            if (_origin != null && _origin != t)
            {
                Debug.Log($"[ArenaSpace] Arena origin değişti: '{_origin.name}' → '{t.name}'.");
            }

            _origin = t;
        }

        /// <summary>Yalnız kayıtlı origin verilen transform ise temizler (sahne yıkımı güvenliği).</summary>
        public static void ClearOrigin(Transform t)
        {
            if (_origin == t)
            {
                _origin = null;
            }
        }

        /// <summary>Dünya pozisyonunu arena uzayına çevirir (origin yoksa kimlik).</summary>
        public static Vector3 WorldToArena(Vector3 worldPosition)
        {
            return _origin != null ? _origin.InverseTransformPoint(worldPosition) : worldPosition;
        }

        /// <summary>Dünya rotasyonunu arena uzayına çevirir (origin yoksa kimlik).</summary>
        public static Quaternion WorldToArena(Quaternion worldRotation)
        {
            return _origin != null ? Quaternion.Inverse(_origin.rotation) * worldRotation : worldRotation;
        }

        /// <summary>Arena pozisyonunu dünya uzayına çevirir (origin yoksa kimlik).</summary>
        public static Vector3 ArenaToWorld(Vector3 arenaPosition)
        {
            return _origin != null ? _origin.TransformPoint(arenaPosition) : arenaPosition;
        }

        /// <summary>Arena rotasyonunu dünya uzayına çevirir (origin yoksa kimlik).</summary>
        public static Quaternion ArenaToWorld(Quaternion arenaRotation)
        {
            return _origin != null ? _origin.rotation * arenaRotation : arenaRotation;
        }

        /// <summary>Dünya pozunu arena uzayına çevirir (origin yoksa kimlik).</summary>
        public static Pose WorldToArena(in Pose worldPose)
        {
            return new Pose(WorldToArena(worldPose.position), WorldToArena(worldPose.rotation));
        }

        /// <summary>Arena pozunu dünya uzayına çevirir (origin yoksa kimlik).</summary>
        public static Pose ArenaToWorld(in Pose arenaPose)
        {
            return new Pose(ArenaToWorld(arenaPose.position), ArenaToWorld(arenaPose.rotation));
        }
    }
}
