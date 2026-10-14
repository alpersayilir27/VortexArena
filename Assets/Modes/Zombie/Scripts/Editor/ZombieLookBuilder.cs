using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Core.Player;

namespace VortexArena.Modes.Zombie.Editor
{
    /// <summary>Builds the zombie bodies from the imported Mixamo assets: clip trims, materials, the
    /// upper-body mask, <c>ZombieAnimator.controller</c>, the five <c>ZB_*</c> look prefabs and the
    /// <c>NO_zombie</c> prefab that picks between them.</summary>
    /// <remarks>⚠️ This tool is the SINGLE author of all of it. Everything it writes is overwritten on
    /// the next run, so a hand edit in the controller or a look prefab is lost without a warning — the
    /// change belongs here. The only exception is <c>ZombieLook</c>'s eye offsets, which the tool only
    /// ESTIMATES from the head bone and expects to be tuned by hand (it writes them only when they are
    /// still zero).
    /// <para>⚠️ The FBX instance is UNPACKED into the look prefab instead of staying a nested prefab:
    /// the eyes and the hitboxes hang off BONES, and as added-object overrides on a nested instance
    /// they are silently dropped whenever the model is reimported with a different bone hierarchy.
    /// Re-running the tool is what picks up a new model.</para></remarks>
    public static class ZombieLookBuilder
    {
        private const string ModelDir = "Assets/Modes/Zombie/Models";
        private const string AnimDir = "Assets/Modes/Zombie/Animations";
        private const string MaterialDir = "Assets/Modes/Zombie/Materials";
        private const string LookDir = "Assets/Modes/Zombie/Prefabs/Looks";
        private const string MaskPath = "Assets/Modes/Zombie/Animation/ZombieUpperBody.mask";
        private const string ControllerPath = "Assets/Modes/Zombie/Animation/ZombieAnimator.controller";
        private const string EyeMaterialPath = MaterialDir + "/M_ZB_EyeGlow.mat";
        private const string ZombiePrefabPath = "Assets/Modes/Zombie/Prefabs/NO_zombie.prefab";
        private const string DissolveMaterialPath = "Assets/_Shared/Materials/DissolveEffect.mat";
        private const string SmokeMaterialPath = "Assets/_Shared/FX/Materials/M_BlastSmoke.mat";
        private const string HeadSurfacePath = "Assets/_Shared/Data/Surfaces/SUR_ZombieHead.asset";
        private const string FleshSurfacePath = "Assets/_Shared/Data/Surfaces/SUR_ZombieFlesh.asset";

        /// <summary>Every look is scaled to this height (m) so one set of hitbox and attack-reach
        /// numbers fits all five models.</summary>
        private const float TargetHeight = 1.80f;

        /// <summary>Head-bone height (m) in the idle pose. ⚠️ Normalised on the IDLE pose, not the
        /// T-pose: the zombie clips are hunched, a 1.80 m T-pose stands ~1.35 m at the head and reads
        /// as a 1.5 m person.</summary>
        private const float TargetIdleHeadHeight = 1.60f;

        /// <summary>Self-light as a fraction of the albedo — a faint silhouette, not a lamp.</summary>
        private const float BodyGlow = 0.2f;

        private const int Fps = 30;

        private static readonly string[] LookNames = { "ZB_Ghoul", "ZB_Cop", "ZB_Soldier", "ZB_Clown", "ZB_Girl" };

        /// <summary>Nominal ground speed (m/s) of each locomotion set's WALK clip, after the
        /// normalising scale. Measured, not derived — the clips are hand animations with no root
        /// motion to read.</summary>
        private static readonly float[] WalkSpeeds = { 0.29f, 0.22f, 0.20f };

        private const float RunClipSpeed = 1.1f;

        /// <summary>One clip's import settings. <c>Last</c> &lt; 0 = keep the whole take.</summary>
        private readonly struct Trim
        {
            public readonly string Name;
            public readonly int First;
            public readonly int Last;
            public readonly bool Loop;

            public Trim(string name, int first, int last, bool loop)
            {
                Name = name;
                First = first;
                Last = last;
                Loop = loop;
            }
        }

        // ⚠️ Frame numbers are at 30 fps and are the ONE place the trims live. Mixamo clips start and
        // end on long idle tails: untrimmed, an attack takes a second to begin and the player is
        // already walking away.
        private static readonly Trim[] Trims =
        {
            new Trim("ZA_Walk_A", 0, -1, true),
            new Trim("ZA_Walk_B", 0, -1, true),
            new Trim("ZA_ZombieWalk", 0, -1, true),
            new Trim("ZA_Run", 0, -1, true),
            new Trim("ZA_Running", 0, -1, true),
            new Trim("ZA_Crawl", 0, -1, true),
            new Trim("ZA_Idle_A", 0, -1, true),
            new Trim("ZA_Idle_B", 0, -1, true),
            new Trim("ZA_Idle_Scratch", 0, -1, true),
            new Trim("ZA_Agonizing", 0, -1, true),
            new Trim("ZA_Attack_A", 36, 96, false),
            new Trim("ZA_Attack_B", 9, 66, false),
            new Trim("ZA_Punch", 0, 48, false),
            new Trim("ZA_Headbutt", 9, 66, false),
            new Trim("ZA_NeckBite", 0, 75, false),
            new Trim("ZA_HitReact_A", 0, 39, false),
            new Trim("ZA_HitReact_B", 0, 42, false),
            new Trim("ZA_StandUp_A", 105, 189, false),
            new Trim("ZA_StandUp_B", 110, 195, false),
            new Trim("ZA_Scream", 0, -1, false),
            // ⚠️ Skips the opening sway: the body must be down well before the dissolve starts
            // (ZombieView.DeathDissolveDelay), or it fades out while still falling.
            new Trim("ZA_Death", 18, -1, false),
            // ZA_Stumble is NOT used: it staggers 3.5 m sideways and only drops at its last frame.
        };

        [MenuItem("Tools/VortexArena/Zombie/Zombi görünüşlerini üret")]
        public static void Build()
        {
            TrimClips();

            EnsureFolder("Assets/Modes/Zombie/Animation");
            EnsureFolder(LookDir);

            AvatarMask mask = BuildMask();
            Material eyeMaterial = BuildEyeMaterial();
            AnimatorController controller = BuildController(mask);

            var lookPrefabs = new List<ZombieLook>();
            for (int i = 0; i < LookNames.Length; i++)
            {
                ZombieLook look = BuildLook(LookNames[i], controller, eyeMaterial);
                if (look != null)
                {
                    lookPrefabs.Add(look);
                }
            }

            UpdateZombiePrefab(lookPrefabs);

            AssetDatabase.SaveAssets();
            Debug.Log($"[ZombieLookBuilder] {lookPrefabs.Count} görünüş üretildi, " +
                      "denetleyici ve NO_zombie güncellendi.");
        }

        // ------------------------------------------------------------------ 1. clip trims

        private static void TrimClips()
        {
            for (int i = 0; i < Trims.Length; i++)
            {
                Trim trim = Trims[i];
                string path = $"{AnimDir}/{trim.Name}.fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    Debug.LogWarning($"[ZombieLookBuilder] {path} yok — klip kırpılamadı.");
                    continue;
                }

                ModelImporterClipAnimation[] clips = importer.clipAnimations;
                if (clips == null || clips.Length == 0)
                {
                    clips = importer.defaultClipAnimations;
                }

                if (clips == null || clips.Length == 0)
                {
                    Debug.LogWarning($"[ZombieLookBuilder] {trim.Name} içinde klip yok.");
                    continue;
                }

                ModelImporterClipAnimation clip = clips[0];
                clip.name = trim.Name;
                clip.firstFrame = trim.First;
                if (trim.Last >= 0)
                {
                    clip.lastFrame = trim.Last;
                }

                // Loop is set here, not left to the import default: an unlooped walk plays once and
                // the body freezes on the last frame mid-stride, which reads as a stuck zombie.
                clip.loopTime = trim.Loop;
                clip.loopPose = trim.Loop;

                clips[0] = clip;
                importer.clipAnimations = clips;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }

        /// <summary>Scale that puts the head bone at <see cref="TargetIdleHeadHeight"/> in the idle
        /// pose (averaged over the loop); 0 = could not measure.</summary>
        private static float IdleHeadScale(GameObject modelAsset)
        {
            AnimationClip idle = LoadClip("ZA_Idle_A");
            var probe = (GameObject)Object.Instantiate(modelAsset);
            probe.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Animator animator = probe.GetComponent<Animator>();
                Transform head = animator != null && animator.avatar != null && animator.avatar.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.Head)
                    : null;
                if (idle == null || head == null)
                {
                    return 0f;
                }

                const int samples = 6;
                float sum = 0f;
                for (int i = 0; i < samples; i++)
                {
                    idle.SampleAnimation(probe, idle.length * i / samples);
                    probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    sum += head.position.y;
                }

                float headHeight = sum / samples;
                return headHeight > 0.5f ? TargetIdleHeadHeight / headHeight : 0f;
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        private static AnimationClip LoadClip(string name)
        {
            string path = $"{AnimDir}/{name}.fbx";
            Object[] all = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    return clip;
                }
            }

            Debug.LogWarning($"[ZombieLookBuilder] {path} içinde AnimationClip bulunamadı.");
            return null;
        }

        // ------------------------------------------------------------------ 2. materials

        private static Material BuildLookMaterial(string lookName)
        {
            string path = $"{MaterialDir}/M_{lookName}.mat";
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}/{lookName}_Albedo.png");

            Material material = LoadOrCreate(path, "Universal Render Pipeline/Lit");
            if (material == null)
            {
                return null;
            }

            if (albedo != null)
            {
                material.SetTexture("_BaseMap", albedo);
            }

            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.15f);
            material.SetFloat("_Metallic", 0f);

            // Faint self-light (albedo × BodyGlow): the body is dynamic, so it only gets light probes,
            // and the night arenas' probes are near black — without this a zombie is no silhouette at
            // all. ⚠️ _EMISSION is fixed on the ASSET (one variant, shipped), never toggled at runtime.
            material.SetTexture("_EmissionMap", albedo);
            material.SetColor("_EmissionColor", new Color(BodyGlow, BodyGlow, BodyGlow, 1f));
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildEyeMaterial()
        {
            Material material = LoadOrCreate(EyeMaterialPath, "Universal Render Pipeline/Unlit");
            if (material == null)
            {
                return null;
            }

            // Unlit, not an emissive Lit: the eyes must read in a dark arena without a realtime light
            // and without the bloom budget of an HDR emissive.
            material.SetColor("_BaseColor", new Color(1f, 0.824f, 0.29f, 1f));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LoadOrCreate(string path, string shaderName)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[ZombieLookBuilder] {shaderName} shader'ı bulunamadı.");
                return null;
            }

            EnsureFolder(MaterialDir);
            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ------------------------------------------------------------------ 3. avatar mask

        private static AvatarMask BuildMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, MaskPath);
            }

            // Legs and root OFF: the hit flinch runs on a layer of its own, and letting it touch the
            // legs would stop the walk cycle dead for the length of the reaction.
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, false);

            EditorUtility.SetDirty(mask);
            return mask;
        }

        // ------------------------------------------------------------------ 4. animator controller

        /// <summary>Rebuilt from scratch every run: clearing layers in place leaves orphaned states and
        /// transitions inside the asset, and they come back as unreachable nodes in the graph.</summary>
        private static AnimatorController BuildController(AvatarMask mask)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("LocoRate", AnimatorControllerParameterType.Float);
            controller.AddParameter("LocoSet", AnimatorControllerParameterType.Int);
            controller.AddParameter("IdleIndex", AnimatorControllerParameterType.Int);
            controller.AddParameter("Crawler", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Spawn", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("SpawnIndex", AnimatorControllerParameterType.Int);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AttackIndex", AnimatorControllerParameterType.Int);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("HitIndex", AnimatorControllerParameterType.Int);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            controller.AddParameter("DeathIndex", AnimatorControllerParameterType.Int);

            // ⚠️ LocoRate defaults to 1: a controller that starts at 0 freezes every state's clip, and
            // the body stands in its first frame until the view's first Update.
            SetDefaultFloat(controller, "LocoRate", 1f);

            BuildBaseLayer(controller);
            BuildUpperBodyLayer(controller, mask);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void SetDefaultFloat(AnimatorController controller, string name, float value)
        {
            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == name)
                {
                    parameters[i].defaultFloat = value;
                }
            }

            controller.parameters = parameters;
        }

        private static void BuildBaseLayer(AnimatorController controller)
        {
            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            // ⚠️ The entry state carries Idle_A instead of being empty: an empty state holds the
            // bind pose, so every spawn and every attack exit would flash a T-pose for one frame
            // before the LocoSet transition fires.
            AnimatorState entry = sm.AddState("LocoEntry", new Vector3(260f, 0f, 0f));
            entry.motion = LoadClip("ZA_Idle_A");
            sm.defaultState = entry;

            AnimatorState[] loco =
            {
                BuildLocoState(controller, sm, "Loco_A", "ZA_Idle_A", "ZA_Walk_A", "ZA_Running", 0,
                    new Vector3(560f, -120f, 0f)),
                BuildLocoState(controller, sm, "Loco_B", "ZA_Idle_B", "ZA_ZombieWalk", "ZA_Run", 1,
                    new Vector3(560f, 0f, 0f)),
                // ⚠️ Scratching is a rolled variant only: as a base idle it played on every stop and
                // read as one endless loop.
                BuildLocoState(controller, sm, "Loco_C", "ZA_Idle_A", "ZA_Walk_B", "ZA_Running", 2,
                    new Vector3(560f, 120f, 0f)),
            };

            for (int i = 0; i < loco.Length; i++)
            {
                AnimatorStateTransition pick = entry.AddTransition(loco[i]);
                pick.hasExitTime = false;
                pick.duration = 0f;
                pick.AddCondition(AnimatorConditionMode.Equals, i, "LocoSet");
            }

            // LocoSet 0 is also the fallback: a body whose parameter was never written must still walk.
            // ⚠️ Needs an exit time: a transition with neither exit time nor condition is ignored.
            AnimatorStateTransition fallback = entry.AddTransition(loco[0]);
            fallback.hasExitTime = true;
            fallback.exitTime = 0.02f;
            fallback.duration = 0f;

            // ⚠️ IdleIndex values mirror ZombieView's Idle* constants.
            BuildIdleVariant(sm, loco, "Idle_Scratch", "ZA_Idle_Scratch", 1, new Vector3(840f, -120f, 0f));
            BuildIdleVariant(sm, loco, "Idle_Agonizing", "ZA_Agonizing", 2, new Vector3(840f, 120f, 0f));
            BuildIdleVariant(sm, loco, "Idle_AltA", "ZA_Idle_A", 3, new Vector3(840f, -240f, 0f));
            BuildIdleVariant(sm, loco, "Idle_AltB", "ZA_Idle_B", 4, new Vector3(840f, 240f, 0f));

            // ⚠️ AnyState order is PRIORITY order and the groups overlap (a dead crawler satisfies both
            // Dead and Crawler): death first, then the crawl, then spawn, then the attacks.
            // The only falling clip we have; the second variant is its mirror, so it drops to the other side.
            AddDeathState(sm, "Death", "ZA_Death", false, 0, new Vector3(260f, 360f, 0f));
            AddDeathState(sm, "Death_Mirror", "ZA_Death", true, 1, new Vector3(560f, 360f, 0f));

            AnimatorState crawl = sm.AddState("Crawl", new Vector3(260f, 240f, 0f));
            crawl.motion = LoadClip("ZA_Crawl");
            crawl.speedParameter = "LocoRate";
            crawl.speedParameterActive = true;
            AnimatorStateTransition toCrawl = sm.AddAnyStateTransition(crawl);
            toCrawl.hasExitTime = false;
            toCrawl.hasFixedDuration = true;
            toCrawl.duration = 0.2f;
            toCrawl.canTransitionToSelf = false;
            toCrawl.AddCondition(AnimatorConditionMode.If, 0f, "Crawler");
            toCrawl.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

            string[] spawnClips = { "ZA_StandUp_A", "ZA_StandUp_B", "ZA_Scream" };
            for (int i = 0; i < spawnClips.Length; i++)
            {
                AddOneShot(sm, loco, $"Spawn_{i}", spawnClips[i], "Spawn", "SpawnIndex", i,
                    new Vector3(-60f, -120f + i * 120f, 0f));
            }

            string[] attackClips = { "ZA_Attack_A", "ZA_Attack_B", "ZA_Punch", "ZA_Headbutt", "ZA_NeckBite" };
            for (int i = 0; i < attackClips.Length; i++)
            {
                AddOneShot(sm, loco, $"Attack_{i}", attackClips[i], "Attack", "AttackIndex", i,
                    new Vector3(840f, 280f + i * 90f, 0f));
            }
        }

        /// <summary>One locomotion set as a 1D blend tree on <c>Speed</c>, played at
        /// <c>LocoRate</c>.</summary>
        private static AnimatorState BuildLocoState(AnimatorController controller, AnimatorStateMachine sm,
            string name, string idleClip, string walkClip, string runClip, int locoSet, Vector3 position)
        {
            AnimatorState state = controller.CreateBlendTreeInController(name, out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;

            // Thresholds are the clips' OWN ground speeds, not the agent's: that is what makes the
            // blend honest, and LocoRate then stretches the winning clip to the real speed.
            tree.AddChild(LoadClip(idleClip), 0f);
            tree.AddChild(LoadClip(walkClip), WalkSpeeds[locoSet]);
            tree.AddChild(LoadClip(runClip), RunClipSpeed);

            state.speedParameter = "LocoRate";
            state.speedParameterActive = true;

            Reposition(sm, state, position);
            return state;
        }

        /// <summary>An idle flavour reached from any locomotion state while the body stands still.
        /// Agonising and scratching are what a WANDERING zombie does — the brain leaves it standing on
        /// an empty storey, and standing still is the only clue a remote headset has.</summary>
        private static void BuildIdleVariant(AnimatorStateMachine sm, AnimatorState[] loco,
            string name, string clipName, int idleIndex, Vector3 position)
        {
            AnimatorState state = sm.AddState(name, position);
            state.motion = LoadClip(clipName);

            for (int i = 0; i < loco.Length; i++)
            {
                AnimatorStateTransition enter = loco[i].AddTransition(state);
                enter.hasExitTime = false;
                enter.hasFixedDuration = true;
                enter.duration = 0.5f;

                // Lets "started walking" cut into a half-finished blend instead of waiting it out.
                enter.interruptionSource = TransitionInterruptionSource.Destination;
                enter.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
                enter.AddCondition(AnimatorConditionMode.Equals, idleIndex, "IdleIndex");
            }

            // Two ways out — it started walking, or the variant was rolled away. A single transition
            // cannot express the OR. Short when walking (feet would skate), long when re-rolled.
            ReturnToLoco(state, loco, false, 0f, 0.3f, AnimatorConditionMode.Greater, 0.15f, "Speed");
            ReturnToLoco(state, loco, false, 0f, 0.6f, AnimatorConditionMode.NotEqual, idleIndex, "IdleIndex");
        }

        /// <summary>Trigger + index state that plays once and blends back into the body's own
        /// locomotion set.</summary>
        private static void AddOneShot(AnimatorStateMachine sm, AnimatorState[] loco, string name,
            string clipName, string trigger, string indexParameter, int index, Vector3 position)
        {
            AnimatorState state = sm.AddState(name, position);
            state.motion = LoadClip(clipName);

            AnimatorStateTransition enter = sm.AddAnyStateTransition(state);
            enter.hasExitTime = false;
            enter.hasFixedDuration = true;
            enter.duration = 0.2f;

            // ⚠️ canTransitionToSelf off: the trigger survives one frame and would restart the clip
            // from the top, so a chain of attacks would never get past its first 0.2 s.
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            enter.AddCondition(AnimatorConditionMode.Equals, index, indexParameter);

            ReturnToLoco(state, loco, true, 0.85f, 0.3f, AnimatorConditionMode.If, 0f, null);
        }

        /// <summary>One transition per locomotion set, gated on <c>LocoSet</c>.</summary>
        /// <remarks>⚠️ Never back through <c>LocoEntry</c>: its hop to the set is instant, so every
        /// exit would blend into Idle_A and then snap to the real gait in one frame.</remarks>
        private static void ReturnToLoco(AnimatorState from, AnimatorState[] loco, bool hasExitTime,
            float exitTime, float duration, AnimatorConditionMode mode, float threshold, string parameter)
        {
            for (int i = 0; i < loco.Length; i++)
            {
                AnimatorStateTransition back = from.AddTransition(loco[i]);
                back.hasExitTime = hasExitTime;
                back.exitTime = exitTime;
                back.hasFixedDuration = true;
                back.duration = duration;
                back.AddCondition(AnimatorConditionMode.Equals, i, "LocoSet");

                if (parameter != null)
                {
                    back.AddCondition(mode, threshold, parameter);
                }
            }
        }

        /// <summary>Death has NO exit: the server despawns the object, and the view fades the corpse
        /// out. A transition back to locomotion would stand a corpse up if a late state arrived.</summary>
        private static void AddDeathState(AnimatorStateMachine sm, string name, string clipName,
            bool mirror, int deathIndex, Vector3 position)
        {
            AnimatorState state = sm.AddState(name, position);
            state.motion = LoadClip(clipName);
            state.mirror = mirror;

            AnimatorStateTransition enter = sm.AddAnyStateTransition(state);
            enter.hasExitTime = false;
            enter.hasFixedDuration = true;
            enter.duration = 0.2f;
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            enter.AddCondition(AnimatorConditionMode.Equals, deathIndex, "DeathIndex");
        }

        private static void BuildUpperBodyLayer(AnimatorController controller, AvatarMask mask)
        {
            var sm = new AnimatorStateMachine
            {
                name = "UpperBody",
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(sm, controller);

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = "UpperBody",
                stateMachine = sm,
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                avatarMask = mask,
            });

            // ⚠️ The default state carries NO motion on purpose: an empty state writes nothing, so the
            // layer passes the base layer's pose through. A placeholder clip here would pin the arms
            // for the zombie's whole life.
            AnimatorState idle = sm.AddState("Empty", new Vector3(260f, 0f, 0f));
            sm.defaultState = idle;

            string[] reactions = { "ZA_HitReact_A", "ZA_HitReact_B" };
            for (int i = 0; i < reactions.Length; i++)
            {
                AnimatorState state = sm.AddState($"Hit_{i}", new Vector3(560f, -90f + i * 180f, 0f));
                state.motion = LoadClip(reactions[i]);

                AnimatorStateTransition enter = sm.AddAnyStateTransition(state);
                enter.hasExitTime = false;
                enter.hasFixedDuration = true;
                enter.duration = 0.15f;
                enter.canTransitionToSelf = false;
                enter.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
                enter.AddCondition(AnimatorConditionMode.Equals, i, "HitIndex");

                AnimatorStateTransition leave = state.AddTransition(idle);
                leave.hasExitTime = true;
                leave.exitTime = 0.8f;
                leave.hasFixedDuration = true;
                leave.duration = 0.3f;
            }
        }

        private static void Reposition(AnimatorStateMachine sm, AnimatorState state, Vector3 position)
        {
            ChildAnimatorState[] states = sm.states;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].state == state)
                {
                    states[i].position = position;
                }
            }

            sm.states = states;
        }

        // ------------------------------------------------------------------ 5. look prefabs

        private static ZombieLook BuildLook(string lookName, AnimatorController controller, Material eyeMaterial)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{lookName}.fbx");
            if (modelAsset == null)
            {
                Debug.LogWarning($"[ZombieLookBuilder] {ModelDir}/{lookName}.fbx yok — görünüş atlandı.");
                return null;
            }

            string prefabPath = $"{LookDir}/{lookName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            ZombieLook existingLook = existing != null ? existing.GetComponent<ZombieLook>() : null;
            Vector3 keptLeftEye = Vector3.zero;
            Vector3 keptRightEye = Vector3.zero;
            if (existingLook != null)
            {
                var so = new SerializedObject(existingLook);
                keptLeftEye = so.FindProperty("leftEyeOffset").vector3Value;
                keptRightEye = so.FindProperty("rightEyeOffset").vector3Value;
            }

            var root = new GameObject(lookName);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);

            Animator animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            // Culling: the clips bake root motion into the pose, so a culled zombie must still have its
            // transforms updated or it would snap when it comes back on screen.
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            animator.updateMode = AnimatorUpdateMode.Normal;

            SkinnedMeshRenderer renderer = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer == null)
            {
                Debug.LogWarning($"[ZombieLookBuilder] {lookName} içinde SkinnedMeshRenderer yok.");
                Object.DestroyImmediate(root);
                return null;
            }

            renderer.sharedMaterial = BuildLookMaterial(lookName);
            renderer.quality = SkinQuality.Bone2;

            // ⚠️ Shadows OFF on Quest: a shadow caster re-skins the mesh for the shadow pass, so thirty
            // zombies would pay the skinning cost twice. The arenas are baked-lit anyway — the zombie
            // has no realtime light to cast from.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // ⚠️ World bounds, not sharedMesh bounds: the mesh sits under a 0.01-scaled node (Mixamo cm
            // rig), so its own bounds read ~190–220 and BakeMesh(useScale: true) reads centimetres too.
            float modelHeight = renderer.bounds.size.y;
            float scale = IdleHeadScale(modelAsset);
            if (scale <= 0f)
            {
                // Fallback: T-pose height (the idle clip or the humanoid head was not found).
                scale = modelHeight > 0.5f && modelHeight < 5f ? TargetHeight / modelHeight : 1f;
            }

            root.transform.localScale = Vector3.one * scale;

            Transform head = FindBone(animator, HumanBodyBones.Head);
            Transform hips = FindBone(animator, HumanBodyBones.Hips);
            Transform neck = FindBone(animator, HumanBodyBones.Neck) ?? head;
            Transform chest = FindBone(animator, HumanBodyBones.UpperChest)
                              ?? FindBone(animator, HumanBodyBones.Chest)
                              ?? FindBone(animator, HumanBodyBones.Spine);

            if (head == null || hips == null || chest == null)
            {
                Debug.LogWarning($"[ZombieLookBuilder] {lookName}: Humanoid kemikleri çözülemedi — " +
                                 "gözler ve vuruş kutuları kurulamadı. FBX'in Animation Type'ı " +
                                 "Humanoid mi?");
                Object.DestroyImmediate(root);
                return null;
            }

            Transform leftEye = BuildEye(head, "Eye_L", eyeMaterial);
            Transform rightEye = BuildEye(head, "Eye_R", eyeMaterial);

            Vector3 leftOffset = keptLeftEye != Vector3.zero
                ? keptLeftEye
                : EstimateEyeOffset(root.transform, head, -1f, renderer);
            Vector3 rightOffset = keptRightEye != Vector3.zero
                ? keptRightEye
                : EstimateEyeOffset(root.transform, head, 1f, renderer);
            leftEye.localPosition = leftOffset;
            rightEye.localPosition = rightOffset;

            // ⚠️ Every offset and size below is in REAL metres, with no scale factor: the root was just
            // normalised to 1.80 m, so world units are human units from here on. Multiplying by the
            // look's scale again would shrink the tall models' boxes by a fifth.
            var hitboxes = new List<GameObject>(13);

            Add(hitboxes, BuildHitbox(root.transform, head, "Hit_Head",
                head.position + root.transform.up * 0.085f + root.transform.forward * 0.015f,
                new Vector3(0.24f, 0.24f, 0.24f), HitZone.Head, HeadSurfacePath));

            // ⚠️ Chest and pelvis SHARE the torso instead of one tall box: the box is also the
            // ragdoll's chest body, and one running down to the hips sweeps through the thighs — the
            // corpse then shakes itself apart on the floor.
            Add(hitboxes, BuildHitbox(root.transform, chest, "Hit_Body",
                Vector3.Lerp(hips.position, neck.position, 0.68f),
                new Vector3(0.42f, 0.42f, 0.30f), HitZone.Body, FleshSurfacePath));

            Add(hitboxes, BuildHitbox(root.transform, hips, "Hit_Hips",
                Vector3.Lerp(hips.position, neck.position, 0.18f),
                new Vector3(0.36f, 0.30f, 0.28f), HitZone.Stomach, FleshSurfacePath));

            // Limbs: the whole body takes hits like a player's does, and the same capsules are the
            // ragdoll's arms and legs.
            Limb armUpperLeft = BuildLimb(root.transform, animator, hitboxes, "Hit_UpperArm_L",
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.055f, HitZone.Body);
            Limb armLowerLeft = BuildLimb(root.transform, animator, hitboxes, "Hit_ForeArm_L",
                HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.045f, HitZone.Body);
            Limb armUpperRight = BuildLimb(root.transform, animator, hitboxes, "Hit_UpperArm_R",
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.055f, HitZone.Body);
            Limb armLowerRight = BuildLimb(root.transform, animator, hitboxes, "Hit_ForeArm_R",
                HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.045f, HitZone.Body);

            Limb legUpperLeft = BuildLimb(root.transform, animator, hitboxes, "Hit_UpLeg_L",
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.085f, HitZone.Leg);
            Limb legLowerLeft = BuildLimb(root.transform, animator, hitboxes, "Hit_Leg_L",
                HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.065f, HitZone.Leg);
            Limb legUpperRight = BuildLimb(root.transform, animator, hitboxes, "Hit_UpLeg_R",
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.085f, HitZone.Leg);
            Limb legLowerRight = BuildLimb(root.transform, animator, hitboxes, "Hit_Leg_R",
                HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.065f, HitZone.Leg);

            BuildRagdoll(root.transform, animator, hitboxes, hips, chest, head, neck,
                armUpperLeft, armLowerLeft, armUpperRight, armLowerRight,
                legUpperLeft, legLowerLeft, legUpperRight, legLowerRight);

            BuildFlinch(root, animator, hips, chest, head, neck);

            var dissolve = root.AddComponent<ZombieDissolve>();
            var dissolveObject = new SerializedObject(dissolve);
            dissolveObject.FindProperty("dissolveMaterial").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Material>(DissolveMaterialPath);
            SerializedProperty targets = dissolveObject.FindProperty("targets");
            targets.arraySize = 1;
            targets.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            dissolveObject.ApplyModifiedPropertiesWithoutUndo();

            var look = root.AddComponent<ZombieLook>();
            var lookObject = new SerializedObject(look);
            lookObject.FindProperty("animator").objectReferenceValue = animator;
            lookObject.FindProperty("bodyRenderer").objectReferenceValue = renderer;
            lookObject.FindProperty("dissolve").objectReferenceValue = dissolve;
            lookObject.FindProperty("headBone").objectReferenceValue = head;
            lookObject.FindProperty("leftEye").objectReferenceValue = leftEye;
            lookObject.FindProperty("rightEye").objectReferenceValue = rightEye;
            lookObject.FindProperty("leftEyeOffset").vector3Value = leftOffset;
            lookObject.FindProperty("rightEyeOffset").vector3Value = rightOffset;

            SerializedProperty hitboxProperty = lookObject.FindProperty("hitboxes");
            hitboxProperty.arraySize = hitboxes.Count;
            for (int i = 0; i < hitboxes.Count; i++)
            {
                hitboxProperty.GetArrayElementAtIndex(i).objectReferenceValue = hitboxes[i];
            }

            SerializedProperty walks = lookObject.FindProperty("walkSpeeds");
            walks.arraySize = WalkSpeeds.Length;
            for (int i = 0; i < WalkSpeeds.Length; i++)
            {
                walks.GetArrayElementAtIndex(i).floatValue = WalkSpeeds[i];
            }

            lookObject.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);

            return saved != null ? saved.GetComponent<ZombieLook>() : null;
        }

        /// <summary>Resolves a humanoid bone. Name search is the fallback: the avatars are
        /// auto-generated, so a model whose mapping failed must say so instead of silently building a
        /// body with no hitboxes.</summary>
        private static Transform FindBone(Animator animator, HumanBodyBones bone)
        {
            Transform resolved = animator.avatar != null && animator.avatar.isHuman
                ? animator.GetBoneTransform(bone)
                : null;
            if (resolved != null)
            {
                return resolved;
            }

            string suffix = bone.ToString();
            Transform[] all = animator.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                {
                    return all[i];
                }
            }

            return null;
        }

        private static Transform BuildEye(Transform head, string name, Material material)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;

            // The primitive ships with a collider: on a zombie it would block rounds in front of the
            // head hitbox and eat headshots.
            Object.DestroyImmediate(sphere.GetComponent<Collider>());

            sphere.transform.SetParent(head, false);

            // Diameter 0.024 m in WORLD metres, so the division is by the bone's accumulated scale.
            float bone = BoneScale(head);
            sphere.transform.localScale = Vector3.one * (0.024f / bone);

            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return sphere.transform;
        }

        /// <summary>Eye position guessed from the head bone: forward and up from the bone's origin,
        /// measured along the BODY's axes (the bone's own axes differ per rig). Only a starting
        /// point — the offsets are serialized so they can be nudged per look.</summary>
        /// <summary>Eye position measured from the mesh: just behind the nose tip, ~45% up the skull
        /// (head bone → crown). Fixed offsets missed on four of five heads.</summary>
        private static Vector3 EstimateEyeOffset(Transform root, Transform head, float side,
            SkinnedMeshRenderer renderer)
        {
            // Measured at root scale 1 (bone-local result is scale-free): BakeMesh(useScale: false)
            // ignores the root's scale and would disagree with the scaled bone positions.
            Vector3 rootScale = root.localScale;
            root.localScale = Vector3.one;
            var baked = new Mesh();
            renderer.BakeMesh(baked, false);
            Vector3[] vertices = baked.vertices;
            Object.DestroyImmediate(baked);

            Transform t = renderer.transform;
            float noseForward = float.MinValue;
            float crown = head.position.y;
            Vector3 headFlat = Vector3.ProjectOnPlane(head.position, root.up);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = t.position + t.rotation * vertices[i];
                if (world.y < head.position.y - 0.02f ||
                    (Vector3.ProjectOnPlane(world, root.up) - headFlat).magnitude > 0.14f)
                {
                    continue;
                }

                crown = Mathf.Max(crown, world.y);
                noseForward = Mathf.Max(noseForward, Vector3.Dot(world - head.position, root.forward));
            }

            float forward = noseForward > 0f ? noseForward - 0.025f : 0.085f;
            float up = crown > head.position.y ? (crown - head.position.y) * 0.45f : 0.055f;
            Vector3 eye = head.position + root.up * up + root.forward * forward + root.right * (side * 0.032f);
            Vector3 local = head.InverseTransformPoint(eye);
            root.localScale = rootScale;
            return local;
        }

        private static GameObject BuildHitbox(Transform root, Transform bone, string name,
            Vector3 worldCentre, Vector3 worldSize, HitZone zone, string surfacePath)
        {
            GameObject box = NewHitbox(bone, name, zone, surfacePath);
            box.transform.localPosition = bone.InverseTransformPoint(worldCentre);

            // Axis-aligned with the BODY, not with the bone: the sizes above are body measurements,
            // and Mixamo's bone axes run along the bone.
            box.transform.localRotation = Quaternion.Inverse(bone.rotation) * root.rotation;

            var collider = box.AddComponent<BoxCollider>();
            collider.size = worldSize / BoneScale(bone);

            return box;
        }

        /// <summary>One limb segment: the bone and the bone it runs to. ⚠️ A segment missing EITHER
        /// of them stays all-null, so the ragdoll never builds a body it has no collider for.</summary>
        private struct Limb
        {
            public Transform Bone;
            public Transform End;
        }

        /// <summary>Capsule hit box along a bone — the ragdoll's arm/leg body at the same time.</summary>
        private static Limb BuildLimb(Transform root, Animator animator, List<GameObject> hitboxes,
            string name, HumanBodyBones bone, HumanBodyBones end, float worldRadius, HitZone zone)
        {
            var limb = new Limb
            {
                Bone = FindBone(animator, bone),
                End = FindBone(animator, end),
            };

            if (limb.Bone == null || limb.End == null)
            {
                // Not fatal: a rig missing a forearm still fights with the rest of its boxes.
                Debug.LogWarning($"[ZombieLookBuilder] {bone}/{end} kemiği çözülemedi — {name} " +
                                 "kutusu ve ragdoll parçası atlandı.");
                limb.Bone = null;
                limb.End = null;
                return limb;
            }

            GameObject box = NewHitbox(limb.Bone, name, zone, FleshSurfacePath);

            // On the bone's OWN axes (unlike the body boxes): a capsule only has to run along the
            // limb, and the bone's axes are the one frame that stays aligned with it in every pose.
            box.transform.localPosition = Vector3.zero;
            box.transform.localRotation = Quaternion.identity;

            Vector3 toEnd = limb.Bone.InverseTransformPoint(limb.End.position);
            float radius = worldRadius / BoneScale(limb.Bone);

            var collider = box.AddComponent<CapsuleCollider>();
            collider.direction = DominantAxis(toEnd);
            collider.center = toEnd * 0.5f;
            collider.radius = radius;
            collider.height = Mathf.Max(toEnd.magnitude, radius * 2f);

            Add(hitboxes, box);
            return limb;
        }

        private static GameObject NewHitbox(Transform bone, string name, HitZone zone, string surfacePath)
        {
            var box = new GameObject(name);
            box.transform.SetParent(bone, false);

            var hitBox = box.AddComponent<RemoteHitBox>();
            var hitObject = new SerializedObject(hitBox);

            // ⚠️ `avatar` stays empty: this is a NETWORK OBJECT box, the hit is reported with
            // targetNetId, not with a playerId.
            hitObject.FindProperty("zone").enumValueIndex = (int)zone;
            hitObject.ApplyModifiedPropertiesWithoutUndo();

            var tag = box.AddComponent<SurfaceTag>();
            var tagObject = new SerializedObject(tag);
            tagObject.FindProperty("surface").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<SurfaceDefinition>(surfacePath);
            tagObject.ApplyModifiedPropertiesWithoutUndo();

            return box;
        }

        private static void Add(List<GameObject> hitboxes, GameObject box)
        {
            if (box != null)
            {
                hitboxes.Add(box);
            }
        }

        /// <summary>Index of the longest component of <paramref name="local"/> (0 = x, 1 = y, 2 = z) —
        /// the axis a capsule on this bone runs along.</summary>
        private static int DominantAxis(Vector3 local)
        {
            float x = Mathf.Abs(local.x);
            float y = Mathf.Abs(local.y);
            float z = Mathf.Abs(local.z);

            if (x >= y && x >= z)
            {
                return 0;
            }

            return y >= z ? 1 : 2;
        }

        /// <summary>Accumulated scale on a bone. Uniform on these rigs; the x component is enough and
        /// keeps the box sizes isotropic.</summary>
        private static float BoneScale(Transform bone)
        {
            float value = bone.lossyScale.x;
            return Mathf.Abs(value) > 0.0001f ? value : 1f;
        }

        // ------------------------------------------------------------------ 5b. ragdoll + flinch

        /// <summary>Mass (kg) per ragdoll body, in the order of <c>BuildRagdoll</c>'s bone array —
        /// ~60 kg in total, split the way Unity's own ragdoll builder splits it.</summary>
        private static readonly float[] RagdollMasses =
            { 11f, 11f, 5f, 3f, 2.5f, 3f, 2.5f, 6f, 5f, 6f, 5f };

        /// <summary>Index of each body's parent body; <c>-1</c> = the root (hips).</summary>
        private static readonly int[] RagdollParents = { -1, 0, 1, 1, 3, 1, 5, 0, 7, 0, 9 };

        // Joint limits (deg). Twist runs ALONG the bone, swing1 around the body's across axis (the
        // elbow/knee bend) and swing2 is the sideways spread — 5° on a hinge, never 0: a perfectly
        // rigid constraint makes the solver ring.
        private static readonly float[] RagdollTwistLow = { 0f, -20f, -40f, -40f, -10f, -40f, -10f, -20f, -10f, -20f, -10f };
        private static readonly float[] RagdollTwistHigh = { 0f, 20f, 25f, 40f, 10f, 40f, 10f, 20f, 10f, 20f, 10f };
        private static readonly float[] RagdollSwing1 = { 0f, 25f, 35f, 70f, 80f, 70f, 80f, 60f, 80f, 60f, 80f };
        private static readonly float[] RagdollSwing2 = { 0f, 15f, 25f, 40f, 5f, 40f, 5f, 25f, 5f, 25f, 5f };

        /// <summary>Builds the corpse's physics: a rigidbody per bone and a character joint to its
        /// parent. The hit boxes above double as its colliders.</summary>
        /// <remarks>⚠️ Bodies are created KINEMATIC: a live zombie is driven by the Animator, and a
        /// dynamic skeleton would fight it from the first frame. <see cref="ZombieRagdoll"/> releases
        /// them at death and freezes them again before the dissolve.</remarks>
        private static void BuildRagdoll(Transform root, Animator animator, List<GameObject> hitboxes,
            Transform hips, Transform chest, Transform head, Transform neck,
            Limb armUpperLeft, Limb armLowerLeft, Limb armUpperRight, Limb armLowerRight,
            Limb legUpperLeft, Limb legLowerLeft, Limb legUpperRight, Limb legLowerRight)
        {
            // ⚠️ Order is the contract shared by the mass/parent/limit tables above.
            Transform[] bones =
            {
                hips, chest, head,
                armUpperLeft.Bone, armLowerLeft.Bone, armUpperRight.Bone, armLowerRight.Bone,
                legUpperLeft.Bone, legLowerLeft.Bone, legUpperRight.Bone, legLowerRight.Bone,
            };

            // Where each bone RUNS TO: the joint's twist axis and nothing else. The head has no child
            // bone, so the crown is estimated.
            Vector3[] ends =
            {
                chest.position,
                neck.position,
                head.position + root.up * 0.17f,
                EndOf(armLowerLeft.Bone), EndOf(armLowerLeft.End),
                EndOf(armLowerRight.Bone), EndOf(armLowerRight.End),
                EndOf(legLowerLeft.Bone), EndOf(legLowerLeft.End),
                EndOf(legLowerRight.Bone), EndOf(legLowerRight.End),
            };

            var bodies = new Rigidbody[bones.Length];

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null)
                {
                    continue;
                }

                var body = bones[i].gameObject.AddComponent<Rigidbody>();
                body.mass = RagdollMasses[i];
                body.isKinematic = true;

                // Interpolation and continuous detection are per-body costs on Quest and buy nothing
                // on a corpse that is frozen again after a couple of seconds.
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                bodies[i] = body;
            }

            for (int i = 1; i < bones.Length; i++)
            {
                if (bones[i] == null || bodies[i] == null)
                {
                    continue;
                }

                Rigidbody parent = ParentBody(bodies, i);
                if (parent == null)
                {
                    continue;
                }

                var joint = bones[i].gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parent;
                joint.anchor = Vector3.zero;
                joint.autoConfigureConnectedAnchor = true;

                // ⚠️ Collision between jointed parts OFF: neighbouring capsules overlap by design and
                // with it on the corpse pushes itself apart the moment it goes dynamic.
                joint.enableCollision = false;
                joint.enablePreprocessing = false;

                // Vector3.zero = the segment this bone runs to was not resolved (EndOf) — the joint
                // then twists around the body's up instead of around a garbage direction.
                Vector3 along = ends[i] != Vector3.zero ? ends[i] - bones[i].position : Vector3.zero;
                Vector3 twist = along.sqrMagnitude > 1e-6f ? along.normalized : root.up;
                joint.axis = bones[i].InverseTransformDirection(twist);
                joint.swingAxis = bones[i].InverseTransformDirection(PerpendicularAxis(root, twist));

                joint.lowTwistLimit = new SoftJointLimit { limit = RagdollTwistLow[i] };
                joint.highTwistLimit = new SoftJointLimit { limit = RagdollTwistHigh[i] };
                joint.swing1Limit = new SoftJointLimit { limit = RagdollSwing1[i] };
                joint.swing2Limit = new SoftJointLimit { limit = RagdollSwing2[i] };
            }

            var ragdoll = root.gameObject.AddComponent<ZombieRagdoll>();
            var ragdollObject = new SerializedObject(ragdoll);
            ragdollObject.FindProperty("animator").objectReferenceValue = animator;
            ragdollObject.FindProperty("hips").objectReferenceValue = hips;

            SerializedProperty bodyList = ragdollObject.FindProperty("bodies");
            int written = 0;
            bodyList.arraySize = bones.Length;
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] != null)
                {
                    bodyList.GetArrayElementAtIndex(written++).objectReferenceValue = bodies[i];
                }
            }

            bodyList.arraySize = written;

            SerializedProperty colliderList = ragdollObject.FindProperty("colliders");
            colliderList.arraySize = hitboxes.Count;
            for (int i = 0; i < hitboxes.Count; i++)
            {
                colliderList.GetArrayElementAtIndex(i).objectReferenceValue =
                    hitboxes[i].GetComponent<Collider>();
            }

            ragdollObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Vector3 EndOf(Transform bone) => bone != null ? bone.position : Vector3.zero;

        /// <summary>Nearest EXISTING ancestor body: a rig missing a forearm must hang its hand bones
        /// off the upper arm instead of losing the joint altogether.</summary>
        private static Rigidbody ParentBody(Rigidbody[] bodies, int index)
        {
            int parent = RagdollParents[index];
            while (parent >= 0 && bodies[parent] == null)
            {
                parent = RagdollParents[parent];
            }

            return parent >= 0 ? bodies[parent] : null;
        }

        /// <summary>A body axis as perpendicular to <paramref name="along"/> as possible — the joint's
        /// swing axis. ⚠️ Picked, not fixed: a T-posed arm runs along the body's right, and a fixed
        /// right axis would collapse onto the twist axis and leave the elbow unconstrained.</summary>
        private static Vector3 PerpendicularAxis(Transform root, Vector3 along)
        {
            float right = Mathf.Abs(Vector3.Dot(along, root.right));
            float up = Mathf.Abs(Vector3.Dot(along, root.up));
            float forward = Mathf.Abs(Vector3.Dot(along, root.forward));

            if (right <= up && right <= forward)
            {
                return root.right;
            }

            return forward <= up ? root.forward : root.up;
        }

        /// <summary>Bone handles for the procedural hit reaction.</summary>
        private static void BuildFlinch(GameObject root, Animator animator, Transform hips,
            Transform chest, Transform head, Transform neck)
        {
            // The chest is already the highest spine bone the rig has; spine1 is the one below it.
            Transform spine1 = FindBone(animator, HumanBodyBones.Chest);
            if (spine1 == null || spine1 == chest)
            {
                spine1 = FindBone(animator, HumanBodyBones.Spine);
            }

            if (spine1 == chest)
            {
                spine1 = null;
            }

            var flinch = root.AddComponent<ZombieFlinch>();
            var flinchObject = new SerializedObject(flinch);
            flinchObject.FindProperty("spine1").objectReferenceValue = spine1;
            flinchObject.FindProperty("spine2").objectReferenceValue = chest;
            flinchObject.FindProperty("neck").objectReferenceValue = neck;
            flinchObject.FindProperty("head").objectReferenceValue = head;
            flinchObject.FindProperty("shoulderLeft").objectReferenceValue =
                FindBone(animator, HumanBodyBones.LeftShoulder);
            flinchObject.FindProperty("upperArmLeft").objectReferenceValue =
                FindBone(animator, HumanBodyBones.LeftUpperArm);
            flinchObject.FindProperty("shoulderRight").objectReferenceValue =
                FindBone(animator, HumanBodyBones.RightShoulder);
            flinchObject.FindProperty("upperArmRight").objectReferenceValue =
                FindBone(animator, HumanBodyBones.RightUpperArm);
            flinchObject.FindProperty("hips").objectReferenceValue = hips;
            flinchObject.FindProperty("upperLegLeft").objectReferenceValue =
                FindBone(animator, HumanBodyBones.LeftUpperLeg);
            flinchObject.FindProperty("upperLegRight").objectReferenceValue =
                FindBone(animator, HumanBodyBones.RightUpperLeg);
            flinchObject.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ 6. NO_zombie

        private static void UpdateZombiePrefab(List<ZombieLook> lookPrefabs)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(ZombiePrefabPath);
            if (contents == null)
            {
                Debug.LogError($"[ZombieLookBuilder] {ZombiePrefabPath} açılamadı.");
                return;
            }

            try
            {
                // The procedural body and the root hitboxes are gone: the body is a look prefab now and
                // the boxes live on its bones.
                DestroyChild(contents, "Body");
                DestroyChild(contents, "Hitboxes");

                ParticleSystem fog = BuildSpawnFog(contents);

                var view = contents.GetComponent<ZombieView>();
                if (view == null)
                {
                    Debug.LogError("[ZombieLookBuilder] NO_zombie üzerinde ZombieView yok.");
                    return;
                }

                var viewObject = new SerializedObject(view);
                SerializedProperty looks = viewObject.FindProperty("looks");
                looks.arraySize = lookPrefabs.Count;
                for (int i = 0; i < lookPrefabs.Count; i++)
                {
                    looks.GetArrayElementAtIndex(i).objectReferenceValue = lookPrefabs[i];
                }

                viewObject.FindProperty("spawnFog").objectReferenceValue = fog;
                viewObject.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(contents, ZombiePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void DestroyChild(GameObject root, string name)
        {
            Transform child = root.transform.Find(name);
            if (child != null)
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>Smoke burst under the body when it appears or changes storey.</summary>
        private static ParticleSystem BuildSpawnFog(GameObject root)
        {
            Transform existing = root.transform.Find("SpawnFog");
            GameObject fogObject = existing != null ? existing.gameObject : new GameObject("SpawnFog");
            if (existing == null)
            {
                fogObject.transform.SetParent(root.transform, false);
            }

            // Rotated to face up: the cone shape emits along its own +Z.
            fogObject.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            fogObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            // ⚠️ No '??': a missing component is Unity's fake null, which C# '??' treats as non-null.
            ParticleSystem system = fogObject.GetComponent<ParticleSystem>();
            if (system == null)
            {
                system = fogObject.AddComponent<ParticleSystem>();
            }

            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.4f;
            main.startLifetime = 1.4f;
            main.startSpeed = 0.4f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.52f, 0.42f, 0.5f));
            main.gravityModifier = 0f;
            main.maxParticles = 24;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // ⚠️ Stop Action stays None: a Destroy here would delete the node at the end of the FIRST
            // burst, and the zombie would then change storeys with no smoke and no error line.
            main.stopAction = ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)12) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.3f;

            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(0f, 1f),
                });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule grow = system.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.4f));

            var fogRenderer = fogObject.GetComponent<ParticleSystemRenderer>();
            fogRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            // An existing smoke material, not a new one: a new shader here is a new variant to compile
            // on the headset for an effect that already has one.
            fogRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SmokeMaterialPath);
            fogRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fogRenderer.receiveShadows = false;

            return system;
        }

        // ------------------------------------------------------------------ helpers

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int split = path.LastIndexOf('/');
            string parent = path.Substring(0, split);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
        }
    }
}
