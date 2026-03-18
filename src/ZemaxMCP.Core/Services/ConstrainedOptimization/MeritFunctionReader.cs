using ZemaxMCP.Core.Models;
using ZOSAPI;
using ZOSAPI.Editors.MFE;

namespace ZemaxMCP.Core.Services.ConstrainedOptimization;

public class MeritFunctionReader
{
    public List<MeritRow> ReadMeritRows(IOpticalSystem system)
    {
        var rows = new List<MeritRow>();
        IMeritFunctionEditor mfe = system.MFE;
        int numRows = mfe.NumberOfRows;

        for (int i = 1; i <= numRows; i++)
        {
            IMFERow row = (IMFERow)mfe.GetRowAt(i);
            double weight = row.Weight;
            double value = row.Value;
            double target = row.Target;
            string typeName = row.Type.ToString();

            if (weight > 0
                && !double.IsNaN(weight) && !double.IsInfinity(weight)
                && !double.IsNaN(value) && !double.IsInfinity(value)
                && !double.IsNaN(target) && !double.IsInfinity(target))
            {
                rows.Add(new MeritRow
                {
                    RowNumber = i,
                    TypeName = typeName,
                    Target = target,
                    Value = value,
                    Weight = weight
                });
            }
        }

        return rows;
    }
}
