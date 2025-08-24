using HarmonyLib;
using Multiplayer.Client.Desyncs;
using Multiplayer.Common;
using RimWorld;
using Verse;

namespace Multiplayer.Client
{
    public static class ConstantTicker
    {
        public static bool ticking;

        public static void Tick()
        {
            ticking = true;

            try
            {
                TickShipCountdown(); // todo control the RNG seed here?
                TickNonSimulation();
            }
            finally
            {
                ticking = false;
            }
        }

        private static void TickNonSimulation()
        {
            DeferredStackTracing.ignoreTraces++;

            try
            {
                TickSyncCoordinator();
                TickAutosave();
            }
            finally
            {
                DeferredStackTracing.ignoreTraces--;
            }
        }

        private const float TicksPerMinute = GenTicks.TicksPerRealSecond * 60;
        private const float TicksPerIngameDay = GenDate.TicksPerDay;

        private static void TickAutosave()
        {
            if (Multiplayer.LocalServer is not { } server) return;

            if (server.settings.autosaveUnit == AutosaveUnit.Minutes)
            {
                var session = Multiplayer.session;
                session.autosaveCounter++;

                if (server.settings.autosaveInterval > 0 &&
                    session.autosaveCounter > server.settings.autosaveInterval * TicksPerMinute)
                {
                    session.autosaveCounter = 0;
                    Autosaving.DoAutosave();
                }
            } else if (server.settings.autosaveUnit == AutosaveUnit.Days && server.settings.autosaveInterval > 0)
            {
                var anyMapCounterUp =
                    Multiplayer.game.mapComps
                    .Any(m => m.autosaveCounter > server.settings.autosaveInterval * TicksPerIngameDay);

                if (anyMapCounterUp)
                {
                    Multiplayer.game.mapComps.Do(m => m.autosaveCounter = 0);
                    Autosaving.DoAutosave();
                }
            }
        }

        private static void TickSyncCoordinator()
        {
            var sync = Multiplayer.game.sync;
            if (sync.ShouldCollect && TickPatch.Timer % 30 == 0 && sync.currentOpinion != null)
            {
                sync.currentOpinion.roundMode = RoundMode.GetCurrentRoundMode();

                if (!TickPatch.Simulating && (Multiplayer.LocalServer != null || Multiplayer.arbiterInstance))
                    Multiplayer.Client.SendFragmented(Packets.Client_SyncInfo, sync.currentOpinion.Serialize());

                sync.AddClientOpinionAndCheckDesync(sync.currentOpinion);
                sync.currentOpinion = null;
            }
        }

        // Moved from RimWorld.ShipCountdown because the original one is called from Update
        private static void TickShipCountdown()
        {
            if (ShipCountdown.timeLeft > 0f)
            {
                ShipCountdown.timeLeft -= 1f / GenTicks.TicksPerRealSecond;

                if (ShipCountdown.timeLeft <= 0f)
                    ShipCountdown.CountdownEnded();
            }
        }
    }

    [HarmonyPatch(typeof(ShipCountdown), nameof(ShipCountdown.CancelCountdown))]
    static class CancelCancelCountdown
    {
        static bool Prefix() => Multiplayer.Client == null || Current.ProgramState != ProgramState.Playing;
    }

    [HarmonyPatch(typeof(ShipCountdown), nameof(ShipCountdown.ShipCountdownUpdate))]
    static class ShipCountdownUpdatePatch
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

}
