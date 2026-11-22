using System;
using System.Collections.Generic;
using System.Linq;
using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Localization;

namespace GangsOfBharat.Tests
{
    public static class CatalogTests
    {
        [Test]
        public static void VehiclesHaveUniqueIdsNamesAndParodyBrands()
        {
            VehicleDef[] all = VehicleCatalog.All;
            Assert.Equal(all.Length, all.Select(v => v.Id).Distinct().Count(), "unique ids");
            Assert.Equal(all.Length, all.Select(v => v.Key).Distinct().Count(), "unique keys");
            string[] realBrands = { "maruti", "suzuki", "mahindra", "tata ", "land rover", "range rover", "mercedes", "bmw", "lamborghini", "ferrari", "toyota", "honda", "hyundai", "bajaj" };
            foreach (VehicleDef v in all)
            {
                Assert.True(!string.IsNullOrWhiteSpace(v.Brand) && !string.IsNullOrWhiteSpace(v.Model) && !string.IsNullOrWhiteSpace(v.Key), "names for " + v.Id);
                Assert.True(v.Seats >= 1 && v.TopSpeedKmh > 0 && v.Acceleration > 0 && v.TurnRate > 0, "stats for " + v.Key);
                string name = v.DisplayName.ToLowerInvariant() + " ";
                foreach (string real in realBrands)
                {
                    Assert.False(name.Contains(real), v.DisplayName + " uses a real brand");
                }
                if (v.Premium)
                {
                    Assert.Equal(0L, v.PriceRupees, v.Key + ": premium cars are coins only");
                    Assert.True(v.PriceCoins > 0, v.Key + " coin price");
                }
                else if (v.FactionOnly)
                {
                    Assert.Equal(0L, v.PriceRupees, v.Key + ": service vehicles are not sold");
                }
                else
                {
                    Assert.True(v.PriceRupees > 0, v.Key + " rupee price");
                    Assert.Equal(0, v.PriceCoins);
                }
            }
            foreach (string brand in VehicleCatalog.ParodyBrands)
            {
                Assert.True(all.Any(v => v.Brand == brand), "catalog has " + brand);
            }
            foreach (VehicleClass c in new[] { VehicleClass.Auto, VehicleClass.Bike, VehicleClass.Bus, VehicleClass.Tractor, VehicleClass.Truck, VehicleClass.MiniTruck })
            {
                Assert.True(all.Any(v => v.Class == c), "catalog has class " + c);
            }
            Assert.True(all.Count(v => v.Premium) >= 3, "premium line-up");
        }

        [Test]
        public static void VehiclePricesMatchEconomyDesign()
        {
            Assert.Equal(5000L, VehicleCatalog.Get(1).PriceRupees, "cycle 5k");
            Assert.Equal(60000L, VehicleCatalog.Get(2).PriceRupees, "bike 60k");
            Assert.Equal(400000L, VehicleCatalog.All.First(v => v.Brand == "Marutha" && v.Class == VehicleClass.SmallCar).PriceRupees, "Marutha 4L");
            Assert.Equal(1500000L, VehicleCatalog.All.First(v => v.Brand == "Mahindar" && v.Key == "mahindar_sher").PriceRupees, "Mahindar 15L");
            Assert.Equal(15000000L, VehicleCatalog.All.First(v => v.Brand == "Ranga Rover" && !v.Premium).PriceRupees, "Ranga Rover 1.5Cr");
            Assert.Equal(50000000L, VehicleCatalog.All.First(v => v.Brand == "Lambor-Gini" && !v.Premium).PriceRupees, "Lambor-Gini 5Cr");
        }

        [Test]
        public static void FactionSlotsRanksAndSalariesMatchDesign()
        {
            Assert.Equal(480, FactionCatalog.TotalFactionSlots, "faction slots");
            Assert.True(FactionCatalog.TotalFactionSlots <= 1000, "fits a 1000-player server");
            Assert.Equal(520, FactionCatalog.CivilianSlots, "civilians are the remainder");
            Assert.Equal(13, FactionCatalog.All.Length);
            Assert.Equal(FactionCatalog.All.Length, FactionCatalog.All.Select(f => f.Id).Distinct().Count(), "unique ids");
            Assert.Equal(FactionCatalog.All.Length, FactionCatalog.All.Select(f => f.Key).Distinct().Count(), "unique keys");
            foreach (FactionDef f in FactionCatalog.All)
            {
                Assert.Equal(f.SlotCap, f.RankSeatTotal, f.Key + " rank counts sum to slot cap");
                Assert.True((int)f.Id < 16, "faction id fits in 4 bits");
                for (int i = 0; i < f.Ranks.Length; i++)
                {
                    Assert.True(!string.IsNullOrWhiteSpace(f.Ranks[i].Name) && !string.IsNullOrWhiteSpace(f.Ranks[i].Key), "rank names");
                    Assert.Equal(i, f.Ranks[i].Level);
                }
                if (f.Kind != FactionKind.Government)
                {
                    Assert.True(f.Ranks.All(r => r.SalaryPerPaydayRupees == 0), f.Key + ": gangs and mafia have no salary");
                }
            }
            Dictionary<string, int> caps = FactionCatalog.All.ToDictionary(f => f.Key, f => f.SlotCap);
            Assert.Equal(80, caps["police"]);
            Assert.Equal(60, caps["army"]);
            Assert.Equal(40, caps["hospital"]);
            Assert.Equal(20, caps["fire"]);
            Assert.Equal(25, caps["government"]);
            Assert.Equal(20, caps["news"]);
            Assert.Equal(160, FactionCatalog.All.Where(f => f.Kind == FactionKind.Gang).Sum(f => f.SlotCap), "4 gangs x 40");
            Assert.Equal(60, FactionCatalog.All.Where(f => f.Kind == FactionKind.Mafia).Sum(f => f.SlotCap), "2 families x 30");
            Assert.Equal(15, caps["court"]);

            FactionDef police = FactionCatalog.Get(FactionId.Police);
            Assert.Equal(80, police.Ranks.Sum(r => r.Count), "police ranks sum to 80");
            int[] counts = { 1, 2, 4, 8, 12, 15, 25, 13 };
            long[] salaries = { 12000, 8000, 6000, 4500, 3500, 2500, 2000, 1500 };
            Assert.SequenceEqual(counts, police.Ranks.Select(r => r.Count), "police rank counts");
            Assert.SequenceEqual(salaries, police.Ranks.Select(r => r.SalaryPerPaydayRupees), "police salaries");
            Assert.Equal("Commissioner", police.Ranks[0].Name);

            Assert.SequenceEqual(new long[] { 12000, 8500, 6500, 4800, 3800, 2800, 2200, 1500 },
                FactionCatalog.Get(FactionId.Army).Ranks.Select(r => r.SalaryPerPaydayRupees), "army salaries");
            Assert.SequenceEqual(new long[] { 11000, 6500, 4500, 2800, 2200, 1500 },
                FactionCatalog.Get(FactionId.Hospital).Ranks.Select(r => r.SalaryPerPaydayRupees), "hospital salaries");
            Assert.SequenceEqual(new long[] { 9000, 5000, 2800, 1500 },
                FactionCatalog.Get(FactionId.Fire).Ranks.Select(r => r.SalaryPerPaydayRupees), "fire salaries");
            Assert.SequenceEqual(new long[] { 13000, 9000, 7000, 2800, 2000 },
                FactionCatalog.Get(FactionId.Government).Ranks.Select(r => r.SalaryPerPaydayRupees), "government salaries");
            Assert.SequenceEqual(new long[] { 7000, 4000, 2800, 2000, 1200 },
                FactionCatalog.Get(FactionId.News).Ranks.Select(r => r.SalaryPerPaydayRupees), "news salaries");
        }

        [Test]
        public static void JobsAndBusinessesMatchDesign()
        {
            Assert.Equal(14, JobCatalog.All.Length);
            Assert.Equal(JobCatalog.All.Length, JobCatalog.All.Select(j => j.Id).Distinct().Count(), "unique job ids");
            foreach (JobDef j in JobCatalog.All)
            {
                Assert.True(!string.IsNullOrWhiteSpace(j.Key), "job key");
                Assert.True(j.MinRupeesPerHour <= j.MaxRupeesPerHour, j.Key + " range");
                if (j.PayModel == JobPayModel.Hourly)
                {
                    Assert.True(j.MinRupeesPerHour > 0, j.Key + " hourly pay");
                }
            }
            JobDef truck = JobCatalog.Get((int)JobId.TruckDriver);
            Assert.Equal(6000L, truck.MinRupeesPerHour);
            Assert.Equal(10000L, truck.MaxRupeesPerHour);
            Assert.True(truck.NeedsHeavyVehicleLicense);
            Assert.Equal(4000L, JobCatalog.Get((int)JobId.TaxiDriver).MinRupeesPerHour);
            Assert.Equal(2500L, JobCatalog.Get((int)JobId.Dabbawala).MinRupeesPerHour);

            Assert.Equal(BusinessCatalog.All.Length, BusinessCatalog.All.Select(b => b.Key).Distinct().Count(), "unique business keys");
            foreach (BusinessDef b in BusinessCatalog.All)
            {
                Assert.True(!string.IsNullOrWhiteSpace(b.Name), "business name");
            }
            Assert.Equal(150000000L, BusinessCatalog.FindByKey("casino").PriceRupees, "casino 15Cr");
            Assert.Equal(30, BusinessCatalog.FindByKey("chai_pan_stall").Count);
            Assert.Equal(15000000L, BusinessCatalog.FindByKey("mechanic_garage").PriceRupees, "garage 1.5Cr");
            Assert.False(BusinessCatalog.FindByKey("medical_store").IsDecided, "medical store TBD");
            Assert.Equal(1, BusinessCatalog.MaxBusinessesPerPlayer);
            Assert.Equal(5, BusinessCatalog.Hotels.Length);
            Assert.Equal(200000000L, BusinessCatalog.Hotels[4].PriceRupees, "5 star 20Cr");
            Assert.Equal(30000L, BusinessCatalog.Hotels[4].RoomPerNightRupees);
        }

        [Test]
        public static void EveryLocalizationKeyHasEnglishAndHindi()
        {
            List<string> keys = LocalizationTable.Keys.ToList();
            Assert.True(keys.Count > 40, "table populated");
            foreach (string key in keys)
            {
                Assert.True(LocalizationTable.HasKey("en", key), key + " en");
                Assert.True(LocalizationTable.HasKey("hi", key), key + " hi");
                Assert.False(string.IsNullOrWhiteSpace(LocalizationTable.Get("hi", key)), key + " hi text");
            }
            foreach (JobDef j in JobCatalog.All)
            {
                Assert.True(LocalizationTable.HasKey("en", j.NameKey) && LocalizationTable.HasKey("hi", j.NameKey), j.NameKey);
            }
            foreach (FactionDef f in FactionCatalog.All)
            {
                Assert.True(LocalizationTable.HasKey("en", f.NameKey) && LocalizationTable.HasKey("hi", f.NameKey), f.NameKey);
            }
            Assert.Equal("Police", LocalizationTable.Get("ta", "faction.police"), "unknown language falls back to en");
            Assert.Equal("पुलिस", LocalizationTable.Get("hi", "faction.police"));
            Assert.Equal("missing.key", LocalizationTable.Get("hi", "missing.key"));
            Assert.Equal("Job done! You earned ₹450.", LocalizationTable.Format("en", "system.job_complete", 450));
        }
    }
}
