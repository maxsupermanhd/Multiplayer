using Ionic.Zlib;
using Multiplayer.Common;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class ClientPlayingState : ClientBaseState
    {
        public ClientPlayingState(ConnectionBase connection) : base(connection)
        {
        }

        [PacketHandler(Packets.Server_TimeControl)]
        public void HandleTimeControl(ByteReader data)
        {
            int tickUntil = data.ReadInt32();
            int sentCmds = data.ReadInt32();
            float stpt = data.ReadFloat();

            if (Multiplayer.session.remoteTickUntil >= tickUntil) return;

            TickPatch.serverTimePerTick = stpt;
            Multiplayer.session.remoteTickUntil = tickUntil;
            Multiplayer.session.remoteSentCmds = sentCmds;
            Multiplayer.session.ProcessTimeControl();
        }

        [PacketHandler(Packets.Server_KeepAlive)]
        public void HandleKeepAlive(ByteReader data)
        {
            int id = data.ReadInt32();
            int ticksBehind = TickPatch.tickUntil - TickPatch.Timer;

            connection.Send(
                Packets.Client_KeepAlive,
                ByteWriter.GetBytes(id, ticksBehind, TickPatch.Simulating, TickPatch.workTicks),
                false
            );
        }

        [PacketHandler(Packets.Server_Command)]
        public void HandleCommand(ByteReader data)
        {
            ScheduledCommand cmd = ScheduledCommand.Deserialize(data);
            cmd.issuedBySelf = data.ReadBool();
            Session.ScheduleCommand(cmd);

            Multiplayer.session.receivedCmds++;
            Multiplayer.session.ProcessTimeControl();
        }

        [PacketHandler(Packets.Server_PlayerList)]
        public void HandlePlayerList(ByteReader data)
        {
            var action = data.ReadEnum<PlayerListAction>();
            if (action == PlayerListAction.Add)
            {
                var info = PlayerInfo.Read(data);
                if (!Multiplayer.session.players.Contains(info))
                {
                    ServerLog.Log($"PlayerList: Adding player {info.id}:{info.username}");
                    Multiplayer.session.players.Add(info);
                }
                else
                {
                    ServerLog.Error($"PlayerList: Adding player {info.id}:{info.username} - player already exists");
                }
            }
            else if (action == PlayerListAction.Remove)
            {
                int id = data.ReadInt32();
                ServerLog.Log($"PlayerList: Removing player with id {id}");
                var matches = Multiplayer.session.players.RemoveAll(p => p.id == id);
                if (matches > 1)
                {
                    ServerLog.Error($"PlayerList: Removing player with id {id} -- occurred {matches} times. This should not happen");
                }
            }
            else if (action == PlayerListAction.List)
            {
                int count = data.ReadInt32();
                ServerLog.Log($"PlayerList: Received player list with {count} entries");

                Multiplayer.session.players.Clear();
                for (int i = 0; i < count; i++)
                {
                    var info = PlayerInfo.Read(data);
                    ServerLog.Log($"PlayerList: Adding player from list {info.id}:{info.username}");
                    Multiplayer.session.players.Add(info);
                }
            }
            else if (action == PlayerListAction.Latencies)
            {
                int count = data.ReadInt32();

                for (int i = 0; i < count; i++)
                {
                    var id = data.ReadInt32();
                    var player = Multiplayer.session.GetPlayerInfo(id);
                    if (player == null)
                    {
                        ServerLog.Log($"PlayerList: Received latency info for unknown player with id {id}");
                        continue;
                    }
                    player.latency = data.ReadInt32();
                    player.ticksBehind = data.ReadInt32();
                    player.simulating = data.ReadBool();
                    player.frameTime = data.ReadFloat();
                }
            }
            else if (action == PlayerListAction.Status)
            {
                var id = data.ReadInt32();
                var status = data.ReadEnum<PlayerStatus>();
                var player = Multiplayer.session.GetPlayerInfo(id);

                if (player == null)
                {
                    ServerLog.Log($"PlayerList: Received player status ({status}) for unknown player with id {id}");
                }
                else
                {
                    player.status = status;
                }
            }
        }

        [PacketHandler(Packets.Server_Chat)]
        public void HandleChat(ByteReader data)
        {
            string msg = data.ReadString();
            Multiplayer.session.AddMsg(msg);
        }

        [PacketHandler(Packets.Server_Cursor)]
        public void HandleCursor(ByteReader data)
        {
            int playerId = data.ReadInt32();
            var player = Multiplayer.session.GetPlayerInfo(playerId);
            if (player == null) return;

            byte seq = data.ReadByte();
            if (seq < player.cursorSeq && player.cursorSeq - seq < 128) return;

            byte map = data.ReadByte();
            player.map = map;

            if (map == byte.MaxValue) return;

            byte icon = data.ReadByte();
            float x = data.ReadShort() / 10f;
            float z = data.ReadShort() / 10f;

            player.cursorSeq = seq;
            player.lastCursor = player.cursor;
            player.lastDelta = Multiplayer.clock.ElapsedMillisDouble() - player.updatedAt;
            player.cursor = new Vector3(x, 0, z);
            player.updatedAt = Multiplayer.clock.ElapsedMillisDouble();
            player.cursorIcon = icon;

            short dragXRaw = data.ReadShort();
            if (dragXRaw != -1)
            {
                float dragX = dragXRaw / 10f;
                float dragZ = data.ReadShort() / 10f;

                player.dragStart = new Vector3(dragX, 0, dragZ);
            }
            else
            {
                player.dragStart = PlayerInfo.Invalid;
            }
        }

        [PacketHandler(Packets.Server_Selected)]
        public void HandleSelected(ByteReader data)
        {
            int playerId = data.ReadInt32();
            var player = Multiplayer.session.GetPlayerInfo(playerId);
            if (player == null) return;

            bool reset = data.ReadBool();

            if (reset)
                player.selectedThings.Clear();

            int[] add = data.ReadPrefixedInts();
            for (int i = 0; i < add.Length; i++)
                player.selectedThings[add[i]] = Time.realtimeSinceStartup;

            int[] remove = data.ReadPrefixedInts();
            for (int i = 0; i < remove.Length; i++)
                player.selectedThings.Remove(remove[i]);
        }

        [PacketHandler(Packets.Server_PingLocation)]
        public void HandlePing(ByteReader data)
        {
            int player = data.ReadInt32();
            int map = data.ReadInt32();
            PlanetTile planetTile = new(data.ReadInt32(), data.ReadInt32());
            var loc = new Vector3(data.ReadFloat(), data.ReadFloat(), data.ReadFloat());

            Session.locationPings.ReceivePing(player, map, planetTile, loc);
        }

        [PacketHandler(Packets.Server_MapResponse)]
        public void HandleMapResponse(ByteReader data)
        {
            int mapId = data.ReadInt32();

            int mapCmdsLen = data.ReadInt32();
            List<ScheduledCommand> mapCmds = new List<ScheduledCommand>(mapCmdsLen);
            for (int j = 0; j < mapCmdsLen; j++)
                mapCmds.Add(ScheduledCommand.Deserialize(new ByteReader(data.ReadPrefixedBytes())));

            Session.dataSnapshot.MapCmds[mapId] = mapCmds;

            byte[] mapData = GZipStream.UncompressBuffer(data.ReadPrefixedBytes());
            Session.dataSnapshot.MapData[mapId] = mapData;

            //ClientJoiningState.ReloadGame(TickPatch.tickUntil, Find.Maps.Select(m => m.uniqueID).Concat(mapId).ToList());
            // todo Multiplayer.client.Send(Packets.CLIENT_MAP_LOADED);
        }

        [PacketHandler(Packets.Server_Notification)]
        public void HandleNotification(ByteReader data)
        {
            string key = data.ReadString();
            string[] args = data.ReadPrefixedStrings();

            var msg = key.Translate(Array.ConvertAll(args, s => (NamedArgument)s));
            Messages.Message(msg, MessageTypeDefOf.SilentInput, false);
            ServerLog.Log($"Notification: {msg} ({key}, {args.Join(", ")})");
        }

        [PacketHandler(Packets.Server_SyncInfo, allowFragmented: true)]
        public void HandleDesyncCheck(ByteReader data)
        {
            Multiplayer.game?.sync.AddClientOpinionAndCheckDesync(ClientSyncOpinion.Deserialize(data));
        }

        [PacketHandler(Packets.Server_Freeze)]
        public void HandleFreze(ByteReader data)
        {
            bool frozen = data.ReadBool();
            int frozenAt = data.ReadInt32();

            TickPatch.serverFrozen = frozen;
            TickPatch.frozenAt = frozenAt;
        }

        [PacketHandler(Packets.Server_Traces, allowFragmented: true)]
        public void HandleTraces(ByteReader data)
        {
            var type = data.ReadEnum<TracesPacket>();

            if (type == TracesPacket.Request)
            {
                var tick = data.ReadInt32();
                var diffAt = data.ReadInt32();
                var playerId = data.ReadInt32();

                var info = Multiplayer.game.sync.knownClientOpinions.FirstOrDefault(b => b.startTick == tick);
                var response = info?.GetFormattedStackTracesForRange(diffAt);

                connection.Send(Packets.Client_Traces, TracesPacket.Response, playerId, GZipStream.CompressString(response));
            }
            else if (type == TracesPacket.Transfer)
            {
                var traces = data.ReadPrefixedBytes();
                Multiplayer.session.desyncTracesFromHost = GZipStream.UncompressString(traces);
            }
        }

        [PacketHandler(Packets.Server_Debug)]
        public void HandleDebug(ByteReader data)
        {
            Rejoiner.DoRejoin();
        }

        [PacketHandler(Packets.Server_SetFaction)]
        public void HandleSetFaction(ByteReader data)
        {
            int player = data.ReadInt32();
            int factionId = data.ReadInt32();

            Session.GetPlayerInfo(player).factionId = factionId;

            if (Session.playerId == player)
            {
                Multiplayer.game.ChangeRealPlayerFaction(factionId);
                Session.myFactionId = factionId;
            }
        }
    }

}
