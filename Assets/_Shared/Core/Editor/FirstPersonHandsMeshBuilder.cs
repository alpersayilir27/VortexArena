using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VortexArena.Core.Player;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Avatars &gt; Build First-Person Hands Mesh</c> — gövde avatarının
    /// tek parça skinned meshinden <b>yalnız el geometrisini</b> keser, ayrı bir mesh asset'i
    /// olarak yazar ve prefabdaki <c>FirstPersonHands</c> renderer'ına bağlar.
    /// <para>
    /// <b>Neden ayrı bir mesh:</b> oyuncu gözlükte kendi kollarını/gövdesini görmez, yalnız
    /// ellerini görür. Gövde renderer'ı kapalı sürülür ve onun yerine bu ikinci renderer çizilir —
    /// yani oyuncunun gördüğü el, başkalarının gördüğü elin ta kendisidir (aynı iskelet, aynı
    /// materyal, aynı kare).
    /// </para>
    /// <para>
    /// <b>Kesim yalnız ÇİZİMDEDİR:</b> yeni mesh kaynağın <c>bindposes</c> dizisini olduğu gibi
    /// taşır, dolayısıyla kaynağın <c>bones</c> dizisini ve <c>rootBone</c>'unu aynen kullanır.
    /// Kemiklere dokunulmaz, telde giden gövde değişmez.
    /// </para>
    /// <para>
    /// <b>Idempotent:</b> mesh asset'i varsa yerinde temizlenip yeniden doldurulur (GUID korunur,
    /// prefabdaki referans kopmaz), <c>FirstPersonHands</c> çocuğu varsa yeniden kullanılır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Yalnız editörde çalışır ve çalışmak zorundadır:</b> kaynak mesh
    /// <c>isReadable = false</c>; vertex verisi yalnız editörün tuttuğu kaynak veriden okunabiliyor.
    /// Aynı kesimi çalışma anında yapmaya çalışan bir kod boş mesh üretir.
    /// </para>
    /// <para>
    /// <b>Dialog YOK:</b> modal dialog Unity ana thread'ini kilitliyor ve CLI'dan çalıştırınca
    /// komut timeout veriyor. Tüm çıktı konsola yazılır.
    /// </para>
    /// </summary>
    public static class FirstPersonHandsMeshBuilder
    {
        // ------------------------------------------------------------ sabitler

        private const string PrefabPath = "Assets/_Shared/Avatars/Resources/LocalBodyAvatar.prefab";

        /// <summary>Üretilen mesh asset'inin klasörü (prefabın bir üstü — <c>Resources/</c> altına
        /// konmaz: mesh koddan yüklenmiyor, prefab referansıyla geliyor).</summary>
        private const string MeshDir = "Assets/_Shared/Avatars";

        private const string MeshSuffix = "_FirstPersonHands.mesh";

        /// <summary>Prefabdaki el renderer'ının obje adı — kaynak renderer'ı ararken de bu ad
        /// dışlanır (araç kendi ürettiğini kaynak sanmasın).</summary>
        private const string HandsChildName = "FirstPersonHands";

        /// <summary>Bir vertexin "el" sayılması için el kemiklerine düşmesi gereken toplam ağırlık.
        /// 0.5 = vertex ağırlıklı çoğunlukla ele bağlı; bilekte temiz bir kesim verir.</summary>
        private const float HandWeightThreshold = 0.5f;

        /// <summary>El kemiği ağacının köklerini ADIYLA bulur. Parmak kemikleri
        /// <c>…HandThumb1</c> gibi biterek bu iki ada takılmaz; kalanı ata zincirinden çözülür.
        /// İndeks aralığı kullanılmaz — kemik sırası dosyadan dosyaya değişir.</summary>
        private static readonly string[] HandRootSuffixes = { "LeftHand", "RightHand" };

        // -------------------------------------------------------------- giriş

        [MenuItem("Tools/VortexArena/Avatars/Build First-Person Hands Mesh", false, 30)]
        private static void Build()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[FirstPersonHands] Prefab bulunamadı: {PrefabPath}");
                return;
            }

            try
            {
                BuildInto(root);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void BuildInto(GameObject root)
        {
            // ------------------------------------------------------ kaynak renderer
            SkinnedMeshRenderer source = ResolveSourceRenderer(root);
            if (source == null)
            {
                return;
            }

            Mesh sourceMesh = source.sharedMesh;
            if (sourceMesh == null)
            {
                Debug.LogError("[FirstPersonHands] Kaynak renderer'da mesh yok.");
                return;
            }

            Transform[] bones = source.bones;
            bool[] isHandBone = ResolveHandBones(bones);
            if (isHandBone == null)
            {
                return;
            }

            // -------------------------------------------------------- vertex seçimi
            Vector3[] srcVertices = sourceMesh.vertices;
            BoneWeight[] srcWeights = sourceMesh.boneWeights;
            if (srcVertices.Length == 0 || srcWeights.Length != srcVertices.Length)
            {
                Debug.LogError(
                    "[FirstPersonHands] Kaynak meshin vertex/ağırlık verisi okunamadı — model " +
                    "içe aktarımında 'Read/Write' ya da skin verisi eksik olabilir.");
                return;
            }

            var keep = new bool[srcVertices.Length];
            for (int i = 0; i < srcVertices.Length; i++)
            {
                keep[i] = HandWeight(srcWeights[i], isHandBone) >= HandWeightThreshold;
            }

            // ------------------------------------------------------------ diziler
            Vector3[] srcNormals = sourceMesh.normals;
            Vector4[] srcTangents = sourceMesh.tangents;
            Vector2[] srcUv = sourceMesh.uv;
            Vector2[] srcUv2 = sourceMesh.uv2;
            Color[] srcColors = sourceMesh.colors;

            bool hasNormals = srcNormals != null && srcNormals.Length == srcVertices.Length;
            bool hasTangents = srcTangents != null && srcTangents.Length == srcVertices.Length;
            bool hasUv = srcUv != null && srcUv.Length == srcVertices.Length;
            bool hasUv2 = srcUv2 != null && srcUv2.Length == srcVertices.Length;
            bool hasColors = srcColors != null && srcColors.Length == srcVertices.Length;

            // Eski indeks → yeni indeks. Yalnız KALAN üçgenlerin kullandığı vertexler yazılır;
            // eşik geçen ama üçgeni düşen vertexler mesh'e hiç girmez.
            var remap = new int[srcVertices.Length];
            for (int i = 0; i < remap.Length; i++)
            {
                remap[i] = -1;
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv = new List<Vector2>();
            var uv2 = new List<Vector2>();
            var colors = new List<Color>();
            var weights = new List<BoneWeight>();

            // ------------------------------------------------- üçgen seçimi + kapak
            var submeshTriangles = new List<List<int>>();
            var submeshMaterials = new List<Material>();
            Material[] srcMaterials = source.sharedMaterials;

            int keptTriangleCount = 0;
            int cappedRingCount = 0;

            for (int sub = 0; sub < sourceMesh.subMeshCount; sub++)
            {
                int[] srcTris = sourceMesh.GetTriangles(sub);

                // Kaynak indekslerle çalışan ara liste: sınır kenarı analizi eski numaralar
                // üzerinden yapılır, sıkıştırma en sonda.
                var kept = new List<int>();
                for (int t = 0; t + 2 < srcTris.Length; t += 3)
                {
                    int a = srcTris[t];
                    int b = srcTris[t + 1];
                    int c = srcTris[t + 2];
                    if (keep[a] && keep[b] && keep[c])
                    {
                        kept.Add(a);
                        kept.Add(b);
                        kept.Add(c);
                    }
                }

                if (kept.Count == 0)
                {
                    // ⚠️ Boş submesh yazılmaz: materyal listesi kalan submeshlerle aynı SIRADA
                    // kurulur, boş bir kanal materyal indekslerini kaydırırdı.
                    continue;
                }

                keptTriangleCount += kept.Count / 3;

                // Yeni numaraları burada üret (submesh sırasıyla, ilk görüldüğü yerde).
                var tris = new List<int>(kept.Count);
                for (int i = 0; i < kept.Count; i++)
                {
                    tris.Add(MapVertex(
                        kept[i], remap, srcVertices, srcNormals, srcTangents, srcUv, srcUv2,
                        srcColors, srcWeights, hasNormals, hasTangents, hasUv, hasUv2, hasColors,
                        vertices, normals, tangents, uv, uv2, colors, weights));
                }

                cappedRingCount += CapCutHoles(
                    srcTris, kept, remap, srcVertices, hasNormals ? srcNormals : null,
                    hasTangents ? srcTangents : null, hasUv ? srcUv : null,
                    hasUv2 ? srcUv2 : null, hasColors ? srcColors : null, srcWeights,
                    vertices, normals, tangents, uv, uv2, colors, weights, tris,
                    hasNormals, hasTangents, hasUv, hasUv2, hasColors);

                submeshTriangles.Add(tris);
                submeshMaterials.Add(sub < srcMaterials.Length ? srcMaterials[sub] : null);
            }

            if (submeshTriangles.Count == 0)
            {
                Debug.LogError(
                    $"[FirstPersonHands] Eşik {HandWeightThreshold:0.##} ile hiç üçgen kalmadı — " +
                    "el kemiği adları ya da ağırlık eşiği gözden geçirilmeli.");
                return;
            }

            // ------------------------------------------------------------- mesh
            string meshName = sourceMesh.name + "_FirstPersonHands";
            string meshPath = MeshDir + "/" + sourceMesh.name + MeshSuffix;

            // ⚠️ Var olan asset SİLİNMEZ, içi temizlenip yeniden doldurulur: silip yeniden
            // yaratmak GUID'i değiştirir ve prefabdaki referans sessizce kopar.
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            bool isNewAsset = mesh == null;
            if (isNewAsset)
            {
                mesh = new Mesh();
            }
            else
            {
                mesh.Clear();
            }

            mesh.name = meshName;
            mesh.indexFormat = vertices.Count < 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            if (hasNormals)
            {
                mesh.SetNormals(normals);
            }

            if (hasTangents)
            {
                mesh.SetTangents(tangents);
            }

            if (hasUv)
            {
                mesh.SetUVs(0, uv);
            }

            if (hasUv2)
            {
                mesh.SetUVs(1, uv2);
            }

            if (hasColors)
            {
                mesh.SetColors(colors);
            }

            // ⚠️ Kemik indeksleri REMAP EDİLMEZ: ağırlıklar olduğu gibi, bindposes ise kaynağın
            // TAM dizisi olarak yazılır. Böylece yeni renderer kaynağın 'bones' dizisini ve
            // 'rootBone'unu aynen kullanabilir. Kullanılmayan kemikleri ayıklamak birkaç bindpose
            // kazandırır ama iki renderer'ın kemik dizisini birbirinden ayırır — o andan sonra
            // iskelette yapılan her değişiklik iki yerde tutulmak zorunda kalırdı.
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = sourceMesh.bindposes;

            mesh.subMeshCount = submeshTriangles.Count;
            for (int sub = 0; sub < submeshTriangles.Count; sub++)
            {
                mesh.SetTriangles(submeshTriangles[sub], sub, true);
            }

            mesh.RecalculateBounds();

            if (isNewAsset)
            {
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            // -------------------------------------------------------- prefab bağı
            SkinnedMeshRenderer hands = ResolveHandsRenderer(source);
            hands.sharedMesh = mesh;
            hands.bones = (Transform[])source.bones.Clone();
            hands.rootBone = source.rootBone;
            hands.sharedMaterials = submeshMaterials.ToArray();

            // ⚠️ Prefabda KAPALI durur; açmak LocalBodyAvatar'ın işidir: bu prefab admin'de ve
            // kurulum tamamlanmadan da örneklenebiliyor, açık gelen bir el meshi oralarda
            // havada duran iki el olurdu.
            hands.enabled = false;

            // Gövde çizilmediği için ellerin gölgesi yerde iki kopuk el gölgesi olarak görünürdü.
            hands.shadowCastingMode = ShadowCastingMode.Off;
            hands.receiveShadows = false;

            // Sınırlar bind pozundan türüyor ve el kemikleri gövdeden uzaklaşınca yanlış kalıyor;
            // birinci şahıs meshinin culling yüzünden kaybolması sessiz ve tam bir arıza olurdu.
            hands.updateWhenOffscreen = true;

            // ⚠️ Auto BIRAKILMAZ: Android kalite seviyesinde 'Skin Weights' 2 kemiğe düşüyor ve bu
            // mesh 30 cm'den bakılıyor — bilekte/parmak diplerinde 2 etkili deformasyon gözle
            // görülür. Burada Bone4 bir görsel süs değil doğruluk ayarıdır; tüm meshleri Bone4
            // yapmak bedava olmadığı için sabitleme yalnız yakından bakılana uygulanır.
            hands.quality = SkinQuality.Bone4;

            BindAvatarField(source.transform.root.gameObject, hands);

            PrefabUtility.SaveAsPrefabAsset(source.transform.root.gameObject, PrefabPath);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[FirstPersonHands] '{sourceMesh.name}' → {vertices.Count} vertex / " +
                $"{keptTriangleCount} üçgen ({submeshTriangles.Count} submesh), " +
                $"{cappedRingCount} bilek halkası kapatıldı. Asset: {meshPath}");
        }

        // ------------------------------------------------------------ çözümleme

        /// <summary>
        /// Kaynak gövde renderer'ı: alt ağaçtaki, adı <see cref="HandsChildName"/> OLMAYAN tek
        /// <see cref="SkinnedMeshRenderer"/>. Sıfır ya da birden çok aday varsa hata basılır —
        /// yanlış renderer'dan sessizce el kesmek teşhisi zor bir arıza olurdu.
        /// </summary>
        private static SkinnedMeshRenderer ResolveSourceRenderer(GameObject root)
        {
            SkinnedMeshRenderer[] all = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer found = null;
            int count = 0;

            for (int i = 0; i < all.Length; i++)
            {
                if (string.Equals(all[i].gameObject.name, HandsChildName, StringComparison.Ordinal))
                {
                    continue;
                }

                found = all[i];
                count++;
            }

            if (count == 0)
            {
                Debug.LogError("[FirstPersonHands] Prefabda kaynak SkinnedMeshRenderer bulunamadı.");
                return null;
            }

            if (count > 1)
            {
                Debug.LogError(
                    $"[FirstPersonHands] Prefabda {count} adet kaynak SkinnedMeshRenderer var — " +
                    "hangisinden kesileceği belirsiz, üretim yapılmadı.");
                return null;
            }

            return found;
        }

        /// <summary>
        /// Hangi kemiklerin "el" sayıldığını çözer: adı <c>LeftHand</c>/<c>RightHand</c> ile biten
        /// kemikler KÖKTÜR, kendisi ya da bir ATASI kök olan her kemik el kemiğidir (parmaklar
        /// böyle gelir). Kök bulunamazsa <c>null</c> döner.
        /// </summary>
        private static bool[] ResolveHandBones(Transform[] bones)
        {
            if (bones == null || bones.Length == 0)
            {
                Debug.LogError("[FirstPersonHands] Kaynak renderer'da kemik dizisi boş.");
                return null;
            }

            var roots = new List<Transform>(2);
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && IsHandRootName(bones[i].name))
                {
                    roots.Add(bones[i]);
                }
            }

            if (roots.Count == 0)
            {
                Debug.LogError(
                    "[FirstPersonHands] El kökü kemiği bulunamadı (adı 'LeftHand'/'RightHand' ile " +
                    "biten kemik yok) — iskelet Mixamo adlandırmasında mı?");
                return null;
            }

            var isHand = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i];
                while (bone != null)
                {
                    if (roots.Contains(bone))
                    {
                        isHand[i] = true;
                        break;
                    }

                    bone = bone.parent;
                }
            }

            return isHand;
        }

        private static bool IsHandRootName(string name)
        {
            for (int i = 0; i < HandRootSuffixes.Length; i++)
            {
                if (name.EndsWith(HandRootSuffixes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Bir vertexin el kemiklerine düşen toplam ağırlığı.</summary>
        private static float HandWeight(BoneWeight w, bool[] isHandBone)
        {
            float sum = 0f;
            if (w.weight0 > 0f && InRange(w.boneIndex0, isHandBone) && isHandBone[w.boneIndex0])
            {
                sum += w.weight0;
            }

            if (w.weight1 > 0f && InRange(w.boneIndex1, isHandBone) && isHandBone[w.boneIndex1])
            {
                sum += w.weight1;
            }

            if (w.weight2 > 0f && InRange(w.boneIndex2, isHandBone) && isHandBone[w.boneIndex2])
            {
                sum += w.weight2;
            }

            if (w.weight3 > 0f && InRange(w.boneIndex3, isHandBone) && isHandBone[w.boneIndex3])
            {
                sum += w.weight3;
            }

            return sum;
        }

        private static bool InRange(int index, bool[] table)
        {
            return index >= 0 && index < table.Length;
        }

        // ------------------------------------------------------- vertex sıkıştırma

        /// <summary>
        /// Kaynak vertexi yeni mesh'e (yoksa) ekler ve yeni indeksini döndürür.
        /// </summary>
        private static int MapVertex(
            int src,
            int[] remap,
            Vector3[] srcVertices,
            Vector3[] srcNormals,
            Vector4[] srcTangents,
            Vector2[] srcUv,
            Vector2[] srcUv2,
            Color[] srcColors,
            BoneWeight[] srcWeights,
            bool hasNormals,
            bool hasTangents,
            bool hasUv,
            bool hasUv2,
            bool hasColors,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> uv2,
            List<Color> colors,
            List<BoneWeight> weights)
        {
            int mapped = remap[src];
            if (mapped >= 0)
            {
                return mapped;
            }

            mapped = vertices.Count;
            remap[src] = mapped;

            vertices.Add(srcVertices[src]);
            if (hasNormals)
            {
                normals.Add(srcNormals[src]);
            }

            if (hasTangents)
            {
                tangents.Add(srcTangents[src]);
            }

            if (hasUv)
            {
                uv.Add(srcUv[src]);
            }

            if (hasUv2)
            {
                uv2.Add(srcUv2[src]);
            }

            if (hasColors)
            {
                colors.Add(srcColors[src]);
            }

            weights.Add(srcWeights[src]);
            return mapped;
        }

        // ---------------------------------------------------------- bilek kapağı

        /// <summary>
        /// Kesimin dokunduğu delikleri yelpaze üçgenlerle kapatır ve kapatılan halka sayısını döndürür.
        /// <para>
        /// ⚠️ <b>Kaynağın KENDİ açıklıkları kapatılmaz.</b> Bu mesh su geçirmez değil:
        /// eldiven/kumaş kabukları binlerce açık sınır kenarı taşıyor ve bunlar normal çizimde
        /// üst üste binen kabuklarca örtülüyor. Hepsini kapatmak eldiven boyunca uzanan görünür bir
        /// zar üretirdi.
        /// </para>
        /// <para>
        /// ⚠️ <b>Ölçüt "kenar yeni mi" DEĞİL, "halkaya kesim dokundu mu"dur</b> — ve bu ayrım
        /// pratikte her şeyi belirler: kesim bilek halkalarının yalnız bir YAYINI açıyor, halkanın
        /// geri kalanı kaynakta zaten açıktı. Yalnız yeni kenarlarla çalışmak kapalı halka değil
        /// açık yay verir, yelpaze kurulamaz ve <b>hiçbir delik kapanmaz</b>. Bu yüzden halkalar
        /// sınırın TAMAMINDAN kurulur; bir halka ancak <b>en az bir kenarı kesimden doğmuşsa</b>
        /// kapatılır. Kaynakta baştan sona açık olan halkalar (kabuk dikişleri) hiç dokunulmadan
        /// bırakılır.
        /// </para>
        /// </summary>
        private static int CapCutHoles(
            int[] srcTris,
            List<int> keptTris,
            int[] remap,
            Vector3[] srcVertices,
            Vector3[] srcNormals,
            Vector4[] srcTangents,
            Vector2[] srcUv,
            Vector2[] srcUv2,
            Color[] srcColors,
            BoneWeight[] srcWeights,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> uv2,
            List<Color> colors,
            List<BoneWeight> weights,
            List<int> outTris,
            bool hasNormals,
            bool hasTangents,
            bool hasUv,
            bool hasUv2,
            bool hasColors)
        {
            // Kaynağın kendi sınırı: bir kez kullanılan yönsüz kenarlar.
            var sourceBoundary = new HashSet<long>(CollectBoundary(srcTris, srcTris.Length, null));

            // Kalan parçanın sınırı — kenarın kalan üçgenlerdeki YÖNÜ de saklanır, kapak üçgeninin
            // sarımı ona bağlı.
            var directed = new Dictionary<long, int[]>();
            HashSet<long> cutBoundary = CollectBoundary(null, 0, keptTris, directed);

            // ⚠️ Halkalar sınırın TAMAMINDAN kurulur (yalnız yeni kenarlardan değil) — gerekçe
            // yukarıda: bilek halkalarının bir yayı kaynakta zaten açıktı.
            var edges = new List<int[]>();
            foreach (long key in cutBoundary)
            {
                edges.Add(directed[key]);
            }

            if (edges.Count == 0)
            {
                return 0;
            }

            // ------------------------------------------------- halkalara ayırma
            var adjacency = new Dictionary<int, List<int>>();
            for (int i = 0; i < edges.Count; i++)
            {
                AddAdjacency(adjacency, edges[i][0], edges[i][1]);
                AddAdjacency(adjacency, edges[i][1], edges[i][0]);
            }

            // Vertex → halka kimliği.
            var ringOf = new Dictionary<int, int>();
            int ringCount = 0;
            foreach (int start in adjacency.Keys)
            {
                if (ringOf.ContainsKey(start))
                {
                    continue;
                }

                var stack = new Stack<int>();
                stack.Push(start);
                ringOf[start] = ringCount;
                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    List<int> neighbours = adjacency[current];
                    for (int i = 0; i < neighbours.Count; i++)
                    {
                        if (ringOf.ContainsKey(neighbours[i]))
                        {
                            continue;
                        }

                        ringOf[neighbours[i]] = ringCount;
                        stack.Push(neighbours[i]);
                    }
                }

                ringCount++;
            }

            // Her halkanın vertexleri + sağlamlık kontrolü (dallanma = kapatılamaz).
            var ringVertices = new List<int>[ringCount];
            var ringValid = new bool[ringCount];
            for (int i = 0; i < ringCount; i++)
            {
                ringVertices[i] = new List<int>();
                ringValid[i] = true;
            }

            foreach (KeyValuePair<int, int> pair in ringOf)
            {
                ringVertices[pair.Value].Add(pair.Key);
                if (adjacency[pair.Key].Count != 2)
                {
                    ringValid[pair.Value] = false;
                }
            }

            // Hangi halkaya kesim dokundu: en az bir kenarı kaynağın sınırında OLMAYAN halka.
            var ringTouchedByCut = new bool[ringCount];
            foreach (long key in cutBoundary)
            {
                if (sourceBoundary.Contains(key))
                {
                    continue;
                }

                ringTouchedByCut[ringOf[directed[key][0]]] = true;
            }

            int capped = 0;
            var centerOfRing = new int[ringCount];
            for (int r = 0; r < ringCount; r++)
            {
                if (!ringTouchedByCut[r])
                {
                    // Kaynağın kendi açıklığı (kabuk dikişi) — normal çizimde üst üste binen
                    // kabuklarca örtülüyor, kapatmak görünür bir zar üretirdi. Sessizce geçilir.
                    centerOfRing[r] = -1;
                    continue;
                }

                if (!ringValid[r])
                {
                    // Bozuk üçgen üretmektense delik bırakılır: dallanan bir sınırda "merkez"
                    // tanımsızdır ve yelpaze kendi üstüne katlanırdı.
                    Debug.LogWarning(
                        $"[FirstPersonHands] Kesim sınırında dallanma var ({ringVertices[r].Count} " +
                        "vertex) — bu halka kapatılmadı.");
                    centerOfRing[r] = -1;
                    continue;
                }

                centerOfRing[r] = CreateCenterVertex(
                    ringVertices[r], srcVertices, srcNormals, srcTangents, srcUv, srcUv2, srcColors,
                    srcWeights, vertices, normals, tangents, uv, uv2, colors, weights,
                    hasNormals, hasTangents, hasUv, hasUv2, hasColors);
                capped++;
            }

            // -------------------------------------------------- yelpaze üçgenleri
            for (int i = 0; i < edges.Count; i++)
            {
                int a = edges[i][0];
                int b = edges[i][1];
                int center = centerOfRing[ringOf[a]];
                if (center < 0)
                {
                    continue;
                }

                // ⚠️ Sarım: kalan üçgenlerde sınır a→b yönünde göründüyse kapak (b, a, merkez)
                // olur. Ters yazmak kapağı içeri baktırır ve bilek ağzı delik görünür.
                outTris.Add(remap[b]);
                outTris.Add(remap[a]);
                outTris.Add(center);
            }

            return capped;
        }

        private static void AddAdjacency(Dictionary<int, List<int>> adjacency, int from, int to)
        {
            if (!adjacency.TryGetValue(from, out List<int> list))
            {
                list = new List<int>(2);
                adjacency[from] = list;
            }

            if (!list.Contains(to))
            {
                list.Add(to);
            }
        }

        /// <summary>
        /// Bir üçgen kümesinin sınır kenarları: yalnız BİR kez kullanılan yönsüz kenarlar.
        /// <paramref name="directed"/> verilirse her sınır kenarının o kümedeki yönü de yazılır.
        /// </summary>
        private static HashSet<long> CollectBoundary(
            int[] array,
            int arrayLength,
            List<int> list,
            Dictionary<long, int[]> directed = null)
        {
            var counts = new Dictionary<long, int>();
            var direction = new Dictionary<long, int[]>();

            int total = array != null ? arrayLength : list.Count;
            for (int t = 0; t + 2 < total; t += 3)
            {
                int a = array != null ? array[t] : list[t];
                int b = array != null ? array[t + 1] : list[t + 1];
                int c = array != null ? array[t + 2] : list[t + 2];

                Accumulate(counts, direction, a, b);
                Accumulate(counts, direction, b, c);
                Accumulate(counts, direction, c, a);
            }

            var boundary = new HashSet<long>();
            foreach (KeyValuePair<long, int> pair in counts)
            {
                if (pair.Value != 1)
                {
                    continue;
                }

                boundary.Add(pair.Key);
                if (directed != null)
                {
                    directed[pair.Key] = direction[pair.Key];
                }
            }

            return boundary;
        }

        private static void Accumulate(
            Dictionary<long, int> counts, Dictionary<long, int[]> direction, int a, int b)
        {
            long key = EdgeKey(a, b);
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
            if (count == 0)
            {
                direction[key] = new[] { a, b };
            }
        }

        private static long EdgeKey(int a, int b)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            return ((long)lo << 32) | (uint)hi;
        }

        /// <summary>
        /// Halkanın ortasına yeni bir vertex ekler ve yeni indeksini döndürür.
        /// <para>
        /// ⚠️ <b>Kemik ağırlığı ortalanmaz</b>, halka merkezine EN YAKIN vertexinki kopyalanır:
        /// dört etkili slot farklı kemiklere baktığı için ağırlık ortalaması anlamlı bir skin
        /// üretmez (kapak yanlış kemikle sürüklenir). Aynı sebeple tangent'ın <c>w</c>'si de
        /// ortalanmaz — o bir yön değil işarettir.
        /// </para>
        /// </summary>
        private static int CreateCenterVertex(
            List<int> ring,
            Vector3[] srcVertices,
            Vector3[] srcNormals,
            Vector4[] srcTangents,
            Vector2[] srcUv,
            Vector2[] srcUv2,
            Color[] srcColors,
            BoneWeight[] srcWeights,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector4> tangents,
            List<Vector2> uv,
            List<Vector2> uv2,
            List<Color> colors,
            List<BoneWeight> weights,
            bool hasNormals,
            bool hasTangents,
            bool hasUv,
            bool hasUv2,
            bool hasColors)
        {
            float inverse = 1f / ring.Count;

            Vector3 position = Vector3.zero;
            Vector3 normal = Vector3.zero;
            Vector3 tangent = Vector3.zero;
            Vector2 texcoord = Vector2.zero;
            Vector2 texcoord2 = Vector2.zero;
            Vector4 color = Vector4.zero;

            for (int i = 0; i < ring.Count; i++)
            {
                int v = ring[i];
                position += srcVertices[v];
                if (hasNormals)
                {
                    normal += srcNormals[v];
                }

                if (hasTangents)
                {
                    tangent += (Vector3)srcTangents[v];
                }

                if (hasUv)
                {
                    texcoord += srcUv[v];
                }

                if (hasUv2)
                {
                    texcoord2 += srcUv2[v];
                }

                if (hasColors)
                {
                    Color c = srcColors[v];
                    color += new Vector4(c.r, c.g, c.b, c.a);
                }
            }

            position *= inverse;

            // Merkeze en yakın halka vertexi: ağırlık ve tangent işareti oradan alınır.
            int nearest = ring[0];
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < ring.Count; i++)
            {
                float distance = (srcVertices[ring[i]] - position).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = ring[i];
                }
            }

            int index = vertices.Count;
            vertices.Add(position);

            if (hasNormals)
            {
                normals.Add(normal.sqrMagnitude > 0f ? normal.normalized : srcNormals[nearest]);
            }

            if (hasTangents)
            {
                Vector3 direction = tangent.sqrMagnitude > 0f
                    ? tangent.normalized
                    : (Vector3)srcTangents[nearest];
                tangents.Add(new Vector4(direction.x, direction.y, direction.z, srcTangents[nearest].w));
            }

            if (hasUv)
            {
                uv.Add(texcoord * inverse);
            }

            if (hasUv2)
            {
                uv2.Add(texcoord2 * inverse);
            }

            if (hasColors)
            {
                color *= inverse;
                colors.Add(new Color(color.x, color.y, color.z, color.w));
            }

            weights.Add(srcWeights[nearest]);
            return index;
        }

        // -------------------------------------------------------- prefab tarafı

        /// <summary>
        /// Kaynak renderer'ın EBEVEYNİ altındaki <see cref="HandsChildName"/> objesini bulur/üretir
        /// ve üstündeki renderer'ı döndürür. Transform identity'ye alınır: mesh kaynağın kendi
        /// uzayında ve kaynağın bindposes'uyla yazılıyor, dolu bir yerel dönüşüm eli iki kez
        /// dönüştürürdü.
        /// </summary>
        private static SkinnedMeshRenderer ResolveHandsRenderer(SkinnedMeshRenderer source)
        {
            Transform parent = source.transform.parent;
            Transform child = parent != null ? parent.Find(HandsChildName) : null;
            if (child == null)
            {
                var created = new GameObject(HandsChildName);
                child = created.transform;
                child.SetParent(parent, false);
            }

            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;

            var renderer = child.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null)
            {
                renderer = child.gameObject.AddComponent<SkinnedMeshRenderer>();
            }

            return renderer;
        }

        /// <summary>
        /// Prefab kökündeki <see cref="LocalBodyAvatar"/>'ın <c>firstPersonHands</c> alanına
        /// üretilen renderer'ı yazar. Alan bulunamazsa üretim iptal EDİLMEZ (mesh yine de
        /// işe yarar) ama uyarı basılır — sessiz bir bağ eksikliği "eller görünmüyor" olarak
        /// geri döner.
        /// </summary>
        private static void BindAvatarField(GameObject root, SkinnedMeshRenderer hands)
        {
            var avatar = root.GetComponent<LocalBodyAvatar>();
            if (avatar == null)
            {
                Debug.LogWarning(
                    "[FirstPersonHands] Prefab kökünde LocalBodyAvatar yok — el renderer'ı " +
                    "hiçbir yere bağlanmadı.");
                return;
            }

            var serialized = new SerializedObject(avatar);
            SerializedProperty property = serialized.FindProperty("firstPersonHands");
            if (property == null)
            {
                Debug.LogWarning(
                    "[FirstPersonHands] LocalBodyAvatar'da 'firstPersonHands' alanı bulunamadı — " +
                    "renderer bağlanmadı. Alan eklendikten sonra aracı tekrar çalıştır.");
                return;
            }

            property.objectReferenceValue = hands;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(avatar);
        }
    }
}
