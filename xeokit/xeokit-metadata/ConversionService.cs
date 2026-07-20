using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XeokitMetadata {

  public class ConversionResult {
    public string InputPath { get; set; }
    public string OutputPath { get; set; }
    public bool Success { get; set; }
    public string Error { get; set; }
    public TimeSpan Duration { get; set; }
    public long MemoryUsedMb { get; set; }
  }

  /// <summary>
  ///   Handles concurrent IFC-to-JSON conversions with controlled parallelism.
  ///   Uses SemaphoreSlim to limit the number of simultaneous conversions,
  ///   preventing excessive memory usage on the server.
  /// </summary>
  public class ConversionService {
    private readonly int _maxConcurrent;
    private readonly bool _useStreaming;

    /// <param name="maxConcurrent">
    ///   Maximum number of files to convert simultaneously.
    ///   Rule of thumb: floor(Available RAM in GB / 2) since each conversion
    ///   uses ~1.5-2 GB for large IFC files.
    /// </param>
    /// <param name="useStreaming">
    ///   If true, uses streaming JSON serializer (lower RAM, recommended).
    /// </param>
    public ConversionService(int maxConcurrent = 2, bool useStreaming = true) {
      _maxConcurrent = Math.Max(1, maxConcurrent);
      _useStreaming = useStreaming;
    }

    /// <summary>
    ///   Converts a single IFC file to metadata JSON.
    /// </summary>
    public ConversionResult ConvertSingle(string ifcPath, string jsonPath) {
      var stopwatch = Stopwatch.StartNew();
      var process = Process.GetCurrentProcess();

      try {
        if (!File.Exists(ifcPath)) {
          return new ConversionResult {
            InputPath = ifcPath,
            OutputPath = jsonPath,
            Success = false,
            Error = $"IFC file not found: {ifcPath}"
          };
        }

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir)) {
          Directory.CreateDirectory(outputDir);
        }

        var metaModel = MetaModel.fromIfc(ifcPath);

        if (_useStreaming) {
          metaModel.toJsonStreaming(jsonPath);
        } else {
          metaModel.toJson(jsonPath);
        }

        // Force GC after each conversion to reclaim xbim memory promptly
        metaModel = default;
        GC.Collect();
        GC.WaitForPendingFinalizers();

        stopwatch.Stop();

        return new ConversionResult {
          InputPath = ifcPath,
          OutputPath = jsonPath,
          Success = true,
          Duration = stopwatch.Elapsed,
          MemoryUsedMb = process.WorkingSet64 / (1024 * 1024)
        };
      } catch (Exception ex) {
        stopwatch.Stop();
        return new ConversionResult {
          InputPath = ifcPath,
          OutputPath = jsonPath,
          Success = false,
          Error = ex.Message,
          Duration = stopwatch.Elapsed
        };
      }
    }

    /// <summary>
    ///   Converts multiple IFC files concurrently with controlled parallelism.
    /// </summary>
    /// <param name="ifcFiles">Dictionary of IFC path → output JSON path.</param>
    /// <param name="progress">Optional progress callback (completed count).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>List of conversion results.</returns>
    public async Task<List<ConversionResult>> ConvertBatchAsync(
      Dictionary<string, string> ifcFiles,
      IProgress<int> progress = null,
      CancellationToken cancellationToken = default) {

      var results = new List<ConversionResult>();
      var completedCount = 0;
      var totalFiles = ifcFiles.Count;

      Console.WriteLine(
        $"Starting batch conversion: {totalFiles} files, max {_maxConcurrent} concurrent");

      using var semaphore = new SemaphoreSlim(_maxConcurrent, _maxConcurrent);

      var tasks = ifcFiles.Select(async entry => {
        var ifcPath = entry.Key;
        var jsonPath = entry.Value;

        await semaphore.WaitAsync(cancellationToken);
        try {
          var result = await Task.Run(() => {
            Console.WriteLine(
              $"[{Interlocked.Increment(ref completedCount)}/{totalFiles}] " +
              $"Converting: {Path.GetFileName(ifcPath)}");
            return ConvertSingle(ifcPath, jsonPath);
          }, cancellationToken);

          lock (results) { results.Add(result); }

          progress?.Report(completedCount);

          if (result.Success) {
            Console.WriteLine(
              $"  ✓ Done: {Path.GetFileName(ifcPath)} " +
              $"({result.Duration.TotalSeconds:F1}s, " +
              $"{result.MemoryUsedMb} MB)");
          } else {
            Console.WriteLine(
              $"  ✗ Failed: {Path.GetFileName(ifcPath)} - {result.Error}");
          }
        } finally {
          semaphore.Release();
        }
      });

      await Task.WhenAll(tasks);

      Console.WriteLine(
        $"Batch complete: {results.Count(r => r.Success)}/{totalFiles} succeeded");

      return results;
    }

    /// <summary>
    ///   Scans a directory for IFC files and builds the conversion map.
    /// </summary>
    /// <param name="inputDir">Directory containing IFC files.</param>
    /// <param name="outputDir">Directory for output JSON files.</param>
    /// <param name="recursive">Whether to scan subdirectories.</param>
    public static Dictionary<string, string> BuildConversionMap(
      string inputDir,
      string outputDir,
      bool recursive = false) {

      var searchOption = recursive
        ? SearchOption.AllDirectories
        : SearchOption.TopDirectoryOnly;

      var ifcFiles = Directory.GetFiles(inputDir, "*.ifc", searchOption);

      if (!Directory.Exists(outputDir)) {
        Directory.CreateDirectory(outputDir);
      }

      return ifcFiles.ToDictionary(
        f => f,
        f => Path.Combine(
          outputDir,
          Path.GetFileNameWithoutExtension(f) + ".json"));
    }
  }
}
