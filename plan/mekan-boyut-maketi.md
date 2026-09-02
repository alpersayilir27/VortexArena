# Mekan boyut maketi — kalan iş

Boyut dosyaları, prefablar, sahneler ve doküman yerinde; kalıcı bilgi `Docs/Sistem-Ozeti.md`
(§4 bileşen sözlüğü, §7 tuzaklar) ·
`Docs/Gelistirici/{Yemek-Kitabi,Sahne-Kurulumu,API-Referansi,Yapma-Listesi}.md` ·
`Docs/Isletme-Kurulum.md` içinde. Kalan: maketten JSON'a geri yazım dosyayı bozuyor ve editör
metinleri muhafazayı taşımaya çağırıyor.

## 1. `DimensionMeshReader` ("DimensionMesh'i JSON'a Çevir") — gidiş-dönüş dosyayı bozmamalı

Elle yazılmış bir dosya, maket hiç oynatılmadan geri çevrilince her satırı değişiyor; "yalnız
oynattığım değer değişsin" hedefi için üç düzeltme:

- **Sayı biçimi:** `ArenaDimensions.ToJson` → `JsonUtility.ToJson` float'ı 17 basamak basar
  (`3.17` → `3.1699981689453127`). Geri yazım yolunda mm'ye yuvarla ve sabit biçimde yaz
  (`0.###`); okuma `JsonUtility`'de kalır (`ArenaDimensions.Parse`). Yazıcı Newtonsoft
  (`com.unity.nuget.newtonsoft-json` kurulu) ya da küçük bir elle yazıcı olabilir.
- **Kolon yüksekliği:** dosyadaki `0` (= `defaultColumnHeight` kullan) geri yazımda ölçülen
  yüksekliğe (`3.0`) dönüşüyor. Ölçülen yükseklik `ArenaDimensionMesh.DefaultColumnHeight`'a mm
  toleransıyla eşitse `0` yazılmalı.
- **Köşe sırası:** `WalkRing` sözlük sırasına göre en küçük köşeden başlıyor, yönü rastgele.
  Kaynak halkanın yönüne (`Polygon2D.SignedArea` işareti) ve ilk noktasına normalize edilmeli ki
  dokunulmayan kolonun satırları yerinde kalsın.
- Bittiğinde `Docs/Gelistirici/Yemek-Kitabi.md` "gidiş-dönüş" cümlesi gerçek davranışı anlatmalı
  (mm'ye yuvarlanır, `0` yükseklik ve köşe sırası korunur).

## 2. Muhafazayı taşımaya çağıran metinler

`DimensionMeshBuilder` (sınıf özeti, pencere HelpBox'ı ve "Sahnede ArenaBoundary yok" uyarısı)
arenayı environment'a oturtmak için `VA_ArenaBoundary`'yi taşımayı/döndürmeyi öneriyor. Bu
`ArenaSpace` sözleşmesine aykırıdır (arena uzayı = dünya uzayı; sahneyi kaydırmak herkesin ağ
konumunu kaydırır) ve dünya uzayı üzerinden geri okumada hassasiyet kaybettirir. Metin tersine
çevrilecek: **environment arenaya taşınır, muhafaza orijinde kalır** (`TemplateBasicsLoader`
raporu zaten böyle söylüyor).

## 3. Sağlık raporuna bekçi

`Configure All Build Elements` sağlık raporu, muhafazası orijinde/dönüşsüz/ölçek 1 olmayan sahneyi
UYARI ile yazmalı — şu an sessizce geçiyor.
