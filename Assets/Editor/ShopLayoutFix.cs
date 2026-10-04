using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace Lottery.EditorTools
{
    /// <summary>
    /// Idempotent layout repair for the tickets / gadgets shop panel.
    ///
    /// PROBLEM 1 — the "MONEY" line wrapped onto two lines, but only while playing.
    ///   MoneyText is the only text in the shop whose TMP wrapping mode is Normal: every
    ///   other label is built by LotterySceneSetup.Text(), which forces NoWrap, and MoneyText
    ///   is never passed through it (it is a pre-existing object that is only re-parented).
    ///   PressStart2P is monospaced — every glyph has m_HorizontalAdvance = 8 at
    ///   m_PointSize = 8, i.e. exactly 1.0 em — so one character occupies `fontSize` canvas
    ///   units. The box was 338.46 wide, which fits the serialized preview "MONEY $0"
    ///   (8 chars * 40 = 320) but not the string the game writes at runtime,
    ///   "MONEY  $" + balance (9 chars * 40 = 360). Hence it fitted in the editor and
    ///   wrapped as soon as the game ran.
    ///   Fix: wrapping off, box widened so the largest supported amount fits, and the
    ///   serialized preview switched to the same two-space format as LotteryGame.RefreshUI
    ///   so the editor can never disagree with play mode again.
    ///
    /// PROBLEM 2 — the shop buttons were hard to tell apart.
    ///   The gaps were 20px (tickets) and 10px (gadgets), while the button fill RGB(28,44,69)
    ///   only reaches about 1.2:1 contrast against the panel RGB(20,27,43). The scene contains
    ///   no Outline, Shadow or Mask at all, so that thin seam was the only separator.
    ///   Fix: one uniform 22px gap in both panels (the gadgets panel gives the room back by
    ///   dropping its button height from 120 to 108) plus a light 3px border on every button.
    ///
    /// Safe to run repeatedly: every value is assigned absolutely, and the border component is
    /// only added when the button does not already have one.
    /// </summary>
    public static class ShopLayoutFix
    {
        // ---- Money line -------------------------------------------------------------------
        const float MoneyX = 25f;
        const float MoneyY = -83f;
        const float MoneyWidth = 600f;
        const float MoneyHeight = 92.49f;
        const float MoneyFontSize = 40f;

        /// <summary>Widest amount we must be able to show: "MONEY  $99999" = 13 characters.</summary>
        const int MoneyMaxChars = 15;

        // ---- Rows -------------------------------------------------------------------------
        /// <summary>One gap value for both panels.</summary>
        const float Gap = 22f;

        const float TicketButtonWidth = 550f;
        const float TicketButtonHeight = 210f;
        /// <summary>Centre Y of the first ticket button (panel-local, anchors top-left).</summary>
        const float TicketFirstCentreY = -105f;

        const float GadgetButtonWidth = 590f;
        const float GadgetButtonHeight = 108f;
        /// <summary>Top edge of the first gadget button (panel-local, anchors top-left).</summary>
        const float GadgetFirstTopY = -10f;

        // ---- Border -----------------------------------------------------------------------
        static readonly Color BorderColor = new Color32(70, 100, 145, 255);
        static readonly Vector2 BorderDistance = new Vector2(3f, -3f);

        const string ShopName = "ShopPanel";
        const string TicketsPanelName = "TicketsPanel";
        const string GadgetsPanelName = "GadgetsPanel";
        const string MoneyName = "MoneyText";

        static readonly string[] TicketButtons =
        {
            "LuckyTicketButton", "GoldTicketButton", "NovaTicketButton",
            "HeartMatchTicketButton", "CrossCodeTicketButton", "ZigzagRunTicketButton"
        };

        static readonly string[] GadgetButtons =
        {
            "MultiplePlatesButton",
            "PurpleSpongeButton", "WasherUnlockButton", "SpeedUpgradeButton", "CapacityUpgradeButton",
            "ScratcherUnlockButton", "ScratcherSpeedButton", "ScratcherCapacityButton"
        };

        [MenuItem("Tools/Lottery/Fix Shop Layout", false, 0)]
        public static void Apply()
        {
            GameObject shop = GameObject.Find(ShopName);
            if (shop == null)
            {
                Debug.LogError("[ShopLayoutFix] '" + ShopName + "' was not found. Open the shop scene first.");
                return;
            }

            FixMoneyLine(shop.transform);

            FixRow(shop.transform, TicketsPanelName, TicketButtons,
                   TicketButtonWidth, TicketButtonHeight, TicketFirstCentreY, true);

            FixRow(shop.transform, GadgetsPanelName, GadgetButtons,
                   GadgetButtonWidth, GadgetButtonHeight, GadgetFirstTopY, false);

            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[ShopLayoutFix] Shop layout repaired and scene saved.\n"
                      + "  money line: NoWrap, box " + MoneyWidth + "x" + MoneyHeight
                      + " at (" + MoneyX + ", " + MoneyY + "), fontSize " + MoneyFontSize
                      + ", fits up to " + (MoneyWidth / MoneyFontSize) + " characters\n"
                      + "  uniform gap: " + Gap + "px\n"
                      + "  tickets: " + TicketButtons.Length + " buttons, height " + TicketButtonHeight
                      + ", pitch " + (TicketButtonHeight + Gap) + "\n"
                      + "  gadgets: " + GadgetButtons.Length + " buttons, height " + GadgetButtonHeight
                      + ", pitch " + (GadgetButtonHeight + Gap)
                      + ", used " + (GadgetButtons.Length * GadgetButtonHeight
                                     + (GadgetButtons.Length - 1) * Gap) + "px of the 915px panel\n"
                      + "  border: RGB" + (Color32)BorderColor + " over " + BorderDistance + "px");
        }

        static void FixMoneyLine(Transform shop)
        {
            Transform money = shop.Find(MoneyName);
            if (money == null)
            {
                Debug.LogWarning("[ShopLayoutFix] '" + MoneyName + "' not found under " + ShopName);
                return;
            }

            RectTransform rect = money as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(MoneyX, MoneyY);
                rect.sizeDelta = new Vector2(MoneyWidth, MoneyHeight);
            }

            TMP_Text text = money.GetComponent<TMP_Text>();
            if (text == null)
            {
                Debug.LogWarning("[ShopLayoutFix] '" + MoneyName + "' has no TMP text component");
                return;
            }

            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = false;
            text.fontSize = MoneyFontSize;
            // Centring keeps the line where it has always looked like it sits, and lets it grow
            // in both directions instead of running off the right edge of the panel.
            text.horizontalAlignment = HorizontalAlignmentOptions.Center;
            // Geometry is what the scene already used, so the line keeps its exact vertical seat.
            text.verticalAlignment = VerticalAlignmentOptions.Geometry;
            // Two spaces, exactly as LotteryGame.RefreshUI builds it at runtime.
            text.text = "MONEY  $0";

            float needed = MoneyMaxChars * MoneyFontSize;
            if (MoneyWidth < needed)
            {
                Debug.LogWarning("[ShopLayoutFix] money box is " + MoneyWidth + "px but "
                                 + MoneyMaxChars + " characters need " + needed
                                 + "px at fontSize " + MoneyFontSize);
            }
        }

        static void FixRow(Transform shop, string panelName, string[] buttonNames,
                           float width, float height, float firstY, bool firstYIsCentre)
        {
            Transform panel = shop.Find(panelName);
            if (panel == null)
            {
                Debug.LogWarning("[ShopLayoutFix] '" + panelName + "' not found under " + ShopName);
                return;
            }

            float pitch = height + Gap;
            PixelRowScroll scroll = panel.GetComponent<PixelRowScroll>();
            if (scroll != null && firstYIsCentre) firstY -= 10f;

            for (int i = 0; i < buttonNames.Length; i++)
            {
                // Rows may sit directly under the panel (tickets) or under a scrolling
                // Content child (gadgets, since the panel became a scroll viewport).
                // Search the whole subtree so this menu keeps working either way.
                Transform button = FindDeep(panel, buttonNames[i]);
                if (button == null)
                {
                    Debug.LogWarning("[ShopLayoutFix] '" + buttonNames[i] + "' not found under " + panelName);
                    continue;
                }

                RectTransform rect = button as RectTransform;
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(0f, 1f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = new Vector2(width, height);

                    float y = firstYIsCentre
                        ? firstY - i * pitch            // the first centre is given directly
                        : firstY - height * 0.5f - i * pitch; // the first top edge is given

                    // Centre on the button's OWN parent, not on the panel.
                    // The gadget rows now live inside a 590-wide Content that sits in a
                    // 620-wide viewport; using the panel width would push them 15px right.
                    RectTransform parentRect = rect.parent as RectTransform;
                    float parentWidth = parentRect != null
                        ? parentRect.rect.width
                        : (panel as RectTransform != null ? ((RectTransform)panel).sizeDelta.x : 0f);
                    rect.anchoredPosition = new Vector2(parentWidth * 0.5f, y);
                }

                EnsureBorder(button);
            }
        }

        static Transform FindDeep(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == name) return all[i];
            }
            return null;
        }

        static void EnsureBorder(Transform button)
        {
            if (button.GetComponent<Graphic>() == null)
            {
                Debug.LogWarning("[ShopLayoutFix] '" + button.name + "' has no Graphic, skipping border");
                return;
            }

            Outline outline = button.GetComponent<Outline>();
            if (outline == null)
            {
                outline = button.gameObject.AddComponent<Outline>();
            }

            outline.effectColor = BorderColor;
            outline.effectDistance = BorderDistance;
        }
    }
}
