# CARE RHYTHM Research Notes

## First-level care loop

- The first level combines **GUIDED MOVEMENT**, **FOCUS SHIFT**, **SCREEN REST**, and the existing **DISTANCE RESET** collection gesture.
- Horizontal and vertical movement are inferred from the MediaPipe face center relative to a fixed session baseline. This is a phone/face relative-motion interaction; it cannot fully distinguish phone motion from head motion.
- Focus Shift is inspired by near/far fixation concepts, but it is an experimental interaction rather than a clinical exercise or treatment.
- Rounds 1, 3, and 4 end with Screen-Down Rest. Round 2 uses Guided Eye Movement after the vertical sweep so that a sustained closed-eye interval and a screen-down interval are not required back-to-back.
- Guided Eye Movement verifies sustained eye closure and provides timed clockwise/pause/counter-clockwise audio guidance. It does not read gaze direction and does not verify eyeball rotation direction or completion. Its reward represents completed closed-eye guidance time only.
- Valid Screen-Down Rest or Guided Eye Movement time creates pending gold fragments, but no experience is awarded until every fragment completes the formal Push Away flight into the experience bar.
- Device orientation is evaluated relative to the attitude captured in the player's normal portrait hold. A gravity-alignment fallback avoids hard-coding one iPhone axis.
- A completed rest requires a stable screen-down hold. A quick shake, eye state, gaze direction, Near/Far movement, and Push Away cannot complete it. Face-tracking loss during a valid screen-down hold is expected.
- Skip returns to the care flow without penalty or rest-time fragments.

## Safety and claims

- Screen-Down Rest is a general screen break, not a medical treatment or diagnostic activity.
- Guided phone movement and Focus Shift do not claim to cure eye fatigue, restore vision, treat astigmatism, or provide a clinically proven result.
- Guided Eye Movement is a closed-eye rest interaction, not a medical treatment or an assessment of eye-movement quality.
- KeepBlinking cannot replace an eye examination or advice from an ophthalmologist or optometrist.
- Stop the activity if it causes pain, double vision, dizziness, nausea, or other noticeable discomfort.

## Validation boundary

Automated logic tests cover movement signs, scale rejection, non-repeatable reward segments, fixed Focus Shift safety ranges, the orientation/stability formula, preview-before-prompt gating, and capped closed-eye guidance rewards. Front-camera mirroring, physical sensor axes, sustained closure behavior, audio interpretation, locked-portrait behavior, haptic feedback in silent mode, tabletop angles, hand-held screen-down use, and perceived motion comfort still require iPhone device testing. Actual eyeball rotation cannot be validated by this system.
