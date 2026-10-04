# Unity project context

Verified 2026-10-05 against commit 1f7d30d and existing working changes.

- Project: `My project (2D)`, Lottery Mania; Unity 6000.5.5f1.
- Confirmed live renderer: UniversalRP. Input System enabled; uGUI and TextMeshPro UI, Press Start 2P font.
- Runtime code: `Assets/Scripts`; scene authoring tools: `Assets/Editor`. No first-party assembly definitions or tests found. Unity Test Framework 1.7.0 installed.
- Single enabled startup scene: `Assets/Scenes/SampleScene.unity`.
- MonoBehaviour architecture. `LotteryGame` owns economy and PlayerPrefs saves. `MainMenuScreen` owns menu states, input blocking and time scale. `ScreenFader` persists through New Game scene reloads.
- New Game discards the old session then reloads; Continue starts the loaded session without reloading. Tutorial is a menu-owned modal view, entered only by the New Game reload path.
- Serialized private UI references and editor setup scripts are established conventions; preserve existing scene changes and GUIDs.
- Coplay Unity MCP available and verified against this project. No new packages needed.
- Evidence: README, Packages/manifest.json, ProjectSettings, MainMenuScreen.cs, ScreenFader.cs, LotteryGame.cs, MainMenuSetup.cs and live editor state.
