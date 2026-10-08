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

## Blender kaynakları (`blender/`)

Betikle üretilen her Blender işi `blender/<Proje>/` altında **sıfırdan yeniden üretilebilir**
durur; `.blend` tek başına kaynak sayılmaz, elle yapılan düzenleme betiğe yazılmadıkça kalıcı
değildir.

| Yer | İçerik |
|---|---|
| `blender/<Proje>/<Proje>.blend` | Çalışma dosyası |
| `blender/<Proje>/scripts/` | Üretim betiklerinin **tamamı**: ortak yardımcı modül + obje başına bir betik (`<id>_<ad>.py`). Betik koşunca modeli kurar ve çıktısını yazar |
| `blender/<Proje>/ref/` | Girdi referans görselleri, PNG (`<id>_<ad>_<görünüm>.png`; görünüm `front` · `left` · `back` · `right` · `sheet`) |
| `blender/<Proje>/README.md` | Nasıl yeniden üretilir: hangi betik hangi sırayla koşar, çıktı nereye yazılır, ölçüler hangi sahne verisine bağlı |

- ⚠️ **Çıktı (FBX, doku) yalnız `Assets/`'ta durur:** betik doğrudan oraya yazar, `blender/`
  altında kopyası tutulmaz. `export/` · `renders/` · `*.blend1` · `__pycache__/` git'e girmez
  (`blender/.gitignore`).
- Dosya, obje ve koleksiyon adları ASCII'dir; Blender'daki obje adı FBX adıyla aynıdır
  (`A4_Fridge` → `A4_Fridge.fbx`).
- Önizleme render'ı geçicidir, repoya girmez.

## Serialize edilen veri

- ⚠️ **Serialize edilen enum'a yeni değer SONA eklenir** (Unity sayısal indeks saklar):
  `Team`, `HitZone` (`Body` sıfırda kalır), `GameType`, `ModeTeamMode`, `ModeScoreKind`,
  `ModeReviveAnchor`, `ModeWeaponSource`, `ModeAudioEvent`, `ModeAudioGameType`; Girdap arayüz
  kitinde `UiButtonKind`, `UiChipKind`, `UiGradientMode`, `GirdapFont` (prefablara serileşir).
- Gerekçe ve diğer serileştirme tuzakları: `Docs/Gelistirici/Yapma-Listesi.md`
  "Serialize edilen veriler" bölümü.
