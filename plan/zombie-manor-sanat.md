# Zombi konağı — 3 katlı arena sanatı (Blender)

Zombi modu için perili konak haritası: üç kat, her katta iç + dış oyun alanı. Bu iş **yalnız
sanat ve modellemedir**; zombi modu kodu (`zombi-modu.md`) ve Unity sahne kurulumu ayrı iştir.
Kaynak `blender/ZombieManor/` (yeniden üretim: proje README'si). Her part kullanıcı onayıyla
kapanır, onaysız sonraki parta geçilmez.

## Yerleşim kuralları (bütün partlar bunlara uyar)

- Ölçü kaynağı hedef mekânın `_dimensions.json`'udur (betikte `zm_common.DIMENSIONS_JSON`); duvar
  ve korkuluklar fiziksel sınırın üstünde durur, iç yüzleri sınıra oturur.
- Kat zeminleri 0 / 3,5 / 7,0 m; görsel tavan zeminden 3,3 m. Kat geçişi yalnız kat portalıyla
  (`Yemek-Kitabi.md` "Çok katlı arena") — görünen merdiven yürünebilir gibi durmaz.
- Alan güneyden kuzeye üç şerittir; ayrım çizgileri `zm_common.ZONE_Z`:
  - **A** — Kat0 avlu (açık hava) · Kat1 ve Kat2 boşluk.
  - **B** — Kat0 giriş holü · Kat1 **damalı balkon** (açık hava) · Kat2 boşluk.
  - **C** — Kat0 odalar · Kat1 odalar · Kat2 **çatı terası** (açık hava).
- Fiziksel kolonlar her katta görünür bir sanat öğesidir: orta kolonlardan biri şömine/baca
  hattı (C), diğeri hol direği ve balkonda heykel kaidesi (B), güney duvarındaki avlu kapısı
  ayağıdır.
- Portal diskleri (r 0,25 m) eski tip kafes asansörün ortasında durur, iki portalın X/Z arası
  ≥ 2 m; yerleri `zm_common.PORTALS`.
- Oyun alanı içindeki her katı geometri `Obstacle` adayıdır: ara duvar az, geçitler ≥ 1,2 m.
  Kanatlar, çatılar, ağaçlar, sokak ve araç sınırın **dışında** kalır.

## Kaynak kuralı

Dokular ve hazır modeller yalnız CC0 (Poly Haven, ambientCG); kaynak listesi repoda durur. Projedeki
CS haritalarından çıkarılmış dokular lisanssızdır, kullanılmaz.

Bütçe: toplam ≤ 300k üçgen (bağlı dekor kopyaları dahil).

## Sahnede kalanlar

Sahne kurulu (`Scenes/<SahneAdı>/`): şablon, iki kat portalı, `Obstacle` hacimleri, kat plakası
collider'ları, `ArenaRoof`, dekor prefabları, pratik ışıklar, ışık/yansıma probları, sahnenin
`.lighting` dosyası (Shadowmask), ışık bake'i, her katta `zombie_spawn` noktaları, bütün katları
kaplayan collider kaynaklı NavMesh. Kalanlar:

- ⚠️ **Boşluk bölgesi mekanizması yok:** üst kattaki oyuncu boşluk şeridine (Kat1 A, Kat2 A+B)
  fiziksel olarak yürüyebilir ve havada kalır. `Obstacle` hacmi çözüm değildir — atış ışını
  maskesizdir, balkondan/terastan avluya ateşi keser. Atışı kesmeyen bir "boşluk ihlali" yolu
  gerekiyor; Blender bu alanları `VOID_*` işaret mesh'i olarak verir.
