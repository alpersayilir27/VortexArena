# El kavrama sistemini eşya geneline açmak — KALAN İŞLER

> **F1 (stüdyo kapısı), F2 (el başına slot defteri) ve F3 (dünya objesi propları) yazıldı**; kalıcı
> bilgi dokümanda: stüdyo ve slot defteri `Docs/Sistem-Ozeti.md` §4 · üç eksen, `GripSocket` ve
> `NetObjectGrabBridge` aynı sözlükte · reçeteler `Docs/Gelistirici/Yemek-Kitabi.md` ·
> imzalar `API-Referansi.md` · yasaklar `Yapma-Listesi.md`. Kavrama telde yok, ama F3 ağ nesnesi
> sahipliğine dayanıyor (protokol **v18**, `Docs/ArenaNet-Protokol.md` §10.10).
> Bu dosya yalnız **yapılmamış** olanı tutar; hepsi bitince silinir.

## 1. Doğrulama (kullanıcı koşar)

- [ ] `Build Readiness`'teki **"Eşya alma yolu ↔ prefab"** satırı temiz.
- [ ] Çift tabanca: iki ayrı örnek → `GRIP_LINKED` **yok**, iki slot da dolu. ⚠️ Projede ikinci bir
      tabanca yok; test silah kararını bekliyor.
- [ ] `Return` eşya bırakılınca yerine dönüyor, `Physics` eşya düştüğü yerde kalıyor.

## 2. İçerik kurulumu (kullanıcı)

- [ ] Hamburgerci'nin dört prop tanımının kavrama pozu stüdyoda yazılmalı — yazılmadan obje ele
      gelir ama kumanda anchor'ında durur.
- [ ] İkinci tabanca kararı (çift tabanca testi buna bağlı).

Yeni prop eklemenin reçetesi `Yemek-Kitabi` 11.5'tedir.
