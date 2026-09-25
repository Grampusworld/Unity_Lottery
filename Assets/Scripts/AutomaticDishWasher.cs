using UnityEngine;

// Automatic income is separate from the single manual plate on the table.
[RequireComponent(typeof(SpriteRenderer))]
public class AutomaticDishWasher : MonoBehaviour
{
    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Sprite workingSprite;

    private SpriteRenderer display;
    private LotteryGame game;
    private float elapsed;
    private float secondsPerCycle = 10f;
    private int platesPerCycle = 5;
    private bool unlocked;

    private void Awake()
    {
        display = GetComponent<SpriteRenderer>();
        display.sprite = idleSprite;
        display.enabled = false;
    }

    public void Configure(LotteryGame owner, bool active, float seconds, int plates)
    {
        game = owner;
        unlocked = active;
        secondsPerCycle = Mathf.Max(1f, seconds);
        platesPerCycle = Mathf.Max(1, plates);
        if (display == null) display = GetComponent<SpriteRenderer>();
        display.enabled = active;
        if (!active)
        {
            elapsed = 0f;
            display.sprite = idleSprite;
        }
    }

    private void Update()
    {
        if (!unlocked || game == null) return;
        elapsed += Time.deltaTime;
        display.sprite = elapsed < 1f ? idleSprite : workingSprite;
        if (elapsed < secondsPerCycle) return;
        elapsed -= secondsPerCycle;
        display.sprite = idleSprite;
        game.AwardWashedPlates(platesPerCycle);
    }
}
