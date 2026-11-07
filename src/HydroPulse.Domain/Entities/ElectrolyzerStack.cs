namespace HydroPulse.Domain.Entities;

public class ElectrolyzerStack
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string ModelName { get; private set; }
    public double RatedCapacityKw { get; private set; }
    public double SpecificEnergyConsumptionKwhPerKg { get; private set; }
    public double WaterConsumptionRatioLitersPerKg { get; private set; }
    public double MinimumOperatingLoadPercent { get; private set; }

    public ElectrolyzerStack(string modelName, double ratedCapacityKw, double specificEnergyKwhKg, double waterRatio = 9.5, double minLoadPercent = 10.0)
    {
        ModelName = modelName;
        RatedCapacityKw = ratedCapacityKw;
        SpecificEnergyConsumptionKwhPerKg = specificEnergyKwhKg;
        WaterConsumptionRatioLitersPerKg = waterRatio;
        MinimumOperatingLoadPercent = minLoadPercent;
    }

    public ElectrolyzerOperationResult EvaluateOperation(double incomingPowerKw, double gridTariffPerKwh)
    {
        double minPowerThreshold = RatedCapacityKw * (MinimumOperatingLoadPercent / 100.0);

        if (incomingPowerKw < minPowerThreshold)
        {
            return new ElectrolyzerOperationResult(
                Status: "Standby / Insufficient Power",
                HydrogenProducedKgPerHour: 0,
                WaterConsumedLitersPerHour: 0,
                ElectricityCostPerHour: 0
            );
        }

        double operationalPower = Math.Min(incomingPowerKw, RatedCapacityKw);
        double h2Output = operationalPower / SpecificEnergyConsumptionKwhPerKg;
        double waterNeeded = h2Output * WaterConsumptionRatioLitersPerKg;
        double hourlyCost = operationalPower * gridTariffPerKwh;

        return new ElectrolyzerOperationResult(
            Status: "Active - Optimal Generation",
            HydrogenProducedKgPerHour: h2Output,
            WaterConsumedLitersPerHour: waterNeeded,
            ElectricityCostPerHour: hourlyCost
        );
    }
}

public record ElectrolyzerOperationResult(
    string Status,
    double HydrogenProducedKgPerHour,
    double WaterConsumedLitersPerHour,
    double ElectricityCostPerHour
);