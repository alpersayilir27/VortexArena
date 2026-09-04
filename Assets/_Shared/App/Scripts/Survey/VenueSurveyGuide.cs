using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;

namespace VortexArena.App.Survey
{
    /// <summary>
    /// The visual side of the survey: point markers, the floor+wall shell and the column prisms,
    /// all built from code under one root.
    /// <para>
    /// ⚠️ Geometry is generated, never authored: the survey scene must stay empty so a new build is
    /// not needed to change what the player sees, and so nothing in the scene can drift from the
    /// captured points.
    /// </para>
    /// <para>
    /// ⚠️ This is the ONLY reason the ProBuilder runtime ships. Keep it inside
    /// <c>VortexArena.App</c>; Core has no ProBuilder reference and does not get one.
    /// </para>
    /// </summary>
    internal sealed class VenueSurveyGuide
    {
        private const string MaterialPath = "VenueSurvey/M_SurveyGuide";
        private static readonly Color WallColor = new Color(0.35f, 0.75f, 0.85f);
        private static readonly Color ColumnColor = new Color(0.9f, 0.45f, 0.2f);
        private static readonly Color AnchorAColor = new Color(0.2f, 0.5f, 1f);
        private static readonly Color AnchorBColor = new Color(1f, 0.55f, 0.1f);

        /// <summary>Faces within this of the top are the cap that gets deleted (m).</summary>
        private const float CapEpsilon = 0.01f;

        /// <summary>The shell is lifted by this so it does not z-fight the ground plane (m).</summary>
        private const float FloorLift = 0.005f;

        private readonly Transform root;
        private readonly Material material;
        private MaterialPropertyBlock block;

        internal VenueSurveyGuide()
        {
            root = new GameObject("SurveyGuide").transform;
            material = LoadMaterial();
        }

        /// <summary>A small cube standing ON the floor point (a captured corner/column corner).</summary>
        internal GameObject PlaceMarker(Vector3 floorPoint, Color color, float size = 0.08f)
        {
            ProBuilderMesh mesh = ShapeGenerator.GenerateCube(PivotLocation.Center, Vector3.one * size);
            mesh.gameObject.name = "Nokta";
            mesh.transform.SetParent(root, false);
            mesh.transform.position = new Vector3(floorPoint.x, size * 0.5f, floorPoint.z);

            Finish(mesh, color);
            return mesh.gameObject;
        }

        /// <summary>A bigger cube for a calibration mark; named like the scene's own anchors so the
        /// operator recognises which one is A and which is B.</summary>
        internal GameObject PlaceCalibrationCube(Vector3 floorPoint, bool isA)
        {
            const float size = 0.2f;

            ProBuilderMesh mesh = ShapeGenerator.GenerateCube(PivotLocation.Center, Vector3.one * size);
            mesh.gameObject.name = isA ? "anchor_a" : "anchor_b";
            mesh.transform.SetParent(root, false);
            mesh.transform.position = new Vector3(floorPoint.x, size * 0.5f, floorPoint.z);

            Finish(mesh, isA ? AnchorAColor : AnchorBColor);
            return mesh.gameObject;
        }

        /// <summary>The room shell: the corner ring extruded up, with the ceiling cap removed so the
        /// player can still see out.</summary>
        internal GameObject BuildFloorAndWalls(IList<Vector2> ringXZ, float wallHeight)
        {
            GameObject shell = BuildPrism("Zemin_Duvar", ringXZ, wallHeight, WallColor);
            if (shell != null)
            {
                RemoveTopCap(shell.GetComponent<ProBuilderMesh>());
            }

            return shell;
        }

        /// <summary>A column prism. The cap stays: a column IS closed at the top.</summary>
        internal GameObject BuildColumn(IList<Vector2> ringXZ, float height, int index)
        {
            return BuildPrism($"Kolon_{index}", ringXZ, height, ColumnColor);
        }

        internal void Clear()
        {
            if (root != null)
            {
                Object.Destroy(root.gameObject);
            }
        }

        private GameObject BuildPrism(string name, IList<Vector2> ringXZ, float height, Color color)
        {
            var points = new List<Vector3>(ringXZ.Count);
            for (int i = 0; i < ringXZ.Count; i++)
            {
                points.Add(new Vector3(ringXZ[i].x, 0f, ringXZ[i].y));
            }

            ProBuilderMesh mesh = ProBuilderMesh.Create();
            mesh.gameObject.name = name;
            mesh.transform.SetParent(root, false);

            // ⚠️ A failed triangulation THROWS NOTHING and leaves an empty mesh — an object with the
            // right name and no geometry (same trap as DimensionMeshBuilder.CreatePolygon).
            ActionResult shape = mesh.CreateShapeFromPolygon(points, height, false);
            if (!shape || mesh.faceCount == 0)
            {
                Debug.LogError($"[VenueSurvey] '{name}' üretilemedi (ProBuilder: {shape.notification}).");
                Object.Destroy(mesh.gameObject);
                return null;
            }

            Finish(mesh, color);

            // The extrusion direction follows the ring's winding, so the result is measured and the
            // object is pushed up until its BOTTOM sits on the floor.
            float bottom = 0f;
            var filter = mesh.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                bottom = filter.sharedMesh.bounds.min.y;
            }

            mesh.transform.localPosition = new Vector3(0f, -bottom + FloorLift, 0f);
            return mesh.gameObject;
        }

        /// <summary>Deletes the horizontal faces at the top of the prism.</summary>
        /// <remarks>Without this the player stands inside a closed box and can see neither the real
        /// room nor the rest of the guide.</remarks>
        private static void RemoveTopCap(ProBuilderMesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            IList<Vector3> positions = mesh.positions;
            if (positions == null || positions.Count == 0)
            {
                return;
            }

            float maxY = float.MinValue;
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].y > maxY)
                {
                    maxY = positions[i].y;
                }
            }

            var cap = new List<Face>();
            foreach (Face face in mesh.faces)
            {
                System.Collections.ObjectModel.ReadOnlyCollection<int> indexes = face.distinctIndexes;
                if (indexes.Count == 0)
                {
                    continue;
                }

                bool onTop = true;
                for (int i = 0; i < indexes.Count; i++)
                {
                    if (positions[indexes[i]].y < maxY - CapEpsilon)
                    {
                        onTop = false;
                        break;
                    }
                }

                if (onTop)
                {
                    cap.Add(face);
                }
            }

            // Deleting every face would leave an empty mesh — a flat ring reaches that.
            if (cap.Count == 0 || cap.Count >= mesh.faceCount)
            {
                return;
            }

            mesh.DeleteFaces(cap);
            mesh.ToMesh();
            mesh.Refresh();
        }

        private void Finish(ProBuilderMesh mesh, Color color)
        {
            if (material != null)
            {
                mesh.SetMaterial(mesh.faces, material);
            }

            mesh.ToMesh();
            mesh.Refresh();

            var renderer = mesh.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                return;
            }

            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);

            // Both names on purpose: URP Lit reads _BaseColor, a fallback built-in shader _Color.
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>Resources material first; a runtime <c>Shader.Find</c> only as a fallback.</summary>
        /// <remarks>⚠️ <c>Shader.Find</c> can return null in a player build (an unreferenced shader
        /// is stripped), so a null result is tolerated — ProBuilder's own default stays.</remarks>
        private static Material LoadMaterial()
        {
            var loaded = Resources.Load<Material>(MaterialPath);
            if (loaded != null)
            {
                return loaded;
            }

            Debug.LogError(
                $"[VenueSurvey] Rehber materyali bulunamadı (Resources/{MaterialPath}); " +
                "geçici materyal deneniyor.");

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            return shader != null ? new Material(shader) : null;
        }
    }
}
