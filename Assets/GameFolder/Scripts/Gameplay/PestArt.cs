using System.Collections.Generic;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Canlıların prosedürel low-poly modelleri (arı, sinek, kuş, kızgın kuş, örümcek, yavru örümcek).
    /// PestController (gezen canlılar, sürüler) ve PestNest (yuvadaki sakinler) ortak kullanır.
    /// Meshler bir kez üretilip önbelleğe alınır.
    /// </summary>
    public static class PestArt
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        public static Transform Bee(Transform parent, float s, Material body, Material wing)
        {
            var bee = new GameObject("Bee").transform; bee.SetParent(parent, false);
            var key = "bee";
            if (!cache.ContainsKey(key))
            {
                var yellow = new Color(1f, 0.78f, 0.12f); var black = new Color(0.12f, 0.09f, 0.07f);
                var mb = new RMesh();
                mb.Ellipsoid(new Vector3(0, 0, -0.01f), new Vector3(0.042f, 0.04f, 0.06f), (c, n) => Mathf.Repeat(c.z * 40f + 10f, 2f) < 1f ? yellow : black);
                mb.Ellipsoid(new Vector3(0, 0.005f, 0.06f), new Vector3(0.03f, 0.03f, 0.028f), (c, n) => black);
                mb.Cone(new Vector3(0, 0, -0.07f), new Vector3(0, 0, -0.1f), 0.01f, black);
                mb.Ellipsoid(new Vector3(0.018f, 0.012f, 0.078f), Vector3.one * 0.008f, (c, n) => new Color(0.05f, 0.05f, 0.08f));
                mb.Ellipsoid(new Vector3(-0.018f, 0.012f, 0.078f), Vector3.one * 0.008f, (c, n) => new Color(0.05f, 0.05f, 0.08f));
                cache[key] = mb.Build("Bee");
            }
            AddMesh(bee, "Body", cache[key], body);
            Wing(bee, new Vector3(0.012f, 0.035f, 0.005f), 0.055f, 0.03f, wing);
            Wing(bee, new Vector3(-0.012f, 0.035f, 0.005f), 0.055f, 0.03f, wing);
            bee.localScale = Vector3.one * s;
            return bee;
        }

        public static Transform Fly(Transform parent, float s, Material body, Material wing)
        {
            var fly = new GameObject("Fly").transform; fly.SetParent(parent, false);
            if (!cache.ContainsKey("fly"))
            {
                var dark = new Color(0.16f, 0.17f, 0.2f); var shine = new Color(0.25f, 0.4f, 0.3f);
                var mb = new RMesh();
                mb.Ellipsoid(new Vector3(0, 0, -0.01f), new Vector3(0.03f, 0.028f, 0.05f), (c, n) => n.y > 0.4f ? shine : dark);
                mb.Ellipsoid(new Vector3(0, 0.004f, 0.045f), new Vector3(0.026f, 0.022f, 0.02f), (c, n) => dark);
                mb.Ellipsoid(new Vector3(0.016f, 0.01f, 0.055f), Vector3.one * 0.013f, (c, n) => new Color(0.75f, 0.12f, 0.1f));
                mb.Ellipsoid(new Vector3(-0.016f, 0.01f, 0.055f), Vector3.one * 0.013f, (c, n) => new Color(0.75f, 0.12f, 0.1f));
                cache["fly"] = mb.Build("Fly");
            }
            AddMesh(fly, "Body", cache["fly"], body);
            Wing(fly, new Vector3(0.01f, 0.027f, 0f), 0.05f, 0.022f, wing);
            Wing(fly, new Vector3(-0.01f, 0.027f, 0f), 0.05f, 0.022f, wing);
            fly.localScale = Vector3.one * 1.6f * s;
            return fly;
        }

        /// <summary>Kuş. angry: kırmızı göz, çatık kaş, kabarık tüyler (yuvası bozulan anne kuş).</summary>
        public static Transform Bird(Transform parent, float s, bool angry, Material body)
        {
            var bird = new GameObject(angry ? "AngryBird" : "Bird").transform; bird.SetParent(parent, false);
            string key = angry ? "birdA" : "bird";
            var brown = angry ? new Color(0.48f, 0.3f, 0.2f) : new Color(0.55f, 0.36f, 0.22f);
            var cream = new Color(0.95f, 0.88f, 0.75f);
            var dark = new Color(0.3f, 0.2f, 0.14f); var beak = new Color(1f, 0.62f, 0.15f);
            if (!cache.ContainsKey(key))
            {
                var mb = new RMesh();
                mb.Ellipsoid(Vector3.zero, new Vector3(0.07f, 0.065f, 0.1f), (c, n) => n.y < -0.2f || n.z > 0.6f ? cream : brown);
                mb.Ellipsoid(new Vector3(0, 0.06f, 0.08f), new Vector3(0.05f, 0.05f, 0.05f), (c, n) => n.y > 0.5f ? dark : brown);
                mb.Cone(new Vector3(0, 0.055f, 0.125f), new Vector3(0, 0.05f, 0.17f), 0.016f, beak);
                var eye = angry ? new Color(0.95f, 0.15f, 0.1f) : new Color(0.05f, 0.04f, 0.04f);
                mb.Ellipsoid(new Vector3(0.03f, 0.075f, 0.11f), Vector3.one * (angry ? 0.012f : 0.009f), (c, n) => eye);
                mb.Ellipsoid(new Vector3(-0.03f, 0.075f, 0.11f), Vector3.one * (angry ? 0.012f : 0.009f), (c, n) => eye);
                mb.DoubleTri(new Vector3(-0.03f, 0.01f, -0.08f), new Vector3(0.03f, 0.01f, -0.08f), new Vector3(0f, 0.04f, -0.18f), dark);
                mb.DoubleTri(new Vector3(0.02f, -0.05f, 0f), new Vector3(0.025f, -0.09f, 0.01f), new Vector3(0.03f, -0.05f, 0f), beak * 0.8f);
                mb.DoubleTri(new Vector3(-0.02f, -0.05f, 0f), new Vector3(-0.025f, -0.09f, 0.01f), new Vector3(-0.03f, -0.05f, 0f), beak * 0.8f);
                if (angry)
                {
                    // çatık kaşlar + kafada diken diken tüy
                    var brow = new Color(0.12f, 0.08f, 0.06f);
                    mb.DoubleTri(new Vector3(0.012f, 0.095f, 0.125f), new Vector3(0.05f, 0.085f, 0.11f), new Vector3(0.012f, 0.085f, 0.13f), brow);
                    mb.DoubleTri(new Vector3(-0.012f, 0.095f, 0.125f), new Vector3(-0.05f, 0.085f, 0.11f), new Vector3(-0.012f, 0.085f, 0.13f), brow);
                    for (int k = -1; k <= 1; k++)
                        mb.DoubleTri(new Vector3(k * 0.015f - 0.006f, 0.1f, 0.07f), new Vector3(k * 0.02f, 0.145f, 0.05f + k * 0.005f), new Vector3(k * 0.015f + 0.006f, 0.1f, 0.07f), dark);
                }
                cache[key] = mb.Build(key);
            }
            AddMesh(bird, "Body", cache[key], body);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                var w = new GameObject(sd < 0 ? "WingL" : "WingR").transform; w.SetParent(bird, false);
                w.localPosition = new Vector3(sd * 0.05f, 0.03f, 0.01f);
                string wk = (angry ? "wingA" : "wing") + sd;
                if (!cache.ContainsKey(wk))
                {
                    var wm = new RMesh();
                    wm.DoubleTri(Vector3.zero, new Vector3(sd * 0.14f, 0.01f, -0.03f), new Vector3(0f, 0f, -0.09f), dark);
                    wm.DoubleTri(Vector3.zero, new Vector3(sd * 0.1f, 0.01f, 0.03f), new Vector3(sd * 0.14f, 0.01f, -0.03f), brown);
                    cache[wk] = wm.Build(wk);
                }
                AddMesh(w, "Mesh", cache[wk], body);
            }
            bird.localScale = Vector3.one * 1.2f * s;
            return bird;
        }

        /// <summary>Örümcek. baby: soluk, küçük yavru (yumurta kesesinden döküşenler).</summary>
        public static Transform Spider(Transform parent, float s, bool baby, Material body)
        {
            var sp = new GameObject(baby ? "Spiderling" : "Spider").transform; sp.SetParent(parent, false);
            string key = baby ? "spiderB" : "spider";
            if (!cache.ContainsKey(key))
            {
                var dark = baby ? new Color(0.32f, 0.26f, 0.24f) : new Color(0.12f, 0.09f, 0.1f);
                var mark = new Color(0.85f, 0.3f, 0.15f);
                var mb = new RMesh();
                mb.Ellipsoid(new Vector3(0, 0, -0.03f), new Vector3(0.045f, 0.04f, 0.05f), (c, n) => !baby && n.y > 0.7f && Mathf.Abs(c.x) < 0.012f ? mark : dark);
                mb.Ellipsoid(new Vector3(0, 0, 0.03f), new Vector3(0.028f, 0.025f, 0.03f), (c, n) => dark);
                for (int side = -1; side <= 1; side += 2)
                    for (int l = 0; l < 4; l++)
                    {
                        float z = 0.045f - l * 0.022f;
                        var hip = new Vector3(side * 0.02f, 0f, z);
                        var knee = new Vector3(side * 0.07f, 0.035f, z + (l < 2 ? 0.02f : -0.02f));
                        var foot = new Vector3(side * 0.1f, -0.03f, z + (l < 2 ? 0.04f : -0.04f));
                        var w2 = new Vector3(0, 0.004f, 0);
                        mb.DoubleTri(hip - w2, knee, hip + w2, dark);
                        mb.DoubleTri(knee - w2, foot, knee + w2, dark);
                    }
                mb.Ellipsoid(new Vector3(0.01f, 0.012f, 0.058f), Vector3.one * 0.006f, (c, n) => new Color(0.9f, 0.9f, 0.9f));
                mb.Ellipsoid(new Vector3(-0.01f, 0.012f, 0.058f), Vector3.one * 0.006f, (c, n) => new Color(0.9f, 0.9f, 0.9f));
                cache[key] = mb.Build(key);
            }
            AddMesh(sp, "Body", cache[key], body);
            sp.localScale = Vector3.one * 1.5f * s;
            return sp;
        }

        static void Wing(Transform parent, Vector3 at, float len, float width, Material mat)
        {
            var w = new GameObject(at.x < 0 ? "WingL" : "WingR").transform;
            w.SetParent(parent, false);
            w.localPosition = at;
            float s = at.x < 0 ? -1f : 1f;
            string key = "iw" + s + "_" + len;
            if (!cache.ContainsKey(key))
            {
                var mb = new RMesh();
                var col = new Color(0.92f, 0.96f, 1f, 0.55f);
                mb.DoubleTri(Vector3.zero, new Vector3(s * len, 0.004f, -width * 0.3f), new Vector3(s * len * 0.6f, 0f, width), col);
                mb.DoubleTri(Vector3.zero, new Vector3(s * len * 0.7f, 0.003f, -width), new Vector3(s * len, 0.004f, -width * 0.3f), col);
                cache[key] = mb.Build(key);
            }
            AddMesh(w, "Mesh", cache[key], mat, false);
        }

        static void AddMesh(Transform parent, string name, Mesh mesh, Material mat, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Kanatları çırp (isimleri Wing ile başlayan alt nesneler).</summary>
        public static void Flap(Transform model, float t, float speed, float amp, float bias = 0f)
        {
            foreach (Transform c in model)
            {
                if (c.name.StartsWith("Wing"))
                {
                    float side = c.localPosition.x < 0 ? -1f : 1f;
                    c.localRotation = Quaternion.Euler(0f, 0f, side * (Mathf.Sin(t * speed) * amp + bias));
                }
                else if (c.childCount > 0 && !c.name.StartsWith("Body")) Flap(c, t, speed, amp, bias);
            }
        }

        /// <summary>Çalışma zamanında küçük low-poly meshler (düz gölgeli, köşe renkli).</summary>
        public class RMesh
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Color> c = new List<Color>();
            readonly List<int> t = new List<int>();

            public void Tri(Vector3 a, Vector3 b, Vector3 d, Color col)
            {
                var lin = col.linear; lin.a = col.a;
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(d);
                c.Add(lin); c.Add(lin); c.Add(lin);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public void DoubleTri(Vector3 a, Vector3 b, Vector3 d, Color col) { Tri(a, b, d, col); Tri(a, d, b, col); }

            static Vector3[] icoV; static int[] icoF;

            public void Ellipsoid(Vector3 center, Vector3 radius, System.Func<Vector3, Vector3, Color> shade)
            {
                if (icoV == null) MakeIco();
                for (int i = 0; i < icoF.Length; i += 3)
                {
                    Vector3 a = center + Vector3.Scale(icoV[icoF[i]], radius);
                    Vector3 b = center + Vector3.Scale(icoV[icoF[i + 1]], radius);
                    Vector3 d = center + Vector3.Scale(icoV[icoF[i + 2]], radius);
                    var mid = (a + b + d) / 3f;
                    var n = (mid - center).normalized;
                    if (Vector3.Dot(Vector3.Cross(b - a, d - a), mid - center) < 0f) { var tmp = b; b = d; d = tmp; }
                    var col = shade(mid - center, n);
                    var lit = col * (0.85f + 0.15f * Mathf.Clamp01(n.y + 0.5f)); lit.a = col.a;
                    Tri(a, b, d, lit);
                }
            }

            public void Cone(Vector3 baseC, Vector3 tip, float r, Color col)
            {
                var ax = (tip - baseC).normalized;
                var u = Vector3.Cross(ax, Mathf.Abs(ax.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var w = Vector3.Cross(ax, u);
                const int n = 6;
                for (int i = 0; i < n; i++)
                {
                    float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1f) / n * Mathf.PI * 2f;
                    var p0 = baseC + (u * Mathf.Cos(a0) + w * Mathf.Sin(a0)) * r;
                    var p1 = baseC + (u * Mathf.Cos(a1) + w * Mathf.Sin(a1)) * r;
                    var mid = (p0 + p1 + tip) / 3f;
                    if (Vector3.Dot(Vector3.Cross(p1 - p0, tip - p0), mid - baseC) < 0f) Tri(p0, tip, p1, col); else Tri(p0, p1, tip, col);
                }
            }

            static void MakeIco()
            {
                float g = (1f + Mathf.Sqrt(5f)) / 2f;
                var vs = new List<Vector3>
                {
                    new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                    new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                    new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
                };
                int[] f0 =
                {
                    0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                    3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
                };
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int i)) return i;
                    vs.Add((vs[a] + vs[b]) * 0.5f); mid[key] = vs.Count - 1; return vs.Count - 1;
                }
                var nf = new List<int>();
                for (int i = 0; i < f0.Length; i += 3)
                {
                    int a = f0[i], b = f0[i + 1], cc = f0[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, cc), ca = Mid(cc, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, cc, ca, bc, ab, bc, ca });
                }
                for (int i = 0; i < vs.Count; i++) vs[i] = vs[i].normalized;
                icoV = vs.ToArray(); icoF = nf.ToArray();
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
                m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }
    }
}
