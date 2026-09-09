using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.App
{
    /// <summary>Fades the player's headset to black before a scene load and back in after it.</summary>
    /// <remarks>
    /// ⚠️ <b>Not a <see cref="VortexArena.Core.Player.ScreenFade"/> source:</b> that arbiter's drawer is
    /// the scene's <c>ArenaBoundary</c>, which does not exist in the shell lobby and dies exactly at the
    /// transition; a disabled boundary also freezes its quad at the last value.
    /// <para>⚠️ The <c>OutOfBoundsFade</c> quad is NOT shared either — two writers on one renderer
    /// overwrite each other per frame. This driver owns its own quad.</para>
    /// <para>⚠️ The quad belongs to the SCENE (rig prefab, under <c>CenterEyeAnchor</c>), not to this
    /// <c>DontDestroyOnLoad</c> root: it dies with its scene and the next one is found again.</para>
    /// </remarks>
    public class SceneTransitionFade : MonoBehaviour
    {
        /// <summary>Fade to black before the load (s) — it blocks, so it stays short.</summary>
        private const float FadeOutSeconds = 0.2f;

        /// <summary>Fade back in after activation (s).</summary>
        private const float FadeInSeconds = 0.35f;

        /// <summary>Quad under <c>CenterEyeAnchor</c>, installed by <c>HmdOverlayBuilder</c>.</summary>
        private const string QuadName = "SceneTransitionFade";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static SceneTransitionFade _instance;

        private MeshRenderer _quad;
        private MaterialPropertyBlock _propertyBlock;
        private float _alpha;
        private bool _fadingIn;

        /// <summary>Warned once per session — a missing layer would otherwise spam every scene.</summary>
        private bool _warnedMissingQuad;

        /// <summary>Installs the singleton. ⚠️ <b>Unconditional</b> — the "is it needed in this
        /// session" decision belongs to <see cref="AppSingletons"/> (rationale is there).</summary>
        internal static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[SceneTransitionFade]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SceneTransitionFade>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        private void OnEnable()
        {
            if (_instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded += HandleSceneLoaded;

            // The install happens after the first scene is up, so that scene gets its fade in here.
            BeginScene();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        /// <summary>Ramps to full black from the CURRENT alpha; returns immediately when there is no
        /// quad (admin, boot scene, uninstalled layer). Black is left in place — the next scene's
        /// fade in takes over.</summary>
        public static IEnumerator FadeOut()
        {
            if (_instance == null)
            {
                yield break;
            }

            yield return _instance.FadeOutRoutine();
        }

        private IEnumerator FadeOutRoutine()
        {
            if (_quad == null)
            {
                yield break;
            }

            _fadingIn = false; // a running fade in is taken over at its current alpha, no flicker

            while (_alpha < 1f)
            {
                _alpha = Mathf.Min(1f, _alpha + Time.unscaledDeltaTime / FadeOutSeconds);
                Apply();
                yield return null;
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BeginScene();
        }

        /// <summary>Binds the new scene's quad, snaps it to black and starts the fade in.</summary>
        private void BeginScene()
        {
            _quad = FindQuad();
            _fadingIn = false;
            _alpha = 0f;

            if (_quad == null)
            {
                return; // no fade, the transition carries on
            }

            _alpha = 1f;
            Apply();
            _fadingIn = true;
        }

        /// <summary>⚠️ The head is <c>OVRCameraRig.centerEyeAnchor</c>, never <c>Camera.main</c> — all
        /// three rig cameras are tagged MainCamera.</summary>
        private MeshRenderer FindQuad()
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null || rig.centerEyeAnchor == null)
            {
                return null; // rig-less scenes (Boot, admin) are normal — no warning
            }

            Transform quad = rig.centerEyeAnchor.Find(QuadName);
            if (quad == null)
            {
                if (!_warnedMissingQuad)
                {
                    _warnedMissingQuad = true;
                    Debug.LogWarning($"[SceneTransitionFade] Rig altında '{QuadName}' katmanı yok — " +
                                     "sahne geçişi kararmadan yapılacak. " +
                                     "'Tools > VortexArena > Arena > HMD Katmanlarını Kur' çalıştırılmalı.");
                }

                return null;
            }

            return quad.GetComponent<MeshRenderer>();
        }

        private void Update()
        {
            if (!_fadingIn)
            {
                return;
            }

            // unscaledDeltaTime: a presentation layer must not depend on timeScale.
            _alpha = Mathf.Max(0f, _alpha - Time.unscaledDeltaTime / FadeInSeconds);
            Apply();

            if (_alpha <= 0f)
            {
                _fadingIn = false;
            }
        }

        /// <summary>Same drawing contract as <c>ArenaBoundary.SetFade</c>: property block + renderer
        /// toggle, so no material instance is created.</summary>
        private void Apply()
        {
            if (_quad == null)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, new Color(0f, 0f, 0f, _alpha));
            _quad.SetPropertyBlock(_propertyBlock);
            _quad.enabled = _alpha > 0.001f;
        }
    }
}
