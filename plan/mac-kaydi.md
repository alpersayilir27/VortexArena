# Maç kaydı ve video çıkarma — plan

Operatör launcher'dan kaydı açınca sunucu her maçı ayrı bir dosyaya kaydeder (`.vxr`, biçim ve
açma/kapama `Docs/ArenaNet-Protokol.md` §12-13); launcher'ın Kayıt sayfası kayıtları tarih-saate
göre listeler, seçilen kayıttan **seçilen kamera ve oyuncuyla** MP4 çıkarır. İleride aynı kayıt bir
WebGL izleyicide oynatılabilir; bugünkü her parça bunu kapatmayacak şekilde yazılır (§7).

## 0. Değişmez kurallar

- **Kayıt = admin'in aldığı akış.** Oynatma yeni bir sahne mantığı yazmaz: kayıt canlı admin
  izleyicisinin parse yollarına (`NetEvents`, registry'ler) geri beslenir. Canlı izleyicide doğru
  görünen her şey oynatmada da doğru görünür; oynatmaya özel görsel mantık yazılmaz.
- **Tel değişmez:** `PROTOCOL_VERSION` artmaz, gözlüğe yeni APK gitmez. Değişen yalnız sunucu ve
  Windows build'idir.
- **Oynatıcı ağa hiçbir şey göndermez ve bağlanmaz.** Oynatma kipinde `ArenaClient` bağlantı
  kurmaz, keşif koşmaz.
- **Oynatma ayrı bir rol DEĞİL, admin rolünün kipidir** (`AppSession.IsReplay`). Gerekçe: kod
  "admin değilse oyuncudur" diye dallanır; üçüncü bir rol yerel rig'i, oyuncu HUD'unu, sonuç
  ekranını oynatmada sessizce açardı. Kip yalnız ağ ve operatör komutu yollarını kapatır.
- **Kayıt maçı yavaşlatamaz:** maç döngüsü ve snapshot döngüsü yalnız kuyruğa ekler; disk ayrı iş
  parçacığındadır.
- **Oynatma çekirdeği OVR/Meta'ya dokunmaz** (`VortexArena.Net` + `VortexArena.Protocol`): thread
  yok, soket yok, kaynak `byte[]`. Web yolunun ön koşulu budur (§6).

## 1. Kamera ve kapsam seçenekleri

İki eksen birbirinden bağımsızdır: **ne kadarı** (kapsam) × **nereden** (kamera).

| Kapsam | Anlamı |
|---|---|
| Tam maç | `load_match`'ten kapanışa |
| Oyuncu highlight'ı | Seçilen oyuncunun öldürmeleri (`kill_event.killerId`), her biri öncesi/sonrası pencereyle; çakışan pencereler birleşir |
| Maç highlight'ı | Bütün öldürmeler, aynı pencere kuralıyla |

| Kamera | Anlamı |
|---|---|
| Oyuncunun gözü (POV) | Mevcut POV; videoda kafa titremesi **yumuşatılır** (gözlükteki ham hareket 2D ekranda mide bulandırır) |
| Tepeden üçüncü şahıs | Seçilen oyuncuyu arkadan-yukarıdan izleyen takip kamerası; duvara girerse oyuncuya yaklaşır |
| Köşe kameraları | Haritanın köşelerindeki sabit noktalar; bakış yönü aksiyonu (seçili oyuncu ya da çatışma merkezi) yumuşakça izler, en iyi gören köşeye geçilir |
| Tepeden plan | Mevcut ortografik tepe görünümü |

**Köşe kamerası konumu = sahnedeki işaretçi** (`ReplayCameraPoint`, Core). Yeni haritada iş tek
adımdır: editor aracı `ArenaBoundary`'den dört köşe işaretçisi üretir, geliştirici Scene görünümünde
duvara/tavana giren varsa elle kaydırır. **İşaretçisi olmayan harita kırılmaz:** oynatıcı köşeleri
çalışma zamanında `ArenaBoundary`'den türetir. Reçete adımı `Yemek-Kitabi.md` arena reçetesine girer.

## 2. Uygulama yerleşimi

- **Kayıtlar:** launcher kökünde `replays/` (launcher sunucuyu `--replay-dir` ile başlatır);
  sunucu klasörü yeniden dağıtılınca kayıtlar gitmez.
- **Oynatıcı / video üretici:** admin build'in kendisi, `--replay <dosya>` ile (`<kök>\admin\`).
  Komut satırı sözleşmesi (`--replay <dosya>`, Faz 3'te `--export …`) masaüstü ile web'in ortak
  giriş noktasıdır: web'de aynı parametreler URL'den gelir.
- **Arayüz = launcher'ın Kayıt sayfası:** kaydı açar/kapatır, listeler (başlık + meta), "İzle" ile
  oynatıcıyı başlatır. Faz 4'te video çıkarma buraya eklenir; videolar `<kök>\videos\` altına
  `<yyyy-MM-dd_HH-mm-ss>_<oyuncu>_<kamera>.mp4` adıyla düşer.

## 3. Faz 1 — Kayıt + oynatma çekirdeği

Kod ve doküman **bitti** (sunucu `MatchRecorder` + launcher kontrol uçları, `NetClock`,
`NetMessageDispatcher`, `ReplayPlayer`, `ReplayController`, `--replay` kipi, launcher Kayıt sayfası;
`Docs/Sistem-Ozeti.md` §4). Kalan:

- [ ] Doğrulama: sunucu + launcher derlemesi; launcher'dan kayıt açılıp (lobide ve maç ortasında)
      bir maçın kaydı ve Kayıt sayfasından oynatılması.

## 4. Faz 2 — Kameralar

- [ ] POV yumuşatma, tepeden üçüncü şahıs, köşe kameraları + otomatik köşe seçimi (§1).
- [ ] `ReplayCameraPoint` + üretici editor aracı + `ArenaBoundary` yedeği; işaretçiler bütün
      arenalara yerleştirilir; `Yemek-Kitabi.md` arena reçetesine adım.
- [ ] Oynatma arayüzü: zaman çizgisi, oynat/duraklat, hız, oyuncu ve kamera seçimi. Operatör
      komut düğmeleri oynatmada **gizlenir** (bugün görünür ama etkisizdir — tek giden kapı
      `AdminCommands.Send` kapalı).
- [ ] Sahne yüklenirken duraklatma: geçiş karartması ölçekli zamanı bekliyorsa `timeScale` 0
      yüklemeyi askıda tutar — yükleme sırasında duraklatma ertelenir.

## 5. Faz 3 — Video çıkarma

- [ ] Sabit adımlı render (`Time.captureDeltaTime`): oynatma saati Unity zamanından türer, video
      gerçek zamandan bağımsız (hızlı makinede gerçek zamandan hızlı) çıkar.
- [ ] Kare yakalama `AsyncGPUReadback` → `ffmpeg.exe` stdin; NVENC varsa donanım, yoksa `libx264`.
      ⚠️ ffmpeg yeniden dağıtımı: LGPL build seçilir, lisans dosyası yanında durur.
- [ ] Ses izi: oyun sesleri olaydan tetiklendiği için (`GameAudio`, `RemoteShotFx`) gerçek zamana
      kilitlenmeden üretilir (Unity `AudioRenderer` ya da olay listesinden ayrı miks) — yoksa
      dışa aktarma gerçek zamana kilitlenir.
- [ ] Highlight yönetmeni (§1 kapsamları) + komut satırı `--export`.
- [ ] Admin PC asgari donanımı (ekran kartı) `Docs/Isletme-Kurulum.md`'ye.

## 6. Faz 4 — Launcher'da video çıkarma + dağıtım

- [ ] Kayıt sayfasına "Video çıkar": kamera, oyuncu ve kapsam seçimi (§1), ilerleme, `videos/`'u aç.
- [ ] Dağıtım: ffmpeg launcher köküne; `scripts/` betiği + `deploy/README.md`.

## 7. Web (ileride) — bugünden korunanlar ve kalan engeller

- **Korunan temel:** biçim + okuyucu motor bağımsız (`ReplayFile`, Protocol); oynatma çekirdeği
  `VortexArena.Net`'te, kaynağı `byte[]`; komut satırı sözleşmesi URL parametresine birebir taşınır.
- **Kalan engeller:** `VortexArena.Core` / `App` asmdef'leri Oculus/Meta'ya koşulsuz bağlı ve
  `UNITY_WEBGL` koruması yok; uzak gövde Movement SDK'nın native retargeter'ına dayanıyor (WebGL'de
  büyük olasılıkla çalışmaz → web izleyicinin kendi avatar yolu gerekir). Video web'de üretilmez —
  sunucu PC'de üretilen MP4 sunulur.

## 8. Açık kararlar

- [ ] **KVKK / açık rıza:** müşterinin adı ve hareketleriyle video üretip vermek — işletme
      sözleşmesine/girişteki onaya madde.
- [ ] Varsayılan saklama süresi (`replayKeepDays`) sahaya göre ayarlanır.
