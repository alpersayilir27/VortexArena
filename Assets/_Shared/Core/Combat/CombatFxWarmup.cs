using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Renders every combat effect ONCE during a map load, behind the loading cover, so the first
    /// bullet and the first grenade of the match do not hitch.
    /// <para>⚠️ Creating the pools is NOT enough — a shader/PSO is only compiled when the effect is
    /// actually DRAWN. So each effect is spawned inside the camera frustum, left alive for
    /// <see cref="VisibleSeconds"/>, then returned to its pool. Everything runs muted: a warmup that
    /// is heard is a bug report.</para>
    /// <para>⚠️ Runs on the admin too: the spectator view is built from remote shots.</para>
    /// <para>Catalog effects run once per session, not once per map: catalogs are statically
    /// cached, the pools are <c>DontDestroyOnLoad</c> and a compiled shader stays compiled for the
    /// process. Break effects are SCENE-scoped: breakables are scene objects, not catalog entries,
    /// so every arena load draws the break prefabs no earlier scene had.</para>
    /// </summary>
    public static class CombatFxWarmup
    {
        /// <summary>How far in front of the eye the effects are spawned (m). ⚠️ Must stay BEHIND the
        /// headset fade quad (<c>HmdOverlayBuilder</c> puts it at 0.5 m) and inside its cone, or the
        /// warmup is visible.</summary>
        private const float DistanceMeters = 1.5f;

        /// <summary>How long each effect is left on screen (s) — enough frames to draw, short enough
        /// to end well before the cover lifts.</summary>
        private const float VisibleSeconds = 0.15f;

        /// <summary>Uniform shrink applied to pooled effects: an authored fireball is metres wide and
        /// would reach past the cover.</summary>
        private const float EffectScale = 0.12f;

        /// <summary>Half length of the warmup tracer (m) — the full line must exceed ShotTracer's
        /// minimum length or nothing is drawn.</summary>
        private const float TracerHalfMeters = 0.35f;

        /// <summary>Muzzle graph event; matches <c>WeaponMuzzleVfx</c>'s default.</summary>
        private const string FallbackFireEvent = "OnFire";

        private static bool _done;

        /// <summary>Break effect prefabs drawn so far this session.</summary>
        private static readonly HashSet<GameObject> WarmedBreakFx = new HashSet<GameObject>();

        /// <summary>Time the caller must keep the view covered (s) for a full run.</summary>
        public static float CoverSeconds => VisibleSeconds + 0.2f;

        /// <summary>Whether the loader must hold the cover for <see cref="Run"/>: the catalog pass
        /// has not finished yet, or the scene just activated brings a break effect no earlier scene
        /// had. ⚠️ Called AFTER scene activation — it reads the scene's breakables.</summary>
        public static bool NeedsRun()
        {
            if (!_done)
            {
                return true;
            }

            var pending = new HashSet<GameObject>();
            CollectBreakFx(pending);
            return pending.Count > 0;
        }

        /// <summary>Runs the warmup. ⚠️ Yielded by the SCENE LOADER while the fade/loading
        /// screen is still up — called anywhere else it draws effects in the player's face.</summary>
        public static IEnumerator Run()
        {
            bool full = !_done;

            var breakFxPrefabs = new HashSet<GameObject>();
            CollectBreakFx(breakFxPrefabs);

            if (!full && breakFxPrefabs.Count == 0)
            {
                yield break;
            }

            Transform view = ResolveViewpoint();
            if (view == null)
            {
                // No camera yet: leave it unfinished so the next map load tries again.
                Debug.LogWarning("[CombatFxWarmup] Kamera bulunamadı — efekt ısıtması atlandı.");
                yield break;
            }

            if (full)
            {
                _done = true;
            }

            WarmedBreakFx.UnionWith(breakFxPrefabs);

            Vector3 center = view.position + view.forward * DistanceMeters;
            Vector3 right = view.right;
            Vector3 up = view.up;
            Vector3 towardEye = -view.forward;

            var explosionPrefabs = new HashSet<GameObject>();
            var throwablePrefabs = new HashSet<GameObject>();
            var casingPrefabs = new HashSet<GameObject>();
            var muzzleGraphs = new Dictionary<VisualEffectAsset, string>();
            if (full)
            {
                CollectFromCatalogs(explosionPrefabs, throwablePrefabs, casingPrefabs, muzzleGraphs);
            }

            // Pass 1 — build everything (Instantiate + Resources.Load), still invisible.
            BlastFxPool blasts = BlastFxPool.Shared;
            foreach (GameObject prefab in explosionPrefabs)
            {
                // The whole ring: every node's first Play allocates its own particle buffers.
                blasts.Prewarm(prefab, BlastFxPool.NodesPerPrefab);
            }

            foreach (GameObject prefab in breakFxPrefabs)
            {
                blasts.Prewarm(prefab, BlastFxPool.NodesPerPrefab);
            }

            ThrowablePool throwables = ThrowablePool.Shared;
            foreach (GameObject prefab in throwablePrefabs)
            {
                throwables.Prewarm(prefab, ThrowablePool.NodesPerPrefab);
            }

            var temporaryGraphs = new List<GameObject>();
            foreach (KeyValuePair<VisualEffectAsset, string> graph in muzzleGraphs)
            {
                GameObject go = CreateGraphProbe(graph.Key, center);
                if (go != null)
                {
                    temporaryGraphs.Add(go);
                }
            }

            // The build pass alone is a heavy frame; the draw pass gets its own.
            yield return null;

            // Pass 2 — draw everything once.
            foreach (GameObject prefab in explosionPrefabs)
            {
                blasts.Warmup(prefab, center, VisibleSeconds, EffectScale);
            }

            foreach (GameObject prefab in breakFxPrefabs)
            {
                blasts.Warmup(prefab, center, VisibleSeconds, EffectScale);
            }

            foreach (GameObject prefab in throwablePrefabs)
            {
                // No shrink: a throwable is ~10 cm, it already fits behind the cover.
                throwables.Warmup(prefab, center, VisibleSeconds);
            }

            if (full)
            {
                SurfaceImpactFx.Shared.Warmup(center, towardEye, VisibleSeconds, EffectScale);
                ShotTracer.Shared.Warmup(center - right * TracerHalfMeters,
                    center + right * TracerHalfMeters, VisibleSeconds);
                HitMarker.Shared.Warmup(center + up * 0.1f, VisibleSeconds);

                CasingPool casings = CasingPool.Shared;
                foreach (GameObject prefab in casingPrefabs)
                {
                    casings.Warmup(prefab, center - up * 0.1f, VisibleSeconds);
                }

                if (RemoteShotFx.Instance != null)
                {
                    RemoteShotFx.Instance.Warmup(center, view.forward, VisibleSeconds);
                }
            }

            for (int i = 0; i < temporaryGraphs.Count; i++)
            {
                SendFireEvent(temporaryGraphs[i], muzzleGraphs);
            }

            // Two frames of drawing, then the effects' own lifetimes run out.
            yield return null;
            yield return null;

            float until = Time.unscaledTime + VisibleSeconds;
            while (Time.unscaledTime < until)
            {
                yield return null;
            }

            for (int i = 0; i < temporaryGraphs.Count; i++)
            {
                Object.Destroy(temporaryGraphs[i]);
            }
        }

        /// <summary>Break effect prefabs of the ACTIVE scene not drawn yet this session. Read off the
        /// scene, not a catalog: a breakable is a scene object (§10.10).</summary>
        private static void CollectBreakFx(HashSet<GameObject> into)
        {
            BreakableObject[] breakables = Object.FindObjectsByType<BreakableObject>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < breakables.Length; i++)
            {
                GameObject prefab = breakables[i].BreakFxPrefab;
                if (prefab != null && !WarmedBreakFx.Contains(prefab))
                {
                    into.Add(prefab);
                }
            }
        }

        /// <summary>⚠️ The eye is <c>OVRCameraRig.centerEyeAnchor</c>, never <c>Camera.main</c> in VR
        /// — all three rig cameras are tagged MainCamera. The admin has no rig and falls back.</summary>
        private static Transform ResolveViewpoint()
        {
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
            {
                return rig.centerEyeAnchor;
            }

            Camera camera = Camera.main;
            return camera != null ? camera.transform : null;
        }

        /// <summary>Gathers what has to be drawn from the catalogs: explosion prefabs, throwable
        /// prefabs, casing prefabs and muzzle graphs. WEAPON prefabs are read as ASSETS and never
        /// instantiated, so no weapon gameplay component wakes up. ⚠️ Throwable prefabs ARE
        /// instantiated (into <see cref="ThrowablePool"/>) but stay kinematic and disabled —
        /// <c>Throwable.Update</c> does nothing while the item is not armed.</summary>
        private static void CollectFromCatalogs(HashSet<GameObject> explosions,
            HashSet<GameObject> throwables, HashSet<GameObject> casings,
            Dictionary<VisualEffectAsset, string> graphs)
        {
            NetItemCatalog items = NetItemCatalog.Load();
            if (items != null)
            {
                CollectItems(items.Items, explosions, throwables, casings, graphs);
            }

            WeaponCatalog weapons = WeaponCatalog.Load();
            if (weapons != null)
            {
                CollectItems(weapons.Definitions, explosions, throwables, casings, graphs);
            }
        }

        private static void CollectItems(IReadOnlyList<ItemDefinition> definitions,
            HashSet<GameObject> explosions, HashSet<GameObject> throwables,
            HashSet<GameObject> casings, Dictionary<VisualEffectAsset, string> graphs)
        {
            if (definitions == null)
            {
                return;
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }

                if (definition is ThrowableDefinition throwable)
                {
                    if (throwable.ExplosionPrefab != null)
                    {
                        explosions.Add(throwable.ExplosionPrefab);
                    }

                    if (throwable.Prefab != null)
                    {
                        throwables.Add(throwable.Prefab);
                    }
                }

                GameObject prefab = definition.Prefab;
                if (prefab == null)
                {
                    continue;
                }

                var ejector = prefab.GetComponentInChildren<ShellEjector>(true);
                if (ejector != null && ejector.CasingPrefab != null)
                {
                    casings.Add(ejector.CasingPrefab);
                }

                var muzzle = prefab.GetComponentInChildren<WeaponMuzzleVfx>(true);
                VisualEffect effect = muzzle != null ? muzzle.Effect : null;
                if (effect != null && effect.visualEffectAsset != null)
                {
                    graphs[effect.visualEffectAsset] = muzzle.FireEventName;
                }
            }
        }

        /// <summary>A bare <c>VisualEffect</c> carrying only the graph: the weapon prefab itself is
        /// NOT instantiated, so no Weapon/grab/network component runs during a scene load.</summary>
        private static GameObject CreateGraphProbe(VisualEffectAsset asset, Vector3 position)
        {
            if (asset == null)
            {
                return null;
            }

            var go = new GameObject("[FxWarmup:" + asset.name + "]");
            go.transform.position = position;
            go.transform.localScale = Vector3.one * EffectScale;

            var effect = go.AddComponent<VisualEffect>();
            effect.visualEffectAsset = asset;
            return go;
        }

        private static void SendFireEvent(GameObject probe,
            Dictionary<VisualEffectAsset, string> graphs)
        {
            var effect = probe.GetComponent<VisualEffect>();
            if (effect == null || effect.visualEffectAsset == null)
            {
                return;
            }

            if (!graphs.TryGetValue(effect.visualEffectAsset, out string eventName) ||
                string.IsNullOrEmpty(eventName))
            {
                eventName = FallbackFireEvent;
            }

            effect.SendEvent(eventName);
        }
    }
}
