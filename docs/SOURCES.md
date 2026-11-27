# Input sources and calibration

Every cost and technical input in `ScenarioConfig.cs` started as a placeholder. This file records which ones now have a cited reference range, what the references say, and what is **still unsourced**. Retrieved 23 Nov 2026. Figures are copied from the cited pages; nothing here is a customer quote.

**Rule:** a number in the model is only "sourced" when it appears in the Source column below. Otherwise it is a placeholder.

## 1. Inputs with a cited reference

| Input | Model default | Reference value | Verdict | Source |
|---|---|---|---|---|
| Electrolyzer installed cost | $1,800/kW (was $1,500) | Alkaline: USD 500-1,100/kW in China (2025); USD 1,900-2,500/kW outside China | Default now sits near the top of the China range and just under the non-China range. For an imported plant in Kenya, run a **1,000-2,500** range. | [IEA, Chinese electrolyser cost advantage](https://www.iea.org/commentaries/what-explains-the-cost-advantage-of-chinese-electrolysers) |
| Electrolyzer specific energy | 50 kWh/kg (system) | Alkaline 52 kWh/kg (2020), 47-49 projected 2030; PEM 57 (2020), 47-50 projected 2030 | Reasonable for current alkaline/PEM. | [Energy Environ. Sci. 2023, d3ee01208e](https://pubs.rsc.org/en/content/articlepdf/2023/ee/d3ee01208e) |
| Solar PV installed cost | $800/kWp | Global weighted average utility PV: USD 691/kW (2024) | Default is above the global average, which is sensible for a first project in East Africa. No Kenya figure found. | [IRENA, Renewable Power Generation Costs in 2024](https://www.irena.org/-/media/Files/IRENA/Agency/Publication/2025/Jul/IRENA_TEC_RPGC_in_2024_Summary_2025.pdf) |
| Onshore wind installed cost | $1,500/kW | Global weighted average: USD 1,041/kW (2024) | Default is conservative. No Kenya or Africa installed-cost figure found. | same IRENA report |
| Discount rate / WACC | 9% (was 8%) | Utility solar, Kenya and Senegal: 8.5-9%. IRENA Africa onshore wind assumption: 12%. Europe/North America: 4.7-6.4% (IEA) and 3.8% (IRENA wind). | Base is now **9%**; use **12%** as the high case. | [IEA, cost of capital in Kenya and Senegal](https://www.iea.org/commentaries/how-a-high-cost-of-capital-is-holding-back-energy-development-in-kenya-and-senegal); IRENA report above |
| Grid tariff (energy only) | $0.12/kWh | KPLC commercial and industrial tariffs, KES/kWh energy charge, Jul 2025: CI1 15.29, CI2 14.02, CI3 13.53, CI4 13.28, CI5 13.01. Time-of-use (off-peak) rows: CI1 7.65 down to CI5 6.51. Demand charge KES 220-1,100/kVA/month. | The tariff table covers **base energy and demand charges only**. Fuel, forex and other pass-through charges are added on top and change monthly, so the all-in price is higher. Convert at the current rate with the dashboard currency picker. The model now supports an off-peak price, off-peak hours and a demand charge (all off by default); fill them in from the buyer's actual tariff. KPLC's published off-peak energy rate is about half the standard rate (7.65 vs 15.29 KES/kWh for CI1), but the off-peak hours are not confirmed. | [EPRA abridged KPLC tariff 2023](https://www.epra.go.ke/wp-content/uploads/2023/01/ABRIDGED-KPLC-TARIFF-2023.pdf) |
| Turkana wind resource | Modelled wind capacity factor 18%; file mean wind 6.59 m/s at 50 m | Environmental assessment summary: average wind speed 11 m/s measured every 10 minutes at 43, 62, 81 and 83 m over more than six years; expected output 1,440 GWh/yr for about 300 MW. Power Technology: 310 MW, about 1,250 GWh/yr, 44 m hubs. Capacity factors of about 46 to 55% are **our arithmetic** from those outputs, not stated by the sources. | The reanalysis input is far too low for this site. The built-in Turkana sites now scale the NASA wind to a **10.0 m/s** mean at 50 m (factor 1.52), which gives a 55% capacity factor, at the top of the 46 to 55% range. 10.7 m/s gives 60%. The height of the published 11 m/s is not stated, so the 50 m conversion is ours. Replace with measured data via `sites.json` before quoting Turkana results. | [EIB, Lake Turkana Wind Power ESIA summary](https://www.eib.org/attachments/registers/53222536.pdf); [Power Technology](https://www.power-technology.com/projects/lake-turkana-wind-power-project-loyangalani/) |
| Renewable capacity factor (sanity check) | About 18-19% in our scenarios | Global 2024 averages: solar PV 17%, onshore wind 34% | Nairobi solar is consistent. A wind-only site far below 34% suggests the reanalysis wind data is **understating** Turkana. Do not treat the wind results as final. | IRENA report above |

## 2. Data and rules already referenced

| Item | Source | Status |
|---|---|---|
| Hourly weather (irradiance, wind 10 m and 50 m, temperature) | [NASA POWER hourly API](https://power.larc.nasa.gov/docs/services/api/temporal/hourly/) | Used directly. Reanalysis, not site measurement. |
| Monthly then hourly matching (RFNBO) | [Delegated Regulation (EU) 2023/1184](https://eur-lex.europa.eu/eli/reg_del/2023/1184/oj) | Dates (monthly to 31 Dec 2029, hourly from 1 Jan 2030) were taken from earlier research in this project and **not re-verified on 23 Nov 2026**. Check the current text before quoting. |
| Exchange rates | [ExchangeRate-API open access](https://open.er-api.com/v6/latest/USD) | Used directly. Attribution required. |

## 3. Still unsourced (placeholders)

| Input | Model default | What is needed |
|---|---|---|
| Stack lifetime | 80,000 h | Manufacturer datasheet or independent review. The papers checked do not state it. |
| Stack replacement cost | 30% of electrolyzer capex | Same. |
| Annual degradation | 1% SEC/yr (electrolyzer), 0.5%/yr (PV) | Manufacturer data, PV module warranty. |
| Opex | 2.5% of capex/yr | The paper checked says fixed opex is a share of system price but the percentage is in its supplement. |
| Other capex (BOP, install) | 10% | Project quotes. Real projects are often much higher. |
| Part-load efficiency gain | 8% at very low load | Manufacturer curve. |
| Minimum load | 10% | Manufacturer datasheet. |
| Water use | 9.5 L/kg | Stoichiometric minimum is about 9 L/kg; the extra covers purification losses. Cite a source. |
| Water price | $2/m3 | Local utility tariff at the site. |
| Wheeling charge | $0.02/kWh | Kenyan wheeling or use-of-system tariff, if one applies. |
| Turbine power curve and losses | Generic, 15% | Turbine-specific curve; site wake and availability losses. |
| PV efficiency factor and temperature coefficient | 0.86, -0.35%/C | Module datasheet. |
| Compression efficiency | 0.75 isentropic | Compressor datasheet. |
| Grid carbon intensity | Not modelled | Kenya grid emission factor, needed for certified-green claims. |

## 4. Recommended next calibration step

1. Done: base case discount rate 9% and electrolyzer $1,800/kW. Still to do: run low/high at ($1,000, $2,500) and (8%, 12%).
2. Ask the buyer for their real tariff structure (energy, demand, time of use) and enter it in the config; the model supports it, it is just not filled in.
3. Replace the stack life, replacement cost and opex placeholders with a manufacturer quote.
4. Replace Turkana reanalysis wind with measured or resource-assessment data before any bid.
