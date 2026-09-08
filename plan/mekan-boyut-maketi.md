# Mekan boyut maketi — kalan iş

Boyut dosyaları, prefablar, sahneler ve doküman yerinde; kalıcı bilgi `Docs/Sistem-Ozeti.md`
(§4 bileşen sözlüğü, §7 tuzaklar) ·
`Docs/Gelistirici/{Yemek-Kitabi,Sahne-Kurulumu,API-Referansi,Yapma-Listesi}.md` ·
`Docs/Isletme-Kurulum.md` içinde. Kalan: editör metinleri muhafazayı taşımaya çağırıyor · sağlık
raporunda orijin bekçisi · geri yazımda dosya biçiminin korunması · kopya boyut dosyası.

## 1. Muhafazayı taşımaya çağıran metinler

`DimensionMeshBuilder` (sınıf özeti, pencere HelpBox'ı ve "Sahnede ArenaBoundary yok" uyarısı)
arenayı environment'a oturtmak için `VA_ArenaBoundary`'yi taşımayı/döndürmeyi öneriyor. Bu
`ArenaSpace` sözleşmesine aykırıdır (arena uzayı = dünya uzayı; sahneyi kaydırmak herkesin ağ
konumunu kaydırır) ve dünya uzayı üzerinden geri okumada hassasiyet kaybettirir. Metin tersine
çevrilecek: **environment arenaya taşınır, muhafaza orijinde kalır** (`TemplateBasicsLoader`
raporu zaten böyle söylüyor).

## 2. Sağlık raporuna bekçi

`Configure All Build Elements` sağlık raporu, muhafazası orijinde/dönüşsüz/ölçek 1 olmayan sahneyi
UYARI ile yazmalı — şu an sessizce geçiyor.

## 3. Geri yazımda dosya biçimi korunur (`ArenaDimensions.ToJson`)

Ölçüm aracının ürettiği dosyada her nokta **üç satırdır** (`{` · `"x": …` · `"y": …` · `}`) ve
`calibration.a/b` aynı biçimde açıktır; `ToJson` noktayı tek satıra (`{ "x": …, "y": … }`)
indirir. Değerler aynı kalsa da her `DimensionMesh'i JSON'a Çevir` git'te dosyanın tamamını
değişmiş gösterir — "dokunulmamış maketi çevirmek dosyayı değiştirmez" sözü bu yüzden tutmaz.

- [ ] **Yazıcı kaynağın nokta biçimini aynalar.** `DimensionMeshReader.Write` kaynak metni zaten
      elinde tutuyor (`target.SourceJson.text`): `"x":` değeri tek başına bir satırdaysa
      (`^\s*"x":\s*-?[0-9.]+,?\s*$`, çok satırlı eşleme) **açık biçim**, değilse bugünkü tek satır.
      `ToJson(bool pretty, bool expandedPoints)`: `Point()` ve `calibration` yazımı bu bayrağa göre
      dallanır; girinti ölçüm aracıyla birebir (2 boşluk, nokta nesnesinin alanları halka girintisinin
      bir içinde). Kaynak yoksa ya da okunamıyorsa **açık biçim** yazılır — kanonik biçim ölçüm
      aracınınkidir. Sayı yazımı değişmez (`Num`: mm yuvarlama, `-0` yok).
- [ ] Doküman: `Docs/Sistem-Ozeti.md` `DimensionMeshReader` satırındaki "gidiş-dönüşün dosyayı
      bozmaması üç korumaya dayanır" cümlesine dördüncü koruma (nokta biçimi kaynağı aynalar) girer.
- [ ] **Kopya boyut dosyası.** `Venues/<İşletme>/Data/` altında aynı işletme için ikinci bir
      `_dimensions` dosyası duruyor (ölçüm aracının ham çıktısı, ` 1` sonekli). Sahnedeki
      `ArenaDimensionMesh.SourceJson` / `ArenaBoundary` hangisine bağlıysa o kalır, diğeri silinir —
      dosya işletme başına TEKTİR (`Docs/Isletme-Kurulum.md`). Hangisinin bağlı olduğu MCP ile
      okunur, YAML'dan tahmin edilmez.
