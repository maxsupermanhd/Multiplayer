using HarmonyLib;
using Multiplayer.Client.Factions;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Multiplayer.Client.AsyncTime
{
    [HarmonyPatch]
    static class CancelMapManagersTick
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Map), nameof(Map.MapPreTick));
            yield return AccessTools.Method(typeof(Map), nameof(Map.MapPostTick));
        }

        static bool Prefix() => Multiplayer.Client == null || AsyncTimeComp.tickingMap != null;
    }

    [HarmonyPatch(typeof(Autosaver), nameof(Autosaver.AutosaverTick))]
    static class DisableAutosaver
    {
        static bool Prefix() => Multiplayer.Client == null;
    }

    [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
    static class MapUpdateMarker
    {
        public static bool updating;

        static void Prefix() => updating = true;
        static void Finalizer() => updating = false;
    }

    [HarmonyPatch]
    static class CancelMapManagersUpdate
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PowerNetManager), nameof(PowerNetManager.UpdatePowerNetsAndConnections_First));
            yield return AccessTools.Method(typeof(GlowGrid), nameof(GlowGrid.GlowGridUpdate_First));
            yield return AccessTools.Method(typeof(RegionGrid), nameof(RegionGrid.UpdateClean));
            yield return AccessTools.Method(typeof(RegionAndRoomUpdater), nameof(RegionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms));
        }

        static bool Prefix() => Multiplayer.Client == null || !MapUpdateMarker.updating;
    }

    [HarmonyPatch(typeof(DateNotifier), nameof(DateNotifier.DateNotifierTick))]
    static class DateNotifierPatch
    {
        static void Prefix(DateNotifier __instance, ref int? __state)
        {
            if (Multiplayer.Client == null && Multiplayer.RealPlayerFaction != null) return;

            Map map = __instance.FindPlayerHomeWithMinTimezone();
            if (map == null) return;

            __state = Find.TickManager.TicksGame;
            FactionContext.Push(Multiplayer.RealPlayerFaction);
            Find.TickManager.DebugSetTicksGame(map.AsyncTime().mapTicks);
        }

        static void Finalizer(int? __state)
        {
            if (!__state.HasValue) return;
            Find.TickManager.DebugSetTicksGame(__state.Value);
            FactionContext.Pop();
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.RegisterAllTickabilityFor))]
    public static class TickListAdd
    {
        static bool Prefix(Thing t)
        {
            if (Multiplayer.Client == null || t.Map == null) return true;

            AsyncTimeComp comp = t.Map.AsyncTime();
            TickerType tickerType = t.def.tickerType;

            if (t is IThingHolder || tickerType == TickerType.Normal)
                comp.tickListNormal.RegisterThing(t);
            else if (tickerType == TickerType.Rare)
                comp.tickListRare.RegisterThing(t);
            else if (tickerType == TickerType.Long)
                comp.tickListLong.RegisterThing(t);

            return false;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.DeRegisterAllTickabilityFor))]
    public static class TickListRemove
    {
        static bool Prefix(Thing t)
        {
            if (Multiplayer.Client == null || t.Map == null) return true;

            AsyncTimeComp comp = t.Map.AsyncTime();
            TickerType tickerType = t.def.tickerType;

            if (tickerType == TickerType.Normal)
                comp.tickListNormal.DeregisterThing(t);
            else if (tickerType == TickerType.Rare)
                comp.tickListRare.DeregisterThing(t);
            else if (tickerType == TickerType.Long)
                comp.tickListLong.DeregisterThing(t);

            return false;
        }
    }

    [HarmonyPatch(typeof(PawnTweener), nameof(PawnTweener.PreDrawPosCalculation))]
    static class PreDrawCalcMarker
    {
        public static Pawn calculating;

        static void Prefix(PawnTweener __instance) => calculating = __instance.pawn;
        static void Finalizer() => calculating = null;
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
    static class TickRateMultiplierPatch
    {
        static void Postfix(ref float __result)
        {
            if (PreDrawCalcMarker.calculating == null) return;
            if (Multiplayer.Client == null) return;
            if (WorldRendererUtility.WorldSelected) return;

            var map = PreDrawCalcMarker.calculating.Map ?? Find.CurrentMap;
            var asyncTime = map.AsyncTime();
            var timeSpeed = Multiplayer.IsReplay ? TickPatch.replayTimeSpeed : asyncTime.DesiredTimeSpeed;

            __result = TickPatch.Simulating ? 6 : asyncTime.ActualRateMultiplier(timeSpeed);
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Paused), MethodType.Getter)]
    static class TickManagerPausedPatch
    {
        static void Postfix(ref bool __result)
        {
            if (Multiplayer.Client == null) return;
            if (WorldRendererUtility.WorldSelected) return;
            if (FactionCreator.generatingMap) return;

            var asyncTime = Find.CurrentMap.AsyncTime();
            var timeSpeed = Multiplayer.IsReplay ? TickPatch.replayTimeSpeed : asyncTime.DesiredTimeSpeed;

            __result = asyncTime.ActualRateMultiplier(timeSpeed) == 0;
        }
    }

    [HarmonyPatch(typeof(TickManager), nameof(TickManager.Notify_GeneratedPotentiallyHostileMap))]
    static class GeneratedHostileMapPatch
    {
        static bool Prefix() => Multiplayer.Client == null;

        static void Postfix()
        {
            if (Multiplayer.Client == null) return;

            // The newly generated map
            Find.Maps.LastOrDefault()?.AsyncTime().slower.SignalForceNormalSpeedShort();
        }
    }

    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), typeof(Letter), typeof(string), typeof(int), typeof(bool))]
    static class ReceiveLetterPause
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand as MethodInfo == AccessTools.PropertyGetter(typeof(Prefs), nameof(Prefs.AutomaticPauseMode)))
                    inst.operand = AccessTools.Method(typeof(ReceiveLetterPause), nameof(AutomaticPauseMode));
                else if (inst.operand as MethodInfo == AccessTools.Method(typeof(TickManager), nameof(TickManager.Pause)))
                    inst.operand = AccessTools.Method(typeof(ReceiveLetterPause), nameof(PauseOnLetter));

                yield return inst;
            }
        }

        private static AutomaticPauseMode AutomaticPauseMode()
        {
            return Multiplayer.Client != null
                ? (AutomaticPauseMode)Multiplayer.GameComp.pauseOnLetter
                : Prefs.AutomaticPauseMode;
        }

        private static void PauseOnLetter(TickManager manager)
        {
            if (Multiplayer.Client == null)
            {
                manager.Pause();
                return;
            }

            if (Multiplayer.GameComp.asyncTime)
            {
                var tickable = (ITickable)Multiplayer.MapContext.AsyncTime() ?? Multiplayer.AsyncWorldTime;
                tickable.SetDesiredTimeSpeed(TimeSpeed.Paused);
                Multiplayer.GameComp.ResetAllTimeVotes(tickable.TickableId);
            }
            else
            {
                Multiplayer.AsyncWorldTime.SetTimeEverywhere(TimeSpeed.Paused);
                foreach (var tickable in TickPatch.AllTickables)
                    Multiplayer.GameComp.ResetAllTimeVotes(tickable.TickableId);
            }
        }
    }
}
