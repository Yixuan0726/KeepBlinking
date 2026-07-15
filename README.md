# KeepBlinking

KeepBlinking is an eye-care interaction MVP built in Unity. It explores whether a short, gentle game loop can train healthier screen habits through eye-driven actions: looking toward soft targets, blinking to capture, closing eyes to rest, and moving the phone away to switch focal distance.

This repository is prepared as a lightweight review build for the current prototype. It focuses on the custom gameplay scripts, Unity scene setup, and the design intent behind the MVP.

## Current MVP Loop

1. **Edge signal observation**: red blocks orbit around the screen edge to encourage smooth gaze movement.
2. **Blink capture**: when gaze hover turns a signal orange, a gentle blink converts it into a green sample.
3. **Inward drift / eye rest**: during inward drift, closing the eyes freezes the field and expands a cleansing circle.
4. **Safe release**: opening the eyes clears blocks inside the current circle radius.
5. **Distance switch harvest**: moving the phone away collects green samples into the bottom reflection bar.
6. **Module choice**: when the bar fills, three simple module cards appear for a roguelite-style choice.
7. **Observation report**: after the timed MVP session, the game summarizes blink signals, blink captures, eye-rest breaks, distance switches, samples collected, and module choices.

## Why This Prototype Exists

The project is not trying to make players stare harder at the screen. The goal is to make screen-facing moments trigger healthier micro-actions:

- Blink before and during screen interaction.
- Break sustained gaze with short eye closure.
- Change viewing distance to relax near-focus tension.
- Keep play sessions short and non-punitive.

## Unity Version

- Unity `6000.1.8f1`
- Target format: portrait iPhone interaction, currently tested in-editor with webcam / MediaPipe face landmarks.

## Important Dependency

This project uses **MediaPipeUnityPlugin 0.16.3** for face landmark, blink, rough gaze, and face-distance signals.

The local package archive is about 290 MB, so it is intentionally not committed to GitHub. To run the project locally, install or restore:

- `com.github.homuler.mediapipe-0.16.3.tgz`
- Package manifest reference: `Packages/manifest.json`
- Plugin: MediaPipeUnityPlugin by homuler, version `0.16.3`

If Unity reports that the local MediaPipe tarball is missing, download the matching release from the MediaPipeUnityPlugin GitHub releases page and place it at:

```text
Packages/com.github.homuler.mediapipe-0.16.3.tgz
```

Then reopen the Unity project.

## Key Files

- `Assets/Scenes/SampleScene.unity`: current MVP scene.
- `Assets/KeepBlinking/Scripts/Gameplay/EdgeOrbitHarvestMvp.cs`: main playable MVP loop.
- `Assets/KeepBlinking/Scripts/Gameplay/BlinkBootSequence.cs`: first-step blink boot prototype.
- `Assets/KeepBlinking/Scripts/Input/EyeInputDebugState.cs`: MediaPipe face landmark bridge for gaze, blink, eye closure, and face distance.
- `Assets/KeepBlinking/Scripts/Input/EyeInputDebugOverlay.cs`: optional debugging overlay.

## Demo Notes

- Press `F1` to toggle the debug HUD.
- Press `F2` to open the MVP observation report early.
- The report is intended for teacher review: it frames the interaction as eye-care behavior data instead of score pressure.

## Current Status

This is an MVP / research prototype, not a final art pass. The focus is proving the feasibility and feel of the core eye-driven loop before expanding narrative, long-term progression, or mobile deployment.
