﻿using NitroxClient.Communication.Abstract;
using NitroxClient.Communication.Packets.Processors.Abstract;
using NitroxClient.GameLogic;
using NitroxClient.GameLogic.Bases;
using NitroxClient.GameLogic.Helper;
using NitroxClient.MonoBehaviours;
using NitroxModel.DataStructures.GameLogic;
using NitroxModel.Logger;
using NitroxModel.Packets;
using System;
using System.Collections.Generic;
using UnityEngine;
using Story;
using System.Reflection;
using NitroxModel.Helper;
using NitroxModel.DataStructures.Util;

namespace NitroxClient.Communication.Packets.Processors
{
    public class InitialPlayerSyncProcessor : ClientPacketProcessor<InitialPlayerSync>
    {
        private readonly IPacketSender packetSender;
        private readonly BuildThrottlingQueue buildEventQueue;
        private readonly Vehicles vehicles;
        private readonly ItemContainers itemContainers;
        private readonly EquipmentSlots equipment;
        private readonly PlayerManager remotePlayerManager;

        public InitialPlayerSyncProcessor(IPacketSender packetSender, BuildThrottlingQueue buildEventQueue, Vehicles vehicles, ItemContainers itemContainers, EquipmentSlots equipment, PlayerManager remotePlayerManager)
        {
            this.packetSender = packetSender;
            this.buildEventQueue = buildEventQueue;
            this.vehicles = vehicles;
            this.itemContainers = itemContainers;
            this.equipment = equipment;
            this.remotePlayerManager = remotePlayerManager;
        }

        public override void Process(InitialPlayerSync packet)
        {
            SetPlayerGuid(packet.PlayerGuid);
            SpawnVehicles(packet.Vehicles);
            SpawnPlayerEquipment(packet.EquippedItems); //Need Set Equipment On Vehicles before SpawnItemContainer because is Locker Is a Upgrade (VehicleStorageModule Seamoth / Prawn)
            SpawnItemContainer(packet.PlayerGuid, packet.InventoryItems); //Need Maintain SpawnItemContainer before SpawnBasePieces/SpawnInventoryItemsAfterBasePiecesFinish because is Locker already Place SpawnItemContainer Duplicated Same Item
            SpawnBasePieces(packet.BasePieces);
            SetEncyclopediaEntry(packet.PDAData.EncyclopediaEntries);
            SetPDAEntryComplete(packet.PDAData.UnlockedTechTypes);
            SetPDAEntryPartial(packet.PDAData.PartiallyUnlockedTechTypes);
            SetKnownTech(packet.PDAData.KnownTechTypes);
            SetPDALog(packet.PDAData.PDALogEntries);
            SetPlayerStats(packet.PlayerStatsData);

            bool hasBasePiecesToSpawn = packet.BasePieces.Count > 0;

            SpawnInventoryItemsAfterBasePiecesFinish(packet.InventoryItems, hasBasePiecesToSpawn);
            SpawnRemotePlayersAfterBasePiecesFinish(packet.RemotePlayerData, hasBasePiecesToSpawn);
            SetPlayerLocationAfterBasePiecesFinish(packet.PlayerSpawnData, packet.PlayerSubRootGuid, hasBasePiecesToSpawn);
        }

        private void SetPDALog(List<PDALogEntry> logEntries)
        {
            Log.Info("Received initial sync packet with " + logEntries.Count + " pda log entries");

            using (packetSender.Suppress<PDALogEntryAdd>())
            {
                Dictionary<string, PDALog.Entry> entries = (Dictionary<string, PDALog.Entry>)(typeof(PDALog).GetField("entries", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));

                foreach (PDALogEntry logEntry in logEntries)
                {
                    if (!entries.ContainsKey(logEntry.Key))
                    {
                        PDALog.EntryData entryData;
                        PDALog.GetEntryData(logEntry.Key, out entryData);
                        PDALog.Entry entry = new PDALog.Entry();
                        entry.data = entryData;
                        entry.timestamp = logEntry.Timestamp;
                        entries.Add(entryData.key, entry);

                        if (entryData.key == "Story_AuroraWarning4")
                        {
                            CrashedShipExploder.main.ReflectionCall("SwapModels", false, false, new object[] { true });
                        }
                    }
                }
            }
        }

        private void SetKnownTech(List<TechType> techTypes)
        {
            Log.Info("Received initial sync packet with " + techTypes.Count + " known tech types");

            using (packetSender.Suppress<KnownTechEntryAdd>())
            {
                foreach (TechType techType in techTypes)
                {
                    HashSet<TechType> complete = (HashSet<TechType>)(typeof(PDAScanner).GetField("complete", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
                    KnownTech.Add(techType, false);
                }
            }
        }
        
        private void SetPlayerStats(PlayerStatsData statsData)
        {
            if (statsData != null)
            {
                using (packetSender.Suppress<PlayerStats>())
                {
                    Player.main.oxygenMgr.AddOxygen(statsData.Oxygen);
                }
            }
        }

        private void SetEncyclopediaEntry(List<string> entries)
        {
            Log.Info("Received initial sync packet with " + entries.Count + " encyclopedia entries");

            using (packetSender.Suppress<PDAEncyclopediaEntryAdd>())
            {
                foreach (string entry in entries)
                {
                    PDAEncyclopedia.Add(entry, false);
                }
            }
        }
    
        private void SetPDAEntryComplete(List<TechType> pdaEntryComplete)
        {
            HashSet<TechType> complete = (HashSet<TechType>)(typeof(PDAScanner).GetField("complete", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));

            foreach (TechType item in pdaEntryComplete)
            {
                complete.Add(item);
            }

            Log.Info("PDAEntryComplete Save:" + pdaEntryComplete.Count + " Read Partial Client Final Count:" + complete.Count);

        }

        private void SetPDAEntryPartial(List<PDAEntry> entries)
        {
            List<PDAScanner.Entry> partial = (List<PDAScanner.Entry>)(typeof(PDAScanner).GetField("partial", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));

            foreach (PDAEntry entry in entries)
            {
                partial.Add(new PDAScanner.Entry { progress = entry.Progress, techType= entry.TechType, unlocked = entry.Unlocked });
            }

            Log.Info("PDAEntryPartial Save :" + entries.Count + " Read Partial Client Final Count:" + partial.Count);
        }

        private void SetPlayerGuid(string playerguid)
        {
            GuidHelper.SetNewGuid(Player.mainObject, playerguid);
            Log.Info("Received initial sync Player Guid: " + playerguid);
        }    
        
        private void SpawnPlayerEquipment(List<EquippedItemData> equippedItems)
        {
            Log.Info("Received initial sync packet with " + equippedItems.Count + " equipment items");

            using (packetSender.Suppress<EquipmentAddItem>())
            {
                equipment.AddItems(equippedItems);
            }
        }

        private void SpawnBasePieces(List<BasePiece> basePieces)
        {
            Log.Info("Received initial sync packet with " + basePieces.Count + " base pieces");

            using (packetSender.Suppress<ConstructionAmountChanged>())
            using (packetSender.Suppress<ConstructionCompleted>())
            using (packetSender.Suppress<PlaceBasePiece>())
            {
                foreach (BasePiece basePiece in basePieces)
                {
                    buildEventQueue.EnqueueBasePiecePlaced(basePiece);

                    if (basePiece.ConstructionCompleted)
                    {
                        buildEventQueue.EnqueueConstructionCompleted(basePiece.Guid, basePiece.NewBaseGuid);
                    }
                    else
                    {
                        buildEventQueue.EnqueueAmountChanged(basePiece.Guid, basePiece.ConstructionAmount);
                    }
                }
            }
        }

        private void SpawnVehicles(List<VehicleModel> vehicleModels)
        {
            Log.Info("Received initial sync packet with " + vehicleModels.Count + " vehicles");

            foreach (VehicleModel vehicle in vehicleModels)
            {
                vehicles.CreateVehicle(vehicle);
            }

        }

        private void SpawnItemContainer(string playerGuid, List<ItemData> inventoryItems)
        {
            Log.Info("Received initial sync packet with " + inventoryItems.Count + " ItemContainer");

            using (packetSender.Suppress<ItemContainerAdd>())
            {
                ItemGoalTracker itemGoalTracker = (ItemGoalTracker)typeof(ItemGoalTracker).GetField("main", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                Dictionary<TechType, List<ItemGoal>> goals = (Dictionary<TechType, List<ItemGoal>>)(typeof(ItemGoalTracker).GetField("goals", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(itemGoalTracker));

                foreach (ItemData itemdata in inventoryItems)
                {
                    GameObject item;

                    try
                    {
                        item = SerializationHelper.GetGameObject(itemdata.SerializedData);
                    }
                    catch(Exception ex)
                    {
                        Log.Error("Error deserializing item data " + itemdata.Guid + " " + ex.Message);
                        continue;
                    }

                    Pickupable pickupable = item.GetComponent<Pickupable>();

                    if (pickupable != null && itemdata.ContainerGuid == playerGuid)
                    {
                        goals.Remove(pickupable.GetTechType());  // Remove Notification Goal Event On Item Player Already have On Any Container

                        ItemsContainer container = Inventory.Get().container;
                        InventoryItem inventoryItem = new InventoryItem(pickupable);
                        inventoryItem.container = container;
                        inventoryItem.item.Reparent(container.tr);

                        container.UnsafeAdd(inventoryItem);
                    }
                    else
                    {
                        itemContainers.AddItem(itemdata);
                    }              
                }
            }

        }

        /*
         * Items should only be added after all base pieces spawn.  Since base pieces will spawn
         * gradually over multiple frames, we need to wait until that process has completely finished
         */
        private void SpawnInventoryItemsAfterBasePiecesFinish(List<ItemData> inventoryItems, bool basePiecesToSpawn)
        {
            Log.Info("Received initial sync packet with " + inventoryItems.Count + " inventory items");

            InventoryItemAdder itemAdder = new InventoryItemAdder(packetSender, itemContainers, inventoryItems);
            
            if (basePiecesToSpawn)
            {
                ThrottledBuilder.main.QueueDrained += itemAdder.AddItemsToInventories;
            }
            else
            {
                itemAdder.AddItemsToInventories(null, null);
            }
        }
        
        /*
         * This class simply encapsulates a callback method that is invoked when the throttled builder
         * is completed with the initial sync of base items.  We keep this in a new class to be able to
         * hold the relevant inventory items and use them when the time comes.  This can be later extended
         * to wait on other events if need be.
         */ 
        private class InventoryItemAdder
        {
            private IPacketSender packetSender;
            private ItemContainers itemContainers;
            private List<ItemData> inventoryItems;

            public InventoryItemAdder(IPacketSender packetSender, ItemContainers itemContainers, List<ItemData> inventoryItems)
            {
                this.packetSender = packetSender;
                this.itemContainers = itemContainers;
                this.inventoryItems = inventoryItems;
            }

            public void AddItemsToInventories(object sender, EventArgs eventArgs)
            {
                Log.Info("Initial sync inventory items are clear to be added to inventories");
                ThrottledBuilder.main.QueueDrained -= AddItemsToInventories;

                using (packetSender.Suppress<ItemContainerAdd>())
                {
                    foreach (ItemData itemData in inventoryItems)
                    {
                        itemContainers.AddItem(itemData);
                    }
                }
            }
        }
        
        private void SetPlayerLocationAfterBasePiecesFinish(Vector3 position, Optional<string> subRootGuid, bool basePiecesToSpawn)
        {
            Log.Info("Received initial sync packet with location " + position + " and subroot " + subRootGuid);
            PlayerSpawner playerSpawner = new PlayerSpawner(position, subRootGuid);
                    
            if (basePiecesToSpawn)
            {
                ThrottledBuilder.main.QueueDrained += playerSpawner.SetPlayerSpawn;
            }
            else
            {
                playerSpawner.SetPlayerSpawn(null, null);
            }
        }

        private class PlayerSpawner
        {
            private Vector3 position;
            private Optional<string> subRootGuid;

            public PlayerSpawner(Vector3 position, Optional<string> subRootGuid)
            {
                this.position = position;
                this.subRootGuid = subRootGuid;
            }
            
            public void SetPlayerSpawn(object sender, EventArgs eventArgs)
            {
                Multiplayer.Main.InitialSyncCompleted = true;

                ThrottledBuilder.main.QueueDrained -= SetPlayerSpawn;

                if (!(position.x == 0 && position.y == 0 && position.z == 0))
                {
                    Player.main.SetPosition(position);
                }

                if (subRootGuid.IsPresent())
                {
                    Optional<GameObject> sub = GuidHelper.GetObjectFrom(subRootGuid.Get());

                    if (sub.IsPresent())
                    {
                        Player.main.SetCurrentSub(sub.Get().GetComponent<SubRoot>());
                    }
                    else
                    {
                        Log.Error("Could not spawn player into subroot with guid: " + subRootGuid.Get());
                    }
                }
            }
        }

        private void SpawnRemotePlayersAfterBasePiecesFinish(List<InitialRemotePlayerData> remotePlayerData, bool basePiecesToSpawn)
        {
            Log.Info("Received initial sync packet with " + remotePlayerData.Count + " remote players");

            RemotePlayerCreator remotePlayerCreator = new RemotePlayerCreator(remotePlayerData, remotePlayerManager);

            if(basePiecesToSpawn)
            {
                ThrottledBuilder.main.QueueDrained += remotePlayerCreator.CreateRemotePlayers;
            }
            else
            {
                remotePlayerCreator.CreateRemotePlayers(null, null);
            }
        }

        private class RemotePlayerCreator
        {
            private List<InitialRemotePlayerData> remotePlayerData;
            private PlayerManager remotePlayerManager;

            public RemotePlayerCreator(List<InitialRemotePlayerData> remotePlayerData, PlayerManager remotePlayerManager)
            {
                this.remotePlayerData = remotePlayerData;
                this.remotePlayerManager = remotePlayerManager;
            }

            public void CreateRemotePlayers(object sender, EventArgs eventArgs)
            {
                ThrottledBuilder.main.QueueDrained -= CreateRemotePlayers;

                foreach (InitialRemotePlayerData playerData in remotePlayerData)
                {
                    RemotePlayer player = remotePlayerManager.Create(playerData.PlayerContext);

                    if (playerData.SubRootGuid.IsPresent())
                    {
                        Optional<GameObject> sub = GuidHelper.GetObjectFrom(playerData.SubRootGuid.Get());

                        if (sub.IsPresent())
                        {
                            player.SetSubRoot(sub.Get().GetComponent<SubRoot>());
                        }
                        else
                        {
                            Log.Error("Could not spawn remote player into subroot with guid: " + playerData.SubRootGuid.Get());
                        }
                    }
                }
            }
        }
    }
}
