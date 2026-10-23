using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FumblePeak
{
    /// <summary>UI Toolkit ile sahne geçiş perdesi. Sahne yüklenince kendiliğinden açılır.</summary>
    public class SceneFader : MonoBehaviour
    {
        static SceneFader instance;
        VisualElement overlay;
        Label label;
        Coroutine running;
        float alpha;

        public static SceneFader Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("[SceneFader]");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = Resources.Load<PanelSettings>("UI/FumblePanelSettings");
            doc.sortingOrder = 1000;
            instance = go.AddComponent<SceneFader>();
            go.SetActive(true);
            instance.Build(doc);
            return instance;
        }

        void Build(UIDocument doc)
        {
            var root = doc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            overlay = new VisualElement { name = "fader" };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0; overlay.style.right = 0; overlay.style.top = 0; overlay.style.bottom = 0;
            overlay.style.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 1f);
            overlay.style.justifyContent = Justify.Center;
            overlay.style.alignItems = Align.Center;
            label = new Label();
            label.style.color = new Color(1f, 1f, 1f, 0.85f);
            label.style.fontSize = 34;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.letterSpacing = 4;
            overlay.Add(label);
            root.Add(overlay);
            SetAlpha(0f);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        bool fadingOut;

        void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (alpha > 0.01f || fadingOut) Run(AfterLoad());
        }

        IEnumerator AfterLoad()
        {
            if (alpha < 0.99f) yield return Fade(alpha, 1f, 0.15f, null);
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Fade(1f, 0f, 0.7f, null);
        }

        void SetAlpha(float a)
        {
            alpha = a;
            overlay.style.opacity = a;
            overlay.pickingMode = a > 0.01f ? PickingMode.Position : PickingMode.Ignore;
            overlay.style.display = a > 0.001f ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void FadeOut(float duration, Action done = null, string text = "")
        {
            label.text = text ?? "";
            Run(Fade(alpha, 1f, duration, done));
        }

        public void FadeIn(float duration, Action done = null) => Run(Fade(alpha, 0f, duration, done));

        /// <summary>Kısa karartma (düşüş / checkpoint dönüşü).</summary>
        public void Flash(string text, float hold = 0.35f)
        {
            label.text = text ?? "";
            Run(FlashRoutine(hold));
        }

        void Run(IEnumerator r)
        {
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(r);
        }

        IEnumerator FlashRoutine(float hold)
        {
            yield return Fade(alpha, 1f, 0.25f, null);
            yield return new WaitForSecondsRealtime(hold);
            yield return Fade(1f, 0f, 0.5f, null);
        }

        IEnumerator Fade(float from, float to, float duration, Action done)
        {
            fadingOut = to > from;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.001f, duration)));
                SetAlpha(Mathf.Lerp(from, to, k));
                yield return null;
            }
            SetAlpha(to);
            fadingOut = false;
            done?.Invoke();
        }
    }
}
