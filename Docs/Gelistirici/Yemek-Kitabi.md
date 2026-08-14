---
title: Yemek Kitabı
---

# Yemek Kitabı

"Şunu yapmak istiyorum" → kopyala, yapıştır, çalıştır. **Günlük olarak kullanacağın sayfa burası.**

Her reçetenin altında *neden böyle* kutusu var — orayı okumazsan çalışır, ama bir gün neden
çalışmadığını anlamazsın.

| İstediğin | Reçete |
|---|---|
| Kendi silahımı ateşleyince ağa bildirmek | [1](#1-kendi-silahımı-yazdım-ateşleyince-ne-çağırayım) |
| Mermi/ok gibi uçan bir şeyin çarpması | [2](#2-hitscan-değil--mermiokbıçak-çarptı) |
| Bomba, el bombası, alan hasarı | [3](#3-alan-hasarı-bomba-şok-dalgası) |
| Bölgeye göre hasar (kafa/karın/bacak) | [4](#4-bölgeye-göre-hasar-kafa--karın--bacak) |
| "Şu an ateş edebilir miyim?" | [5](#5-ateş-edilebilir-mi) |
| Biri öldü / canlandı olayını yakalamak | [6](#6-biri-öldü--canlandı) |
| Yerel oyuncunun canı, ölümü, durumu | [7](#7-yerel-oyuncunun-canı-ve-durumu) |
| Maç fazı, kalan süre, skor | [8](#8-maç-fazı-süre-skor) |
| Uzak oyuncular nerede | [9](#9-uzak-oyuncular-nerede) |
| Modun kurallarını okumak | [10](#10-modun-kurallarını-okumak) |
| Yeni silah eklemek | [11](#11-yeni-silah-eklemek) |
| Kendi mod HUD'ını yazmak | [12](#12-kendi-hudını-yazmak) |
| Yeni mod eklemek | [13](#13-yeni-mod-eklemek) |
| Yeni arena eklemek | [14](#14-yeni-arena-eklemek) |
| Hazır bir environment'ın içinde arena bölgesi kurmak | [14.1](#141-hazır-bir-environmentın-içinde-arena-bölgesi-kurmak) |
| Gözlüksüz test (dev penceresi) | [15](#15-gözlüksüz-test-dev-penceresi) |
| Bir konumu ağdan paylaşmak | [16](#16-bir-konumu-ağ-üzerinden-paylaşmak-arena-uzayı) |
| Arena ölçüsünü girmek (boyut dosyası) | [17](#17-arena-ölçüsü-boyut-dosyası) |
| VR'da tıklanabilir dünya-uzayı paneli | [18](#18-vrda-tıklanabilir-bir-dünya-uzayı-paneli) |
| İsabet göstergesinin (X) görünümü | [19](#19-isabet-göstergesinin-x-görünümünü-değiştirmek) |

---

## 1. Kendi silahımı yazdım, ateşleyince ne çağırayım?

Tek ihtiyacın `ArenaCombat`. Hitscan (anında ışın) bir silah için tam örnek:

```csharp
using UnityEngine;
using VortexArena.Core.Combat;

public class Yay : MonoBehaviour
{
    [SerializeField] private Transform muzzle;      // okun çıktığı nokta
    [SerializeField] private float damage = 60f;
    [SerializeField] private float range = 40f;

    public void Firlat()                            // ← kendi "ateşledi" olayın
    {
        // 1) Ateş edebilir miyiz? (ölüyken / geri sayımda tetik boşa basılır)
        //    ⚠️ Oyuncunun KENDİSİ engelin içindeyse bu zaten false döner (§10.9) —
        //    "bloğun içinde durup silahı dışarı uzatma" hilesinin kapısı burasıdır.
        if (!ArenaCombat.CanFire) return;

        Vector3 dir = muzzle.forward;

        // 2) Namlu bir iç engelin içinde mi? Öyleyse ATIŞ HİÇ OLMAZ — ne ses, ne efekt,
        //    ne cephane, ne ağ olayı (§10.9: duvar arkasından ateş etme kapısı).
        //    Silahın GÖVDESİNİ de sınamak istiyorsan: ArenaCombat.IsWeaponBlocked(...)
        //    (yönlendirilmiş kutu; Weapon onu kullanıyor).
        if (ArenaCombat.IsMuzzleBlocked(muzzle.position, dir)) return;

        // 3) Atışı bildir: diğer oyuncular namlu alevini/sesini görsün.
        //    Hasarla ilgisi yok, sunucu doğrulamaz — yalnız relay eder.
        ArenaCombat.ReportShot(muzzle.position, dir, "yay");

        // 4) Isabet. ⚠️ Kendi Physics.Raycast'ini YAZMA: TraceShot trigger'ları eliyor
        //    (kavrama hacimleri mermiyi durdururdu) ve engel kuralını ikinci kez uyguluyor.
        ArenaCombat.ShotTrace iz = ArenaCombat.TraceShot(muzzle.position, dir, range);
        if (!iz.HasHit) return;

        // Hedef bir AĞ OYUNCUSU ise vuruşu bildirir; değilse hiçbir şey olmaz.
        // Dönüş değeri yalnız sunum içindir: gövde efekti mi, duvar efekti mi?
        ArenaCombat.ReportRaycastHit(iz.Hit, damage, "yay");
    }
}
```

Bu kadar. Hiçbir DTO kurmadın, hiçbir koordinat dönüşümü yapmadın, hiçbir yere abone olmadın.

> **Neden böyle:** bir vuruşu doğru bildirmek dört şeyi bilmeyi gerektirir — poz *arena uzayına*
> çevrilmeli, **yön bir nokta değildir** (öteleme düşülmeli), hedef bir `RemoteHitBox` üzerinden
> çözülmeli ve hasarı istemci belirler. `ArenaCombat` dördünü de kapsar.
> ⚠️ **Işını kendin atma:** `Physics.Raycast` orijini İÇİNDE olduğu collider'ı hiç vurmaz, yani
> namlusunu sandığın içine sokan oyuncu sandığı delip arkasındakini vurur. `IsMuzzleBlocked`
> (tetik kapısı) + `TraceShot` (ışın) ikisi de aynı testi kullanır; kendi raycast'ini yazan
> kaybeder.
> `ReportRaycastHit` `false` dönerse hedef ağ oyuncusu değildir (dekor, duvar) —
> **hasar uygulanmaz ve yapılacak yerel bir şey yoktur**; istemcide can tutan bir yol YOKTUR.
> Dönüş değerini yalnız sunum için kullan (kan efekti mi, isabet izi mi). Kırılabilir objeler
> ileride ağsal (sunucu-otoriter) olacak → `plan/agsal-kirilabilir-objeler.md`.

> **İsabet göstergesini yazma, hazır geliyor.** Bildirilen her vuruşta değdiği noktada bir X
> belirir (`HitMarker`) ve onu **yalnız vuran oyuncu görür** — `ReportHit`'in içinde olduğu için
> yazdığın her hasar kaynağı onu bedavaya alır. İkinci bir gösterge kurma: aynı vuruşta iki X
> çizilir. ⚠️ Gösterge *bildirimin yapıldığını* söyler, hasarın uygulandığını değil — sunucu
> vuruşu reddedebilir (dost ateşi kapalı, faz `playing` değil).

> ⚠️ **Canı yerelde düşürme.** `ReportHit` yalnızca *bildirir*. Hedefin canı sunucudan
> `health_update` ile geri gelir; yerel oyuncunun canını `PlayerCombatState` (ve ondan beslenen
> HUD) okur. Yerelde düşürürsen hasar iki kez uygulanmış gibi görünür ve iki istemci farklı can
> görür.

---

## 2. Hitscan değil — mermi/ok/bıçak çarptı

Uçan bir obje varsa `OnTriggerEnter`/`OnCollisionEnter` içinde aynı kapıyı kullan:

```csharp
using UnityEngine;
using VortexArena.Core.Combat;

public class Mermi : MonoBehaviour
{
    [SerializeField] private float damage = 40f;

    private void OnTriggerEnter(Collider other)
    {
        // Ağ oyuncusu değilse (duvar, dekor) mermi yalnız yok olur — yerel hasar yolu yoktur.
        if (ArenaCombat.TryGetTargetPlayerId(other, out int playerId))
        {
            ArenaCombat.ReportHit(playerId, transform.position, damage, "mermi");
        }

        Destroy(gameObject);
    }
}
```

> **Neden `TryGetTargetPlayerId`:** isabet kutusu (`RemoteHitBox`) uzak oyuncu gövdesinin
> herhangi bir çocuğunda olabilir; metot yukarı doğru arar. Kendi `GetComponent` çağrını yazarsan
> gövde/kafa/el kutularının bir kısmını kaçırırsın.

> ⚠️ Merminin **atıcının kendisine** çarpmasını sen engellemelisin (layer ya da kısa bir doğma
> gecikmesiyle). Sunucu "kendini hedefledi" vuruşunu zaten reddeder ama mermi yine de yok olur.

---

## 3. Alan hasarı (bomba, şok dalgası)

Protokolde "alan hasarı" diye bir mesaj **yoktur**. Alan etkisi = etkilenen her oyuncuya ayrı bir
vuruş. Bunu senin için yapan hazır metot var:

```csharp
// Merkezde 120 hasar, 6 m yarıçapın kenarında 120 × 0.25 = 30 hasar.
int vurulan = ArenaCombat.ReportAreaHit(
    worldCenter: transform.position,
    radius:      6f,
    damage:      120f,
    weaponId:    "bomba",
    edgeScale:   0.25f);

Debug.Log($"{vurulan} oyuncu vuruldu");
```

Hasar merkeze uzaklıkla doğrusal düşer ve her oyuncuya **en fazla bir** vuruş gider (bir gövdede
birden çok isabet kutusu vardır).

> ⚠️ **Duvar arkası kontrolü yapılmaz.** Görüş hattı istiyorsan kendin kur:
> ```csharp
> foreach (var col in Physics.OverlapSphere(merkez, 6f))
> {
>     if (!ArenaCombat.TryGetTargetPlayerId(col, out int id)) continue;
>     if (Physics.Linecast(merkez, col.bounds.center, engelKatmani)) continue;  // duvar var
>     ArenaCombat.ReportHit(id, col.bounds.center, HasarHesapla(merkez, col), "bomba");
> }
> ```

---

## 4. Bölgeye göre hasar (kafa / karın / bacak)

```csharp
if (Physics.Raycast(muzzle.position, dir, out RaycastHit hit, range))
{
    float uygulanan = damage * definition.GetZoneMultiplier(ArenaCombat.GetHitZone(hit.collider));

    ArenaCombat.ReportRaycastHit(hit, uygulanan, "ak47");
}
```

> **Çarpanı sen uygularsın.** Sunucu gönderdiğin sayıyı aynen kullanır — bölge çarpanı, mesafe
> düşüşü, zırh, hepsi senin tarafında. Bu yüzden denge değişikliği için sunucuya dokunmazsın
> (ama APK build'i gerekir, çünkü sayılar istemcide yaşar).

`GetHitZone` ağ oyuncusu olmayan bir hedefte `HitZone.Body` döner, yani çarpan 1'dir — dekora
ateş ederken ayrı bir kontrol yazmana gerek yok. Yalnız kafayı sorgulaman yetiyorsa
`ArenaCombat.IsHeadshot(...)` hâlâ duruyor.

⚠️ Bölge, isabet eden **kutunun** özelliğidir ve kutular **elle bakılır** (üreten bir araç yoktur).
Kemiğe yeni bir kutu asarsan `RemoteHitBox` eklemeyi ve `zone`'unu seçmeyi unutma: işaretsiz
collider hiç vurulamaz, işaretli ama bölgesi seçilmemiş kutu `Body` (1×) sayılır.
Kutular her gövdenin altından otomatik toplanır — güncellenecek bir liste yok.

---

## 5. Ateş edilebilir mi?

```csharp
if (!ArenaCombat.CanFire) return;
```

`CanFire` şunların hepsini birden kontrol eder: oyuncu **hayatta mı**, faz **Lobby veya Live mı**
(Loading/Countdown/End'de ateş yok) ve bir kez bağlanıldıysa **bağlantı açık mı**.

> **Neden tek özellik:** bunları ayrı ayrı kontrol eden kod kaçınılmaz olarak birini unutur —
> en sık unutulan geri sayım fazıdır ve oyuncu "başla" demeden ateş eder.
> Hiç bağlanılmamışsa (sunucusuz editör testi) `true` döner, yerel testin bozulmaz.

---

## 6. Biri öldü / canlandı

Bütün sunucu olayları `NetEvents` üzerinden gelir. **Statiktir** — ne zaman abone olduğunu
düşünmene gerek yok.

```csharp
using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

public class OlumEfektleri : MonoBehaviour
{
    private void OnEnable()
    {
        NetEvents.OnKillEvent    += Oldurme;
        NetEvents.OnHealthUpdate += CanDegisti;
    }

    private void OnDisable()          // ← abonelikten çıkmayı UNUTMA
    {
        NetEvents.OnKillEvent    -= Oldurme;
        NetEvents.OnHealthUpdate -= CanDegisti;
    }

    private void Oldurme(KillEventMsg msg)
    {
        // msg.killerId (0 = çevre ölümü), msg.victimId, msg.weaponId
        Debug.Log($"{msg.killerId} → {msg.victimId} ({msg.weaponId})");
    }

    private void CanDegisti(HealthUpdateMsg msg)
    {
        // msg.playerId, msg.hp, msg.attackerId (0 = canlanma, saldırı değil)
        if (msg.hp <= 0f) PatlamaOynat(msg.playerId);
    }
}
```

Kullanabileceğin olayların tamamı → [API Referansı → NetEvents](API-Referansi.md#netevents).

> **Canlanmayı nasıl anlarım?** `OnHealthUpdate` içinde `hp > 0` gelmesi ve `attackerId == 0`
> olması canlanmadır — canlanma bir saldırı sonucu değildir.

> ⛔ **Canlanan oyuncuyu bir yere taşıma.** `respawn` mesajında konum/slot alanı **yoktur**:
> oyuncu öldüğü yerde durur, taban bölgesine (`BaseZone`) kendi ayaklarıyla yürür ve orada
> canlanır. Aynısı harita değişiminde de geçerli — `load_match` kimseyi yeniden doğurmaz ve
> kalibrasyonu sıfırlamaz.

---

## 7. Yerel oyuncunun canı ve durumu

`PlayerCombatState` kalıcı bir tekildir ve **kendini önyükler** — sahneye koymana gerek yok.

```csharp
using UnityEngine;
using VortexArena.Core.Combat;

public class CanTitresimi : MonoBehaviour
{
    private PlayerCombatState _combat;

    private void Start()
    {
        _combat = PlayerCombatState.Instance;
        if (_combat == null) return;

        _combat.HpChanged     += Can;        // float hp
        _combat.AliveChanged  += Hayatta;    // bool alive
        _combat.StatusChanged += Durum;      // "Canlanmak için sabit dur — 2 sn"
    }

    private void OnDestroy()
    {
        if (_combat == null) return;
        _combat.HpChanged     -= Can;
        _combat.AliveChanged  -= Hayatta;
        _combat.StatusChanged -= Durum;
    }

    private void Can(float hp) { /* ... */ }
    private void Hayatta(bool alive) { /* ... */ }
    private void Durum(string metin) { /* ... */ }
}
```

Anlık okuma da yapabilirsin: `PlayerCombatState.Instance.Hp / .IsAlive / .Team / .Phase /
.PlayerId / .StatusText`.

> ⚠️ **`Instance` null olabilir.** Tekil `AfterSceneLoad`'da önyüklenir; `Awake` içinde okursan
> henüz yok olabilir. `Start`'ta bağlan ya da null kontrolü yaz.

> **`StatusText`'i sen yazma.** "Öldün — canlanmaya 3 sn", "Canlanmak için sabit dur" gibi metinler
> modun kuralına göre zaten üretiliyor. Kendi metnini yazarsan mod değişince yalan söyler.

---

## 8. Maç fazı, süre, skor

```csharp
NetEvents.OnMatchState += msg =>
{
    // msg.phase: "Lobby" | "Loading" | "Countdown" | "Live" | "End"
    // msg.timeRemaining (saniye), msg.scoreRed, msg.scoreBlue
};

NetEvents.OnCountdown += msg => { /* msg.seconds: 5,4,3,2,1 */ };
NetEvents.OnMatchEnd  += msg => { /* msg.winnerTeam VEYA msg.winnerPlayerId */ };
```

`match_state` **saniyede bir** gelir — her karede değil. Akıcı bir geri sayım istiyorsan son gelen
değeri kendin azalt.

> ⚠️ **Kazanan iki kanaldan biriyle gelir.** Takım skorlu modlarda `winnerTeam` (`"red"`/`"blue"`/`""`),
> bireysel skorlu modlarda `winnerPlayerId` (`0` = berabere). Hangisine bakacağını
> `ModeRuntime.Scoring` söyler; bir mod ikisini birden doldurmaz.

---

## 9. Uzak oyuncular nerede

```csharp
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Net;

public class YakinlikSensoru : MonoBehaviour
{
    private readonly List<int> _ids = new List<int>();

    private void Update()
    {
        RemotePlayerRegistry reg = RemotePlayerRegistry.Instance;
        if (reg == null) return;

        reg.GetActivePlayerIds(_ids);
        foreach (int id in _ids)
        {
            if (!reg.GetInterpolatedPose(id, out Pose head, out Pose handL, out Pose handR))
                continue;

            // ⚠️ Pozlar ARENA UZAYINDA gelir — dünyaya çevir.
            Vector3 dunyaKafa = VortexArena.Core.Arena.ArenaSpace.ArenaToWorld(head.position);

            if (Vector3.Distance(dunyaKafa, transform.position) < 1.2f)
                Uyar(id);
        }
    }
}
```

`reg.IsAlive(playerId)` ile ölü/diri de sorabilirsin.

> ⚠️ **Ölü oyuncuları listeden eleme.** Free-roam'da ölüm bir durum değişimidir — ölü oyuncunun
> bedeni sahada durmaya devam eder ve fiziksel çarpışma riski aynıdır.

> **Neden interpolasyon:** pozlar 20 Hz gelir, oyun 72–90 Hz çizer. `GetInterpolatedPose`
> `INTERP_DELAY_MS` (100 ms) geriden iki örnek arasında yumuşatır. Ham örnekleri okursan
> avatarlar zıplar.

---

## 10. Modun kurallarını okumak

**`if (modeId == "ffa")` yazma.** Modun şekli telden gelir:

```csharp
using VortexArena.Core;

if (ModeRuntime.IsTeamless)                  // takım var mı
    RengiNotraYap();

if (ModeRuntime.Revive == ModeReviveAnchor.StandStill)
    SabitDurGostergesiniAc();

// ⚠️ "Silahı mod mu dağıtıyor" sorusu tek başına kaynağa bakılarak cevaplanmaz — aşağıya bak
if (ModeRuntime.Weapons == ModeWeaponSource.RandomGrant && !ModeRuntime.FireWhilePaused)
    SahnedekiSilahlariGizle();

float gecikme = ModeRuntime.RespawnDelay;    // 0 GEÇERLİDİR (anında canlanma)

ModeRuntime.Changed += KurallarDegisti;      // maç yüklenince tetiklenir
```

Okunabilir alanlar: `ModeId`, `Teams`, `Scoring`, `FriendlyFire`, `Revive`, `Weapons`,
`RespawnDelay`, `FireWhilePaused`, `IsTeamless`.

> **Neden tek okuma noktası:** canlanma, skor satırı, silah kaynağı ve admin arayüzü aynı bilgiyi
> ister. Dördü ayrı ayrı `load_match` dinlerse dördü ayrı ayrı bayatlar.

> ⚠️ **`RespawnDelay == 0` geçerli bir değerdir** (FFA'da öyle). `if (delay > 0)` diye kontrol edip
> varsayılana düşme.

> ⚠️ **`Weapons == RandomGrant` "kurulmuş bir maç var" demek DEĞİLDİR.** Operatör lobideyken bir
> arena seçtiğinde o arena sahnelenir ve kural şekli lobi profilinde kalır — kaynak orada da
> `random`'dır. Serbest alanı ayıran bileşim `random` + `FireWhilePaused`'dur; koşan FFA maçı da
> `random`'dır ama serbest atışı yoktur. Silah tezgâhlarını gizlemek gibi "maç kuruldu" varsayan
> her davranış bu bileşimi sorar, yoksa maçı bekleyen oyuncunun elinden silahı alır
> (`Docs/ArenaNet-Protokol.md` §10.7).

---

## 11. Yeni silah eklemek

**Sunucuda hiçbir iş yoktur ve export gerekmez.** Sunucuda silah tablosu bulunmaz.

1. Prefabı `Assets/_Shared/Arsenal/Prefabs/` altına koy.
2. `WeaponDefinition` SO'sunu `Assets/_Shared/Arsenal/Data/` altına oluştur
   (*Create → VortexArena → Weapon Definition*): `weaponId`, hasar, atış hızı, menzil, saçılım,
   şarjör, haptik (`hapticAmplitude` 0-1 + `hapticDuration` sn — atış başına kumanda titreşimi;
   ikisinden biri 0 ise o silahta haptik yoktur), `prefab`.
3. Modun kullanmasını istiyorsan `ModeDefinition.loadout` listesine ekle.

`weaponId` yalnızca **kill feed etiketidir** — sunucu doğrulamaz, istediğini yazabilirsin.

> Silahın eldeki **rotasyonu ayarlanmaz: kumanda anchor'ının rotasyonudur (kimlik)** — kumandayı
> uzatınca namlu ileri bakar. **Tutulma noktasını ve el modelinin bileğini** `WD_*`'a yakalanan
> kavrama kayıtları belirler (§11.0); dördü de el başınadır (`primaryGripRight/Left`,
> `secondaryGripRight/Left`). ⚠️ **Ölçü TEK yerden okunur:** aynı kayıt yerel duruşu, uzak
> oyuncudaki çizimi ve soketin yerini birlikte besliyor.
> Çerçeveden seçilen silah (`weaponSource:"weaponcanvas"`), modun verdiği silah
> (`weaponSource:"random"`) ve elde ISDK ile kavranan eşya **aynı yolu** kullanır; ayrım yoktur.

> ⚠️ Denge sayıları istemcide yaşadığı için değişiklik **APK build'i ister** — sunucuyu yeniden
> başlatmak yetmez.

> ⚠️ **Silah sesi tabloda DEĞİLDİR ve araç ona dokunmaz.** Ses değiştirmek =
> `Assets/_Shared/Arsenal/Data/WD_<Ad>.asset`'i seç, klibi ilgili alana **sürükle**. Beş yuva var:
> `fireClips` (dizi — her atışta rastgele biri seçilir), `magOutClip` (reload `t=0`),
> `magInClip` (reload `t = 0.70 × reloadTime`; boş bırakılırsa o an sessiz kalır ve `magOutClip`
> tüm reload sesini taşır), `dryFireClip` (boş şarjörde tetik), `pickupClip` (silah alınırken).
> `Build Weapon Prefabs` koşmak gerekmez — ama koşarsan ateş sesi atanmamış silahları listeler.
> ⚠️ Ateş klipleri `PlayOneShot` ile çalınır, yani **üst üste biner**: aranan dosya her zaman
> **tek atış**tır, tarama/loop kaydı saniyede 12 kez çalınıp çorbaya döner. Kuyruğu kısa tut —
> `pitch` AudioSource'un özelliği olduğu için her yeni atış hâlâ çalan kuyrukları da yeniden
> perdeler.
> Aynı şey diğer alanlar için geçerli DEĞİLDİR: hasar/rpm/menzil/saçılım her koşuda tablodan ezilir.

> Sahnedeki silahın **çerçevesi** için elle iş yoktur: `Build Weapon Prefabs` her `WPN_*` köküne
> `VA_WeaponFrame` örneğini kendisi koyar. Çerçevenin arenada görünüp görünmemesi ayrı bir konudur
> → bir sonraki reçete.

---

## 11.0 Bir silahın kavramasını YAKALAMAK

Kavrama **elle yazılmaz, gözlükle ölçülür**: silahı gerçek elinle tutar gibi tutarsın, araç o anda
elinin (ISDK **bileğinin**) silaha göre pozunu `WD_*.asset`'e yazar. Ayarlanacak bir sayı, sürüklenecek
bir hayalet el ve bir euler düğmesi **yoktur**.

⚠️ **Silahın eldeki DÖNÜŞÜ ayarlanamaz — kumanda anchor'ının dönüşüdür (kimlik):** kumanda nereye,
namlu oraya. Yakaladığın pozun rotasyonu silaha hiç karışmaz, yalnız el modelinin bileğini silahın
üstüne oturtur. Silah elde yatık görünüyorsa sebebi kavrama kaydı değil, `Model`'in prefabtaki
yerleşimidir.

⚠️ **El takibi ZORUNLU değil, tercih edilendir.** Kumanda tutulurken de ölçülecek bir bilek vardır
(rig kumanda pozundan sentetik el üretir, `controllerDrivenHandPosesType = Natural`) ve oyun oyunda
da aynı bileği okur — yani kumandayla yakalanan kavrama tutarlıdır. Kumandaları bırakmanın kazancı
görünürlüktür: parmaklarının kabzaya nereye oturduğunu gözünle görürsün. HUD hangi kipte olduğunu
her karede yazar.

**Akış (editörde, APK build'i gerekmez):**

1. `Tools > VortexArena > Development > Dev` → **Rol: Silah** (kısayol `Ctrl+Alt+R` üç rolü
   döndürür) → listeden ölçülecek `WD_*`'ı seç. Hedef/sandbox/başlangıç seçimleri bu rolde sönük
   çizilir: sunucuya bağlanılmaz.
2. **Play**. Doğrudan `WeaponCalibration` sahnesi açılır (Boot/Lobby akışı hiç koşmaz). Seçilen
   silah kafanın **karşısında** belirir ve **donar** — sen ona yaklaşırsın, o seni izlemez.
3. HUD üç satır yazar: geri sayım · hangi aşamadasın · yönerge. Sıra **sabittir**:
   **1/4 ana kabza sağ → 2/4 ana kabza sol → 3/4 ön kabza sağ → 4/4 ön kabza sol.**
4. Elini silahı tutacağın yere getir ve sayacı başlat → tepede **5 saniyelik** geri sayım başlar.
   Sayım sürerken elini aç ve kabzayı normal tuttuğun gibi sar.
   **Başlatmanın iki yolu var ve ikisi de geçerlidir:** **pinch** (baş parmağınla işaret parmağının
   UÇLARINI birbirine değdirmek) ya da **kumanda tetiği**. HUD'un yönerge satırı o an hangisinin
   canlı olduğunu yazar.
5. Sayaç bitince ölçü alınır, `WD_*.asset`'e yazılır ve diske kaydedilir; HUD onaylar ve bir sonraki
   aşamaya geçer. Dördü bitince iş biter — Play'i durdur.

- ⚠️ **Başlatma girdisi ölçünün parçası DEĞİLDİR:** ölçü, sayaç bittiğinde okunan bileğin pozudur ve
  o bilek iki kipte de aynı kaynaktan gelir (`HandGripPoser.TryGetTrackedWrist`). Kumanda kipinde
  yakalanan kavrama oyunla **tutarlıdır** — oyuncu zaten kumanda tutuyor ve oyun aynı bileği okuyor.
  El takibinin üstünlüğü ölçünün doğruluğu değil **görünürlüğüdür**: parmaklarının kabzaya nereye
  oturduğunu gözünle görürsün.
- ⚠️ **Sayaç girdi bırakılınca İPTAL OLMAZ** ve edilmemeli: sayacın var olma sebebi tam olarak
  başlattıktan sonra elini açıp kabzayı sarmandır. Ölçülen şey başlatma anındaki el değil, **sayaç
  bittiğindeki** eldir.
- ⚠️ **Kayıt EL BAŞINADIR** ve dört kaydın dördü de ayrı ölçülür: kabza simetrik değildir (tetik,
  şarjör, kurma kolu tek taraftadır), tek kayıttan aynalamak sol eli silahın **içine** sokar. Bir
  eli atlarsan oyun onu öteki elin kaydına düşürür — çalışır ama o el yanlış tutar.
- ⚠️ Sahnedeki silah bir oyuncak değil bir **ölçü hedefidir**: kavranamaz, ateş etmez, ses çıkarmaz
  (bileşenleri sökülür). Onu tutmaya çalışma, sadece elini doğru yere koy.
- ⚠️ **Parmakların duruşu ölçülmez ve yazılmaz.** Oyunda silah tutan elde parmaklar izlemeden/
  kumandadan gelir (grip parmakları kapatır, tetik parmağı serbest kalır); silah başına parmak
  ayarı diye bir veri yoktur.
- Kavraması yakalanmamış silahta el kumandanın ekseninde kalır + konsola oturum başına bir uyarı
  gider; `Build Weapon Prefabs` de koşu sonunda **"kavraması YAKALANMAMIŞ silahlar"**ı listeler.
- Yakalama diske `AssetDatabase` ile yazıldığı için bu rol **yalnız editörde** anlamlıdır ve
  `WeaponCalibration` sahnesi hiçbir build'e girmez.

### Başkalarının gördüğü el (uzak avatarın parmakları)

Parmaklar telde gitmez (`Docs/ArenaNet-Protokol.md` §6.9), yani uzak avatarın parmakları
**sentezlenir**: eşya tutan elde `HandPoseProfile.DefaultGrip`, boş elde `RemoteAvatar.prefab` →
`idleHandPose` (boşsa `HandPoseProfile.Idle`). ⚠️ Eşya başına yazılan bir parmak duruşu **yoktur ve
eklenmez** — silah başına ikinci bir elle-ayar düğmesi doğururdu.

**Genel his** (tüm silahlar birden) eklem başına açı tavanlarından gelir:
`HandFingerRig.FingerMaxAngles` / `ThumbMaxAngles`. Tek bir silah tuhaf duruyorsa bakılacak yer o
silahın **kavrama kaydıdır**, parmak değil.

## 11.1 Bir arenada silah çerçevesini görünür/görünmez yapmak

**Ne zaman:** çerçeve o arenanın sanat diline uymuyor (ör. silah bir masanın üstünde dursun,
çerçeve görünmesin) ya da tersine oyuncuya "silah buradan alınır" diye göstermek istiyorsun.

1. Arena sahnesini aç.
2. Hiyerarşide sahnedeki `WPN_*` **örneğini** seç.
3. Altındaki `VA_WeaponFrame` çocuğuna in.
4. `WeaponFrame` bileşenindeki **`isFrameVisible`** kutusunu işaretle / kaldır.
5. Sahneyi kaydet. (Birden çok silahı aynı anda seçip tek hamlede yapabilirsin.)

> ⚠️ Bu bir **prefab override**'dır, yani ayar **sahneye özeldir** — istenen davranış budur:
> aynı silah bir arenada çerçeveli, başka arenada çerçevesiz durabilir. `VA_WeaponFrame`
> prefabının kendisini düzenlersen **tüm arenalar** etkilenir.

> ⚠️ Görünürlük **yalnız sunumdur.** Çerçeve görünmez olsa bile silah yine oradan,
> `maxGrabDistance` mesafesinden nişan alınarak seçilir ve ele klonlanır; alma menzilini ya da
> kavramayı kapatmaz.

> ⚠️ **Nişan ışını çerçeveden gelmez.** Oyuncunun gördüğü uzaktan-seçim göstergesi ISDK'nın kendi
> mesafe-kavrama görselidir (tüp + reticle); `WeaponFrame`'in kendi `LineRenderer` ışını
> (`isRayVisible`) **kapalıdır**, ikisi birden açıkken elde iki ışın görünür. Menzil bilgisi
> kaybolmaz: ISDK adaylarını `WeaponFrame.Filter`'dan geçiriyor, 2 m'nin dışındaki çerçeve hover
> bile almaz.

> **Çerçeve yalnız silah SABİT dururken vardır.** Silah hangi yoldan tutulursa tutulsun — ele
> verildi (`WeaponGranter`) ya da doğrudan kavrandı (ISDK) — çerçevenin GameObject'i kapanır;
> bırakılınca geri gelir. Yani elde duran silahta ne çerçeve görseli ne de uzaktan seçim kapısı
> olur. Bu `isFrameVisible` ile ilgisizdir ve elle kurulum istemez:
> `WeaponFrame` silahın `Weapon.HeldChanged` olayını dinler. Yeni bir "silahı ele alma" yolu
> yazarsan o yola ayrıca bir şey eklemene gerek YOKTUR — kural olayda durur.

---

## 11.2 Silah yerde/masada dursun, oyuncu eğilip ELLE alsın

**Ne zaman:** o sahnede silah bir çerçeve kaynağı değil, yerde duran normal bir nesne olsun
istiyorsun (oyuncu yaklaşıp soketinden kavrasın, uzaktan seçme olmasın).

⚠️ **`isFrameVisible` bunu YAPMAZ** — o yalnız çerçeve modelini gizler (§11.1). Kapatman gereken
şey görsel değil, `WeaponFrame`'in **kendisidir**:

1. Sahnedeki `WPN_*` örneğini seç → altındaki **`VA_WeaponFrame` çocuğunu** seç.
2. Objenin **aktiflik kutusunu kaldır** (`SetActive(false)`) — bileşeni değil, GameObject'i.
3. Sahneyi kaydet.

Böylece `WeaponFrame.Awake` hiç koşmaz: silah donmaz (`Rigidbody` fizikli kalır), kendi
`Grabbable`/`GrabInteractable`/`HandGrabInteractable`/`ItemGripSockets`'i açık kalır → normal
yakın kavrama çalışır (iki kavrama hattı da açık kaldığı için el izleme ayarından bağımsızdır).
Çerçeve görseli prefabda zaten pasif durduğu için kendiliğinden görünmez.

⚠️ **Bileşeni `enabled = false` yapma.** Unity kapalı bileşende de `Awake` çağırır → silah yine
donar ve yakın kavrama kapanır; ama `OnEnable` koşmadığı için uzaktan da seçilemez. Sonuç: hiç
alınamayan ölü bir silah.

Üç seçeneğin ölçülen farkı (`WPN_M4A1` örneği üstünde):

| | Grabbable / GripSockets | Rigidbody | çerçeve görseli |
|---|---|---|---|
| Normal (çerçeve açık) | kapalı | kinematik, yerçekimsiz | görünür, silaha oturtulmuş |
| `isFrameVisible = false` | **kapalı** (değişmez) | kinematik, yerçekimsiz | gizli |
| `VA_WeaponFrame` GO kapalı | **açık** | fizikli, yerçekimli | gizli |

---

## 12. Kendi HUD'ını yazmak

Sıfırdan yazma — `ModeHudBase`'den türet. Faz/süre, geri sayım, can barı, ölüm ekranı, kill-feed
ve kendi öldürme/ölüm sayacın **hazır gelir**. Sen yalnız skoru yazarsın:

```csharp
using VortexArena.Core.UI;
using VortexArena.Protocol;

public class BenimHudum : ModeHudBase
{
    protected override string ScoreLine(MatchStateMsg msg)
        => Skor(msg.scoreRed, msg.scoreBlue);

    protected override string WinnerLine(MatchEndMsg msg)
        => msg.winnerTeam == "red" ? "KIRMIZI KAZANDI"
         : msg.winnerTeam == "blue" ? "MAVİ KAZANDI" : "BERABERE";

    // İsteğe bağlı — maç sonu skoru match_end'den gelir (match_state DEĞİL).
    protected override string EndScoreLine(MatchEndMsg msg)
        => Skor(msg.scoreRed, msg.scoreBlue);

    // İsteğe bağlı — bireysel skorlu modlarda sıralama tablosu buradan beslenir.
    protected override void OnLobbyStateApplied(LobbyStateMsg msg) { }

    private static string Skor(int kirmizi, int mavi) => $"KIRMIZI {kirmizi} — {mavi} MAVİ";
}
```

Prefabı `Assets/Modes/<Mod>/UI/` altına koy, `ModeDefinition.hudPrefab`'a bağla. Sahneye elle
koymana gerek yok — `ModeHudSpawner` maç başlayınca örnekler (yalnız player rolünde).

> ⚠️ Taban sınıfın `OnEnable`/`OnDisable`/`Start`/`Update`'ini override edersen **`base.` çağır**,
> yoksa kill-feed ve can bağlantısı ölür.

---

## 13. Yeni mod eklemek

Üç yerde iş var — ama toplamda ~100 satır.

**Sunucu** (`Server/VortexArena.Server.Core/Modes/<Ad>Mode.cs`):

```csharp
public sealed class BenimModum : IGameMode
{
    public string ModeId => "benim";
    public ModeRules Rules => new() { Teams = TeamMode.None, Scoring = ScoreKind.Player };
    public int DefaultRoundSeconds => 300;
    public int DefaultScoreLimit => 20;

    public void OnMatchStart(MatchDirector d) { }

    public void OnKill(MatchDirector d, int killerId, int victimId, string weaponId)
    {
        if (killerId > 0 && killerId != victimId) d.AddPlayerScore(killerId, 1);
    }

    public bool IsMatchOver(MatchDirector d, out MatchOutcome outcome) { /* ... */ }
}
```
+ `MatchDirector.RegisterModes()` içine `Register(new BenimModum());`

**Unity** — `Assets/Modes/<Ad>/`:
`Scripts/VortexArena.Modes.<Ad>.asmdef` (refs: Core, Net, Protocol) + `ModeHudBase` alt sınıfı +
`UI/<Ad>Hud.prefab` + `Data/<Ad>.asset` (`ModeDefinition`).

**Katalog:** `_Shared/Data/Resources/GameCatalog.asset` → `modes[]`'e ekle, oynanacak
`MapDefinition`'ların `supportedModeIds`'ine yeni `modeId`'yi yaz, sonra
**`Tools > VortexArena > Server > Export Server Config`** çalıştır.

> ⚠️ Export'u unutursan `start_match` "harita bu modu desteklemiyor" diye **sessizce** reddedilir;
> sebep yalnızca sunucu konsolunda tek satır olarak görünür.

> ⚠️ asmdef üretirken mevcut moddan **JSON'u kopyala, `.meta`'yı KOPYALAMA** — GUID çakışır.

Ayrıntı: `ModeRules` alanlarının tamamı → [Sistem Özeti §3.9](../Sistem-Ozeti.md).

---

## 14. Yeni arena eklemek

Tek düğmeli bir sihirbaz **yoktur** (kaldırıldı). Akış altı adımdır ve her adımın kendi aracı var:

| # | Yaptığın | Araç |
|---|---|---|
| 1 | Boş sahne aç, arena kutusuna kaydet (`Venues/<İşletme>/Scenes/<SahneAdı>/<SahneAdı>.unity`) | `File > New Scene` |
| 2 | Ağ altyapısını koy | `Tools > VortexArena > Arena > Template Temellerini Yükle` |
| 3 | Mekanın ölçü maketini + kalibrasyon işaretçilerini üret | `… > Arena > JSON'dan DimensionMesh Üret` |
| 4 | Ölçü yanlışsa köşeleri düzelt, dosyaya geri yaz | ProBuilder + `… > Arena > DimensionMesh'i JSON'a Çevir` |
| 5 | Environment sanatı (**dünya orijinine**, zemin y=0), bake | elle |
| 6 | Tüm kayıtları yap | `… > Build > Configure All Build Elements` |

**2. adım** altyapıyı prefab **ÖRNEĞİ** olarak koyar (`VA_ArenaBoundary` = `ArenaBoundary`,
`VA_CameraRig`, `VA_PoseSync`, `VA_CalibrationManager`, seçime bağlı
`VA_ModeHud` · taban bölgeleri), sahneye bakan referansları bağlar
(`ArenaCalibrator`'ın `rigRoot`'u ile `ArenaBoundary`'nin
`head`/`fadeRenderer`/`warningText`'i — sonuncular rig'in içindedir ve **boş kalırsa muhafaza
sessizce hiçbir şey göstermez**), taban şeritlerini takım rengine boyar ve mekanın boyut dosyasını
`ArenaBoundary.dimensionsJson`'a takar. İdempotenttir: var olanı atlar, ikinci kopya koymaz ve
**dolu bir alanın üstüne yazmaz** — elle bağladığın referans korunur.
⚠️ Kalibrasyon işaretçisi **koymaz**: onlar 3. adımda gelir.

**3. adımın sırası serbest ama kendisi ZORUNLUDUR** — sahnenin `anchor_a`/`anchor_b`
işaretçileri maketle gelir, maketsiz sahne kalibre edilemez. Maket sahnedeki `ArenaBoundary`'nin
**altına**, yerel konum/dönüş sıfırda kurulur (muhafaza yoksa sahne köküne, dünya orijininde ve
dönüşsüz): arenayı yerleştirmek için yalnız `VA_ArenaBoundary` örneğini taşırsın/döndürürsün, maket
ve işaretçiler onu izler. Geri okuma maketin kendi kökünü referans aldığı için bundan etkilenmez.
⚠️ **Ölçeğini değiştirme** — plan metre cinsindendir.

⚠️ **Maket oynanan geometri DEĞİLDİR:** taban + kolonlar + kalibrasyon işaretçilerinden ibarettir
ve **duvar üretmez**. Build'e yalnız kök + kalibrasyon işaretçileri girer (onlar çalışma anında
gerekir); taban/kolon görselini build kancası ayıklar, editör Play kipinde ise `Awake` gizler —
yani oyuncu maketten yalnız işaretçileri, onları da yalnız kalibrasyon sürerken görür
([17.4](#174-maket-build-ayrımı)). Arena sanatı hazır environment'ların içine
kurulur; maket yalnız o sanatın oturacağı fiziksel alanı gösterir
([Reçete 17](#17-arena-ölçüsü-boyut-dosyası)).

⚠️ **Ölçekleme yoktur ve eklenmez.** Her işletmenin alanı farklı ölçüde ve çoğu kare/dikdörtgen
bile değil — orantılı ölçekleme elle düzeltilecek bir yalancı-doğru üretir.

**1. adımda klasör adı sahne adıyla AYNI yazılır** ve MapDefinition da o kutuya aynı adla girer
(`Data/<SahneAdı>.asset`). Sahne adı zaten katalog anahtarı olduğu için klasöre bakan anahtarı
görür; araç bu üçünü karşılaştırır ve uyuşmayan kutuyu uyarı olarak bildirir.

**6. adım** (**Hepsini Yapılandır**, sahne açıkken) `MapDefinition`'ı yazar, sonra kayıtları
`Venues/*/Scenes/*/` ağacına göre **eşitler**: `GameCatalog.maps`, haritayı destekleyen her modun
**dolu** `maps` listesi, Build Settings ve `maps.json`. Ağaçta karşılığı olmayan satırlar
(silinmiş/taşınmış arena, `Missing` referans) **silinir**; kutuda eksik olan şey (sahne yok, birden
çok sahne var, ad uyuşmuyor, MapDefinition yok ya da yanlış yerde) **uyarı** olur. `Boot.unity`
index 0'da kalır, `_Shared/Scenes/*` gibi mekan-dışı sahnelere ve `Template/`'e dokunulmaz.
Sonunda sağlık raporu basar: `ArenaBoundary` var mı · `dimensionsJson` dolu mu · muhafaza dünya
orijinine yakın mı · ölçü maketi `EditorOnly` etiketli mi (etiketliyse build'e girmez ve
kalibrasyon işaretçileri onunla birlikte silinir).

⚠️ **MapDefinition kendiliğinden üretilmez** — `supportedModeIds` boş bırakmak "kısıtsız" demek
olduğu için üretilen boş bir tanım lobiyi sessizce her modda oynanır kılardı. Sahneyi aç, modları
araç penceresinden seç.

⚠️ **Arena sildiysen/taşıdıysan aynı pencereden `Yalnız Senkronize Et`** — sahne açık olmadan da
koşar ve kalıntı kayıtları temizler; kayıtlar elle düzenlenmez.

> Arena ölçüsü **sunucuya gitmez** (maps.json'a yalnız `sceneName` + `modes` yazılır); arenanın
> tek ölçü kaynağı **boyut dosyasıdır**. Export'u ise ölçü için değil,
> **yeni `sceneName` tabloya girsin** diye çalıştırıyorsun — 6. adım atlanırsa `start_match`
> sessizce reddedilir.

Sahnede bulunması gerekenler → [Sahne Kurulumu](Sahne-Kurulumu.md).

> **Başlangıç noktası nedir, ne değildir?** Maçtan önce operatörün oyuncuyu yönlendirdiği fiziksel
> yer. Takımı ve slotu yoktur, arena başına bir tanedir ve oyuncuyu oraya taşıyan bir mekanizma
> yoktur (free-roam). Ölünce dönülecek yer de bu değil, **taban bölgesi**dir (`BaseZone` —
> kırmızı/mavi şerit).
> Ama **arena uzayının sıfırı odur**: ağa giden/gelen tüm pozlar bu transforma göre çevrilir →
> **zemin seviyesine** koy ve yerleştirdikten sonra taşıma (taşımak herkesin koordinatını kaydırır).

Boyut dosyasının biçimi, elle yazma ve yeniden üretme →
[Reçete 17](#17-arena-ölçüsü-boyut-dosyası).

> ⚠️ **Sahne adı = katalog anahtarıdır.** `load_match` bu string'i taşır ve Build Settings'teki
> adla boşluk/harf farkı dahil birebir eşleşmelidir. Sonradan değiştirme.

---

## 14.1 Hazır bir environment'ın içinde arena bölgesi kurmak

Satın alınan/hazır bir sahnenin (kasaba, hangar, istasyon) **bir bölgesinde** oynatmak istiyorsun.
Fiziksel oda değişmedi: ölçü aynı, kalibrasyon bantları aynı yerde.

| # | Yaptığın |
|---|---|
| 1 | Environment'ı **import edildiği yerde bırak** — orijini bozuk olabilir, önemsiz; hiçbir şeyini taşıma |
| 2 | [Reçete 14](#14-yeni-arena-eklemek)'teki normal altı adımı uygula (altyapı → maket → ölçü düzeltme → sanat → kayıtlar) |
| 3 | `VA_ArenaBoundary` örneğini oynatmak istediğin bölgenin üstüne **taşı ve döndür** (ölçek **1** kalır). Maket ve `anchor_a`/`anchor_b` onun altındadır, birlikte gelirler |
| 4 | `BaseZone`'ları, silahları (`WPN_*` örnekleri / `VA_WeaponCanvas`) ve `ArenaObstacle`'ları **elle o bölgeye** yerleştir |

- **Boyut dosyası mekanın AYNI dosyasıdır** (`Venues/<İşletme>/Data/<İşletme>_dimensions.json`):
  fiziksel oda değişmedi, ikinci bir ölçü dosyası açılmaz.
- **Birden çok bölge oynatacaksan her bölge ayrı bir arena kutusudur** (kendi sahnesi + kendi
  `MapDefinition`'ı). Aynı sahnede iki muhafaza olmaz — hangi ölçünün geçerli olduğu belirsizleşir.
- Kalibresiz açılışta oyuncu **o sahnenin A-B ortasında** başlar, yani taşıdığın bölgenin içinde
  ([API: ArenaCalibrator](API-Referansi.md#arenacalibrator--kalibresiz-ön-hizalama)).

> **Neden böyle?** Taşınan tek obje muhafaza olduğu için "arena nerede" sorusunun tek bir cevabı
> kalır: ölçü kutusu, kalibrasyon işaretçileri ve muhafaza mesafesi hep aynı transformdan türer.
> Environment'ı arenaya taşımak ise tersi olurdu — hazır sahnelerin içinde LOD, ışık probu, bake
> edilmiş aydınlatma ve navigasyon verisi kendi konumlarına bağlıdır.
> Ağ koordinatları **dünya uzayındadır** ve muhafaza onların sıfırı DEĞİLDİR
> ([Reçete 16](#16-bir-konumu-ağ-üzerinden-paylaşmak-arena-uzayı)); bölgeyi kaydırmak kimsenin
> koordinatını bozmaz, çünkü oyuncu da admin de aynı sahneyi yükler.

---

## 15. Gözlüksüz test (dev penceresi)

`Tools > VortexArena > Development > Dev` penceresi (kısayol **Ctrl+Alt+R** rolü player↔admin çevirir):

| Düğme | Ne yapar |
|---|---|
| **Rol** | player / admin — sahne kirletmeden, `EditorPrefs`'te kişisel kalır |
| **Sunucusuz sandbox** | Sunucuya hiç bağlanmadan Play; silahlar loadout'tan sırayla ele gelir — aşağı bak |
| **Hedef** | Sunucu adresi (`dev-targets.json`'dan gelir: Local, Keşif, örnek PC) |
| **Play başlangıcı** | Boot'tan mı, açık sahneden mi |

Pencerede maç parametresi yoktur: mod / takım / süre / limit **yalnız sunucudan** gelir, yani maçı
bir **admin** başlatmalıdır. Kurallar telde gelmezse (`rules == null`) `ModeDefinition`'daki
önizleme alanları fallback olarak devreye girer.

### Sunucusuz sandbox — silah/namlu/ses denemenin kısa yolu

Silah duruşu, namlu alevi, kavrama soketi, ses gibi **tümüyle yerel** şeyleri denerken sunucu
açmak, admin'den harita seçmek ve elle kalibrasyon almak gerekmez:

1. Test edeceğin arena (ya da mekan lobisi) sahnesini aç.
2. Dev penceresi → **Sunucusuz sandbox** işaretle (başlangıcı otomatik "Açık sahneden" yapar) ve
   **mod**'u seç — silahlar o modun `loadout`'undan gelir.
3. Play. Grip'e bas: silah elde. **Bırakıp tekrar bas: loadout'un bir sonraki silahı.**

Böylece bütün silahları tek turda gözden geçirebilirsin (duruş, namlu, ses, kovan). Sıra
loadout sırasıdır ve başa sarar; rastgelelik burada bilerek kapalıdır — üretimdeki
`RandomGrant` davranışı DEĞİŞMEZ, bayrak (`WeaponGranter.SequentialGrant`) yalnız editörde
vardır ve yalnız sandbox yazar.

Silahların gelip ateş edebilmesinin sebebi: sunucuya hiç bağlanılmadığı için kalibrasyon kapısı
zaten açıktır (`CalibrationState.IsCalibrated` = `!_hasEverConnected`) ve `ArenaCombat` UDP
kanalı yokken sessiz no-op'tur; kapalı kalan iki kapıyı `DevSession` tek `ModeRuntime.Apply`
çağrısıyla açar — `modeId` (**silah loadout'u buradan okunur**, onsuz silah gelmez) ve
`fireWhilePaused` (faz sunucusuz `paused` kaldığı için tetiği açan tek şey).

> Çerçeve (`WeaponFrame`) yolu sandbox'ta kullanılmaz: amaç silahı uzaktan seçmek değil, hemen
> ele almak. Zaten **ele alınan her silahta çerçeve kapanır** — bu sandbox'a özel değil, genel
> kuraldır (bkz. bu dosyada çerçeve bölümü).

> ⚠️ **Sandbox bir maç DEĞİLDİR:** hasar, skor, faz, canlanma yoktur (üçünün de otoritesi
> sunucudadır) ve takım/skor/canlanma kuralları `ModeRulesInfo` varsayılanında kalır. Maç
> kuralı davranışı test edilecekse sunucu + admin yolu kullanılır.

> ⚠️ Yalnız **"Açık sahneden"** başlangıcıyla ve **kabuk dışı** bir sahnede çalışır: Boot/Lobby'de
> akışı kabuk controller'ı sürer ve sunucuya bağlanmayı dener. İkisi de sağlanmazsa sandbox
> sessizce atlanmaz — konsola uyarı düşer.

> ⚠️ **Sunucu editörden yönetilmez** — dev penceresinin sunucuyla hiç işi yoktur (başlatmaz,
> durdurmaz, derlemez). Sunucu her zaman elle derlenir, elle çalıştırılır ve elle kapatılır.

> ⚠️ **Sapmada sunucu kazanır.** `ModeDefinition`'daki kural alanları yalnız önizleme içindir;
> gerçek bir `load_match` geldiği anda ezilirler.

---

## 16. Bir konumu ağ üzerinden paylaşmak (arena uzayı)

Her oyuncunun fiziksel odası farklı yerdedir. Ağda dolaşan **her** konum bu yüzden *arena
uzayında* taşınır: arena uzayı **sahnenin dünya uzayıdır** (origin dünya (0,0,0)) ve her başlık
`ArenaCalibrator` ile bu ortak çerçeveye hizalanır.

```csharp
using VortexArena.Core.Arena;

// GÖNDERİRKEN: dünya → arena
Vector3 arenaPos = ArenaSpace.WorldToArena(transform.position);

// ALIRKEN: arena → dünya
Vector3 dunyaPos = ArenaSpace.ArenaToWorld(gelenPoz);
```

`Pose` ve `Quaternion` aşırı yüklemeleri de var.

> ⚠️ **YÖN BİR NOKTA DEĞİLDİR.** Yön vektörünü `WorldToArena`'dan geçirme, kendi kapısı var:
> ```csharp
> Vector3 arenaDir = ArenaSpace.WorldToArenaDirection(dir);
> ```
> Sonuç **normalize** edilir (protokol her olayda bir birim yön taşır) ve sıfır/NaN girdide
> `Vector3.forward` döner. `ArenaCombat.ReportShot` bunu zaten doğru yapar — kendi mesajını
> yazmıyorsan hiç düşünme.

> ⚠️ **Arena uzayı dünya uzayıyla çakışıktır, ama çağrıyı yine de `ArenaSpace`'ten geçir:**
> koordinat çerçevesi tek yerde tanımlı kalsın. Bunun bedeli bir sahne kuralıdır — arena
> geometrisi **dünya orijinine göre** kurulur (zemin dünya y=0'da); sahnenin tamamını kaydırmak
> ya da döndürmek arenadaki bütün oyuncuların ağ koordinatını kaydırır.

---

## 17. Arena ölçüsü: boyut dosyası

Ölçü bir **boyut dosyasına** yazılır (`ArenaDimensions` — elle düzenlenebilir JSON) ve dosya
**MEKAN başınadır**: `Venues/<İşletme>/Data/<İşletme>_dimensions.json`, ör.
`Arenas/Venues/VortexAntep/Data/VortexAntep_dimensions.json`. Bir işletmede hep aynı fiziksel alan
oynatıldığı için o mekanın **tüm** sahneleri (arenalar + lobi) `ArenaBoundary.dimensionsJson`
alanında **aynı** dosyayı gösterir — sahne başına kopya kaçınılmaz olarak sapar. İçerik **çalışma
anında** okunur.

**Aynı dosya ölçü maketini üretir, muhafazayı besler, admin kuş bakışı kadrajını verir ve
kalibrasyon işaretçilerini yerleştirir** — ölçüyü ikinci bir yere yazma.

⚠️ **Taban da kolon da TEK sıralı köşe halkasıdır; parçalardan birleştirme (union) YOKTUR.**
İçbükeylik için ek bir şey gerekmez — L şekli, yamuk, girintili duvar tek halkayla ifade edilir ve
ProBuilder içbükey çokgeni sorunsuz üçgenler. Aynı sebeple "dikdörtgense şu hızlı yol" ayrımı ve
ona ait bileşen alanları da yoktur: aynı ölçünün iki ayrı ifadesi kaçınılmaz olarak birbirinden
sapıyordu.

⚠️ **Boyut dosyası zorunludur.** Bağlı değilse ya da okunamıyorsa `ArenaBoundary` bir kez hata
basıp **kendini kapatır** — alan-dışı karartması ve uyarı çalışmaz. Bu bilinçli bir seçim: ölçüsü
bilinmeyen bir arenada doğru bir muhafaza zaten üretilemez, her karede ekranı karartmak ise
işletmede oyunu tümden oynanamaz kılardı. Yeni bir arena sahnesini ilk açtığında konsolu oku.

### 17.1 Dosyayı yazmak

```json
{
  "name": "VortexAntep",
  "plane": [
    { "x": 0.00, "y": 0.00 },
    { "x": 8.32, "y": 0.00 },
    { "x": 8.32, "y": 13.23 },
    { "x": 0.46, "y": 13.12 }
  ],
  "columns": [
    {
      "name": "Kolon_Orta",
      "height": 0,
      "points": [
        { "x": 3.27, "y": 7.19 },
        { "x": 3.94, "y": 7.19 },
        { "x": 3.94, "y": 7.57 },
        { "x": 3.27, "y": 7.57 }
      ]
    }
  ],
  "calibration": {
    "a": { "x": 3.17, "y": 1.82 },
    "b": { "x": 3.17, "y": 7.19 }
  },
  "defaultColumnHeight": 3.0
}
```

| Alan | Anlamı |
|---|---|
| `name` | Yalnız etiket (üretilen objelerin adlandırmasında görünür) |
| `plane` | Tabanın sıralı köşeleri, **metre**. Halka **kapalıdır** — ilk noktayı sona tekrar yazma. Koordinatlar `ArenaBoundary`'yi taşıyan transformun **yerel XZ**'sidir: JSON'daki `y` = dünya **Z**'si |
| `columns[]` | `name` + `height` (0 = `defaultColumnHeight`) + `points` = kolonun kendi sıralı köşe halkası (tabanla aynı uzay, aynı kurallar) |
| `calibration` | Zemin bandındaki **A** ve **B** işaretlerinin yeri (aynı uzay). Maketin `anchor_a`/`anchor_b` küpleri buradan konumlanır — küpün merkezi noktanın kendisidir, yarısı zeminin altında kalır |
| `defaultColumnHeight` | `height: 0` bırakılan kolonların yüksekliği |
| `topViewHeight` | Admin kuş bakışı kamerasının zeminden yüksekliği (opsiyonel; 0 = kameranın varsayılanı). Kamera ortografik olduğu için **kadrajı değiştirmez** — yalnız çatının/yüksek objelerin üstünde kalmasını sağlar |

> ⚠️ **Sıra A → B'dir ve geometrik olarak doğrulanamaz** (iki nokta hangisinin önce alındığını
> söylemez, mesafe kontrolü simetriktir). Garanti prosedüreldir: başlıkta ilk yakalanan nokta A
> sayılır ve o anda `anchor_a` işaretçisi yanar. Karıştırılırsa arena **180° ters döner** — zemin
> bandını okunur biçimde etiketle.

> ⚠️ İki nokta arasında **en az 0,5 m** olmalı (`ArenaDimensions.MinCalibrationSpan`); daha yakın
> bir çift yön tanımlamaz ve yok sayılır. Pratikte alabildiğin kadar uzun tut: yaw hatası mesafeyle
> ters orantılı büyür.

> ⚠️ **Kolondaki `{"points": […]}` sarmalayıcısı zorunludur, süs değil:** `JsonUtility` iç içe dizi
> (`Vector2[][]`) serialize etmiyor. Karşılığında `name`/`height` bedava geliyor — paralel
> dizilerde tutulsalardı indeksleri elle hizada tutulan, sessizce kayabilen bir yapı olurdu.
> `plane` tek halka olduğu için ona sarmalayıcı gerekmez.

> ⚠️ **`wallHeight` alanı YOKTUR.** Duvar üretimi de muhafazanın yarı saydam duvar göstergesi de
> kaldırıldı; okuyanı olmayan bir ölçü bayatlar. Arenanın duvarları environment sanatına aittir.

> **Yazmadığın alan varsayılanında kalır** (`JsonUtility.FromJsonOverwrite`): yalnız `plane`
> yazıp gerisini atlayabilirsin. Bozuk dosya **exception atmaz** — sahne yüklenmeye devam eder,
> ama muhafaza kapanır ve konsola sebebini yazar.

> ⚠️ **Dosyayı alana bağlamayı unutma — bağlanmayan dosya build'e GİRMEZ.** İçerik çalışma anında
> okunur ve Unity bir `TextAsset`'i yalnız referanslandığı için paketler.

### 17.2 Adım adım

1. **Dosyayı oluştur** → **mekanın** `Data/` klasörüne koy (`<İşletme>_dimensions.json`). Dosya
   fiziksel odayı tarif eder, tek bir arenayı değil: mekanın bütün arenaları ve lobisi onu
   birlikte kullanır.
2. **Tabanın köşelerini gir** (`plane`): alanın çevresini dolaş, her köşeyi sırayla yaz — **metre**.
   Halka **kapalıdır**, ilk noktayı sona tekrar yazma. Koordinatlar `ArenaBoundary`'yi taşıyan
   transformun **yerel XZ**'sindedir (X = sağ, Y alanı = Z = ileri); ölçüyü bir köşeden alıyorsan
   o köşe (0,0) olur. Girintili/çıkıntılı duvar sorun değil — içbükey halka olduğu gibi çalışır.
3. **Kolonları gir** (`columns`): her biri ad + yükseklik (0 bırakılırsa `defaultColumnHeight`) +
   kendi köşe halkası (`points`). Eğik duran bir paye de köşeleriyle yazılır — dönüş açısı diye bir
   alan yoktur, gerek de yoktur. Kolonlar **her zaman** muhafaza hesabına girer.
3b. **Kalibrasyon noktalarını gir** (`calibration.a` / `.b`): zemine yapıştıracağın A ve B
   bantlarının yeri. Bunlar da mekan başınadır — aynı odadaki tüm arenalar ve lobi aynı iki
   fiziksel işareti kullanır. Maketin küplerini **elle taşıma**, ölçü buraya yazılır.
4. **Maketi üret:** `Tools > VortexArena > Arena > JSON'dan DimensionMesh Üret` → dosyayı seç, **Üret**.
   `<Mekan>_DimensionMesh` sahnedeki **`ArenaBoundary`'nin altına, yerel sıfırda** kurulur
   (muhafaza yoksa sahne köküne, dünya orijininde ve dönüşsüz): `Plane`
   (ProBuilder çokgeni) + `Columns/<ad>` (prizmalar) + **sahnenin kalibrasyon işaretçileri**
   `anchor_a` (kırmızı küp) / `anchor_b` (mavi küp). Dosyada 12×12 yazıyorsa sahnede de 12×12
   ölçersin — araç ürettiği ölçüyü ayrıca konsola basar. Araç **idempotenttir**: dosya değişince
   yeniden çalıştır, aynı mekanın eski maketi silinip yenisi kurulur.

   > **Arenayı yerleştirmek = `VA_ArenaBoundary` örneğini taşımak/döndürmek** — maket ve
   > işaretçiler onun altındadır, birlikte gelirler; geri okuma maketin KENDİ kökünü referans
   > aldığı için taşınmış/döndürülmüş maket de doğru çevrilir
   > ([14.1](#141-hazır-bir-environmentın-içinde-arena-bölgesi-kurmak)).
   > ⚠️ Ama **ölçeğini değiştirme**: plan metre cinsindendir, ölçek onu sessizce yalan yapar.
   >
   > ⚠️ *Ölçüyü seçim kutusundan okuma:* Inspector, seçim kutusu ve ProBuilder ölçü göstergesi hep
   > **dünya eksenine hizalı** kutuyu gösterir. Döndürülmüş bir kökün altında kusursuz bir 12×12
   > kare `12 × (cos θ + sin θ)` okunur — 48,72°'de **16,93**, ve araç ölçeği bozuyor sanılır.
   > Ölçünün okunacağı yer dosyadır; maketin kendi yerel uzayında değer birebirdir.
5. **Muhafazaya bağla:** dosyayı `ArenaBoundary.dimensionsJson` alanına.
   (`Template Temellerini Yükle` bunu mekan klasöründen çözüp kendisi bağlar; elle kurduysan
   kontrol et.) İşaretçileri `ArenaCalibrator` her `Start`'ta dosyadaki noktalara yeniden oturtur,
   yani otorite her hâlükârda dosyadadır.

> ⚠️ **Build'e maketin yalnız kökü ve kalibrasyon işaretçileri girer** (görsel dal ayıklanır →
> [17.4](#174-maket-build-ayrımı)). Oyuncunun gördüğü zemin/duvar environment sanatından gelir;
> maket yalnız o sanatın oturacağı fiziksel alanı gösterir.

> ⚠️ **`ArenaObstacle` collider DEĞİLDİR** — fizik yapmaz, hiçbir şeyi durdurmaz. Free-roam'da
> oyuncuyu durduran şey gerçek nesnedir; bileşenin tek işi muhafazanın o engele yaklaşırken
> uyarmasıdır. Dosyada yazmayan, sahneye elle konan kasa/direk için de aynısı geçerlidir: objeye
> ekle, `size` alanına zemindeki ölçüsünü yaz.

> ⚠️ **Plan sıfırı ile arena sıfırı ayrı şeylerdir.** Plan koordinatları `ArenaBoundary`
> transformunun yerelidir; ağ koordinatlarının sıfırı ise sahnenin **dünya orijinidir**. Duvarı büyütmek ya da
> kaydırmak ağ uzayını bozmaz — bu ayrım bilinçlidir.

### 17.3 Ölçü yanlışsa: maketi düzeltip dosyaya geri yazmak

Şeritmetre yanılır. Sahada maketin gerçek duvarla örtüşmediğini gördüğünde sayıları dosyada
kovalamak yerine **maketi düzelt**:

1. `Plane` (ya da bir `Columns/<ad>`) objesini seç, ProBuilder'ın **Vertex** kipine geç, kayan
   köşeyi gerçek yerine taşı. Kolonun tamamı yanlış yerdeyse objeyi Move tool ile sürükleyebilirsin
   — pivotu ayak izinin ağırlık merkezindedir ve geri okuma dünya üstünden geçtiği için sürükleme
   de dönüş de doğru yazılır. Kalibrasyon noktası için maketin `anchor_a`/`anchor_b` küpünü
   sürüklemen yeter.
2. `Tools > VortexArena > Arena > DimensionMesh'i JSON'a Çevir`.

Hedef dosya **sorulmaz**: maketin kökündeki işaretçi hangi dosyadan üretildiğini biliyor ve onun
üstüne yazılır.

✔ **Gidiş-dönüş kayıpsızdır:** tek halka → tek mesh → tek halka. Dokunulmamış bir maketi çevirmek
dosyayı (kayan nokta yuvarlamasına kadar) aynı bırakır; tek beklenen fark senin taşıdığın
köşelerdir.

Araç ayak izini şöyle okur:

| Adım | Kural |
|---|---|
| Yüz seçimi | Yatay yüzler (normal'in Y bileşeninin mutlak değeri > 0.9), Y seviyesine göre gruplanıp **en alt** grup |
| Kenar | Yalnız **bir kez** geçen kenar sınırdır; kenarlar köşe indeksiyle değil **konumla** anahtarlanır |
| Sadeleştirme | Bir kenar üstünde duran doğrusal ara köşeler atılır |
| Yükseklik | Mesh'in Y aralığı (kolonlar için) |
| Kalibrasyon | `DimensionAnchor` küplerinin transformu. ⚠️ Küp yoksa dosyadaki `calibration` **korunur**, sıfırlanmaz |

⚠️ **Bir kolonun üst yüzünü alttan farklı düzenlersen kazanan ALT yüzdür** — muhafaza zemindeki
ayak izini önemsiyor.

⚠️ **Kenarların konumla anahtarlanması bir tuzağın karşılığıdır:** ProBuilder sert normaller için
köşeleri yüz başına ayırır; indeksle bakan bir sınır tespiti her yüzün her kenarını "yalnız bir kez
geçmiş" sanar ve tüm mesh'i sınır olarak çıkarır.

> Yazmadan önce sonuç geri ayrıştırılır; doğrulanamazsa dosyaya **hiç dokunulmaz**. Bozuk bir yazım
> o mekanın bütün sahnelerini ölçüsüz bırakırdı.

### 17.4 Maket build ayrımı

Maketin iki dalı iki ayrı muameleye tabidir ve bunlar **birbirinin yedeği değildir**:

| Bağlam | Kök + `anchor_a`/`anchor_b` | Görsel dal (`Plane` + `Columns`) |
|---|---|---|
| Gerçek build | **Girer** — kalibrasyon onlara bağlı | **Hiç girmez**: `DimensionMeshBuildStripper` (`IProcessSceneWithReport`) build'e giden **geçici sahne kopyasından** siler; sahne dosyan değişmez |
| Editör Play kipi | Sahnede | Sahnede, ama `ArenaDimensionMesh.Awake` `Renderer.enabled`'ı false yapar |

⚠️ **Ayıklamanın gerekçesi boyut değil bağımlılıktır:** taban ve kolonlar `ProBuilderMesh` taşır,
o da `Unity.ProBuilder` runtime derlemesini build'e sokardı — bu projede ProBuilder yalnız editör
tarafıdır. Aynı sebeple maket **`EditorOnly` etiketlenmez**: etiket kalibrasyon işaretçilerini de
silerdi.

---

## 18. VR'da tıklanabilir bir dünya-uzayı paneli

Ekran-uzayı arayüzü Quest'te çizilmez; sahnede duran bir panel **world-space canvas**'tır ve
tıklanabilir olması için üç ayrı şeyin birden kurulması gerekir. Örneği `Lobby` sahnesindeki
`LobbyCanvas`'tır (gizli IP paneli oradadır).

**1 · Canvas** — `Render Mode: World Space`, `Event Camera` **boş bırakılır**: VR'da onu ISDK'nın
`PointableCanvasModule`'ü her karede kendi kamerasıyla doldurur, masaüstünde `GraphicRaycaster`
`Camera.main`'e düşer. Canvas'ta `GraphicRaycaster` bulunmalıdır (`Blocking Objects: None` —
yoksa panelin kendi collider'ı grafik raycast'ini keser).

**2 · İşaretçi köprüsü** — canvas objesine `PointableCanvas` (alanı: canvas'ın kendisi). Olayı ona
taşıyan interactable'lar ayrıdır ve **ikisi de gerekir**:

| Ne için | Bileşenler | `_pointableElement` |
|---|---|---|
| Kumanda ışını | `RayInteractable` + `ColliderSurface` + `BoxCollider` (canvas rect'i kadar) | `PointableCanvas` |
| Parmakla dokunma | `PokeInteractable` + `ClippedPlaneSurface` (← `PlaneSurface` + `BoundsClipper`) | `PointableCanvas` |

`PlaneSurface.Facing` **kullanıcının geldiği tarafı** gösterir (`Backward` = transformun -Z'si);
`BoundsClipper.Size` canvas'ın **yerel** ölçüsüdür (piksel — ör. 1000×700×10), dünya metresi değil.

**3 · EventSystem** — sahnede bir `EventSystem` + üstünde iki modül (`PointableCanvasModule` ve
`InputSystemUIInputModule`) + `InputModuleAutoSwitch`. Modüllerden **yalnız biri** etkin kalır,
seçimi XR aygıtının etkin olup olmadığı yapar. Yeni sahneye bunu kopyalarken **iki modülü de bırak**:
biri eksikse o sahne ya Quest'te ya editörde tıklanmaz olur.

⚠️ **Canvas altındaki her RectTransform'un Pos Z'si 0'dır.** Düzlemden sapmış bir öge çizilmeye
devam eder ama **hiçbir işaretçi ona ulaşamaz** — belirti "panel açılıyor, tuşları basmıyor"
olur. Gerekçe: `Docs/Sistem-Ozeti.md` §7, "world-space canvas'ta düzlemden sapmış bir çocuk"
ve "ışınla tıklama ile parmakla dokunma ayrı kurulumdur" maddeleri.

---

## 19. İsabet göstergesinin (X) görünümünü değiştirmek

Rakibi vurunca isabet noktasında beliren X'in **hiçbir ayarı kodda değildir**. Hepsi tek bir
asset'te:

**`Assets/_Shared/Data/Resources/HitMarkerStyle.asset`** → Project penceresinde tıkla, Inspector'da
ayarla.

⚠️ Asset `Resources/` altından **çıkarılmaz ve adı değiştirilmez**: `HitMarker` onu sahne referansı
olmadan `Resources.Load` ile bulur. Taşırsan gösterge çalışmayı sürdürür ama koddaki
varsayılanlara döner, yani yaptığın ayarlar sessizce yok sayılır.

| İstediğin | Alan |
|---|---|
| Daha büyük / küçük X | `Size At One Meter` (1 m mesafedeki kenar uzunluğu, m) + `Min/Max Size Meters` |
| Daha saydam / opak | `Color` alanının **alfası** |
| Daha kalın / ince çizgi | `Thickness Of Size` (boyun oranı) |
| Daha uzun / kısa kalsın | `Lifetime Seconds` |
| Nasıl sönsün, nasıl büyüsün | `Alpha Over Life` / `Size Over Life` eğrileri (yatay eksen = ömrün 0→1'i) |
| Dışına koyu çerçeve (okunurluk) | `Outline Color` (alfa 0 → kapalı) + `Outline Thickness Scale` |
| Parlama (glow) | `Line Material` → additive harmanlayan kendi materyalin |
| Görünümün tamamı benim olsun | `Marker Prefab` → aşağı bak |

> **Play kipinde canlı ayarlanır.** Sayılar, renkler ve eğriler her karede asset'ten okunur:
> Play'deyken Inspector'da oynattığın değer bir sonraki isabette (çoğu alanda aynı karede) ekrana
> yansır. ⚠️ `Line Material` ve `Marker Prefab` havuz düğümü kurulurken bağlanır — onların
> değişimi havuz o düğümü yenilediğinde, yani birkaç isabet sonra geçerli olur.

### Görünümün tamamını kendin yapmak (`Marker Prefab`)

Parçacık, shader, animasyon, ışık — X'in yerine ne istersen. Prefabı bağladığın anda çizgi X hiç
çizilmez.

1. `GameObject > 3D Object > Quad` (ya da bir `Particle System`) → istediğin materyali/efekti kur.
2. **1 birim = 1 metre** olacak şekilde kur: ölçek asset'teki boy alanlarından gelir, prefabın
   kendi ölçeği onunla **çarpılır**.
3. Kameraya bakan yüz: prefab kameranın dönüşünü **aynen** alır (ekrana paralel). Unity'nin
   varsayılan Quad'ı bu hâlde kameraya bakar; kendi mesh'in ters duruyorsa çocuğu 180° çevir.
   Dünyada sabit dursun istiyorsan `Face Camera`'yı kapat.
4. Prefab olarak kaydet ve `Marker Prefab` alanına sürükle.

> **Bu yolda renk, saydamlık ve sönme SENİN işindir** — `HitMarker` yalnız yeri, boyu, dönüşü ve
> ömrü yönetir; asset'teki renk/kontur alanları okunmaz. Prefab kendi ömrünü yönetmeye çalışmasın
> (`Destroy`, `Stop Action: Destroy` koyma): örnek **havuzlanır**, her isabette yeniden kullanılır
> ve içindeki parçacık sistemleri baştan oynatılır.

> ⚠️ **İkinci bir gösterge kurma.** Silah kodunda kendi efektini `Instantiate` edersen aynı vuruşta
> iki işaret çizilir; gösterge zaten `ArenaCombat.ReportHit`'in içinden geliyor ve **yalnız vuran
> oyuncu görüyor** (bkz. [1](#1-kendi-silahımı-yazdım-ateşleyince-ne-çağırayım)).
