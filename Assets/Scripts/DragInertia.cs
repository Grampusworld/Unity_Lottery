using UnityEngine;

// 松手后的滑行惯性 + 撞边界反弹。手写速度积分器，**不挂 Rigidbody2D**：
//   · 机身自己的 Update 在写入场下落位置、HoverJelly 在写 localScale，
//     再加一个物理体就是三个源同帧抢同一个 transform；
//   · 物理在 FixedUpdate 步进，和这里的逐帧位置写入对不齐；
//   · 项目里所有弹性效果（HoverJelly 弹簧）本来就是手写积分，不引第二套运动模型。
//
// 模型：恒减速 v(t) = v0 - a·t（干燥桌面的摩擦感），到 0 真停。
//   滑行距离 = v0² / (2a)。默认 maxSpeed 90 u/s、deceleration 320 u/s²
//   → 满速甩一把滑约 12.7 世界单位（洗盘机自身宽度 29.6 的 43%），轻轻拖几乎不滑。
//   三个参数都暴露出来，想调手感直接改，距离用 v²/(2a) 心算得到。
//
// 初速度估计：拖动期间每一帧把瞬时速度过一遍**一阶低通**（时间常数 speedSmoothing）。
//   等效取样窗 ≈ 2.2 × speedSmoothing，默认 0.045s → 约 0.1s。
//   用它而不是「最近 0.1s 的位移」，是因为窗口还没填满时后者估不准（刚按下那几帧），
//   而低通在 60fps 下 5 帧就收敛到真实速度的 87%。
//
// 撞边界：反向 ×bounce，**每个轴整个滑行期只弹一次**（弹过之后再撞该轴就清零）。
//   机身比海绵大得多，允许连弹会表现成「机身在墙边抽插」，很容易被当成 bug。
[DisallowMultipleComponent]
public class DragInertia : MonoBehaviour
{
    [Header("Velocity estimate")]
    [Tooltip("瞬时速度的一阶低通时间常数（秒）。等效取样窗 ≈ 2.2 倍该值。")]
    [SerializeField, Min(0.005f)] private float speedSmoothing = 0.045f;

    [Header("Glide")]
    [Tooltip("初速度上限（世界单位/秒），防止一次极快的甩动飞出半个桌面。")]
    [SerializeField, Min(0f)] private float maxSpeed = 90f;
    [Tooltip("减速度（世界单位/秒²）。滑行距离 = v²/(2a)。")]
    [SerializeField, Min(1f)] private float deceleration = 320f;
    [Tooltip("低于这个速度就直接停，不再逐帧逼近 0（否则会留一段看不出来的蠕动）。")]
    [SerializeField, Min(0.01f)] private float stopSpeed = 0.8f;

    [Header("Bounce")]
    [SerializeField, Range(0f, 1f)] private float bounce = 0.4f;
    [Tooltip("Reduce Motion 打开时的减速度倍数：惯性还在、还是「滑了一段」，但满速距离砍到 1/3。")]
    [SerializeField, Range(1f, 6f)] private float reducedDecelFactor = 3f;

    // 由持有者在 Awake 里接上：把「想去的位置」夹回合法区（桌面内表面 + 实体碰撞）。
    // 用委托而不是接口 —— 持有者有两种完全不同的夹取实现（海绵看 sprite.bounds、
    // 刮票机看会随等级变宽的 contentRect），为这点差异建一层抽象不划算。
    public System.Func<Vector3, Vector3> Clamp;

    public bool Sliding { get; private set; }

    private Vector3 previousPosition;
    private Vector2 velocity;
    private float decel;
    private bool bouncedX;
    private bool bouncedY;

    private void Awake()
    {
        decel = HoverJellySettings.ReducedMotion ? deceleration * reducedDecelFactor : deceleration;
        previousPosition = transform.position;
        HoverJellySettings.Changed += OnSettingsChanged;
    }

    private void OnDestroy() => HoverJellySettings.Changed -= OnSettingsChanged;

    // REDUCE MOTION 是**运行时**开关（HoverJellySettings 存 PlayerPrefs 并广播 Changed），
    // 果冻 / 水花 / 票飞入都是当场改。这里不在 Awake 之后重读，就等于「拖动手感必须重启
    // 才生效」—— 同一场里一半动画变了、滑行距离没变，最容易被当成 bug。
    private void OnSettingsChanged()
    {
        decel = HoverJellySettings.ReducedMotion ? deceleration * reducedDecelFactor : deceleration;
    }

    // 开始拖动：清掉上一次的滑行与速度估计。
    public void BeginDrag()
    {
        Sliding = false;
        velocity = Vector2.zero;
        bouncedX = bouncedY = false;
        previousPosition = transform.position;
    }

    // 拖动期间每帧调用，必须在本帧写完 transform.position 之后。
    public void TrackDrag()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector2 instant = (transform.position - previousPosition) / dt;
        float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.005f, speedSmoothing));
        velocity = Vector2.Lerp(velocity, instant, k);
        previousPosition = transform.position;
    }

    // 松手：把估计出来的速度交给滑行。
    public void Release()
    {
        velocity = Vector2.ClampMagnitude(velocity, maxSpeed);
        KillOutwardBlockedAxis();
        if (velocity.magnitude <= stopSpeed)
        {
            Stop();
            return;
        }
        Sliding = true;
    }

    // 直接停掉滑行，不清速度估计（下次 BeginDrag 会清）。
    public void Stop()
    {
        Sliding = false;
        velocity = Vector2.zero;
    }

    private void Update()
    {
        if (!Sliding) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float speed = velocity.magnitude;
        if (speed <= stopSpeed)
        {
            Stop();
            return;
        }
        velocity = velocity.normalized * Mathf.Max(0f, speed - decel * dt);

        Vector3 here = transform.position;
        Vector3 desired = here + (Vector3)(velocity * dt);
        Vector3 clamped = Clamp != null ? Clamp(desired) : desired;

        // 「被挡住」= 夹取结果和想去的位置在某个轴上对不上。
        // 逐轴判断，所以斜着撞壁只清掉撞到的那个轴，另一个轴继续滑（沿边滑）。
        float wantX = desired.x - here.x;
        float wantY = desired.y - here.y;
        bool blockedX = Mathf.Abs((clamped.x - here.x) - wantX) > 1e-4f;
        bool blockedY = Mathf.Abs((clamped.y - here.y) - wantY) > 1e-4f;
        if (blockedX) ApplyBlock(ref velocity.x, ref bouncedX);
        if (blockedY) ApplyBlock(ref velocity.y, ref bouncedY);

        transform.position = clamped;
        if (velocity.magnitude <= stopSpeed) Stop();
    }

    private void ApplyBlock(ref float axis, ref bool alreadyBounced)
    {
        if (alreadyBounced)
        {
            axis = 0f;      // 该轴已经弹过一次：再撞就停死，绝不连弹
            return;
        }
        axis = -axis * bounce;
        alreadyBounced = true;
    }

    // 松手那一瞬间已经贴在边界上、速度又朝外的那个轴，直接清零。
    // 否则「把机器顶到墙边松手」会看到它自己弹回来一小段，很像 bug。
    private void KillOutwardBlockedAxis()
    {
        if (Clamp == null) return;
        const float Probe = 0.05f;
        if (velocity.sqrMagnitude < 1e-6f) return;

        Vector2 dir = velocity.normalized;
        Vector3 here = transform.position;
        Vector3 got = Clamp(here + (Vector3)(dir * Probe)) - here;
        float wantX = dir.x * Probe;
        float wantY = dir.y * Probe;

        // 探针只走 Probe 那么远，被挡住时位移会明显小于它的一半。
        if (Mathf.Abs(dir.x) > 0.01f && Mathf.Abs(got.x - wantX) > Probe * 0.5f) velocity.x = 0f;
        if (Mathf.Abs(dir.y) > 0.01f && Mathf.Abs(got.y - wantY) > Probe * 0.5f) velocity.y = 0f;
    }
}
