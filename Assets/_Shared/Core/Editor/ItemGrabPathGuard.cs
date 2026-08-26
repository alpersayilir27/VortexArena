using System.Collections.Generic;
using System.Text;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>Compares the RULE against the PREFAB: an item whose grab path is not
    /// <see cref="ItemGrabPath.DistanceGrab"/> must carry NO distance-grab component
    /// (<see cref="DistanceGrabInteractable"/> / <see cref="DistanceHandGrabInteractable"/>).</summary>
    /// <remarks>
    /// ⚠️ <b>Why the component's absence and not a filter:</b> closing ISDK's candidate list does not
    /// make an object ungrabbable. With an empty candidate list the interactor still enters hover
    /// (<c>Interactor.ShouldHover = HasCandidate || ComputeShouldSelect</c>) and <c>Select()</c>
    /// DEQUEUES the grip press without selecting anything — the press is silently eaten and the player
    /// only sees "sometimes the grab does not work". <c>WeaponFrame</c>'s filter is a DISTANCE rule, not
    /// an "is it grabbable" rule.
    /// <para>⚠️ This drift breaks no compile and throws nothing: a ketchup bottle accidentally
    /// obtainable by ray looks correct in the Inspector and is only found on site. Hence a build
    /// readiness row (<see cref="BuildReadiness"/>) — same pattern as <see cref="NetItemIdGuard"/>.</para>
    /// <para>⚠️ <b>WRITES NOTHING.</b> The fix is a human step (remove the component from the prefab, or
    /// correct the path in the definition) — a tool that stripped components would silently make an
    /// intentionally distance-grabbed weapon unobtainable.</para>
    /// </remarks>
    internal static class ItemGrabPathGuard
    {
        /// <summary>Are rule and prefab in agreement — the build readiness check.</summary>
        internal static bool ArePrefabsMatched(out string detail)
        {
            List<string> mismatches = Collect(out int checkedCount);

            if (mismatches.Count == 0)
            {
                detail = $"{checkedCount} eşyanın alma yolu prefabıyla uyumlu.";
                return true;
            }

            var sb = new StringBuilder();
            sb.Append($"{mismatches.Count} eşyada alma yolu ile prefab çelişiyor: ");
            sb.Append(mismatches[0]);
            if (mismatches.Count > 1)
            {
                sb.Append($" (+{mismatches.Count - 1} tane daha)");
            }

            detail = sb.ToString();
            return false;
        }

        /// <summary>Reports every mismatch to the console (the readiness row only shows the first).</summary>
        internal static void LogMismatches()
        {
            List<string> mismatches = Collect(out int checkedCount);

            if (mismatches.Count == 0)
            {
                Debug.Log($"[ItemGrabPathGuard] {checkedCount} eşya tarandı; alma yolu ile prefab uyumlu.");
                return;
            }

            for (int i = 0; i < mismatches.Count; i++)
            {
                Debug.LogError($"[ItemGrabPathGuard] {mismatches[i]}");
            }
        }

        /// <summary>Scans every <see cref="ItemDefinition"/> in the project; subtypes match
        /// <c>t:ItemDefinition</c> too, so a new item TYPE needs no change here.</summary>
        private static List<string> Collect(out int checkedCount)
        {
            var mismatches = new List<string>();
            checkedCount = 0;

            string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                ItemDefinition def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (def == null)
                {
                    continue;
                }

                checkedCount++;

                if (def.GrabPath == ItemGrabPath.DistanceGrab || def.Prefab == null)
                {
                    continue;
                }

                // Inactive children are included: a disabled interactable is still IN the prefab and a
                // later "why is this off?" cleanup re-enables it.
                bool hasController = def.Prefab.GetComponentInChildren<DistanceGrabInteractable>(true) != null;
                bool hasHand = def.Prefab.GetComponentInChildren<DistanceHandGrabInteractable>(true) != null;

                if (!hasController && !hasHand)
                {
                    continue;
                }

                string which = hasController && hasHand
                    ? "DistanceGrabInteractable + DistanceHandGrabInteractable"
                    : (hasController ? "DistanceGrabInteractable" : "DistanceHandGrabInteractable");

                mismatches.Add(
                    $"'{path}' alma yolu {def.GrabPath} ama '{def.Prefab.name}' prefabında {which} var — " +
                    "bileşen prefabdan KALDIRILMALI. ISDK'da 'alınamaz' filtreyle ifade edilemez: boş " +
                    "aday listesiyle bile interactor hover'a girer, Select() kavrama basışını kuyruktan " +
                    "düşürür ve basış sessizce yenir. Bu eşya uzaktan nişanla yanlışlıkla alınabilir " +
                    "ya da yakındaki kavraması sebepsiz çalışmaz.");
            }

            return mismatches;
        }
    }
}
