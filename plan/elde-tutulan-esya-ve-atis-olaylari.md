# Elde tutulan eşya + atış olayları — KALAN İŞLER

Kod, tel formatı ve kavrama kayıtları yerinde; kalan iş **sahada gözle ayarlanacak değerler** ve
bir his kararı. Kalıcı bilgi dokümanda: tel formatı + kurallar `Docs/ArenaNet-Protokol.md`
§6.2–§6.6 · akış/bileşenler ve editör aracı `Docs/Sistem-Ozeti.md` · stüdyo reçetesi
`Docs/Gelistirici/Yemek-Kitabi.md`.

## 1. Tracer + ön kabza göstergesi görünüm değerleri — playtest ayarı

`ItemDefinition`'daki `tracerColor` / `tracerWidth` / `tracerLifetime` / `tracerEveryNthRound`
(varsayılan 3) sahada gözle ayarlanır. Dokümana sayı yazılmaz.
Karar verilecek: her silahın tracer'ı farklı mı görünecek, yoksa hepsi aynı mı kalacak (altyapı
ikisini de destekliyor — alanlar silah başına, değerler şu an aynı).

Ön kabza tarafında ayarlanacaklar: **soket yarıçapı silah başınadır** (`secondaryGripRadius`,
varsayılan 0.10 = 20 cm çap — Inspector'dan girilir; görülen küre = kabul hacmi). `Weapon`
sabitleri (kod içinde, tüm silahlarda ortak): `SecondaryGripHoverRadius` (0.30 m — kürenin
görünmeye başladığı kumanda uzaklığı) · `IndicatorHoverAlpha`/`IndicatorReadyAlpha` · `IndicatorColor`.
Kürenin sanatı (`VA_GripSocket.prefab` + `M_GripSocket.mat`: renk/materyal) prefabtır, orada
düzenlenir; **1 m çap sözleşmesi** korunur (ölçeği `Weapon` verir).
⚠️ Ön kabza silah ana elde SALLANIRKEN tutuluyor: hareketli bir hedefe 10 cm dar geliyorsa önce
`secondaryGripRadius`'u büyüt — kod değişikliği değil, silah başına bir ayar (küre de büyür).
İsteğe bağlı: `WeaponCatalog.secondaryGripIndicatorPrefab`'a tasarlanmış bir soket sanatı bağlamak —
varsayılan küre silah kiti koşusuyla üretilip bağlanıyor, yani **iş yapılmadan da çalışıyor**.
Soket silahın dönüşünü alır (küre için önemsiz).

## 2. İki elli yerel nişan kuralı — his kararı

Silah sabit ana eli mi izleyecek (bugünkü davranış), yoksa iki elin doğrultusuna mı hizalanacak?
⚠️ **Tel formatını ETKİLEMEZ**: iki elin pozu da telde olduğu için uzak istemci aynı kuralı kendi
tarafında yeniden uygular. Yani playtest'te serbestçe değiştirilebilir, protokol sabit kalır.
