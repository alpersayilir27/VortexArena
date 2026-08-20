using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Prunes the rig's VISUAL representations: <b>in the headset the player sees only the rig's
    /// synthetic hands</b> — no controller model is drawn, no ghost hands of distance grabbing are
    /// drawn, <b>no pointer ray is drawn</b>, and there is no body/arm at all.
    /// <para>
    /// ⚠️ <b>For the ray, what is disabled is NOT the INTERACTOR but its <c>Visuals</c> node.</b>
    /// (Under <c>ControllerRayInteractor</c> and <c>HandRayInteractor</c>.) The interactor itself stays
    /// alive: ISDK's UI pointing path (<c>PointableCanvasModule</c>) depends on it, and this
    /// component's job is to silence VISUALS, not to disable behaviour. On the arena side the ray has
    /// nothing to point at — the weapon frame draws its own indicator and grabbing happens through
    /// near/distance grab — so a drawn ray is nothing but noise on the player's screen.
    /// </para>
    /// <para>
    /// ⚠️ <b>The hand the player sees does NOT come from here and is NOT touched here:</b> that hand is
    /// ISDK's synthetic hand (<c>OVRHandVisualLeft</c>/<c>OVRHandVisualRight</c> →
    /// <c>SyntheticHandData</c>) and is driven by its own <c>HandVisual</c>. This component's only job
    /// is to silence the secondary visuals that overlay it — if they were all drawn at once the player
    /// would see intersecting hands and a controller model sitting in their hand.
    /// </para>
    /// <para>
    /// ⚠️ <b>Whether the synthetic hand is visible does NOT depend on this component but on a single
    /// rig setting:</b> <c>OVRManager.controllerDrivenHandPosesType</c> (<c>Natural</c> in the
    /// <c>VA_CameraRig</c> prefab). If it becomes <c>None</c>, no hand data is produced at all while a
    /// controller is held, <c>HandVisual</c> disables its own mesh and the player sees NO hands — even
    /// if nothing changed in this component.
    /// </para>
    /// <para>
    /// ⚠️ <b>THE NAMES ARE ALMOST IDENTICAL — only the word ORDER differs.</b> There are two separate
    /// hand families in the rig:
    /// <list type="bullet">
    /// <item><c>OVRHandVisualLeft</c> / <c>OVRHandVisualRight</c> — <b>the hand the player sees</b>, a
    /// direct child of the interaction rig. Never touched.</item>
    /// <item><c>OVRLeftHandVisual</c> / <c>OVRRightHandVisual</c> — the distance-grab ghost, under
    /// <c>…/DistanceHandGrabInteractor/Visuals/…Reticle/…Synthetic/</c>. Its object is
    /// disabled.</item>
    /// </list>
    /// Both have the same component type (<c>HandVisual</c>), so the type cannot tell them apart; the
    /// only thing that can is the name. That is why matching uses the <b>full name</b> (NOT contains):
    /// "contains" would catch both families, the real hands would be disabled too and <b>the player
    /// would lose their hands entirely</b>. If the name list matches nothing,
    /// <see cref="ErrorNoDrivenHandVisual"/> reports it explicitly.
    /// </para>
    /// <para>
    /// <b>Why the component type and NOT a name pattern (for the ones being disabled):</b> a name
    /// pattern like <c>questController_animrig</c> matches 24 objects but <b>none of them is
    /// active</b> — that is the Quest 1 / Rift S variant. The variant active on Quest 3 is
    /// <c>MetaQuestTouchPlus_Left/Right</c> and does not match the pattern AT ALL. The GameObject name
    /// changes with the hardware variant; <b>the component type does not</b>. The name is used only for
    /// the question "which hand is THE PLAYER'S", and the type cannot answer that (all six objects have
    /// the same component).
    /// </para>
    /// <para>
    /// ⚠️ <b>THE UNTOUCHABLES</b> — grabbing/interaction depends on these, disabling them breaks the
    /// game: <c>SyntheticHand</c>, <c>OVRHand</c>, the interactors, <c>HandSphereMap</c>. That is why
    /// the scan is kept LIMITED to two types instead of sweeping up "anything that resembles a hand".
    /// </para>
    /// <para>
    /// Hiding is redone every frame: these visuals are RE-ACTIVATED by Meta when a controller is put
    /// down and picked up again — a one-shot hide does not stick.
    /// </para>
    /// </summary>
    public class ControllerModelHider : MonoBehaviour
    {
        /// <summary>
        /// The second type to scan for: ISDK's hand visual.
        /// <para>⚠️ The type <b>cannot be written directly</b> (<c>Oculus.Interaction.Input.HandVisual</c>):
        /// writing it would require adding an <c>Oculus.Interaction</c> reference to the Core asmdef.
        /// Instead <see cref="MonoBehaviour"/>s are scanned and the type NAME is compared — unlike the
        /// GameObject name, that name does not change with the hardware variant.</para>
        /// </summary>
        private const string HandVisualTypeName = "HandVisual";

        /// <summary>
        /// The third type to scan for: ISDK's pointer ray interactor (it is the <b>same</b> type on both
        /// the controller and the hand branch — the <c>ControllerRayInteractor</c> and
        /// <c>HandRayInteractor</c> objects are separate but their components are identical, so a single
        /// type catches both).
        /// <para>⚠️ The type is again looked up BY NAME: writing it directly would mean adding an
        /// <c>Oculus.Interaction</c> reference to the Core asmdef.</para>
        /// </summary>
        private const string RayInteractorTypeName = "RayInteractor";

        /// <summary>
        /// The visual container under the ray interactor — this is the only thing that gets disabled.
        /// <para>⚠️ The interactor ITSELF is not disabled (see the class documentation): ripping out the
        /// behaviour just to remove visual noise would be a loss whose cause could not be found later,
        /// when pointing at a world UI becomes necessary.</para>
        /// </summary>
        private const string RayVisualsNodeName = "Visuals";

        /// <summary>
        /// Interval for rescanning the whole rig (s). ⚠️ <b>It is not scanned every frame:</b> the rig
        /// carries hundreds of components and walking the entire subtree every frame is a measurable
        /// cost on Quest. A new visual only appears when the rig is rebuilt (rare on a human time
        /// scale); what Meta does on put-down/pick-up is not to create a new object but to re-enable a
        /// KNOWN one — and that one is disabled every frame below.
        /// </summary>
        private const float RescanIntervalSeconds = 0.5f;

        [Tooltip("Rig kökünün adı. Bulunamazsa OVRCameraRig tipinden aranır — bu alan yalnız hızlandırıcıdır.")]
        [SerializeField] private string rigRootName = "VA_CameraRig";

        [Tooltip("OYUNCUNUN GÖRDÜĞÜ eller: bu adlardaki el görsellerine HİÇ dokunulmaz (tam ad " +
                 "eşleşmesi). Benzer adlı hayalet eller için sınıf açıklamasına bak.")]
        [SerializeField] private string[] drivenHandVisuals =
        {
            "OVRHandVisualLeft",
            "OVRHandVisualRight",
        };

        private Transform rigRoot;
        private readonly List<MonoBehaviour> scanBuffer = new List<MonoBehaviour>(256);

        /// <summary>Visual roots whose object gets fully disabled (controller models + ghost hands).</summary>
        private readonly List<GameObject> targets = new List<GameObject>(16);

        /// <summary>Already logged ones: hiding is REPEATED every frame but the log is printed once.</summary>
        private readonly HashSet<GameObject> logged = new HashSet<GameObject>();

        private float rescanTimer = float.NegativeInfinity;

        /// <summary>The "player's hand not found" error, once per session.</summary>
        private static bool erroredNoDrivenHandVisual;

        /// <summary>The "ray's visual node not found" warning, once per session.</summary>
        private static bool warnedNoRayVisuals;

        private void LateUpdate()
        {
            if (rigRoot == null)
            {
                GameObject go = string.IsNullOrEmpty(rigRootName) ? null : GameObject.Find(rigRootName);
                if (go == null)
                {
                    // The name did not match: the rig prefab may have been renamed or may sit in the
                    // scene under a different name. Its identity is determined by its COMPONENT, not
                    // its NAME — search by type.
                    OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
                    go = rig != null ? rig.gameObject : null;
                }

                if (go == null)
                {
                    return; // the rig is not in the scene yet — retried next frame
                }

                rigRoot = go.transform;
                targets.Clear();
                rescanTimer = float.NegativeInfinity; // new rig: scan immediately
            }

            if (Time.unscaledTime - rescanTimer >= RescanIntervalSeconds)
            {
                rescanTimer = Time.unscaledTime;
                Rescan();
            }

            // ⚠️ Hiding is REPEATED every frame: Meta re-enables these visuals when a controller is put
            // down and picked up again. The "once hidden, never look again" shortcut is wrong for
            // exactly this reason — the visual would come back and silently stay visible.
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                GameObject target = targets[i];
                if (target == null)
                {
                    targets.RemoveAt(i);
                    continue;
                }

                if (!target.activeSelf)
                {
                    continue;
                }

                target.SetActive(false);

                if (logged.Add(target))
                {
                    string parentName = target.transform.parent != null ? target.transform.parent.name : "(kök)";
                    Debug.Log($"[ControllerModelHider] Gizlendi: '{target.name}' ({parentName} altında).", this);
                }
            }
        }

        /// <summary>Re-finds the visuals to hide under the rig (both types in a single pass).
        /// <para>The hand visuals the player sees are NEVER added to the list — neither their object
        /// nor their Renderer is touched.</para></summary>
        private void Rescan()
        {
            rigRoot.GetComponentsInChildren(true, scanBuffer);

            int handVisualsSeen = 0;
            int handVisualsDriven = 0;
            int raysSeen = 0;
            int rayVisualsFound = 0;

            for (int i = 0; i < scanBuffer.Count; i++)
            {
                MonoBehaviour mb = scanBuffer[i];
                if (mb == null)
                {
                    continue;
                }

                string typeName = mb.GetType().Name;
                bool isHandVisual = typeName == HandVisualTypeName;
                bool isRayInteractor = typeName == RayInteractorTypeName;
                if (!isHandVisual && !isRayInteractor && !(mb is OVRControllerHelper))
                {
                    continue;
                }

                GameObject target = mb.gameObject;

                if (isRayInteractor)
                {
                    // What gets disabled is NOT the interactor but the visual container under it.
                    raysSeen++;
                    Transform visuals = mb.transform.Find(RayVisualsNodeName);
                    if (visuals == null)
                    {
                        continue;
                    }

                    rayVisualsFound++;
                    target = visuals.gameObject;
                }
                else if (isHandVisual)
                {
                    handVisualsSeen++;
                    if (IsPlayerHand(target.name))
                    {
                        handVisualsDriven++;
                        continue; // the hand the player sees: never touched
                    }
                }

                if (!targets.Contains(target))
                {
                    targets.Add(target);
                }
            }

            if (handVisualsSeen > 0 && handVisualsDriven == 0)
            {
                ErrorNoDrivenHandVisual();
            }

            if (raysSeen > 0 && rayVisualsFound == 0)
            {
                WarnNoRayVisuals();
            }
        }

        /// <summary>Is this hand visual the hand the player sees — a <b>full name</b> match (rationale in
        /// the class documentation: the ghost hands' names differ only by word order).</summary>
        private bool IsPlayerHand(string objectName)
        {
            if (drivenHandVisuals == null)
            {
                return false;
            }

            for (int i = 0; i < drivenHandVisuals.Length; i++)
            {
                if (string.Equals(drivenHandVisuals[i], objectName, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// There are hand visuals in the rig but none matched the list → <b>they were all disabled and
        /// the player lost their hands entirely.</b>
        /// <para>⚠️ An ERROR, not a warning: these are the only hands drawn on the player's screen, so
        /// the symptom reads as "tracking is broken / the hands are gone" — whereas the only thing
        /// missing is a GameObject name that the Meta SDK changed.</para>
        /// </summary>
        private void ErrorNoDrivenHandVisual()
        {
            if (erroredNoDrivenHandVisual)
            {
                return;
            }

            erroredNoDrivenHandVisual = true;
            Debug.LogError(
                "[ControllerModelHider] Rig'de el görseli bulundu ama hiçbiri 'Driven Hand " +
                "Visuals' ile eşleşmedi — hepsi hayalet sayılıp kapatıldı, yani OYUNCU ELLERİNİ " +
                "GÖRMEYECEK. Meta SDK'sı objeleri yeniden adlandırmış olabilir: rig altındaki " +
                "gerçek adlara bakıp listeyi güncelle (beklenen: OVRHandVisualLeft / " +
                "OVRHandVisualRight). ⚠️ Listeye benzer adlı OVRLeftHandVisual/OVRRightHandVisual " +
                "yazma — onlar mesafeli kavrama hayaletidir ve gizli kalmalıdır.", this);
        }

        /// <summary>
        /// There are ray interactors in the rig but no visual container was found under any of them →
        /// <b>the ray keeps being drawn.</b>
        /// <para>⚠️ A WARNING, not an error: the game works, the player just sees a line coming out of
        /// their hand. There can be only one cause — ISDK renamed the node; the place to look is
        /// <see cref="RayVisualsNodeName"/>.</para>
        /// </summary>
        private void WarnNoRayVisuals()
        {
            if (warnedNoRayVisuals)
            {
                return;
            }

            warnedNoRayVisuals = true;
            Debug.LogWarning(
                $"[ControllerModelHider] Rig'de ışın interactor'ı bulundu ama hiçbirinin altında " +
                $"'{RayVisualsNodeName}' düğümü yok — işaret ışını GİZLENEMEDİ ve oyuncunun " +
                "elinden çıkan çizgi görünmeye devam edecek. ISDK düğümü yeniden adlandırmış " +
                "olabilir: interactor'ın altındaki gerçek görsel düğüm adına bakıp sabiti güncelle.",
                this);
        }
    }
}
