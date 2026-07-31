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
| Kafa vuruşu çarpanı | [4](#4-kafa-vuruşu) |
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
| Gözlüksüz test (dev penceresi) | [15](#15-gözlüksüz-test-dev-penceresi) |
| Bir konumu ağdan paylaşmak | [16](#16-bir-konumu-ağ-üzerinden-paylaşmak-arena-uzayı) |
| Arena ölçüsünü girmek (boyut dosyası) | [17](#17-arena-ölçüsü-boyut-dosyası) |

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
        if (!ArenaCombat.CanFire) return;

        Vector3 dir = muzzle.forward;

        // 2) Atışı bildir: diğer oyuncular namlu alevini/sesini görsün.
        //    Hasarla ilgisi yok, sunucu doğrulamaz — yalnız relay eder.
        ArenaCombat.ReportShot(muzzle.position, dir, "yay");

        // 3) Isabet
        if (!Physics.Raycast(muzzle.position, dir, out RaycastHit hit, range)) return;

        // Hedef bir AĞ OYUNCUSU ise vuruşu bildirir; değilse hiçbir şey olmaz.
        // Dönüş değeri yalnız sunum içindir: gövde efekti mi, duvar efekti mi?
        ArenaCombat.ReportRaycastHit(hit, damage, "yay");
    }
}
```

Bu kadar. Hiçbir DTO kurmadın, hiçbir koordinat dönüşümü yapmadın, hiçbir yere abone olmadın.

> **Neden böyle:** bir vuruşu doğru bildirmek dört şeyi bilmeyi gerektirir — poz *arena uzayına*
> çevrilmeli, **yön bir nokta değildir** (öteleme düşülmeli), hedef bir `RemoteHitBox` üzerinden
> çözülmeli ve hasarı istemci belirler. `ArenaCombat` dördünü de kapsar.
> `ReportRaycastHit` `false` dönerse hedef ağ oyuncusu değildir (dekor, duvar) —
> **hasar uygulanmaz ve yapılacak yerel bir şey yoktur**; istemcide can tutan bir yol YOKTUR.
> Dönüş değerini yalnız sunum için kullan (kan efekti mi, isabet izi mi). Kırılabilir objeler
> ileride ağsal (sunucu-otoriter) olacak → `plan/agsal-kirilabilir-objeler.md`.

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

## 4. Kafa vuruşu

```csharp
if (Physics.Raycast(muzzle.position, dir, out RaycastHit hit, range))
{
    float uygulanan = ArenaCombat.IsHeadshot(hit.collider) ? damage * 2.5f : damage;

    ArenaCombat.ReportRaycastHit(hit, uygulanan, "ak47");
}
```

> **Çarpanı sen uygularsın.** Sunucu gönderdiğin sayıyı aynen kullanır — kafa çarpanı, mesafe
> düşüşü, zırh, hepsi senin tarafında. Bu yüzden denge değişikliği için sunucuya dokunmazsın
> (ama APK build'i gerekir, çünkü sayılar istemcide yaşar).

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

if (ModeRuntime.Weapons == ModeWeaponSource.RandomGrant)
    RafiGizle();

float gecikme = ModeRuntime.RespawnDelay;    // 0 GEÇERLİDİR (anında canlanma)

ModeRuntime.Changed += KurallarDegisti;      // maç yüklenince tetiklenir
```

Okunabilir alanlar: `ModeId`, `Teams`, `Scoring`, `FriendlyFire`, `Revive`, `Weapons`,
`RespawnDelay`, `IsTeamless`.

> **Neden tek okuma noktası:** canlanma, skor satırı, silah kaynağı ve admin arayüzü aynı bilgiyi
> ister. Dördü ayrı ayrı `load_match` dinlerse dördü ayrı ayrı bayatlar.

> ⚠️ **`RespawnDelay == 0` geçerli bir değerdir** (FFA'da öyle). `if (delay > 0)` diye kontrol edip
> varsayılana düşme.

---

## 11. Yeni silah eklemek

**Sunucuda hiçbir iş yoktur ve export gerekmez.** Sunucuda silah tablosu bulunmaz.

1. Prefabı `Assets/_Shared/Arsenal/Prefabs/` altına koy.
2. `WeaponDefinition` SO'sunu `Assets/_Shared/Arsenal/Data/` altına oluştur
   (*Create → VortexArena → Weapon Definition*): `weaponId`, hasar, atış hızı, menzil, saçılım,
   şarjör, `prefab`.
3. Modun kullanmasını istiyorsan `ModeDefinition.loadout` listesine ekle.

`weaponId` yalnızca **kill feed etiketidir** — sunucu doğrulamaz, istediğini yazabilirsin.

> Silahın elde nasıl duracağını `ItemDefinition` (tabandaki) `primaryGripPosition` /
> `primaryGripEuler` alanları belirler, çift ellide ek olarak `secondaryGrip*`. VR'da ince ayar
> buradan yapılır, kod değişmez. ⚠️ **Tek yer, üç tüketici:** aynı ölçü yerel duruşu, uzak
> oyuncudaki çizimi ve kavrama soketinin yerini birlikte besliyor — ikinci bir "duruş" alanı
> açmak biri güncellenip diğeri unutulan bir çift üretir. Çerçeveden seçilen silah (`"rack"`),
> modun verdiği silah (`weaponSource:"random"`) ve elde ISDK ile kavranan eşya **aynı alanları**
> kullanır; ayrım yoktur.

> ⚠️ Denge sayıları istemcide yaşadığı için değişiklik **APK build'i ister** — sunucuyu yeniden
> başlatmak yetmez.

> ⚠️ **Mevcut bir silahın SESİNİ tablodan değiştirdiysen aracı koşmak yetmez.**
> `WeaponKitBuilder` klip alanlarını yalnız **boşsa** yazar (elle sürüklenen klip korunsun diye),
> dolayısıyla dolu bir alan sessizce atlanır ve tablo uygulanmamış bir niyet olarak kalır. Önce
> `WD_<Ad>.asset`'te `fireClips` / `magOutClip` / `dryFireClip` alanlarını boşalt, sonra
> `Build Weapon Prefabs`'i koş — ve sonucu aracın çıktısından değil **asset'ten** doğrula.
> Aynı şey diğer alanlar için geçerli DEĞİLDİR: hasar/rpm/menzil/saçılım her koşuda ezilir.

> Sahnedeki silahın **çerçevesi** için elle iş yoktur: `Build Weapon Prefabs` her `WPN_*` köküne
> `VA_WeaponFrame` örneğini kendisi koyar. Çerçevenin arenada görünüp görünmemesi ayrı bir konudur
> → bir sonraki reçete.

---

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

> ⚠️ Görünürlük **yalnız sunumdur.** Çerçeve görünmez olsa bile silah yine oradan, ≤2 m'den
> nişan alınarak seçilir ve ele klonlanır; alma menzilini ya da kavramayı kapatmaz.

> **Çerçeve yalnız silah SABİT dururken vardır.** Silah hangi yoldan tutulursa tutulsun — ele
> verildi (`WeaponGranter`) ya da doğrudan kavrandı (ISDK) — çerçevenin GameObject'i kapanır;
> bırakılınca geri gelir. Yani elde duran silahta ne çerçeve görseli, ne nişan ışını, ne de
> uzaktan seçim kapısı olur. Bu `isFrameVisible` ile ilgisizdir ve elle kurulum istemez:
> `WeaponFrame` silahın `Weapon.HeldChanged` olayını dinler. Yeni bir "silahı ele alma" yolu
> yazarsan o yola ayrıca bir şey eklemene gerek YOKTUR — kural olayda durur.

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
**`Tools > VortexArena > Export Server Config`** çalıştır.

> ⚠️ Export'u unutursan `start_match` "harita bu modu desteklemiyor" diye **sessizce** reddedilir;
> sebep yalnızca sunucu konsolunda tek satır olarak görünür.

> ⚠️ asmdef üretirken mevcut moddan **JSON'u kopyala, `.meta`'yı KOPYALAMA** — GUID çakışır.

Ayrıntı: `ModeRules` alanlarının tamamı → [Sistem Özeti §3.9](../Sistem-Ozeti.md).

---

## 14. Yeni arena eklemek

`Tools > VortexArena > Create Arena From Template` sihirbazını çalıştır: arenaId, sahne adı,
hedef (Standard / Venue) ve **geometri kaynağı**. Sihirbaz klasörleri, **sahnenin bire bir
kopyasını**, `MapDefinition` asset'ini, katalog kaydını ve Build Settings girdisini üretir.

**Geometri kaynağı (`ArenaGeometrySource`) ZORUNLUDUR** — "geometriye dokunmadan kopyala" diye bir
seçenek yoktur:

| Kaynak | Ne verirsin | Sihirbaz ne yapar |
|---|---|---|
| `DimensionsJson` | Boyut dosyası (`TextAsset`) | Şablonun zemin/duvarını siler, geometriyi dosyadan üretir, dosyayı `ArenaBoundary.dimensionsJson`'a bağlar |
| `TestMesh` | Kaba blok yığınının kök objesi | Bloklardan bir boyut dosyası ÇIKARIP arena kutusunun `Data/` klasörüne yazar, sonrası üstteki satırla birebir aynıdır |

İki yol da aynı noktada buluşur: sahnede sonunda bir boyut dosyası vardır ve `dimensionsJson` +
`wallRenderers` bağlıdır. Ölçü için `ArenaBoundary`'de doldurulacak ayrı bir alan **yoktur** —
tek temsil boyut dosyasıdır ([Reçete 17](#17-arena-ölçüsü-boyut-dosyası)).

⚠️ **Sihirbaz boyut sormaz, geometriyi ölçeklemez.** Kazandırdığı şey sahnenin ağ bileşenlerini
eksiksiz taşıması (`ArenaBoundary`, `ArenaCalibrator` + işaretçiler, `PlayerPoseTracker`,
`RemotePlayerSpawner`, `ModeHudSpawner`, `BaseZone`, `VA_CameraRig`) — arena ölçüsü zaten her
kurulumda baştan alınıyor, orantılı ölçekleme elle düzeltilecek bir yalancı-doğru üretir.

Sonra **elde**: kalibrasyon işaretçilerini yerleştir · `GameObject > VortexArena > Spawn Point` ile
arenanın **tek** başlangıç noktasını koy · NavMesh/ışık bake et · **`Export Server Config`**.

> Arena ölçüsü **sunucuya gitmez** (maps.json'a yalnız `sceneName` + `modes` yazılır); arenanın
> tek ölçü kaynağı **boyut dosyasıdır**. Export'u ise ölçü için değil,
> **yeni `sceneName` tabloya girsin** diye çalıştırıyorsun.

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

## 15. Gözlüksüz test (dev penceresi)

`Tools > VortexArena > Dev` penceresi (kısayol **Ctrl+Alt+R** rolü player↔admin çevirir):

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
uzayında* taşınır — arenada sabit tek bir noktanın (sahnedeki `SpawnPoint`) orijin olduğu ortak
çerçeve.

```csharp
using VortexArena.Core.Arena;

// GÖNDERİRKEN: dünya → arena
Vector3 arenaPos = ArenaSpace.WorldToArena(transform.position);

// ALIRKEN: arena → dünya
Vector3 dunyaPos = ArenaSpace.ArenaToWorld(gelenPoz);
```

`Pose` ve `Quaternion` aşırı yüklemeleri de var.

> ⚠️ **YÖN BİR NOKTA DEĞİLDİR.** Bir yön vektörünü doğrudan `WorldToArena`'dan geçirirsen arena
> orijini kadar kayar. Doğrusu iki noktayı çevirip farkı almaktır:
> ```csharp
> Vector3 a = ArenaSpace.WorldToArena(p);
> Vector3 arenaDir = (ArenaSpace.WorldToArena(p + dir) - a).normalized;
> ```
> `ArenaCombat.ReportShot` bunu zaten doğru yapar — kendi mesajını yazmıyorsan hiç düşünme.

> ⚠️ **Orijin sahnedeki `SpawnPoint`'tir.** Silersen ya da kapatırsan dönüşüm kimliğe düşer
> (`ArenaSpace` sahne başına bir kez uyarır) ve **bütün uzak oyuncular dünya orijinine yığılır**;
> yerini oynatırsan hepsi o kadar kayar.

---

## 17. Arena ölçüsü: boyut dosyası

Her arenanın ölçüsü bir **boyut dosyasına** yazılır (`ArenaDimensions` — elle düzenlenebilir JSON):
`Venues/<İşletme>/…/Data/<ad>_dimensions.json`, ör.
`Arenas/Venues/VortexAntep/Data/vortexantep_dimensions.json`. Sahnede
`ArenaBoundary.dimensionsJson` alanına bağlanır ve **çalışma anında** okunur.
**Aynı dosya geometriyi üretir, muhafazayı besler ve admin kuş bakışı kadrajını verir** — ölçüyü
ikinci bir yere yazma.

⚠️ **Alan tam kare/dikdörtgen bile olsa dört köşeli bir `outline` olarak yazılır.** "Dikdörtgense
şu hızlı yol, değilse çokgen" ayrımı ve ona ait bileşen alanları YOKTUR: aynı ölçünün iki ayrı
ifadesi kaçınılmaz olarak birbirinden sapıyordu (biri düzeltiliyor, öteki eski değerde kalıyordu).

⚠️ **Boyut dosyası zorunludur.** Bağlı değilse ya da okunamıyorsa `ArenaBoundary` bir kez hata
basıp **kendini kapatır** — duvar alfası, alan-dışı karartması ve uyarı çalışmaz. Bu bilinçli bir
seçim: ölçüsü bilinmeyen bir arenada doğru bir muhafaza zaten üretilemez, her karede ekranı
karartmak ise işletmede oyunu tümden oynanamaz kılardı. Yeni bir arena sahnesini ilk açtığında
konsolu oku.

### 17.1 Dosyayı yazmak

```json
{
  "name": "VortexAntep",
  "outline": [
    { "x": 0.00, "y": 0.00 },
    { "x": 8.32, "y": 0.00 },
    { "x": 8.32, "y": 13.23 },
    { "x": 0.46, "y": 13.12 }
  ],
  "wallHeight": 3.0,
  "columns": [
    {
      "name": "Kolon_Orta",
      "center": { "x": 3.605, "y": 7.38 },
      "size":   { "x": 0.67,  "y": 0.38 },
      "yaw": 0,
      "height": 0
    }
  ],
  "defaultColumnHeight": 3.0,
  "columnsBlockPlayer": true
}
```

| Alan | Anlamı |
|---|---|
| `name` | Yalnız etiket (üretilen objelerin adlandırmasında görünür) |
| `outline` | Sıralı sınır köşeleri, **metre**. Çokgen **kapalıdır** — ilk noktayı sona tekrar yazma. Koordinatlar `ArenaBoundary`'yi taşıyan transformun **yerel XZ**'sidir: JSON'daki `y` = dünya **Z**'si |
| `wallHeight` | Üretilen duvarların yüksekliği (m) |
| `columns[]` | `name` + `center` (XZ merkez) + `size` (XZ ölçü) + `yaw` (derece) + `height` (0 = `defaultColumnHeight`) |
| `defaultColumnHeight` | `height: 0` bırakılan kolonların yüksekliği |
| `columnsBlockPlayer` | Açıkken kolonlar muhafaza hesabına da girer — oyuncu gerçek kolona çarpmadan uyarı alır |

> **Yazmadığın alan varsayılanında kalır** (`JsonUtility.FromJsonOverwrite`): yalnız `outline`
> yazıp gerisini atlayabilirsin. Bozuk dosya **exception atmaz** — sahne yüklenmeye devam eder,
> ama muhafaza kapanır ve konsola sebebini yazar.

> ⚠️ **Dosyayı alana bağlamayı unutma — bağlanmayan dosya build'e GİRMEZ.** İçerik çalışma anında
> okunur ve Unity bir `TextAsset`'i yalnız referanslandığı için paketler.

### 17.2 Adım adım

1. **Dosyayı oluştur** → **mekanın** `Data/` klasörüne koy. Dosya fiziksel odayı tarif eder, tek bir
   arenayı değil: aynı mekanın arenası ve lobisi onu birlikte kullanır.
2. **Köşeleri gir** (`outline`): sıralı 2B köşeler, **metre**. Çokgen **kapalıdır** — ilk noktayı
   sona tekrar yazma. Koordinatlar `ArenaBoundary`'yi taşıyan transformun **yerel XZ**'sindedir
   (X = sağ, Y alanı = Z = ileri); ölçüyü bir köşeden alıyorsan o köşe (0,0) olur.
   `wallHeight` = duvar yüksekliği.
3. **Kolonları gir** (`columns`): her biri ad + merkez XZ + ölçü XZ + `yaw` + yükseklik
   (0 bırakılırsa `defaultColumnHeight`). `columnsBlockPlayer` açıkken kolonlar muhafaza hesabına
   da girer — oyuncu gerçek kolona çarpmadan uyarı alır.
4. **Geometriyi üret:** sahnede `ArenaBoundary`'yi taşıyan objeyi ve boyut dosyasını
   (`TextAsset`) seç → `Tools > VortexArena > Build Arena From Dimensions`.
   Üretilen her şey arena kökünün altında açılan **tek bir `ArenaGeometry` dalında** durur:
   `Zemin` (ProBuilder çokgen), `Duvarlar` (kenar başına bir duvar) ve `Kolonlar` (her kolonda
   `ArenaObstacle` ile). Tek dal olmasının sebebi elle konan sahne objeleriyle karışmaması ve tek
   seferde silinebilmesi. Araç **idempotenttir**: dosya değişince yeniden çalıştır, eski dal
   silinip yeniden kurulur. ⚠️ Kök **`ArenaBoundary`'yi taşıyan transform olmalıdır** — koordinatlar
   onun yerelidir, başka bir objenin altına üretmek planı sessizce kaydırır.
5. **Muhafazaya bağla:** dosyayı `ArenaBoundary.dimensionsJson` alanına, `wallRenderers` = üretilen
   duvarlar. (Araç ikisini de kendisi bağlar; elle kurduysan kontrol et.)

> Yeni arenayı sihirbazla açıyorsan bu adımlar kendiliğinden olur: seçtiğin geometri kaynağından
> şablonun zemin/duvarı silinip geometri üretilir, `dimensionsJson` + `wallRenderers` bağlanır.

> ⚠️ **`ArenaObstacle` collider DEĞİLDİR** — fizik yapmaz, hiçbir şeyi durdurmaz. Free-roam'da
> oyuncuyu durduran şey gerçek nesnedir; bileşenin tek işi muhafazanın o engele yaklaşırken
> uyarmasıdır. Dosyada yazmayan, sahneye elle konan kasa/direk için de aynısı geçerlidir: objeye
> ekle, `size` alanına zemindeki ölçüsünü yaz.

> ⚠️ **Plan sıfırı ile arena sıfırı ayrı şeylerdir.** Plan koordinatları `ArenaBoundary`
> transformunun yerelidir; ağ koordinatlarının sıfırı ise `SpawnPoint`'tir. Duvarı büyütmek ya da
> kaydırmak ağ uzayını bozmaz — bu ayrım bilinçlidir.

### 17.3 Boyut dosyasını kaba maketten çıkarmak (TestMesh)

Alanı sayı yazarak değil, kabaca modelleyerek çıkardıysan (tek kök altında MeshRenderer taşıyan
basit quad/blok yığını — "TestMesh") dosyayı elle yazmana gerek yok: kaynağı ve
`ArenaBoundary`'yi taşıyan objeyi seç → `Tools > VortexArena > Build Arena From TestMesh`.
Araç bloklardan bir plan çıkarır, **onu boyut dosyası olarak diske yazar**
(`<sahneAdı>_dimensions.json`, arena kutusunun `Data/` klasörüne) ve geometriyi o dosyadan üretir.

⚠️ **TestMesh ayrı bir üretim yolu DEĞİLDİR** — boyut dosyasının otomatik yazılma biçimidir. Bu
yüzden çıkardığın ölçüyü sonradan dosyada elle düzeltip
`Build Arena From Dimensions` ile yeniden çizebilirsin, ve çalışma anında muhafazanın okuduğu ölçü
de üretilen geometriyle aynı dosyadan gelir.

Sınıflandırma önce **ad ipucuna** bakar, ad bir şey söylemiyorsa **geometriye**:

| Sonuç | Ad ipucu | Ad yoksa geometri |
|---|---|---|
| Zemin | `zemin` · `floor` · `ground` · `taban` | Yassı kutu (yüksekliği taban ölçüsünün yanında ihmal edilebilir) |
| Duvar | `duvar` · `wall` | Yatay ayak izinin uzun kenarı kısa kenarından belirgin şekilde uzun |
| Kolon | `kolon` · `column` · `sutun` · `sütun` | Kalan her şey |

Sınır çokgeni (`outline`) **zemin parçasının gerçek mesh sınırından** çıkarılır: düz bir quad ya da
`extrude = 0` bir poly-shape çizdiysen L/yamuk şekli olduğu gibi korunur. Zemin **kapalı bir katı**
ise (ProBuilder küpü) parçanın kendi yönelimli dikdörtgeninden dört köşe üretilir — kare/dikdörtgen
alanların beklenen yoludur, hata değil. Kolonlar **kendi frame'lerinde** ölçülür ve dönüşleri `yaw`
alanında korunur, yani arena köküne göre döndürülmüş bloklar şişmez.

> Şekli korunsun istediğin zemini **quad / `extrude = 0` poly-shape** olarak çiz; katı bir küple
> modellersen dosyaya dört köşe yazılır (kutunun kendisi doğru ölçüdedir, ama girinti/çıkıntı
> kaybolur). Çıkan dosyayı her zaman elle düzeltebilirsin.

> Adlandırma ipucu ölçüden güçlüdür ve modelciye maliyeti sıfırdır: blokları `Duvar_Kuzey`,
> `Kolon_Orta`, `Zemin` gibi adlandır — sınıflandırma tahmine kalmasın.
