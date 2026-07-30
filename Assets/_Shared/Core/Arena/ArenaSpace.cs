using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Sahnedeki arena origin kaydı + dünya↔arena dönüşümü için statik yardımcı.
    /// Ağa giden/gelen tüm pozlar ARENA UZAYINDADIR; bu sınıf sahnedeki tek
    /// origin'e göre çeviriyi yapar. Lobby'de origin yok → dünya=arena (sahne
    /// kökü 0,0,0); arena sahnelerinde origin = <see cref="SpawnPoint"/> transformu
    /// (OnEnable'da kendini kaydeder).
    /// <para>
    /// ⚠️ Origin <b>muhafazadan (<see cref="ArenaBoundary"/>) bağımsızdır</b>: muhafaza duvarın
    /// nerede olduğunu, origin ağ koordinatlarının sıfırını söyler. İkisi aynı objede olduğu
    /// sürece duvarı kaydırmak tüm oyuncuların ağ konumunu da kaydırıyordu.
    /// </para>
    /// <para>
    /// Origin YOKKEN dönüşümler <b>kimlik</b>dir (dünya = arena) — bu bilinçli bir seçimdir,
    /// lobide origin olmaması normaldir. Ama arena sahnesinde origin unutulursa aynı sessiz
    /// kimlik dönüşümü "her şey çalışıyor ama koordinatlar kaymış" gibi görünür; bu yüzden ilk
    /// kullanımda sahne başına <b>bir kez</b> uyarı basılır.
    /// </para>
    /// </summary>
    public static class ArenaSpace
    {
        private static Transform _origin;

        // Sahne başına tek uyarı: dönüşümler kare başına onlarca kez çağrılıyor, log'u boğmasın.
        private static bool _missingOriginWarned;
        private static bool _sceneHookInstalled;

        /// <summary>Sahnede kayıtlı bir arena origin'i var mı.</summary>
        public static bool HasOrigin => _origin != null;

        /// <summary>
        /// Arena origin'ini kaydeder (<see cref="SpawnPoint"/>.OnEnable çağırır).
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
            if (_origin == null)
            {
                WarnMissingOrigin();
                return worldPosition;
            }

            return _origin.InverseTransformPoint(worldPosition);
        }

        /// <summary>Dünya rotasyonunu arena uzayına çevirir (origin yoksa kimlik).</summary>
        public static Quaternion WorldToArena(Quaternion worldRotation)
        {
            if (_origin == null)
            {
                WarnMissingOrigin();
                return worldRotation;
            }

            return Quaternion.Inverse(_origin.rotation) * worldRotation;
        }

        /// <summary>Arena pozisyonunu dünya uzayına çevirir (origin yoksa kimlik).</summary>
        public static Vector3 ArenaToWorld(Vector3 arenaPosition)
        {
            if (_origin == null)
            {
                WarnMissingOrigin();
                return arenaPosition;
            }

            return _origin.TransformPoint(arenaPosition);
        }

        /// <summary>Arena rotasyonunu dünya uzayına çevirir (origin yoksa kimlik).</summary>
        public static Quaternion ArenaToWorld(Quaternion arenaRotation)
        {
            if (_origin == null)
            {
                WarnMissingOrigin();
                return arenaRotation;
            }

            return _origin.rotation * arenaRotation;
        }

        /// <summary>
        /// Bir <b>YÖNÜ</b> dünya→arena çevirir (öteleme düşer, yalnız dönüş kalır).
        /// <para>
        /// ⚠️ <b>YÖN BİR NOKTA DEĞİLDİR:</b> <see cref="WorldToArena(Vector3)"/>'yı bir yöne
        /// uygulamak onu arena origin'i kadar KAYDIRIR — sonuç patlamaz, sessizce yanlış bir nişan
        /// yönü olur. Bu yardımcı o tuzağı tek bir yerde kapatır; yön gönderen hiçbir çağıran farkı
        /// elle yazmasın (ağa vuruş/atış bildiren tek kapı <c>ArenaCombat</c> de bunu kullanır).
        /// </para>
        /// <para>
        /// Uygulama iki noktayı ayrı ayrı çevirip farkını alır. Referans nokta
        /// <see cref="Vector3.zero"/> seçildi ama <b>değeri fark etmez</b>: dönüşüm rijittir
        /// (dönüş + öteleme), farkı almak ötelemeyi hangi noktada olursan ol aynı şekilde düşürür.
        /// </para>
        /// <para>
        /// Normalize edilemeyen girdide (sıfır vektör, NaN) <see cref="Vector3.forward"/> döner:
        /// çağıranı sıfır vektörle beslemek telde "yön yok" gibi bir şey üretirdi, oysa protokolde
        /// öyle bir değer yok (§6.4 her olayda bir birim yön taşır).
        /// </para>
        /// </summary>
        public static Vector3 WorldToArenaDirection(Vector3 worldDirection)
        {
            // Vector3.normalized sıfır/NaN girdide SIFIR döner (Unity sözleşmesi) — ayrı bir
            // IsNaN kontrolü gerekmiyor, tek eşik ikisini birden yakalar.
            Vector3 unit = worldDirection.normalized;
            if (unit.sqrMagnitude < 0.5f)
            {
                return Vector3.forward;
            }

            Vector3 arena = (WorldToArena(unit) - WorldToArena(Vector3.zero)).normalized;
            return arena.sqrMagnitude < 0.5f ? Vector3.forward : arena;
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

        // ------------------------------------------------------------------ uyarı

        /// <summary>
        /// Origin yokken yapılan ilk dönüşümde bir kez uyarır. Dönüşüm kare başına onlarca kez
        /// çağrıldığı için bayrak şart; bayrak sahne değişiminde sıfırlanır ki yeni sahnedeki
        /// eksiklik de görünsün.
        /// </summary>
        private static void WarnMissingOrigin()
        {
            if (_missingOriginWarned)
            {
                return;
            }

            _missingOriginWarned = true;
            EnsureSceneHook();

            Debug.LogWarning(
                "[ArenaSpace] Arena origin'i yok — dünya uzayı arena uzayı sayılıyor (kimlik " +
                "dönüşümü). Lobide bu NORMALDİR; bir arena sahnesindeysen sahnede SpawnPoint " +
                "eksik ya da kapalı olabilir (GameObject > VortexArena > Spawn Point).");
        }

        private static void EnsureSceneHook()
        {
            if (_sceneHookInstalled)
            {
                return;
            }

            _sceneHookInstalled = true;
            SceneManager.activeSceneChanged += (_, _) => _missingOriginWarned = false;
        }
    }
}
