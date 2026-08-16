---
title: Yüzey çarpma efektleri
---

# Yüzey çarpma efektleri (kar / tahta / metal / beton)

Mermi neye çarptıysa **o yüzeyin** parçacığı ve sesi çıkar: kar duvarda kar püskürür, tahtada
talaş, metalde kıvılcım. Bugün efekt yüzeye değil **silaha** bağlı (`Weapon.hitEffectPrefab`,
`WPN_*` prefablarının hepsinde aynı `FX_HitSpark`) ve atış başına `Instantiate`/`Destroy`
yapıyor.

## Verilmiş kararlar

**Yüzey kimliği materyalden gelir, tag/layer'dan DEĞİL.** Gerekçe:

- **Tag:** düz global liste, obje başına tek tag, iki materyalli duvarı ayıramaz. Projede hiçbir
  mantık tag okumuyor (`TagManager`'daki `TeamRed`/`TeamBlue` kullanılmıyor) — bu yolu açmak yeni
  bir sözleşme türü doğurur.
- **Layer:** 32 tane var, üçü dolu (`ArenaRoof`, `LocalBody`, `Obstacle`). Layer fizik filtresi
  kaynağıdır; malzeme çeşidine harcanırsa gerçek bir fizik ihtiyacı doğduğunda yer kalmaz.
- **`PhysicMaterial`:** free-roam'da çarpışma yok, alan boş duruyor. Çalışır ama kimsenin
  bakmayacağı bir yerde yaşar.
- **Materyal:** hazır environment paketlerinde yüzlerce renderer, düzinelerce materyal var.
  Materyali bir kez eşlemek tüm arenayı kapsar ve sahnede elle iş doğurmaz.

**Çözüm iki kademelidir** (ilk eşleşen kazanır):

1. `collider.GetComponentInParent<SurfaceTag>()` — açık override. `RemoteHitBox` ile aynı şekil:
   collider herhangi bir çocukta olabilir, yukarı aranır. İstisnalar içindir (çok materyalli mesh,
   materyali paylaşan ama farklı davranması gereken obje).
2. `Renderer.sharedMaterial` → `SurfaceLibrary` sözlüğü. Ana yol.
3. Hiçbiri değilse `default` yüzey (toz/kıvılcım) — sessizce hiçbir şey çıkmaması "efekt bozuk"
   diye okunur, jenerik bir efekt okunmaz.

**Çok materyalli mesh DESTEKLENMEZ.** Hangi submesh'e vurulduğunu bilmek `hit.triangleIndex`
ister; o da mesh'in `Read/Write Enabled` olmasını (bellek iki katı) ve collider'ın `MeshCollider`
olmasını şart koşar. Karşılığı bu bedele değmez — o objeyi ikiye bölmek ya da `SurfaceTag`
koymak yeterlidir.

**Prefab HAVUZLANIR.** `HitMarker`'ın prefab yolunun aynısı: yüzey başına N örnek, `SetActive` +
parçacıkları baştan oynatma. Atış başına `Instantiate`/`Destroy` Quest'te doğrudan GC dikenidir
(600 RPM × oyuncu sayısı). Havuz dolunca en eski örnek geri alınır.

**Protokole hiçbir şey EKLENMEZ.** Uzak taraf çarpma noktasını zaten türetebiliyor:
`RemoteShotFx.DrawTracer` `origin + worldDir * distanceMeters` hesaplıyor (§6.4'teki atış olayı
yön + mesafe taşıyor). Uzak istemci aynı ışını yerel atar, `hit.normal` ve yüzeyi kendi çözer —
bombanın balistiğini alıcıların yerel simüle etmesiyle aynı mantık. Yeni bir "çarpma" mesajı
ikinci bir doğruluk kaynağı olurdu.

## Yapılacaklar

- [ ] `SurfaceDefinition` (SO): `id`, çarpma prefabı, çarpma sesi (+ ses seviyesi/pitch bandı),
      eşlendiği materyal listesi. İleride mermi deliği alanı buraya eklenir.
- [ ] `SurfaceLibrary` (SO, `_Shared/Data/Resources/SurfaceLibrary.asset`): tanım listesi +
      `default` tanım + materyal→tanım sözlüğü (ilk sorguda kurulur). `WeaponCatalog` ile aynı
      `Resources.Load` gerekçesi — tüketici kendini önyükleyen bir tekil, bağlanacak alanı yok.
- [ ] `SurfaceTag` (MonoBehaviour): tek alan, bir `SurfaceDefinition`. Çalışma anı davranışı yok.
- [ ] `SurfaceImpactFx`: kendini önyükleyen DDOL tekil, yüzey başına havuz
      (`HitMarker`'ın prefab yolu kopyalanabilir). `Play(in RaycastHit hit)` →
      yüzeyi çöz, havuzdan örnek al, `hit.point + hit.normal * ε` konumuna
      `Quaternion.LookRotation(hit.normal)` ile koy, parçacıkları başlat, sesi çal.
- [ ] `ArenaCombat.ReportImpact(in RaycastHit hit)` — tek kapı. `ReportHit`'in yanına, aynı
      gerekçeyle: yeni bir hasar kaynağı (ok, balta) çarpma efektini bedavaya alsın.
- [ ] `Weapon.Fire`: `hitEffectPrefab` bloğu **silinir**, yerine `ArenaCombat.ReportImpact(hit)`.
      ⚠️ Alan da silinir (`Weapon.cs` serialize alanı + altı `WPN_*` prefabındaki bağ):
      bırakılırsa iki efekt üst üste çıkar ve hangisinin geçerli olduğu belirsizleşir.
      `FX_HitSpark.prefab` `default` yüzeyin prefabı olarak kalır.
- [ ] `RemoteShotFx`: tracer'ın ucunda aynı ışını atıp `SurfaceImpactFx.Play` çağır. Işın
      `distanceMeters + ε` uzunluğunda; isabet yoksa efekt yok (mermi boşluğa gitti).
- [ ] Yüzey seti + prefabları: kar, tahta, metal, beton, toprak, `default`. Oynanan arenaların
      environment materyalleri kütüphaneye eşlenir.
- [ ] Oyuncuya isabet: kütüphaneye `body` tanımı, `RemoteHitBox` taşıyan collider ona eşlenir —
      kan/darbe efekti ayrı bir kod yolu açmadan gelir.

## Tuzaklar

- ⚠️ **Işın maskesizdir** (`TraceShot`, `~0` + trigger'lar elenir): çarpma efekti dekora, sahnedeki
  silaha, kavrama çerçevesine de düşebilir. Materyal eşlemesi olmayan her şey `default`'a düşeceği
  için belirti "her yerde toz çıkıyor" olur — eşleme listesi arena bittiğinde gözden geçirilir.
- ⚠️ **Materyal örneği ≠ materyal asset'i.** Çalışma anında `renderer.material` (tekil) okunursa
  Unity bir KOPYA üretir ve sözlükte hiçbir zaman eşleşmez. Daima `sharedMaterial` /
  `sharedMaterials` okunur.
- ⚠️ **Mermi deliği (decal) bu işin parçası değil, ayrı bir adım.** URP Decal Renderer Feature
  Quest'te tam ekran bir geçiş ekler; yapılacaksa havuzlu quad + sert bir tavan (en eski delik
  geri alınır) ile yapılır.

## Doğrulama

- [ ] Kar duvara, tahtaya ve metale sıkılınca üç ayrı efekt + üç ayrı ses çıkıyor.
- [ ] Materyali eşlenmemiş bir yüzey `default` efekti veriyor (hiçbir şey çıkmaması DEĞİL).
- [ ] `SurfaceTag` konmuş bir obje, materyali başka bir yüzeye eşli olsa da tag'i kazanıyor.
- [ ] İki oyuncu: A'nın duvara sıktığı efekti B de görüyor ve aynı yüzeyden çıkıyor.
- [ ] Tam otomatik ateşte (600 RPM) profiler'da atış başına ayırma (GC Alloc) yok.
- [ ] Uzak atışta ışın maliyeti kabul edilebilir (~53 atış/sn → 53 raycast/sn).
