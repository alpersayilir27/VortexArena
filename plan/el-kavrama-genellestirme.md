# El kavrama sistemini eşya geneline açmak — KALAN İŞLER

> **F1 (stüdyo kapısı), F2 (el başına slot defteri) ve F3 (dünya objesi propları) yazıldı**; kalıcı
> bilgi dokümanda: stüdyo ve slot defteri `Docs/Sistem-Ozeti.md` §4 · üç eksen, `GripSocket` ve
> `NetObjectGrabBridge` aynı sözlükte · reçeteler `Docs/Gelistirici/Yemek-Kitabi.md` ·
> imzalar `API-Referansi.md` · yasaklar `Yapma-Listesi.md`. Kavrama telde yok, ama F3 ağ nesnesi
> sahipliğine dayanıyor (protokol **v18**, `Docs/ArenaNet-Protokol.md` §10.10).
> Bu dosya yalnız **yapılmamış** olanı tutar; hepsi bitince silinir.

## 1. Doğrulama (kullanıcı koşar)

**Bozulmama (F1):**

- [ ] Var olan bir `WPN_*` prefabı stüdyoda açılır, **hiçbir şeye dokunulmadan** kaydedilir →
      `ItemGripPose` alanları **birebir aynı** kalır (stüdyonun kimlik sözleşmesi:
      `AnchorInItem` ↔ `ResolveStartPose`).
- [ ] Riglenmiş bir silah oyunda aynı duruyor: parmaklar, elin kumanda üstündeki yeri, iki elli çözüm.
- [ ] `WeaponCatalog` ve `NetItemCatalog` içeriği değişmedi.
- [ ] Kopyalama menüsü hâlâ silahtan silaha kopyalıyor.
- [ ] Silah kaydedilince konsolda silah kiti satırı var; silah olmayan eşyada **yok**, yerine katalog
      satırı var.
- [ ] Menüde eski `Weapons` alt menüsü yok, stüdyo `Items` altında.

**Bomba (F1'in asıl amacı):**

- [ ] Bomba prefabı stüdyoda açılıyor (tanım `prefab` alanından ters aramayla bulunuyor, pencerede
      hangi tanıma yazılacağı görünüyor), ana kabza sağ/sol yazılıyor, oyunda parmaklar bombayı
      kavrıyor.

**Slot defteri kimlik testi (F2) — üçünde de `itemL/itemR/gripFlags` eskisiyle birebir aynı:**

- [ ] Çift tabanca: iki ayrı örnek → `GRIP_LINKED` **yok**, iki slot da dolu.
- [ ] İki elli tüfek (ön kabza bağlı): `GRIP_LINKED` **var**, `PRIMARY_RIGHT` ana ele göre doğru.
- [ ] Silah + bomba: bomba eli silahı devralır, `GRIP_LINKED` yok; atış sonrası **aynı silah** aynı
      ele döner.
- [ ] Kumandası çözülemeyen el (editörde): tel eli **sağ** sayar (eşya kaybolmaz), poz o eli
      **kilitlemez** (idle kalır).
- [ ] Uzak başlıkta iki elin de eşyası ve parmakları doğru.

**Üç eksenin bozulmama testi (F3) — mevcut içerik tek satır iş görmeden aynı davranmalı:**

- [ ] 13 silah hâlâ **uzaktan** ışınla alınıyor; nişan ışını ve retikül aynı (hepsi
      `DistanceGrab / PerViewerClone / Return` varsayılanını okuyor).
- [ ] `Build Readiness`'teki **"Eşya alma yolu ↔ prefab"** satırı temiz.

## 2. İçerik kurulumu (kullanıcı)

- [ ] Hamburgerci'nin dört prop tanımının kavrama pozu stüdyoda yazılmalı — yazılmadan obje ele
      gelir ama kumanda anchor'ında durur.

İlk dünya propları (Hamburgerci'nin bıçak/spatula/tahta/malzemeleri) ve spawn kataloğu **kuruldu**;
yeni prop eklemenin reçetesi `Yemek-Kitabi` 11.5'tedir.

## 3. Kabul ölçütü (kullanıcı koşar)

- [ ] `ProximitySocket` eşyaya uzaktan nişan alınınca **hiçbir şey olmuyor**: retikül çıkmıyor,
      basış yenmiyor (⚠️ ISDK kuyruk tuzağı — "alınamaz" bileşen yokluğuyla ifade edilir).
- [ ] El gizmoya girince gösterge beliriyor, kavrama basılınca **o objenin kendisi** ele geliyor
      (sahnede yerinde bir kopya kalmıyor).
- [ ] Objeyi bir oyuncu tutarken ikinci oyuncu gizmoya elini soksa da alamıyor; sahibi bırakınca
      alabiliyor.
- [ ] Bıçak fırlatılınca **iki başlıkta da aynı yerde**: poz sahibinden akıyor, karşı taraf simüle
      etmiyor.
- [ ] Uzak oyuncunun elinde obje **bir tane** görünüyor (avatar ikinci kopya üretmiyor) ve parmaklar
      stüdyoda yazılmış kavramayı alıyor.
- [ ] `Return` eşya bırakılınca yerine dönüyor, `Physics` eşya düştüğü yerde kalıyor.

## 4. Karar bekleyen tek şey

- **Fırlatma hızının ayarı.** Bugün kumandanın ham hızı ölçek/tavan olmadan uygulanıyor. Propların
  fırlatılması sahada sert/yavaş gelirse ölçek ve tavan alanları eşya tanımına girer — ⚠️ ama
  **ölçülmeden alan açılmaz**: uydurulmuş bir tavan, gerçek şikâyet geldiğinde zaten değişecek
  ikinci bir sayıdır.
