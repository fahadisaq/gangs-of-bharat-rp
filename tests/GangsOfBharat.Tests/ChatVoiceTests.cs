using System.Collections.Generic;
using GangsOfBharat.Server;
using GangsOfBharat.Shared.Net;

namespace GangsOfBharat.Tests
{
    public static class ChatVoiceTests
    {
        private const float X = 4096f;
        private const float Z = 4096f;

        [Test]
        public static void LocalChatReachesOnlyPlayersWithin30Metres()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient a = h.Connect("Asha");
            TestClient b = h.Connect("Bunty");
            TestClient c = h.Connect("Chotu");
            h.Core.TeleportForTest(a.Session, X, Z);
            h.Core.TeleportForTest(b.Session, X + 25f, Z);
            h.Core.TeleportForTest(c.Session, X + 35f, Z);
            a.Say(ChatChannel.Local, "chai peene chalein?");
            h.Step(3);
            Assert.True(b.Heard(a.Id, ChatChannel.Local, "chai peene chalein?"), "b at 25 m hears");
            Assert.True(a.Heard(a.Id, ChatChannel.Local, "chai peene chalein?"), "sender sees own message");
            Assert.False(c.Heard(a.Id, ChatChannel.Local, "chai peene chalein?"), "c at 35 m does not");
        }

        [Test]
        public static void GlobalChatHasSlowMode()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient a = h.Connect("A");
            TestClient b = h.Connect("B");
            h.Core.TeleportForTest(b.Session, 100f, 100f);
            a.Say(ChatChannel.Global, "first");
            h.Step(2);
            a.Say(ChatChannel.Global, "second");
            h.Step(2);
            Assert.True(b.Heard(a.Id, ChatChannel.Global, "first"), "global is map-wide");
            Assert.False(b.Heard(a.Id, ChatChannel.Global, "second"), "second message inside slow mode");
            Assert.True(a.GotSystem("slow mode"), "sender told about slow mode");
            h.Step(200); // 10 s
            a.Say(ChatChannel.Global, "third");
            h.Step(2);
            Assert.True(b.Heard(a.Id, ChatChannel.Global, "third"), "allowed after 10 s");
        }

        [Test]
        public static void FactionAndCrewChatAreMapWideButMembersOnly()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient cop1 = h.Connect("Cop1", "faction=police;crew=7");
            TestClient cop2 = h.Connect("Cop2", "faction=police");
            TestClient soldier = h.Connect("Jawan", "faction=army;crew=7");
            TestClient civ = h.Connect("Civ");
            h.Core.TeleportForTest(cop2.Session, 200f, 7900f);
            h.Core.TeleportForTest(soldier.Session, 7900f, 200f);
            cop1.Say(ChatChannel.Faction, "backup chahiye");
            cop1.Say(ChatChannel.Crew, "crew meet at 9");
            civ.Say(ChatChannel.Faction, "can I join?");
            civ.Say(ChatChannel.Crew, "hello crew");
            h.Step(3);
            Assert.True(cop2.Heard(cop1.Id, ChatChannel.Faction, "backup chahiye"), "faction member across the map");
            Assert.False(soldier.Heard(cop1.Id, ChatChannel.Faction, "backup chahiye"), "other faction");
            Assert.False(civ.Heard(cop1.Id, ChatChannel.Faction, "backup chahiye"), "civilian");
            Assert.True(soldier.Heard(cop1.Id, ChatChannel.Crew, "crew meet at 9"), "crew member in another faction");
            Assert.False(cop2.Heard(cop1.Id, ChatChannel.Crew, "crew meet at 9"), "not in crew");
            Assert.True(civ.GotSystem("not in a faction"));
            Assert.True(civ.GotSystem("not in a crew"));
        }

        [Test]
        public static void VipChatIsGatedToVipMembers()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient vip1 = h.Connect("Vip1", "vip=1");
            TestClient vip2 = h.Connect("Vip2", "vip=1");
            TestClient normal = h.Connect("Normal");
            vip1.Say(ChatChannel.Vip, "vip lounge");
            normal.Say(ChatChannel.Vip, "let me in");
            h.Step(3);
            Assert.True(vip2.Heard(vip1.Id, ChatChannel.Vip, "vip lounge"));
            Assert.False(normal.Heard(vip1.Id, ChatChannel.Vip, "vip lounge"), "non-VIP does not receive");
            Assert.False(vip1.Heard(normal.Id, ChatChannel.Vip, "let me in"), "non-VIP cannot send");
            Assert.True(normal.GotSystem("VIP members only"));
        }

        [Test]
        public static void AdChatChargesRupeesAndRejectsWhenBroke()
        {
            ServerConfig cfg = Harness.SmallWorld();
            cfg.AdCostRupees = 500;
            Harness h = new Harness(cfg);
            TestClient seller = h.Connect("Seller", "wallet=1000");
            TestClient reader = h.Connect("Reader");
            h.Core.TeleportForTest(reader.Session, 100f, 100f);
            seller.Say(ChatChannel.Ad, "Selling Marutha Chhotu, 3L");
            h.Step(3);
            Assert.True(reader.Heard(seller.Id, ChatChannel.Ad, "Selling Marutha Chhotu, 3L"), "ads are server-wide");
            Assert.Equal(500L, seller.Session.WalletRupees, "fee deducted");
            Assert.Equal(250L, h.Core.MediaFundRupees, "50% to Mass Media");
            Assert.Equal(250L, h.Core.TreasuryRupees);
            seller.Say(ChatChannel.Ad, "Second ad");
            h.Step(3);
            Assert.Equal(0L, seller.Session.WalletRupees);
            seller.Say(ChatChannel.Ad, "Third ad");
            h.Step(3);
            Assert.False(reader.Heard(seller.Id, ChatChannel.Ad, "Third ad"), "rejected without money");
            Assert.Equal(0L, seller.Session.WalletRupees, "never negative");
            Assert.True(seller.GotSystem("Not enough money"));
        }

        [Test]
        public static void SmsIsOneToOneAndUsesPhoneBalance()
        {
            ServerConfig cfg = Harness.SmallWorld();
            cfg.SmsCostRupees = 2;
            Harness h = new Harness(cfg);
            TestClient a = h.Connect("A", "phone=3");
            TestClient b = h.Connect("B");
            TestClient c = h.Connect("C");
            h.Core.TeleportForTest(b.Session, 8000f, 8000f);
            a.Say(ChatChannel.Sms, "kahan ho?", b.Id);
            h.Step(3);
            Assert.True(b.Heard(a.Id, ChatChannel.Sms, "kahan ho?"), "target receives at any distance");
            Assert.False(c.Heard(a.Id, ChatChannel.Sms, "kahan ho?"), "others do not");
            Assert.True(a.Heard(a.Id, ChatChannel.Sms, "kahan ho?"), "sender copy");
            Assert.Equal(1L, a.Session.PhoneBalanceRupees, "2 rupees deducted");
            a.Say(ChatChannel.Sms, "second", b.Id);
            h.Step(3);
            Assert.False(b.Heard(a.Id, ChatChannel.Sms, "second"), "no balance");
            Assert.Equal(1L, a.Session.PhoneBalanceRupees);
            Assert.True(a.GotSystem("Recharge"));
            c.Say(ChatChannel.Sms, "to nobody", 9999);
            h.Step(2);
            Assert.True(c.GotSystem("not online"));
        }

        [Test]
        public static void MuteAndBlockAreHonoured()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient a = h.Connect("Spammer", "phone=100");
            TestClient b = h.Connect("Muter");
            TestClient c = h.Connect("Blocker");
            TestClient d = h.Connect("Other");
            b.Conn.SendReliable(new SetMuteMessage { TargetPlayerId = a.Id, Enabled = true });
            c.Conn.SendReliable(new SetMuteMessage { TargetPlayerId = a.Id, Block = true, Enabled = true });
            h.Step(2);
            a.Say(ChatChannel.Local, "spam");
            a.Say(ChatChannel.Sms, "sms to blocker", c.Id);
            h.Step(3);
            Assert.False(b.Heard(a.Id, ChatChannel.Local, "spam"), "muted");
            Assert.False(c.Heard(a.Id, ChatChannel.Local, "spam"), "blocked");
            Assert.True(d.Heard(a.Id, ChatChannel.Local, "spam"), "others still hear");
            Assert.False(c.Heard(a.Id, ChatChannel.Sms, "sms to blocker"));
            Assert.True(a.GotSystem("blocked you"));
            Assert.Equal(100L, a.Session.PhoneBalanceRupees, "no charge for a refused SMS");

            b.Conn.SendReliable(new SetMuteMessage { TargetPlayerId = a.Id, Enabled = false });
            h.Step(100);
            a.Say(ChatChannel.Local, "after unmute");
            h.Step(3);
            Assert.True(b.Heard(a.Id, ChatChannel.Local, "after unmute"), "unmuted");
        }

        [Test]
        public static void AdminAnnouncementsAndSystemChannelArePrivileged()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient admin = h.Connect("ADM-27", "admin=1");
            TestClient player = h.Connect("Player");
            h.Core.TeleportForTest(player.Session, 50f, 50f);
            player.Conn.SendReliable(new SetMuteMessage { TargetPlayerId = admin.Id, Enabled = true });
            h.Step(2);
            admin.Say(ChatChannel.AdminAnnouncement, "Server restart at 05:00");
            player.Say(ChatChannel.AdminAnnouncement, "fake announcement");
            player.Say(ChatChannel.System, "fake system");
            h.Step(3);
            Assert.True(player.Heard(admin.Id, ChatChannel.AdminAnnouncement, "Server restart at 05:00"), "everyone gets it, even if muted");
            Assert.False(admin.Heard(player.Id, ChatChannel.AdminAnnouncement, "fake announcement"));
            Assert.True(player.GotSystem("Only admins"));
            Assert.True(player.GotSystem("cannot send to that channel"));
            Assert.False(admin.Chats.Exists(c => c.Text == "fake system"));
        }

        [Test]
        public static void ChatIsFilteredRateLimitedLoggedAndLocalized()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient a = h.Connect("A");
            TestClient hi = h.Connect("Hindi", "", "hi");
            a.Say(ChatChannel.Local, "you chutiya shit");
            h.Step(3);
            Assert.True(hi.Heard(a.Id, ChatChannel.Local, "you ******* ****"), "abuse words masked");
            for (int i = 0; i < 6; i++)
            {
                a.Say(ChatChannel.Local, "flood " + i);
            }
            h.Step(3);
            Assert.False(hi.Heard(a.Id, ChatChannel.Local, "flood 4"), "5 per 5 s limit (1 already used)");
            Assert.True(a.GotSystem("too fast"));

            List<ChatLogEntry> log = new List<ChatLogEntry>();
            h.Core.GetRecentChat(0, log);
            Assert.True(log.Exists(e => e.SenderId == a.Id && e.Channel == ChatChannel.Local && e.Text == "you ******* ****"), "logged with sender and channel");

            hi.Say(ChatChannel.Vip, "namaste");
            h.Step(3);
            Assert.True(hi.GotSystem("वीआईपी चैट"), "system message in Hindi");
        }

        // ------------------------------------------------------------ voice

        private static void SendVoice(TestClient c, VoiceChannel channel, int count)
        {
            for (int i = 0; i < count; i++)
            {
                VoiceFrameMessage v = new VoiceFrameMessage { Channel = channel, Sequence = (ushort)i };
                v.SetData(new byte[40], 0, 40);
                c.Conn.SendUnreliable(v);
            }
        }

        [Test]
        public static void ProximityVoiceOnlyReachesPlayersWithin40Metres()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient a = h.Connect("Speaker");
            TestClient b = h.Connect("Near");
            TestClient c = h.Connect("Far");
            TestClient d = h.Connect("Muter");
            h.Core.TeleportForTest(a.Session, X, Z);
            h.Core.TeleportForTest(b.Session, X, Z + 35f);
            h.Core.TeleportForTest(c.Session, X, Z + 45f);
            h.Core.TeleportForTest(d.Session, X + 5f, Z);
            d.Conn.SendReliable(new SetMuteMessage { TargetPlayerId = a.Id, Enabled = true });
            h.Step(2);
            SendVoice(a, VoiceChannel.Proximity, 5);
            h.Step(2);
            Assert.Equal(5, b.VoiceSenders.FindAll(s => s == a.Id).Count, "near listener gets every frame");
            Assert.Equal(0, c.VoiceSenders.Count, "listener at 45 m gets nothing");
            Assert.Equal(0, d.VoiceSenders.Count, "muted speaker not relayed");
            Assert.Equal(0, a.VoiceSenders.Count, "no echo to the speaker");
        }

        [Test]
        public static void FactionRadioReachesAllFactionMembersMapWide()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient a = h.Connect("Major", "faction=army");
            TestClient b = h.Connect("Soldier", "faction=army");
            TestClient c = h.Connect("Cop", "faction=police");
            TestClient civ = h.Connect("Civ");
            h.Core.TeleportForTest(b.Session, 100f, 8000f);
            SendVoice(a, VoiceChannel.FactionRadio, 3);
            SendVoice(civ, VoiceChannel.FactionRadio, 3);
            h.Step(2);
            Assert.Equal(3, b.VoiceSenders.FindAll(s => s == a.Id).Count, "army member across the map");
            Assert.Equal(0, c.VoiceSenders.Count, "other faction (even nearby) does not hear the radio");
            Assert.False(a.VoiceSenders.Contains(civ.Id), "civilians have no radio");
        }

        [Test]
        public static void VoiceIsRateLimited()
        {
            Harness h = new Harness(Harness.SmallWorld());
            TestClient a = h.Connect("Loud");
            TestClient b = h.Connect("Ear");
            SendVoice(a, VoiceChannel.Proximity, 100);
            h.Step(2);
            Assert.Equal(h.Core.Config.VoiceFramesPerSecond, b.VoiceSenders.Count, "frames above 60/s dropped");
            Assert.True(h.Core.Metrics.VoiceFramesDropped >= 40);
        }
    }
}
