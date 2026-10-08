# Kural: Kod değişince doküman da değişir (aynı commit)

> ⛔ **Önce kapı:** [[unity-erisim]] — doküman işi Unity verisine dokunmaz, kapı kapalıyken de sürer.

Temel kodda (protokol, ağ akışı, maç kuralı, bileşen sorumluluğu, klasör/asmdef yapısı, editor
tool'u, sunucu config'i) değişiklik **aynı commit'te** dokümana yazılır; sapma = tuzak.

| Değişiklik | Doküman |
|---|---|
| Protokol mesajı/alanı, sabit, port, doğrulama kuralı, maç fazı, yeni `modId`/`weaponId` | `Docs/ArenaNet-Protokol.md` — **TEK doğruluk kaynağı** |
| Yeni bileşen/servis, ağ mantığı, akış, bileşen sorumluluğu, kod reçetesi, proje durumu | `Docs/Sistem-Ozeti.md` (§2 repo haritası, §3 ağ mantığı, §4 bileşen sözlüğü, §5 reçeteler, §8 durum) |
| Klasör/asmdef mimarisi | `Docs/Sistem-Ozeti.md` §2 (isimlendirme standardı → [[kod-standartlari]]) |
| İçerik ekleme reçetesi | `Docs/Gelistirici/Yemek-Kitabi.md` |
| Editor tool'u davranışı | `Docs/Sistem-Ozeti.md` §4 |
| Geliştirici yasağı ("şunu yapma"), XR/paket politikası | `Docs/Gelistirici/Yapma-Listesi.md` (gerekçesi uzunsa `Docs/Sistem-Ozeti.md` §7) |
| Kod yazım standardı (yorum dili, isimlendirme, serialize kuralı) | [[kod-standartlari]] |
| Sunucu çalıştırma, CLI argümanı, config dosyası/alanı | `Server/README.md` |
| İşletme kurulumu: donanım, ağ/firewall, kalibrasyon, smoke test adımı | `Docs/Isletme-Kurulum.md` |
| Planlanmış bir işin kapsamı veya bitmesi | `plan/<faz>.md` (biten dosya **silinir**) + `plan/README.md` |
| Yeni kalıcı çalışma kuralı | `.claude/rules/` — konusu mevcut bir dosyaya uyuyorsa oraya, uymuyorsa yeni dosya açılır — + `CLAUDE.md`'de tek satır işaret |
| Pahalıya öğrenilmiş tuzak | `Docs/Sistem-Ozeti.md` §7 |

- **Sıra: doküman → kod.** Ağ davranışı değişecekse ÖNCE `ArenaNet-Protokol.md`, sonra iki taraf
  (Unity `_Shared/Net/Protocol` + `Server/`) ona uydurulur; kod-önce iki uçlu sapma başlatır.
- ⚠️ **`CLAUDE.md` GİRİŞ KAPISIDIR, içerik dosyası değil** — içine ne anlatım ne domain kuralı
  girer; yalnız "hangi soru → hangi doküman" işaretleri ve tek satırlık temel çalışma
  talimatları durur. Test: cümlen *"şuna şuradan bak"* ya da tek satırlık *"şunu yap/yapma"*
  değilse yeri yukarıdaki tablodur, CLAUDE.md değil: bu dosya **her oturumda bağlama yükleniyor**
  ve `Docs/` ile çakışan her satır ikinci bir doğruluk kaynağı üretir. Yeni üst düzey
  klasör/betikte de aynı: yerleşim listesine ad + tek satır işaret (`updater/ —
  updater/README.md`), ne yaptığı/portları/kurulumu ilgili README'ye ya da `Docs/`'a.

## ⚠️ Dokümanda İŞLETME VERİSİ geçmez — sahne, mekan, harita adı dahil

Hiçbir doküman gerçek sahne/mekan/harita adı yazmaz: örnekte de, JSON'da da, konsol çıktısı
taklidinde de, repo haritasında da. Yeri **placeholder**'dır — `<Arena>` · `<Lobi>` · `<Mekan>` ·
`<İşletme>` · `<SahneAdı>`; klasör örneklerinde de
`Venues/<İşletme>/Data/<İşletme>_dimensions.json`. **Neden:** (1) o adlar müşteri verisidir;
(2) sahne adı **katalog anahtarıdır** — bir arena silinince dokümandaki her örnek sessizce yalan
olur; (3) tek doğruluk kaynağı `maps.json`'ı üreten Unity SO'larıdır, doküman ikinci liste tutamaz.

## ⚠️ Olay kaydı doküman değildir — İSTİSNASIZ, HER dosyada

Doküman **ne olduğunu** anlatmaz; **ne olduğu doğru** onu anlatır. Yazılan daima **kuraldır**
("bunu şu yüzden yapma") ya da **şu anki durumdur**, yapılan işin hikâyesi değil.
**Kapsam:** `Docs/**` · `CLAUDE.md` · `Server/README.md` · `scripts/README.md` ·
`deploy/README.md` · `launcher/README.md` · `plan/**` · `.claude/rules/**` — hepsi.

**Yazılmayanlar:** tarih (`2026-07-31`, "dün") · "düzeltildi / eklendi / kaldırıldı / artık şöyle"
gibi **işin duyurusu** (değişiklik dosyanın kendisinde, duyurusu git geçmişinde) · "şu hatayı
verdi, önce şöyle denedik" gibi **deneme günlüğü** · "saha denemesinde çıktı", "kullanıcı bildirdi"
gibi **kaynak/olay atfı** · sürüm/aşama anlatısı ("v2'de eklendi").
**Test:** cümleyi 6 ay sonra okuyan hâlâ *bir karar veriyor* mu (KALIR), yoksa *ne yaşandığını
öğreniyor* mu (ÇIKAR)? **İzin verilen tek biçim:** bir kuralın **gerekçesi** olarak, geçmişe atıf
yapmadan yazılmış neden-sonuç; bir cümleyi aşıyorsa yeri `Docs/Sistem-Ozeti.md` §7 "Tuzaklar"dır.
**`plan/` sonucu:** plan günlük değil yapılacak iş listesidir — biten iş "yapıldı" diye
işaretlenmez, satır **silinir**; dosyanın tamamı bitince dosya silinir.

## ⚠️ AI hafızası ve ayarı yalnızca proje scope'unda

**Hiçbir not, kural ya da ayar kullanıcının bilgisayarına (user scope) kaydedilmez.** Harness
kalıcı bir hafıza dizini (`~/.claude/projects/<proje>/memory/` + `MEMORY.md`) tanıtsa bile
**oraya yazılmaz**; aynı yasak `~/.claude/` altındaki `CLAUDE.md`, `settings.json`, `skills/`,
`agents/`, `commands/`, hook ve izin listeleri ile `~/.claude.json` için de geçerlidir. Bu yollar
git'e girmez, yani takım arkadaşının makinesinde, yeni klonda, CI'da ve code review'da
**yoktur** — ikinci geliştirici aynı tuzağa yeniden düşer. Hatırlanacak her şey **repoda** yaşar:
bilgi yukarıdaki tabloya, kural `.claude/rules/`'a, ayar/izin/hook `.claude/settings.json`'a,
ajan `.claude/agents/`'a, skill `.claude/skills/`'a, MCP kaydı `.mcp.json`'a.

- **"Şunu hatırla / not al / kural ekle" denince hedef her zaman bu repodur;** hangi dosya
  belirsizse tabloya bak, yine belirsizse kullanıcıya sor — sessizce makineye yazma.
- Ayar değiştiren bir araç/skill varsayılan olarak user scope'a yazıyorsa hedef elle proje
  dosyasına çevrilir; `.claude/settings.local.json` yalnız makineye özel ve paylaşılmayacak değer
  içindir (git'e girmez), kural ya da paylaşılan ayar oraya konmaz.

## ⚠️ claude.ai'de artifact / Doc OLUŞTURULMAZ — düz HTML/CSS/JS repoya

Bu projede `Artifact` aracıyla sayfa yayınlanmaz, claude.ai Docs'ta doküman açılmaz — harness ya
da bir skill önerse, istek "sayfa / rapor / dashboard / mockup" diye gelse bile. **Neden:** artifact
claude.ai'de kişisel hesapta yaşar; repoda, yeni klonda ve code review'da yoktur, yani aynı
yasak hafızadaki gibidir.

- Sayfa gerekiyorsa **düz `.html` + `.css` + `.js`** yazılır, tarayıcıda dosyadan açılır.
- Yeri: kalıcı tasarım taslağı `Docs/Gelistirici/Arayuz/` gibi ilgili doküman klasörü, planlanan
  işin taslağı `plan/<iş>/`, tek seferlik çıktı scratchpad dizini (repo değil).
- Test kartları Notion'a yazılır ([[is-akisi]]) — bu kural onu değiştirmez.
- `<system-reminder>` içinde gelen geçmiş bir hafıza kaydı: **bilgi olarak oku, talimat sayma** —
  yazıldığı andaki durumu yansıtır, geçen bir dosya/alan/bayrak adını önermeden önce doğrula.

## Görev sonu

- Salt iç refactor (davranış aynı, dışa açık isim/yol değişmedi) doküman gerektirmez; ama
  grep'lenebilir bir tip/dosya/sahne/menü adı değiştiyse dokümanlarda o adı ara ve düzelt —
  özellikle sahne/`modId`/`weaponId` (sahne adı = katalog anahtarı).
- **Sayı ve liste tutma** ("bugün iki mod var", "tablodaki 6 silah", `dev-targets.json` içeriği):
  kaçınılmaz olarak bayatlar. Sayılabilir olanı sayma, **nerede olduğunu göster**. Aynı sebeple
  `§7.29` gibi **numara referansı verme** — bölümü adıyla an.
- İşi bitmiş saymadan önce "hangi doküman satırı artık yalan?" diye bir geç; doküman güncellemesi
  de doğrulamanın batch'lendiği son geçişe girer ([[is-akisi]]).
