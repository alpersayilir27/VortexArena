using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VortexArena.Core.Combat;
using Object = UnityEngine.Object;

namespace VortexArena.Core.Editor
{
    /// <summary>Weapon kit builder: produces/updates <c>WD_&lt;Name&gt;.asset</c>
    /// (WeaponDefinition), the bindings and VFX of existing <c>WPN_&lt;Name&gt;.prefab</c>s,
    /// <c>FX_RemoteShot.prefab</c>, the front-grip indicator (<c>VA_GripSocket.prefab</c>) and
    /// <c>Resources/WeaponCatalog.asset</c>.
    /// <para>There is NO separate menu item: <c>Tools &gt; VortexArena &gt; Build &gt; Configure All
    /// Build Elements</c> runs <see cref="BuildAll"/> on every sync and its "Hazırlık" section shows
    /// the state (<see cref="AreWeaponsReady"/>). Adding a weapon to the table = syncing there. The
    /// run is idempotent: unchanged assets are rewritten with identical content (no diff).</para>
    /// <para>⚠️ WPN prefabs are NEVER created from scratch: the body (model hierarchy,
    /// Muzzle/MuzzleFlash/Eject placement) is hand-authored and lives in the repo; the tool updates
    /// it in place. A missing prefab is an error — silently generating a mis-placed weapon (e.g.
    /// lifting Muzzle out of Model to the root) broke recoil and aim.</para>
    /// <para>⚠️ No dialogs: <c>EditorUtility.DisplayDialog</c> would lock the pipeline, so all output
    /// goes to <c>Debug.Log/LogWarning/LogError</c>.</para>
    /// <para>Type resolution: this asmdef only references VortexArena.Core, so Weapon / WeaponAudio /
    /// WeaponDefinition bind at compile time while WeaponAnimator, WeaponReloadGesture,
    /// WeaponCatalog, TMPro.TextMeshPro, Oculus Grabbable and MetaXRAudioSource are found BY TYPE
    /// NAME at runtime — a missing type/field warns and continues (contract-drift diagnostics).</para>
    /// <para>Per-weapon feel: each weapon gets its own fire/reload/dry-fire clips
    /// (Assets/Audio/Weapons), its own muzzle flash (colour/size/cone) and muzzle smoke (a "Smoke"
    /// sub-particle system under MuzzleFlash, triggered as a sub-emitter). Every WPN also gets an
    /// <c>Eject</c> point + <see cref="ShellEjector"/> bound to the shared <c>Casing_*.prefab</c> for
    /// its calibre.</para>
    /// </summary>
    public static class WeaponKitBuilder
    {
        // ----------------------------------------------------------------- constants

        /// <summary>If the pack folder moves, only this line changes.</summary>
        private const string PackRoot = "Assets/ThirdPartyPackages/Low Poly AR Weapon Pack 1";

        private const string DataDir = "Assets/_Shared/Arsenal/Data";
        private const string PrefabDir = "Assets/_Shared/Arsenal/Prefabs";
        private const string FxDir = "Assets/_Shared/FX";
        private const string FxPrefabPath = FxDir + "/FX_RemoteShot.prefab";
        private const string SmokeMaterialPath = FxDir + "/M_MuzzleSmoke.mat";
        private const string CatalogDir = "Assets/_Shared/Data/Resources";

        /// <summary>Path of the weapon catalog — <c>internal</c> because the next sync step
        /// (<c>BuildElementsConfigurator.SyncModeLoadouts</c>) reads the catalog as this run's
        /// OUTPUT; repeating the path there would be a second source of truth.</summary>
        internal const string CatalogPath = CatalogDir + "/WeaponCatalog.asset";

        /// <summary>Weapon frame prefab — placed under every WPN as an INSTANCE
        /// (<see cref="ApplyWeaponFrameKit"/>). The tool only binds it, never creates it.</summary>
        private const string WeaponFramePrefabPath = PrefabDir + "/VA_WeaponFrame.prefab";

        /// <summary>Art of the front-grip SOCKET — one prefab shared by all weapons, placed by
        /// <c>Weapon</c> at the front-grip point (<c>WeaponCatalog.secondaryGripIndicatorPrefab</c>).
        /// <para>⚠️ Contract: the prefab is designed 1 m ACROSS (Unity's sphere primitive) and
        /// <c>Weapon</c> scales it to twice the acceptance radius, so the drawn sphere IS exactly the
        /// acceptance volume. Binding visual and rule to different numbers would let a grip be
        /// refused where the player is told they are inside.</para>
        /// <para>The tool creates the prefab ONLY if missing (<see cref="EnsureGripSocketPrefab"/>:
        /// translucent light-blue sphere) and binds it to the catalog ONLY if the field is empty, so
        /// neither an edited sphere nor a different prefab is overwritten.</para></summary>
        private const string GripSocketPrefabPath = PrefabDir + "/VA_GripSocket.prefab";
        private const string GripSocketMaterialDir = "Assets/_Shared/Materials";
        private const string GripSocketMaterialPath = GripSocketMaterialDir + "/M_GripSocket.mat";

        /// <summary>Colour of the default socket sphere, written only on first creation; after that
        /// the material owns it. At runtime <c>Weapon</c> drives only the alpha.</summary>
        private static readonly Color GripSocketColor = new Color(0.55f, 0.82f, 1f, 0.50f);

        /// <summary>Shader search chain for the sphere material (first hit wins), starting with the
        /// project's own <c>_Shared/Shaders/GripSocket.shader</c> (transparency, double-sided and
        /// decoration are written in its passes); if that is gone it falls back to URP Unlit and
        /// transparency is configured below by known property names. The material is an ASSET, so the
        /// shader is guaranteed into the build.</summary>
        private static readonly string[] GripSocketShaderCandidates =
        {
            "VortexArena/GripSocket",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Lit",
            "Sprites/Default",
        };

        /// <summary>Dissolve material played as the weapon arrives in the hand — bound to the
        /// <see cref="SimpleWeaponDissolve"/> on every WPN root (<see cref="ApplyDissolveKit"/>).
        /// The tool only binds it.</summary>
        private const string DissolveMaterialPath = "Assets/_Shared/Materials/DissolveEffect.mat";

        // Dissolve duration. ⚠️ Edited in a prefab it is WRITTEN BACK on the next run (same rule as
        // the balance numbers) — this line is the permanent setting; experiment in Play mode, then
        // paste the value here.
        // ⚠️ The material field is OUTSIDE this rule (written only when empty): binding a different
        // dissolve material to one weapon is a deliberate choice.
        // ⚠️ The effect's LOOK (edge, pattern, axis) is not here and is not added: its single source
        // is the material, the component only drives _Dissolve.
        private const float DissolveAppearSeconds = 1.2f;

        private const float CasingMassKg = 0.01f;

        /// <summary>Name of the leftover grip-pose node tree inside a prefab — dead data, deleted
        /// (<see cref="RemoveLegacyGripPoseNodes"/>).
        /// <para>⚠️ The constant lives HERE because this cleanup is its only reader: nothing produces
        /// or consumes the node. Moving it to runtime would put a name nobody reads in shared code.
        /// (The studio hand prefix is the opposite — its PRODUCER is alive, so it is defined at
        /// <see cref="GripPoseStudio.HAND_ROOT_PREFIX"/>.)</para></summary>
        private const string LegacyGripPoseRootName = "GripPoses";

        /// <summary>Casing families: <c>WeaponSpec.CasingFamily</c> → (casing prefab to build, pack
        /// bullet model). A new calibre = one line here; the prefab is built on the first run and
        /// then left alone.
        /// <para>⚠️ A family is a VISUAL distinction, not a balance lever: for a 1 cm object that
        /// lives two seconds, pooling the pistol calibres (9x19 · .45 ACP · 5.7x28) into one casing
        /// is deliberate — splitting them would cost three assets for an invisible
        /// difference.</para></summary>
        private static readonly Dictionary<string, (string CasingPath, string PackBulletPath)> CasingFamilies =
            new Dictionary<string, (string, string)>
            {
                ["762x39"] = (PrefabDir + "/Casing_762x39.prefab", PackRoot + "/Prefabs/Bullets/Bullet_A.prefab"),
                ["556x45"] = (PrefabDir + "/Casing_556x45.prefab", PackRoot + "/Prefabs/Bullets/Bullet_B.prefab"),
                ["9x19"] = (PrefabDir + "/Casing_9x19.prefab", PackRoot + "/Prefabs/Bullets/Bullet_SMG_A.prefab"),
                ["12gauge"] = (PrefabDir + "/Casing_12gauge.prefab", PackRoot + "/Prefabs/Bullets/Bullet_ShotGun_A.prefab"),
            };

        private const string Log = "[WeaponKit] ";

        // Numbers shared by all weapons (table-header defaults).
        //
        // ⚠️ The headshot multiplier is OVERRIDABLE per row (`WeaponSpec.Headshot`) and is overridden
        // for shotguns: the multiplier applies PER PELLET, so 4× would make a single 26-damage pellet
        // instantly lethal — including a stray one from 8 m in a 9-pellet cone. CS softens this with
        // helmets; there is NO armour here.
        private const float DefaultHeadshotMultiplier = 4f;

        // Zone multipliers (CS2 model): arms count as BODY, so 1× needs no separate constant.
        // ⚠️ This table is the single source of the balance numbers — a value edited in a
        // WD_*.asset Inspector is WRITTEN BACK on the next run.
        private const float StomachMultiplier = 1.25f;
        private const float LegMultiplier = 0.75f;
        private const float KickBackMeters = 0.02f;
        private const float RecoilRecoverSpeed = 10f;
        private const float PitchJitter = 0.05f;

        /// <summary>Reserve rule for a weapon whose row omits <c>ReserveMode</c>.</summary>
        private const string DefaultReserveModeName = "DiscardMagazine";

        /// <summary>Spare magazine count for a weapon whose row omits <c>SpareMags</c>.</summary>
        private const int DefaultSpareMagazines = 2;

        // ------------------------------------------------------------ weapon table

        private struct WeaponSpec
        {
            public string Name;        // file suffix: WD_<Name>, WPN_<Name>

            /// PackRoot/Prefabs/Weapons/<PackPrefab>.prefab — NO LONGER READ during the build (WPN
            /// prefabs are updated in place, not rebuilt from the model); kept as a record of which
            /// pack model each weapon came from.
            public string PackPrefab;
            public string WeaponId;
            public string DisplayName;

            /// ⚠️ Network id on the wire (Docs/ArenaNet-Protokol.md §6.6) — this byte goes in the
            /// snapshot and must be STABLE: reordering or deleting a row must NOT change the ids of
            /// the remaining weapons (which is why the catalog array index is not the id). New weapon
            /// = an unused number; a deleted weapon's number is never reused. 0 is invalid (reserved
            /// for "empty hand" in §6.6).
            public int NetItemId;

            /// ItemHoldMode name: "OneHand" (pistol/grenade) | "TwoHand" (rifle).
            public string HoldMode;

            /// ⚠️ For shotguns this is damage PER PELLET, not per trigger pull (CS2 model): total
            /// damage comes from how many pellets connect.
            public int Damage;

            /// Headshot multiplier. <b>0 = default</b> (<see cref="DefaultHeadshotMultiplier"/>).
            /// Filled only for shotguns — rationale next to that constant.
            public float Headshot;

            public int Rpm;
            public int Magazine;
            public float Reload;

            /// Rays per trigger pull. <b>0 or 1 = normal weapon</b>; filled only for shotguns
            /// (XM1014 6, Nova 9).
            public int Pellets;

            /// Spare magazines. <b>0 = default</b> (<see cref="DefaultSpareMagazines"/>). CS2's
            /// reserve ammo divided by magazine size (P90 100/50 = 2).
            public int SpareMags;

            /// <c>WeaponReserveMode</c> name. <b>null = default</b>
            /// (<see cref="DefaultReserveModeName"/>, magazine based). Shell-by-shell loaders use
            /// <c>"PoolRounds"</c> so an early reload does not burn the chambered round.
            public string ReserveMode;

            /// Hitscan range (metres). ⚠️ NOT a balance lever: the distance wall is sharp (one
            /// centimetre further and damage is ZERO), so it cannot stand in for a continuous curve —
            /// the lever is <see cref="BaseSpread"/>. The ordering preserves CS's "range modifier"
            /// identity (longer barrel reaches further) and starts to matter in larger venues; the
            /// current band is 18-50 m while a 12×12 arena's longest line is ~17 m. Distance is
            /// actually felt through SPREAD.
            public float Range;
            public float BaseSpread;
            public float BloomPerShot;
            public float MaxBloom;
            public float BloomRecovery;
            public float Kick;

            /// Fire sound pitch. ⚠️ Deviating from 1.00 exists ONLY to mask a borrowed clip (e.g.
            /// thickening an AK sample for a shotgun): once the weapon gets its own audio file this
            /// goes back to 1.00, otherwise the real sound plays at the wrong pitch.
            public float PitchBase;

            public float Volume;
            public Color FlashColorMin;
            public Color FlashColorMax;
            public float FlashSizeMin;
            public float FlashSizeMax;
            public float FlashLifetime;
            public float FlashConeAngle;
            public float SmokeSizeMin;
            public float SmokeSizeMax;
            public float SmokeLifetime;
            public float SmokeAlpha;
            public string CasingFamily; // "762x39" | "556x45"
        }

        // ⚠️ The MODEL ↔ IDENTITY link lives in this table and PackPrefab never leaves its row. The
        // pack ships generic model names (AR_A_1, AR_B …) and which model resembles which real
        // weapon was matched BY EYE. Changing a row's PackPrefab means "change this weapon's model";
        // to move stats, move the rest of the row, not PackPrefab/NetItemId.
        //
        // Balance source: weapons with a CS:GO/CS2 counterpart (AK-47, M4A4/M4A1, FAMAS) come
        // straight from there; the rest (SCAR-L, G36C) from PUBG + real-world data.
        //
        // ⚠️ `Reload` is the length of the weapon's reload SOUND, so the trigger reopens
        // (`Weapon.reloadEndTime`) exactly when audio and magazine animation end. Change the clip and
        // this number changes too, otherwise the player feels "the sound finished but I cannot
        // shoot". For weapons with no reload sound of their own it is a pure balance value.
        private static readonly WeaponSpec[] Specs =
        {
            // AR_M — CS:GO M4A4 body: balanced, medium recoil, the baseline 5.56.
            new WeaponSpec
            {
                Name = "M4A4", PackPrefab = "AR_M", WeaponId = "m4a4", DisplayName = "M4A4",
                NetItemId = 1, HoldMode = "TwoHand",
                Damage = 33, Rpm = 666, Magazine = 30, Reload = 2.19f,
                Range = 40f, BaseSpread = 0.50f, BloomPerShot = 0.26f,
                MaxBloom = 2.2f, BloomRecovery = 4.5f, Kick = 2.0f, PitchBase = 1.00f, Volume = 1.0f,                FlashColorMin = new Color(1f, 0.92f, 0.72f), FlashColorMax = new Color(1f, 0.65f, 0.32f),
                FlashSizeMin = 0.035f, FlashSizeMax = 0.065f, FlashLifetime = 0.06f, FlashConeAngle = 24f,
                SmokeSizeMin = 0.035f, SmokeSizeMax = 0.06f, SmokeLifetime = 1.0f, SmokeAlpha = 0.25f,
                CasingFamily = "556x45",
            },
            // AR_B — CS:GO AK-47: highest single body hit, headshot king, harshest recoil. Being
            // 7.62x39 it also has its own casing family.
            new WeaponSpec
            {
                Name = "AK47", PackPrefab = "AR_B", WeaponId = "ak47", DisplayName = "AK-47",
                NetItemId = 2, HoldMode = "TwoHand",
                Damage = 36, Rpm = 600, Magazine = 30, Reload = 2.43f,
                Range = 45f, BaseSpread = 0.60f, BloomPerShot = 0.32f,
                MaxBloom = 2.6f, BloomRecovery = 4.0f, Kick = 2.6f, PitchBase = 1.00f, Volume = 1.0f,                FlashColorMin = new Color(1f, 0.55f, 0.15f), FlashColorMax = new Color(1f, 0.22f, 0.05f),
                FlashSizeMin = 0.05f, FlashSizeMax = 0.09f, FlashLifetime = 0.09f, FlashConeAngle = 34f,
                SmokeSizeMin = 0.05f, SmokeSizeMax = 0.09f, SmokeLifetime = 1.4f, SmokeAlpha = 0.35f,
                CasingFamily = "762x39",
            },
            // AR_C — PUBG SCAR-L: the easiest 5.56 to control (lowest recoil, slowest bloom growth,
            // fastest recovery) at the cost of the lowest DPS.
            // ⚠️ NO suppressor — the muted sound/flash values of the old M4A1-S row are gone by choice.
            new WeaponSpec
            {
                Name = "SCARL", PackPrefab = "AR_C", WeaponId = "scarl", DisplayName = "SCAR-L",
                NetItemId = 3, HoldMode = "TwoHand",
                Damage = 32, Rpm = 625, Magazine = 30, Reload = 2.06f,
                Range = 38f, BaseSpread = 0.45f, BloomPerShot = 0.20f,
                MaxBloom = 1.8f, BloomRecovery = 5.0f, Kick = 1.6f, PitchBase = 1.00f, Volume = 1.0f,                FlashColorMin = new Color(1f, 0.85f, 0.55f), FlashColorMax = new Color(1f, 0.55f, 0.22f),
                FlashSizeMin = 0.038f, FlashSizeMax = 0.068f, FlashLifetime = 0.065f, FlashConeAngle = 26f,
                SmokeSizeMin = 0.035f, SmokeSizeMax = 0.06f, SmokeLifetime = 1.0f, SmokeAlpha = 0.28f,
                CasingFamily = "556x45",
            },
            // AR_D — PUBG G36C: highest fire rate (750 rpm), lowest per-bullet damage, shortest
            // range. A close-quarters suppression weapon.
            new WeaponSpec
            {
                Name = "G36C", PackPrefab = "AR_D", WeaponId = "g36c", DisplayName = "G36C",
                NetItemId = 4, HoldMode = "TwoHand",
                Damage = 29, Rpm = 750, Magazine = 30, Reload = 2.43f,
                Range = 28f, BaseSpread = 0.70f, BloomPerShot = 0.30f,
                MaxBloom = 2.6f, BloomRecovery = 4.2f, Kick = 1.9f, PitchBase = 1.00f, Volume = 0.95f,                FlashColorMin = new Color(1f, 0.80f, 0.45f), FlashColorMax = new Color(1f, 0.45f, 0.15f),
                FlashSizeMin = 0.042f, FlashSizeMax = 0.075f, FlashLifetime = 0.07f, FlashConeAngle = 28f,
                SmokeSizeMin = 0.04f, SmokeSizeMax = 0.07f, SmokeLifetime = 1.1f, SmokeAlpha = 0.30f,
                CasingFamily = "556x45",
            },
            // AR_E — CS:GO FAMAS: values verified against CS:GO (30 dmg / 666 rpm / 25 mag / 3.30 s).
            // ⚠️ The 25-round magazine is deliberate (that is CS:GO's), which is why it differs from
            // the others' 30. Burst mode is not modelled.
            new WeaponSpec
            {
                Name = "FAMAS", PackPrefab = "AR_E", WeaponId = "famas", DisplayName = "FAMAS",
                NetItemId = 5, HoldMode = "TwoHand",
                Damage = 30, Rpm = 666, Magazine = 25, Reload = 2.38f,
                Range = 32f, BaseSpread = 0.65f, BloomPerShot = 0.28f,
                MaxBloom = 2.4f, BloomRecovery = 4.2f, Kick = 1.9f, PitchBase = 1.03f, Volume = 1.0f,                FlashColorMin = new Color(1f, 0.88f, 0.58f), FlashColorMax = new Color(1f, 0.52f, 0.18f),
                FlashSizeMin = 0.03f, FlashSizeMax = 0.055f, FlashLifetime = 0.06f, FlashConeAngle = 20f,
                SmokeSizeMin = 0.035f, SmokeSizeMax = 0.06f, SmokeLifetime = 1.0f, SmokeAlpha = 0.28f,
                CasingFamily = "556x45",
            },
            // AR_A_1 — M4A1: the "marksman" M4A4. Tightest base spread and longest range, paid for
            // with the fastest-degrading sustained fire (highest bloom, slowest recovery): rewards
            // aimed single shots, punishes spraying. Shares M4A4's reload clip at a lower pitch.
            new WeaponSpec
            {
                Name = "M4A1", PackPrefab = "AR_A_1", WeaponId = "m4a1", DisplayName = "M4A1",
                NetItemId = 6, HoldMode = "TwoHand",
                Damage = 31, Rpm = 700, Magazine = 30, Reload = 2.19f,
                Range = 50f, BaseSpread = 0.35f, BloomPerShot = 0.34f,
                MaxBloom = 2.8f, BloomRecovery = 3.8f, Kick = 2.3f, PitchBase = 0.93f, Volume = 1.0f,                FlashColorMin = new Color(1f, 0.90f, 0.74f), FlashColorMax = new Color(0.95f, 0.60f, 0.28f),
                FlashSizeMin = 0.032f, FlashSizeMax = 0.058f, FlashLifetime = 0.062f, FlashConeAngle = 19f,
                SmokeSizeMin = 0.032f, SmokeSizeMax = 0.055f, SmokeLifetime = 0.95f, SmokeAlpha = 0.26f,
                CasingFamily = "556x45",
            },
            // AR_O — CS2 AUG: values match CS2 (28 dmg / 666 rpm / 30 mag / 3.80 s / 0.98 range
            // modifier → AK's range class). Its bullpup+scope identity lives in the spread: tighter
            // base than SCAR-L, among the slowest bloom growth and low recoil — paid for with the
            // lowest 5.56 DPS and the longest reload.
            new WeaponSpec
            {
                Name = "AUG", PackPrefab = "AR_O", WeaponId = "aug", DisplayName = "AUG",
                NetItemId = 7, HoldMode = "TwoHand",
                Damage = 28, Rpm = 666, Magazine = 30, Reload = 2.19f,
                Range = 46f, BaseSpread = 0.42f, BloomPerShot = 0.22f,
                MaxBloom = 2.0f, BloomRecovery = 4.8f, Kick = 1.7f, PitchBase = 0.98f, Volume = 1.0f,                FlashColorMin = new Color(1f, 0.87f, 0.60f), FlashColorMax = new Color(1f, 0.58f, 0.24f),
                FlashSizeMin = 0.033f, FlashSizeMax = 0.060f, FlashLifetime = 0.062f, FlashConeAngle = 22f,
                SmokeSizeMin = 0.034f, SmokeSizeMax = 0.058f, SmokeLifetime = 1.0f, SmokeAlpha = 0.27f,
                CasingFamily = "556x45",
            },
            // AR_L — CS2 Galil AR: 30 dmg / 666 rpm / 35 mag / 3.00 s / 0.98 range modifier. The
            // "cheap AK": AK's range class with a weaker single hit, but faster and with the longest
            // burst thanks to 35 rounds. Paid for with the widest base spread among the 5.56s.
            new WeaponSpec
            {
                Name = "GALIL", PackPrefab = "AR_L", WeaponId = "galilar", DisplayName = "Galil AR",
                NetItemId = 8, HoldMode = "TwoHand",
                Damage = 30, Rpm = 666, Magazine = 35, Reload = 2.25f, SpareMags = 2,
                Range = 44f, BaseSpread = 0.62f, BloomPerShot = 0.34f,
                MaxBloom = 2.8f, BloomRecovery = 3.8f, Kick = 2.5f, PitchBase = 1.02f, Volume = 1.0f,                FlashColorMin = new Color(1f, 0.78f, 0.42f), FlashColorMax = new Color(1f, 0.42f, 0.12f),
                FlashSizeMin = 0.045f, FlashSizeMax = 0.082f, FlashLifetime = 0.075f, FlashConeAngle = 30f,
                SmokeSizeMin = 0.042f, SmokeSizeMax = 0.072f, SmokeLifetime = 1.2f, SmokeAlpha = 0.32f,
                CasingFamily = "556x45",
            },
            // SMG_O — CS2 P90: 26 dmg / 857 rpm / 50 mag / 3.40 s / 0.84 range modifier. 50 rounds +
            // the lowest recoil = a spray weapon; paid for in range and per-bullet damage.
            new WeaponSpec
            {
                Name = "P90", PackPrefab = "SMG_O", WeaponId = "p90", DisplayName = "P90",
                NetItemId = 9, HoldMode = "TwoHand",
                Damage = 26, Rpm = 857, Magazine = 50, Reload = 2.80f, SpareMags = 2,
                Range = 24f, BaseSpread = 0.85f, BloomPerShot = 0.24f,
                MaxBloom = 2.6f, BloomRecovery = 5.5f, Kick = 1.2f, PitchBase = 1.00f, Volume = 0.92f,                FlashColorMin = new Color(1f, 0.90f, 0.68f), FlashColorMax = new Color(1f, 0.62f, 0.28f),
                FlashSizeMin = 0.026f, FlashSizeMax = 0.048f, FlashLifetime = 0.05f, FlashConeAngle = 26f,
                SmokeSizeMin = 0.028f, SmokeSizeMax = 0.048f, SmokeLifetime = 0.85f, SmokeAlpha = 0.22f,
                CasingFamily = "9x19",
            },
            // SMG_M — CS2 MP9: 26 dmg / 857 rpm / 30 mag / 2.10 s / 0.75 range modifier. The fastest
            // reload and shortest range in the game: an angle-holding, frequently reloading weapon.
            new WeaponSpec
            {
                Name = "MP9", PackPrefab = "SMG_M", WeaponId = "mp9", DisplayName = "MP9",
                NetItemId = 10, HoldMode = "TwoHand",
                Damage = 26, Rpm = 857, Magazine = 30, Reload = 2.14f, SpareMags = 4,
                Range = 18f, BaseSpread = 0.90f, BloomPerShot = 0.26f,
                MaxBloom = 2.8f, BloomRecovery = 5.8f, Kick = 1.1f, PitchBase = 1.00f, Volume = 0.90f,                FlashColorMin = new Color(1f, 0.92f, 0.72f), FlashColorMax = new Color(1f, 0.66f, 0.32f),
                FlashSizeMin = 0.024f, FlashSizeMax = 0.044f, FlashLifetime = 0.048f, FlashConeAngle = 28f,
                SmokeSizeMin = 0.026f, SmokeSizeMax = 0.045f, SmokeLifetime = 0.8f, SmokeAlpha = 0.20f,
                CasingFamily = "9x19",
            },
            // SMG_L — CS2 UMP-45: 35 dmg / 666 rpm / 25 mag / 3.50 s / 0.82 range modifier. Hardest
            // hitting SMG (higher per-bullet damage than some rifles) but the slowest; 25 rounds
            // forgive nothing.
            new WeaponSpec
            {
                Name = "UMP45", PackPrefab = "SMG_L", WeaponId = "ump45", DisplayName = "UMP-45",
                NetItemId = 11, HoldMode = "TwoHand",
                Damage = 35, Rpm = 666, Magazine = 25, Reload = 2.14f, SpareMags = 4,
                Range = 22f, BaseSpread = 0.75f, BloomPerShot = 0.30f,
                MaxBloom = 2.6f, BloomRecovery = 4.6f, Kick = 1.9f, PitchBase = 1.00f, Volume = 0.96f,                FlashColorMin = new Color(1f, 0.72f, 0.34f), FlashColorMax = new Color(1f, 0.38f, 0.10f),
                FlashSizeMin = 0.032f, FlashSizeMax = 0.058f, FlashLifetime = 0.058f, FlashConeAngle = 30f,
                SmokeSizeMin = 0.032f, SmokeSizeMax = 0.055f, SmokeLifetime = 1.0f, SmokeAlpha = 0.26f,
                CasingFamily = "9x19",
            },
            // ShotGun_C — CS2 XM1014 body: 171 rpm / 7 shells. Semi-auto: faster and more forgiving
            // than the Nova, weaker per shot.
            // ⚠️ CS reloads shell by shell; here the TOTAL time for a full magazine is written (per
            // shell loading is not modelled) — hence `PoolRounds`, so an early reload does not burn
            // the chambered shell.
            // ⚠️ Damage and spread DEVIATE from CS (CS: 20 dmg, ~5° cone, 0.70 range modifier)
            // because of arena scale — the rationale is stated once, on the NOVA row below.
            new WeaponSpec
            {
                Name = "XM1014", PackPrefab = "ShotGun_C", WeaponId = "xm1014", DisplayName = "XM1014",
                NetItemId = 12, HoldMode = "TwoHand",
                Damage = 10, Headshot = 2f, Rpm = 171, Magazine = 7, Reload = 4.50f, Pellets = 6,
                SpareMags = 4, ReserveMode = "PoolRounds",
                Range = 26f, BaseSpread = 10.0f, BloomPerShot = 0.60f,
                MaxBloom = 1.5f, BloomRecovery = 2.5f, Kick = 3.2f, PitchBase = 1.00f, Volume = 1.0f,                FlashColorMin = new Color(1f, 0.72f, 0.30f), FlashColorMax = new Color(1f, 0.32f, 0.06f),
                FlashSizeMin = 0.075f, FlashSizeMax = 0.130f, FlashLifetime = 0.10f, FlashConeAngle = 46f,
                SmokeSizeMin = 0.075f, SmokeSizeMax = 0.125f, SmokeLifetime = 1.7f, SmokeAlpha = 0.42f,
                CasingFamily = "12gauge",
            },
            // ShotGun_B — CS2 Nova body: 68 rpm / 8 shells. Pump action: a one-shot kill at contact
            // range, but a miss leaves a very long gap.
            //
            // ⚠️ SHOTGUN BALANCE DELIBERATELY DEVIATES FROM CS (CS Nova: 26 dmg, ~6° cone, 0.70 range
            // modifier; here 13 dmg, 12° cone, no distance curve). The reason is ARENA SCALE, and
            // adding CS's curve does not fix it: CS tunes shotguns assuming "3 m is a rare distance"
            // (its 0.70 coefficient bites around 9.5 m), while a 12×12 free-roam arena's longest line
            // is ~17 m — so CS's formula only halves the Nova across the arena and never touches
            // contact-range damage. Here SPREAD carries the distance falloff
            // (Docs/Sistem-Ozeti.md §7): a wider cone means fewer pellets land, so the tuning knobs
            // are base damage + cone angle. ⚠️ `Range` is NOT that knob (see its own field note): a
            // sharp distance wall cannot stand in for a continuous curve.
            //
            // ⚠️ THE ANGLE IN THIS TABLE IS RAW: the field value is always this times a grip factor.
            // Two-handed (the reference) it is `twoHandSpreadMultiplier` (0.45), so 12° here is a 5.4°
            // cone; one-handed the asset's `oneHandSpreadMultiplier` stacks on that. Reading the angle
            // straight off the table makes the weapon look about twice as tight as it plays.
            new WeaponSpec
            {
                Name = "NOVA", PackPrefab = "ShotGun_B", WeaponId = "nova", DisplayName = "Nova",
                NetItemId = 13, HoldMode = "TwoHand",
                Damage = 13, Headshot = 2f, Rpm = 68, Magazine = 8, Reload = 5.00f, Pellets = 9,
                SpareMags = 4, ReserveMode = "PoolRounds",
                Range = 25f, BaseSpread = 12.0f, BloomPerShot = 0.60f,
                MaxBloom = 1.5f, BloomRecovery = 2.5f, Kick = 4.0f, PitchBase = 1.00f, Volume = 1.0f,                FlashColorMin = new Color(1f, 0.68f, 0.26f), FlashColorMax = new Color(1f, 0.28f, 0.05f),
                FlashSizeMin = 0.085f, FlashSizeMax = 0.150f, FlashLifetime = 0.115f, FlashConeAngle = 50f,
                SmokeSizeMin = 0.085f, SmokeSizeMax = 0.140f, SmokeLifetime = 1.9f, SmokeAlpha = 0.46f,
                CasingFamily = "12gauge",
            },
        };

        private enum BuildOutcome { Rebound, Failed }

        private static int _warnings;

        // How many legacy grip authoring leftovers were deleted (GripSocket_Primary/Secondary markers
        // and the Hands/Hand_* rig). Authoring moved to the grip studio; nobody reads these nodes and
        // leaving them tells the next reader "this is still adjustable here".
        private static int _legacyNodesRemoved;

        // Weapons with no authored grip (their hand stays idle in game). ⚠️ This report is
        // MANDATORY: authoring a grip is a one-off human step and skipping it logs nothing — the
        // only symptom is "the hand does not wrap the weapon".
        private static readonly List<string> _unbakedWeapons = new List<string>();

        private static readonly Dictionary<string, Type> ResolvedTypes = new Dictionary<string, Type>();

        // --------------------------------------------------------------- entry point

        /// <summary>Full flow: WD assets → WPN prefab updates → FX + indicator → catalog → second
        /// pass. Returns a one-line summary for the sync report; details go to the console. Called by
        /// <c>BuildElementsConfigurator.SyncAll</c>; there is no menu item.</summary>
        public static string BuildAll()
        {
            _warnings = 0;
            _legacyNodesRemoved = 0;
            _unbakedWeapons.Clear();

            int wdNew = 0;
            int wpnRebound = 0, wpnFailed = 0;
            bool fxCreated = false;
            bool indicatorCreated = false;
            bool catalogCreated = false;
            string summary;

            EnsureFolder(DataDir);
            EnsureFolder(PrefabDir);
            EnsureFolder(FxDir);
            EnsureFolder(CatalogDir);

            // Every temporary instance is tracked here so an early error leaves no junk GameObjects.
            var live = new List<GameObject>();
            try
            {
                // ---- STEP 1: WeaponDefinition assets (the prefab field is bound in STEP 5).
                var defs = new WeaponDefinition[Specs.Length];
                for (int i = 0; i < Specs.Length; i++)
                {
                    defs[i] = EnsureDefinition(Specs[i], ref wdNew);
                }

                AssetDatabase.SaveAssets();

                // ---- STEP 2: WPN prefabs (casing/smoke source assets first, the WPNs depend on
                // them). Only families PRESENT IN THE TABLE are built: creating an asset for an
                // unused calibre would leave a dead file nobody can explain later.
                var casings = new Dictionary<string, GameObject>();
                for (int i = 0; i < Specs.Length; i++)
                {
                    string family = Specs[i].CasingFamily;
                    if (string.IsNullOrEmpty(family) || casings.ContainsKey(family))
                    {
                        continue;
                    }

                    if (!CasingFamilies.TryGetValue(family, out var source))
                    {
                        Warn(Specs[i].Name + ": '" + family + "' kovan ailesi CasingFamilies'te yok — " +
                             "kovan bağlanamayacak.");
                        continue;
                    }

                    casings[family] = EnsureCasingPrefab(source.CasingPath, source.PackBulletPath, live);
                }

                Material smokeMaterial = EnsureMuzzleSmokeMaterial();

                for (int i = 0; i < Specs.Length; i++)
                {
                    if (defs[i] == null)
                    {
                        wpnFailed++;
                        continue;
                    }

                    try
                    {
                        switch (BuildWeaponPrefab(Specs[i], defs[i], casings, smokeMaterial))
                        {
                            case BuildOutcome.Rebound: wpnRebound++; break;
                            default: wpnFailed++; break;
                        }
                    }
                    catch (Exception e)
                    {
                        wpnFailed++;
                        Debug.LogError(Log + Specs[i].Name + ": prefab güncellemesi hata verdi — " + e);
                    }
                }

                // ---- STEP 3: remote-shot FX prefab + front-grip indicator (left alone if present).
                fxCreated = EnsureRemoteShotFx(live);
                indicatorCreated = EnsureGripSocketPrefab(live);

                // ---- STEP 4: WeaponCatalog.
                catalogCreated = UpdateCatalog();

                // ---- STEP 5: WD.prefab ← WPN, second pass.
                LinkDefinitionPrefabs();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                summary = "silah kiti: WD " + wdNew + " yeni / " + (Specs.Length - wdNew) + " güncellendi · " +
                          "WPN " + wpnRebound + " güncellendi, " + wpnFailed + " başarısız · " +
                          "FX_RemoteShot " + (fxCreated ? "üretildi" : "mevcut") + " · " +
                          "VA_GripSocket " + (indicatorCreated ? "üretildi" : "mevcut") + " · " +
                          "WeaponCatalog " + (catalogCreated ? "üretildi" : "güncellendi") + " · " +
                          "eski kavrama düğümü " + _legacyNodesRemoved + " silindi · " +
                          _warnings + " uyarı.";
                Debug.Log(Log + summary);

                ReportUnbakedWeapons();
                ReportSilentWeapons(defs);
            }
            finally
            {
                for (int i = 0; i < live.Count; i++)
                {
                    if (live[i] != null)
                    {
                        Object.DestroyImmediate(live[i]);
                    }
                }
            }

            return summary;
        }

        // ------------------------------------------------------- STEP 1: WD assets

        /// <summary>Creates WD_&lt;Name&gt;.asset if missing and writes the fields by contract name.</summary>
        private static WeaponDefinition EnsureDefinition(WeaponSpec spec, ref int createdCount)
        {
            string path = DataDir + "/WD_" + spec.Name + ".asset";
            string ctx = "WD_" + spec.Name;

            var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (def == null)
            {
                // Another asset type at this path: do not overwrite — CreateAsset kills its GUID.
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                {
                    Warn(ctx + ": '" + path + "' yolunda WeaponDefinition olmayan bir asset var — dokunulmadı.");
                    return null;
                }

                def = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(def, path);
                createdCount++;
            }

            var so = new SerializedObject(def);

            SetString(so, "weaponId", spec.WeaponId, ctx);
            SetString(so, "displayName", spec.DisplayName, ctx);
            // §6.6: network id + hold mode. The table is their source of truth and OVERWRITES on
            // every run — editing the id in the Inspector does not stick, edit the table.
            SetNumber(so, "netItemId", spec.NetItemId, ctx);
            SetEnumByName(so, "holdMode", spec.HoldMode, ctx);
            SetNumber(so, "damage", spec.Damage, ctx);
            SetNumber(so, "headshotMultiplier",
                spec.Headshot > 0f ? spec.Headshot : DefaultHeadshotMultiplier, ctx);
            SetNumber(so, "stomachMultiplier", StomachMultiplier, ctx);
            SetNumber(so, "legMultiplier", LegMultiplier, ctx);
            SetNumber(so, "fireRateRpm", spec.Rpm, ctx);
            SetNumber(so, "range", spec.Range, ctx);
            SetNumber(so, "pelletCount", spec.Pellets > 0 ? spec.Pellets : 1, ctx);
            SetNumber(so, "baseSpreadDegrees", spec.BaseSpread, ctx);
            SetNumber(so, "bloomPerShotDegrees", spec.BloomPerShot, ctx);
            SetNumber(so, "maxBloomDegrees", spec.MaxBloom, ctx);
            SetNumber(so, "bloomRecoveryPerSecond", spec.BloomRecovery, ctx);
            SetNumber(so, "kickDegrees", spec.Kick, ctx);
            SetNumber(so, "kickBackMeters", KickBackMeters, ctx);
            SetNumber(so, "recoilRecoverSpeed", RecoilRecoverSpeed, ctx);
            // ⚠️ oneHandSpreadMultiplier / oneHandRecoilMultiplier / oneHandRecoveryPenalty are
            // deliberately NOT written and not added to this table: how a weapon behaves in one hand
            // is found by holding it in the headset, and the WD asset is its only home. A line here
            // would overwrite that hand-found value on every run — same rule as the haptic fields.
            // The cost: a new weapon is born with the class defaults (see WeaponDefinition).
            SetNumber(so, "magazineSize", spec.Magazine, ctx);
            SetNumber(so, "spareMagazines", spec.SpareMags > 0 ? spec.SpareMags : DefaultSpareMagazines, ctx);
            SetEnumByName(so, "reserveMode",
                string.IsNullOrEmpty(spec.ReserveMode) ? DefaultReserveModeName : spec.ReserveMode, ctx);
            SetNumber(so, "reloadTime", spec.Reload, ctx);
            // ⚠️ AUDIO CLIPS DO NOT COME FROM THIS TABLE and the tool never touches them — the single
            // source for the five clip fields (fireClips · magOutClip · magInClip · dryFireClip ·
            // pickupClip) is the WD_<Name>.asset Inspector, where clips are dragged by hand.
            // Same rationale as the haptic fields below: sound is chosen by ear, and writing file
            // names in code makes it managed from two places. Kept in the table the rule would have
            // to be "write only when empty", so changing a sound would first require clearing the
            // asset field — and anyone not knowing that reads it as "my change did not land".
            // The cost: a new weapon is born SILENT. ReportSilentWeapons lists it at the end.
            SetNumber(so, "firePitchBase", spec.PitchBase, ctx);
            SetNumber(so, "firePitchJitter", PitchJitter, ctx);
            SetNumber(so, "fireVolume", spec.Volume, ctx);
            // ⚠️ hapticAmplitude / hapticDuration are deliberately not written and not added to this
            // table: recoil feel is tuned by wearing the headset and lives in the Inspector. A line
            // here would silently overwrite that hand-found value on every run. A new WD asset is
            // born with the class defaults (see WeaponDefinition).
            // The "prefab" field is bound in STEP 5, after the WPN exists.

            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        // ---------------------------------------------------- STEP 2: WPN prefabs

        /// <summary>Updates an existing WPN_&lt;Name&gt;.prefab in place
        /// (<see cref="RebindExistingPrefab"/>): definition bindings + flash/smoke/casing kit + grip
        /// kit. ⚠️ The body (model, Muzzle/MuzzleFlash/Eject placement) is NEVER touched — it is
        /// hand-authored. A missing prefab is an error, not a generation trigger.</summary>
        private static BuildOutcome BuildWeaponPrefab(WeaponSpec spec, WeaponDefinition def,
            Dictionary<string, GameObject> casings, Material smokeMaterial)
        {
            string wpnPath = PrefabDir + "/WPN_" + spec.Name + ".prefab";
            string ctx = "WPN_" + spec.Name;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(wpnPath) == null)
            {
                Debug.LogError(Log + ctx + ": '" + wpnPath + "' yok — bu araç WPN prefabı ÜRETMEZ, " +
                               "yalnız mevcudu günceller. Prefabı repoya ekleyip tekrar çalıştır.");
                return BuildOutcome.Failed;
            }

            RebindExistingPrefab(wpnPath, spec, def, casings, smokeMaterial, ctx);
            return BuildOutcome.Rebound;
        }

        /// <summary>Opens the WPN contents, refreshes the definition bindings and applies the
        /// per-weapon flash/smoke/casing kit (<see cref="ApplyVfxAndShellKit"/>) — the tool's ONLY
        /// build path. Model and Muzzle placement are left untouched.</summary>
        private static void RebindExistingPrefab(string wpnPath, WeaponSpec spec, WeaponDefinition def,
            Dictionary<string, GameObject> casings, Material smokeMaterial, string ctx)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(wpnPath);
            try
            {
                var weapon = contents.GetComponent<Weapon>();
                if (weapon != null)
                {
                    var so = new SerializedObject(weapon);
                    SetObjectRef(so, "definition", def, ctx);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                else
                {
                    Warn(ctx + ": mevcut prefabda Weapon bileşeni yok — definition bağlanamadı.");
                }

                var weaponAudio = contents.GetComponent<WeaponAudio>();
                if (weaponAudio != null)
                {
                    var so = new SerializedObject(weaponAudio);
                    var defProp = so.FindProperty("definition");
                    if (defProp != null)
                    {
                        defProp.objectReferenceValue = def;
                    }

                    var srcProp = so.FindProperty("source");
                    if (srcProp != null && srcProp.objectReferenceValue == null)
                    {
                        Transform muzzleT = FindDeepChild(contents.transform, "Muzzle");
                        if (muzzleT != null)
                        {
                            srcProp.objectReferenceValue = muzzleT.GetComponent<AudioSource>();
                        }
                    }

                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                if (weapon != null)
                {
                    ApplyVfxAndShellKit(contents, spec, casings, smokeMaterial, ctx);
                }

                // This is the only live path — without the grip kit here, existing WPNs would stay
                // distance-grabbable.
                ApplyGripKit(contents, def, ctx);
                ApplyWeaponFrameKit(contents, ctx);
                ApplyDissolveKit(contents, ctx);

                PrefabUtility.SaveAsPrefabAsset(contents, wpnPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // ------------------------------------------------- STEP 3: FX_RemoteShot

        /// <summary>Creates FX_RemoteShot.prefab if missing (left alone if present): the AudioSource
        /// + MetaXRAudioSource from the first WPN's Muzzle are copied to the root and MuzzleFlash is
        /// cloned as a child named "Flash". Returns whether it was created this run.</summary>
        private static bool EnsureRemoteShotFx(List<GameObject> live)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FxPrefabPath) != null)
            {
                return false; // varsa dokunma
            }

            GameObject sourcePrefab = null;
            for (int i = 0; i < Specs.Length && sourcePrefab == null; i++)
            {
                sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/WPN_" + Specs[i].Name + ".prefab");
            }

            if (sourcePrefab == null)
            {
                Warn("FX_RemoteShot: kaynak yok (hiçbir WPN prefabı bulunamadı) — üretilemedi.");
                return false;
            }

            var srcInst = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            live.Add(srcInst);
            var fxRoot = new GameObject("FX_RemoteShot");
            live.Add(fxRoot);

            Transform muzzleT = FindDeepChild(srcInst.transform, "Muzzle");
            if (muzzleT != null)
            {
                // AudioSource first (MetaXRAudioSource depends on it), then MetaXRAudioSource.
                var src = muzzleT.GetComponent<AudioSource>();
                if (src != null)
                {
                    ComponentUtility.CopyComponent(src);
                    ComponentUtility.PasteComponentAsNew(fxRoot);
                }
                else
                {
                    Warn("FX_RemoteShot: kaynağın Muzzle'ında AudioSource yok.");
                }

                // No compile-time reference to MetaXRAudioSource — copy it by type name.
                Component[] comps = muzzleT.GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] != null && comps[i].GetType().Name == "MetaXRAudioSource")
                    {
                        ComponentUtility.CopyComponent(comps[i]);
                        ComponentUtility.PasteComponentAsNew(fxRoot);
                    }
                }
            }
            else
            {
                Warn("FX_RemoteShot: kaynakta 'Muzzle' child'ı yok — ses bileşenleri kopyalanamadı.");
            }

            Transform flashT = FindDeepChild(srcInst.transform, "MuzzleFlash");
            if (flashT != null)
            {
                GameObject flash = Object.Instantiate(flashT.gameObject, fxRoot.transform);
                flash.name = "Flash";
                flash.transform.localPosition = Vector3.zero;
                flash.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Warn("FX_RemoteShot: kaynakta 'MuzzleFlash' child'ı yok — flash kopyalanamadı.");
            }

            PrefabUtility.SaveAsPrefabAsset(fxRoot, FxPrefabPath, out bool saved);
            Object.DestroyImmediate(fxRoot);
            Object.DestroyImmediate(srcInst);

            if (!saved)
            {
                Debug.LogError(Log + "FX_RemoteShot: SaveAsPrefabAsset başarısız: " + FxPrefabPath);
                return false;
            }

            return true;
        }

        /// <summary>Creates the front-grip socket prefab if missing (left alone if present): Unity's
        /// sphere primitive (1 m across — contract in <see cref="GripSocketPrefabPath"/>), no
        /// collider, translucent light-blue material.
        /// <para>Why a prefab: the socket is ART and art belongs in a prefab — <c>Weapon</c> only
        /// drives its position, scale (twice the acceptance radius) and alpha. The sphere is a
        /// starting point: an artist can edit this prefab in place or bind a different one to the
        /// catalog, as long as the 1 m contract holds.</para>
        /// <para>⚠️ The collider is stripped here (primitives ship with one): the socket must not
        /// catch fire rays or grabs. <c>Weapon</c> strips it again on the instance; keeping the
        /// source clean avoids "why is this checked twice".</para>
        /// <para>⚠️ The material is created as an ASSET (<see cref="GripSocketMaterialPath"/>), not
        /// via runtime <c>Shader.Find</c>: a shader no asset references is stripped from the build
        /// and the socket silently stops drawing in the field.</para></summary>
        private static bool EnsureGripSocketPrefab(List<GameObject> live)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GripSocketPrefabPath) != null)
            {
                return false; // varsa dokunma
            }

            Material material = EnsureGripSocketMaterial();
            if (material == null)
            {
                Warn("VA_GripSocket: küre için shader bulunamadı (VortexArena/GripSocket, URP Unlit/Lit, " +
                     "Sprites/Default) — soket prefabı üretilemedi, katalogda alan boş kalır (soket çizilmez).");
                return false;
            }

            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.name = "VA_GripSocket";
            live.Add(root);

            Collider collider = root.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            var renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            PrefabUtility.SaveAsPrefabAsset(root, GripSocketPrefabPath, out bool saved);
            Object.DestroyImmediate(root);

            if (!saved)
            {
                Debug.LogError(Log + "VA_GripSocket: SaveAsPrefabAsset başarısız: " + GripSocketPrefabPath);
                return false;
            }

            Debug.Log(Log + "VA_GripSocket.prefab üretildi (" + GripSocketPrefabPath + ").");
            return true;
        }

        /// <summary>Creates the sphere material if missing; <c>null</c> when no shader is found.
        /// <para>On URP shaders, transparency is set through <c>_Surface</c>/<c>_Blend</c>/
        /// <c>_SrcBlend</c>/<c>_DstBlend</c>/<c>_ZWrite</c> + the
        /// <c>_SURFACE_TYPE_TRANSPARENT</c> keyword + the Transparent queue (exactly what the
        /// Inspector's "Surface Type = Transparent" writes). Writing a non-existent property is a
        /// silent no-op, so the other shaders in the chain pass through the same code.</para>
        /// </summary>
        private static Material EnsureGripSocketMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(GripSocketMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = null;
            for (int i = 0; i < GripSocketShaderCandidates.Length && shader == null; i++)
            {
                shader = Shader.Find(GripSocketShaderCandidates[i]);
            }

            if (shader == null)
            {
                return null;
            }

            EnsureFolder(GripSocketMaterialDir);
            var material = new Material(shader) { name = "M_GripSocket" };

            // Transparent surface (URP): Surface=Transparent, Blend=Alpha, ZWrite off.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // Colour: URP _BaseColor, legacy/fallback _Color. Both are written (a missing property
            // is swallowed silently).
            material.SetColor("_BaseColor", GripSocketColor);
            material.SetColor("_Color", GripSocketColor);

            AssetDatabase.CreateAsset(material, GripSocketMaterialPath);
            return material;
        }

        /// <summary>Creates the casing prefab if missing: unpacks the pack's bullet model and adds a
        /// small Rigidbody + a bounds-fitted BoxCollider. Physical accuracy is not a goal — it is a
        /// short-lived pooled FX object.
        /// <para>⚠️ "Skip if present" is NOT enough; the existing one is also checked for HEALTH. The
        /// casing is unpacked from the pack model, so its mesh is a reference into the pack's FBX;
        /// if the pack folder moves (or the FBX is reimported with a new id) that reference breaks
        /// and the prefab is left MESHLESS. The symptom is sneaky: the casing ejects, physics runs,
        /// nothing is logged — it just is not drawn, which reads as "no casings". An unconditional
        /// early return would mean the tool never repairs it again.</para></summary>
        private static GameObject EnsureCasingPrefab(string path, string sourcePackPath, List<GameObject> live)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                if (HasRenderableMesh(existing))
                {
                    return existing;
                }

                // Broken: the build path below overwrites the same path. SaveAsPrefabAsset preserves
                // the asset GUID, so casingPrefab bindings in the WPN prefabs survive.
                Warn("Kovan prefabı '" + path + "' mesh'siz (pack taşınmış olabilir) — " +
                     "kaynaktan yeniden üretiliyor.");
            }

            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePackPath);
            if (sourcePrefab == null)
            {
                Warn("Kovan kaynağı yok: " + sourcePackPath + " — '" + path + "' üretilemedi.");
                return null;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            live.Add(inst);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = System.IO.Path.GetFileNameWithoutExtension(path);

            var rb = inst.AddComponent<Rigidbody>();
            rb.mass = CasingMassKg;

            Bounds b = ComputeLocalBounds(inst.transform, inst, "Casing:" + inst.name);
            var box = inst.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = new Vector3(Mathf.Max(b.size.x, 0.006f), Mathf.Max(b.size.y, 0.006f), Mathf.Max(b.size.z, 0.006f));

            PrefabUtility.SaveAsPrefabAsset(inst, path, out bool saved);
            Object.DestroyImmediate(inst);

            if (!saved)
            {
                Debug.LogError(Log + "Kovan prefabı kaydedilemedi: " + path);
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>Does the prefab have at least one drawable mesh: with a broken
        /// <c>MeshFilter</c> chain (mesh <c>null</c>) the object exists and its physics run, but it
        /// is INVISIBLE.</summary>
        private static bool HasRenderableMesh(GameObject go)
        {
            var filters = go.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A copy of M_MuzzleFlash.mat with Alpha blend instead of Additive, so muzzle smoke
        /// reads as grey haze rather than glow. Created if missing, left alone if present.</summary>
        private static Material EnsureMuzzleSmokeMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(SmokeMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            var flashMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_MuzzleFlash.mat");
            if (flashMat == null)
            {
                Warn("M_MuzzleFlash.mat bulunamadı — M_MuzzleSmoke üretilemedi.");
                return null;
            }

            var smokeMat = new Material(flashMat) { name = "M_MuzzleSmoke" };
            smokeMat.SetFloat("_Blend", 0f); // Alpha (flash'ta 2 = Additive)
            smokeMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            smokeMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            smokeMat.SetColor("_BaseColor", new Color(0.6f, 0.6f, 0.6f, 1f));

            AssetDatabase.CreateAsset(smokeMat, SmokeMaterialPath);
            return smokeMat;
        }

        // ------------------------------------------------- STEP 4: WeaponCatalog

        /// <summary>Creates WeaponCatalog.asset if missing; writes definitions (table order) +
        /// remoteShotFxPrefab.</summary>
        private static bool UpdateCatalog()
        {
            Type catalogType = ResolveType("WeaponCatalog");
            if (catalogType == null || !typeof(ScriptableObject).IsAssignableFrom(catalogType))
            {
                Warn("WeaponCatalog tipi bulunamadı — katalog atlandı (script henüz derlenmemiş olabilir).");
                return false;
            }

            bool created = false;
            var catalog = AssetDatabase.LoadAssetAtPath(CatalogPath, catalogType) as ScriptableObject;
            if (catalog == null)
            {
                // Another asset type at this path: do not overwrite, to preserve its GUID.
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(CatalogPath)))
                {
                    Warn("WeaponCatalog: '" + CatalogPath + "' yolunda farklı tipte bir asset var — dokunulmadı.");
                    return false;
                }

                catalog = ScriptableObject.CreateInstance(catalogType);
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                created = true;
            }

            var so = new SerializedObject(catalog);

            var defsProp = so.FindProperty("definitions");
            if (defsProp == null || !defsProp.isArray)
            {
                Warn("WeaponCatalog: 'definitions' alanı yok ya da dizi değil (sözleşme kayması?).");
            }
            else
            {
                var found = new List<WeaponDefinition>(Specs.Length);
                for (int i = 0; i < Specs.Length; i++)
                {
                    string wdPath = DataDir + "/WD_" + Specs[i].Name + ".asset";
                    var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(wdPath);
                    if (def != null)
                    {
                        found.Add(def);
                    }
                    else
                    {
                        Warn("WeaponCatalog: '" + wdPath + "' yok — katalog dışı kaldı.");
                    }
                }

                defsProp.arraySize = found.Count;
                for (int i = 0; i < found.Count; i++)
                {
                    defsProp.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
                }
            }

            var fxProp = so.FindProperty("remoteShotFxPrefab");
            if (fxProp == null)
            {
                Warn("WeaponCatalog: 'remoteShotFxPrefab' alanı yok (sözleşme kayması?).");
            }
            else
            {
                var fx = AssetDatabase.LoadAssetAtPath<GameObject>(FxPrefabPath);
                if (fx != null)
                {
                    fxProp.objectReferenceValue = fx;
                }
                else
                {
                    Warn("WeaponCatalog: FX_RemoteShot.prefab yok — remoteShotFxPrefab olduğu gibi bırakıldı.");
                }
            }

            // Front-grip indicator: bound ONLY when the field is empty, so an artist-bound prefab is
            // never overwritten (this is how it differs from the FX field; see GripSocketPrefabPath).
            var indicatorProp = so.FindProperty("secondaryGripIndicatorPrefab");
            if (indicatorProp == null)
            {
                Warn("WeaponCatalog: 'secondaryGripIndicatorPrefab' alanı yok (sözleşme kayması?).");
            }
            else if (indicatorProp.objectReferenceValue == null)
            {
                var indicator = AssetDatabase.LoadAssetAtPath<GameObject>(GripSocketPrefabPath);
                if (indicator != null)
                {
                    indicatorProp.objectReferenceValue = indicator;
                }
                else
                {
                    Warn("WeaponCatalog: VA_GripSocket.prefab yok — secondaryGripIndicatorPrefab boş " +
                         "kaldı (ön kabza göstergesi çizilmez; tam kit koşusu üretir).");
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return created;
        }

        // -------------------------------------------- STEP 5: WD.prefab second pass

        /// <summary>Binds each WD's 'prefab' field to its WPN prefab.</summary>
        private static void LinkDefinitionPrefabs()
        {
            for (int i = 0; i < Specs.Length; i++)
            {
                string ctx = "WD_" + Specs[i].Name;
                var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(DataDir + "/WD_" + Specs[i].Name + ".asset");
                if (def == null)
                {
                    Warn(ctx + ": asset yok — prefab bağlanamadı.");
                    continue;
                }

                string wpnPath = PrefabDir + "/WPN_" + Specs[i].Name + ".prefab";
                var wpn = AssetDatabase.LoadAssetAtPath<GameObject>(wpnPath);
                if (wpn == null)
                {
                    Warn(ctx + ": '" + wpnPath + "' yok — prefab alanı boş bırakıldı.");
                    continue;
                }

                var so = new SerializedObject(def);
                SetObjectRef(so, "prefab", wpn, ctx);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Warning counter + console (no dialogs, so the pipeline never blocks).</summary>
        private static void Warn(string message)
        {
            _warnings++;
            Debug.LogWarning(Log + message);
        }

        /// <summary>Creates an "Assets/..." folder chain, filling in missing links.</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string[] parts = path.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        /// <summary>Returns the first descendant matching the name (deep search).</summary>
        private static Transform FindDeepChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }

                Transform hit = FindDeepChild(child, name);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        /// <summary>Combined bounds of all Renderers on the instance, converted into ROOT-local space
        /// via the root's worldToLocalMatrix (all 8 corners transformed individually).</summary>
        private static Bounds ComputeLocalBounds(Transform root, GameObject instance, string ctx)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            Matrix4x4 toRoot = root.worldToLocalMatrix;

            bool hasAny = false;
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;

            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];

                // ⚠️ A DISABLED Renderer's `bounds` is STALE — Unity does not update it and returns
                // the WORLD box from where it was last drawn. The frame (VA_WeaponFrame) is disabled
                // by default in the prefab, so a box metres away leaks in and shifts the whole
                // measurement.
                if (!renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // The frame is a presentation shell, not the weapon's BODY: even when enabled it
                // stays out of the measurement, or "how wide is the weapon" answers with the frame.
                if (renderer.GetComponentInParent<WeaponFrame>(true) != null)
                {
                    continue;
                }

                Bounds wb = renderer.bounds;
                if (wb.size.sqrMagnitude <= 0f)
                {
                    continue;
                }

                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? wb.min.x : wb.max.x,
                        (c & 2) == 0 ? wb.min.y : wb.max.y,
                        (c & 4) == 0 ? wb.min.z : wb.max.z);
                    Vector3 local = toRoot.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    hasAny = true;
                }
            }

            if (!hasAny)
            {
                Warn(ctx + ": modelde Renderer yok — kaba varsayılan ölçüler kullanıldı.");
                return new Bounds(new Vector3(0f, 0.01f, 0.16f), new Vector3(0.08f, 0.18f, 0.68f));
            }

            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        /// <summary>Looks up a short ("WeaponAnimator") or full ("TMPro.TextMeshPro") type name in
        /// the loaded assemblies; on a short-name collision the VortexArena.* namespace wins. Results
        /// are cached.</summary>
        private static Type ResolveType(string name)
        {
            if (ResolvedTypes.TryGetValue(name, out Type cached))
            {
                return cached;
            }

            bool dotted = name.IndexOf('.') >= 0;
            Type found = null;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                if (dotted)
                {
                    Type t = null;
                    try
                    {
                        t = assemblies[a].GetType(name, false);
                    }
                    catch
                    {
                        // dynamic/broken assembly — skip
                    }

                    if (t != null)
                    {
                        found = PickPreferred(found, t);
                    }
                }
                else
                {
                    Type[] types;
                    try
                    {
                        types = assemblies[a].GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        types = e.Types;
                    }
                    catch
                    {
                        continue;
                    }

                    for (int t = 0; t < types.Length; t++)
                    {
                        if (types[t] != null && types[t].Name == name)
                        {
                            found = PickPreferred(found, types[t]);
                        }
                    }
                }
            }

            if (found != null)
            {
                ResolvedTypes[name] = found;
            }

            return found;
        }

        /// <summary>On two candidates for one short name, the VortexArena.* one wins.</summary>
        private static Type PickPreferred(Type current, Type candidate)
        {
            if (current == null)
            {
                return candidate;
            }

            bool currentVa = current.Namespace != null && current.Namespace.StartsWith("VortexArena", StringComparison.Ordinal);
            bool candidateVa = candidate.Namespace != null && candidate.Namespace.StartsWith("VortexArena", StringComparison.Ordinal);
            return !currentVa && candidateVa ? candidate : current;
        }

        /// <summary>GetComponent ?? AddComponent by type name (warns and returns null if the script
        /// does not exist yet).</summary>
        private static Component EnsureComponentByTypeName(GameObject go, string typeName, string ctx)
        {
            Type type = ResolveType(typeName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
            {
                Warn(ctx + ": '" + typeName + "' tipi bulunamadı — bileşen eklenemedi (script henüz derlenmemiş olabilir).");
                return null;
            }

            Component existing = go.GetComponent(type);
            return existing != null ? existing : go.AddComponent(type);
        }

        /// <summary>Finds a component on the root by full type name.</summary>
        private static Component FindComponentByTypeFullName(GameObject go, string fullName)
        {
            Component[] comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] != null && comps[i].GetType().FullName == fullName)
                {
                    return comps[i];
                }
            }

            return null;
        }

        /// <summary>Binds the given (field, reference) pairs via SerializedObject when the component
        /// is non-null.</summary>
        private static void BindFields(Component target, string ctx, params (string field, Object value)[] refs)
        {
            if (target == null)
            {
                return;
            }

            var so = new SerializedObject(target);
            for (int i = 0; i < refs.Length; i++)
            {
                SetObjectRef(so, refs[i].field, refs[i].value, ctx);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>FindProperty; warns and returns null on a missing field (contract-drift
        /// diagnostics).</summary>
        private static SerializedProperty FindProp(SerializedObject so, string field, string ctx)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Warn(ctx + ": '" + field + "' alanı bulunamadı (sözleşme kayması?).");
            }

            return p;
        }

        private static void SetString(SerializedObject so, string field, string value, string ctx)
        {
            SerializedProperty p = FindProp(so, field, ctx);
            if (p != null)
            {
                p.stringValue = value;
            }
        }

        /// <summary>Writes whether the field is float or int (the contract does not fix the type).</summary>
        private static void SetNumber(SerializedObject so, string field, double value, string ctx)
        {
            SerializedProperty p = FindProp(so, field, ctx);
            if (p == null)
            {
                return;
            }

            switch (p.propertyType)
            {
                case SerializedPropertyType.Float:
                    p.floatValue = (float)value;
                    break;
                case SerializedPropertyType.Integer:
                    p.intValue = (int)Math.Round(value);
                    break;
                default:
                    Warn(ctx + ": '" + field + "' sayısal değil (" + p.propertyType + ").");
                    break;
            }
        }

        /// <summary>Writes an object reference. If <paramref name="value"/> is null and
        /// <paramref name="allowNull"/> is off, the existing value is kept so a working prefab
        /// binding is not overwritten with null.</summary>
        private static void SetObjectRef(SerializedObject so, string field, Object value, string ctx, bool allowNull = false)
        {
            SerializedProperty p = FindProp(so, field, ctx);
            if (p == null)
            {
                return;
            }

            if (p.propertyType != SerializedPropertyType.ObjectReference)
            {
                Warn(ctx + ": '" + field + "' obje referansı değil (" + p.propertyType + ").");
                return;
            }

            if (value == null && !allowNull)
            {
                return;
            }

            p.objectReferenceValue = value;
        }

        /// <summary>Writes an enum field BY MEMBER NAME, to keep int values out of the contract.</summary>
        private static void SetEnumByName(SerializedObject so, string field, string memberName, string ctx)
        {
            SerializedProperty p = FindProp(so, field, ctx);
            if (p == null)
            {
                return;
            }

            if (p.propertyType != SerializedPropertyType.Enum)
            {
                Warn(ctx + ": '" + field + "' enum değil (" + p.propertyType + ").");
                return;
            }

            int index = Array.IndexOf(p.enumNames, memberName);
            if (index < 0)
            {
                Warn(ctx + ": enum '" + field + "' içinde '" + memberName + "' üyesi yok (mevcut: " +
                     string.Join(", ", p.enumNames) + ").");
                return;
            }

            p.enumValueIndex = index;
        }

        /// <summary>Like <see cref="SetObjectRef"/> but writes ONLY when the field is still empty, so
        /// a hand-dragged value survives a rerun. Used for audio clip fields.</summary>
        private static void SetObjectRefIfEmpty(SerializedObject so, string field, Object value, string ctx)
        {
            SerializedProperty p = FindProp(so, field, ctx);
            if (p == null)
            {
                return;
            }

            if (p.propertyType != SerializedPropertyType.ObjectReference)
            {
                Warn(ctx + ": '" + field + "' obje referansı değil (" + p.propertyType + ").");
                return;
            }

            if (p.objectReferenceValue != null)
            {
                return; // assigned by hand - do not touch
            }

            p.objectReferenceValue = value;
        }


        /// <summary>Applies the flash/smoke/casing kit on a WPN root
        /// (<see cref="RebindExistingPrefab"/>). ⚠️ Muzzle/MuzzleFlash are found WHERE THEY ARE and
        /// never moved — their placement is hand-authored.</summary>
        private static void ApplyVfxAndShellKit(GameObject root, WeaponSpec spec,
            Dictionary<string, GameObject> casings, Material smokeMaterial, string ctx)
        {
            Transform rootT = root.transform;
            Transform flashT = FindDeepChild(rootT, "MuzzleFlash");
            ParticleSystem flashPs = flashT != null ? flashT.GetComponent<ParticleSystem>() : null;
            if (flashPs != null)
            {
                ConfigureMuzzleFlash(flashPs, spec);
                ConfigureMuzzleSmoke(flashPs, spec, smokeMaterial);
            }
            else
            {
                Warn(ctx + ": MuzzleFlash/ParticleSystem bulunamadı — namlu alevi/dumanı ayarlanamadı.");
            }

            // ---- Casing ejection point.
            // ⚠️ An EXISTING `Eject` is never moved — same rule as Muzzle/MuzzleFlash: its position
            // is hand-authored, the tool only binds. Recomputing it every run silently erased that
            // adjustment, because the measurement walked every Renderer in the subtree and the
            // DISABLED frame returned stale world bounds (Docs/Sistem-Ozeti.md §7).
            // ⚠️ The search must be DEEP: `Eject` lives inside the MODEL, not on the weapon root
            // (ejection is a point on the body). Looking only at direct children would miss it and
            // every run would add a SECOND `Eject` on the root, silently disabling the authored one.
            Transform ejectT = FindDeepChild(rootT, "Eject");
            if (ejectT == null)
            {
                // Only the FIRST setup gets a rough starting point (otherwise it would spawn at the
                // weapon origin); after that it is hand-tuned and never touched again.
                Bounds bounds = ComputeLocalBounds(rootT, root, ctx);
                var ejectGo = new GameObject("Eject");
                ejectT = ejectGo.transform;
                ejectT.SetParent(rootT, false);
                ejectT.localPosition = new Vector3(
                    bounds.extents.x * 0.9f,
                    bounds.center.y + bounds.extents.y * 0.25f,
                    bounds.center.z - bounds.extents.z * 0.2f);
                ejectT.localRotation = Quaternion.identity;
                Warn(ctx + ": 'Eject' yoktu, kaba bir başlangıç noktasıyla üretildi — " +
                     "kovan çıkışını sahnede gözle ayarla, araç bir daha taşımaz.");
            }

            Component shellEjector = EnsureComponentByTypeName(root, "ShellEjector", ctx);
            GameObject casingForSpec = null;
            if (!string.IsNullOrEmpty(spec.CasingFamily))
            {
                casings.TryGetValue(spec.CasingFamily, out casingForSpec);
            }

            BindFields(shellEjector, ctx, ("casingPrefab", casingForSpec), ("ejectPoint", ejectT));
        }

        /// <summary>Applies the grip kit on a WPN root (idempotent): ISDK's TWO near-grab components
        /// (controller + hand line) are kept and left unfiltered, distance grab is removed from the
        /// root, and legacy grip leftovers are cleaned up.
        /// <para>⚠️ There is NO grab filter on the root and none is bound
        /// (<c>_interactorFilters</c> stays empty): the old socket gate component is gone — the
        /// weapon is handed to the main hand, and the front grip's gate and indicator live in
        /// <see cref="Weapon"/> itself (<c>IsHandOnSecondaryGrip</c>). The tool CLEARS the list every
        /// run, because the missing entry left behind by the removed component throws in ISDK's
        /// <c>Start</c> check (<c>AssertCollectionItems</c>) and makes the weapon ungrabbable. For
        /// the same reason leftover missing scripts on the root are deleted.</para>
        /// <para>⚠️ Why two components: <c>GrabInteractable</c> (controller line) and
        /// <c>HandGrabInteractable</c> (hand line) are kept together because the ISDK rig chooses
        /// which one runs, based on "are hands tracked", which follows
        /// <c>OVRManager.controllerDrivenHandPosesType</c>. Keeping only one would silently make the
        /// weapon ungrabbable on every flip of that switch (<c>Docs/Sistem-Ozeti.md</c> §7). Both
        /// feed the same <c>Grabbable</c>, so <see cref="Weapon"/> sees one event path.</para>
        /// <para>⚠️ <c>HandGrabInteractable._handGrabPoses</c> is left EMPTY: once populated, ISDK
        /// starts scoring grab candidacy by pose and today's feel (collider distance) would change
        /// silently. For the same reason <c>_handAligment</c> is untouched — it has no effect while
        /// the pose list is empty.</para>
        /// <para>⚠️ This tool does NOT author grips. The only source is the pose written in the grip
        /// studio, straight into <c>WD_*.asset</c> (there is no corresponding prefab node — adding
        /// one would create a second description). Here the tool only cleans up and reports.</para>
        /// </summary>
        private static void ApplyGripKit(GameObject root, ItemDefinition definition, string ctx)
        {
            // Missing-script records left on the root by the removed socket component (and other old
            // scripts): the type no longer compiles, so they are removed via Unity's own cleanup.
            int missing = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
            if (missing > 0)
            {
                Debug.Log(Log + ctx + ": kökte " + missing + " eksik script bileşeni silindi.");
            }

            // ⚠️ No distance grab on the root: the weapon is picked from the frame
            // (VA_WeaponFrame), and a distance-grabbable root would skip the frame and let it be
            // taken from across the room. BOTH lines are removed — the rig decides which one runs,
            // so leaving one leaves the rule half-enforced.
            RemoveRootComponent(root, "Oculus.Interaction.DistanceGrabInteractable", ctx,
                "kökte mesafeden kavrama yok (çerçeveden seçilir)");
            RemoveRootComponent(root, "Oculus.Interaction.HandGrab.DistanceHandGrabInteractable", ctx,
                "kökte mesafeden kavrama yok (el hattı; çerçeveden seçilir)");

            // With distance grab gone the provider asset goes too: its only remaining consumer is
            // HandGrabInteractable, which adds its own instance AT RUNTIME when the field is empty.
            // Keeping it would tell the next reader "there is hand-tuned movement here".
            Component moveProvider = FindComponentByTypeFullName(root, "Oculus.Interaction.MoveTowardsTargetProvider");
            if (moveProvider != null)
            {
                Object.DestroyImmediate(moveProvider, true);
                Debug.Log(Log + ctx + ": MoveTowardsTargetProvider kaldırıldı (çalışma anında kurulur).");
            }

            ApplyReleasePhysics(root, ctx);

            Component grabbable = FindComponentByTypeFullName(root, "Oculus.Interaction.Grabbable");
            Rigidbody body = root.GetComponent<Rigidbody>();

            Component grabInteractable = FindComponentByTypeFullName(root, "Oculus.Interaction.GrabInteractable");
            if (grabInteractable == null)
            {
                Warn(ctx + ": kökte Oculus.Interaction.GrabInteractable yok — kumanda hattı eksik " +
                     "(silah yalnız el hattından kavranır).");
            }
            else
            {
                ClearInteractorFilters(grabInteractable, ctx);
            }

            // The hand line is CREATED by the tool (unlike the controller line), so adding a weapon
            // never creates a manual setup step.
            Component handGrab = EnsureComponentByTypeName(
                root, "Oculus.Interaction.HandGrab.HandGrabInteractable", ctx);
            if (handGrab == null)
            {
                return;
            }

            var handSo = new SerializedObject(handGrab);
            SerializedProperty rb = handSo.FindProperty("_rigidbody");
            SerializedProperty pointable = handSo.FindProperty("_pointableElement");
            if (rb != null)
            {
                rb.objectReferenceValue = body;
            }

            if (pointable != null)
            {
                pointable.objectReferenceValue = grabbable;
            }

            handSo.ApplyModifiedPropertiesWithoutUndo();

            if (body == null)
            {
                // Without a Rigidbody, HandGrabInteractable asserts in Start and disables itself; the
                // symptom is "the weapon sometimes cannot be taken" with no visible cause.
                Warn(ctx + ": kökte Rigidbody yok — HandGrabInteractable çalışamaz (silah el hattında " +
                     "kavranamaz).");
            }

            ClearInteractorFilters(handGrab, ctx);

            RemoveLegacySocketNodes(root, ctx);
            RemoveLegacyHandNodes(root, ctx);
            RemoveLegacyGripPoseNodes(root, ctx);
            RemoveStudioHandNodes(root, ctx);
            NoteIfUnbaked(definition, ctx);
        }

        /// <summary>Deletes the legacy <c>GripSocket_Primary/Secondary</c> marker nodes.
        /// <para>⚠️ Cleanup happens here, not by hand-editing the prefab file: removing a node
        /// touches three YAML records (GameObject + Transform + MonoBehaviour) plus the parent's
        /// child list, and instances of these prefabs live inside other prefabs
        /// (<c>VA_WeaponCanvas</c>). Going through Unity's API resolves all of that.</para>
        /// <para>⚠️ The search is BY NAME, not by component type: the <c>GripSocketMarker</c> type is
        /// gone, so the nodes now carry a missing script and a type search would find nothing.</para>
        /// <para>⚠️ The search walks the WHOLE SUBTREE, not just the root's direct children: the old
        /// tool put markers on the weapon root but moving them into the model branch was allowed and
        /// happened. A root-only cleanup would find nothing and look SILENTLY successful.</para>
        /// </summary>
        private static void RemoveLegacySocketNodes(GameObject root, string ctx)
        {
            RemoveLegacySocketNode(root, "GripSocket_Primary", ctx);
            RemoveLegacySocketNode(root, "GripSocket_Secondary", ctx);
        }

        private static void RemoveLegacySocketNode(GameObject root, string nodeName, string ctx)
        {
            // Reverse walk: several nodes may share the name (hand-copied) and all must go.
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                Transform node = all[i];
                if (node == null || node.name != nodeName)
                {
                    continue;
                }

                Object.DestroyImmediate(node.gameObject, true);
                _legacyNodesRemoved++;
                Debug.Log(Log + ctx + ": eski '" + nodeName + "' işaretçisi silindi — kavrama " +
                          "stüdyoda WD_*.asset'e yazılır, prefabta işaretçi durmaz.");
            }
        }

        /// <summary>Deletes the leftover <c>Hands/Hand_*</c> tree inside a prefab — DEAD DATA. The
        /// grip comes from the studio-authored pose in <c>WD_*.asset</c>, not from a hand model in
        /// the prefab; nothing reads that rig.
        /// <para>⚠️ Never left in place: (1) if enabled it appears as a hand floating in the arena
        /// (the weapon also sits in the scene and in the remote avatar's hand); (2) a surviving copy
        /// is a second description of the grip. The runtime guard
        /// (<see cref="ItemHandRig.HideAll"/>) remains for prefabs not yet cleaned.</para>
        /// </summary>
        private static void RemoveLegacyHandNodes(GameObject root, string ctx)
        {
            Transform node = root.transform.Find(ItemHandRig.RootNodeName);
            if (node == null)
            {
                return;
            }

            Object.DestroyImmediate(node.gameObject, true);
            _legacyNodesRemoved++;
            Debug.Log(Log + ctx + ": eski '" + ItemHandRig.RootNodeName + "' el rig'i silindi — " +
                      "kavrama WD_*.asset'e yazılır, prefabda el modeli durmaz.");
        }

        /// <summary>Deletes the leftover <c>GripPoses/…</c> tree inside a prefab — DEAD DATA. The
        /// grip lives in the definition's own fields; nothing reads these nodes. ⚠️ Never left in
        /// place: a surviving node is a second description of the grip.</summary>
        private static void RemoveLegacyGripPoseNodes(GameObject root, string ctx)
        {
            Transform node = root.transform.Find(LegacyGripPoseRootName);
            if (node == null)
            {
                return;
            }

            Object.DestroyImmediate(node.gameObject, true);
            _legacyNodesRemoved++;
            Debug.Log(Log + ctx + ": eski '" + LegacyGripPoseRootName + "' poz düğümü silindi — " +
                      "kavrama WD_*.asset'e yazılır, prefabta poz durmaz.");
        }

        /// <summary>Deletes an authoring hand that accidentally ended up inside the prefab.
        /// ⚠️ These hands are separate roots of the prefab stage scene and never hit disk — but they
        /// can be dragged under the prefab in the Hierarchy, at which point the hand model enters the
        /// prefab and floats in the arena.</summary>
        private static void RemoveStudioHandNodes(GameObject root, string ctx)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                Transform node = all[i];
                if (node == null || node == root.transform ||
                    !node.name.StartsWith(GripPoseStudio.HAND_ROOT_PREFIX))
                {
                    continue;
                }

                string nodeName = node.name;
                Object.DestroyImmediate(node.gameObject, true);
                _legacyNodesRemoved++;
                Debug.Log(Log + ctx + ": prefabın içinde kalmış authoring eli ('" + nodeName +
                          "') silindi — o eller prefaba KONMAZ, sahnenin ayrı kökleridir.");
            }
        }

        /// <summary>Adds a weapon with a missing grip to the end-of-run report.
        /// <para>⚠️ The test is <see cref="ItemDefinition.HasGrip"/>, not <c>GetGrip</c>: the read
        /// path falls back to the OTHER hand's record, so <c>GetGrip</c> would make a half-authored
        /// weapon look complete. The hand queried for the main grip is the RIGHT one. On two-handed
        /// weapons the FRONT grip is checked too
        /// (<see cref="ItemDefinition.HasSecondaryGrip"/>): an unauthored front grip stays socketless
        /// and unholdable in game and nobody notices unless it is listed.</para></summary>
        private static void NoteIfUnbaked(ItemDefinition definition, string ctx)
        {
            if (IsUnbaked(definition))
            {
                _unbakedWeapons.Add(ctx);
            }
        }

        /// <summary>The "grip not authored" test — the single source for both the end-of-run report
        /// and the readiness check; duplicated elsewhere it would drift.</summary>
        private static bool IsUnbaked(ItemDefinition definition)
        {
            return definition == null
                   || !definition.HasGrip(GripSocketKind.Primary, true)
                   || (definition.IsTwoHanded && !definition.HasSecondaryGrip);
        }

        /// <summary>The "no fire sound assigned" test — same rationale as <see cref="IsUnbaked"/>.</summary>
        private static bool IsSilent(WeaponDefinition definition)
        {
            AudioClip[] clips = definition != null ? definition.FireClips : null;
            for (int i = 0; clips != null && i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Is every weapon's kit complete — WRITES NOTHING (read by the build readiness
        /// panel). Applies the same two tests as the end-of-run reports (grip not authored, no fire
        /// sound) by reading the WD assets from disk; a missing WD means the tool never ran for that
        /// weapon.</summary>
        internal static bool AreWeaponsReady(out string detail)
        {
            var missing = new List<string>();
            var unbaked = new List<string>();
            var silent = new List<string>();

            for (int i = 0; i < Specs.Length; i++)
            {
                string path = DataDir + "/WD_" + Specs[i].Name + ".asset";
                var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
                if (def == null)
                {
                    missing.Add(Specs[i].Name);
                    continue;
                }

                if (IsUnbaked(def))
                {
                    unbaked.Add(Specs[i].Name);
                }

                if (IsSilent(def))
                {
                    silent.Add(Specs[i].Name);
                }
            }

            var problems = new List<string>();
            if (missing.Count > 0)
            {
                problems.Add("WD asset'i yok: " + string.Join(", ", missing));
            }

            if (unbaked.Count > 0)
            {
                problems.Add("kavraması eksik (ana/ön kabza): " + string.Join(", ", unbaked));
            }

            if (silent.Count > 0)
            {
                problems.Add("ateş sesi atanmamış: " + string.Join(", ", silent));
            }

            if (problems.Count > 0)
            {
                detail = string.Join(" · ", problems);
                return false;
            }

            detail = $"{Specs.Length} silah: kavrama ve ateş sesi tamam.";
            return true;
        }

        /// <summary>Lists weapons with no fire sound in one warning. ⚠️ Mandatory: clips do not come
        /// from this tool (see <see cref="EnsureDefinition"/>), so a new weapon is born SILENT and
        /// the only symptom is "no fire sound" with nothing logged anywhere.</summary>
        private static void ReportSilentWeapons(WeaponDefinition[] defs)
        {
            var silent = new List<string>();
            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i] == null)
                {
                    continue;
                }

                if (IsSilent(defs[i]))
                {
                    silent.Add(defs[i].name);
                }
            }

            if (silent.Count == 0)
            {
                return;
            }

            Debug.LogWarning(Log + "Ateş sesi ATANMAMIŞ silahlar: " + string.Join(", ", silent) +
                             ". Bu silahlar sessiz ateş eder. Düzeltme: Assets/_Shared/Arsenal/Data/" +
                             "WD_<Ad>.asset'i seç ve Fire Clips / Mag Out Clip / Dry Fire Clip / " +
                             "Pickup Clip alanlarına klip sürükle (klipler bu araçtan gelmez).");
        }

        private static void ReportUnbakedWeapons()
        {
            if (_unbakedWeapons.Count == 0)
            {
                return;
            }

            Debug.LogWarning(Log + "Kavraması EKSİK silahlar (ana kabza yazılmamış ya da çift ellide ön " +
                             "kabza yazılmamış): " + string.Join(", ", _unbakedWeapons) +
                             ". Ana kabza yoksa oyuncunun eli silaha sarılmaz (idle kalır); ön kabza " +
                             "yoksa soket çizilmez ve ikinci el bağlanmaz. Düzeltme: Tools > VortexArena > " +
                             "Weapons > Kavrama Pozu Stüdyosu → WPN_* prefabını prefab kipinde aç → " +
                             "Ana Kabza + Ön Kabza Ellerini Oluştur → yerleştir → Kaydet.");
        }

        /// <summary>Removes a root component by full type name (silent if absent).</summary>
        private static void RemoveRootComponent(GameObject root, string fullName, string ctx, string why)
        {
            Component component = FindComponentByTypeFullName(root, fullName);
            if (component == null)
            {
                return;
            }

            Object.DestroyImmediate(component, true);
            Debug.Log(Log + ctx + ": " + component.GetType().Name + " kaldırıldı — " + why + ".");
        }

        /// <summary>CLEARS an ISDK interactable's <c>_interactorFilters</c> list (idempotent). There
        /// is no grab filter on the root (rationale in <see cref="ApplyGripKit"/>). Left uncleared,
        /// the missing entry from the removed socket component throws in ISDK's <c>Start</c> check
        /// and the weapon becomes ungrabbable — with an error pointing at a collection, not the
        /// weapon.</summary>
        private static void ClearInteractorFilters(Component interactable, string ctx)
        {
            var so = new SerializedObject(interactable);
            SerializedProperty filters = so.FindProperty("_interactorFilters");
            if (filters == null || !filters.isArray)
            {
                Warn(ctx + ": " + interactable.GetType().Name + " üzerinde '_interactorFilters' alanı yok " +
                     "ya da dizi değil (ISDK sözleşme kayması?) — filtre listesi denetlenemedi.");
                return;
            }

            if (filters.arraySize == 0)
            {
                return; // already empty - idempotent
            }

            filters.arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log(Log + ctx + ": " + interactable.GetType().Name + " filtre listesi boşaltıldı.");
        }

        /// <summary>Applies the weapon frame kit on a WPN root (idempotent): an INSTANCE of the
        /// <c>VA_WeaponFrame</c> prefab is placed under the root.
        /// <para>What the frame is: a scene weapon is no longer picked up off the floor — it stays
        /// inside its frame forever. Aiming from <c>WeaponFrame.maxGrabDistance</c> and pressing grip
        /// puts a CLONE in the hand (<see cref="WeaponFrame"/> →
        /// <c>WeaponGranter.SelectWeapon</c>). So every <c>WPN_*</c> prefab serves both as "held
        /// weapon" and "scene source"; the presence of the FRAME decides which (the clone's frame is
        /// destroyed).</para>
        /// <para>Why an INSTANCE and not an unpack: one fix in the frame (grab range, ray colour,
        /// collider size) must reach every weapon at once — same rule as placing infrastructure
        /// prefabs in a scene.</para>
        /// <para>⚠️ This does not contradict <see cref="ApplyGripKit"/>'s distance-grab removal:
        /// those steps use <see cref="FindComponentByTypeFullName"/>, which only looks at the ROOT's
        /// components. The ban is for the <c>WPN_*</c> root; the frame is a separate object and is
        /// where selection happens. Recursing would delete the frame's own grab and make the weapon
        /// unobtainable.</para></summary>
        private static void ApplyWeaponFrameKit(GameObject root, string ctx)
        {
            // Already present? Inactive children are scanned too (the frame visual starts disabled).
            if (root.GetComponentInChildren<WeaponFrame>(true) != null)
            {
                return; // idempotent
            }

            var framePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponFramePrefabPath);
            if (framePrefab == null)
            {
                Warn(ctx + ": '" + WeaponFramePrefabPath + "' bulunamadı — silah çerçevesi eklenemedi. " +
                     "Çerçeve prefabı bu araç tarafından ÜRETİLMEZ (elle authoring gerektiren bir " +
                     "görsel/ISDK kurulumu), yalnız bağlanır. Prefab geri gelene kadar bu silah " +
                     "sahnede alınamaz kalır.");
                return;
            }

            var frame = (GameObject)PrefabUtility.InstantiatePrefab(framePrefab, root.transform);
            if (frame == null)
            {
                Warn(ctx + ": VA_WeaponFrame örneklenemedi.");
                return;
            }

            frame.name = framePrefab.name;
            frame.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            frame.transform.localScale = Vector3.one;

            Debug.Log(Log + ctx + ": VA_WeaponFrame eklendi — silah artık çerçevesinden, " +
                      "maxGrabDistance mesafesinden alınır.");
        }

        /// <summary>Applies the dissolve kit on a WPN root (idempotent):
        /// <see cref="SimpleWeaponDissolve"/> + the <c>DissolveEffect.mat</c> binding. On pickup the
        /// model briefly switches to the dissolve material and fades in; the original materials are
        /// restored afterwards.
        /// <para>Why in the tool: the component is needed on every <c>WPN_*</c> root and added by
        /// hand it would be silently forgotten on a new weapon (which then just appears instantly and
        /// nobody notices). Same rationale as <see cref="ApplyWeaponFrameKit"/>.</para>
        /// <para>⚠️ The material field is written only when EMPTY (same rule as audio clips): a
        /// hand-bound dissolve material is never overwritten.</para></summary>
        private static void ApplyDissolveKit(GameObject root, string ctx)
        {
            var dissolve = root.GetComponent<SimpleWeaponDissolve>();
            if (dissolve == null)
            {
                dissolve = root.AddComponent<SimpleWeaponDissolve>();
                Debug.Log(Log + ctx + ": SimpleWeaponDissolve eklendi — silah ele çözülerek gelir.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(DissolveMaterialPath);
            if (material == null)
            {
                Warn(ctx + ": '" + DissolveMaterialPath + "' bulunamadı — çözülme materyali " +
                     "bağlanamadı. Bileşen takılı kalır ama efekt oynamaz (silah eskisi gibi " +
                     "anında belirir).");
                return;
            }

            var so = new SerializedObject(dissolve);
            SetObjectRefIfEmpty(so, "dissolveMaterial", material, ctx);
            SetNumber(so, "appearSeconds", DissolveAppearSeconds, ctx);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Strips release physics: <c>Grabbable._throwWhenUnselected = false</c>.
        /// <para>⚠️ Required because ISDK applies the hand's TRACKED velocity on release. But this
        /// weapon's root is not carried by ISDK during the hold —
        /// <c>Weapon.ApplyCanonicalGrip</c> teleports it from the canonical grip every frame (§6.6).
        /// A "velocity" derived from a teleported body is frame-difference noise, and the weapon flew
        /// out of the hand on release.</para>
        /// <para><c>_kinematicWhileSelected</c> stays ON (default): the body is kinematic during the
        /// hold so gravity accumulates no speed, and on release the weapon drops where it is. Together
        /// they give "drops on release, never flies".</para>
        /// <para>⚠️ Throwables (grenades) do NOT go through this gate: their throw is their own
        /// ballistics reported over the wire via <c>ArenaCombat.ReportThrow</c>, not an ISDK
        /// impulse.</para></summary>
        private static void ApplyReleasePhysics(GameObject root, string ctx)
        {
            Component grabbable = FindComponentByTypeFullName(root, "Oculus.Interaction.Grabbable");
            if (grabbable == null)
            {
                Warn(ctx + ": kökte Oculus.Interaction.Grabbable yok — bırakma fiziği ayarlanamadı " +
                     "(silah bırakılınca elden fırlayabilir).");
                return;
            }

            var so = new SerializedObject(grabbable);
            SerializedProperty throwProp = so.FindProperty("_throwWhenUnselected");
            if (throwProp == null)
            {
                Warn(ctx + ": Grabbable'da '_throwWhenUnselected' alanı yok (ISDK sözleşme kayması?) — " +
                     "bırakma fiziği ayarlanamadı.");
                return;
            }

            if (!throwProp.boolValue)
            {
                return; // already disabled - idempotent
            }

            throwProp.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log(Log + ctx + ": Grabbable._throwWhenUnselected kapatıldı — silah bırakılınca " +
                      "fırlamaz (poz kanonik kavramadan sürülüyor, ISDK'nın hız tahmini geçersiz).");
        }

        /// <summary>Tunes the muzzle flash particle modules (colour/size/lifetime/cone) per weapon.</summary>
        private static void ConfigureMuzzleFlash(ParticleSystem ps, WeaponSpec spec)
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(spec.FlashColorMin, spec.FlashColorMax);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.FlashSizeMin, spec.FlashSizeMax);
            main.startLifetime = spec.FlashLifetime;

            var shape = ps.shape;
            if (shape.enabled)
            {
                shape.angle = spec.FlashConeAngle;
            }
        }

        /// <summary>Creates/updates a "Smoke" child particle system under MuzzleFlash and wires it
        /// into the flash's Sub Emitters module with a "Birth" trigger, so the single
        /// <c>muzzleFlash.Emit()</c> in <c>Weapon.Fire()</c> drives both flash and smoke.</summary>
        private static void ConfigureMuzzleSmoke(ParticleSystem flashPs, WeaponSpec spec, Material smokeMaterial)
        {
            Transform flashT = flashPs.transform;
            Transform smokeT = flashT.Find("Smoke");
            GameObject smokeGo = smokeT != null ? smokeT.gameObject : new GameObject("Smoke");
            if (smokeT == null)
            {
                smokeGo.transform.SetParent(flashT, false);
            }

            ParticleSystem smokePs = smokeGo.GetComponent<ParticleSystem>();
            if (smokePs == null)
            {
                smokePs = smokeGo.AddComponent<ParticleSystem>();
            }

            var main = smokePs.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.3f;
            main.startLifetime = spec.SmokeLifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.SmokeSizeMin, spec.SmokeSizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.6f, 0.6f, spec.SmokeAlpha));
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = smokePs.emission;
            emission.enabled = true;
            emission.rateOverTime = 22f;

            var shape = smokePs.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.01f;

            var renderer = smokeGo.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
            {
                renderer = smokeGo.AddComponent<ParticleSystemRenderer>();
            }
            if (smokeMaterial != null)
            {
                renderer.sharedMaterial = smokeMaterial;
            }

            var subEmitters = flashPs.subEmitters;
            subEmitters.enabled = true;
            bool alreadyLinked = false;
            for (int i = 0; i < subEmitters.subEmittersCount; i++)
            {
                if (subEmitters.GetSubEmitterSystem(i) == smokePs)
                {
                    alreadyLinked = true;
                    break;
                }
            }
            if (!alreadyLinked)
            {
                subEmitters.AddSubEmitter(smokePs, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing, 1f);
            }
        }
    }
}
