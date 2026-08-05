# Oyuncu boy ölçeği — kalan iş

Kod (protokol v9, sunucu, istemci ölçümü, uzak avatar, admin arayüzü) ve dokümanların tamamı
yazıldı. Kalıcı bilgi `Docs/ArenaNet-Protokol.md` (§5.1/§5.2/§5.3, **§10.8**),
`Docs/Sistem-Ozeti.md` (§3.3, §4 `BodyScaleState`, §7), `CLAUDE.md`,
`Docs/Isletme-Kurulum.md` ve `Docs/Kullanim-Kilavuzu.md` (§4.2) altındadır.

---

## 1. Prefab kurulumu

- [ ] **`Assets/_Shared/Avatars/Resources/LocalBodyAvatar.prefab` → `EyeAnchor`**
      Karakterin `mixamorig:Head` kemiğinin **altına** boş bir GameObject (`EyeAnchor`) konur ve
      **modelin iki gözünün arasına** sürüklenir. `LocalBodyAvatar.eyeAnchor` alanına bağlanır.
      ⚠️ **Ölçümün tek referansı budur:** kafa kemiğinin ham konumu kullanılamaz (göz hizası değil)
      ve işaretçi taşınırsa tüm oyuncuların boyu sessizce sapar. Alan boşsa ölçüm hiç yapılmaz ve
      konsola hata düşer.
- [ ] `VA_CalibrationManager.prefab`'ta `floorProbeDropMeters = 0.08` (eski `tipLocalOffset.y`
      **-0.19** idi) → sahada ilk kalibrasyondan sonra `floor R m` log satırıyla doğrula ve
      gerekirse `y_yeni = y_eski − R` ile ayarla (`Docs/Isletme-Kurulum.md` §3).

## 2. Doğrulama (kullanıcı koşar)

- [ ] `dotnet build` (Server) + Unity derlemesi temiz
- [ ] Kumanda **eğik** tutulup kalibre edilince zemin, dik tutuşla aynı çıkıyor (log satırındaki
      `floor` değeri)
- [ ] Admin satırındaki **ÖLÇ**: hedef oyuncunun avatarı diğer başlıklarda ve admin ekranında
      anında yeniden ölçekleniyor; etikette çarpan görünüyor
- [ ] **TÜM OYUNCULARI ÖLÇEKLE**: kalibre olmayan atlanıyor ve sayısı admin duyurusunda
- [ ] Ölçüm anında oyuncu eğilirse ölçüm reddediliyor (konsolda tek satır sebep), eski değer duruyor
- [ ] Kısa ve uzun iki oyuncu yan yana: avatarlar gözle doğru boyda, **duruşlar bozulmuyor**
      (blob tutarlılığı korunmuş demektir)
- [ ] Ölçeklenmiş avatarın kafasına nişan alınca vuruş tutuyor (kutular ölçekle birlikte gitti)
- [ ] Kırmızı takım gövdesi ve hayalet gövde de aynı ölçekte çiziliyor
- [ ] Elde çizilen silah **ölçeklenmiyor** (gerçek boyunda)
- [ ] **Aynı oyuncu üst üste iki kez ölçülünce aynı çarpan çıkıyor** (yerel karakterin ölçeği 1
      kaldığı için ölçüm tekrarlanabilir olmalı)
- [ ] Oyuncu yeniden bağlanınca ölçek kendiliğinden geri geliyor (yeniden ölçüm gerekmiyor)
- [ ] Admin kalibrasyonu sıfırlayınca ölçek de sıfırlanıyor **ve oyuncu yeniden bağlandığında geri
      GELMİYOR** (yerel `PlayerPrefs` de temizlenmiş olmalı)
- [ ] Karakterin gözü oyuncunun gözüyle aynı hizaya geliyor; kafa tepesi hizalamaya karışmıyor

⚠️ **Protokol v9** — tüm başlıklara yeni APK gerekiyor.
