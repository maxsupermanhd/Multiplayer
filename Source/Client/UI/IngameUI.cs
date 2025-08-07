using HarmonyLib;
using Multiplayer.Common;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld.Planet;
using Multiplayer.Client.DebugUi;

namespace Multiplayer.Client
{
    [HarmonyPatch(typeof(MainButtonsRoot), nameof(MainButtonsRoot.MainButtonsOnGUI))]
    public static class IngameUIPatch
    {
        public static List<Func<float, float>> upperLeftDrawers =
        [
            SyncDebugPanel.DoSyncDebugPanel, // Enhanced expandable debug panel
        ];
        public static List<Func<float, float>> upperRightDrawers =
        [
            DoChatAndTicksBehind,
            IngameDebug.DoDevInfo,
            IngameDebug.DoDebugModeLabel,
            IngameDebug.DoTimeDiffLabel
        ];

        private const float BtnMargin = 8f;
        private const float BtnHeight = 27f;
        private const float BtnWidth = 80f;

        public static float tps;
        private static float lastTicksAt;
        private static int lastTicks;
        private static int lastTicksMapId;

        static bool Prefix()
        {
            Text.Font = GameFont.Small;

            // Legacy debug printout disabled - now handled by SyncDebugPanel
            // if (MpVersion.IsDebug) {
            //     IngameDebug.DoDebugPrintout();
            // }

            if (Multiplayer.Client != null && Find.CurrentMap != null && Time.time - lastTicksAt > 0.5f)
            {
                var async = Find.CurrentMap.AsyncTime();

                if (lastTicksMapId != Find.CurrentMap.uniqueID)
                {
                    lastTicksMapId = Find.CurrentMap.uniqueID;
                    lastTicks = async.mapTicks;
                    tps = 0;
                }

                tps = (tps + (async.mapTicks - lastTicks) * 2f) / 2f;
                lastTicks = async.mapTicks;
                lastTicksAt = Time.time;
            }

            if (Multiplayer.IsReplay && Multiplayer.session.showTimeline || TickPatch.Simulating)
                ReplayTimeline.DrawTimeline();

            if (TickPatch.Simulating)
            {
                IngameModal.DrawModalWindow(
                    TickPatch.simulating.simTextKey.Translate(),
                    () => TickPatch.Simulating,
                    TickPatch.simulating.onCancel,
                    TickPatch.simulating.cancelButtonKey.Translate()
                );

                HandleUiEventsWhenSimulating();
            }

            if (TickPatch.Frozen)
            {
                IngameModal.DrawModalWindow(
                    "Waiting for other players",
                    () => TickPatch.Frozen,
                    MainMenuPatch.AskQuitToMainMenu,
                    "Quit"
                );
            }

            // Add extra space for the left side to avoid overlapping with game notifications.
            DoWidgetList(BtnMargin + BtnHeight + BtnMargin, upperLeftDrawers);
            DoWidgetList(BtnMargin, upperRightDrawers);

            if (Multiplayer.Client != null
                && !Multiplayer.IsReplay
                && MultiplayerStatic.ToggleChatDef.KeyDownEvent)
            {
                Event.current.Use();

                if (ChatWindow.Opened != null)
                    ChatWindow.Opened.Close();
                else
                    ChatWindow.OpenChat();
            }

            return Find.Maps.Count > 0;
        }

        private static void DoWidgetList(float y, List<Func<float, float>> widgets)
        {
            if (Multiplayer.session == null)
                return;

            foreach (var drawer in widgets)
                y += drawer(y);
        }

        private static float DoChatAndTicksBehind(float y)
        {
            if (Multiplayer.IsReplay)
                return 0;

            float x = UI.screenWidth - BtnWidth - BtnMargin;
            var session = Multiplayer.session;

            var btnRect = new Rect(x, y, BtnWidth, BtnHeight);
            var chatColor = session.players.Any(p => p.status == PlayerStatus.Desynced) ? "#ff5555" : "#dddddd";
            var hasUnread = session.hasUnread ? "*" : "";
            var chatLabel = $"{"MpChatButton".Translate()} <color={chatColor}>({session.players.Count})</color>{hasUnread}";

            TooltipHandler.TipRegion(btnRect, "MpChatHotkeyInfo".Translate() + " " + MultiplayerStatic.ToggleChatDef.MainKeyLabel);

            if (Widgets.ButtonText(btnRect, chatLabel))
            {
                ChatWindow.OpenChat();
            }

            if (!TickPatch.Simulating)
            {
                IndicatorInfo(out Color color, out string text, out bool slow);

                var indRect = new Rect(btnRect.x - 25f - 5f + 6f / 2f, btnRect.y + 6f / 2f, 19f, 19f);
                var biggerRect = new Rect(btnRect.x - 25f - 5f + 2f / 2f, btnRect.y + 2f / 2f, 23f, 23f);

                if (slow && Widgets.ButtonInvisible(biggerRect))
                    TickPatch.SetSimulation(toTickUntil: true, canEsc: true);

                Widgets.DrawRectFast(biggerRect, new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f));
                Widgets.DrawRectFast(indRect, color);
                TooltipHandler.TipRegion(indRect, new TipSignal(text, 31641624));
            }

            return BtnHeight;
        }

        private static void IndicatorInfo(out Color color, out string text, out bool slow)
        {
            int behind = TickPatch.tickUntil - TickPatch.Timer;
            text = "MpTicksBehind".Translate(behind);
            slow = false;

            if (behind > 30)
            {
                color = new Color(0.9f, 0, 0);
                text += $"\n\n{"MpLowerGameSpeed".Translate()}\n{"MpForceCatchUp".Translate()}";
                slow = true;
            }
            else if (behind > 15)
            {
                color = Color.yellow;
            }
            else
            {
                color = new Color(0.0f, 0.8f, 0.0f);
            }

            if (!WorldRendererUtility.WorldSelected)
                text += $"\n\nCurrent map avg TPS: {tps:0.00}";
        }

        private static void HandleUiEventsWhenSimulating()
        {
            if (TickPatch.simulating.canEsc && Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Escape)
            {
                TickPatch.ClearSimulating();
                Event.current.Use();
            }
        }
    }
}
