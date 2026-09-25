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
| Çocuk oyunu eklemek (silahsız, kooperatif) | [13.1](#131-çocuk-oyunu-eklemek-silahsız-kooperatif) |
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

Hasar merkeze uzaklıkla doğrusal düşer ve her oyuncuya **bir** vuruş gider. ⚠️ **Oyuncu gövde
kapsülüyle ölçülür, isabet kutularıyla değil:** arena zemini→kafa dikey kapsülünün yüzeyine olan
uzaklık (merkez içerideyse sıfır); atan da diğerleri de aynı kapsülle puanlanır. Çömelmek hasarı
değiştirmez; kutudan ya da kafadan ölçen bir düşüm ayağının dibindeki bombayı uzak sayardı. Yarıçaptaki **ağ nesneleri** de aynı yoldan raporlanır
(`hit_report{targetNetId}`, aynı mesafe düşümü); dönen sayı yine **yalnız oyuncu** isabetidir.

**Duvar arkası** istemiyorsan kendin kurma — son parametreyi aç:
`ReportAreaHit(…, requireLineOfSight: true)`. Merkezle hedef arasında engel varsa o hedef atlanır.

> ⚠️ **Kendine hasar bu metottan ÇIKMAZ ve bu bir hata değildir.** `ReportAreaHit` hedefleri uzak
> oyuncuların isabet kutularından bulur; **yerel oyuncunun kendi rig'inde hedef collider yoktur**
> (kendi gövdeni görmezsin, §3 "oyuncu kendi gövdesini görmez"). Kendi patlamandan hasar almak
> istiyorsan ikinci çağrı gerekir:
> ```csharp
> ArenaCombat.ReportAreaHit(merkez, 6f, 120f, "bomba", 0.25f);      // ötekiler
> ArenaCombat.ReportAreaSelfHit(merkez, 6f, 120f, "bomba", 0.25f);  // ben
> ```
> `ReportAreaSelfHit` **dost ateşi anahtarını kendisi okur**: kapalıyken hiç rapor yollamaz (sunucu
> zaten reddederdi, boş rapor konsolu kirletir). Aynı kapı takımsız modda da geçerlidir — "kendi
> bombamın hasarını alır mıyım" sorusunun cevabı her modda operatörün anahtarıdır (`Protokol` §10.3
> 5. kapı).

> ⚠️ **Atıcının ölmesi patlamayı iptal ETMEZ.** Elden çıkmış hasar kaynağı sahibi öldükten sonra da
> raporlanır ve skoru normal yazılır — sunucudaki kapı bir **pencere**dir (`Protokol` §10.3 2. kapı).
> Yani havadaki bombanın sayacını "oyuncu öldü" diye durdurma.

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
(Loading/Countdown/End'de ateş yok), bir kez bağlanıldıysa **bağlantı açık mı** ve mod
**silahsız mı** (`weaponSource:"none"` → `ModeRuntime.IsWeaponless`; atılabilir eşya da bu kapıyı
okur).

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
`RespawnDelay`, `FireWhilePaused`, `IsTeamless`, `IsWeaponless`.

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
   `dryFireHapticAmplitude` + `dryFireHapticDuration` — boş şarjörde tetiğe basınca; her çiftte
   ikisinden biri 0 ise o ipucunun haptiği yoktur), `prefab`.
3. **Tek el cezası** (*Tek El Cezası* başlığı): `oneHandSpreadMultiplier` ·
   `oneHandRecoilMultiplier` · `oneHandRecoveryPenalty`. Ölçü **iki elli tutuşa göredir** (1 = ceza
   yok), yani silahın ne kadar ağır/uzun olduğu buraya yazılır: hafif bir SMG 1.5 civarı, uzun bir
   tüfek ya da pompalı 2.0. Bu üç alan **yalnız burada** yaşar: eşitleme aracı onlara dokunmaz, yani
   Inspector'da bulduğun değer kalıcıdır. ⚠️ Saçılım ve geri tepme alanlarına yazdığın sayı **ham**dır — sahadaki
   değer her zaman onun kavrayış çarpanıyla çarpımıdır (bkz. Sistem Özeti, Tuzaklar: "Saçmalının
   mesafe kimliğini saçılım taşır").
4. `Tools > VortexArena > Build > Configure All Build Elements` → **Hepsini Çalıştır**.

`weaponId` yalnızca **kill feed etiketidir** — sunucu doğrulamaz, istediğini yazabilirsin.

> ⚠️ **`ModeDefinition.loadout` ELLE DÜZENLENMEZ** — eşitleme onu `WeaponCatalog`'a göre geri
> yazar. Rastgele silah veren modların havuzu **arsenalin tamamıdır**; mod başına silah kısıtı
> diye bir şey yoktur ve elle kırpılan liste bir sonraki koşuda dolar. Kısıt gerçekten
> gerekirse önce onu taşıyacak alan tasarlanır. `weaponSource:"weaponcanvas"` modlarında
> `loadout` zaten hiç okunmaz — sahnede hangi silahın duracağını arena belirler.

> Silahın elde **nerede** durduğunu `WD_*`'taki kavrama kayıtları belirler (§11.0); dördü de el
> başınadır (`primaryGripRight/Left`, `secondaryGripRight/Left`) ve her biri üç şey taşır:
> **kumanda anchor'ının konumu** (silahın kumandaya göre yeri — **dönüş taşımaz, silah her zaman
> kumandayla hizalıdır**), **el modelinin o kumanda üstündeki yerleşimi** (konum + **dönüş** — el
> silaha göre yan ya da alttan durabilir) ve o silaha özel **riglenmiş parmak duruşu**. ⚠️ İlk ikisi
> bilerek ayrı: eli çevirmek silahı çevirmez. Ön kabzada elin görseli silaha yapışır.
> ⚠️ **Ölçü TEK yerden okunur:** aynı kayıt yerel duruşu, uzak oyuncudaki çizimi ve
> ön kabza kapısının/göstergesinin yerini birlikte besliyor.
> Çerçeveden seçilen silah (`weaponSource:"weaponcanvas"`), modun verdiği silah
> (`weaponSource:"random"`) ve elde ISDK ile kavranan eşya **aynı yolu** kullanır; ayrım yoktur.

> ⚠️ Denge sayıları istemcide yaşadığı için değişiklik **APK build'i ister** — sunucuyu yeniden
> başlatmak yetmez.

> ⚠️ **Silah sesi tabloda DEĞİLDİR ve araç ona dokunmaz.** Ses değiştirmek =
> `Assets/_Shared/Arsenal/Data/WD_<Ad>.asset`'i seç, klibi ilgili alana **sürükle**. Beş yuva var:
> `fireClips` (dizi — her atışta rastgele biri seçilir), `magOutClip` (reload `t=0`),
> `magInClip` (reload `t = 0.70 × reloadTime`; boş bırakılırsa o an sessiz kalır ve `magOutClip`
> tüm reload sesini taşır), `dryFireClip` (boş şarjörde tetik), `pickupClip` (silah alınırken).
> Silah kiti koşusu (`Configure All Build Elements`) klibe dokunmaz — yalnız ateş sesi atanmamış
> silahları koşu sonunda listeler.
> ⚠️ Ateş klipleri `PlayOneShot` ile çalınır, yani **üst üste biner**: aranan dosya her zaman
> **tek atış**tır, tarama/loop kaydı saniyede 12 kez çalınıp çorbaya döner. Kuyruğu kısa tut —
> `pitch` AudioSource'un özelliği olduğu için her yeni atış hâlâ çalan kuyrukları da yeniden
> perdeler.
> Aynı şey diğer alanlar için geçerli DEĞİLDİR: hasar/rpm/menzil/saçılım her koşuda tablodan ezilir.

> Sahnedeki silahın **çerçevesi** için elle iş yoktur: silah kiti koşusu
> (`Tools > VortexArena > Build > Configure All Build Elements` — **Hepsini Çalıştır**; silah kiti
> her koşuda çalışır) her `WPN_*` köküne
> `VA_WeaponFrame` örneğini kendisi koyar. Çerçevenin arenada görünüp görünmemesi ayrı bir konudur
> → bir sonraki reçete.

---

## 11.0 Bir silahın kavramasını YAZMAK

Kavrama **stüdyoda, gözlük takmadan yazılır**: **kumanda çerçevesini** Scene View'da silahın
kabzasına oturtur, **el modelini** o kumandanın üstüne yerleştirir ve elin **parmaklarını o silaha
göre riglersin**; araç üçünü de (`çerçevenin silaha göre konumu` · `elin çerçeveye göre pozu` ·
`parmak eklemlerinin dönüşleri`) `WD_*.asset`'e yazar. Çerçevenin altında iki çocuk durur:
**Quest 3 kumanda modeli** — **kilitli**, oyunda anchor'ın altında tam böyle durur, yani hizanın
referansıdır (çerçeveyi, kumanda kabzada gerçekte tutulduğu yere gelecek biçimde taşı) — ve **ISDK
hayalet eli**, ki o **düzenlenebilir**: taşınır ve **çevrilir**. Prefaba hiçbir şey yazılmaz —
kavramanın tek yeri `WD_*`'tır.

⚠️ **Silahı hiçbir elin dönüşü çevirmez; ÇERÇEVENİN dönüşü kayda girmez.** Çerçeve bu yüzden yalnız
**taşınır** — çevirirsen araç onu silahla hizalı hâline geri alır, çünkü dönüşün oyunda karşılığı
yoktur. **Elin dönüşü bunun istisnası değil, başka bir şeyidir:** el modelini çevirmek silahı
kımıldatmaz, yalnız elin kumanda üstündeki açısını yazar (kimi kabza yandan, kimi alttan tutulur).
Ana elde eşya kumandaya asılır.

⚠️ **Silah ele gelince el modeli SİLAHIN çerçevesine geçer** (iki elde de): tezgâhta yazdığın
yerleşim, tutulduğu sürece silaha göre korunur. Bu yüzden oyuncu silahı ön kabzadan çevirdiğinde
arka el de onunla döner — el kumandaya kilitli kalsaydı silah elin dışına çıkardı. Tek elli tutuşta
fark yoktur (silahın dönüşü zaten kumandanınkidir); elin KONUMU her durumda kumandada kalır.
Ön kabza kaydının iki karşılığı vardır: ikinci elin **görseli** oraya yapışır ve iki elli tutuşta
silahın *ana kavrama → ön kabza* **ekseni o elin avuç KONUMUNA nişanlanır** — silah ikinci ele
doğru kaymaz (ana kavrama noktası ana avuçta kalır) ve o elin DÖNÜŞÜ hiçbir yoldan silaha geçmez.
Ayarlanabilir bir "silah dönüşü" alanı da yoktur.

**Akış (prefab kipinde, Play gerekmez):**

1. `Tools > VortexArena > Items > Kavrama Pozu Stüdyosu` ile pencereyi aç.
2. `_Shared/Arsenal/Prefabs/WPN_*.prefab`'ı **prefab kipinde** aç (Project'te çift tık). Pencere
   stage'i kendiliğinden tanır.
3. **Ana Kabza Ellerini Oluştur** (iki elli silahta ayrıca **Ön Kabza Ellerini Oluştur**) → sağ ve
   sol kumanda çerçeveleri (ve çocukları olan hayalet eller) sahnede belirir. Kayıt zaten varsa
   çerçeveler **o kayıttan** doğar; yoksa kabza parçasının üstüne makul bir başlangıçla konur.
4. Çerçeveleri Scene View'da **taşı** (gizmo'daki mavi ok kumandanın ilerisidir; hayalet elin ya da
   kumanda modelinin mesh'ine tıklamak da çerçeveyi seçer — Scene View'daki tıklama her zaman
   çerçeveye düşer): kumanda kabzada gerçekte tutulduğu gibi dursun.
5. **El modelini o kumandanın üstüne yerleştir.** Penceredeki **El Modeli** düğmesi (ya da
   hiyerarşideki `Hand` objesi) hayalet eli seçer; onu **taşı ve ÇEVİR** — avuç kabzaya otursun,
   işaret parmağı tetiğe ulaşsın. Silah kımıldamaz: bu kayıt yalnız elin nerede/hangi açıda
   çizileceğini söyler ve **silah + el başına** yazılır (ön kabza yandan da alttan da tutulabilir).
   **El Yerleşimini Sıfırla** paylaşılan varsayılana döndürür.
6. **Parmakları o silaha göre rigle.** Eli seç (ya da penceredeki *Kumanda*) → pencerede o elin altında
   **parmak rigi** listesi açılır: parmak başına numaralı düğmeler, `1` bileğe en yakın boğum.
   Düğmeye basmak o kemiği seçer ve Scene View'ı döndürme aracına alır (`Pivot: Local`); kemiği
   çevir, avuç kabzaya otursun, işaret parmağı tetiğe değsin. **Parmakları Sıfırla** boş elin
   duruşuna döndürür. Riglenmemiş el boşta duruşunda kalır — hazır bir "sıkma/kabza" preset'i
   YOKTUR. Oyunda gördüğün el tezgâhtaki elin aynısıdır, hiçbir parmak kumandadan/el izlemesinden
   oynamaz; silahı alınca el boşta duruşundan bu duruşa yumuşakça kapanır, bırakınca geri açılır.
   Elin **yerleşimi** de (5. adımda yazdığın açı/konum) aynı sürede kayarak gelir — silaha bir anda
   geçmez. Geçişin süresi tek bir yerdedir: `HandPoseLibrary.TransitionSeconds`.
7. İstersen **Karşı Ele Aynala** ile öteki eli başlat, sonra elle düzelt.
8. **Kaydet** (stüdyo penceresinden; elin Inspector'ında kaydet düğmesi yoktur) → yaşayan **her el**
   `WD_*.asset`'e yazılır (Undo'lu) ve **silah kiti kendiliğinden eşitlenir** — `Configure All Build
   Elements`'e ayrıca gitmen gerekmez. Kit açık prefabı yeniden yazdığı için
   tezgâhtaki eller kalkabilir: kayıt diskte olduğundan **Elleri Oluştur** onları aynı yere geri
   getirir. **Elleri Temizle** tezgâhı elle toplar.

- ⚠️ **Aynalama yalnız BAŞLANGIÇTIR**, son söz değil: kabza simetrik değildir (tetik, şarjör, kurma
  kolu tek taraftadır) ve kayıt el başınadır. Bir eli hiç yazmazsan oyun onu öteki elin kaydına
  düşürür — çalışır ama o el yanlış tutar.
- ⚠️ **Eller prefabın İÇİNE sürüklenmez.** Her el, stage sahnesinin ayrı bir kök objesidir
  (`[VA El_*]`, diske yazılmaz). Prefabın altına asılan bir el ilk kaydetmede silahın içine girer ve
  arenada **havada el** olarak çizilir; silah kiti koşusu böyle bir kaçağı siler.
- Eller Play'e girerken, prefab kipi kapanınca ya da sahne değişince kendiliğinden silinir. Play
  kipinde kayıt yazılmaz.
- ⚠️ **Parmak duruşu slider'la/sayıyla ayarlanmaz — kemik çevrilir.** Kayda giren şey kemiklerin
  Kaydet anındaki hâlidir; aynı duruşu ikinci kez sayı olarak yazacak bir alan yoktur ve
  eklenmez ("hangisi geçerli" sorusunu doğururdu). Tezgâhta gördüğün parmak duruşu oyundaki
  sentetik elde birebir tekrarlanır.
- ⚠️ **Metakarpallar (avuç içindeki ilk kemikler) listede YOKTUR ve riglenmez.** Bilekle birlikte
  hareket ederler ve sentetik el proksimal eklemleri bilek uzayında beklediği için onları çevirmek
  tezgâhta doğru, oyunda kaymış bir el üretirdi.
- ⚠️ **Aynalama parmakları da el yerleşimini de taşır**: parmaklar kopyalanır (sol/sağ hayalet el
  aynı eklem sözleşmesini paylaşır), el yerleşimi ise gerçekten aynalanır (x tersine döner, dönüş
  Y/Z ekseninde ters işaretlenir). Yine başlangıçtır, kabzanın tek taraflı parçaları elle
  düzeltilir.
- **Aynı el başka bir silahta zaten yazılıysa: Kopya Al.** El satırındaki üçüncü düğme, o elin
  **görselini** — kumanda üstündeki yerleşim + parmak rigi — seçtiğin silahtan aynen alır, yani 5.
  ve 6. adımı tekrar etmen gerekmez. ⚠️ **Silahın kumandaya göre yeri kopyalanmaz** (4. adım her
  silahın kendi geometrisidir; başka silahtan almak silahı elde kaydırırdı). Listede yalnız **aynı
  kavrama noktasının aynı eli** yazılmış silahlar çıkar — sağ el için sol el kaydı kaynak olmaz —
  ve hiç yoksa menü boş açılmaz, kapalı bir satır gösterir. Kaydın iki yarısı da yazılır: kaynağın
  yerleşimi ya da parmakları eksikse o yarı paylaşılan varsayılana döner (menü satırı hangisinin
  eksik olduğunu söyler). Kopya diske inmez — kalıcı olması için **Kaydet**.
- Kavraması yazılmamış silahta el boşta duruşunda kalır + konsola oturum başına bir uyarı gider;
  silah kiti koşusu da sonunda **"kavraması EKSİK silahlar"**ı listeler — ana kabzası yazılmamış
  olanlar VE çift elli olup ön kabzası yazılmamış olanlar (`Configure All Build Elements`
  penceresindeki Hazırlık satırı aynı listeyi gösterir).
- Ön kabza noktası da stüdyoda, ön kabza elinin kumanda çerçevesiyle yazılır — Scene View'da ayrı bir
  tutamak/gizmo YOKTUR (kayıt tek yerde yaşasın). Oyunda boş elin kumandası o noktaya yaklaşınca
  beliren soket küresi (`WeaponCatalog.secondaryGripIndicatorPrefab`, `Weapon` sürer; yarıçapı
  `WD_*`'daki `secondaryGripRadius`, varsayılan 20 cm çap) kaydın oyundaki yerini gösterir ve
  kabul hacminin kendisidir: kumanda kürenin içindeyken grip ikinci eli bağlar. Küre kabzadan uzakta
  çıkıyorsa kayıt o el için yanlış yazılmış demektir. **Ön kabza kaydı hiç yazılmamışsa** küre HİÇ
  çıkmaz ve ikinci el bağlanmaz (`ItemDefinition.HasSecondaryGrip`; konsola tanım başına bir uyarı
  gider) — yazılmamış kayıt eşyanın köküne düşerdi, o da ana elin dibidir.
- ⚠️ Silah elde yatık görünüyorsa tek aday var: `Model`'in prefabtaki yerleşimi. Çerçevenin dönüşü
  kayda girmediği için silah tek elde kumandayla hizalıdır (iki elli tutuşta yalnız ekseni ikinci
  elin avucuna nişanlanır); yatıklık modelin kendi eksenlerinden gelir. **EL** yatık görünüyorsa
  aday başkadır: o slotun el yerleşimi (stüdyoda `Hand`'i çevirerek düzeltilir).
- **Admin ekranı ayrı bir teşhis yeri değildir:** kayıt telde giden el poz uzayında olduğu için
  rig'i olmayan izleyici uzak silahları oyuncuyla birebir aynı çizer. Silah adminde yanlış
  duruyorsa oyuncuda da yanlıştır.
- **Tezgâhtaki el ile oyundaki el AYNIDIR ve bu kurgu gereğidir:** elin bileği oyunda her karede
  kumandaya kilitleniyor ve kilidin ofseti tezgâhtaki hayalet elin **aynı** kaydıdır — kavraması
  yazılmış slotta senin yerleştirdiğin poz, yazılmamışta paylaşılan varsayılan
  (`HandPoseLibrary.AnchorToWrist` — avuç merkezi kumandanın üstünde, dönüş kimlik). Yani Meta'nın
  kumandadan sentezlediği "doğal" el pozu devrede değil; ölçülecek/yapıştırılacak bir sabit de yok.
  ⚠️ Elin kumandaya göre eğimi **koda anatomik bir tahmin olarak yazılmaz** — bir kez denendi ve
  parmak ekseni etrafında ~70° saptı; o eğim gözle, silah başına tezgâhta verilir.

### Başkalarının gördüğü el (uzak avatarın parmakları)

Parmaklar telde gitmez (`Docs/ArenaNet-Protokol.md` §6.9), yani uzak avatarın parmakları
**sentezlenir**: eşya tutan elde **o slot için riglediğin duruş** (`WD_*`'ta yazılı olan), boş elde
`RemoteAvatar.prefab` → `idleHandPose` (boşsa `HandPoseProfile.Idle`). Yani stüdyoda riglediğin
duruş yalnız senin elini değil, herkesin gördüğü eli de sürer.

⚠️ **Uzaktaki el KABACA çizilir ve bu bilinçlidir.** Uzak avatarın eli humanoid (Mixamo) bir
rig'dir; ISDK eklem dönüşleri ona doğrudan yazılamaz (iki iskeletin kemik eksenleri aynı değil).
Köprü, riglediğin duruştan **ölçülen** parmak başına kapanma oranıdır — yani uzakta parmakların
"ne kadar kapalı" olduğu doğrudur, kemik kemik ince ayar değil. İnce ayarın tüketicisi zaten
oyuncunun kendi eli: uzak oyuncunun eli metrelerce öteden görülüyor.

**Genel his** (tüm silahlar birden, uzak elde) eklem başına açı tavanlarından gelir:
`HandFingerRig.FingerMaxAngles` / `ThumbMaxAngles`. Tek bir silah tuhaf duruyorsa bakılacak yer o
silahın **kavrama kaydıdır**, tavanlar değil.

## 11.1 Silah çerçevesinin görünümü

`VA_WeaponFrame` prefabında **çerçeve MODELİ yoktur** — çerçeve görünmeyen bir seçim hacmidir
(kutu collider + iki mesafe-kavrama bileşeni). `WeaponFrame.isFrameVisible` işaretlense de
çizilecek görsel bulunmaz; görsel isteniyorsa önce prefaba bir model kökü eklenip `frameVisual`
alanına bağlanır, model çalışma anında silahın ölçüsüne oturtulur.

> ⚠️ Görsel **yalnız sunumdur.** Çerçeve görünmese de silah yine oradan, `maxGrabDistance`
> mesafesinden nişan alınarak seçilir ve ele klonlanır; görselin yokluğu alma menzilini ya da
> kavramayı kapatmaz.

> ⚠️ **Nişan ışını çerçeveden gelmez.** Oyuncunun gördüğü uzaktan-seçim göstergesi ISDK'nın kendi
> mesafe-kavrama görselidir (tüp + reticle); `WeaponFrame`'in kendi `LineRenderer` ışını
> (`isRayVisible`) **kapalıdır**, ikisi birden açıkken elde iki ışın görünür. Menzil bilgisi
> kaybolmaz: ISDK adaylarını `WeaponFrame.Filter`'dan geçiriyor, menzil dışındaki çerçeve hover
> bile almaz.

> **Çerçeve yalnız silah SABİT dururken vardır.** Silah hangi yoldan tutulursa tutulsun — ele
> verildi (`WeaponGranter`) ya da doğrudan kavrandı (ISDK) — çerçevenin GameObject'i kapanır;
> bırakılınca geri gelir. Yani elde duran silahta uzaktan seçim kapısı olmaz. Elle kurulum
> istemez: `WeaponFrame` silahın `Weapon.HeldChanged` olayını dinler. Yeni bir "silahı ele alma"
> yolu yazarsan o yola ayrıca bir şey eklemene gerek YOKTUR — kural olayda durur.

---

## 11.2 Silah yerde/masada dursun, oyuncu eğilip ELLE alsın

**Ne zaman:** o sahnede silah bir çerçeve kaynağı değil, yerde duran normal bir nesne olsun
istiyorsun (oyuncu yaklaşıp elle kavrasın, uzaktan seçme olmasın).

⚠️ Kapatman gereken şey `WeaponFrame`'in **kendisidir** — çerçeve zaten görünmez olduğu için
(bkz. "Silah çerçevesinin görünümü") görselle uğraşmak silahı yerde bırakmaz:

1. Sahnedeki `WPN_*` örneğini seç → altındaki **`VA_WeaponFrame` çocuğunu** seç.
2. Objenin **aktiflik kutusunu kaldır** (`SetActive(false)`) — bileşeni değil, GameObject'i.
3. Sahneyi kaydet.

Böylece `WeaponFrame.Awake` hiç koşmaz: silah donmaz (`Rigidbody` fizikli kalır), kendi
`Grabbable`/`GrabInteractable`/`HandGrabInteractable`'ı açık kalır → normal
yakın kavrama çalışır (iki kavrama hattı da açık kaldığı için el izleme ayarından bağımsızdır).

⚠️ **Bileşeni `enabled = false` yapma.** Unity kapalı bileşende de `Awake` çağırır → silah yine
donar ve yakın kavrama kapanır; ama `OnEnable` koşmadığı için uzaktan da seçilemez. Sonuç: hiç
alınamayan ölü bir silah.

İki seçeneğin ölçülen farkı (`WPN_M4A1` örneği üstünde):

| | Grabbable / GripSockets | Rigidbody | nasıl alınır |
|---|---|---|---|
| Normal (çerçeve açık) | kapalı | kinematik, yerçekimsiz | yalnız uzaktan, klon olarak |
| `VA_WeaponFrame` GO kapalı | **açık** | fizikli, yerçekimli | yalnız elle, yakından |

---

## 11.3 Atılabilir eşya eklemek (bomba, molotof, flashbang, sis)

⚠️ **Protokole DOKUNULMAZ.** Atma olayı `itemId` taşıdığı için yeni tür telde bedavadır: bir
`netItemId` + bir katalog girdisi. Yeni tür **hiçbir zaman** protokol sürümünü artırmaz, sunucuya da
tek satır iş çıkarmaz (denge sayıları istemcide yaşar — `Protokol` §10.3 sonu).

**1. Tanım.** `Create > VortexArena > Throwable Definition` → `ThrowableDefinition`:
`netItemId` (benzersiz, 1-255), prefab, `holdMode = OneHand`, tetik kipi (**Fuse** = fitil, bomba;
**Impact** = ilk temas, molotof), fitil/dolum süreleri, atış hızı ölçeği + tavanı, patlama
yarıçapı/hasarı/`edgeScale`, `requireLineOfSight`, patlama prefabı + ses klibi.

⚠️ **Patlama prefabının materyalleri Unlit, soft particle'sız, distortion'suz ve LDR olmalıdır** —
başlık ile admin farklı boru hattı ayarlarıyla koşar, üçünden biri kaçarsa efekt VR'da *hata
vermeden* başka bir şey çizer; kural ve gerekçe `Yapma-Listesi.md` "Parçacık materyalinde soft
particle, distortion ya da 1'i aşan renk kullanma", kurulu takım `_Shared/FX/Materials/M_Blast*`.
Sunum havuzludur (`BlastFxPool`): efekt prefabı sahnede kendi ömrünü yönetmez, `Instantiate`/
`Destroy` yazma.

**2. Prefab.** Root'ta `Rigidbody` + collider + **fizik materyali**, `Throwable` bileşeni ve bir
`ThrowableEffect` (bomba için `BlastEffect`). Rigidbody'nin interpolasyon/kinematik/çarpışma
alanlarını prefabda ayarlamaya çalışma: taşınırken kılıf kapatır, uçuşta `Arm` açar
(`Yapma-Listesi` "taşınan Rigidbody").
⚠️ **Sekme katsayısını düşük ama sıfır olmayan tut** (`PM_Bomba`: düşük sekme, `bounceCombine`
**Maximum** — zemin materyalinden bağımsız bir kez seker) — kopyaların ayrışması sekme sayısıyla
büyür (`Sistem-Ozeti` §7 "atılabilir avatarla çarpışmaz"); sıfır sekme ise yere yapışıp yuvarlanan
top verir. Yuvarlanmayı durduran şey materyal değil `Throwable`'ın ilk temasta yükselttiği sönümdür
— prefabda yüksek `angularDamping` YAZMA, uçuştaki fırılı da söndürür.

**3. Kavrama.** `Tools > VortexArena > Items > Kavrama Pozu Stüdyosu` ile **ana kabza** kaydını yaz
(tek elli — ön kabza yok). Yazılmazsa eşya elde idle parmaklarla ve yanlış açıyla durur; uzak
oyuncuların gördüğü de odur.
⚠️ **Önce tanımın `prefab` alanını bağla:** atılabilirin prefabında tanımı taşıyan bir bileşen
yoktur (`Throwable` tanımını çalışma zamanında `Arm`'da alır), stüdyo hedef tanımı **ters aramayla**
bulur — `prefab` alanı boşsa prefab stüdyoda açılır ama yazılacak asset bulunamaz.

**4. Katalog.** `NetItemCatalog`'a ekle → `Configure All Build Elements` (kimlik bekçisi çakışmayı
burada yakalar).

**5. Taşıma.** Bileklikte taşınacaksa rig'deki `WristHolster`'a tanımı bağla. Kılıf sol bilektedir,
sağ el alır; alırken sağ eldeki silah **askıya alınır** (yere düşmez, yeniden seçilmez) ve atıştan
sonra aynı silah geri gelir.

**Yeni bir ETKİ yazmak** (ateş havuzu, flashbang, sis): `ThrowableEffect`'ten türet, `Trigger`'ı
yaz, prefaba ekle. Sunumu pahalı olan bir etki `Prewarm`'ı da ezer — havuz/ısınma bedeli orada, fitil
boyunca ödenir; tetik anı stall'a en kapalı andır. Kurallar:
- **Hasarı yalnız atanın kopyası raporlar** (`source.LocalOwner`) — uzak kopyalar sadece FX oynatır.
- **Hasarsız etkiler hiç `hit_report` üretmez:** flashbang/sis'te her istemci **yalnız kendi
  oyuncusunu** değerlendirir (mesafe + bakış + görüş hattı), sunucu onları hiç görmez.
- ⚠️ **Sis/ateş hacmi raycast'e takılmamalı ve `Obstacle` layer'ında olmamalıdır:** hasar görüş
  hattı, engel ihlali kuralı ve silah namlu kapısı hepsi raycast okur — sis onları tetiklerse
  oyuncular sisin içinde sessizce ölmeye başlar.
- ⚠️ **Süreli etki** (yanan havuz) sunucudaki ölüm sonrası penceresinden uzun yaşayamaz; son tikleri
  sessizce reddedilir (`Protokol` §10.3 2. kapı).

---

## 11.4 Ağ nesnesi eklemek (kırılabilir örtü, hedef tahtası)

Oyuncu olmayan ama **herkeste aynı** olması gereken bir obje (kırılan siper, vurulan tahta) ağ
nesnesidir: canını sunucu tutar, sen yalnız sunumunu yazarsın. Kural
`Docs/ArenaNet-Protokol.md` §10.10.

**1. Tür.** `Create > VortexArena > Net Object Kind` → `kind` (telde taşınan kimlik, projede
benzersiz) + `maxHp` (`0` = hasar almaz; kimliği olan dekoratif nesne meşrudur).

**Tutulabilir olacaksa** aynı asset'te `Grab = Anyone` (varsayılan `None` = alınamaz). **Objeye özel
bir etkileşim** (kesme, doldurma, alma) olacaksa izinli olay listesine (`Events`) birer satır yaz:
`Name` (telde giden ad) + `Policy` (`Anyone` / `Owner` = yalnız objeyi tutan) + `PhaseGate`
(`Playing` / `Any` = lobide de serbest).
⚠️ **Listede olmayan olay sunucuda REDDEDİLİR** — ad telde serbest metindir, sunucuda değildir. Buraya
yazılan bir yazım hatası derleme hatası vermez, "hiç gerçekleşmeyen etkileşim" olarak görünür.
⚠️ **Kural TÜRDE durur, objede değil:** aynı tür on arenada geçer; kuralı objeye kopyalamak on ayrı
doğruluk kaynağı üretir.

**2. Sahnedeki obje.** Objenin köküne `NetObject` ekle (`NetIdentity`'yi kendisi zorunlu kılar) ve
`kind` alanına 1. adımdaki asset'i bağla. ⚠️ **Kimliği elle yazma** — sahne kaydında bake'lenir;
sahneyi kopyalarsan `SceneIdGuard` çakışanları kendisi ayırır.

**3. Hasar collider'ı.** Objenin raycast'e takılan bir collider'ı olmalı; yoksa mermi ona hiç
çarpmaz ve obje "kırılmıyor" görünür. Hitscan silahlar `ArenaCombat.ReportRaycastHit` üzerinden
ağ nesnesini **kendiliğinden** raporlar — silah kodunda yapılacak bir iş yoktur.
⚠️ **Alan hasarı (bomba) da ağ nesnesine geçer:** `ReportAreaHit` yarıçaptaki ağ nesnelerini de
tarar ve her biri için ayrı `hit_report{targetNetId}` yollar; görüş hattı isteyen etkilerde objenin
**kendi collider'ı kendini gölgelemez** (kırılabilir siper çoğu zaman `Obstacle` layer'ındadır).
⚠️ **Siper `Obstacle` layer'ında DEĞİLSE hiç siper değildir:** görüş hattı sorgusu yalnız o
maskeyi okur — başka bir layer'daki kırılabilir obje ne engeller ne soğurur, patlama içinden
geçer.
⚠️ **Kırılabilir siper hasarı KESMEZ, SOĞURUR** (`requireLineOfSight` açık etkilerde): arada duran
kırılabilir obje **kalan canı kadarını** yutar, artan hasar arkasındaki oyuncuya/objeye geçer;
kırık olan (enkaz) hiçbir şey soğurmaz. Kırılamayan her şey — arena geometrisi, `maxHp 0` ağ
nesnesi — hasarı **tümden** keser. Pratik sonuç: **`maxHp`, sipere "kaç patlama dayanır" değil
"tek patlamanın ne kadarını yutar" anlamı da yükler**; patlama hasarından büyük bir `maxHp`
o siperi tek patlamaya karşı geçilmez yapar.
⚠️ `maxHp > 0` olup raycast'e takılan collider'ı olmayan obje **hiç vurulamaz**: sahne kaydı bunu
konsola uyarı olarak düşürür (obje yine listeye girer).

**4. Sahneyi KAYDET.** Kayıt `<Sahne klasörü>/Data/<SahneAdı>_objects.json`'u yazar. Kaydetmeden
export hiçbir şey görmez.

**5. Export.** `Tools > VortexArena > Server > Export Server Config` → `maps.json`'a haritanın
`objects[]`'i ve kökteki `kinds[]` girer — **`grab` ve `events[]` de o satırla gider**. ⚠️ **Export
koşulmazsa sunucu o objeyi tanımaz**, vuruş sessizce reddedilir ve sahada yalnız "kırılmıyor" diye
görünür; türü sonradan tutulabilir yapıp export'u unutmanın belirtisi de aynıdır — "kavrıyorum ama
obje elime gelmiyor" (sunucu hâlâ `grab:"none"` okur).

**6. Sunum.** **Hazır yol:** objeye `BreakableObject` ekle ve alanlarını bağla (`damageRenderers`,
`hitColliders` — boş bırakılırsa alt ağaçtan kendisi toplar —, `intactRoot`, `brokenRoot`,
`breakFxPrefab`, `breakFxLifetime`, `breakClip`, `breakVolume`); yazılacak kod yoktur. Otomatik
toplama `brokenRoot`'un altını **dışlar** — enkazın kendi collider'ları kırılınca kapanmaz.

Hasar görünümünün materyali `VortexArena/BreakableSurface` shader'ını kullanmalıdır (hazır prop
materyalleri `_Shared/World/Materials/`): ⚠️ `_DamageAmount` özelliği olmayan bir shader'da yazım
sessizce yok sayılır — obje kırılır ama arada "hasarlı ama ayakta" hâli hiç görünmez. Shader'ın
aydınlatması URP'nin kendisidir, yani prop hasarsızken kaynak `Lit` materyaliyle **birebir aynı**
görünür; ayrı bir görünüm ayarlaması gerekmez.

Kurulu iki örnek `_Shared/World/Prefabs/` altındadır (`NO_BreakableCover` · `NO_TargetBoard`) —
yeni bir kırılabilir için en kısa yol birini kopyalayıp mesh/tür/`maxHp`'sini değiştirmektir.
Ortak kırılma efekti `_Shared/FX/FX_BreakDebris` (enkaz parçacıkları + toz).

Kendi sunumunu yazacaksan `NetObject.StateChanged`'e abone ol: **hasar oranını** materyale yaz
(`_DamageAmount`, `MaterialPropertyBlock`), `IsBroken` olunca collider'ı kapat ve kırık görünüme
geç:

```csharp
private void OnEnable()  { netObject.StateChanged += Uygula; Uygula(netObject, NetStateOrigin.Snapshot); }
private void OnDisable() { netObject.StateChanged -= Uygula; }

private void Uygula(NetObject o, NetStateOrigin origin)
{
    _block.SetFloat(DamageId, 1f - o.HealthRatio);   // 0 = sağlam, 1 = yok olmuş
    _renderer.SetPropertyBlock(_block);
    _collider.enabled = !o.IsBroken;

    if (origin == NetStateOrigin.Live && o.IsBroken) { /* efekt + ses */ }
}
```

⚠️ **`Snapshot` efekt oynatmaz** (`world_state` = anlık görüntü): geç katılan oyuncu katılmadan
önce olmuş patlamayı görmemelidir.

⚠️ **`Hp`/`Flags`'i yerelde YAZMA** (`Yapma-Listesi` → "Ağ nesnesinin durumunu istemcide yazma"):
kırılma kararı sunucudadır, yerel kısayol iki başlıkta farklı siper üretir.

Hasar görünümü objenin shader'ında `_DamageAmount` özelliği varsa çalışır; yoksa yazım **sessizce
yok sayılır** (hata vermez, kırılma yine çalışır).

**7. Yer değiştiren obje mi?** Objenin pozu ağdan gelecekse (taşınıyor, fırlatılıyor) köküne
`NetObjectBody` + `NetObjectPoseSender` ekle: ilki **serbest** objeyi yerine koyar ve fizik
otoritesini tutar (`isKinematic = !IsMine`), ikincisi sahibi olduğun objenin pozunu akıtıp durunca
`object_rest` yollar.
⚠️ **Yerinden oynamayan objede İKİSİ DE GEREKMEZ** — kırılan ama duran bir örtünün pozu telde hiç
gitmez, sahnedeki yeri zaten doğrudur. İkisini yine de eklemek boşuna iş değil, **zarardır**:
dinlenme pozu olmayan objede gereksiz bir yazar doğar ve kutuyu her karede sahnedeki yerine
oturtmaya çalışır.
⚠️ **`NetObjectBody`, obje ELDEYKEN hiçbir şey yazmaz** — o an transform'un sahibi kavrama
köprüsüdür (§11.5); tek transform'a iki yazar derleme hatası değil, görünür bir titremedir.

**Tur başında sıfırlama** çekirdekte hazırdır (`MatchDirector.TryResetObjectsForMode`) ve sahne her
sahnelendiğinde de sıfırlanır — modun içinde yazılacak bir şey yoktur.

---

## 11.5 Elle tutulan dünya objesi eklemek (tek örnek, sahibi devredilen)

**Ne zaman:** arenada **tek** örneği olan, oyuncunun eline alıp taşıdığı/fırlattığı bir şey (bıçak,
tabak, malzeme). Silahlar bu yolu kullanmaz — onlar her oyuncuya kopyalanır ve ağ nesnesi değildir.

⚠️ **Atlanan adımın bedeli:** alma yolu ile prefab çelişirse obje ya hiç alınmaz ya da **yakınındaki
başka bir objenin kavrama basışını yer** (aşağıda 2. adım) · `WorldSingle` yazılmazsa uzak elde
**iki kopya** görünür ve gecikmede ayrışır · kavrama pozu yazılmazsa obje her istemcide farklı bir
ofsetle durur · spawn kataloğuna kaydedilmezse dinamik doğan obje **hiç görünmez** (tek satır log).

**1. Ağ nesnesi tarafını kur** (§11.4): tür asset'i `Grab = Anyone` ile, sahnedeki objede
`NetObject`, sonra `NetObjectBody` + `NetObjectPoseSender` (obje yer değiştiriyor). Sahneyi kaydet,
export'u koştur.

**2. Üç ekseni seç** (`ItemDefinition`, `Create > VortexArena > …`):

| Eksen | Bu obje için | Ne demek |
|---|---|---|
| Alma yolu (`GrabPath`) | `ProximitySocket` | Oyuncu yaklaşıp kavrar (ışınla uzaktan değil) |
| Örnekleme (`Instancing`) | `WorldSingle` | Tek örnek vardır, sahiplik devredilir |
| Bırakma (`ReleaseMode`) | `Physics` ya da `Return` | Serbest düşsün mü, yerine mi otursun |

⚠️ **Alma yolu `DistanceGrab` DEĞİLSE prefabda mesafeli kavrama bileşeni BULUNMAMALIDIR**
(`DistanceGrabInteractable` / `DistanceHandGrabInteractable`): "aday listesini kapatmak" yetmez, boş
listeyle bile interactor hover'a girer ve kavrama basışını sessizce yer — belirti "kavrama tuşu bazen
çalışmıyor"dur (`Sistem-Ozeti` Tuzaklar).

**3. Soketi yerleştir.** Prefabda objenin alınacağı yere `GripSocket` koy: kabul yarıçapı + gösterge
prefabı + hangi ellerin alabileceği. ⚠️ **Oyuncunun gördüğü küre kabul hacminin kendisidir** —
gösterge ile yarıçapı ayrı ayrı ayarlamak "içindeyim ama almıyor" üretir. Soket "nereden alınır"ı
söyler, elin nasıl duracağını **söylemez**; o bir sonraki adımdır. Yarıçap **duran** objenin kabul
hacmidir; uçuşta soket koddaki `GripSocket.CatchRadius`'a kendiliğinden büyür ve kareyi süpürür —
yakalanabilirlik için prefab yarıçapını büyütme.

**4. Kavramayı stüdyoda yaz.** `Tools > VortexArena > Items > Kavrama Pozu Stüdyosu` ile ana kabza
kaydını yaz. ⚠️ **Kavrama pozu SABİT olmak zorundadır:** obje ele kanonik pozla bağlanır, serbest
kavrama her istemcide farklı bir ofset demektir ve obje uzak başlıkta elin yanında durur.

**5. Köprüyü bağla.** Objenin köküne `NetObjectGrabBridge` ekle ve **eşya tanımını ata** (soket boş
bırakılırsa çocuklarda aranır). Kavrama iyimserdir: basış objeyi hemen yerelde alır, sahip başkası
çıkarsa kavrama kendiliğinden geri alınır — yazacağın bir red yolu yoktur.

**6. Dinamik doğacaksa katalog.** Obje çalışma zamanında doğuyorsa (§11.6) `NetSpawnCatalog`'a
`kind` → prefab satırı ekle. Kaydı olmayan `kind` gelirse obje doğmaz ve konsola tek satır düşer.

**7. Bekçiyi çalıştır.** `Tools > VortexArena > Build > Configure All Build Elements` →
**Hazırlık** bölümündeki *Eşya alma yolu ↔ prefab* satırı, alma yolu `DistanceGrab` olmayan
eşyaların prefabında mesafeli kavrama bileşeni kalıp kalmadığını söyler. ⚠️ Bekçi **hiçbir şey
yazmaz** — düzeltme senin adımın (bileşeni kaldır ya da tanımdaki yolu düzelt).

---

## 11.6 Dinamik obje doğuran mod

Doğuşun **iki kaynağı vardır ve üçüncüsü yoktur**: modun kendisi ve türün kuralı (dağıtıcıya gelen
bir olay gibi). ⚠️ **İstemci spawn isteyemez** ve böyle bir mesaj eklenmez — isteyebilse bozuk bir
başlık arenayı objeyle doldurabilirdi.

1. Modun içinde `director.SpawnObject(kind, pose)` çağır; dönen `netId` `0` ise doğuş reddedilmiştir
   (bilinmeyen `kind` ya da tükenen aralık) ve konsola sebep yazılmıştır. Kaldırma:
   `director.DespawnObject(netId)`.
   Obje **doğrudan bir elde** doğacaksa aynı çağrıya sahip ve el verilir:
   `SpawnObject(kind, pose, playerId, rightHand)` — obje `owner` dolu ve `Held` bayrağıyla gelir,
   ayrı bir "eline ver" mesajı yoktur. Eldeki obje istemcide kendiliğinden benimsenir
   (`NetObjectGrabBridge`), ama ⚠️ **doğuşu isteyen bileşen dolu eli kendisi reddetmelidir**
   (`HeldItems.RightHand/LeftHand` → `IsEmpty`): sunucu elin dolu olduğunu bilmez, aynı yumrukta iki
   obje bırakır ve eskisi yenisinin altında görünmez olur.
2. Örneğe ait metin (bir müşterinin siparişi, slot numarası) `payload` parametresiyle ya da
   `director.SetObjectPayload(netId, …)` ile yazılır ve `object_state.s` olarak gider. ⚠️ Biçimini
   **mod** tanımlar, çekirdek yorumlamaz — `modeState` ile aynı sözleşme. Yeri olay değil obje
   durumudur, çünkü `world_state` onu taşır: geç katılan oyuncu bekleyen müşteriyi siparişiyle
   görmek zorundadır.
3. Olay tetikleyecekse `IGameMode.OnObjectEvent`'i yaz. Dönüş değeri yalnız **relay** sorusunu
   cevaplar: `true` = "ben hallettim, relay etme", `false` = kozmetik (aynı olay herkese relay
   edilir). ⚠️ **Duyuran, yazandır:** `SetObjectStage`/`SetObjectFlags`/`SetObjectPayload`/
   `SpawnObject`/`DespawnObject` sonucu kendisi yayınlar — bir olay birden çok objeyi
   değiştirebildiği için "olayın objesini yayınla" kısayolu yalnız birini duyururdu. Mod tabloya
   dokunmaz; okuma da `director.TryReadObject(...)` üzerindendir.
4. İstemci tarafında yazılacak bir şey yoktur: prefabı `NetSpawnCatalog` çözer, örneği
   `NetObjectSpawner` kurar. Tur/sahne sıfırlaması dinamik objelerin **hepsini** siler.

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

**Ölüm ekranını yeniden çizme:** `_Shared/App/Resources/UI/DeathHud.prefab`'ı HUD prefabının
altına **iç içe prefab** olarak koy (en son kardeş → en üstte çizilir), örneği **kapat**, sonra üç
alanı bağla: `deathOverlay` → örneğin kökü, `deathKillerNameText` → `DeathPanel/KillerName`,
`deathStatusText` → `DeathPanel/StatusText`. Metinleri taban yazar. ⚠️ Panel opaktır ve HUD'ın
kendi `statusText`'ini örter — canlanma sayacını oyuncu **yalnız** `deathStatusText` bağlıysa
görür.

**Can barını da yeniden çizme:** `_Shared/App/Resources/UI/HealthHud.prefab`'ı aynı şekilde HUD
prefabının altına **iç içe prefab** olarak koy, sonra iki alanı bağla: `healthFill` →
`Backdrop/Fill`, `healthText` → `Backdrop/Value`. Bar kendi `HeadLockedHud`'uyla kafaya kilitlidir;
HUD'da ayrıca bir can göstergesi **bulundurma** — aynı sayı iki yerde çizilirdi.

**Takım skoru / tur sonucu gerekiyorsa:** ikisi de aynı `HealthHud` örneğinin içinden gelir, yeni
prefab koymana gerek yok — çünkü ikisi de barın **tek** `HeadLockedHud`'una biner (ikinci bir kafa
kilidi eklenmez, gerekçesi `HeadLockedHud`'un kendi belgesinde). Modun kendi bileşenine iki
`[SerializeField]` alan koy — **tabana koyma**, taban takım-agnostiktir — ve HUD prefabında bağla:
`TeamScorePanel` → `HealthHud/RoundScore`, `RoundResultBanner` → `HealthHud/RoundResult`. Sonra
`OnMatchStateApplied(msg)`'i override edip `panel.SetScore(msg.scoreRed, msg.scoreBlue)` de; tur
kavramın varsa `panel.SetRoundLabel($"TUR {n}")`, sonucu duyuracaksan
`banner.Show("TUR KAZANILDI", RoundOutcome.Won)`. Lobiye dönüşte `panel.Clear()`. Takımsız modda
alanları **boş bırak**: panel zaten `ModeRuntime.IsTeamless` ile kendini gizler.

**Ortada büyük yazı gerekiyorsa (geri sayım, "şunu bekliyorsun"):**
`_Shared/App/Resources/UI/RoundNoticeHud.prefab`'ı aynı desenle HUD prefabının altına **iç içe
prefab** olarak koy — **en son kardeş** (opak ölüm ekranının üstünde çizilsin diye) — örneği
**kapat**, sonra üç alanı bağla: `centerNoticeRoot` → örneğin kökü, `centerNoticeText` → `Notice`,
`centerNoticeDim` → `Dim`. Geri sayımı taban kendiliğinden yazar; kendi metnini modun bileşeninden
`hud.SetCenterNotice("…")` ile ver, boş string temizler. ⚠️ **Aynı ögeyi paylaşırlar** — bu yüzden
metin KISA ve tek satır olmalı, ayrıntı `statusText`'te kalır. ⚠️ Karartmayı elle açma: taban onu
yalnız geri sayımda açar.

**Ölüm ekranı canlanması olmayan modda kilitli kalmasın:** `deathOverlaySeconds`'ı **3** yap
(0 = canlanana kadar açık). `reviveAnchor:none`'da canlanma hiç gelmez, ekran tur bitene kadar durur
ve oyuncunun asıl beklediği şeyi örter.

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

**Oyun tipi:** `ModeDefinition`'ın `gameType` alanı ile sunucudaki `IGameMode.GameType` **aynı
aileyi** göstermelidir ([Sistem Özeti §3.10](../Sistem-Ozeti.md)). İkisinin de varsayılanı Hızlı
Savaş'tır (`QuickBattle` / `"quickbattle"`), yani Hızlı Savaş modu yazarken ikisine de dokunmazsın;
yalnız bir Çocuk Oyunları modu ikisini birden çevirir — SO'da `Kids`, mod sınıfında
`public string GameType => "kids";`.

> ⚠️ **İkisi tutmazsa mod o haritada BAŞLATILAMAZ:** `start_match` modun tipini haritanın
> `gameType`'ıyla karşılaştırır ve uyuşmazsa reddeder. Sahadaki belirti yine "maç başlamıyor"dur,
> sebep yalnız sunucu konsolunda tek satırdır.

> ⚠️ asmdef üretirken mevcut moddan **JSON'u kopyala, `.meta`'yı KOPYALAMA** — GUID çakışır.

Ayrıntı: `ModeRules` alanlarının tamamı → [Sistem Özeti §3.9](../Sistem-Ozeti.md).

---

## 13.1 Çocuk oyunu eklemek (silahsız, kooperatif)

Reçete 13'ün üstüne binen **altı fark** vardır; gerisi aynıdır.

**1. Aile.** Sunucuda `public string GameType => "kids";`, `ModeDefinition`'da `gameType = Kids`,
haritanın `MapDefinition`'ında da `gameType = Kids`. ⚠️ Üçü tutmazsa `start_match` **sessizce**
reddedilir — sahadaki belirti "maç başlamıyor"dur, sebep yalnız sunucu konsolundadır.

**2. Kural şekli.** `Weapons = None` (hasarı kapatan şey budur, ayrı bir anahtar yoktur — atılabilir
eşya da atılamaz), `Teams = None`, `Revive = None`, `RespawnDelay = 0`. Kooperatif skor için
`Scoring = ScoreKind.PlayerAndShared` ve yazan yol `director.AddSharedScore(playerId, puan)`:
bireysel katkı ve ortak toplam **tek çağrıdan** gider, `scoreBlue` `0` kalır ve **kazanan yoktur**
(`IsMatchOver` her zaman `MatchOutcome.Draw` döndürür). Sonuç tablosu grubun birlikte okuduğu şey
olduğu için `HoldsResultForOperator => true`.

> **Yarışmalı varyant.** Ailenin değişmezi yalnız ilk üçüdür — `Weapons = None` (hasarı kapatan
> şey), `Revive = None`, `HoldsResultForOperator => true`. **Skor kanalı ve takım kipi serbesttir:**
> kırmızı–mavi yarışan bir çocuk oyunu `Teams = TwoTeams` + `Scoring = Team` alır, kazananı olur ve
> `IsMatchOver` süre dolunca önde olan takımı döndürür (örnek: `mole`). ⚠️ **Takımlı olmak taban
> gerektirmez:** taban `Revive = OwnBase`'in aracıydı ve burada canlanma yoktur — haritaya BaseZone
> koymak, oyuncuya oyunun parçası olmayan bir hedef gösterir. ⚠️ Yarışmalı bir çocuk oyununda
> **ceza kanalını da seç:** takım skorunu eksiye açmak mı, `0`'da kısmak mı? Küçük yaş grubunda eksi
> skor anlatılamaz, ama ceza hiç görünmezse yanlış vuruşun karşılığı da kalmaz — ikisini ayırmanın
> yolu takım skorunu kısıp bireysel katkıyı eksiye bırakmaktır (o zaman iki kanal birbirinin toplamı
> **değildir** ve HUD bunu böyle sunmalıdır).

**3. HUD.** `ModeHudBase`'den türet ama silah/can/ölüm/kill-feed alanlarını prefabda **boş bırak** —
taban atanmamış alan için hiçbir şey çizmez, bu modda karşılıkları yoktur. Kendi sayaçlarını
`modeState`'ten oku (biçimi mod tanımlar, çekirdek yorumlamaz) ve **bilinmeyen anahtarı atla**:
alan sonradan yeni sayaç kazanacaktır, bozuk dize HUD'ı düşürmemelidir.

**4. Etkileşim = ağ nesnesi.** Oyunun eşyaları `NetObjectKind` asset'leriyle tanımlanır (`grab` +
`events[]`), dinamik olanların prefabı `NetSpawnCatalog`'a girer, sunucu tarafı
`IGameMode.OnObjectEvent` ile yorumlar → [11.4](#114-ağ-nesnesi-eklemek-kırılabilir-örtü-hedef-tahtası) ·
[11.5](#115-elle-tutulan-dünya-objesi-eklemek-tek-örnek-sahibi-devredilen) ·
[11.6](#116-dinamik-obje-doğuran-mod). Elle tutulan proplar `PropDefinition` + `WorldSingle` +
`ProximitySocket` + `Physics`tir ve `netItemId` **almazlar**.

**5. Sunum sahnede kalır.** Bir NPC'nin yürüyüşü, bir istasyonun ışığı, bir balonun içeriği telde
taşınmaz: sunucu **metre bilmez**. Taşınan tek şey `stage` (aşama) ve `s` (örnek verisi); nerede
durduğunu sahnedeki yol/slot bileşenleri söyler. ⚠️ Böyle bir objeye `NetObjectBody`/
`NetObjectPoseSender` **eklenmez** — objeyi hem yoldan hem ağdan süren iki yazar, iki başlıkta iki
yer demektir.

**6. Ses.** `ModeAudioRegistry`'de ailenin duyuruları (maç başı, geri sayım) oyun tipi filtresi
`Çocuk Oyunları` olan satırlarla verilir — mod başına satır yazılmaz, aileye eklenen yeni çocuk
oyunu sesleri kendiliğinden alır. Tek bir moda ayrı klip istiyorsan (ör. daha yumuşak bir maç başı)
satırı moda daralt. ⚠️ Ortak "hadi hadi" duyuru kliplerini çocuk ailesine bağlama.

⚠️ **Bir olayı KİM bildirir?** `policy:"anyone"` olan bir olayı herkes gönderebilir, ama aynı
gerçeği N istemcinin bildirmesi sunucuda aynı sayacı N kez başlatır. Seçici sahnede aranır ve tek
kişiyi göstermelidir: objeyi **durduran** istemci (`NetObjectPoseSender.RestSent`), objeyi **elinde
tutan** istemci, ya da olayı üreten aleti tutan kişi.

---

## 13.2 Bir oyun istasyonunu ikinci bir mekana kurmak

Bir çocuk oyununun sahne kurulumu (banko, mutfak/atölye, işaretler, proplar) **tek prefabtır** ve
`Modes/<Mod>/Prefabs/Station/` altında durur. Parçaları ikinci mekana tek tek taşımak bu bölümdeki
her tuzağı davet eder.

**1. Kabuk prefaba GİRMEZ.** Zemin, duvar, kapı çerçevesi mekana özgüdür — her işletmenin ölçüsü
başkadır. Prefab yalnız **oynanan** kısmı taşır; kabuk sahnede kalır.

**2. ⚠️ Görünen parça ile görünmez oyun hacmi aynı prefabın içindedir.** İkisi kardeş obje olursa
biri taşınır, öbürü unutulur — ve belirtisi **hata değildir**: obje doğru görünür, oyuncu doğru
hareketi yapar, hiçbir şey olmaz. Kural: her görünmez hacim, ait olduğu görselin **çocuğudur**.

| Görünen | Ona kilitli görünmez parça |
|---|---|
| Izgara/ocak gövdesi | pişirme hacmi (`BurgerGrill`) |
| Banko | slot hacimleri + müşterinin duracağı nokta. ⚠️ Servis tahtası sabittir ve yuvasına **konumuyla** bağlanır (slot numarasıyla değil): tahta ile slot hacmi aynı prefabta, tahta hacmin içinde durur — ayrı düşerse mod sessizce hiç servis yapmaz. Tahta başka tezgâha alınacaksa **yuva objesi** (`CounterSlot_N`) da taşınır: müşteri noktası onun çocuğudur, tek başına taşınırsa müşteri doğru yerde bekler ama tahta hacmin dışında kalır |
| Kapı | müşteri yolunun waypoint'leri |
| Malzeme rafı | dağıtıcıların kavrama soketleri |

**3. Kök döndürülmemiş olur.** İstasyonun (ve tek tek çıkarılan parçaların) kökü `rot 0`, `scale 1`
alır; yön ve ölçek **çocuklarda** durur. Yeni mekanda kökü döndürmek o zaman bütün istasyonu
birlikte döndürür, hizalar bozulmaz.

**4. `sceneId` elle yönetilmez.** Prefab, çıkarıldığı sahnenin `sceneId`'lerini taşır; yeni sahnede
çakışırsa `SceneIdGuard` **sahne kaydında** onarır ve hemen ardından `<Sahne>_objects.json`'ı
yazar. Yani yeni mekanda yapılacak tek şey: prefabı koy, **sahneyi kaydet**. Ayrı bir export adımı
yoktur. ⚠️ Kaydetmeden Play'e girersen onarım yalnız bellekte kalır, sunucunun okuduğu liste eski
kalır — belirti "obje iki kez kayıtlı"dır.

**5. Ölçüler mekana göre yeniden bakılır.** Prefab konumları korur ama **mekanın ölçüsünü bilmez**:
soketlerin yerden yüksekliği (hedef kitle çocuksa özellikle), müşteri yolunun uzunluğu ve bankonun
oyun alanına sığıp sığmadığı her mekanda yeniden kontrol edilir.

**6. ⚠️ Eşya bırakılan yüzeyin collider'ı görünen yüzeye oturur.** Mekanın kendi mobilyası
(environment paketinin masası/tezgâhı) kullanılıyorsa kutu **ölçüyle** kurulur: üstü çalışma
yüzeyinde, mesh sınırında değil. Arka pervaz sınırı şişirir → bırakılan eşya havada durur; kutu
yüzeyin altında kalırsa ince eşya (bıçak, spatula) masaya gömülür. `MeshCollider` + Convex
kullanılmaz: L masada hull iç köşeyi doldurur, pervazı yüzeye eğim olarak yayar. Kalıp: gövde kutusu
(L masada kol başına bir) mobilyanın `Obstacle` layer'ında; pervaz kutuları `Default` layer'daki
bir çocuk objede — engel aday sınırını ([14](#14-yeni-arena-eklemek) 5. adım) boşa doldurmasın.

**7. ⚠️ Kabuğun zemin collider'ı kalın bir levhadır.** Üst yüzü `y=0`'da, kalınlığı ~0,5 m, izdüşümü
duvarların birkaç metre dışına taşar. Sıfır kalınlıklı (düz plane) zemin ya da yalnız oda kadar
kutu, fırlatılan ince malzemeyi (domates dilimi ~1,4 cm) bir fizik adımında içinden geçirir; düşen
eşya dinlenmeye hiç varamaz ve kaybolur. Eşya prefablarının `Rigidbody`'si
`ContinuousSpeculative` kalır — `Discrete` ince eşyada aynı delinmeyi masada da yapar.

**8. İstasyonu koymak haritayı oynanabilir yapmaz.** Sahne adı katalog anahtarıdır; haritanın
`MapDefinition`'ı ve `gameType`'ı olmadan maç başlamaz → [13.1](#131-çocuk-oyunu-eklemek-silahsız-kooperatif) ·
[14](#14-yeni-arena-eklemek).

---

## 14. Yeni arena eklemek

Tek düğmeli bir sihirbaz **yoktur** (kaldırıldı). Akış altı adımdır ve her adımın kendi aracı var:

| # | Yaptığın | Araç |
|---|---|---|
| 1 | Boş sahne aç, arena kutusuna kaydet (`Venues/<İşletme>/Scenes/<SahneAdı>/<SahneAdı>.unity`) | `File > New Scene` |
| 2 | Ağ altyapısını koy | `Tools > VortexArena > Arena > Template Temellerini Yükle` |
| 3 | Mekanın ölçü maketini + kalibrasyon işaretçilerini üret | `… > Arena > JSON'dan DimensionMesh Üret` |
| 4 | Ölçü yanlışsa köşeleri düzelt, dosyaya geri yaz | ProBuilder + `… > Arena > DimensionMesh'i JSON'a Çevir` |
| 5 | Environment sanatı (**dünya orijinine**, zemin y=0), hareketsiz dekora **static flag**, bake | elle + `… > Arena > Sahne Bütçesini Ölç` |
| 6 | Tüm kayıtları yap | `… > Build > Configure All Build Elements` |

**3. adımın boyut dosyası hazır olmalıdır.** Dosya elle yazılabilir (§17.2) ya da sahada kumandayla
ölçülüp sunucudan alınabilir (§17.5) — sonuç aynı dosyadır, mekan başına tektir ve o mekanın bütün
sahneleri onu gösterir.

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

**5. adımın engel kuralı — `Obstacle` layer'ı.** Engel ihlali (kafa → karartma + ceza, namlu → atış
yok; `Docs/ArenaNet-Protokol.md` engel ihlali bölümü) yalnız bu layer'daki collider'ları görür; layer
değeri sahne objesinde durduğu için arena başına elle yapılır ve **atlanırsa o arenada ihlal hiç
çalışmaz**, hata da vermez.

- **Giren:** oyuncunun ulaşabildiği, kutunun **içindeki** katı geometri — sütun, kasa, sandık, blok,
  mobilya, kutuya 1 m'den fazla giren duvar çıkıntısı, kırılır siper.
- **Girmeyen:** dış duvar, zemin, tavan, kutu kenarına oturan **kabuk** (görünmez sınır kutuları,
  çit, cam kenar), köşe dolgusu ve kutunun dışında kalan her şey — kalibrasyonu kaymış oyuncu
  kenarda durduk yere ölmesin, `ObstacleVolumes` aday sınırı boşa dolmasın. Aynı ad ailesi
  (`ArenaBoundry*` gibi) iki türü de kapsayabilir: **ada değil konuma** bakılır. Küçük prop
  (mikrodalga, el aleti) da girmez — kafa sığmaz, aday sınırını yer. Layer bir prop'a çocuklarıyla
  birlikte verildiyse içindeki parçalar (vitrindeki şişeler) `Default`'a geri alınır: gövdenin
  içinde kaldıkları için ihlale bir şey katmazlar, oyuncunun çevresindeki 8 aday yerini doldururlar.
- **Collider konveks olmalı:** Box/Sphere/Capsule ya da `MeshCollider` + Convex. Environment
  paketleri **konkav** `MeshCollider` ile gelir: layer vermek işe yaramaz (çalışma anında elenir +
  hata), convex işaretlemek ise içbükey mesh'te hull'ü çukura doldurur ve oyuncu **boşlukta** ceza
  alır. Doğru hamle mesh'in yerel sınırlarına oturan kaba bir **Box/Capsule** koymaktır. Convex
  `MeshCollider` 255 üçgeni aşarsa Unity **kısmi hull** kullanır ve her sahne yüklemesinde uyarı
  basar (gözlük günlüğü saniyelik sınırda kısılır, gerçek satırlar kaybolur) — yine Box. İnce panel
  (çit, ızgara) kutusu en az ~0,3 m kalınlık alır: kafa ince kutudan tek karede geçip ihlale
  yakalanmaz.
- **Yüzey (çarpma efekti) ataması elle yapılır:** `Tools > VortexArena > Arena > Yüzey Atama`
  seçili objenin materyallerini, hangi yüzeye düştüğünü ve nedenini gösterir; materyali yüzeye
  bağlar ya da objeye etiket koyar. ⚠️ Eşleme **göz kararıyla toplu** yapılmaz: aynı atlas dokusunu
  paylaşan tahta ve metal objeler tek materyalde birleşebilir — objeyi oyunda görerek ata.
- **Dekor haritalı arenada** (collider'sız tek mesh harita) görünmez sınır kutusu etiket aldıysa
  yenisini eklerken etiket de kopyalanır ([Sistem Özeti, `SurfaceTag`](../Sistem-Ozeti.md)).
- **Environment paketinin prop'ları materyale çözülmüyorsa** (collider ile renderer ayrı objelerde)
  etiket prop başına değil **grup köküne** konur — etiket collider'dan yukarı arandığı için tek
  bileşen yüzlerce collider'ı kapsar; oyun alanı içindeki prop'lar ise materyal eşlemesiyle
  çözülmeye devam eder.
- Sonunda `Tools > VortexArena > Arena > Engel Hacimlerini Denetle` koşulur: konveks olmayan,
  şişkin ve trigger collider'lar düzeltilene kadar o objeler yanlış ceza üretir. Rapor tüm açık
  sahneleri kapsar, hiçbir şeyi düzeltmez.

**5. adımın bütçe kuralı.** Environment yerleştikten sonra `Tools > VortexArena > Arena > Sahne
Bütçesini Ölç` koşulur: tenant'ın sahneleri yan yana, eşiği aşan hücre kırmızı (1M+ LOD0 üçgen ·
1500+ LOD0 renderer · birden çok terrain · static flag'siz dekor). Hareketsiz dekor **Static**
işaretlenir (yapraklı vegetasyon/çimde Occluder kapalı; `Animator`/`Rigidbody` altındaki obje
işaretsiz), bake **ondan sonra** alınır — işaretsiz objeye bake uygulanmaz
([Yapma Listesi](Yapma-Listesi.md)). Hazır environment paketleri **çok parçalı terrain**le gelir;
Quest'te terrain batch'lenmez — tek terrain'e indirilir ya da mesh'e çevrilir. Paylaşılan
`TerrainData`'ya dokunulmaz: sahneye özel kopyası arena kutusunun `Art/`'ına alınır, sonra
düşürülür. Oyuncunun gitmediği fon terrain'inde Pixel Error yüksek, Basemap Distance düşük,
Draw Instanced açık.

Paket dekorunun `LODGroup`'u **en fazla 3 seviye (yer bitkisi 2)** taşır, ara seviyelerin objesi
kapatılır, ağaç billboard'u düşer. Paket ağacının/çiminin LOD0'ı Quest için fazla detaylıdır —
en yakın görünüm bir alt seviyedir, LOD0 objesi kapatılır; paket dekorunun tamamında da öyle.
İstisna: arenaya 10 m'den yakın olup bir alt seviyesi LOD0'ın %25'inin altına düşen obje
(yakında kutu gibi görünür) LOD0'da kalır. ⚠️ **Son seviyenin geçişi 0'dır — dekor uzakta kaybolmaz**:
kaybolan dekor ufku boşaltır. `Fade Mode` = None (mobil asset'te LOD Cross Fade kapalı).
Controller'sız `Animator` altındaki hareketsiz skinned hayvan, pozu bake edilmiş `MeshRenderer`'a
çevrilir (skinning her karede boşuna ödenir); boş `Animator` silinir. Static dekor
(duvar, çit, yer bitkisi, çok parçalı eşya) bölge bölge (6–12 m hücre) materyal başına tek
mesh'e birleştirilir: LOD seviyeleri ve geçiş mesafeleri korunur, UV2 yeniden üretilir, mesh
arena kutusunun `Art/`'ına yazılır; eski parçaların yalnız renderer'ı kapanır — collider'ları
yerinde kalır. Saydam, `Animator`/script/`Rigidbody` taşıyan obje birleştirilmez. Birleştirmeden
sonra bake yeniden alınır.

Arenadan uzaktaki dekor ucuzlatılır, gizlenmez: `Scale In Lightmap` arenaya 10–30 m'de 0.5, 30 m
ötesinde 0.25 (fon lightmap belleğini yutmasın). LOD'u hiç olmayan fon modeline (şehir
blokları gibi) `MeshLodUtility.GenerateMeshLods` ile üretilen seviyeler ayrı mesh olarak
`LODGroup`'a konur (orijinal · %62 · %16; geçiş 0.30 / 0.10 / 0). Oyun alanının hiçbir noktasından
ekrana gelemeyen üst LOD seviyesi kapatılır — görüntü değişmez, sayaç düşer. Arena sınırına 5 m'den uzak dekorun collider'ı
silinir — paketin konkav `MeshCollider`'ları yüklemede tek tek pişirilir; bedeli: mermi o dekora
çarpmaz, iz bırakmaz. Occluder yalnız ardını gerçekten kapatan
yapıdadır (bina, duvar, döşeme, tank, araç); iskele, çit, tel örgü, kolon ve eşyada kapalı —
sonra occlusion bake alınır. Yalnız bu sahnede ve arenaya 10 m'den uzakta kullanılan paket
dokusuna Android override `Max Size 1024` konur; başka oyun sahnesinin de kullandığı dokuya
dokunulmaz. ⚠️ Sahnenin `.lighting` dosyası klasörde durup **sahneye bağlı olmayabilir** —
bağlı değilse bake hiç alınmamıştır ve bütün static dekor her karede gerçek zamanlı gölge öder.

⚠️ **Aydınlatma kurulumu ana haritada bir kez, bake her mekan sahnesinde.** Ana harita
(`Assets/Maps/<Harita>/`) mekan sahnelerinin kopyalandığı kaynaktır: `VA_LightProbes`, `.lighting`
(Mixed + Shadowmask), static flag'ler, gölge işaretleri ve güneş transform'u orada kurulur ve
kopyayla gelir. **Lightmap gelmez** — bake sahnenin kendisine yazılır, o yüzden her mekan sahnesi
çiti ve mekana özel yerleştirmesi bittikten sonra **kendi başına** bake edilir
([Sahne Kurulumu](Sahne-Kurulumu.md)).

⚠️ **Ölçekleme yoktur ve eklenmez.** Her işletmenin alanı farklı ölçüde ve çoğu kare/dikdörtgen
bile değil — orantılı ölçekleme elle düzeltilecek bir yalancı-doğru üretir.

**1. adımda klasör adı sahne adıyla AYNI yazılır** ve MapDefinition da o kutuya aynı adla girer
(`Data/<SahneAdı>.asset`). Sahne adı zaten katalog anahtarı olduğu için klasöre bakan anahtarı
görür; araç bu üçünü karşılaştırır ve uyuşmayan kutuyu uyarı olarak bildirir.

**6. adım** (**Hepsini Çalıştır**, sahne açıkken) `MapDefinition`'ı yazar, sonra kayıtları
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

⚠️ **Aynı `MapDefinition`'daki `gameType` haritayı bir oyun ailesine bağlar**
([Sistem Özeti §3.10](../Sistem-Ozeti.md)): Hızlı Savaş arenasında alana **dokunulmaz** —
varsayılanı `QuickBattle`'dır; yalnız Çocuk Oyunları haritasında `Kids` seçilir. Değer 6. adımda
`maps.json`'a `gameType` olarak girer, yani sonradan değiştirirsen o adım yeniden koşar. Yanlış tip
hiçbir yerde hata vermez: harita yalnız kendi ailesinin modlarıyla başlatılabilir, diğerlerinde
`start_match` reddedilir — sahadaki belirti **"maç başlamıyor"**dur.

⚠️ **Köstebek arenasında `moleDensity` de o tanımdadır** — aynı anda kaç köstebeğin ayakta duracağını
belirler (`1` = temel yoğunluk) ve alanın varsayılanı yeni bir mekanın başlayacağı değerdir. Delikleri
seyrek ya da salonu büyük bir mekanda yükselt, dar mekanda düşür; değer 6. adımda `maps.json`'a girer
(protokol §10.5). Diğer arenalarda alana **dokunulmaz**, oralarda hiçbir şeye bağlı değildir.

⚠️ **Arena sildiysen/taşıdıysan aynı pencereden `Hepsini Çalıştır`** — sahne açık olmadan da koşar
(o durumda `MapDefinition` adımı atlanır) ve kalıntı kayıtları temizler; kayıtlar elle düzenlenmez.

> Arena ölçüsü **sunucuya gitmez** (maps.json'a yalnız `sceneName` + `gameType` + `modes` +
> `moleDensity` + ağ nesneleri yazılır); arenanın
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

## 14.2 Çok katlı arena (kat portalı)

Arena birden çok katta oynanacak. Oyuncu fiziksel olarak tek katta yürümeye devam eder; kat
değişimi **dikey sanal ofsettir** (`Docs/Sistem-Ozeti.md` §3.13, ağ tarafı
`ArenaNet-Protokol.md` §10.6 "Kat modeli").

**Haritanın kat kuralları — tasarıma başlamadan önce:**

- **Zemin kat (kat 0, dünya y = 0) zorunludur**, üst katlar isteğe bağlıdır.
- **Zeminin altına kat konmaz** (bodrum diye bir seviye yoktur).
- **Her takımın taban şeridi zemin kattadır**, üst kata taban konmaz: ölüm oyuncuyu tabanının katına
  indirir ve hedef hiçbir zaman bulunduğu kattan yukarı değildir.

| # | Yaptığın | Görmen gereken |
|---|---|---|
| 1 | `Tools > VortexArena > Arena > Kat Portalı Kitini Üret` | `M_FloorPortal` + `M_FloorPortalFill` ve `VA_FloorPortal.prefab` üretildi/güncellendi (konsolda tek satır). Kit zaten varsa da çalıştırılabilir |
| 2 | `Tools > VortexArena > Arena > Template Temellerini Yükle` → **Kat sayısı** = kaç kat oynanacaksa (1–4) → *Yükle* | Her ek kat için bir `Portal_Kat{k}_{k+1}` örneği sahnede, X'te 2 m aralıkla dizili (`Portal_Kat0_1` (0,0,0), `Portal_Kat1_2` (2,3,0) …). Sahnede zaten portal varsa araç hiç dokunmaz (raporda "atlandı") |
| 3 | Her portalı oynanacak yere **taşı** (ölçek 1 kalır) ve `upperHeight`'ını gerçek kat yüksekliğine yaz | `Üst` çemberi o yükseklikte belirir (Gizmo çemberleri Scene view'de çizilir). ⚠️ Portalın **kökü kendi katının zeminine** oturmalı (±0,25 m); ⚠️ iki portalın diskleri **aynı X/Z'ye gelmez** — konsolda çakışma uyarısı görürsen portalı kaydır |
| 4 | Üst kat plakasını (zemin mesh'i) portalın tanımladığı yüksekliğe kur; collider'ı **`Default`** layer'da bırak | Alt kattan yukarı ateş eden oyuncunun mermisi plakada durur. ⚠️ Plakayı **`Obstacle` layer'ına ALMA** — üst kattaki herkes sürekli ihlalde sayılır ve ekranı kararır |
| 5 | (İsteğe bağlı, admin kuş bakışı için) plakayı `ArenaRoof` kökünün altına al — `GameObject > VortexArena > Arena Roof` | Admin tepeden bakarken üst kat gizlenir, alt kat okunur |
| 6 | Portalın ses kancalarını bağla: dolum (2 sn boyunca döner) · geçiş anı · meşgul uyarısı | Boş bırakılan kanca sessizdir, uyarı çıkmaz |
| 7 | Taban bölgelerini **zemin kata** koy (üst kata taban konmaz) | Ölen oyuncu tabanının katına iner; bölge yalnız oyuncunun katı ona eşitken içeride sayar |
| 8 | Play (ya da gözlükte aç) | Konsolda `[ArenaFloors] n kat: 0.00 m / 3.00 m …`. Çemberin üstünde 2 sn durunca HUD'da geri sayım, sonra kısa karartma ve diğer kat |
| 9 | Admin istatistik panelini aç | Üst kattaki oyuncunun ayrıntı şeridinde `1. kat` jetonu (zemin kat 0'dır, yazılmaz), halkası onun katının zemininde |

- **Kat yüksekliğini hiçbir yere elle yazma:** kat listesinin tek kaynağı portalların kendisidir.
  Portal hiçbir kat zeminine oturmuyorsa uyarı basar ve **kendini kapatır** — yani "portal çalışmıyor"
  belirtisi konsoldadır, sessiz değildir.
- **Üçüncü kat = aynı prefabın bir kopyası**, kökü 1. katın zemininde (yani 1. kat yüksekliğinde).
  Her portal bir kat ÇİFTİ tanımlar.
- **Çemberlere collider ekleme** — kapı kafanın XZ mesafesiyle çalışır; collider atış izini yer.
- **Aynı anda tek oyuncu geçer:** iki kattaki çemberlerin içindeki en düşük `playerId` sahiptir,
  diğeri kırmızı çember + "Portal meşgul" görür. Ölü oyuncu da geçebilir; kalibre olmayan geçemez.
- **Portal disklerini aynı X/Z'ye koyma:** geçiş yatayda kıpırdatmadığı için gelen oyuncu doğrudan
  diğerinin dolumuna iner ve katlar arasında zincirlenir. Şablon aracının 2 m aralığı bunun içindir.
- **Admin kat seçicisi için sahnede hiçbir şey kurmazsın** (kat düğmeleri kat sayısından türer), ama
  bedeli şudur: kuş bakışı katları yalnız **dünya Y'sine** göre ayırır — üst kat plakası gerçek
  yükseklikte **gerçek geometri** olmalıdır, "göze öyle dursun" diye alçağa kurulmuş bir plaka
  operatörün kat görüntüsünü yalanlar.
- **Başka kattaki oyuncu** izleyenin katına düz, kenar parlaması olmayan, %20 saydamlıkta takım
  tonunda bir **silüet** olarak düşer (ölü/kalibresiz hayaletten bilerek farklı görünür); gerçek
  gövde kendi katında vurulabilir kalır.
- Sunucuda portal, sayaç ya da kilit **yoktur**: sunucu yalnız oyuncunun katını defter olarak tutar.

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

Silah duruşu, namlu alevi, ön kabza göstergesi, ses gibi **tümüyle yerel** şeyleri denerken sunucu
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
**MEKAN başınadır**: `Venues/<İşletme>/Data/<İşletme>_dimensions.json`. Bir işletmede hep aynı fiziksel alan
oynatıldığı için o mekanın **tüm** sahneleri (arenalar + lobi) `ArenaBoundary.dimensionsJson`
alanında **aynı** dosyayı gösterir — sahne başına kopya kaçınılmaz olarak sapar. İçerik **çalışma
anında** okunur.

**Aynı dosya ölçü maketini üretir, muhafazayı besler, admin kuş bakışı kadrajını verir ve
kalibrasyon işaretçilerini yerleştirir** — ölçüyü ikinci bir yere yazma.

Dosyayı üretmenin iki yolu var ve **ikisi de aynı dosyayı** yazar: metreyle ölçüp elle yazmak
(§17.1–17.2) ya da sahada kumandayla ölçmek (§17.5).

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
  "name": "<İşletme>",
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

✔ **Gidiş-dönüş dosyayı bozmaz:** tek halka → tek mesh → tek halka. Dokunulmamış bir maketi
çevirmek dosyayı olduğu gibi bırakır; tek beklenen fark senin taşıdığın köşelerdir. Bunu ayakta
tutan yazım kuralları:

| Kural | Ne olur |
|---|---|
| Sayı biçimi | Her sayı milimetreye yuvarlanarak (`0.###`) yazılır |
| Kolon yüksekliği | Ölçülen yükseklik `defaultColumnHeight` ile aynıysa alan `0` kalır (yani "varsayılanı kullan") |
| Halka yönü ve ilk köşe | Kaynak dosyadaki halkaya hizalanır — kolon önce **adıyla**, adsızsa **en yakın merkeziyle** eşlenir |
| Kolon adı | Dosyada boş bırakılmış ad boş kalır (makette görünen `Kolon_00` geri yazılmaz) |
| `topViewHeight` | Maketten ölçülemez; kaynak dosyadaki değer olduğu gibi taşınır |

⚠️ **Elle 3 ondalıktan hassas yazılmış bir sayı milimetreye yuvarlanır** — bu istenen davranıştır:
şeritmetre milimetrenin altını ölçmez ve daha uzun sayılar dosyanın diff'ini okunmaz yapar.

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

⚠️ **Ayıklamanın gerekçesi boyut değil sahipliktir:** ProBuilder runtime'ı build'e zaten giriyor, ama
yalnız `VortexArena.App`'in mekan ölçüm rehberi üzerinden (`_Shared/App/Scripts/Survey/`, §17.5) —
`VortexArena.Core`'un ProBuilder'a referansı **yoktur ve eklenmez**. Maketteki `ProBuilderMesh` sanat
değil **ölçü referansıdır**: sahada çizilmesi gereken bir şey değildir, bu yüzden ayıklanır. Aynı
sebeple maket **`EditorOnly` etiketlenmez**: etiket kalibrasyon işaretçilerini de silerdi.

### 17.5 Alternatif: dosyayı sahada kumandayla üretmek

Ölçüyü metreyle alıp JSON'u elle yazmak (§17.1–17.2) tek yol değildir: aynı dosya sahada, gözlüğü
takan kişi tarafından da çıkarılabilir. Jest akışı ve doğrulama kuralları
`Docs/ArenaNet-Protokol.md` §10.11'de; operatör dili anlatımı `Docs/Kullanim-Kilavuzu.md`'dedir.

| # | Yaptığın | Nerede |
|---|---|---|
| 1 | Sahada ölçümü al (kalibrasyon noktaları → duvar köşeleri → kolonlar), bitir | gözlük, sağ kumanda |
| 2 | Sunucu exe'sinin yanına düşen `<İşletme>_dimensions.json`'ı al | sunucu makinesi |
| 3 | Dosyayı mekanın `Venues/<İşletme>/Data/` klasörüne kopyala (var olanı ezer) | Unity projesi |
| 4 | Maketi yeniden üret ve sanatla karşılaştır | `… > Arena > JSON'dan DimensionMesh Üret` |

⚠️ **3. adım bir EZME işlemidir ve geri alınamaz** — kopyalamadan önce mekanın mevcut dosyasının
sürüm kontrolünde temiz olduğundan emin ol. Sunucudaki `<İşletme>_dimensions.prev.json` yalnız **tek
kuşak** yedektir ve bir sonraki ölçüm onu da ezer.

⚠️ **Çıktı yeni bir çerçeve kurmaz:** ölçüm, dosyadaki mevcut `calibration` A/B noktalarının
çerçevesine döndürülerek yazılır — bant sahada sabittir ve sahne sanatı ona kuruludur. Bu yüzden
4. adımda bakılacak şey "maket sanatla örtüşüyor mu"dur; örtüşmüyorsa şüphelenilecek ilk yer duvar
köşeleri değil **A/B noktalarıdır** (`Docs/Gelistirici/Yapma-Listesi.md`, Koordinat).

⚠️ Sunucu bağlı değilken de ölçüm tamamlanır; o durumda dosyanın tek nüshası gözlüğün kendi
depolamasındadır (`Application.persistentDataPath/VenueSurvey/`) ve kabloyla çekilir.

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
