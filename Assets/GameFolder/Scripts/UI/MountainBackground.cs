using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace FumblePeak
{
    /// <summary>
    /// Ana menü arka planı (gündüz, pastel): gökyüzü gradyanı, güneş, süzülen bulutlar,
    /// karlı tepeli paralaks dağ katmanları ve uçuşan ışıltılar. Tamamen UI Toolkit Painter2D.
    /// </summary>
    public class MountainBackground : VisualElement
    {
        static readonly Color SkyTop = new Color32(0x7A, 0xC8, 0xFF, 0xFF);
        static readonly Color SkyBottom = new Color32(0xFF, 0xE3, 0xC8, 0xFF);
        static readonly Color[] Layers =
        {
            new Color32(0xB9, 0xB4, 0xF2, 0xFF),   // uzak lila
            new Color32(0x8E, 0xB9, 0xEE, 0xFF),   // gök mavisi
            new Color32(0x7E, 0xD2, 0xAA, 0xFF),   // nane
            new Color32(0x5C, 0xB8, 0x86, 0xFF),   // çimen
        };
        static readonly bool[] Snowy = { true, true, false, false };

        struct Cloud { public float x, y, s, speed; }
        readonly List<Cloud> clouds = new List<Cloud>();
        readonly Vector4[] sparkles = new Vector4[40]; // x, y, faz, boyut
        float time;
        public float Parallax;

        public MountainBackground()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0; style.right = 0; style.top = 0; style.bottom = 0;
            for (int i = 0; i < 6; i++)
                clouds.Add(new Cloud { x = Random.value, y = Random.Range(0.06f, 0.36f), s = Random.Range(0.7f, 1.3f), speed = Random.Range(0.004f, 0.012f) });
            for (int i = 0; i < sparkles.Length; i++)
                sparkles[i] = new Vector4(Random.value, Random.value, Random.value * 10f, Random.Range(2f, 5f));
            generateVisualContent += Draw;
            schedule.Execute(Tick).Every(16);
        }

        void Tick(TimerState t)
        {
            float dt = Mathf.Min(0.05f, t.deltaTime / 1000f);
            time += dt;
            for (int i = 0; i < clouds.Count; i++)
            {
                var c = clouds[i];
                c.x += c.speed * dt;
                if (c.x > 1.2f) c.x = -0.2f;
                clouds[i] = c;
            }
            for (int i = 0; i < sparkles.Length; i++)
            {
                var s = sparkles[i];
                s.y -= dt * 0.012f * (0.5f + s.w * 0.1f);
                s.x += dt * 0.01f * Mathf.Sin(time * 0.6f + i);
                if (s.y < -0.02f) { s.y = 1.02f; s.x = Random.value; }
                sparkles[i] = s;
            }
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float w = r.width, h = r.height;
            if (w < 1 || h < 1) return;

            // Gökyüzü
            var mesh = ctx.Allocate(4, 6);
            mesh.SetNextVertex(new Vertex { position = new Vector3(0, 0, Vertex.nearZ), tint = SkyTop });
            mesh.SetNextVertex(new Vertex { position = new Vector3(w, 0, Vertex.nearZ), tint = SkyTop });
            mesh.SetNextVertex(new Vertex { position = new Vector3(w, h, Vertex.nearZ), tint = SkyBottom });
            mesh.SetNextVertex(new Vertex { position = new Vector3(0, h, Vertex.nearZ), tint = SkyBottom });
            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
            mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);

            var p = ctx.painter2D;

            // Güneş (yumuşak hale + çekirdek)
            var sun = new Vector2(w * 0.8f, h * 0.2f);
            float pulse = 1f + Mathf.Sin(time * 1.2f) * 0.03f;
            p.fillColor = new Color(1f, 0.93f, 0.6f, 0.25f); Circle(p, sun, h * 0.11f * pulse);
            p.fillColor = new Color(1f, 0.9f, 0.5f, 0.35f); Circle(p, sun, h * 0.085f * pulse);
            p.fillColor = new Color32(0xFF, 0xD8, 0x6B, 0xFF); Circle(p, sun, h * 0.065f);

            // Bulutlar (arka)
            foreach (var c in clouds) if (c.s < 1f) DrawCloud(p, c, w, h);

            // Dağ katmanları
            for (int l = 0; l < Layers.Length; l++)
            {
                float baseY = h * (0.5f + l * 0.12f);
                float amp = h * (0.26f - l * 0.045f);
                float freq = 1.4f + l * 0.8f;
                float drift = time * (0.002f + l * 0.004f) + Parallax * (0.01f + l * 0.02f);
                const int steps = 80;
                var xs = new float[steps + 1]; var ys = new float[steps + 1];
                float minY = float.MaxValue;
                for (int s = 0; s <= steps; s++)
                {
                    float x = s / (float)steps;
                    float n = Mathf.PerlinNoise((x + drift) * freq + l * 13.1f, l * 3.7f);
                    float peak = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin((x + drift) * Mathf.PI * (freq * 0.7f) + l)), 1.4f); // sivri tepeler
                    xs[s] = x * w;
                    ys[s] = baseY - amp * (0.4f * n + 0.6f * peak);
                    minY = Mathf.Min(minY, ys[s]);
                }
                p.fillColor = Layers[l];
                p.BeginPath();
                p.MoveTo(new Vector2(0, h));
                for (int s = 0; s <= steps; s++) p.LineTo(new Vector2(xs[s], ys[s]));
                p.LineTo(new Vector2(w, h));
                p.ClosePath();
                p.Fill();

                // Karlı tepeler
                if (Snowy[l])
                {
                    float capLine = minY + amp * 0.16f;
                    p.fillColor = Color.white;
                    int s0 = -1;
                    for (int s = 0; s <= steps + 1; s++)
                    {
                        bool above = s <= steps && ys[s] < capLine;
                        if (above && s0 < 0) s0 = s;
                        if (!above && s0 >= 0)
                        {
                            int s1 = s - 1;
                            if (s1 - s0 >= 1)
                            {
                                p.BeginPath();
                                p.MoveTo(new Vector2(xs[s0], capLine));
                                for (int k = s0; k <= s1; k++) p.LineTo(new Vector2(xs[k], ys[k]));
                                p.LineTo(new Vector2(xs[s1], capLine));
                                // kar kenarı: küçük zikzak
                                int teeth = Mathf.Max(2, (s1 - s0) / 2);
                                for (int k = teeth; k >= 0; k--)
                                {
                                    float tx = Mathf.Lerp(xs[s0], xs[s1], k / (float)teeth);
                                    p.LineTo(new Vector2(tx, capLine + (k % 2 == 0 ? 0f : h * 0.012f)));
                                }
                                p.ClosePath();
                                p.Fill();
                            }
                            s0 = -1;
                        }
                    }
                }
            }

            // Bulutlar (ön)
            foreach (var c in clouds) if (c.s >= 1f) DrawCloud(p, c, w, h);

            // Işıltılar
            foreach (var s in sparkles)
            {
                float a = 0.35f + 0.35f * Mathf.Sin(time * 2f + s.z);
                p.fillColor = new Color(1f, 1f, 1f, a);
                var c = new Vector2(s.x * w, s.y * h);
                float size = s.w;
                p.BeginPath();
                p.MoveTo(c + new Vector2(-size, 0));
                p.LineTo(c + new Vector2(0, -size * 1.6f));
                p.LineTo(c + new Vector2(size, 0));
                p.LineTo(c + new Vector2(0, size * 1.6f));
                p.ClosePath();
                p.Fill();
            }
        }

        static void DrawCloud(Painter2D p, Cloud c, float w, float h)
        {
            var o = new Vector2(c.x * w, c.y * h);
            float u = h * 0.05f * c.s;
            p.fillColor = new Color(1f, 1f, 1f, 0.92f);
            Circle(p, o + new Vector2(-u * 1.3f, u * 0.25f), u * 0.9f);
            Circle(p, o + new Vector2(0, 0), u * 1.25f);
            Circle(p, o + new Vector2(u * 1.3f, u * 0.3f), u * 0.95f);
            Circle(p, o + new Vector2(u * 0.4f, -u * 0.55f), u * 0.9f);
            p.BeginPath();
            p.MoveTo(o + new Vector2(-u * 2.1f, u * 0.6f));
            p.LineTo(o + new Vector2(u * 2.2f, u * 0.6f));
            p.LineTo(o + new Vector2(u * 2.2f, u * 1.0f));
            p.LineTo(o + new Vector2(-u * 2.1f, u * 1.0f));
            p.ClosePath();
            p.Fill();
        }

        static void Circle(Painter2D p, Vector2 c, float rad)
        {
            p.BeginPath();
            const int seg = 28;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var pt = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            p.Fill();
        }
    }
}
