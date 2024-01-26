using System.Text.Json;

using Microsoft.Build.Framework;

namespace Squeel;

public class ReadSqueelJsonFile : Microsoft.Build.Utilities.Task
{
    [Required]
    public required string JsonFilePath { get; set; }

    [Output]
    public required string ConnectionString { get; set; }

    public override bool Execute()
    {
        try
        {
            Log.LogMessage(MessageImportance.High, $"Reading JSON file: {JsonFilePath}");

            if (!File.Exists(JsonFilePath))
            {
                Log.LogError($"File not found: {JsonFilePath}");
                return false;
            }

            string jsonContent = File.ReadAllText(JsonFilePath);
            using JsonDocument doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("connectionstring", out JsonElement connectionStringElement))
            {
                ConnectionString = connectionStringElement.GetString() ?? "";
                Log.LogMessage(MessageImportance.High, $"ConnectionString: {ConnectionString}");
                return true;
            }
            else
            {
                Log.LogError($"'connectionstring' property not found in JSON file.");
                return false;
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }
}
