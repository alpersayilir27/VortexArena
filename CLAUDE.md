# VortexArena — Giriş Kapısı (CLAUDE.md)

## ⚠️ Bu dosyanın kuralı — GİRİŞ KAPISIDIR, içerik dosyası değil

Bu dosya her oturumda bağlama yüklenir: her satırı kalıcı maliyettir. İçinde proje detayı,
mimari anlatım ve domain kuralı DURMAZ — yalnız *"hangi soru → hangi doküman"* işaretleri ve
tek satırlık temel çalışma talimatları durur. **Test:** yazacağın cümle *"şuna şuradan bak"*
ya da tek satırlık *"şunu yap/yapma"* değilse yeri burası değildir → hedef tablosu
`.claude/rules/docs-sync.md`. **Tavan: 120 satır.**

> # ⛔ HER ŞEYDEN ÖNCE: UnityMCP kapısı
> İlk iş **`UnityMCP` ayakta mı** kontrolüdür (`mcp__UnityMCP__manage_editor` →
> `telemetry_status`). Düşerse önce sebebi ayır; iş Unity verisine dokunuyorsa tek çıktı
> **"MCP'yi çalıştır."**dır, dokunmuyorsa tek satır notla devam edilir.
> → `.claude/rules/unity-erisim.md`

**Ürün (tek paragraf):** free-roam VR PvP arena; işletmelere kurulum (LBE), Meta Quest 3/3S,
Unity 6000.3.20f1, URP. VR build = player, Windows build = admin. Online haberleşme kendi
.NET sunucumuz (`Server/`, offline LAN) — Mirror/NGO YOK. Ayrıntı: `Docs/Sistem-Ozeti.md` §1.

## Nereye bakılır

| Soru | Yer |
|---|---|
| Dokümanı tarayıcıda okumak | repo kökünde `docs-serve.bat` → http://localhost:1111 (yeni PC'de bir kez `scripts/docs-setup.bat`) |
| Sistem nasıl çalışıyor, bileşen sorumlulukları, editor araçları, tuzak gerekçeleri | `Docs/Sistem-Ozeti.md` (§2 repo · §3 ağ · §4 bileşen/araç sözlüğü · §5-6 kullanım · §7 Tuzaklar) |
| Ağ mesajı/sabit/port/doğrulama/otorite | `Docs/ArenaNet-Protokol.md` — **TEK doğruluk kaynağı** |
| **Bir şeyi değiştirmeden ÖNCE: bu yasak mı?** | `Docs/Gelistirici/Yapma-Listesi.md` (+ gerekçeler `Docs/Sistem-Ozeti.md` §7) |
| Yeni içerik ekleme (arena, lobi, mod, silah, kavrama, ses) | `Docs/Gelistirici/Yemek-Kitabi.md` — reçetesiz içerik eklenmez |
| Geliştirici başlangıcı, dev penceresi, Multiplayer Play Mode | `Docs/Gelistirici/Ilk-Adimlar.md` · ortam/MCP kurulumu `Docs/Gelistirici/Ortam-Kurulumu.md` |
| API imzaları · sahne kurulumu · arayüz düzenleme | `Docs/Gelistirici/API-Referansi.md` · `Sahne-Kurulumu.md` · `Arayuz-Tasarimi.md` |
| Sahadaki operatör (teknik olmayan dil) | `Docs/Kullanim-Kilavuzu.md` |
| İşletme kurulumu (donanım, ağ, kalibrasyon) | `Docs/Isletme-Kurulum.md` |
| Sunucu çalıştırma / CLI / config | `Server/README.md` |
| Build, dağıtım, OTA | `scripts/README.md` · `deploy/README.md` · `updater/README.md` |
| Sıradaki planlanmış işler | `plan/` (biten iş dokümanı **silinir**) |
| Çalışma kuralları | `.claude/rules/` — `unity-erisim` · `is-akisi` · `docs-sync` · `kod-standartlari` |

## Repo üst düzey yerleşim (ayrıntı `Docs/Sistem-Ozeti.md` §2)

`Assets/` (Unity) · `Server/` (.NET sunucu) · `launcher/` (operatör başlatıcısı) ·
`updater/` + `updater_uploader/` (Quest OTA) · `scripts/` (deploy/kurulum betikleri) ·
`deploy/` (üretilen exe'ler, **git'e girmez**) · `dev-targets.json` (commit'li hedef kataloğu) ·
`Docs/` · `plan/` · `.claude/rules/`.

## Temel talimatlar (ayrıntı ve gerekçe ilgili kural dosyasında)

- **Arama = önce auggie.** `mcp__auggie__codebase-retrieval` birincil bağlam aracıdır; sonucu
  Read/Grep ile teyit et. Tam simge biliniyorsa doğrudan Grep. → `is-akisi.md`
- **Projeyi ajan DERLEMEZ.** Derleme/build/test/Play kullanıcıya aittir; doğrulama sona
  batch'lenir. → `is-akisi.md`
- **Shell SON basamaktır** — aynı işi MCP tool'u ya da yerleşik araç yapabiliyorsa açılmaz;
  Unity verisi `manage_*` ile okunur, **YAML grep'lenmez**. Makine HER ZAMAN Windows.
  → `unity-erisim.md`
- **Ağır uygulama işi alt-ajana** (`subagent_type: "uygulayici"`) — kararı verilmemiş iş
  devredilmez. → `is-akisi.md`
- **Kod değişti = doküman AYNI commit'te değişti.** Ağ davranışında sıra: önce
  `Docs/ArenaNet-Protokol.md`, sonra kod. Hangi değişiklik nereye → `docs-sync.md`
- **AI notu kullanıcının makinesine YAZILMAZ** — hatırlanacak her şey repoda. → `docs-sync.md`
- **Kod yorumları İNGİLİZCE yazılır;** UI/log string'leri Türkçe kalır. → `kod-standartlari.md`
- **Değişiklikten önce `Yapma-Listesi.md`'ye bak** — bu projede tuzaklar hata vermez, sessizce
  yanlış çalışır.
- **Yeni içerik yalnız `Yemek-Kitabi.md` reçetesiyle eklenir** — adım atlamanın bedeli
  reçetenin başında yazar.
- **Editörde rol/adres dev penceresinden seçilir** (`Tools > VortexArena > Development > Dev`,
  `Ctrl+Alt+R`); sahneye rol/IP için `[SerializeField]` override KOYULMAZ.
  → `Docs/Gelistirici/Ilk-Adimlar.md`
