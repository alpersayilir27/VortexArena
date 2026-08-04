# Hayalet avatar (ölü / kalibresiz) — kalan iş

Kod, shader, materyal, hayalet modeli, editör aracı ve dokümanların tamamı yazıldı. Kalıcı bilgi
`Docs/Sistem-Ozeti.md` (§4 `RemoteAvatar`, §7), `Docs/ArenaNet-Protokol.md` (§10.4 respawn akışı,
§10.6 kalibrasyon) ve `CLAUDE.md` (editör aracı tablosu) altındadır.

---

## 1. Prefab kurulumu (kullanıcı koşar — bir kez)

- [ ] Unity'ye odaklan, `Armature.fbx` import olsun (Rig = Humanoid ile gelir, `.meta` yazılı)
- [ ] `Tools > VortexArena > Avatars > Hayalet Gövdesini Kur` — konsolda tek satır başarı logu
      beklenir; hata basarsa mesaj hangi bağın eksik olduğunu söyler
- [ ] `RemoteAvatar.prefab` açılıp gözle: kökte `Ghost` kabı (karakterin KARDEŞİ), altında
      `GhostBody` robot örneği, kapta `GhostPoseDriver` (dört alanı dolu),
      `RemoteAvatar.ghostRoot` = `Ghost`

## 2. Doğrulama (kullanıcı koşar)

- [ ] Unity konsolu temiz — dokunulan dosyalar: `RemoteAvatar` · `GhostPoseDriver` ·
      `GhostBodyBuilder` (yeni) · `WeaponGranter` · `WeaponFrame`
- [ ] `AvatarGhost.shader` derleniyor (materyal önizlemesi pembe değil)
- [ ] Ölen uzak oyuncu robot hayalete dönüyor; içi görünüyor, rengi oyuncunun KENDİ takımı
      (kırmızı takım kırmızı, mavi takım mavi) — hem oyuncu başlığında hem admin ekranında AYNI
- [ ] Takımsız modda (FFA) hayalet **nötr** (kirli beyaz)
- [ ] Admin koşan maçta takım değiştirince ÖLÜ oyuncunun hayaleti anında yeni takım rengine geçiyor
- [ ] Kalibrasyonu admin'den sıfırla → hayalet turuncuya nabız atıyor **ve ölümü eziyor**
- [ ] Canlı + kalibreli oyuncunun gövdesi bugünküyle **birebir aynı** (doku, renk, gölge)
- [ ] Hayalet duvarların arkasından **görünmüyor**
- [ ] Hayalet gövdeyle birlikte yürüyor/eğiliyor; boy oranı makul — değilse
      `GhostPoseDriver.matchScale` ya da `GhostBody` ölçeği
- [ ] Avatar görünmezken (ilk poz gelmeden) hayalet havada asılı kalmıyor
- [ ] Kalibrasyon bozulduğu anda silah elden gidiyor; çerçeveye nişan alınca ışın **çıkmıyor**;
      grip'e basınca silah gelmiyor. Yeniden kalibre olunca geri geliyor
- [ ] Quest'te kalabalık ölümde kare süresi — saydam overdraw bütçesi (MSAA 4)

## 3. Karar bekleyen

- [ ] Kalibrasyon geri geldiğinde silah **tam şarjörle mi** dönsün? Bugün olduğu gibi döner
      (canlanmadaki `RefillFull` yalnız ölüme bağlı). Kalibrasyonu oyuncu kendi bozamadığı için
      istismar riski yok; tazeleme istenirse `CalibrationState.Changed` → `RefillSummoned` tek satır
