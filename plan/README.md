# VortexArena — Uygulama Planı (sıradaki işler)

> Bu klasör **yalnız henüz yapılmamış işleri** tutar: biten işin dokümanı silinir, kalıcı bilgisi
> `Docs/` altına işlenir (eski metin git geçmişinde kalır).

| Planlanmış iş | Dosya |
|---|---|
| **Zombi modu** (`zombie`, kooperatif dalga savunması): zombi bir **ağ nesnesidir** — sunucu doğurur/öldürür/skorlar, **beyni (NavMesh + saldırı kararı) en eski bağlı Windows admin'de** koşar ve pozu `0x09` ile akar; admin düşünce/susunca sıradaki admin devralır, admin yoksa maç duraklar. Sunucu geometri almaz. Henüz **hiçbir şey yazılmadı**: protokol maddeleri (kinds, `attack` olayı, obje poz tavanı 16→48, `admin_state.objectHostId`), sunucu (`ZombieMode`, iki `IGameMode` kancası, `DamagePlayerFromObject`, sahip seçimi + susma bekçisi), istemci (`VortexArena.Modes.Zombie`: beyin, görünüm, HUD, doğum noktası), NavMesh bake, denge tablosu | `zombi-modu.md` |
| **Lisans ve izleme**: işletme başına süreli lisans + gözlük limiti (koltuk) + lease başına maç bütçesi; updater ve oyun **tek keystore**, gözlük kısa kodla aktive olur. Şifreli ve bütünlüğü doğrulanan yerel kullanım defteri: maçtan önce kalıcı kayıt, eksik/bozuk dosyada **tam kilit**, merkezle imzalı mutabakat ve denetimli kurtarma. İşletme bazında maç/zaman/katılım istatistiği; ileride oyun bazlı lisans için ayrı kredi rezervasyonu ve çevrimdışı geri alma koruması şartı. .NET backend bugünkü OTA uçlarının yerini alır. Henüz **hiçbir şey yazılmadı**. ⚠️ Faz 3 `PROTOCOL_VERSION` 25→26 — tüm gözlüklere yeni APK | `lisans-ve-izleme.md` |
| **Bulut kalibrasyonu** (`anchor_cloud`): tek gözlüğün hizası Meta paylaşılan uzamsal çapasıyla (grup paylaşımı) herkese dağıtılır — operatör bir oyuncuyu **kaynak** yapar, sunucu grup kimliğini taşır, alıcılar `AlignRigToAnchorPose` ile hizalanıp `source:"cloud"` bildirir. `PROTOCOL_VERSION` artmaz; diğer iki mod bugünkü gibi kalır (bulut yolu eski kalibrasyon kaydına yazmaz). ⚠️ Gözlüklere **internet** + her gözlükte **Enhanced spatial services** ister. Henüz **hiçbir şey yazılmadı** | `bulut-kalibre.md` |
| **Maç kaydı ve video çıkarma**: operatör launcher'dan kaydı açınca sunucu her maçı admin'in aldığı akış olarak `.vxr` dosyasına kaydeder (`Docs/ArenaNet-Protokol.md` §12-13); admin build'in `--replay` oynatma kipi kaydı canlı izleyicinin parse yollarına besler; kamera seçenekleri POV · tepeden üçüncü şahıs · köşe kameraları (sahne işaretçisi, yoksa `ArenaBoundary`'den) · tepeden plan, kapsam tam maç · oyuncu/maç highlight'ı; MP4 sabit adımlı render + ffmpeg, launcher'ın Kayıt sayfasından; WebGL izleyici ileride. `PROTOCOL_VERSION` artmaz. Faz 1 (kayıt + oynatma çekirdeği + launcher'dan aç/kapa) kod ve doküman **bitti**, kalan: doğrulama; Faz 2-4 yazılmadı | `mac-kaydi.md` |
| **Dev kalibre atlama + çubukla yürüme** (yalnız editör, Quest Link testi): Dev penceresindeki anahtar açıkken oyuncu mekanın A işaretinde, 1,80 m boy varsayımıyla ve `source:"dev"` ile kalibreli başlar; sol çubuk rig'i yatayda yürütür, sağ çubuk 30° döndürür. Kodun tamamı `#if UNITY_EDITOR` — build, sunucu ve `PROTOCOL_VERSION` değişmez; operatör sıfırlaması dev hizalamayı da düşürür; APK'ya taşınmaz. Kod ve doküman **bitti**. Kalan: doğrulama | `dev-kalibre-atlama.md` |
| **Arayüz yenileme — "Girdap" teması**: mockup'lar `Docs/Gelistirici/Arayuz/` altında durur (ortak stil `tema.css`, parça ve durumlar `kit.html`), ölçülerin kalıcı kaynağıdır. Admin HUD · istatistik · tercihler · oyuncunun maç sonu ekranı · oyuncu HUD'u (can şeridi · skor bandı · öldün kartı) · aşçı modunun lokanta arayüzü builder'dan üretiliyor (`Docs/Gelistirici/Arayuz-Tasarimi.md`). Kalan: (1) saha doğrulaması; (2) Mole'un sonuç ekranı (`Assets/Modes/Mole/UI/MoleResultOverlay.prefab`) hâlâ taban prefabın eski varyantı — gizli sütun override'ları, StatsPanel sprite override'ı ve metin altına düşen süslemeler çakışıyor; builder'dan üretilen yola taşınacak (`Yemek-Kitabi.md` "Moda özel maç sonu ekranı" yol B, örneği `BurgerResultOverlay`); (3) canlı ihlal çerçevesi (`AdminViolations.Of`) editör önizlemesinde test edilemiyor, sahada bakılacak | `../Docs/Gelistirici/Arayuz/admin-hud.html` |
| **Performans**: önce cihaz telemetrisi (`status`'a isteğe bağlı en kötü kare · yavaş kare sayısı · pil sıcaklığı, sunucu eşik aşımını loglar; `PROTOCOL_VERSION` artmaz), sonra ölçüme bağlı 90 Hz ve shader/PSO ısınması, kod tarafında rastgele silah kipinde yerel havuz ve durum paketi ayrıştırmasında ayırmasızlık. Henüz **hiçbir şey yazılmadı** | `performans.md` |

## Dağıtım — protokol sürümü artınca

⚠️ **Sürüm artışı = tüm başlıklara yeni APK + admin + sunucu, AYNI turda**
(`scripts\deploy-player-apk.bat` + `deploy-admin-game.bat` + `deploy-server.bat`). Sunucu sürümü
uyuşmayan cihazı bağlantıda reddeder (`Docs/ArenaNet-Protokol.md` `PROTOCOL_VERSION` satırı): eski
APK'lı gözlük oyuna giremez, kabuk lobide bekler (yeni APK'lar bağlantı kartında "SÜRÜM UYUMSUZ"
yazar); sebep sunucu konsoluna ve `admin_state.notice`'e yazılır.

## Değişmeyecekler (karar verildi, yeniden açılmaz)

- Sunucu incelemesinin şu maddeleri **uygulanmaz**; bugünkü davranışlar bilinçli kararlardır: maç
  başı sıfırlama listesi, mod duraklamasında maç bitişi, tek lider kuralı, anında `match_state`, WS
  gönderim kuyruğu, konsol QuickEdit. (Ölüm sonrası hasar penceresi bu listede DEĞİL: atılabilirlerde
  atıcı ölse de hasar verir.)
- Lag compensation / rewind, interest management: oyun hiç online olmayacak, gerekmez.
- Uzak avatarın bileğinde bombanın görünmesi: böyle bir özellik **olmayacak** — kılıf yalnız
  sahibinin ekranındadır.
- Sunucu test projesi: ileride.
- Tracer rengi silah başına farklılaşmaz — hepsi aynı kalır (altyapı destekliyor, istenmiyor).
- Çarpma efekti ve sesi yüzeye göre farklılaşmaz — her isabet `default` yüzeydir (altyapı
  destekliyor, anahtarı `SurfaceLibrary.definitions`; haritalara yüzey ataması yapılmaz).
- Fırlatılan bombanın uzak vuruş kutularıyla çarpışma eşlemesi ve kırılan objenin efekti
  değişmez: ikisi zaten kayıt ve havuz kullanır (`RemoteHitBox.Active`, `BlastFxPool`), sahne
  taraması ya da `Instantiate` yoktur.
- Quest gölge mesafesi kısaltılmaz: Shadowmask'ta statik geometri gerçek zamanlı gölge çizmez,
  gölgeyi yalnız oyuncu ve eşya düşürür; mesafeyi mekan köşegeninin altına çekmek alanın uzak
  ucunda oyuncu gölgesini keser, kazancı küçüktür.
- Gövde oranı kalibrasyonu (`CharacterRetargeter.Calibrate`) açılmaz: herkes prefabın oranlarıyla
  çizilir, boy farkı `LocalBodyAvatar`'ın boy ipucuyla çözülür; `SkeletonRetargeter.ScaleRange`
  ayarlanmaz.

## Nereye bakmalı

| Konu | Dosya |
|---|---|
| Sistem bugün ne, nasıl çalışıyor, hangi bileşen ne yapıyor | `Docs/Sistem-Ozeti.md` |
| Protokol (mesajlar, sabitler, kurallar) — **TEK doğruluk kaynağı** | `Docs/ArenaNet-Protokol.md` |
| İçerik ekleme reçeteleri | `Docs/Gelistirici/Yemek-Kitabi.md` |
| Yapılmayacaklar (yasaklar) | `Docs/Gelistirici/Yapma-Listesi.md` |
| Hangi soru hangi dokümanda (giriş kapısı) | `CLAUDE.md` |
| Çalışma kuralları | `.claude/rules/` |

## Bir faz bitince

1. Kalıcı olan her şey dokümana yazılır (`.claude/rules/docs-sync.md` tablosu): protokol
   `Docs/ArenaNet-Protokol.md`'ye, bileşen/akış `Docs/Sistem-Ozeti.md`'ye, reçete
   `Docs/Gelistirici/Yemek-Kitabi.md`'ye, yasak `Docs/Gelistirici/Yapma-Listesi.md`'ye,
   tuzaklar `Sistem-Ozeti.md` §7'ye.
2. Faz dosyası **silinir** — planın kendisi arşivlenmez, doküman güncel kalır.
3. `plan/README.md`'den satırı çıkarılır.
