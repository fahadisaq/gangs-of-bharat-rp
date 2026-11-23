using System;
using System.Collections.Generic;
using System.Linq;
using GangsOfBharat.Shared.World;

namespace GangsOfBharat.Tests
{
    public static class WorldTests
    {
        [Test]
        public static void GridMatchesBruteForceOnRandomData()
        {
            Random rng = new Random(7);
            SpatialHashGrid grid = new SpatialHashGrid();
            Dictionary<int, float[]> truth = new Dictionary<int, float[]>();
            for (int id = 0; id < 3000; id++)
            {
                float x = (float)(rng.NextDouble() * 8192);
                float z = (float)(rng.NextDouble() * 8192);
                grid.Insert(id, x, z);
                truth[id] = new[] { x, z };
            }
            List<int> results = new List<int>();
            for (int round = 0; round < 40; round++)
            {
                // random moves (small and large), removes and re-inserts
                for (int k = 0; k < 300; k++)
                {
                    int id = rng.Next(3000);
                    if (rng.NextDouble() < 0.1)
                    {
                        if (truth.Remove(id))
                        {
                            Assert.True(grid.Remove(id));
                        }
                        else
                        {
                            Assert.False(grid.Remove(id));
                        }
                        continue;
                    }
                    float x = rng.NextDouble() < 0.5 ? (float)(rng.NextDouble() * 8192) : Clamp((truth.ContainsKey(id) ? truth[id][0] : 4000) + (float)(rng.NextDouble() * 20 - 10));
                    float z = rng.NextDouble() < 0.5 ? (float)(rng.NextDouble() * 8192) : Clamp((truth.ContainsKey(id) ? truth[id][1] : 4000) + (float)(rng.NextDouble() * 20 - 10));
                    if (truth.ContainsKey(id))
                    {
                        grid.Move(id, x, z);
                    }
                    else
                    {
                        grid.Insert(id, x, z);
                    }
                    truth[id] = new[] { x, z };
                }
                Assert.Equal(truth.Count, grid.Count, "count");
                for (int q = 0; q < 20; q++)
                {
                    float qx = (float)(rng.NextDouble() * 8192);
                    float qz = (float)(rng.NextDouble() * 8192);
                    float radius = (float)(rng.NextDouble() * 400);
                    grid.QueryRadius(qx, qz, radius, results);
                    List<int> expected = truth.Where(kv => Dist2(kv.Value[0], kv.Value[1], qx, qz) <= radius * radius)
                        .Select(kv => kv.Key).OrderBy(i => i).ToList();
                    Assert.SequenceEqual(expected, results.OrderBy(i => i), "query " + round + "/" + q);
                }
            }
        }

        private static float Clamp(float v)
        {
            return Math.Max(0f, Math.Min(8192f, v));
        }

        private static float Dist2(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return dx * dx + dz * dz;
        }

        [Test]
        public static void GridQueryDoesNotAllocateAfterWarmup()
        {
            SpatialHashGrid grid = new SpatialHashGrid();
            Random rng = new Random(3);
            for (int i = 0; i < 1000; i++)
            {
                grid.Insert(i, 4000 + (float)rng.NextDouble() * 300, 4000 + (float)rng.NextDouble() * 300);
            }
            List<int> results = new List<int>(2000);
            grid.QueryRadius(4100, 4100, 200, results);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                grid.QueryRadius(4100, 4100, 200, results);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(0L, after - before, "bytes allocated by 1000 queries");
        }

        [Test]
        public static void InterestTiersFollowDistance()
        {
            SpatialHashGrid grid = new SpatialHashGrid();
            InterestManager im = new InterestManager(grid, new InterestSettings());
            grid.Insert(1, 1000, 1000); // self
            grid.Insert(2, 1030, 1000); // 30 m near
            grid.Insert(3, 1100, 1000); // 100 m mid
            grid.Insert(4, 1000, 1250); // 250 m far
            grid.Insert(5, 1400, 1000); // 400 m out of range
            ClientInterest ci = new ClientInterest();
            im.Update(ci, 1, 1000, 1000);
            Dictionary<int, InterestTier> tiers = new Dictionary<int, InterestTier>();
            for (int i = 0; i < ci.Relevant.Count; i++)
            {
                tiers[ci.Relevant[i]] = ci.Tiers[i];
            }
            Assert.False(tiers.ContainsKey(1), "self excluded");
            Assert.Equal(InterestTier.Near, tiers[2]);
            Assert.Equal(InterestTier.Mid, tiers[3]);
            Assert.Equal(InterestTier.Far, tiers[4]);
            Assert.False(tiers.ContainsKey(5), "beyond far radius");
            Assert.Equal(3, ci.Entered.Count);

            // Rates: near every tick, mid every 2nd, far every 4th.
            int near = 0;
            int mid = 0;
            int far = 0;
            for (uint t = 0; t < 40; t++)
            {
                near += im.ShouldSend(InterestTier.Near, t, 2) ? 1 : 0;
                mid += im.ShouldSend(InterestTier.Mid, t, 3) ? 1 : 0;
                far += im.ShouldSend(InterestTier.Far, t, 4) ? 1 : 0;
            }
            Assert.Equal(40, near);
            Assert.Equal(20, mid);
            Assert.Equal(10, far);

            // Entity 2 leaves, 5 comes close.
            grid.Move(2, 3000, 3000);
            grid.Move(5, 1010, 1000);
            im.Update(ci, 1, 1000, 1000);
            Assert.SequenceEqual(new[] { 5 }, ci.Entered);
            Assert.SequenceEqual(new[] { 2 }, ci.Left);
            Assert.True(ci.IsRelevant(5));
            Assert.False(ci.IsRelevant(2));
        }

        [Test]
        public static void InterestCapsToNearestEntities()
        {
            Random rng = new Random(9);
            SpatialHashGrid grid = new SpatialHashGrid();
            InterestSettings settings = new InterestSettings();
            Assert.Equal(100, settings.MaxEntitiesPerSnapshot, "default cap");
            InterestManager im = new InterestManager(grid, settings);
            Dictionary<int, float> dist = new Dictionary<int, float>();
            for (int id = 1; id <= 1000; id++)
            {
                float x = 4000 + (float)(rng.NextDouble() * 200 - 100);
                float z = 4000 + (float)(rng.NextDouble() * 200 - 100);
                grid.Insert(id, x, z);
                dist[id] = Dist2(x, z, 4000, 4000);
            }
            grid.Insert(5000, 4000, 4000);
            ClientInterest ci = new ClientInterest();
            im.Update(ci, 5000, 4000, 4000);
            Assert.Equal(100, ci.Relevant.Count, "capped to 100");
            List<int> expected = dist.OrderBy(kv => kv.Value).Take(100).Select(kv => kv.Key).OrderBy(i => i).ToList();
            Assert.SequenceEqual(expected, ci.Relevant.OrderBy(i => i), "the 100 nearest");

            settings.MaxEntitiesPerSnapshot = 50;
            im.Update(ci, 5000, 4000, 4000);
            Assert.Equal(50, ci.Relevant.Count, "configurable cap");
            Assert.Equal(50, ci.Left.Count);
        }
    }
}
