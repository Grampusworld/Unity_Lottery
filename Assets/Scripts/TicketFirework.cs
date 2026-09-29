using UnityEngine;

// 手动刮开彩票时的像素烟花：在票的位置炸开双层金白烟火，纯运行时 Texture2D，
// 不引粒子系统（与 ScratcherEffect / WasherWaterEffect 同一套做法）。
//
// 设计约束（2026-09-29 定稿）：
//   · 只在**手动**刮开时触发（LotteryGame.CompleteTicket）；机器结算已有爆点 + 机身脉冲，
//     再叠烟花会遮机身和计数牌，多票连结时也太闹。
//   · 总寿命 ~1.05s，比票的停留时间（disappearDelay = 1.5s）短一截 —— 特效先散完，
//     票再消失，不会出现「烟花跟着票一起蒸发」的突兀感。
//   · 降级档（Reduce Motion）直接不炸：烟花是纯装饰，静默跳过比放慢更符合该档的意图。
public class TicketFirework : MonoBehaviour
{
    private const int TextureSize = 128;      // 方形纹理，1 边 = 64 世界单位（PPU 2）
    private const float PixelsPerUnit = 2f;
    private const float Lifetime = 1.05f;

    // 配色沿用全场景三色：金（满级金）、米白（文字色）、少量红点缀。
    private static readonly Color32 Gold = new Color32(230, 180, 70, 255);
    private static readonly Color32 Cream = new Color32(246, 235, 205, 255);
    private static readonly Color32 Red = new Color32(210, 70, 60, 255);

    private struct Particle
    {
        public Vector2 pos;      // 纹理像素坐标
        public Vector2 vel;      // 像素/秒
        public float life;       // 剩余寿命（秒）
        public float maxLife;
        public Color32 color;
    }

    private Texture2D texture;
    private Color32[] buffer;
    private Particle[] particles;
    private float age;

    // 在世界坐标 pos 处炸一朵。粒子速度按世界单位给定，写入纹理时乘 PPU。
    public static void Play(Vector3 worldPosition)
    {
        if (HoverJellySettings.ReducedMotion) return;   // 降级档：纯装饰直接跳过
        Camera cam = Camera.main;
        if (cam == null) return;

        GameObject go = new GameObject("TicketFirework");
        go.transform.position = new Vector3(worldPosition.x, worldPosition.y, worldPosition.z - 0.5f);
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 10;                     // 盖过票（涂层 2）与机身（4），不被拖拽层(12+)压
        TicketFirework firework = go.AddComponent<TicketFirework>();
        firework.Initialize(renderer);
    }

    private void Initialize(SpriteRenderer renderer)
    {
        texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        buffer = new Color32[TextureSize * TextureSize];
        var clear = new Color32(0, 0, 0, 0);
        for (int i = 0; i < buffer.Length; i++) buffer[i] = clear;
        texture.SetPixels32(buffer);
        texture.Apply(false, false);

        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize),
            new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
        renderer.sprite = sprite;

        particles = BuildBurst();
    }

    // 双层放射：外圈金（快、散得远），中圈米白（慢、更密），再撒一小撮红。
    // 粒子带重力下坠 + 线性阻尼，寿命末端先闪烁再熄灭。
    private Particle[] BuildBurst()
    {
        const int outerCount = 46;
        const int innerCount = 34;
        const int redCount = 10;
        var list = new Particle[outerCount + innerCount + redCount];
        int n = 0;
        float cx = TextureSize * 0.5f;
        float cy = TextureSize * 0.55f;   // 爆心略偏上，给下坠留空间

        void Ring(int count, float minSpeed, float maxSpeed, Color32 color)
        {
            for (int i = 0; i < count; i++)
            {
                // 均匀方向 + 少量抖动，避免出现明显的「轮辐」。
                float angle = (i + Random.value * 0.7f) / count * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float speed = Random.Range(minSpeed, maxSpeed) * PixelsPerUnit;
                float life = Random.Range(0.55f, Lifetime);
                list[n++] = new Particle
                {
                    pos = new Vector2(cx, cy),
                    vel = dir * speed,
                    life = life,
                    maxLife = life,
                    color = color
                };
            }
        }

        Ring(outerCount, 14f, 22f, Gold);
        Ring(innerCount, 7f, 13f, Cream);
        Ring(redCount, 10f, 17f, Red);
        return list;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        age += dt;

        var clear = new Color32(0, 0, 0, 0);
        for (int i = 0; i < buffer.Length; i++) buffer[i] = clear;

        const float gravity = 26f * PixelsPerUnit;    // 像素/秒²，视觉上下坠但不拖沓
        const float drag = 1.6f;                      // 线性阻尼 / 秒
        bool alive = false;

        for (int i = 0; i < particles.Length; i++)
        {
            Particle p = particles[i];
            if (p.life <= 0f) continue;
            p.life -= dt;
            if (p.life <= 0f) { particles[i] = p; continue; }
            alive = true;

            p.vel.y -= gravity * dt;
            p.vel *= Mathf.Max(0f, 1f - drag * dt);
            p.pos += p.vel * dt;

            // 末端 25% 生命里按 8Hz 闪烁后熄灭，比线性淡出更有「火星」感。
            float k = p.life / p.maxLife;
            if (k < 0.25f && (int)(age * 8f) % 2 == 0) { particles[i] = p; continue; }

            int x = Mathf.RoundToInt(p.pos.x);
            int y = Mathf.RoundToInt(p.pos.y);
            if (x < 0 || x >= TextureSize || y < 0 || y >= TextureSize) { particles[i] = p; continue; }
            buffer[y * TextureSize + x] = p.color;
            // 右下一枚暗色伴随像素，2px 的「粗颗粒」观感。
            if (x + 1 < TextureSize && y - 1 >= 0)
            {
                var dim = new Color32((byte)(p.color.r * 3 / 4), (byte)(p.color.g * 3 / 4),
                    (byte)(p.color.b * 3 / 4), p.color.a);
                buffer[(y - 1) * TextureSize + (x + 1)] = dim;
            }
            particles[i] = p;
        }

        texture.SetPixels32(buffer);
        texture.Apply(false, false);

        if (!alive || age > Lifetime + 0.2f)
        {
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }
    }
}
