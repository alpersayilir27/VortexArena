namespace VortexArena.Protocol
{
    /// Protokol sabitleri — tek doğruluk kaynağı: Docs/ArenaNet-Protokol.md §1.
    public static class ArenaProtocol
    {
        /// <summary>
        /// v10 ayrıca <c>clear_calibration</c>'a <b>sunucu → istemci yönü</b> ekler (§5.2/§5.3):
        /// sıfırlama artık roster'a yazılan bir boole değil, hedef başlığa <b>alansız</b> iletilen
        /// bir komuttur. Gerekçe §10.6'dadır — yarım kalmış elle kalibrasyonun (A alındı, B
        /// alınmadı) telde izi yoktur (<c>calibrated</c> zaten <c>false</c>'tur), yani roster
        /// deltası sıfırlamayı taşıyamaz.
        /// <para>⚠️ Bu yön de <b>eklemelidir</b>: tanımayan eski istemci mesajı yok sayar ve yarım
        /// sekansı başlığında tutar. Karışık sürümde bozulan tek şey operatörün o oyuncuyu
        /// sıfırlayamamasıdır — yine de APK turu tamamlanmalıdır.</para>
        /// v8: <b>üç değerli bağlantı durumu</b> — <c>PlayerInfo.online</c> (bool) KALDIRILDI,
        /// yerine <c>connection</c> (<see cref="CONNECTION_CONNECTED"/>/
        /// <see cref="CONNECTION_RECONNECTING"/>/<see cref="CONNECTION_LEFT"/>) +
        /// <c>reconnectSeconds</c> + <c>inMatch</c> geldi (§2/§5.3/§10.2).
        /// <para>⚠️ <b>v8'i kırıcı yapan şey alanın ÇIKARILMASIDIR:</b> eski istemci <c>online</c>
        /// alanını bulamayınca varsayılan <c>false</c> okur ve TÜM roster'ı "çevrimdışı" çizer;
        /// yeni istemci eski sunucudan <c>connection</c> alamaz ama boş değer <c>connected</c>
        /// sayıldığı için o yönde yalnız "yeniden bağlanıyor" satırını kaçırır. Sürüm uyuşmazlığı
        /// bağlantıyı <b>reddetmez</b> (yalnız uyarı basılır) — APK turu tamamlanmalıdır.</para>
        /// v7: <b>arena uzayı = dünya uzayı</b> (§3) — arena origin'i artık sahnedeki bir marker
        /// değil, dünya (0,0,0) ve kimlik rotasyonudur.
        /// <para>⚠️ <b>v7'yi kırıcı yapan şey tel DÜZENİ değil ANLAMIDIR:</b> baytlar birebir aynı
        /// kaldı, ama <c>0x01</c>/<c>0x02</c>/<c>0x05</c> pozları, <c>0x03</c> atış yönleri ve
        /// <c>0x07</c>/<c>0x08</c> iskelet kökleri artık BAŞKA bir çerçevede okunuyor. Eski
        /// istemci onları kendi marker'ına göre çözer: karışık sürümde iki taraf da birbirini
        /// metrelerce kaymış, hatta zeminin altında/havada görür. Sürüm uyuşmazlığı bağlantıyı
        /// <b>reddetmez</b> (yalnız uyarı basılır) — bu yüzden APK turu tamamlanmalıdır ve
        /// eksik kalırsa belirti "uzak oyuncular rastgele yerlere ışınlanıyor" olur.</para>
        /// v6: <b>iskelet akışı</b> (<c>0x07</c> §6.9 / <c>0x08</c> §6.10) — gövde artık üç noktadan
        /// TÜRETİLMİYOR, sahibinin cihazında Meta Movement SDK ile çözülüp retarget edilmiş iskelet
        /// olarak akıyor. Blob <b>opak</b>tır: sunucu açmaz, doğrulamaz, kopyalar (<c>netItemId</c>
        /// baytlarıyla aynı gerekçe, §6.6).
        /// <para>⚠️ <c>0x01 PoseUpdate</c> <b>kaldırılmadı ve kaldırılmaz</b>: silah duruşu, eşya
        /// baytları ve vuruş bildirimi ham el pozundan besleniyor (§6.2). İskelet onun yerine değil
        /// YANINA gelir; iki kanalın kadansı da ayrıdır (20 Hz ↔ <see cref="SKELETON_RATE_HZ"/>).</para>
        /// <para>⚠️ <b>v6'yı kırıcı yapan şey:</b> <c>0x07</c>/<c>0x08</c>'i tanımayan istemci uzak
        /// oyuncuların GÖVDESİNİ hiç çizemez (eli/kafası yerinde durur) — eski istemcinin gönderdiği
        /// iskeletsiz akış da yeni istemcide gövdesiz avatar üretir. Tel formatı eklemelidir ama
        /// görüntü karışık sürümde iki yönde de bozuktur; APK turu tamamlanmalıdır.</para>
        /// v5: ağ telemetrisi (<c>0x06</c> RTT yoklaması §6.7, <c>status.rttMs/jitterMs/lossPct</c>,
        /// admin-only <c>net_stats</c>) ve <b>paket birleştirme</b> (<c>0x05</c> §6.8 — snapshot +
        /// olaylar tek datagramda; <c>0x02</c>/<c>0x04</c> geri düşüş yolu olarak korundu).
        /// <c>health_update</c> broadcast olmaktan çıkıp <b>kurban + adminler</b>e gitmeye başladı
        /// (§10.3) — alan düzeni değişmedi.
        /// <para>⚠️ <b>v5'i kırıcı yapan tek şey <c>0x05</c>'tir:</b> onu tanımayan bir istemci
        /// birleştirme devreye girdiğinde uzak avatarları ve tracer'ları kaybeder. Diğer üç değişiklik
        /// tümüyle eklemelidir. Sürüm uyuşmazlığı bağlantıyı <b>reddetmez</b> (yalnız konsola uyarı
        /// basılır), bu yüzden karışık sürüm sessizce bozuk çizim üretir — APK turu tamamlanmalıdır.</para>
        /// v4: elde tutulan eşya tele girdi (<c>0x01</c>/<c>0x02</c> byte düzeni; §6.2/6.3/6.6),
        /// atış olayları WS'ten UDP'ye taşındı (<c>shot_fired</c> <b>kaldırıldı</b> →
        /// <c>0x03</c>/<c>0x04</c>; §6.4/6.5).
        /// v3: faz makinesi <c>paused</c>/<c>playing</c>/<c>finished</c>'a indi, <c>phaseReason</c> +
        /// <c>modeState</c> eklendi, lobi faz olmaktan çıkıp <b>tür</b> oldu, <c>set_team</c> yalnız
        /// admin (§10.1).
        /// v2: <c>set_name</c> kaldırıldı (→ <c>set_identity</c>), <c>lobby_state.version</c> +
        /// <c>status.rosterVersion</c> + <c>PlayerInfo.number</c> eklendi (§1).
        /// </summary>
        public const int PROTOCOL_VERSION = 8;
        public const string APP_ID = "VortexArena";

        /// <summary>Forma numarası aralığı (§2). <c>0</c> = atanmamış ve bu aralığın dışındadır;
        /// admin'de daima 0 kalır. Numara TÜM kayıtlı cihazlar arasında benzersizdir.</summary>
        public const int PLAYER_NUMBER_MIN = 1;
        public const int PLAYER_NUMBER_MAX = 99;

        public const int UDP_BEACON_PORT = 47820;
        public const int CONTROL_PORT = 47821;
        public const int STATE_PORT = 47822;
        public const string WS_PATH = "/ws";

        // Aralıklar/timeout'lar saniye cinsinden.
        public const float BEACON_INTERVAL = 2f;
        public const float DISCOVERY_TIMEOUT = 5f;
        public const float STATUS_INTERVAL = 5f;
        /// <summary>Status gelmezse soketin ölü sayılıp kapatıldığı süre (§1/§8). ⚠️ Tek başına
        /// "oyuncu gitti" DEMEZ: cihaz bunun sonunda yalnız <see cref="CONNECTION_RECONNECTING"/>'e
        /// düşer, asıl çıkarma kararı <see cref="RECONNECT_GRACE"/>'indir.</summary>
        public const float HEARTBEAT_TIMEOUT = 15f;

        /// <summary>
        /// Bağlantısı kopan cihazın geri beklendiği süre (§2). Dolunca oyuncu oyundan çıkarılır:
        /// koşan maçın katılımcısıysa kaydı <see cref="CONNECTION_LEFT"/> olarak maç sonuna kadar
        /// durur (§10.2), değilse tümden silinir ve <c>playerId</c>'si havuza döner.
        /// <para>⚠️ Bu süre <b>sunucunun</b> kaydı ne zaman düşüreceğini söyler, istemcinin ne zaman
        /// pes edeceğini değil: yeniden deneme <see cref="RECONNECT_BACKOFF"/> ile SONSUZDUR (§8).
        /// Kopuştan çıkarılmaya toplam süre <see cref="HEARTBEAT_TIMEOUT"/> + bu değerdir.</para>
        /// </summary>
        public const float RECONNECT_GRACE = 45f;

        public const float HELLO_TIMEOUT = 10f; // §8: hello'suz bağlantı bu süre içinde kapatılır

        // Yeniden bağlanma backoff dizisi; son eleman tavandır.
        public static readonly float[] RECONNECT_BACKOFF = { 1f, 2f, 5f };

        public const int POSE_RATE_HZ = 20;
        public const int SNAPSHOT_RATE_HZ = 20;
        public const int INTERP_DELAY_MS = 100;

        /// <summary>
        /// İskelet blob'unun gönderim frekansı (§6.9). <b>Poz kanalından AYRI ve daha düşüktür</b>:
        /// blob poz paketinin birkaç katı büyüklüktedir ve bu ürünün darboğazı bant değil datagram
        /// sayısıdır (<c>Docs/Sistem-Ozeti.md</c> §3.12).
        /// <para>Düşük hızın görüntüyü bozmamasının sebebi, alıcıda <b>SDK'nın kendi
        /// interpolasyonunun</b> koşmasıdır (<c>GetInterpolatedSkeleton</c> render zamanına göre
        /// örnekler) — 12 Hz akış 72 Hz çizime yumuşak yayılır. Aynı sebeple bu sayı yükseltilerek
        /// "daha akıcı gövde" alınmaz; yalnız paket sayısı artar.</para>
        /// </summary>
        public const int SKELETON_RATE_HZ = 12;

        /// <summary>
        /// Tek oyuncunun iskelet blob'u için kabul edilen en büyük boy (§6.9). Üst sınır bir bütçe
        /// değil <b>emniyet</b>tir: <c>0x07</c> başlığıyla birlikte tek datagramda kalmalı
        /// (34 + 1024 = 1058 B &lt; <see cref="COMBINED_MAX_BYTES"/>), çünkü bu kanalda parçalama
        /// YOKTUR — blob bölünürse alıcı yarım bir kareyi deserialize etmeye çalışır.
        /// <para>⚠️ Aşan blob <b>gönderilmez</b> (bir kez uyarı basılır): sığmayan paketi yollamak
        /// IP parçalanmasına güvenmek olurdu ve tek parçanın kaybı tüm kareyi çöpe atardı. Blob bu
        /// sınırı zorluyorsa çözüm sıkıştırmayı/eklem listesini daraltmaktır (parmak eklemleri
        /// kumandayla oynanırken gerçek veri taşımaz).</para>
        /// </summary>
        public const int SKELETON_MAX_BLOB_BYTES = 1024;

        /// <summary>
        /// Tek <c>0x08</c> datagramına yazılan en fazla oyuncu girdisi (§6.10). ⚠️ Asıl kısıt
        /// <b>bayt bütçesidir</b> (<see cref="COMBINED_MAX_BYTES"/>) — girdiler değişken uzunluklu
        /// olduğu için sunucu her ikisine birden bakar; bu sayı yalnız <c>count</c> alanının
        /// <c>u8</c> olmasının makul bir tavanıdır.
        /// <para>Taşan girdi aynı tik içinde <b>ek datagrama</b> yazılır (snapshot parçalamasının
        /// aynısı, §6.3): her datagram kendi <c>count</c>'unu, hepsi aynı <c>serverTick</c>'i taşır.
        /// İstemcide birleştirme mantığı gerekmez — her girdi bağımsız uygulanır.</para>
        /// </summary>
        public const int SKELETON_MAX_ENTRIES_PER_PACKET = 16;

        /// <summary>
        /// <c>playerId</c> tahsis tavanı. <b>Ürün kotası DEĞİL, tel formatı tavanıdır</b> —
        /// playerId UDP paketlerinde <c>u8</c> taşınır (0 ayrılmıştır). Eşzamanlı oyuncu/admin
        /// sayısına başka bir sınır yoktur; kota ileride lisanslama katmanıyla gelecek.
        /// </summary>
        public const int PLAYER_ID_MAX = 255;

        /// <summary>
        /// Atılan istemcinin bağlantısı bu <b>kapanış çerçevesi sebebiyle</b> kapatılır (§5.4).
        /// İkinci emniyet: `kicked` JSON'u kapanışa yetişemezse istemci kopuşu sıradan bir
        /// kesinti sanıp yeniden bağlanırdı — atılan oyuncu kendiliğinden geri gelirdi.
        /// </summary>
        public const string KICK_CLOSE_REASON = "kicked";

        /// <summary>
        /// Tek snapshot datagramına yazılan en fazla oyuncu girdisi. Fazlası aynı tik içinde
        /// ek datagramlara taşar (§6.3) — 6 + 16×88 = 1414 B, MTU 1500'ün altında kalır.
        /// İstemcide birleştirme mantığı gerekmez: her paket kendi girdilerini bağımsız uygular.
        /// </summary>
        public const int SNAPSHOT_MAX_ENTRIES_PER_PACKET = 16;

        /// <summary>
        /// Tek <c>0x04</c> EventBatch datagramına yazılan en fazla olay (§6.5) —
        /// 6 + 128×9 = 1158 B, MTU 1500'ün altında kalır.
        /// <para>⚠️ Taşan olay <b>ATILMAZ, sonraki tik'in batch'ine kayar</b>: "tik başına en fazla
        /// bir batch" değişmezi istemcideki kopya korumasının dayanağıdır (kimlik <c>serverTick</c>).
        /// Aynı tik için ikinci bir batch üretilirse istemci onu birebir tekrar sanıp düşürür.</para>
        /// </summary>
        public const int EVENT_MAX_ENTRIES_PER_PACKET = 128;

        /// <summary>
        /// İstemcinin kopya ayıklama için hatırladığı <c>0x04</c> tik sayısı (§6.5). Halka tampon:
        /// yalnız birebir tekrar düşürülür, eski tik'li ama görülmemiş batch OYNATILIR.
        /// <para>⚠️ <c>0x05</c> (§6.8) de <b>aynı</b> halkayı kullanır — ayrı bir halka açılırsa aynı
        /// tik iki kez oynar (çift tracer + çift ses).</para>
        /// </summary>
        public const int EVENT_TICK_HISTORY = 64;

        /// <summary>
        /// <c>0x05</c> birleşik datagramının (§6.8) üst sınırı. Sunucu snapshot + olayları yalnız bu
        /// boyutun altında kalıyorsa tek pakette birleştirir; aşarsa <c>0x02</c>+<c>0x04</c>'e düşer.
        /// <para>MTU 1500 iken 1200 seçildi: LAN'da tünel/VPN yok ama sahada 1500'den küçük MTU
        /// görülebiliyor ve birleştirme bir <b>optimizasyon</b>dur — parçalanma riskine karşılık
        /// alınacak bir kazanç değil. Sınırın altında kalmayan tik zaten eski yolla çalışır.</para>
        /// </summary>
        public const int COMBINED_MAX_BYTES = 1200;

        /// <summary>
        /// Lobi türünün <c>modeId</c>'si (§10.7). <b>Kayıtlı bir maç modu DEĞİLDİR</b> — sunucuda
        /// <c>IGameMode</c> karşılığı yoktur, <c>start_match</c> ile başlatılamaz (yani "lobi türü
        /// seçiliyken maç başlamaz" kuralı buradan gelir, ayrı bir kontrol yoktur). İstemci bununla
        /// silah loadout'unu, HUD'unu ve ateş serbestliğini (<c>rules.fireWhilePaused</c>) çözer.
        /// </summary>
        public const string LOBBY_MODE_ID = "lobby";

        // ---- Bağlantı durumu (§2/§5.3). Telde string taşınır; bilinmeyen/boş değer CONNECTED
        // sayılır — böylece ileride dördüncü bir durum eklemek PROTOCOL_VERSION artırmaz.
        // ⚠️ "Çevrimdışı" diye bir değer YOKTUR ve eklenmez: kopan cihaz ya geri beklenir
        // (reconnecting) ya da oyundan çıkarılır (left). ----

        /// <summary>Soket canlı; oyuncu tüm maç kapılarına girer.</summary>
        public const string CONNECTION_CONNECTED = "connected";

        /// <summary>Soket düştü, cihaz <see cref="RECONNECT_GRACE"/> boyunca geri bekleniyor.
        /// Kayıt durur ama maç kapılarına GİRMEZ (yükleme kapısı beklemez, vurulamaz, canlanmaz,
        /// snapshot'ta yer almaz).</summary>
        public const string CONNECTION_RECONNECTING = "reconnecting";

        /// <summary>Süre doldu, oyuncu oyundan çıkarıldı. Kayıt yalnız koşan maçın katılımcısıysa
        /// (<c>PlayerInfo.inMatch</c>) maç sonuna kadar durur (§10.2).</summary>
        public const string CONNECTION_LEFT = "left";

        // ---- Faz değerleri (§10.1). Telde string taşınır; bilinmeyen değer PAUSED sayılır. ----

        /// <summary>Maç koşmuyor: lobi, yükleme, geri sayım, duraklatma. Hasar KAPALI.</summary>
        public const string PHASE_PAUSED = "paused";

        /// <summary>Maç koşuyor. <b>Hasarın işlendiği TEK faz</b> (§10.3).</summary>
        public const string PHASE_PLAYING = "playing";

        /// <summary>Maç bitti, skorlar kesin. Hasar KAPALI.</summary>
        public const string PHASE_FINISHED = "finished";

        // ---- phaseReason değerleri (§10.1): yalnız PHASE_PAUSED iken doludur. ----

        /// <summary>Lobi türü açık, maç kurulmadı.</summary>
        public const string PAUSE_REASON_LOBBY = "lobby";

        /// <summary>Sahne yükleme kapısı: tüm oyuncuların set_ready'si bekleniyor.</summary>
        public const string PAUSE_REASON_LOADING = "loading";

        /// <summary>Geri sayım (COUNTDOWN_SECONDS).</summary>
        public const string PAUSE_REASON_COUNTDOWN = "countdown";

        /// <summary>Operatör koşan maçı duraklattı.</summary>
        public const string PAUSE_REASON_OPERATOR = "operator";

        /// <summary>Mod duraklatma istedi; gerekçesi <c>modeState</c>'tedir (ör. turnuva toplanması).</summary>
        public const string PAUSE_REASON_MODE = "mode";

        // ---- Maç akışı + savaş (Docs/ArenaNet-Protokol.md §10) ----

        /// <summary>Oyuncu tam canı; sunucu-otoriter (health_update bunu aşamaz).</summary>
        public const float PLAYER_MAX_HP = 100f;

        /// <summary>Countdown fazının VARSAYILAN uzunluğu (saniye, tam sayı — countdown mesajı
        /// saniye sayar). Admin start_match.countdownSeconds ile o maça özel bir değer verebilir
        /// (§5.2); tur tabanlı modlarda (tournament) turlar arasındaki geri sayım da odur.</summary>
        public const int COUNTDOWN_SECONDS = 5;

        /// <summary>start_match.countdownSeconds kırpma aralığı (§5.2). <b>Arayüz listesi değil,
        /// sunucunun uyguladığı kısıttır:</b> 1 sn'lik geri sayım oyuncuya yerini alacak zaman
        /// bırakmaz, 30 sn'den uzun bekleme tur tabanlı modda ölü zamandır.</summary>
        public const int COUNTDOWN_SECONDS_MIN = 5;

        /// <inheritdoc cref="COUNTDOWN_SECONDS_MIN"/>
        public const int COUNTDOWN_SECONDS_MAX = 30;

        /// <summary>End fazı: match_end sonrası otomatik return_to_lobby'ye kadar geçen süre.
        /// <para>⚠️ <b>Pratikte bu bir emniyettir, akış değil:</b> kazanan ekranını kapatan şey
        /// operatörün harita/lobi seçimi (ya da BAŞLAT/İPTAL) olsun diye bilerek uzun tutuldu —
        /// hepsi fazı değiştirdiği için sayacı öldürür (§10.1). Operatör hiçbir şey yapmazsa maç
        /// yine de sonsuza kadar askıda kalmaz.</para></summary>
        public const int MATCH_END_SECONDS = 999;

        /// <summary>Loading fazında tüm oyuncuların "sahne yüklendi" (set_ready) bildirimi
        /// beklenir; bu süre dolunca eksik olsa da Countdown'a geçilir.</summary>
        public const float LOADING_TIMEOUT = 20f;

        /// <summary>Ölüm → en erken canlanma süresinin VARSAYILANI (respawn.delaySeconds).
        /// Mod bunu ModeRules.RespawnDelay ile ezebilir (§10.5).</summary>
        public const float RESPAWN_DELAY = 5f;

        /// <summary>Free-roam canlanma: süre dolduktan sonra oyuncu modun canlanma şartını
        /// sağlayıp revive_request gönderir. Bu süre (ölümden itibaren) dolduğunda sunucu yine de
        /// canlandırır — takılan/bildirim gönderemeyen istemci kalıcı ölü kalmasın.</summary>
        public const float REVIVE_GRACE = 20f;

        /// <summary>reviveAnchor="standstill" (§10.5): ölü oyuncunun canlanmak için kesintisiz
        /// sabit durması gereken süre.</summary>
        public const float REVIVE_HOLD_SECONDS = 3f;

        /// <summary>reviveAnchor="standstill": ölüm anındaki çapadan bu yarıçapı (metre) aşan
        /// hareket sayacı ve çapayı sıfırlar.</summary>
        public const float REVIVE_HOLD_RADIUS = 1f;

        /// <summary>
        /// Admin arayüzünün maç süresi seçenekleri (saniye): 1 · 1.5 · 2 · 2.5 · 3 · 5 · 10 · 15 ·
        /// 20 · 30 dk · 1 saat.
        /// <para><b>Protokol kısıtı DEĞİL, arayüz listesidir</b> — sunucu start_match.roundSeconds
        /// alanında her pozitif değeri kabul eder (§5.2).</para>
        /// <para>Kısa uçtaki değerler tur tabanlı modlar içindir: <c>tournament</c>'ta bu alan
        /// <b>turun</b> süresidir, maçın değil (§10.5).</para>
        /// </summary>
        public static readonly int[] ROUND_SECONDS_OPTIONS = { 60, 90, 120, 150, 180, 300, 600, 900, 1200, 1800, 3600 };

        // NOT: atış hızı toleransı gibi bir sabit YOKTUR ve eklenmez (§10.3). Sunucuda atış hızı
        // denetimi ve silah tablosu yoktur — hasarı istemci hesaplar, sunucu aynen uygular. Ürün
        // gözetimli özel alanda çalıştığı için hile koruması bilinçli olarak eklenmez.
    }
}
