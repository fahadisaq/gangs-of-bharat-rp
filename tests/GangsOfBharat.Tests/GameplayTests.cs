using System.Linq;
using GangsOfBharat.Server;
using GangsOfBharat.Shared.Gameplay;
using GangsOfBharat.Shared.Net;

namespace GangsOfBharat.Tests
{
    public static class GameplayTests
    {
        [Test]
        public static void MovementIsDeterministicAndSpeedLimited()
        {
            VehicleDef car = VehicleCatalog.Get(18);
            CharacterState c1 = new CharacterState { X = 100, Z = 100 };
            CharacterState c2 = c1;
            VehicleState v1 = new VehicleState { X = 1000, Z = 1000 };
            VehicleState v2 = v1;
            for (int i = 0; i < 2000; i++)
            {
                float mx = (i % 37) / 18f - 1f;
                float mz = (i % 23) / 11f - 1f;
                Movement.StepCharacter(ref c1, mx, mz, 0.3f, i % 2 == 0, 0.05f);
                Movement.StepCharacter(ref c2, mx, mz, 0.3f, i % 2 == 0, 0.05f);
                Movement.StepVehicle(ref v1, car, 1f, (i % 50) / 25f - 1f, false, 0.05f);
                Movement.StepVehicle(ref v2, car, 1f, (i % 50) / 25f - 1f, false, 0.05f);
            }
            Assert.Equal(c1.X, c2.X, "character x deterministic");
            Assert.Equal(c1.Z, c2.Z);
            Assert.Equal(v1.X, v2.X, "vehicle x deterministic");
            Assert.Equal(v1.Speed, v2.Speed);
            Assert.AtMost(car.TopSpeedMetersPerSecond + 1e-3, v1.Speed, "top speed clamp");

            CharacterState c = new CharacterState { X = 500, Z = 500 };
            Movement.StepCharacter(ref c, 5f, 5f, 0f, true, 1f); // over-long input is normalized
            float moved = (float)System.Math.Sqrt((c.X - 500) * (c.X - 500) + (c.Z - 500) * (c.Z - 500));
            Assert.Near(Movement.RunSpeed, moved, 1e-3, "run speed");
            Movement.StepCharacter(ref c, float.NaN, float.PositiveInfinity, 0f, false, 1f);
            Assert.False(float.IsNaN(c.X) || float.IsNaN(c.Z), "NaN input ignored");

            VehicleState edge = new VehicleState { X = 8190, Z = 4000, Yaw = (float)(System.Math.PI / 2), Speed = 30 };
            Movement.StepVehicle(ref edge, car, 1f, 0f, false, 1f);
            Assert.Equal(8192f, edge.X, "clamped to map edge");
            Assert.Equal(0f, edge.Speed, "stopped at the edge");
        }

        [Test]
        public static void ValidatorRejectsTeleportsAndSpeedHacks()
        {
            Assert.True(MovementValidator.IsPlausibleMove(100, 100, 100.3f, 100, 0.05f, Movement.RunSpeed), "normal step");
            Assert.True(MovementValidator.IsPlausibleMove(100, 100, 104, 100, 0.05f, 90f), "fast car step");
            Assert.False(MovementValidator.IsPlausibleMove(100, 100, 200, 100, 0.05f, Movement.RunSpeed), "100 m teleport");
            Assert.False(MovementValidator.IsPlausibleMove(100, 100, 160, 100, 10f, 90f), "any jump over 50 m is a teleport");
            Assert.False(MovementValidator.IsPlausibleMove(100, 100, 100 + 3 * Movement.RunSpeed, 100, 1f, Movement.RunSpeed), "3x run speed");
            Assert.False(MovementValidator.IsPlausibleMove(100, 100, float.NaN, 100, 1f, Movement.RunSpeed), "NaN");
        }

        private static PlayerInputMessage InputAt(uint tick, float x, float z, float moveX = 0f)
        {
            return new PlayerInputMessage { InputTick = tick, MoveX = moveX, ClientX = x, ClientZ = z };
        }

        [Test]
        public static void ServerRejectsTeleportingClientsAndKicksRepeatOffenders()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient cheat = h.Connect("Cheat");
            PlayerSession s = cheat.Session;
            float x = s.Character.X;
            float z = s.Character.Z;
            uint tick = 1;
            cheat.Conn.SendUnreliable(InputAt(tick++, x, z, 1f));
            h.Step(1);
            Assert.Equal(0L, s.TotalViolations, "honest input accepted");
            Assert.True(s.Character.X > x, "server moved the player from the input");

            float before = s.Character.X;
            cheat.Conn.SendUnreliable(InputAt(tick++, x + 500f, z, 1f));
            h.Step(1);
            Assert.Equal(1L, s.TotalViolations, "teleport detected");
            Assert.AtMost(before + 1f, s.Character.X, "authoritative position did not jump");

            for (int i = 0; i < 12 && cheat.Conn.State == ClientState.Connected; i++)
            {
                cheat.Conn.SendUnreliable(InputAt(tick++, x + 1000f + i * 100f, z, 1f));
                h.Step(1);
            }
            h.Step(2);
            Assert.Equal(ClientState.Disconnected, cheat.Conn.State, "kicked");
            Assert.Equal(DisconnectReason.Kicked, cheat.Conn.DisconnectReason);
            Assert.True(cheat.GotSystem("impossible movement"));
            Assert.Equal(0, h.Core.Sessions.Count);
        }

        [Test]
        public static void VehicleEnterExitAndTaxiJobPaysRupees()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient d = h.Connect("Driver");
            PlayerSession s = d.Session;
            Vehicle v = h.Core.Vehicles[0];
            h.Core.TeleportForTest(s, v.State.X + 20f, v.State.Z);
            d.Conn.SendReliable(new JobStartMessage { JobId = (int)JobId.TaxiDriver });
            d.Conn.SendReliable(new EnterVehicleMessage { VehicleId = v.Id });
            h.Step(3);
            Assert.True(d.GotSystem("need a vehicle"), "taxi needs a vehicle");
            Assert.True(d.GotSystem("too far"), "must be within 5 m");
            Assert.Equal(0, s.VehicleId);

            h.Core.TeleportForTest(s, v.State.X + 2f, v.State.Z);
            d.Conn.SendReliable(new EnterVehicleMessage { VehicleId = v.Id });
            h.Step(3);
            Assert.Equal(v.Id, s.VehicleId, "entered");
            Assert.Equal(s.Id, v.DriverId);
            Assert.True(d.Others.OfType<EnterVehicleMessage>().Any(m => m.Success));

            TestClient other = h.Connect("Other");
            h.Core.TeleportForTest(other.Session, v.State.X + 1f, v.State.Z);
            other.Conn.SendReliable(new EnterVehicleMessage { VehicleId = v.Id });
            h.Step(3);
            Assert.True(other.GotSystem("already driving"), "occupied");

            long walletBefore = s.WalletRupees;
            d.Conn.SendReliable(new JobStartMessage { JobId = (int)JobId.TaxiDriver });
            h.Step(3);
            JobStartMessage started = d.Others.OfType<JobStartMessage>().LastOrDefault(m => m.Accepted);
            Assert.NotNull(started, "taxi job accepted");
            Assert.True(d.GotSystem("Taxi driver"), "job name in system message");
            h.Step(400); // 20 s: long enough to have driven 600 m
            v.State.X = s.JobWaypointX;
            v.State.Z = s.JobWaypointZ;
            h.Step(3);
            JobCompleteMessage done = d.Others.OfType<JobCompleteMessage>().FirstOrDefault();
            Assert.NotNull(done, "job completed");
            Assert.True(done.PaymentRupees >= 50, "paid");
            Assert.Equal(walletBefore + done.PaymentRupees, s.WalletRupees, "wallet credited in rupees");
            Assert.Equal(ServerCore.JobPayment(JobCatalog.Get((int)JobId.TaxiDriver), s.JobDistance), done.PaymentRupees);

            d.Conn.SendReliable(new ExitVehicleMessage { VehicleId = v.Id });
            h.Step(3);
            Assert.Equal(0, s.VehicleId, "exited");
            Assert.Equal(0, v.DriverId);
        }

        [Test]
        public static void JobCompletedImpossiblyFastIsNotPaid()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient p = h.Connect("Rider");
            PlayerSession s = p.Session;
            p.Conn.SendReliable(new JobStartMessage { JobId = (int)JobId.FoodDelivery });
            h.Step(3);
            Assert.Equal((int)JobId.FoodDelivery, s.ActiveJobId, "food delivery works on foot");
            long wallet = s.WalletRupees;
            h.Core.TeleportForTest(s, s.JobWaypointX, s.JobWaypointZ);
            h.Step(2);
            Assert.Equal(wallet, s.WalletRupees, "no pay for a teleport");
            Assert.Equal(0, s.ActiveJobId);
            Assert.True(s.TotalViolations > 0);
        }

        [Test]
        public static void SnapshotsAreCappedAtMaxEntities()
        {
            ServerConfig cfg = Harness.SmallWorld();
            cfg.SpawnRadius = 40f;
            Harness h = new Harness(cfg);
            Assert.Equal(100, h.Core.InterestSettings.MaxEntitiesPerSnapshot, "default cap");
            TestClient[] clients = new TestClient[150];
            for (int i = 0; i < clients.Length; i++)
            {
                clients[i] = h.Connect("P" + i);
            }
            h.Step(10);
            int maxSeen = 0;
            foreach (TestClient c in clients)
            {
                foreach (int n in c.EntitiesPerTick.Values)
                {
                    Assert.AtMost(100, n, "entities per snapshot tick");
                    if (n > maxSeen)
                    {
                        maxSeen = n;
                    }
                }
            }
            Assert.Equal(100, maxSeen, "a crowded spot fills the cap exactly");
            Assert.True(clients[0].Known.Count <= 100 + 10, "client tracks about the nearest 100");
        }

        [Test]
        public static void ConnectRejectsWrongVersionBadTokenAndFullServer()
        {
            ServerConfig cfg = Harness.SmallWorld();
            cfg.MaxPlayers = 1;
            Harness h = new Harness(cfg);
            h.Connect("Only");
            TestClient extra = new TestClient(h, 50001);
            h.Core.HandlePacket(extra.EndPoint, new byte[3], 0, 3, h.Now); // garbage ignored
            Assert.Equal(1, h.Core.Sessions.Count);

            ServerConfig open = Harness.SmallWorld();
            Harness h2 = new Harness(open);
            ClientConnection bad = new ClientConnection();
            CapturingSink sink = new CapturingSink();
            bad.BeginConnect("", "NoToken", "en", 0);
            bad.Update(0, sink);
            h2.Core.HandlePacket(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 50002), sink.Last, 0, sink.Last.Length, 0);
            Assert.Equal(0, h2.Core.Sessions.Count, "empty token rejected");
            Assert.Equal(1L, h2.Core.Metrics.ConnectsRejected);

            ClientConnection full = new ClientConnection();
            full.BeginConnect("dev:", "Late", "en", 0);
            full.Update(0, sink);
            Harness h3 = new Harness(cfg);
            h3.Connect("First");
            h3.Core.HandlePacket(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 50003), sink.Last, 0, sink.Last.Length, h3.Now);
            Assert.Equal(1, h3.Core.Sessions.Count, "server full");

            byte[] old = (byte[])sink.Last.Clone();
            old[4] = 0; // protocol version 0
            long rejected = h2.Core.Metrics.ConnectsRejected;
            h2.Core.HandlePacket(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 50004), old, 0, old.Length, 0);
            Assert.Equal(rejected + 1, h2.Core.Metrics.ConnectsRejected, "old client version rejected");
        }

        private sealed class CapturingSink : IPacketSink
        {
            public byte[] Last;

            public void SendPacket(byte[] buffer, int length)
            {
                Last = new byte[length];
                System.Buffer.BlockCopy(buffer, 0, Last, 0, length);
            }
        }
    }
}
