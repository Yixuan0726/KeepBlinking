# Care Station Research Protocol

## Scope

Care Station records short-session interaction outcomes for exploratory product research. The four in-game pre/post ratings (eye comfort, dryness, eye strain, and focus difficulty) are custom, immediate, subjective measures created for this project. They are not validated medical diagnostic scales, and they must not be interpreted as diagnoses or evidence that the game treats an eye condition.

The Computer Vision Syndrome Questionnaire (CVS-Q) was developed to assess the frequency and intensity of screen-related symptoms in workplace settings. The Ocular Surface Disease Index (OSDI) mainly addresses dry-eye symptoms, visual function, and environmental triggers over the preceding week. Their intended constructs and time horizons differ from a brief Care Station session. This project does not reproduce either copyrighted questionnaire.

A pre/post change from one short session can support only a statement about the participant's immediate self-reported experience. It cannot demonstrate treatment efficacy, vision recovery, or a clinical improvement. Care Station may report short-term self-reported changes, completion of care routines, and sensor availability.

## Sensor interpretation

Sensor completion rate is defined as:

`Sensor completed / eligible sensor actions`

Results must also report eligible actions, sensor-completed actions, fallback-completed actions, replaced steps, developer-skipped steps, and tracking-lost duration. This rate is not medical accuracy, eye-tracking accuracy, or diagnostic accuracy because the prototype has no external ground truth. Guided Eye Circles provides timed guidance and does not verify eyeball movement direction.

## Privacy and storage

Research Mode stores numbers, enumerated workflow outcomes, timestamps, and anonymous random UUIDs locally under `Application.persistentDataPath/KeepBlinking/Research/`. It does not upload data and makes no network requests. It does not store camera photos, video, face screenshots, face-landmark coordinates, raw gaze trajectories, biometric templates, names, email addresses, precise location, contacts, advertising identifiers, account identifiers, or hardware-derived participant identifiers.

## Future formal study requirements

Any future formal publication study should use appropriate ethics approval, informed consent, a preregistered protocol, an adequate sample size, and validated measures selected for the study duration and research question. Participants should be told to stop if they experience pain, double vision, dizziness, or marked discomfort. This game does not replace an eye examination or professional medical advice.

## References

Seguí Mdel M, et al. A reliable and valid questionnaire was developed to measure computer vision syndrome at the workplace. *Journal of Clinical Epidemiology*. 2015;68(6):662–673. doi:10.1016/j.jclinepi.2015.01.015.

Schiffman RM, et al. Reliability and validity of the Ocular Surface Disease Index. *Archives of Ophthalmology*. 2000;118(5):615–621. doi:10.1001/archopht.118.5.615.
