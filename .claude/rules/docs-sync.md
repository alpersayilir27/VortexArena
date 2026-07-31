# Kural: Kod değişince doküman da değişir (aynı commit)

Temel bir kodda (protokol, ağ akışı, maç kuralı, bileşen sorumluluğu, klasör/asmdef yapısı,
editor tool'u, sunucu config'i) değişiklik yapıldığında **ilgili doküman aynı commit'te**
güncellenir. Doküman ile kod arasında sapma = tuzak; bu projede tek doğruluk kaynağı dokümandır.

| Değişiklik | Güncellenecek doküman |
|---|---|
| Protokol mesajı/alanı, sabit, port, doğrulama kuralı, maç fazı, yeni `modId`/`weaponId` | `Docs/ArenaNet-Protokol.md` — **TEK doğruluk kaynağı** |
| Yeni bileşen/servis, ağ mantığı, akış, bileşen sorumluluğu, kod reçetesi, proje durumu | `Docs/Sistem-Ozeti.md` (§2 repo haritası, §3 ağ mantığı, §4 bileşen sözlüğü, §5 reçeteler, §8 durum) |
| Klasör/asmdef mimarisi, içerik ekleme reçetesi, editor tool'u, XR/paket politikası | `CLAUDE.md` |
| Sunucu çalıştırma, CLI argümanı, config dosyası/alanı | `Server/README.md` |
| İşletme kurulumu: donanım, ağ/firewall, kalibrasyon, smoke test adımı | `Docs/Isletme-Kurulum.md` |
| Planlanmış bir işin kapsamı veya bitmesi | `plan/<faz>.md` (biten dosya **silinir**) + `plan/README.md` |
| Yeni kalıcı çalışma kuralı | `.claude/rules/<ad>.md` + `CLAUDE.md`'de tek satır işaret |

- **Sıra: doküman → kod.** Ağ davranışı değişecekse ÖNCE `ArenaNet-Protokol.md` güncellenir,
  sonra iki taraf (Unity `_Shared/Net/Protocol` + `Server/`) ona uydurulur. Kod-önce gidilirse
  iki uçlu sapma başlar.
- ⚠️ **`CLAUDE.md` TALİMAT dosyasıdır, anlatım değil.** Yukarıdaki tabloda ona ayrılan dört satırın
  dışına çıkma. Somut test: yazacağın cümle *"şunu şöyle yap / şunu yapma"* mı, yoksa
  *"sistem şöyle çalışıyor"* mu? İkincisi ise yeri `Docs/`'tur ve CLAUDE.md'ye en fazla tek satırlık
  işaret girer. Sebep: bu dosya **her oturumda bağlama yükleniyor**, yani oradaki her satır kalıcı
  bir maliyet; ayrıca `Docs/` ile çakışan anlatım ikinci bir doğruluk kaynağı üretir
  (protokol için bu doğrudan "TEK doğruluk kaynağı" kuralını çiğner).
- **Sayı ve liste tutma.** "Bugün iki mod var", "tablodaki 6 silah", `dev-targets.json`'un
  içeriğini satır satır yazmak gibi şeyler kaçınılmaz olarak bayatlar ve kimse fark etmez.
  Sayılabilir olanı sayma, **nerede olduğunu göster**. Aynı sebeple `§7.29` gibi **numara
  referansı verme** — araya madde eklenince sessizce yanlış yeri gösterir; bölümü adıyla an.
- **Olay kaydı doküman değildir.** "Dört denemede de şu hatayı verdi", "şu tarihte yaşandı",
  "şu kusur düzeltildi" gibi anlatılar tutulmaz; tutulacak olan **kuraldır**
  ("bunu şu yüzden yapma"). Gerekçe bir cümleyi aşıyorsa yeri `Docs/Sistem-Ozeti.md` §7'dir,
  CLAUDE.md değil.
  ⚠️ **Bu `plan/` dosyaları DAHİL her yerde geçerlidir** — plan bir günlük değil, **yapılacak işin
  listesidir**. Biten iş oraya "yapıldı" diye yazılmaz, listeden **silinir**; "ne zaman neyi
  düzelttik" sorusunun cevabı zaten git geçmişindedir ve orada tek kopya kalır. Tarih atmak
  ("2026-07-31") bunun en görünür biçimidir ve hiçbir doküman türünde yapılmaz.
- Pahalıya öğrenilen bir tuzak çıktıysa `Docs/Sistem-Ozeti.md` §7 "Tuzaklar" listesine bir madde
  ekle; tekrarlanabilir bir çalışma kuralıysa `.claude/rules/` altına taşı.
- Salt iç refactor (davranış aynı, dışa açık isim/yol değişmedi) doküman gerektirmez. Ama
  grep'lenebilir bir tip/dosya/sahne/menü adı değiştiyse dokümanlarda o adı ara ve düzelt —
  sahne adı = katalog anahtarı olduğu için özellikle sahne/`modId`/`weaponId` adlarında.
- **Görev sonu kontrolü:** işi bitmiş saymadan önce "bu değişiklikten sonra hangi doküman satırı
  artık yalan?" diye bir geç. Doğrulama batch'lenirken ([[batch-build-verification]]) doküman
  güncellemesi de aynı son geçişe girer.
