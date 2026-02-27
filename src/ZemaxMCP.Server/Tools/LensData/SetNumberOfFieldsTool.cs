using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class SetNumberOfFieldsTool
{
    private readonly IZemaxSession _session;

    public SetNumberOfFieldsTool(IZemaxSession session) => _session = session;

    public record SetNumberOfFieldsResult(
        bool Success,
        string? Error,
        int NumberOfFields
    );

    [McpServerTool(Name = "zemax_set_number_of_fields")]
    [Description("Set the number of field points in the system")]
    public async Task<SetNumberOfFieldsResult> ExecuteAsync(
        [Description("Number of fields to set (1-100)")] int numberOfFields)
    {
        try
        {
            if (numberOfFields < 1 || numberOfFields > 100)
            {
                return new SetNumberOfFieldsResult(
                    false,
                    "Number of fields must be between 1 and 100",
                    0);
            }

            var result = await _session.ExecuteAsync("SetNumberOfFields",
                new Dictionary<string, object?> { ["numberOfFields"] = numberOfFields },
                system =>
            {
                var sysFields = system.SystemData.Fields;
                var currentCount = sysFields.NumberOfFields;

                // Add fields if needed
                while (sysFields.NumberOfFields < numberOfFields)
                {
                    sysFields.AddField(0, 0, 1.0); // Default to on-axis
                }

                // Remove fields if needed
                while (sysFields.NumberOfFields > numberOfFields)
                {
                    sysFields.RemoveField(sysFields.NumberOfFields);
                }

                return new SetNumberOfFieldsResult(
                    Success: true,
                    Error: null,
                    NumberOfFields: sysFields.NumberOfFields
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new SetNumberOfFieldsResult(false, ex.Message, 0);
        }
    }
}
