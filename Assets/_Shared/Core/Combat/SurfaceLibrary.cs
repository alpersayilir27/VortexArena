using System;
using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>Answers "what surface is this collider" — the single place the two-tier rule lives.
    /// <para>Like <see cref="WeaponCatalog"/> it MUST live under Resources
    /// (<c>Assets/_Shared/Data/Resources/SurfaceLibrary.asset</c>): its consumer
    /// (<see cref="SurfaceImpactFx"/>) bootstraps itself and has no field to bind.</para>
    /// <para><b>Resolution order, first match wins:</b> (1) a <see cref="SurfaceTag"/> above the
    /// collider — the explicit override; (2) the renderer's <c>sharedMaterial</c> in the map;
    /// (3) <see cref="DefaultSurface"/>. ⚠️ The third tier is not optional: a surface nobody mapped
    /// producing NOTHING reads as "the effect is broken", while a generic puff reads as scenery.</para>
    /// <para>⚠️ Identity comes from the MATERIAL, not a tag or a layer. A tag is one flat name per
    /// object and cannot describe a wall built from two materials; layers are the physics filter and
    /// there are only 32 of them. A material is already authored per look and one entry here covers
    /// every arena using it.</para></summary>
    [CreateAssetMenu(fileName = "SurfaceLibrary", menuName = "VortexArena/Surface Library")]
    public class SurfaceLibrary : ScriptableObject
    {
        /// <summary>Resources.Load key (identical to the asset file name).</summary>
        private const string ResourcePath = "SurfaceLibrary";

        private static SurfaceLibrary _cached;
        private static bool _loadAttempted;

        [Tooltip("Yüzey tanımları. Materyal eşlemesi tanımların KENDİ içindedir; buraya yalnız " +
                 "tanımı eklemek yeter.")]
        [SerializeField] private SurfaceDefinition[] definitions = Array.Empty<SurfaceDefinition>();

        [Tooltip("Eşleşmeyen her yüzeyin düştüğü tanım (toz/kıvılcım). ⚠️ Boş bırakma: eşleşmeyen " +
                 "yüzeyde hiçbir şey çıkmaması 'efekt bozuk' diye okunur.")]
        [SerializeField] private SurfaceDefinition defaultSurface;

        /// <summary>Material → definition, built on the first query. Rebuilding it per shot would
        /// walk every definition's material list ten times a second.</summary>
        private Dictionary<Material, SurfaceDefinition> _byMaterial;

        public SurfaceDefinition DefaultSurface => defaultSurface;

        /// <summary>Loads the library from Resources; the result is cached once. A missing asset logs
        /// a SINGLE warning and returns null — callers must tolerate null.</summary>
        public static SurfaceLibrary Load()
        {
            if (_cached != null)
            {
                return _cached;
            }

            if (_loadAttempted)
            {
                return null;
            }

            _loadAttempted = true;
            _cached = Resources.Load<SurfaceLibrary>(ResourcePath);
            if (_cached == null)
            {
                Debug.LogWarning(
                    $"[SurfaceLibrary] Resources'ta '{ResourcePath}' bulunamadı — yüzey çarpma " +
                    "efektleri hiç çıkmayacak.");
            }

            return _cached;
        }

        /// <summary>The surface a round landed on. Never throws; falls back to
        /// <see cref="DefaultSurface"/>, which may itself be null in an unfinished project.</summary>
        public SurfaceDefinition Resolve(Collider collider)
        {
            if (collider == null)
            {
                return defaultSurface;
            }

            // Tier 1 — explicit override, searched upwards: the collider may sit on a child of the
            // object carrying the tag.
            SurfaceTag tag = collider.GetComponentInParent<SurfaceTag>();
            if (tag != null && tag.Surface != null)
            {
                return tag.Surface;
            }

            // Tier 2 — the material asset.
            Material material = FindMaterial(collider);
            if (material != null && Map.TryGetValue(material, out SurfaceDefinition mapped))
            {
                return mapped;
            }

            return defaultSurface;
        }

        private Dictionary<Material, SurfaceDefinition> Map
        {
            get
            {
                if (_byMaterial != null)
                {
                    return _byMaterial;
                }

                _byMaterial = new Dictionary<Material, SurfaceDefinition>();
                for (int i = 0; i < definitions.Length; i++)
                {
                    SurfaceDefinition definition = definitions[i];
                    if (definition == null)
                    {
                        continue;
                    }

                    Material[] materials = definition.Materials;
                    if (materials == null)
                    {
                        continue;
                    }

                    for (int m = 0; m < materials.Length; m++)
                    {
                        Material material = materials[m];
                        if (material == null)
                        {
                            continue;
                        }

                        // ⚠️ The FIRST binding wins and the duplicate is reported: silently letting
                        // the last one win would make the effect depend on list order, and the same
                        // wall would spark in one build and splinter in the next.
                        if (_byMaterial.TryGetValue(material, out SurfaceDefinition existing))
                        {
                            if (existing != definition)
                            {
                                Debug.LogWarning(
                                    $"[SurfaceLibrary] '{material.name}' materyali iki yüzeye bağlı " +
                                    $"('{existing.SurfaceId}' ve '{definition.SurfaceId}') — ilki " +
                                    "geçerli sayıldı. Materyali tek yüzeyde bırak.", this);
                            }

                            continue;
                        }

                        _byMaterial.Add(material, definition);
                    }
                }

                return _byMaterial;
            }
        }

        /// <summary>The renderer's shared material: same object first, then a child, then a parent —
        /// a collider does not have to sit on the renderer.
        /// <para>⚠️ <c>sharedMaterial</c>, never <c>material</c>: reading the singular property at
        /// runtime makes Unity CLONE the material, and a clone matches nothing in the map. The
        /// symptom would be "everything is the default surface".</para></summary>
        private static Material FindMaterial(Collider collider)
        {
            var renderer = collider.GetComponent<Renderer>();
            if (renderer == null)
            {
                renderer = collider.GetComponentInChildren<Renderer>();
            }

            if (renderer == null)
            {
                renderer = collider.GetComponentInParent<Renderer>();
            }

            return renderer != null ? renderer.sharedMaterial : null;
        }
    }
}
