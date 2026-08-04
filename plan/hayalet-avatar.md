# Hayalet avatar (ölü / kalibresiz) — kalan iş

Kod, shader, materyal, prefab bağı ve dokümanların tamamı yazıldı. Kalıcı bilgi
`Docs/Sistem-Ozeti.md` (§4 `RemoteAvatar`, §7) ve `Docs/ArenaNet-Protokol.md` (§10.4 respawn
akışı, §10.6 kalibrasyon) altındadır.

Bugün **karakterin kendi mesh'i** hayalet materyaline çevriliyor (`RemoteAvatar.ghostRoot` boş).
Ayrı hayalet gövdesi bağlandığında kod DEĞİŞMEZ — `ghostRoot` doldurulur, `bodyRenderers` kapanır
ve `GhostPoseDriver` hayaleti canlı iskeletten sürer.

---

## 1. Doğrulama (kullanıcı koşar)

- [ ] Unity konsolu temiz — dokunulan dosyalar: `RemoteAvatar` · `GhostPoseDriver` (yeni) ·
      `WeaponGranter` · `WeaponFrame`
- [ ] `AvatarGhost.shader` derleniyor (materyal önizlemesi pembe değil)
- [ ] Ölen uzak oyuncu yarı saydam ve içi görünür; dost mavi, düşman kırmızı
- [ ] Takımsız modda (FFA) ve admin ekranında hayalet **kırmızı**
- [ ] Kalibrasyonu admin'den sıfırla → hayalet turuncuya nabız atıyor **ve ölümü eziyor**
- [ ] Canlı + kalibreli oyuncunun gövdesi bugünküyle **birebir aynı** (doku, renk, gölge)
- [ ] Hayalet duvarların arkasından **görünmüyor**
- [ ] Kalibrasyon bozulduğu anda silah elden gidiyor; çerçeveye nişan alınca ışın **çıkmıyor**;
      grip'e basınca silah gelmiyor
- [ ] Yeniden kalibre olunca grip'e basınca silah geri geliyor
- [ ] Quest'te kalabalık ölümde kare süresi — saydam overdraw bütçesi (MSAA 4)

## 2. Ayrı hayalet gövdesi (Starter Assets robotu)

- [ ] Starter Assets ThirdPerson URP'den **yalnız robot FBX + dokuları**
      `Assets/ThirdPartyPackages/StarterAssetsRobot/` altına alınır.
      ⚠️ Paketin scriptleri, InputSystem action asset'i, Cinemachine bağımlılığı ve prefabları
      GİRMEZ — proje Input System-only ve rig'de yapay hareket kapalı
- [ ] FBX importer: **Rig = Humanoid** (Avatar üretilmeli; `HumanPoseHandler`'ın ön koşulu)
- [ ] `RemoteAvatar.prefab`: `Ghost` alt ağacı (robot örneği + `SkinnedMeshRenderer`), üstüne
      `GhostPoseDriver` — kaynak Avatar/kök = Ch15 karakteri, hedef Avatar/kök = robot
- [ ] `RemoteAvatar.ghostRoot` alanı bu alt ağaca bağlanır
- [ ] Robotun materyali hayalet materyaline çevrilir (`M_AvatarGhost`)
- [ ] Hayalet, gövdeyle birlikte yürüyor/eğiliyor; boy oranı makul (değilse `matchScale` ya da
      hayalet kökünün ölçeği)

## 3. Karar bekleyen

- [ ] Kalibrasyon geri geldiğinde silah **tam şarjörle mi** dönsün? Bugün olduğu gibi döner
      (canlanmadaki `RefillFull` yalnız ölüme bağlı). Kalibrasyonu oyuncu kendi bozamadığı için
      istismar riski yok; tazeleme istenirse `CalibrationState.Changed` → `RefillSummoned` tek satır
