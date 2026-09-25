using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Pick up the visible sponge, drag it across a plate, then return it to the table.
[RequireComponent(typeof(SpriteRenderer))]
public class SpongeDrag : MonoBehaviour
{
    [SerializeField] private Sprite yellowSprite;
    [SerializeField] private Sprite purpleSprite;
    [SerializeField] private Camera inputCamera;
    [SerializeField, Min(1)] private int yellowBrushRadius = 4;

    private LotteryGame game;
    private SpriteRenderer spriteRenderer;
    private HoverJelly hoverJelly;
    private Vector3 homePosition;
    private Vector3 previousPosition;
    private Vector3 baseScale;
    private bool dragging;
    private bool advanced;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        homePosition = transform.position;
        baseScale = transform.localScale;
        if (inputCamera == null) inputCamera = Camera.main;
        hoverJelly = GetComponent<HoverJelly>();
    }

    public void Initialize(LotteryGame owner, bool usePurple)
    {
        game = owner;
        SetAdvanced(usePurple);
    }

    public void SetAdvanced(bool usePurple)
    {
        advanced = usePurple;
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = usePurple ? purpleSprite : yellowSprite;
    }

    private void Update()
    {
        if (game == null || inputCamera == null || !ReadMouse(out Vector2 screen, out bool pressed, out bool justPressed)) return;
        if (!pressed)
        {
            if (dragging)
            {
                transform.position = homePosition;
                if (hoverJelly != null) hoverJelly.SetPressed(false);
            }
            dragging = false;
            return;
        }

        Vector3 world = inputCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y,
            Mathf.Abs(inputCamera.transform.position.z - transform.position.z)));
        world.z = transform.position.z;
        if (!dragging)
        {
            if (spriteRenderer.sprite == null) return;
            // 判定基准是静止尺寸的包围盒，hover 果冻缩放时命中区域保持不变。
            if (!justPressed || !HoverJelly.ContainsPointUnscaled(spriteRenderer.transform,
                    spriteRenderer.sprite.bounds, world, baseScale)) return;
            dragging = true;
            previousPosition = world;
            if (hoverJelly != null) hoverJelly.SetPressed(true);
        }

        transform.position = world;
        DirtyPlate plate = game.CurrentPlate;
        if (plate != null)
        {
            int brush = yellowBrushRadius * (advanced ? 2 : 1);
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(previousPosition, world) / 0.5f), 1, 50);
            for (int i = 0; i <= steps; i++) plate.ScrubAt(Vector3.Lerp(previousPosition, world, (float)i / steps), brush);
        }
        previousPosition = world;
    }

    private static bool ReadMouse(out Vector2 screen, out bool pressed, out bool justPressed)
    {
        screen = Vector2.zero;
        pressed = justPressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return false;
        screen = Mouse.current.position.ReadValue();
        pressed = Mouse.current.leftButton.isPressed;
        justPressed = Mouse.current.leftButton.wasPressedThisFrame;
        return true;
#elif ENABLE_LEGACY_INPUT_MANAGER
        screen = Input.mousePosition;
        pressed = Input.GetMouseButton(0);
        justPressed = Input.GetMouseButtonDown(0);
        return true;
#else
        return false;
#endif
    }

    private void OnDisable()
    {
        if (dragging)
        {
            transform.position = homePosition;
            if (hoverJelly != null) hoverJelly.SetPressed(false);
        }
        dragging = false;
    }
}
