using System.Text;
using System.Text.Json;

namespace ZemaxMCP.Documentation;

public class OperandDatabase
{
    private readonly Dictionary<string, OperandDefinition> _operands = new(StringComparer.OrdinalIgnoreCase);

    public OperandDatabase()
    {
        InitializeOperands();
    }

    public OperandDefinition? GetOperand(string name)
    {
        return _operands.TryGetValue(name, out var op) ? op : null;
    }

    public IEnumerable<OperandDefinition> GetAllOperands() => _operands.Values;

    public IEnumerable<OperandDefinition> GetByCategory(string category)
    {
        return _operands.Values.Where(o =>
            o.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<string> GetCategories()
    {
        return _operands.Values.Select(o => o.Category).Distinct().OrderBy(c => c);
    }

    public IEnumerable<SearchResult> SearchOperands(string query, int maxResults = 10)
    {
        var queryLower = query.ToLowerInvariant();
        var words = queryLower.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        return _operands.Values
            .Select(op => new SearchResult
            {
                Operand = op,
                Score = CalculateScore(op, words, queryLower)
            })
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .Take(maxResults);
    }

    private double CalculateScore(OperandDefinition op, string[] words, string fullQuery)
    {
        double score = 0;

        // Exact name match
        if (op.Name.Equals(fullQuery, StringComparison.OrdinalIgnoreCase))
            score += 100;
        // Name starts with query
        else if (op.Name.StartsWith(fullQuery, StringComparison.OrdinalIgnoreCase))
            score += 50;
        // Name contains query
        else if (op.Name.IndexOf(fullQuery, StringComparison.OrdinalIgnoreCase) >= 0)
            score += 25;

        // Check description
        var descLower = op.Description.ToLowerInvariant();
        foreach (var word in words)
        {
            if (descLower.Contains(word))
                score += 5;
        }

        // Check category
        if (op.Category.ToLowerInvariant().Contains(fullQuery))
            score += 10;

        return score;
    }

    public string GenerateFullReference()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Zemax Optimization Operand Reference");
        sb.AppendLine();

        foreach (var category in GetCategories())
        {
            sb.AppendLine($"## {category}");
            sb.AppendLine();

            foreach (var op in GetByCategory(category))
            {
                sb.AppendLine($"### {op.Name}");
                sb.AppendLine(op.Description);
                sb.AppendLine();

                if (op.Parameters.Any())
                {
                    sb.AppendLine("**Parameters:**");
                    foreach (var param in op.Parameters)
                    {
                        sb.AppendLine($"- {param.Name}: {param.Description}");
                    }
                    sb.AppendLine();
                }

                if (!string.IsNullOrEmpty(op.Example))
                {
                    sb.AppendLine("**Example:**");
                    sb.AppendLine($"```{op.Example}```");
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    public string GenerateOperandHelp(OperandDefinition op)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {op.Name}");
        sb.AppendLine();
        sb.AppendLine($"**Category:** {op.Category}");
        sb.AppendLine();
        sb.AppendLine("## Description");
        sb.AppendLine(op.Description);
        sb.AppendLine();

        if (op.Parameters.Any())
        {
            sb.AppendLine("## Parameters");
            sb.AppendLine();
            sb.AppendLine("| Parameter | Description | Default |");
            sb.AppendLine("|-----------|-------------|---------|");
            foreach (var param in op.Parameters)
            {
                sb.AppendLine($"| {param.Name} | {param.Description} | {param.DefaultValue} |");
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(op.Example))
        {
            sb.AppendLine("## Example");
            sb.AppendLine($"```{op.Example}```");
            sb.AppendLine();
        }

        if (op.RelatedOperands.Any())
        {
            sb.AppendLine("## Related Operands");
            sb.AppendLine(string.Join(", ", op.RelatedOperands));
        }

        return sb.ToString();
    }

    private void InitializeOperands()
    {
        // Aberration operands
        AddOperand(new OperandDefinition
        {
            Name = "SPHA",
            Description = "Spherical aberration in waves contributed by the surface defined by Surf at the wavelength defined by Wave. If Surf is zero, the sum for the entire system is used.",
            Category = "Aberration",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number (0 for entire system)", DefaultValue = "0" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "COMA", "ASTI", "FCUR", "DIST" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "COMA",
            Description = "Coma in waves contributed by the surface defined by Surf at the wavelength defined by Wave. If Surf is zero, the sum for the entire system is used. Third order coma from Seidel coefficients.",
            Category = "Aberration",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number (0 for entire system)", DefaultValue = "0" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "SPHA", "ASTI", "FCUR", "DIST" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "ASTI",
            Description = "Astigmatism in waves contributed by the surface at the wavelength defined by Wave. Third order astigmatism from Seidel coefficients.",
            Category = "Aberration",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number (0 for entire system)", DefaultValue = "0" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "SPHA", "COMA", "FCUR", "DIST" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "FCUR",
            Description = "Field curvature in waves contributed by the surface at the wavelength defined by Wave.",
            Category = "Aberration",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number (0 for entire system)", DefaultValue = "0" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "SPHA", "COMA", "ASTI", "DIST" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "DIST",
            Description = "Distortion in waves contributed by the surface at the wavelength defined by Wave.",
            Category = "Aberration",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number (0 for entire system)", DefaultValue = "0" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "SPHA", "COMA", "ASTI", "FCUR", "DISG", "DIMX" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "AXCL",
            Description = "Axial color, measured in lens units. Image separation between the two wavelengths defined by Wave1 and Wave2.",
            Category = "Chromatic",
            Parameters = new()
            {
                new() { Name = "Wave1", Description = "First wavelength number", DefaultValue = "1" },
                new() { Name = "Wave2", Description = "Second wavelength number", DefaultValue = "2" },
                new() { Name = "Zone", Description = "Zone (0 for paraxial, 0-1 for real rays)", DefaultValue = "0" }
            },
            RelatedOperands = new() { "LACL" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "LACL",
            Description = "Lateral color measured as the difference in chief ray height at the image surface between two wavelengths.",
            Category = "Chromatic",
            Parameters = new()
            {
                new() { Name = "Wave1", Description = "First wavelength number", DefaultValue = "1" },
                new() { Name = "Wave2", Description = "Second wavelength number", DefaultValue = "2" },
                new() { Name = "Hx", Description = "Normalized field x coordinate", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y coordinate", DefaultValue = "0.7" }
            },
            RelatedOperands = new() { "AXCL" }
        });

        // Paraxial operands
        AddOperand(new OperandDefinition
        {
            Name = "EFFL",
            Description = "Effective focal length at the wavelength defined by Wave.",
            Category = "Paraxial",
            Parameters = new()
            {
                new() { Name = "Surf1", Description = "Starting surface number", DefaultValue = "0" },
                new() { Name = "Surf2", Description = "Ending surface number", DefaultValue = "0" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            Example = "EFFL Surf1=1 Surf2=last Wave=1 Target=100 Weight=1",
            RelatedOperands = new() { "EFLY", "PMAG", "AMAG" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "EFLY",
            Description = "Effective focal length in the Y-Z plane at the wavelength defined by Wave.",
            Category = "Paraxial",
            Parameters = new()
            {
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "EFFL" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "PMAG",
            Description = "Paraxial magnification at the wavelength defined by Wave.",
            Category = "Paraxial",
            Parameters = new()
            {
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "AMAG", "EFFL" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "AMAG",
            Description = "Angular magnification. Ratio of image to object space paraxial chief ray angles.",
            Category = "Paraxial",
            Parameters = new()
            {
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "PMAG", "EFFL" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "ENPP",
            Description = "Entrance pupil position relative to surface 1.",
            Category = "Paraxial",
            Parameters = new()
            {
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "EPDI", "EXPP", "EXPD" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "EPDI",
            Description = "Entrance pupil diameter.",
            Category = "Paraxial",
            Parameters = new()
            {
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "ENPP", "EXPP", "EXPD" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "EXPP",
            Description = "Exit pupil position relative to the image surface.",
            Category = "Paraxial",
            Parameters = new()
            {
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "ENPP", "EPDI", "EXPD" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "EXPD",
            Description = "Exit pupil diameter.",
            Category = "Paraxial",
            Parameters = new()
            {
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "ENPP", "EPDI", "EXPP" }
        });

        // MTF operands
        AddOperand(new OperandDefinition
        {
            Name = "MTFT",
            Description = "Tangential (Y-direction) MTF at specified spatial frequency.",
            Category = "MTF",
            Parameters = new()
            {
                new() { Name = "Samp", Description = "Sampling (1-6, higher = more accurate)", DefaultValue = "3" },
                new() { Name = "Wave", Description = "Wavelength (0 for polychromatic)", DefaultValue = "0" },
                new() { Name = "Field", Description = "Field number", DefaultValue = "1" },
                new() { Name = "Freq", Description = "Spatial frequency (cy/mm)", DefaultValue = "50" }
            },
            Example = "MTFT Samp=3 Wave=0 Field=1 Freq=50 Target=0.5 Weight=1",
            RelatedOperands = new() { "MTFS", "MTFA", "MTFN", "MTFX" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "MTFS",
            Description = "Sagittal (X-direction) MTF at specified spatial frequency.",
            Category = "MTF",
            Parameters = new()
            {
                new() { Name = "Samp", Description = "Sampling (1-6, higher = more accurate)", DefaultValue = "3" },
                new() { Name = "Wave", Description = "Wavelength (0 for polychromatic)", DefaultValue = "0" },
                new() { Name = "Field", Description = "Field number", DefaultValue = "1" },
                new() { Name = "Freq", Description = "Spatial frequency (cy/mm)", DefaultValue = "50" }
            },
            RelatedOperands = new() { "MTFT", "MTFA", "MTFN", "MTFX" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "MTFA",
            Description = "Average MTF (average of tangential and sagittal).",
            Category = "MTF",
            Parameters = new()
            {
                new() { Name = "Samp", Description = "Sampling (1-6)", DefaultValue = "3" },
                new() { Name = "Wave", Description = "Wavelength (0 for polychromatic)", DefaultValue = "0" },
                new() { Name = "Field", Description = "Field number", DefaultValue = "1" },
                new() { Name = "Freq", Description = "Spatial frequency (cy/mm)", DefaultValue = "50" }
            },
            RelatedOperands = new() { "MTFT", "MTFS", "MTFN", "MTFX" }
        });

        // RMS Spot operands
        AddOperand(new OperandDefinition
        {
            Name = "RSCE",
            Description = "RMS spot size calculated using Gaussian quadrature, referenced to the centroid.",
            Category = "Spot",
            Parameters = new()
            {
                new() { Name = "Ring", Description = "Number of rings for Gaussian quadrature", DefaultValue = "4" },
                new() { Name = "Wave", Description = "Wavelength (0 for polychromatic)", DefaultValue = "0" },
                new() { Name = "Hx", Description = "Normalized field x", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y", DefaultValue = "0" }
            },
            Example = "RSCE Ring=4 Wave=0 Hx=0 Hy=0.7 Target=0 Weight=1",
            RelatedOperands = new() { "RSCH", "RSRE", "RSRH" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "RSCH",
            Description = "RMS spot size using Gaussian quadrature, referenced to the chief ray.",
            Category = "Spot",
            Parameters = new()
            {
                new() { Name = "Ring", Description = "Number of rings", DefaultValue = "4" },
                new() { Name = "Wave", Description = "Wavelength (0 for polychromatic)", DefaultValue = "0" },
                new() { Name = "Hx", Description = "Normalized field x", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y", DefaultValue = "0" }
            },
            RelatedOperands = new() { "RSCE", "RSRE", "RSRH" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "RSRE",
            Description = "RMS spot size using rectangular grid, referenced to the centroid.",
            Category = "Spot",
            Parameters = new()
            {
                new() { Name = "Samp", Description = "Grid sampling size", DefaultValue = "4" },
                new() { Name = "Wave", Description = "Wavelength (0 for polychromatic)", DefaultValue = "0" },
                new() { Name = "Hx", Description = "Normalized field x", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y", DefaultValue = "0" }
            },
            RelatedOperands = new() { "RSCE", "RSCH", "RSRH" }
        });

        // OPD operands
        AddOperand(new OperandDefinition
        {
            Name = "OPDC",
            Description = "Optical path difference with respect to the centroid at specified field and wavelength.",
            Category = "Wavefront",
            Parameters = new()
            {
                new() { Name = "Samp", Description = "Sampling grid size", DefaultValue = "4" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" },
                new() { Name = "Hx", Description = "Normalized field x", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y", DefaultValue = "0" },
                new() { Name = "Px", Description = "Normalized pupil x", DefaultValue = "0" },
                new() { Name = "Py", Description = "Normalized pupil y", DefaultValue = "0" }
            },
            RelatedOperands = new() { "OPDX", "OPDM", "OPTH" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "OPDX",
            Description = "OPD excluding tilt with respect to the centroid.",
            Category = "Wavefront",
            Parameters = new()
            {
                new() { Name = "Samp", Description = "Sampling grid size", DefaultValue = "4" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" },
                new() { Name = "Hx", Description = "Normalized field x", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y", DefaultValue = "0" },
                new() { Name = "Px", Description = "Normalized pupil x", DefaultValue = "0" },
                new() { Name = "Py", Description = "Normalized pupil y", DefaultValue = "0" }
            },
            RelatedOperands = new() { "OPDC", "OPDM", "OPTH" }
        });

        // Ray trace operands
        AddOperand(new OperandDefinition
        {
            Name = "REAX",
            Description = "Real ray X coordinate at specified surface.",
            Category = "RayTrace",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number", DefaultValue = "Image" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" },
                new() { Name = "Hx", Description = "Normalized field x", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y", DefaultValue = "0" },
                new() { Name = "Px", Description = "Normalized pupil x", DefaultValue = "0" },
                new() { Name = "Py", Description = "Normalized pupil y", DefaultValue = "0" }
            },
            RelatedOperands = new() { "REAY", "REAZ", "TRCX", "TRCY" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "REAY",
            Description = "Real ray Y coordinate at specified surface.",
            Category = "RayTrace",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number", DefaultValue = "Image" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" },
                new() { Name = "Hx", Description = "Normalized field x", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y", DefaultValue = "0" },
                new() { Name = "Px", Description = "Normalized pupil x", DefaultValue = "0" },
                new() { Name = "Py", Description = "Normalized pupil y", DefaultValue = "0" }
            },
            RelatedOperands = new() { "REAX", "REAZ", "TRCX", "TRCY" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "TRCX",
            Description = "Transverse ray aberration X, measured relative to the centroid.",
            Category = "RayTrace",
            Parameters = new()
            {
                new() { Name = "Samp", Description = "Sampling", DefaultValue = "4" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" },
                new() { Name = "Hx", Description = "Normalized field x", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y", DefaultValue = "0" },
                new() { Name = "Px", Description = "Normalized pupil x", DefaultValue = "0" },
                new() { Name = "Py", Description = "Normalized pupil y", DefaultValue = "0" }
            },
            RelatedOperands = new() { "TRCY", "TRAD", "TRAE", "REAX", "REAY" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "TRCY",
            Description = "Transverse ray aberration Y, measured relative to the centroid.",
            Category = "RayTrace",
            Parameters = new()
            {
                new() { Name = "Samp", Description = "Sampling", DefaultValue = "4" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" },
                new() { Name = "Hx", Description = "Normalized field x", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y", DefaultValue = "0" },
                new() { Name = "Px", Description = "Normalized pupil x", DefaultValue = "0" },
                new() { Name = "Py", Description = "Normalized pupil y", DefaultValue = "0" }
            },
            RelatedOperands = new() { "TRCX", "TRAD", "TRAE", "REAX", "REAY" }
        });

        // Boundary operands
        AddOperand(new OperandDefinition
        {
            Name = "MNCT",
            Description = "Minimum center thickness constraint.",
            Category = "Boundary",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number", DefaultValue = "1" }
            },
            Example = "MNCT Surf=2 Target=3.0 Weight=1",
            RelatedOperands = new() { "MXCT", "MNET", "MXET", "CTGT", "CTLT" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "MXCT",
            Description = "Maximum center thickness constraint.",
            Category = "Boundary",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "MNCT", "MNET", "MXET", "CTGT", "CTLT" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "MNET",
            Description = "Minimum edge thickness constraint.",
            Category = "Boundary",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "MNCT", "MXCT", "MXET" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "MXET",
            Description = "Maximum edge thickness constraint.",
            Category = "Boundary",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "MNCT", "MXCT", "MNET" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "CVGT",
            Description = "Curvature greater than constraint.",
            Category = "Boundary",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "CVLT", "CVVA" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "CVLT",
            Description = "Curvature less than constraint.",
            Category = "Boundary",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "CVGT", "CVVA" }
        });

        // Configuration operand
        AddOperand(new OperandDefinition
        {
            Name = "CONF",
            Description = "Configuration operand. Changes the configuration number during merit function evaluation.",
            Category = "System",
            Parameters = new()
            {
                new() { Name = "Cfg#", Description = "Configuration number to switch to", DefaultValue = "1" }
            },
            RelatedOperands = new() { }
        });

        // F-Theta Distortion operands
        AddOperand(new OperandDefinition
        {
            Name = "DIMX",
            Description = "F-theta distortion in the X direction, measured as percentage deviation from ideal f-theta mapping (h = f × θ). Returns (actual - ideal) / ideal × 100%.",
            Category = "Distortion",
            Parameters = new()
            {
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "0" },
                new() { Name = "Field", Description = "Field number", DefaultValue = "1" }
            },
            Example = "DIMX Wave=0 Field=2 Target=0 Weight=1",
            RelatedOperands = new() { "DISY", "DIST", "DISC" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "DISY",
            Description = "F-theta distortion in the Y direction, measured as percentage deviation from ideal f-theta mapping (h = f × θ). Returns (actual - ideal) / ideal × 100%.",
            Category = "Distortion",
            Parameters = new()
            {
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "0" },
                new() { Name = "Field", Description = "Field number", DefaultValue = "1" }
            },
            Example = "DISY Wave=0 Field=2 Target=0 Weight=1",
            RelatedOperands = new() { "DIMX", "DIST", "DISC" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "DISC",
            Description = "Calibrated distortion as a percentage. Measures the deviation of actual image height from paraxial prediction.",
            Category = "Distortion",
            Parameters = new()
            {
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "0" },
                new() { Name = "Field", Description = "Field number", DefaultValue = "1" }
            },
            RelatedOperands = new() { "DIMX", "DISY", "DIST" }
        });

        // Relative Illumination operand
        AddOperand(new OperandDefinition
        {
            Name = "RELI",
            Description = "Relative illumination at specified field point. Returns the ratio of illumination at the field point to the on-axis illumination, accounting for vignetting and cos^4 falloff.",
            Category = "Illumination",
            Parameters = new()
            {
                new() { Name = "Wave", Description = "Wavelength number (0 for polychromatic)", DefaultValue = "0" },
                new() { Name = "Field", Description = "Field number", DefaultValue = "1" }
            },
            Example = "RELI Wave=0 Field=3 Target=0.4 Weight=1",
            RelatedOperands = new() { }
        });

        // Real ray angle operands
        AddOperand(new OperandDefinition
        {
            Name = "RANG",
            Description = "Real ray angle at specified surface. Returns the angle in degrees of the specified ray with respect to the surface normal or optical axis. Used for telecentricity control.",
            Category = "RayTrace",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number (0 for image)", DefaultValue = "0" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" },
                new() { Name = "Hx", Description = "Normalized field x", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y", DefaultValue = "0" },
                new() { Name = "Px", Description = "Normalized pupil x", DefaultValue = "0" },
                new() { Name = "Py", Description = "Normalized pupil y", DefaultValue = "0" }
            },
            Example = "RANG Surf=0 Wave=1 Hx=0 Hy=0.7 Px=0 Py=0 Target=0 Weight=1",
            RelatedOperands = new() { "RAID", "REAB", "REAX", "REAY" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "RAID",
            Description = "Ray angle of incidence at specified surface. Returns the angle in degrees between the incident ray and the surface normal.",
            Category = "RayTrace",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number", DefaultValue = "1" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" },
                new() { Name = "Hx", Description = "Normalized field x", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y", DefaultValue = "0" },
                new() { Name = "Px", Description = "Normalized pupil x", DefaultValue = "0" },
                new() { Name = "Py", Description = "Normalized pupil y", DefaultValue = "0" }
            },
            Example = "RAID Surf=2 Wave=1 Hx=0 Hy=1 Px=0 Py=1 Target=0 Weight=1",
            RelatedOperands = new() { "RANG", "REAB" }
        });

        AddOperand(new OperandDefinition
        {
            Name = "REAB",
            Description = "Real ray angle with respect to local surface normal or Z-axis. Returns direction cosine or angle depending on Data settings.",
            Category = "RayTrace",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number", DefaultValue = "0" },
                new() { Name = "Wave", Description = "Wavelength number", DefaultValue = "1" },
                new() { Name = "Hx", Description = "Normalized field x", DefaultValue = "0" },
                new() { Name = "Hy", Description = "Normalized field y", DefaultValue = "0" },
                new() { Name = "Px", Description = "Normalized pupil x", DefaultValue = "0" },
                new() { Name = "Py", Description = "Normalized pupil y", DefaultValue = "0" }
            },
            RelatedOperands = new() { "RANG", "RAID" }
        });

        // Beam diameter operand
        AddOperand(new OperandDefinition
        {
            Name = "DMLT",
            Description = "Maximum beam diameter at specified surface considering all fields. Returns the diameter of the smallest circle that encloses all ray intersections from all field points at the surface.",
            Category = "Beam",
            Parameters = new()
            {
                new() { Name = "Surf", Description = "Surface number", DefaultValue = "1" },
                new() { Name = "Wave", Description = "Wavelength number (0 for all)", DefaultValue = "0" }
            },
            Example = "DMLT Surf=1 Wave=0 Target=4.0 Weight=1",
            RelatedOperands = new() { "EPDI", "EXPD" }
        });
    }

    private void AddOperand(OperandDefinition operand)
    {
        _operands[operand.Name] = operand;
    }
}
