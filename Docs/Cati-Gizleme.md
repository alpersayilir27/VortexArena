# Çatılı Arenalar ve Admin Kuş Bakışı — Teknik Not

> **Kime:** arena/sahne yapan geliştiriciye (level designer) ve admin arayüzüne dokunacak
> programcıya. **Kapsam:** `ArenaRoof` bileşeni, `ArenaRoof` katmanı, `GameObject > VortexArena >
> Arena Roof` editör aracı ve admin tarafındaki "Çatı" tercihi.
>
> İlgili: bileşen sözlüğü `Sistem-Ozeti.md` §4 · arena bağlama adımları `Sistem-Ozeti.md` §6.4 ·
> mimari kural `CLAUDE.md`. Bu özellik **ağ protokolüne dokunmaz** — `ArenaNet-Protokol.md`'de
> karşılığı yoktur, tamamen istemci-görsel bir konudur.

---

## 1. Ne işe yarar

Admin gözlemcinin **kuş bakışı** kipi arenayı tepeden ortografik gösterir (`AdminSpectatorCamera`).
Arenanın kapalı bir tavanı/çatısı varsa bu kip işe yaramaz: operatör çatının üstünü görür, sahayı
göremez.

`ArenaRoof`, "bu geometri çatıdır" diyen bir işaretçidir. Admin kuş bakışına geçtiğinde çatı
**çizilmez**, POV/serbest kipe dönünce geri gelir.

**Oyuncu tarafında etkisi sıfırdır.** Bileşen kendiliğinden hiçbir şey yapmaz; yalnız
`ArenaRoof.ApplyAll()` çağrıldığında tepki verir ve onu yalnız admin gözlemci çağırır. Quest
build'inde bu kod yolu hiç koşmaz.

**İsteğe bağlıdır.** Açık tavanlı arenalarda (bugünkü `Arena12x12`, `IceWorld`…)
bu adım hiç yapılmaz, hiçbir şey değişmez.

---

## 2. Hızlı reçete (level designer için — 30 saniye)

1. Sahnede çatı geometrisinin **kökünü** seç (tek bir parent altında toplanmış olsun; değilse
   önce boş bir GameObject açıp çatı mesh'lerini altına al).
2. `GameObject > VortexArena > Arena Roof`.
3. Bitti. Sahneyi kaydet.

Menü şunları yapar: seçime `ArenaRoof` bileşenini ekler ve **altındaki tüm Renderer'lara**
`ArenaRoof` katmanını damgalar.

**Doğrulama:** sahne görünümünün sağ üstündeki **Layers** açılırından `ArenaRoof`'u kapat — çatı
kaybolmalı, geri kalan her şey durmalı. Kaybolan şey doğru değilse hiyerarşiyi düzelt ve bileşene
sağ tıklayıp *Çatı katmanını uygula*'yı tekrar çalıştır.

> Çatı tek bir kök altında değilse: birden çok objeye de aynı menüyü uygulayabilirsin, her birine
> ayrı bir `ArenaRoof` eklenir. Sahnede kaç tane olduğunun önemi yok — hepsi birlikte yönetilir.

---

## 3. Katman: `ArenaRoof` (user layer 8)

`ProjectSettings/TagManager.asset` içinde **8 numaralı kullanıcı katmanı** olarak tanımlıdır
(0–7 Unity rezervidir, oraya yazılmaz).

**Katman NE İŞE YARAR:** "hangi objeler gizlenecek" sorusunu sahnede görünür kılar — Layers
süzgeci, Inspector'daki Layer alanı, hiyerarşi araması. Yani ayıklama ve gözle doğrulama içindir.

**Katman NEYİ BELİRLEMEZ:** gizleme davranışını. Davranış bileşenin çözdüğü **Renderer listesinden**
gelir. Damgayı unutsan da (ör. sonradan mesh ekleyip menüyü tekrar çalıştırmadıysan) gizleme
çalışmaya devam eder; yalnız sahnede süzme kolaylığını kaybedersin.

Bu bilinçli bir ayrımdır: davranışı katmana bağlasaydık, katmanı elle değiştiren biri özelliği
sessizce kırardı.

> ⚠️ **Katman fizik davranışını değiştirmez — bugün.** Doğrulandı: `ProjectSettings/DynamicsManager.asset`
> çarpışma matrisi tümüyle açık (her katman her katmanla çarpışır) ve projedeki tek raycast
> (`Weapon.cs`, `Physics.Raycast(... range)`) **LayerMask kullanmıyor**, yani tüm katmanlara vurur.
> Çatıyı layer 8'e almak atış/çarpışma sonucunu etkilemez.
>
> Bu bir **gelecek uyarısıdır**: ileride bir raycast'e veya çarpışma matrisine LayerMask koyarsan
> `ArenaRoof` (8) katmanını listeye eklemeyi unutma — yoksa çatı sessizce "mermi geçiren" bir
> yüzeye döner. Ayrıca gizleme **yalnız admin ekranında** olduğu için "çatı gizliyken mermi geçsin"
> gibi bir ihtiyaç yoktur: oyuncu çatıyı hep görür, hasarı zaten atıcı istemci hesaplar (§10.3).

---

## 4. Editör aracı — tam davranış

Bileşene ulaşmanın **üç** yolu var; üçü de aynı şeyi yapar.

### 4.1 `GameObject > VortexArena > Arena Roof` (asıl yol)

Kaynak: `Assets/_Shared/Core/Editor/ArenaRoofMenu.cs` (asmdef `VortexArena.Core.Editor`, yalnız Editor).

| Davranış | Ayrıntı |
|---|---|
| **Çoklu seçim** | Seçili tüm objeler tek çağrıda işlenir. Unity bağlam menüsünde komutu her obje için bir kez çağırdığı için ilk çağrı dışındakiler elenir (`command.context` kontrolü) — aksi hâlde iş N kez tekrarlanırdı |
| **Zaten bileşeni olan obje** | Bileşen tekrar eklenmez ama **katman yine tazelenir** — sonradan mesh eklenmiş çatılar için pratik yol budur |
| **Prefab ASSET'i** | Atlanır (`go.scene.IsValid()` kontrolü). Sahne objesi bekler; projedeki prefab'a uygulamak istiyorsan prefab'ı Prefab Mode'da aç ve orada çalıştır |
| **Undo** | Tüm değişiklikler **tek adımda** geri alınır (`Undo.CollapseUndoOperations`) — Ctrl+Z bir kez |
| **Katman yoksa** | `ArenaRoof` katmanı projede tanımlı değilse konsola uyarı basar ve devam eder (gizleme yine çalışır) |
| **Rapor** | Konsola `[ArenaRoof] N bileşen eklendi, M objede katman tazelendi, K atlandı` satırı |
| **Menü koşulu** | Seçim boşken menü öğesi grileşir |

Menü sırası 31'dir — `GameObject > VortexArena > Network Parent` (30) ile aynı grupta, hemen altında.

### 4.2 Bileşen ilk eklendiğinde (`Reset`)

`Add Component > VortexArena > Arena Roof` ile elle eklersen de katman **otomatik damgalanır**
(`Reset()`). Yani menüyü kullanmayı unutup bileşeni elle ekleyen biri de doğru sonucu alır.

### 4.3 Bileşene sağ tık → *Çatı katmanını uygula* (`ContextMenu`)

Çatıya **sonradan mesh eklediğinde** çalıştırılır: Renderer listesini yeniden çözer ve katmanı
tazeler. Kendi içinde `Undo` kaydı tutar.

### 4.4 Inspector alanı: `roofRenderers`

| Durum | Sonuç |
|---|---|
| **Boş (varsayılan)** | Bileşenin altındaki **tüm** Renderer'lar (kapalı olanlar dâhil) çatı sayılır. Normal kullanım budur — hiçbir şey doldurmana gerek yok |
| **Dolu** | Yalnız listedeki Renderer'lar gizlenir. Çatı kökünün altındaki bir alt kümeyi (ör. kirişler kalsın, paneller gitsin) hedeflemek istersen kullan |

---

## 5. Operatörün gördüğü taraf

Admin uygulamasında **Tercihler paneli → GÖRÜNÜM → Çatı** satırı, `[<] değer [>]` döngüleyicisi:

| Değer | Anlamı |
|---|---|
| `görünür` | Çatı hiç gizlenmez (oyuncunun gördüğü hâl) |
| `kuş bakışında gizli` | **Varsayılan.** Yalnız kuş bakışı kipinde kalkar |
| `hep gizli` | POV ve serbest kipte de tavan kapatmaz |

- Tercih **o admin PC'sine özeldir** (`PlayerPrefs`, anahtar `VortexArena.Admin.Roof`) — diğer
  adminlere yayılmaz. Görünüm tercihleri bilinçli olarak yereldir; ortak olan yalnız mod/harita
  seçimidir (bkz. `ArenaNet-Protokol.md` §5.3 `admin_state`).
- Ayrı klavye kısayolu **yoktur**. Kuş bakışına geçmek zaten `3` tuşudur ve varsayılan tercihte
  çatı onunla birlikte kalkar.
- Sahnede hiç `ArenaRoof` yoksa bu satır bir işe yaramaz ama zararsızdır (hata/uyarı üretmez).

---

## 6. Nasıl çalışıyor (programcı için)

Kaynak: `Assets/_Shared/Core/Arena/ArenaRoof.cs`.

### Gizleme yöntemi

`MaterialPropertyBlock` ile `_BaseColor` alfası — `ArenaBoundary`'nin duvar saydamlığıyla **birebir
aynı desen** (yeni bir teknik getirilmedi).

Tam gizlemede (`alpha < 0.004`) Renderer **kapatılmaz**, `ShadowCastingMode.ShadowsOnly`'ye alınır:

> Çatı çizilmez ama **gölgesini atmaya devam eder.** Renderer'ı `enabled = false` yapsaydık gölge de
> giderdi, iç mekân dışarıdan gelen ışıkla dolar ve kuş bakışı düz, kontrastsız bir aydınlık levhaya
> dönerdi. Gölgenin kalması haritanın okunabilirliğinin yarısıdır.

Görünür duruma dönerken her Renderer'ın **özgün** `shadowCastingMode`'u geri yüklenir (Awake'te
saklanır) — `Off` ayarlanmış dekoratif bir mesh yanlışlıkla gölge atmaya başlamaz.

### Sahne geçişine dayanıklılık

Son uygulanan alfa `static` tutulur ve yeni sahnedeki `ArenaRoof` onu `OnEnable`'da kendine uygular.
Sonuç: admin kuş bakışındayken başka bir arena açıldığında **çatı bir kare bile görünmez**. Sahne
yükleme ile kamera kipi arasındaki yarışı bu çözer.

### Tetikleyiciler

`AdminSpectator.RefreshRoof()` tek giriş noktasıdır; `AdminSession.RoofAlphaNow()`'u (tercih + o anki
kamera kipi) `ArenaRoof.ApplyAll()`'a verir. Üç çağıran:

| Çağıran | Ne zaman |
|---|---|
| `AdminSpectator.AdoptScene` | Her `sceneLoaded` — yeni arenanın çatısı doğru değere otursun |
| `AdminSpectatorCamera.EnterMode` | Kamera kipi değiştiğinde |
| `AdminPreferencesPanel.StepRoof` | Operatör tercihi değiştirdiğinde (kip değişimini beklemesin) |

Etkin çatılar statik bir listede tutulur (`ArenaRoof.Active`) — her karede `FindObjectsByType`
taraması yoktur, maliyet sıfırdır.

---

## 7. Neden böyle — alternatifler ve reddedilme gerekçeleri

| Alternatif | Neden seçilmedi |
|---|---|
| **Kamera `cullingMask`'i** (layer'ı maskeden çıkar) | Tek satırlık çözüm ve cazip, ama gölgeyi de eler → iç mekân aydınlanır. Ayrıca davranışı tamamen katmana bağlar: katmanı elle değiştiren biri özelliği sessizce kırar. Yine de layer'ı **ayıklama için** tuttuk |
| **Shader ile yükseklik kesme** (kamera altındaki her şeyi kes) | URP shader bakımı gerektirir, üçüncü parti/ProBuilder malzemeleriyle uyumsuzluk riski, çatı dışındaki geometriyi de keser |
| **Sahne başına ayrı "admin çatısız" varyant** | İki sahne bakımı = sapma garantisi |
| **Her karede `FindObjectsByType<ArenaRoof>`** | Gereksiz; statik kayıt listesi bedava |

---

## 8. Tuzaklar

- **Ara alfa (ör. %30 saydam çatı) yalnız Transparent malzemede çalışır.** Opaque malzemede
  `_BaseColor.a` görsel olarak etkisizdir → ara değer 1 gibi davranır. `0` yine tam gizler (o
  `ShadowsOnly` yoluyla olur, alfadan bağımsız). Duvar saydamlığındaki kısıtın aynısıdır. Bugünkü
  arayüz zaten yalnız 0/1 üretiyor; yarı saydam çatı isteyen malzemeyi Transparent'a almalı.
- **Çatıyı `ArenaBoundary`'nin `wallRenderers`'ına KOYMA.** İkisi aynı Renderer'a farklı alfa yazar,
  hangisi son yazarsa o kazanır → titreme. Duvar duvardır, çatı çatıdır.
- **`ArenaRoof` bileşenini devre dışı bırakma** (`enabled = false`): kayıt listesinden düşer ve çatı
  o anki hâlinde donar. Gizlemek istiyorsan tercihi kullan, bileşeni kapatma.
- **Prefab'a bake edeceksen** katmanı Prefab Mode'da damgala; sahne örneğinde yapılan katman
  değişikliği prefab override'ı olarak kalır ve başka örneklere taşınmaz.
- **Yeni katman eklerken 8'i kaydırma.** `ArenaRoof` user layer 8'dir; TagManager'da araya katman
  sokup indeksleri kaydırırsan sahnelerdeki damgalar yanlış katmana düşer (Unity katmanı **indeksle**
  saklar, isimle değil).

---

## 9. Test adımları (PR öncesi)

1. Çatılı sahneyi aç, `Tools > VortexArena > Dev` penceresinden rolü **admin** yap (`Ctrl+Alt+R`).
2. Play → `3` (kuş bakışı): çatı kalkmalı, **zemindeki gölge deseni durmalı**.
3. `2` (serbest) → çatı geri gelmeli, gölge kipi bozulmamış olmalı.
4. Tercihler (`P`) → Çatı → `hep gizli`: POV'da da tavan olmamalı. → `görünür`: hiçbir kipte kalkmamalı.
5. Kuş bakışındayken tercihler panelinden **başka bir harita** seç (sahne yüklenir — sunucuya
   bağlıysan oyuncular da o arenaya geçer, §10.7): yeni sahnenin çatısı **hiç görünmeden**
   gizli gelmeli.
6. Rolü **player** yap, aynı sahneyi aç: çatı normal, hiçbir gizleme olmamalı.

---

## 10. Sorun giderme

| Belirti | Sebep / çözüm |
|---|---|
| Kuş bakışında çatı hâlâ duruyor | Bileşen çatının **kökünde** değil (kardeşinde/üstünde). Hiyerarşiyi kontrol et: Layers süzgecinden `ArenaRoof`'u kapat, kaybolan set doğru mu? |
| Çatının bir kısmı kalkıyor, bir kısmı kalıyor | Kalan mesh'ler bileşenin altında değil, ya da `roofRenderers` elle doldurulmuş ve eksik. Alanı boşalt (tüm çocuklar çatı sayılır) veya listeyi tamamla |
| Çatı kalkınca içerisi bembeyaz / kontrast kayboldu | Çatı gölge atmıyor. Renderer'ların özgün `shadowCastingMode`'u `Off` olabilir — `On` yap. Ya da sahnede yönlü ışık/gölge kapalı |
| Layers listesinde `ArenaRoof` yok | `ProjectSettings/TagManager.asset` güncel değil (başka branch'ten gelmiş olabilir). Katman user layer 8'de tanımlı olmalı |
| Menü öğesi gri | Hiçbir obje seçili değil |
| Konsolda "`ArenaRoof` katmanı yok" uyarısı | Yukarıdaki satır. Gizleme yine çalışır, yalnız sahnede süzemezsin |
| Oyuncu build'inde çatı kayboluyor | Olmaması gereken bir durum — `ArenaRoof.ApplyAll` yalnız `AdminSpectator`'dan çağrılır ve o rol admin değilse kendini yok eder. Böyle bir şey görürsen çağıran yeri bul, bu bir regresyondur |

---

## 11. Diğer VortexArena editör araçları (yönlendirme)

Bu notun kapsamı çatıdır; tam liste ve sorumluluklar `CLAUDE.md` → *Editor araçları*, ayrıntılar
`Sistem-Ozeti.md` §4/§6'dadır. Özet:

| Araç | İş |
|---|---|
| `Tools > VortexArena > Dev` (`Ctrl+Alt+R`) | Rol · sunucu hedefi · Play başlangıcı |
| `Tools > VortexArena > Create Arena From Template` | Yeni arena kutusu + sahne + `MapDefinition` + katalog + Build Settings |
| `Tools > VortexArena > Export Server Config` | `MapDefinition` SO'larından `Server/config/maps.json` |
| `GameObject > VortexArena > Network Parent` | Sahne objesine `NetIdentity` + benzersiz `sceneId` |
| **`GameObject > VortexArena > Arena Roof`** | **Bu not (§4)** |
