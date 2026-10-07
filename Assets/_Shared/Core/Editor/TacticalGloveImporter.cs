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
    /// <summary>Rewrites the tactical glove meshes from the Blender dumps (<c>blender/TacticalGlove/export</c>).</summary>
    /// <remarks>
    /// Writes into the existing mesh assets, so GUIDs and the rig's references survive. Bindposes stay the
    /// asset's own (package hand mesh); weights are matched to the rig renderer's bone order BY NAME.
    /// Both dumps are parsed before anything is written. Pipeline: <c>blender/TacticalGlove/README.md</c>.
    /// </remarks>
    public static class TacticalGloveImporter
    {
        private const string AssetDir = "Assets/_Shared/Avatars/TacticalGlove";
        private const string RigPath = "Assets/_Shared/App/Prefabs/VA_CameraRig.prefab";
        private static readonly string[] Textures = { "T_TacticalGlove_Albedo", "T_TacticalGlove_Normal" };

        private sealed class Dump
        {
            public string Side;
            public Mesh Mesh;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector4> Tangents = new List<Vector4>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<BoneWeight> Weights = new List<BoneWeight>();
            public readonly List<int>[] Triangles = { new List<int>(), new List<int>() };
        }

        [MenuItem("Tools/VortexArena/Avatars/Taktik Eldiven Mesh'ini İçe Aktar", false, 25)]
        private static void Import()
        {
            var exportDir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "blender", "TacticalGlove", "export");
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            if (rig == null)
            {
                Debug.LogError($"[TacticalGlove] Rig prefabı yok: {RigPath}");
                return;
            }

            var dumps = new List<Dump>();
            foreach (var side in new[] { "R", "L" })
            {
                var dump = Parse(rig, exportDir, side, out var error);
                if (dump == null)
                {
                    Debug.LogError("[TacticalGlove] " + error);
                    return;
                }

                dumps.Add(dump);
            }

            // Blender writes the PNGs straight into Assets; make sure Unity has the current pixels.
            foreach (var texture in Textures)
            {
                AssetDatabase.ImportAsset($"{AssetDir}/{texture}.png", ImportAssetOptions.ForceUpdate);
            }

            var report = new List<string>();
            foreach (var dump in dumps)
            {
                Apply(dump);
                report.Add($"{dump.Side}: {dump.Vertices.Count} vertex, gövde {dump.Triangles[0].Count / 3} / kayış {dump.Triangles[1].Count / 3} üçgen");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[TacticalGlove] Eldiven mesh'leri yeniden yazıldı — " + string.Join(" · ", report));
        }

        private static Dump Parse(GameObject rig, string exportDir, string side, out string error)
        {
            var path = Path.Combine(exportDir, $"TacticalGlove_{side}.txt");
            if (!File.Exists(path))
            {
                error = $"Döküm yok: {path} — önce Blender'da glove_tex.py çalıştırılmalı.";
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

            var dump = new Dump { Side = side, Mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{AssetDir}/TacticalGlove_{side}.asset") };
            if (dump.Mesh == null)
            {
                error = $"Mesh asset'i yok: {AssetDir}/TacticalGlove_{side}.asset — araç yalnız var olan asset'i yeniden yazar.";
                return null;
            }

            // Bindpose i belongs to bone i, so the asset and the renderer must agree on the bone count.
            if (dump.Mesh.bindposes.Length != smr.bones.Length)
            {
                error = $"{side}: bindpose sayısı ({dump.Mesh.bindposes.Length}) rig kemik sayısıyla ({smr.bones.Length}) uyuşmuyor.";
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
                        if (sub < 0 || sub > 1)
                        {
                            error = $"{side}: bilinmeyen alt-mesh {sub} (0 gövde, 1 kayış).";
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

        private static void Apply(Dump dump)
        {
            var mesh = dump.Mesh;
            var bindposes = mesh.bindposes;
            mesh.Clear();
            mesh.indexFormat = dump.Vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(dump.Vertices);
            mesh.SetNormals(dump.Normals);
            mesh.SetTangents(dump.Tangents);
            mesh.SetUVs(0, dump.Uvs);
            mesh.boneWeights = dump.Weights.ToArray();
            mesh.bindposes = bindposes;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(dump.Triangles[0], 0);
            mesh.SetTriangles(dump.Triangles[1], 1);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
        }
    }
}
