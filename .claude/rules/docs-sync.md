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
| Faz kapsamı veya durumu | `plan/<faz>.md` + `plan/README.md` |
| Yeni kalıcı çalışma kuralı | `.claude/rules/<ad>.md` + `CLAUDE.md`'de tek satır işaret |

- **Sıra: doküman → kod.** Ağ davranışı değişecekse ÖNCE `ArenaNet-Protokol.md` güncellenir,
  sonra iki taraf (Unity `_Shared/Net/Protocol` + `Server/`) ona uydurulur. Kod-önce gidilirse
  iki uçlu sapma başlar.
- Pahalıya öğrenilen bir tuzak çıktıysa `Docs/Sistem-Ozeti.md` §7 "Tuzaklar" listesine bir madde
  ekle; tekrarlanabilir bir çalışma kuralıysa `.claude/rules/` altına taşı.
- Salt iç refactor (davranış aynı, dışa açık isim/yol değişmedi) doküman gerektirmez. Ama
  grep'lenebilir bir tip/dosya/sahne/menü adı değiştiyse dokümanlarda o adı ara ve düzelt —
  sahne adı = katalog anahtarı olduğu için özellikle sahne/`modId`/`weaponId` adlarında.
- **Görev sonu kontrolü:** işi bitmiş saymadan önce "bu değişiklikten sonra hangi doküman satırı
  artık yalan?" diye bir geç. Doğrulama batch'lenirken ([[batch-build-verification]]) doküman
  güncellemesi de aynı son geçişe girer.
