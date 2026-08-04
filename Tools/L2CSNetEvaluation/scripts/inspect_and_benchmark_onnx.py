from __future__ import annotations

import argparse
import gc
import hashlib
import json
import math
import os
import platform
import statistics
import sys
import time
from pathlib import Path
from typing import Any

import cv2
import numpy as np
import onnx
import onnxruntime as ort
import psutil


AILIA_COMMIT = "e28ef95d123bc4e625fea40462d3cd33e0eef688"
MODEL_URL = "https://storage.googleapis.com/ailia-models/l2cs_net/l2cs.onnx"
MODEL_BYTES = 95_414_596
MODEL_SHA256 = "3DF55DDB1AD6667496F394A635AB7DCA6947800C93E941B1E7669CF3BA30E2BF"
PROTOTXT_URL = (
    "https://storage.googleapis.com/ailia-models/l2cs_net/l2cs.onnx.prototxt"
)
PROTOTXT_BYTES = 52_773
PROTOTXT_SHA256 = (
    "F7C0F52E2EACACA70F3F2AA48728463A8F29551C115D96A6C3B8A2819997A644"
)
SELFIE_URL = (
    "https://raw.githubusercontent.com/ailia-ai/ailia-models/"
    f"{AILIA_COMMIT}/face_recognition/l2cs_net/selfie.png"
)
SELFIE_BYTES = 1_094_546
SELFIE_SHA256 = "B1BAFF650739794C5B99963F564549D9BD630FB61CF6E9B9130ECDF3CF38CA9E"
REFERENCE_URL = (
    "https://raw.githubusercontent.com/ailia-ai/ailia-models/"
    f"{AILIA_COMMIT}/face_recognition/l2cs_net/output.png"
)
REFERENCE_BYTES = 1_340_170
REFERENCE_SHA256 = (
    "4AE95CDF3622856E119DE2F689A600FE885C44A49BAD03002249FF81DA719EFB"
)

IMAGENET_MEAN = np.asarray([0.485, 0.456, 0.406], dtype=np.float32)
IMAGENET_STD = np.asarray([0.229, 0.224, 0.225], dtype=np.float32)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def verify_file(
    path: Path, expected_bytes: int, expected_sha256: str, source_url: str
) -> dict[str, Any]:
    if not path.is_file():
        raise FileNotFoundError(f"Missing required file: {path}")
    actual_bytes = path.stat().st_size
    actual_sha256 = sha256(path)
    if actual_bytes != expected_bytes:
        raise RuntimeError(
            f"Unexpected size for {path.name}: {actual_bytes}, expected {expected_bytes}"
        )
    if actual_sha256 != expected_sha256:
        raise RuntimeError(
            f"Unexpected SHA-256 for {path.name}: {actual_sha256}, "
            f"expected {expected_sha256}"
        )
    return {
        "path": str(path.resolve()),
        "source_url": source_url,
        "bytes": actual_bytes,
        "sha256": actual_sha256,
    }


def tensor_shape(value_info: onnx.ValueInfoProto) -> list[int | str | None]:
    dimensions: list[int | str | None] = []
    tensor_type = value_info.type.tensor_type
    for dimension in tensor_type.shape.dim:
        if dimension.HasField("dim_value"):
            dimensions.append(int(dimension.dim_value))
        elif dimension.HasField("dim_param"):
            dimensions.append(dimension.dim_param)
        else:
            dimensions.append(None)
    return dimensions


def value_info_summary(value_info: onnx.ValueInfoProto) -> dict[str, Any]:
    tensor_type = value_info.type.tensor_type
    return {
        "name": value_info.name,
        "shape": tensor_shape(value_info),
        "element_type": onnx.TensorProto.DataType.Name(tensor_type.elem_type),
    }


def percentile(values: list[float], quantile: float) -> float:
    return float(np.percentile(np.asarray(values, dtype=np.float64), quantile))


def preprocess_rgb(image_rgb: np.ndarray) -> np.ndarray:
    resized = cv2.resize(image_rgb, (448, 448))
    normalized = resized.astype(np.float32) / np.float32(255.0)
    normalized = (normalized - IMAGENET_MEAN) / IMAGENET_STD
    return np.transpose(normalized, (2, 0, 1))[np.newaxis, ...].astype(
        np.float32, copy=False
    )


def scalar_outputs(outputs: list[np.ndarray]) -> tuple[float, float]:
    if len(outputs) != 2:
        raise RuntimeError(f"Expected two outputs, received {len(outputs)}")
    first = np.asarray(outputs[0])
    second = np.asarray(outputs[1])
    if first.size != 1 or second.size != 1:
        raise RuntimeError(
            f"Expected scalar outputs, received {first.shape} and {second.shape}"
        )
    return float(first.reshape(-1)[0]), float(second.reshape(-1)[0])


def gaze_vector(pitch_degrees: float, yaw_degrees: float, length: float) -> np.ndarray:
    pitch = math.radians(pitch_degrees)
    yaw = math.radians(yaw_degrees)
    return np.asarray(
        [
            -length * math.sin(pitch) * math.cos(yaw),
            -length * math.sin(yaw),
        ],
        dtype=np.float64,
    )


def largest_green_rectangle(
    source_bgr: np.ndarray, reference_bgr: np.ndarray
) -> tuple[int, int, int, int]:
    changed = np.max(cv2.absdiff(source_bgr, reference_bgr), axis=2) > 30
    green = (
        (reference_bgr[:, :, 1] > 180)
        & (reference_bgr[:, :, 0] < 100)
        & (reference_bgr[:, :, 2] < 100)
        & changed
    )
    contours, _ = cv2.findContours(
        green.astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE
    )
    if not contours:
        raise RuntimeError("Could not recover the face rectangle from Ailia output.png")
    rectangles = [cv2.boundingRect(contour) for contour in contours]
    x, y, width, height = max(rectangles, key=lambda rect: rect[2] * rect[3])
    if width < 20 or height < 20:
        raise RuntimeError(f"Recovered face rectangle is implausibly small: {(x, y, width, height)}")
    return x, y, x + width - 1, y + height - 1


def reference_arrow_vector(
    source_bgr: np.ndarray,
    reference_bgr: np.ndarray,
    origin: np.ndarray,
    face_width: float,
) -> np.ndarray:
    changed = np.max(cv2.absdiff(source_bgr, reference_bgr), axis=2) > 30
    red = (
        (reference_bgr[:, :, 2] > 160)
        & (reference_bgr[:, :, 1] < 120)
        & (reference_bgr[:, :, 0] < 120)
        & changed
    )
    component_count, labels, stats, _ = cv2.connectedComponentsWithStats(
        red.astype(np.uint8), connectivity=8
    )
    candidates: list[tuple[float, np.ndarray]] = []
    for label in range(1, component_count):
        if stats[label, cv2.CC_STAT_AREA] < 5:
            continue
        ys, xs = np.nonzero(labels == label)
        points = np.column_stack((xs, ys)).astype(np.float64)
        minimum_distance = float(np.min(np.linalg.norm(points - origin, axis=1)))
        candidates.append((minimum_distance, points))
    if not candidates:
        raise RuntimeError("Could not recover the red gaze arrow from Ailia output.png")
    minimum_distance, points = min(candidates, key=lambda candidate: candidate[0])
    if minimum_distance > face_width * 0.20:
        raise RuntimeError(
            "The closest Ailia reference arrow is too far from the selected face center"
        )
    vectors = points - origin
    distances = np.linalg.norm(vectors, axis=1)
    plausible = (distances > face_width * 0.15) & (distances < face_width * 1.25)
    if np.count_nonzero(plausible) < 5:
        raise RuntimeError("Ailia reference gaze arrow did not contain enough pixels")
    points = points[plausible]
    distances = distances[plausible]
    far_threshold = np.percentile(distances, 90)
    return np.mean(points[distances >= far_threshold], axis=0) - origin


def vector_cosine(left: np.ndarray, right: np.ndarray) -> float:
    denominator = float(np.linalg.norm(left) * np.linalg.norm(right))
    if denominator <= 1e-9:
        raise RuntimeError("Cannot compare a zero-length gaze vector")
    return float(np.dot(left, right) / denominator)


def run_case(
    session: ort.InferenceSession, input_name: str, image_rgb: np.ndarray
) -> dict[str, Any]:
    tensor = preprocess_rgb(image_rgb)
    outputs = session.run(None, {input_name: tensor})
    pitch_degrees, yaw_degrees = scalar_outputs(outputs)
    finite = bool(
        np.isfinite(np.asarray(outputs[0])).all()
        and np.isfinite(np.asarray(outputs[1])).all()
    )
    return {
        "pitch_degrees": pitch_degrees,
        "yaw_degrees": yaw_degrees,
        "finite": finite,
        "output_shapes": [list(np.asarray(value).shape) for value in outputs],
    }


def benchmark(
    session: ort.InferenceSession,
    input_name: str,
    tensor: np.ndarray,
    warmup: int,
    iterations: int,
) -> dict[str, Any]:
    process = psutil.Process(os.getpid())
    for _ in range(warmup):
        session.run(None, {input_name: tensor})

    rss_before = process.memory_info().rss
    peak_rss = rss_before
    cpu_before = process.cpu_times()
    wall_start = time.perf_counter()
    latencies_ms: list[float] = []

    for _ in range(iterations):
        start = time.perf_counter()
        outputs = session.run(None, {input_name: tensor})
        latencies_ms.append((time.perf_counter() - start) * 1000.0)
        peak_rss = max(peak_rss, process.memory_info().rss)
        if not all(np.isfinite(np.asarray(value)).all() for value in outputs):
            raise RuntimeError("Non-finite output encountered during benchmark")

    wall_seconds = time.perf_counter() - wall_start
    cpu_after = process.cpu_times()
    cpu_seconds = (
        cpu_after.user
        + cpu_after.system
        - cpu_before.user
        - cpu_before.system
    )
    logical_cpus = psutil.cpu_count(logical=True) or 1
    machine_cpu_percent = (
        (cpu_seconds / wall_seconds) * 100.0 / logical_cpus
        if wall_seconds > 0
        else 0.0
    )

    return {
        "warmup_iterations": warmup,
        "measured_iterations": iterations,
        "latency_ms": {
            "average": statistics.fmean(latencies_ms),
            "p50": percentile(latencies_ms, 50),
            "p95": percentile(latencies_ms, 95),
            "minimum": min(latencies_ms),
            "maximum": max(latencies_ms),
        },
        "inference_fps": iterations / wall_seconds,
        "wall_seconds": wall_seconds,
        "process_cpu_seconds": cpu_seconds,
        "equivalent_cpu_cores": cpu_seconds / wall_seconds,
        "machine_cpu_percent": machine_cpu_percent,
        "logical_cpu_count": logical_cpus,
        "rss_before_bytes": rss_before,
        "peak_rss_bytes": peak_rss,
    }


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )


def set_first_dimension(value_info: onnx.ValueInfoProto, value: int) -> None:
    dimension = value_info.type.tensor_type.shape.dim[0]
    dimension.ClearField("dim_param")
    dimension.dim_value = value


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Audit and benchmark the pinned Ailia L2CS-Net ONNX candidate."
    )
    parser.add_argument("--warmup", type=int, default=10)
    parser.add_argument("--iterations", type=int, default=50)
    args = parser.parse_args()

    evaluation_root = Path(__file__).resolve().parents[1]
    model_root = (
        evaluation_root / "models" / f"ailia_{AILIA_COMMIT}"
    )
    fixture_root = evaluation_root / "artifacts" / "ailia_fixed_fixture"
    batch1_root = evaluation_root / "artifacts" / "ailia_batch1"
    parity_root = evaluation_root / "artifacts" / "unity_parity"
    results_root = evaluation_root / "results"

    model_path = model_root / "l2cs.onnx"
    prototxt_path = model_root / "l2cs.onnx.prototxt"
    selfie_path = fixture_root / "selfie.png"
    reference_path = fixture_root / "output.png"

    files = {
        "onnx": verify_file(model_path, MODEL_BYTES, MODEL_SHA256, MODEL_URL),
        "prototxt": verify_file(
            prototxt_path, PROTOTXT_BYTES, PROTOTXT_SHA256, PROTOTXT_URL
        ),
        "fixed_input_image": verify_file(
            selfie_path, SELFIE_BYTES, SELFIE_SHA256, SELFIE_URL
        ),
        "ailia_reference_output": verify_file(
            reference_path, REFERENCE_BYTES, REFERENCE_SHA256, REFERENCE_URL
        ),
    }

    checker_started = time.perf_counter()
    model = onnx.load_model(model_path, load_external_data=False)
    onnx.checker.check_model(model)
    checker_seconds = time.perf_counter() - checker_started

    graph_inputs = [value_info_summary(value) for value in model.graph.input]
    graph_outputs = [value_info_summary(value) for value in model.graph.output]
    if len(graph_inputs) != 1:
        raise RuntimeError(f"Expected one graph input, received {len(graph_inputs)}")
    if graph_inputs[0]["shape"] != [6, 3, 448, 448]:
        raise RuntimeError(f"Unexpected input shape: {graph_inputs[0]['shape']}")
    if graph_inputs[0]["element_type"] != "FLOAT":
        raise RuntimeError(f"Unexpected input dtype: {graph_inputs[0]['element_type']}")
    if len(graph_outputs) != 2:
        raise RuntimeError(f"Expected two graph outputs, received {len(graph_outputs)}")
    if [value["shape"] for value in graph_outputs] != [[6], [6]]:
        raise RuntimeError(f"Unexpected output shapes: {graph_outputs}")

    # Ailia's downloadable ONNX is fixed to a six-face batch even though the
    # graph operations themselves are batch-agnostic. Create a local,
    # metadata-only batch-one derivative and prove it numerically against the
    # unmodified model below. The downloaded source model is never overwritten.
    batch1_root.mkdir(parents=True, exist_ok=True)
    batch1_model_path = batch1_root / "l2cs_batch1.onnx"
    batch1_model = onnx.load_model(model_path, load_external_data=False)
    set_first_dimension(batch1_model.graph.input[0], 1)
    for output in batch1_model.graph.output:
        set_first_dimension(output, 1)
    onnx.checker.check_model(batch1_model)
    onnx.save_model(batch1_model, batch1_model_path)
    batch1_graph_inputs = [
        value_info_summary(value) for value in batch1_model.graph.input
    ]
    batch1_graph_outputs = [
        value_info_summary(value) for value in batch1_model.graph.output
    ]
    batch1_file = {
        "path": str(batch1_model_path.resolve()),
        "bytes": batch1_model_path.stat().st_size,
        "sha256": sha256(batch1_model_path),
        "transformation": (
            "Only graph input/output first-dimension metadata changed from 6 to 1."
        ),
        "parent_sha256": MODEL_SHA256,
    }

    session_options = ort.SessionOptions()
    session_options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    load_started = time.perf_counter()
    session = ort.InferenceSession(
        str(batch1_model_path),
        sess_options=session_options,
        providers=["CPUExecutionProvider"],
    )
    session_load_seconds = time.perf_counter() - load_started
    input_name = session.get_inputs()[0].name

    height = width = 448
    x = np.linspace(0, 255, width, dtype=np.float32)
    y = np.linspace(0, 255, height, dtype=np.float32)
    xx, yy = np.meshgrid(x, y)
    gradient_rgb = np.stack((xx, yy, (xx + yy) * 0.5), axis=2).astype(np.uint8)
    asymmetric_rgb = np.zeros((height, width, 3), dtype=np.uint8)
    asymmetric_rgb[:, : width // 2, 0] = 220
    asymmetric_rgb[:, width // 2 :, 2] = 220
    asymmetric_rgb[
        height // 4 : 3 * height // 4,
        width // 3 : 2 * width // 3,
        1,
    ] = 180

    synthetic_cases = {
        "gray": run_case(
            session,
            input_name,
            np.full((height, width, 3), 127, dtype=np.uint8),
        ),
        "gradient": run_case(session, input_name, gradient_rgb),
        "gradient_mirrored": run_case(
            session, input_name, np.ascontiguousarray(gradient_rgb[:, ::-1])
        ),
        "asymmetric": run_case(session, input_name, asymmetric_rgb),
        "asymmetric_mirrored": run_case(
            session, input_name, np.ascontiguousarray(asymmetric_rgb[:, ::-1])
        ),
    }
    if not all(case["finite"] for case in synthetic_cases.values()):
        raise RuntimeError("One or more fixed synthetic cases produced non-finite output")

    equivalence_tensor = preprocess_rgb(gradient_rgb)
    derived_equivalence_outputs = session.run(None, {input_name: equivalence_tensor})
    original_session = ort.InferenceSession(
        str(model_path),
        sess_options=session_options,
        providers=["CPUExecutionProvider"],
    )
    original_input_name = original_session.get_inputs()[0].name
    original_batch_tensor = np.repeat(equivalence_tensor, 6, axis=0)
    original_equivalence_outputs = original_session.run(
        None, {original_input_name: original_batch_tensor}
    )
    maximum_batch1_difference = 0.0
    maximum_repeated_batch_difference = 0.0
    for original, derived in zip(
        original_equivalence_outputs, derived_equivalence_outputs
    ):
        original_array = np.asarray(original)
        derived_array = np.asarray(derived)
        maximum_batch1_difference = max(
            maximum_batch1_difference,
            float(np.max(np.abs(original_array[0:1] - derived_array))),
        )
        maximum_repeated_batch_difference = max(
            maximum_repeated_batch_difference,
            float(np.max(np.abs(original_array - original_array[0]))),
        )
    del original_session
    gc.collect()
    if maximum_batch1_difference > 1e-4:
        raise RuntimeError(
            "Batch-one metadata derivative does not match the source ONNX: "
            f"{maximum_batch1_difference}"
        )
    if maximum_repeated_batch_difference > 1e-4:
        raise RuntimeError(
            "Unmodified six-item batch is not repeatable across identical inputs: "
            f"{maximum_repeated_batch_difference}"
        )

    selfie_bgr = cv2.imread(str(selfie_path), cv2.IMREAD_COLOR)
    reference_bgr = cv2.imread(str(reference_path), cv2.IMREAD_COLOR)
    if selfie_bgr is None or reference_bgr is None:
        raise RuntimeError("OpenCV could not decode the fixed Ailia fixture images")
    if selfie_bgr.shape != reference_bgr.shape:
        raise RuntimeError(
            f"Fixture size mismatch: {selfie_bgr.shape} vs {reference_bgr.shape}"
        )

    x_min, y_min, x_max, y_max = largest_green_rectangle(
        selfie_bgr, reference_bgr
    )
    face_bgr = selfie_bgr[y_min:y_max, x_min:x_max]
    if face_bgr.size == 0:
        raise RuntimeError("Recovered Ailia face crop is empty")
    face_rgb = cv2.cvtColor(face_bgr, cv2.COLOR_BGR2RGB)
    mirrored_face_rgb = np.ascontiguousarray(face_rgb[:, ::-1])

    face_case = run_case(session, input_name, face_rgb)
    mirrored_face_case = run_case(session, input_name, mirrored_face_rgb)
    face_width = float(x_max - x_min)
    origin = np.asarray(
        [x_min + face_width / 2.0, y_min + (y_max - y_min) / 2.0],
        dtype=np.float64,
    )
    predicted_vector = gaze_vector(
        face_case["pitch_degrees"], face_case["yaw_degrees"], face_width
    )
    mirrored_vector = gaze_vector(
        mirrored_face_case["pitch_degrees"],
        mirrored_face_case["yaw_degrees"],
        face_width,
    )
    expected_vector = reference_arrow_vector(
        selfie_bgr, reference_bgr, origin, face_width
    )
    direction_cosine = vector_cosine(predicted_vector, expected_vector)
    if direction_cosine < 0.90:
        raise RuntimeError(
            "ONNX output order/sign does not align with the Ailia reference arrow: "
            f"cosine={direction_cosine:.6f}"
        )

    mirror_horizontal_reversal = bool(
        abs(predicted_vector[0]) < face_width * 0.03
        or abs(mirrored_vector[0]) < face_width * 0.03
        or np.sign(predicted_vector[0]) != np.sign(mirrored_vector[0])
    )

    parity_tensor = preprocess_rgb(face_rgb)
    parity_outputs = session.run(None, {input_name: parity_tensor})
    repeated_outputs = session.run(None, {input_name: parity_tensor})
    maximum_repeat_difference = max(
        float(np.max(np.abs(np.asarray(left) - np.asarray(right))))
        for left, right in zip(parity_outputs, repeated_outputs)
    )
    if maximum_repeat_difference > 1e-6:
        raise RuntimeError(
            f"Fixed-input CPU output is not repeatable: {maximum_repeat_difference}"
        )

    parity_root.mkdir(parents=True, exist_ok=True)
    parity_tensor.astype("<f4").tofile(parity_root / "input_nchw_float32.bin")
    for index, value in enumerate(parity_outputs):
        np.asarray(value, dtype="<f4").tofile(
            parity_root / f"expected_output_{index}_float32.bin"
        )
    cv2.imwrite(str(parity_root / "face_crop_rgb.png"), cv2.cvtColor(face_rgb, cv2.COLOR_RGB2BGR))

    benchmark_result = benchmark(
        session,
        input_name,
        parity_tensor,
        warmup=args.warmup,
        iterations=args.iterations,
    )

    report = {
        "status": "passed",
        "scope": (
            "Third-party converted ONNX technical feasibility only; "
            "not real-person gaze accuracy."
        ),
        "required_disclosure": [
            "Official Google Drive checkpoint unavailable.",
            "Third-party converted ONNX evaluated.",
            "Original PyTorch checkpoint equivalence not verified.",
        ],
        "source": {
            "ailia_repository": "https://github.com/ailia-ai/ailia-models",
            "ailia_commit": AILIA_COMMIT,
            "ailia_sample_path": "face_recognition/l2cs_net",
            "luxonis_model_card": (
                "https://models.luxonis.com/luxonis/l2cs-net/"
                "7051c9d2-78a4-420b-91a8-2d40ecf958dd"
            ),
            "license_declaration": "MIT; copyright (c) 2022 Ahmed Abdelrahman",
            "conversion_limit": (
                "Ailia does not publish the source checkpoint filename/hash or "
                "a complete conversion recipe in this sample."
            ),
        },
        "files": files,
        "derived_batch1_file": batch1_file,
        "environment": {
            "python": sys.version,
            "platform": platform.platform(),
            "processor": platform.processor(),
            "onnx": onnx.__version__,
            "onnxruntime": ort.__version__,
            "opencv": cv2.__version__,
            "numpy": np.__version__,
            "psutil": psutil.__version__,
            "available_ort_providers": ort.get_available_providers(),
            "selected_ort_providers": session.get_providers(),
        },
        "source_model": {
            "checker_passed": True,
            "checker_seconds": checker_seconds,
            "ir_version": model.ir_version,
            "producer_name": model.producer_name,
            "producer_version": model.producer_version,
            "domain": model.domain,
            "model_version": model.model_version,
            "opsets": [
                {"domain": opset.domain, "version": opset.version}
                for opset in model.opset_import
            ],
            "node_count": len(model.graph.node),
            "initializer_count": len(model.graph.initializer),
            "inputs": graph_inputs,
            "outputs": graph_outputs,
            "fixed_batch_observation": (
                "The Ailia download is fixed to batch 6, unlike the Luxonis "
                "model-card batch-1 description."
            ),
        },
        "runtime_model": {
            "inputs": batch1_graph_inputs,
            "outputs": batch1_graph_outputs,
            "runtime_inputs": [
                {
                    "name": value.name,
                    "shape": value.shape,
                    "type": value.type,
                }
                for value in session.get_inputs()
            ],
            "runtime_outputs": [
                {
                    "name": value.name,
                    "shape": value.shape,
                    "type": value.type,
                }
                for value in session.get_outputs()
            ],
            "session_load_seconds": session_load_seconds,
            "batch1_equivalence": {
                "source_input_shape": [6, 3, 448, 448],
                "derived_input_shape": [1, 3, 448, 448],
                "maximum_source_vs_derived_difference": maximum_batch1_difference,
                "maximum_difference_across_six_repeated_source_inputs": (
                    maximum_repeated_batch_difference
                ),
            },
        },
        "preprocess": {
            "crop": "full face rectangle",
            "source_color_order": "BGR from OpenCV, converted to RGB",
            "resize": [448, 448],
            "layout": "NCHW",
            "dtype": "float32",
            "scale": "RGB uint8 / 255.0",
            "mean": IMAGENET_MEAN.tolist(),
            "std": IMAGENET_STD.tolist(),
            "interpolation": "OpenCV resize default (INTER_LINEAR)",
        },
        "postprocess": {
            "output_0": "pitch scalar in degrees",
            "output_1": "yaw scalar in degrees",
            "radians": "degrees * pi / 180",
            "ailia_screen_vector": {
                "dx": "-L * sin(pitch) * cos(yaw)",
                "dy": "-L * sin(yaw)",
            },
            "confidence": (
                "No model confidence output. Use existing MediaPipe face validity."
            ),
        },
        "fixed_cases": synthetic_cases,
        "ailia_fixture": {
            "recovered_face_box_xyxy": [x_min, y_min, x_max, y_max],
            "output_0_pitch_degrees": face_case["pitch_degrees"],
            "output_1_yaw_degrees": face_case["yaw_degrees"],
            "predicted_screen_vector": predicted_vector.tolist(),
            "reference_arrow_vector": expected_vector.tolist(),
            "direction_cosine": direction_cosine,
            "mirrored_pitch_degrees": mirrored_face_case["pitch_degrees"],
            "mirrored_yaw_degrees": mirrored_face_case["yaw_degrees"],
            "mirrored_screen_vector": mirrored_vector.tolist(),
            "mirror_horizontal_reversal": mirror_horizontal_reversal,
            "maximum_repeat_difference": maximum_repeat_difference,
        },
        "benchmark_cpu": benchmark_result,
        "gpu_benchmark": {
            "status": "not_run",
            "reason": (
                "Only onnxruntime CPU is installed. No CUDA runtime, driver, "
                "or PyTorch download was added for this evaluation."
            ),
        },
        "unity_parity_fixture": {
            "directory": str(parity_root.resolve()),
            "input_file": "input_nchw_float32.bin",
            "input_shape": [1, 3, 448, 448],
            "expected_output_files": [
                "expected_output_0_float32.bin",
                "expected_output_1_float32.bin",
            ],
            "output_names": [value.name for value in session.get_outputs()],
        },
    }

    write_json(results_root / "onnx_technical_report.json", report)
    write_json(parity_root / "fixture_metadata.json", report["unity_parity_fixture"])
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
