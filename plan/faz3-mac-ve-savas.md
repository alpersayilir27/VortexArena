# Faz 3 — Maç Akışı + TDM Modu + Silah/Can Senkronu + Respawn

**Amaç:** Faz sonunda uçtan uca oynanabilir bir **Team Deathmatch**: admin mod+harita seçer → herkes arena sahnesine geçer → geri sayım → maç → vuruşlar sunucu onaylı → skor/kill-feed → süre/limit dolunca maç sonu → lobiye dönüş.

**Ön koşul:** Faz 2 tamam. Kural otoritesi **SUNUCUDADIR** (kullanıcı kararı 1).

---

## Adım 1 — İçerik veri modeli (Unity, `VortexArena.Core`)

- **`WeaponDefinition`** (SO, `_Shared/Core/Combat/`): `weaponId` (string, protokol anahtarı), ad, hasar, rpm, menzil, spread, şarjör, prefab referansı. Mevcut `Weapon.cs` değerlerini SO'dan okuyacak şekilde bağlanır (Inspector'daki mevcut değerler iki prefab için SO'lara taşınır: `ak47`, `m4`). SO'lar: `_Shared/Arsenal/Data/{AK47,M4}.asset`.
- **`MapDefinition`** (SO, `_Shared/Core/Arena/`): `sceneName` (build listesiyle birebir), görünen ad, boyut (Vector2, ör. 10×10), takım başına spawn slot sayısı, desteklenen modId listesi. `Arenas/Standard/A10x10/Data/A10x10.asset`.
- **`ModeDefinition`** (SO, `_Shared/Core/`): `modeId` (`"tdm"`), görünen ad, roundSeconds, scoreLimit, uyumlu haritalar (MapDefinition listesi), loadout (WeaponDefinition listesi). `Modes/TeamDeathmatch/Data/TDM.asset` (Faz 3'te Modes klasörü açılır).
- **`GameCatalog`** (SO, `_Shared/Data/`): tüm ModeDefinition + MapDefinition'ları listeler. AdminConsole seçim UI'ı bunu okur. (Server'a katalog GEREKMEZ — admin `start_match{modeId, sceneName}` gönderir; server modId'yi kendi `IGameMode` kayıtlarıyla eşler, bilinmeyense reddedip loglar.)
- **`SpawnPoint`** (MonoBehaviour, `_Shared/Core/Arena/`): `team` + `slot` alanları. `Arena10x10` sahnesine takım başına ≥4 adet yerleştirilir (kırmızı taban / mavi taban şeritlerine — mevcut `BaseZone`'larla hizalı).

## Adım 2 — Server: MatchDirector + TDM kuralı

`Server/VortexArena.Server.Core/`:

- **`IGameMode`**: `string ModeId`, `void OnMatchStart(MatchContext)`, `void OnHitApplied(hit)`, `void OnKill(killer, victim)`, `void OnTick(dt)`, `bool IsMatchOver(out winnerTeam)`. `MatchContext`: oyuncular, skorlar, süre, yayın API'si.
- **`TdmMode : IGameMode`** (`Modes/TdmMode.cs`): kill → takım skoru +1; `scoreLimit` veya süre → maç biter (süre bitiminde yüksek skor kazanır, eşitlikte berabere).
- **`MatchDirector`**: faz makinesi `Lobby → Loading → Countdown(5) → Live → End(10 sn) → Lobby`.
  - `start_match` (admin): doğrula (en az 1 player online + her iki takımda oyuncu; sceneName hello'daki scenes listelerinde var mı) → takımı atanmamışları otomatik dengele → herkese `load_match` (kişiye `yourTeam`+`spawnSlot`) → Loading.
  - Loading: tüm player'lar `set_ready` (sahne yüklendi anlamında) gönderince → Countdown → `countdown` yayını → Live.
  - Live: `match_state` 1 Hz; süre sayar; `IGameMode.OnTick`.
  - **Vuruş hattı:** `hit_report` → doğrulamalar (atıcı+hedef hayatta, farklı takım, silah rate-limit: son atıştan bu yana ≥ `60/rpm×0.8` sn, damage=WeaponDefinition'la uyumlu — server tarafında silah tablosu: v1'de `config/weapons.json` — **Unity SO'larından elle senkron, weaponId+damage+rpm**; Faz 4'te export otomasyonu) → hp düş → `health_update`; hp≤0 → `kill_event` + skor + `respawn{spawnSlot, delay:5}` planla; respawn zamanı gelince hp=100 + flags.alive=1.
  - `abort_match`/`return_to_lobby` → herkese `return_to_lobby`, faz Lobby.

## Adım 3 — Unity: mod modülü + savaş bağlama

- **`Assets/Modes/TeamDeathmatch/`**: `Scripts/VortexArena.Modes.Tdm.asmdef` (refs: `VortexArena.Core`, `VortexArena.Net`, `VortexArena.Protocol`, `Unity.TextMeshPro`, `UnityEngine.UI`), `Data/TDM.asset`, `UI/` (skor paneli prefabı).
  - `TdmClientController`: `match_state/kill_event/match_end` dinler → VR'da bilek/duvar skor paneli, kill-feed, maç sonu podyum metni. Arena sahnesine `load_match` sonrası App tarafından eklenir (modeId→prefab eşlemesi `GameCatalog`'dan; mod prefabı `Modes/TeamDeathmatch/UI/TdmUI.prefab`).
- **`Weapon.cs` ağa bağlanır** (`VortexArena.Core.Combat`):
  - Yerel ateş: mevcut raycast + VFX/ses AYNEN; ek olarak `shot_fired` gönder; raycast bir `RemoteAvatar`'a (veya Faz 3'te avatarlara eklenen `HitBox` collider'larına — kafa/gövde basit kapsül) değerse `hit_report` gönder. **Hasarı yerel UYGULAMAZ** (Health.TakeDamage çağrısı KALDIRILIR; server `health_update` bekler).
  - Uzak ateş: `NetEvents.OnShotFired` → o oyuncunun avatar el pozisyonunda namlu alevi + `WeaponAudio` sesi (FX_HitSpark + ses mevcut prefablardan).
- **`PlayerCombatState`** (`VortexArena.Core.Combat`, yerel oyuncu): `health_update(kendi id)` → HUD can göstergesi + hasar vinyet efekti; hp≤0 → ölüm ekranı (gri + geri sayım), `respawn` gelince `SpawnPoint(team,slot)`'a ışınla (rig'i taşıma — free-roam'da IŞINLAMA YOK: bunun yerine "spawn noktana yürü" oku + hayalet modu: ölüyken silah ateşlemez, avatar'ı yarı saydam — **free-roam respawn tasarımı**: fiziksel oyuncu taşınamaz!).
  - ⚠️ Bu, free-roam'un en önemli tasarım farkı: respawn = konum değil DURUM değişimi. Server `respawn.spawnSlot`'u yine gönderir ("tabanına dön" hedefi olarak gösterilir); oyuncu kendi tabanı `BaseZone`'una fiziken girince canlanma aktifleşir (`BaseZone.onPlayerEntered` zaten var — bağla). Süre VE bölge koşulu birlikte: delay dolmuş + tabanda.
- **`Health.cs`**: yerel otoriter kullanım kaldırılır; `Current` yalnız `health_update`'ten set edilir (server-driven). Dummy/hedef pratiği için eski yol `#if UNITY_EDITOR` altında kalabilir.

## Adım 4 — AdminConsole genişletmesi

- Mod seçici (GameCatalog'dan) + harita seçici (seçilen modun uyumlu haritaları) + "Maçı Başlat" → `start_match`.
- Canlı: faz + süre + skor; kill-feed listesi; "Maçı Bitir" (`abort_match`), "Lobiye Dön".
- Taktik görünüm: MapDefinition boyutunu kullan (10×10 hardcode kalkar); ölü oyuncu noktası soluk.

## Adım 5 — Doğrulama (tek toplu geçiş)

1. Loopback: server + admin + 2 player (1 Editor + 1 Windows build): start_match → herkes Arena10x10'a geçer → countdown → Live; A, B'ye ateş eder → server konsolunda hit doğrulaması → B'nin canı düşer → kill → skor artar → respawn akışı (delay + taban bölgesi) → scoreLimit'e ulaşınca match_end → return_to_lobby.
2. Hile/hata yolları: aynı takıma ateş → hasar YOK (server reddi loglanır); ölüyken ateş → shot_fired relay yok; rate-limit ihlali (spam hit_report) → reddedilir.
3. Quest 2-cihaz testi: gerçek alanda kısa TDM raundu; respawn "tabana dön" akışı anlaşılır mı (metin/ok yeterli mi) not al.
4. `config/weapons.json` ile Unity SO değerleri tutarlı (elle kontrol — Faz 4'te otomatiğe bağlanacak).
5. Commit: `Faz 3: sunucu-otoriter maç akışı + TDM + silah/can senkronu + free-roam respawn`

## Çıktı kontrol listesi

- [ ] SO veri modeli (Weapon/Map/Mode/GameCatalog) + SpawnPoint'ler sahnede
- [ ] Server: MatchDirector faz makinesi + TdmMode + hit doğrulama hattı + weapons.json
- [ ] Unity: Modes/TeamDeathmatch modülü + Weapon/Health ağ bağlama + free-roam respawn (taban bölgesi koşullu)
- [ ] AdminConsole: mod/harita seç + start + canlı skor/kill-feed
- [ ] E2E TDM raundu (loopback + 2 Quest) geçti
- [ ] Commit atılmış
