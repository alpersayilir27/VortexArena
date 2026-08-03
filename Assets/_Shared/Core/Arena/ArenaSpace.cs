using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Dünya↔arena dönüşümü için statik yardımcı. Ağa giden/gelen tüm pozlar ARENA
    /// UZAYINDADIR ve arena uzayı <b>dünya uzayıyla çakışıktır</b> (origin 0,0,0, identity
    /// rotation) — bu yüzden buradaki dönüşümlerin hepsi kimliktir.
    /// <para>
    /// ⚠️ Bunun sahne tarafındaki bağlayıcı sonucu: <b>arena geometrisi dünya orijinine göre
    /// kurulur</b> — arenanın zemini dünya y=0'da, arena merkezi dünya (0,0,0) civarında
    /// olmalıdır. Sahneyi topluca kaydırmak ya da döndürmek tüm oyuncuların ağ koordinatını
    /// kaydırır; telde bunu telafi eden bir origin YOKTUR.
    /// </para>
    /// <para>
    /// Dönüşüm kimlik olduğu hâlde sınıf duruyor, çünkü çağrı yerleri
    /// (<c>PlayerPoseTracker</c>, <c>RemoteAvatar</c>, <c>ArenaCombat</c>,
    /// <c>AdminSpectatorCamera</c>, <c>AdminPlayerMarkers</c>, <c>ProximityWarning</c>,
    /// <c>ArenaNetCharacterBehaviour</c>, <c>RemoteShotFx</c>) hangi değerin hangi uzayda
    /// olduğunu okurken görsün — ve dönüşüm bir gün geri gelirse tek yerden gelsin.
    /// </para>
    /// </summary>
    public static class ArenaSpace
    {
        /// <summary>Dünya pozisyonunu arena uzayına çevirir (uzaylar çakışık → kimlik).</summary>
        public static Vector3 WorldToArena(Vector3 worldPosition)
        {
            return worldPosition;
        }

        /// <summary>Dünya rotasyonunu arena uzayına çevirir (uzaylar çakışık → kimlik).</summary>
        public static Quaternion WorldToArena(Quaternion worldRotation)
        {
            return worldRotation;
        }

        /// <summary>Arena pozisyonunu dünya uzayına çevirir (uzaylar çakışık → kimlik).</summary>
        public static Vector3 ArenaToWorld(Vector3 arenaPosition)
        {
            return arenaPosition;
        }

        /// <summary>Arena rotasyonunu dünya uzayına çevirir (uzaylar çakışık → kimlik).</summary>
        public static Quaternion ArenaToWorld(Quaternion arenaRotation)
        {
            return arenaRotation;
        }

        /// <summary>
        /// Bir <b>YÖNÜ</b> dünya→arena çevirir.
        /// <para>
        /// ⚠️ Dönüşüm kimlik olduğu hâlde bu kapı ayrı duruyor, çünkü işi dönüşüm değil
        /// <b>normalizasyon sözleşmesi</b>: protokolde §6.4 her olay bir <b>birim</b> yön taşır,
        /// "yön yok" diye bir değer yoktur. Yön gönderen hiçbir çağıran bu sözleşmeyi elle
        /// yazmasın (ağa vuruş/atış bildiren tek kapı <c>ArenaCombat</c> de bunu kullanır).
        /// </para>
        /// <para>
        /// Normalize edilemeyen girdide (sıfır vektör, NaN) <see cref="Vector3.forward"/> döner.
        /// </para>
        /// </summary>
        public static Vector3 WorldToArenaDirection(Vector3 worldDirection)
        {
            // Vector3.normalized sıfır/NaN girdide SIFIR döner (Unity sözleşmesi) — ayrı bir
            // IsNaN kontrolü gerekmiyor, tek eşik ikisini birden yakalar.
            Vector3 unit = worldDirection.normalized;
            return unit.sqrMagnitude < 0.5f ? Vector3.forward : unit;
        }

        /// <summary>Dünya pozunu arena uzayına çevirir (uzaylar çakışık → kimlik).</summary>
        public static Pose WorldToArena(in Pose worldPose)
        {
            return worldPose;
        }

        /// <summary>Arena pozunu dünya uzayına çevirir (uzaylar çakışık → kimlik).</summary>
        public static Pose ArenaToWorld(in Pose arenaPose)
        {
            return arenaPose;
        }
    }
}
