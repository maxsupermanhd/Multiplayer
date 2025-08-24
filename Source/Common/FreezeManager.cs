﻿using System.Linq;
using Verse;

namespace Multiplayer.Common
{
    public class FreezeManager
    {
        private bool frozen;

        public bool Frozen
        {
            get => frozen;
            private set
            {
                frozen = value;
                Server.SendToPlaying(Packets.Server_Freeze, new object[] { frozen, Server.gameTimer });
            }
        }

        public MultiplayerServer Server { get; }

        public FreezeManager(MultiplayerServer server)
        {
            Server = server;
        }

        private const int MaxFreezeWaitTime = GenTicks.TicksPerRealSecond * 10; // 10 seconds

        public void Tick()
        {
            if (!Server.PlayingPlayers.Any(p => p.IsHost))
                return;

            if (!Frozen && Server.HostPlayer.frozen)
                Frozen = true;

            if (Frozen && !Server.HostPlayer.frozen && (!Server.PlayingPlayers.Any(p => p.frozen) || Server.NetTimer - Server.HostPlayer.unfrozenAt > MaxFreezeWaitTime))
                Frozen = false;
        }
    }
}
