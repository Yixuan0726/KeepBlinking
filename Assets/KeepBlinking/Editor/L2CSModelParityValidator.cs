using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KeepBlinking.Editor
{
  [InitializeOnLoad]
  public static class L2CSModelParityValidator
  {
    private const string ModelAssetPath = "Assets/KeepBlinking/Resources/L2CSExperimental/l2cs_batch1.onnx";
    private const string FixtureDirectory = "Tools/L2CSNetEvaluation/artifacts/unity_parity";
    private const string ReportRelativePath = "Tools/L2CSNetEvaluation/results/unity_parity_report.json";
    private const int BenchmarkIterations = 8;
    private static bool _automaticAttempted;

    static L2CSModelParityValidator()
    {
      EditorApplication.delayCall += TryRunAutomatically;
    }

    [MenuItem("KeepBlinking/Diagnostics/Run L2CS ONNX Parity")]
    public static void RunFromMenu()
    {
      RunValidation(true);
    }

    private static void TryRunAutomatically()
    {
      if (_automaticAttempted || EditorApplication.isPlayingOrWillChangePlaymode)
      {
        return;
      }

      _automaticAttempted = true;
      var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
      var reportPath = Path.Combine(projectRoot ?? string.Empty, ReportRelativePath);
      var modelPath = Path.Combine(projectRoot ?? string.Empty, ModelAssetPath);
      var inputPath = Path.Combine(projectRoot ?? string.Empty, FixtureDirectory, "input_nchw_float32.bin");
      if (File.Exists(reportPath) && File.GetLastWriteTimeUtc(reportPath) >= File.GetLastWriteTimeUtc(modelPath) &&
          File.GetLastWriteTimeUtc(reportPath) >= File.GetLastWriteTimeUtc(inputPath))
      {
        return;
      }

      RunValidation(false);
    }

    private static void RunValidation(bool logMissingInputs)
    {
      var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
      if (string.IsNullOrEmpty(projectRoot))
      {
        return;
      }

      var inputPath = Path.Combine(projectRoot, FixtureDirectory, "input_nchw_float32.bin");
      var expectedPitchPath = Path.Combine(projectRoot, FixtureDirectory, "expected_output_0_float32.bin");
      var expectedYawPath = Path.Combine(projectRoot, FixtureDirectory, "expected_output_1_float32.bin");
      var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelAssetPath);
      if (modelAsset == null || !File.Exists(inputPath) || !File.Exists(expectedPitchPath) || !File.Exists(expectedYawPath))
      {
        if (logMissingInputs)
        {
          Debug.LogError("L2CS parity validation inputs are missing or the ONNX asset has not finished importing.");
        }
        return;
      }

      var report = new ParityReport
      {
        createdUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        unityVersion = Application.unityVersion,
        inferenceEngineVersion = "2.4.1",
        modelAssetPath = ModelAssetPath,
        inputShape = "[1,3,448,448]",
        outputNames = "516=pitch degrees, 523=yaw degrees",
        graphicsDevice = SystemInfo.graphicsDeviceName,
        graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
      };

      try
      {
        var input = ReadFloatArray(inputPath);
        var expectedPitch = ReadFloatArray(expectedPitchPath)[0];
        var expectedYaw = ReadFloatArray(expectedYawPath)[0];
        var loadWatch = Stopwatch.StartNew();
        var model = ModelLoader.Load(modelAsset);
        loadWatch.Stop();
        report.modelLoadMilliseconds = (float)loadWatch.Elapsed.TotalMilliseconds;
        report.onnxRuntimePitchDegrees = expectedPitch;
        report.onnxRuntimeYawDegrees = expectedYaw;

        report.cpu = RunBackend(model, BackendType.CPU, input, expectedPitch, expectedYaw);
        if (SystemInfo.supportsComputeShaders)
        {
          report.gpuCompute = RunBackend(model, BackendType.GPUCompute, input, expectedPitch, expectedYaw);
        }
        else
        {
          report.gpuCompute = new BackendReport { backend = "GPUCompute", succeeded = false, failureReason = "Compute shaders are unavailable." };
        }

        report.maximumCpuGpuDifferenceDegrees = report.cpu.succeeded && report.gpuCompute.succeeded
          ? Mathf.Max(Mathf.Abs(report.cpu.pitchDegrees - report.gpuCompute.pitchDegrees), Mathf.Abs(report.cpu.yawDegrees - report.gpuCompute.yawDegrees))
          : -1f;
        report.passed = report.cpu.succeeded && report.cpu.maximumOnnxRuntimeDifferenceDegrees <= 0.01f &&
                        (!report.gpuCompute.succeeded || report.gpuCompute.maximumOnnxRuntimeDifferenceDegrees <= 0.01f);
        report.note = "Fixed preprocessed input comparison only; camera direction and mirror checks require normal Play Mode.";
      }
      catch (Exception exception)
      {
        report.passed = false;
        report.failureReason = exception.ToString();
      }

      var reportPath = Path.Combine(projectRoot, ReportRelativePath);
      Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? projectRoot);
      File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
      if (report.passed)
      {
        Debug.Log($"L2CS ONNX parity passed. Report: {reportPath}");
      }
      else
      {
        Debug.LogError($"L2CS ONNX parity did not pass. Report: {reportPath}");
      }
    }

    private static BackendReport RunBackend(Model model, BackendType backendType, float[] inputData, float expectedPitch, float expectedYaw)
    {
      var result = new BackendReport { backend = backendType.ToString() };
      try
      {
        using var input = new Tensor<float>(new TensorShape(1, 3, 448, 448), inputData);
        using var worker = new Worker(model, backendType);
        RunOnce(worker, input, out _, out _);

        var timings = new List<float>(BenchmarkIterations);
        for (var i = 0; i < BenchmarkIterations; i++)
        {
          var watch = Stopwatch.StartNew();
          RunOnce(worker, input, out var pitch, out var yaw);
          watch.Stop();
          timings.Add((float)watch.Elapsed.TotalMilliseconds);
          result.pitchDegrees = pitch;
          result.yawDegrees = yaw;
        }

        timings.Sort();
        result.succeeded = IsFinite(result.pitchDegrees) && IsFinite(result.yawDegrees);
        result.averageLatencyMilliseconds = timings.Average();
        result.p50LatencyMilliseconds = Percentile(timings, 0.50f);
        result.p95LatencyMilliseconds = Percentile(timings, 0.95f);
        result.effectiveFps = result.averageLatencyMilliseconds > 0f ? 1000f / result.averageLatencyMilliseconds : 0f;
        result.pitchOnnxRuntimeDifferenceDegrees = Mathf.Abs(result.pitchDegrees - expectedPitch);
        result.yawOnnxRuntimeDifferenceDegrees = Mathf.Abs(result.yawDegrees - expectedYaw);
        result.maximumOnnxRuntimeDifferenceDegrees = Mathf.Max(result.pitchOnnxRuntimeDifferenceDegrees, result.yawOnnxRuntimeDifferenceDegrees);
      }
      catch (Exception exception)
      {
        result.succeeded = false;
        result.failureReason = exception.ToString();
      }

      return result;
    }

    private static void RunOnce(Worker worker, Tensor<float> input, out float pitch, out float yaw)
    {
      worker.Schedule(input);
      var pitchOutput = worker.PeekOutput("516") as Tensor<float>;
      var yawOutput = worker.PeekOutput("523") as Tensor<float>;
      if (pitchOutput == null || yawOutput == null)
      {
        throw new InvalidOperationException("Expected L2CS outputs 516 and 523 were not found.");
      }

      using var pitchCpu = pitchOutput.ReadbackAndClone();
      using var yawCpu = yawOutput.ReadbackAndClone();
      pitch = pitchCpu[0];
      yaw = yawCpu[0];
    }

    private static float[] ReadFloatArray(string path)
    {
      var bytes = File.ReadAllBytes(path);
      if (bytes.Length % sizeof(float) != 0)
      {
        throw new InvalidDataException($"Float fixture has an invalid byte length: {path}");
      }

      var values = new float[bytes.Length / sizeof(float)];
      Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
      return values;
    }

    private static float Percentile(IReadOnlyList<float> sorted, float percentile)
    {
      if (sorted.Count == 0) return 0f;
      var position = Mathf.Clamp01(percentile) * (sorted.Count - 1);
      var lower = Mathf.FloorToInt(position);
      var upper = Mathf.CeilToInt(position);
      return Mathf.Lerp(sorted[lower], sorted[upper], position - lower);
    }

    private static bool IsFinite(float value)
    {
      return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [Serializable]
    private sealed class ParityReport
    {
      public string createdUtc;
      public string unityVersion;
      public string inferenceEngineVersion;
      public string modelAssetPath;
      public string inputShape;
      public string outputNames;
      public string graphicsDevice;
      public string graphicsDeviceType;
      public float modelLoadMilliseconds;
      public float onnxRuntimePitchDegrees;
      public float onnxRuntimeYawDegrees;
      public BackendReport cpu;
      public BackendReport gpuCompute;
      public float maximumCpuGpuDifferenceDegrees;
      public bool passed;
      public string failureReason;
      public string note;
    }

    [Serializable]
    private sealed class BackendReport
    {
      public string backend;
      public bool succeeded;
      public float pitchDegrees;
      public float yawDegrees;
      public float pitchOnnxRuntimeDifferenceDegrees;
      public float yawOnnxRuntimeDifferenceDegrees;
      public float maximumOnnxRuntimeDifferenceDegrees;
      public float averageLatencyMilliseconds;
      public float p50LatencyMilliseconds;
      public float p95LatencyMilliseconds;
      public float effectiveFps;
      public string failureReason;
    }
  }
}
