---
title: Yapma Listesi
---

# Yapma Listesi

Pahalıya öğrenilmiş tuzaklar. Ortak özellikleri: **hiçbiri hata vermez** — sessizce yanlış çalışır.

> Bir şey "çalışması gerekirken çalışmıyorsa" önce buraya bak.

---

## Free-roam kuralları

### ⛔ Rig'i, kamerayı, oyuncuyu taşıma

Oyuncu fiziksel olarak yürüyor. Işınlanma, knockback, "spawn noktasına götür", "duvardan geri it" —
hiçbiri yok. Ölüp canlanmak bile bir **durum** değişimidir, konum değişimi değil.

Ölünce dönülecek bir "başlangıç noktası" da yoktur: oyuncu **taban bölgesine** (`BaseZone`) kendi
ayaklarıyla yürüyerek canlanır.

### ⛔ Harita değişiminde oyuncuyu "yeniden doğurma"

`load_match` oyuncu için yalnız bir sahne değişimidir. Kimse başlangıç noktasına götürülmez ve
**kalibrasyon sıfırlanmaz**: yeni sahnenin `ArenaCalibrator`'ı kayıtlı `OVRSpatialAnchor`'dan
hizalamayı geri yükler, oyuncu fiziksel olarak nerede duruyorsa orada kalır.

Ön koşul: aynı işletmede oynanan arenaların **zemin işaretleri aynı yerde** olmalı — anchor
fiziksel dünyada sabittir, sanal işaretler sahneden gelir.

⚠️ Sunucu tarafında da `calibrated` **korunur** (§10.6). Harita değişiminde sıfırlarsan her
`load_match` tüm oyuncuları savaş dışı bırakır (ateş edemez, hasar yemez, canlanamaz).

### ⛔ Kalibrasyon durumunu istemcide "doğru kabul etme"

"Kalibreli miyim" sorusunun cevabı sahnedeki `ArenaCalibrator`'ın kendi sayacı DEĞİL,
`CalibrationState.IsCalibrated`'tır (sunucudan, `lobby_state` ile gelir). Operatör admin
ekranından kalibrasyonu sıfırlayabilir ve o an başlık hâlâ kendini hizalı sanıyor olabilir.

Aynı sebeple: bir oyuncu durumuna savaş kapısı eklerken **o durumu değiştiren tüm yolları ara**.
Kalibrasyon yasağı canlandırmanın **iki yolunda birden** duruyor (oyuncunun `revive_request`'i ve
yalnızca `revive_request`); ikinci bir yol eklenip yasak orada tekrarlanmazsa kural sessizce
işlevsizleşir — hata da vermez.

### ⛔ Arena yerleşimini `ArenaBoundary`'den bağımsız kaydırma

Arena uzayı **dünya uzayıdır**: ağa giden/gelen tüm pozların sıfırı sahnenin dünya sıfırıdır ve
telde bunu telafi eden bir origin yoktur. Yerleşimin referansı **`VA_ArenaBoundary`'dir**: varsayılan
yerleşim dünya orijinidir; hazır bir environment'ın içinde bölge oynatılacaksa boundary (altındaki
maketiyle) o bölgenin üstüne **bilinçli** taşınır ve kalibrasyon oyuncuları oraya hizalar — bu
meşrudur. Yasak olan, **sanat ile boundary'yi birbirinden bağımsız** kaydırmak/döndürmektir:
muhafaza, kalibrasyon işaretçileri ve kadraj boundary'yi izler, sanat izlemez — ayrışırlarsa oyuncu
fiziksel alanda yanlış yere göre kalibre olur ve hata ancak sahada görünür.

Aynı sebeple oynanan zemin **boundary'nin Y'sinde** (varsayılan: dünya y=0) durmalı: uzak
avatarların kökü arena koordinatına oturur → sanat zemini işaretçilerin zemininden yukarıdaysa
herkes o yükseklik kadar havada durur. `VA_CameraRig`'in sahnedeki kökü de Y=0'dadır
(tracking origin `Stage` onu fiziksel zemin sayar; kalibrasyon rig'i zaten taşır).

### ⛔ Muhafazayı susturmak için `ArenaBoundary` bileşenini kapatma

Kapatılan bileşen alan-dışı karartmasını son yazdığı değerde dondurur ve planı çözmeyi bırakır —
admin kuş bakışı kadrajı onun `HalfExtents`/`LocalCenter` değerlerini okuyor. Doğrusu
`SetSpectatorMode(true)`: uyarıyı keser, bileşeni ayakta tutar (admin gözlemci bunu kullanır).

### ⛔ Arena ölçüsünü boyut dosyası dışında bir yere yazma

Ölçünün tek temsili `ArenaBoundary.dimensionsJson`'a bağlanan boyut dosyasıdır ve dosya **mekan
başınadır** — aynı işletmenin ikinci arenası için kopya çıkarma. Alan tam kare olsa bile dört
köşeli bir `plane` halkası olarak yazılır. Bileşene "kısa yol" bir yarım-ölçü alanı geri eklemeye
kalkma — aynı ölçünün iki ifadesi kaçınılmaz olarak birbirinden sapar. Dosyayı yazıp **alana
bağlamayı unutmak** da sessiz değil, yıkıcıdır: dosya build'e girmez ve muhafaza konsola hata basıp
tümden kapanır.

### ⛔ Muhafazaya duvar Renderer'ı bağlamaya çalışma

Yarı saydam duvar göstergesi kaldırıldı ve **geri eklenmez**. Environment'ın gerçek duvarlarına
bağlanamaz da: alfa yazımı yalnız Transparent malzemede iş görür (gerçek duvarlar opak) ve
mekanizma alfa düşünce Renderer'ı kapatırdı — oyuncu uzaktayken duvar tümden kaybolurdu. Yaklaşma
uyarısı artık HMD'ye bağlı karartma quad'ından geliyor (`warnFadeAlpha`), arena geometrisinden
tümden bağımsız.

### ⛔ Ölü oyuncuları uzak oyuncu listesinden eleme

Ölüm bir durum değişimi olduğu için ölünün bedeni sahada durmaya devam eder. Çarpışma/yakınlık
riski canlı oyuncuyla aynıdır.

---

## Otorite

### ⛔ Canı yerelde düşürme

```csharp
avatar.hp -= 25f;                              // ❌ iki istemci farklı can görür
ArenaCombat.ReportHit(id, nokta, 25f, "ak47"); // ✅ sunucu düşürür, health_update ile döner
```

Aynısı skor, ölüm sayısı ve maç fazı için de geçerli — hepsi sunucudan gelir.

### ⛔ Yerel can havuzu yazma

```csharp
public class Kirilabilir : MonoBehaviour { private float hp = 100f; }   // ❌ ikinci doğruluk kaynağı
if (!ArenaCombat.ReportRaycastHit(hit, 25f, "ak47")) { /* hiçbir şey yapma */ }   // ✅
```

İstemcide `hp` alanı tutan bir bileşen (kaldırılan `Health` gibi) **yazılmaz**: iki istemci farklı
can görür, kimin haklı olduğunu söyleyecek bir merci kalmaz. `ReportRaycastHit` `false` dönmesi
"hedef ağ oyuncusu değil, hasar yok" demektir — dönüş değeri yalnız sunum kararıdır. Ağa bağlı
olmayan geometri (duvar, dekor) hasar almaz; hasar alması gereken her şey ağsal olur (`NetIdentity`).

### ⛔ Ağ nesnesinin durumunu istemcide yazma

```csharp
netObject.Flags |= NetObject.FLAG_BROKEN;                          // ❌ yalnız SENDE kırılır
ArenaCombat.ReportObjectHit(netId, nokta, 25f, "ak47");            // ✅ sunucu karar verir
```

`NetObject`'in `Hp`/`Flags`'i **sunucudan gelen** `object_state`/`world_state` ile yazılır; senin
işin `StateChanged`'i dinleyip sunumu (collider, materyal, ses) güncellemektir. "Ben vurdum, ben
kırayım" kısayolu iki başlıkta farklı siper üretir ve fark tam da oyuncunun arkasına saklandığı
şeyde ortaya çıkar. Aynı sebeple **yerel bir "kırıldı" bayrağı** da tutulmaz — ikinci doğruluk
kaynağıdır. Kırılabilir obje için **yerel can bileşeni** (kendi `Health`/`Destructible`
MonoBehaviour'ın) da yazılmaz: can tek defterde, sunucuda durur; sunum `BreakableObject` ya da
kendi `StateChanged` aboneliğin üzerinden yapılır.

Aynısı **sahiplik, aşama ve doğuş** için de geçerlidir: `Owner`/`Stage`'i istemci yazmaz —
kavramanın cevabı yayınlanan `object_state.owner`'dır (istemci yalnız **iyimser** kavrar ve sahip
başkası çıkarsa geri alır), aşamayı sunucu yazar, istemci yalnız `object_event` bildirir. ⚠️
**İstemcinin "spawn et" mesajı YOKTUR ve eklenmez:** doğuşun iki kaynağı moddur ve türün kuralıdır;
üçüncüsü açılırsa "sunucu icat etmez" kuralı anlamını yitirir ve bozuk bir başlık arenayı objeyle
doldurabilir.

### ⛔ Obje pozunu `0x02`/`0x04`'e taşımaya kalkma

Obje pozu yalnız `0x09` (yukarı) ve `0x05`'in obje bölümünde (aşağı) taşınır. `0x02`/`0x04` **geri
düşüş yoludur** ve düzeni sabittir; oraya bir bölüm eklemek bu sefer onları kırar. Geri düşüşte obje
pozunun düşmesi bilinçlidir: kaybolan şey objenin son pozu değil hareketinin akıcılığıdır —
dinlenme pozu güvenilir WS kanalından gelir.

### ⛔ Sunucuda ikinci kilit açma

Maç durumunun tek kilidi `MatchDirector._gate`'tir. Ona bağlı yaşayan tablolar (`WorldObjectTable`)
**kendi kilidini açmaz**; metotları `…Locked` adlanır ve kilidi tutmak çağıranın sözleşmesidir. İki
kilit deadlock adayıdır ve kilitlenme sahada "sunucu dondu" diye görünür — sebebi aylar sonra
bulunur. UDP alım thread'i ise `_gate`'e **hiç girmez** (kilitsiz `volatile` okuma deseni,
`Docs/ArenaNet-Protokol.md` §10.3).

### ⛔ `if (modeId == "ffa")` zinciri yazma

Modun şekli telden gelir. `ModeRuntime`'dan oku. Zincir yazarsan her yeni mod senin kodunu
değiştirir ve dört ayrı yerde ayrı ayrı bayatlar.

### ⚠️ `RespawnDelay == 0` geçerli bir değerdir

FFA'da öyle: bekleme yerine "sabit dur" şartı işler. `if (delay > 0)` deyip varsayılana düşme.

### ⚠️ Kazanan iki kanaldan biriyle gelir

Takım skorlu modlarda `match_end.winnerTeam`, bireysel skorlu modlarda `winnerPlayerId`.
Hangisine bakacağını `ModeRuntime.Scoring` söyler. Bir mod ikisini birden doldurmaz.

### ⛔ Operatörü bekleyen duraklamalara zaman aşımı ekleme

Turnuvada tur sonu beklemesi (`RoundStage.Review`) ve `finished` ekranı **bilerek** süresizdir:
ikisi de tam olarak operatör skoru ve oyuncu tablosunu okusun diye durur, sayaçla açılan bir kapı
tabloyu tam okunurken elinden alır. Takılan akışın çıkışı sayaç değil operatördür (`mode_continue` ·
`end_match` · `abort_match` · `return_to_lobby`). Gerekçe: `Docs/Sistem-Ozeti.md` §3.8.2.

### ⛔ Turu ilerleten kararı incelemeye taşıma

Puan, galibiyet limiti, tur tavanı ve maç saati kararları turu **kapatan** yerde verilir
(`TournamentMode.EndRound`), bekleme basamağında değil. İncelemeye taşınan bir karar, maçın sonucunu
bir düğmeye ne zaman basıldığına bağlar.

---

## Koordinat

### ⚠️ Yön bir nokta değildir

```csharp
ArenaSpace.WorldToArena(dir);                                  // ❌ orijin kadar kayar
(ArenaSpace.WorldToArena(p + dir) - ArenaSpace.WorldToArena(p)).normalized;  // ✅
```

`ArenaCombat.ReportShot` bunu zaten doğru yapar.

### ⚠️ Ağdan gelen her poz arena uzayındadır

`RemotePlayerRegistry.GetInterpolatedPose` ve `ShotFiredMsg.muzzlePos` dünya koordinatı **değildir**.
`ArenaSpace.ArenaToWorld` ile çevir.

---

## Sahne ve prefab

### ⛔ Arayüz prefabındaki bir ögeyi SİLME

Arayüzün tamamı `_Shared/App/Resources/UI/` altında prefabtır ve her öge kök bileşende bir
`[SerializeField]` alanına bağlıdır (`scoreRedText`, `hpFill`, `_modeDropdown`…). Ögeyi silersen
alan boşalır ve **hiçbir hata çıkmaz — o parça sessizce çizilmez.** Gizlemen gerekiyorsa objeyi
devre dışı bırak ya da alfasını sıfırla.

Aynı sebeple prefabı `Resources/` klasöründen **çıkarma**: sahneye konmuyorlar, çalışırken
`Resources.Load` ile yükleniyorlar. Taşınırsa o arayüz hiç doğmaz (konsola
`… prefabı bulunamadı` düşer).

### ⛔ Arayüz düğmesinin `onClick`'ini inspector'dan doldurma

Prefablarda bilerek boştur; davranış çalışırken koddan bağlanır (`WireButtons` / `Initialize`).
Geri çağrıların çoğu **koşulludur** — iki adımlı onay (`AT`, `KAL`, toplu kalibrasyon sıfırlama),
maç sürerken kilitlenen mod/harita satırları, faza göre komut değiştiren DURAKLAT/DEVAM.
Inspector'dan eklenen kalıcı bir kayıt bu koşulları **atlar**: oyuncu satırındaki `AT` düğmesi
"EMİN?" adımını geçip doğrudan atardı.

Ayrıca hedef statik değildir — satır her `Bind`'da başka bir oyuncuya bağlanır; kalıcı bir kayıt
yanlış oyuncuya komut gönderir.

→ ayrıntı ve düzenleme kuralları: **[Arayüz Tasarımı](Arayuz-Tasarimi.md)**

### ⛔ `BaseZone`'u gizlemek için yalnız bileşeni kapatma

Bileşeni kapatmak görsel taban şeridini ekranda bırakır. Gizlemen gerekiyorsa **bileşeni** kapat
(`zone.enabled = false`) **ve** Renderer'lı çocukları ayrıca gizle — ama bunu **elle yazma**: kararın tek yeri
`BaseZoneVisibility`'dir (kapı takım kipi; `WeaponGranter`'ın silah süpürmesiyle ilgisi yoktur).

İkinci yüzü: **kapalı bir `BaseZone` canlanma için açık sayılmaz.** `Update` koşmadığı için
`IsPlayerInside` donar — açık sayılsaydı oyuncu bölgeye girse de hiç canlanamazdı (sunucuda
kendiliğinden işleyen bir emniyet ağı yok; geriye kalan tek çare operatörün elle canlandırmasıdır).

### ⛔ Taban bölgesinin boyutunu sayı alanıyla ayarlamaya çalışma

`BaseZone`'da ölçü alanı YOKTUR: algılama alanı **altındaki şeridin kapladığı dikdörtgendir**
(bölgenin kendi yerel XZ'sinde ölçülür, yükseklik yok sayılır). Bölgeyi büyütmek, daraltmak,
döndürmek ya da kaydırmak istiyorsan **şerit mesh'ini** öyle yap; sonucu bölgeyi seçince çizilen
Gizmo'dan gör. Sayı alanı olsaydı görselle sessizce sapardı — oyuncu kırmızının üstünde dururken
canlanamaz olurdu ve hiçbir yerde uyarı çıkmazdı.

⚠️ Aynı sebeple **şeridi silme, Renderer'sız bölge bırakma**: ölçü alınamayan bölge bir kez hata
basıp kendini kapatır ve "açık taban yok" fail-open'ı devreye girer — belirtisi "taban çalışmıyor"
değil, herkesin arenanın her yerinde canlanmasıdır.

### ⛔ `ArenaObstacle`'ı collider sanma

Fizik YAPMAZ, collider EKLEMEZ, hiçbir şeyi durdurmaz. Free-roam'da oyuncuyu durduran şey gerçek
dünyadaki nesnedir; bileşenin tek işi `ArenaBoundary`'ye "burası engel" demek — oyuncu kolona
yaklaşırken duvar uyarısını alsın diye. Ölçü `size` alanından gelir, transform scale'inden değil.

Tekil engel işaretlemek arena ölçüsünün yerini tutmaz: sınırın kendisi arenanın **boyut
dosyasından** gelir ve o dosya `ArenaBoundary.dimensionsJson` alanına bağlanır.

### ⛔ Görsel hizalamak için silahın `Muzzle` nesnesini oynatma

`Muzzle` bir efekt yuvası değil, **atışın kendisinin çıkış noktasıdır**: `Weapon` hitscan ışınını
oradan atar, `ArenaCombat.ReportShot` uzak oyunculara o konumu bildirir (karşı taraf mermi izini
oradan çizer) ve siperden ateş kapısı `IsWeaponBlocked` yine o konum + `forward` ile sorulur.
Namlu alevi yanlış yerde diye kaydırılırsa mermi silahın gövdesinin içinden çıkmaya başlar,
üstelik hiçbir şey hata vermez — kimse fark etmeden isabet geometrisi bozulur.

Yeri **namlu ucudur** (`AR_B_Barrel` mesh'inin ileri sınırı); efekt kayması efektin kendi
ofsetinden düzeltilir, `Muzzle`'dan değil. VFX Graph namlu alevinde bu ofsetler `VisualEffect`
inspector'ında **açığa çıkarılmış özellik** olarak durur (`Alev Ofseti`, `Parlama Ofseti`) —
grafiği açmadan elle ayarlanır. Alev mesh'i **kendi ekseni boyunca ileri uzar**, yani `Set Size`
büyütmek onu ileri de taşır: "alev çok önde" şikâyetinin sebebi genelde ofset değil boydur.

⚠️ **Alev mesh'inin ekseni ve UV'si namluyla kendiliğinden hizalı DEĞİLDİR** — `MuzzlePlanes`
uzunluğunu `-X`'te taşır (namlu `+Z`'dir, aradaki fark açı bloğunun `Y` bileşeniyle kapatılır) ve
`V` eksenini uzun eksene ters bindirir: dokunun parlak kökü mesh'in **uzak ucuna** düşer. Ters
kalırsa alev namludan kopuk, ileride bir leke gibi görünür; düzeltmesi çıktının `uvMode`'unu
`ScaleAndBias`'a alıp `uvScale.y = -1`, `uvBias.y = 1` vermektir — geometriyi kaydırmak DEĞİL
(kaydırma boyla ölçeklenmediği için `Set Size` her değiştiğinde yeniden bozulur). Yeni bir alev
mesh'i getirildiğinde ikisi de yeniden kontrol edilir.

### ⛔ Sahneye elle kalibrasyon işaretçisi koyma

`anchor_a`/`anchor_b` tektir ve ölçü maketinin (`<Mekan>_DimensionMesh`) altındadır; sahneye
aynı adla ikinci bir obje koymak "hangisine hizalandık" sorusunu sahneye bakarak cevaplanamaz
yapar. İşaretçi üreten tek yer `JSON'dan DimensionMesh Üret`, konumlarının otoritesi ise boyut
dosyasının `calibration` alanıdır (`ArenaCalibrator` her `Start`'ta oradan oturtur — sahnede
sürüklemenin kalıcı etkisi yoktur, düzeltmeyi `DimensionMesh'i JSON'a Çevir` ile geri yaz).

⚠️ Maketi sahneden silme: kalibrasyon işaretçileri onunla gider, sahne fiziksel alana hizalanamaz.

### ⛔ TMP fallback fontunu listeden çıkarma

Ana font `LiberationSans SDF` **statik atlaslıdır ve Türkçe `ı ş ğ İ` gliflerini İÇERMEZ** —
`ö ü ç` vardır, o yüzden eksik ilk bakışta görünmez: metin patlamaz, yalnız o harfler **kutu (□)**
çizilir ("taraf□ndan"). Boşluğu `Assets/_Shared/App/UI/Fonts/LiberationSans SDF - Fallback`
(dinamik atlas) doldurur ve **iki yere birden** bağlıdır: ana fontun `Fallback Font Assets`
tablosu + `TMP Settings > Fallback Font Assets` (ikincisi Meta SDK'nın Roboto'su gibi başka bir
fontla yazılmış metni de kurtarır). Biri boşaltılırsa ya da tabloya **null bir satır** bırakılırsa
oyun içindeki her Türkçe metin sessizce kutulanır.

⚠️ Yeni bir font asset'i eklersen fallback'i ona da bağla; statik atlas ürettiğinde eksik glifi
**derleme değil, gözlükteki metin** söyler.

### ⚠️ Arena sahnelerinde `EventSystem` yoktur

Yalnız Lobby'de bir tane var. Sahnene UI düğmesi koyup "tıklanmıyor" diyorsan sebebi budur.
Proje **Input System-only**: modül `InputSystemUIInputModule` olmalı — `StandaloneInputModule`
runtime'da patlar. İki etkin `EventSystem` de girdiyi ikiye böler.

### ⚠️ `VA_CameraRig`'in üç kamerası da `MainCamera` etiketli

Left/Right/CenterEye. `Camera.main` hangisini döndüreceği **garanti değildir**. Kafa transformu
gerekiyorsa rig'in `centerEyeAnchor`'ını kullan:

```csharp
OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
Transform kafa = rig != null ? rig.centerEyeAnchor : null;
```

### ⚠️ Layer 11 (`Breakable`) ve 12 (`PlayerHitbox`) boş DEĞİL, REZERVE

İkisi de adı konmuş sonraki işlere aittir. "Boş görünüyor" diye başka bir amaç için kullanırsan o
iş geldiğinde sahnelerdeki katman numaraları sessizce yanlış şeyi işaretler — katman numarası
sahnelerde sayı olarak saklanır, yeniden adlandırmak eski sahneleri düzeltmez.

### ⛔ `.meta` dosyası kopyalayarak asmdef/asset üretme

GUID çakışır ve Unity referansları rastgele koparır. JSON'u kopyala, `.meta`'yı Unity üretsin.

### ⛔ İçi boş klasör açma (ne araçla ne elle)

Git klasör değil dosya izler: boş klasör commit'e girmez, klonda **yoktur** ve geride yalnız
ona ait yetim bir `.meta` kalır — "bende var, sende yok" biçiminde ortaya çıkar. Klasör, içine
ilk dosya girdiğinde açılır.

### ⛔ `_Shared` köküne asmdef'siz script koyma

`Assembly-CSharp`'a düşer, hiçbir asmdef göremez.

---

## Kod ve assembly düzeni

### ⛔ Namespace'i asmdef adından ayırma, tipi global namespace'te bırakma

Kural: asmdef adı `VortexArena.<Katman>`, namespace **birebir aynı**, asmdef'in `rootNamespace`'i
dolu. Global namespace'te tip bırakmak iki asmdef aynı adı üretince çözülmesi zor bir çakışma
doğurur; ayrıştığında ise "aynı ada iki farklı tip" hatası kodun bir katman yukarısında patlar.
Serialize edilen ikincil tipler kendi dosyasında durur (`Team.cs` gibi) — Unity dosya adına göre
script çözümlediği için bir dosyaya sıkıştırılmış enum/`[Serializable]` sınıf yeniden
adlandırmalarda referansı sessizce koparır.

### ⛔ Core'a URP referansı ekleme

`Unity.RenderPipelines.Universal.Runtime` `VortexArena.Core`'un bağımlılık listesinde YOKTUR ve
geri eklenmez: oyun kodunun render pipeline'ına bağlanması Core'u pipeline değişimine ve
platform-özel derlemeye bağlar. Editor asmdef'leri `includePlatforms:["Editor"]` ile sınırlıdır
ve yalnız kendi runtime'ını referanslar — `Core.Editor`'ün ProBuilder bağımlılığı bu yüzden
runtime'a **bulaşmaz**.

### ⚠️ "`_Shared` mi, kutu mu" sorusunun tek testi

*"İkinci bir mod ya da arena bunu aynen kullanır mı?"* — evet ise `_Shared`, hayır ise kendi
kutusu (`Modes/<Mod>/`, arena kutusu). Emin olmadan `_Shared`'a koymak ortak katmanı tek bir
modun varsayımlarıyla kirletir; kutuya koymak ise ikinci kullanıcı çıkınca kopyalamayı davet eder.

---

## Silah ve kavrama

### ⚠️ `PitchBase`'i 1.00'dan kaydırma

Perde kaydırması sesi "farklı silah" yapmaz, yalnız **ödünç alınmış klibi maskeler**: silahın
kendi klibi bağlanmadığı sürece kulak tanıdık sesi tanımaya devam eder. Doğrusu klibi
`WD_*.asset`'in Inspector'ına sürüklemektir — silah seslerinin tek doğruluk kaynağı orasıdır.

### ⛔ Tutulabilir bir türün prefabında kavrama pozunu serbest bırakma

Ağ nesnesi ele **kanonik kavrama poziyle** bağlanır ve duruş telde gitmez: iki uç aynı kaydı okur.
Serbest kavrama (elin objeye değdiği yerden tutmak) her istemcide farklı bir ofset demektir ve obje
uzak başlıkta elin yanında durur. Kavrama stüdyoda yazılır, çalışma anında ölçülmez.

### ⛔ Mesafeli kavrama bileşenini "nasılsa filtreliyorum" diye prefabda bırakma

Alma yolu `ProximitySocket` / `WristHolster` / `None` olan bir eşyanın prefabında
`DistanceGrabInteractable` ya da `DistanceHandGrabInteractable` **bulunmaz**. Aday listesini
kapatmak objeyi alınamaz yapmaz: boş listeyle bile interactor hover'a girer ve `Select()` kavrama
basışını hiçbir şey seçmeden kuyruktan düşürür — basış sessizce yenir ve belirti, kavranmak istenen
objede değil **yakınındaki başka bir objede** "kavrama tuşu bazen çalışmıyor" olur. Hazırlık
panelindeki *Eşya alma yolu ↔ prefab* satırı bunu listeler ama **düzeltmez**.

### ⛔ Tutulan ağ nesnesinde eşya baytını doldurma

`WorldSingle` bir eşya elde tutulurken `itemL`/`itemR` **`0` kalır**. Baytı da yazarsan uzak elde
**iki obje** çizilir — biri ağ nesnesinin kendi örneği, biri baytdan üretilmiş klon — ve ikisi
gecikmede ayrışır. Bastırma kaynaktadır (`HeldItems` slotu, `ItemDefinition.IsWorldSingle`);
tüketici tarafında ayrıca "bu objeyi çizme" dalı açılmaz, ilk unutulan yerde geri gelir.

---

## Serialize edilen veriler

### ⛔ Enum'un başına/ortasına yeni değer ekleme

Unity enum'ları **sayısal indeksle** saklar. `Team`'e başa bir değer eklemek sahnelerdeki tüm
`BaseZone`/`Weapon` takımlarını kaydırır. Yeni değer **her zaman sona** eklenir —
`Team.Neutral` bu yüzden sonda (`BaseZone`'da "herkese açık" anlamına da gelir).

Aynısı `HitZone` (`Body` sıfırda kalır) / `ModeTeamMode` / `ModeScoreKind` / `ModeReviveAnchor` /
`ModeWeaponSource` / `ModeAudioEvent` için de geçerli.

Eşyanın üç ekseni de aynı kuraldadır ve **0. indeksleri bugünkü davranıştır**:
`ItemGrabPath.DistanceGrab` · `ItemInstancing.PerViewerClone` · `ItemReleaseMode.Return`. Bu alanlar
var olmadan yazılmış her asset `0` okuyor — sıra bozulursa arsenalin tamamı hata vermeden başka bir
şeye döner (raftaki silah yakınlık soketinden alınmaya çalışılır, tek örnek eşya kopyalanır).

### ⛔ `Server/config/maps.json`'ı elle düzenleme

`Export Server Config` üretir ve bir sonraki export elini ezer. Tek doğruluk kaynağı
`MapDefinition` SO'larıdır.

### ⚠️ `Resources/` altındaki asset'i taşıma, adını değiştirme

Bu asset'lerin **hiçbirinin sahneden referansı yoktur** — hepsi koddan ada göre çözülür
(`Resources.Load<GameCatalog>("GameCatalog")` gibi). Taşınan ya da yeniden adlandırılan asset
"eksik referans" hatası vermez: ona bağlı olan şey sessizce hiç çalışmaz/çizilmez. Kapsam:
`Data/Resources/` altındaki katalog ve ses bankası asset'leri (`GameCatalog`, `WeaponCatalog`,
`GameSoundBank`, `ModeAudioRegistry`), `Materials/Resources/M_BaseZoneXRay.mat`,
`Avatars/Resources/LocalBodyAvatar.prefab` ve `App/Resources/UI/` altındaki arayüz prefablarının
tamamı.

---

## Ağ olayları

### ⚠️ `OnDisable`'da abonelikten çık

`NetEvents` statiktir; abonelikte kalan ölü nesne `MissingReferenceException` üretir.

### ⚠️ `OnLoadMatch` sahne yüklenmeden ÖNCE gelir

Sahnedeki bir bileşende dinlersen **kaçırırsın**. Sahneye özel iş için `Start`'ta
`SceneRouter.Instance.LastModeId` / `LastMatchScene` oku, ya da kendini önyükleyen kalıcı bir
tekil kullan (`PlayerCombatState` deseni).

### ⚠️ `match_state` saniyede bir gelir

Her karede değil. Akıcı geri sayım istiyorsan son değeri kendin azalt.

---

## Build ve paketler

### ⛔ Meta umbrella paketini (`com.meta.xr.sdk.all`) ekleme

Meta Project Setup Tool önerse bile. Çektiği `voice` paketi Android namespace çakışmasıyla build'i
kırar. Bireysel paketler kullanılır: core + interaction + interaction.ovr @203.0.0, audio @85.0.0.

### ⛔ `.unitypackage` arşivini `Assets/` altına kopyalama

Paket Unity'nin içe aktarma penceresinden alınır; arşivin kendisi projeye girmez. Aynı yayıncının
iki pack'i **aynı GUID'leri paylaştığı** için ikincisi birincinin klasörüne açılır — yani klasör
adı artık içeriğini anlatmaz. Yeni bir pack aramadan önce **mevcut pack klasörüne bak**.

### ⛔ `Assets/ThirdPartyPackages/` altındaki klasörleri editör AÇIKKEN taşıma

Windows dosya kilidi yüzünden taşıma yarıda kalır ve geride yetim `.meta`'larla yarım bir ağaç
bırakır. Taşıma editör kapalıyken `git mv` ile yapılır; tek kod ayağı `WeaponKitBuilder.PackRoot`
sabitidir (tek satır) — o güncellenmezse silah kiti kaynaklarını bulamaz ama hata da vermez.

### ⚠️ `Shader.Find` build'de `null` dönebilir

Hiçbir materyalin referanslamadığı shader strip edilir. Runtime'da üretilen görseller bu yüzden
UI/TMP shader'ları üzerinden çizilir.

### ⚠️ Quest'te "Soft Particles" yok

`supportsCameraDepthTexture = false` (PC asset'te açık — editörde çalışır, cihazda çalışmaz).
Derinlik dokusu gerektirmeyen iki araç var: materyalde **Camera Fading** ve Collision modülünde
tek düzlem + `lifetimeLoss = 1`.

### ⚠️ Silah dengesi değişikliği APK build'i ister

Hasar sayıları istemcide yaşar; sunucuyu yeniden başlatmak yetmez.

### ⛔ Oyuncu build'inde `PlayerSettings` geri almasını `EditorApplication.Exit`'ten SONRAYA bırakma

Sürümlü oyuncu build'i `PlayerSettings`'i (bundle id, `bundleVersion`, `AndroidBundleVersionCode`,
ürün adı) geçici olarak değiştirir; eski değerlerin geri yazılması **`Exit` çağrılmadan önce**
bitmiş olmalıdır. `Exit` süreci anında sonlandırır — `finally` bloğu çalışmaz ve
`ProjectSettings.asset` diskte sürümlü değerlerle kalır, sonraki her build o bozuk hâlden başlar.
⚠️ Paket eki **noktasızdır** (`com.vortex.arenav132`, `com.vortex.arena.v132` DEĞİL): Android paket
segmenti rakamla başlayamaz.

### ⚠️ Build/import "sebepsiz" yavaşsa önce Defender dışlamalarına bak

Yeni bilgisayarda `scripts\defender-exclusions.cmd` (yönetici) bir kez çalıştırılır. Gerçek zamanlı
koruma her dosya açılışında araya girer; IL2CPP on binlerce `.cpp`/`.obj` üretip `Library/`'yi
sürekli okuduğu için paralel derlemenin önünde kuyruk oluşur — %20-40 bandında fark eder. Kurulu
mu diye bakmak için: `defender-exclusions.cmd -List` (bu da yönetici ister; Defender listeyi
yetkisiz oturuma vermez). ⚠️ Dışlanan klasörler taranmıyor, oraya indirme yapma.

### ⛔ Her güvenlik uyarısını "antivirüs" sanıp dışlama listesine koşma

`Get-MpThreatDetection` **boşsa** olay Defender AV değildir. Unity'de en sık ikinci kaynak **Smart
App Control**: bir Code Integrity politikasıdır, Defender dışlamalarını **hiç okumaz** ve açıkken
AV dışlamalarını da geçersiz kılar. Burst `Library/BurstCache/JIT/` altına **imzasız** DLL üretip
yüklediği için SAC onu engeller — `CodeIntegrity` olayı **3077** + `3118 Smart App Control Block
Details`; `git pull` sonrası Burst yeniden derledikçe tekrarlar. Aynı politika imzasız
`deploy\*.exe` çıktılarımızı da engelleyebilir. Teşhis:
`Get-WinEvent -LogName Microsoft-Windows-CodeIntegrity/Operational -MaxEvents 20`.
⚠️ Gerçek zamanlı korumayı kapatmak uyarıyı susturur, sebebi gizler. SAC'ı kapatmak çözer ama
**geri açılamaz** (Windows yeniden kurmak gerekir).

---

## Doküman

### ⛔ Kodu değiştirip dokümanı bırakma

Bu projede kural: protokol, ağ akışı, maç kuralı, bileşen sorumluluğu, klasör/asmdef yapısı,
editör aracı ya da sunucu config'i değiştiyse **ilgili doküman aynı commit'te** güncellenir.

Ağ davranışı değişecekse sıra: **önce `ArenaNet-Protokol.md`, sonra kod.** Kod-önce gidilirse
istemci ve sunucu iki uçlu sapmaya başlar.
