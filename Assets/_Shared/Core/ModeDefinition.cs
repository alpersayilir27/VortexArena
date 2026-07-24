using System;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;

namespace VortexArena.Core
{
    /// <summary>
    /// Oyun modu tanımı: modId + varsayılan kural parametreleri + uyumlu haritalar.
    /// <para>
    /// <see cref="ModeId"/> protokol anahtarıdır ("tdm") — admin <c>start_match{modeId}</c>
    /// gönderir, sunucu bunu kendi <c>IGameMode</c> kayıtlarıyla eşler. Kural OTORİTESİ
    /// SUNUCUDADIR; buradaki roundSeconds/scoreLimit yalnız arayüz/ön izleme değerleridir.
    /// </para>
    /// <see cref="HudPrefab"/> mod UI prefabıdır (Modes/&lt;Mod&gt;/UI/ altında); Core mod
    /// assembly'lerine referans vermez, bu yüzden alan tipi düz <c>GameObject</c>'tir.
    /// </summary>
    [CreateAssetMenu(fileName = "Mode", menuName = "VortexArena/Mode Definition")]
    public class ModeDefinition : ScriptableObject
    {
        [Header("Kimlik")]
        [Tooltip("Protokol anahtarı — sunucudaki IGameMode.ModeId ile birebir aynı.")]
        [SerializeField] private string modeId = "";
        [SerializeField] private string displayName = "";

        [Header("Varsayılan kurallar (otorite sunucudadır)")]
        [SerializeField] private int roundSeconds = 300;
        [SerializeField] private int scoreLimit = 30;

        [Header("İçerik")]
        [Tooltip("Bu modun oynanabildiği haritalar; boş bırakılırsa katalogdaki tüm uyumlu haritalar.")]
        [SerializeField] private MapDefinition[] maps = Array.Empty<MapDefinition>();
        [Tooltip("Modun silah seti.")]
        [SerializeField] private WeaponDefinition[] loadout = Array.Empty<WeaponDefinition>();
        [Tooltip("Mod HUD prefabı (Modes/<Mod>/UI/); maç sahnesine App tarafından eklenir.")]
        [SerializeField] private GameObject hudPrefab;

        /// <summary>Protokol anahtarı ("tdm").</summary>
        public string ModeId => modeId;

        /// <summary>Arayüzde gösterilen ad.</summary>
        public string DisplayName => displayName;

        /// <summary>Varsayılan raund süresi (saniye).</summary>
        public int RoundSeconds => roundSeconds;

        /// <summary>Varsayılan skor limiti.</summary>
        public int ScoreLimit => scoreLimit;

        /// <summary>Modun oynanabildiği haritalar (boş = katalogdaki tüm uyumlu haritalar).</summary>
        public MapDefinition[] Maps => maps;

        /// <summary>Modun silah seti.</summary>
        public WeaponDefinition[] Loadout => loadout;

        /// <summary>Mod HUD prefabı (atanmamış olabilir).</summary>
        public GameObject HudPrefab => hudPrefab;
    }
}
