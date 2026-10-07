using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VortexArena.Core.Editor
{
    /// <summary>Writes the local-hand glove meshes from their Blender dumps (<c>blender/&lt;Glove&gt;/export</c>).</summary>
    /// <remarks>
    /// An existing mesh asset is rewritten in place, so GUIDs and references survive; a missing one is created with
    /// the package hand's bindposes. Weights are matched to the rig renderer's bone order BY NAME. Both dumps are
    /// parsed before anything is written. Pipeline: the README next to each glove's Blender file.
    /// </remarks>
    public static class GloveMeshImporter
    {
        private const string RigPath = "Assets/_Shared/App/Prefabs/VA_CameraRig.prefab";
        private const string PackageHandPath = "Packages/com.meta.xr.sdk.interaction/Runtime/Meshes/OpenXR{0}Hand.fbx";

        // Name = Blender folder = dump/asset/texture prefix.
        private sealed class Glove
        {
            public string Name;
            public string AssetDir;
            public int Submeshes;
        }

        private static readonly Glove Tactical = new Glove { Name = "TacticalGlove", AssetDir = "Assets/_Shared/Avatars/TacticalGlove", Submeshes = 2 };
        private static readonly Glove Chef = new Glove { Name = "ChefGlove", AssetDir = "Assets/Modes/Burger/Avatars/ChefGlove", Submeshes = 1 };

        private sealed class Dump
        {
            public string Side;
            public string AssetPath;
            public Mesh Mesh;               // null = created on apply
            public Matrix4x4[] Bindposes;   // for a new asset
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector4> Tangents = new List<Vector4>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<BoneWeight> Weights = new List<BoneWeight>();
            public List<int>[] Triangles;
        }

        [MenuItem("Tools/VortexArena/Avatars/Eldiven Mesh'ini İçe Aktar/Taktik Eldiven", false, 25)]
        private static void ImportTactical() => Import(Tactical);

        [MenuItem("Tools/VortexArena/Avatars/Eldiven Mesh'ini İçe Aktar/Aşçı Eldiveni", false, 26)]
        private static void ImportChef() => Import(Chef);

        private static void Import(Glove glove)
        {
            var tag = $"[GloveImport:{glove.Name}] ";
            var exportDir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "blender", glove.Name, "export");
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            if (rig == null)
            {
                Debug.LogError(tag + $"Rig prefabı yok: {RigPath}");
                return;
            }

            if (!AssetDatabase.IsValidFolder(glove.AssetDir))
            {
                Debug.LogError(tag + $"Hedef klasör yok: {glove.AssetDir} — Blender betiği dokuları oraya yazar, önce o çalışmalı.");
                return;
            }

            var dumps = new List<Dump>();
            foreach (var side in new[] { "R", "L" })
            {
                var dump = Parse(rig, glove, exportDir, side, out var error);
                if (dump == null)
                {
                    Debug.LogError(tag + error);
                    return;
                }

                dumps.Add(dump);
            }

            // Blender writes the PNGs straight into the asset folder; make sure Unity has the current pixels.
            foreach (var texture in new[] { "Albedo", "Normal" })
            {
                AssetDatabase.ImportAsset($"{glove.AssetDir}/T_{glove.Name}_{texture}.png", ImportAssetOptions.ForceUpdate);
            }

            var report = new List<string>();
            foreach (var dump in dumps)
            {
                Apply(dump, glove);
                report.Add($"{dump.Side}: {dump.Vertices.Count} vertex, alt-mesh üçgenleri " +
                           string.Join(" / ", dump.Triangles.Select(t => t.Count / 3)));
            }

            AssetDatabase.SaveAssets();
            Debug.Log(tag + "Eldiven mesh'leri yazıldı — " + string.Join(" · ", report));
        }

        private static Dump Parse(GameObject rig, Glove glove, string exportDir, string side, out string error)
        {
            var path = Path.Combine(exportDir, $"{glove.Name}_{side}.txt");
            if (!File.Exists(path))
            {
                error = $"Döküm yok: {path} — önce Blender'daki doku betiği çalıştırılmalı.";
                return null;
            }

            var hand = side == "R" ? "Right" : "Left";
            var smr = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(s => s.name == hand + "Hand" && s.transform.parent != null && s.transform.parent.name == $"OpenXR{hand}Hand");
            if (smr == null)
            {
                error = $"Rig'de OpenXR{hand}Hand/{hand}Hand SkinnedMeshRenderer'ı yok.";
                return null;
            }

            var dump = new Dump
            {
                Side = side,
                AssetPath = $"{glove.AssetDir}/{glove.Name}_{side}.asset",
                Triangles = Enumerable.Range(0, glove.Submeshes).Select(_ => new List<int>()).ToArray(),
            };
            dump.Mesh = AssetDatabase.LoadAssetAtPath<Mesh>(dump.AssetPath);
            var bindposeCount = dump.Mesh != null ? dump.Mesh.bindposes.Length : -1;
            if (dump.Mesh == null)
            {
                // Bindpose i belongs to bone i: the package mesh is only usable when the rig keeps its bone order.
                var package = AssetDatabase.LoadAssetAtPath<GameObject>(string.Format(PackageHandPath, hand))?.GetComponentInChildren<SkinnedMeshRenderer>();
                if (package == null || !package.bones.Select(b => b.name).SequenceEqual(smr.bones.Select(b => b.name)))
                {
                    error = $"{side}: yeni asset için paketin el mesh'i bulunamadı ya da kemik sırası rig'inkinden farklı.";
                    return null;
                }

                dump.Bindposes = package.sharedMesh.bindposes;
                bindposeCount = dump.Bindposes.Length;
            }

            if (bindposeCount != smr.bones.Length)
            {
                error = $"{side}: bindpose sayısı ({bindposeCount}) rig kemik sayısıyla ({smr.bones.Length}) uyuşmuyor.";
                return null;
            }

            var rigBone = new Dictionary<string, int>();
            for (int i = 0; i < smr.bones.Length; i++)
            {
                rigBone[smr.bones[i].name] = i;
            }

            var inv = CultureInfo.InvariantCulture;
            string[] dumpBones = null;
            foreach (var line in File.ReadLines(path))
            {
                var p = line.Split(' ');
                float F(int i) => float.Parse(p[i], inv);

                // Blender is right-handed: z is negated, which also flips tangent sign and winding.
                switch (p[0])
                {
                    case "bones":
                        dumpBones = p.Skip(1).ToArray();
                        var missing = dumpBones.FirstOrDefault(b => !rigBone.ContainsKey(b));
                        if (missing != null)
                        {
                            error = $"{side}: '{missing}' kemiği rig'de yok — kemik adları paketinkiyle aynı kalmalı.";
                            return null;
                        }

                        break;
                    case "v":
                        dump.Vertices.Add(new Vector3(F(1), F(2), -F(3)));
                        break;
                    case "n":
                        dump.Normals.Add(new Vector3(F(1), F(2), -F(3)));
                        break;
                    case "t":
                        dump.Uvs.Add(new Vector2(F(1), F(2)));
                        break;
                    case "g":
                        dump.Tangents.Add(new Vector4(F(1), F(2), -F(3), -F(4)));
                        break;
                    case "w":
                        if (dumpBones == null)
                        {
                            error = $"{side}: dökümde 'bones' satırı ağırlıklardan önce gelmeli.";
                            return null;
                        }

                        dump.Weights.Add(ToBoneWeight(p, dumpBones, rigBone, inv));
                        break;
                    case "f":
                        var sub = int.Parse(p[4], inv);
                        if (sub < 0 || sub >= glove.Submeshes)
                        {
                            error = $"{side}: bilinmeyen alt-mesh {sub} (bu eldivende {glove.Submeshes} alt-mesh var).";
                            return null;
                        }

                        dump.Triangles[sub].Add(int.Parse(p[1], inv));
                        dump.Triangles[sub].Add(int.Parse(p[3], inv));
                        dump.Triangles[sub].Add(int.Parse(p[2], inv));
                        break;
                }
            }

            var n = dump.Vertices.Count;
            if (n == 0 || dump.Normals.Count != n || dump.Uvs.Count != n || dump.Tangents.Count != n || dump.Weights.Count != n)
            {
                error = $"{side}: döküm tutarsız (v {n} · n {dump.Normals.Count} · t {dump.Uvs.Count} · g {dump.Tangents.Count} · w {dump.Weights.Count}).";
                return null;
            }

            error = null;
            return dump;
        }

        // Bone4: the four heaviest influences, renormalised, heaviest first (Unity expects that order).
        private static BoneWeight ToBoneWeight(string[] p, string[] dumpBones, Dictionary<string, int> rigBone, IFormatProvider inv)
        {
            var pairs = new List<(int bone, float weight)>();
            for (int i = 1; i + 1 < p.Length; i += 2)
            {
                pairs.Add((rigBone[dumpBones[int.Parse(p[i], inv)]], float.Parse(p[i + 1], inv)));
            }

            pairs = pairs.OrderByDescending(x => x.weight).Take(4).ToList();
            var sum = pairs.Sum(x => x.weight);
            var bw = new BoneWeight();
            if (pairs.Count > 0) { bw.boneIndex0 = pairs[0].bone; bw.weight0 = pairs[0].weight / sum; }
            if (pairs.Count > 1) { bw.boneIndex1 = pairs[1].bone; bw.weight1 = pairs[1].weight / sum; }
            if (pairs.Count > 2) { bw.boneIndex2 = pairs[2].bone; bw.weight2 = pairs[2].weight / sum; }
            if (pairs.Count > 3) { bw.boneIndex3 = pairs[3].bone; bw.weight3 = pairs[3].weight / sum; }
            return bw;
        }

        private static void Apply(Dump dump, Glove glove)
        {
            var mesh = dump.Mesh;
            var bindposes = mesh != null ? mesh.bindposes : dump.Bindposes;
            if (mesh == null)
            {
                mesh = new Mesh { name = $"{glove.Name}_{dump.Side}" };
                AssetDatabase.CreateAsset(mesh, dump.AssetPath);
            }

            mesh.Clear();
            mesh.indexFormat = dump.Vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(dump.Vertices);
            mesh.SetNormals(dump.Normals);
            mesh.SetTangents(dump.Tangents);
            mesh.SetUVs(0, dump.Uvs);
            mesh.boneWeights = dump.Weights.ToArray();
            mesh.bindposes = bindposes;
            mesh.subMeshCount = dump.Triangles.Length;
            for (int i = 0; i < dump.Triangles.Length; i++)
            {
                mesh.SetTriangles(dump.Triangles[i], i);
            }

            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
        }
    }
}
