# Engel ihlali — kalan işler

Kod, protokol ve doküman **yazıldı**. Sistemin anlatımı artık dokümanlarda:
`Docs/ArenaNet-Protokol.md` §10.9 (kural + otorite) · `Docs/Sistem-Ozeti.md` §4
(`BodyViolationProbe` · `ScreenFade` · `ArenaLayers` · `ObstacleLayerAuditor`) ve §7 tuzak
126–128 · `CLAUDE.md` (layer sözleşmesi + araç satırı).

⚠️ **Protokol v11** — tüm başlıklara yeni APK gerekir. Karışık sürümde bozulan şey bir kuraldır,
çizim değil: eski istemci biti hiç göndermez (o oyuncu engelde ceza almaz) ve gelen biti yok sayar
(admin halkası yanıp sönmez).

## 1. Sahne işi (elle)

- [ ] Her arenada **iç engellerin** (sütun, kasa, sandık, blok) collider'ını `Obstacle` layer'ına al.
      ⚠️ Dış duvar, zemin ve tavan **girmez** — kalibrasyonu kaymış oyuncu durduk yere ölmesin.
- [ ] Her sahnede `Tools > VortexArena > Arena > Engel Hacimlerini Denetle` koştur; rapordaki
      **konveks olmayan** collider'lar düzeltilene kadar o objeler ihlal üretmez.

## 2. VFX (asset yok)

- [ ] Engel yüzeyindeki kesişim efekti — **havuzlanmış tek** partikül sistemi, ihlal başına
      `Instantiate` YOK. Karartma ve titreşim koddan geliyor, eksik olan yalnız partikül.

## 3. Doğrulama (kullanıcı koşar)

- [ ] `dotnet build` (Server) + Unity derlemesi
- [ ] Oyuncu: engele gövdeyle gir → ekran kızarır, titreşim başlar, ~3.3 sn'de ölüm; çıkınca durur
- [ ] Uyarı bandı: engele yaklaşırken (%15 civarı) hafif kararma, %30'da tam ihlal
- [ ] Yalnız kafayı sok → tetiklenir; yalnız eli sok → tetiklenmez
- [ ] Silahı tümüyle engele sok → tetiklenir
- [ ] Admin: ihlal eden oyuncunun halkası kırmızı, 3 Hz yanıp söner; çıkınca takım rengine döner
- [ ] Kill feed: "… engelde kaldı" satırı; skor **değişmez**, `deaths` artar
- [ ] Kalibresiz oyuncu ihlalde **ceza almaz**
- [ ] Sınır karartması ile ihlal karartması aynı anda → titreme YOK (en yüksek alfa çizilir)
- [ ] Admin gözlemcide karartma hiç çizilmez, sonda hiç çalışmaz (rig yok)
