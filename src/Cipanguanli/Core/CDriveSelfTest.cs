using System.IO;
using System.Text.Json;

namespace Cipanguanli.Core;

internal static class CDriveSelfTest
{
    public static async Task<bool> RunAsync(string reportPath)
    {
        try
        {
            var analyzer = new CDriveAnalyzer();
            var result = await analyzer.AnalyzeAsync("C:\\", includeSystemCommands: false);
            var plan = CDriveTargetPlanner.Build(result.Findings, 1L * 1024 * 1024 * 1024);
            var checks = new Dictionary<string, bool>
            {
                ["drive_capacity_detected"] = result.TotalBytes > 0 && result.UsedBytes >= 0 && result.FreeBytes >= 0,
                ["findings_created"] = result.Findings.Count > 0,
                ["finding_sizes_valid"] = result.Findings.All(x => x.SizeBytes >= 0 && x.ReclaimableBytes >= 0 && x.ReclaimableBytes <= x.SizeBytes),
                ["safety_scores_valid"] = result.Findings.All(x => x.SafetyScore is >= 0 and <= 100),
                ["protected_never_reclaimable"] = result.Findings.Where(x => x.IsProtected).All(x => x.ReclaimableBytes == 0),
                ["target_plan_excludes_protected"] = plan.Items.All(x => !x.IsProtected),
                ["system_drive_path_detection"] = CDriveWriteMonitor.IsSystemDrivePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "probe.tmp"))
            };

            var payload = new
            {
                passed = checks.Values.All(v => v),
                checks,
                drive = new { result.Root, result.TotalBytes, result.UsedBytes, result.FreeBytes },
                findingCount = result.Findings.Count,
                appDataCount = result.AppDataEntries.Count,
                virtualDiskCount = result.VirtualDisks.Count,
                topFindings = result.Findings.Take(20).Select(x => new
                {
                    x.Name,
                    x.Category,
                    x.SizeBytes,
                    x.ReclaimableBytes,
                    x.SafetyScore,
                    x.IsProtected
                }).ToArray()
            };
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return checks.Values.All(v => v);
        }
        catch (Exception ex)
        {
            try
            {
                await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
            return false;
        }
    }
}
