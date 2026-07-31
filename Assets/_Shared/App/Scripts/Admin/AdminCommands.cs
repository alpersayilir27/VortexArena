using System;
using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Admin komutlarının tek çıkış kapısı (§5.2). Otorite SUNUCUDADIR: buradan yalnız istek
    /// gider, kabul/ret kararını sunucu verir ve sebebini kendi konsoluna yazar — arayüz
    /// "gönderildi" der, "oldu" demez.
    /// <para>
    /// <see cref="Status"/> son işlemin insan okuyabilir sonucudur; tercihler paneli onu gösterir.
    /// </para>
    /// </summary>
    public static class AdminCommands
    {
        /// <summary>Son komutun/denemenin sonucu (arayüzde gösterilir).</summary>
        public static string Status { get; private set; } = "";

        /// <summary>Durum metni değiştiğinde.</summary>
        public static event Action StatusChanged;

        /// <summary>
        /// Maçı başlatır. <paramref name="roundSeconds"/>/<paramref name="scoreLimit"/> o maça
        /// özeldir; <c>0</c> gönderilirse sunucu modun varsayılanını kullanır (§5.2) — yani
        /// operatör bir şey seçmediyse davranış bugünküyle birebir aynıdır.
        /// </summary>
        public static void StartMatch(string modeId, string sceneName, int roundSeconds = 0,
            int scoreLimit = 0, int countdownSeconds = 0)
        {
            if (string.IsNullOrEmpty(modeId) || string.IsNullOrEmpty(sceneName))
            {
                SetStatus("Mod/harita seçilmedi; maç başlatılamadı.");
                return;
            }

            var msg = new StartMatchMsg
            {
                modeId = modeId,
                sceneName = sceneName,
                roundSeconds = Mathf.Max(0, roundSeconds),
                scoreLimit = Mathf.Max(0, scoreLimit),
                countdownSeconds = Mathf.Max(0, countdownSeconds)
            };

            if (Send(msg))
            {
                string parameters = msg.roundSeconds > 0 || msg.scoreLimit > 0 || msg.countdownSeconds > 0
                    ? $" ({(msg.roundSeconds > 0 ? FormatDuration(msg.roundSeconds) : "mod süresi")}" +
                      $" · {(msg.scoreLimit > 0 ? "limit " + msg.scoreLimit : "mod limiti")}" +
                      $"{(msg.countdownSeconds > 0 ? " · geri sayım " + msg.countdownSeconds + " sn" : "")})"
                    : "";
                SetStatus($"Maç isteği gönderildi: {modeId} · {sceneName}{parameters}");
            }
        }

        /// <summary>Saniyeyi operatörün okuduğu biçime çevirir ("2.5 dk", "1 saat").</summary>
        public static string FormatDuration(int seconds)
        {
            if (seconds <= 0)
            {
                return "-";
            }

            if (seconds % 3600 == 0)
            {
                return $"{seconds / 3600} saat";
            }

            float minutes = seconds / 60f;
            return Mathf.Approximately(minutes, Mathf.Round(minutes))
                ? $"{Mathf.RoundToInt(minutes)} dk"
                : $"{minutes:0.#} dk";
        }

        /// <summary>
        /// Bir sonraki maçın <b>ortak</b> mod/harita seçimi (§5.2 <c>set_selection</c>). Maçı
        /// başlatmaz: sunucudaki seçimi değiştirir, sunucu da onu <c>admin_state</c> ile tüm
        /// adminlere yayar. Bu yüzden arayüz seçimi yerel bir alana YAZMAZ — sunucudan geri
        /// gelen değeri gösterir (tek doğruluk kaynağı, iki operatör sapmaz).
        /// <para>Durum satırı burada yazılmaz; sunucunun yayınladığı duyuru zaten gelecek.</para>
        /// <para>Süre/limit de bu kanaldan gider: parametreler yerel kalsaydı bir operatörün
        /// 5 dk sandığı maç diğerinin seçtiği 30 dk ile başlardı. <c>0</c> = "bu alanı değiştirme".</para>
        /// </summary>
        public static void SetSelection(string modeId, string sceneName, int roundSeconds = 0,
            int scoreLimit = 0, int countdownSeconds = 0)
        {
            if (string.IsNullOrEmpty(modeId) && string.IsNullOrEmpty(sceneName) &&
                roundSeconds <= 0 && scoreLimit <= 0 && countdownSeconds <= 0)
            {
                return;
            }

            Send(new SetSelectionMsg
            {
                modeId = modeId ?? "",
                sceneName = sceneName ?? "",
                roundSeconds = Mathf.Max(0, roundSeconds),
                scoreLimit = Mathf.Max(0, scoreLimit),
                countdownSeconds = Mathf.Max(0, countdownSeconds)
            });
        }

        public static void AbortMatch()
        {
            if (Send(new AbortMatchMsg()))
            {
                SetStatus("Maç iptali gönderildi.");
            }
        }

        /// <summary>Koşan maçı dondurur (§5.2). Sunucu yalnız <c>playing</c> iken uygular;
        /// başka fazda komut sessizce düşer, durum değişmez.</summary>
        public static void PauseMatch()
        {
            if (Send(new PauseMatchMsg()))
            {
                SetStatus("Duraklatma gönderildi.");
            }
        }

        /// <summary>Operatörün duraklattığı maçı sürdürür (§5.2). Modun ya da geri sayımın
        /// duraklamasını KALDIRMAZ — sunucu onu reddeder.</summary>
        public static void ResumeMatch()
        {
            if (Send(new ResumeMatchMsg()))
            {
                SetStatus("Devam isteği gönderildi.");
            }
        }

        public static void ReturnToLobby()
        {
            if (Send(new ReturnToLobbyMsg()))
            {
                SetStatus("Lobiye dönme isteği gönderildi.");
            }
        }

        public static void SetTeam(int playerId, string team)
        {
            if (playerId <= 0 || (team != "red" && team != "blue"))
            {
                return;
            }

            if (Send(new SetTeamMsg { playerId = playerId, team = team }))
            {
                SetStatus($"Takım değişikliği gönderildi: {playerId} → {team}");
            }
        }

        /// <summary>Oyuncunun adını ve/veya forma numarasını değiştirir (§5.1).
        /// <para>Boş <paramref name="name"/> ve <c>0</c> <paramref name="number"/> ilgili alanı
        /// DEĞİŞTİRMEZ (sunucu konvansiyonu) — "yalnız numarayı düzelt" tek çağrıdır. Numara
        /// çevrimiçi biri tarafından kullanılıyorsa sunucu reddeder ve sebebi admin_state.notice
        /// ile döner; burada iyimser bir yerel güncelleme YAPILMAZ (otorite sunucudadır).</para></summary>
        public static void SetIdentity(int playerId, string name, int number)
        {
            if (playerId <= 0)
            {
                return;
            }

            string trimmed = string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
            if (trimmed.Length == 0 && number == 0)
            {
                return; // her iki alan da "koru" — mesaj yollamaya değmez
            }

            if (number != 0 && (number < ArenaProtocol.PLAYER_NUMBER_MIN || number > ArenaProtocol.PLAYER_NUMBER_MAX))
            {
                SetStatus($"Numara {ArenaProtocol.PLAYER_NUMBER_MIN}-{ArenaProtocol.PLAYER_NUMBER_MAX} aralığında olmalı.");
                return;
            }

            if (Send(new SetIdentityMsg { playerId = playerId, name = trimmed, number = number }))
            {
                string what = trimmed.Length > 0 && number != 0 ? $"{number} · {trimmed}"
                    : trimmed.Length > 0 ? trimmed
                    : $"numara {number}";
                SetStatus($"Kimlik gönderildi: oyuncu {playerId} → {what}");
            }
        }

        public static void Kick(int playerId)
        {
            if (playerId <= 0)
            {
                return;
            }

            if (Send(new KickMsg { playerId = playerId }))
            {
                SetStatus($"Atma isteği gönderildi: oyuncu {playerId}");
            }
        }

        public static void Identify(int playerId)
        {
            if (playerId <= 0)
            {
                return;
            }

            if (Send(new IdentifyMsg { playerId = playerId }))
            {
                SetStatus($"Kimlik gösterimi gönderildi: oyuncu {playerId}");
            }
        }

        /// <summary>
        /// Bir oyuncunun kalibrasyonunu sıfırlar; <paramref name="playerId"/> <b>0 = HERKES</b>
        /// (§10.6). Sıfırlanan oyuncu ateş edemez, hasar yemez, canlanamaz ve diğer oyuncuların
        /// ekranında avatarı parlar — kalibrasyonu geri açmayı YALNIZ başlığın kendisi yapabilir.
        /// </summary>
        public static void ClearCalibration(int playerId)
        {
            if (playerId < 0)
            {
                return;
            }

            if (Send(new ClearCalibrationMsg { playerId = playerId }))
            {
                SetStatus(playerId == 0
                    ? "Tüm kalibrasyonların sıfırlanması gönderildi."
                    : $"Kalibrasyon sıfırlama gönderildi: oyuncu {playerId}");
            }
        }

        /// <summary>Elle yeniden bağlanma (bağlantı kesildikten sonra tek geri dönüş yolu).</summary>
        public static void Reconnect()
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null)
            {
                SetStatus("İstemci hazır değil.");
                return;
            }

            if (!AppSession.HasServerEndpoint)
            {
                SetStatus($"Sunucu adresi yok. Uygulama launcher'dan başlatılmalı ({AppBoot.ArgServerIp} <ip>).");
                return;
            }

            client.Connect(AppSession.ServerIp, AppSession.ServerPort, AppSession.RoleAdmin);
            SetStatus($"Bağlanılıyor: {AppSession.ServerIp}:{AppSession.ServerPort}");
        }

        public static void Disconnect()
        {
            if (ArenaClient.Instance != null)
            {
                ArenaClient.Instance.Disconnect();
                SetStatus("Bağlantı kesildi.");
            }
        }

        /// <summary>
        /// Sunucuya gitmeyen, yalnız arayüzde gösterilecek bilgi (ör. yerel harita önizlemesi).
        /// Komut kanalıyla aynı durum satırını kullanır ki operatör tek yere baksın.
        /// </summary>
        public static void Note(string text)
        {
            SetStatus(text);
        }

        private static bool Send<T>(T msg) where T : class
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                SetStatus("Bağlantı yok; komut gönderilemedi.");
                return false;
            }

            client.Send(msg);
            return true;
        }

        private static void SetStatus(string text)
        {
            Status = text ?? "";
            Debug.Log($"[AdminCommands] {Status}");
            StatusChanged?.Invoke();
        }
    }
}
