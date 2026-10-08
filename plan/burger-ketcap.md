# Burger — asılı ketçap şişesi (sıkma, kirletme, sos) — plan

Burger modunda tavandan yaylı kordonla sarkan ketçap şişesi. Oyuncu şişeyi tutar, **işaret
tetiğini** sıkınca kalın bir ketçap akışı yay çizerek fışkırır: tezgâha, zemine, oyunculara ve
burgerlere sıçrar. Burger yığınına sıkılan ketçap **sos katmanı** sayılır; duvardaki sos
dağıtıcısı (`NO_dispenser_sauce`) sahneden kalkar. Mayonez/hardal yoktur.

## 0. Değişmez kurallar

- **Sıra: doküman → kod.** Önce `Docs/ArenaNet-Protokol.md` Burger bölümüne (kinds, olaylar,
  sunucu kuralı) yazılır, sonra istemci (`Assets/Modes/Burger/`) ve sunucu
  (`Server/.../BurgerMode.cs`) ona uydurulur.
- **Tel formatı değişmez:** yeni kind ve olay adları veridir (`object_event`, `maps.json kinds[]`).
  `PROTOCOL_VERSION` **artmaz**. Yine de istemci kodu yenidir: tüm gözlüklere yeni APK + admin +
  sunucu aynı turda dağıtılır; eski APK şişeyi doğuramaz (katalogda yok, tek log satırı).
- **İstemci nesne doğurmaz** (`Yapma-Listesi.md`): sos katmanını sunucu doğurur.
- **Hasar, puan, ölüm yok.** Oyuncuya isabet yalnız görseldir (çocuk modu, `Weapons = None`).
- Quest: efekt materyalleri Unlit/LDR, gerçek zamanlı ışık yok, havuzlu efektte
  `Stop Action = None`, döngüsel ses taşınan objede değil (`Yapma-Listesi.md` efekt maddeleri).
- `maps.json` elle yazılmaz: kind asset'i + `Export Server Config`.

## 1. Nesne: `ketchup_bottle`

| Alan | Değer |
|---|---|
| Kind | `ketchup_bottle` (`KIND_ketchup_bottle.asset`), grab **anyone**, olaylar `squeeze` (policy owner), `squirt` (policy owner) |
| Prefab | `NO_ketchup_bottle`: `NetIdentity`, `NetObject`, `NetObjectBody`, `NetObjectPoseSender`, `NetObjectGrabBridge`, `GripSocket`, yeni `BurgerKetchupBottle` |
| Sahnede | 2 adet, her çalışma adasının üstünde; asılma noktası `HangAnchor_N` (y 1,85, burun aşağı), kordon ucu `CordAnchor_N` (makaranın altı) |
| Görsel | `K1_KetchupBottle` (kırmızı şişe + sarı kapak, 0,43 m), tavanda `K2_KetchupReel`; sos katmanı `K3_KetchupPuddle` (`NO_sauce` içinde `CartoonVisual`: ince havuz + sıkılmış zikzak çizgi + alt katmanın kenarından sarkan damlalar, parlak `M_BK_Ketchup`; palet kırmızısı domatesle aynı olduğu için kullanılmaz) |

**Kordon ve geri dönüş (yalnız sahibin fiziği):**
- Bırakılan şişe, sahibi olan gözlükte asılma noktasına yay + sönümle döner (salınarak), durunca
  bugünkü `object_rest` yolu çalışır. Mevcut yol: sahip fiziği koşar, pozu `0x09` ile yayınlar
  (`NetObjectBody`, `NetObjectPoseSender`). Öneri: sahipte `SpringJoint` (asılma noktasına bağlı
  kinematik gövde) ya da elle sönümlü yay; tutulurken eklem devre dışı.
- Kordon her istemcide `LineRenderer`'dır (makara ↔ şişe boğazı), fizik değildir. Kordon uzunluğu
  sınırsız esner (VR'da el durdurulamaz); görsel olarak gerilir.
- Boştayken (sahipsiz) şişe asılma noktasında durur; sahnedeki ev pozu ile aynı.
- Bırakılan şişe yay ile dönerken açısı da hemen asılma açısına döner (`uprightSpeed`, 720°/sn);
  elin son açısında kalmaz.
- **Kordon sınırı:** tutan gözlükte şişe asılma noktasından `leashRadius` (3 m) uzaklaşınca
  `NetObjectGrabBridge.ForceRelease()` ile elden düşer ve yay onu yerine çeker.

## 2. Sıkma ve akış

- **Girdi:** şişeyi tutan elin **işaret tetiği**, histerezisli: basma > 0,55, bırakma < 0,35
  (`Weapon.TickTrigger` deseni; `ArenaCombat.CanFire` Burger'de false olduğu için **kopyalanır**,
  çağrılmaz). Eli `NetObjectGrabBridge.GrabbedLocally/ReleasedLocally` söyler.
- **Durum yayını:** sahip sıkmaya başlayınca `object_event {name:"squeeze", f:[1]}`, bırakınca
  `f:[0]` (WS, güvenilir). Sunucu doğrular (sahip + oynanıyor) ve **röle eder**
  (`OnObjectEvent` false). Gerekçe: bu bir durum geçişidir, sık atılan kozmetik olay değil;
  kayıp, akışın karşı tarafta hiç durmaması demektir. Şişe bırakılınca ya da sahip düşünce
  her istemci akışı kendisi kapatır.
- **Akış simülasyonu (her istemcide, yerel):** sıkılırken şişe ucundan saniyede ~25 "damla"
  çıkar; çıkış hızı ~4 m/s şişe ekseni boyunca, yerçekimli yay. Damla görseli havuzlu kalın
  kırmızı parçacık/şerit (Unlit). Damlalar `Physics.SphereCast`/adım adım raycast ile çarpar.
  Herkes aynı şişe pozunu (sahibin el pozu) gördüğü için sonuç yaklaşık aynıdır; kozmetik fark
  kabul edilir.
- **Görünüm:** akış damla damla değil, nozülden havadaki damlaların konumlarından geçen tek parça
  kalın parlak kırmızı şerittir (`LineRenderer`, Unlit); damlalar görünmez, çarpma mantığını
  taşır.
- **Lekeler:** çarpılan yüzeye havuzlu düz leke quad'ı (Unlit, alfa kesmeli, birkaç rastgele
  doku). Tavan **300** leke; dolunca en eskisi yeniden kullanılır. Lekeler 4 s tam görünür, 1 s'de
  solar (toplam 5 s), maç sonunda/yeni maçta temizlenir. `DecalProjector` kullanılmaz (projede yok, Quest'te pahalı).
- Ses: sıkma başlangıç/bitiş sesi şişede tek seferlik (`SqueezeStartAudio`/`SqueezeStopAudio`);
  çarpma sesi akışın 3 kaynaklı havuzundan, çarpma noktasında, en sık 0,12 sn'de bir; döngü yok.

## 3. Oyuncuya isabet (yalnız görsel)

- **Kendi görüşü:** her istemci yalnız **kendi** oyuncusunu değerlendirir (flashbang emsali,
  `Yemek-Kitabi.md` §11.3): damla başın ~0,2 m çevresine girerse görüşün **kenarlarına** ketçap
  lekesi gelir, **ortası açık kalır**, ~3 s'de solar. `DamageVignette` deseni (Overlay kuyruğu,
  ZTest Always); `ScreenFade` kaynağı olarak kaydedilmez. Katmanı ilk şişe kameranın altına
  örnekler (HUD prefabı arayüz kurucusundan yeniden üretildiği için oraya konmaz).
- **Avatar:** damla bir uzak avatarın `RemoteHitBox` collider'ına çarparsa leke o kemiğe
  çocuk olarak yapışır (aynı havuz ve tavan). Her istemci kendi simülasyonuyla karar verir.

## 4. Burgere sos

- Sahip istemci, akış bir burger yığınına (servis tahtası ya da kesme tahtasının `Stack`
  hacmi) **0,4 s kesintisiz** çarparsa `object_event {name:"squirt", i:[boardNetId], f:[x,y,z]}`
  gönderir (konum = yığının üstü, arena uzayı).
- **Sunucu (`BurgerMode.OnObjectEvent`):** gönderen şişenin sahibi mi, oynanıyor mu, hedef
  `board`/`cutting_board` mi, aynı şişeden son `squirt` üzerinden **1,5 s** geçti mi → geçtiyse
  `sauce` nesnesini verilen konuma doğurur, **sahibi = sıkan oyuncu**, tutulmuyor. Böylece
  katmanı sıkanın gözlüğü dinlendirir ve yığın onu bugünkü yolla oturtur
  (`BurgerStackColumn`, yalnız bu gözlüğün dinlendirdiği katmanı oturtur). `squirt` röle
  edilmez (`true`).
- Sipariş kuralı değişmez: sos isteğe bağlı bir katmandır, fazlası siparişi reddeder.
- `NO_dispenser_sauce` sahneden kaldırılır (ketçap şişesi görseli `E2_Ketchup` dahil); sunucu
  whitelist'i ve kind'ı silinmez (başka arenalar kullanıyor olabilir).

## 5. Durum ve kalan iş

Kod, protokol dokümanı ve Unity verisi yazıldı; `maps.json` dışa aktarıldı. Sahnede iki şişe
(`BurgerStation/Props/NO_ketchup_bottle (1..2)`), makaralar ve asılma noktaları
(`Kitchen_Props/KetchupReels`), modeller `blender/BurgerKitchen/scripts/k_ketchup.py`. Kalan:

1. Kavrama pozu: `ITEM_BurgerKetchupBottle` bıçağın kavrama pozunu kopyalar; şişe kalın olduğu
   için el pozu sahada ayarlanabilir.
2. Sunucu derlemesi + yeni APK/admin/sunucu dağıtımı ve §6 doğrulaması (kullanıcı).

## 6. Doğrulama (Quest, kullanıcı)

Şişeyi tutup sıkma; akışın diğer gözlükte görünmesi; lekelerin tezgâh/zemin/avatarda kalması;
görüş kenarı lekesi; burgere sıkınca sos katmanı ve siparişin kabulü; bırakınca şişenin
salınarak yerine dönmesi.
