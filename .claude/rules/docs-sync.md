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
- ## ⚠️ Olay kaydı doküman değildir — İSTİSNASIZ, HER dosyada

  Bu projede hiçbir doküman **ne olduğunu** anlatmaz; **ne olduğu doğru** onu anlatır. Yazılacak
  olan daima **kuraldır** ("bunu şu yüzden yapma") ya da **şu anki durumdur** ("sistem böyle
  çalışıyor"), yapılan işin hikâyesi değil.

  **Kapsam:** `Docs/**` · `CLAUDE.md` · `Server/README.md` · `scripts/README.md` ·
  `deploy/README.md` · `launcher/README.md` · `plan/**` · `.claude/rules/**` — **hepsi.**
  "Bu sadece bir plan dosyası / sadece bir README" diye bir istisna YOKTUR.

  **Yazılmayanlar:**
  - Tarih (`2026-07-31`, "dün", "bu hafta") — hiçbir doküman türünde, hiçbir gerekçeyle.
  - "Düzeltildi", "eklendi", "kaldırıldı", "yeniden yazıldı", "artık şöyle" gibi **yapılan işin
    duyurusu**. Değişiklik zaten dosyanın kendisinde; duyurusu git geçmişindedir.
  - "Şu hatayı verdi", "dört denemede de patladı", "önce şöyle denedik sonra böyle yaptık" gibi
    **deneme günlüğü**.
  - "Saha denemesinde çıktı", "kullanıcı bildirdi" gibi **kaynak/olay atfı**.
  - Sürüm/aşama anlatısı ("v2'de eklendi", "ilk fazda yoktu").

  **Somut test — yazacağın cümleyi 6 ay sonra okuyan biri için:** hâlâ *bir karar veriyor* mu
  (kural/durum → KALIR), yoksa *ne yaşandığını öğreniyor* mu (olay → ÇIKAR)? Cümleyi geçmiş
  zamandan bugüne çeviremiyorsan yeri doküman değildir.

  **İzin verilen tek biçim:** bir kuralın **gerekçesi** olarak, geçmişe atıf yapmadan yazılmış
  neden-sonuç ("koşulsuz yayınlasa her çağrı bir tam broadcast olurdu"). Gerekçe bir cümleyi
  aşıyorsa yeri `Docs/Sistem-Ozeti.md` §7 "Tuzaklar"dır — CLAUDE.md değil.

  **`plan/` için özel sonuç:** plan bir günlük değil, **yapılacak işin listesidir**. Biten iş
  "yapıldı" diye işaretlenmez, satır **silinir**; dosyanın tamamı bitince dosya silinir.
- Pahalıya öğrenilen bir tuzak çıktıysa `Docs/Sistem-Ozeti.md` §7 "Tuzaklar" listesine bir madde
  ekle; tekrarlanabilir bir çalışma kuralıysa `.claude/rules/` altına taşı.
- Salt iç refactor (davranış aynı, dışa açık isim/yol değişmedi) doküman gerektirmez. Ama
  grep'lenebilir bir tip/dosya/sahne/menü adı değiştiyse dokümanlarda o adı ara ve düzelt —
  sahne adı = katalog anahtarı olduğu için özellikle sahne/`modId`/`weaponId` adlarında.
- **Görev sonu kontrolü:** işi bitmiş saymadan önce "bu değişiklikten sonra hangi doküman satırı
  artık yalan?" diye bir geç. Doğrulama batch'lenirken ([[batch-build-verification]]) doküman
  güncellemesi de aynı son geçişe girer.
