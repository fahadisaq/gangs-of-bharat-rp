using System.Collections.Generic;

namespace GangsOfBharat.Shared.Gameplay
{
    public enum VehicleClass : byte
    {
        Cycle = 0,
        Bike = 1,
        Auto = 2,
        SmallCar = 3,
        Sedan = 4,
        Suv = 5,
        Luxury = 6,
        Sports = 7,
        Super = 8,
        MiniTruck = 9,
        Truck = 10,
        Bus = 11,
        Tractor = 12,
        Service = 13,
        Boat = 14,
        Helicopter = 15,
    }

    public sealed class VehicleDef
    {
        public readonly int Id;
        public readonly string Key;
        /// <summary>Parody brand. Never a real trademark.</summary>
        public readonly string Brand;
        public readonly string Model;
        public readonly VehicleClass Class;
        /// <summary>Showroom price in rupees. 0 for premium (coins only) and faction service vehicles.</summary>
        public readonly long PriceRupees;
        /// <summary>Premium cars are sold ONLY for IAP coins, never for rupees.</summary>
        public readonly bool Premium;
        /// <summary>Coin price for premium cars. Placeholder values: DRAFT - owner review.</summary>
        public readonly int PriceCoins;
        /// <summary>Faction-only service vehicle (police, ambulance, fire, army); not sold.</summary>
        public readonly bool FactionOnly;
        public readonly int Seats;
        public readonly float TopSpeedKmh;
        /// <summary>Forward acceleration in m/s^2 used by Movement.</summary>
        public readonly float Acceleration;
        /// <summary>Turn rate at full steer in rad/s.</summary>
        public readonly float TurnRate;

        public VehicleDef(int id, string key, string brand, string model, VehicleClass vehicleClass,
            long priceRupees, bool premium, int priceCoins, bool factionOnly, int seats, float topSpeedKmh,
            float acceleration, float turnRate)
        {
            Id = id;
            Key = key;
            Brand = brand;
            Model = model;
            Class = vehicleClass;
            PriceRupees = priceRupees;
            Premium = premium;
            PriceCoins = priceCoins;
            FactionOnly = factionOnly;
            Seats = seats;
            TopSpeedKmh = topSpeedKmh;
            Acceleration = acceleration;
            TurnRate = turnRate;
        }

        public float TopSpeedMetersPerSecond
        {
            get { return TopSpeedKmh / 3.6f; }
        }

        public string DisplayName
        {
            get { return Brand + " " + Model; }
        }
    }

    /// <summary>
    /// Vehicles from docs/design/07-economy.md. Prices given in the design: Cycle 5k, Bike 60k,
    /// Marutha 4L, Mahindar 15L, Ranga Rover 1.5Cr, Lambor-Gini 5Cr. Every other price is a
    /// balancing placeholder.
    /// </summary>
    public static class VehicleCatalog
    {
        private const long L = 100000L;
        private const long Cr = 10000000L;

        private static VehicleDef Std(int id, string key, string brand, string model, VehicleClass c, long price,
            int seats, float topKmh, float accel, float turn)
        {
            return new VehicleDef(id, key, brand, model, c, price, false, 0, false, seats, topKmh, accel, turn);
        }

        private static VehicleDef Prem(int id, string key, string brand, string model, VehicleClass c, int coins,
            int seats, float topKmh, float accel, float turn)
        {
            return new VehicleDef(id, key, brand, model, c, 0, true, coins, false, seats, topKmh, accel, turn);
        }

        private static VehicleDef Svc(int id, string key, string brand, string model, int seats, float topKmh,
            float accel, float turn)
        {
            return new VehicleDef(id, key, brand, model, VehicleClass.Service, 0, false, 0, true, seats, topKmh, accel, turn);
        }

        public static readonly VehicleDef[] All = new[]
        {
            // Two and three wheelers
            Std(1, "cycle_roadie", "Bharat Cycles", "Roadie", VehicleClass.Cycle, 5000, 1, 25f, 1.5f, 2.6f),
            Std(2, "bike_sawari_100", "Bharat Motors", "Sawari 100", VehicleClass.Bike, 60000, 2, 90f, 4.0f, 2.4f),
            Std(3, "bike_toofan_350", "Bharat Motors", "Toofan 350", VehicleClass.Bike, 2 * L, 2, 120f, 5.0f, 2.3f),
            Std(4, "auto_rikshaw", "Bharat Motors", "Rikshaw 3W", VehicleClass.Auto, 25 * L / 10, 4, 55f, 2.0f, 2.0f),
            // Indian cars
            Std(5, "marutha_chhotu", "Marutha", "Chhotu", VehicleClass.SmallCar, 4 * L, 4, 150f, 3.0f, 1.8f),
            Std(6, "marutha_safar", "Marutha", "Safar", VehicleClass.Sedan, 7 * L, 5, 165f, 3.2f, 1.7f),
            Std(7, "mahindar_sher", "Mahindar", "Sher", VehicleClass.Suv, 15 * L, 7, 160f, 3.4f, 1.5f),
            Std(8, "mahindar_pahadi", "Mahindar", "Pahadi 4x4", VehicleClass.Suv, 14 * L, 4, 150f, 3.2f, 1.5f),
            Std(9, "tatva_nayan", "Tatva", "Nayan", VehicleClass.Suv, 10 * L, 5, 160f, 3.3f, 1.6f),
            // Trucks, buses, tractors
            Std(10, "tatva_chhota_hathi", "Tatva", "Chhota Hathi", VehicleClass.MiniTruck, 6 * L, 2, 90f, 2.2f, 1.4f),
            Std(11, "tatva_bharat_4028", "Tatva", "Bharat 4028", VehicleClass.Truck, 35 * L, 2, 90f, 1.4f, 0.9f),
            Std(12, "tatva_city_bus", "Tatva", "City Bus", VehicleClass.Bus, 40 * L, 40, 90f, 1.3f, 0.8f),
            Std(13, "mahindar_khet_605", "Mahindar", "Khet 605", VehicleClass.Tractor, 7 * L, 1, 30f, 1.2f, 1.2f),
            // Foreign / luxury cars
            Std(14, "mercedez_e_klasse", "Mercedez", "E-Klasse", VehicleClass.Luxury, 75 * L, 5, 240f, 5.5f, 1.8f),
            Std(15, "bmv_saat", "BMV", "Saat Series", VehicleClass.Luxury, 14 * Cr / 10, 5, 250f, 5.8f, 1.8f),
            Std(16, "ranga_rover_raja", "Ranga Rover", "Raja", VehicleClass.Luxury, 15 * Cr / 10, 5, 225f, 5.0f, 1.6f),
            Std(17, "ferraro_rosso", "Ferraro", "Rosso", VehicleClass.Sports, 38 * Cr / 10, 2, 320f, 8.0f, 2.0f),
            Std(18, "lambor_gini_toofani", "Lambor-Gini", "Toofani", VehicleClass.Super, 5 * Cr, 2, 325f, 8.5f, 2.0f),
            // Premium: IAP coins only (rotating store, limited editions)
            Prem(19, "ranga_rover_raja_mayanagri", "Ranga Rover", "Raja Mayanagri Edition", VehicleClass.Luxury, 4000, 5, 235f, 5.3f, 1.6f),
            Prem(20, "lambor_gini_toofani_sona", "Lambor-Gini", "Toofani Sona", VehicleClass.Super, 9000, 2, 340f, 9.0f, 2.1f),
            Prem(21, "ferraro_rosso_monsoon", "Ferraro", "Rosso Monsoon", VehicleClass.Sports, 7000, 2, 330f, 8.5f, 2.1f),
            Prem(22, "mercedez_g_kaala", "Mercedez", "G-Kaala", VehicleClass.Suv, 5000, 5, 220f, 5.2f, 1.5f),
            Prem(23, "bmv_m_neon", "BMV", "M Neon", VehicleClass.Sports, 6000, 4, 305f, 7.8f, 2.0f),
            // Faction service vehicles (not sold)
            Svc(24, "police_jeep", "Mahindar", "Police Jeep", 5, 150f, 3.6f, 1.6f),
            Svc(25, "ambulance_108", "Tatva", "Ambulance 108", 4, 130f, 2.8f, 1.3f),
            Svc(26, "fire_tender", "Tatva", "Fire Tender", 6, 110f, 1.6f, 0.9f),
            Svc(27, "army_truck", "Tatva", "Army Truck", 12, 100f, 1.5f, 0.9f),
            // Water and air
            Std(28, "boat_machhli", "Bharat Marine", "Machhli Boat", VehicleClass.Boat, 8 * L, 4, 60f, 1.8f, 1.0f),
            Std(29, "heli_udaan", "Udaan Aero", "Udaan H1", VehicleClass.Helicopter, 6 * Cr, 4, 250f, 4.0f, 1.2f),
        };

        private static readonly Dictionary<int, VehicleDef> ById = BuildIndex();

        private static Dictionary<int, VehicleDef> BuildIndex()
        {
            Dictionary<int, VehicleDef> map = new Dictionary<int, VehicleDef>();
            for (int i = 0; i < All.Length; i++)
            {
                map[All[i].Id] = All[i];
            }
            return map;
        }

        public static VehicleDef Get(int id)
        {
            VehicleDef def;
            return ById.TryGetValue(id, out def) ? def : null;
        }

        /// <summary>Brands used in the catalog. All are parodies.</summary>
        public static readonly string[] ParodyBrands = new[]
        {
            "Marutha", "Mahindar", "Tatva", "Ranga Rover", "Mercedez", "BMV", "Lambor-Gini", "Ferraro",
        };
    }
}
