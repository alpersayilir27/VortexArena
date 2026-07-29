using UnityEngine;
using UnityEditor;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using System.Collections.Generic;
using System.IO;

public static class MekanOlustur
{
    const float KOLON_YUKSEKLIGI = 3.0f;
    const string MAT_YOLU = "Assets/Materials/M_Mekan.mat";

    static readonly List<Vector3> TABAN = new List<Vector3>
    {
        new Vector3(0.00f, 0f,  0.00f),  // v0 sol alt (kapi duvari)
        new Vector3(8.32f, 0f,  0.00f),  // v1 sag alt
        new Vector3(8.32f, 0f, 13.23f),  // v2 sag ust
        new Vector3(0.46f, 0f, 13.12f),  // v3 sol ust
        new Vector3(0.19f, 0f,  7.57f),  // v4 sol duvar kirilma
        new Vector3(0.07f, 0f,  2.20f),  // v5 sol duvar kirilma
    };

    // ad, genislik(X), derinlik(Z), merkez X, merkez Z
    static readonly (string ad, float gen, float der, float x, float z)[] KOLONLAR =
    {
        ("Kolon_Orta",     0.67f, 0.38f, 3.605f,  7.380f),
        ("Kolon_Alt",      0.66f, 0.38f, 3.600f,  2.010f),
        ("Kolon_UstDuvar", 0.32f, 0.40f, 3.820f, 12.920f),
    };

    [MenuItem("Tools/Mekan/Zemin ve Kolonlari Olustur")]
    static void Olustur()
    {
        var mat = MaterialGetirVeyaOlustur();
        if (mat == null) return;

        var kok = new GameObject("Mekan");

        var zemin = ProBuilderMesh.Create();
        zemin.gameObject.name = "Zemin";
        zemin.CreateShapeFromPolygon(TABAN, 0f, false);   // 0 = extrude yok, duz yuzey
        zemin.SetMaterial(zemin.faces, mat);
        zemin.ToMesh();
        zemin.Refresh();
        zemin.transform.SetParent(kok.transform, false);

        var kolonKok = new GameObject("Kolonlar");
        kolonKok.transform.SetParent(kok.transform, false);

        foreach (var k in KOLONLAR)
        {
            var pb = ShapeGenerator.GenerateCube(
                PivotLocation.Center,
                new Vector3(k.gen, KOLON_YUKSEKLIGI, k.der));

            pb.gameObject.name = k.ad;
            pb.SetMaterial(pb.faces, mat);
            pb.ToMesh();
            pb.Refresh();
            pb.transform.SetParent(kolonKok.transform, false);
            pb.transform.localPosition = new Vector3(k.x, KOLON_YUKSEKLIGI * 0.5f, k.z);
        }

        Selection.activeGameObject = kok;
        Debug.Log($"Mekan olusturuldu. Materyal: {mat.name} / Shader: {mat.shader.name}");
    }

    static Material MaterialGetirVeyaOlustur()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(MAT_YOLU);
        if (m != null) return m;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("URP Lit shader bulunamadi. Proje gercekten URP mi?");
            return null;
        }

        var klasor = Path.GetDirectoryName(MAT_YOLU).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(klasor))
            AssetDatabase.CreateFolder("Assets", "Materials");

        m = new Material(shader) { color = new Color(0.75f, 0.75f, 0.72f) };
        AssetDatabase.CreateAsset(m, MAT_YOLU);
        AssetDatabase.SaveAssets();
        return m;
    }
}