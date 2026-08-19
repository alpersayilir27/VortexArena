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
        /// <para><paramref name="scoreLimit"/> ayrıca <b>sınırsız</b> olabilir
        /// (<see cref="ArenaProtocol.SCORE_LIMIT_UNLIMITED"/>): o maçta hiçbir skor/tur limiti
        /// işlemez. ⚠️ Bu yüzden negatif değer <c>0</c>'a KIRPILMAZ — kırpılsa sınırsız seçimi
        /// sessizce "mod varsayılanı"na dönerdi.</para>
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
                scoreLimit = ArenaProtocol.NormalizeScoreLimit(scoreLimit),
                countdownSeconds = Mathf.Max(0, countdownSeconds)
            };

            if (Send(msg))
            {
                string parameters = msg.roundSeconds > 0 || msg.scoreLimit != 0 || msg.countdownSeconds > 0
                    ? $" ({(msg.roundSeconds > 0 ? FormatDuration(msg.roundSeconds) : "mod süresi")}" +
                      $" · {(msg.scoreLimit != 0 ? "limit " + FormatScoreLimit(msg.scoreLimit) : "mod limiti")}" +
                      $"{(msg.countdownSeconds > 0 ? " · geri sayım " + msg.countdownSeconds + " sn" : "")})"
                    : "";
                SetStatus($"Maç isteği gönderildi: {modeId} · {sceneName}{parameters}");
            }
        }

        /// <summary>Skor/tur limitini operatörün okuduğu biçime çevirir: <c>sınırsız</c> ·
        /// <c>mod varsayılanı</c> · sayı. Üç durumun TEK yazımı burada durur — panel, istatistik
        /// özeti ve durum satırı aynı kaynaktan okur.</summary>
        public static string FormatScoreLimit(int scoreLimit) =>
            scoreLimit > 0 ? scoreLimit.ToString()
            : scoreLimit < 0 ? "sınırsız"
            : "mod varsayılanı";

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
        /// <para>⚠️ <paramref name="scoreLimit"/> bu sözleşmenin istisnasıdır: negatif değer
        /// (<see cref="ArenaProtocol.SCORE_LIMIT_UNLIMITED"/>) "dokunulmadı" değil <b>sınırsız
        /// seçildi</b> demektir, bu yüzden ne kırpılır ne de boş komut sayılır.</para>
        /// </summary>
        public static void SetSelection(string modeId, string sceneName, int roundSeconds = 0,
            int scoreLimit = 0, int countdownSeconds = 0)
        {
            int limit = ArenaProtocol.NormalizeScoreLimit(scoreLimit);
            if (string.IsNullOrEmpty(modeId) && string.IsNullOrEmpty(sceneName) &&
                roundSeconds <= 0 && limit == 0 && countdownSeconds <= 0)
            {
                return;
            }

            Send(new SetSelectionMsg
            {
                modeId = modeId ?? "",
                sceneName = sceneName ?? "",
                roundSeconds = Mathf.Max(0, roundSeconds),
                scoreLimit = limit,
                countdownSeconds = Mathf.Max(0, countdownSeconds)
            });
        }

        /// <summary>
        /// Dost ateşi anahtarı (§5.2 <c>set_friendly_fire</c>) — <b>seçim değil, anlık komuttur</b>:
        /// koşan maçta da geçerlidir, bu yüzden <c>set_selection</c>'a binmez (o mesajın
        /// "boş/0 = değiştirme" sözleşmesi bir <c>bool</c>'u ifade edemez) ve seçim kilidine takılmaz.
        /// <para>Değer yerel bir alana YAZILMAZ: sunucu <c>admin_state</c> ile geri yayar, panel onu
        /// gösterir (tek doğruluk kaynağı — iki operatör sapmaz).</para>
        /// </summary>
        public static void SetFriendlyFire(bool enabled)
        {
            Send(new SetFriendlyFireMsg { enabled = enabled });
        }

        /// <summary>
        /// Başlıkların AÇILIŞTA nasıl hizalanacağı (§5.2 <c>set_calibration_mode</c>) —
        /// <see cref="SetFriendlyFire"/> ile aynı sınıf: <b>anlık komut</b>, <c>set_selection</c>'a
        /// binmez ve seçim kilidine girmez.
        /// <para>Değer yerel bir alana YAZILMAZ: sunucu <c>admin_state</c> ile geri yayar
        /// (tek doğruluk kaynağı — iki operatör sapmaz).</para>
        /// <para>⚠️ <c>anchor_cloud</c> buradan GÖNDERİLMEZ: rezerve bir değerdir, sunucu da
        /// reddeder — arayüz o seçeneği pasif tutar.</para>
        /// </summary>
        public static void SetCalibrationMode(string mode)
        {
            if (Send(new SetCalibrationModeMsg { mode = mode ?? "" }))
            {
                SetStatus($"Kalibre modu gönderildi: {CalibrationModeLabel(mode)}");
            }
        }

        /// <summary>Kalibre modunun operatöre gösterilen adı; bilinmeyen değer ham geçer.</summary>
        public static string CalibrationModeLabel(string mode)
        {
            return mode == ArenaProtocol.CALIB_MODE_TWO_ANCHOR ? "2 Çapa"
                : mode == ArenaProtocol.CALIB_MODE_SAVED_ANCHOR ? "Eski Kalibre"
                : mode ?? "";
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
        /// Ölü bir oyuncuyu canlandırır; <paramref name="playerId"/> <b>0 = o an ölü olan HERKES</b>
        /// (§10.4). ⚠️ <c>0</c> ELENMEZ — <see cref="ClearCalibration"/> ile aynı toplu-hedef
        /// deseni; toplu canlandırma geçerli bir komuttur.
        /// <para>Modun canlanma şartını (turnuvada "canlanma yok") ve canlanma gecikmesini bilerek
        /// GEÇER. Kalibresiz ya da engelin içindeki oyuncuyu sunucu yine de canlandırmaz ve sebebi
        /// kendi konsoluna yazar — arayüz "gönderildi" der, "oldu" demez.</para>
        /// </summary>
        public static void RevivePlayer(int playerId)
        {
            if (playerId < 0)
            {
                return;
            }

            if (Send(new RevivePlayerMsg { playerId = playerId }))
            {
                SetStatus(playerId == 0
                    ? "Canlandırma gönderildi: tüm ölüler"
                    : $"Canlandırma gönderildi: oyuncu {playerId}");
            }
        }

        /// <summary>
        /// Bir oyuncunun kalibrasyonunu sıfırlar; <paramref name="playerId"/> <b>0 = HERKES</b>
        /// (§10.6). Sıfırlanan oyuncu ateş edemez, hasar yemez, canlanamaz ve diğer oyuncuların
        /// ekranında avatarı parlar — hizalamayı geri açmayı YALNIZ başlığın kendisi yapabilir.
        /// <para><paramref name="keepSaved"/> komutun iki kipini ayırır:
        /// <b>true = yumuşak</b> — yalnız o anki hizalama geçersiz kılınır, gözlükteki KAYITLI çapa
        /// yerinde kalır; <b>false = sert</b> — kayıtlı çapa ve UUID de kalıcı olarak silinir.</para>
        /// <para>⚠️ <b><see cref="ReloadCalibration"/> ile TAMAMEN AYRI bir komuttur ve onun tersi
        /// DEĞİLDİR:</b> sıfırlama oyuncuyu savaş dışı bırakır, yeniden yükleme ise gözlükteki
        /// kayıttan hizalamayı geri kurmayı dener. İkisini karıştırmak sahada oynayan bir oyuncuyu
        /// durduk yere oyun dışı bırakır.</para>
        /// <para>⚠️ <b>SERT kip, <see cref="ReloadCalibration"/>'ın okuyacağı veriyi yok eder:</b>
        /// kayıtlı çapa silindikten sonra "kalibre et" her başlıkta "cihazda kayıtlı kalibrasyon
        /// yok" ile döner ve oyuncular elle A/B sekansı almak zorunda kalır. Bu yüzden <b>günlük
        /// eylem YUMUŞAK kiptir</b>; sert kip yalnız zemin bantları taşındığında yapılan bir mekan
        /// bakımıdır.</para>
        /// </summary>
        public static void ClearCalibration(int playerId, bool keepSaved)
        {
            if (playerId < 0)
            {
                return;
            }

            if (Send(new ClearCalibrationMsg { playerId = playerId, keepSaved = keepSaved }))
            {
                SetStatus(playerId == 0
                    ? (keepSaved
                        ? "Tüm hizalamaların geçersiz kılınması gönderildi (cihaz kayıtları korunuyor)."
                        : "Tüm cihaz kalibrasyon kayıtlarının silinmesi gönderildi.")
                    : (keepSaved
                        ? $"Hizalama geçersiz kılma gönderildi: oyuncu {playerId} (cihaz kaydı korunuyor)"
                        : $"Cihaz kalibrasyon kaydının silinmesi gönderildi: oyuncu {playerId}"));
            }
        }

        /// <summary>
        /// Bir oyuncunun kalibrasyonunu gözlükte KAYITLI çapa verisinden yeniden yükletir;
        /// <paramref name="playerId"/> <b>0 = TÜM oyuncular</b>. Sunucu hesap yapmaz, komutu
        /// başlığa iletir; hizalamayı başlık geri yükler ve sonucu <c>calibration_result</c> ile
        /// bildirir.
        /// <para>⚠️ <b><see cref="ClearCalibration"/> ile TAMAMEN AYRI bir komuttur ve onun tersi
        /// DEĞİLDİR:</b> sıfırlama kalibrasyonu siler ve oyuncuyu savaş dışı bırakır; bu komut
        /// silmez, gözlükteki kayıttan hizalamayı geri kurmayı dener. İkisini karıştırmak sahada
        /// oynayan bir oyuncuyu durduk yere oyun dışı bırakır.</para>
        /// <para>Yeniden yükleme <b>geri alınabilir bir denemedir</b>: tutmazsa oyuncu zaten eskisi
        /// gibi kalır, bu yüzden onay penceresi yoktur.</para>
        /// </summary>
        public static void ReloadCalibration(int playerId)
        {
            if (playerId < 0)
            {
                return;
            }

            if (Send(new ReloadCalibrationMsg { playerId = playerId }))
            {
                SetStatus(playerId == 0
                    ? "Tüm kalibrasyonların yeniden yüklenmesi gönderildi."
                    : $"Kalibrasyon yeniden yükleme gönderildi: oyuncu {playerId}");
            }
        }

        /// <summary>
        /// Bir oyuncunun gövde ölçüsünü ALDIRIR; <paramref name="playerId"/> <b>0 = HERKES</b>
        /// (§10.8). Sunucu hesap yapmaz, komutu başlığa iletir; ölçümü başlık yapıp
        /// <c>set_body_scale</c> ile döner ve sonuç roster'dan herkese yayılır.
        /// <para>⚠️ Ölçüm anında oyuncu <b>ayakta ve dik</b> olmalıdır — doğru anı bilen operatördür,
        /// bu yüzden tetikleyici bir düğmedir. Kalibresiz oyuncuya komut gönderilmez (sunucu keser),
        /// sebebi duyuru satırında görünür.</para>
        /// </summary>
        public static void MeasureBodyScale(int playerId)
        {
            if (playerId < 0)
            {
                return;
            }

            if (Send(new MeasureBodyScaleMsg { playerId = playerId }))
            {
                SetStatus(playerId == 0
                    ? "Tüm oyuncuların ölçülmesi gönderildi."
                    : $"Ölçüm gönderildi: oyuncu {playerId}");
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
