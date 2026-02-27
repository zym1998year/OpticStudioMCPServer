using System.ComponentModel;
using ModelContextProtocol.Server;
using ZemaxMCP.Core.Models;
using ZemaxMCP.Core.Session;

namespace ZemaxMCP.Server.Tools.LensData;

[McpServerToolType]
public class SetFieldsTool
{
    private readonly IZemaxSession _session;

    public SetFieldsTool(IZemaxSession session) => _session = session;

    public record FieldDefinition(double X, double Y, double Weight = 1.0);

    public record SetFieldsResult(
        bool Success,
        string? Error,
        int NumberOfFields,
        List<Field> Fields
    );

    [McpServerTool(Name = "zemax_set_fields")]
    [Description("Set field point values. Automatically adds fields if needed.")]
    public async Task<SetFieldsResult> ExecuteAsync(
        [Description("Array of field definitions [{x, y, weight}]")] List<FieldDefinition> fields,
        [Description("Field type: Angle, ObjectHeight, ParaxialImageHeight, RealImageHeight")] string fieldType = "Angle")
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                ["fieldCount"] = fields.Count,
                ["fieldType"] = fieldType
            };

            var result = await _session.ExecuteAsync("SetFields", parameters, system =>
            {
                var sysFields = system.SystemData.Fields;

                // Set field type
                var fType = fieldType.ToLower() switch
                {
                    "angle" => ZOSAPI.SystemData.FieldType.Angle,
                    "objectheight" => ZOSAPI.SystemData.FieldType.ObjectHeight,
                    "paraxialimageheight" => ZOSAPI.SystemData.FieldType.ParaxialImageHeight,
                    "realimageheight" => ZOSAPI.SystemData.FieldType.RealImageHeight,
                    _ => ZOSAPI.SystemData.FieldType.Angle
                };
                sysFields.SetFieldType(fType);

                // Add fields if needed
                while (sysFields.NumberOfFields < fields.Count)
                {
                    sysFields.AddField(0, 0, 1.0);
                }

                // Remove excess fields if needed
                while (sysFields.NumberOfFields > fields.Count)
                {
                    sysFields.RemoveField(sysFields.NumberOfFields);
                }

                // Configure all fields
                var resultFields = new List<Field>();
                for (int i = 0; i < fields.Count; i++)
                {
                    var field = sysFields.GetField(i + 1);
                    field.X = fields[i].X;
                    field.Y = fields[i].Y;
                    field.Weight = fields[i].Weight;

                    resultFields.Add(new Field
                    {
                        Number = i + 1,
                        X = field.X,
                        Y = field.Y,
                        Weight = field.Weight
                    });
                }

                return new SetFieldsResult(
                    Success: true,
                    Error: null,
                    NumberOfFields: sysFields.NumberOfFields,
                    Fields: resultFields
                );
            });

            return result;
        }
        catch (Exception ex)
        {
            return new SetFieldsResult(false, ex.Message, 0, new List<Field>());
        }
    }
}
