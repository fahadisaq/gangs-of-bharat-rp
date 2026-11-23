using System.Collections.Generic;

namespace GangsOfBharat.Shared.Gameplay
{
    public enum JobId
    {
        Dabbawala = 1,
        Construction = 2,
        FoodDelivery = 3,
        AutoDriver = 4,
        Fisherman = 5,
        BusDriver = 6,
        Farmer = 7,
        TaxiDriver = 8,
        QuarryWorker = 9,
        TruckDriver = 10,
        Mechanic = 11,
        Lawyer = 12,
        RealEstateAgent = 13,
        ShopStaff = 14,
    }

    public enum JobPayModel : byte
    {
        /// <summary>Pay is a rupees-per-hour range.</summary>
        Hourly = 0,
        /// <summary>Per repair or tuning job.</summary>
        PerService = 1,
        /// <summary>Per case (client fees).</summary>
        PerCase = 2,
        /// <summary>Commission per deal.</summary>
        Commission = 3,
        /// <summary>Wage set by the business owner.</summary>
        OwnerWage = 4,
    }

    public sealed class JobDef
    {
        public readonly JobId Id;
        public readonly string Key;
        public readonly string NameKey;
        public readonly JobPayModel PayModel;
        /// <summary>Rupees per hour, lower bound (0 when not hourly).</summary>
        public readonly long MinRupeesPerHour;
        /// <summary>Rupees per hour, upper bound (0 when not hourly).</summary>
        public readonly long MaxRupeesPerHour;
        public readonly bool NeedsHeavyVehicleLicense;
        /// <summary>The server's simple waypoint job flow supports this job.</summary>
        public readonly bool WaypointFlow;
        /// <summary>The waypoint flow requires the player to be in a vehicle.</summary>
        public readonly bool NeedsVehicle;

        public JobDef(JobId id, string key, JobPayModel payModel, long minPerHour, long maxPerHour,
            bool heavyLicense, bool waypointFlow, bool needsVehicle)
        {
            Id = id;
            Key = key;
            NameKey = "job." + key;
            PayModel = payModel;
            MinRupeesPerHour = minPerHour;
            MaxRupeesPerHour = maxPerHour;
            NeedsHeavyVehicleLicense = heavyLicense;
            WaypointFlow = waypointFlow;
            NeedsVehicle = needsVehicle;
        }

        public long MidRupeesPerHour
        {
            get { return (MinRupeesPerHour + MaxRupeesPerHour) / 2; }
        }
    }

    /// <summary>Jobs and pay from docs/design/07-economy.md.</summary>
    public static class JobCatalog
    {
        public static readonly JobDef[] All = new[]
        {
            new JobDef(JobId.Dabbawala, "dabbawala", JobPayModel.Hourly, 2500, 3500, false, false, false),
            new JobDef(JobId.Construction, "construction", JobPayModel.Hourly, 3000, 3000, false, false, false),
            new JobDef(JobId.FoodDelivery, "food_delivery", JobPayModel.Hourly, 3000, 4000, false, true, false),
            new JobDef(JobId.AutoDriver, "auto_driver", JobPayModel.Hourly, 3000, 5000, false, false, true),
            new JobDef(JobId.Fisherman, "fisherman", JobPayModel.Hourly, 3000, 5000, false, false, false),
            new JobDef(JobId.BusDriver, "bus_driver", JobPayModel.Hourly, 3500, 4500, false, false, true),
            new JobDef(JobId.Farmer, "farmer", JobPayModel.Hourly, 3000, 6000, false, false, false),
            new JobDef(JobId.TaxiDriver, "taxi_driver", JobPayModel.Hourly, 4000, 6000, false, true, true),
            new JobDef(JobId.QuarryWorker, "quarry_worker", JobPayModel.Hourly, 5000, 7000, false, false, false),
            new JobDef(JobId.TruckDriver, "truck_driver", JobPayModel.Hourly, 6000, 10000, true, false, true),
            new JobDef(JobId.Mechanic, "mechanic", JobPayModel.PerService, 0, 0, false, false, false),
            new JobDef(JobId.Lawyer, "lawyer", JobPayModel.PerCase, 0, 0, false, false, false),
            new JobDef(JobId.RealEstateAgent, "real_estate_agent", JobPayModel.Commission, 0, 0, false, false, false),
            new JobDef(JobId.ShopStaff, "shop_staff", JobPayModel.OwnerWage, 0, 0, false, false, false),
        };

        private static readonly Dictionary<int, JobDef> ById = BuildIndex();

        private static Dictionary<int, JobDef> BuildIndex()
        {
            Dictionary<int, JobDef> map = new Dictionary<int, JobDef>();
            for (int i = 0; i < All.Length; i++)
            {
                map[(int)All[i].Id] = All[i];
            }
            return map;
        }

        public static JobDef Get(int id)
        {
            JobDef def;
            return ById.TryGetValue(id, out def) ? def : null;
        }
    }
}
