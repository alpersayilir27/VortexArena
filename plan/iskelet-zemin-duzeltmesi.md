# İskelet zemin düzeltmesi (gönderen tarafı, filtreli)

Sorun: body tracking iskeleti OS'in zemin tahminine basar, A/B kalibrasyonu yalnız rig'i düzeltir
(`Docs/Sistem-Ozeti.md` §7 "Tuzaklar", "body tracking iskeleti Quest'in kendi zemin tahminine
basar" maddesi). Gövde oturumdan oturuma zeminin altında/havada çizilir; etiket ve silah doğrudur.
⚠️ Kare başına ham göz-sabitlemesi YAPILMAZ — yasak ve gerekçesi aynı maddede.

## Yapılacaklar

- `[AvatarTanı]` verisiyle saha ölçümü: kök y sapmasının büyüklüğü ve oturum içi gezinme hızı
  (filtre zaman sabitini bu ölçüm belirler).
- Ofset kaynağı: rig'in `centerEyeAnchor`'ı − karakterin `EyeAnchor`'ı (iki referans aynı fiziksel
  nokta — oyuncunun gözü). Uygulama yeri: `ArenaNetCharacterBehaviour.ReceiveStreamData`,
  `WorldToArena`'dan ÖNCE, yalnız tele giden kökte.
- Filtre: ağır alçak geçiren (saniyeler mertebesinde zaman sabiti); ağırlık Y ekseninde; kafanın
  doğrusal/açısal hızı eşik üstündeyken güncelleme donar (son filtrelenmiş değer kullanılmaya
  devam eder). Referanslardan biri çözülemiyorsa ofset sıfır (bilinen davranışa düşüş).
- `GuardRootJump` emniyeti aynen korunur; yerel karakterin transformuna yazılmaz (§10.8 boy
  ölçümünün referansı odur).
- Tel formatı değişmez (düzeltme gönderende) — yine de yeni oyuncu APK'sı + admin build ister.
- Doğrulama: `[AvatarTanı]` kök y ≈ 0'a oturmalı; kafa hızla dönerken uzak gövde savrulmamalı;
  hem VortexAntep (taşınmış arena) hem IceWorld (orijindeki arena) üzerinde.
