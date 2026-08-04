# L2CS-Net ONNX Evaluation

This directory contains the isolated, local-only validation harness for the
KeepBlinking L2CS-Net experiment.

The harness never uploads camera frames and does not use cloud inference. The
Python virtual environment, upstream source snapshots, model files, generated
artifacts, and benchmark results are ignored by the main repository.

## Validation route

The checkpoint folder linked by the official L2CS-Net README is unavailable.
This evaluation therefore does not download or deserialize any third-party
pickle checkpoint and does not claim PyTorch-checkpoint equivalence.

The current candidate is Ailia's third-party converted ONNX model:

- Ailia repository: `https://github.com/ailia-ai/ailia-models`
- Fixed commit: `e28ef95d123bc4e625fea40462d3cd33e0eef688`
- Ailia sample: `face_recognition/l2cs_net`
- ONNX URL:
  `https://storage.googleapis.com/ailia-models/l2cs_net/l2cs.onnx`
- Ailia descriptor URL:
  `https://storage.googleapis.com/ailia-models/l2cs_net/l2cs.onnx.prototxt`
- Luxonis model card:
  `https://models.luxonis.com/luxonis/l2cs-net/7051c9d2-78a4-420b-91a8-2d40ecf958dd`

This is a third-party converted ONNX candidate, not an official original
checkpoint.

## Technical gate

Only the ONNX model and its Ailia descriptor are downloaded. The gate:

1. records source URL, byte length, server metadata, and SHA-256;
2. runs `onnx.checker.check_model`;
3. records graph input/output names, shapes, dtypes, and opset;
4. runs finite-output smoke tests using ONNX Runtime CPU;
5. benchmarks model load time and warm inference latency;
6. tests deterministic fixed images, horizontal mirroring, and output order;
7. produces fixed fixtures for Unity Inference Engine parity testing.

The technical gate cannot establish real gaze accuracy. Real camera and user
accuracy are measured by the Unity Development A/B test.

Required final wording:

> Official Google Drive checkpoint unavailable.  
> Third-party converted ONNX evaluated.  
> Original PyTorch checkpoint equivalence not verified.

## Generated local files

- `.venv/`: project-local Python environment
- `upstream/`: source snapshots used for audit
- `models/`: downloaded research-only ONNX and source metadata
- `artifacts/`: deterministic parity fixtures
- `results/`: checker, CPU benchmark, and parity reports

None of these generated directories should be committed.
