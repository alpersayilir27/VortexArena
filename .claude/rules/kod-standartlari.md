# Kural: Kod yazım standartları

## Yorum dili: İNGİLİZCE

- **Tüm kod yorumları İngilizce yazılır** — `//`, `/* */`, `///` XML doc ve `#region` adları
  dahil; kapsam kaynak kodun tamamıdır (`Assets/`, `Server/`, `launcher/`, `updater/`,
  `scripts/` içindeki betik yorumları).
- Dokunulan bir dosyada Türkçe yorum kaldıysa aynı değişiklikte İngilizceye çevrilir —
  anlam ve taşıdığı uyarı/gerekçe korunarak.
- ⚠️ **String literaller bu kuralın DIŞINDADIR:** oyuncu/operatör arayüz metinleri, log ve
  hata mesajları Türkçedir ve Türkçe KALIR — ürün dili Türkçe, kod dili İngilizce.
- Doküman dili de Türkçedir (`Docs/`, README'ler, `plan/`, `.claude/`) — bu kural yalnız
  kod yorumlarını kapsar.

## Yorum uzunluğu: KISA

Yorum kodun yerine geçmez, boşluğunu doldurur. Uzun yorum bakım borcudur — kod değişince
sessizce yalan olur.

- **Satır içi yorum = tek kısa ibare; blok yorum = en fazla 2-3 satır.** Uzunsa yeri
  yorum değil `Docs/`'tur ([[docs-sync]]) — koda tek satırlık işaret bırakılır.
- Cümle değil ibare yaz: *"This method is responsible for calculating the damage"* değil
  *"Calculates damage"*. `Note that` · `It should be noted` · `In order to` gibi dolgular
  yazılmaz.
- **Kodun kendisinin söylediğini tekrarlayan yorum SİLİNİR** — yorum yalnız kodun
  söyleyemediğini söyler: gerekçe, kısıt, tuzak.
- ⚠️ Kısaltmak **bilgi atmak değildir:** uyarı/gerekçe taşıyan bir yorum ("yoksa şu bozulur")
  daha yoğun ifade edilir, silinmez.
- `///` XML doc'ta `<summary>` tek satırdır.

## İsimlendirme

- asmdef = `VortexArena.<Katman>`; namespace = asmdef adıyla birebir (`rootNamespace` dolu).
- Global namespace'te tip YOK; serialize edilen ikincil tipler kendi dosyasında (`Team.cs` gibi).
- Sahne adı = katalog anahtarı (`load_match` string'i) → birebir eşleşme.

## Serialize edilen veri

- ⚠️ **Serialize edilen enum'a yeni değer SONA eklenir** (Unity sayısal indeks saklar):
  `Team`, `HitZone` (`Body` sıfırda kalır), `GameType`, `ModeTeamMode`, `ModeScoreKind`,
  `ModeReviveAnchor`, `ModeWeaponSource`, `ModeAudioEvent`, `ModeAudioGameType`.
- Gerekçe ve diğer serileştirme tuzakları: `Docs/Gelistirici/Yapma-Listesi.md`
  "Serialize edilen veriler" bölümü.
