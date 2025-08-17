using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Verse;
using Multiplayer.Client.Patches;
using RimWorld;

// ReSharper disable once CheckNamespace
namespace Multiplayer.Common
{
    // Note: the API expects this type to be Multiplayer.Common.MultiplayerAPIBridge
    public class MultiplayerAPIBridge : IAPI
    {
        public static readonly IAPI Instance = new MultiplayerAPIBridge();

        public bool IsHosting => MultiplayerServer.instance != null;

        public bool IsInMultiplayer => Client.Multiplayer.session != null;

        public string PlayerName => Client.Multiplayer.username;

        public bool IsExecutingSyncCommand => Client.Multiplayer.ExecutingCmds;

        public bool IsExecutingSyncCommandIssuedBySelf => TickPatch.currentExecutingCmdIssuedBySelf;

        public bool CanUseDevMode => Client.Multiplayer.GameComp.LocalPlayerDataOrNull?.canUseDevMode ?? false;

        public bool InInterface => Client.Multiplayer.InInterface;

        public Faction RealPlayerFaction => Client.Multiplayer.RealPlayerFaction;

        public void WatchBegin()
        {
            SyncFieldUtil.FieldWatchPrefix();
        }

        public void Watch(Type targetType, string fieldName, object target = null, object index = null)
        {
            var syncField = Sync.GetRegisteredSyncField(targetType, fieldName);

            if (syncField == null) {
                throw new ArgumentException($"{targetType}/{fieldName} not found in {target}");
            }

            syncField.Watch(target, index);
        }

        public void Watch(object target, string fieldName, object index = null)
        {
            var syncField = Sync.GetRegisteredSyncField(target.GetType(), fieldName);

            if (syncField == null) {
                throw new ArgumentException($"{fieldName} not found in {target}");
            }

            syncField.Watch(target, index);
        }

        public void Watch(string memberPath, object target = null, object index = null)
        {
            var syncField = Sync.GetRegisteredSyncField(memberPath);

            if (syncField == null) {
                throw new ArgumentException($"{memberPath} not found");
            }

            syncField.Watch(target, index);
        }

        public void WatchEnd()
        {
            SyncFieldUtil.FieldWatchPostfix();
        }

        public void RegisterAll(Assembly assembly)
        {
            Sync.RegisterAllAttributes(assembly);
            PersistentDialog.BindAll(assembly);
        }

        public ISyncField RegisterSyncField(Type targetType, string memberPath)
        {
            return Sync.RegisterSyncField(targetType, memberPath);
        }

        public ISyncField RegisterSyncField(FieldInfo field)
        {
            return Sync.RegisterSyncField(field);
        }

        public ISyncMethod RegisterSyncMethod(Type type, string methodOrPropertyName, SyncType[] argTypes = null)
        {
            return Sync.RegisterSyncMethod(type, methodOrPropertyName, argTypes);
        }

        public ISyncMethod RegisterSyncMethod(MethodInfo method, SyncType[] argTypes)
        {
            return Sync.RegisterSyncMethod(method, argTypes);
        }

        public ISyncMethod RegisterSyncMethodLambda(Type parentType, string parentMethod, int lambdaOrdinal, Type[] parentArgs = null, ParentMethodType parentMethodType = ParentMethodType.Normal)
        {
            return SyncMethod.Lambda(parentType, parentMethod, lambdaOrdinal, parentArgs, (MethodType)parentMethodType);
        }

        public ISyncMethod RegisterSyncMethodLambdaInGetter(Type parentType, string parentMethod, int lambdaOrdinal)
        {
            return SyncMethod.LambdaInGetter(parentType, parentMethod, lambdaOrdinal);
        }

        public ISyncDelegate RegisterSyncDelegate(Type inType, string nestedType, string methodName, string[] fields, Type[] args = null)
        {
            return Sync.RegisterSyncDelegate(inType, nestedType, methodName, fields, args);
        }

        public ISyncDelegate RegisterSyncDelegate(Type type, string nestedType, string method)
        {
            return Sync.RegisterSyncDelegate(type, nestedType, method);
        }

        public ISyncDelegate RegisterSyncDelegateLambda(Type parentType, string parentMethod, int lambdaOrdinal, Type[] parentArgs = null, ParentMethodType parentMethodType = ParentMethodType.Normal)
        {
            return SyncDelegate.Lambda(parentType, parentMethod, lambdaOrdinal, parentArgs, (MethodType)parentMethodType);
        }

        public ISyncDelegate RegisterSyncDelegateLambdaInGetter(Type parentType, string parentMethod, int lambdaOrdinal)
        {
            return SyncDelegate.LambdaInGetter(parentType, parentMethod, lambdaOrdinal);
        }

        public ISyncDelegate RegisterSyncDelegateLocalFunc(Type parentType, string parentMethod, string localFuncName, Type[] parentArgs = null)
        {
            return SyncDelegate.LocalFunc(parentType, parentMethod, localFuncName, parentArgs);
        }

        public void RegisterSyncWorker<T>(SyncWorkerDelegate<T> syncWorkerDelegate, Type targetType = null, bool isImplicit = false, bool shouldConstruct = false)
        {
            Sync.RegisterSyncWorker(syncWorkerDelegate, targetType, isImplicit: isImplicit, shouldConstruct: shouldConstruct);
        }

        public void RegisterDialogNodeTree(Type type, string methodOrPropertyName, SyncType[] argTypes = null)
        {
            Sync.RegisterSyncDialogNodeTree(type, methodOrPropertyName, argTypes);
        }

        public void RegisterDialogNodeTree(MethodInfo method)
        {
            Sync.RegisterSyncDialogNodeTree(method);
        }

        public void RegisterPauseLock(PauseLockDelegate pauseLock)
        {
            Sync.RegisterPauseLock(pauseLock);
        }

        public ISessionManager GetGlobalSessionManager()
        {
            return Client.Multiplayer.WorldComp.sessionManager;
        }

        public ISessionManager GetLocalSessionManager(Map map)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            return map.MpComp().sessionManager;
        }

        public void SetCurrentSessionWithTransferables(ISessionWithTransferables session)
        {
            SyncSessionWithTransferablesMarker.DrawnSessionWithTransferables = session;
        }

        public void RegisterDefaultLetterChoice(MethodInfo method, Type letterType = null) => CloseDialogsForExpiredLetters.RegisterDefaultLetterChoice(method, letterType);

        public Thing GetThingById(int id)
        {
            return Client.Multiplayer.ThingsById.GetValue(id);
        }

        public bool TryGetThingById(int id, out Thing value)
        {
            return Client.Multiplayer.ThingsById.TryGetValue(id, out value);
        }

        public IReadOnlyList<IPlayerInfo> GetPlayers()
        {
            return Client.Multiplayer.session.players.AsReadOnly();
        }

        public IPlayerInfo GetPlayerById(int id)
        {
            return Client.Multiplayer.session.GetPlayerInfo(id);
        }

        public void SetThingFilterContext(ThingFilterContext context)
        {
            ThingFilterMarkers.DrawnThingFilter = context;
        }
    }
}
