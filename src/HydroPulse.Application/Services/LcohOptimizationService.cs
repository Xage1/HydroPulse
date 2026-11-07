namespace HydroPulse.Application.Services;

public class LcohOptimizationService
{
    // Constants for 1 MW PEM Plant economic baseline
    private const double AnnualCapexUsd = 180000.0; // Amortized annual capital cost
    private const double AnnualOpexFixedUsd = 25000.0; // Maintenance, insurance, stack replacement fund
    private const double AnnualProjectedH2Kg = 150000.0; // 150 tonnes/year nominal

    public LcohCalculationResult CalculateLevelizedCost(
        double totalElectricityCostAccruedUsd, 
        double totalHydrogenProducedKg, 
        double currentTariffPerKwh,
        double renewablePenetrationPercent)
    {
        if (totalHydrogenProducedKg <= 0)
        {
            return new LcohCalculationResult(0, 0, "Idle", false);
        }

        // Base capital & fixed operational cost contribution per kg produced so far
        double baseCapexOpexPerKg = (AnnualCapexUsd + AnnualOpexFixedUsd) / Math.Max(totalHydrogenProducedKg, 1.0);
        
        // Variable electricity cost per kg
        double electricityCostPerKg = totalElectricityCostAccruedUsd / totalHydrogenProducedKg;

        double totalLcoh = baseCapexOpexPerKg + electricityCostPerKg;

        // Economic threshold rules for green hydrogen certification and profitability
        bool isEconomicallyViable = currentTariffPerKwh <= 0.08 && renewablePenetrationPercent >= 80.0;
        string economicStatus = isEconomicallyViable ? "Optimal Green Production" : "Economic Alert: High Tariff / Grid Reliance";

        return new LcohCalculationResult(
            LevelizedCostPerKg: Math.Round(totalLcoh, 3),
            ElectricityCostComponentPerKg: Math.Round(electricityCostPerKg, 3),
            Status: economicStatus,
            IsViable: isEconomicallyViable
        );
    }
}

public record LcohCalculationResult(
    double LevelizedCostPerKg,
    double ElectricityCostComponentPerKg,
    String Status,
    bool IsViable
);