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

Birinci istisna **dikey** sanal ofsettir: çok katlı arenada kat geçişi rig kökünü yalnız Y'de taşır
ve bunu yapan tek yer `ArenaCalibrator.SetFloorLift`'tir (`FloorState` üzerinden). **Ürün kodunda
yatayda hiçbir kod rig'i oynatmaz**; başka hiçbir kod rig kökünü de oynatmaz.

İkinci istisna **yalnız editördedir**: dev hizalama ("Kalibrasyonu atla") yürürlükteyken kumanda
çubukları rig'i yatayda yürütür ve kafanın etrafında döndürür. Bunu yapan tek yer
`ArenaCalibrator.Dev.cs`'dir ve dosyanın tamamı `#if UNITY_EDITOR` içindedir — build'e tek satır
girmez; ürün kodu için kural yukarıdaki biçimiyle aynen geçerlidir. ⚠️ Meta rig'indeki kapalı
locomotion yığını (`Locomotor` vb.) bunun için de **açılmaz**: dev yürüyüşü o yığını kullanmaz.

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

⚠️ Editördeki dev hizalama bir **yerel ezme değildir**: o da `Calibrated` → `set_calibration`
yolundan `source:"dev"` ile bildirir, kapı yine sunucudadır. `IsCalibrated` / `ManualAllowed`
editörde bile ezilmez.

### ⛔ `VA_ArenaBoundary`'yi taşımak / döndürmek

Arena uzayı **dünya uzayıdır**: ağa giden/gelen tüm pozların sıfırı sahnenin dünya sıfırıdır ve
telde bunu telafi eden bir origin yoktur. Muhafaza bu yüzden **dünya orijininde ve dönüşsüz durur**;
altındaki maket de yerel sıfırdadır. Arenayı hazır bir environment'ın içine oturtmanın yolu
**environment'ı arenaya taşımaktır**, tersi değil — `Configure All Build Elements` sağlık raporu
orijinden kaymış ya da döndürülmüş muhafazayı UYARI ile yazar.

Kaydırmanın bedeli iki katmanlıdır: (1) muhafaza, kalibrasyon işaretçileri ve kadraj boundary'yi
izler, sanat izlemez — ayrışırlarsa oyuncu fiziksel alanda yanlış yere göre kalibre olur ve hata
ancak sahada görünür; (2) maketin geri okuması dünya uzayından geçtiği için döndürülmüş bir kökte
hassasiyet kaybeder, ölçü dosyası her gidiş-dönüşte biraz daha kayar.

Aynı sebeple oynanan zemin **dünya y=0'da** (yani muhafazanın Y'sinde) durmalı: uzak
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

### ⛔ Sahne geçişi karartmasını `ScreenFade`'e kaynak yapma, quad'ını paylaştırma ya da taşıma

Geçişin siyahı (`SceneTransitionFade`) hakeme kaynak olarak eklenmez ve alan-dışı karartmasının
quad'ına (`OutOfBoundsFade`) ikinci bir yazıcı olarak konmaz — kendi quad'ı ve kendi sürücüsü
vardır. Quad **sahnenindir**: `DontDestroyOnLoad` kökün altına taşınırsa yeni sahnede elle
gizlenmesi gerekir ve eski sahnede inişi çizecek yüzey kalmaz. Gerekçeler `Docs/Sistem-Ozeti.md`
bileşen sözlüğünde ve Tuzaklar bölümündedir.

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

### ⛔ İskelet tel düzenini `SkeletonWire.FORMAT` ve `PROTOCOL_VERSION` artırmadan değiştirme

`SkeletonWire.JOINT_INDICES`'e eklem eklemek, sırasını değiştirmek ya da blob düzenine alan eklemek
iki ucun aynı baytları başka kemiklere okuması demektir. Uzunluk değişirse alıcı kareyi atar ve uzak
gövde kafa + kol yedeğine düşer; aynı uzunlukta bir sıra değişikliği ise hiçbir kapıya takılmaz,
rotasyonlar sessizce yanlış kemiklere yazılır. İkisi birlikte artırılır ve tüm başlıklar + admin +
sunucu aynı turda dağıtılır. Prefabdaki `_bodyIndicesToSync`/`_bodyIndicesToSend` tele etki etmez —
tel listesi orada değiştirilmez. Gerekçe: `ArenaNet-Protokol` §6.9.

### ⛔ Kalça konumunu ölçeğe bölmeden tele koyma

`SkeletonWire`'a yazılan kalça gönderenin **oran uzayındadır** (`localPosition / bodyScale`); ham SDK
konumu gerçek metredir ve alıcı ölçeği bir kez daha uygular — hata derlemede değil, oyuncu boyunda
görünür (kısa oyuncu daha kısa, uzun oyuncu daha uzun çizilir). Ayak yüksekliği ayrı alandır
(`footY`, gerçek metre) ve bölünmez. Gerekçe: `ArenaNet-Protokol` §6.9.

### ⛔ Sunucuya kenar tetikli bildirdiğin durumu yeniden bağlanmada tekrar bildirmeden bırakma

Sunucu `hello`'da ve kopuşta oturum durumunu sıfırlar (`ready`, `calibrated`, …). "Değişince
yolla" diye yazılmış bir bildirim bunu bilemez: oyuncu tabanında dururken Wi-Fi kopup gelirse
istemci "zaten bildirdim" der, sunucu "hiç gelmedi" der ve toplanma süresiz bekler. Kenar tetikli
her bildirim `NetEvents.OnConnected`'da son kenarı unutur ve mevcut durumu tekrar yollar
(`TournamentRegroupReporter`, `CalibrationState`, `SceneRouter` aynı kalıptır). İkinci tetik
roster'ın kendisidir: kendi satırında bayrağın düşmüş görünmesi de kenarı unutturur — bayrak yalnız
kopuşta değil, sunucunun tur sonunda yaptığı temizlikte de düşer ve o yolda `welcome` gelmez.

### ⛔ Bağlantı kopmasını "soket hata verir" diye TCP'ye bırakma

Sessizce ölen Wi-Fi'da sokete hata gelmez: gönderimler TCP kuyruğunda bekler, `ReceiveAsync`
sonsuza dek bekler, yeniden bağlanma döngüsü hiç başlamaz. Ölü bağlantıyı düşüren istemcinin kendi
bekçisidir (`ArenaClient.LinkWatchdogAsync`, girdisi sunucunun `heartbeat`'i); WebSocket
ping/pong bu iş için kullanılmaz. Gerekçe: `ArenaNet-Protokol` §8.

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

### ⛔ Kill feed / ölüm ekranı metnini ikinci bir yerde kurma

Bir `kill_event`in metne dönüşmesi **`UI/KillFeedText`**'te olur; oyuncu HUD'ı da admin listesi de
onu çağırır. Metin dallara ayrılıyor çünkü **"bu oyuncuyu ne öldürdü"** sorusunu cevaplıyor: rakip
(`A -> B`) · kendi bombası (`killerId == victimId`) · engel (`weaponId == "obstacle"`) · bilinmeyen.

Kendi kopyasını kuran yüzey, eksik bıraktığı dalda **cevapsız kalmaz — YANLIŞ cevap verir**: intihar
"öldü" diye okunur ve operatör olmayan bir sunucu hatasını kovalar. Belirti hiçbir yerde hata
üretmez, yalnız yanlış bir cümledir.

⚠️ **Ölüm ekranı bu kuralın kopyası değil, ikinci kişisidir** (`ModeHudBase.RefreshDeathLine`):
sınıflandırma aynı, sözcükler farklı — feed "kendini havaya uçurdu", ekran "Kendini havaya
uçurdun" der. Yeni bir ölüm sebebi ikisine birden eklenir.

### ⛔ Alan hasarında mesafe düşümünü siper soğurmasından ÖNCE uygulama

Sıra: siper soğurmasını **merkez hasarından** düş, mesafe düşümünü **kalana** uygula. Ters sıra
sipere düşümle küçülmüş bir sayı üzerinden bedel ödetir. Belirti: bomba sipere yeterli hasarı
verip onu kırar, ama arkasındaki oyuncuya sıfır hasar gider; siper zaten kırıkken (soğurma sıfır)
aynı bomba aynı yerden öldürür — bu yüzden "menzil sorunu" gibi okunur ve teşhisi pahalıdır.
Kural üç yolda da aynıdır (uzak oyuncu · ağ nesnesi · kendine hasar); biri sapınca atan
başkasından farklı bir eğriye tabi olur. Mesafenin ölçüldüğü nokta da sabittir: oyuncu için
**gövde kapsülünün yüzeyi** (arena zemini→kafa; atan ve diğerleri için aynı kapsül), ağ nesnesi için
collider'ın en yakın noktası. Kafa gibi tek bir noktadan ya da kemiğe asılı isabet kutularından
ölçmek ayağının dibindeki bombayı uzak sayar, bir adım ötedekini kafasından puanlar ve çömelmeyi
hasar ayarına çevirir.

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

### ⚠️ Kumandayla alınan ölçüm ESKİ dosyanın A/B çerçevesinde yazılır

Mekan ölçümü (`Docs/ArenaNet-Protokol.md` §10.11) yeni bir çerçeve kurmaz: ölçüme girilen sahnenin
planında kalibrasyon noktaları varsa çıktı **onların** çerçevesine döndürülür. Zemindeki bant sahada
fizikseldir ve mevcut sahne sanatı ona göre kuruludur — düzeltilen şey duvarların banda göre
konumudur. Sonuç: dosyadaki `calibration` noktaları yanlışsa **önce onlar düzeltilir**; yanlış bir
çerçevenin üstüne alınan ölçüm de aynı yanlış yere oturur ve ölçünün "düzeldiğini" sanırsınız. Bant
gerçekten yer değiştirdiyse dosyadaki A/B elle güncellenir, sonra ölçüm alınır.

---

## Arayüz (Girdap)

### ⛔ Girdap prefabını elle düzenleme

Admin ekranları ve maç sonu ekranı koddan üretilir (`GirdapUiBuilder`); Inspector'da yapılan her
şey bir sonraki üretimde kaybolur — değişiklik builder'a yazılır, sonra `Tools > VortexArena > UI >
Girdap > Yalnız <Ekran>` koşulur.

### ⛔ Girdap prefabına Layout Group / ContentSizeFitter koyma

Kitin yerleşimi sabit anchor'a ve kodda ölçülen genişliğe dayanır (`GetPreferredValues` +
`anchoredPosition`); araya giren bir layout bileşeni mockup'tan gelen px ölçülerini yeniden akıtır
ve ekran çözünürlükten çözünürlüğe sessizce kayar.

### ⛔ Tam ekran panel kartını sol-üstten `Place` ile koyma

İstatistik ve tercihler kartı `PlaceCenter` ile merkeze ankrajlanır. CanvasScaler `Expand` 16:9
dışı bir pencerede tuvale fazladan genişlik ya da yükseklik verir; mockup'ın `left: 240px; top:
135px` değerleriyle sol-üstten konan kart o pencerede merkezden kayar.

### ⛔ TMP metin parıltısını çalışma anında keyword açarak verme

`UNDERLAY_ON` bir `shader_feature`'dır; build'de varyant yalnız bir material asset onu kullanıyorsa
kalır. Çalışma anında `EnableKeyword` ile açılan parıltı editörde görünür, build'de sessizce yok
olur. Parıltı builder'ın `TextGlow(...)` preset'inden gelir (`Resources/UI/Fonts/<Font> SDF Glow
<renk>.mat`); çalışma anında yalnız `fontMaterial` örneğinin rengi değiştirilir.

### ⛔ Admin arayüzünde ve maç sonu ekranında `UiKit` paletini kullanma

O ekranların tek palet kaynağı `Girdap`'tır; ikinci bir palet aynı rolü iki tonda çizer ve oyuncu
"bu kırmızı hangi takım" eşlemesini kaybeder. `UiKit` yalnız elle düzenlenen ekranlar, sahne
işaretçileri ve `UiKit.EnsureEventSystem()` için kalır.

### ⛔ Düğme/rozet rengini ekrandan ayarlama

Düğme çalışırken tür değiştiriyor (BİTİR → BİTİR?, tehlike → onay) ve elle boyanan dört katmanı
geri almak gerekiyor: tek kapı `UiButtonStyle.SetKind`/`SetInteractable` ve `UiChip.Set`.

### ⛔ Özel `Graphic` türevini kendi `[RequireComponent(typeof(CanvasRenderer))]`'ı olmadan bırakma

`RequireComponent` tabandan **miras alınmaz**: eksikse `AddComponent` hiçbir şey çizmeyen bir
grafik üretir ve obje pasifleşirken `MissingComponentException` fırlatır.

### ⛔ TMP `Ellipsis` metin kutusunu fontun satır kutusundan kısa bırakma

`Ellipsis` DİKEY de keser (Saira Condensed 24 px'in satır kutusu ≈ 38 px): dar bir rect'te metin
kısalmaz, **tamamen kaybolur** — kutu büyütülür, görsel konum midline hizalamayla korunur.

### ⚠️ Girdap şeklinin `raycastTarget`'ını açık bırakma

`Graphic`'in varsayılanı `true`'dur ve serileşen taban alanı olduğu için koddan "bir kez"
kapatılamaz; builder'ın fabrika yardımcıları şekil/yazı/ikon başına kapatır, tıklanabilir yüzey
kendi açar. Açık kalan dekoratif bir grafik üstündeki düğmenin tıklamasını yutar.

---

## Sahne ve prefab

### ⛔ Arayüz prefabındaki bir ögeyi SİLME

Elle düzenlenen arayüz prefablarında (`_Shared/App/Resources/UI/` altındaki bildirim, yükleme ve
bağlantı ekranları) her öge kök bileşende bir `[SerializeField]` alanına bağlıdır
(`centerNoticeText`, `_reconnectButton`…). Ögeyi silersen alan boşalır ve
**hiçbir hata çıkmaz — o parça sessizce çizilmez.** Gizlemen gerekiyorsa objeyi devre dışı bırak ya
da alfasını sıfırla. (Girdap ekranlarında kural daha katıdır: orada prefab hiç elle düzenlenmez.)

Aynı sebeple prefabı `Resources/` klasöründen **çıkarma**: sahneye konmuyorlar, çalışırken
`Resources.Load` ile yükleniyorlar. Taşınırsa o arayüz hiç doğmaz (konsola
`… prefabı bulunamadı` düşer).

### ⛔ Arayüz düğmesinin `onClick`'ini inspector'dan doldurma

Prefablarda bilerek boştur; davranış çalışırken koddan bağlanır (`WireButtons` / `Initialize`).
Geri çağrıların çoğu **koşulludur** — `AT`'ın iki adımlı onayı, `SIFIRLA`'nın kısa basış/basılı
tutma ayrımı, maç sürerken kilitlenen mod/harita satırları, faza göre komut değiştiren
DURAKLAT/DEVAM. Inspector'dan eklenen kalıcı bir kayıt bu koşulları **atlar**: oyuncu kartındaki
`AT` düğmesi "EMİN?" adımını geçip doğrudan atardı, `SIFIRLA` ise basış süresine bakmadan hem
yumuşak sıfırlamayı hem kayıt silmeyi gönderirdi.

Ayrıca hedef statik değildir — satır her `Bind`'da başka bir oyuncuya bağlanır; kalıcı bir kayıt
yanlış oyuncuya komut gönderir.

→ ayrıntı ve düzenleme kuralları: **[Arayüz Tasarımı](Arayuz-Tasarimi.md)**

### ⛔ Moda özel maç sonu ekranını `MatchResultOverlay.prefab`'ın KOPYASI olarak yapma

Moda özel görünüm, tabanın **varyantı** olur (`ModeDefinition.resultScreenPrefab`'a bağlanır).
Kopya, alan bağlarını o anki hâliyle dondurur: tabana sonradan eklenen bir öge ya da alan kopyaya
inmez ve o modda **hata vermeden** çizilmez. Aynı sebeple varyantın kökündeki `MatchResultOverlay`
bileşeni kaldırılmaz, `modeId`'ye bakan bir görünüm dalı da kodda açılmaz — görünüm veridir.
→ reçete: **[Yemek Kitabı 13.2](Yemek-Kitabi.md#132-moda-özel-maç-sonu-ekranı)**

### ⛔ `BaseZone`'u gizlemek için yalnız bileşeni kapatma

Bileşeni kapatmak görsel taban şeridini ekranda bırakır. Gizlemen gerekiyorsa **bileşeni** kapat
(`zone.enabled = false`) **ve** Renderer'lı çocukları ayrıca gizle — ama bunu **elle yazma**: kararın tek yeri
`BaseZoneVisibility`'dir (kapı takım kipi; `WeaponGranter`'ın silah süpürmesiyle ilgisi yoktur).

İkinci yüzü: **kapalı bir `BaseZone` canlanma için açık sayılmaz.** `Update` koşmadığı için
`IsPlayerInside` donar — açık sayılsaydı oyuncu bölgeye girse de hiç canlanamazdı (sunucuda
kendiliğinden işleyen bir emniyet ağı yok; geriye kalan tek çare operatörün elle canlandırmasıdır).

### ⛔ Silah panosu prefabını `WeaponCanvas` bileşeni olmadan yapma, sahnedeki örneği unpack etme

Süpürme panoyu o işaretçi bileşenden bulur (`WeaponGranter`). Bileşen yoksa ya da unpack'te
koptuysa silahı modun kendisi dağıttığı durumlarda silahları gizlenmiş **boş pano** tabanda ayakta
kalır. Bileşen panonun **kökünde** durur, prefabdan gelir.

### ⛔ Taban bölgesinin boyutunu sayı alanıyla ayarlamaya çalışma

`BaseZone`'da ölçü alanı YOKTUR: algılama alanı **altındaki şeridin kapladığı dikdörtgendir**
(bölgenin kendi yerel XZ'sinde ölçülür, yükseklik yok sayılır). Bölgeyi büyütmek, daraltmak,
döndürmek ya da kaydırmak istiyorsan **şerit mesh'ini** öyle yap; sonucu bölgeyi seçince çizilen
Gizmo'dan gör. Sayı alanı olsaydı görselle sessizce sapardı — oyuncu kırmızının üstünde dururken
canlanamaz olurdu ve hiçbir yerde uyarı çıkmazdı.

⚠️ Aynı sebeple **şeridi silme, Renderer'sız bölge bırakma**: ölçü alınamayan bölge bir kez hata
basıp kendini kapatır ve "açık taban yok" fail-open'ı devreye girer — belirtisi "taban çalışmıyor"
değil, herkesin arenanın her yerinde canlanmasıdır.

### ⛔ Taban şeridini occlusion culling'e sokma

Şeridin Renderer'ında **`Dynamic Occlusion` kapalıdır** (`VA_BaseZone` prefabında böyle gelir) ve
şerit **`Occludee Static` işaretlenmez**. Occlusion bake'li sahnede duvarın ardında kalan renderer
**bütünüyle** elenir; duvar-arkası çizim (x-ray) aynı renderer'ın ikinci slotu olduğu için onunla
gider ve ölen oyuncu canlanma noktasını göremez. Bake'siz sahnede hiçbir belirti çıkmaz — hata
ancak o haritaya occlusion bake alındığında görünür. `BaseZoneVisibility` slotu eklerken
`Dynamic Occlusion`'ı kendisi de kapatır; static işaretli şeridi kurtaramaz (o, bake verisine
girmiştir).

### ⛔ Kopyalanan sahneyi kaynağın occlusion verisiyle bırakma

Sahne kopyalanınca `Occlusion Culling` bağlantısı da kopyalanır: kopya **kaynağın** veri dosyasını
gösterir ve **başka yerleşim için** bake edilmiş görünürlük verisiyle çalışır — görünmesi gereken
objeler elenir, hata ya da uyarı çıkmaz. Occlusion kullanan bir sahneden kopyalanan sahne **kendi**
bake'ini alır ve bağlı olduğu `OcclusionCullingData.asset` **kendi klasöründe** durur. Kontrol:
sahneye sağ tık → *Select Dependencies*; başka sahnenin klasöründen bir `OcclusionCullingData.asset`
seçiliyorsa bağlantı hâlâ kaynağınkidir.

### ⛔ Görünürlükle budanmış dekoru, kaynağı değişince yeniden üretmeden bırakma

Sahnede `FarImpostors` kökü varsa arena dışı dekor oyun alanından görünürlüğe göre budanmıştır:
renderer'lar sahneye özel `Art/Optimized/Culled/` mesh'lerini gösterir, uzak dekor kartlara
çevrilmiştir ([Yemek Kitabı, Reçete 14](Yemek-Kitabi.md)). Bu çıktılar üretildikleri andaki
**oyun alanı sınırına, iç duvarlara, dekor yerleşimine ve ışık bake'ine** göredir: alan genişler
ya da tek yüzlü bir duvar kalkarsa atılmış üçgenler delik olarak görünür, yeniden bake sonrası
kartlar eski ışıkla kalır — hata ya da uyarı çıkmaz. Biri değişince budama yeniden üretilir.
Sahne kopyalanınca kopya kaynağın `Art/Optimized/` asset'lerini göstermeye devam eder; kopyanın
oyun alanı farklıysa budama kopya için ayrıca üretilir. Gölge/LOD tarafındaki editör-gözlük farkı
→ [Sistem Özeti, Tuzaklar](../Sistem-Ozeti.md).

### ⛔ Üst kat plakasını `Obstacle` layer'ına koyma / collider'sız bırakma

İki yarısı da bağlayıcıdır. `Obstacle` layer'ı **"kafa girerse ceza"** sözleşmesidir: plaka oraya
damgalanırsa `ObstacleViolationProbe` üst kattaki **herkesi** sürekli ihlalde sayar, ekranları
kararır.

Collider'ını sökmek ya da plakasız bir üst kat kurmak ise ters yönden kırar: atış ışını maskesizdir
(`ArenaCombat.TraceShot`), yani alt kattaki oyuncu üst kattakini tavanın içinden vurur. Doğrusu:
collider **`Default`** layer'da kalır.

Admin kuş bakışı için plakayı `ArenaRoof` kökünün altına al (`GameObject > VortexArena > Arena Roof`)
— aksi hâlde operatör tepeden alt katı hiç göremez.

### ⛔ Kuş bakışında gizlemek için layer'ı elle `ArenaRoof` yapma

Layer hiçbir şeyi gizlemez — gizleyen `ArenaRoof` **bileşenidir**, layer yalnız sahne görünümünde
süzme içindir. Elle değiştirmek iki yerden kırar: obje gizlenmez, objede collider varsa `Obstacle`
sözleşmesi (engel ihlali, namlu engeli, patlama siperi) o obje için hata vermeden kalkar. Doğrusu:
kapatan objeyi seç → `GameObject > VortexArena > Arena Roof`; damga collider'lı objenin layer'ına
dokunmaz.

### ⛔ Çatısı gövdeyle tek mesh olan modele `ArenaRoof` koyma

Bileşen altındaki Renderer'ı **bütün olarak** gizler: çatı ile duvarlar aynı mesh'teyse admin kuş
bakışında yapının tamamı kaybolur ve içi de görünmez. Doğrusu: çatıyı Blender'da ayrı mesh'e ayır,
gövde mesh'ini değiştirip çatıyı kardeş bir kökün altına koy ve bileşeni yalnız o köke ekle. Paket
FBX'inin kendisi yerinde değiştirilmez: ayrılmış kopya onun yanına, sahneye özel `_opt` mesh'ten
ayrılan parçalar o sahnenin `Art/Optimized/Props/`'una yazılır. Kulübenin hazırı var: sahneye
`ConstructionPropsBundle/base/base.fbx` değil **`BaseHut.prefab`** konur — `base.fbx` konursa
çatı yeniden gövdeyle tek parça gelir.

### ⛔ Kat yüksekliğini elle yazma

Kat seviyelerinin tek kaynağı sahnedeki `FloorPortal`'lardır: `k`. katın zemini, o kata çıkan
portalın `upperHeight`'ının tanımladığı yüksekliktedir (`ArenaFloors`). İkinci bir sayı listesi
mesh'ten sessizce sapar ve oyuncu **altında zemin olmayan** bir yüksekliğe kaldırılır.

Yükümlülük yerleştirmededir: portalın kökü kendi katının zeminine 0,25 m içinde oturmalı — oturmazsa
portal kat listesine girmez ve uyarı basıp kendini kapatır.

### ⛔ Kat portalı çemberine collider koyma

Kapıyı kafanın çember merkezine XZ mesafesi çözüyor; konan bir collider maskesiz atış ışınını ve
engel/kavrama ölçümlerini yalanlar — geçiş noktası mermi yiyen görünmez bir disk olur.

### ⛔ Portal disklerini aynı X/Z'ye koyma

Kat geçişi oyuncuyu yatayda kıpırdatmaz: üst kata çıkan kafa doğrudan diğer portalın dolum
çemberinin içine iner ve oyuncu diskten çıkmadıkça katlar arasında zincirlenir. `ArenaFloors`
çakışan diskleri uyarır, şablon aracı portalları X'te 2 m aralıkla dizer — aralığı kapatma.
Gerekçe: `Docs/Sistem-Ozeti.md` §7 "İki kat portalının diskleri aynı X/Z'ye KONMAZ".

### ⛔ Zeminin altına kat ya da üst kata taban şeridi koyma

Zemin kat (kat 0, dünya y = 0) her arenada **zorunludur**, üst katlar isteğe bağlıdır ve zeminin
altında kat **yoktur**. Taban şeritleri de zemin kattadır: ölüm oyuncuyu tabanının katına indirir ve
hedef hiçbir zaman bulunduğu kattan yukarı olmaz — üst kata konan bir taban, o tabana ait oyuncunun
canlanma şartını hiç sağlayamaz.

### ⛔ Alıcıda iskelet köküne kat yüksekliği ekleme

İskelet karesinin `dy`'si kökün **mutlak** arena yüksekliğidir (`ArenaNet-Protokol.md` §6.9), kat
bilgisi zaten içindedir. Üstüne kat yüksekliği eklenirse üst kattaki gövde **bir kat daha** yukarıda,
tavanın içinde çizilir ve hata basılmaz. Kat defteri yalnız iskelet akışı yokken kullanılan yedek
yola aittir. Gerekçe: `Docs/Sistem-Ozeti.md` §7 "Alıcıda iskelet köküne kat yüksekliği EKLENMEZ".

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

### ⛔ `NetIdentity.sceneId` override'ını prefaba Apply etme

`sceneId` **sahneye** aittir: prefabdan gelen objede prefab asset'i 0 taşır, sahnedeki her kopya kendi
kimliğini prefab override olarak alır. `Overrides > Apply` ile o sayı prefaba yazılırsa o prefabın her
kopyası aynı kimlikle doğar; `NetObjectRegistry` ikinciyi kaydetmez ve belirtisi sahada "obje
kırılmıyor"dur. `SceneIdGuard` sahne kaydında çakışmayı ayırır ama prefab asset'i kirli kalır, her yeni
sahnede yeniden çakışır. Normal prefabda Apply penceresinde `NetIdentity` satırının işaretini kaldır;
model prefab (FBX) örneğine Apply zaten yapılamaz, oraya eklemek güvenlidir.

### ⛔ Avatar prefabının retargeter'ında `_objectsToHideUntilValid` listesini doldurma

`NetworkCharacterRetargeter._objectsToHideUntilValid`'deki nesneleri görünür yapan SDK'nın
`ReceiveData` yoludur ve uzak gövdede o yol hiç çağrılmaz (kemikleri `ArenaNetCharacterBehaviour`
yazar). Listeye konan nesne uzak gövdede **hiç görünmez**, hata da vermez. `SkeletonStreamGuard` iki
avatar prefabında da listeyi boş ister, doluysa Hazırlık satırı ✗ olur. Gerekçe: `Docs/Sistem-Ozeti.md`
§7 "`_objectsToHideUntilValid` uzak gövdede nesneyi kalıcı gizler".

### ⛔ Moda özel gövdeye Mixamo dışı bir rig'le riglenmiş FBX koyma

`SkeletonPoseMirror` kemikleri **ADLA** eşler; paketin kendi rig'i (3ds Max Biped tarzı adlar) hiçbir
kemiği tutturmaz, gövde T-pozda kalır. Ayna bunu bir hata satırıyla bildirip kendini kapatır, yani
belirtisi sahada "oyuncu kıpırdamıyor"dur. Model **Mixamo auto-rigger**'dan geçmek zorundadır
(`mixamorig:*`); hazırlığı `Docs/Gelistirici/Yemek-Kitabi.md`'de.

### ⛔ Mod gövdesini kendi vuruş kutuları olmadan bırakma

Kutular gövdenin **kendi** iskeletindedir ve yalnız çizilen gövdeninkiler açıktır: kutusuz bir mod
gövdesi çizildiği anda oyuncu **vurulamaz** olur ve hiçbir yerde hata çıkmaz. Kutular gövde prefabına
elle konur, bölgeleri açıkça seçilir ve o modelin kendi oranlarına göre ölçülür.

### ⛔ İki takıma aynı (ya da ayırt edilemeyecek kadar benzer) mod gövdesi verme

Takım kimliği karakter mesh'ine yazılmaz kuralının istisnası **modelin kendisidir**: renk dost/düşman
bilgisini taşımıyor, iki takımı ayıran tek şey `bodyPrefab` ile `redBodyPrefab`'ın farklı olması.
Benzer iki model, oyuncunun kime ateş ettiğini maçın ortasında belirsizleştirir.

### ⛔ Mod gövdesinden üçüncü parti paketin içindeki materyale/dokuya referans verme

`Assets/ThirdPartyPackages/` altındaki bir paket kaldırılabilir ya da yeniden içe aktarılıp üzerine
yazılabilir; referans koptuğunda gövde mor çizilir, ayar ezildiğinde sessizce değişir. Kullanılan
materyal ve doku modun kendi klasörüne **kopyalanır** (Android doku override'ı da orada verilir),
paketin gitmesi gövdeyi bozmaz.

### ⛔ NPC görünümünü oyuncu gövdesi hattıyla kurma

Oyuncu gövdesi hattı (`Mod Gövdesi Kur` aracı + `SkeletonPoseMirror` + `RemoteHitBox`) uzak
oyuncunun pozunu **aynalamak** için vardır: rig Generic/Avatar'sız kalır, pozu ağ yazar. NPC
(müşteri, köstebek gibi) ise Humanoid'dir ve kendi `Animator`'ıyla oynar — ayna kemikleri ele
geçirir ve animasyonu dondurur. `RemoteHitBox` ayrıca `RemoteAvatar` ister ve vurulduğunda
`hit_report` göndererek NPC'yi **oyuncu isabeti** saydırır. NPC görünümünün reçetesi
`Docs/Gelistirici/Yemek-Kitabi.md`'dedir.

### ⛔ `.meta` dosyası kopyalayarak asmdef/asset üretme

GUID çakışır ve Unity referansları rastgele koparır. JSON'u kopyala, `.meta`'yı Unity üretsin.

### ⛔ İçi boş klasör açma (ne araçla ne elle)

Git klasör değil dosya izler: boş klasör commit'e girmez, klonda **yoktur** ve geride yalnız
ona ait yetim bir `.meta` kalır — "bende var, sende yok" biçiminde ortaya çıkar. Klasör, içine
ilk dosya girdiğinde açılır.

### ⛔ `_Shared` köküne asmdef'siz script koyma

`Assembly-CSharp`'a düşer, hiçbir asmdef göremez.

### ⛔ Parçacık materyalinde soft particle, distortion ya da 1'i aşan renk kullanma

VR (Android) **Mobile** kalite seviyesiyle, admin (Standalone) **PC** seviyesiyle koşar; Mobile
tarafında **Depth Texture · Opaque Texture · HDR üçü de kapalıdır**. `_SOFTPARTICLES_ON` derinlik
dokusuna, `_DISTORTION_ON` opaque dokusuna, 1'i aşan renk/emisyon ise HDR'a bağlıdır: üçü de VR'da
**sessizce başka bir şey çizer** — yumuşak kenar sertleşir, distortion kartı boş/koyu kalır, 1'i
aşan ateş rengi beyaz lekeye kırpılır. Editörde ve admin build'inde doğru görünür, yalnız başlıkta
yanlıştır: **hata satırı yoktur.**

Kural: patlama/çarpma parçacıkları **Unlit**, soft particle'sız, distortion'suz ve **LDR**
renklerle yazılır — renk 1'i aşacaksa yerine tonu koyulaştır, parlaklığı değil. Yumuşak kenar
gerçekten gerekiyorsa derinlik dokusu istemeyen iki araç var: materyalde **Camera Fading** ve
Collision modülünde tek düzlem + `lifetimeLoss = 1`.

⚠️ Bu, RP asset'lerini Mobile'da açarak da "çözülür" ama çözülmez: derinlik ön-geçişi + tam
çözünürlüklü renk kopyası per-eye maliyeti başlıkta tüm kareyi yavaşlatır. Düzeltme **efektte**
yapılır, boru hattında değil. ⚠️ `Particles/Lit` de kaçınılır: saydam parçacık başına aydınlatma
Quest'te pahalıdır ve iki platformun ek-ışık bütçesi farklı olduğu için görünüm ayrışır.

Kurulu örnek: `_Shared/FX/Materials/M_Blast*` (patlama efektinin platform-bağımsız materyal takımı).

### ⛔ Havuzlanan efekt prefabında `Stop Action`'ı `Destroy` yapma

`_Shared/FX/` altındaki efektler (`SurfaceImpactFx`, `BlastFxPool`) **bir kez üretilip yeniden
oynatılır**. `Stop Action = Destroy` olan parçacık sistemi ilk oynatmanın sonunda kendi objesini
siler: kökteyse havuz her seferinde yeni kopya kurar (havuzun ve ısıtmanın bütün anlamı gider,
patlamada takılma döner), çocuktaysa o parça **oturum boyunca bir daha görünmez** — "efektin bir
kısmı nadiren çıkıyor" diye okunur. **Hata satırı yoktur.** Hepsi `None` kalır; gizleme havuzun
işidir.

### ⛔ Efekt için çalışma anında gerçek zamanlı ışık yakma

Arena sahneleri gerçek zamanlı **nokta/spot ışık taşımaz**. Patlamada, namlu alevinde ya da isabette
bir tanesi açılınca URP Forward o kareden itibaren ekrandaki **her Lit materyali** ek-ışık
varyantıyla (`_ADDITIONAL_LIGHTS`) çizer ve o varyantlar tam o karede derlenir: ilk görüşte büyük
bir takılma olur, **hata satırı yoktur**, ikinci bakışta geçtiği için "bir kerelik" sanılır. Işığı
prefaba koymak da aynı şeydir — havuz düğümü açıldığı anda ışık sahnededir. Parlama **additive bir
parçacıkla** yapılır (kurulu örnek: `FX_BombBlast/Flash`); parçacık sisteminin `Lights` modülü de
aynı kuralın içindedir.

### ⛔ İstasyonun loop sesini/efektini taşınan nesneye koyma

Izgara cızırtısı gibi "istasyon çalışıyor" sesi **istasyonun kendisinde** durur ve yerel durumdan
sürülür (`BurgerGrill`). Taşınan nesneye (köfte) konup ağ olayıyla açılıp kapanırsa tek bir
"kapat" kaybı sesi nesneye yapıştırır: nesne tahtaya, tabağa, vardiya sonrasına kadar cızırdamaya
devam eder. **Hata satırı yoktur.** Nesnenin üstünde yalnız tek seferlik geri bildirim (pişti
sesi, parıltı) durur.

### ⛔ `.glb` modelini GLB'nin kendi materyaliyle build sahnesine koyma

glTFast (`GltfImporter`) gömülü dokuları **sıkıştırmasız ARGB32** alt-asset olarak üretir: doku
import ayarı (ASTC, max size) uygulanamaz, importer materyal remap'i desteklemez ve her `.glb` aynı
dokunun kendi kopyasını taşır. 2048'lik tek doku build'de ~21 MB tutar; birkaç düzine GLB'li sahne
Gradle'ın 4 GB arşiv sınırını aşar (`bundleReleaseLocalLintAar` → *"Archive's size exceeds the
limit of 4GByte"*). zip64 açmak çözüm değildir, APK 4 GB'ı taşıyamaz.

Mesh GLB'den kullanılır, **materyal kullanılmaz**:

- Dokular `_Extracted/Textures/` klasörüne yazılır: piksel içeriğine göre tekil, alfasız doku JPG,
  alfalı doku PNG. `_Extracted/` klasörü `ThirdPartyPackages/<paket>/` kökünde, onun dışındaki
  GLB'de GLB'nin kendi klasöründe durur. Aynı GLB'yi kullanan sahneler aynı kopyaları paylaşır.
- Dokular kaynakla aynı sRGB/linear ayarıyla, Android override'ı ile import edilir: renk
  `ASTC_6x6`, linear (normal, metallic-roughness) `ASTC_5x5`, en fazla 2048.
- Materyal kopyaları `_Extracted/Materials/<glb>/` altına kaydedilir.
- Sahnedeki renderer'lar bu kopyalara bağlanır. Bağlama prefab instance override'ıdır, `.glb`'ye
  dokunulmaz.
- Bake'e giren GLB/FBX'te lightmap UV'si üretilir (GLB: `Generate Secondary UV Set`, FBX:
  `Generate Lightmap UVs`). UV2 yoksa bake doku UV'sine yazılır; üst üste binen UV bölgeleri
  çatıda/cephede koyu leke olarak çıkar, hata vermez. Başka sahnenin de kullandığı modelin UV'si
  değiştirilmez (o sahnenin bake'i geçersizleşir) — o renderer `Receive GI = Light Probes` alır.

Kontrol: sahnedeki bileşenlerin serialize referanslarında yolu `.glb` ile biten `Material` ya da
`Texture2D` kalmamalı. `EditorUtility.CollectDependencies` prefab kaynağını da izleyip GLB'nin
tamamını saydığı için bu kontrolde yanıltır.

### ⛔ Arena dekorunu static flag'siz bırakma

Hareketsiz environment objesi (duvar, taş, ağaç, prop) **Static** işaretlenmeden sahnede kalmaz:
işaretsiz obje static batching'e girmez (her biri ayrı draw call), occlusion'a girmez, lightmap
almaz — sahne klasöründe lightmap dosyası dursa bile bake **hiçbir renderer'a uygulanmaz**. Hata
vermez; sahne yalnız Quest'te kasar. Yapraklı vegetasyon ve çimde **Occluder kapalı** kalır
(yaprak örtmez, Umbra bake'i şişer); `Animator`/`Rigidbody` altındaki obje **işaretlenmez** — static
batching transform'u dondurur, obje görünürde kımıldamaz. Static'e çekilen obje lightmap'e girer:
mesh'inde lightmap UV'si (UV2) yoksa bake sonrası siyah leke çıkar — önce UV2 üretilir, üretilemiyorsa
obje ya işaretsiz kalır ya da `Receive GI = Light Probes` alır. Kontrol:
`Tools > VortexArena > Arena > Sahne Bütçesini Ölç` → "static flag yok" uyarısı.
Gerekçe → Sistem Özeti, Tuzaklar ("Arena dekoru static flag'siz bırakılmaz").

### ⛔ Rüzgârda sallanan bitkiyi "hareketli" sayıp bake dışında bırakma

Ağaç ve çimin sallanması **shader'daki vertex animasyonudur** (`_WindScroll` / `_WindJitter`,
dünya konumu + vertex renginden); transform kımıldamaz. Bake rüzgârı **durdurmaz** — tek yan etki
yaprağın yere düşen gölgesinin sallanmamasıdır ve tepeden 10° güneşte bu görünmez. Bitkiyi
`Contribute GI` dışında bırakırsan tersini alırsın: zemin bake'li, bitki yalnız ambient'te kalır,
ortama yapıştırılmış görünür ve gerçek zamanlı gölge maliyetini ödemeye devam edersin.
⚠️ Ayrımı **Animator/Rigidbody** yapar, rüzgâr değil: gerçek controller'ı olan obje (yel değirmeni,
testere) ve ağ nesneleri static işaretlenmez. Kurulum → [Sahne Kurulumu](Sahne-Kurulumu.md).

### ⚠️ `VA_CameraRig`'in el küreleri editörde çizilir, Quest'te çizilmez

Meta `HandSphereMap` prefabındaki `sphere` objeleri (yüzlerce, gölge açık) yalnız editörde görünür;
`HandSphereMap.Start()` onları okuyup `SetActive(false)` yapar. Editör istatistiğinde çıkan üçgeni
sahnenin yükü sanma. **GameObject'ini kapatma** — script yalnız `activeSelf` küreleri okur, kapatılan
küre kavrama haritasından düşer.

### ⛔ Yerel elin görünümünü rig'in kemiklerine/hiyerarşisine dokunarak değiştirme

Oyuncunun gördüğü el `VA_CameraRig` → `OVRHandVisualLeft/Right` altındaki **OpenXR dalının**
SkinnedMeshRenderer'ıdır (`OpenXR{Left,Right}Hand/{Left,Right}Hand`); eldiven onun üstünde yalnız
**mesh + materyal override'ıdır** (varsayılanı `_Shared/Avatars/TacticalGlove/`, moda özel olanı
`Modes/<Mod>/Avatars/<Eldiven>/`). Kemikler, nesne adları ve
`HandVisual` alanları paketin olduğu gibi kalır: kavrama pozları eklem kimliğiyle, kumanda gizleyici
tam adla bağlanır. Her eldivenin kaynağı ve değiştirme akışı `blender/<Eldiven>/README.md`'dir; yeni
bir mod eldiveninin reçetesi **[Yemek Kitabı 13.4](Yemek-Kitabi.md#134-moda-özel-eldiven-oyuncunun-kendi-eli)**.
Yeni bir el mesh'i:
- paket mesh'inin (`OpenXR{Left,Right}Hand.fbx`) **bindpose'larını aynen taşır**, kemik ağırlıkları
  SMR'ın `bones` sırasına **adla** eşlenir (`GloveMeshImporter` bunu yapar). Blender'dan gelen
  FBX'in mesh'i doğrudan atanırsa kemik sırası ve bindpose farklıdır: hata çıkmaz, el parçalanmış
  çizilir — bu yüzden `GloveSkin`'in mesh alanlarına **yalnız importer'ın ürettiği asset** konur,
  FBX'in mesh'i hiç atanmaz;
- OVR dalına (`OculusHand_L/R`) konmaz: ISDK OpenXR dalında derlenir ve OVR kökünü kapatır, değişiklik
  hiç görünmez;
- `quality` Bone4 kalır (gerekçe: `Docs/Sistem-Ozeti.md` §7 "Bilek 30 cm'den bakıldığında
  `skinWeights` bir görsel ayar değil, DOĞRULUK ayarıdır");
- takım rengi alacak parça **alt-mesh 1**'dir, materyal sırası `[gövde, kayış]` kalır:
  `LocalGloves` (`VA_CameraRig` kökü) çalışma anında yalnız slot 1'i değiştirir. Sıra bozulursa
  hata çıkmaz, gövde boyanır. Takım renkleri `Girdap.Red/Blue`'dan gelir, materyale elle yazılmaz.
  Aynı kural `GloveSkin.materials` için de geçerlidir: **slot 1 takım kayışına ayrılmıştır** — iki
  ya da daha çok materyalli bir deride slot 1 takımlı modda boyanır, kayış olmayan bir parça oraya
  konmaz (tek materyalli deride boyama hiç yapılmaz).

Eldiven görünümünü yazan tek taraf `LocalGloves`'tır:
- iki eldiven SMR'ının `sharedMesh`/`sharedMaterials`'ına **başka bir betik yazmaz** — yazılan değer
  bir sonraki mod ya da takım değişiminde sessizce ezilir, hata çıkmaz;
- mod eldiveni için rig'e **ikinci bir renderer konmaz**: eldiven var olan iki SMR'ın override'ıdır;
  eklenen renderer kavrama pozlarından ve kumanda gizleyiciden bağımsız kalır, el iki kez çizilir.

### ⛔ `VenueSurvey` sahnesine elle bileşen koyma

Ölçüm sahnesinde yalnız rig, ışık ve zemin durur; denetleyiciyi, rehber geometriyi ve etiketi
`VenueSurveyGesture` sahne yüklendiğinde **kodla** kurar. Sahneye konan bir bileşen seri alan ister,
seri alan da elle bağlanan bir referans demektir — bu sahne hiçbir arena reçetesine girmez ve
hiçbir kurulum aracının denetlemediği tek sahnedir, yani boşalan bir referans kimsenin bakmayı
düşünmediği yerde sessizce çizmemeye başlar. Ölçüme yeni bir görsel gerekiyorsa yeri
`_Shared/App/Scripts/Survey/` içindeki rehber sınıfıdır, sahne değil.

### ⚠️ `VenueSurvey` mekana ait değildir — Build Settings'te elle durur

Sahne `_Shared/Scenes/` altındadır, bir mekan kutusunda değil: `Configure All Build Elements` yalnız
mekan kutularını tarar ve bu sahneye **dokunmaz**. Build listesinden düşerse jest hatasız çalışır,
sahne yüklenmez ve oyuncu bir şey olmadığını görür. Listeye elle konur ve orada kalır.

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
ve yalnız kendi runtime'ını referanslar.

### ⛔ Core'a ProBuilder referansı ekleme

ProBuilder runtime'ı build'e **yalnız `VortexArena.App`** üzerinden girer: mekan ölçüm rehberi
(`_Shared/App/Scripts/Survey/`) rehber geometriyi sahada çalışma anında üretir, o yüzden App
asmdef'i `Unity.ProBuilder`'ı referanslar. **Bu, Core için bir izin değildir.** Core her sahnede,
her modda ve admin build'inde derlenir; arena sahnelerindeki ölçü maketi ise sanat değil **ölçü
referansıdır** ve `DimensionMeshBuildStripper` onun görsel dalını build'e giden sahne kopyasından
ayıklamaya devam eder — referansı Core'a taşımak sahada hiç çizilmeyen bir mesh ailesini kalıcı
kılar. `Core.Editor`'ün ProBuilder bağımlılığı `includePlatforms:["Editor"]` sayesinde runtime'a
**bulaşmaz**.

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

### ⛔ Yakınlık grip basışına doğrudan kavramayla cevap verme

Kendi `GripSocket`'ini okuyup basışta objeyi doğrudan alan ya da olayını yollayan her bileşen, tek
soket gördüğü için yanındaki soketi göremez: soketlerin çakıştığı yerde (üst üste duran eşyalar,
dağıttığı malzemenin arasında duran dağıtıcı) tek basışa birden çok taraf cevap verir — tek avuca
birkaç obje, telde birkaç mesaj, tek ele birkaç yazar. Belirti sessizdir, hata vermez. Doğru yol
`IGrabClaimant` uygulamak ve basışta yalnız `GrabArbiter.Submit` ile aday olmaktır; kavramayı
hakemin çağırdığı `CommitGrab` yapar. Bilerek çoklu kavrayan bir yol (taşıyıcının kargo hacmi)
hakemden geçmez, ama o istisna **basış yolu değildir**.

### ⛔ Kapatılan çarpışmayı iki collider iç içeyken geri açma

`Physics.IgnoreCollision(a, b, false)` çift **üst üsteyken** çağrılırsa fizik o kareyi bir
iç-içe-geçme olarak çözer ve nesneyi odanın öbür ucuna fırlatır — belirti "eşya kendiliğinden
uçtu"dur, hata verilmez. Kapatılan çarpışma ancak çift **ayrıldıktan** sonra geri açılır
(`Physics.ComputePenetration` ile sınanır). Aynı sebeple kapatılan çift bileşenin ömrüne
bırakılmaz: bayrak fizik sahnesinde yaşar, `OnDisable`'da geri açılmazsa o eşya maç boyunca
birbirinden geçer.

### ⛔ Tutulabilir bir türün prefabında kavrama pozunu serbest bırakma

Ağ nesnesi ele **kanonik kavrama poziyle** bağlanır ve duruş telde gitmez: iki uç aynı kaydı okur.
Serbest kavrama (elin objeye değdiği yerden tutmak) her istemcide farklı bir ofset demektir ve obje
uzak başlıkta elin yanında durur. Kavrama stüdyoda yazılır, çalışma anında ölçülmez.

### ⛔ Eşyayı ele sıfır ofsetle takma

Eşyayı ele koyan her kod (yeni bir granter, moda özel bir "ele ver" bileşeni) duruşu
`ItemGripSolver.Solve` ile kurar; el anchor'ına `localPosition = zero` ile takmak kaydı yok sayar.
El ise kaydı her zaman okur (`HandGripPoser`): el ile eşya birbirine göre doğru görünür ama ikisi
birlikte gerçek kumandadan kayar, bilek dönünce eşya görünmeyen kumandanın etrafında yay çizer.
Uzak uç ortak çözücüyü kullandığı için diğer oyuncular doğru görür — hata yalnız tutanın gözündedir.

### ⛔ Ön kabza bağına mesafe ya da kabul yarıçapı kapısı koyma

İkinci el, öbür elde iki elli bir silah varken boş elin grip'i basılı tutulduğu an bağlanır; tek
koşul ön kabza kaydının **yazılmış** olmasıdır (`Weapon.ForegripAuthored`). Ön kabzanın ikinci elin
kumandasından uzaklığı *|ellerin arası − silahın kavrama arası|* kadardır ve normal nişanda her
makul yarıçapı aşar — mesafeye bakan bir kapı bağı oyuncu tuşu bırakmadan koparır. Silahı oyuncunun
kendisinden uzak tutan şey yarıçap değil **çözücünün gövde konisidir** (`ItemGripSolver`) ve ⚠️ koni
**iki uçta da** uygulanır: tek uçta uygulanırsa aynı silah iki ekranda farklı duruşta görünür, bu
yüzden her iki uç çözücüye bir kafa konumu vermek zorundadır. Silahlarda soket küresi de çizilmez
(küre yakınlık soketlerine aittir). Uzun gerekçe: `Sistem-Ozeti.md` §7 Tuzaklar.

### ⛔ Mesafeli kavrama bileşenini "nasılsa filtreliyorum" diye prefabda bırakma

Alma yolu `ProximitySocket` / `WristHolster` / `None` olan bir eşyanın prefabında
`DistanceGrabInteractable` ya da `DistanceHandGrabInteractable` **bulunmaz**. Aday listesini
kapatmak objeyi alınamaz yapmaz: boş listeyle bile interactor hover'a girer ve `Select()` kavrama
basışını hiçbir şey seçmeden kuyruktan düşürür — basış sessizce yenir ve belirti, kavranmak istenen
objede değil **yakınındaki başka bir objede** "kavrama tuşu bazen çalışmıyor" olur. Hazırlık
panelindeki *Eşya alma yolu ↔ prefab* satırı bunu listeler ama **düzeltmez**.

### ⛔ Elde/bilekte taşınan Rigidbody'de `isKinematic`'i elle yazma, interpolasyonu açık bırakma

Transform'la sürülen kinematik gövdede `Interpolate` açık kalırsa fizik transform'u her karede
geri yazar: eşya elin çevresinde kayar, yalnız yerel oyuncu görür (uzak taraf Rigidbody'yi siler).
Kinematik bayrağı **`RigidbodyDrive.SetKinematic`** ile çevrilir — interpolasyon onunla birlikte
gider. Taşırken kapattığın (`detectCollisions`, yerçekimi) neyse fırlatma yolu (`Throwable.Arm`)
kendisi geri açar; "taşıyan geri açar" diye güvenme, uzak kopya taze prefabdan doğduğu için
eksiği yalnız atanın kendi kopyası gösterir. Gerekçe: `Sistem-Ozeti` §7 "Transform'la sürülen
Rigidbody".

### ⛔ Tutulan ağ nesnesinde eşya baytını doldurma

`WorldSingle` bir eşya elde tutulurken `itemL`/`itemR` **`0` kalır**. Baytı da yazarsan uzak elde
**iki obje** çizilir — biri ağ nesnesinin kendi örneği, biri baytdan üretilmiş klon — ve ikisi
gecikmede ayrışır. Bastırma kaynaktadır (`HeldItems` slotu, `ItemDefinition.IsWorldSingle`);
tüketici tarafında ayrıca "bu objeyi çizme" dalı açılmaz, ilk unutulan yerde geri gelir.

### ⛔ Duran eşyayı sıkılı elle (seviye tetik) aldırma — seviye yalnız uçuştaki obje içindir

Kavrama kenar tetiktir: soketin içinde **basış** alır. Uçuştaki obje (`Awake`, `Held` değil)
istisnadır — boş ve sıkılı bir el ona değince alır, çünkü gerçek yakalama hareketi "önce kapan,
sonra gelsin"dir ve 12 m/sn'de basışı zamanlamak imkânsızdır. Bu istisnayı duran objeye genişletme:
sıkılı elle yanından geçilen her eşya ele yapışır, silahlı el yanından uçan spatulayı tüfeğin yerine
alır. Yakalanabilirlik için prefab yarıçapını da büyütme — uçuş yarıçapı koddaki
`GripSocket.CatchRadius`'tır, prefabdaki yarıçap **duran** objenin kabul hacmidir.

---

## Serialize edilen veriler

### ⛔ Enum'un başına/ortasına yeni değer ekleme

Unity enum'ları **sayısal indeksle** saklar. `Team`'e başa bir değer eklemek sahnelerdeki tüm
`BaseZone`/`Weapon` takımlarını kaydırır. Yeni değer **her zaman sona** eklenir —
`Team.Neutral` bu yüzden sonda (`BaseZone`'da "herkese açık" anlamına da gelir).

Aynısı `HitZone` (`Body` sıfırda kalır) / `ModeTeamMode` / `ModeScoreKind` / `ModeReviveAnchor` /
`ModeWeaponSource` / `ModeAudioEvent` için de geçerli.

Arayüz kitinin enum'ları da öyledir (`UiButtonKind` · `UiChipKind` · `UiGradientMode` ·
`GirdapFont`): araya giren bir değer üretilmiş her Girdap prefabında düğme türünü, rozet türünü,
gradyan yönünü ve fontu kaydırır — görünüm bozulur, derleme susar.

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

### ⛔ Uzamsal veri (Scene) iznini açma

`USE_SCENE` izni manifest'e, `sceneSupport` `OculusProjectConfig`'e, `requestScenePermissionOnStartup`
`OVRManager`'a **KONMAZ**: sahne (Scene) API'si hiçbir yerde kullanılmıyor (uzamsal çapa için
`USE_ANCHOR_API` yeter) ve açılıştaki "uzamsal veriler" dialogu odağı çalıp o pencerede T-poz
yedeğini tele koyar, sahada da gövde takibi izniyle karıştırılır.

### ⛔ `.unitypackage` arşivini `Assets/` altına kopyalama

Paket Unity'nin içe aktarma penceresinden alınır; arşivin kendisi projeye girmez. Aynı yayıncının
iki pack'i **aynı GUID'leri paylaştığı** için ikincisi birincinin klasörüne açılır — yani klasör
adı artık içeriğini anlatmaz. Yeni bir pack aramadan önce **mevcut pack klasörüne bak**.

### ⛔ Paket içe aktarırken `Assets/Settings/` ve `ProjectSettings/` satırlarını işaretli bırakma

URP şablonundan üretilmiş projelerin `Mobile_RPAsset` / `PC_RPAsset` / global settings asset'leri
**aynı GUID'leri taşır**; bu asset'leri içeren bir pack içe aktarılınca bizimkilerin **üzerine
yazar** — hata da uyarı da çıkmaz. Sonuç sessizdir: Quest'te MSAA/HDR/gölge değişir, kapalı tutulan
varyant çarpanları (Light Cookies, LOD Cross Fade) açılır ve build süresi katlanır. İçe aktarma
penceresinde bu iki klasörün işareti kaldırılır; commit'ten önce `git status`'ta `Assets/Settings/`
görünüyorsa geri alınır.

### ⛔ `Assets/ThirdPartyPackages/` altındaki klasörleri editör AÇIKKEN taşıma

Windows dosya kilidi yüzünden taşıma yarıda kalır ve geride yetim `.meta`'larla yarım bir ağaç
bırakır. Taşıma editör kapalıyken `git mv` ile yapılır; tek kod ayağı `WeaponKitBuilder.PackRoot`
sabitidir (tek satır) — o güncellenmezse silah kiti kaynaklarını bulamaz ama hata da vermez.

### ⛔ `.gitattributes`'ta LFS satırı olmayan uzantıda büyük binary commit'leme

Model/doku/ses `.gitattributes`'taki uzantı listesiyle LFS'e gider; listede olmayan uzantı
(`.gltf`, `.bin`, yeni bir format) normal blob olarak commit'lenir. GitHub 100 MB'ı aşan blob'u
reddeder, VS Code bunu yanıltıcı biçimde "önce Pull yap" diye gösterir. Yeni formatta paket
eklemeden önce uzantıyı `.gitattributes`'a `lfs` olarak ekle; push'lanmamış commit'te kaldıysa
`git lfs migrate import --include="*.<uzantı>" --include-ref=refs/heads/<dal> --exclude-ref=refs/remotes/origin/<dal>`.
Bu komut aynı commit'i gösteren **başka yerel dalları da** yeniden yazar — yedek dal açacaksan
commit hash'ini ayrıca not al. ⚠️ Ardından **`git lfs checkout`** çalıştır: migrate o uzantının
diskteki dosyalarını ~130 baytlık LFS işaretçi metnine çevirir; `git status` temiz görünür ama
Unity bunları import edemez ve sahnede hata vermeden "Missing Prefab with guid" gösterir.

### ⚠️ Yamalı satıcı dosyasını paketi yeniden içe aktararak EZME

Paket yeniden içe aktarılırsa yama geri alınır — içe aktarma penceresinde o dosyanın işaretini
kaldır. Yamalı dosyalar:

- **Construction Site `LightmappedLOD.cs`** — static batch'e giren ve **kapalı** renderer'ları
  (optimizasyonla `LODGroup`'tan çıkarılmış LOD seviyeleri) atlar: yamasız hâli her sahne
  yüklemesinde LOD başına uyarı/log basar ve gözlük günlüğü saniyelik sınırda kısılıp gerçek
  satırlar kaybolur.
- **Toon Series `Shared/Shaders/CustomToon*.shader`** (satırlar `VortexArena patch` yorumuyla
  işaretli) — Forward geçişi toon rengini URP aydınlatmasından **ikinci kez geçirmeden** yazar ve
  toon terimindeki ana ışık shadowmask'ı okur. Yamasız hâlde güneşe dönük olmayan her dikey yüz
  (ev cephesi, ağaç gövdesi) yalnız dolaylı ışıkla kalıp kararır; yalnız dolaylı ışığı kaldırmak
  bunu çözmez. ⚠️ Bu shader'lar **Amplify Shader Editor** çıktısıdır: grafiği ASE'de açıp
  kaydetmek dosyayı yeniden üretir ve yamayı **sessizce** siler — değişiklik elle yapılır.
  Toon teriminde güneşin **Intensity**'si normalize edilir, parlaklığı değiştirmez; sahneyi
  aydınlatan kol ortam/dolaylı ışık ve bake'tir.

### ⚠️ `Shader.Find` build'de `null` dönebilir

Hiçbir materyalin referanslamadığı shader strip edilir. Runtime'da üretilen görseller bu yüzden
UI/TMP shader'ları üzerinden çizilir.

### ⚠️ Silah dengesi değişikliği APK build'i ister

Hasar sayıları istemcide yaşar; sunucuyu yeniden başlatmak yetmez.

### ⛔ Anlık çalan kısa sesi `Preload Audio Data` kapalı bırakma

`GameSoundBank`'a (ya da `PlayOneShot` ile anında çalan herhangi bir yere) giren kısa ses
**`Preload Audio Data` açık, `ADPCM`, `Decompress On Load`** olur. Kapalıyken klip ilk çalınacağı
anda yüklenir; Quest'te o ilk `PlayOneShot` sessizce düşer — editörde duyulur, gözlükte duyulmaz.

### ⛔ Oyuncu build'inde `PlayerSettings` geri almasını `EditorApplication.Exit`'ten SONRAYA bırakma

Sürümlü oyuncu build'i `PlayerSettings`'i (bundle id, `bundleVersion`, `AndroidBundleVersionCode`,
ürün adı) geçici olarak değiştirir; eski değerlerin geri yazılması **`Exit` çağrılmadan önce**
bitmiş olmalıdır. `Exit` süreci anında sonlandırır — `finally` bloğu çalışmaz ve
`ProjectSettings.asset` diskte sürümlü değerlerle kalır, sonraki her build o bozuk hâlden başlar.
⚠️ Paket eki **noktasızdır** (`com.vortex.arenav132`, `com.vortex.arena.v132` DEĞİL): Android paket
segmenti rakamla başlayamaz.

### ⛔ Shader varyantını katlayan ayarları açma

Android grafik API listesi **yalnız Vulkan** kalır: listeye OpenGLES3 eklemek her shader varyantını
birebir ikiye katlar, Quest 3/3S Vulkan ile koşar, GLES3 yedeğine ihtiyaç yoktur.
URP'nin varyant eleyicisi **projedeki TÜM kalite seviyelerinin** URP asset'ini ve
`Graphics > Default Render Pipeline` alanını okur — kalite seviyesindeki `excludedTargetPlatforms`
elemeyi **etkilemez**. Yani masaüstü kalite seviyesinin asset'indeki yüksek gölge cascade sayısı ve
soft shadow kalitesi, Android dışlanmış olsa bile Quest build'ine sızıp varyantı ikiye katlar; bir
kalite seviyesinin asset'ini "nasılsa o platforma girmiyor" diye zenginleştirme.
`Shader Stripping > Fog Modes` = Custom, yalnız **Linear** işaretli: başka bir fog
formülü seçen sahne **sisi sessizce kaybeder** (hata vermez) — gerçekten gerekiyorsa önce eleme
ayarı açılır, bedeli varyant sayısının ~1,5 katına çıkmasıdır. **İki URP asset'inde de**
(`Mobile_RPAsset` · `PC_RPAsset`) **Light Cookies** ve **LOD Cross Fade** kapalıdır, **Soft Shadow
Quality** ikisinde aynıdır (Low): biri açılınca ya da kalite ayrışınca varyant her iki build'de de
katlanır. Cookie'li ışık kullanılacaksa önce Light Cookies açılır (yoksa cookie sessizce çizilmez),
kapalı cross-fade'de LOD geçişi yumuşamaz.
⚠️ Yavaş build'in teşhisi `deploy/player-build.log` içindeki `compiled <N> variants` satırları,
hangi çarpanın açık kaldığı ise `Logs/shadercompiler-*.log`. Gerekçe: `Docs/Sistem-Ozeti.md`,
"Tuzaklar".

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
