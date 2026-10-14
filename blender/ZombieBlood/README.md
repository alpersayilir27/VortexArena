# ZombieBlood — zombi kan dokuları

Zombi vuruş ve ölüm efektlerinin dokuları. Hepsi betikle sıfırdan üretilir; `.blend` yalnız
çalışma kopyasıdır, elle düzenleme kalıcı değildir.

## Yeniden üretmek

Blender'da (MCP ya da *Scripting* sekmesi) sırayla:

1. `scripts/b1_spray_sheet.py` → `T_ZB_BloodSpray_4x4.png` — 4×4 flipbook (1024², kare 256²), kameraya
   bakan püskürme parçacığı. Unity'de *Texture Sheet Animation* 4×4 ile okunur; sol üstten başlar.
2. `scripts/b2_droplet_pool.py` → `T_ZB_BloodDrop.png` (tek damla, 128²) ve `T_ZB_BloodPool.png`
   (yerdeki birikinti, üstten, 512²).

Ortak ayarlar (renk, ışık, kamera, PNG yazımı) `scripts/zb_common.py`'dedir. Betikler kendi
`ZB_Render` sahnesini kurar; açık `.blend`'in diğer sahnelerine dokunmaz.

## Çıktı

Doğrudan `Assets/Modes/Zombie/FX/Textures/` altına yazılır; `blender/` altında kopyası tutulmaz.

- Renk dokuda pişmiştir (koyu, doygunluğu düşük kırmızı); Unity'deki parçacık rengi yalnız ince
  ayardır. ⚠️ Rengi parlak kırmızıya çeken bir tint koyma — efektin "cıyak" görünmesinin sebebi buydu.
- Alfa = metaball kaplaması; arka plan şeffaf (`film_transparent`).
