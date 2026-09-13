using System.Management;

namespace AllyAutoTDP.Hardware;

public sealed record AllyDetectionResult(
    string Model,
    bool IsAllyFamily,
    bool WriteAuthorized,
    string? ErrorMessage);

public sealed class AllyDetector
{
    public AllyDetectionResult Detect()
    {
        try
        {
            string model = ReadModel();
            if (string.IsNullOrWhiteSpace(model))
                model = "UNKNOWN";

            bool isAllyFamily = IsAllyFamilyModel(model);
            bool writeAuthorized = IsWriteAuthorizedModel(model);
            return new AllyDetectionResult(model, isAllyFamily, writeAuthorized, null);
        }
        catch (Exception ex)
        {
            return new AllyDetectionResult("UNKNOWN", false, false, ex.Message);
        }
    }

    public static bool IsAllyFamilyModel(string model) =>
        !string.IsNullOrWhiteSpace(model) &&
        model.Contains("RC7", StringComparison.OrdinalIgnoreCase);

    public static bool IsWriteAuthorizedModel(string model) =>
        !string.IsNullOrWhiteSpace(model) &&
        model.Contains("RC71", StringComparison.OrdinalIgnoreCase);

    private static string ReadModel()
    {
        using var searcher = new ManagementObjectSearcher("Select * from Win32_ComputerSystem");
        foreach (ManagementObject obj in searcher.Get())
        {
            using (obj)
                return obj["Model"]?.ToString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }
}
