# Mekan boyut maketi — kalan iş

Boyut dosyaları, prefablar, sahneler ve doküman yerinde; kalıcı bilgi `Docs/Sistem-Ozeti.md`
(§4 bileşen sözlüğü, §7 tuzaklar) ·
`Docs/Gelistirici/{Yemek-Kitabi,Sahne-Kurulumu,API-Referansi,Yapma-Listesi}.md` ·
`Docs/Isletme-Kurulum.md` içinde. Kalan: editör metinleri muhafazayı taşımaya çağırıyor.

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
