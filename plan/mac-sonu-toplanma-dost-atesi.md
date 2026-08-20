# Maç sonu bekleme · Toplanma · Dost ateşi — kalan doğrulama

**Kod, prefab ve dokümanların tamamı yazıldı**; sunucu derlemesi temiz. Kalıcı bilgi dokümanlara
işlendi: `Docs/ArenaNet-Protokol.md` (§1 sabit, §5.2 `set_friendly_fire`, §5.3 `rules_update` +
`admin_state.friendlyFire`, §10.1 `finished` + tur tabanlı modlar, §10.2 takımdaş öldürme,
§10.5 `friendlyFire`) · `Docs/Sistem-Ozeti.md` (§3.6, §3.8.2, §3.9, §4, §7) ·
`Docs/Kullanim-Kilavuzu.md` (turnuva bölümü + dost ateşi) · `Docs/Gelistirici/Yapma-Listesi.md`.

Bu dosya yalnız **elde kalan doğrulamayı** tutuyor; hepsi geçince silinir.

---

## 1. Derleme (kullanıcı koşar)

- [ ] Unity konsolu temiz — dokunulan istemci dosyaları: `ControlMessages` · `MessageTypes` ·
      `NetEvents` · `ArenaClient` · `ModeRuntimePump` · `AdminSelection` · `AdminCommands` ·
      `AdminPreferencesPanel`
- [x] `dotnet build Server/` temiz (0 uyarı, 0 hata)

## 2. Tercihler paneli (`AdminHud.prefab`)

- [ ] MAÇ bölümünde satırlar üst üste binmiyor: Mod · Harita · Süre · Skor limiti · Geri sayim ·
      **Dost atesi**
- [ ] Kart ekrana sığıyor (alt kenarda BAĞLANTI bölümü kesilmiyor)
- [ ] "Dost atesi" satırındaki iki düğme de aç/kapa yapıyor; değer AÇIK iken kırmızı
- [ ] Satır **maç koşarken de basılabiliyor** (mod/harita kilitliyken bile)

## 3. Davranış

**Maç sonu**
- [ ] Maç bitti → kazanan ekranı duruyor, 10 sn'de lobiye dönmüyor
- [ ] Kazanan ekranındayken harita seç → herkes o arenaya geçiyor; lobi satırı → herkes lobiye;
      BAŞLAT → yeni maç kuruluyor (üçü de sayacı kesiyor)

**Turnuva toplanması**
- [ ] Biri tabanına gelmezse tur başlamıyor (60 sn'de de başlamıyor), konsola 30 sn'de bir
      "toplanma bekleniyor (n/m) — tabanına dönmeyenler: …" düşüyor
- [ ] O oyuncu atılınca (kick) sayaç düşüyor ve geri sayım hemen başlıyor
- [ ] Geri sayımda tabandan çıkan **her** durumda iptal oluyor

**Dost ateşi**
- [ ] Sunucu yeni açıldı → anahtar kapalı (mod ne olursa olsun)
- [ ] Maç **koşarken** açıldı → bir sonraki mermi takım arkadaşına hasar veriyor (yeniden başlatma yok)
- [ ] İki admin panelinde aynı anda değişiyor + duyuru satırı düşüyor
- [ ] Açıkken TDM'de takım arkadaşı öldürüldü → **takım skoru değişmiyor**, öldürende +1 kill,
      ölende +1 death, kill feed'de satır var, sunucu konsolunda "(TAKIMDAŞ — skor yazılmadı)"
- [ ] Anahtar açıkken harita seç / lobiye dön / yeni maç başlat → ayar korunuyor
- [ ] Yeni bağlanan oyuncu doğru değeri alıyor (`welcome.match.rules`)
