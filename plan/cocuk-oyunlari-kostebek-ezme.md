# Çocuk Oyunları — Köstebek Ezme (plan; henüz kod YOK)

Ailenin ikinci oyunu ve ilk **yarışmalı** çocuk oyunu. Zemin `Docs/Gelistirici/Yemek-Kitabi.md`
"Çocuk oyunu eklemek" reçetesidir; iki bilinçli sapma var: `Teams = TwoTeams` + `Scoring = Team`
(kooperatif değil — kırmızı ile mavi yarışır ve **kazanan vardır**). Ailenin değişmezi korunur:
`Weapons = None` (hasarı kapatan şey bu; balyoz silah DEĞİL, eşyadır), `Revive = None` (ölüm yok),
`HoldsResultForOperator => true`. **Protokol sürümü ARTMAZ:** yeni modeId/kind/olay/netItemId
mevcut mekanizmalara biner (Hamburgerci ile aynı durum).

## 1. Senaryo

Free-roam 14×16 alanda zemine dağınık köstebek delikleri. Maç başında herkes kırmızı ya da mavi
takımdadır (sunucu lobide dengeler — yeni oyuncu küçük takıma; admin `set_team` ile değiştirir);
Live girişinde iki ele birer balyoz gelir ve vardiya boyunca ellerde kalır. Deliklerden rastgele
köstebek çıkar — kırmızı ya da mavi — **2 sn** havada durur, sonra iner. Oyuncu **kendi renginin**
köstebeğini ezince takımı puan kazanır; **rakip renge** vurunca kendi takımı puan kaybeder.
Ölme/hasar/taban yok. Süre bitince skoru yüksek takım kazanır (eşitse berabere); sonuç ekranı
operatör kapatana kadar kalır. Oyuncu başına doğru vuruş, yanlış vuruş ve net katkı tutulur.

## 2. Kural şekli (mod tanımı)

| Alan | Değer |
|---|---|
| modeId / displayName | `mole` / "Köstebek Ezme" |
| gameType | Kids — ÜÇ yerde: sunucuda `GameType => "kids"`, `ModeDefinition`, haritanın `MapDefinition`'ı (biri tutmazsa `start_match` sessizce reddedilir) |
| teamMode | TwoTeams (taban KONMAZ — taban `OwnBase` canlanmasının aracıydı, burada canlanma yok) |
| scoring | Team (`scoreRed`/`scoreBlue`); bireysel katkı AYRICA `AddPlayerScore` ile `PlayerInfo.score`'a yazılır (API iki kanalı birlikte kullanmaya izin verir) |
| friendlyFire | kapalı (hasar zaten yok) |
| revive | None |
| weapons | None |
| roundSeconds / scoreLimit | 300 önerisi (admin geçersiz kılar) / 0 — bitişi yalnız süredir |
| HoldsResultForOperator | true |
| hudPrefab | `MoleHud` |

## 3. Yetki dağılımı

| Sunucu (`MoleMode`) | İstemci (`VortexArena.Modes.Mole`) |
|---|---|
| Çıkış zamanlayıcısı: hangi delik, hangi renk, ne zaman; dengeli renk destesi | Köstebeğin yükselme/bekleme/ezilme/inme sunumu (stage + payload'dan) |
| `whack` doğrulaması (stage + nonce); ilk gelen kazanır | Balyoz sallama algısı + `whack` raporu (yalnız KENDİ balyozu) |
| Skor: doğru = takım & oyuncu +P; yanlış = takım & oyuncu −P | Balyozların Live'da ele verilmesi + takım rengine boyanması |
| Doğru/yanlış sayaçları → `modeState` | HUD: takım skorları, kendi doğru/yanlış/katkı, süre; sonuç tablosu sütunları |
| `IsMatchOver`: süre dolunca önde olan takım | — |

Güven modeli atış hattıyla aynıdır: vuruşu istemci raporlar, sunucu yalnız **durum** kapılarını
doğrular (köstebek ayakta mı, bu çıkışta ilk vuruş mu) — mesafe/fizik yargılamaz, metre bilmez.

## 4. Ağ sözleşmesi (uygulamada ÖNCE `Docs/ArenaNet-Protokol.md`'ye yazılır)

- **`mole_hole`** — sahne ağ objesi (delik başına bir `NetIdentity`): `maxHp 0`, `grab none`,
  olaylar `whack` (`policy anyone` — gönderen objenin sahibi değil). Köstebek deliğin sunum
  çocuğudur; **ayrı kind değildir** ve `NetObjectBody`/`NetObjectPoseSender` **eklenmez**
  (sunum sahnede kalır — yükseliş animasyonu telde taşınmaz).
- **stage:** `0 Hidden` · `1 Up` · `2 Squashed`. Hepsini sunucu yazar; `Squashed` kısa bir ezik
  süresinden sonra `Hidden`'a döner.
- **payload `s`:** `Up`'ta `n:<çıkış sayacı>;c:red|blue` — `Squashed`'ta ek `by:<playerId>;ok:1|0`.
  Geç katılan her state'te payload'ı yeniden okur (Hamburgerci müşteri kuralıyla aynı).
- **`whack`** (istemci→sunucu): payload `n:<sayaç>`. Bayat sallama (köstebek inmiş ya da yeni
  çıkışla sayaç dönmüş) nonce uyuşmazlığıyla **sessizce** düşer — ceza yok, tekrar yok. Mod olayı
  işler ve **relay etmez**: gerçeği `SetObjectStage`/`SetObjectPayload`'ın yayınladığı
  `object_state` duyurur; relay aynı gerçeği iki kez oynatırdı.
- **Balyoz:** `PropDefinition` + `netItemId` → uzak elde `itemR`/`itemL` baytıyla çizilir. Ağ
  nesnesi DEĞİLDİR (kişisel, dünyaya hiç bırakılmaz; obje pozu kanalına girmez). Eksenler
  varsayılanda kalır (klon / geri dönüş); bırakma girdisi tüketilmez.
- **`modeState`:** `p<playerId>:<doğru>/<yanlış>;…` — biçimi mod tanımlar, çekirdek yorumlamaz;
  HUD bilinmeyen anahtarı atlar.

## 5. Sunucu — `Server/.../Modes/MoleMode.cs`

Sabitler (playtest'te ayarlanır; `MoleUpSeconds` kullanıcı kararıdır):

| Sabit | Öneri | Not |
|---|---|---|
| `MoleUpSeconds` | 2 | havada kalış; istemcinin yükselme animasyonu bu pencerenin içindedir, köstebek `Up` boyunca vurulabilir |
| `PopIntervalSeconds` | 1.5 | çıkış denemesi aralığı |
| `MaxConcurrentUp` | `min(oyuncu, 6)`, en az 2 | aynı anda ayakta köstebek tavanı |
| `SquashedSeconds` | 0.6 | ezik kalma süresi, sonra `Hidden` |
| `WhackPoints` / `WrongWhackPenalty` | +10 / −10 | takım VE oyuncu skoruna aynı işaretle |

- **OnTick (10 Hz):** karıştırılmış eşit kırmızı/mavi destesinden renk çek; interval dolduysa ve
  tavan altındaysa **gizli** deliklerden rastgele birine çıkış: `stage=Up`, payload `n;c`, delik
  sayacı artar. Süre dolan köstebek vurulmadan iner (`Hidden`) — **kaçırma cezası yok**.
- **OnObjectEvent `whack`:** `kind==mole_hole` + `stage==Up` + nonce eşit → ilk gelen kazanır.
  Renk vuranın takımına eşitse doğru (takım +P, oyuncu +P, doğru++), değilse yanlış (takım −P,
  oyuncu −P, yanlış++). `stage=Squashed` + `by/ok` payload'ı; `SquashedSeconds` sonra `Hidden`.
- **Takım skoru 0'da kıstırılır** (`AddScore` kırpmaz, mod kırpar): eksi takım skoru çocuk
  kitlesinde kafa karıştırır; kimin yanlış vurduğu zaten oyuncu skorunda ve sayaçlarda görünür.
- Sayaç değişiminde `SetModeState`; `IsMatchOver` süre dolunca `red>blue`/`blue>red`/`Draw`
  (skor limiti yolu yok). Kayıt: `MatchDirector.RegisterModes()` + `GameType => "kids"`.
- ⚠️ **"Yakın iki delikten aynı anda çıkarma" sunucudan yapılamaz** — sunucu delik konumu bilmez.
  Çözüm yerleşimdedir (delik aralığı, aşağıda) + `MaxConcurrentUp`.

## 6. İstemci — `Assets/Modes/Mole/` kutusu (`VortexArena.Modes.Mole`)

| Bileşen | Sorumluluk |
|---|---|
| `MoleKinds` | tek sözlük: kind/olay/payload anahtar sabitleri — protokol tablosunun birebir aynası, serbest literal reddedilir |
| `MoleHole` | delik sunumu: `NetObject` stage/payload → köstebeği yükselt/beklet/ez/indir; `c:`'ye göre boya; `ok:`'a göre neşeli/uyarı geri bildirimi; her state'te payload yeniden okunur |
| `MoleHammer` | balyoz başı tetikleyicisi: yalnız yerel oyuncunun balyozu + baş hızı eşik üstü + hedef `Up` ise `whack{n}` gönderir; temas başına tek rapor (debounce) |
| `MoleClientController` | Live girişinde iki ele balyoz verir (bırakılırsa anında geri verir), oyuncunun takım rengine boyar, HUD'ı besler |
| `MoleHud` (`ModeHudBase`) | silah/can/ölüm/kill-feed alanları prefabda BOŞ bırakılır; takım skorları + kendi doğru/yanlış/katkı + süre; bilinmeyen `modeState` anahtarını atlar |

- **Sallama eşiği:** balyoz başının dünya hızı `MinSwingSpeed` (öneri 1.5 m/s) altındaysa temas
  vuruş sayılmaz — "dokunarak ezme" kapanır; değer playtest'te oturur.
- **Olayı kim bildirir:** aleti tutan — balyoz kişisel olduğu için seçici doğal olarak tek kişidir
  (reçetedeki N-muhabir tuzağı burada oluşmaz).
- Balyoz rengi oyuncunun takım hatırlatıcısıdır: "balyozunla aynı renkteki köstebeği ez."

## 7. Sahne + katalog

- Yeni arena sahnesi — `Yemek-Kitabi` "Yeni arena eklemek" akışıyla, 14×16 mekân kutusunda
  `<SahneAdı>`; `MapDefinition` `gameType = Kids`, modlar `[mole]`.
- **Delik yerleşimi (seviye tasarımı, elle):** duvar hattından **en az 3 m içeride** (çocuk duvara
  doğru eğilip sallamasın), delik çapı **~45 cm**, delik merkezleri arası **≥ 2 m** (iki oyuncu yan
  yana eğilebilsin). İç alanda 12–16 delik rahat oturur; son sayı sahnede belirlenir.
- Her delik `NetIdentity`'li sahne objesi (Network Parent bake) → `Configure All Build Elements` →
  `maps.json objects[]`. Zeminde fiziksel çukur YOK (free-roam zemin düz) — delik görseli
  halka/decal, köstebek onun içinden yükselir.
- Taban bölgesi (BaseZone) KONMAZ; iç engel yok (açık saha) — `Obstacle` katmanı işi çıkmaz.
- Mod kutusu `Assets/Modes/Mole/{Scripts,Data,UI,Prefabs}` + `VortexArena.Modes.Mole.asmdef`
  (refs: Core, Net, Protocol); `GameCatalog.modes[]`'e mod tanımı.
- Balyoz: `ITEM_Balyoz.asset` (`PropDefinition`; `netItemId` elle, çakışmayı Configure bekçisi
  yakalar) + prefab (prototip: silindir sap + kutu baş). İki el aynı tanımı taşır.

## 8. İçerik (sonraya — Hamburgerci ile aynı statü)

- [ ] Gerçek modeller + animasyonlar: köstebek (çıkış/bekleme/ezilme/iniş), delik halkası, balyoz.
      Önce prototip primitiflerle kurulur; model gelince **yerleşim korunur**.
- [ ] Balyoz kavrama pozu (`Kavrama Pozu Stüdyosu`, sağ + sol ana kabza) — yazılmadan eşya ele
      gelir ama kumanda anchor'ında durur.
- [ ] Sesler: köstebek çıkış "pop"u, doğru vuruş (neşeli), yanlış vuruş (uyarı), ezilme, iniş.
- [ ] HUD sanatı.

## 9. Uygulama sırası (doc-first)

1. `Docs/ArenaNet-Protokol.md`: mod sözleşmesi bölümü (kind/olay/stage/payload/`modeState`/balyoz
   baytı), kinds tablosuna `mole_hole`, modeId listesine `mole`.
2. Sunucu: `MoleMode.cs` + kayıt.
3. İstemci kutusu: bileşenler + HUD + `MoleKinds`.
4. Asset'ler + sahne: `ITEM_Balyoz`, `mole_hole` kind asset'i, mod tanımı, sahne kurulumu, bake +
   `Configure All Build Elements` (katalog + `maps.json` + Build Settings).
5. Doküman eşitlemesi: `Docs/Sistem-Ozeti.md` (repo haritasına mod kutusu, bileşen sözlüğüne
   sunucu satırı + istemci kutusu), `Server/README.md` mod tablosu, `Yemek-Kitabi` çocuk oyunu
   reçetesine yarışmalı varyant notu (kural şekli maddesi bugün kooperatife göre yazılı).
6. Doğrulama listesi kullanıcıya (derleme/build ajan tarafından tetiklenmez).

## 10. Playtest ayarları / açık kararlar

- `MinSwingSpeed` · `PopIntervalSeconds` / `MaxConcurrentUp` (kalabalıkta yoğunluk hissi) ·
  puan/ceza oranı (ceza caydırmıyorsa artırılır) · köstebek boyu (eğilme derinliği konforu —
  diz/bel seviyesi).
- Takım skoru 0 tabanı: sahada bilinçli yanlış vurma (grief) görülürse eksiye açmak caydırıcılığı
  artırır.
- Sahada oyuncu çarpışması görülürse `MaxConcurrentUp` düşürülür (sunucudan mesafe çözümü yok).

## 11. Doğrulama (kullanıcı koşar)

- İki başlıkta aynı delikten aynı anda aynı renk köstebek çıkar; 2 sn sonra ikisinde de iner.
- İki oyuncu aynı köstebeğe sallar: tek `whack` işlenir, skor bir kez yazılır; ezilme iki başlıkta
  da oynar.
- Doğru vuruş: takım + oyuncu skoru ve doğru sayacı artar. Yanlış vuruş: vuranın takım skoru düşer
  (0 altına inmez), oyuncu skoru eksilir, yanlış sayacı artar.
- Köstebek indikten sonra ulaşan sallama hiçbir şey yapmaz (nonce); yavaş temas (eşik altı) vuruş
  sayılmaz.
- Balyozlar Live'da iki elde belirir ve bırakılamaz; uzak avatarın iki elinde doğru ve takım
  renginde çizilir.
- Geç katılan: ayaktaki köstebekleri doğru renkte görür; skor/sayaçlar doğru gelir.
- Lobide takım dengesi otomatik; admin `set_team` ile değiştirir, balyoz rengi yeni takımı izler.
- Süre bitince önde olan takım ilan edilir; sonuç ekranı operatör kapatana kadar durur;
  doğru/yanlış sütunları admin HUD ile tutarlı.
- Hasar tümüyle kapalı: balyozla oyuncuya vurmak hiçbir şey yapmaz, can HUD'ı yok.
