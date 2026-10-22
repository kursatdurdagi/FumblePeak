// This gate must compile inside EnemyMasses asmdef without referencing Crystal Save
// (Crystal Save does not use asmdefs). It uses reflection to hook the remember events.
using UnityEngine;
using System;
using System.Reflection;
using UnityEngine.UI;
using Arawn.EnemyMasses.Runtime;

namespace Arawn.EnemyMasses.Runtime.UI
{
    /// <summary>
    /// Disables RTS input/UI while RTS command restoration is in progress.
    /// </summary>
    [AddComponentMenu("Enemy Masses/UI/RTS Restore Load Gate")]
    public class RTSRestoreLoadGate : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Optional: drag the RememberEnemyMassesRTSCommands component here. If null, this gate will try to find it by type name via reflection.")]
        public MonoBehaviour remember;

        [Tooltip("RTS controller to disable during restore. If null, will try to GetComponent on this GameObject.")]
        public EnemyMassesRTSController rtsController;

        [Tooltip("Optional command panel to hide/disable during restore.")]
        public RTSSelectionCommandPanel commandPanel;

        [Tooltip("Optional overlay to show while restoring (e.g., Loading UI).")]
        public GameObject loadingOverlay;

        [Header("Loading Bar")]
        [Tooltip("Optional Slider (0..1) used as a loading bar during restore.")]
        public Slider loadingBar;

        [Tooltip("If true and RestoreProgress is unavailable/stalled, animate the bar so it doesn't look frozen.")]
        public bool animateWhenProgressUnknown = true;

        [Tooltip("When progress is unknown/stalled, slowly fill up to this value (never decreases).")]
        [Range(0f, 1f)] public float indeterminateMaxValue = 0.95f;

        [Tooltip("Unscaled fill speed (0..1 per second) used for the indeterminate creep.")]
        public float indeterminateFillSpeed = 0.12f;

        [Header("Debug")]
        [Tooltip("Logs why the gate did/didn't lock during restore.")]
        public bool debugLogging = false;

        [Header("Behavior")]
        [Tooltip("Disable the RTS controller component during restore to block input.")]
        public bool disableController = true;

        [Tooltip("Hide the panel root during restore to prevent clicks.")]
        public bool hidePanel = true;

        private bool _locked;

        private const string RememberTypeName = "Arawn.CrystalSave.Runtime.RememberEnemyMassesRTSCommands";

        private EventInfo _restoreStartedEvent;
        private EventInfo _restoreCompletedEvent;
        private PropertyInfo _isRestoringProperty;
        private PropertyInfo _restoreProgressProperty;
        private Action _onStarted;
        private Action _onCompleted;

        private float _lastProgress;
        private float _lastProgressChangeTime;
        private float _displayedProgress;

        private void Awake()
        {
            if (remember == null)
            {
                remember = FindRememberInScene();
            }
            if (rtsController == null)
            {
                rtsController = GetComponent<EnemyMassesRTSController>();
            }
            if (commandPanel == null)
            {
                commandPanel = GetComponent<RTSSelectionCommandPanel>();
            }
        }

        private void OnEnable()
        {
            TryHookRemember();
        }

        private void OnDisable()
        {
            TryUnhookRemember();
        }

        private void HandleRestoreStarted()
        {
            Lock();
        }

        private void HandleRestoreCompleted()
        {
            Unlock();
        }

        private void Update()
        {
            if (!_locked || loadingBar == null)
                return;

            float progress = ReadRestoreProgress();
            if (progress >= 0f)
            {
                // Track whether progress is changing
                if (Mathf.Abs(progress - _lastProgress) > 0.0001f)
                {
                    _lastProgress = progress;
                    _lastProgressChangeTime = Time.unscaledTime;
                }

                // Never move backwards visually.
                _displayedProgress = Mathf.Max(_displayedProgress, Mathf.Clamp01(progress));
            }

            bool stalled = Time.unscaledTime - _lastProgressChangeTime > 1.5f;
            if (animateWhenProgressUnknown && stalled && _displayedProgress < Mathf.Min(0.999f, indeterminateMaxValue))
            {
                _displayedProgress = Mathf.Min(indeterminateMaxValue,
                    _displayedProgress + Mathf.Max(0f, indeterminateFillSpeed) * Time.unscaledDeltaTime);
            }

            loadingBar.value = Mathf.Clamp01(_displayedProgress);
        }

        private MonoBehaviour FindRememberInScene()
        {
            // No compile-time type reference: scan behaviours and match by full name.
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b == null) continue;
                var t = b.GetType();
                if (t != null && t.FullName == RememberTypeName)
                    return b;
            }
            return null;
        }

        private void TryHookRemember()
        {
            // If user dragged the wrong component (common), auto-find the correct remember component.
            if (remember == null || remember.GetType().FullName != RememberTypeName)
            {
                var found = FindRememberInScene();
                if (found != null)
                {
                    remember = found;
                    if (debugLogging)
                        Debug.Log($"[RTSRestoreLoadGate] Auto-assigned remember component: {RememberTypeName}", this);
                }
            }

            if (remember == null)
            {
                if (debugLogging)
                    Debug.LogWarning($"[RTSRestoreLoadGate] No remember component found ({RememberTypeName}). Gate will stay unlocked.", this);
                Unlock();
                return;
            }

            try
            {
                var t = remember.GetType();
                if (debugLogging && t.FullName != RememberTypeName)
                    Debug.LogWarning($"[RTSRestoreLoadGate] Remember component type is '{t.FullName}', expected '{RememberTypeName}'. Trying to hook anyway.", this);

                _restoreStartedEvent = t.GetEvent("RestoreStarted", BindingFlags.Instance | BindingFlags.Public);
                _restoreCompletedEvent = t.GetEvent("RestoreCompleted", BindingFlags.Instance | BindingFlags.Public);
                _isRestoringProperty = t.GetProperty("IsRestoring", BindingFlags.Instance | BindingFlags.Public);
                _restoreProgressProperty = t.GetProperty("RestoreProgress", BindingFlags.Instance | BindingFlags.Public);

                _onStarted = HandleRestoreStarted;
                _onCompleted = HandleRestoreCompleted;

                if (_restoreStartedEvent != null)
                    _restoreStartedEvent.AddEventHandler(remember, _onStarted);
                if (_restoreCompletedEvent != null)
                    _restoreCompletedEvent.AddEventHandler(remember, _onCompleted);

                if (debugLogging)
                {
                    Debug.Log($"[RTSRestoreLoadGate] Hooked remember. Has RestoreStarted={_restoreStartedEvent != null}, RestoreCompleted={_restoreCompletedEvent != null}, IsRestoring={_isRestoringProperty != null}, RestoreProgress={_restoreProgressProperty != null}", this);
                }

                bool isRestoring = false;
                if (_isRestoringProperty != null && _isRestoringProperty.PropertyType == typeof(bool))
                {
                    isRestoring = (bool)_isRestoringProperty.GetValue(remember);
                }

                _lastProgress = 0f;
                _lastProgressChangeTime = Time.unscaledTime;
                _displayedProgress = 0f;

                if (isRestoring)
                    Lock();
                else
                    Unlock();
            }
            catch
            {
                // If reflection fails, don't block the game.
                if (debugLogging)
                    Debug.LogWarning("[RTSRestoreLoadGate] Reflection hook failed; gate will stay unlocked.", this);
                Unlock();
            }
        }

        private void TryUnhookRemember()
        {
            if (remember == null)
                return;

            try
            {
                if (_restoreStartedEvent != null && _onStarted != null)
                    _restoreStartedEvent.RemoveEventHandler(remember, _onStarted);
                if (_restoreCompletedEvent != null && _onCompleted != null)
                    _restoreCompletedEvent.RemoveEventHandler(remember, _onCompleted);
            }
            catch { }
            finally
            {
                _restoreStartedEvent = null;
                _restoreCompletedEvent = null;
                _isRestoringProperty = null;
                _restoreProgressProperty = null;
                _onStarted = null;
                _onCompleted = null;
            }
        }

        private float ReadRestoreProgress()
        {
            try
            {
                if (remember != null && _restoreProgressProperty != null && _restoreProgressProperty.PropertyType == typeof(float))
                {
                    return (float)_restoreProgressProperty.GetValue(remember);
                }
            }
            catch { }
            return -1f;
        }

        private void Lock()
        {
            if (_locked)
                return;
            _locked = true;

            if (loadingBar != null)
            {
                loadingBar.minValue = 0f;
                loadingBar.maxValue = 1f;
                loadingBar.value = 0f;
                _lastProgress = 0f;
                _lastProgressChangeTime = Time.unscaledTime;
                _displayedProgress = 0f;
            }

            if (disableController && rtsController != null)
            {
                rtsController.enabled = false;
            }

            if (hidePanel && commandPanel != null)
            {
                if (commandPanel.panelRoot != null)
                {
                    commandPanel.panelRoot.SetActive(false);
                }
            }

            if (loadingOverlay != null)
            {
                loadingOverlay.SetActive(true);
            }
        }

        private void Unlock()
        {
            if (!_locked)
                return;
            _locked = false;

            if (loadingBar != null)
            {
                loadingBar.value = 1f;
            }

            if (disableController && rtsController != null)
            {
                rtsController.enabled = true;
            }

            if (hidePanel && commandPanel != null)
            {
                if (commandPanel.panelRoot != null)
                {
                    commandPanel.panelRoot.SetActive(true);
                }
            }

            if (loadingOverlay != null)
            {
                loadingOverlay.SetActive(false);
            }
        }
    }
}
