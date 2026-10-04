# New Game tutorial

- Five English pages, shown only in the New Game scene-reload branch of `MainMenuScreen.Start`. Continue, Resume and normal scene startup do not start it.
- Centered gold/navy pixel dialog using Press Start 2P. Kitty1 appears on pages 1–4; Kitty2 appears only on page 5. Both use the same bottom-right position and point filtering.
- NEXT advances; PREVIOUS is available on pages 2–5; LET'S PLAY! closes the final page. Only numeric page counts are shown. Menu navigation selects the button for keyboard submission. Escape does not bypass orientation.
- The menu owns the modal view: time is paused, gameplay raycasts disabled, and existing GameplayBlocked guards remain active. Transition completion preserves the tutorial view.
- Scene wiring is already saved in SampleScene. `Tools/Lottery/Add New Game Tutorial` is an authoring command for scenes without the panel; it refuses duplicates.
- No save schema changes or additional dependencies.

## Validation (2026-10-05)

Unity compiled without errors or warnings. Editor-side execution verified five-page progression, blocked gameplay/time scale, final dismissal, redundant advance safety and restarting at page one. TMP reported no overflow on any page (longest preferred height 128.02 within 158 available). A 1280x720 camera preview confirmed layout and both cats; the temporary preview settings were discarded by reopening the saved scene.

Play Mode New Game/reload and real mouse/keyboard interaction remain untested. Existing player saves were not cleared for validation.

## Tutorial navigation and spotlight update

Page 2 spotlights One More Plate; pages 3 and 4 select Tickets and Gadgets respectively and spotlight the tabs/product area. Four dark rectangles leave a clear opening with a gold pixel border. The dialog moves right beside the shop on pages 3–4. Spotlight coordinates follow the target canvas at runtime, and all gameplay remains blocked. Returning with Previous updates all page state.

Unity compilation and editor-side forward/backward checks passed for all pages: cat exclusivity, Previous visibility, numeric counter, shop selection, spotlight visibility, text overflow, first-page boundary, and completion. An editor camera preview checked the updated layout. Live mouse/keyboard interaction in Play Mode remains untested.
