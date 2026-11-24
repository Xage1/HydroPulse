namespace HydroPulse.Domain.Engineering;

/// <summary>Cash flows and output for one project year (t = 1..N), all in the same real/nominal basis.</summary>
public sealed record YearlyProjection(
    double OpexUsd,
    double ElectricityCostUsd,
    double StackReplacementUsd,
    double HydrogenKg);

public sealed record LcohResult(
    double LcohUsdPerKg,
    double DiscountedCostsUsd,
    double DiscountedHydrogenKg);

/// <summary>
/// Discounted-cash-flow levelized cost of hydrogen:
///   LCOH = (CAPEX + sum_t (OPEX_t + Elec_t + Repl_t)/(1+r)^t) / sum_t (H2_t/(1+r)^t)
/// CAPEX is incurred at t = 0 (undiscounted); operating years are t = 1..N, end-of-year convention.
/// </summary>
public static class LcohCalculator
{
    public static LcohResult Calculate(
        double capexUsd,
        double discountRate,
        IReadOnlyList<YearlyProjection> years)
    {
        if (capexUsd < 0) throw new ArgumentOutOfRangeException(nameof(capexUsd));
        if (discountRate <= -1.0) throw new ArgumentOutOfRangeException(nameof(discountRate));
        if (years is null || years.Count == 0) throw new ArgumentException("At least one project year is required.", nameof(years));

        double costs = capexUsd;
        double hydrogen = 0.0;

        for (int i = 0; i < years.Count; i++)
        {
            double factor = Math.Pow(1.0 + discountRate, i + 1);
            var y = years[i];
            costs += (y.OpexUsd + y.ElectricityCostUsd + y.StackReplacementUsd) / factor;
            hydrogen += y.HydrogenKg / factor;
        }

        if (hydrogen <= 0.0)
            throw new InvalidOperationException("Discounted hydrogen output is zero; LCOH is undefined.");

        return new LcohResult(costs / hydrogen, costs, hydrogen);
    }

    /// <summary>
    /// Running operational cost per kg for live dashboards. This is NOT an LCOH: it excludes
    /// capital and discounting and only reflects variable cost so far.
    /// </summary>
    public static double RunningElectricityCostPerKg(double electricityCostUsd, double hydrogenKg)
        => hydrogenKg > 0 ? electricityCostUsd / hydrogenKg : 0.0;
}
