# Zombi parçalanma kiti — Blender kaynağı

Zombi modunun kopma kapakları (stump) ve et/kemik parçacıkları (gib). Her obje bir Python
betiğiyle sıfırdan kurulur ve FBX olarak doğrudan Unity'ye yazılır. `ZombieGore.blend` çalışma
dosyasıdır, kaynak betiklerdir: elle yapılan bir düzenleme betiğe yazılmadıkça bir sonraki
üretimde kaybolur. Doku ve referans görsel yoktur (`ref/` yok).

## Yerleşim

| Yer | İçerik |
|---|---|
| `scripts/zg_common.py` | Ortak modül: `Flesh`/`Bone` materyalleri, parça kurucular (`stump_flesh` · `bone_nub` · `tube` · `blob` · `shard` · `octa` · `skull_patch`), `bake()` (z-fight denetimi + birleştirme + pivot + kutu UV + gölgeleme + üçgen bütçesi), `export_fbx()`, `save_blend()` |
| `scripts/<id>_<ad>.py` | Obje başına bir betik (`s1_stump_neck` · `s2_stump_limb` · `g1`–`g6` gib'ler). Koşunca objeyi kurar, FBX'ini yazar, `.blend`'i kaydeder |
| `scripts/build_all.py` | Bütün obje betiklerini sırayla koşar |
| `Assets/Modes/Zombie/Art/Gore/<Ad>.fbx` | Çıktı; `blender/` altında kopyası tutulmaz |

## Yeniden üretmek

1. `ZombieGore.blend`'i aç (yoksa boş bir dosya; ilk kayıt onu oluşturur). Betik Blender MCP'den
   (`execute_blender_code`) ya da Text Editor'den koşar:
   `exec(open(r"<repo>\blender\ZombieGore\scripts\build_all.py").read(), {"__name__": "__main__"})`
   — tek obje için aynı satır `<id>_<ad>.py` ile.
2. Sıra bağımsızdır; `build_all.py` sırası: `s1_stump_neck` → `s2_stump_limb` → `g1_gib_flesh_a`
   → `g2_gib_flesh_b` → `g3_gib_flesh_c` → `g4_gib_bone_a` → `g5_gib_skull_a` → `g6_gib_skull_b`.
3. Her betik `ZombieGore` koleksiyonunda objeyi orijinde kurar, FBX'i yazar, objeyi yalnız
   görüntü için X ekseninde yana dizer ve dosyayı kaydeder. ⚠️ Başka bir `.blend` açıkken
   `save_blend()` kaydetmez (`NOT SAVED` yazar) — o sahneyi `ZombieGore.blend`'in üstüne yazmasın diye.
4. Konsolda her obje için üçgen sayısı, slot listesi ve sınırlar yazılır. `Z-FIGHT …` ya da
   `OVER BUDGET …` satırıyla dışa aktarılmış obje bitmiş sayılmaz.

## Kurallar

- **Eksen ve pivot:** dışa aktarma `axis_forward=-Z`, `axis_up=Y`, `bake_space_transform`,
  `apply_unit_scale` + `FBX_SCALE_ALL` (1 birim = 1 m; Unity'de Scale 1, Use File Scale).
  Unity yerel `(x, y, z)` = Blender `(−x, z, −y)`.
  - **Kapaklar (`ZG_Stump_*`):** pivot **kesim düzleminin merkezi** (Blender `z = 0`). Dışa bakan
    yüz (kemik ucunun çıktığı, gövdeden uzak taraf) Blender **+Z** = Unity **+Y**. Etek ve sarkan
    et parçaları −Z'ye (gövdenin içine) iner. Boyun kapağında ön Blender −Y = Unity +Z: omurga
    ucu merkezin arkasında, nefes borusu halkası önde.
  - **Gib'ler (`ZG_Gib_*`):** pivot hacim ağırlık merkezi (`bake(pivot="mass")`).
- **Materyal slotları ada göre eşlenir:** `Flesh` (koyu ıslak kırmızı) ve `Bone` (kırık beyaz).
  Unity kendi materyalini slot adına bağlar; Blender'daki renk yalnız görüntü içindir. Bir obje
  yalnız kullandığı slotları taşır (et gib'lerinde yalnız `Flesh`). Kemik ucunun ilik çukuru ve
  kafatası parçasının iç yüzü `Flesh`'tir.
- **Üçgen bütçesi (`MAX_TRIS`):** boyun kapağı ≤ 300, uzuv kapağı ≤ 200, gib ≤ 80. Et gib'i
  2 bölmeli ikosferdir (tam 80 üçgen): ona ayrıntı eklemek bütçeyi aşar.
- **Ölçüler:** boyun kapağı 11 × 9 cm elips, omurga ucu Ø 2,5 cm; uzuv kapağı yarıçap 5 cm
  (kod eklem collider yarıçapına ölçekler), kemik ucu Ø 1,8 cm, kapaktan ~1,6 cm çıkar.
- **Rastgelelik tohumludur** (`seed`): aynı betik her koşuda aynı modeli üretir. Biçimi
  değiştirmek için tohum ya da parametre betikte değişir.
- ⚠️ **Z-fight yok:** iki parçanın aynı yöne bakan yüzleri aynı düzlemde üst üste binmez —
  Quest'te titrer, Blender'da görünmez. Kemik ucu ve nefes borusu halkası et hacminin içinden
  başlar (yüzle hizalanmaz, içine girer); üst yüzeyleri etin en az `ZF_GAP` (2 mm) üstündedir.
  `bake()` her objede `zfight_pairs()` koşar.
- **Gölgeleme:** bütün yüzler yumuşak, 50°'den keskin kenarlar sert (`set_sharp_from_angle`);
  normaller FBX'e yazılır (`mesh_smooth_type="OFF"`), Unity'de Normals = Import.
- **UV:** kutu izdüşümü, 10 cm = 1 UV birimi (doku zorunlu değil; ayrıntı normal haritası için).
- Önizleme render'ı geçicidir, repoya girmez.
