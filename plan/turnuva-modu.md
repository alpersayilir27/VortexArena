# Turnuva Modu (`tournament`) — kalan iş

Kod, asset ve dokümanların **tamamı yazıldı**. Kalıcı bilgi dokümanlara işlendi:
`Docs/ArenaNet-Protokol.md` (§5.2/5.3 `countdownSeconds`, §10.1 tur tabanlı modlar + `modeState`
sözlüğü, §10.4 `reviveAnchor:"none"`, §10.5 kayıtlı modlar) · `Docs/Sistem-Ozeti.md`
(§3.7, §3.8.2, §3.9, §4, §7/67–70, §8) · `CLAUDE.md` (yeni mod reçetesi) · `Server/README.md`.

Bu dosya yalnız **elde kalan adımları** ve doğrulama listesini tutuyor; hepsi bitince silinir.

---

## 1. Ön koşul: silah kaynağı (ENGEL)

Turnuva `weaponSource:"weaponcanvas"` kullanıyor ve silahlar arenaya **elle** konuyor; üç arenaya
henüz konmadı → **sahnede silah yok**. Aşağıdaki listenin silah gerektiren maddeleri
`plan/arenaya-dagilmis-silah.md`'deki yerleştirme bitmeden koşulamaz.

`BaseZone` ×2 (toplanma kapısı) üç arenada da yerinde — `Arena12x12` · `IceWorld` ·
`ArenaVortexAntep`.

---

## 2. Doğrulama listesi

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
- [ ] ⚠️ **Geri sayım sırasında biri tabandan çıkarsa geri sayım İPTAL** oluyor: HUD'daki sayı
      kayboluyor, "TOPLANMA 1/2" geri geliyor, durum metni "Yeni tur — tabanına dön". Geri girince
      geri sayım **baştan** başlıyor
- [ ] **Yeni turda herkes tam şarjör + yedek şarjörle başlıyor** — turu sağ bitiren dahil
- [ ] Yeni turda ölmüş oyuncu **canlı ve ateş edebiliyor** (ölüm ekranı kapandı = `health_update` geldi)

**Sınır durumları**
- [ ] Süre dolduğunda çok kişi ayakta olan takıma puan yazılıyor; eşitse kimseye yazılmıyor
- [ ] Bir başlığı kapat → tur **başlamıyor**, toplanma süresiz bekliyor; konsola 30 sn'de bir
      "toplanma bekleniyor (1/2) — tabanına dönmeyenler: …" düşüyor
- [ ] O oyuncu **atılınca** (kick) sayaç 1/2 → 1/1 oluyor ve geri sayım hemen başlıyor
- [ ] `abort_match` toplanma sırasında lobiye döndürüyor
- [ ] Skor limitine ulaşılınca `finished` + "KIRMIZI/MAVİ KAZANDI"; kazanan ekranı **operatör
      harita/lobi seçene kadar duruyor** (otomatik dönüş yalnız uzun emniyet süresinde)

**Gerileme (bunlar DEĞİŞMEMELİ)**
- [ ] TDM: geri sayım seçilmediyse 5 sn, canlanma tabanda çalışıyor, skor öldürme sayıyor
- [ ] FFA: sabit durarak canlanma çalışıyor, rastgele silah geliyor
- [ ] Lobide serbest atış çalışıyor (artık `random`: grip'e basınca elde silah belirir)

---

## 3. Bilinçli olarak YAPILMAYANLAR (sorulursa cevap burada)

- **Taraf değişimi (side swap) yok.** CS'te taraflar yarıda değişir; free-roam'da bu, oyuncuların
  fiziksel olarak karşı tabana yürümesi demek ve arena simetrik değilse anlamı da az. İstenirse
  ayrı bir iş olarak planlanır.
- **Toplanmada zorunlu başlatma yok.** Tur, herkes tabanına girene kadar bekler; zaman aşımı diye
  bir şey yoktur ve geri eklenmez (eksik oyuncuyla açılan tur, tam kadro beklemenin varlık sebebini
  çiğniyordu). Takılan başlık için çıkış operatörün **AT**'ı ya da **İPTAL**'idir.
- **`PROTOCOL_VERSION` artmadı.** `countdownSeconds` yalnız admin↔sunucu yönünde, isteğe bağlı ve
  `0 = varsayılan` fallback'i olan bir alan (`roundSeconds`/`scoreLimit` ile birebir aynı
  sözleşme); `reviveAnchor:"none"` ise §10.5'in "bilinmeyen değer varsayılana düşer" kuralına
  giriyor. Eski bir istemci bağlanabilir, yalnız ölüm ekranı metnini yanlış yazar.
