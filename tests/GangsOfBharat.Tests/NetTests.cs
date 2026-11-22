using System;
using System.Collections.Generic;
using GangsOfBharat.Shared.Net;

namespace GangsOfBharat.Tests
{
    public static class NetTests
    {
        private static T RoundTrip<T>(T message) where T : NetMessage
        {
            byte[] bytes = Protocol.Encode(message);
            NetMessage back = Protocol.Decode(bytes, 0, bytes.Length);
            Assert.NotNull(back, "decoded " + message.Type);
            Assert.Equal(message.Type, back.Type);
            return (T)back;
        }

        [Test]
        public static void EveryMessageTypeRoundTrips()
        {
            HashSet<MessageType> covered = new HashSet<MessageType>();

            ConnectMessage c = RoundTrip(new ConnectMessage { SessionToken = "tok-123", PlayerName = "राजू", Language = "hi" });
            Assert.Equal("tok-123", c.SessionToken);
            Assert.Equal("राजू", c.PlayerName);
            Assert.Equal("hi", c.Language);
            Assert.Equal(ProtocolConstants.ProtocolVersion, c.ProtocolVersion);
            covered.Add(c.Type);

            ConnectAcceptMessage a = RoundTrip(new ConnectAcceptMessage
            {
                PlayerEntityId = 777, ServerTick = 123456, TickRate = 20, SpawnX = 4096.5f, SpawnY = 2f, SpawnZ = 100.25f,
                WalletRupees = 50000000000L, PhoneBalanceRupees = 50, FactionId = 1, IsVip = true,
            });
            Assert.Equal(777, a.PlayerEntityId);
            Assert.Equal(123456u, a.ServerTick);
            Assert.Equal((byte)20, a.TickRate);
            Assert.Near(4096.5f, a.SpawnX, 0.01);
            Assert.Near(100.25f, a.SpawnZ, 0.01);
            Assert.Equal(50000000000L, a.WalletRupees);
            Assert.Equal(50L, a.PhoneBalanceRupees);
            Assert.True(a.IsVip);
            covered.Add(a.Type);

            ConnectRejectMessage rj = RoundTrip(new ConnectRejectMessage { Reason = RejectReason.ServerFull });
            Assert.Equal(RejectReason.ServerFull, rj.Reason);
            covered.Add(rj.Type);

            DisconnectMessage d = RoundTrip(new DisconnectMessage { Reason = DisconnectReason.Kicked });
            Assert.Equal(DisconnectReason.Kicked, d.Reason);
            covered.Add(d.Type);

            PingMessage ping = RoundTrip(new PingMessage { ClientTimeMs = 99999 });
            Assert.Equal(99999u, ping.ClientTimeMs);
            covered.Add(ping.Type);

            PongMessage pong = RoundTrip(new PongMessage { ClientTimeMs = 5, ServerTick = 6 });
            Assert.Equal(5u, pong.ClientTimeMs);
            Assert.Equal(6u, pong.ServerTick);
            covered.Add(pong.Type);

            PlayerInputMessage inp = RoundTrip(new PlayerInputMessage
            {
                InputTick = 42, MoveX = 0.5f, MoveZ = -1f, Yaw = 1.2f, Buttons = InputButtons.Sprint | InputButtons.Brake,
                Throttle = 1f, Steer = -0.25f, ClientX = 10f, ClientY = 0f, ClientZ = 20f,
            });
            Assert.Equal(42u, inp.InputTick);
            Assert.Near(0.5, inp.MoveX, 0.01);
            Assert.Near(-1, inp.MoveZ, 0.01);
            Assert.Near(1.2, inp.Yaw, 0.002);
            Assert.Equal(InputButtons.Sprint | InputButtons.Brake, inp.Buttons);
            Assert.Near(-0.25, inp.Steer, 0.01);
            Assert.Near(20, inp.ClientZ, 0.01);
            covered.Add(inp.Type);

            WorldSnapshotMessage snap = new WorldSnapshotMessage { ServerTick = 9, LastProcessedInputTick = 8, SelfX = 1, SelfZ = 2, SelfVehicleId = 3 };
            for (int i = 0; i < 70; i++)
            {
                EntityState e = new EntityState
                {
                    Id = 100 + i, Kind = i % 2 == 0 ? EntityKind.Player : EntityKind.Vehicle, Spawn = i % 3 == 0,
                    ModelId = i % 3 == 0 ? 18 : 0, FactionId = (byte)(i % 3 == 0 ? 2 : 0), X = 1000 + i, Y = 1, Z = 2000 - i,
                    Yaw = 0.5f, LinkedId = i % 5 == 0 ? 7 : 0,
                };
                snap.AddEntity(ref e);
            }
            snap.AddDespawn(55);
            snap.AddDespawn(66);
            WorldSnapshotMessage s2 = RoundTrip(snap);
            Assert.Equal(9u, s2.ServerTick);
            Assert.Equal(8u, s2.LastProcessedInputTick);
            Assert.Equal(3, s2.SelfVehicleId);
            Assert.Equal(70, s2.EntityCount);
            for (int i = 0; i < 70; i++)
            {
                EntityState e = snap.Entities[i];
                EntityState g = s2.Entities[i];
                Assert.Equal(e.Id, g.Id);
                Assert.Equal(e.Kind, g.Kind);
                Assert.Equal(e.Spawn, g.Spawn);
                Assert.Equal(e.ModelId, g.ModelId);
                Assert.Equal(e.FactionId, g.FactionId);
                Assert.Near(e.X, g.X, 0.01);
                Assert.Near(e.Z, g.Z, 0.01);
                Assert.Equal(e.LinkedId, g.LinkedId);
            }
            Assert.Equal(2, s2.DespawnCount);
            Assert.Equal(66, s2.Despawns[1]);
            covered.Add(s2.Type);

            ChatMessage chat = RoundTrip(new ChatMessage { Channel = ChatChannel.Sms, SenderId = 4, TargetId = 5, SenderName = "Raju", Text = "Kal milte hain। कल मिलते हैं" });
            Assert.Equal(ChatChannel.Sms, chat.Channel);
            Assert.Equal(5, chat.TargetId);
            Assert.Equal("Kal milte hain। कल मिलते हैं", chat.Text);
            covered.Add(chat.Type);

            VoiceFrameMessage v = new VoiceFrameMessage { Channel = VoiceChannel.FactionRadio, SenderId = 12, Sequence = 65000 };
            byte[] opus = new byte[40];
            for (int i = 0; i < opus.Length; i++)
            {
                opus[i] = (byte)(i * 7);
            }
            v.SetData(opus, 0, opus.Length);
            VoiceFrameMessage v2 = RoundTrip(v);
            Assert.Equal(VoiceChannel.FactionRadio, v2.Channel);
            Assert.Equal((ushort)65000, v2.Sequence);
            Assert.Equal(40, v2.Length);
            Assert.Equal(unchecked((byte)(39 * 7)), v2.Data[39]);
            covered.Add(v2.Type);

            EnterVehicleMessage ev = RoundTrip(new EnterVehicleMessage { VehicleId = 3, PlayerId = 300, Success = true });
            Assert.Equal(3, ev.VehicleId);
            Assert.True(ev.Success);
            covered.Add(ev.Type);

            ExitVehicleMessage xv = RoundTrip(new ExitVehicleMessage { VehicleId = 3, PlayerId = 300, Success = true });
            Assert.Equal(300, xv.PlayerId);
            covered.Add(xv.Type);

            JobStartMessage js = RoundTrip(new JobStartMessage { JobId = 8, Accepted = true, WaypointX = 500, WaypointZ = 600 });
            Assert.Equal(8, js.JobId);
            Assert.Near(600, js.WaypointZ, 0.01);
            covered.Add(js.Type);

            JobCompleteMessage jc = RoundTrip(new JobCompleteMessage { JobId = 8, PaymentRupees = 450, WalletRupees = 950 });
            Assert.Equal(450L, jc.PaymentRupees);
            Assert.Equal(950L, jc.WalletRupees);
            covered.Add(jc.Type);

            SetMuteMessage sm = RoundTrip(new SetMuteMessage { TargetPlayerId = 9, Block = true, Enabled = true });
            Assert.Equal(9, sm.TargetPlayerId);
            Assert.True(sm.Block);
            covered.Add(sm.Type);

            foreach (MessageType t in Enum.GetValues(typeof(MessageType)))
            {
                if (t == MessageType.None)
                {
                    continue;
                }
                Assert.NotNull(Protocol.Create(t), "factory for " + t);
                Assert.True(covered.Contains(t), "round trip test covers " + t);
            }
        }

        [Test]
        public static void ChatChannelsMatchCommunicationDesign()
        {
            string[] expected = { "Local", "Global", "Faction", "Crew", "Vip", "Ad", "Sms", "AdminAnnouncement", "System" };
            Assert.SequenceEqual(expected, Enum.GetNames(typeof(ChatChannel)));
            Assert.SequenceEqual(new[] { "Proximity", "FactionRadio" }, Enum.GetNames(typeof(VoiceChannel)));
        }

        [Test]
        public static void MalformedMessagesDecodeToNull()
        {
            Assert.True(Protocol.Decode(new byte[] { 200 }, 0, 1) == null, "unknown type");
            byte[] chat = Protocol.Encode(new ChatMessage { Channel = ChatChannel.Local, Text = "hello there" });
            Assert.True(Protocol.Decode(chat, 0, chat.Length - 3) == null, "truncated chat");
            VoiceFrameMessage v = new VoiceFrameMessage();
            Assert.True(ThrowsOnOversize(v), "SetData refuses frames above the cap");
        }

        private static bool ThrowsOnOversize(VoiceFrameMessage v)
        {
            try
            {
                v.SetData(new byte[ProtocolConstants.MaxVoiceBytes + 1], 0, ProtocolConstants.MaxVoiceBytes + 1);
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return true;
            }
        }

        // ------------------------------------------------------------ reliability

        private sealed class Collector : IMessageReceiver
        {
            public readonly List<int> Reliable = new List<int>();
            public readonly List<int> Unreliable = new List<int>();

            public void OnMessage(byte[] buffer, int offset, int length, bool reliable)
            {
                int value = BitConverter.ToInt32(buffer, offset);
                (reliable ? Reliable : Unreliable).Add(value);
            }
        }

        /// <summary>Simulated network link with loss, random delay (reordering) and duplication.</summary>
        private sealed class LossyLink : IPacketSink
        {
            private readonly Random _rng;
            private readonly double _loss;
            private readonly double _duplicate;
            public readonly List<KeyValuePair<double, byte[]>> InFlight = new List<KeyValuePair<double, byte[]>>();
            public double Now;
            public int Sent;
            public int Dropped;

            public LossyLink(int seed, double loss, double duplicate)
            {
                _rng = new Random(seed);
                _loss = loss;
                _duplicate = duplicate;
            }

            public void SendPacket(byte[] buffer, int length)
            {
                Sent++;
                if (_rng.NextDouble() < _loss)
                {
                    Dropped++;
                    return;
                }
                byte[] copy = new byte[length];
                Buffer.BlockCopy(buffer, 0, copy, 0, length);
                InFlight.Add(new KeyValuePair<double, byte[]>(Now + 10 + _rng.NextDouble() * 120, copy));
                if (_rng.NextDouble() < _duplicate)
                {
                    InFlight.Add(new KeyValuePair<double, byte[]>(Now + 10 + _rng.NextDouble() * 120, copy));
                }
            }

            public void Deliver(ReliableChannel to, IMessageReceiver receiver)
            {
                for (int i = InFlight.Count - 1; i >= 0; i--)
                {
                    if (InFlight[i].Key <= Now)
                    {
                        byte[] p = InFlight[i].Value;
                        InFlight.RemoveAt(i);
                        to.ReadPacket(p, 0, p.Length, Now, receiver);
                    }
                }
            }
        }

        [Test]
        public static void ReliableMessagesArriveOnceInOrderDespiteLossAndReordering()
        {
            ReliableChannel a = new ReliableChannel();
            ReliableChannel b = new ReliableChannel();
            LossyLink ab = new LossyLink(11, 0.3, 0.1);
            LossyLink ba = new LossyLink(12, 0.3, 0.1);
            Collector atB = new Collector();
            Collector atA = new Collector();
            int total = 600;
            int queued = 0;
            for (double now = 0; now < 60000; now += 20)
            {
                ab.Now = now;
                ba.Now = now;
                for (int k = 0; k < 3 && queued < total; k++)
                {
                    if (a.QueueReliable(BitConverter.GetBytes(queued), 0, 4))
                    {
                        queued++;
                    }
                }
                a.QueueUnreliable(BitConverter.GetBytes(-1), 0, 4);
                a.Flush(now, ab, true);
                b.Flush(now, ba, true);
                ab.Deliver(b, atB);
                ba.Deliver(a, atA);
                if (queued == total && atB.Reliable.Count == total && a.PendingReliableCount == 0)
                {
                    break;
                }
            }
            Assert.Equal(total, atB.Reliable.Count, "all reliable messages delivered exactly once");
            for (int i = 0; i < total; i++)
            {
                Assert.Equal(i, atB.Reliable[i], "in order at " + i);
            }
            Assert.Equal(0, a.PendingReliableCount, "sender saw every ack");
            Assert.True(a.ReliableResends > 0, "losses caused resends");
            Assert.True(b.DuplicatePacketsDropped > 0, "duplicates were detected");
            Assert.True(ab.Dropped > 0);
            Assert.True(atB.Unreliable.Count < ab.Sent, "unreliable messages are not resent");
        }

        [Test]
        public static void UnreliablePassThroughAndPacketSplitting()
        {
            ReliableChannel a = new ReliableChannel();
            ReliableChannel b = new ReliableChannel();
            LossyLink link = new LossyLink(5, 0.0, 0.0);
            Collector got = new Collector();
            byte[] big = new byte[400];
            for (int i = 0; i < 10; i++)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(i), 0, big, 0, 4);
                a.QueueUnreliable(big, 0, big.Length);
            }
            int packets = a.Flush(0, link, false);
            Assert.True(packets >= 4, "10 x 400 bytes needs several 1200-byte packets, got " + packets);
            foreach (KeyValuePair<double, byte[]> p in link.InFlight)
            {
                Assert.AtMost(ProtocolConstants.MaxPacketBytes, p.Value.Length, "packet size");
            }
            link.Now = 1000;
            link.Deliver(b, got);
            got.Unreliable.Sort();
            Assert.SequenceEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, got.Unreliable);
        }

        [Test]
        public static void HeaderRejectsWrongProtocolAndVersion()
        {
            ReliableChannel a = new ReliableChannel();
            LossyLink link = new LossyLink(1, 0, 0);
            a.Flush(0, link, true);
            byte[] packet = link.InFlight[0].Value;
            Assert.Equal(PacketReadResult.Ok, PacketHeader.Validate(packet, 0, packet.Length));
            byte[] wrongVersion = (byte[])packet.Clone();
            wrongVersion[4] = 99;
            Assert.Equal(PacketReadResult.WrongVersion, PacketHeader.Validate(wrongVersion, 0, wrongVersion.Length));
            byte[] wrongId = (byte[])packet.Clone();
            wrongId[0] ^= 0xFF;
            Assert.Equal(PacketReadResult.WrongProtocol, PacketHeader.Validate(wrongId, 0, wrongId.Length));
            Assert.Equal(PacketReadResult.Malformed, PacketHeader.Validate(packet, 0, 5));
            ReliableChannel b = new ReliableChannel();
            Collector c = new Collector();
            Assert.Equal(PacketReadResult.Ok, b.ReadPacket(packet, 0, packet.Length, 0, c));
            Assert.Equal(PacketReadResult.Duplicate, b.ReadPacket(packet, 0, packet.Length, 0, c));
        }

        [Test]
        public static void SequenceComparisonWraps()
        {
            Assert.True(ReliableChannel.SequenceGreaterThan(1, 0));
            Assert.True(ReliableChannel.SequenceGreaterThan(0, 65535));
            Assert.False(ReliableChannel.SequenceGreaterThan(65535, 0));
            Assert.True(ReliableChannel.SequenceGreaterThan(100, 65000));
        }
    }
}
