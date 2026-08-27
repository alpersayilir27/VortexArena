# Çocuk Oyunları — Köstebek Ezme: kalan iş

Kod, protokol ve doküman yerinde (protokol sürümü **değişmedi**). Sistemin anlatımı dokümanlarda:
mod sözleşmesinin tamamı (tür, olay, `stage`/`s`, nonce kapısı, skor kanalları, `modeState`)
`Docs/ArenaNet-Protokol.md` §10.5 · sunucu ve istemci bileşenlerinin sorumlulukları
`Docs/Sistem-Ozeti.md` §4 (`Modes/MoleMode` + `VortexArena.Modes.Mole` kutusu) · kural şekli
`Server/README.md` mod tablosu · yarışmalı çocuk oyununun reçetesi
`Docs/Gelistirici/Yemek-Kitabi.md` "Çocuk oyunu eklemek".

## Kalan içerik işi

⚠️ **Sahnedeki her şey prototiptir:** delik bir halka (yassı silindir), köstebek bir kapsül, balyoz
silindir sap + kutu baş. Gerçek modeller gelince yerine geçecek; **yerleşim korunmalı** — köstebeğin
yükselme mesafesi ve deliğin çapı ona göre ayarlı.

- [ ] **Gerçek modeller + animasyonlar:** köstebek (çıkış / bekleme / ezilme / iniş), delik halkası,
      balyoz. ⚠️ Yükseliş animasyonu sunucunun havada kalma penceresinin **içinde** bitmeli — dışına
      taşarsa sunucunun indirdiği köstebek ekranda hâlâ tırmanıyor görünür.
      ⚠️ **Köstebek modeli için tek dokunulacak yer `Mole/Model`:** prototip parçaları (gövde, kafa,
      burun, göz, pençe) silinip gerçek model oraya konur. Ayakları pivotun orijinine oturmalı
      (köstebek yükselince zemin hizasında durur), kafası bugünkü kafanın hizasında olmalı. Sonra
      takım rengini alacak görseller `teamRenderers`'a sürüklenir ve köstebeğin **trigger**
      collider'ı kontrol edilir. Kodda ölçü yoktur — vuruşun cevabı collider'lardan çıkar.
- [ ] **Balyoz kavrama pozu** (`Kavrama Pozu Stüdyosu`, sağ + sol ana kabza) — yazılmadan balyoz ele
      gelir ama kumanda anchor'ında, yanlış açıyla durur.
- [ ] **Sesler:** köstebek çıkışı, doğru vuruş (neşeli), yanlış vuruş (uyarı), ezilme, iniş.
- [ ] **HUD sanatı** (bugün Hamburgerci HUD'ının kopyası).
- [ ] Sahnenin ortam sesi boş; köstebek arenasına uygun bir `ambienceClip` seçilecek.

## Playtest ayarları

- [ ] `MinSwingSpeed` (dokunarak ezmeyi kapatan eşik) ve balyoz ucundaki vuruş küresinin yarıçapı —
      küçük küre "ıskaladım" hissi, büyük küre "değmeden ezdim" hissi verir.
- [ ] Çıkış aralığı / aynı anda ayakta köstebek tavanı — kalabalıkta yoğunluk hissi.
- [ ] Puan ve ceza oranı; ceza caydırmıyorsa artırılır.
- [ ] Köstebeğin yükseklik ayarı: eğilme derinliği çocuk için konforlu mu.
- [ ] Takım skorunun `0` tabanı: sahada bilinçli yanlış vurma görülürse eksiye açmak caydırıcılığı
      artırır (karar gerekçesi `Server/README.md` mod bloğunda).
- [ ] Oyuncu çarpışması görülürse tavan düşürülür — sunucudan mesafe çözümü yoktur.

## Doğrulama (kullanıcı koşar)

- [ ] Sunucu derlemesi temiz (`dotnet build`; Unity tarafı derleniyor).
- [ ] İki başlıkta aynı delikten aynı anda aynı renk köstebek çıkar; süre dolunca ikisinde de iner.
- [ ] İki oyuncu aynı köstebeğe sallar: **tek** vuruş işlenir, skor bir kez yazılır, ezilme iki
      başlıkta da oynar.
- [ ] Doğru vuruş: takım skoru + oyuncu katkısı + doğru sayacı artar. Yanlış vuruş: takım skoru
      düşer (**0 altına inmez**), oyuncu katkısı eksilir, yanlış sayacı artar.
- [ ] Köstebek indikten sonra ulaşan sallama hiçbir şey yapmaz (nonce) ve **ceza yazmaz**; eşik altı
      yavaş temas vuruş sayılmaz.
- [ ] Köstebeğin **herhangi bir yerine** (tepesi, yanı) hızlı vuruş sayılır; **çok hızlı** sallamada
      da kaçmaz (süpürme). Balyozla havayı sallamak ya da zemine vurmak hiçbir şey yapmaz.
- [ ] Balyozlar iki elde belirir, bırakılamaz; uzak avatarın iki elinde doğru ve **takım renginde**
      çizilir.
- [ ] Geç katılan başlık: ayaktaki köstebekleri doğru renkte görür (çıkışı baştan oynamaz),
      skor/sayaçlar doğru gelir.
- [ ] Lobide takım dengesi otomatik; admin `set_team` ile değiştirince balyoz rengi yeni takımı
      izler.
- [ ] Süre bitince önde olan takım ilan edilir, eşitlikte berabere; sonuç ekranı operatör kapatana
      kadar durur.
- [ ] Hasar tümüyle kapalı: balyozla oyuncuya vurmak hiçbir şey yapmaz, can HUD'ı yok.
- [ ] Delik yerleşimi sahada: duvar hattından en az 1 m içeride, delikler arası en az 2 m, kolonların
      dibinde delik yok.
