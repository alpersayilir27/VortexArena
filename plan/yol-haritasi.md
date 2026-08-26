# Yol haritası — bomba · ağ nesnesi modeli · kırılabilir objeler · oyun tipleri · Hamburgerci

**Öncelik sırası (karar):** bomba → kırılabilir objeler → yeni modlar/kurallar (oyun tipi zemini →
Çocuk Oyunları/Hamburgerci). **Ağ nesnesi modeli** kırılabilir objelerin ve Hamburgerci'nin ortak
zeminidir; bomba ona bağımlı değildir. Sunucu kapanışı bağımsız küçük iştir.

Her başlığın kendi dosyası var; bu dosya yalnız **sırayı, bağımlılığı, protokol sürüm gruplarını ve
karar durumunu** tutar. Tüm kararlar verilmiş ve dosyalara işlenmiştir; yeni bir karar noktası
çıkarsa §3'e eklenir.

## 1. Sıra ve bağımlılık

| # | İş | Dosya | Bağımlı | Protokol |
|---|---|---|---|---|
| 1 | Bomba ve atılabilir zemini (bilek kılıfı, fizik+sekme, fitil/dolum, molotof/flashbang/sis için ortak yapı) | `bomba.md` | — | sürüm **sabit** (`hit_report` 2. kapı ölüm sonrası penceresi + 5. kapı kendine vuruş; §10.3 dokümanı). Yeni atılabilir türü ileride de sürüm artırmaz: olay `itemId` taşıyor |
| 1b | El kavrama sistemini eşya geneline açmak — **F1 · F2 · F3 yazıldı**, kalan: içerik kurulumu + doğrulama | `el-kavrama-genellestirme.md` | — | kavrama telde yok; ⚠️ `WorldSingle` eşyada `itemL/itemR` baytı **`0` kalır** (§6.6) — obje uzak tarafta kendi örneğinden çizilir |
| 2 | Ağ nesnesi modeli **B1** — **yazıldı** (kimlik, tür kataloğu, export, `WorldObjectTable`, `object_state`/`world_state`, `hit_report.targetNetId`, `TryResetObjectsForMode`); kalan: doğrulama | `ag-nesne-modeli.md` | — | **sürüm artışı #1** (v17) |
| 3 | Kırılabilir objeler (B1'in ilk tüketicisi) — **kod yazıldı** (`BreakableObject` sunumu, alan hasarının ağ nesnesine geçmesi, hasar collider'ı bekçisi); kalan: arenalara siper yerleşimi + doğrulama. Shader, materyaller, iki tür asset'i, iki prefab, kırılma efekti ve bir lobi yerleşimi kuruldu | `agsal-kirilabilir-objeler.md` | 2 | #1 ile aynı |
| 4 | Sunucu kapanışı (`StopAsync` yaşam döngüsü + `ProcessExit`) | `sunucu-kapanis.md` | — | yok — 2 ile aynı sunucu turunda çıkar |
| 5 | Oyun tipi / tur tipi taksonomisi + kural zemini — **yazıldı** (`maps.json.gameType`, `IGameMode.GameType`, `WeaponSource.None`, `ScoreKind.PlayerAndShared`, admin panelinin "Oyun tipi" satırı); kalan: doğrulama | `oyun-tipi-ve-tur-tipi.md` | — | tel değişmez (config + §10.5 string değerleri; eski istemci bilinmeyeni varsayılana düşürür) |
| 6 | Ağ nesnesi modeli **B2 + B3** — **yazıldı** (sahiplik, `0x09` obje pozu + `0x05` obje bölümü, elden çıkış/durma ayrımı, `object_event`, spawn/despawn, `IGameMode.OnObjectEvent`); kavrama **F3** aynı commit'te; içerik kurulumu Hamburgerci ile yapıldı. Kalan: doğrulama | `ag-nesne-modeli.md` · `el-kavrama-genellestirme.md` | 2 | **sürüm artışı #2** (v18) |
| 7 | Çocuk Oyunları — Hamburgerci — **yazıldı** (mod, HUD, arena + prototip dükkân, admin satırı, katalog/export, oyun mantığının tamamı ve prototip içerik kurulumu); kalan: gerçek modeller + doğrulama | `cocuk-oyunlari-hamburgerci.md` | 5, 6 | #2 ile aynı (`object_state.s` v18'in içinde kaldı) |

- Paralel koşabilenler: **1 ‖ 2**, **5 ‖ 2–3**; 4 sunucu koduna dokunan ilk turla birleşir.
- Her sürüm artışı = tüm başlıklara yeni APK + admin + sunucu **aynı turda**
  (`scripts\deploy-player-apk.bat` + `deploy-admin-game.bat` + `deploy-server.bat`).
  İki artış bilinçlidir: B2/B3 DTO'larını B1'de "ileride lazım" diye tele koymak spekülatif alan
  borcudur; ama kırılabilir objeler Hamburgerci'yi beklemek zorunda değildir.
- Her iş için sıra **doküman → kod**: protokole dokunan her satır önce `Docs/ArenaNet-Protokol.md`,
  sonra iki uç (`Assets/_Shared/Net/Protocol` + `Server/`), en son istemci sunumu.
- Hamburgerci'nin kitte olmayan modelleri (müşteri, bütün ekmek, bıçak, spatula, ızgara, tahta,
  dağıtıcılar, banko) ayrı içerik işidir; 6 ve 7'nin kod tarafı onları beklemez.

## 2. Değişmeyecekler (karar verildi, yeniden açılmaz)

- Sunucu incelemesinin şu maddeleri **uygulanmaz**; bugünkü davranışlar bilinçli kararlardır: maç
  başı sıfırlama listesi, mod duraklamasında maç bitişi, tek lider kuralı, anında `match_state`, WS
  gönderim kuyruğu, konsol QuickEdit. (Ölüm sonrası hasar penceresi bu listede DEĞİL: bomba kararıyla
  `bomba.md` §2b'ye girdi.)
- Lag compensation / rewind, interest management: oyun hiç online olmayacak, gerekmez.
- Sunucu test projesi: ileride.

## 3. Karar bekleyenler

Yok. (Verilen kararlar ilgili dosyaların gövdesindedir; bu bölüm yeni bir soru çıkınca doldurulur.)
