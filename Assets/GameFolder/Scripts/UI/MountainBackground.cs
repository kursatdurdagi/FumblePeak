using UnityEngine;
using UnityEngine.UIElements;

namespace FumblePeak
{
    /// <summary>
    /// Ana menü arka planı: gökyüzü gradyanı, paralaks dağ katmanları ve yağan kar.
    /// Tamamen UI Toolkit (generateVisualContent + Painter2D) ile çizilir.
    /// </summary>
    public class MountainBackground : VisualElement
    {
        static readonly Color SkyTop = new Color(0.07f, 0.09f, 0.18f);
        static readonly Color SkyBottom = new Color(0.36f, 0.27f, 0.42f);
        static readonly Color[] Layers =
        {
            new Color(0.27f, 0.25f, 0.42f),
            new Color(0.18f, 0.18f, 0.32f),
            new Color(0.11f, 0.12f, 0.22f),
            new Color(0.06f, 0.07f, 0.13f),
        };

        readonly Vector3[] snow = new Vector3[90];
        float time;
        public float Parallax; // fare ile hafif kaydırma

        public MountainBackground()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0; style.right = 0; style.top = 0; style.bottom = 0;
            for (int i = 0; i < snow.Length; i++)
                snow[i] = new Vector3(Random.value, Random.value, Random.Range(0.4f, 1f));
            generateVisualContent += Draw;
            schedule.Execute(Tick).Every(16);
        }

        void Tick(TimerState t)
        {
            float dt = Mathf.Min(0.05f, t.deltaTime / 1000f);
            time += dt;
            for (int i = 0; i < snow.Length; i++)
            {
                var s = snow[i];
                s.y += dt * 0.05f * s.z;
                s.x += dt * 0.012f * Mathf.Sin(time * 0.7f + i);
                if (s.y > 1.02f) { s.y = -0.02f; s.x = Random.value; }
                snow[i] = s;
            }
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float w = r.width, h = r.height;
            if (w < 1 || h < 1) return;

            // Gökyüzü gradyanı
            var mesh = ctx.Allocate(4, 6);
            mesh.SetNextVertex(new Vertex { position = new Vector3(0, 0, Vertex.nearZ), tint = SkyTop });
            mesh.SetNextVertex(new Vertex { position = new Vector3(w, 0, Vertex.nearZ), tint = SkyTop });
            mesh.SetNextVertex(new Vertex { position = new Vector3(w, h, Vertex.nearZ), tint = SkyBottom });
            mesh.SetNextVertex(new Vertex { position = new Vector3(0, h, Vertex.nearZ), tint = SkyBottom });
            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
            mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);

            var p = ctx.painter2D;

            // Ay
            p.fillColor = new Color(1f, 0.93f, 0.8f, 0.9f);
            Circle(p, new Vector2(w * 0.78f, h * 0.2f), h * 0.06f);

            // Dağ katmanları
            for (int l = 0; l < Layers.Length; l++)
            {
                float baseY = h * (0.42f + l * 0.13f);
                float amp = h * (0.22f - l * 0.03f);
                float freq = 1.6f + l * 0.9f;
                float drift = time * (0.004f + l * 0.006f) + Parallax * (0.01f + l * 0.02f);
                p.fillColor = Layers[l];
                p.BeginPath();
                p.MoveTo(new Vector2(0, h));
                const int steps = 64;
                for (int s = 0; s <= steps; s++)
                {
                    float x = s / (float)steps;
                    float n = Mathf.PerlinNoise((x + drift) * freq + l * 13.1f, l * 3.7f);
                    float peak = Mathf.Abs(Mathf.Sin((x + drift) * Mathf.PI * (freq * 0.8f) + l));
                    float y = baseY - amp * (0.55f * n + 0.45f * peak);
                    p.LineTo(new Vector2(x * w, y));
                }
                p.LineTo(new Vector2(w, h));
                p.ClosePath();
                p.Fill();
            }

            // Kar
            p.fillColor = new Color(1f, 1f, 1f, 0.7f);
            foreach (var s in snow)
            {
                float size = 1.5f + s.z * 2.5f;
                var c = new Vector2(s.x * w, s.y * h);
                p.BeginPath();
                p.MoveTo(c + new Vector2(-size, 0));
                p.LineTo(c + new Vector2(0, -size));
                p.LineTo(c + new Vector2(size, 0));
                p.LineTo(c + new Vector2(0, size));
                p.ClosePath();
                p.Fill();
            }
        }

        static void Circle(Painter2D p, Vector2 c, float rad)
        {
            p.BeginPath();
            const int seg = 32;
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
