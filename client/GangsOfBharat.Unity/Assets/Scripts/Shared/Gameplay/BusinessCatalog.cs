namespace GangsOfBharat.Shared.Gameplay
{
    public sealed class BusinessDef
    {
        public readonly string Key;
        public readonly string Name;
        /// <summary>How many exist per server. -1 = to be set during balancing.</summary>
        public readonly int Count;
        /// <summary>Purchase price in rupees (in-game money only, never coins). -1 = TBD.</summary>
        public readonly long PriceRupees;

        public BusinessDef(string key, string name, int count, long priceRupees)
        {
            Key = key;
            Name = name;
            Count = count;
            PriceRupees = priceRupees;
        }

        public bool IsDecided
        {
            get { return Count >= 0 && PriceRupees >= 0; }
        }
    }

    public sealed class HotelDef
    {
        public readonly int Stars;
        public readonly string Key;
        public readonly long PriceRupees;
        public readonly long RoomPerNightRupees;

        public HotelDef(int stars, string key, long priceRupees, long roomPerNightRupees)
        {
            Stars = stars;
            Key = key;
            PriceRupees = priceRupees;
            RoomPerNightRupees = roomPerNightRupees;
        }
    }

    /// <summary>Businesses and hotels from docs/design/07-economy.md. 1 L = 100,000; 1 Cr = 10,000,000.</summary>
    public static class BusinessCatalog
    {
        public const long Lakh = 100000L;
        public const long Crore = 10000000L;
        public const int MaxBusinessesPerPlayer = 1;
        public const int OwnerInactivityDays = 7;

        public static readonly BusinessDef[] All = new[]
        {
            new BusinessDef("chai_pan_stall", "Chai / pan stall", 30, 10 * Lakh),
            new BusinessDef("dhaba", "Dhaba", 15, 40 * Lakh),
            new BusinessDef("store_24_7", "24/7 store", 15, 60 * Lakh),
            new BusinessDef("clothing_store", "Clothing store", 8, 1 * Crore),
            new BusinessDef("petrol_pump", "Petrol pump", 10, 2 * Crore),
            new BusinessDef("mechanic_garage", "Mechanic garage", 8, 15 * Crore / 10),
            new BusinessDef("bar_club", "Bar / club", 6, 3 * Crore),
            new BusinessDef("gym", "Gym", 5, 80 * Lakh),
            new BusinessDef("used_car_showroom", "Used car showroom", 3, 5 * Crore),
            new BusinessDef("cinema", "Cinema", 2, 4 * Crore),
            new BusinessDef("real_estate_office", "Real estate office", 2, 3 * Crore),
            new BusinessDef("taxi_company", "Taxi company", 2, 2 * Crore),
            new BusinessDef("transport_company", "Transport company", 3, 4 * Crore),
            new BusinessDef("port_warehouse", "Port warehouse", 2, 8 * Crore),
            new BusinessDef("medical_store", "Medical store", -1, -1),
            new BusinessDef("casino", "Casino (chips only)", 1, 15 * Crore),
        };

        public static readonly HotelDef[] Hotels = new[]
        {
            new HotelDef(1, "hotel_1_star", 50 * Lakh, 500),
            new HotelDef(2, "hotel_2_star", 15 * Crore / 10, 1500),
            new HotelDef(3, "hotel_3_star", 4 * Crore, 4000),
            new HotelDef(4, "hotel_4_star", 8 * Crore, 10000),
            new HotelDef(5, "hotel_5_star_sea_facing", 20 * Crore, 30000),
        };

        public static BusinessDef FindByKey(string key)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Key == key)
                {
                    return All[i];
                }
            }
            return null;
        }
    }
}
