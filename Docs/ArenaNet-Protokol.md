# ArenaNet Protokol Referansı (v1) — TEK DOĞRULUK KAYNAĞI

> Unity `VortexArena.Protocol` asmdef'i ile .NET sunucu aynı C# kaynaklarını derler (yapısal sapma imkânsız); bu doküman **semantiğin** tek doğruluk kaynağıdır. İki taraftan biri davranış değiştirecekse ÖNCE burası güncellenir.

## 1. Sabitler

Tümü paylaşılan `ArenaProtocol` statik sınıfında tanımlanır (`Assets/_Shared/Net/Protocol/ArenaProtocol.cs`).

| Sabit | Değer | Açıklama |
|---|---|---|
| `PROTOCOL_VERSION` | `15` | hello/welcome'da taşınır; uyumsuzlukta log uyarısı (bağlantı **kesilmez** — `Server/VortexArena.Server.Core/LobbyService.cs` uyarıyı basıp devam eder). ⚠️ **Karışık sürüm desteklenmez** — sürüm artınca tüm başlıklara yeni APK kurulur; bağlantı reddedilmediği için bunu zorlayan tek şey APK turunun tamamlanmasıdır. v15 `clear_calibration`'a **`keepSaved` (bool)** ekler (§5.2/§5.3, davranış §10.6): sıfırlama iki eyleme ayrılır — *hizalamayı geçersiz kıl* (gözlükteki kayıtlı çapa ve UUID korunur, `reload_calibration` çalışmaya devam eder) ve *cihaz kaydını da sil*. ⚠️ **Alanın YOKLUĞU `keepSaved:false` demektir** (sert kip): alanı tanımayan bir uç bugünkü davranışı sürdürür, sürpriz yapmaz. Karışık sürümde kaybolan şey bozuk çizim değil, operatörün *yumuşak* seçiminin sert uygulanmasıdır — kayıtlı çapa silinir ve o oyuncuda `reload_calibration` bir daha iş görmez. v14 **alan-dışını tele taşır**: `flags` bit7 = `FLAG_OUT_OF_BOUNDS` (§6.3) + yalnız adminlere giden `violation` akışı (§5.3, §10.9). Tel formatı **değişmez** (95 B / 88 B aynı, bit rezervden alındı, bant artışı sıfır) ama sürüm yine de artar: biti yazan **istemcidir**, yani eski APK'lı oyuncu onu hiç göndermez ve adminde alan dışına çıktığı **hiç görünmez** — kaybolan şey bozuk çizim değil, operatörün göremediği bir ihlaldir. ⚠️ **Bu bit CAN ERİTMEZ** (§10.9): ceza modeli yalnız `FLAG_IN_OBSTACLE`'a bağlıdır. v13 **kalibre modunu** (`set_calibration_mode` §5.2, `admin_state.calibrationMode` + `welcome.calibrationMode` §5.3, davranış §10.6), **zemin sapması bildirimini** (`set_calibration.floorOffset` §5.1 → `PlayerInfo.floorOffset` §5.3) ve **ölçüm başarısızlığı geri bildirimini** (`set_body_scale.error` §5.1 → `PlayerInfo.scaleError` §5.3, §10.8) getirir; tümüyle **eklemelidir**. Karışık sürümde: alanları göndermeyen eski istemcinin zemin sapması ve ölçüm gerekçesi operatöre hiç görünmez, `welcome.calibrationMode`'u okumayan başlık ise modu yok sayıp bugünkü davranışta (diskten çapa geri yükleme) kalır — kaybolan kural, bozuk çizim değil. v12 iskelet blob'undan **parmak eklemlerini çıkarır** (§6.9): hedef iskeletin 40 parmak eklemi tele hiç girmez, parmakları alıcı kendi sentezler. ⚠️ **Bu değişiklik KIRICIDIR ve sessizdir** — blob opak olduğu için eklem listesi uyuşmayan iki uç hata vermez, yalnız gövdeyi bozuk çizer; karışık sürümde belirti "uzak oyuncular garip duruyor"dur. v11 **engel ihlalini** taşır: `flags` bit5 = `FLAG_IN_OBSTACLE` (§6.3) + sunucu tarafında saniyelik can eritme (§10.9) — tümüyle **eklemelidir** (bayt düzeni değişmedi, bit rezervden alındı). Karışık sürümde: eski istemci biti hiç göndermez (o oyuncu duvarda ceza almaz) ve gelen biti yok sayar (admin halkası yanıp sönmez). v10 kumanda durumunu taşır: `flags` bit3/bit4 = **bayat el** (§6.3) + `status`/`PlayerInfo` üzerinde `ctrlL`/`ctrlR` (§5.1/§5.3) — tümüyle **eklemelidir** (bayt düzeni değişmedi, bitler rezervden alındı), bilmeyen uç bitleri yok sayar ve alanları `0` = "bildirilmedi" okur. v10 ayrıca `clear_calibration`'a **sunucu → istemci yönü** ekler (§5.2/§5.3): sıfırlama artık roster'a yazılan bir boole değil hedef başlığa iletilen bir komuttur. Bu yön de eklemelidir — tanımayan eski istemci mesajı yok sayar ve **yarım kalmış elle kalibrasyonu** (A alındı, B alınmadı) başlığında tutmaya devam eder, yani karışık sürümde bozulan tek şey operatörün o oyuncuyu sıfırlayamamasıdır. v9 gövde ölçeğini getirdi (`measure_body_scale` · `set_body_scale` · `PlayerInfo.bodyScale`, §10.8): tümüyle **eklemelidir**, eski istemci alanı bulamayınca `0` okur ve herkesi ölçeksiz çizer — yani karışık sürümde bozulan tek şey avatar boylarıdır. v8'de `lobby_state`'in `online` (bool) alanı yerini üç değerli `connection` + `reconnectSeconds`'a bıraktı (§5.3): alanı tanımayan eski admin her satırı "bağlı" çizer, yani kopan oyuncular hiç fark edilmez. v7'yi kırıcı yapan tel DÜZENİ değil **ANLAMIDIR**: baytlar v6 ile birebir aynı, ama `0x01`/`0x02`/`0x05` pozları, `0x03` atış yönleri ve `0x07`/`0x08` iskelet kökleri artık arena uzayı = dünya uzayı çerçevesinde okunur (§3). Eski istemci aynı baytları kendi sahne marker'ına göre çözer → iki taraf birbirini metrelerce kaymış, zeminin altında veya havada görür; belirti **"uzak oyuncular rastgele yerlere ışınlanıyor"**. v6'da bozulma iki yönlüydü: `0x07`/`0x08`'i tanımayan istemci uzak gövdeleri hiç çizemez, iskelet göndermeyen istemci de gövdesiz görünür (§6.9). v5'te bozulan tek yer `0x05` birleştirmesiydi (§6.8) |
| `UDP_BEACON_PORT` | `47820` | Sunucu → broadcast (cosmos 47800/47801 ile bilerek çakışmaz) |
| `CONTROL_PORT` | `47821` | WS TCP, endpoint `/ws` |
| `STATE_PORT` | `47822` | UDP poz kanalı |
| `BEACON_INTERVAL` | 2 sn | Beacon yayın aralığı |
| `DISCOVERY_TIMEOUT` | 5 sn | Beacon gelmezse statik IP fallback (`StreamingAssets/arena.json`); komut satırı adresi ve elle girilen IP beacon'ın **üstündedir** (zincirin tamamı §4) |
| `STATUS_INTERVAL` | 5 sn | İstemci status kalp atışı |
| `HEARTBEAT_TIMEOUT` | 15 sn | Status gelmezse soket ölü sayılır, kapatılır ve cihaz `reconnecting`'e düşer (§2). ⚠️ Tek başına "oyuncu gitti" DEMEZ — asıl karar `RECONNECT_GRACE`'indir |
| `RECONNECT_GRACE` | 45 sn | `reconnecting` cihazın geri beklendiği süre. Dolunca oyuncu oyundan **çıkarılır**: koşan maçın katılımcısıysa kaydı `left` olarak maç sonuna kadar durur, değilse tümden silinir ve `playerId`'si havuza döner (§2, §10.2). Kopuştan çıkarılmaya toplam süre `HEARTBEAT_TIMEOUT + RECONNECT_GRACE` |
| `RECONNECT_BACKOFF` | 1 → 2 → 5 sn (tavan 5) | Kopunca sonsuz yeniden deneme; her denemede discovery baştan. ⚠️ İstemci `RECONNECT_GRACE` dolsa da denemeyi BIRAKMAZ (§8) |
| `POSE_RATE_HZ` | `20` | İstemci poz gönderim frekansı |
| `SNAPSHOT_RATE_HZ` | `20` | Sunucu snapshot yayın frekansı |
| `INTERP_DELAY_MS` | `100` | Uzak avatar interpolasyon tamponu |
| `SKELETON_RATE_HZ` | `12` | İskelet blob'u gönderim frekansı (§6.9). Poz kanalından **ayrı ve daha düşük** — blob poz paketinin birkaç katı ve darboğaz paket sayısı. Alıcıda SDK'nın kendi interpolasyonu koştuğu için 12 Hz akış 72 Hz çizime yumuşak yayılır; yükseltmek akıcılık değil yalnız paket satın alır |
| `SKELETON_MAX_BLOB_BYTES` | `1024` | Tek oyuncunun blob tavanı (§6.9). Bütçe değil **emniyet**: 34 + 1024 = 1058 B < `COMBINED_MAX_BYTES`, çünkü bu kanalda **parçalama yoktur**. Aşan blob hiç gönderilmez |
| `SKELETON_MAX_ENTRIES_PER_PACKET` | `16` | Tek `0x08` datagramına yazılan en fazla girdi (§6.10). Asıl kısıt **bayt bütçesidir** (`COMBINED_MAX_BYTES`) — girdiler değişken uzunluklu; bu sayı `count`'un `u8` olmasının tavanıdır |
| `PLAYER_ID_MAX` | `255` | `playerId` tahsis tavanı. **Ürün kotası değil, tel formatı tavanıdır** — `playerId` UDP paketlerinde `u8`. Eşzamanlı oyuncu/admin sayısına başka sınır YOKTUR (kota ileride lisanslamayla gelecek) |
| `PLAYER_NUMBER_MIN` / `PLAYER_NUMBER_MAX` | `1` / `99` | Forma numarası aralığı (§2). `0` = atanmamış ve aralığın dışındadır. Numara **tüm kayıtlı cihazlar** arasında benzersizdir |
| `CALIB_MODE_TWO_ANCHOR` / `CALIB_MODE_SAVED_ANCHOR` / `CALIB_MODE_ANCHOR_CLOUD` | `"two_anchor"` / `"saved_anchor"` / `"anchor_cloud"` | Kalibre modunun geçerli değerleri (§5.2/§10.6). Sunucu açılış varsayılanı `two_anchor`. ⚠️ `anchor_cloud` **rezervdir** — sunucu kabul etmez, loglayıp durumu değiştirmez; bilinmeyen/boş değer de aynı şekilde reddedilir (sessizce varsayılana düşmez: mod bir operatör kararıdır, tahmin edilmez) |
| `CALIB_FLOOR_WARN_METERS` | `0.5` | Elle kalibrasyonda bildirilen zemin sapmasının (`set_calibration.floorOffset`) mutlak değeri bunu aşarsa sunucu adminlere duyuru basar (§10.6). Bir kapı değil **teşhis eşiğidir**: kalibrasyon yine kabul edilir, operatör gözlükte alan verisi temizliğine yönlendirilir |
| `BODY_SCALE_MIN` / `BODY_SCALE_MAX` | `0.5` / `1.6` | `set_body_scale` kırpma aralığı (§10.8). Ölçüm istemcide yapılır ama sonuç **herkesin ekranına** gider; sunucu bu yüzden kırpar — bozuk bir istemci arenaya 4 metrelik bir avatar koyamasın. `0` bu aralığın dışındadır ve "ölçülmemiş" demektir |
| `SNAPSHOT_MAX_ENTRIES_PER_PACKET` | `16` | Tek snapshot datagramına yazılan en fazla oyuncu; fazlası ek pakete taşar (§6.3). 6 + 16×88 = 1414 B < MTU |
| `EVENT_MAX_ENTRIES_PER_PACKET` | `128` | Tek `0x04` datagramına yazılan en fazla olay (§6.5). 6 + 128×9 = 1158 B < MTU. Taşan olay **atılmaz, sonraki tik'e kayar** — "tik başına en fazla bir batch" değişmezi kopya korumasının dayanağıdır |
| `EVENT_TICK_HISTORY` | `64` | İstemcinin kopya ayıklama için hatırladığı `0x04` tik sayısı (§6.5) |
| `PLAYER_MAX_HP` | `100` | Oyuncu tam canı (sunucu-otoriter; §10) |
| `OBSTACLE_GRACE_SECONDS` | `3` | Engelin içinde **can erimeye başlamadan önceki** tolerans (§10.9). Bu sürede oyuncunun ekranı zaten kapkaranlıktır: bedava olan görüş değil **yalnız candır**. ⚠️ Engelden çıkınca **tümden sıfırlanır** (kısmi sönüm yok) — girip çıkan oyuncu her girişinde yeniden kör kalıyor, yani kazandığı bir şey yok |
| `OBSTACLE_DRAIN_SECONDS` | `5` | Tolerans dolduktan sonra **tam candan** ölüme geçen süre (§10.9). Engelde geçirilebilen toplam süre `OBSTACLE_GRACE_SECONDS` + bu değerdir (**8 sn**). ⚠️ Yaralı oyuncu daha çabuk ölür: erime bir HIZ'dır, geri sayım değil |
| `OBSTACLE_DAMAGE_PER_SECOND` | `20` | Engel ihlalinde saniyelik can kaybı (§10.9). ⚠️ **Elle yazılmaz, türetilir:** `PLAYER_MAX_HP / OBSTACLE_DRAIN_SECONDS`. Tasarım parametresi süredir, hız onun sonucudur — ikisini ayrı ayrı yazmak aynı sayının iki kaynağı olurdu. ⚠️ **Üçünün de tek tüketicisi sunucudur** — değerleri değiştirmek yeni APK gerektirmez, sunucu derlemesi yeter |
| `OBSTACLE_REVIVE_BLOCK_SECONDS` | `40` | Engelin içindeyken canlanmanın en fazla bu kadar ertelenmesi (§10.9/§10.4). Kapı istemcinin bildirdiği bayrağa baktığı için tavansız bırakılamaz: yanlış konuşan bir istemci oyuncuyu kalıcı ölü bırakırdı (`OBSTACLE_FLAG_STALE_MS` yalnız **susmuş** istemciyi çözer). Tavan dolunca oyuncu engelde de olsa canlandırılır — çıkmadıysa ceza anında yeniden başlar, yani kural işlevsizleşmez |
| `OBSTACLE_FLAG_STALE_MS` | `300` | `FLAG_IN_OBSTACLE` bu süredir tazelenmemişse bayrak **düşürülür** (§10.9). Poz kanalı 20 Hz (50 ms) olduğu için 6 paketlik kayba dayanır; susmuş bir istemci sonsuza kadar ceza almaz |
| `VIOLATION_KIND_OBSTACLE` / `VIOLATION_KIND_OUT_OF_BOUNDS` | `"obstacle"` / `"out_of_bounds"` | `violation` mesajının `kind` alanının geçerli değerleri (§5.3/§10.9). ⚠️ İkisi **ayrı türdür ve birleştirilmez**: biri ceza üretir, diğeri yalnız görünürlüktür — tek bir "ihlal" türü operatöre hangisine müdahale edeceğini söylemezdi |
| `VIOLATION_MIN_SECONDS` | `0.5` sn | Bir ihlalin admin akışına **yazılmaya değer** sayılması için gereken en kısa süre (§10.9). Altındaki temaslar için `violation` mesajı **hiç** gönderilmez — başlangıç kenarı bu süre dolana kadar bekletilir. ⚠️ **Yalnız akış içindir:** halka ve ceza ilk kareden itibaren çalışır; sınır çizgisinde salınan oyuncu aksi hâlde saniyede birkaç satır üretip akışı okunamaz hâle getirirdi |
| `COUNTDOWN_SECONDS` | `5` | Geri sayımın **varsayılan** uzunluğu (`phaseReason:"countdown"`); admin `start_match.countdownSeconds` ile o maça özel bir değer verebilir (§5.2) |
| `COUNTDOWN_SECONDS_MIN` / `COUNTDOWN_SECONDS_MAX` | `5` / `30` | `countdownSeconds` kırpma aralığı. **Bu bir arayüz listesi değil, sunucunun uyguladığı kısıttır** — 1 sn'lik geri sayım oyuncuya yerini alacak zaman bırakmaz, 30 sn'den uzun bekleme turnuvada ölü zamandır |
| `SCORE_LIMIT_UNLIMITED` | `-1` | `scoreLimit` alanının **sınırsız** değeri (§5.2): maçın skor/tur limiti yoktur, bitişi süre ya da operatörün `abort_match`'i belirler. ⚠️ Ayrı bir değer olmasının sebebi `0`'ın o alanda zaten "operatör seçmedi → modun varsayılanı" demesidir. Sunucudaki limit kapıları `limit > 0` diye sorduğu için sentinel hepsini birden kapatır; tur tabanlı modda **tur tavanı da** kalkar (tavan limitten türüyor). Her negatif değer buna normalize edilir (`NormalizeScoreLimit`) |
| `MATCH_END_SECONDS` | `999` | `finished` → otomatik `return_to_lobby`. **Akış değil emniyettir:** kazanan ekranı operatör bir şey seçene kadar durur (harita/lobi seçimi, `start_match`, `abort_match`/`return_to_lobby` — hepsi fazı değiştirdiği için sayacı öldürür, §10.1). Bilerek uzun tutuldu ki turnuvada tur/maç aralarını hakem yönetsin; operatör hiçbir şey yapmazsa maç yine de sonsuza kadar askıda kalmaz |
| `LOADING_TIMEOUT` | 20 sn | Yükleme kapısında (`phaseReason:"loading"`) tüm `set_ready` beklenmezse yine de geri sayıma geçilir |
| `RESPAWN_DELAY` | 5 sn | Ölüm → en erken canlanma (`respawn.delaySeconds`) **varsayılanı**; mod `rules.respawnDelay` ile ezebilir (§10.5) |
| `REVIVE_HOLD_SECONDS` | 5 sn | `reviveAnchor:"standstill"` (§10.5): ölü oyuncunun canlanmak için kesintisiz sabit durması gereken süre |
| `REVIVE_HOLD_RADIUS` | 1 m | `reviveAnchor:"standstill"`: ölüm anındaki çapadan bu yarıçapı aşan hareket sayacı sıfırlar |
| `ROUND_SECONDS_OPTIONS` | `60, 90, 120, 150, 180, 300, 600, 900, 1200, 1800, 3600` | Admin arayüzünün maç süresi seçenekleri (1 · 1.5 · 2 · 2.5 · 3 · 5 · 10 · 15 · 20 · 30 dk · 1 saat). **Protokol kısıtı değil, arayüz listesidir** — sunucu `start_match.roundSeconds`'ta her pozitif değeri kabul eder. Kısa uçtaki değerler tur tabanlı modlar içindir (`tournament`'ta bu alan **turun** süresidir, maçın değil) |

## 2. Roller ve kimlik

- `role`: `"player"` (VR/Quest) veya `"admin"` (Windows masaüstü). Admin oynamaz; lobi rosterinde görünür, komut yetkisi vardır.
- **Admin sahne olarak oyuncuları takip eder:** `load_match` / `welcome.match` / `return_to_lobby` admin istemcisinde de sahne yükler (gözlemci görünümü). İki fark: admin `set_ready` **göndermez** (Loading kapısını yalnız `role=player` besler) ve poz **göndermez** (`0x01 PoseUpdate` yok), ama `0x00` ile UDP kaydı yapıp snapshot'ları alır.
- `deviceId` — **role göre iki ayrı semantik:**
  - `player`: `SystemInfo.deviceUniqueIdentifier`, **kalıcı** kimlik. Sunucu `devices.json`'da **ad + numara** çiftine eşler (ikisi de otomatik atanır, aşağıda), kayıt bağlantı kopsa da durur (aynı gözlük geri gelince adı/numarası/kimliği korunur).
  - `admin`: `<deviceUniqueIdentifier>:admin:<oturum GUID'i>` — **oturum başına benzersiz**. Sebep: aynı fiziksel PC'de iki admin penceresi açılabilsin. Ortak deviceId ile ikisi aynı kaydı paylaşır, her `hello` diğerinin soketini kapatır ve sonsuz kick döngüsü olurdu. GUID süreç ömrü boyunca sabittir (yeniden bağlanma aynı kaydı bulur), uygulama kapanınca ölür.
- ⚠️ **"Çevrimdışı" diye bir oyuncu durumu YOKTUR ve eklenmez.** Bağlantı durumu üç değerlidir ve `lobby_state.connection` ile taşınır (§5.3):
  - `connected` — soket canlı.
  - `reconnecting` — soket düştü (kopma ya da `HEARTBEAT_TIMEOUT`), cihaz `RECONNECT_GRACE` boyunca geri bekleniyor. Kayıt durur, **maç kapılarına girmez**: yükleme kapısı onu beklemez, vurulamaz, canlanmaz, snapshot'ta yer almaz.
  - `left` — süre doldu, oyuncu oyundan çıkarıldı. Kayıt **yalnız koşan maçın katılımcısıysa** durur (§10.2: adı ve sayaçları maç sonuna kadar tabloda kalsın), aksi hâlde tümüyle silinir ve `playerId` havuza döner.
  Süresiz duran bir "çevrimdışı" satır bilerek yok: roster canlı bağlantıları, maç defteri ise katılımcıları gösterir — ikisi ayrı sorudur.
- **Admin kayıtları kalıcı DEĞİLDİR:** admin bağlantısı koptuğunda (veya `HEARTBEAT_TIMEOUT` dolduğunda) kaydı registry'den **tümüyle silinir** ve `playerId`'si havuza döner; adı `devices.json`'a **yazılmaz**. ⚠️ Admin `reconnecting` durumuna **hiç girmez**: `deviceId`'si oturumluktur, yani geri gelen admin yeni bir kimlikle gelir ve eski satır asla o bağlantıyla eşleşemezdi — "yeniden bağlanıyor" demek yalan olurdu. Böylece admin'i her açıp kapatma roster'da hayalet satır bırakmaz.
- **Yeniden bağlanma kimliği KORUR.** Aynı `deviceId` hangi durumdan dönerse dönsün (`reconnecting` ya da `left`) mevcut kayda oturur: `playerId`, ad, forma numarası, takım ve `kills`/`deaths`/`score` olduğu gibi kalır — oyuncu kaldığı yerden devam eder, ikinci bir satır açılmaz.
- **Admin sayısı sınırsız ve hepsi eş yetkilidir.** Birincil/ikincil admin kavramı yoktur: `role=="admin"` olan her bağlantı §5.2'deki tüm komutları gönderebilir, son gelen komut uygulanır. Operatörlerin birbirini ezmemesi için ortak seçim `admin_state` ile senkronlanır (§5.3) ve her komut `admin_state.notice` ile diğerlerine duyurulur.
- `playerId` = sunucunun `welcome`'da atadığı **1..`PLAYER_ID_MAX`** arası küçük tamsayı (UDP paketlerinde 1 bayt). Admin'e de atanır (poz göndermez). Havuz dolarsa `kicked{reason:"Sunucu dolu"}` ile reddedilir — bu bir ürün kotası değil, `u8` tel formatının tavanıdır.
- **Ad ve numara = CİHAZ kimliğidir, oturum kimliği değil.** İkisi de oyuncunun ilk bağlantısında otomatik atanır ve `devices.json`'a `deviceId` başına kalıcı yazılır; admin `set_identity` ile ikisini de değiştirebilir (§5.1). Roster'da `name` + `number` olarak taşınır (§5.3).
  - **Ad:** 20 kişilik havuzdan **rastgele** — `umut, alper, ertu, yunus, resul, enver, enes, nisa, ceren, tuğba, elif, pınar, taner, yasemin, hüseyin, deniz, selin, kaan, burcu, emre`. Henüz hiçbir kayıtlı cihazın kullanmadığı adlar arasından seçilir; hepsi kullanımdaysa havuzun tamamından. **Adlar benzersiz DEĞİLDİR** (21. cihazdan sonra tekrar eder) — ayırt edici alan numaradır.
  - **Numara:** `1..99`, **1'den itibaren ilk boş** değer (sıralı, rastgele değil). `0` = atanmamış. **Değişmez kural: `devices.json` içinde iki cihaz aynı numarayı ASLA taşımaz** — benzersizlik çevrimiçilerle sınırlı değil, **tüm kayıtlı cihazlar** arasında geçerlidir, böylece bir gözlük numarasını kalıcı korur.
  - Admin bir numarayı **çevrimiçi** bir oyuncudan isterse **reddedilir** (`admin_state.notice` ile bildirilir). **Çevrimdışı kayıtlı** bir cihazdan isterse kabul edilir ve o cihaz **aynı anda** 1'den itibaren ilk boş numaraya taşınıp diske yazılır. ⚠️ Çevrimdışına karşı da reddetmek operatörü kilitlerdi (numarayı tutan cihaz roster'da görünmez, serbest bırakılamaz); yeniden numaralamayı sonraki bağlantıya ertelemek ise dosyayı o süre boyunca çift numaralı bırakırdı.
  - `1..99` havuzu dolarsa (100+ kayıtlı cihaz) yeni cihaz `0` alır + konsol logu; operatör elle numaralar. 16 gözlüklü bir işletme bunu görmez.
  - **Admin'e ad atanır ama numara ATANMAZ** (`number:0`): admin oynamaz. Admin adı havuzdan değil `hello.deviceName`'den (PC adı) gelir ve diske yazılmaz — admin `deviceId`'si oturumluktur.
- Aynı `deviceId` ikinci kez bağlanırsa eski bağlantı kapatılır, yenisi kabul edilir (cihaz yeniden bağlanmıştır).

## 3. Koordinat çerçevesi — ARENA UZAYI

Tüm ağ pozları **arena uzayındadır** ve arena uzayı **sahnenin dünya uzayıdır**: origin = dünya (0,0,0), rotasyon kimlik. ⚠️ **Origin'i sahnedeki bir marker belirlemez** — arena geometrisi doğrudan bu çerçeveye kurulur: zemin dünya y=0'da, eksenler arena duvarlarına hizalı. Her Quest, `ArenaCalibrator` (2-nokta + OVRSpatialAnchor) ile fiziksel alana hizalandığı için bütün cihazlar aynı fiziksel çerçeveyi paylaşır. Dönüşüm istemcide yapılır (rig-world → arena-local); sunucu ve admin görünümü ham arena koordinatı kullanır.

⚠️ Bu tanım **tel formatında görünmez ama sürüm kırar**: pozların baytları çerçeveyi taşımıyor, iki taraf onu paylaştığı için anlaşıyorlar. Çerçeve tanımı değişirse `PROTOCOL_VERSION` artar (§1) ve tüm başlıklar aynı sürümü çalıştırmak zorundadır.

## 4. UDP Beacon (sunucu → 47820 broadcast, her 2 sn)

Hem `255.255.255.255` hem her arayüzün subnet-broadcast adresine gönderilir:

```json
{ "app": "VortexArena", "ver": 1, "ip": "192.168.1.10", "controlPort": 47821, "statePort": 47822, "serverId": "GUID-string" }
```

İstemci `app == "VortexArena"` doğrular. Android'de beacon dinlemek için **MulticastLock** gerekir (cosmos `ServerLocator.cs` çözümü port edilir).

**Rol başına keşif akışı (istemci davranışı):**

| Rol | Adres nereden gelir |
|---|---|
| `player` (Quest) | **komut satırı `--server-ip <ip> [--server-port <port>]`** > PlayerPrefs (elle girilmiş) > **beacon** > `StreamingAssets/arena.json`. Bulunan adrese **otomatik bağlanılır**; oyuncuya sorulmaz. VR build'ine argüman geçilmediği için pratikte beacon kazanır. Hiçbiri yoksa lobide sağ kumandada **joystick 1 sn basılı tutularak** gizli IP paneli açılır ve elle girilen değer beacon'ı ezer (PlayerPrefs'e kalıcı yazılır). |
| `admin` (Windows) | **Yalnız komut satırı:** `--server-ip <ip> [--server-port <port>]` — operatör launcher'ı geçer. Beacon/PlayerPrefs kullanılmaz, kullanıcıya IP sorulmaz. Argüman yoksa bağlanmaz ve ekranda sebebini yazar. |

> **Zincir rolden bağımsızdır:** `AppBoot` komut satırı adresini **her rolde** okur; verilmişse keşfin en üstünde yer alır (açıkça verilen adres kazanır, gelen beacon onu ezmez). **Editörde** rol ve adres komut satırı yerine `Tools > VortexArena > Development > Dev` penceresinden gelir (`EditorPrefs` — sahnede rol/IP override alanı YOKTUR). Maç verisi (mod / takım / süre / limit) **yalnız sunucudan** gelir: editörün enjekte ettiği bir yol yoktur.

> **Bağlantı kurulamazsa:** istemci bağlantısızlık ~3 sn sürdüğünde tasarımlı bir hata ekranı gösterir (`ConnectionOverlay`, VR + masaüstü): adres biliniyorsa "SUNUCUYA BAĞLANILAMIYOR" + adres + deneme sayacı + son hata, adres hiç yoksa "SUNUCU BULUNAMADI". Sunum katmanıdır, protokolü etkilemez; yeniden deneme kuralı `RECONNECT_BACKOFF`'tur (§1).

## 5. WS kontrol mesajları (JSON, text)

**Zarf kuralı:** her mesajda `"type"` alanı. Alıcı önce yalnız `{"type":"..."}` parse eder, sonra tipe göre tam DTO'ya parse eder. **Bilinmeyen type → logla ve yok say** (ileri sürüm uyumluluğu).

### 5.1 İstemci → Sunucu

**`hello`** — bağlantı açılır açılmaz, bir kez:
```json
{ "type": "hello", "protocolVersion": 1, "role": "player",
  "deviceId": "...", "deviceName": "...", "appVersion": "0.1.0",
  "currentScene": "Lobby", "scenes": ["Boot","Lobby","<Arena>","<Arena2>"] }
```
`scenes` = build listesinden runtime'da toplanır (`SceneUtility.GetScenePathByBuildIndex`) → admin katalog doğrulaması bunu kullanır.

**`status`** — her 5 sn: `{ "type":"status", "scene":"<Arena>", "battery":0.87, "ctrlL":1, "ctrlR":3, "fps":71.6, "rosterVersion":42, "rttMs":14, "jitterMs":3.2, "lossPct":0.4 }`

**`ctrlL`/`ctrlR`** = sol/sağ **kumandanın durumu** (`ArenaProtocol.CONTROLLER_*`). Değerler:

| Değer | Sabit | Anlam |
|---|---|---|
| `0` | `CONTROLLER_UNKNOWN` | Bildirilmedi (admin kaydı, bildirmeyen istemci) |
| `1` | `CONTROLLER_OK` | Bağlı ve izleniyor |
| `2` | `CONTROLLER_UNTRACKED` | Bağlı ama pozu geçersiz (görüş dışı / uykuda) |
| `3` | `CONTROLLER_LOST` | Hiç bağlı değil — pil bitti ya da kapandı |

⚠️ **`0` neden "bilinmiyor":** JSON'da atanmamış `int` `0`'dır; `0`'ı `OK` yapmak bu alanı hiç
doldurmayan her kaydı (admin, eski istemci) sağlıklı gösterirdi. `battery = -1` sözleşmesiyle aynı
desen — "bilinmiyor" değeri geçerli ölçümlerin aralığının dışında durur.

⚠️ **Kumandanın pil YÜZDESİ telde YOKTUR ve eklenmez.** Quest'te OpenXR altında okunamıyor
(`OVRInput.GetControllerBatteryPercentRemaining` kullanımdan kalktı ve daima `0` döner; Unity OpenXR
sağlayıcısı bu veriyi hiç yayınlamaz). Okunamayan bir sayıyı telde taşımak sahada **"%0 yazıyor ama
kumanda çalışıyor"** olarak okunurdu; taşınan şey bu yüzden bir yüzde değil, bir **durumdur**.
`battery` alanı **gözlüğün** pilidir, kumandanın değil.

⚠️ Bu iki alan `rttMs`/`fps`'in aksine **roster yayını TETİKLER** ve `PlayerInfo`'da da taşınır
(§5.3): kesikli bir durumdur, sürekli değişen bir sayı değil — bir oyuncunun kumandası düştüğünde
operatörün bunu bir sonraki roster değişikliğine kadar beklememesi gerekir.

> **`rttMs`/`jitterMs`/`lossPct` (v5)** — ağ telemetrisi; **ölçen taraf İSTEMCİDİR** (§6.7), sunucu yalnız taşır. `-1` = bilinmiyor (`0` değil: 0 ms gerçekten mümkün bir ölçüm gibi okunur). Ayrı bir kanal açılmadı çünkü bu mesaj zaten 5 sn'de bir gidiyor ve operatör göstergesi için o ritim fazlasıyla yeterli.
> ⚠️ **Bu üç alan `PlayerInfo`'ya GİRMEZ** (yani `lobby_state` roster'ında taşınmaz). Sürekli değişen sayılar oldukları için sunucudaki "görünen bir alan gerçekten değişti mi" kapısını her `status`'ta açar ve **her status'u bir tam roster yayınına** çevirirler — çözülmüş bir hata geri gelir. `fps` tam bu sebeple `PlayerInfo`'da yok; izlenecek emsal odur. Adminlere ayrı ve kaybı zararsız bir kanaldan gider: `net_stats` (§5.3).

`rosterVersion` = istemcinin **uyguladığı son** `lobby_state.version`'ı (§5.3). Sunucu istemci geride
kalmışsa — ve **yalnız o bağlantıya** — tam bir `lobby_state` yollar; güncelse hiçbir şey yapmaz.
Bu bir **yedek uzlaştırma ağıdır, birincil yol değil**: kontrol kanalı WS/TCP olduğu için bir
`lobby_state` "ping yüzünden düşmez", ya sırayla teslim edilir ya bağlantı ölür. Alan, istemcinin bir
yayını uygulayamadığı pencereleri (sahne geçişi, kopma anı) kapatır. Alanı hiç göndermeyen istemci
`0` yollar → sunucu tam snapshot ile yanıtlar.

**`set_ready`** `{ "type":"set_ready", "ready":true }` (yalnız player)
**`set_team`** — **yalnız admin** (§5.2'ye taşındı). Oyuncu kendi takımını seçemez.

**`set_identity`** `{ "type":"set_identity", "playerId":5, "name":"ertu", "number":7 }` — oyuncunun
**adı ve/veya numarası** (§2). Boş string ya da `0` bırakılan alan **mevcut değerini korur**
(`set_selection` ile aynı konvansiyon) → "yalnız numarayı değiştir" tek mesajdır. Yetki: oyuncu
yalnız KENDİ `playerId`'si için, admin herkes için (`playerId:0` = "kendim"). Numara `1..99`
dışındaysa veya **çevrimiçi** bir oyuncuda kullanılıyorsa reddedilir (§2); değer `devices.json`'a
kalıcı yazılır.
> ⚠️ **`set_name` diye bir mesaj YOKTUR ve eklenmez** — ad ve numara tek kapıdan
> (`set_identity`) yönetilir; ikinci bir kapı iki doğruluk kaynağı olurdu.

> ⚠️ **`shot_fired` diye bir WS mesajı YOKTUR ve eklenmez.** Atış WS/JSON'da değil, UDP olay
> kanalındadır: `0x03 FireEvent` (§6.4). Gerekçe: 600 RPM = 10 atış/sn/oyuncu; 16 oyuncu tam ateşte
> sunucu saniyede ~2400 WS mesajı serileştiriyordu ve bu yük hasar/can/faz ile **aynı güvenilir TCP
> kanalını** paylaşıyordu. Atış bir sunum olayıdır — kaybı kozmetiktir, güvenilirlik gerektirmez.

**`hit_report`** — istemci bir oyuncuya hasar verdiğinde (mermi, balta, ok, patlama, çevre — kaynağı fark etmez):
```json
{ "type":"hit_report", "seq":124, "targetPlayerId":5, "weaponId":"ak47",
  "damage":25.0, "hitPos":[0.4,1.5,2.2] }
```
`hitPos` arena uzayında. **`damage` istemcinin hesapladığı değerdir ve sunucu onu aynen uygular** — sunucuda silah tablosu YOKTUR (§10.3). `weaponId` yalnız bir etikettir (kill feed / istatistik), doğrulanmaz: yeni bir silah/hasar kaynağı eklemek için sunucuya hiçbir şey tanıtmak gerekmez. Sunucu yalnız durum tutarlılığını kontrol eder (faz, atıcı/hedef canlı mı, dost ateşi). Geçerse hasarı uygular ve `health_update` yayınlar. **İstemci hasarı yerel uygulamaz** — `health_update` bekler.

Alan etkisi (bomba, el bombası) ayrı bir mesaj tipi gerektirmez: patlamayı gören istemci **etkilenen her hedef için bir `hit_report`** yollar, mesafeye göre düşen hasarı kendisi hesaplar. Aynı şekilde yaydaki çekiş gücü, kafa vuruşu çarpanı veya düşme hasarı da istemci tarafında hesaplanıp `damage` alanına yazılır.

**`revive_request`** `{ "type":"revive_request" }` — ölü oyuncu, `respawn.delaySeconds` dolduktan **ve** modun canlanma şartını sağladıktan (taban bölgesine girme ya da sabit durma) sonra gönderir; sunucu koşulları doğrulayıp canlandırır (§10.4). Free-roam'da oyuncu ışınlanamadığı için canlanma bir **konum değişimi değil, durum değişimidir**.

**`set_calibration`** `{ "type":"set_calibration", "calibrated":true, "source":"manual", "floorOffset":0.07, "error":"" }` (yalnız player) — başlık **kendi** hizalama durumunu bildirir (§10.6). `source` ∈ `"manual"` (kumandada elle: A basılıyken B'ye çift basış) · `"anchor"` (kayıtlı `OVRSpatialAnchor`'dan geri yükleme) · `"cloud"` (ileride: paylaşılan uzamsal anchor). **`source` doğrulanmaz**, yalnız kaydedilip roster'da yayılır — `weaponId` gibi serbest etikettir, yeni bir kaynak eklemek sunucuda iş çıkarmaz. `calibrated:false` de gönderilebilir (başlık kendi hizalamasını geçersiz kıldıysa).

`floorOffset` = elle kalibrasyonda kumanda ucunun **yakalama anındaki tracking-yerel yüksekliği**
(metre, **işaretli**): sistemin zemin tahmininin gerçek zeminden sapması. Kumanda ucu fiziksel
zemine değdiği için sıfırdan farklı her değer doğrudan o tahminin hatasıdır. Kayıtlı çapadan geri
yüklemede `0` gönderilir — orada bir ölçüm yoktur. Sunucu değeri yorumlamaz: roster'a yazar
(`PlayerInfo.floorOffset`, §5.3) ve `CALIB_FLOOR_WARN_METERS` eşiğini aşarsa operatörü uyarır
(§10.6). ⚠️ **Bir kapı değildir** — sapma ne olursa olsun kalibrasyon kabul edilir; oyuncuyu
savaş dışı bırakmak operatörün kararıdır.

`error` = **hizalama yeniden yüklenemedi** (boş = sorun yok). Sözleşmesi `set_body_scale.error` ile
birebir aynıdır: doluysa `calibrated`/`source`/`floorOffset` **YOK SAYILIR**, sunucudaki kayıtlı
kalibrasyon **aynen durur** ve gerekçe roster'a yazılıp (`PlayerInfo.calibrationError`, §5.3)
adminlere duyurulur. Bugünkü tek üreticisi operatörün `reload_calibration` komutudur (§5.2/§10.6):
denemenin düştüğünü söyleyen kanal budur. ⚠️ **Doğrulanmayan serbest metindir** —
`calibrationSource` ile aynı sözleşme: hata kodu listesi YOKTUR ve eklenmez, tek tüketicisi
operatörün ekranıdır ve yeni bir başarısızlık türü sunucuda iş çıkarmamalıdır.

**`set_body_scale`** `{ "type":"set_body_scale", "scale":1.04, "error":"" }` (yalnız player) — başlık **kendi**
gövde ölçeğini bildirir (§10.8). `playerId` taşımaz, bağlantıdan çözülür (`set_calibration` ile aynı
sözleşme). Ölçümü istemci yapar, sunucu **yorumlamaz**: yalnız `[BODY_SCALE_MIN, BODY_SCALE_MAX]`
aralığına kırpar ve roster'da yayar.

`error` = ölçüm başarısızsa insan okuyabilir gerekçesi (boş = başarılı ölçüm). **Doluysa `scale`
YOK SAYILIR ve kayıtlı ölçek DEĞİŞMEZ**: gerekçe adminlere duyurulur ve roster'a yazılır
(`PlayerInfo.scaleError`, §5.3). Başarısızlığı hiç bildirmemek operatörü *"bastım, bir şey olmadı"*
durumunda bırakır; başarısız ölçümü ölçek olarak yazmak ise sessizce yanlış bir avatar boyu
üretirdi (§10.8).

### 5.2 Yalnız admin → Sunucu

- **`start_match`** `{ "type":"start_match", "modeId":"tdm", "sceneName":"<Arena>", "roundSeconds":600, "scoreLimit":30, "countdownSeconds":10 }`
  `roundSeconds`/`scoreLimit`/`countdownSeconds` **o maça özeldir**: `0` ya da eksikse modun kendi varsayılanı (`IGameMode.DefaultRoundSeconds`/`DefaultScoreLimit`, geri sayımda `COUNTDOWN_SECONDS`) kullanılır. Operatörün arayüzde seçtiği değerler buradan geçer; `ROUND_SECONDS_OPTIONS` yalnız arayüz listesidir, sunucu her pozitif değeri kabul eder.
  ⚠️ **`scoreLimit` ÜÇ değerlidir** ve bu yüzden `≤ 0` ile okunmaz: `> 0` = o limit · `0` = modun varsayılanı · **`SCORE_LIMIT_UNLIMITED` (`-1`) = sınırsız** — maçın skor/tur limiti yoktur, bitişi süre ya da operatörün `abort_match`'i belirler. Sunucu sentineli **olduğu gibi taşır** (0'a çevirmez): `load_match`/`admin_state` onu geri yayar, yani panelde "mod varsayılanı" ile "sınırsız" ayırt edilebilir kalır. Her negatif değer `SCORE_LIMIT_UNLIMITED`'a normalize edilir (`ArenaProtocol.NormalizeScoreLimit`), yani telde tek bir "sınırsız" yazımı vardır.
  Sınırsız ayrı bir kural dalı AÇMAZ: sunucudaki limit kapılarının tamamı zaten `limit > 0` diye sorar (`TdmMode`/`FfaMode`/`TournamentMode`), yani sentinel hepsini birden kapatır. Tur tabanlı modda (`tournament`, §10.5) **tur tavanı da kalkar** — tavan `2 × limit − 1` olarak limitten türüyor.
  ⚠️ **`PROTOCOL_VERSION` bu değer için ARTMAZ:** mevcut bir alanın yeni bir değeridir (tel formatı aynı) ve tanımayan eski bir sunucu `> 0` kapısında kalıp modun varsayılanını kullanır — kaybolan şey bozuk bir çizim değil, uygulanmamış bir operatör seçimidir. Admin ile sunucu aynı depodan birlikte dağıtıldığı için kabul edilir; oyuncu istemcisi bu alanı hiç okumaz.
  `countdownSeconds` sunucuda `[COUNTDOWN_SECONDS_MIN, COUNTDOWN_SECONDS_MAX]` aralığına **kırpılır** ve maç boyunca **her** geri sayımda kullanılır — tur tabanlı modlarda (`tournament`, §10.5) turlar arasındaki geri sayım da budur. Oyuncu istemcisine ayrı bir alan olarak GİTMEZ: `countdown{seconds}` zaten her saniye gerçek değeri taşır.
- **`abort_match`** `{ "type":"abort_match" }`
- **`pause_match`** `{ "type":"pause_match" }` — koşan maçı dondurur: `playing` → `paused` + `phaseReason:"operator"` (§10.1). Süre durur, hasar kapanır, skorlar ve `modeState` **korunur**. **Yalnız `playing` iken iş yapar**; başka fazda loglanıp yok sayılır (duraklı bir maçı duraklatmanın anlamı yok).
- **`resume_match`** `{ "type":"resume_match" }` — `paused`/`operator`'dan `playing`'e döner; süre kaldığı yerden akar, canlar/skorlar sıfırlanmaz. ⚠️ **Yalnız operatörün duraklattığı maç sürdürülebilir:** `phaseReason` `loading`/`countdown`/`mode`/`lobby` iken reddedilir. Sebep: o duraklamaların sahibi operatör değildir — modun istediği duraklamayı (`mode`) operatörün kaldırması modun ara durumunu bozar, geri sayımı elle bitirmek de yükleme kapısını atlar. Her duraklamayı kendi sahibi kaldırır.
- **`set_team`** `{ "type":"set_team", "playerId":5, "team":"blue" }` (`"red"|"blue"`) — hedef oyuncunun takımı. **Faz kapısı YOKTUR:** operatör `playing` dahil her fazda, sunucuya bağlı herkesin takımını değiştirebilir; değişiklik `lobby_state` ile yayılır ve istemcide anında geçerlidir (taban bölgesi, arayüz renkleri). Hedef admin ise reddedilir. Oyuncudan gelen `set_team` loglanıp yok sayılır — **oyuncu kendi takımını seçemez, bunun için protokol mesajı YOKTUR ve eklenmeyecektir.**
- **`set_friendly_fire`** `{ "type":"set_friendly_fire", "enabled":true }` — dost ateşi anahtarı (§10.5). **Faz kapısı YOKTUR:** operatör `playing` dahil her fazda basabilir ve etkisi anlıktır — gerekçe `set_team` ile aynıdır: operatör sahadaki durumu maçı iptal etmeden düzeltebilmeli. Değer sunucuda yaşar (açılışta `false`), yürürlükteki kural şekline damgalanır ve koşan maçta `rules_update` ile herkese yayılır (§5.3). Maç başlangıcı, harita sahneleme ve lobiye dönüş anahtarı **sıfırlamaz** (süre/limit seçimiyle aynı sözleşme); sıfırlayan tek şey sunucunun yeniden başlatılmasıdır. Oyuncudan gelirse loglanıp yok sayılır.
  ⚠️ **Neden `set_selection` alanı değil:** o mesaj "boş/`0` = dokunulmadı" sözleşmesiyle çalışır ve bir `bool` "dokunulmadı"yı ifade edemez. Aynı sebeple seçim kilidine (§10.7 "ne zaman serbest") de takılmaz — bu bir sonraki maçın seçimi değil, o anın durumudur.
- **`set_calibration_mode`** `{ "type":"set_calibration_mode", "mode":"two_anchor" }` — oyuncu
  başlıklarının **açılışta nasıl hizalanacağı** (§10.6). `set_friendly_fire` ile aynı sınıftadır:
  **anlık bir komuttur**, `set_selection`'a binmez, seçim kilidine girmez ve koşan maçta da
  değiştirilebilir. Değer sunucuda yaşar (açılışta `CALIB_MODE_TWO_ANCHOR`) ve `admin_state` ile
  tüm adminlere yayılır.

  | `mode` | Anlam |
  |---|---|
  | `"two_anchor"` | Oyuncu her uygulama açılışında elle 2 çapa kalibrasyonu alır; diskteki çapa UUID'si **hiç okunmaz** |
  | `"saved_anchor"` | Başlık açılışta kayıtlı `OVRSpatialAnchor`'dan hizalamayı geri yükler |
  | `"anchor_cloud"` | **Rezerve** — sunucu KABUL ETMEZ (loglar, durum değişmez); arayüzde de pasiftir |

  ⚠️ **Bilinmeyen/boş değer REDDEDİLİR** (loglanır, durum değişmez) — kural değerlerinin
  "bilinmeyen → varsayılana düş" sözleşmesi burada geçerli DEĞİLDİR: bu bir kural şekli değil bir
  operatör kararıdır ve sessizce varsayılana dönmek, operatöre bastığı düğmenin uygulandığını
  gösterirdi.
- **`kick`** `{ "type":"kick", "playerId":5 }` — hedef bağlantı kapatılır ve **o başlıkta uygulama kapanır** (kapanış dizisi §5.4).
- **`identify`** `{ "type":"identify", "playerId":5 }` → o cihazda kimlik overlay'i (cosmos deseni)
- **`clear_calibration`** `{ "type":"clear_calibration", "playerId":5, "keepSaved":true }` — o oyuncunun kalibrasyonunu **sıfırlar** (§10.6). **`playerId:0` = TÜM oyuncular** (toplu sıfırlama). Admin kalibrasyonu yalnız SIFIRLAYABİLİR, "kalibre oldu" diye işaretleyemez — hizalamanın gerçekten oturduğunu yalnız başlık bilir (§10.6).

  `keepSaved` (bool) sıfırlamanın **kapsamını** seçer; operatörün iki ayrı eylemi budur:

  | Kip | Alan | Başlıkta ne olur | Sonrası |
  |---|---|---|---|
  | Hizalamayı geçersiz kıl | `keepSaved:true` | Hizalama düşer, yarım kalmış elle kalibrasyon sekansı silinir, elle kalibrasyon kapısı açılır. **Kayıtlı `OVRSpatialAnchor` ve UUID KORUNUR** | `reload_calibration` çalışır — operatör oyuncuyu kayıttan geri kurabilir |
  | Cihaz kaydını da sil | `keepSaved:false` | Yukarıdakilerin hepsi + kayıtlı çapa cihazdan silinir, UUID kalıcı olarak silinir | `reload_calibration` "cihazda kayıtlı kalibrasyon yok" ile başarısız olur; oyuncu elle A/B sekansı almak zorundadır |

  ⚠️ **Alanın YOKLUĞU `keepSaved:false` demektir** (sert kip). Gerekçe geri uyumluluktur: alanı
  tanımayan bir uç bugünkü davranışı sürdürür, sürpriz yapmaz — kural değerlerinin
  "bilinmeyen → varsayılana düş" sözleşmesiyle aynı sınıftadır.
  ⚠️ **Sunucu için iki kip AYNIDIR:** roster etkisi (`calibrated:false`, `floorOffset`/`bodyScale`/
  hata alanlarının sıfırlanması) kipe bakmaz, sunucu alanı yalnız hedefe **iletir**. Fark tümüyle
  başlıktadır.
  ⚠️ **Sunucu komutu hedefe KOŞULSUZ iletir** (`identify`/`measure_body_scale` ile aynı çift yönlü desen, §5.3): roster'daki `calibrated` zaten `false` olsa bile hedef başlığa bir `clear_calibration` gider (tek alanı iletilen `keepSaved`'dir). Sebep, sıfırlanacak her şeyin roster'da görünmemesidir — **yarım kalmış elle kalibrasyon** (A alındı, B alınmadı) yalnız başlıkta yaşar ve telde hiçbir izi yoktur. Sunucu "değer zaten `false`, değişen bir şey yok" diye erken dönseydi komut tam da düzeltmek için var olduğu durumda hiçbir iş yapmazdı (§10.6).
- **`reload_calibration`** `{ "type":"reload_calibration", "playerId":5 }` — o oyuncunun başlığına
  **gözlükte KAYITLI çapadan hizalamayı yeniden yükletir** (§10.6). **`playerId:0` = TÜM oyuncular.**
  Sunucu bir şey hesaplamaz; hedefe **alansız** bir `reload_calibration` iletir (`identify` /
  `measure_body_scale` ile aynı çift yönlü desen) ve sonucu başlık `set_calibration` ile döner:
  başarıda `calibrated:true, source:"anchor"`, başarısızlıkta dolu `error` (§5.1). Operatörün
  bastığı düğmenin cevabı ayrıca `calibration_result` ile adminlere gider (§5.3).
  ⚠️ **`measure_body_scale`'in aksine kalibresiz hedef ATLANMAZ:** komutun var olma sebebi tam da
  hizalaması olmayan ya da bozulmuş oyuncudur — kalibrasyon kapısı komutu var olduğu durumda
  işlevsiz bırakırdı.
- **`measure_body_scale`** `{ "type":"measure_body_scale", "playerId":5 }` — o oyuncunun gövde
  ölçüsünü **şimdi aldırır** (§10.8). **`playerId:0` = TÜM oyuncular.** Sunucu bir şey hesaplamaz;
  hedefe alansız bir `measure_body_scale` iletir (`identify` ile aynı çift yönlü desen) ve ölçümü
  başlık yapıp `set_body_scale` ile döner. ⚠️ **Kalibresiz oyuncuya iletilmez:** ölçü arena zeminine
  göredir, kalibresiz başlıkta zemin bilinmiyor — atlanan hedefler `admin_state.notice` ile bildirilir.
- **`revive_player`** `{ "type":"revive_player", "playerId":5 }` — ölü oyuncuyu **operatör
  canlandırır** (§10.4). **`playerId:0` = o an ölü olan TÜM oyuncular** (`clear_calibration` ile aynı
  toplu-hedef deseni). Sunucuda `MatchDirector` işler (`hp`/`alive` maç durumudur), canlandırmayı
  `revive_request` ile **aynı kod yolu** yapar: `hp=PLAYER_MAX_HP`, `alive=1`, `health_update{hp:100,
  attackerId:0}`. **Skor defterine ve `deaths` sayacına dokunmaz** — canlandırma bir düzeltmedir,
  ölümü geri almaz.

  Komut `revive_request`'in yasaklarından ikisini bilerek geçer, ikisine tabidir:

  | Kapı | Operatör komutunda | Gerekçe |
  |---|---|---|
  | Faz `playing` | uygulanır | Başka fazda ölü oyuncu kavramı yoktur; `playing`'e girişte zaten herkes canlanır |
  | `reviveAnchor:"none"` (turnuva, §10.5) | **GEÇİLİR** | Komutun varlık sebebi: takılan oyuncu her modda kurtarılabilmeli. ⚠️ Tur sonucunu değiştirir (tur, bir takımın tamamı ölünce biter) — operatörün bilinçli kararıdır |
  | Canlanma gecikmesi (`rules.respawnDelay`) | **GEÇİLİR** | Operatör beklemez, komut anında uygulanır |
  | Kalibrasyon (§10.6) | uygulanır | Kalibresiz oyuncu ateş edemez ve vurulamaz; canlandırmak onu savaşa döndürmez, yalnız tabloda "canlı" gösterir — yanıltıcı olur |
  | Engelin içinde (§10.9) | uygulanır | Engelin içinde canlanan oyuncu kör kalır ve tolerans dolar dolmaz yeniden ölmeye başlar; komut bir ölüm döngüsü üretirdi |

  ⚠️ **Geçilen iki kapının gerekçesi ürün kararı, geçilmeyen ikisininki fizikseldir** — mod kuralı ve
  bekleme süresi operatörün üstlenebileceği şeylerdir, kalibresiz ya da engele gömülü bir oyuncuyu
  canlandırmak ise gözle görülür bir yalan üretir.

  **Reddedilen komut istemciye ret mesajı GÖNDERMEZ**, sunucu konsoluna gerekçesiyle tek satır yazar
  (§ Sunucu README konsol tablosu). Operatör satırın canlanmadığını roster'da zaten görür; ayrı bir
  ret kanalı açmak tek tüketicisi konsol olan bir mesaj tipi üretirdi.

  ⚠️ **`PROTOCOL_VERSION` bu komut için ARTMAZ:** tipi tanımayan bir sunucu mesajı `default` dalında
  sessizce düşürür, eski admin komutu hiç göndermez. Bedeli, eski sunucuya karşı komutun sessizce
  hiçbir şey yapmasıdır — admin ile sunucu aynı depodan birlikte dağıtıldığı için kabul edilir.
- **`return_to_lobby`** `{ "type":"return_to_lobby" }`
- **`set_selection`** `{ "type":"set_selection", "modeId":"tdm", "sceneName":"<Arena>", "roundSeconds":600, "scoreLimit":30, "countdownSeconds":10 }` — bir sonraki maçın **ortak** mod/harita/süre/limit/geri sayım seçimi. Maçı BAŞLATMAZ; yalnız sunucudaki seçimi günceller ve sunucu bunu `admin_state` ile tüm adminlere yayar (çoklu admin senkronu, §5.3). Boş string veya `0` bırakılan alan mevcut değerini korur. Seçim maç bitiminde sıfırlanmaz — operatör aynı haritayı tekrar başlatabilsin.
  ⚠️ **`sceneName` yalnız operatör harita/mod imlecini gerçekten oynattığında doldurulur** (süre/limit dokunuşunda boş gider): dolu harita alanı sahnelemeyi tetikler (§10.7), yani süre değiştirmek herkesi bir arenaya taşırdı.
  ⚠️ **`scoreLimit` "0 = dokunulmadı" sözleşmesinin İSTİSNASIDIR:** `SCORE_LIMIT_UNLIMITED` (`-1`) bir seçimdir — "sınırsız seçildi" der, "bu alana dokunmadım" demez. Sunucudaki kapı bu yüzden pozitiflik değil sıfırdan farklılıktır; pozitiflik kapısı sınırsız seçimini sessizce yutar ve diğer operatörün panelinde hiç görünmezdi.
  **Neden maç parametreleri de ortak:** iki operatör aynı ekranı görmezse biri 5 dk sandığı maçı 30 dk başlatır. Süre/limit *operasyonel* durumdur, görünüm tercihi değil (§5.3 son madde).
  ⚠️ **`sceneName` yalnız bir not değil, anlık bir sahne komutudur:** harita değiştiğinde sunucu o arenayı **sahneler** — TÜM istemciler (oyuncular + adminler) oraya geçer (§10.7). Bu yüzden **`modeId`/`sceneName` yalnız MAÇ KURULMAMIŞKEN kabul edilir**, yani tam iki durumda: `finished` (maç bitti, sıradaki seçilebilmeli) ve `paused` + `phaseReason:"lobby"`. Diğer her durumda ikisi de **düşürülür** — `playing` ama aynı zamanda `paused` + `loading`/`countdown`/`operator`/`mode`: BAŞLAT'a basıldığı andan itibaren maç kuruludur, kurulmakta olanın (yükleme/geri sayım) ya da donmuş olanın (operatör/mod duraklatması) altından sahne çekilemez. Düşürülen alanlar için komutun süre/limit kısmı yine işlenir (onlar sahne yüklemez), konsola sebep yazılır ve `admin_state` mevcut seçimle geri yayınlanır — iyimser davranan panelin imleci sunucunun değerine çekilsin. Kurulmuş bir maçın haritasını değiştirmek diye bir şey yoktur; önce `abort_match`, sonra yeni harita `start_match` ile gelir.
  Aynı kuralı istemci de **arayüz kapısı** olarak uygular (`AdminRoster.CanChangeSelection` → mod/harita seçicileri pasif). Otorite yine sunucudadır: arayüz kapısı yalnız operatörü boşuna tıklatmamak içindir, bayat/yarışan bir panelin komutunu sunucu keser.

Sunucu, `role != "admin"` bağlantıdan gelen admin komutunu loglayıp yok sayar.

### 5.3 Sunucu → İstemci

**`welcome`** — hello yanıtı:
```json
{ "type":"welcome", "protocolVersion":3, "playerId":3, "udpToken":123456789,
  "calibrationMode":"two_anchor",
  "match": { "phase":"paused", "phaseReason":"lobby", "modeId":"lobby", "modeState":"",
             "sceneName":"<Lobi>", "sceneElapsed":137.4,
             "timeRemaining":0, "scoreRed":0, "scoreBlue":0,
             "rules": { "teamMode":"two", "scoring":"team", "friendlyFire":false,
                        "reviveAnchor":"base", "weaponSource":"weaponcanvas", "respawnDelay":5.0,
                        "fireWhilePaused":true } } }
```
**`match.sceneName` HER ZAMAN doludur ve istemcinin tek yönlendirme kaynağıdır** (§10.1): bağlanan
istemci koşulsuz o sahneyi yükler. Sunucu açık sahnesini çözemiyorsa zaten **açılmaz** (§11) — boş
`sceneName` yalnız bozuk/eski bir sunucudan gelebilir, o durumda istemci kabuk `Lobby` sahnesinde
bekler ve sebebi konsola yazar.

**`match.sceneElapsed`** = o an açık olan sahnenin **kaç saniyedir sahnelendiği** (saniye, sunucu
saati). Sahne değiştiği anda sıfırlanır; maçın başlaması/bitmesi onu **sıfırlamaz** — ölçtüğü şey
maç değil sahnedir. Tek tüketicisi ortam sesinin ortak fazıdır: geç katılan başlık müziği baştan
değil, herkesin bulunduğu yerden açar (`SceneAmbience`, `Docs/Sistem-Ozeti.md` §4). Klip süresinden
uzun bir değer normaldir — istemci klip uzunluğuna göre modunu kendisi alır.
⚠️ Bir **kural/otorite** alanı değildir: kaybı ya da sıfır gelmesi yalnız müziğin baştan
başlamasıdır, bu yüzden `PROTOCOL_VERSION` **artmaz** (alanı hiç göndermeyen eski sunucuya karşı
istemci sessizce eski davranışa düşer — `set_selection`/`selection_state` ile aynı sözleşme).

`match.rules` = o an geçerli kural şekli (§10.5) — geç katılan istemci/admin kendini aynı kurallara
göre kurar. `phase`/`phaseReason`/`modeState` anlamları §10.1'de.

`calibrationMode` = yürürlükteki kalibre modu (§5.2/§10.6). ⚠️ **Oyuncu bu değeri bağlantıda BİR
KEZ çeker:** modun kapıladığı tek şey açılıştaki diskten çapa geri yüklemesidir ve o karar
`welcome` geldiğinde zaten verilmiştir. Bu yüzden **bağlı oyuncuya canlı yayılım YOKTUR** —
operatörün mod değişikliği yalnız bundan sonra bağlananlara işler; sahadaki karşılığı oyuncu
başlığının yeniden başlatılmasıdır.

**`lobby_state`** — roster her değiştiğinde **ve maç sayaçları değiştiğinde** (ölüm/canlanma) TAM anlık görüntü:
```json
{ "type":"lobby_state", "version":42, "players":[
  { "playerId":3, "number":7, "name":"ertu", "role":"player", "team":"red",
    "ready":true, "connection":"connected", "reconnectSeconds":0,
    "battery":0.87, "ctrlL":1, "ctrlR":3, "scene":"<Arena>",
    "kills":4, "deaths":2, "hp":72.0, "alive":true, "score":7,
    "inMatch":true, "calibrated":true, "calibrationSource":"anchor", "floorOffset":0.07,
    "bodyScale":1.04, "scaleError":"", "calibrationError":"" } ] }
```

`version` = **monoton artan** roster sürümü (sunucu ömrü boyunca; sunucu yeniden başlarsa `0`'dan).
İstemci `version <= uyguladığı son sürüm` olan mesajı **atar** ve sürümü her `welcome`'da sıfırlar.
Sunucuda yayın **tek bir yayıncı** üzerinden gittiği için sıra zaten korunur; bu guard ikinci
emniyettir. ⚠️ Gerekçesi ucuz bir "her ihtimale karşı" değil: sürümsüz ve ateşle-unut yayında eski
bir anlık görüntü yeniyi ezebilir ve roster bir sonraki değişikliğe kadar bayat kalır — belirtisi
**"atılan oyuncu hâlâ listede bağlı görünüyor"**dur.

`connection` = `"connected"` | `"reconnecting"` | `"left"` (§2). **Bilinmeyen/boş değer `connected`
sayılır** — kural değerlerinde olduğu gibi (§10.5), böylece ileride dördüncü bir durum eklemek sürüm
artırmaz. `reconnectSeconds` yalnız `reconnecting` iken anlamlıdır: cihazın çıkarılmasına kalan
saniye (`0` = yok). ⚠️ Bu sayı bir **geri sayımdır, bir zaman damgası değil** ve her `lobby_state`'te
yeniden hesaplanır; roster yayını olay tabanlı olduğu için arayüz onu yerelde de tüketmeli (aksi
hâlde sayaç yalnız başka bir değişiklik olduğunda ilerler).

`inMatch` = bu kayıt **koşan maçın katılımcısı** mı (§10.2). Maç sonu tablosunun kapsamı budur:
`left` bir satır yalnız `inMatch` olduğu için listede durur. Maç kapanınca hepsi `false` olur ve
`left` kayıtlar silinir.

`number` = oyuncunun **1..99 forma numarası** (§2); `0` = atanmamış, admin'de daima `0`. **Ad benzersiz
değildir, ayırt edici alan budur** — arayüzlerde `"7 · ertu"` biçiminde gösterilir.
`kills`/`deaths`/`hp`/`alive`/`score` **sunucu-otoriter** maç sayaçlarıdır (§10.2) ve admin gözlemci
arayüzünün tek doğruluk kaynağıdır: yalnız `kill_event`/`health_update` sayılsa admin yeniden
bağlandığında tablo sıfırlanırdı. Maç dışında (`paused`/`lobby`) `hp=PLAYER_MAX_HP`, `alive=true`, sayaçlar 0.

⚠️ **`hp` ve `alive`'ı OYUNCU istemcisi de kendi satırı için uygular** ve bu zorunludur: `welcome`
bu ikisini taşımıyor, yani yeniden bağlanan bir istemci sunucudaki durumunu **başka hiçbir yerden**
öğrenemez. Uygulanmazsa ölüyken kopup dönen oyuncu kendi ekranında canlı ve tam canlı görünür —
ölüm ekranı kapanır, tetik çalışır, mermi düşer — ama sunucu onun her `hit_report`'unu "atıcı ölü"
diye atar; belirtisi teşhis edilemez bir *"vuruyorum ama adam ölmüyor"*dur. Roster **boşluğu
doldurur, akışı ezmez**: `health_update`/`respawn` bir kez geldikten sonra otoriter akış odur
(iki mesaj ayrı yollardan gidiyor, sıraları garanti değil).

`score` = **bireysel** maç skoru (`rules.scoring == "player"` olan modlarda anlamlı; takım
skoru `match_state.scoreRed`/`scoreBlue`'da kalır — §10.5). Bireysel skorun değiştiği an =
öldürmenin olduğu an = roster'ın zaten tazelendiği an, bu yüzden ayrı bir mesaj tipi yoktur.

`ctrlL`/`ctrlR` = o başlığın sol/sağ **kumanda durumu**; değer tablosu ve `0`'ın neden "bilinmiyor"
olduğu §5.1'dedir. Burada taşınmasının sebebi kalibrasyon alanlarınınkiyle aynıdır: kesikli bir
durumdur ve değiştiği an roster'ın zaten tazelendiği andır — telemetri sayılarının (`fps`/`rttMs`)
`PlayerInfo`'ya girmeme gerekçesi buna işlemez. Admin kaydında daima `0` kalır. ⚠️ Bu alanlar
**kumanda pil yüzdesi DEĞİLDİR ve olamaz** (§5.1); `battery` gözlüğün pilidir.

`calibrated`/`calibrationSource` = başlığın hizalama durumu (§10.6). **Aynı gerekçeyle ayrı bir
`calibration_changed` mesajı YOKTUR:** durumun değiştiği an roster'ın zaten tazelendiği andır
(hem `set_calibration` hem `clear_calibration` registry'yi değiştirir → `lobby_state` yayınlanır).
Admin'de her ikisi de `false`/`""` kalır — admin kalibre olmaz, arayüzde "kalibresiz" sayılmaz.
⚠️ **Roster durumu taşır, sıfırlama KOMUTUNU taşımaz:** başlığı sıfırlayan şey bu alanın `false`'a
düşmesi değil, hedefe iletilen `clear_calibration`'dır (§5.2). Roster'a bağlanamamasının sebebi
şudur: yarım kalmış bir elle kalibrasyonda alan **zaten** `false`'tur, yani sıfırlamanın burada
görünür bir deltası yoktur — dinleyen istemci hiçbir şey olmadığını sanır.

`floorOffset` = son **elle** kalibrasyonda bildirilen zemin sapması (metre, işaretli; §5.1).
`0` = ölçüm yok ya da temiz. Mutlak değeri `CALIB_FLOOR_WARN_METERS`'i aşan satır arayüzde ⚠ ile
gösterilir (§10.6). `clear_calibration` bu alanı sıfırlar.

`calibrationError` = son **kayıtlı hizalamayı yeniden yükleme** denemesi başarısız olduysa gerekçesi,
boş = sorun yok (§5.1/§10.6). ⚠️ **Başarılı bir kalibrasyon alanı temizler** (`scaleError` ile aynı
gerekçe: bir kez başarısız olan oyuncunun satırında uyarı sonsuza kadar kalırdı ve operatör sorunun
sürdüğünü sanardı); `clear_calibration` de sıfırlar.

`scaleError` = son gövde ölçümü başarısız olduysa gerekçesi, boş = sorun yok (§5.1/§10.8). Başarılı
bir ölçüm alanı **temizler**; `clear_calibration` de sıfırlar. Kalibrasyon alanlarıyla aynı
sınıftadır (kesikli cihaz durumu) ve aynı sebeple roster'da taşınır: değiştiği an roster'ın zaten
tazelendiği andır.

`bodyScale` = o oyuncunun avatarına uygulanacak **üniform ölçek** (§10.8). **`0` = ölçülmemiş ve
okuyan taraf `1` uygular** — kural değerleriyle aynı sözleşme, alanı hiç göndermeyen bir uç
sessizce doğru davranır. Kalibrasyon alanlarıyla aynı sebepten burada taşınır: değiştiği an
roster'ın zaten tazelendiği andır ve **iskelet kanalına girmez** — 12 Hz'de her karede tekrar eden
bir sabit olurdu.

**`load_match`** `{ "type":"load_match", "modeId":"tdm", "sceneName":"<Arena>", "roundSeconds":300, "scoreLimit":30, "yourTeam":"red", "sceneElapsed":0, "rules":{ … } }`
→ istemci sahneyi yükler, `status`'ta yeni sahne görünür. Sahne yüklenince istemci `set_ready` (yükleme tamam anlamında) gönderir; herkes hazır olunca sunucu `countdown` başlatır. Bu süre boyunca faz `paused`'dur (`phaseReason` sırayla `loading` → `countdown`); **`load_match`'in gelmesi maçın başladığı anlamına GELMEZ** — maç `phase:"playing"` ile başlar.
**Oyuncu ışınlanmaz ve kalibrasyon SIFIRLANMAZ** — harita değişimi oyuncu için yalnız bir sahne değişimidir, fiziksel duruşu ve hizalaması kaldığı yerden devam eder (§10.4).
**Adminlere de gönderilir** (gözlemci sahneyi yüklesin diye) ama `yourTeam:""` ile — admin oynamadığı için takım anlamsızdır ve admin `set_ready` göndermez.
`scoreLimit` burada **yürürlükteki** değerdir (sunucu çözdü): `> 0` = limit, `-1` = sınırsız; "modun varsayılanı" anlamına gelen `0` bu yönde gelmez.
`rules` = bu maçın kural şekli (§10.5). İstemci kendini **buna** göre kurar: takımsız modda `yourTeam` boş gelir, canlanma şartı `reviveAnchor`'dan okunur. İstemcide `if (modeId == "...")` zinciri YOKTUR — mod eklemek istemci kodunu değiştirmez.
`sceneElapsed` = sahnenin kaç saniyedir sahnelendiği (§5.3 `welcome.match.sceneElapsed` ile aynı alan). **Yeni bir sahne sahnelenirken `0`'dır**; aynı sahnede ikinci bir maç başlatılırsa sıfırlanmaz, çünkü ölçtüğü şey maç değil sahnedir — ortam sesi harita değişmedikçe kesilmez.

**`countdown`** `{ "type":"countdown", "seconds":5 }` — 0'a inince faz `playing`.
**`match_state`** — faz/gerekçe değişimlerinde + `playing`'de saniyede 1:
```json
{ "type":"match_state", "phase":"playing", "phaseReason":"", "modeState":"",
  "timeRemaining":287.5, "scoreRed":3, "scoreBlue":5 }
```
Fazlar ve alanların anlamı §10.1'de. `phase` yalnız üç değer alır: `paused` · `playing` · `finished`.

> ⚠️ **WS üzerinde atış relay'i YOKTUR** — atış olayları `0x04 EventBatch` ile gider (§6.5). Relay kapısı (faz + atıcı canlı/kalibreli) aynıdır, yalnız kanal UDP'dir.
**`health_update`** `{ "type":"health_update", "playerId":5, "hp":75.0, "attackerId":3 }`
> ⚠️ **Broadcast DEĞİL** (v5): yalnız **`playerId`'nin sahibine ve adminlere** gider. İki tüketicisi de dar — istemci kendisine ait olmayan her `health_update`'i **zaten düşürüyor**, admin tablosu ise herkesin canını çiziyor. Herkese yayınlandığı dönemde 10 oyunculu bir maçta her isabette 11 mesaj gidip **9'u çöpe** atılıyordu; isabet başına üretildiği için de fan-out'u oyuncu sayısıyla **kare** büyüyen tek WS kanalıydı. Alan düzeni **değişmedi**; `attackerId`'yi bugün okuyan yoktur (yönlü hasar göstergesi için ayrılmıştır ve mesaj artık zaten yalnız kurbana gittiği için doğal yeri burasıdır). Bu, "WS mesajları tanımı gereği herkese gider" varsayımının **bilinçli istisnası**dır → `Docs/Sistem-Ozeti.md` §3.12.

**`kill_event`** `{ "type":"kill_event", "killerId":3, "victimId":5, "weaponId":"ak47" }`
**`respawn`** `{ "type":"respawn", "playerId":5, "delaySeconds":5.0 }` — istemci `delaySeconds` sonra, modun canlanma şartını sağlayınca canlanır (§10.4). Sunucu sahne geometrisini bilmez; canlanma yeri diye bir alan taşınmaz.
**`match_end`** `{ "type":"match_end", "winnerTeam":"blue", "winnerPlayerId":0, "scoreRed":12, "scoreBlue":30 }`
Kazanan **iki kanaldan biriyle** ifade edilir (`rules.scoring`, §10.5): takım skorlu modlarda `winnerTeam` (`"red"|"blue"|""`), bireysel skorlu modlarda `winnerPlayerId` (`0` = yok/berabere). Bir mod ikisini de doldurmaz; okuyan istemci dolu olana bakar.
**`return_to_lobby`** `{ "type":"return_to_lobby", "modeId":"lobby", "sceneName":"<Lobi>", "sceneElapsed":0, "rules":{ … } }` — herkesi sunucunun **açık sahnesine** taşır. Şekli `load_match` ile aynıdır (§10.7): `sceneName` o an açık olan sahne, `modeId`/`rules` o sahnenin profili. Adı tarihseldir — yalnız "lobiye dön" değil, operatörün seçtiği arenayı sahnelemek için de kullanılır (§10.7 Sahneleme).
Aynı mesaj **lobi sahnelemesini** de taşır (§10.7): operatör lobideyken harita seçtiğinde `sceneName` o arenadır. İstemci için ikisi de aynı şeydir — *"lobideyiz, şu sahneyi yükle"* — bu yüzden ayrı bir mesaj tipi YOKTUR. `modeId` her iki durumda da `"lobby"` kalır: sahnenin arena olması fazı değiştirmez.
**`ping`** `{ "type":"ping" }` — istemci `status` ile yanıtlar (ayrı pong yok).
**`identify`** `{ "type":"identify" }` — istemci büyük kimlik overlay'i gösterir (playerId + ad).
**`measure_body_scale`** `{ "type":"measure_body_scale" }` — istemci gövde ölçüsünü alır ve sonucu
`set_body_scale` ile döner (§10.8). Yalnız player'a gider; ölçüm başarısız olursa istemci yine
`set_body_scale` yollar ama `error` alanı **dolu** olur — eski ölçek durur, gerekçe operatöre
gider (§5.1).
**`clear_calibration`** `{ "type":"clear_calibration", "keepSaved":true }` — istemci hizalamayı
**ve yarım kalmış elle kalibrasyon sekansını** siler ve elle kalibrasyon kapısını yeniden açar
(§10.6). Yalnız player'a gider; `playerId` taşımaz — hedef zaten o bağlantıdır. Tek alanı
`keepSaved`'dir: `true` gözlükteki kayıtlı `OVRSpatialAnchor`'ı ve UUID'yi **korur**
(`reload_calibration` çalışmaya devam eder), `false` ikisini de **yok eder**. ⚠️ **Alanın yokluğu
`false` okunur** (§5.2).
⚠️ **Rig TAŞINMAZ:** free-roam'da oyuncu fiziksel olarak neredeyse orada kalır, yalnız hizalama
geçersiz sayılır.
**`reload_calibration`** `{ "type":"reload_calibration" }` — istemci **kayıtlı çapadan hizalamayı
yeniden yüklemeyi dener** ve sonucu bildirir (§10.6): başarıda normal bir
`set_calibration{calibrated:true, source:"anchor"}`, başarısızlıkta `set_calibration{error:"…"}`
(§5.1). Yalnız player'a gider; alan taşımaz — hedef zaten o bağlantıdır.
**`kicked`** `{ "type":"kicked", "reason":"" }` — istemci bağlantıyı kapatır ve **oyuncu başlığında uygulama kapanır**; kapanış dizisi ve gerekçeleri §5.4'te.

**`admin_state`** — **yalnız `role=admin` bağlantılara**; adminler arası ortak durumun tek doğruluk kaynağı:
```json
{ "type":"admin_state", "modeId":"tdm", "sceneName":"<Arena>",
  "venueId":"<Mekan>", "venueScenes":["<Arena>","<Arena2>","<Lobi>"],
  "roundSeconds":600, "scoreLimit":30, "countdownSeconds":10, "friendlyFire":false,
  "calibrationMode":"two_anchor",
  "notice":"Ofis-PC: harita -> <Arena>", "adminCount":2 }
```
- Gönderim anları: admin `hello` yanıtında (welcome'dan hemen sonra, geç katılan admin senkron başlasın), her `set_selection`'da, her admin komutunda (`start_match`/`abort_match`/`pause_match`/`resume_match`/`return_to_lobby`/`kick`/`identify`/`set_team`/`set_friendly_fire`/`set_calibration_mode`), oyuncunun bildirdiği
  zemin sapması eşiği aştığında, gövde ölçümü ya da kayıtlı hizalamanın yeniden yüklenmesi
  başarısız olduğunda (§10.6/§10.8) ve admin
  bağlanıp ayrıldığında. ⚠️ `pause_match`/`resume_match` için duyuru **yalnız komut gerçekten uygulandıysa** yayılır — reddedilen komut diğer operatörlerin ekranına olmamış bir eylemi yazmamalı.
- `modeId`/`sceneName` = ortak seçim. ⚠️ **Hiçbir zaman boş değildir:** sunucu açılışta seçimi **mekanın lobi haritasıyla** tohumlar (`modeId:"lobby"`, `sceneName:<mekanın lobisi>` — §10.7 açık sahnenin açılış değeri), sonrasında da boş alan mevcut değeri koruduğu için seçim bir daha boşalamaz. Böylece ilk `admin_state`'i alan admin de "hiç harita seçilmemiş" bir durum görmez. Admin arayüzü **kendi yerel seçimini değil bunu gösterir**; gelen değer arayüzdeki mod/harita seçicisini günceller. Yani bir operatör haritayı değiştirdiğinde diğerinin ekranı da (paneli açık olmasa bile) o haritaya döner — sahneyi zaten `return_to_lobby` sahnelemesi taşır (§10.7), `admin_state` yalnız seçiciyi hizalar.
- `roundSeconds`/`scoreLimit`/`countdownSeconds` = bir sonraki maçın ortak parametreleri (`0` = hiç seçilmedi, modun/protokolün varsayılanı kullanılacak). Mod/harita ile aynı kanaldan gider — sebebi §5.2 `set_selection` notunda. `scoreLimit` burada da üç değerlidir: `-1` = **sınırsız seçildi** (panel bunu "sınırsız" yazar, "mod varsayılanı" değil).
- `friendlyFire` = dost ateşi anahtarının **o anki** değeri (§5.2). Bir seçim değil **yürürlükteki durum**dur: koşan maçta da geçerli olduğu için diğer alanların "`0`/boş = değişmedi" sözleşmesine girmez ve mod/harita seçicisinin kilidine takılmaz — panelde maç kuruluyken de basılabilir.
- `calibrationMode` = kalibre modunun **o anki** değeri (§5.2/§10.6). `friendlyFire` ile aynı
  sınıftadır: bir seçim değil yürürlükteki durumdur, mod/harita seçicisinin kilidine takılmaz.
  Oyuncuya buradan gitmez — o değeri `welcome`'da bir kez alır.
- `notice` = son admin eyleminin insan okuyabilir özeti (`"<admin adı>: <eylem>"`), tüm adminlerin durum satırında görünür. Boş olabilir.
- `adminCount` = o an çevrimiçi admin sayısı.
- `venueId`/`venueScenes` = sunucunun açılışta seçtiği mekan ve o mekanın sahne adları (§11.1). Oturum boyunca değişmez ama her `admin_state`'te taşınır ki geç bağlanan admin de ilk mesajda hangi arenaları görebileceğini öğrensin. **Admin harita seçicisi kendi yerel kataloğunu bununla süzer**: katalog tüm projeyi tanır, oynatılabilir olana sunucu karar verir. Boş gelirse süzme yapılmaz.
- **Yalnız operasyonel durum senkronlanır.** Görünüm tercihleri (kamera kipi, seçili oyuncu, halka/ad etiketi, kamera hızı, çatı saydamlığı) her admin'in **kendi ekranına** aittir, protokole girmez ve `PlayerPrefs`'te yerel kalır.

**`selection_state`** — **HERKESE** (oyuncular dahil); seçilen modun **sunuma** ait tek alanı:
```json
{ "type":"selection_state", "modeId":"tdm", "teamMode":"two" }
```
- Gönderim anları: her bağlantının `welcome`'ından hemen sonra (rolü ne olursa olsun) ve **seçilen
  mod değiştiğinde** (`set_selection` / `start_match`). Harita, süre, limit değişimi bu mesajı
  ÜRETMEZ — oyuncunun tek kullandığı alan `teamMode`'dur.
- `modeId` = ortak seçim (`admin_state.modeId` ile aynı değer; açılışta `"lobby"`).
- `teamMode` = `"two"` | `"none"` — seçili modun takım kipi (§10.5 sözlüğü). Sunucu kayıtlı modun
  `Rules`'undan okur; tanınmayan/kayıtsız modda (lobi dahil) `"two"` döner.
- ⚠️ **Bu bir KURAL mesajı DEĞİLDİR ve `ModeRuntime`'a uygulanmaz.** Aktif kuralların tek kaynağı
  `load_match.rules` / `welcome.match.rules` / `return_to_lobby.rules`'tur (§10.5). Buradaki alan
  yalnız *"henüz başlamamış maçın şekli"*ni anlatır; istemci onu tek bir şey için kullanır: **taban
  bölgesi şeritlerinin görünürlüğü** (§10.7). Bu yüzden `modeId` telde ayrıca taşınsa da maç
  türünü, HUD'u ya da loadout'u DEĞİŞTİRMEZ — onlar `start_match`'i bekler.
- ⚠️ **`admin_state`'e binmez:** o mesaj yalnız adminlere gider (roster/duyuru/telemetri taşır) ve
  herkese açılması bu ayrımı bozardı. Bu yüzden ayrı, tek alanlı ve seyrek bir yayın.
- Eski sunucu bu mesajı hiç yollamaz; istemci o zaman **aktif kuralın** takım kipine düşer (§10.7).
  Bu yüzden `PROTOCOL_VERSION` artmaz.

**`rules_update`** — **HERKESE** (oyuncular dahil); koşan maçın **kural şekli değişti**:
```json
{ "type":"rules_update", "modeId":"tdm",
  "rules": { "teamMode":"two", "scoring":"team", "friendlyFire":true,
             "reviveAnchor":"base", "weaponSource":"weaponcanvas", "respawnDelay":5.0,
             "fireWhilePaused":false } }
```
- Kurallar normalde `welcome` / `load_match` / `return_to_lobby` ile gelir, yani **maçın başında**.
  Maç ORTASINDA değişebilen bir kural (bugün tek örnek: dost ateşi anahtarı, §5.2) için taşıyıcı
  kanal yoktu; bu mesaj o farkı taşır. Tetikleyicisi `set_friendly_fire`'dır.
- ⚠️ **`selection_state`'in aksine bu GERÇEK bir kural mesajıdır ve `ModeRuntime`'a uygulanır**
  (§10.5) — istemcide `ModeRuntimePump` dinler. `selection_state` "henüz başlamamış maçın şekli"ni
  anlatır ve yalnız sunuma (taban şeritleri) dokunur; bu mesaj **aktif kuralı** değiştirir.
- `modeId` de taşınır çünkü `ModeRuntime` kuralı türle birlikte saklar; değeri `match_state.modeId`
  ile aynıdır (mesaj türü değiştirmez, yalnız kural şeklini tazeler).
- Geç bağlanan istemci doğru değeri yine `welcome.match.rules`'tan alır — sunucu yürürlükteki
  kural şeklini güncel tutar. Bu yüzden mesajın kaybı kalıcı bir sapma üretmez.
- Eski sunucu hiç yollamaz, eski istemci tanımadığı tipi yok sayar → `PROTOCOL_VERSION` **artmaz**.

**`net_stats`** (v5) — yalnız adminlere, **1 Hz**: oyuncu başına ağ telemetrisi.

```json
{ "type":"net_stats",
  "players":[ {"playerId":3,"rttMs":14,"jitterMs":3.2,"lossPct":0.4} ] }
```

- Değerleri **istemciler ölçer** (§6.7) ve `status` ile bildirir; sunucu yalnız adminlere taşır. `-1` = bilinmiyor.
- ⚠️ **Broadcast EDİLMEZ.** Herkese yayınlamak oyuncu sayısıyla **kare** büyüyen bir fan-out üretirdi — yani bu telemetrinin ölçmek için var olduğu sorunun aynısını. Hedef kuralı `admin_state` ile aynı.
- ⚠️ **Roster'a (`lobby_state`) alternatif değil, bilinçli olarak ayrı bir kanal:** roster'ın bir `version`'ı ve uzlaştırma protokolü var (§5.1); saniyede bir değişen telemetriyi oraya koymak versiyonu sürekli çevirip uzlaştırmayı anlamsızlaştırırdı. Bu mesajın **kaybı zararsızdır** (bir sonraki saniye yenisi gelir), o yüzden uzlaştırması da yoktur.
- ⚠️ **Bant/bayt alanı YOKTUR ve eklenmez.** Hacim sayıları (bayt/sn, paket/sn, anlık paket boyutu, tik kayması) sunucu konsolundaki `[state]` satırındadır ve oraya aittir; operatörün eyleme çevirebileceği sayı ping'dir. Admin panelinde de yalnız **PING** kolonu vardır (jitter/kayıp ölçülür ama gösterilmez).
- Admin yokken sunucu bu mesajı **hiç serileştirmez** — kimse bakmıyorken üretmek boşa pakettir.

**`violation`** — yalnız adminlere; bir oyuncunun **fiziksel kural ihlali** başladı ya da bitti:

```json
{ "type":"violation", "playerId":3, "kind":"obstacle", "active":true,
  "seconds":0.0, "count":2, "totalSeconds":7.4 }
```

- `kind` = `VIOLATION_KIND_OBSTACLE` (kafa bir iç engelin içinde) veya
  `VIOLATION_KIND_OUT_OF_BOUNDS` (kafa muhafazanın güvenli alanının dışında) — §10.9.
- `active:true` = ihlal **başladı**, `seconds` o an `0`'dır. `active:false` = ihlal **bitti** ve
  `seconds` o tek ihlalin süresidir.
- `count`/`totalSeconds` = o oyuncunun bu maçtaki **o TÜRDEN** ihlallerinin sayısı ve toplam
  süresi. Sunucuda skor defteriyle aynı yerde yaşar ve `return_to_lobby`'de skorla birlikte
  sıfırlanır — operatörün maç sonunda oyuncuyla konuşurken elinde tuttuğu somut veri budur.
- ⚠️ **Bu mesaj KENAR TETİKLİDİR, halkanın kaynağı DEĞİLDİR.** İşaretçi halkası snapshot bitlerinden
  (`FLAG_IN_OBSTACLE` / `FLAG_OUT_OF_BOUNDS`, §6.3) beslenir; yani kaybolan bir `violation` yalnız
  bir **log** kaybıdır, görsel bozulmaz. Ayrım bilinçlidir: durum tabanlı bilgi 20 Hz akışta
  kendini onarır, kenar tetikli bildirim onaramaz — bu yüzden ona görsel bağlanmaz.
- ⚠️ **Kaynağı SUNUCUDUR, adminin kenar türetmesi değil.** İki operatör aynı listeyi görür, süre
  tek saatle ölçülür ve maç sonu istatistiği aynı defterden çıkar. Sunucu bayrağın tazeliğini
  zaten izliyor (`OBSTACLE_FLAG_STALE_MS`) — aynı kapı **her iki türe de** uygulanır, yani susmuş
  bir istemci akışta sonsuza kadar açık bir ihlal bırakmaz.
- ⚠️ `VIOLATION_MIN_SECONDS`'tan kısa temaslar için **hiç gönderilmez** (§1) — ne başlangıç ne
  bitiş satırı. Sayaçlara da girmez: akışta görünmeyen bir ihlalin istatistikte belirmesi
  operatöre iki farklı gerçek anlatırdı.

**`calibration_result`** — yalnız adminlere; operatörün `reload_calibration` düğmesinin **cevabı**:

```json
{ "type":"calibration_result", "playerId":5, "ok":true, "error":"" }
```

- `ok:true` = başlık kayıtlı çapadan hizalamayı geri yükledi; `ok:false` ise `error` insan
  okuyabilir gerekçedir (§5.1 ile aynı serbest metin).
- Hedef kuralı `net_stats`/`violation` ile aynıdır: bunu okuyan tek şey operatörün ekranıdır.
- ⚠️ **Bu bir OLAYDIR, durum değildir.** Roster'daki `calibrated`/`calibrationError` alanları durumu
  taşır; bu mesaj yalnız *"az önce basılan düğme ne oldu"* sorusunu cevaplar.
- ⚠️ **Sunucu bekleyen istek defteri TUTMAZ.** Başlıktan gelen **her** `set_calibration{calibrated:true}`
  ve **her** `set_calibration{error}` bir sonuç satırı yayınlar; hangi satırın hangi düğmeye ait
  olduğunu **admin arayüzü** bilir (bekleyen satırı yoksa yok sayar). Defter tutmak sunucuya, tek
  tüketicisi bir ekran olan bir zaman aşımı sorumluluğu yüklerdi.
- ⚠️ Sonuç neden `lobby_state` ile taşınmıyor: zaten kalibreli bir oyuncuda **başarılı** yeniden
  yükleme roster'da hiçbir alanı değiştirmez (§5.3 yayın guard'ı), yani operatörün düğmesi sonsuza
  kadar "yükleniyor" kalırdı.

### 5.4 Atma (kick) kapanış dizisi

Operatör `kick` yolladığında sıra şudur:

0. Sunucu hedefin kaydını **roster'dan siler** (`lobby_state` yayını `Removed` ile gider, `playerId`
   havuza döner). ⚠️ Atmanın **bağlantısız** bir kayıtta da iş yapmasının tek yolu budur: kopan
   cihaz `reconnecting`/`left` olarak listede **durur** (aynı gözlük geri geldiğinde playerId'sini
   ve adını korusun diye, §2), atılan cihaz **kalkar**. Yalnız soket kapatılsaydı bağlantısı olmayan
   bir satırda `kick` hiçbir şey yapmazdı. Silme maç katılımcılığını da düşürür — atılan oyuncu maç
   sonu tablosunda da yer almaz (§10.2). `devices.json`'a dokunulmaz: **atma yasak değildir**, cihaz
   yeniden bağlanırsa adını ve forma numarasını korur (yeni bir `playerId` alır).
1. Bağlantısı varsa sunucu hedefe `kicked` yollar (bağlantısız kayıtta bu adım ve sonrası atlanır).
2. Sunucu bağlantıyı **kapanış çerçevesiyle** kapatır; çerçevenin sebebi `"kicked"`
   (`ArenaProtocol.KICK_CLOSE_REASON`). Cevap gelmezse en fazla 2 sn beklenir, sonra koparılır.
   ⚠️ **Soket doğrudan koparılmaz (`Abort`):** abortif kapanış (RST) istemcinin henüz okumadığı
   `kicked` çerçevesini tamponundan silebilir; o zaman kopuş sıradan bir kesinti gibi görünür,
   istemci backoff'la geri bağlanır ve **atılan oyuncu kendiliğinden oyuna döner**. Kapanış sebebi
   ikinci emniyettir: JSON kaybolsa bile istemci bu kopuşun atma olduğunu oradan anlar.
3. İstemci yeniden bağlanmayı **kapatır** (oto-reconnect yok) ve soketi kapatır.
4. **Oyuncu başlığında uygulama kapanır** (~1,5 sn sonra; soketin kapanması ve log için pay).
   Atmanın karşılığı "lobiye dön" değil "oturumdan çık"tır: başlık açık kalırsa operatör panelde
   düşmüş ama sahada hâlâ oynayan bir oyuncu görür. **Admin uygulaması kapanmaz** — operatör
   penceresi sahadaki tek yönetim aracıdır, yalnız bağlantısız duruma düşer.

Aynı dizi `playerId` havuzu dolduğunda gelen ret için de işler (`kicked{reason:"Sunucu dolu"}`).
⚠️ Bayat soketin değiştirilmesi (aynı cihaz yeniden `hello` yolladı), `HEARTBEAT_TIMEOUT` temizliği
ve `RECONNECT_GRACE` dolunca yapılan çıkarma atma **değildir** — hiçbiri `kicked` yollamaz, soket
koparılarak kapatılır. Yoksa istemci kendini atılmış sanıp uygulamayı kapatırdı; oysa bu üç durumda
da geri bağlanması BEKLENİR (§8).

## 6. UDP state mesajları (binary, little-endian)

### 6.1 Kayıt: `0x00 UdpHello` (istemci → sunucu, welcome'dan sonra)

```
[u8 0x00][u8 playerId][u32 udpToken]
```
Sunucu `playerId↔udpToken` eşleşirse istemcinin UDP endpoint'ini kaydeder ve aynı 6 baytı geri yollar (ack). İstemci ack gelene dek 1 sn arayla tekrarlar. Pozlar yalnız kayıtlı endpoint'ten kabul edilir (yanlış eşleşme koruması; güvenlik amaçlı değil — LAN).

### 6.2 `0x01 PoseUpdate` (istemci → sunucu, 20 Hz; yalnız player)

```
[u8 0x01][u8 playerId][u16 seq][u32 clientTimeMs]
[u8 itemL][u8 itemR][u8 gripFlags]    (3 B — v4)
[head : f32 px,py,pz, qx,qy,qz,qw]   (28 B)
[handL: aynı düzen]                   (28 B)
[handR: aynı düzen]                   (28 B)
Toplam: 11 + 84 = 95 B  → 20 Hz'de ~15.2 kbps/oyuncu
```
Pozlar **arena uzayında**. `seq` sarmalanır (u16); eski `seq` gelirse paket atılır (son gelen kazanır). Quaternion sıkıştırma **YOKTUR ve planlanmıyor**: bant hiçbir zaman darboğaz değil, bağlayıcı kısıt paket sayısıdır (`Docs/Sistem-Ozeti.md` §8).

**`itemL`/`itemR`/`gripFlags` (v4)** — elde tutulan eşya (§6.6). Pozla aynı pakette gider çünkü aynı otoriteye aittir: "elimde ne var" da "elim nerede" gibi **istemci-otoriter bir sunum bilgisidir**. Sunucu bu üç baytı **doğrulamaz**, snapshot'a kopyalar (§6.3) — sunucuda eşya tablosu YOKTUR ve eklenmez (§10.3 felsefesi). `gripFlags`'te bit0 ya da bit6 gelirse **yok sayılır**: onlar snapshot'ta `FLAG_ALIVE` ve `FLAG_SPAWN_PROTECTED`'dır, yazarı yalnız sunucudur (istemci ne kendini canlı ne dokunulmaz ilan edebilir).

`gripFlags` **kavrama bitlerinden ibaret değildir**, snapshot'a kopyalanan tüm istemci bitlerini taşır:
`FLAG_GRIP_LINKED` · `FLAG_PRIMARY_RIGHT` · `FLAG_HAND_L_STALE` · `FLAG_HAND_R_STALE` ·
`FLAG_IN_OBSTACLE` · `FLAG_OUT_OF_BOUNDS` (§6.3). Sunucu
gelen baytı `GRIP_FLAG_MASK` ile süzer — **maskenin varlık sebebi bir bekçidir**, sunucunun bitlerini
(`FLAG_ALIVE` bit0, `FLAG_SPAWN_PROTECTED` bit6) elemek: yukarıdaki altısı maskeye dahildir ve
**doğrulanmadan** kopyalanır, çünkü hepsi eşya baytlarıyla aynı türden
**istemci-otoriter sunum bilgisidir** (§6.6 / §10.3). Maskeye yeni bir bit eklemek onu tel üzerinde
istemcinin yazdığı bir alan yapar; sunucunun yazdığı bitler maskenin DIŞINDA kalır.

⚠️ **`FLAG_IN_OBSTACLE` maskenin içinde olsa da SONUCU sunucu yazar** (§10.9): istemci "kafam engelin
içinde" der, cezayı (tolerans + can eritme) sunucu kendi saatiyle uygular. Bu, `hit_report`'un hasar modelinin
aynısıdır — ölçümü istemci yapar, otorite sunucudadır.

⚠️ **`FLAG_OUT_OF_BOUNDS`'ta sunucunun uygulayacağı bir ceza YOKTUR** (§10.9): biti maskeden geçirip
snapshot'a kopyalar ve yalnız ihlal defterini yazar. Taşınmasının sebebi otoritedir — sunucu
bilmezse defter, maç sonu istatistiği ve iki operatörün ortak listesi mümkün olmaz; admin'in kuralı
kendi hesaplaması ise "alan dışı"nın biri istemcide biri adminde iki kez yaşaması demek olurdu.

⚠️ **İskelet akışı (§6.9) bu paketin yerine GEÇMEZ.** Gövde ayrı bir kanaldan gelir ama poz kanalı durur: silahın ele oturması, eşya baytları ve vuruş bildirimi ham anchor pozundan besleniyor. İki kanalın kadansı da ayrıdır (20 Hz ↔ `SKELETON_RATE_HZ`) — iskeletin gecikmesi silahın gecikmesi olmasın diye. "İskelet zaten el eklemini taşıyor, poz kanalı silinsin" **yapılmaz**: blob opaktır (alıcı içinden tek bir eklemi ucuza okuyamaz) ve o eklem elin fiziksel pozu değil retarget edilmiş bilek kemiğidir.

⚠️ **Poz kanalı FİZİKSEL gerçeği taşır.** `handL`/`handR` ham rig anchor'larıdır — eşyaya "yapıştırılmış" düzeltilmiş poz DEĞİL. Çift elli silahta boş elin kabzaya oturtulması bir **sunum** kararıdır ve alıcı tarafta yapılır (§6.6). Bu kanal bir kez bulanırsa (düzeltilmiş poz taşımaya başlarsa) sonraki her tüketici — yakın dövüş, elle etkileşim, admin teşhisi — o bulanıklığı miras alır.

### 6.3 `0x02 Snapshot` (sunucu → tüm istemciler, 20 Hz)

```
[u8 0x02][u8 playerCount][u32 serverTick]
oyuncu başına: [u8 playerId][u8 flags][u8 itemL][u8 itemR][head 28][handL 28][handR 28] = 88 B
```

`flags` bitleri — **tek bayt, iki yazar** (otorite bölünmesi §10.1'in tel karşılığı):

| Bit | Ad | Yazarı | Anlamı |
|---|---|---|---|
| 0 | `FLAG_ALIVE` | **sunucu** | Oyuncu hayatta (otoriter durum) |
| 1 | `FLAG_GRIP_LINKED` | istemci (`gripFlags`'ten kopya) | İki el **aynı** eşyayı tutuyor |
| 2 | `FLAG_PRIMARY_RIGHT` | istemci (aynı) | Ana el sağ (yalnız `GRIP_LINKED` iken anlamlı) |
| 3 | `FLAG_HAND_L_STALE` | istemci (aynı) | Sol el pozu **ölçülmüş değil**: gönderen son geçerli pozu tutuyor |
| 4 | `FLAG_HAND_R_STALE` | istemci (aynı) | Sağ el için aynısı |
| 5 | `FLAG_IN_OBSTACLE` | istemci (aynı) | Gönderen bir **iç engelin içinde** (§10.9). Sunucu bunu can eritmeye, admin halkayı kırmızı yakıp söndürmeye çevirir |
| 6 | `FLAG_SPAWN_PROTECTED` | **sunucu** | Oyuncu **doğma koruması** altında: `hit_report` ona hasar yazmaz, istemci gövdesinin üstüne kalkan kabuğu çizer (§10.4) |
| 7 | `FLAG_OUT_OF_BOUNDS` | istemci (aynı) | Gönderenin kafası muhafazanın **güvenli alanının dışında** (§10.9). ⚠️ **Ceza üretmez** — sunucu bundan yalnız ihlal defterini yazar, admin turuncu halkayı yakıp söndürür |

⚠️ **Bayt düzeni bu bayrakla DEĞİŞMEDİ:** `PoseUpdate.SIZE = 95` ve `SnapshotEntry.SIZE = 88`
aynı kalır, alan tipleri de değişmez — bit zaten rezervdeydi ve bant artışı sıfırdır.

⚠️ **Rezerv bitten yeni bir bayrak almak `PROTOCOL_VERSION`'ı ARTIRMAZ:** bayt düzeni değişmez ve
bit zaten "sıfır yazılır, okuyucu yok sayar" sözleşmesindeydi — bayrağı tanımayan eski istemci onu
yok sayar. Koşul bayrağın **otoritesinin sunucuda kalmasıdır**: `FLAG_SPAWN_PROTECTED` böyledir
(korumayı sunucu uygular, karışık sürümde kaybolan tek şey kalkanın çizilmesidir, §10.4). İstemcinin
yazacağı bir bit rezervden alınırsa bu serbestlik geçmez — o bilgiyi göndermeyen uç sessizce yanlış
davranır. `FLAG_OUT_OF_BOUNDS` tam olarak bu ikinci sınıftadır ve sürümü bu yüzden artırır (§1).

**Bayat el bitleri (`3`/`4`) neden var:** kumandanın pili biterse rig el anchor'ını **koşulsuz** yazar
ve okuma `(0,0,0)` döner — yani sıfır poz eli oyuncunun ayağının dibine koyar, üstelik gövde çözümünü
de (dolayısıyla `0x07`'nin kökünü) zehirler. Gönderen bunu tel dışında çözemez: paket **sabit
uzunlukludur** (95 B / 88 B), "eli olmayan oyuncu" diye bir tel durumu YOKTUR ve eklenmez — akışı
kesmek ya da eli sıfırlamak seçenek değildir. Bunun yerine gönderen **son geçerli eli kafaya göreli**
tutmaya devam eder (el arena uzayında donmaz, oyuncunun gövdesiyle taşınır; donsaydı oyuncu yürüdükçe
eli geride kalırdı) ve durumu bu bitle bildirir.

⚠️ **Bayrağın işi alıcının YORUMUDUR:** taze olmayan el bir ölçüm değil bir **tahmindir**. Nişan
teşhisi, temas/yakın dövüş kararı ve "şu oyuncu neye bakıyor" türü çıkarımlar ona dayandırılmaz;
admin bunu operatöre gösterir (kumandası düşen oyuncu sahada fark edilsin diye).

İstemci kendi pozunu snapshot'tan ÇİZMEZ (yerelden çizer); uzak oyuncuları `INTERP_DELAY_MS` tamponuyla interpole eder. Admin'e de aynı snapshot gider (gözlemci avatarları/işaretçileri bundan beslenir).

**Parçalama (MTU):** pozlu oyuncu sayısı `SNAPSHOT_MAX_ENTRIES_PER_PACKET`'i aşarsa sunucu aynı tik'i **birden çok datagrama böler**; her datagram kendi `playerCount`'unu taşır, hepsi aynı `serverTick`'i taşır ve aynı hedeflere yollanır. 16 girdi = 6 + 16×88 = **1414 B** (MTU 1500 altı). **İstemcide birleştirme mantığı YOKTUR ve gerekmez:** her paket taşıdığı girdileri bağımsız olarak uygular, oyuncu düşürme kararı "bu pakette yok" değil ~1.5 sn'lik zaman aşımıdır. Bu yüzden parçalama tel formatını değiştirmez — ek başlık alanı yoktur, eski okuyucu da doğru çalışır.

⚠️ **Olay batch'i (`0x04`) snapshot'a EKLENMEZ, ayrı datagramdır.** 1414 B + tek olay MTU'yu aşar ve snapshot'ın boyut garantisi çöker.

**İçerik kuralı:** snapshot'a yalnız *online* olup en az bir `PoseUpdate`'i alınmış `role=player` girişleri konur (admin hiç girmez — poz göndermez; ama UDP kaydı yaptığı için snapshot ALIR, ve birden çok admin varsa her biri ayrı hedeftir). Kopan oyuncu (WS kapanışı/OFFLINE_TIMEOUT) bir sonraki tikten itibaren düşer; `playerCount=0` snapshot yine yayınlanır (istemciler bayat avatarı böyle temizler). **Yayın hedefi:** UDP kaydı yapılmış tüm online endpoint'ler (admin dahil). **İstemci düşürme kuralı:** bir `playerId` snapshot'larda ~1.5 sn görünmezse uzak avatarı kaldırılır (paket kaybı toleransı; sunucunun 15 sn'lik OFFLINE_TIMEOUT'unu beklemez).

⚠️ **Yayın hedef başına ayrı unicast'tir (multicast yok)** — yani snapshot trafiği hem girdi hem hedef sayısıyla, `N²` olarak büyür. Bu bir bant sorunu değil **paket/airtime** sorunudur; bütçe hesabı, ölçek tavanları ve "sıkıştırma bu darboğaza dokunmaz" gerekçesi `Docs/Sistem-Ozeti.md` §3.12'de.

### 6.4 `0x03 FireEvent` (istemci → sunucu, olay başına; yalnız player)

```
[u8 0x03][u8 playerId][u16 seq][u8 kindHand][u8 itemId][i16 dirOctX][i16 dirOctY][u16 magnitude]
Toplam: 12 B
```

| Alan | Anlamı |
|---|---|
| `kindHand` | Alt nibble = **tür**: `0` atış (hitscan), `1` atma (fırlatma). Bit7 = **el**: `0` sol, `1` sağ |
| `itemId` | Atış/atma anındaki eşya (§6.6). Sunum profilini (ses/alev/tracer) çözer; durum baytı kaybolsa da olay kendi kendine yeter |
| `dirOctX/Y` | **Oktahedral sıkıştırılmış birim yön**, arena uzayında (2×i16 = 4 B, ~0.01° hata). 3×f32 = 12 B yerine |
| `magnitude` | Türe göre: atışta **mesafe** (u16, cm → 0–655 m), atmada **başlangıç hızı** (u16, cm/sn) |

**HEMEN gönderilir**, poz tik'i beklenmez: bekletmek yerel tetik ile relay arasına 0–50 ms koyar, karşılığı yoktur (10 paket/sn/oyuncu bir AP için hiçtir).

**`seq` sözleşmesi — YALNIZ yukarı yön:**
- ✅ **Kopya bastırma:** sunucu oyuncu başına son `seq`'i tutar; aynısı ikinci kez gelirse relay etmez (UDP paket çoğaltabilir → çift tracer + çift ses).
- ✅ **Kayıp ölçümü:** `seq` boşluğu = kaybolan olay sayısı (başlık başına Wi-Fi teşhisi).
- ❌ **SIRA ZORLAMASI YOK.** "Eski `seq`'i at" kuralı **POZ** kuralıdır (durum: son gelen kazanır) ve olaylara **UYGULANMAZ**: sırası bozuk gelen atış gerçekten olmuş bir atıştır; atmak sessizce bir tracer ve bir ses silmektir.

**Yön neden gönderiliyor** (el pozundan türetilebilir gibi duruyor): 20 Hz interpole el pozundan türetilirse aynı tik'e düşen iki atış aynı yöne gider ve geri tepme kaybolur. Nişan, oyun açısından anlamlı bilgidir; 4 B'ye değer.

**Geri tepmenin kendisi neden gönderilmiyor** (ve bir alan eklenmeyecek): silahın sarsılması deterministik bir eğridir ve girdisi zaten telde — olayın kendisi + `itemId`. Alıcı, eğriyi `WeaponDefinition`'dan (`kickDegrees`/`kickBackMeters`/`recoilRecoverSpeed`) çizdiği silahın `Model` pivotuna uygular (`RemoteAvatar.ApplyShotRecoil`), çift elli tutuşun çarpanını da snapshot'taki `FLAG_GRIP_LINKED`'den bilir. Yani atış başına ek bayt sıfırdır. Silahın kare-kare duruşunu akıtmak ise aynı görüntüyü **20 Hz × oyuncu** maliyetle satın almak olurdu.

**Saçmalı silahta olay yine TEK'tir** (ilk saçmanın yönü/mesafesiyle) ve saçma başına alan/olay eklenmez: aynı ateşi 9 kez duyurmak, olay kanalının paket başı sınırını tek tetikle doldurur. Alıcı yelpazeyi **kendisi üretir** — telden gelen ışın olduğu gibi çizilir, kalan saçmalar `WeaponDefinition.baseSpreadDegrees` konisinden dağıtılıp yerel ışınla ölçülür (`RemoteShotFx.BuildScatter`). Eksik olan veri kozmetiktir ve iki uç aynı arenayı yüklüyor; hasar zaten saçma başına ayrı `hit_report` ile atıcının istemcisinden gidiyor (§10.3). Yelpazenin atıcının ekranındakiyle birebir aynı olması **gerekmez**: saçmaların dağılımı atıcıda da rastgeledir, telin taşıdığı bilgi atışın kendisidir.

**Orijin neden gönderilmiyor:** tracer, alıcının **çizdiği silahın namlusundan** çıkmalıdır. Mutlak bir namlu konumu gönderilirse alıcı silahı interpole edilmiş el pozundan çizdiği için tracer çizilen namludan kaymış başlar — atıcının gerçeğine bir tık daha sadık, gözle daha bozuk bir sonuç. **Tutarlılık > sadakat.** Orijin `itemId` + o tik'teki el pozu + eşyanın statik `muzzle` ofsetinden türetilir; eşya çözülemezse el pozuna düşülür.

### 6.5 `0x04 EventBatch` (sunucu → tüm istemciler, 20 Hz; yalnız olay varken)

```
[u8 0x04][u8 count][u32 serverTick]
olay başına: [u8 playerId][u8 kindHand][u8 itemId][i16 dirOctX][i16 dirOctY][u16 magnitude] = 9 B
Toplam: 6 + count×9 B
```

Alanlar §6.4 ile birebir aynı; `seq` **taşınmaz** (sunucu kopyayı zaten ayıkladı).

**Relay kapısı** (§10.3): faz `playing` **VEYA** `rules.fireWhilePaused`, atıcı online + `role=player` + **hayatta** + **kalibreli**. Ara fazlarda (yükleme/geri sayım/maç sonu, `fireWhilePaused:false` iken) relay yoktur. İçerik **doğrulanmaz**: yön, mesafe ve `itemId` serbesttir (§10.3).

**Hedef:** UDP kayıtlı tüm online endpoint'ler (admin dahil — gözlemci de uzak atışları görür/duyar). **Atan da kendi olayını geri alır ve kendisi yok sayar** — snapshot'ta kendi pozunu yok saymasıyla birebir aynı desen (§6.3). Sunucuda hedef başına ayrı batch üretmek (atanı süzmek) tik başına N serileştirme demek olurdu; karşılığı oyuncu başına ~90 B/sn'lik bir israftır ve tek satırlık istemci süzgeci onu bedavaya kapatır.

**Oynatma zamanı:** olay kendi `serverTick`'inde, alıcının interpolasyon saatiyle oynatılır. Bu yüzden 20 Hz batch'leme **algılanan gecikmeye eklenmez**: batch bekleme süresi (≤50 ms) `INTERP_DELAY_MS` (100 ms) tamponunun içinde erir — anında yollansa da el pozu tampon kadar geriden geldiği için daha erken OYNATILAMAZDI.

**Kopya koruması `seq` değil TİK'tir:** batch'in kimliği `serverTick` ve **tik başına en fazla bir batch** üretilir. İstemci son işlediği `EVENT_TICK_HISTORY` tik'i halkada tutar ve yalnız **birebir tekrarı** düşürür. Eski tik'li ama görülmemiş batch **OYNATILIR** (interp saati o tik'i geçmişse hemen) — ~50 ms gecikmiş tracer, kaybolmuş tracer'dan iyidir.

⚠️ `EVENT_MAX_ENTRIES_PER_PACKET`'i aşan olaylar **atılmaz, sonraki tik'in batch'ine kayar** — "tik başına bir batch" değişmezi korunsun (kopya koruması buna dayanıyor). Sınır 128/tik = 2560 olay/sn; pratikte erişilmez.

**Olay yoksa paket yok:** lobide, geri sayımda ve sessiz anlarda bu kanal tümüyle susar (snapshot'ın `count=0` yayınından farklı — burada bayat durum temizlenmesi gerekmez, olaylar anlıktır).

### 6.6 `netItemId` — elde tutulan eşya kimliği

`itemL`/`itemR` (§6.2/6.3) ve `itemId` (§6.4/6.5) alanlarının hepsi aynı `u8` isim uzayını kullanır:

| Değer | Anlam |
|---|---|
| `0` | **El boş** (rezerve — hiçbir eşyaya verilemez) |
| `1..255` | Bir `ItemDefinition`'ın `netItemId`'si |

Eşleme tablosu Unity tarafındadır (`ItemDefinition.netItemId`, katalog `NetItemCatalog`) ve **sunucuya export EDİLMEZ** — sunucu bu baytı yalnız kopyalar, çözmez. Kimlikler `WeaponKitBuilder` tablosundan açıkça verilir; **katalog dizi indeksi kimlik olarak KULLANILMAZ** (dizi sırası değişince tüm eşyalar kayar — serialize edilen enum tuzağının aynısı). Editör bekçisi çakışan `netItemId`'de hata verir: çakışma derlemede patlamaz, sahada "elinde yanlış eşya çizildi" olarak görünürdü.

**Alıcının çözüm tablosu** (`itemL`, `itemR`, `FLAG_GRIP_LINKED`):

| Durum | `itemL` | `itemR` | `GRIP_LINKED` | Çizim |
|---|---|---|---|---|
| Boş | `0` | `0` | – | Eşya yok |
| Tek elli, sağ (tabanca) | `0` | `p` | `0` | Sağ elde `p` |
| Çift tabanca (aynı eşya!) | `p` | `p` | `0` | **İki** ayrı `p` |
| Tüfek, iki el | `r` | `r` | `1` | **Bir** `r`, ana elin (`FLAG_PRIMARY_RIGHT`) pozundan; ekseni boş elin konumu nişanlar |
| Sağda tüfek, solda bomba | `b` | `r` | `0` | İkisi ayrı |

⚠️ "Aynı id iki slotta" tek başına **çift elle tutmak demek DEĞİLDİR** (çift tabanca meşru bir durum) — ayrımı yalnız `FLAG_GRIP_LINKED` taşır.

**Duruş telde gitmez.** Eşyanın ele göre duruşu her istemcinin **kendi APK'sındaki** eşya verisinden gelir: el başına yazılmış kavrama kaydından (`ItemGripPose` — **kumanda anchor'ının** eşyaya göre yerel KONUMU; ⚠️ **kaydın bu yarısında DÖNÜŞ YOKTUR**, eşyayı hiçbir elin dönüşü çevirmez. Kaydın el yarısı — el modelinin kumanda üstündeki yerleşimi ve parmak rigi — yalnız **yerel görseldir**, eşyanın duruşuna girmez). Kayıt telde giden el pozuyla **aynı uzaydadır**, yani `ItemGripSolver` onu doğrudan okur (`ItemDefinition.PrimaryGripPosition`); ön kabza kaydı (`secondaryGrip`) hem ikinci elin GÖRSELİNİN yapışacağı yeri söyler hem de iki elli nişanın eksenini tanımlar. **İki uç aynı çözücüyü koşar**, dolayısıyla iki elle tutulan eşyada ikinci elin telden gelen **KONUMU** da çözüme girer (`FLAG_GRIP_LINKED` + o elin pozu; uzak uçta nişan ağırlığı sabit 1'dir, yumuşatma zaten telin interpolasyonundan gelir) — eşya ikinci ele doğru KAYMAZ, yalnız ekseni oraya döner ve o elin DÖNÜŞÜ hiçbir yoldan hesaba girmez. ⚠️ **Kavrama kaydı tel formatını DEĞİŞTİRMEZ:** iki uç aynı `WD_*`'ı okur ve arada çevrilecek bir şey yoktur — rig'i olmayan izleyici (admin) de eşyayı oyuncuyla birebir aynı çizer. Ön koşulu **kanonik kavramadır**: her eşyanın eline denk gelen noktası sabittir (serbest kavrama = keyfi ofset = uzak tarafta yanlış duruş). Aynı sebeple namlu yönü de telde gitmez — `muzzle` çocuğu prefabdadır.

⚠️ **Uzak uçta eşyanın KONUMU çizilen bilekten okunur, telden gelen el pozundan değil** (dönüş yine telden gelir). Sebep iki poz kanalının **iki ayrı sensörden** doğmasıdır: `handL`/`handR` kumandanın pozudur, çizilen el ise iskelet blob'undaki **body tracking** bileğidir. İkisinin çakışması ancak modelin kol uzunluğu oyuncununkine eşitse mümkündür — ve bu proje gövde oranlarını **bilerek kalibre etmez** (§10.8: `Calibrate()` blob'un eklem sıkıştırmasını bozardı). Yani model prefab oranlarını taşır: kol uzandıkça (nişan alan oyuncu) modelin eli kumandaya yetişemez ve fark açılır, kol büküldükçe kapanır. Eşya ham el pozunda bırakılırsa aynı oyuncu bazı duruşlarda silahı elinde, bazılarında havada tutuyor görünür. Konumu çizilen bileğe bağlamak bu farkı **tanım gereği** sıfırlar ve takım gövdesinin ayrı kol uzunluğunu da kendiliğinden kapsar. ⚠️ **Dönüş çizilen bileğe TAŞINMAZ:** kavrama kumanda anchor'ı çerçevesinde çözülüyor, humanoid bileğin bind eksenine geçmek tüm silahların duruşunu bir anda geçersiz kılardı — ayrışan şey erişim, yönelim değil.

⚠️ **Bu, poz kanalının otoritesini değiştirmez:** atış yönü, `hit_report` ve hedef çözümü hâlâ ham el pozundan gelir (§6.4/§10.3). Ham poz **fiziksel ölçümdür** ve orada doğru olan odur; bilek yalnız **çizimin** referansıdır.

⚠️ **Ön kabza yerelde bir kapıdır** (boş el yaklaşınca gösterge belirir, kabul yarıçapında grip'e basılınca ikinci el bağlanır — `Weapon.IsHandOnSecondaryGrip` + gösterge) ve **bu telde HİÇBİR ŞEY değiştirmez:** kapıyı da uzak çizimi de aynı yerel kaynaklar besler (etkin kavrama + `secondaryGrip`), yani kapı için ne yeni bir alan ne yeni bir mesaj vardır. Kapı bir **giriş koşuludur** (ikinci el nerede bağlanabilir), duruşun kaynağı değil. Buraya bir "kabul yarıçapı/gösterge durumu" alanı eklemek gerekmiyor ve eklenmemeli: yarıçap bir his ayarıdır, uzak taraf kavramanın nasıl başladığını değil yalnız SONUCUNU (hangi eşya, hangi el, kavrama bağlı mı) çizer.

**Çift ellide boş el:** `GRIP_LINKED` iken alıcı boş elin **GÖRSELİNİ** `secondaryGrip`'e **eşikli** (~25 cm) yapıştırır — eşyanın **kavrama noktası** yalnız ana elden çözülür, ikinci elin telden gelen konumu çözüme yalnız **nişan** olarak girer (eksen o avuca döner, `ItemGripSolver`); yani bayrağın anlamı "silah iki elle taşınır" değil "ikinci el silahı nişanlar ve görseli sokete yapışır"dır. Eşik güzellik ayarı değil **paket kaybı emniyetidir**: bayrağın kaybolduğu bayat tik penceresinde oyuncu silahı gerçekten bırakmışsa koşulsuz yapıştırma kolu arenanın öbür ucuna uzatırdı.

### 6.7 `0x06 RttProbe` (istemci → sunucu, 1 Hz; echo ile döner)

```
[u8 0x06][u8 playerId][u32 clientStamp]
Toplam: 6 B — sunucu AYNI 6 baytı geri yollar (echo)
```

**Ölçen taraf İSTEMCİDİR:** `RTT = şimdi − clientStamp`. `clientStamp` **opak bir damgadır** — sunucu okumaz, yorumlamaz, aynen taşır; bu yüzden **saat senkronu gerekmez** (iki damga da istemcinin). Sunucu tarafında durum tutulmaz: doğrulama poz/olay yolundaki kuralın aynısı (yalnız `0x00` ile kaydedilmiş endpoint'ten) ve yanıt `0x00` ack'inin birebir aynı deseni.

**Neden ayrı bir paket gerekiyor** — akla gelen üç alternatifin hiçbiri bu işi görmez:

| Alternatif | Neden olmaz |
|---|---|
| `clientTimeMs`'i kullanmak (§6.2, zaten telde) | Saat senkronu olmadan **mutlak gecikme vermez**; yalnız farkının değişimi tek yönlü jitter verir — onu snapshot varışları zaten daha iyi ölçüyor |
| Sunucunun snapshot'ta istemcinin damgasını geri yollaması | Damga **hedefe özel** olurdu ve tek paylaşımlı buffer'ı tik başına N serileştirmeye çevirirdi (§6.5 olay batch'ini de aynı gerekçeyle hedefe özelleştirmiyor) |
| WS/TCP üzerinden ölçmek | TCP retransmit'i gecikmeye karışır. **Gecikme oyunun aktığı kanaldan ölçülmelidir.** ⚠️ WS'teki `ping` mesajı bir gecikme ölçümü DEĞİL, sunucunun "bana bir `status` yolla" tetiğidir |

**Jitter ve paket kaybı bu paketle ölçülmez** — istemci ikisini de zaten aldığı **20 Hz snapshot akışından** çıkarır (varış aralığının 50 ms'den sapması = jitter, `serverTick` boşluğu = kayıp), yani **sıfır ek paketle**. ⚠️ Aynı tik'in parçaları (§6.3) ve `0x05` (§6.8) bu sayımda **doğru** ele alınmalıdır: parçaları ayrı varış saymak jitter'ı sıfıra çeker, `0x05`'i saymamak ise birleştirme devreye girdiğinde kaybı %100 gösterir.

⚠️ **1 Hz'in üstüne çıkarılmaz.** Her yoklama **2 datagram**dır (gidiş + echo) ve bu ürünün darboğazı bant değil paket sayısıdır: 5 Hz ping 10 oyuncuda ~110 paket/sn eder ve §6.8'in kazancının dörtte birini geri verir. Bu paketin tek işi operatörün okuduğu sayıdır (`net_stats` → admin panelinde **PING** kolonu); teşhis çözünürlüğü jitter'dan gelir.

Sunucu tarafı ölçüm (**uplink**: poz varış aralığı + `seq` boşluğu) ve sunucunun kendi tik kayması **konsolda** kalır (`[state]` satırı) — yön asimetriktir, iki taraf ayrı ayrı ölçülür. Bütçe ve gerekçeler: `Docs/Sistem-Ozeti.md` §3.12.

### 6.8 `0x05 SnapshotWithEvents` (sunucu → tüm istemciler, 20 Hz; snapshot + olaylar birlikte)

```
[u8 0x05][u8 playerCount][u8 eventCount][u32 serverTick]
oyuncu başına: SnapshotEntry (88 B, §6.3 ile birebir aynı)
olay başına:   FireEventEntry (9 B, §6.5 ile birebir aynı)
Başlık: 7 B
```

**Varlık sebebi paket sayısıdır, bant değil.** Tipik bir maçta (10 oyuncu, 5 olay/tik) snapshot 886 B ve olay bloğu 45 B — ikisi tek datagrama rahat sığıyor, oysa ayrı gönderildiklerinde **tik başına hedef başına iki** datagram üretiliyordu. 10 oyuncu + 1 admin'de bu ~220 paket/sn'dir; bant kazancı ihmal edilebilir, kazanç **airtime**'dadır (`Docs/Sistem-Ozeti.md` §3.12).

**Sunucunun birleştirme kapısı — üç koşulun HEPSİ gerekli:**

1. O tik'te **olay var** (yoksa birleştirilecek bir şey yok, düz `0x02` gider).
2. Snapshot **tek parçaya sığıyor** (girdi sayısı `SNAPSHOT_MAX_ENTRIES_PER_PACKET`'i aşmıyor).
3. Toplam boyut `COMBINED_MAX_BYTES` (1200 B) altında.

Koşullar sağlanmazsa sunucu **bugünkü davranışa düşer**: `0x02` parçaları + `0x04`. ⚠️ **`0x02` ve `0x04` kaldırılmadı ve kaldırılmaz** — geri düşüş yolu onlardır.

⚠️ **Tik başına ya `0x05` ya `0x04` üretilir, ikisi birden ASLA.** §6.5'in kopya koruması "tik başına en fazla bir olay datagramı" değişmezine dayanıyor ve kimlik `serverTick`; aynı tik için iki olay datagramı çıkarsa istemci ikincisini birebir tekrar sanıp **düşürür**. Aynı sebeple **parçalanmış snapshot'ta olaylar bu pakete hiç girmez** — parçalar arasında olay bloğu çoğaltmak tam olarak bu değişmezi kırardı.

⚠️ **İstemcide olay bloğu `0x04` ile AYNI koddan ve AYNI tik halkasından geçer** (`EVENT_TICK_HISTORY`). Ayrı bir halka açılırsa aynı tik iki kez oynar: çift tracer + çift ses.

Snapshot bloğu `0x02` ile birebir aynı işlenir; tik tekrarında durumu yeniden uygulamak zararsızdır (durum kanalı, son gelen kazanır) — düşürülmesi gereken yalnız **olaylardır**.

### 6.9 `0x07 SkeletonUpdate` (istemci → sunucu, `SKELETON_RATE_HZ`; yalnız player)

```
[u8 0x07][u8 playerId][u16 seq][root : f32 px,py,pz, qx,qy,qz,qw] (28 B)
[u16 len][blob len B]
Başlık: 34 B  →  len=200'de 234 B
```

Gövde artık üç noktadan **türetilmiyor**: sahibinin cihazında Meta Movement SDK'nın body tracking'i
koşuyor, retarget ediliyor ve **sonuç iskelet** akıyor. Sebep yapısaldır — body tracking bir cihaz
servisidir, dışarıdan poz kabul etmez; uzak avatara "aynı body tracking'i" takmak her avatarın
**yerel** gövdeyi oynatması demektir.

**`blob` OPAKTIR.** İçeriği SDK'nın native serileştirmesidir; sunucu açmaz, doğrulamaz, kopyalar —
sunucuda iskelet tablosu YOKTUR ve eklenmez. Gerekçe `netItemId` baytlarınınkiyle aynıdır (§6.6):
bu bir **istemci-otoriter sunum bilgisidir**.

⚠️ **PARMAK EKLEMLERİ TELE GİRMEZ.** Blob yalnız gövde + kol zincirini taşır; bilek (`…Hand`) dahil,
bileğin ALTINDAKİ hiçbir eklem gönderilmez. Hedef iskeletin 66 ekleminin **40'ı parmaktır**, yani
kanalın %61'i. Kesilmesinin sebebi yalnız bant genişliği değil **doğruluk**: parmakların nerede
duracağı bir ölçüm sorusu değil bir **kavrama** sorusudur ve cevabı zaten her istemcinin
APK'sındadır (§6.6 — hangi eşya, hangi el, kavrama bağlı mı). İzlemeden gelen parmak, kumanda
tutan bir elde gerçek parmak duruşunu zaten göstermiyor; onu telde taşımak, alıcının **daha iyi
bildiği** bir şeyi bant genişliğiyle satın almaktı.

Alıcı parmakları **kendi** sentezler: eşya tutan elde o slot için **silaha özel riglenmiş duruş**
(`ItemDefinition.GripFingerCurl(slot, rightHand)` — kavrama kaydındaki `fingerJoints`'ten
ölçülür), boş elde idle duruşu (`RemoteAvatar.idleHandPose`, boşsa `HandPoseProfile.Idle`).
Böylece aynı el her ekranda aynı çizilir ve sol/sağ farkı kalmaz — duruşun kaynağı ölçüm değil
**tanımdır**. ⚠️ Uzak avatarın eli **humanoid** olduğu için ham eklem dönüşü DEĞİL, o duruştan
ölçülen **beş kapanma oranı** uygulanır (`HandPoseLibrary.MeasureCurl`); ham rotasyonu humanoid
kemiğe yazmak projenin bir kez öğrendiği tuzaktır. Oran **asset'te saklanmaz**, kayıttan türetilir.

⚠️ **Gönderen ile alıcının eklem listesi AYNI olmak ZORUNDADIR** (`NetworkCharacterRetargeter`'ın
`_bodyIndicesToSend`/`_bodyIndicesToSync` alanları, iki prefabta birden). Listeler ayrışırsa blob
yanlış çözülür ve gövde tümden bozulur — bu, sürüm uyumsuzluğunun sessiz biçimidir.

⚠️ **`root` neden ayrı bir alan** (blob'un kendi kökü varken): SDK kök eklemi
`JointType.NoWorldSpace` ile yazıyor, yani **gönderenin dünya pozu** — alıcının arenasıyla ilgisi
yok. Blob opak olduğu için içindeki kökü çeviremeyiz; kök bu yüzden **arena uzayında ayrıca**
taşınır ve alıcı `ApplyBodyPose`'dan sonra karakterin kökünü bununla yazar. Blob'un kendi kökü
kullanılmaz. Aynı madde `Docs/Sistem-Ozeti.md` §7'deki "retarget avatarı hareket eden kökün altına
konmaz" tuzağının da çözümüdür: karakter hiçbir şeyin altına parent'lanmaz.

⚠️ **Bu kanalda PARÇALAMA YOKTUR.** Blob `SKELETON_MAX_BLOB_BYTES`'ı aşarsa paket **hiç
gönderilmez** (bir kez uyarı basılır). Yarım bir kareyi deserialize etmek bozuk iskelet demektir ve
IP parçalanmasına güvenmek tek parçanın kaybında tüm kareyi çöpe atardı. Blob sınırı zorluyorsa
çözüm eklem listesini daraltmaktır — **parmak eklemleri kumandayla oynanırken gerçek veri
taşımaz**.

⚠️ **Kırpılmış blob = boş blob.** Okuyucu `len` kadar bayt isteyip daha azını alırsa girdi
`blobLength = 0` ile döner ve tüketici onu düşürür — yarım blob'u deserialize etmeye çalışmak bozuk
iskelet çizmektir. Bu yol sunucunun **uplink** okuyucusudur ve sunucu blob'u olduğu gibi relay
ettiği için yarım bir kare tek oyuncuyu değil arenadaki **herkesi** bozuk gövdeyle çizerdi.

⚠️ **İzleme bozulduğunda gönderen SUSMAZ — T-poz yedeği devreye girer.** Yedeği üreten
`ArenaNetCharacterBehaviour`'dur ve **iki** durumda çalışır:

- **(a) Body tracking hiç geçerli poz üretmemişse.** Kaynağı açılmayan/izinsiz başlıkta (Link'te
  geliştirici özelliği kapalı, `BODY_TRACKING` izni yok) SDK'nın gönderim kapısı hiç açılmaz ve
  oyuncu diğer ekranlarda tümden görünmez kalırdı — sahada "ağ bozuk" diye okunan bir arıza. Yedeği
  tanıma süresi dolunca `LocalBodyAvatar` ister; blob karakterin bind (T) pozudur.
- **(b) Oyun içinde SDK'nın ürettiği kök akıl sağlığı denetiminden düşerse.** Zemin/boy çözümü
  karışan başlıkta (çok katlı mekân, bayat izleme haritası) SDK "geçerli" bayrağıyla çöp kök
  üretebilir; kaynak büsbütün de susabilir. İstemci her SDK karesinde kökü **HMD'nin zemin
  izdüşümüne yatay uzaklığı** ve **kök yüksekliği** ile yargılar, ayrıca akışın **bayatlamasına**
  bakar (eşikler `ArenaNetCharacterBehaviour`'daki sabitlerdedir). Düşen kare hiç gönderilmez,
  yerine yedek kare gider; SDK yoluna dönüş **histerezislidir** — ardışık temiz kare sayacı dolana
  kadar temiz kareler de bastırılır, yoksa uzak tarafta gövde iki yol arasında kare kare titrerdi.

İki durumda da `root` HMD'nin zemine izdüşümünden (yalnız yaw) türetilir, yani donuk gövde
oyuncunun gerçek konumunu izler ve arıza "izlemesi bozuk" diye okunur. Tel açısından bu sıradan bir
`0x07`'dir — sunucu ve alıcı için özel bir durum YOKTUR. Yedek ile SDK yolu aynı anda asla
göndermez.

`seq` sarmalanır (u16); eski `seq` gelirse paket atılır. `0x01` ile aynı "son gelen kazanır"
kuralıdır — bu bir **durum** kanalıdır, olay değil (karşılaştır §6.4: olayda sıra zorlaması yoktur).

### 6.10 `0x08 SkeletonBatch` (sunucu → tüm istemciler, `SKELETON_RATE_HZ`)

```
[u8 0x08][u8 count][u32 serverTick]
oyuncu başına: [u8 playerId][root 28][u16 len][blob] = 31 + len B
Başlık: 6 B
```

**Neden batch:** oyuncu başına ayrı datagram, tik başına **hedef başına N** paket demek olurdu. Bu
üründe darboğaz bant değil paket/airtime'dır (`Docs/Sistem-Ozeti.md` §3.12) — batch onu N'den
`⌈N/5⌉`e indirir.

**Parçalama:** girdiler değişken uzunluklu olduğu için sunucu **hem** bayt bütçesine
(`COMBINED_MAX_BYTES`) **hem** girdi tavanına (`SKELETON_MAX_ENTRIES_PER_PACKET`) bakar; taşan girdi
aynı tik içinde ek datagrama yazılır. Her datagram kendi `count`'unu, hepsi aynı `serverTick`'i
taşır. **İstemcide birleştirme mantığı YOKTUR ve gerekmez** — her girdi bağımsız uygulanır, tıpkı
snapshot parçalamasında olduğu gibi (§6.3).

**Hedef:** UDP kayıtlı tüm online endpoint'ler (admin dahil — gözlemci de gövdeleri görür).
⚠️ **Gönderen kendi girdisini geri alır ve KENDİSİ yok sayar** (kendi gövdesi zaten yerelde
çözülüyor; oyuncuya ondan hiçbir şey çizilmez).
Hedefe özel batch üretmek tik başına N serileştirme demek olurdu; §6.5 olay batch'i de aynı
gerekçeyle atanı süzmüyor.

⚠️ **Snapshot'a (`0x05`) birleştirilmez.** Snapshot 16 girdide zaten 1414 B; değişken uzunluklu bir
blok eklemek `0x05`'in boyut garantisini çökertir.

⚠️ **Kırpılmış girdiden sonra okuma DURUR.** Girdiler değişken uzunluklu olduğu için kırpılmış bir
blob'un ardındaki baytların nerede başladığı bilinemez; okuyucu o girdiyi boş blob sayar
(§6.9) ve döngüyü keser. Okumayı sürdürmek akış sonunda istisna atar ve **ondan önce okunmuş sağlam
girdileri de** düşürürdü.

**Girdi yoksa paket yok** — lobide ve gövdesiz anlarda bu kanal tümüyle susar (`0x04` ile aynı
davranış; bayat durum temizliği snapshot'ın işidir).

⚠️ **İstemci düşürme kuralı iskelete DAYANMAZ:** uzak avatarın yaşam süresi snapshot'tan gelir
(§6.3, ~1.5 sn). İskelet akışı kesilirse avatar kaybolmaz, gövdesi son karede donar — gövdeyi
kaybetmek avatarı kaybetmekten iyidir.

## 7. DTO tasarım kuralları

- **Paylaşılan kaynak:** tüm DTO'lar + `ArenaProtocol` sabitleri + binary yazıcı/okuyucular `Assets/_Shared/Net/Protocol/` altında **saf C#** (`UnityEngine`'e referans YASAK — asmdef `noEngineReferences:true`; server csproj aynı dosyaları `<Compile Include>` ile derler, Unity API kullanılırsa server derlemesi kırılır = otomatik bekçi).
- **JsonUtility kısıtları** (Unity tarafı bunları kullanır): Dictionary YOK, polimorfizm YOK, property değil **public alan**, sınıflar `[Serializable]`. Binary tarafında `BinaryWriter/BinaryReader` yerine elle offset'li `Span<byte>`/`BitConverter` KULLANMA tartışması yok — v1'de basit `BinaryWriter/BinaryReader` (little-endian garanti: `BinaryWriter` zaten LE).
- Unity DTO'larında `[UnityEngine.Scripting.Preserve]` KULLANILMAZ (saf C# dosyaları Unity attribute'u içeremez); IL2CPP stripping'e karşı **`Assets/link.xml`**'de `VortexArena.Protocol` ve `VortexArena.Net` assembly'leri preserve edilir.
- .NET sunucu JSON için `System.Text.Json` + `JsonSerializerOptions { IncludeFields = true }` (DTO'lar public ALAN olduğu için şart) kullanır; alan adları camelCase birebir aynı.

## 8. Bağlantı yaşam döngüsü

```
İstemci: aç → discovery (komut satırı --server-ip verildiyse onu kullan; yoksa elle girilmiş IP
         (PlayerPrefs); yoksa beacon dinle 5 sn; yoksa StreamingAssets/arena.json statik IP)
       → ws://ip:47821/ws bağlan → hello → welcome (playerId + udpToken + match durumu)
       → UDP kayıt (0x00, ack'e dek tekrar) → geç katılımsa sahne senkronu
       → StatusLoop (5 sn; status.rosterVersion ile roster uzlaştırması) + (player ise,
         Live/Lobby fark etmez) PoseLoop (20 Hz)
Kopma  → 1→2→5 sn backoff ile discovery'den itibaren baştan (SONSUZ — aşağıdaki nota bak)
       → bağlantısızlık ~3 sn sürerse istemci hata ekranı gösterir (sunum; §4 notu)
Sunucu : hello'suz bağlantıyı 10 sn içinde kapat; deviceId çakışmasında eskisini kapat
       → roster değişince TEK yayıncı üzerinden lobby_state (version artar); status.rosterVersion
         geride kalan istemciye YALNIZ ona tam snapshot yollatır
       → soket düştü VEYA HEARTBEAT_TIMEOUT (15 sn) doldu → connection=reconnecting,
         soketi kapat, lobby_state yayınla
       → reconnecting RECONNECT_GRACE (45 sn) boyunca sürdü → oyuncu oyundan çıkarılır:
         maç katılımcısıysa connection=left (kayıt maç sonuna kadar durur),
         değilse kayıt silinir ve playerId havuza döner. Admin her iki adımda da SİLİNİR (§2)
       → match_end: defter DOKUNULMAZ (ayrılmış oyuncular maç sonu tablosunda görünmeli)
       → return_to_lobby: yayın gittikten SONRA left kayıtlar silinir, inMatch bayrakları temizlenir
```

⚠️ **İstemci `RECONNECT_GRACE` dolduktan sonra da denemeyi bırakmaz.** Süre sunucunun kaydı ne zaman
düşüreceğini söyler, başlığın ne zaman pes edeceğini değil: sahada bir gözlüğün ağ dönene kadar ölü
beklemesi, operatörün her kopan başlığa elle gitmesi demektir. Süre dolduğunda değişen tek şey
sunumdur (§5.3 `reconnectSeconds` biter, ekran "oyundan çıkarıldınız — yeniden bağlanılıyor" der);
ağ geri gelince başlık normal `hello` ile katılır ve maç sürüyorsa **eski satırına oturur** (§2).

⚠️ **İstemcinin geri sayımı sunucununkiyle her zaman aynı anda başlamaz.** Bağlantısız istemci
sunucudan sayı alamadığı için süreyi kendi kopuş anından sayar: soket düzgün kapanırsa iki saat
birlikte başlar, ama Wi-Fi **sessizce** ölürse sunucu düşüşü ancak `HEARTBEAT_TIMEOUT` sonunda fark
eder ve istemcinin sayacı 15 sn'ye kadar erken biter. Sapma bilerek bu yöndedir: ekran "çıkarıldın"
derken sunucu kaydı hâlâ tutuyor olabilir, tersi olamaz — oyuncuya hiçbir zaman olduğundan fazla
süre vaat edilmez. ⚠️ Bu yüzden **admin başlığında bu iki hâl HİÇ gösterilmez** ve bugünkü
"sunucuya bağlanılamıyor" metni kalır: admin kaydı kopar kopmaz silinir (§2), ona bir geri sayım
göstermek yalan olurdu.

## 9. Güvenlik (v1)

LAN-içi, auth YOK. `hello`'ya ileride `token` alanı eklenebilir (rezerve). Sunucu yalnız özel ağda; Windows Firewall kuralları `Server/firewall-kur.cmd` ile (TCP 47821 + UDP 47820/47822).

## 10. Maç akışı + savaş kuralları (sunucu-otoriter)

Tüm kural otoritesi sunucudadır (`MatchDirector` + `Modes/<X>Mode.cs : IGameMode`). İstemci **sunum + girdi**dir: hasar uygulamaz, skor tutmaz, faz değiştirmez.

### 10.1 Durum modeli — dört alan, dört ayrı sahip

Maçın durumu **tek bir enum değildir.** Dört alan taşınır (`welcome.match` + `match_state`), her
birinin tek sahibi vardır:

| Alan | Sahibi | Değerler | Anlamı |
|---|---|---|---|
| `modeId` | operatör seçimi | `lobby` · `tdm` · `ffa` · … | **Ne oynanıyor.** Lobi de bir türdür (§10.7) |
| `phase` | çekirdek (`MatchDirector`) | `paused` · `playing` · `finished` | **Maçın genel durumu** |
| `phaseReason` | çekirdek | `""` · `lobby` · `loading` · `countdown` · `operator` · `mode` | **Neden** duraklı (yalnız `paused` iken dolu) |
| `modeState` | mod (`IGameMode`) | serbest string (`round3`, `regroup`, …) | **Modun kendi ara durumu.** Çekirdek yorumlamaz |

⚠️ **`phase`'in TEK yetkisi hasar kapısıdır:** `hit_report` yalnız `playing` fazında işlenir
(§10.3). Başka hiçbir kural doğrudan `phase`'e bakmaz — "ateş edebilir miyim", "silahım nereden
gelir", "hangi HUD" sorularının cevabı **moddan** gelir (§10.5).

⚠️ **`modeState` asla bir kural/hasar kapısı olamaz.** Çekirdek onu okumaz, yalnız HUD okur. Bir mod
oyunu durdurmak isterse çekirdekten `phase = paused` + `phaseReason = "mode"` ister ve gerekçesini
`modeState`'e yazar. Aksi hâlde ikinci bir otorite doğar ve "maç koşuyor mu" sorusunun iki cevabı olur.

**Bugün kullanılan `modeState` değerleri** (yalnız `tournament`, §10.5 — sözlük modun kendisine aittir):

| Değer | Faz | Anlamı / HUD |
|---|---|---|
| `round:<n>` | `playing` | Kaçıncı tur oynanıyor. HUD skor satırına "TUR n" yazar |
| `regroup:<hazır>/<toplam>` | `paused` + `mode` | Turlar arası toplanma: kaç oyuncu tabanına döndü. HUD "TOPLANMA 2/6" yazar |

⚠️ Bu tablo **çekirdeğin sözleşmesi değildir** — `MatchDirector` bu stringleri hiç ayrıştırmaz.
Yeni bir tur tabanlı mod kendi sözlüğünü tanımlar ve buraya bir satır ekler.

```
              start_match                 herkes set_ready | LOADING_TIMEOUT
paused ─────────────────────► paused ──────────────────────────────► paused
(lobby)                       (loading)                              (countdown)
   ▲                                                                    │ 0'a indi
   │ return_to_lobby                                                    ▼
   └──────────── finished ◄──── maç sonu ◄──────────────────────────  playing
       (operatör seçer; MATCH_END_SECONDS emniyeti)                   ▲    │
                                                                      │    │ operatör duraklattı
                                                                      └────┘ / mod istedi
                                                                    paused(operator|mode)
```

**`paused`'da hasar alma da verme de kapalıdır — başka ek kuralı yoktur.** Süre işlemez, skor
değişmez. `finished` de aynı şekilde hasarsızdır; farkı skorların kesinleşmiş olmasıdır.

**Neden lobi bir faz değil:** `phase` sadece hasar kapısına indirgenince "hasar yok ama ateş
serbest" bir maç durumu olmaktan çıkıp bir **tür özelliği** olur. Lobi faz olarak dursaydı,
turnuva gibi kendi ara durumu olan her yeni mod da çekirdek enum'unu büyütmek zorunda kalırdı —
modlar çekirdeğe dokunamaz. Bunun yerine lobi `modeId:"lobby"` + `rules.fireWhilePaused:true`
ile tanımlanır (§10.5, §10.7).

- **`start_match` doğrulaması** (sırayla): `modeId` sunucudaki `IGameMode` kayıtlarında var; `sceneName` boş değil; **`sceneName` `config/maps.json` harita tablosunda var ve o harita `modeId`'yi destekliyor** (harita girdisindeki `modes` boşsa kısıt yok; **tablo boşsa — maps.json yoksa — bu adım tümüyle atlanır**); `sceneName` tüm çevrimiçi oyuncuların `hello.scenes` listesinde var. Geçmezse komut reddedilir ve konsola sebep yazılır (durum değişmez). İki oyuncu+ varken takımlar dengelenir (boş takım kalmaz); tek oyuncuyla ve **hiç oyuncu yokken** başlatmaya izin verilir (konsolda uyarı) — ikincisi admin gözlemcinin haritayı boş arenada açması için vardır.
  ⚠️ **`lobby` bir `IGameMode` DEĞİLDİR** → `start_match{"lobby"}` "bilinmeyen mod" diye reddedilir. Yani **lobi türü seçiliyken maç başlatılamaz** (§10.7); bunun için ayrı bir kural yazılmaz, kayıtlı olmaması yeterlidir.
- **Oyuncusuz maç (yalnız admin):** `load_match` yalnız adminlere gider, yükleme kapısında beklenecek `set_ready` olmadığı için doğrudan geri sayıma geçilir ve maç normal işler (skor 0, süre akar). Ayrım şu: **oyuncularla başlamış** bir maçta yükleme sırasında son oyuncu da düşerse sunucu maçı bırakıp açık sahneye döner; oyuncusuz **başlatılmış** maçta dönmez — çıkış operatörün `abort_match`/`return_to_lobby` komutudur.
- **Mod/harita/parametre seçimi sunucuda yaşar (çoklu admin):** admin arayüzündeki seçiciler yerel bir değişkeni değil, `set_selection` ile sunucudaki ortak seçimi değiştirir; sunucu `admin_state` ile hepsine geri yayar. `start_match` kendi `modeId`/`sceneName`'i ile gelmeye devam eder (protokol yüzeyi genişledi ama kırılmadı) ama sunucu onu aynı zamanda ortak seçime yazar — böylece maç başladığında tüm admin panelleri aynı değeri gösterir. Seçim yalnız bir niyet beyanıdır: doğrulama `start_match` anında yapılır.
- **Maç parametreleri admin'den gelebilir:** `start_match.roundSeconds`/`scoreLimit`/`countdownSeconds` doluysa (`> 0`) o maç bu değerlerle koşar; boş/`0` ise modun varsayılanı (`IGameMode.DefaultRoundSeconds`/`DefaultScoreLimit`) ya da protokol varsayılanı (`COUNTDOWN_SECONDS`) kullanılır. `scoreLimit` ayrıca `SCORE_LIMIT_UNLIMITED` (`-1`) olabilir: **sınırsız** maç — varsayılana DÜŞMEZ, hiçbir limit dalı çalışmaz (§5.2). Yani `ModeDefinition`/`IGameMode` üzerindeki sayılar **varsayılandır, kilit değil** — operatör raundu kısaltıp uzatabilir. Değer `load_match`/`match_state`/`countdown` üzerinden istemcilere zaten gidiyor, ek bir kanal doğmaz.
- **`load_match` kişiselleştirilir:** her oyuncuya kendi `yourTeam`'i gider; **takımsız modda** (`rules.teamMode == "none"`, §10.5) takım boş gider. Yükleme kapısına girerken tüm `ready` bayrakları sıfırlanır. **Çevrimiçi adminlere de bir kopya gider** (`yourTeam:""`) — admin gözlemci aynı sahneyi yükler.
- **`phaseReason:"loading"`:** istemci sahneyi yükleyince `set_ready{ready:true}` gönderir ("sahne yüklendi" anlamında). Tüm çevrimiçi **oyuncular** hazır olunca (veya `LOADING_TIMEOUT` dolunca) geri sayım başlar. Kapı yalnız `role=player` bağlantılarını sayar: admin sahneyi yüklese de `set_ready` göndermez, geri sayımı ne hızlandırır ne geciktirir.
- **`phaseReason:"countdown"`:** saniyede bir `countdown{seconds}` (5→1); 0'da faz `playing`.
- **`playing`:** `match_state` 1 Hz; `timeRemaining` sunucuda azalır; `IGameMode.OnTick` çağrılır. **Hasar yalnız burada işlenir.**
- **`finished`:** `match_end` yayınlanır ve **kazanan ekranı operatör bir şey seçene kadar durur.** Sayacı öldüren şey fazı değiştiren her komuttur: harita seçmek ya da harita seçicisindeki lobi satırı (sahneleme fazı `paused`/`lobby`'ye çeker, §10.7), `start_match`, `abort_match`/`return_to_lobby`. Operatör hiçbir şey yapmazsa `MATCH_END_SECONDS` sonra kendiliğinden `return_to_lobby` + faz `paused`/`lobby` gelir (skorlar/canlar sıfırlanır) — ama bu **emniyet subabıdır, akış değil**: tur/maç aralarını sahada hakem yönetir. `finished` iken operatör harita/mod seçebilir ve yeni maç başlatabilir.
- **`abort_match`** her durumdan `paused`/`lobby`'ye düşürür (`return_to_lobby` yayınlanır); `return_to_lobby` doğrudan aynı işi yapar.
- **Duraklatma (`phaseReason:"operator"` / `"mode"`):** `playing` iken duraklatılan maç `paused`'a geçer — süre durur, hasar kapanır, `modeState` **korunur** (mod kaldığı yerden sürer). Devam edilince `playing`'e döner. ⚠️ Operatörün duraklatması ile modun duraklatması aynı fazı üretir ama gerekçeleri ayrıdır: turnuva "herkes tabana dönsün" derken (`mode`) operatör de duraklatırsa (`operator`) HUD'un doğru mesajı gösterebilmesi için ikisi karışmamalıdır.
  - Operatörün kapısı `pause_match` / `resume_match`'tir (§5.2) ve **yalnız kendi duraklatmasını kaldırabilir** (`phaseReason == "operator"`). `mode` gerekçesini kaldırma yetkisi modundur; `loading`/`countdown` zaten kendi koşullarıyla biter.
  - `abort_match` duraklı maçta da çalışır: duraklatmak maçtan çıkmak değildir, çıkış hâlâ `abort_match`/`return_to_lobby`'dir.

**Tur tabanlı modlar (`tournament`) — çekirdek TUR diye bir şey bilmez.** Turun tamamı modun
içindedir; çekirdek yalnız dört yetenek sunar ve hiçbirini yorumlamaz:

```
paused/loading → paused/countdown → playing                     ◄── TUR n
                        ▲   │             │
                        │   │             │ mod turu bitirdi (eleme / süre)
                        │   │             ▼
                        │   │    maç bitti mi? ──evet──► finished (normal yol)
                        │   │             │ hayır
                        │   │             ▼
                        │   └───►paused/mode · modeState="regroup:2/6"
                        └─────────────────┘ modun şartı sağlandı → yeni tur
                            ▲
                            └── mod geri sayımı İPTAL etti (şart bozuldu)
```

- Duraklamayı **mod koydu** (`phaseReason:"mode"`), kaldırma yetkisi de onundur — `resume_match`
  bu duraklamayı kaldırmaz (§5.2). Duraklama boyunca **süre işlemez ve hasar yoktur** (faz `paused`).
- Yeni tur, çekirdeğin **mevcut geri sayımına** girer (`phaseReason:"countdown"`,
  `countdownSeconds` uzunluğunda) ve oradan `playing`'e döner. Yeni bir faz/gerekçe **eklenmedi.**
- Tur başında sunucu **her oyuncuya `health_update{hp:PLAYER_MAX_HP, attackerId:0}` gönderir** —
  `playing`'e girerken canların dolması sunucu içi bir tazeleme değil, telde görünen bir olaydır.
  ⚠️ Gönderilmezse tur içinde ölmüş oyuncu istemcide **ölüm ekranında kalır**: maç içi tur
  geçişinde `load_match` yoktur, yani istemcinin kendini sıfırlayacağı ikinci bir yol da yoktur.
- Toplanma kapısı **`set_ready` bayrağını yeniden kullanır** (yükleme kapısının aynısı, §5.1):
  oyuncu kendi taban bölgesine girince `set_ready{true}`, çıkınca `set_ready{false}` yollar. Yeni
  bir mesaj tipi YOKTUR ve eklenmez — "hazırım" zaten bu bayrağın anlamıdır.
- **Modun açtığı geri sayım GERİ ALINABİLİR.** Şart yalnız girişte değil geri sayım **boyunca** da
  ölçülür: bayrağı düşen tek oyuncu turu erteler, mod geri sayımı iptal eder ve faz `paused`/`mode`'a
  döner (`modeState` yine `regroup:<h>/<t>`). İstemci için ek bir mesaj yoktur — geri sayımı
  `phaseReason != "countdown"` görünce zaten siliyor, yani yayınlanan `match_state` tek başına yeter.
  ⚠️ Bunun iki sonucu var: (1) `set_ready` bildirimi geri sayım boyunca da **sürer**, (2) mod
  duraklamasından geri sayıma geçerken `ready` bayrakları **temizlenmez** (bayrak orada "şu anda
  tabanımdayım" demektir; temizlenseydi iptal kararının dayanağı kalmazdı). Bayrakları temizleyen
  tek yer toplanmanın **başıdır**.
- ⚠️ Maçın **ilk** geri sayımı bu kapıya girmez: o yükleme kapısından gelir, öncesinde toplanma
  yoktur. İstemci de sunucu da bu ayrımı "toplanmadan mı geldik" diye kendi durumundan yapar —
  `phaseReason` ikisini ayırt etmez.

### 10.2 Oyuncu maç durumu (sunucuda)

Oyuncu başına: `hp` (0..`PLAYER_MAX_HP`), `alive`, `team`, `kills`, `deaths`, `score`, ölüm zamanı. `playing`'e girerken herkes `hp=PLAYER_MAX_HP`, `alive=1`. Snapshot'taki `SnapshotEntry.flags` bit0 (`FLAG_ALIVE`) bu `alive` alanından beslenir — maç dışında (`paused`/`lobby`) herkes canlı sayılır.

⚠️ **Takımdaş öldürme skor YAZMAZ.** Dost ateşi açıkken (§5.2) takım arkadaşını öldüren vuruşta `IGameMode.OnKill` **hiç çağrılmaz** — skorun tek yazarı orası olduğu için ne takım skoru ne bireysel skor işler. `kills`/`deaths` sayaçları ve `kill_event` (kill feed) normal işlemeye devam eder: olay gerçekleşti, yalnız ödülü yok. **Ceza (−1) YOKTUR.** Kapı modun içinde değil çağrı yerindedir, böylece her yeni mod ona kendiliğinden uyar; takımsız modları etkilemez (boş takım takımdaş sayılmaz).

`score` = **bireysel maç skoru**. Yazarı yalnız `IGameMode`'dur (`MatchDirector`'ın skor defteri üzerinden); `kills` ile aynı şey DEĞİLDİR — bir mod öldürme başına 1, bir başkası objektif başına 5 yazabilir, Silah Yarışı'nda aynı alan "seviye" anlamına gelir. Maç kurulurken ve açık sahneye dönerken 0'lanır.

`hp`/`alive`/`kills`/`deaths`/`score` **`lobby_state` ile de yayınlanır** (§5.3): ölüm işlendikten sonra roster bir kez tazelenir, böylece admin istatistik tablosu sunucudaki sayaçla birebir kalır ve admin yeniden bağlandığında geçmişi kaybetmez. Anlık akış (her vuruş) yine `health_update`/`kill_event` üzerinden gider — `lobby_state` sağlama noktasıdır, sıcak yol değil.

**Maç katılımcısı defteri (`inMatch`).** Maç kurulurken o an **bağlı** olan her `role=player` kaydı
katılımcı işaretlenir; maça sonradan bağlanan da işaretlenir. Bayrağın tek işi **istatistik satırını
bağlantıdan bağımsız kılmaktır**: bağlantısı kopup `RECONNECT_GRACE`'i dolan bir katılımcı oyundan
çıkarılır (`connection:"left"`) ama kaydı — adı, forma numarası, takımı, `kills`/`deaths`/`score`'u —
**maç bitene kadar durur** ve maç sonu tablosunda görünür. Katılımcı olmayan bir kayıt aynı durumda
tümüyle silinir.

⚠️ **Defter `finished` fazının TAMAMI boyunca durur** ve ancak **lobiye dönerken**
(`return_to_lobby` yayınından sonra) kapanır — `match_end`'de silmek maç sonu tablosunu tam da
okunduğu anda boşaltırdı, oysa ayrılmış oyuncuların orada görünmesi bu defterin var olma sebebi.
⚠️ Temizlik yayından **sonra** koşar (kayıt silmek `lobby_state` tetikliyor); ters sırada son
roster satır eksik gider.

⚠️ **Ayrılmış oyuncu maçı KAZANAMAZ.** Kazanan hesabı (§10.1) **bağlı** oyuncular arasından yürür:
skoru tabloda durur ama kupayı almaz. Aksi hâlde kimsenin göremediği bir kazanan ilan edilirdi.

⚠️ **Atma (§5.4) katılımcılıktan da düşürür:** operatör bilinçli attıysa kayıt tümüyle silinir ve
maç sonu tablosunda da yer almaz — kopmadan farkı budur.

⚠️ `calibrated`/`calibrationSource` (§10.6) bu listeye **dahil değildir**: maç durumu değil cihaz durumudur, yazarı `MatchDirector` değil `PlayerRegistry`'dir (`Team` ile aynı desen — registry kilidinde yazılır, director kilidinde okunur; `bool` okuması atomik olduğu için iki kilidi birbirine bağlamaya gerek yoktur) ve maç sıfırlamalarında **korunur**.

### 10.3 Vuruş hattı — genel hasar modeli

**Hile koruması yoktur ve bilinçli olarak eklenmez.** Ürün, gözetim altındaki özel alanlarda
(işletme kurulumu, turnuva) çalışır; hile yapmanın kimseye faydası olmadığı bir ortamda hile
denetimi yalnız meşru vuruşları yiyen bir tuzaktır. Bu yüzden hasar hesabı **tamamen istemcide**
yapılır, sunucu hakemlik değil **defter tutar**: canı düşürür, ölümü ilan eder, skoru işler.

`hit_report` şu sırayla kontrol edilir; **herhangi biri düşerse paket sessizce reddedilir**
(konsola tek satır log, istemciye yanıt yok). Bunlar hile denetimi değil, **durum tutarlılığı**
kontrolleridir — kaldırılırlarsa çift ölüm / maç dışı hasar gibi hatalar üretilir:

1. Faz `playing` mi? (**tek hasar kapısı budur**, §10.1)
2. Atıcı çevrimiçi + `role=player` + `alive` + **`calibrated`** mi? (§10.6: kalibresiz oyuncu ateş edemez)
3. Hedef var, çevrimiçi, `alive` + **`calibrated`** mi? (aynı karede gelen iki ölümcül vuruş çift `kill_event` yazmasın; kalibresiz oyuncu hasar YEMEZ — §10.6)
4. Hedef **doğma koruması altında değil** mi? (§10.4: canlanan oyuncu `SpawnProtectionSeconds` boyunca hasar almaz. ⚠️ Kapı **sunucudadır** — istemci korumayı yalnız çizer, atış kararını ona dayandırmaz: atıcının ekranında kalkan bir kare geç sönse bile vuruş burada düşer)
5. Hedef atıcının kendisi değil ve **takım arkadaşı değil** mi? Kural: `rules.friendlyFire == false` iken *takım arkadaşı* vurulamaz, ve **boş takım asla takım arkadaşı sayılmaz** — takımsız modda (§10.5 `teamMode:"none"`) herkesin takımı `""` olduğu için `"" == ""` karşılaştırması tüm vuruşları reddederdi. `friendlyFire == true` ise bu adım hiç uygulanmaz. ⚠️ **Bu kapının değerini mod değil OPERATÖR belirler** (`set_friendly_fire`, §5.2) ve maç ortasında değişebilir — kapı her `hit_report`'ta yürürlükteki değeri okur. Geçen bir takımdaş vuruşu öldürücü olursa skor yazılmaz (§10.2).
6. `damage` sonlu ve pozitif bir sayı mı? (NaN/∞ canı kalıcı bozar; sayı denetimi, hile denetimi değil)

Geçerse: `hp -= damage` (istemcinin bildirdiği değer) → `health_update{playerId, hp, attackerId}`
**herkese** yayınlanır. `hp ≤ 0` ise `alive=0`, `kill_event{killerId, victimId, weaponId}` +
`IGameMode.OnKill` (skor) + kurbana `respawn{delaySeconds:RESPAWN_DELAY}`.

Atış hızı denetimi, `weaponId` beyaz listesi ve sunucu-otoriter silah tablosu **YOKTUR** ve
eklenmez: pompalı saçması, bomba parçası ve ok yaylımı gibi meşru "hızlı art arda vuruş"
örüntülerini sessizce düşürürler.

**Atış olayı** (`0x03`/`0x04`, §6.4/6.5) sunucuda **doğrulanmaz**, yalnız relay edilir (atan hariç
herkese, `playerId` ile) — ölü/**kalibresiz** oyuncunun atışı relay EDİLMEZ. Kapısı
**`phase == playing` VEYA `rules.fireWhilePaused`**'tur: lobide hedef atışı yapılabildiği için
(§10.7) başkalarının namlu alevini görmesi doğrudur. Yükleme/geri sayım/duraklatma sırasında
(`fireWhilePaused:false` olan modlarda) relay yoktur.

⚠️ **`hit_report`'un kapısı bundan AYRIDIR ve yalnız `playing`'dir** — lobide, yüklemede,
duraklatmada oyuncuya hasar verilemez. İki kapı bilerek ayrı: atış bir sunum olayı, vuruş bir
durum değişimidir. Bu yüzden "ateş edebilir miyim" moda (`fireWhilePaused`), "hasar var mı"
çekirdeğe (`phase`) bağlıdır.

⚠️ **İki kapının KANALI da ayrıdır ve bu bilinçlidir** (v4): atış olayı **UDP**'dedir (kaybı
kozmetik, güvenilirlik gerekmez), `hit_report` **WS/TCP**'de kalır (otoriter hasar, kaybı bir ölümü
yutar). `hit_report`'u UDP'ye taşıma — ve atış olayını WS'e geri getirme (10 atış/sn/oyuncu otoriter
kanalı boğar; v4'ün taşıma gerekçesi budur).

⚠️ **Atış relay kapısı UDP recv thread'inde okunur** ve `MatchDirector`'ın maç kilidine (`_gate`)
GİREMEZ — girerse 20 Hz poz alım yolunu bekletir. `PlayerState.Alive`'ın mevcut kilitsiz okuma
deseni buraya da uygulanır: faz değişiminde "atış relay edilir" bayrağı volatile yayınlanır, olay
yolu yalnız onu ve `Alive`/`Calibrated`'ı okur. Bir tik gecikme sunum için önemsizdir.

> **Denge sayıları istemcide yaşar.** Hasar/atış hızı/menzil tek kaynak olarak Unity'deki
> `WeaponDefinition` SO'larındadır; sunucuya export edilmez, `config/weapons.json` diye bir dosya
> yoktur. Bedeli bilinçlidir: denge değişikliği istemci build'i gerektirir. Karşılığında yeni bir
> silah/hasar kaynağı (balta, yay, bomba, tuzak, düşme hasarı) eklemek **sıfır sunucu işi**dir.

### 10.4 Free-roam respawn (canlanma)

Fiziksel oyuncu ışınlanamaz → **respawn = konum değil durum değişimi**:

1. Ölünce sunucu `respawn{playerId, delaySeconds}` gönderir (`delaySeconds` = `rules.respawnDelay`, §10.5); istemci ölüm ekranı gösterir, silah ateşlemez, avatar **hayalete döner** (yarı saydam; renk oyuncunun kendi takımı, takımsız modda nötr).
2. `delaySeconds` dolduktan **ve modun canlanma şartı sağlandıktan** sonra istemci `revive_request` gönderir (canlanana dek ~1 sn'de bir tekrarlar). Şart `rules.reviveAnchor` ile seçilir:
   - **`"base"`** (varsayılan, TDM): oyuncu bir **taban bölgesine** (`BaseZone` — arenadaki kırmızı/mavi şerit) fiziken girer. Ölüm ekranı "Tabanına dön ve canlan" der.
   - **`"standstill"`**: oyuncu ölüm anındaki HMD konumunu çapa alır ve `REVIVE_HOLD_RADIUS` içinde `REVIVE_HOLD_SECONDS` boyunca kesintisiz sabit durur; çapadan çıkınca sayaç ve çapa sıfırlanır. Taban bölgesi olmayan modlar (FFA) bunu kullanır. ⚠️ **İç engelin içinde geçen süre sayılmaz** (§10.9): sayaç orada ilerlemez, çapa her karede tazelenir ve süre engelden **çıkıldıktan sonra** sıfırdan başlar — şart "kıpırdama" değil, *meşru bir yerde* kıpırdamamaktır; sayılsaydı engel bir sığınağa dönerdi (içeride bekle, çıkar çıkmaz canlan).
   - **`"none"`**: canlanma **YOKTUR**. İstemci `revive_request` hiç göndermez, sunucu gelirse reddeder. Ölü oyuncuyu yalnız modun başlattığı yeni bir tur canlandırır (tur tabanlı eleme, `tournament` §10.5). Ölüm ekranı "canlanmaya N sn" değil "takımın turu bitirene kadar bekle" der.
3. Sunucu doğrular (faz `playing`, oyuncu ölü, gecikme dolmuş, **kalibreli**) → `hp=PLAYER_MAX_HP`, `alive=1` → `health_update{hp:100, attackerId:0}`.

> **`reviveAnchor` sunucuda DOĞRULANMAZ.** §10.3 felsefesinin aynısı: sunucu hakemlik değil defter tutar. "Tabanda mı / sabit mi durdu" kararı istemcinindir; sunucu faz + ölü + gecikme kontrolüyle yetinir.

**Doğma koruması.** Canlanan oyuncu `ModeRules.SpawnProtectionSeconds` boyunca **hasar almaz**:
`hit_report` ona ulaşmadan reddedilir (§10.3, kapı sırası orada). Süre **modun kuralıdır** —
`tdm` ve `ffa`'da 5 sn, değer yazmayan modlarda `0`, yani "koruma yok" varsayılan davranıştır ve
hiçbir modun bugünkü akışı bu alanla değişmez.

- **Damga tek kapıdan vurulur:** canlanmanın tek yolu olan `RevivePlayerLocked` — yani hem
  `revive_request` hem operatörün `revive_player`'ı.
- ⚠️ **Maç/tur BAŞLANGICI koruma vermez.** `playing`'e giren herkes — o an ölü olan da canlı olan
  da — **korumasız** başlar (`EnterLiveLocked` damgayı `MinValue`'ya çeker, ölü dalı
  `RevivePlayerLocked`'ı `spawnProtect:false` ile çağırır). Gerekçe: koruma ölüp dönen oyuncuyu
  doğduğu karede vurulmaktan korumak içindir, oysa maç başında herkes aynı anda ve geri sayımla
  başlıyor — orada koruma yalnız maçın ilk saniyelerini hasarsız kılardı. ⚠️ İki dal da aynı
  davranır: biri korumalı diğeri korumasız başlasa aynı maçta iki farklı kural olurdu.
- **Ölümde silinir.** Ölü oyuncunun telde korumalı görünmesi anlamsızdır; snapshot biti bu yüzden
  `FLAG_ALIVE` ile birlikte okunur (kalkan hayaletin üstüne çizilmez).
- ⚠️ **Süre TELDE GİTMEZ.** `rules` nesnesinde karşılığı yoktur (§10.5) ve istemci koruma durumunu
  **yalnız snapshot bit6'dan** (`FLAG_SPAWN_PROTECTED`, §6.3) okur; istemcide sayaç tutulmaz.
  Sayıyı da yollamak ikinci bir doğruluk kaynağı olurdu: bayrak her snapshot'ta yeniden geldiği
  için koruma bitince ek bir mesaj olmadan kendiliğinden söner.
- İstemci tarafı **yalnız sunumdur**: korunan oyuncunun gövdesinin üstüne kalkan kabuğu çizilir
  (`Docs/Sistem-Ozeti.md` §4, `RemoteAvatar`). Kalkan **takım renginde değildir** — anlattığı şey
  takım değil dokunulmazlıktır, takım rengi zaten hayaletin dili. Atış kararı buna dayandırılmaz —
  hasarın olup olmayacağına sunucu karar verir.
- ⚠️ **Oyuncu kendi kalkanını GÖREMEZ** ve bu yapısaldır: kalkan uzak avatarlara çiziliyor, oyuncunun
  kendi gövdesi ise hiç çizilmiyor (gördüğü eller rig'in sentetik elleri). Bu yüzden korumanın
  oyuncuya ulaştığı yol **HUD durum satırıdır** ("Yeniden doğma koruması"). Kaynağı yine aynı
  bayraktır: istemci **kendi** snapshot girdisinin bit6'sını okur — o girdinin POZU yok sayılıyor
  (sunucu echo'su) ama durum bitleri okunuyor. Ayrı bir mesaj/alan eklenmez.
- **Engel hasarı (§10.9) bu korumanın DIŞINDADIR** ve bilerek: canlanma zaten engelin içinde
  reddedildiği için taze doğmuş oyuncu yapısal olarak engelde olamaz; oraya ikinci bir kapı koymak
  okuyanı olmayan bir dal olurdu.

**Operatör yolu.** Canlandırmanın **iki yolu vardır**: oyuncunun `revive_request`'i (yukarıdaki üç
adım) ve operatörün `revive_player`'ı (§5.2). Sunucunun zamanlayıcı tabanlı bir canlandırması —
yani üçüncü, kendiliğinden işleyen bir emniyet ağı — yoktur: modun şartını sağlamayan oyuncu
kendiliğinden geri gelmez, onu ya kendi şartı ya operatör canlandırır. Operatör yolu `MatchDirector`
üzerinden aynı `hp=PLAYER_MAX_HP` / `alive=1` / `health_update{hp:100, attackerId:0}` sonucunu yazar;
skor defterine ve `deaths` sayacına dokunmaz.

> ⚠️ **Canlanma yasakları İKİ YOLDA da durur — hangisinin hangisini geçtiği bilinçli bir ayrımdır:**
>
> | Yasak | `revive_request` | `revive_player` (operatör) |
> |---|---|---|
> | **Kalibrasyon** (§10.6) | Uygulanır — kalibresiz oyuncunun talebi reddedilir, yani **kalibre olana dek ölü kalır**; kalibrasyon gelince gecikme zaten dolmuş olduğu için ilk tekrarlanan talepte canlanır | Uygulanır — kalibresiz oyuncu ateş edemez ve vurulamaz, canlandırmak onu savaşa döndürmez |
> | **Engelin içinde olmak** (§10.9) | Uygulanır — oyuncu çıkana kadar talep reddedilir. Diğerlerinden farkı yasağın **tavanlı** olmasıdır (`OBSTACLE_REVIVE_BLOCK_SECONDS`): kapı istemcinin bildirdiği bir bayrağa bakıyor, tavansız bırakılırsa yanlış konuşan bir istemci oyuncuyu kalıcı ölü bırakırdı | Uygulanır — engelin içinde canlanan kör kalır ve tolerans dolar dolmaz yeniden ölür, komut bir ölüm döngüsü üretirdi |
> | **`reviveAnchor:"none"`** (§10.5) | Uygulanır — istemci talebi hiç göndermez, sunucu gelirse reddeder. Ölü oyuncuyu yalnız modun başlattığı yeni tur canlandırır; "tur içinde canlanma yok" kuralı buradan gelir | **GEÇİLİR** — komutun varlık sebebi budur: takılan oyuncu turnuvada da kurtarılabilmeli. Tur sonucunu değiştirmesi operatörün bilinçli kararıdır |
> | **Canlanma gecikmesi** (`rules.respawnDelay`) | Uygulanır | **GEÇİLİR** — operatör beklemez |
>
> ⚠️ Genel kural: **bir oyuncu durumuna yasak koyarken o durumu değiştiren TÜM yolları ara** — bir
> yasak, tüm yolları kapatmadıkça yoktur. Kalibrasyon ve engel yasakları bu yüzden iki yolda da
> tekrarlanır; yalnız birine konsaydı operatörün tek tuşu ikisini birden delerdi. Mod kuralının
> yalnız operatör yolunda geçilmesi bunun istisnası değil, **açıkça verilmiş** bir karardır.

**Taban bölgesi eşleşmesi (istemci):** bir `BaseZone` oyuncuya açıktır eğer takımı oyuncunun takımıyla aynıysa, **ya da** bölge `Neutral` işaretliyse, **ya da** oyuncunun takımı boşsa (takımsız mod). Aynı takıma ait birden çok bölge varsa **herhangi birine** girmek yeter. Sahnede hiç açık bölge yoksa şart aranmaz. ⚠️ Bu fail-open davranış kritiktir: sunucu tarafında kendiliğinden işleyen bir emniyet ağı yoktur, yani taban bölgesi eksik/yanlış takıma ait bir sahnede oyuncuyu kalıcı ölü kalmaktan kurtaran **yalnız** istemcinin bu davranışıdır — geriye kalan tek çare operatörün elle canlandırmasıdır (`revive_player`, §5.2).

**Konum diye bir alan protokolde YOKTUR.** Ne `load_match` ne `respawn` bir spawn noktası/slotu taşır; sunucu sahne geometrisini bilmez ve rig'i bir yere taşıyan mekanizma yoktur — oyuncu fiziksel olarak nerede duruyorsa orada canlanır. Telde taşınan tüm koordinatların sıfırı sahnenin **dünya orijinidir** (bkz. koordinat uzayı bölümü).

**Harita değişimi kalibrasyonu sıfırlamaz.** `load_match` oyuncu için yalnız bir sahne değişimidir: kimse "yeniden doğmaz", rig taşınmaz. Yeni sahnenin `ArenaCalibrator`'ı `Start`'ta kayıtlı `OVRSpatialAnchor`'dan hizalamayı geri yükler, oyuncu fiziksel olarak nerede duruyorsa orada kalır. **Poz gönderimi hizalamayı beklemez:** `PlayerPoseTracker` baştan kaydolur, hizalama gelene dek gönderilen pozlar arena ile örtüşmez (rig ofsetli) ama akar — oyuncunun bağlı ve hareket hâlinde olduğu ağdan görülebilsin diye. Sunucu bu ayrımı bilmez; pozlar her hâlde `PoseUpdate` olarak kabul edilir ve snapshot'a girer.

### 10.5 Mod kuralları (`ModeRules` / `rules`)

Bir modun **ne tür bir oyun olduğunu** anlatan, **sunucu-otoriter** şekil tanımı. Her `IGameMode`
kendi `Rules`'ünü döner; sunucu bunu `load_match.rules` ve `welcome.match.rules` ile istemciye
yollar. Amaç tek: **istemci modun ne olduğunu TAHMİN ETMESİN.** Kural telden gelirse istemcide
`if (modeId == "ffa")` zinciri hiç doğmaz — yeni mod eklemek istemci kodunu değiştirmez.

```json
"rules": { "teamMode":"two", "scoring":"team", "friendlyFire":false,
           "reviveAnchor":"base", "weaponSource":"weaponcanvas", "respawnDelay":5.0,
           "fireWhilePaused":false }
```

| Alan | Değerler | Varsayılan | Anlamı |
|---|---|---|---|
| `teamMode` | `"two"` \| `"none"` | `"two"` | `"two"`: kırmızı/mavi, sunucu takımları dengeler, slot takım içi. `"none"`: takım yok (`team:""`), slot tek havuzdan |
| `scoring` | `"team"` \| `"player"` | `"team"` | Skor kime yazılır: `match_state.scoreRed/scoreBlue` mi, `lobby_state → PlayerInfo.score` mü (§10.2) |
| `friendlyFire` | `true` \| `false` | `false` | `false` = takım arkadaşı vurulamaz (§10.3, dost ateşi kapısı). Boş takım asla takım arkadaşı sayılmaz. ⚠️ **Bir mod kuralı DEĞİL, operatör anahtarıdır** — aşağı bak |
| `reviveAnchor` | `"base"` \| `"standstill"` \| `"none"` | `"base"` | Canlanma şartı (§10.4/2). `"none"` = tur içinde canlanma yok; `revive_request` reddedilir — bu kuralı yalnız operatörün `revive_player` komutu bilerek geçer (§5.2) |
| `weaponSource` | `"weaponcanvas"` \| `"random"` | `"weaponcanvas"` | Silah nereden gelir: `"weaponcanvas"` = sahnedeki **çerçeveler** (silah çerçeveden ayrılmaz ve tükenmez; seçilen silah grip'e basılınca oyuncunun eline **klonlanır**), `"random"` = modun dağıtımı. **Tümüyle istemci sunumu** — sunucuda karşılığı yok (§10.3: silah tablosu yoktur) |
| `respawnDelay` | saniye | `RESPAWN_DELAY` (5) | `respawn.delaySeconds` ve sunucudaki `revive_request` gecikme eşiği. **`0` geçerli bir değerdir** (anında canlanma) ve varsayılana çekilmez — alan hiç gönderilmezse DTO'nun kendi başlangıcı geçerli olduğu için "yazılmadı" ile "sıfır yazıldı" karışmaz |
| `fireWhilePaused` | `true` \| `false` | `false` | Faz `playing` değilken silah ateşlenebilir mi. `true` = lobi gibi serbest atış alanı: namlu alevi/ses relay edilir (§10.3) ama **hasar yine yoktur** (`hit_report` kapısı `playing`). Bu alan sayesinde istemcide `if (modeId == "lobby")` zinciri doğmaz |

- **Varsayılan = bugünkü TDM.** Bir mod hiçbir alan yazmazsa bugünkü davranışı alır; yani yeni mod
  yalnız *farklı* olduğu alanları belirtir.
- ⚠️ **`ModeRules`'un her alanı telde taşınmaz.** `SpawnProtectionSeconds` (doğma koruması, §10.4)
  sunucu tarafında bir mod kuralıdır ama `ModeRulesInfo`'ya — yani yukarıdaki `rules` nesnesine —
  **alan olarak EKLENMEZ**: istemcinin süreyle yapacağı bir iş yoktur, korumayı snapshot bit6
  sürer (§6.3). Genel ölçüt budur: bir kural şekli telde ancak **istemcide bir tüketicisi varsa**
  yer alır; tüketicisiz alan, sunucudaki değerle sessizce sapabilen ikinci bir doğruluk kaynağıdır.
- ⚠️ **`friendlyFire` bu tablonun tek istisnasıdır: modun değil OPERATÖRÜN alanıdır.** Değeri
  sunucuda yaşar (`set_friendly_fire`, §5.2), açılışta `false`'tur ve **modlar onu bildirmez** —
  bir modun kendi kuralında bu alana değer yazması, operatörün anahtarını sessizce ezmek olurdu.
  Telde taşıdığı şey "bu modun tercihi" değil **"o an geçerli değer"**dir; kural şekline
  damgalanması sunucuda **tek kapıdan** yapılır (modun/lobinin kuralı + yürürlükteki anahtar).
  Maç ortasında değişince `rules_update` ile herkese yayılır (§5.3), geç bağlanan `welcome`'dan
  alır. Anahtarı maç başlangıcı, harita sahneleme ve lobiye dönüş **sıfırlamaz**.
- **Bilinmeyen/boş değer varsayılana düşer.** Değerler bilerek string: eski istemci yeni sunucudan
  tanımadığı bir `teamMode` görürse takımlı TDM gibi davranır, bağlantı kopmaz. Bu yüzden yeni bir
  kural değeri eklemek `PROTOCOL_VERSION`'ı **artırmaz**.
- **Kazanan ifadesi `scoring`'e bağlıdır:** `"team"` → `match_end.winnerTeam`, `"player"` →
  `match_end.winnerPlayerId`.
**Kayıtlı modlar** (sunucuda `MatchDirector.RegisterModes()`; `start_match.modeId` bunlardan biri
olmalı, tanınmayan `modeId` reddedilir):

| `modId` | Ad | `teamMode` | `scoring` | `reviveAnchor` | `weaponSource` | `respawnDelay` | `fireWhilePaused` | Varsayılan süre / limit |
|---|---|---|---|---|---|---|---|---|
| `tdm` | Takım Ölüm Maçı | `two` | `team` | `base` | `weaponcanvas` | `5` | `false` | 300 sn / 30 |
| `ffa` | Herkes Tek | `none` | `player` | `standstill` | `random` | `0` | `false` | 300 sn / 20 |
| `tournament` | Turnuva | `two` | `team` | **`none`** | `weaponcanvas` | `0` | `false` | 120 sn / 4 tur |

> ⚠️ **`friendlyFire` bu tabloda YOKTUR:** artık bir mod kuralı değil **operatör anahtarıdır**
> (§5.2) ve üç modda da aynı kaynaktan gelir. Modlar onu bildirmez.

> **`tournament` — tur tabanlı takım elemesi.** TDM varsayılanından ayrıldığı **tek** kural
> `reviveAnchor:"none"`dır; geri kalan her şey varsayılandır. Tur kavramı bir kural alanı DEĞİLDİR
> ve `ModeRules`'a girmez — turlar modun iç durumudur, çekirdek onları bilmez (§10.1 "tur tabanlı
> modlar"). Telde görünen tek izleri `modeState` (`round:<n>` / `regroup:<h>/<t>`) ve tur başındaki
> `health_update`'lerdir.
>
> | Soru | Cevap |
> |---|---|
> | Skor ne sayar? | `scoreRed`/`scoreBlue` = **kazanılan tur sayısı** (öldürme değil) |
> | `roundSeconds` neyin süresi? | **Turun**, maçın değil. Süre dolunca maç bitmez, tur biter |
> | `scoreLimit` ne? | Maçı kazanmak için gereken tur sayısı. Tavan `2 × scoreLimit − 1` tur (best-of); tavanda yüksek skor kazanır, eşitse berabere |
> | Sınırsız tur olur mu? | Olur: `scoreLimit = SCORE_LIMIT_UNLIMITED` (`-1`, §5.2) → **galibiyet limiti de tur tavanı da yok**, turlar birbirini izler. Tek çıkış operatörün `abort_match`'idir; ⚠️ süre dolması maçı bitirmez, yalnız turu bitirir |
> | Tur nasıl biter? | Bir takımın **tüm** çevrimiçi oyuncuları ölü → diğer takıma +1. Süre dolarsa **çok kişi ayakta kalan** kazanır; eşitse kimseye puan yok |
> | Ayakta sayımında kim sayılır? | Yalnız `alive` **ve** `calibrated` oyuncular (§10.6): kalibresiz oyuncu ne vurur ne vurulur, savaş dışıdır. **Eleme** kontrolünde ise kalibrasyona bakılmaz — kalibresiz oyuncu ölü değildir, takımını ayakta tutar (tur süreye gider, kıyas onu zaten dışarıda bırakır) |
> | Eleme neden `OnKill` ile değil tik ile ölçülür? | Takım **bağlantı kopmasıyla** da boşalır ve o yolda `OnKill` hiç çağrılmaz. Tek tarama = tek doğruluk kaynağı |
> | Turlar arası ne olur? | `paused`/`mode`, `modeState:"regroup:<h>/<t>"`. Geri sayım **yalnız** herkes kendi taban bölgesine girip `set_ready{true}` yollayınca başlar — zaman aşımı YOKTUR, bekleme süresizdir |
> | Geri sayımda biri tabandan çıkarsa? | Geri sayım **iptal edilir**, faz `paused`/`mode`'a döner ve sayaç sıfırdan başlar. Kural "tabanda **bekle**"dir, "tabana uğra" değil. ⚠️ İptalin **istisnası yoktur**: geri sayım her koşulda geri alınabilir |
> | Toplanma takılırsa ne olur? | Çıkış operatöründür: takılan oyuncuyu **atar** (`kick`) ya da `abort_match` yapar. Atılan/kopan oyuncu toplamdan düştüğü için kalanlar hazırsa tur o an başlar — sayım her tikte çevrimiçi oyunculardan yeniden yapılır. Bekleme uzarsa sunucu konsoluna 30 sn'de bir "toplanma bekleniyor (h/t) — tabanına dönmeyenler: …" satırı düşer; bu bir **teşhis** satırıdır, tur başlatmaz |
> | Cephane? | Şarjör + yedek şarjör (`weaponSource:"weaponcanvas"`), **her tur başında herkes tam dolu** — istemci geri sayımda doldurur. Sunucunun bundan haberi yoktur (§10.3: silah tablosu yok) |
> | Taraflar yarıda değişir mi? | **Hayır, taraf değişimi (side swap) YOKTUR.** Free-roam'da taraf değiştirmek oyuncuların fiziksel olarak karşı tabana yürümesi demektir ve arena simetrik olmadığı sürece karşılığı da azdır. İstenirse ayrı bir iş olarak planlanır |

> ⚠️ **`lobby` bu tabloda YOKTUR ve olmayacaktır.** Lobi bir **tür**dür ama `IGameMode` değildir
> (§10.7): sunucuda kaydı olmadığı için `start_match{"lobby"}` "bilinmeyen mod" diye reddedilir —
> yani lobi türü seçiliyken maç başlatılamaz. Kural şekli yine de tanımlıdır ve telde taşınır:
> `fireWhilePaused:true` + `weaponSource:"random"`, geri kalanı varsayılan. Lobi `modeId`'si
> istemcide silah loadout'unu, HUD'u ve ateş serbestliğini çözer.

> `ffa` satırı kuralların somut örneğidir: **takım yok** (`team:""` gelir, `winnerPlayerId`
> dolar), ölünce 5 sn'lik gecikme yerine **sabit durma** şartı işler (`REVIVE_HOLD_SECONDS` = 5 sn,
> `REVIVE_HOLD_RADIUS` = 1 m) ve silah sahnedeki çerçevelerden değil **istemcinin dağıtımından** gelir.
> Dost ateşi anahtarı FFA'yı **hiç etkilemez** — boş takım asla takım arkadaşı sayılmadığı için
> (§10.3, dost ateşi kapısı) kapı zaten hiç kapanmaz; bu yüzden FFA o alana değer yazmaz.
> **`weaponSource` sunucuyu hiç ilgilendirmez** (§10.3: silah tablosu yok) — telde yalnız
> istemciye "silahı nasıl vereceksin" diye taşınır.

> ⚠️ **Bu alanın adını değiştirmek `PROTOCOL_VERSION`'ı artırmaz:** değer `"random"` DEĞİLSE
> varsayılana düşülür ve varsayılan `"weaponcanvas"`ın kendisidir, yani tanınmayan bir yazım
> karışık sürümde iki yönde de doğru yere çözülür.
> ⚠️ **Bu serbestlik ÜÇÜNCÜ bir kaynak türü eklenince biter:** o zaman açık bir eşleşme dalı
> gerekir ve onu tanımayan eski istemci sessizce varsayılana düşer (yanlış silah davranışı) —
> `PROTOCOL_VERSION` o değişiklikte artar.

- **3+ takım bugün YOK.** Geldiğinde yol açık: `PlayerInfo.team` zaten serbest string
  (`"green"`/`"yellow"` bugün de geçer) ve `match_state`'e `teamScores:[{team,score}]` eklenir;
  `scoreRed`/`scoreBlue` iki takımlı modlar için kısayol olarak kalır. Karar **o mod gelince**
  verilir — şimdi yapılırsa tüketicisi olmayan bir soyutlama için TDM ve admin arayüzü baştan yazılır.

**İstemcide tek okuma noktası:** `VortexArena.Core.ModeRuntime` (statik). `load_match`/`welcome`/
`return_to_lobby` onu besler, `rules_update` (§5.3) maç ortasında tazeler; canlanma (`PlayerCombatState`), skor satırı (`ModeHudBase`) ve admin takım kipi
(`AdminRoster`) yalnız oradan okur. Dördü ayrı ayrı `load_match` dinlerse dördü ayrı ayrı bayatlar.
Kurallar telde gelmediğinde (`rules == null` — kuralları taşımayan bir sunucu) `ModeDefinition`'ın
önizleme alanları devralır; **sapmada sunucu kazanır** — `ModeDefinition`'daki kural alanları
yalnız önizleme/fallback içindir.

### 10.6 Kalibrasyon durumu (sunucu-otoriter)

Oyuncu başına `calibrated` (bool) + `calibrationSource` (string) sunucuda tutulur ve `lobby_state`
ile yayılır (§5.3). Amaç operasyoneldir: sahada bir başlığın hizalaması kayar, o oyuncunun avatarı
fiziksel konumundan sapar — operatörün onu **maçtan çıkarmadan** savaş dışı bırakıp yeniden
kalibre ettirebilmesi gerekir.

**İki yazar, ama asimetrik:**

| Yazar | Mesaj | Ne yapabilir |
|---|---|---|
| Başlık | `set_calibration` (§5.1) | `true` **ve** `false` |
| Admin | `clear_calibration` (§5.2) | **yalnız `false`** |

**Admin neden "kalibre oldu" diyemez:** hizalamanın gerçekten oturduğunu yalnız başlık bilir.
Admin elle işaretleyebilseydi, sunucunun hizalı sandığı ama fiilen kaymış bir oyuncuya ateş ve
hasar açılırdı — bu sistemin önlemek için var olduğu durumun ta kendisi.

⚠️ **Sıfırlama KOŞULSUZDUR: oyuncunun hangi aşamada olduğuna bakılmaz.** Koşulsuzluk iki kipte de
geçerlidir. `clear_calibration` sunucudaki boole'yi `false` yapmakla kalmaz, hedef başlığa **komut
olarak** iletilir (§5.2/§5.3) ve başlık her hâlükârda iki şeyi siler: hizalamayı ve **elle
kalibrasyon sekansının ara durumunu** (alınmış A noktası, beklenen B, çift basış sayacı). Oyuncu
hiçbir ara aşamada bırakılmaz; sekansa baştan başlar.

**Kayıtlı çapanın akıbetini `keepSaved` belirler** (§5.2) ve iki kip arasındaki tek fark budur:

- `keepSaved:true` — *hizalamayı geçersiz kıl*. Gözlükteki `OVRSpatialAnchor` ve UUID **korunur**,
  yani `reload_calibration` (aşağıda) hâlâ okuyacak bir kayıt bulur. Kaydı silmeden "sessizce geri
  alınmamasını" sağlayan şey başlıktaki bir kapıdır: geçersiz kılmadan sonra **otomatik** geri
  yükleme (uygulama açılışı + harita değişimi) kapanır — kayıt cihazda durur ama kendiliğinden
  okunmaz. O kapıyı yalnız operatörün `reload_calibration`'ı ve oyuncunun kendi elle kalibrasyonu
  açar. Kapı olmasaydı sonraki `load_match` bozuk hizalamayı sessizce geri yüklerdi.
  ⚠️ **Bu kapı uygulama ömrü kadar yaşar:** başlık yeniden başlatılırsa süreçle birlikte gider ve
  `saved_anchor` modunda hizalama açılışta geri yüklenir. Bu bir eksiklik değil, iki komut
  arasındaki farkın kendisidir — cihazdaki kaydı gerçekten yok etmek isteyen operatör sert kipi
  kullanır.
- `keepSaved:false` — *cihaz kaydını da sil*. Kayıtlı çapa cihazdan, UUID kalıcı olarak silinir;
  o oyuncuda `reload_calibration` artık başarısız olur ve tek yol elle A/B sekansıdır.

Bunu üç yerde birden **koşulsuz** tutmak zorunludur, çünkü *yarım kalmış kalibrasyon* telde
görünmez — A alınmış ama B alınmamış bir başlık henüz `set_calibration{true}` göndermemiştir, yani
roster'da `calibrated:false` yazar:

1. **Arayüz** komutu göndermeyi "satır kalibresiz görünüyor" diye atlamamalıdır — operatörün
   gördüğü kırmızı satır iki farklı durumu birden gösterir (hiç başlamamış / yarıda kalmış) ve
   komut ikisini de aynı yere götürmelidir.
2. **Sunucu** "değer zaten `false`, değişen bir şey yok" diye erken dönmemelidir. Yalnız `lobby_state`
   yayını değişime bağlı kalır (§5.3 guard'ı: gereksiz roster yayınını önler); komutun **iletilmesi**
   ona bağlanamaz.
3. **İstemci** komutu "yerelde hizalama yoktu" diye yutmamalıdır — silinecek şey tam da hizalama
   olmayan durumda vardır.

Üçünden birini duruma bağlamak kuralı tam da düzeltilmesi gereken durumda işlevsiz bırakır:
operatör basar, hiçbir şey olmaz, oyuncunun yarım sekansı yaşamaya devam eder ve sonraki tek bir
tuş basışı **eski A noktasıyla** kalibrasyonu tamamlayabilir.

**Kalibresiz oyuncunun durumu:**

1. `hit_report`'u **reddedilir** (ateş edemez) — §10.3/2
2. Ona gelen `hit_report` **reddedilir** (hasar yemez) — §10.3/3
3. Atış olayı (`0x03`) **relay edilmez** — §10.3
4. **Canlanamaz** — `revive_request`'i reddedilir ve operatörün `revive_player` komutu da onu
   canlandırmaz (§10.4). Yasak canlandırmanın iki yolunda birden durur: kalibresiz oyuncuyu
   "canlı" yapmak onu savaşa döndürmez, yalnız roster'da yanıltıcı gösterirdi.
5. Maç sayaçları (`hp`/`kills`/`deaths`/`score`) **korunur** — kalibrasyon geri gelince oyuncu
   kaldığı yerden devam eder; bu bir cezalandırma değil, geçici bir dondurmadır.

**İstemci tarafı** (protokolün zorunlu kıldığı değil, beklenen davranış): kalibresizken tetik
kilitlenir, **eldeki silah alınır ve yenisi alınamaz**, uzak avatar **hayalete döner** (yarı
saydam, turuncu nabız) ve vuruş kutuları kapanır, kumandada elle kalibrasyon
(A basılıyken B'ye çift basış) **açılır**. Kalibreli durumdayken bu jest **kilitlidir** — oyuncu
kendi hizalamasını kazara bozamaz,
kapıyı yalnız operatör açar.

⚠️ **Silah kapısı ile tetik kapısı AYNI ŞEY DEĞİLDİR ve ikisi de gerekir.** Tetik kapısı tek
başına bırakılsaydı oyuncunun elinde ateş etmeyen bir silah kalırdı ve bu, sorunu silahta
sanmasına yol açardı. Kapı üç yolu birden kapatmak zorundadır: rastgele dağıtım, çerçeve klonu
(ikisi `WeaponGranter`'ın tek kapısından) ve çerçevenin ISDK aday listesine girmesi
(`WeaponFrame.Filter` — kapı orada olduğu için oyuncu ışını hiç görmez). Kalibrasyon geri
geldiğinde silah **kendiliğinden dönmez**: oyuncu grip'e basınca seçili silahı geri gelir.

⚠️ **Kalibrasyon `false` olduğunda `bodyScale` de sıfırlanır** (§10.8): ölçü zemine göredir.
Sıfırlama iki yoldan da geçer (`clear_calibration` ve başlığın kendi `set_calibration{false}`'u) —
tek yolu kapatmak kuralı işlevsiz bırakırdı.

**`hello`'da `calibrated` sıfırlanır.** Sunucu yeniden bağlanan bir başlığın hizalama durumunu
bilemez (uygulama yeniden başlamış olabilir); başlık kayıtlı anchor'dan geri yükleyince zaten
`set_calibration{source:"anchor"}` ile yeniden bildirir.

⚠️ **Bu yüzden roster'daki `calibrated:false` istemci için bir SIFIRLAMA SİNYALİ DEĞİLDİR.** Alan
her yeniden bağlanışta bir kez `false` yayınlanır — sıradan bir ağ dalgalanmasında bile — ve o yayın
başlığın kendi yeniden bildirimiyle aynı sokette yarışır. İstemci ona bakıp kayıtlı
`OVRSpatialAnchor`'ını silerse bedel **gecikmeli ve sessiz** olur: rig o an taşınmadığı için oturum
düzgün görünür, ama sonraki `load_match`'te geri yüklenecek hizalama kalmaz ve oyuncu herkese
**metrelerce kaymış** çizilir. Üstelik yeniden bildirim sunucuyu "kalibreli" yaptığı için elle
kalibrasyon kapısı da kapalıdır — oyuncu kendi başına düzeltemez. **Hizalamayı silen tek şey
`clear_calibration` komutudur** (§5.2/§5.3); roster istemcide yalnız aynadır.

⚠️ **`load_match` kalibrasyonu SIFIRLAMAZ** (§10.4). Harita değişimi oyuncu için yalnız bir sahne
değişimidir; sunucu `calibrated`'i korur. Yanlışlıkla sıfırlanırsa her harita değişimi tüm
oyuncuları savaş dışı bırakır.

⚠️ **Poz gönderimi kalibrasyona BAĞLI DEĞİLDİR.** Kalibresiz oyuncu `PoseUpdate` göndermeye devam
eder (pozları arena ile örtüşmez ama akar). Bu bilinçlidir: operatörün "avatar kaymış" teşhisini
koyabilmesi ve parlayan avatarın hareket ettiğini görebilmesi için pozun akıyor olması gerekir.

**Kalibre modu (`set_calibration_mode`, §5.2).** Operatör başlıkların **açılışta** nasıl
hizalanacağını seçer; değer sunucuda yaşar ve `welcome.calibrationMode` ile taşınır (§5.3).

⚠️ **Modun kapıladığı TEK şey, uygulama açılışındaki diskten çapa geri yüklemesidir.**
`saved_anchor` bugünkü davranıştır (başlık kayıtlı `OVRSpatialAnchor` UUID'sini okur ve hizalamayı
geri yükler); `two_anchor`'da o UUID **hiç okunmaz**, oyuncu her açılışta elle 2 çapa kalibrasyonu
alır. **HARİTA DEĞİŞİMİNDEKİ geri yükleme moddan bağımsız koşar** (tek istisnası operatörün
geçersiz kılmasıdır: `clear_calibration` sonrasında otomatik geri yükleme kapalıdır, yukarı bak) —
o, oturum içinde
bellekte duran çapayla yapılır ve `load_match`'in kalibrasyonu sıfırlamaması kuralının (yukarıda)
uygulanma biçimidir. İkisini tek anahtara bağlamak, `two_anchor` seçili bir işletmede her harita
değişiminde tüm oyuncuları savaş dışı bırakırdı.

⚠️ **Mod değişimi bağlı oyunculara YAYILMAZ** (§5.3): karar `welcome` anında verilmiştir, sonradan
gönderilen bir bayrağın uygulanacağı bir an yoktur. Sahadaki karşılığı başlığı yeniden
başlatmaktır.

**Zemin sağlığı (`floorOffset`, §5.1).** Elle kalibrasyonda başlık kumanda ucunun tracking-yerel
yüksekliğini bildirir; bu, sistemin zemin tahmininin hatasıdır. `|floorOffset| >
CALIB_FLOOR_WARN_METERS` ise sunucu **`admin_state.notice` ile duyuru basar** ve değer roster'la
taşınıp satırın kalibre etiketini turuncu `KAL ?` yapar. Duyurunun işaret ettiği eylem gözlükte **alan verisi temizliğidir**: kayan
zemin tahmini kalıcıdır, kalibrasyonu tekrarlamak onu düzeltmez.
⚠️ Eşik bir **kapı değil teşhis eşiğidir** — kalibrasyon kabul edilir, oyuncu savaş dışı kalmaz.
Sunucu zemini bilmediği için otomatik bir düzeltme de yapmaz (ikinci bir hizalama otoritesi
olurdu); tek çıktı operatöre giden bilgidir.

**Kayıtlı hizalamayı yeniden yükletme (`reload_calibration`).** Operatör bir oyuncunun (ya da
`playerId:0` ile herkesin) başlığına, gözlükte **kayıtlı çapadan** hizalamayı yeniden yükletir
(§5.2). Sahadaki karşılığı, hizalaması kaymış ya da hiç kurulmamış bir oyuncuyu başlığı yeniden
başlatmadan / oyunu kesmeden toparlamaktır.

| Kim | Ne yapar |
|---|---|
| Operatör | Denemeyi **başlatır** (`reload_calibration`) |
| Başlık | Kayıtlı çapayı yükleyip rig'i hizalamayı **dener ve sonucu bildirir** (`set_calibration`, başarısızsa dolu `error`) |
| Sunucu | Yalnız **iletir**; hesaplamaz, doğrulamaz, sonucu `calibration_result` ile adminlere yayar |

⚠️ **Bu komut "admin `calibrated`'i `true` yapamaz" kuralını ÇİĞNEMEZ:** admin yalnız *denemeyi*
başlatır, "hizalandım" işaretini yine **başlık** koyar (`set_calibration`). Otorite değişmez —
yukarıdaki asimetrik yazar tablosu aynen geçerlidir.

⚠️ **Kayıtlı çapa YOKSA deneme BAŞARISIZDIR ve öyle bildirilir** — hiç kalibre olunmamış ya da
`clear_calibration` **sert kipte** (`keepSaved:false`) UUID'yi silmiş olabilir. Yumuşak kip kaydı
koruduğu için günlük akış *hizalamayı geçersiz kıl → yeniden yükle*'dir ve orada bu başarısızlık
görülmez. Sessizce başarılı sayılmaz: sunucunun hizalı sandığı ama fiilen kaymış bir oyuncuya ateş
ve hasar açmak, bu sistemin önlemek için var olduğu durumdur.

⚠️ **Sonucun kanalı `lobby_state` DEĞİL `calibration_result`'tır** (§5.3): zaten kalibreli bir
oyuncuda başarılı yeniden yükleme roster'da hiçbir alanı değiştirmez (yayın guard'ı), yani
operatörün düğmesi sonsuza kadar "yükleniyor" kalırdı. Başarısızlığın gerekçesi ayrıca roster'da
taşınır (`PlayerInfo.calibrationError`) — o, olayın değil **durumun** kaydıdır.

⚠️ **Kalibre modu bu komutu KAPILAMAZ.** `two_anchor` seçiliyken başlık diskteki çapa UUID'sini
*açılışta* okumaz (yukarıdaki mod maddesi) — ama operatörün isteği bir açılış değildir ve komut
diski moddan bağımsız okur. Kapı burada da uygulansaydı komut `two_anchor` işletmelerinde hiç iş
görmezdi: hizalaması oturum ortasında bozulan oyuncunun tek çıkış yolu elle 2 çapa sekansı olurdu,
oysa düğme tam da onu gereksiz kılmak için vardır. Modun kapıladığı şey **başlığın kendi
başlangıç davranışıdır**, operatörün elindeki kurtarma yolu değil.

**Bulut kalibrasyonu (ileride).** Paylaşılan uzamsal anchor ile toplu hizalama geldiğinde protokol
değişmez: `source:"cloud"` zaten geçerli bir değer, `CALIB_MODE_ANCHOR_CLOUD` zaten rezerve ve
`clear_calibration{playerId:0}` zaten toplu sıfırlama yapıyor. Grup/oturum kimliği taşıyan alanlar
**o iş gelene kadar eklenmez**.

### 10.7 Lobi (tür + sahne + profil)

Lobi bir **türdür** (`modeId:"lobby"`), bir faz değildir ve bir maç değildir. Boş bir bekleme
durumu da değildir: işletmenin kendi lobi sahnesi vardır, oyuncular orada birbirini görür,
**kalibrasyonunu orada yapar**, silah çerçevesinden silah seçip hedeflere ateş eder — birbirlerine
hasar veremeden.

| Soru | Cevap |
|---|---|
| Lobi sahnesi hangisi? | Sunucu söyler: `server.json → lobbyScene`, boşsa mekanın tek lobi haritası (§11). Çözülemezse **sunucu açılmaz** (§11) |
| Faz ne olur? | `paused` + `phaseReason:"lobby"` (§10.1). Lobi diye bir faz YOKTUR |
| Oyuncuya hasar? | **İmkânsız** — `hit_report` yalnız `playing` fazında işlenir (§10.3) |
| Atış görünür mü? | Evet — `rules.fireWhilePaused:true` olduğu için atış olayı relay edilir (§6.5/§10.3) |
| Silah nereden gelir? | **Mod dağıtır** (`weaponSource:"random"`): grip'e basılı tutulan elde loadout'tan rastgele bir silah durur, bırakınca yok olur. Loadout'u istemci `modeId:"lobby"` ile kendi katalogundan çözer. Lobi bilinçli olarak `"weaponcanvas"` değil `"random"` taşır: iki lobi sahnesine elle silah yerleştirme işi doğmasın diye |
| Taban şeritleri görünür mü? | **Seçili mod belirler** (`selection_state.teamMode`, §5.3): takımlı mod seçiliyken (`tdm`/`tournament`) kırmızı/mavi şeritler durur, takımsız mod seçiliyken (`ffa`) gizlenir. Kapı **silah kaynağı DEĞİLDİR** — aktif kural hâlâ lobi profilidir, değişen yalnız sunumdur. Sunucu bu mesajı hiç yollamamışsa istemci aktif kuralın `teamMode`'una düşer |
| Canlanma / skor / süre? | Yok. Herkes canlı (`hp=PLAYER_MAX_HP`), sayaçlar 0 (§5.3) |
| Takım? | Vardır ve **yalnız admin atar** (`set_team`, §5.2) — her fazda, sunucuya bağlı herkes için. Oyuncu kendi takımını seçemez; bunun için protokol mesajı YOKTUR ve eklenmeyecektir |
| Maç başlatılabilir mi? | **Hayır.** `lobby` kayıtlı bir `IGameMode` olmadığı için `start_match` reddedilir (§10.1) |

**Lobi türünün iki kilidi birbirini tamamlar:**

1. **Lobi haritasında yalnız lobi türü oynanır** — `MapDefinition.supportedModeIds = ["lobby"]`,
   yani `start_match` başka bir modu o sahnede kabul etmez.
2. **Lobi türü yalnız lobi haritasında olur** — gerçek arenalar `lobby`'yi listelemediği için lobi
   profili başka haritaya sızamaz.

İkisi de `maps.json`'daki `modes` alanından gelir; **ayrıca bir kural yazılmaz.**

**`modeId:"lobby"` neden var?** İstemcide silah loadout'u, HUD ve (artık) ateş serbestliği `modeId`
üzerinden çözülen kural şeklinden geliyor. Lobi türü `rules.fireWhilePaused:true` +
`rules.weaponSource:"random"` taşır; geri kalan alanlar varsayılandır (§10.5). Böylece **savaşı kapatan şey faz** (`hit_report` yalnız `playing`),
**ateşe izin veren şey mod** olur — ikisi ayrı kapı olduğu için lobi "hasarsız atış alanı" olabilir.

> ⚠️ **Lobi bir maç yapılMAZ.** `playing`'e taşınsaydı hasar kapısı açılır, ayrıca yükleme/geri
> sayım/tur sayacı/`finished` yaşam döngüsü ve `return_to_lobby`'nin kendini çağırması gibi lobide
> karşılığı olmayan bir makine devralınırdı. "Maç koşuyor mu?" sorusunun tek cevabı
> `phase == playing`'dir.

#### Sahneleme — operatörün seçtiği harita herkese açılır

**Sunucunun her zaman bir "açık sahnesi" vardır** ve istemcinin tek yönlendirme kaynağı odur.
Açılışta bu, mekanın lobi haritasıdır — **ortak seçim de (§5.3 `admin_state`) aynı değerle
tohumlanır**, yani sunucu ayakta olduğu sürece "harita seçilmemiş" diye bir durum yoktur.
Operatör admin panelinden başka bir harita seçtiğinde
(`set_selection`, §5.2) sunucu o arenayı **sahneler**:
`return_to_lobby{ modeId:"lobby", sceneName:<seçilen arena> }` TÜM istemcilere gider ve herkes o
sahneyi yükler. Amaç saha akışıdır — oyuncular maç başlamadan arenaya girer, kalibrasyonunu orada
yapar, yerini alır; operatör bunu tek tek anlatmak zorunda kalmaz.

| | |
|---|---|
| Faz | **Değişmez, `paused` kalır** — hasar kapısı (§10.3) kapalı, `set_ready` yok, süre/skor işlemez |
| Ne zaman olur | `set_selection`'ın **`sceneName` alanı dolu geldiğinde** — istenen sahne zaten açıksa hiçbir şey olmaz (idempotent). Süre/limit dokunuşunda alan boş gider, kimse taşınmaz. ⚠️ Ölçüt "seçim değişti mi" DEĞİL "açık sahne bu mu": maç bitip lobiye dönüldüğünde seçim hâlâ o arenayı gösterir, operatör aynı arenayı tekrar seçtiğinde sahnelenebilmelidir |
| Ne zaman serbest | **Yalnız maç KURULMAMIŞKEN**, yani tam iki durumda: `paused` + `phaseReason:"lobby"`, ve `finished` (operatör maç bitince yeni haritayı seçebilsin) |
| Ne zaman OLMAZ | Bunun dışındaki her durumda — `playing`, ayrıca `paused` + `loading`/`countdown`/`operator`/`mode`. Sahne komutu herkese gittiği için kurulmakta olan (yükleme/geri sayım) ya da donmuş (operatör/mod duraklatması) bir maçın altından sahne çekmek maçı bozardı: **donmuş maç da kurulmuş maçtır.** Reddedilen sahneleme `admin_state.notice` ile operatöre yazılır; önce `abort_match` |
| Doğrulama | `start_match` ile aynı (§10.1): sahne harita tablosunda olmalı **ve** tüm çevrimiçi oyuncuların build listesinde bulunmalı. Geçmezse sahneleme yapılmaz, seçim yine kaydedilir ve sebep `admin_state.notice` ile operatöre yazılır |
| Geç katılan | `welcome.match.sceneName` sahnelenen arenadır → doğrudan oraya düşer |
| Maç bitince | `return_to_lobby` normal yolundan gelir ve açık sahne **işletmenin lobi haritasına** döner; sahneleme kalıcı değildir |

> ⚠️ **`modeId` sahnelemede de `"lobby"` kalır.** Seçili maç modunu yazmak maç HUD'unu ve maç
> loadout'unu maç başlamadan açardı; sahnenin arena olması türü değiştirmez. Tür ancak
> `start_match` ile değişir.
>
> **Sonucu:** sahnelenen arena lobi profiliyle koşar (`weaponSource:"random"` + `fireWhilePaused`),
> yani maç kurulana kadar **arenanın silah tezgâhları kullanılabilir kalır** — oyuncu bekleme
> süresince silah alır ve serbest atış yapar. ⚠️ İstemci "mod silah dağıtıyor" durumunu `modeId`'den
> değil bu **bileşimden** ayırır (§10.5): yalnız `random` = mod dağıtıyor (FFA, tezgâhlar gizlenir),
> `random` + `fireWhilePaused` = serbest alan.

### 10.8 Gövde ölçeği (`bodyScale`)

Oyuncular arasındaki boy farkı avatara **tek bir üniform çarpanla** taşınır: `bodyScale`. Değer
oyuncu başına sunucuda durur, `lobby_state` ile yayılır (§5.3) ve her istemci uzak avatarın
karakter kökünün ölçeğine yazar. `0` = ölçülmemiş → `1` uygulanır.

**Kim ne yapar:**

| Taraf | Ne yapar |
|---|---|
| Operatör | `measure_body_scale` ile ölçümü **başlatır** (§5.2); tek oyuncu ya da `playerId:0` ile hepsi |
| Başlık | Ölçer ve `set_body_scale` ile **bildirir** (§5.1) — başarısızlığı da (`error`) |
| Sunucu | Aralığa kırpar, saklar, yayar. **Hesaplamaz** |

**Ölçüm başarısızlığı geri bildirilir.** Başlık ölçemediğinde (kalibrasyon düşmüş, karakter henüz
sürülmemiş, göz hizası okunamadı…) sessiz kalmaz: `set_body_scale`'i `error` dolu, `scale`
önemsiz olarak yollar. Sunucu ölçeği **yazmaz** (kayıtlı değer aynen durur), gerekçeyi roster'a
(`PlayerInfo.scaleError`) yazar ve adminlere duyurur.
⚠️ **Gerekçe doğrulanmayan serbest bir metindir** (`weaponId`/`calibrationSource` ile aynı
sözleşme): sunucuda karşılığı olan bir hata kodu listesi YOKTUR ve eklenmez — tek tüketicisi
operatörün ekranıdır, yeni bir başarısızlık türü sunucuda iş çıkarmamalıdır.
⚠️ **Başarılı ölçüm alanı temizler.** Aksi hâlde bir kez başarısız olan oyuncunun satırında uyarı
sonsuza kadar kalırdı ve operatör sorunun sürdüğünü sanardı.

**Ölçüm zamana değil komuta bağlıdır.** Ölçünün doğru anını (oyuncu ayakta ve dik) makine bilemez;
operatör bilir. Kalibrasyondan otomatik tetiklenen bir ölçüm, oyuncu kumandayı zemine değdirmek
için **eğilmişken** ölçmek demektir.

**Ölçüm iki göz hizasının oranıdır:** oyuncunun gözü (HMD) ile karakterin **o anki** göz hizası aynı
karede okunur, ikisi de arena uzayında. Karakter zaten body tracking'den sürüldüğü için oyuncuyla
aynı pozdadır — yani duruş farkı orandan düşer ve "göz + şu kadar cm = boy" gibi bir tahmine hiç
girilmez. ⚠️ **Kafa tepesi hiçbir yerde kullanılmaz:** modelin kafası oyuncunun gözüne göre
hizalansaydı uzun bir kafa, kısa bir gövde satın alınırdı.

⚠️ **Ölçek iskelet blob'una GİRMEZ.** Meta Movement SDK'nın `Calibrate()`'i gönderenin gövde
ORANLARINI değiştirir; blob `SerializationCompressionType.High` ile eklem uzunlukları üzerinden
sıkıştığı için alıcının hedef iskeleti artık gönderenin kodladığı uzunluklarla uyuşmaz ve uzak
avatar **bozuk duruşlara** girer. Bu yüzden gönderenin iskeleti prefab oranlarında bırakılır
(`Calibrate()` hiç çağrılmaz) ve boy ayrı bir alanda, **bir kez** taşınır.

⚠️ **Yerel karakter ölçeklenmez.** Ölçek yalnız uzak avatarlara uygulanır. Gönderen kendi
karakterini de ölçekleseydi bir sonraki ölçüm zaten ölçeklenmiş bir referansı okur ve çarpanı
`1`'e yaklaştırırdı — düğme ikinci basışta sessizce bozulurdu.

⚠️ **Kalibrasyon sıfırlanınca `bodyScale` de sıfırlanır.** Ölçü arena zeminine göredir; zemin
geçersizse ölçü de geçersizdir. Kapı `clear_calibration` değil **kalibrasyonun `false` olması**dır:
başlığın kendi `set_calibration{false}`'u da aynı sonucu doğurur.

⚠️ **Ölçek karakterin KÖKÜNE yazılır — el pozundan sürülen her şey aynı dönüşümü tekrarlamak
zorundadır.** İskelet kök NOKTASI etrafında büyür/küçülür, yani çizilen el
`kök + ölçek × (ham el − kök)` konumundadır; telden gelen ham el pozu ise yerinde durur. Elde
çizilen eşya ham pozda bırakılırsa gövdeden **kopar** — `1.3` ölçekte silah elin yarım metre
uzağında, havada çizilir. ⚠️ Taşınan **yalnız konumdur**: eşyanın kendisi ölçeklenmez (silah
herkeste gerçek boyundadır) ve avuç → eşya kavrama ofseti metre olarak kalır, yani gerçek boyunda
bir silah büyütülmüş bir elin içinde durur.

### 10.9 İhlal görünürlüğü (`FLAG_IN_OBSTACLE` · `FLAG_OUT_OF_BOUNDS`)

Oyuncunun **fiziksel kural ihlali** iki türdür ve ikisi de tele girer, ama **sonuçları farklıdır**:

| Tür | Bit | Ne olur | Adminde |
|---|---|---|---|
| **Kafa iç engelin içinde** | `FLAG_IN_OBSTACLE` | Karartma + uyarı + titreşim, tolerans sonrası **can erimesi** | Kırmızı **3 Hz** halka |
| **Kafa alanın dışında** | `FLAG_OUT_OF_BOUNDS` | Karartma + uyarı (muhafaza), **ateş kapanır**, **can gitmez** | Turuncu **1.5 Hz** halka |

⚠️ **Alan-dışı bayrağı CEZA ÜRETMEZ ve üretmeyecek.** Gerekçesi dış duvarın `Obstacle` layer'ına
konmama gerekçesinin aynısıdır: dış sınır oyuncunun her an dibindedir, kalibrasyonu birkaç santim
kaymış bir başlıkta sürekli yalancı ihlal doğar ve oyuncu **durduk yere ölür**. Alan dışı bir
**görünürlük** işidir; ceza kararını operatör verir (elinde `kick` ve `revive_player` var).

⚠️ **Halkanın önceliği: engel > alan dışı.** İkisi aynı anda olabilir (alanın dışındaki bir kolonun
içi); halka **can eriteni** çizer.

Arenanın **iç engellerine** (sütun, kasa, sandık, blok) **kafasını** sokan oyuncu ceza alır: ekranı
anında kapkaranlık olur, uyarı yazısı belirir, titreşim başlar; `OBSTACLE_GRACE_SECONDS` sonra
sunucu saniyede `OBSTACLE_DAMAGE_PER_SECOND` can eritmeye başlar ve admin o oyuncunun kuş bakışı
halkasını kırmızı yakıp söndürür.

**Girdiler ve sonuçları — CEZA ile ATEŞ KAPISI ayrı sorulardır:**

| Girdi | Ceza (can) | Karartma + titreşim | Ateş kapısı |
|---|---|---|---|
| **Kafa** engelin içinde | ✅ uyarı yazısı + (tolerans sonrası) can erimesi + `FLAG_IN_OBSTACLE` | ✅ tam siyah + nabız | ✅ tetik ölür |
| **Kafa kabuğu** engele değiyor ama ceza eşiğinin altında | — | ✅ tam siyah + nabız | — |
| **El** engelin içinde | — | — | ✅ tetik ölür |
| **Silahın herhangi bir parçası** engele değiyor | — | — | ✅ tetik ölür |
| **Kafa** alanın dışında | ✅ uyarı yazısı + `FLAG_OUT_OF_BOUNDS`; **can gitmez** | ✅ tam siyah + nabız (muhafazanın kendi karartması) | ✅ tetik ölür |
| Kol / gövde / bacak | — | — | — |

⚠️ **Tablonun üç sütunu üç ayrı kapıdan geçer.** "Ceza" sütunu faz `playing` + canlı + kalibre
ister (sunucuda); "Karartma + titreşim" ve ateş kapısı **hiçbirini istemez** — her harita, her mod,
her faz ve ölü/diri fark etmeksizin çalışırlar.

⚠️ **Ceza yalnız kafayı yargılar, ateş kapısı kafa + eli.** Sebep iki sorunun farklı olmasıdır:
ceza *"görüşüm geometrinin içinde mi"* diye sorar, ateş kapısı *"gövdemi göstermeden mi ateş
ediyorum"* diye. Bloğun içinde durup silahı dışarı uzatan oyuncu ikincisini ihlal ediyor ama silahı
tertemiz bir boşlukta — yalnız silaha bakan bir kapı onu göremez. Bu yüzden ateş kapısı
**oyuncunun kendisinde** durur (`PlayerCombatState.CanFire`), silahta değil.

⚠️ **El ve SİLAH durumu TELE HİÇ GİRMEZ** (ne bayrak, ne `violation` satırı): `FLAG_IN_OBSTACLE`
yalnız kafayı taşır. Sunucuda hiçbir şey değiştirmezler, ama asıl gerekçe operatördedir: o durum
**kaynağında zaten engelleniyor** — tetik işlemez, cephane gitmez, ses/alev oynamaz, ağa atış olayı
gitmez. Müdahale edilecek bir şey yoktur; göstermek yalnız ihlal akışını ve halkayı gürültüye
boğardı.

⚠️ **Ölçülen kütlenin oranını yargılayan bir kural YOKTUR ve eklenmez.** Quest'te alt gövde sensörle
ölçülmez, üst gövdeden ÜRETİLİR; üretilmiş bir uzuv oyuncu siperin *arkasında* dururken siperin
*içinde* çözülebilir — böyle bir kural kaçınılmaz olarak dokunulmamış bir cisimden ceza üretir.
Aynı sebeple ateş kapısı da **yalnız ölçülmüş** parçaları (HMD + izlenen eller) sorar; izlenmeyen
el hiç sorulmaz (rig onun anchor'ını rig orijinine yazıyor, yani kumandası kapanan oyuncu sebepsizce
ateş edemez hâle gelirdi).

⚠️ **Silahın ya da elin engelde olması CAN GÖTÜRMEZ.** Karşılığı tetiğin ölmesidir; en beklenmedik
ölüm "silahım duvara değdi diye öldüm"dür.

**Ceza iki aşamalıdır** ve ikisinin işi farklıdır:

| Aşama | Süre | Ne olur |
|---|---|---|
| Tolerans | `OBSTACLE_GRACE_SECONDS` (3 sn) | Ekran kapkaranlık, can **hiç azalmaz** — oyuncunun çıkacak zamanı var |
| Erime | `OBSTACLE_DRAIN_SECONDS` (5 sn) | Tam candan ölüme lineer erime; kırmızı vinyet karartmanın üstünde nabız atar |

⚠️ **Anti-hile olan şey KARARTMADIR, ceza değil.** Toleransın cömert olabilmesinin sebebi bu: o üç
saniyede oyuncu zaten kördür, engelin içinden kimseyi göremez. Ceza engelin içinde **kamp kurmayı**
engeller. Aynı sebeple tolerans engelden çıkınca **tümden sıfırlanır** — girip çıkan oyuncu her
girişinde yeniden kör kalıyor, yani kazandığı bir şey yok.

⚠️ **Yaralı oyuncu daha çabuk ölür:** erime bir HIZ'dır, sabit bir geri sayım değil. "8 saniye" tam
candaki süredir, bir garanti değil.

**Otorite bölünmesi hasar modelinin aynısıdır (§10.3):** ölçümü istemci yapar, sonucu sunucu yazar.

| Taraf | Ne yapar |
|---|---|
| İstemci | Kafasını ölçer, `0x01`'in `gripFlags` bit5'ini (engel) ve bit7'sini (alan dışı) set eder (20 Hz); ekranı karartır, tetiği kapatır |
| Sunucu | Bitleri `PlayerState`'e alır, **kendi saatiyle** toleransı ve erimeyi işletir (yalnız bit5), `health_update` yayar, ölümü işler; **her iki bit için** ihlal defterini tutar ve adminlere `violation` yayar |
| Admin | Snapshot bitlerini okur, halkayı yakıp söndürür — engel **kırmızı 3 Hz**, alan dışı **turuncu 1.5 Hz**, ikisi birdense kırmızı (yerel çizim); `violation` satırlarını ihlal akışına yazar |

⚠️ **Toleransın saati sunucunundur** (`PlayerState.ObstacleSince`). İstemci "ne zamandır
içerideyim" diye bir süre gönderseydi cezanın başlama anını o belirlerdi; bit yalnız "şu an
içerideyim" der. Saat ölümde, kalibrasyon kaybında ve engelden çıkışta sıfırlanır — ölüp canlanan
oyuncu toleransı dolmuş halde bulmaz.

**Sunucu bunu DOĞRULAMAZ ve doğrulayamaz:** sunucuda arena geometrisi yoktur (`maps.json` yalnız
sahne adı + mod listesi taşır, §11.1). Bu bilinçlidir — hasarı bile istemci hesaplıyor (§10.3,
gözetimli özel alan). Sunucunun işi **sebebi doğrulamak değil sonucu sınırlamaktır**:

1. Süreyi kendi saatiyle ölçer → istemci cezayı hızlandıramaz,
2. `OBSTACLE_FLAG_STALE_MS` dolunca bayrağı düşürür → susmuş/donmuş istemci sonsuz ceza üretmez,
3. Faz/canlılık/kalibrasyon kapılarını kendisi uygular.

**Can eritme kapıları** (hepsi gerekli, sırayla): faz `playing` (`hit_report` ile **aynı** kapı) ·
oyuncu canlı · oyuncu **kalibre** (§10.6) · bayrak taze.

⚠️ **Kalibrasyon kapısı zorunludur:** hizalaması kaymış bir başlıkta sanal engel gerçeğinden
sapar ve tespit yalancı pozitif üretir — oyuncu durduk yere ölürdü. Kalibresiz oyuncu zaten ateş
edemez ve hasar yemez; ceza da aynı kapıya girer.

**`health_update` KISILIR (≈4 Hz), tik kadansında gitmez.** Bu hasar olay tabanlı değil süreklidir:
her tikte yayınlansaydı tek bir engel ölümü yüzlerce WS mesajı üretir ve her biri kurbana **+ her
admine** giderdi. HUD yuvarlanmış tam sayı çizdiği için kaybedilen bilgi yoktur; **`hp = 0` paketi
asla kısılmaz.**

**Ölüm çevreseldir:** can 0'a inince `kill_event` **`killerId = 0`** ve `weaponId = "obstacle"` ile
yayınlanır, `health_update`'in `attackerId`'si `0`'dır. Kurbanın `deaths` sayacı işler,
**`IGameMode.OnKill` ÇAĞRILMAZ ve skor yazılmaz** — öldüren yoktur (takımdaş öldürmedeki kuralın
aynısı, §10.2: olay olur, ödülü olmaz).

⚠️ **Ölümü yazan kod TEKTİR** (`MatchDirector.KillPlayerLocked`) ve vuruş ölümüyle aynı koddur:
`Alive`, `DiedAt`, sayaçlar, `kill_event` ve `respawn` orada yazılır. İki ayrı ölüm gövdesi
yazılırsa (ve bir süre öyleydi) birine eklenen her yeni adım diğerinde sessizce eksik kalır —
"ölmek" tek cümledir.

⚠️ **Bayrak ölümde SIFIRLANMAZ** ve sıfırlanmamalı: oyuncu hâlâ engelin içindedir ve istemci bunu
20 Hz bildirmeye devam eder. Sıfırlansaydı engelin içi **kalıcı bir sığınak** olurdu. Yalnız
**yeniden bağlanmada** temizlenir (bayat bayrak yeni oturuma taşınmasın).

⚠️ **Engelin İÇİNDE canlanma YOKTUR** — oyuncu önce çıkar. Yasak canlandırmanın **her iki yoluna**
da konur (§10.4): bayrağı düşmemiş oyuncunun `revive_request`'i reddedilir ve operatörün
`revive_player` komutu da onu canlandırmaz. Kapı olmasaydı oyuncu engelin içinde tam canla canlanır,
bir sonraki ölüme kadar oradan ateş eder ve döngü hiç bitmezdi; operatör yolunda kapı ayrıca
düğmeyi bir ölüm döngüsü üretecine çevirirdi (canlanan oyuncu saniyeler içinde yeniden ölür).
Erteleme `OBSTACLE_REVIVE_BLOCK_SECONDS` ile **tavanlıdır** (gerekçe §1).

⚠️ **`reviveAnchor:"standstill"` sayacı da engelin içinde ilerlemez** (§10.4) ve bu ayrı bir
kuraldır: yalnız canlanmayı reddetmek yetmez, çünkü sayaç içeride dolarsa oyuncu engelden çıktığı
**anda** canlanır — beklemenin tamamını korunaklı bir yerde geçirmiş olur. Sayaç çıkışta sıfırdan
başlar. Ölçüm istemcidedir (şart zaten sunucuda doğrulanmıyor), yani kural istemci sunumudur.

**Görüş kısıtı ve atış kapısı istemci sunumudur, protokolde karşılığı YOKTUR** — ikisi de sahne
tarafında ölçülür ve sunucuya bildirilmez:

- **Karartma:** kafa kabuğunun **herhangi bir noktası** geometriye değdiği anda ekran **kademesiz**
  olarak tam siyah olur (orada görülecek meşru bir şey yoktur; duvar arkasını görmek tam olarak
  istismarın kendisidir). ⚠️ **FAZDAN, MODDAN, HARİTADAN ve CANLILIKTAN bağımsızdır** ve bu bir
  ihmal değil kuraldır: lobide, yüklemede, geri sayımda, maç sonunda ve oyuncu ölüyken de çalışır.
  Cezanın kapıları (faz `playing` · canlı · kalibre) **yalnız can eritmeye** aittir ve sunucudadır;
  karartma bir ceza değil bir **görüş kısıtıdır** — maç başlamadan duvarın öbür yüzünü okumak da
  aynı istismardır. Ölüyken susturmak ayrıca "engelin içinde canlanma yok" kapısıyla çelişirdi:
  oyuncu neden canlanmadığını göremezdi. ⚠️ **Kapısı ceza eşiği DEĞİL TEMASTIR** ve oraya bağlanmaz: ceza eşiği
  (nokta sayısı + minimum süre) bilerek toleranslıdır ve aynı toleransı görüşe uygulamak, oyuncunun
  kafasını bloğun içine **görecek kadar** sokmasına izin verir. ⚠️ Aynı sebeple ne giriş rampası ne
  de kısmi kararma (**değme bandı**) vardır — ikisi de birkaç kare boyunca yarı saydam bir perde
  çizer, yani duvarın öbür yüzü **okunabilir** kalır. Rampa yalnız **çıkışta** vardır: sınırda gidip
  gelen kafa, ölçüm kadansıyla siyah/açık arasında çırpınır ve VR'da bu bir strobe demektir.
  Kalibrasyon sapmasının bedeli olan ani kararma bilinçli olarak kabul edilir — dış duvar, zemin ve
  tavan zaten `Obstacle` layer'ında değildir.
- **Titreşim:** karartmayla **aynı kapıdan** (temas boyunca, 2 Hz nabız) iki kumandaya birden gider.
  Kararan ekran tek başına *"ne oldu"* sorusunu doğuruyor; nabız ona *"duvardasın, geri çekil"*
  cevabını verir. ⚠️ Sürekli titreşim uyarı olmaktan çıkar, bu yüzden nabızdır.
  ⚠️ **Titreşimi isteyen İKİ kaynak var** (engel ihlali · alan dışı) ve ikisi aynı anda doğru
  olabilir; motoru doğrudan süren yoktur, ikisi de `ControllerHaptics` hakeminden geçer —
  `ScreenFade` ile birebir aynı sözleşme (kare başına bildirim, en yüksek genlik kazanır, susan
  kaynak kendiliğinden düşer).
- **Uyarı yazısı:** karartmanın üstünde nabız atarak "duvarın içindesin, oyun alanına dön" der.
  Karartmanın açıklaması olduğu için **onunla aynı kapıdadır**: faz ve canlılık sorulmaz.
- **Can kaybının kırmızısı:** karartmanın **üstünde** ayrı bir katmandır. ⚠️ Karartma hakemine
  (`ScreenFade`) kaynak olarak eklenemez: "en yüksek alfa kazanır" kuralı siyah 1.0'dayken kırmızıyı
  tümden yutar ve oyuncu canının gittiğini hiç görmez.
- **Atış kapısı** dört testten geçer, **herhangi biri** tetiği öldürür (cephane gitmez, namlu
  alevi/sesi oynamaz, ağa `shot_event` gitmez, atış gecikmesi bile ilerlemez):
  1. **Oyuncunun kendisi:** kafası ya da izlenen bir eli engelin içinde (`CanFire`).
  2. **Silahın gövdesi:** çizilen geometrinin **yönlendirilmiş kutusu** bir engelle kesişiyor.
     ⚠️ Namlu bir NOKTA, silah bir HACİMDİR: tüfeği tuğlanın arkasına geçirip yalnız namlu ucunu
     boşlukta bırakmak nokta testini atlatır.
  3. **Namlu:** ucu bir engelin içinde ya da namlu gövdesi (30 cm geri) bir engelden geçiyor.
  4. **Alan dışı:** oyuncunun kafası muhafazanın güvenli alanının dışında — aynı `CanFire` kapısı,
     `FLAG_OUT_OF_BOUNDS` ile aynı ölçüm.
  Üçüncü test atış ışınında da **ikinci savunma hattı** olarak durur (tetiği olmayan hasar
  kaynakları için): mermi engelde ölür, `hit_report` hiç gönderilmez.

**Neden DURUM taşınıyor, olay değil:** bayrak zaten 20 Hz giden iki pakete biniyor (`0x01` yukarı,
`0x02`/`0x05` aşağı), yani ek bant yoktur. Daha önemlisi **kaybolan bir UDP paketi 50 ms sonra
kendini onarır**: kenar tetikli (`enter`/`exit`) bir bayrakta kaybolan bir "çıktım" oyuncuyu
sonsuza kadar duvarda (ya da adminde turuncu) bırakırdı. Bu kural **her iki bit için de** geçerlidir
ve `violation` mesajı onun alternatifi DEĞİLDİR: o kenar tetiklidir ve yalnız operatörün iş
listesini besler, hiçbir görsel ona bağlanmaz (§5.3).

**İhlal defteri** sunucuda skorla aynı yerde yaşar: oyuncu **ve tür** başına ihlal sayısı ile toplam
süre. `return_to_lobby`'de skorla birlikte sıfırlanır — maç sonu istatistiğinin kapsamı maçtır.
Sayaçları besleyen tek yol `violation` üreten kenarlardır, yani `VIOLATION_MIN_SECONDS`'tan kısa
temaslar deftere de girmez.

**Alan dışındayken ATEŞ EDİLEMEZ.** Kapı `ArenaCombat.CanFire`'dadır, tamamen yereldir ve
**protokolde karşılığı yoktur** — sunucu bunu doğrulamaz, sormaz. Gerekçe: silahı engele sokma yolu
zaten kapalı olduğu için **alanın dışına çıkıp içeri ateş etmek geriye kalan tek fiziksel hile
yoludur**. Ceza değil bir kapıdır: oyuncu içeri girdiği anda tetik geri gelir.

**Arenanın DIŞ duvarları ve zemini CEZA sistemine GİRMEZ.** Dış sınırı istemci tarafındaki muhafaza
ölçer (**tam karartma + nabız + uyarı + tetik kapanması, hasar yok**) ve sonucu yalnız bir bayrak
olarak taşır. ⚠️ **Sunum engel ihlaliyle AYNIDIR, ayrım cezadadır:** ikisi de görüşü kapatır ve
kumandayı titretir, ama can yalnız engelde erir. Sınırı geçen oyuncunun ekranı kademesiz olarak
tam siyah olur (yaklaşma rampası sınıra kadar sürer; dışarıda ikinci bir rampa yoktur).
Gerekçe yine kalibrasyondur: dış duvar oyuncunun her an dibinde olduğu için kayan bir hizalamada
sürekli yalancı ihlal üretirdi — görünürlük buna dayanır, can eritme dayanmaz. Hangi geometrinin
ihlal sayılacağı **sunucuya hiç bildirilmez** — o karar tümüyle sahne tarafındadır (`Obstacle`
layer'ı ve muhafazanın planı, `CLAUDE.md`).

⚠️ **Muhafazası PLANSIZ bir sahnede bayrak hiç yanmaz.** Boyut dosyası bağlı değilse ya da
okunamıyorsa muhafaza kendini kapatır ve "alan dışı" sorusunu cevaplamaz (açık başarısızlık):
ölçüyü bilmeden herkesi alan-dışı ilan etmek sessiz bir yalancı pozitif olurdu.

⚠️ **Kalibresiz oyuncuda alan-dışı anlamsızdır ama bayrak yine gönderilir** — yorum okuyanındır
(`lobby_state.calibrated` ile birlikte okunur). Gönderende susturmak, susmanın sebebini operatörden
gizlerdi.

## 11. Sunucu config dosyaları

`Server/config/` altındaki üç dosya; kaynakları FARKLIDIR:

| Dosya | Kaynağı | Not |
|---|---|---|
| `server.json` | **Elle** | Portlar + `venueName` + `tickHz` + `venue` + `lobbyScene`; yoksa varsayılanlarla oluşturulur (§1 sabitleri). `venue` = açılışta seçilecek mekan (boş = konsolda sorulur). `lobbyScene` = lobi sahnesi (§10.7); **boş = seçilen mekanın lobi haritası otomatik bulunur**. ⚠️ Çözülemezse sunucu **açılmaz** (aşağı). |
| `devices.json` | **Sunucu üretir** | `deviceId → { "name":"ertu", "number":7 }`; ilk bağlantıda ve `set_identity`'de yazılır (§2). Eski v1 biçimi (`deviceId → "ad"`) okunur — numara `0` sayılır — ve ilk yazımda yeni biçime yükseltilir. UTF-8, BOM'suz. |
| `maps.json` | **Unity export** | `MapDefinition` SO'larından: `sceneName`, `venue`, `modes` (§10.1, §11.1). Arena ölçüsü YOKTUR — sunucu metre kullanmaz, ölçü istemcide sahnenin `ArenaBoundary`'sinde kalır. |

> **`weapons.json` YOKTUR:** sunucu silah tanımı tutmaz, hasarı istemci bildirir (§10.3). Silah
> istatistikleri yalnız Unity'deki `WeaponDefinition` SO'larındadır.
>
> ⚠️ **Açık sahne çözülemezse sunucu AÇILMAZ (fail-fast).** `lobbyScene` boş **ve** seçilen mekanda
> `modes == ["lobby"]` olan harita yoksa, ya da verilen `lobbyScene` `maps.json`'da bulunmuyorsa
> sunucu sebebi + düzeltme yolunu yazıp sıfırdan farklı bir çıkış koduyla kapanır. Gerekçe: sunucunun
> açık sahnesi istemcinin **tek** yönlendirme kaynağıdır (§5.3) — çözülemiyorsa zaten bir
> yapılandırma hatası vardır ve oyuncu doğru oynayamaz; sessizce boş sahneyle açılmak hatayı sahaya
> taşır. (`maps.json` hiç yoksa doğrulama atlanır, aşağıdaki maddeye bakın.)
>
> **`maps.json` ELLE DÜZENLENMEZ** — `Tools > VortexArena > Server > Export Server Config` üretir ve bir sonraki export elle yapılan değişikliği **ezer**. Tek doğruluk kaynağı Unity SO'larıdır; çıktı deterministiktir (alfabetik, LF, UTF-8 BOM'suz) → git diff'i temiz kalır. Harita ekleyip export'u çalıştırmayı unutursanız bilinmeyen `sceneName` → `start_match` reddedilir. `maps.json` hiç yoksa sunucu harita doğrulamasını **atlar** (geriye dönük uyumlu davranış).

### 11.1 Mekan seçimi (açılışta)

Bir sunucu kurulumu **tek bir işletmeye** hizmet eder, ama içerik projesi tüm işletmeleri tanır.
Bu yüzden sunucu açılırken **hangi mekanın oynatılacağı seçilir** ve o oturum boyunca sabit kalır.

```
Hangi mekan açılsın?
  1) <Mekan>   (3 harita)
  2) <Mekan2>  (2 harita)
Seçim [1-2]:
```

**Mekan asset yolundan gelir, ayrı bir alan YOKTUR.** Export şu kuralı uygular:
`Assets/Arenas/Venues/<İşletme>/Scenes/<SahneAdı>/…` → o işletme; kutunun klasör adı, sahne dosyası
adı ve `MapDefinition` asset adı (`Data/<SahneAdı>.asset`) üçü de aynıdır, yani sahne adı = katalog
anahtarı klasöre bakınca okunur. Klasör yerleşimi zaten mekanı anlatıyor; ikinci bir
alan eklemek onu unutulabilir hâle getirirdi. Bir haritayı yanlış mekana yazmanın tek yolu onu
yanlış klasöre koymaktır, o da gözle görülür.

⚠️ **Mekan klasörü dışındaki haritalar export'a HİÇ girmez.** `Assets/Arenas/Template/` altındakiler
(referans şablonlar) sessizce atlanır; başka bir yerdeki `MapDefinition` ise uyarı basılarak
atlanır. Sebep: bu listenin her satırı operatörün açılışta seçebileceği gerçek bir işletmedir —
şablonlar ya da yanlış yere konmuş bir harita orada var olmayan bir mekan satırı açardı.

Seçim sırası: `--venue <ad>` → `server.json → venue` → tek mekan varsa o → **konsolda sor**.
Soru yalnız konsol etkileşimliyse sorulur; girdi yönlendirilmişse (servis, betik) sunucu
**bloklanmaz**, ilk mekanla açılır ve bunu loglar. Yazılan ad tanınmazsa yine sorulur — sessizce
başka bir mekanı açmak, operatörün yanlış arenaları görmesi demek olurdu.

⚠️ **Bu "ilk mekan" yolu bir emniyet subabıdır, kullanılacak yol değildir** — hangi mekanın
açıldığı yalnız logda görünür ve sahada kimse logu okumaz. Operatör launcher'ı bu yüzden mekan
seçilmeden sunucuyu **hiç başlatmaz** ve her açılışta `--venue` geçer; betikten/servisten
kaldırılacaksa `server.json → venue` doldurulur.

Seçimin üç sonucu:

| Nereye | Ne olur |
|---|---|
| `start_match` doğrulaması | Harita tablosu o mekana daraltılır → başka mekanın sahnesi "harita tablosunda yok" diye reddedilir. Ayrı bir kontrol yazılmaz |
| Admin harita seçicisi | `admin_state.venueId` + `venueScenes` ile bildirilir; admin **kendi yerel kataloğunu bununla süzer**. Katalog tüm projeyi tanır, oynatılabilir olana sunucu karar verir |
| Lobi | `server.json → lobbyScene` boşsa mekanın kendi lobi haritası (`modes:["lobby"]`) otomatik seçilir (§10.7) |

> ⚠️ **Mekan çalışırken DEĞİŞMEZ.** Başka bir mekana geçmek sunucuyu yeniden başlatmak demektir —
> bilinçli: kalibrasyon, lobi ve harita listesi hep birlikte o fiziksel odaya aittir, maç ortasında
> hepsini birden takas etmenin güvenli bir anlamı yok.
