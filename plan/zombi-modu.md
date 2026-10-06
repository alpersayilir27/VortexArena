# Zombi modu (`zombie`) — yapılacak iş

Kooperatif dalga savunması: oyuncular tek takım, zombiler dalga dalga gelir, yakın dövüşle vurur,
vurulunca ölür. Sistemin dayandığı kurallar: ağ nesnesi modeli `Docs/ArenaNet-Protokol.md` §10.10 ·
mod şekli §10.5 · hasar hattı §10.3 · "sunucu geometri bilmez" `Docs/Sistem-Ozeti.md` §7.

## Karar özeti (yeniden açılmaz)

- **Zombi bir ağ nesnesidir (`kind:"zombie"`), oyuncu değil.** Sunucu doğurur, canını tutar, öldürür,
  skorunu yazar; **pozunu sahibi akıtır** (`0x09`). Oyuncunun zombiyi vurması bugünkü
  `hit_report{targetNetId}` yoludur — sunucuda yeni bir hasar kapısı yazılmaz.
- **Beyin (NavMesh + hedef seçimi + saldırı kararı) Windows admin build'inde koşar.** Sunucu NavMesh,
  collider ya da harita ölçüsü **almaz**: geometri sunucuya taşınırsa ikinci doğruluk kaynağı doğar
  (§7). Sahip = **en eski bağlı admin**; sunucu seçer ve `admin_state.objectHostId` ile duyurur.
- **Sahip düşerse** (kopma **ya da** susma) → kalan en eski admin; admin yoksa → sahiplik `0`, zombiler
  son pozda donar, mod `TryPauseForMode` ile duraklatır, admin gelince sürer. Geri gelen eski admin
  sıranın **sonuna** girer, geri almaz.
- **Tel üzerinde yalnız poz gider** (`0x09` → `0x05`, 12 B/zombi, `OBJECT_POSE_RATE_HZ` = 20 Hz).
  ⚠️ Hız 10 Hz'e indirilmez: enterpolasyon gecikmesi tek örnek aralığına eşitlenir, zombi takılır.
  İskelet yok. Walk/idle **stage değildir** — istemci akan pozun hızından türetir; stage her
  değişimde herkese JSON'dur.
- Yalnız **yakın dövüş**. Seviye = dalga; dalga sayı/hasar/hızı **sunucuda** tanımlar, beyin hızı
  `payload`'dan okur, hasarı **hiç bilmez** (olay sayı taşımaz).
- ⚠️ **Hız güvenlik tavanı:** zombi hızlı yürüyüşün üstüne çıkmaz (aşağıda `Speed` tavanı) —
  free-roam alanda oyuncuyu **koşturan** bir düşman gerçek duvara koşturur.

## Protokol — ÖNCE `Docs/ArenaNet-Protokol.md`, sonra iki uç

- [ ] **§10.5 mod tablosuna `zombie` satırı:** `teamMode:none` · `allies:true` (kooperatif: dost
      ateşi anahtarı ve takım öldürme cezası herkesi tek takım sayar — `BurgerMode` şekli) ·
      `scoring:shared` · `reviveAnchor:standstill` · `weaponSource:weaponcanvas` · `respawnDelay:0` ·
      `fireWhilePaused:false` · varsayılan 300 sn / limitsiz · `gameType:"quickbattle"` (silahlı).
      Spawn koruması 3 sn (canlanma olduğu yerde, zombilerin ortasında olabilir).
- [ ] **§10.5 `zombie` bloğu** (mod sözleşmesi):
  - `kinds[]`: **`zombie`** (`maxHp:100`, `grab:"none"`, `events:[{name:"attack", policy:"owner",
    phaseGate:"playing"}]`) · **`zombie_spawn`** (`maxHp:0`, olay yok — sahnede bake'li doğum
    noktası, görseli yok).
  - `stage`: yalnız `0`. Ölüm `Broken` bayrağıdır, ayrı stage yok.
  - `payload` (`s`): `sp:<doğum noktası netId>;w:<dalga>;a:<saldırı sayacı>` — sunucu her yazışta
    **tam** string'i yazar (anahtar düşmez). `a` her kabul edilen saldırıda artar; istemci saldırı
    animasyonunu bu sayacın **değişiminden** oynatır (tek broadcast, geri-alma yok, nonce gibi).
  - **`object_event attack`**: `i:[hedef playerId]`, `f`/`s` boş — **hasar sayısı taşımaz.**
    Sunucu kapıları (sırayla, düşen sessiz red): sahip mi (politika) · `playing` · zombi `Broken`
    değil · zombinin saldırı bekleme süresi dolmuş · hedef bağlı **oyuncu**, canlı, kalibreli,
    doğma koruması altında değil · zombinin son `0x09` pozu ile hedefin son pozu arasındaki yatay
    uzaklık `ATTACK_REACH_BOUND` altında (**sınır**dır, doğrulama değil — §10.9 mantığı: sunucu
    yalnız mantıksız olanı düşürür). Geçen saldırı: hasar **dalga tablosundan**, `health_update`
    (`attackerId:0`), ölüm çevresel (`killerId:0`, kimsenin `kills`'i artmaz, `OnKill` skor yazmaz).
    Dönüş `true` (relay yok): gerçek `object_state`'tir.
  - `modeState`: `w:<dalga>;left:<ayakta zombi>;next:<sonraki dalgaya sn|0>;over:lost` (son anahtar
    yalnız kayıpta).
  - Skor (`shared`): ortak toplam `scoreRed` = öldürülen zombi; bireysel `PlayerInfo.score` = son
    vuruşu yapanın öldürmesi. Kazanan yok. Bitiş: süre **ya da** tüm bağlı oyuncular aynı anda ölü
    (kayıp).
- [ ] **§10.10 "Sunucu-atanmış sahiplik":** doğuşta `owner` dolu ama `Held` **değil**, `Awake` set;
      sahip `object_rest` **göndermez** (nesne hiç dinlenmez); admin sahibi olduğu nesne için `0x09`
      ve `object_event` gönderebilir; **admin sahipli** nesne sahip düşünce serbest bırakılmaz,
      sıradaki admin'e yazılır (oyuncu sahipli nesnenin bugünkü "serbest bırak" kuralı değişmez).
      ⚠️ `OBJECT_OWNER_SILENCE_MS` bekçisi sahibin **`0x01`** sessizliğine bakar ve admin `0x01`
      göndermez → admin sahibi bu bekçiden **muaftır**, susması `0x09` ile ölçülür
      (`OBJECT_HOST_SILENCE_MS`, aşağıda). Muafiyet yoksa her zombi ilk taramada zemine bırakılır.
- [ ] **§6.8 / §1:** `OBJECT_MAX_ENTRIES_PER_PACKET` 16 → **48** (§1 satırındaki ve `ArenaProtocol`
      yorumundaki "16×34 + 16×12" hesabı birlikte güncellenir). Byte bütçesi: 1200 − 8 − 34×oyuncu −
      9×olay; 10 oyuncuda 48×12 = 576 B sığar. ⚠️ **İki ayrı düşme vardır:** tavanı aşan girdi
      **taşmaz, düşer** (`StateHost.DrainObjectPoses`; `Dictionary` sırası adil değildir — 17. zombi
      açlıktan donar) **ve** toplam `COMBINED_MAX_BYTES`'ı aşarsa o tick'in obje bölümünün **tamamı**
      düşer (birleştirme kapısı) — 16 oyuncu + 48 zombide olaylara 72 B (8 atış) kalır. Tel formatı
      değişmez, istemcinin uzunluk kontrolü sayıdan hesaplanır → **sürüm artmaz.**
- [ ] **§5.3 `admin_state.objectHostId`:** sunucu-atanmış nesne sahipliğini taşıyan admin'in
      `playerId`'si; `0` = yok. Operatör "zombiler neden dondu"yu buradan görür.
- [ ] **§2 Roller:** "admin poz göndermez" cümlesine istisna: sahibi olduğu nesnenin `0x09`'u.
      "Birincil/ikincil admin yoktur" **doğru kalır** — komut yetkisi eşittir; nesne sahipliği için
      sunucunun en eski admin'i seçmesi yetki değil, tek poz kaynağı kuralıdır.

## Sunucu (`Server/VortexArena.Server.Core`)

- [ ] **`Modes/ZombieMode.cs`** — `ModeId:"zombie"`, `Rules` yukarıdaki şekil, `DefaultRoundSeconds
      300`, `DefaultScoreLimit SCORE_LIMIT_UNLIMITED`, `HoldsResultForOperator true`.
  - `OnMatchStart`: `ObjectIdsOfKind("zombie_spawn")` toplanır (boşsa uyarı, zombi çıkmaz —
    `MoleMode` deseni); sahip yoksa hemen duraklat.
  - `OnTick`: dalga makinesi (aşağıdaki tablo) — eşzamanlı tavanın altındayken 2 sn'de bir doğum,
    doğum noktası **rastgele ama son kullanılan hariç**; dalganın son zombisi ölünce `WaveBreak`
    geri sayımı; ölü zombi `DespawnDelay` sonra `DespawnObject`; saldırı bekleme sayaçları.
    ⚠️ `OnTick` geri sayımda ve mod duraklamasında da koşar → makine yalnız `playing`'de ilerler.
  - `OnObjectEvent`: `attack` kapıları (yukarıda) → `director.DamagePlayerFromObject(...)` +
    `SetObjectPayload` (`a` artar).
  - `OnObjectHit`: `broken` ise öldürme sayılır — `AddSharedScore(attackerId, 1)`; despawn sayacı
    başlar.
  - `OnObjectHostChanged(0)` → `TryPauseForMode("Yönetici bağlantısı yok — zombiler bekliyor")`;
    host gelince mod kendi duraklamasını kaldırır (`TryStartRound`, otomatik). ⚠️ Çekirdek bu yolda
    saati iki kez ezer: `TryPauseForMode` **sıfırlar**; `TryStartRound` geri sayımdan sonra
    `roundSeconds`'a kurar, **herkesi canlandırır** ve `OnRoundStart` çağırır → mod kalan süreyi
    kendisi saklar ve iki noktada da `SetTimeRemaining` ile geri yazar (`TournamentMode` deseni);
    `OnRoundStart` dalga makinesini **sıfırlamaz**.
  - `IsMatchOver`: süre ≤ 0 → `Draw`; tüm bağlı oyuncular ölü → `modeState over:lost` + `Draw`.
  - ⚠️ **Mod kendi kilidini tutar:** `OnObjectEvent` / `OnObjectHit` WS bağlantı thread'inde,
    `OnTick` ile **eşzamanlı** koşar — zombi tablosu ve bekleme sayaçları kilitsiz koleksiyonda
    tutulmaz.
- [ ] **`IGameMode`** — iki yeni kanca, ikisi de varsayılan gövdeli:
      `OnObjectHit(director, attackerId, netId, kind, damage, broken)` (bugün `hit_report{netId}`
      yolu moda **hiç haber vermiyor**, `MatchDirector` yalnız broadcast ediyor) ·
      `OnObjectHostChanged(director, hostPlayerId)` (`0` = yok).
- [ ] **`MatchDirector`**
  - `DamagePlayerFromObject(int playerId, float damage, int sourceNetId)`: `hit_report`'un oyuncu
    kapılarının aynısı (faz `playing` · hedef bağlı/oyuncu/canlı · kalibreli · doğma koruması),
    **dost ateşi/takım testi yok** (kaynak nesne, takımı yok), `health_update attackerId:0`, ölümde
    `KillPlayerLocked(..., killer:null, weaponId:"zombie")` — obstacle drain'in yolu.
    `KillPlayerLocked` private'tır ve çağıranın outbox'ını alır → bu yol `_gate` altında
    `_pendingOutbox`'a yazar, `DelayRegenLocked`'ı kendisi çağırır.
  - `SpawnObject` sürücülü doğuş: `owner` dolu + `Awake` set + **`Held` değil** (bugünkü `owner`
    parametresi eli dolu doğurur ve `IsObjectOwner` elde tutulan nesnenin `0x09`'unu reddeder —
    ayrı overload ya da `held:false` bayrağı).
  - `SetObjectOwner(netId, playerId)` — devir, `object_state` yayınlar, `SyncOwnerLocked`.
  - **Nesne sahibi seçimi:** bağlı admin'ler arasında en eski bağlantı (`PlayerState`'e
    `ConnectedAt` — admin kaydı oturumluktur, bağlantı anı = kayıt anı). Değişince
    `admin_state` yayını + `OnObjectHostChanged`. `admin_state` `LobbyService`'te kurulur →
    `objectHostId` director'dan lobiye bir kancayla taşınır.
  - `TickObjectOwnersLocked` kuralı ikiye ayrılır: sahip **oyuncu** → serbest bırak (bugünkü,
    `0x01` sessizliği dahil); sahip **admin** → `0x01` sessizliği **sorulmaz**; gittiyse tüm
    nesneleri yeni sahibe `SetObjectOwner`, yoksa `0` + kanca.
  - **Susma bekçisi:** sahip bağlı ama admin-sahipli ≥1 nesne varken hiçbirinden
    `OBJECT_HOST_SILENCE_MS` (1500) boyunca `0x09` yoksa → sahip "sustu" sayılır, sıranın sonuna
    alınır, devir yapılır. Damga **yeni bir alandır**: `0x09` kabul yolunda, poz kaydının yanında
    kilitsiz yazılan "son admin poz anı" (`StateHost._objectSeq` private ve yalnız recv
    thread'indedir, `_lastObjectPoses` zaman taşımaz).
  - **Gönderen kapısı ayrılır:** `ObjectSenderOkLocked` grab · release · rest · event'in **ortak**
    kapısıdır ve yalnız `player` geçirir. `"admin"` **yalnız `object_event`** için ve yalnız sahibi
    olduğu nesnede geçer — kapı toptan açılırsa admin eşya kavrayabilir. (`0x09` kapısı role
    bakmaz: `IsObjectOwner` = sahip + `Held` değil.)
- [ ] **`Server/README.md`** mod tablosuna `zombie` satırı + "nesne sahibi admin" paragrafı (susma
      bekçisi, devir, duraklatma).

## İstemci (`Assets/`)

- [ ] **`Assets/Modes/Zombie/`** + asmdef `VortexArena.Modes.Zombie` (refs: `VortexArena.Net`,
      `VortexArena.Core`, `VortexArena.Protocol`, `Unity.TextMeshPro`, `Unity.AI.Navigation` — paket
      `com.unity.ai.navigation` kurulu).
- [ ] **`ZombieKinds.cs`** — tür/olay/payload/modeState sabitleri (`MoleKinds` deseni; call-site
      literal sunucuda sessiz red).
- [ ] **`ZombieBrain.cs`** (prefabda, yalnız **`IsMine`** iken çalışır — `NetObjectBody`'nin
      `IsMine` ayrımının aynısı): `NavMeshAgent` (⚠️ sahip olmayan uçta **kapalı** — açık kalırsa
      `ZombieView` ile transform'u çekiştirir) + hedef = en yakın **canlı ve kalibreli** uzak oyuncu
      (poz · canlı · doğma koruması `RemotePlayerRegistry`'den; ⚠️ **kalibre snapshot bayrağı
      değildir**, roster'dan gelir — `RemoteAvatar.IsTargetable` hepsini tek kapıda toplar; admin'in
      yerel gövdesi yok) · hız `payload w`'den dalga tablosuyla · `stoppingDistance` = erişim − pay
      (oyuncunun gerçek bedeninin **içinden geçmez**) · erişimdeyken saldırı animasyonunun **vuruş
      karesinde** `object_event attack i:[hedef]` · `UdpStateChannel.SendObjectPose` ile
      `OBJECT_POSE_RATE_HZ` hızında `0x09` ·
      ⚠️ **`NetObjectPoseSender` KONMAZ** — 0,3 sn duran nesneye `object_rest` yollar, sunucu
      sahipliği bitirir, zombi öksüz kalır · `Broken` → agent durur · `IsMine` maç ortasında
      kazanılırsa (devir, `NetObject.OwnerChanged`) transform'un **o anki** pozundan `Warp`
      (`TryGetInterpolatedPose` sahip olunan nesnede `false` döner), akışa devam.
- [ ] **`ZombieView.cs`**: akan pozu kendisi uygular (`RemoteObjects.TryGetInterpolatedPose`;
      `NetObjectBody` yok — o dinlenme pozuna oturtur); akış 1 sn kesilince kayıt düşer → **son
      transform'da kalır** ("son pozda donma" budur) · `Animator`: idle/walk **hızdan**,
      attack `payload a` **değişiminden** (yalnız `NetStateOrigin.Live`), ölüm `IsBroken` ·
      ⚠️ doğuş animasyonu köken bayrağından **anlaşılamaz**: canlı doğuş da geç katılım da
      `Snapshot` olarak uygulanır → ayrı işaret gerekir (Karar bekleyenler) · `IsBroken` olunca hit
      collider'lar kapanır · bölge collider'ları **trigger değil ve `Default` layer'da** (mermi ve
      patlama trigger'ı yok sayar; `Obstacle` layer'ı içine giren oyuncunun kafasına engel ihlali
      yazar) · bölge etiketi `RemoteHitBox` ile (⚠️ `RemoteAvatar` bulamayan kutu `Awake`'te uyarı
      loglar — nesne üstünde sessiz çalışacak yol açılır; kafa çarpanı silahın
      `headshotMultiplier`'ıdır, sabit değil; `ArenaCombat.TryGetTargetNetId` nesneyi kendisi bulur).
- [ ] **`ZombieClientController : ModeHudBase`**: dalga, ayakta zombi, sonraki dalga geri sayımı,
      kendi öldürmesi / ortak toplam (`Burger` HUD deseni), kayıp/süre sonu ekranı.
- [ ] **`ZombieSpawnPoint`**: sahne nesnesi — `NetObject` + tür `zombie_spawn`, görseli editor
      gizmo'su; `ZombieBrain` `sp` netId'sini `NetObjectRegistry`'den çözüp orada doğar.
- [ ] **İçerik:** `NetObjectKind` asset ×2 · prototip prefab `NO_Zombie` (kapsül; `Model` alt kökü
      gerçek modele ayrılır — köstebek reçetesi) · `NetSpawnCatalog` satırı · `ModeDefinition` SO
      "Zombi" (`modId:zombie`, `allies` açık, `scoreLimit 0`, önizleme kuralları) ·
      `GameCatalog.modes[]` satırı · seçilen arenaların `MapDefinition.supportedModeIds`'ine `zombie`
      (⚠️ boş liste = kısıt yok: NavMesh'siz arena da modu sunar) · **arena sahnesine
      `NavMeshSurface` + bake** (işletme sahnesi ayrı kopyadır → her işletme sahnesi ayrı bake) ·
      doğum noktaları (duvardan ≥ 2 m; ⚠️ taban ölçütü yok — `teamMode:none`'da `BaseZone` gizlidir,
      oyuncu her yerde durabilir) · sahne kaydet → **Export Server Config** → sunucu yeniden
      başlatılır.
- [ ] **Bake kaynağı bilinçli seçilir:** çevre sanatı fiziksel düzlemin dışına taşar — kaynak
      kısıtlanmazsa zombi oyuncunun gidemeyeceği dekorun üstünde dolaşır · `ArenaRoof` ve üst kat
      döşemeleri kaynak dışı · kaynak collider'dır, birleştirilmiş render mesh değil · fiziksel
      kolonlar delik bırakır · ajan tipi zombiye özeldir (varsayılan 0,5 m yarıçap dar geçitte tıkar) ·
      kırılabilir siper `NavMeshObstacle` (carve) taşır, `Broken` olunca kapanır.
- [ ] **Admin build:** `Run In Background` açık (mevcut) — kapanırsa alt-tab'da beyin durur, bekçi
      devreder ya da maç duraklar; `Yapma-Listesi`'ne girer.

## Denge — ilk değerler (playtest ile oynanır, gerekçe yanında)

Referans: `PLAYER_MAX_HP 100`; tüfek gövde 26-36 hasar / 600-860 rpm, kafa ×4; pompalı yakından
tek fişek.

| Parametre | Değer | Gerekçe |
|---|---|---|
| Zombi `maxHp` | 100 | Tüfek gövdeden 3-4 mermi, kafadan 1; pompalı yakından 1 fişek. Zombi tek başına ucuz ölür — baskı **sayıdan** gelir |
| Saldırı erişimi | 1,2 m yatay | Kol boyu + pay. ⚠️ `ProximityWarning` halkası zombide **belirmez** (yalnız uzak oyuncuları okur) — yaklaşma uyarısı ses ve saldırı öncesi animasyondur |
| `ATTACK_REACH_BOUND` (sunucu) | 2,5 m | Sınır, doğrulama değil — gecikme + poz aralığı payı |
| Saldırı bekleme | 1,5 sn | Vuruş karesi animasyonun ~0,4. sn'sinde |
| Hasar / dalga | `12 + 4×(dalga−1)`, tavan 32 | 1. dalga 9 vuruş (~13 sn temas), 6. dalga 4 vuruş |
| Eşzamanlı zombi | `3 + dalga`, tavan 12 | Paket tavanının altında; 12 zombi 20 Hz = istemci başına 2,9 KB/s iniş |
| Dalga toplamı | `4 × dalga` | Doğum 2 sn'de bir, tavanın altındayken |
| Hız | `0,7 + 0,1×(dalga−1)`, **tavan 1,2 m/s** | ⚠️ Güvenlik: hızlı yürüyüş ~1,4 m/s; oyuncu geri adımla kurtulur, koşmaz |
| Dalga arası | 8 sn | `modeState next` HUD'da sayar |
| Canlanma | `standstill` 5 sn + 3 sn koruma | Olduğu yerde; koruma zombinin ortasında canlanmayı kurtarır |
| Ölü zombi despawn | 3 sn | Ölüm animasyonu süresi |
| Kayıp | tüm bağlı oyuncular aynı anda ölü | Tek fail state; süre dolarsa "dayanıldı" |

## Doküman işleri (aynı commit'te)

- `Docs/ArenaNet-Protokol.md` — yukarıdaki beş madde.
- `Docs/Sistem-Ozeti.md` §4: `Modes/ZombieMode` + `VortexArena.Modes.Zombie` kutusu; nesne sahibi
  admin akışı §3; §7 Tuzaklar: "sürücülü nesneye `NetObjectPoseSender` koyma" · "admin build
  `Run In Background`" · "16 üstü obje pozu düşer".
- `Docs/Gelistirici/Yemek-Kitabi.md`: "Yeni arena eklemek"e **NavMesh bake** adımı (yalnız `zombie`
  taşıyan arenalar) · yeni reçete "Sunucu-sürülen ağ nesnesi (beyin sahipte)".
- `Docs/Isletme-Kurulum.md` ve `TemplateBasicsLoader` ipucu metni: koşulsuz "NavMesh bake et"
  cümlesi aynı kurala çekilir (yalnız `zombie` taşıyan arena).
- `Docs/Gelistirici/Yapma-Listesi.md`: sürücülü nesnede `NetObjectPoseSender`/`NetObjectBody` yasağı ·
  zombi hasarını istemcide hesaplama yasağı (olay sayı taşımaz) · zombi collider'ını `Obstacle`
  layer'ına koyma yasağı.
- `Server/README.md` mod tablosu.

## Doğrulama (kullanıcı koşar)

- [ ] Admin yokken maç başlatılınca mod anında duraklar, admin bağlanınca dalga başlar.
- [ ] İki admin: zombiler **ilk** bağlananda yürür; o kapanınca ≤ 2 sn içinde ikincide yürümeye
      devam eder (donma görünür, ışınlanma yok); ilk admin geri gelince sahiplik **değişmez**.
- [ ] Sahip admin'in Unity'si dondurulunca (debugger pause) 1,5 sn'de devir olur; tek admin ise maç
      duraklar ve `admin_state.objectHostId` `0` gösterir.
- [ ] Admin dönünce maç **kaldığı süreden** sürer; dalga sayısı ve ayaktaki zombiler değişmez.
- [ ] İki başlıkta zombi aynı yerde, aynı anda saldırır; hasar yalnız hedefte düşer; kafa vuruşu tek
      mermide öldürür; öldürme ortak toplama ve son vurana yazılır.
- [ ] Zombi ölünce her başlıkta ölüm animasyonu oynar, collider'ı kapanır, 3 sn sonra kaybolur;
      geç katılan başlık ayaktaki zombileri doğru yerde görür, ölüm/saldırı animasyonunu **baştan
      oynamaz**.
- [ ] Kalibresiz ya da koruma altındaki oyuncuya saldırı hasar yazmaz; oyuncu kalibre olunca yazar.
- [ ] 12 zombi ayaktayken hiçbiri donmaz (paket tavanı); admin konsolunda `rxRejected` artmaz.
- [ ] Tüm oyuncular ölünce maç "kayıp" ile biter; süre dolunca dalga sayısı ve ortak toplam sonuç
      ekranında kalır (operatör kapatana kadar).
- [ ] Zombi hiçbir zaman oyuncunun gerçek bedeninin içinden geçmez; 1,2 m/s tavanında oyuncu geri
      adımla uzaklaşabiliyor.
- [ ] Kafası zombinin içine giren oyuncunun ekranı kararmaz (engel ihlali yazılmaz).
- [ ] NavMesh'i olmayan arenada mod seçilirse zombiler doğar ama yürümez → konsolda tek satır uyarı
      (bake eksik).

## Karar bekleyenler (yazmadan önce)

- [ ] **Doğum noktasını kim seçer?** Sunucu noktaların yerini bilmez (`maps.json` nesne satırı
      yalnız `sceneId` + `kind` taşır) → "oyunculardan uzak nokta" sunucuda seçilemez. (a) Sunucu
      rastgele seçer, `sp` taşır (yukarıdaki şekil) · (b) beyin canlı oyunculara en uzak noktayı
      seçer: `zombie_spawn` türü ve `sp` anahtarı kalkar, nokta düz sahne işaretçisi olur, görünüm
      ilk poz gelene kadar gizlenir.
- [ ] **Zombi nereden gelir?** Yalnız fiziksel düzlemin içinde doğup yürür (mevcut arenalar
      kullanılır; küçük odada atış mesafesi birkaç saniyedir) · ya da düzlemin dışındaki tasarlanmış
      koridorlardan girer (atış mesafesi ve "şuradan geliyorlar" okunurluğu kazandırır; moda özel
      arena sanatı ister).
- [ ] **Paket tavanı şimdi mi?** Eşzamanlı tavan 12 iken 16 yeter; 48'e çıkarma ve adil sıra, sayı
      16'yı aşacağı zaman da yapılabilir.
- [ ] **Aradaki oyuncu:** başka hedefe yürüyen zombi yolundaki oyuncunun bedeninden geçer — admin'de
      her canlı oyuncuya carve'sız `NavMeshObstacle` eklenir mi?
- [ ] **Doğuş animasyonunun işareti:** payload'a doğuş anı (`t:<kalan sn>`) · ya da doğuş animasyonu
      yok (zombi görüş dışında doğar).
- [ ] **Build Readiness satırı:** `supportedModeIds`'i `zombie` içeren ya da boş olan arenada
      NavMesh verisi yoksa uyarı.

## Açık kalanlar (playtest kararı)

- [ ] Hasar/hız/eşzamanlı sayı eğrileri — tablo başlangıçtır.
- [ ] Zombinin oyuncuyu "görmesi": bugün en yakın oyuncu, görüş hattı yok. Kolonlu arenada duvar
      arkasından kilitlenme rahatsız ederse beyne `NavMesh.Raycast` eklenir (admin'de ucuz).
- [ ] Ses: doğuş, yürüyüş, saldırı, ölüm kancaları `ZombieView`'da; klipler içerik işi.
- [ ] Gerçek model/animasyon seti (idle · walk · attack · death · rise).
