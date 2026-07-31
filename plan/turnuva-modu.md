# Turnuva Modu (`tournament`) — kalan iş

Kod, asset ve dokümanların **tamamı yazıldı**. Kalıcı bilgi dokümanlara işlendi:
`Docs/ArenaNet-Protokol.md` (§5.2/5.3 `countdownSeconds`, §10.1 tur tabanlı modlar + `modeState`
sözlüğü, §10.4 `reviveAnchor:"none"`, §10.5 kayıtlı modlar) · `Docs/Sistem-Ozeti.md`
(§3.7, §3.8.2, §3.9, §4, §7/67–70, §8) · `CLAUDE.md` (yeni mod reçetesi) · `Server/README.md`.

Bu dosya yalnız **elde kalan iki adımı** ve doğrulama listesini tutuyor; ikisi de bitince silinir.

---

## 1. Elde kalan (ajan yapamaz — editör/derleme kullanıcıya aittir)

| # | İş | Neden elle |
|---|---|---|
| 1 | **Unity'yi aç, derlenmesini bekle** | Yeni asmdef (`VortexArena.Modes.Tournament`) ve yeni asset'ler import edilecek |
| 2 | **`Tools > VortexArena > Export Server Config`** | `Server/config/maps.json`'a `"tournament"` girsin. ⚠️ **Atlanırsa `start_match` "harita bu modu desteklemiyor" diye SESSİZCE reddedilir** (sebep yalnız sunucu konsolunda görünür). Araç sonda modal dialog açtığı için CLI/MCP'den çalıştırılmaz |
| 3 | `dotnet build Server/` (ya da `scripts\deploy-server.bat`) | Sunucu tarafı derlemesi |

**Build Settings'e dokunmak gerekmez** — turnuva yeni sahne getirmiyor, mevcut arenalarda oynanıyor.

## 2. Arena ön koşulu (kontrol edilecek, iş çıkarsa yapılacak)

Turnuva `weaponSource:"rack"` + taban tabanlı toplanma kullanıyor. Oynanacak her arenada:

- [ ] **Silah rafı var mı** (`WeaponRackSpawner` + `RackSlot`'lar) — yoksa oyuncu silahsız kalır.
      TDM de raf kullandığı için mevcut arenalarda olması beklenir.
- [ ] **`BaseZone` ×2 var mı** (kırmızı/mavi) — toplanma kapısı tabana girmeye bağlı. Sahnede hiç
      açık bölge yoksa raporlayıcı oyuncuyu kilitlemez (anında hazır sayar) ama toplanma
      anlamsızlaşır.

Etkilenen arenalar: `Arena12x12` · `IceWorld` · `ArenaVortexAntep`.

---

## 3. Doğrulama listesi

**Derleme**
- [ ] Unity konsolu temiz (yeni asmdef + `ModeReviveAnchor.None` + `PlayerCombatState` refactoru)
- [ ] `dotnet build Server/` temiz
- [ ] Sunucu açılış başlığı: `Modlar : tdm, ffa, tournament`
- [ ] `Server/config/maps.json` — lobi olmayan haritalarda `"tournament"` **var**, lobilerde **yok**

**Admin arayüzü**
- [ ] Mod seçicisinde `Turnuva` görünüyor
- [ ] Tercihler panelinde **"Geri sayim (sn)"** satırı var, 5–30 arası ±1 adımlıyor, iki admin
      arasında senkron (`admin_state`)
- [ ] Süre seçicisinde kısa değerler (1 · 1.5 · 2 · 2.5 · 3 dk) çıkıyor

**Tur akışı (2 oyuncu, farklı takımlar)**
- [ ] Maç başlıyor, HUD faz satırında **"TUR 1"** yazıyor, skor satırında `KIRMIZI 0 — 0 MAVİ · 1v1`
- [ ] Ölen oyuncu **canlanmıyor**: ölüm ekranı "Elendin — takımın turu bitirene kadar bekle" diyor
- [ ] ⚠️ **20 sn beklendiğinde de canlanmıyor** (`REVIVE_GRACE` kapalı — iki yolun ikincisi)
- [ ] Bir takım tümüyle ölünce tur bitiyor, karşı takıma **+1** yazılıyor
- [ ] Faz `paused`/`mode`'a geçiyor, HUD **"TOPLANMA 0/2"** yazıyor, durum metni "Yeni tur —
      tabanına dön"
- [ ] Oyuncu tabanına girince sayaç **0/2 → 1/2** ilerliyor, tabandan çıkınca geri düşüyor
- [ ] İkisi de tabandayken geri sayım başlıyor (seçilen `countdownSeconds` kadar) ve yeni tur açılıyor
- [ ] **Yeni turda herkes tam şarjör + yedek şarjörle başlıyor** — turu sağ bitiren dahil
- [ ] Yeni turda ölmüş oyuncu **canlı ve ateş edebiliyor** (ölüm ekranı kapandı = `health_update` geldi)

**Sınır durumları**
- [ ] Süre dolduğunda çok kişi ayakta olan takıma puan yazılıyor; eşitse kimseye yazılmıyor
- [ ] Bir başlığı kapat → 60 sn sonra toplanma zaman aşımıyla tur yine başlıyor (konsolda
      "tabanına dönmeyenler: …")
- [ ] `abort_match` toplanma sırasında lobiye döndürüyor
- [ ] Skor limitine ulaşılınca `finished` + "KIRMIZI/MAVİ KAZANDI", 10 sn sonra lobi

**Gerileme (bunlar DEĞİŞMEMELİ)**
- [ ] TDM: geri sayım seçilmediyse 5 sn, canlanma tabanda çalışıyor, skor öldürme sayıyor
- [ ] FFA: sabit durarak canlanma çalışıyor, rastgele silah geliyor
- [ ] Lobide serbest atış ve silah çerçevesi çalışıyor

---

## 4. Bilinçli olarak YAPILMAYANLAR (sorulursa cevap burada)

- **Taraf değişimi (side swap) yok.** CS'te taraflar yarıda değişir; free-roam'da bu, oyuncuların
  fiziksel olarak karşı tabana yürümesi demek ve arena simetrik değilse anlamı da az. İstenirse
  ayrı bir iş olarak planlanır.
- **Toplanma süresi parametrik değil** (60 sn sabit, `TournamentMode` içinde). O bir emniyet
  zaman aşımıdır, oyun ayarı değil — normal akışta hiç devreye girmez; operatörün göreceği tek
  bekleme geri sayımdır ve o parametrik.
- **`PROTOCOL_VERSION` artmadı.** `countdownSeconds` yalnız admin↔sunucu yönünde, isteğe bağlı ve
  `0 = varsayılan` fallback'i olan bir alan (`roundSeconds`/`scoreLimit` ile birebir aynı
  sözleşme); `reviveAnchor:"none"` ise §10.5'in "bilinmeyen değer varsayılana düşer" kuralına
  giriyor. Eski bir istemci bağlanabilir, yalnız ölüm ekranı metnini yanlış yazar.
