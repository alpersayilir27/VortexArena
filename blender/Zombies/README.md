# Zombies — zombi karakterleri ve animasyonları

Zombi modunun beş gövdesi (`ZB_*`) burada üretilir. Çıktı yalnız `Assets/`'ta durur; bu klasörde
kaynak ve betik yaşar.

## Kaynaklar

Mixamo'dan **elle indirilir**, repoya buraya konur:

| Yer | İçerik |
|---|---|
| `source/characters/` | Karakter FBX'leri (skinli, animasyonsuz), Mixamo'daki adlarıyla |
| `source/anims/` | Animasyon FBX'leri (`<karakter>@<Mixamo klip adı>.fbx`), ham |

İndirme ayarı: karakter için **FBX Binary + T-pose**, animasyon için **FBX Binary, 30 fps,
In Place** (tek istisna *Zombie Stumbling* — öne düştüğü için In Place değildir ve yalnız ölüm
çeşidi olarak kullanılır).

`scripts/zb_build.py` içindeki `CHARACTERS` sözlüğü hangi kaynak dosyanın hangi `ZB_` adına
gittiğini söyler — yeni karakter oraya bir satır olarak eklenir.

## Nasıl yeniden üretilir

Blender'ın Python konsolunda:

```python
exec(open(r"C:\UnityProjects\VortexArena\blender\Zombies\scripts\zb_build.py").read())
```

Tek karakter yetiyorsa önce `ZB_ONLY` kurulur:

```python
ZB_ONLY = "ZB_Girl"
exec(open(r"...\zb_build.py").read())
```

Betik karakter başına: içe alır → mesh'leri birleştirir → `TARGET_TRIS`'e decimate eder → bütün
materyallerin UV'lerini tek atlasa paketler → base color'ı bake eder → tek materyale indirir →
skinli FBX + albedo PNG'yi `Assets/Modes/Zombie/Models/` altına yazar.

⚠️ **Base color EMIT olarak pişirilir, DIFFUSE renk geçişi olarak değil:** Cycles'ın difüz renk
geçişi rengi (1 − metallik) ve speküler ile kısar, Mixamo malzemeleri ikisini de dolu getirir ve
atlas kaynak dokudan üç kata kadar koyu çıkar. Betik her malzemede taban rengini geçici bir
Emission'a bağlar; kontrol: atlasın siyah olmayan piksel ortalaması kaynak dokununkine eşit olmalı.

⚠️ **Mixamo iskeleti (`mixamorig:*`) korunur:** animasyon FBX'leri aynı iskelete bağlıdır ve
Unity'nin Humanoid eşlemesi bu adlardan türer. Kemik adı değişirse beş gövdenin hiçbiri klipleri
oynatamaz.

## Bütçe

| Ölçü | Değer |
|---|---|
| Üçgen | karakter başına `TARGET_TRIS` (8k) — sahnede ~30 zombi |
| Materyal | 1 (atlas) |
| Doku | 1024 × 1024 base color, tek atlas |

Decimate oranı bu bütçeden türer; elle oran verilmez.

## Animasyonlar

Animasyon FBX'leri Blender'dan geçmez — ham dosya **adı değiştirilerek**
`Assets/Modes/Zombie/Animations/` altına kopyalanır. Unity tarafındaki kırpma ve döngü bayrakları
`ZombieLookBuilder`'a aittir (`Tools > VortexArena > Zombie > Zombi görünüşlerini üret`), elle
ayarlanmaz.

| Mixamo klibi | Unity adı |
|---|---|
| Walking | `ZA_Walk_A` |
| Walking (1) | `ZA_Walk_B` |
| Zombie Walk | `ZA_ZombieWalk` |
| Zombie Run | `ZA_Run` |
| Zombie Running | `ZA_Running` |
| Zombie Crawl | `ZA_Crawl` |
| Zombie Idle | `ZA_Idle_A` |
| Zombie Idle (1) | `ZA_Idle_B` |
| Zombie Scratch Idle | `ZA_Idle_Scratch` |
| Zombie Agonizing | `ZA_Agonizing` |
| Zombie Attack | `ZA_Attack_A` |
| Zombie Attack (1) | `ZA_Attack_B` |
| Zombie Punching | `ZA_Punch` |
| Zombie Headbutt | `ZA_Headbutt` |
| Zombie Neck Bite | `ZA_NeckBite` |
| Zombie Reaction Hit | `ZA_HitReact_A` |
| Zombie Reaction Hit (1) | `ZA_HitReact_B` |
| Zombie Scream | `ZA_Scream` |
| Zombie Stand Up | `ZA_StandUp_A` |
| Zombie Stand Up (1) | `ZA_StandUp_B` |
| Zombie Stumbling | `ZA_Stumble` |
| Zombie Death | `ZA_Death` |
| Zombie Transition | `ZA_Transition` |
| Zombie Turn | `ZA_Turn` |

`ZA_Transition` ve `ZA_Turn` denetleyicide kullanılmaz; iskelet eşlemesi bozulursa karşılaştırma
için durur. `ZA_Stumble` da kullanılmaz: 3,5 m yana sendeleyip ancak son karesinde düşer, ceset
erimesine yetişmez — ikinci ölüm çeşidi `ZA_Death`'in aynalanmış hâlidir.

⚠️ **Import ayarı:** animasyon FBX'i Humanoid'dir ve Root Transform'un üçü de (Rotation · Position Y ·
Position XZ) **Bake Into Pose**'dur. Aksi hâlde kök hareketi NavMesh ajanının hareketiyle üst üste
biner ve zombi kendi animasyonuyla yerinden kayar.

## Yeni gövde eklemek

Reçete `Docs/Gelistirici/Yemek-Kitabi.md`'de ("Zombi gövdesi (görünüş) ekleme").
