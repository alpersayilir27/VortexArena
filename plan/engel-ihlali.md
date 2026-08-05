# Engel ihlali (oyuncu iç engelin İÇİNE girince ceza)

**Hedef:** arenanın iç engellerine (sütun, kasa, sandık, blok) gömülen oyuncu tespit edilsin;
oyuncunun gözünde karartma + VFX, sunucuda **saniyede 30 HP** can kaybı (tam can 100 → 4. saniye
içinde ölüm), admin kuş bakışında kırmızı yanıp sönen halka.

**Tetik üç koşuldan HERHANGİ BİRİ** (VEYA — biri yeterli):
1. Gövdenin **%30'u** engelin içinde,
2. **Kafa tamamen** içeride,
3. Elde tutulan **silah tamamen** içeride.

⚠️ **Arenanın DIŞ duvarları ve zemini bu sisteme GİRMEZ.** Dış sınırı `ArenaBoundary` yönetir
(karartma + uyarı, hasar yok) ve öyle kalır. Gerekçe kalibrasyondur: hizalaması kaymış bir başlıkta
sanal duvar gerçeğinden metrelerce sapabilir ve oyuncu durduk yere ölürdü. İç engelde aynı risk
daha küçüktür (hacim küçük, oyuncu ona bilerek yaklaşır) ve kalibresiz oyuncuya zaten ceza
verilmez.

---

## 1. Sözleşme: layer

**`Obstacle` layer'ı "bunun içinde olmak ihlaldir" demektir.** İkinci bir hacim, ikinci bir
collider, ayrı bir kayıt sistemi YAZILMAZ — Unity'nin layer + collider sistemi bu iş için zaten
doğru araçtır ve ileriki isabet-VFX / kırılabilir obje işleri de aynı tablodan beslenecektir.

| Layer | Ne | Mermi çarpar | İhlal |
|---|---|---|---|
| 0 `Default` | Zemin, dış duvarlar, dekor, sanat | ✅ | ❌ |
| 8 `ArenaRoof` | Çatı (mevcut, admin kuş bakışında gizlenir) | ✅ | ❌ |
| **10 `Obstacle`** | **İç engeller** — sütun, kasa, sandık, blok | ✅ | ✅ |
| 11 `Breakable` | Kırılabilir obje — `agsal-kirilabilir-objeler.md` işi | ✅ | ✅ |
| 12 `PlayerHitbox` | `RemoteHitBox` collider'ları (bugün 0'da) | ✅ | — |

- **Bu iş yalnız 10'u açar.** 11 ve 12 burada **rezerve** edilir ki sonraki işler indeksleri
  kaydırmak zorunda kalmasın; bileşen taşıma o işlerin kapsamıdır.
- ⚠️ Layer 9 `LocalBody` tanımlı ama projede hiçbir şey kullanmıyor — bu iş ona dokunmaz.

### `Weapon`'a DOKUNULMAZ

Bu özellik atış hattında hiçbir değişiklik gerektirmez: engel zaten mermiyle vurulabilir bir
objedir ve `Weapon.Fire`'ın maskesiz raycast'i onu bugün de vuruyor. İleride isabet VFX'i (kan /
duvar / kırılma) yazılırken `Weapon`'a açık bir `ShootableMask` gelecektir — **o iş bu işi
beklemez, bu iş de onu beklemez.**

### ⚠️ Tek zorunlu kural: `Obstacle` collider'ı KONVEKS olmalı

Kabul edilen: `BoxCollider` · `SphereCollider` · `CapsuleCollider` · `MeshCollider` + `Convex ✓`.

**Neden hayati:** nokta-içeride testi `Collider.ClosestPoint` ile yapılıyor ve bu metot **non-convex
`MeshCollider`'da girdi noktasını AYNEN döndürür** → test her noktayı "içeride" okur → o sahnedeki
**herkes anında ölmeye başlar.** Belirtisi teşhisinden çok uzak bir hatadır, bu yüzden iki yerden
bekçilenir:

1. `Tools > VortexArena > Arena > Engel Hacimlerini Denetle` — sahneyi tarar, `Obstacle`
   layer'ındaki her non-convex mesh'i (ve collider'sız objeyi) tek listede raporlar.
2. Çalışma anında: aday collider ilk görüldüğünde tipi doğrulanır, düşen collider **kalıcı olarak
   yok sayılır** ve bir kez hata basılır. Açık başarısızlık — kimseyi öldürmez.

**İstisna (nadir):** gerçekten konkav olması gereken obje (parmaklık, kafes). Sanat collider'ı
`Default`'ta bırakılır, yanına kaba bir konveks kutu `Obstacle`'a konur. O kutu mermiyi de
durduracağı için yalnız gerçekten gerektiğinde yapılır; çoğu objede konveks yaklaşım hem mermi hem
ihlal için yeterlidir.

### `ObstacleVolume` bileşeni — İSTEĞE BAĞLI

Sözleşme layer'dır; bileşen yalnız **istisna** içindir ve yoksa varsayılan davranış işler:
- `damageScale` (varsayılan 1) — o objede ceza hızını değiştirmek,
- `bodyRule` / `headRule` / `weaponRule` (varsayılan üçü de açık) — ör. alçak bir bariyerde yalnız
  kafa kuralının işlemesi.

⚠️ Bileşen **collider EKLEMEZ ve layer DAMGALAMAZ** — ikisi de yazımcının kararıdır, bileşen
yalnız o kararı ince ayarlar. (`ArenaObstacle` ile karıştırma: o 2B'dir, collider'ı yoktur ve
muhafazanın "buraya yürüme" uyarısını besler. İki bileşen aynı objede yan yana durabilir.)

---

## 2. Tespit — neden nokta örneklemesi

| Yol | Neden olmadı / oldu |
|---|---|
| Trigger collider + `OnTriggerEnter` | ❌ İki sebeple düşer: (1) **yerel gövdeye collider konamaz** — `Weapon.Fire` maskesiz raycast atıyor ve `m_QueriesHitTriggers: 1`, oyuncu kendi atışını kendi gövdesine yerdi (`LocalBodyAvatar` sınıf notu); (2) trigger "değdi mi" der, "**ne kadarı içeride**" demez — %30 kuralı temas sayısıyla ölçülemez |
| `Physics.ComputePenetration` | ❌ Derinlik verir, oran vermez; kemik başına çağrılırsa pahalıdır |
| **Ağırlıklı nokta örneklemesi** ✅ | Üç kuralı da tek mekanizmayla çözer, gövdeye collider gerektirmez, maliyeti sabittir |

⚠️ **İhlali yalnız ihlal eden istemci ölçer.** Uzak iskelet 100 ms gecikmeli ve interpolelidir;
başkasının gövdesinden ceza üretmek bayat veriyle karar yazmak olurdu.

### Akış (istemci, 20 Hz — `POSE_RATE_HZ` ile aynı kadans)

```
1) GENİŞ FAZ : Physics.OverlapSphereNonAlloc(göğüs, 1.2 m, buffer[8], obstacleMask,
                                             QueryTriggerInteraction.Ignore)
               → 0 sonuç ise DUR.  Yaygın durum: kare başına TEK sorgu.
2) DAR FAZ   : her örnek noktası × her aday collider
               a) aday.bounds.Contains(p)        → AABB ön eleme (çoğu nokta burada düşer)
               b) aday.ClosestPoint(p) == p      → kesin içeride testi (konveks garantili)
3) KARAR     : oran ≥ 0.30  VEYA  kafa tam içerde  VEYA  silah tam içerde
```

- Aday collider'ların `bounds`'u tik başına **bir kez** okunur (property her çağrıda native'e iner).
- ⚠️ **Dönüşüm yok:** collider'lar da kemikler de dünya uzayındadır (arena uzayı = dünya uzayı).
  `ArenaSpace` bu hesapta hiç kullanılmaz — telde giden tek şey bir bittir.
- ⚠️ Örnekleme `LateUpdate`'te ve `LocalBodyAvatar`'dan (execution order 30000) **SONRA**
  (`[DefaultExecutionOrder(30100)]`): kemikler retarget döngüsünde yazılıyor, erken okuyan kare
  bayat poz ölçer.
- Kemikler `Animator.GetBoneTransform(HumanBodyBones.*)` ile **bir kez** çözülüp önbelleklenir. Bu
  bir transform aramasıdır, poz aktarımı değil — `HumanPoseHandler` yasağı (Sistem-Özeti §7)
  buraya değmez.

**Maliyet:** engel yakınında değilken saniyede 20 physics sorgusu. Yakındayken ~29 nokta × ≤3 aday,
ezici çoğunluğu AABB'de eleniyor. Quest 3'te ölçülebilir yük değil.

---

## 3. %30 nasıl ölçülür — ağırlık tablosu

Nokta *sayısı* değil **kütle oranı** sayılır. "10 noktadan 3'ü" kuralında iki el + bir ayak (%1.8)
ile göğüs + karın (%36) aynı ağırlığa düşer ve kural anlamını kaybeder.

| Segment | Ağırlık | Nokta | Not |
|---|---|---|---|
| Kafa + boyun | 8.1 | **7** | Küre kabuğu: merkez + ±x/±y/±z (yarıçap ~11 cm); her nokta 8.1/7 |
| Göğüs (upper chest) | 21.6 | 2 | Ön/arka ±12 cm, her biri 10.8 |
| Karın (spine) | 13.9 | 2 | Ön/arka, her biri 6.95 |
| Leğen (hips) | 14.2 | 2 | Ön/arka, her biri 7.1 |
| Üst kol L/R | 2.8 ×2 | 2 | Omuz–dirsek ortası |
| Ön kol L/R | 1.6 ×2 | 2 | Dirsek–bilek ortası |
| El L/R | 0.6 ×2 | 2 | |
| Uyluk L/R | 10.0 ×2 | 2 | Kalça–diz ortası |
| Baldır L/R | 4.65 ×2 | 2 | Diz–ayak bileği ortası |
| Ayak L/R | 1.45 ×2 | 2 | |
| **Toplam** | **100.0** | **~29** | |

- **Oran** = içerideki noktaların ağırlık toplamı ÷ 100.
- **Eşik 0.30 girişte, 0.24 çıkışta** (histerezis). Tek eşikte sınırda duran oyuncu saniyede
  onlarca kez girip çıkar: halka titrer, karartma çırpınır, tel bayrağı zıplar.
- **Minimum süre 0.15 sn** — body tracking sıçraması (bir karelik kol ışınlanması) ceza başlatmasın.
- ⚠️ **Birlik (union) semantiği:** nokta *herhangi bir* aday collider'ın içindeyse içeride sayılır.
  İki kutunun ek yerinde duran kafa aksi hâlde "hiçbirinin tam içinde değil" diye kaçardı.

**Kafa tam içerde:** 7 kafa noktasının **hepsi** içeride. Küre kabuğu seçildi çünkü `ClosestPoint`
içerideki bir nokta için noktanın kendisini döner — **içeriden yüzey mesafesi ölçülemez.**

**Silah tam içerde:** elde tutulan silahın collider'ının yönelimli sınır kutusunun 8 köşesi +
merkezi (9 nokta); hepsi içerideyse tetik. Nokta kümesi yerel uzayda bir kez alınır, transform ile
taşınır. ⚠️ `WPN_*` prefablarına kurulum adımı EKLENMEZ, `WeaponKitBuilder` tablosuna dokunulmaz.

---

## 4. Ağ — yeni mesaj YOK

`SnapshotEntry.flags` baytında bit3-7 rezerv ("sıfır yazılır, okuyucu yok sayar") ve bit1-2 zaten
istemciden `GRIP_FLAG_MASK` süzgeciyle kopyalanıyor. İhlal bayrağı bit3 olur:

```
İstemci  → PoseUpdate(0x01).gripFlags bit3          (20 Hz, zaten giden paket)
Sunucu   → GRIP_FLAG_MASK'e bit3 eklenir → PlayerState.InObstacle
Sunucu   → Snapshot(0x02/0x05).flags bit3           (20 Hz, zaten giden paket)
Admin    → RemotePlayerRegistry.IsInObstacle(id)    (IsAlive ile aynı desen)
```

- **Ek bant yok** — bayrak zaten gönderilen iki pakete biner.
- **Durum tabanlıdır, olay tabanlı değil**: kaybolan UDP paketi 50 ms sonra kendini onarır. Kenar
  tetikli (`enter`/`exit`) bir mesajda kaybolan bir "çıktım" oyuncuyu **sonsuza kadar duvarda**
  bırakırdı.
- Otorite bölünmesine uyar: "gövdem nerede" istemci-otoriter sunum bilgisidir (poz ve elde tutulan
  eşya ile aynı kategori); **cezayı sunucu yazar.**

**`PROTOCOL_VERSION` artar** — rezerv bitin sözleşmesi değişiyor. Sıra: önce
`Docs/ArenaNet-Protokol.md`, sonra iki uç.

---

## 5. Sunucu

**Yeni sabitler (`ArenaProtocol`):**
- `OBSTACLE_DAMAGE_PER_SECOND = 30f` → 100 HP / 30 = **3.33 sn**, yani 4. saniye içinde ölüm
- `OBSTACLE_FLAG_STALE_MS = 300` — bu süredir taze poz gelmediyse bayrak **düşürülür**

**`PlayerState`:** `InObstacle` (bool) + `InObstacleStamp`. `StateHost` pozu yazarken (PoseGate
altında) biti çıkarır ve damgalar.

**`MatchDirector` tikinde (100 ms) sırayla:**
```
faz == playing        (hit_report ile AYNI kapı — §10.3)
oyuncu Alive          (ölüye ceza yok)
oyuncu Calibrated     (§10.6 — kalibresizde tespit yalancı pozitif üretir)
bayrak taze           (now - InObstacleStamp < OBSTACLE_FLAG_STALE_MS)
→ Hp -= 30 * delta                                  (tik başına 3 HP)
→ health_update { playerId, hp, attackerId = 0 }    → kurban + adminler
→ Hp ≤ 0: Alive=false, DiedAt, Deaths++,
          kill_event { killerId = 0, victimId, weaponId = "obstacle" },
          respawn { delaySeconds = _rules.RespawnDelay }
```

- ⚠️ **`OnKill` ÇAĞRILMAZ, skor yazılmaz** — öldüren yok. Takımdaş öldürmedeki kuralın aynısı: olay
  gerçekleşir (`deaths`, kill feed satırı), ödülü olmaz. `killerId = 0` protokolde zaten "saldırı
  değil" demek.
- ⚠️ **Ölünce / canlanınca / faz değişince / harita değişince bayrak SIFIRLANIR** — yoksa ölü
  oyuncunun bayrağı canlanma anında hâlâ set olur ve oyuncu doğar doğmaz erir.

### "İstemciden gelen 'içerideyim' doğrulanmalı mı?"

**Doğrulanamaz ve doğrulanmaya çalışılmaz.** Sunucuda arena geometrisi yoktur (`maps.json` yalnız
sahne adı + mod listesi). Bu zaten hasarın da modelidir: hasarı istemci hesaplar, sunucu aynen
uygular; §10.3 hile korumasının **bilinçli yokluğunu** yazar (gözetimli özel alan).

Sunucunun işi sebebi doğrulamak değil **sonucu sınırlamaktır**: süreyi kendi saatiyle ölçer
(istemci cezayı hızlandıramaz), bayat bayrağı düşürür (donmuş istemci sonsuz ceza üretemez),
faz/canlılık/kalibrasyon kapılarını kendisi uygular.

---

## 6. Oyuncu tarafı

### Karartma: tek renderer, iki yazar → hakem şart

`ArenaBoundary` karartma quad'ına `MaterialPropertyBlock` ile **doğrudan** yazıyor. İhlal sistemi de
aynı quad'a yazarsa kare başına birbirini ezerler (sınıra yaklaşırken engele girildiğinde alfa
titrer).

**Çözüm:** `ScreenFade` hakemi (`_Shared/Core/Player/`) — kaynaklar kendi alfa + rengini bildirir,
hakem en yükseğini çizer. `ArenaBoundary` renderer'a yazmayı bırakıp hakeme bildirir (davranışı
değişmez), ihlal sistemi ikinci kaynak olur.

### ⚠️ Tam karartma YAPILMAZ — emniyet meselesi

Oyuncu **fiziksel olarak** bir engelin içindedir; ekranı tümden karartmak onu kör hâlde gerçek bir
objenin dibinde bırakır ve çıkışını imkânsızlaştırır. Karartmanın işlevi cezalandırmak değil
"geri çekil" demektir.

Kırmızı tonlu, **tavanı ~0.75 alfa**, kenardan merkeze koyulaşan vinyet (merkez okunur kalır) +
`OVRInput.SetControllerVibration` ile nabız haptiği.

### VFX
Engelin en yakın yüzeyinde kıvılcım/enerji efekti — **havuzlanmış tek partikül sistemi**, ihlal
başına `Instantiate` YOK.

### Ne zaman çalışır
Tespit ve uyarı **her fazda** çalışır (lobide de oyuncu duvara girmemeyi öğrensin); **can yalnız
`playing` fazında** azalır ve o kapı sunucudadır — istemci onu taklit etmez.

---

## 7. Admin

`AdminPlayerMarkers.LateUpdate` içinde mevcut `ResolveColor` hattına ihlal dalı:

```csharp
bool blinkOn = ((int)(Time.unscaledTime * 6f) & 1) == 0;   // 3 Hz = 6 yarı periyot/sn
if (registry.IsInObstacle(playerId))
    color = blinkOn ? UiKit.Danger : UiKit.Dim(UiKit.Danger, 0.35f);
```

- **Öncelik: ihlal > seçili > takım rengi.** İhlal bir uyarıdır, seçim bir tercihtir.
- ⚠️ **Yanıp sönme fazı SENKRONLANMAZ ve gerekmez** — telde giden tek şey boolean; her admin ekranı
  kendi saatinde yanar. İki operatörün halkalarının aynı anda yanması hiçbir karar değiştirmez,
  karşılığında protokole zaman damgası sokardı.
- Bayrak durum tabanlı olduğu için halka ihlal bitince **kendiliğinden** takım rengine döner.

---

## 8. Doküman/kod sırası (bağlayıcı)

1. **`Docs/ArenaNet-Protokol.md`** — `FLAG_IN_OBSTACLE` (bit3), `GRIP_FLAG_MASK`'in yeni değeri,
   `OBSTACLE_DAMAGE_PER_SECOND`, `OBSTACLE_FLAG_STALE_MS`, çevre ölümünün sözleşmesi
   (`killerId = 0`, `weaponId = "obstacle"`), `PROTOCOL_VERSION`++
2. **Protokol DTO + sunucu** (`_Shared/Net/Protocol` saf C# ⇄ `Server/`)
3. **İstemci tespiti ve sunumu**
4. **`Docs/Sistem-Ozeti.md`** §4 (`BodyViolationProbe`, `ObstacleVolume`, `ScreenFade`) + §7
   tuzaklar (non-convex mesh → `ClosestPoint` yalanı · tek renderer iki yazar · kalibresiz yalancı
   pozitif)
5. **`CLAUDE.md`** — layer tablosu + yeni arena reçetesine tek satır ("iç engeller `Obstacle`
   layer'ına, collider konveks olmalı")

---

## 9. Görev listesi

### Protokol
- [ ] `SnapshotEntry.FLAG_IN_OBSTACLE = 1 << 3`; `GRIP_FLAG_MASK`'e eklenmesi
- [ ] `ArenaProtocol`: `OBSTACLE_DAMAGE_PER_SECOND`, `OBSTACLE_FLAG_STALE_MS`, `PROTOCOL_VERSION`++
- [ ] `IPoseSource.GetHeldItems` sözleşmesi (bit3 artık meşru)

### Sunucu
- [ ] `PlayerState.InObstacle` + `InObstacleStamp`
- [ ] `StateHost`: poz yazarken bit3'ü çıkar + damgala
- [ ] `MatchDirector`: tik başına can eritme, ölüm yolu (`killerId=0`), `OnKill` çağrılmaması
- [ ] Bayrağın ölüm/canlanma/faz/harita değişiminde sıfırlanması

### Ortam / editör
- [ ] `Obstacle` layer'ı (index 10) + `Breakable` (11) / `PlayerHitbox` (12) indekslerinin rezervi
- [ ] Mevcut arenalardaki iç engellerin layer'a taşınması
- [ ] `ObstacleVolume` (isteğe bağlı ince ayar bileşeni)
- [ ] `Tools > VortexArena > Arena > Engel Hacimlerini Denetle` — non-convex mesh / collider'sız
      obje raporu
- [ ] ⚠️ `Weapon`'a DOKUNULMAZ; `Template Temellerini Yükle`'ye de dokunulmaz (engeller arena
      sanatına aittir, altyapı değil)

### İstemci — tespit
- [ ] `BodyViolationProbe` (`_Shared/Core/Player/`): kemik çözümü, ağırlık tablosu, geniş/dar faz,
      histerezis, minimum süre, çalışma anı konvekslik bekçisi
- [ ] Silah nokta kümesi (elde tutulan eşyanın collider bounds'undan)
- [ ] `PlayerPoseTracker.GetHeldItems`'a bit3'ün yazılması
- [ ] Body tracking yokken zarif düşüş: yalnız kafa + eller örneklenir, oran kuralı devre dışı,
      kafa/silah kuralları çalışır

### İstemci — sunum
- [ ] `ScreenFade` hakemi + `ArenaBoundary`'nin ona taşınması (davranış aynı kalacak)
- [ ] Kırmızı vinyet + haptik + havuzlanmış kesişim VFX'i
- [ ] Kill feed'de çevre ölümü etiketi (`ModeHudBase`)

### Admin
- [ ] `RemotePlayerRegistry.IsInObstacle(playerId)` (IsAlive deseni)
- [ ] `AdminPlayerMarkers`: ihlal dalı + 3 Hz yanıp sönme + öncelik sırası

---

## 10. Karar bekleyen (küçük)

- **Uyarı kademesi olsun mu?** İhlalden önce (ör. oran 0.15'te) hafif bir uyarı katmanı oyuncuya
  "çekil" deme şansı verir; yoksa ceza aniden başlar.
- **`ObstacleVolume`'un ince ayar alanları gerçekten gerekiyor mu?** Obje başına farklı ceza
  istenmiyorsa bileşen hiç yazılmaz (okuyanı olmayan ayar bayatlar) — layer tek başına yeter.
