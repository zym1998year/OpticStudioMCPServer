namespace ZemaxMCP.Core.Services.GlassCatalog;

public static class DispersionCalculator
{
    private const double Lambda_d = 0.5875618;
    private const double Lambda_C = 0.6562725;
    private const double Lambda_F = 0.4861327;
    private const double Lambda_g = 0.4358343;

    public static double ComputeIndex(int formula, double[] c, double lambdaMicrons)
    {
        double L = lambdaMicrons;
        double L2 = L * L;

        switch (formula)
        {
            case 1: // Schott
                {
                    double n2 = c[0] + c[1] * L2 + c[2] / L2 + c[3] / (L2 * L2)
                               + c[4] / (L2 * L2 * L2) + c[5] / (L2 * L2 * L2 * L2);
                    return Math.Sqrt(Math.Abs(n2));
                }
            case 2: // Sellmeier 1
                {
                    double n2 = 1.0;
                    n2 += Safe(c, 0) * L2 / (L2 - Safe(c, 1));
                    n2 += Safe(c, 2) * L2 / (L2 - Safe(c, 3));
                    n2 += Safe(c, 4) * L2 / (L2 - Safe(c, 5));
                    return Math.Sqrt(Math.Abs(n2));
                }
            case 3: // Herzberger
                {
                    double LL = 1.0 / (L2 - 0.028);
                    double n = Safe(c, 0) + Safe(c, 1) * LL + Safe(c, 2) * LL * LL
                             + Safe(c, 3) * L2 + Safe(c, 4) * L2 * L2 + Safe(c, 5) * L2 * L2 * L2;
                    return n;
                }
            case 4: // Sellmeier 2
                {
                    double n2 = 1.0 + Safe(c, 0) * L2 / (L2 - Safe(c, 1))
                                   + Safe(c, 2) * L2 / (L2 - Safe(c, 3));
                    return Math.Sqrt(Math.Abs(n2));
                }
            case 5: // Conrady
                {
                    double n = Safe(c, 0) + Safe(c, 1) / L + Safe(c, 2) / Math.Pow(L, 3.5);
                    return n;
                }
            case 6: // Sellmeier 3
                {
                    double n2 = 1.0;
                    n2 += Safe(c, 0) * L2 / (L2 - Safe(c, 1));
                    n2 += Safe(c, 2) * L2 / (L2 - Safe(c, 3));
                    n2 += Safe(c, 4) * L2 / (L2 - Safe(c, 5));
                    n2 += Safe(c, 6) * L2 / (L2 - Safe(c, 7));
                    return Math.Sqrt(Math.Abs(n2));
                }
            case 7: // Handbook of Optics 1
                {
                    double n2 = Safe(c, 0) + Safe(c, 1) / (L2 - Safe(c, 2))
                               - Safe(c, 3) * L2;
                    return Math.Sqrt(Math.Abs(n2));
                }
            case 8: // Handbook of Optics 2
                {
                    double n2 = Safe(c, 0) + Safe(c, 1) * L2 / (L2 - Safe(c, 2))
                               - Safe(c, 3) * L2;
                    return Math.Sqrt(Math.Abs(n2));
                }
            case 9: // Sellmeier 4
                {
                    double n2 = Safe(c, 0) + Safe(c, 1) * L2 / (L2 - Safe(c, 2))
                               + Safe(c, 3) * L2 / (L2 - Safe(c, 4));
                    return Math.Sqrt(Math.Abs(n2));
                }
            case 10: // Extended 1
                {
                    double n2 = Safe(c, 0) + Safe(c, 1) * L2 + Safe(c, 2) / L2
                               + Safe(c, 3) / (L2 * L2) + Safe(c, 4) / (L2 * L2 * L2)
                               + Safe(c, 5) / (L2 * L2 * L2 * L2)
                               + Safe(c, 6) / (L2 * L2 * L2 * L2 * L2)
                               + Safe(c, 7) / (L2 * L2 * L2 * L2 * L2 * L2);
                    return Math.Sqrt(Math.Abs(n2));
                }
            case 11: // Sellmeier 5
                {
                    double n2 = 1.0;
                    n2 += Safe(c, 0) * L2 / (L2 - Safe(c, 1));
                    n2 += Safe(c, 2) * L2 / (L2 - Safe(c, 3));
                    n2 += Safe(c, 4) * L2 / (L2 - Safe(c, 5));
                    n2 += Safe(c, 6) * L2 / (L2 - Safe(c, 7));
                    n2 += Safe(c, 8) * L2 / (L2 - Safe(c, 9));
                    return Math.Sqrt(Math.Abs(n2));
                }
            case 12: // Extended 2
                {
                    double n2 = Safe(c, 0) + Safe(c, 1) * L2 + Safe(c, 2) / L2
                               + Safe(c, 3) / (L2 * L2) + Safe(c, 4) / (L2 * L2 * L2)
                               + Safe(c, 5) / (L2 * L2 * L2 * L2)
                               + Safe(c, 6) * L2 * L2;
                    return Math.Sqrt(Math.Abs(n2));
                }
            case 13: // Extended 3
                {
                    double n2 = Safe(c, 0) + Safe(c, 1) * L2 + Safe(c, 2) * L2 * L2
                               + Safe(c, 3) / L2 + Safe(c, 4) / (L2 * L2)
                               + Safe(c, 5) / (L2 * L2 * L2);
                    return Math.Sqrt(Math.Abs(n2));
                }
            default:
                return 1.5;
        }
    }

    public static double ComputeDPgF(int formula, double[] coefficients, double Vd)
    {
        double nF = ComputeIndex(formula, coefficients, Lambda_F);
        double nC = ComputeIndex(formula, coefficients, Lambda_C);
        double ng = ComputeIndex(formula, coefficients, Lambda_g);

        double denominator = nF - nC;
        if (Math.Abs(denominator) < 1e-15)
            return 0.0;

        double PgF = (ng - nF) / denominator;
        double PgF_normal = 0.6438 - 0.001682 * Vd;

        return PgF - PgF_normal;
    }

    private static double Safe(double[] c, int index)
    {
        return (c != null && index < c.Length) ? c[index] : 0.0;
    }
}
