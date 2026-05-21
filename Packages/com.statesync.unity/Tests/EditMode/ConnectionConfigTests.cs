using NUnit.Framework;
using StateSync.Config;

namespace StateSync.Tests
{
    [TestFixture]
    public class ConnectionConfigTests
    {
        [SetUp]
        public void SetUp() => ConnectionConfig.ResetForTests();

        [TearDown]
        public void TearDown() => ConnectionConfig.ResetForTests();

        [Test]
        public void Defaults_When_No_Args()
        {
            ConnectionConfig.ParseFrom(new[] { "Game.exe" });
            Assert.IsTrue(ConnectionConfig.IsHost);
            Assert.AreEqual(5050, ConnectionConfig.WsPort);
        }

        [Test]
        public void Mode_Host_Parses()
        {
            ConnectionConfig.ParseFrom(new[] { "Game.exe", "--ws-mode=host" });
            Assert.IsTrue(ConnectionConfig.IsHost);
        }

        [Test]
        public void Mode_Follower_Parses()
        {
            ConnectionConfig.ParseFrom(new[] { "Game.exe", "--ws-mode=follower" });
            Assert.IsFalse(ConnectionConfig.IsHost);
        }

        [Test]
        public void Mode_Case_Insensitive()
        {
            ConnectionConfig.ParseFrom(new[] { "Game.exe", "--ws-mode=FOLLOWER" });
            Assert.IsFalse(ConnectionConfig.IsHost);
        }

        [Test]
        public void Port_Parses()
        {
            ConnectionConfig.ParseFrom(new[] { "Game.exe", "--ws-port=5051" });
            Assert.AreEqual(5051, ConnectionConfig.WsPort);
        }

        [Test]
        public void Invalid_Port_Falls_Back_To_Default()
        {
            ConnectionConfig.ParseFrom(new[] { "Game.exe", "--ws-port=notanumber" });
            Assert.AreEqual(5050, ConnectionConfig.WsPort);
        }

        [Test]
        public void Out_Of_Range_Port_Falls_Back_To_Default()
        {
            ConnectionConfig.ParseFrom(new[] { "Game.exe", "--ws-port=70000" });
            Assert.AreEqual(5050, ConnectionConfig.WsPort);
        }

        [Test]
        public void Unknown_Mode_Falls_Back_To_Default()
        {
            ConnectionConfig.ParseFrom(new[] { "Game.exe", "--ws-mode=spectator" });
            Assert.IsTrue(ConnectionConfig.IsHost); // default
        }

        [Test]
        public void Order_Does_Not_Matter()
        {
            ConnectionConfig.ParseFrom(new[] { "Game.exe", "--ws-port=5060", "--ws-mode=follower" });
            Assert.IsFalse(ConnectionConfig.IsHost);
            Assert.AreEqual(5060, ConnectionConfig.WsPort);
        }

        [Test]
        public void Override_Takes_Precedence_Over_Cli()
        {
            ConnectionConfig.ParseFrom(new[] { "Game.exe", "--ws-mode=host", "--ws-port=5050" });
            ConnectionConfig.OverrideIsHost = false;
            ConnectionConfig.OverridePort = 5099;
            Assert.IsFalse(ConnectionConfig.IsHost);
            Assert.AreEqual(5099, ConnectionConfig.WsPort);
        }

        [Test]
        public void Null_Args_Yields_Defaults()
        {
            ConnectionConfig.ParseFrom(null);
            Assert.IsTrue(ConnectionConfig.IsHost);
            Assert.AreEqual(5050, ConnectionConfig.WsPort);
        }
    }
}
