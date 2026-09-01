# Ağsal kırılabilir objeler

**Hedef:** hasar alabilen ağ-dışı hiçbir şey kalmasın. Kod tarafı ağ nesnesi modelinin B1 fazına
(`ag-nesne-modeli.md`) oturdu ve yazıldı; kalan iş **içerik** ve **doğrulama**. Kural
`Docs/ArenaNet-Protokol.md` §10.10, reçete `Docs/Gelistirici/Yemek-Kitabi.md` "Ağ nesnesi eklemek".

## Kalan içerik işi

Shader, iki tür asset'i (`breakable_cover` · `target_board`), iki prefab
(`_Shared/World/Prefabs/NO_BreakableCover` · `NO_TargetBoard`), materyaller ve kırılma efekti
(`_Shared/FX/FX_BreakDebris`) kuruldu; lobilerde ve bir oyun arenasında hedef tahtası + siperler
duruyor ve export koşuldu.

- [ ] **Kırılma sesi geçicidir** — bugün ISDK örnek paketinden 0,19 sn'lik bir darbe sesi bağlı
      (`breakClip`). Gerçek bir kırılma/parçalanma klibi `Assets/Audio/` altına girmeli;
      ⚠️ paket örneği bir gün yerinden kalkarsa ses sessizce yok olur, hata vermez.

## Doğrulama (kullanıcı koşar)

- Turnuvada tur başı: kırık objeler tam cana ve sağlam görünüme döner, efekt oynamaz.
- Bomba: patlama yarıçapındaki objeler hasar alır; siperin arkasındaki oyuncu korunurken siperin
  kendisi hasar alır (obje kendi collider'ında gölgelenmez).
- `maxHp > 0` olup collider'ı olmayan obje: sahne kaydında konsola uyarı düşer.
