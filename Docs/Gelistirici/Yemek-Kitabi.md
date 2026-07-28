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
| Sunucusuz / gözlüksüz test | [15](#15-sunucusuz-ve-gözlüksüz-test) |
| Bir konumu ağdan paylaşmak | [16](#16-bir-konumu-ağ-üzerinden-paylaşmak-arena-uzayı) |

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

        // Hedef bir AĞ OYUNCUSU ise vuruşu bildir; değilse yerel hasar yolu.
        if (!ArenaCombat.ReportRaycastHit(hit, damage, "yay"))
        {
            hit.collider.GetComponentInParent<Health>()?.TakeDamage(damage);
        }
    }
}
```

Bu kadar. Hiçbir DTO kurmadın, hiçbir koordinat dönüşümü yapmadın, hiçbir yere abone olmadın.

> **Neden böyle:** bir vuruşu doğru bildirmek dört şeyi bilmeyi gerektirir — poz *arena uzayına*
> çevrilmeli, **yön bir nokta değildir** (öteleme düşülmeli), hedef bir `RemoteHitBox` üzerinden
> çözülmeli ve hasarı istemci belirler. `ArenaCombat` dördünü de kapsar.
> `ReportRaycastHit` `false` dönerse hedef ağ oyuncusu değildir (pratik dummy'si, kırılabilir
> obje) — onların canı sunucuda tutulmaz, eski yerel `Health` yolu geçerlidir.

> ⚠️ **Canı yerelde düşürme.** `ReportHit` yalnızca *bildirir*. Hedefin canı sunucudan
> `health_update` ile geri gelir ve `Health.ApplyServerHealth` ile uygulanır. Yerelde düşürürsen
> hasar iki kez uygulanmış gibi görünür ve iki istemci farklı can görür.

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
        if (ArenaCombat.TryGetTargetPlayerId(other, out int playerId))
        {
            ArenaCombat.ReportHit(playerId, transform.position, damage, "mermi");
        }
        else
        {
            other.GetComponentInParent<Health>()?.TakeDamage(damage);
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

    if (!ArenaCombat.ReportRaycastHit(hit, uygulanan, "ak47"))
        hit.collider.GetComponentInParent<Health>()?.TakeDamage(uygulanan);
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
.PlayerId / .SpawnSlot / .StatusText`.

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

> **Silah rafsız modlar için** (`weaponSource:"random"`, ör. FFA) `WeaponDefinition` üzerindeki
> `grantedHoldPosition` / `grantedHoldEuler` alanları silahın elde nasıl duracağını belirler.
> VR'da ince ayar buradan yapılır, kod değişmez.

> ⚠️ Denge sayıları istemcide yaşadığı için değişiklik **APK build'i ister** — sunucuyu yeniden
> başlatmak yetmez.

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
boyut, takım başına spawn sayısı, hedef (Standard / Venue). Sihirbaz klasörleri, sahne kopyasını,
duvar/zemin/spawn ölçeklemesini, `MapDefinition` asset'ini, katalog kaydını ve Build Settings
girdisini üretir.

Sonra: sanat rötuşu **elde**, ardından **`Export Server Config`**.

Sahnede bulunması gerekenler → [Sahne Kurulumu](Sahne-Kurulumu.md).

> ⚠️ **Sahne adı = katalog anahtarıdır.** `load_match` bu string'i taşır ve Build Settings'teki
> adla boşluk/harf farkı dahil birebir eşleşmelidir. Sonradan değiştirme.

---

## 15. Sunucusuz ve gözlüksüz test

`Tools > VortexArena > Dev` penceresi (kısayol **Ctrl+Alt+R** rolü player↔admin çevirir):

| Düğme | Ne yapar |
|---|---|
| **Rol** | player / admin — sahne kirletmeden, `EditorPrefs`'te kişisel kalır |
| **Hedef** | Sunucu adresi (`dev-targets.json`'dan gelir: Local, Keşif, örnek PC) |
| **Play başlangıcı** | Boot'tan mı, açık sahneden mi |
| **Sentetik maç** | Mod, takım, spawn slot, raund süresi, skor limiti — **sunucu olmadan** `load_match` enjekte eder |
| **N Bot / N Bot + Admin** | Sentetik oyuncu süreçleri başlatır (poz + atış) |
| **Derle** | `dotnet build` |

Sunucusuz oturumda mod kuralları `ModeDefinition`'daki önizleme alanlarından okunur.

> ⚠️ **Sunucu editörden yönetilmez** — dev penceresinde başlat/durdur düğmesi yoktur. Sunucu her
> zaman elle çalıştırılır ve elle kapatılır.

> ⚠️ **Sapmada sunucu kazanır.** `ModeDefinition`'daki kural alanları yalnız önizleme içindir;
> gerçek bir `load_match` geldiği anda ezilirler.

---

## 16. Bir konumu ağ üzerinden paylaşmak (arena uzayı)

Her oyuncunun fiziksel odası farklı yerdedir. Ağda dolaşan **her** konum bu yüzden *arena
uzayında* taşınır — arena merkezinin orijin olduğu ortak çerçeve.

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

> ⚠️ **`ArenaBoundary`'yi devre dışı bırakma.** Arena orijinini o kaydeder; kapatırsan kayıt
> silinir ve **bütün uzak oyuncular dünya orijinine yığılır.**
