# Faz 2 — UDP Poz Senkronu + Uzak Avatarlar + Admin Taktik Görünüm + Kalibrasyon Entegrasyonu

**Amaç:** Faz sonunda lobide (ve her sahnede) oyuncular **birbirini görür** (kafa + iki el hayalet avatarı, yumuşak interpolasyonlu); admin ekranında canlı **üstten taktik görünüm** vardır; tüm pozlar **arena uzayında** akar.

**Ön koşul:** Faz 1 tamam (UDP kayıt `0x00` çalışıyor). Protokol §6 formatları birebir uygulanır.

---

## Adım 1 — Arena-uzayı dönüşümü (`VortexArena.Core`)

- **`ArenaSpace.cs`** (`_Shared/Core/Arena/`, namespace `VortexArena.Core.Arena`) — statik yardımcı + sahnedeki "arena origin" kaydı:
  - Arena sahnelerinde origin = `ArenaBoundary`'nin transformu (arena merkezi, duvar hizalı). Lobby'de origin = sahne kökü (0,0,0) — lobi de fiziksel alanda oynandığı için aynı kalibrasyon geçerli.
  - API: `Vector3 WorldToArena(Vector3)`, `Quaternion WorldToArena(Quaternion)`, ters yönleri, `bool HasOrigin`.
  - `ArenaCalibrator` rig'i hizaladıktan sonra origin sabittir; dönüşüm origin transformuna göre `InverseTransformPoint/Rotation`.
- `ArenaCalibrator`'a küçük ek: kalibrasyon tamam/yüklendi olayı (`public static event Action Calibrated`) — Net katmanı poz göndermeye kalibrasyondan sonra başlar (kalibre olmamış cihaz pozu YANLIŞ çerçevede olur; kalibrasyon yoksa origin=rig varsayılanıyla yine gönder, ama `flags`'te işaretlemek v2 — v1'de sadece bekle).

## Adım 2 — İstemci poz gönderimi (`VortexArena.Net.UdpStateChannel` genişler)

- 20 Hz döngü (yalnız `role=player` + bağlı + UDP kayıtlı): `CenterEyeAnchor`, `LeftHandAnchor`, `RightHandAnchor` dünya pozları → `ArenaSpace.WorldToArena` → `PoseUpdate(0x01)` yaz → gönder.
  - Rig anchor referansları Net'e **dışarıdan verilir** (`UdpStateChannel.SetTrackedTransforms(head, l, r)` — App, Lobby/Arena sahnesi yüklenince BB rig'inden bulup çağırır; sahne başına bir kez). Net, OVR tiplerine dokunmaz (katman kuralı).
- `seq` u16 monoton; `clientTimeMs` = `Environment.TickCount` benzeri monoton saat.

## Adım 3 — Server: poz alımı + snapshot yayını (`StateHost` genişler)

- `0x01` al → kayıtlı endpoint doğrula → `PlayerRegistry`'deki `PlayerState.lastPose/lastSeq/lastPoseTime` güncelle (eski seq at; u16 sarmalamasına dikkat: `(short)(gelen - eldeki) > 0` hilesi).
- 20 Hz timer: tüm online **player**'ların son pozlarından `Snapshot(0x02)` kur (serverTick++) → kayıtlı TÜM endpoint'lere (admin dahil) gönder.
- Konsola saniyede bir özet: oyuncu sayısı, poz alınan oyuncu sayısı, snapshot boyutu.

## Adım 4 — Uzak avatarlar (Unity)

- **`RemotePlayerRegistry`** (`VortexArena.Net`): snapshot'ları çözer, oyuncu başına zaman damgalı poz halkası (ring buffer) tutar, `INTERP_DELAY_MS` geriden örnekleyerek interpolasyonlu poz verir. Kendi `playerId`'sini ATLAR. Olaylar: `OnRemoteJoined(playerId)`, `OnRemoteLeft(playerId)`, `GetInterpolatedPose(playerId, out head, out handL, out handR)`.
- **`RemoteAvatar`** (`VortexArena.Core.Player/` — prefab sürücüsü): kafa (basit kask/vizör mesh) + 2 el (basit eldiven) + üstte ad etiketi (TMP) + takım rengi materyali. Her `LateUpdate`'te registry'den interpolasyonlu pozu okur, `ArenaSpace.ArenaToWorld` ile dünyaya çevirir.
- **`RemotePlayerSpawner`** (`VortexArena.App`): `OnRemoteJoined/Left` + `lobby_state` (ad/takım) dinler; `_Shared/App/Prefabs/RemoteAvatar.prefab` örneği yaratır/yok eder. Lobby + Arena sahnelerinde bulunur (sahneye konur, DontDestroyOnLoad DEĞİL — sahne kendi spawner'ını taşır).
- Prefab: `_Shared/App/Prefabs/RemoteAvatar.prefab` (basit primitiflerle; görsellik sonra).

## Adım 5 — Admin taktik görünüm (`VortexArena.App`)

- `AdminConsole` dashboard'ına **TacticalView** paneli: UGUI `RawImage` üzerine arena krokisi — arena sınırları (şimdilik 10×10 sabit; Faz 3'te MapDefinition'dan), oyuncu noktaları (takım renkli, bakış yönü çizgisi, üstünde ad).
  - Kaynak: `RemotePlayerRegistry` (admin da snapshot alıyor — Faz 3 adım 3'te admin'in de UDP kaydı yapıldığından emin ol; admin poz GÖNDERMEZ, yalnız alır).
  - Çizim: basit `Canvas` + oyuncu başına `RectTransform` nokta (Shader/GL gerekmiyor).
- Roster satırlarına canlı fps/batarya zaten Faz 1'den geliyor; taktik görünümle aynı ekranda.

## Adım 6 — Doğrulama (tek toplu geçiş)

1. Server + 2 istemci loopback (Editor player + ikinci bir Windows player build ya da Editor iki kopya — `ParrelSync` YOK, ikinci kopya için Windows player build kullan): iki istemci birbirinin hayalet avatarını görür; hareket yumuşak (interp), ışınlanma yok.
2. Admin bağlı: taktik görünümde iki nokta canlı geziniyor; bir istemci koparsa avatar + nokta kaybolur (OFFLINE_TIMEOUT).
3. **Quest cihaz testi (kritik):** 2 Quest aynı fiziksel alanda, ikisi de kalibre → sanal avatar konumu **gerçek kişinin konumuyla örtüşüyor** (kol boyu hata kabul; sapma büyükse kalibrasyon/origin dönüşümünü şüphelen — pozlar arena uzayında mı?).
4. Bant genişliği/performans: server konsolunda snapshot ~⩽1.4 KB; Quest'te fps düşüşü yok (72 Hz hedef).
5. Commit: `Faz 2: UDP poz senkronu + uzak avatarlar + admin taktik görünüm`

## Çıktı kontrol listesi

- [x] `ArenaSpace` dönüşümü + `ArenaCalibrator.Calibrated` olayı
- [x] PoseUpdate 20 Hz gidiyor (yalnız player, kalibrasyon sonrası); Snapshot 20 Hz dönüyor (admin dahil herkese)
- [x] `RemotePlayerRegistry` interpolasyonu (100 ms tampon) + `RemoteAvatar` prefabı + spawner
- [x] Admin taktik görünüm canlı (2 PoseBot ile loopback doğrulandı; ayrıca `Server/VortexArena.PoseBot` test istemcisi eklendi)
- [ ] 2-Quest fiziksel örtüşme testi geçti (kullanıcıda — `Builds/vortexarena-faz2.apk`)
- [x] Commit atılmış
