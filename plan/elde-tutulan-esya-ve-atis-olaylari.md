# Elde tutulan eşya + atış olayları — KALAN İŞLER

Kod, tel formatı ve kavrama kayıtları yerinde; kalan iş **sahada gözle ayarlanacak değerler** ve
bir his kararı. Kalıcı bilgi dokümanda: tel formatı + kurallar `Docs/ArenaNet-Protokol.md`
§6.2–§6.6 · akış/bileşenler ve editör aracı `Docs/Sistem-Ozeti.md` · stüdyo reçetesi
`Docs/Gelistirici/Yemek-Kitabi.md`.

## 1. Ön kabza göstergesi görünüm değerleri — playtest ayarı

Ön kabza tarafında ayarlanacaklar: **soket yarıçapı silah başınadır** (`secondaryGripRadius`,
varsayılan 0.10 = 20 cm çap — Inspector'dan girilir; görülen küre = kabul hacmi). `Weapon`
sabitleri (kod içinde, tüm silahlarda ortak): `SecondaryGripHoverRadius` (0.30 m — kürenin
görünmeye başladığı kumanda uzaklığı) · `IndicatorHoverAlpha`/`IndicatorReadyAlpha` · `IndicatorColor`.
Kürenin sanatı (`VA_GripSocket.prefab` + `M_GripSocket.mat`: renk/materyal) prefabtır, orada
düzenlenir; **1 m çap sözleşmesi** korunur (ölçeği `Weapon` verir).
⚠️ Ön kabza silah ana elde SALLANIRKEN tutuluyor: hareketli bir hedefe 10 cm dar geliyorsa önce
`secondaryGripRadius`'u büyüt — kod değişikliği değil, silah başına bir ayar (küre de büyür).
Küre kalıcı değil: yerine tasarlanmış görsel gelecek — hedef Meta el modelinin kendisi.
Bağlama noktası `WeaponCatalog.secondaryGripIndicatorPrefab`; varsayılan küre silah kiti koşusuyla
üretilip bağlandığı için o iş gelene kadar sistem çalışmaya devam eder. Soket silahın dönüşünü alır
(küre için önemsiz, el modeli için önemli).

## 2. İki elli yerel nişan kuralı — his kararı

Silah sabit ana eli mi izleyecek (bugünkü davranış), yoksa iki elin doğrultusuna mı hizalanacak?
⚠️ **Tel formatını ETKİLEMEZ**: iki elin pozu da telde olduğu için uzak istemci aynı kuralı kendi
tarafında yeniden uygular. Yani playtest'te serbestçe değiştirilebilir, protokol sabit kalır.
