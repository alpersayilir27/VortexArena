using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class HDRPtoURPConverter : EditorWindow
{
    private List<string> materialPaths = new List<string>();
    private Vector2 scrollPos;

    [MenuItem("Tools/HDRP to URP Converter")]
    public static void ShowWindow()
    {
        GetWindow<HDRPtoURPConverter>("HDRP→URP Converter");
    }

    private void OnGUI()
    {
        GUILayout.Label("HDRP to URP Material Converter", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Once projenizi yedekleyin! LayeredLit materyallerde sadece 0. katman (ana katman) URP'ye aktarilir.", MessageType.Warning);

        if (GUILayout.Button("1) TUM Materyalleri Tekrar Tara ve Cevir (HDRP + zaten URP olan bozuklar dahil)"))
        {
            ScanAllMaterials();
            ConvertMaterials();
        }

        GUILayout.Label($"Son islemde bulunan materyal sayisi: {materialPaths.Count}");

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(150));
        foreach (var path in materialPaths)
        {
            EditorGUILayout.LabelField(path);
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(10);

        if (GUILayout.Button("2) Sadece Eksik Base Map Texture'lari Otomatik Duzelt"))
        {
            FixMissingBaseMaps();
        }

        EditorGUILayout.Space(10);
        if (GUILayout.Button("3) TUM URP Materyallerinde: Texture Varsa BaseColor'u Beyaza Sabitle (SIYAH SORUNUNU DUZELTIR)"))
        {
            ForceWhiteBaseColorWhereTextured();
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox("Debug icin: Project penceresinde bir materyal secip asagidaki butona basin.", MessageType.Info);
        if (GUILayout.Button("4) DEBUG: Secili Materyalin Texture'larini Listele"))
        {
            DebugSelectedMaterial();
        }
    }

    // Texture'i olan ama BaseColor'i siyah/koyu kalmis materyalleri duzeltir.
    // HDRP'den gelen eski, kullanilmayan bir _Color degeri yanlislikla
    // tint olarak alinmis olabilir; texture varken tint her zaman beyaz olmalidir.
    private void ForceWhiteBaseColorWhereTextured()
    {
        int fixedCount = 0;
        string[] guids = AssetDatabase.FindAssets("t:Material");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            if (!mat.shader.name.Contains("Universal Render Pipeline")) continue;
            if (!mat.HasProperty("_BaseMap")) continue;
            if (!mat.HasProperty("_BaseColor")) continue;

            Texture baseTex = mat.GetTexture("_BaseMap");
            if (baseTex == null) continue;

            Color currentColor = mat.GetColor("_BaseColor");
            bool isDark = currentColor.r < 0.05f && currentColor.g < 0.05f && currentColor.b < 0.05f;

            if (isDark)
            {
                Undo.RecordObject(mat, "Force White BaseColor");
                mat.SetColor("_BaseColor", Color.white);
                EditorUtility.SetDirty(mat);
                fixedCount++;
                Debug.Log($"BaseColor beyaza cekildi: {path}");
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Tamamlandi", $"{fixedCount} materyalin BaseColor'u beyaza cekildi.", "Tamam");
    }

    // Taranacak materyaller: HDRP olanlar + zaten URP'ye cevrilmis ama Base Map'i bos olanlar
    private void ScanAllMaterials()
    {
        materialPaths.Clear();
        string[] guids = AssetDatabase.FindAssets("t:Material");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;

            string shaderName = mat.shader.name;
            bool isHDRP = shaderName.StartsWith("HDRP/") || shaderName.Contains("Hidden/InternalErrorShader");
            bool isBrokenURP = shaderName.Contains("Universal Render Pipeline") && mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == null;

            if (isHDRP || isBrokenURP)
            {
                materialPaths.Add(path);
            }
        }
        Debug.Log($"Tarama tamamlandi. {materialPaths.Count} materyal islenecek.");
    }

    private void ConvertMaterials()
    {
        int converted = 0;
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            EditorUtility.DisplayDialog("Hata", "URP/Lit shader bulunamadi. URP paketinin kurulu oldugundan emin olun.", "Tamam");
            return;
        }

        foreach (string path in materialPaths)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            Dictionary<string, Texture> savedTextures = GetAllSavedTextures(mat);

            Texture albedoTex = FindTextureByPriority(savedTextures,
                new[] { "_basecolormap0", "_basecolormap", "_unlitcolormap", "_maintex", "_basemap" },
                new[] { "basecolor", "albedo", "diffuse", "unlitcolor", "maintex", "basemap" },
                new[] { "normal", "mask", "emiss", "height", "ao", "occlusion", "metallic", "smooth", "detail" });

            Texture normalTex = FindTextureByPriority(savedTextures,
                new[] { "_normalmap0", "_normalmap" },
                new[] { "normal" },
                new string[0]);

            Texture maskTex = FindTextureByPriority(savedTextures,
                new[] { "_maskmap0", "_maskmap" },
                new[] { "mask" },
                new string[0]);

            Texture emissiveTex = FindTextureByPriority(savedTextures,
                new[] { "_emissivecolormap0", "_emissivecolormap" },
                new[] { "emiss" },
                new string[0]);

            Color baseColor = GetColorSafe(mat, "_BaseColor0", Color.white);
            if (baseColor == Color.white) baseColor = GetColorSafe(mat, "_BaseColor", Color.white);
            if (baseColor == Color.white) baseColor = GetColorSafe(mat, "_Color", Color.white);

            float smoothness = GetFloatSafe(mat, "_Smoothness0", -1f);
            if (smoothness < 0f) smoothness = GetFloatSafe(mat, "_Smoothness", 0.5f);

            float metallic = GetFloatSafe(mat, "_Metallic0", -1f);
            if (metallic < 0f) metallic = GetFloatSafe(mat, "_Metallic", 0f);

            Color emissiveColor = GetColorSafe(mat, "_EmissiveColor0", Color.black);
            if (emissiveColor == Color.black) emissiveColor = GetColorSafe(mat, "_EmissiveColor", Color.black);

            Undo.RecordObject(mat, "Convert HDRP to URP");
            mat.shader = urpLit;

            TrySetTexture(mat, "_BaseMap", albedoTex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);

            if (normalTex != null)
            {
                TrySetTexture(mat, "_BumpMap", normalTex);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (maskTex != null)
            {
                TrySetTexture(mat, "_MetallicGlossMap", maskTex);
            }
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

            if (emissiveTex != null)
            {
                TrySetTexture(mat, "_EmissionMap", emissiveTex);
                mat.EnableKeyword("_EMISSION");
            }
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissiveColor);

            // Eger hic albedo texture bulunamadiysa, en azindan elde ettigimiz herhangi bir
            // "renkli" texture'i (mask/normal disinda) son care olarak ata
            if (albedoTex == null)
            {
                Texture fallback = savedTextures.Values.FirstOrDefault();
                if (fallback != null && !(fallback is Texture2DArray))
                {
                    TrySetTexture(mat, "_BaseMap", fallback);
                }
            }

            EditorUtility.SetDirty(mat);
            converted++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Tamamlandi", $"{converted} materyal cevrildi/duzeltildi.", "Tamam");
        materialPaths.Clear();
    }

    private void FixMissingBaseMaps()
    {
        int fixedCount = 0;
        string[] guids = AssetDatabase.FindAssets("t:Material");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            if (!mat.shader.name.Contains("Universal Render Pipeline")) continue;
            if (!mat.HasProperty("_BaseMap")) continue;
            if (mat.GetTexture("_BaseMap") != null) continue;

            Dictionary<string, Texture> savedTextures = GetAllSavedTextures(mat);
            Texture best = FindTextureByPriority(savedTextures,
                new[] { "_basecolormap0", "_basecolormap", "_unlitcolormap", "_maintex", "_basemap" },
                new[] { "basecolor", "albedo", "diffuse", "unlitcolor", "maintex", "basemap" },
                new[] { "normal", "mask", "emiss", "height", "ao", "occlusion", "metallic", "smooth", "detail" });

            if (best == null)
            {
                best = savedTextures.Values.FirstOrDefault(t => !(t is Texture2DArray));
            }

            if (best != null)
            {
                Undo.RecordObject(mat, "Fix Base Map");
                mat.SetTexture("_BaseMap", best);
                EditorUtility.SetDirty(mat);
                fixedCount++;
                Debug.Log($"Duzeltildi: {path} -> {best.name}");
            }
            else
            {
                Debug.LogWarning($"Texture bulunamadi: {path}");
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Tamamlandi", $"{fixedCount} materyalin Base Map'i dolduruldu.", "Tamam");
    }

    private void DebugSelectedMaterial()
    {
        Material mat = Selection.activeObject as Material;
        if (mat == null)
        {
            Debug.LogWarning("Once Project penceresinde bir materyal secin.");
            return;
        }

        Debug.Log($"=== {mat.name} === Shader: {(mat.shader != null ? mat.shader.name : "YOK")}");
        Dictionary<string, Texture> textures = GetAllSavedTextures(mat);
        if (textures.Count == 0)
        {
            Debug.Log("Bu materyalde HIC saklanmis texture bulunamadi.");
        }
        foreach (var kv in textures)
        {
            string typeName = kv.Value != null ? kv.Value.GetType().Name : "null";
            Debug.Log($"  Property: '{kv.Key}'  ->  Texture: '{kv.Value.name}'  (Tip: {typeName})");
        }
    }

    private Dictionary<string, Texture> GetAllSavedTextures(Material mat)
    {
        Dictionary<string, Texture> result = new Dictionary<string, Texture>();
        SerializedObject so = new SerializedObject(mat);
        SerializedProperty savedProps = so.FindProperty("m_SavedProperties");
        if (savedProps == null) return result;
        SerializedProperty texEnvs = savedProps.FindPropertyRelative("m_TexEnvs");
        if (texEnvs == null) return result;

        for (int i = 0; i < texEnvs.arraySize; i++)
        {
            SerializedProperty entry = texEnvs.GetArrayElementAtIndex(i);
            SerializedProperty first = entry.FindPropertyRelative("first");
            SerializedProperty second = entry.FindPropertyRelative("second");
            if (first == null || second == null) continue;
            SerializedProperty texProp = second.FindPropertyRelative("m_Texture");
            if (texProp == null) continue;
            Texture tex = texProp.objectReferenceValue as Texture;
            if (tex != null)
            {
                result[first.stringValue] = tex;
            }
        }
        return result;
    }

    // Once tam eslesen property isimlerini (oncelik sirasiyla) dener,
    // bulamazsa anahtar kelime ile arar (mask/normal gibi olanlari haric tutarak)
    private Texture FindTextureByPriority(Dictionary<string, Texture> textures, string[] exactNamesInOrder, string[] includeKeywords, string[] excludeKeywords)
    {
        Dictionary<string, Texture> lowerMap = new Dictionary<string, Texture>();
        foreach (var kv in textures) lowerMap[kv.Key.ToLower()] = kv.Value;

        foreach (var exactName in exactNamesInOrder)
        {
            if (lowerMap.TryGetValue(exactName, out Texture t) && !(t is Texture2DArray))
            {
                return t;
            }
        }

        foreach (var kv in textures)
        {
            string lower = kv.Key.ToLower();
            bool excluded = excludeKeywords.Any(ex => lower.Contains(ex));
            if (excluded) continue;
            if (kv.Value is Texture2DArray) continue;

            foreach (var inc in includeKeywords)
            {
                if (lower.Contains(inc)) return kv.Value;
            }
        }
        return null;
    }

    private void TrySetTexture(Material mat, string propName, Texture tex)
    {
        if (tex == null) return;
        if (!mat.HasProperty(propName)) return;
        if (tex is Texture2DArray) return;
        mat.SetTexture(propName, tex);
    }

    private Color GetColorSafe(Material mat, string propName, Color fallback)
    {
        if (mat.HasProperty(propName)) return mat.GetColor(propName);
        return fallback;
    }

    private float GetFloatSafe(Material mat, string propName, float fallback)
    {
        if (mat.HasProperty(propName)) return mat.GetFloat(propName);
        return fallback;
    }
}