using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace XeokitMetadata {
  /// <summary>
  /// Command line application that extracts the hierarchical structure of
  /// building elements from an IFC file and creates a JSON output compatible
  /// with the xeokit-sdk's metadata model.
  ///
  /// Usage:
  ///   Single file:  xeokit-metadata <input.ifc> <output.json>
  ///   Batch dir:    xeokit-metadata --batch <input-dir> <output-dir> [--concurrent N]
  ///
  /// Options:
  ///   --batch           Process all .ifc files in input-dir
  ///   --concurrent N    Max simultaneous conversions (default: 2)
  ///   --no-streaming    Use legacy toJson() instead of streaming serializer
  ///   --help            Show usage
  /// </summary>
  internal static class Program {
    private static async Task<int> Main(string[] args) {
      if (args.Length == 0 || args.Contains("--help") || args.Contains("-h")) {
        PrintUsage();
        return 0;
      }

      try {
        // Parse options
        var useStreaming = !args.Contains("--no-streaming");
        var isBatch = args.Contains("--batch");
        var concurrentIndex = Array.IndexOf(args, "--concurrent");
        var maxConcurrent = concurrentIndex >= 0 && concurrentIndex + 1 < args.Length
          ? int.Parse(args[concurrentIndex + 1])
          : Environment.ProcessorCount / 2; // default: half of CPU cores

        maxConcurrent = Math.Max(1, maxConcurrent);

        var service = new ConversionService(
          maxConcurrent: maxConcurrent,
          useStreaming: useStreaming);

        if (isBatch) {
          return await RunBatch(service, args);
        }
        return RunSingle(service, args);
      }
      catch (Exception ex) {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
      }
    }

    private static int RunSingle(ConversionService service, string[] args) {
      if (args.Length < 2) {
        Console.Error.WriteLine("Single file mode requires: <input.ifc> <output.json>");
        return 1;
      }

      var ifcPath = args[0];
      var jsonPath = args[1];

      if (!File.Exists(ifcPath)) {
        Console.Error.WriteLine($"IFC file not found: {ifcPath}");
        return 1;
      }

      var fileSizeMb = new FileInfo(ifcPath).Length / (1024.0 * 1024.0);
      var startTime = DateTime.Now;

      Console.WriteLine($"[{startTime:HH:mm:ss}] Starting conversion: {Path.GetFileName(ifcPath)} ({fileSizeMb:F1} MB)");

      var result = service.ConvertSingle(ifcPath, jsonPath);

      var endTime = DateTime.Now;
      var elapsed = endTime - startTime;

      if (result.Success) {
        Console.WriteLine(
          $"[{endTime:HH:mm:ss}] Completed: {Path.GetFileName(ifcPath)} " +
          $"in {elapsed.TotalSeconds:F1}s " +
          $"({result.MemoryUsedMb} MB RAM)");
        return 0;
      }

      Console.Error.WriteLine(
        $"[{endTime:HH:mm:ss}] Failed: {Path.GetFileName(ifcPath)} - {result.Error}");
      return 1;
    }

    private static async Task<int> RunBatch(ConversionService service, string[] args) {
      // Filter out option flags to get positional args
      var positional = args.Where(a =>
        !a.StartsWith("--") && !int.TryParse(a, out _))
        .ToArray();

      if (positional.Length < 2) {
        Console.Error.WriteLine(
          "Batch mode requires: --batch <input-dir> <output-dir>");
        return 1;
      }

      var inputDir = positional[0];
      var outputDir = positional[1];

      if (!Directory.Exists(inputDir)) {
        Console.Error.WriteLine($"Input directory not found: {inputDir}");
        return 1;
      }

      var conversionMap = ConversionService.BuildConversionMap(
        inputDir, outputDir, recursive: true);

      if (conversionMap.Count == 0) {
        Console.WriteLine("No .ifc files found in input directory.");
        return 0;
      }

      Console.WriteLine(
        $"Found {conversionMap.Count} IFC files in {inputDir}");

      var batchStartTime = DateTime.Now;
      Console.WriteLine($"[{batchStartTime:HH:mm:ss}] Starting batch conversion ({conversionMap.Count} files)");

      var progress = new Progress<int>(count => { });
      var results = await service.ConvertBatchAsync(conversionMap, progress);

      var batchEndTime = DateTime.Now;
      var batchElapsed = batchEndTime - batchStartTime;

      // Summary
      Console.WriteLine($"\n[{batchEndTime:HH:mm:ss}] --- Summary ---");
      var succeeded = results.Where(r => r.Success).ToList();
      var failed = results.Where(r => !r.Success).ToList();

      Console.WriteLine(
        $"Succeeded: {succeeded.Count}, Failed: {failed.Count}");
      Console.WriteLine(
        $"Total time: {batchElapsed.TotalMinutes:F1} min ({batchElapsed.TotalSeconds:F0}s)");

      if (failed.Any()) {
        Console.WriteLine("\nFailed files:");
        foreach (var f in failed) {
          Console.WriteLine($"  {Path.GetFileName(f.InputPath)}: {f.Error}");
        }
      }

      return failed.Count > 0 ? 1 : 0;
    }

    private static void PrintUsage() {
      Console.WriteLine(@"
xeokit-metadata - IFC to xeokit metadata JSON converter

Usage:
  Single file:
    xeokit-metadata <input.ifc> <output.json>

  Batch directory:
    xeokit-metadata --batch <input-dir> <output-dir> [--concurrent N]

Options:
  --batch               Process all .ifc files in input-dir recursively
  --concurrent N        Max simultaneous conversions (default: CPU cores / 2)
  --no-streaming        Use legacy toJson() instead of streaming serializer
  --help, -h            Show this help message

Examples:
  # Convert a single file
  xeokit-metadata model.ifc output.json

  # Convert all IFC files in a directory with 4 parallel workers
  xeokit-metadata --batch ./ifc-files ./json-output --concurrent 4

  # Legacy mode (loads full JSON into memory)
  xeokit-metadata --no-streaming model.ifc output.json
");
    }
  }
}
