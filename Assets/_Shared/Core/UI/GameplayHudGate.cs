using System;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// Oyun içi HUD'ların (mod HUD'ı ve cephane göstergesi) <b>tek görünürlük anahtarı</b>.
    /// Anahtar kapalıyken bu HUD'lar kendilerini çizmez; ekranı maç sonu ekranı devralmıştır.
    /// <para>
    /// <b>Tek yazarı <c>MatchResultOverlay</c>'dir</b> (App). Başka hiçbir yerden set edilmez —
    /// ikinci bir yazar, "kim kapattı, kim açacak" sorusunu belirsiz bırakır ve HUD bir gün
    /// sessizce kapalı kalır.
    /// </para>
    /// <para>
    /// <b>Neden faz değil de anahtar:</b> HUD'lar "faz <c>finished</c> mı" diye kendileri
    /// baksaydı, maç sonu ekranı herhangi bir sebeple çizilmediğinde (prefab bulunamadı, rol
    /// admin) oyuncu maç sonunda HİÇBİR şey görmezdi. Anahtarı ekranın kendisi çevirdiği için
    /// HUD yalnız gerçekten yerine bir şey konduğunda gizlenir.
    /// </para>
    /// <para>
    /// Durum ve olay <b>statiktir</b> (<c>ModeRuntime</c> deseni): dinleyiciler yazarın ne zaman
    /// doğduğunu bilmek zorunda kalmasın. Yazar uyanırken anahtarı açık duruma çeker, yani
    /// domain reload'suz Play girişinden kalan bayat bir <c>true</c> taşınmaz.
    /// </para>
    /// </summary>
    public static class GameplayHudGate
    {
        /// <summary>Oyun içi HUD'lar gizli mi.</summary>
        public static bool Hidden { get; private set; }

        /// <summary>Yalnız DEĞER değiştiğinde tetiklenir (ana thread).</summary>
        public static event Action<bool> HiddenChanged;

        /// <summary>Anahtarı çevirir. Yazarı için bkz. sınıf dokümanı.</summary>
        public static void SetHidden(bool hidden)
        {
            if (Hidden == hidden)
            {
                return;
            }

            Hidden = hidden;
            HiddenChanged?.Invoke(hidden);
        }
    }
}
