using System;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Bir arenanın <b>2B planı</b>: zemin sınırı (sıralı köşe listesi) + içindeki kolonlar.
    /// Dikdörtgen olmayan (yamuk, L, kırık duvarlı) işletme alanları için tek veri kaynağıdır.
    /// <para>
    /// <b>Aynı asset üç yeri birden besler</b> — plan tek yerde durur, üçü sapamaz:
    /// (1) editör aracı zemini + kolonları geometri olarak üretir,
    /// (2) <see cref="ArenaBoundary"/> muhafaza mesafesini bu çokgenden hesaplar
    ///     (dikdörtgen hızlı yolu asset boşken devrede kalır),
    /// (3) admin kuş bakışı kadrajı çokgenin sınırlayıcı kutusunu okur.
    /// </para>
    /// <para>
    /// ⚠️ <b>Koordinat sistemi:</b> tüm noktalar metre cinsinden ve <see cref="ArenaBoundary"/>'yi
    /// taşıyan transformun YEREL XZ düzlemindedir (X = sağ, Y alanı = Z = ileri). Ölçüyü bir
    /// köşeden alıyorsan o köşe (0,0) olur; plan sıfırının arena origin'i olması GEREKMEZ —
    /// ağ origin'i ayrı bir objededir (<see cref="SpawnPoint"/>).
    /// </para>
    /// <para>
    /// ⚠️ Sınır <b>kapalı</b> kabul edilir: son köşe ilk köşeye kendiliğinden bağlanır, aynı
    /// noktayı sona tekrar yazma. Köşeler saat yönü ya da tersi olabilir — hesap yöne duyarsızdır.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "Shape", menuName = "VortexArena/Arena Shape Definition")]
    public class ArenaShapeDefinition : ScriptableObject
    {
        /// <summary>Geçerli bir çokgen için gereken en az köşe sayısı.</summary>
        public const int MinOutlinePoints = 3;

        /// <summary>
        /// Arena içindeki bir kolon/engel. Kutu olarak çizilir ve (istenirse) muhafaza
        /// hesabına engel olarak girer — oyuncu gerçek kolona yürümeden uyarı alsın diye.
        /// </summary>
        [Serializable]
        public struct Column
        {
            [Tooltip("Üretilen objenin adı. Boşsa 'Kolon_<sıra>' kullanılır.")]
            public string name;

            [Tooltip("Kolon merkezi (metre, arena yerel XZ).")]
            public Vector2 center;

            [Tooltip("Kolon ölçüsü: X = genişlik, Y = derinlik (metre).")]
            public Vector2 size;

            [Tooltip("Y ekseni etrafında dönüş (derece). Duvara paralel olmayan kolonlar için.")]
            public float yaw;

            [Tooltip("Yükseklik (metre). 0 bırakılırsa asset'teki varsayılan yükseklik kullanılır.")]
            public float height;
        }

        [Header("Zemin sınırı")]
        [Tooltip("Sıralı köşeler (metre, arena yerel XZ). Kapalı kabul edilir — ilk noktayı sona tekrarlama.")]
        [SerializeField] private Vector2[] outline = Array.Empty<Vector2>();

        [Tooltip("Duvar yüksekliği (metre) — üretilen geometri için.")]
        [SerializeField] private float wallHeight = 3f;

        [Header("Kolonlar")]
        [Tooltip("Arena içindeki kolonlar/engeller. Boş bırakılabilir.")]
        [SerializeField] private Column[] columns = Array.Empty<Column>();

        [Tooltip("Kolonun 'height' alanı 0 bırakılırsa kullanılacak yükseklik (metre).")]
        [SerializeField] private float defaultColumnHeight = 3f;

        [Header("Muhafaza")]
        [Tooltip("Kolonlar da muhafaza hesabına girsin mi (yaklaşınca uyarı). Kapalıysa kolonlar " +
                 "yalnız geometridir.")]
        [SerializeField] private bool columnsBlockPlayer = true;

        /// <summary>Sıralı sınır köşeleri (arena yerel XZ, kapalı çokgen).</summary>
        public Vector2[] Outline => outline;

        /// <summary>Üretilen duvarların yüksekliği (metre).</summary>
        public float WallHeight => wallHeight;

        /// <summary>Arena içindeki kolonlar.</summary>
        public Column[] Columns => columns;

        /// <summary>Kolonun kendi yüksekliği 0 ise kullanılacak değer.</summary>
        public float DefaultColumnHeight => defaultColumnHeight;

        /// <summary>Kolonlar muhafaza hesabına engel olarak giriyor mu.</summary>
        public bool ColumnsBlockPlayer => columnsBlockPlayer;

        /// <summary>Kullanılabilir bir plan mı (en az bir üçgen).</summary>
        public bool IsValid => outline != null && outline.Length >= MinOutlinePoints;

        /// <summary>Bir kolonun etkin yüksekliği (kendi değeri 0 ise varsayılan).</summary>
        public float HeightOf(in Column column)
        {
            return column.height > 0f ? column.height : defaultColumnHeight;
        }

        /// <summary>
        /// Sınır çokgeninin XZ sınırlayıcı kutusu (arena yerel). Admin kuş bakışı kadrajı ve
        /// <see cref="ArenaBoundary.HalfExtents"/> bunu okur. Plan geçersizse sıfır kutu döner.
        /// </summary>
        public Rect LocalBounds()
        {
            if (!IsValid)
            {
                return new Rect(0f, 0f, 0f, 0f);
            }

            float minX = outline[0].x, maxX = outline[0].x;
            float minZ = outline[0].y, maxZ = outline[0].y;
            for (int i = 1; i < outline.Length; i++)
            {
                minX = Mathf.Min(minX, outline[i].x);
                maxX = Mathf.Max(maxX, outline[i].x);
                minZ = Mathf.Min(minZ, outline[i].y);
                maxZ = Mathf.Max(maxZ, outline[i].y);
            }

            return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        }
    }
}
