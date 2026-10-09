# Kavrama yakalama — kalan iş

Kavrama Pozu Stüdyosu Play'de (Quest Link) el izlemeyle yakalar; araç ve akış
`Docs/Gelistirici/Yemek-Kitabi.md` "Bir silahın kavramasını YAZMAK", bileşenler `Docs/Sistem-Ozeti.md`
(Core.Editor ve `VortexArena.Core.GripCapture` satırları), tuzaklar `Docs/Sistem-Ozeti.md` Tuzaklar,
Link ortamı `Docs/Gelistirici/Ortam-Kurulumu.md`. Kod ve doküman **bitti**; kalan yalnız gözlükle
doğrulama.

## Doğrulama (gözlükle, Link üzerinden)

- [ ] Multimodal: bir elde kumanda varken öbür el izleniyor (HUD'daki kumanda/el satırı). Olmazsa:
      iki el izlenir, kayıt parmak sıkıştırmayla — ayrı karar.
- [ ] Canlı hayalet el gerçek elle örtüşüyor; parmak kökleri kaymıyor (işaret/orta/yüzük).
- [ ] Parmaklar eşya yüzeyinde duruyor, içine girmiyor; HUD temas harfleri doğru.
- [ ] Çubuk: sağ/sol yaw, yukarı/aşağı pitch; çubuğa basınca eşya önüne geliyor.
- [ ] X/Y sağ eli, A/B sol eli kaydediyor; titreşim ve donan hayalet doğru slotta.
- [ ] Play'den çıkınca kayıt eşyaya yazılıyor; yalnız 1. kavrama → tek kavramalı + 2. kavrama
      siliniyor; silahta kit yeniden kuruluyor.
- [ ] Oyunda yakalanan kavrama: eşya kumanda yönünde, el yakalandığı gibi sarılı; hayalet kumandanın
      doğal durduğu kayıtta sanal bilek gerçek bilekle uyumlu.
- [ ] Pencereden aynalama (Play'siz) karşı eli doğru yazıyor.
