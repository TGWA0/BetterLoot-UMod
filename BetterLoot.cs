using System;
using System.IO;
using Oxide.Core;
using System.Data;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using Facepunch.Extend;
using Oxide.Core.Plugins;
using System.Collections;
using Newtonsoft.Json.Linq;
using static ConsoleSystem;
using Pool = Facepunch.Pool;
using Random = System.Random;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Oxide.Plugins.BetterLootExtensions;

namespace Oxide.Plugins
{
    [Info("BetterLoot", "MagicServices.co // TGWA", "4.1.6")]
    [Description("A light loot container modification system with rarity support | Previously maintained and updated by Khan & Tryhard")]
    public class BetterLoot : RustPlugin
    {
        #region Fields
        [PluginReference] Plugin? CustomLootSpawns;

        // Static Instance
        private static BetterLoot? _instance;
        private static PluginConfig? _config;

        // System States
        private bool Changed = true;
        private bool Initialized;

        private static Random? RNG;
        private static Regex? UniqueTagREGEX;
        
        // Data Instances
        private Dictionary<string, List<string>[]> Items = new Dictionary<string, List<string>[]>(); // Cached Item Data for each container
        private Dictionary<string, List<string>[]> Blueprints = new Dictionary<string, List<string>[]>(); // Cached Blueprint Data for each container
        private Dictionary<string, int[]> ItemWeights = new Dictionary<string, int[]>(); // Item weights for each container
        private Dictionary<string, int[]> BlueprintWeights = new Dictionary<string, int[]>(); // Blueprint weights for each container
        private Dictionary<string, int> TotalItemWeights = new Dictionary<string, int>(); // Total sum of item weights for each container
        private Dictionary<string, int> TotalBlueprintWeights = new Dictionary<string, int>(); // Total sum of blueprint weights for each container

        #region Info Caching
        private Dictionary<string, WI_Cache>? WeaponInfoCache; // Item info for building table 
        private Dictionary<string, ItemSlot>? WeaponModInfoCache; // Weapon mod shortname to enum mapping.
        private List<string>? DurabilityItems;  // Items that need the durability property 

        /// <summary>
        /// Info for each weapon type item
        /// Contains info for building config data for if a item is a weapon (it will be in this dict) as well as how many mods and how much ammo it is allowed to have.
        /// </summary>
        private sealed class WI_Cache
        {
            public bool IsLiquidWeapon { get; } // is a watergun.
            public int MaxMods { get; } // if > 0 can have mods + max mods property.
            public int MaxAmmo { get; } // vanilla max ammo.
            public ItemSlot ModTypes { get; } // The types of mods that can be applied to this weapon.

            public WI_Cache(bool isLiquidWeapon, int maxMods, int maxAmmo, ItemSlot modTypes)
            {
                //IsLiquidWeapon = isLiquidWeapon;
                MaxMods = maxMods;
                MaxAmmo = maxAmmo;
                ModTypes = modTypes;
            }
        }
        #endregion
        #endregion

        #region Instance Constants
        private const double BASE_ITEM_RARITY = 2;
        private const string ADMIN_PERM = "betterloot.admin";
        #endregion

        #region Lang
        private string BLLang(string key, string? id = null) => lang.GetMessage(key, this, id);
        private string BLLang(string key, string? id, params object[] args) => string.Format(BLLang(key, id), args);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                { "initialized", "Plugin not enabled" },
                { "perm", "You are not authorized to use this command" },
                { "syntax", "Usage: /blacklist [additem|deleteitem] \"ITEMNAME\"" },
                { "none", "There are no blacklisted items" },
                { "blocked", "Blacklisted items: {0}" },
                { "notvalid", "Not a valid item: {0}" },
                { "blockedpass", "The item '{0}' is now blacklisted" },
                { "blockedtrue", "The item '{0}' is already blacklisted}" },
                { "unblacklisted", "The item '{0}' has been unblacklisted" },
                { "blockedfalse", "The item '{0}' is not blacklisted" },
                { "lootycmdformat", "Usage: /looty \"looty-id\"" }, // Blank code provided.
                { "lootynotfound", "The requested table id was not found. Please ensure youve got the right code." } // 404 Looty api
            }, this); //en
        }
        #endregion

        #region Config
        private class PluginConfig : SerializableConfiguration
        {
            [JsonProperty("Chat Configuration")]
            public ChatConfiguration ChatConfig = new ChatConfiguration();
            [JsonProperty("General Configuration")]
            public GenericConfiguration Generic = new GenericConfiguration();
            [JsonProperty("Loot Configuration")]
            public LootConfiguration Loot = new LootConfiguration();
            [JsonProperty("Loot Groups Configuration")]
            public LootGroupsConfiguration LootGroupsConfig = new LootGroupsConfiguration();
        }

        private class GenericConfiguration
        {
            [JsonProperty("Blueprint Probability")]
            public double BlueprintProbability = 0.11;
            [JsonProperty("Log Updates On Load")]
            public bool ListUpdatesOnLoad = true;
            [JsonProperty("Remove Stacked Containers")]
            public bool RemoveStackedContainers = true;
            [JsonProperty("Watched Prefabs")]
            public HashSet<string> WatchedPrefabs = new HashSet<string>();
        }

        private class LootConfiguration
        {
            [JsonProperty("Enable Hammer Hit Loot Cycle")]
            public bool EnableHammerLootCycle = false;
            [JsonProperty("Hammer Loot Cycle Time")]
            public double HammerLootCycleTime = 3.0;
            [JsonProperty("Loot Multiplier")]
            public int LootMultiplier = 1;
            [JsonProperty("Scrap Multipler")]
            public int ScrapMultiplier = 1;
            [JsonProperty("Allow duplicate items")]
            public bool AllowDuplicateItems = false;
            [JsonProperty("Enable logging for item attachments auto balancing operations")]
            public bool EnableBonusItemsAutoBalanceLogging { get; set; } = false;
            [JsonProperty("Always allow duplicate items from bonus items list (if set, will override 'Allow duplicate items option')")]
            public bool AllowBonusItemsDuplicateItems { get; set; } = true;
        }

        private class ChatConfiguration
        {
            [JsonProperty("Chat Message Prefix")]
            public string Prefix = $"[<color=#00ff00>{nameof(BetterLoot)}</color>]";
            [JsonProperty("Chat Message Icon SteamID (0 = None)")]
            public ulong MessageIcon = 0;
        }

        private class LootGroupsConfiguration
        {
            [JsonProperty("Enable creation of example loot group on load?")]
            public bool EnableExampleGroupCreation = true;
            [JsonProperty("Enable auto profile probability balancing?")]
            public bool EnableProbabilityBalancing = true;
            [JsonProperty("Always allow duplicate items from loot groups (if true overrides 'Allow duplicate items option')")]
            public bool AllowLootGroupDuplicateItems = true;
        }

        private void CheckWatchedPrefabs()
        {
            /* Watched Prefabs Auto-Population */
            if (_config?.Generic.WatchedPrefabs.Any() ?? false)
                return;

            Log("Updating watched prefabs from manifest...");

            // Name filtering
            List<string> negativePartialNames = Pool.Get<List<string>>();
            List<string> partialNames = Pool.Get<List<string>>();

            // If does not contain, skip
            negativePartialNames.AddRange(
                new List<string> {
                    "resource/loot",
                    "misc/supply drop/supply_drop",
                    "/npc/m2bradley/bradley_crate",
                    "/npc/patrol helicopter/heli_crate",
                    "/deployable/chinooklockedcrate/chinooklocked",
                    "/deployable/chinooklockedcrate/codelocked",
                    "prefabs/radtown",
                    "props/roadsigns",
                    "humannpc/scientist",
                    "humannpc/tunneldweller",
                    "humannpc/underwaterdweller"
                }
            );

            // If does contain, skip
            partialNames.AddRange(
                new List<string>
                {
                    "radtown/ore",
                    "static",
                    "/spawners",
                    "radtown/desk",
                    "radtown/loot_component_test",
                    "water_puddles_border_fix" // Weird container prefab from radtown update??
                }
            );

            foreach (GameManifest.PrefabProperties category in GameManifest.Current.prefabProperties)
            {
                string name = category.name;

                if (!negativePartialNames.ContainsPartial(name) || partialNames.ContainsPartial(name))
                    continue;

                if (!_config.Generic.WatchedPrefabs.Contains(name))
                    _config.Generic.WatchedPrefabs.Add(name);
            }

            SaveConfig();

            Log("Updated configuration with manifest values.");

            AttemptSendLootyLink();

            Pool.FreeUnmanaged(ref negativePartialNames);
            Pool.FreeUnmanaged(ref partialNames);
        }

        protected override void LoadDefaultConfig() => _config = new PluginConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<PluginConfig>();

                if (_config == null)
                {
                    Log($"Generating Config File for Better Loot");
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_config))
                {
                    Log("Configuration appears to be outdated; updating and saving Better Loot");
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                Log("Failed to load Better Loot config file (is the config file corrupt?) (" + ex.Message + ")");
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {nameof(BetterLoot)}.json");
            Config.WriteObject(_config, true);
        }

        #region Configuration Updater
        internal class SerializableConfiguration
        {
            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>().ToDictionary(prop => prop.Name, prop => ToObject(prop.Value));
                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(SerializableConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            bool changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                if (currentRaw.TryGetValue(key, out object? currentRawValue) && currentRawValue is not null)
                {
                    var defaultDictValue = currentWithDefaults[key] as Dictionary<string, object>;
                    var currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (defaultDictValue != null)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                            changed = true;
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }
        #endregion
        #endregion

        #region Oxide Loaded / Unload / Server Load 
        private void Loaded()
        {
            _instance = this;
            RNG = new Random();
            UniqueTagREGEX = new Regex(@"\{\d+\}");

            DataSystem.LoadBlacklist();
            DataSystem.LoadLootTables();
            DataSystem.LoadLootGroups();

            CheckWatchedPrefabs();
        }

        private void OnServerInitialized()
        {
            ItemManager.Initialize();
            BuildWeaponInfoCache();

            permission.RegisterPermission(ADMIN_PERM, this);
            InitLootSystem();
        }

        private void InitLootSystem(bool newData = false)
        {
            // Ensure empty, is called when looty tables are loaded.
            if (newData)
            {
                Items.Clear();
                Blueprints.Clear();
                ItemWeights.Clear();
                BlueprintWeights.Clear();
                TotalItemWeights.Clear();
                TotalBlueprintWeights.Clear();

                BuildWeaponInfoCache();
            }

            // Load container data
            LoadAllContainers();

            Pool.FreeUnmanaged(ref WeaponInfoCache);
            Pool.FreeUnmanaged(ref WeaponModInfoCache);
            Pool.FreeUnmanaged(ref DurabilityItems);

            UpdateInternals(_config.Generic.ListUpdatesOnLoad);
        }

        private void Unload()
        {
            // Static variable instances
            UniqueTagREGEX = null;

            storedBlacklist = null;
            lootTables = null;
            lootGroups = null;
            RNG = null;

            // Static BetterLoot instance
            _instance = null;

            foreach(HammerHitLootCycle hhlc in UnityEngine.Object.FindObjectsByType<HammerHitLootCycle>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(i => i is not null))
                UnityEngine.Object.Destroy(hhlc);
        }
        #endregion
        
        #region DataFile
        private static LootTableData? lootTables = null;
        private static StoredBlacklist? storedBlacklist = null;
        private static LootGroupsData? lootGroups = null;

        // Looty API Schema
        private class LootyResponse
        {
            [JsonProperty("LootTable")]
            public Dictionary<string, PrefabLoot> LootTables = new();
            [JsonProperty("Loot Groups")]
            public Dictionary<string, LootProfile>? LootGroups = new();

            public LootyResponse() { }
        }

        // LootTables.json structure
        private class LootTableData
        {
            public Dictionary<string, PrefabLoot> LootTables = new Dictionary<string, PrefabLoot>();

            public LootTableData() { }
        }

        // Blacklist.json structure
        private class StoredBlacklist
        {
            public HashSet<string> ItemList = new HashSet<string>();

            public StoredBlacklist() { }
        }

        private class LootGroupsData
        {
            [JsonProperty("Loot Groups", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, LootProfile> LootGroups = new Dictionary<string, LootProfile>
            {
                ["example_group"] = new LootProfile(new Dictionary<string, LootProfile.LootRNG> { ["lmg.m249"] = new LootProfile.LootRNG(10, new LootEntry(1, 2)) }, false)
            };

            public LootGroupsData() { }

            public static void ValidateGroups(LootGroupsData? Data)
            {
                ItemManager.Initialize();

                if (ItemManager.itemDictionaryByName is null)
                {
                    Log("Error: Failed to initialize ItemDictionary. Unloading");
                    _instance.Server.Command($"o.unload BetterLoot");
                    return;
                }

                if (Data is null || Data.LootGroups is null)
                {
                    Log($"Error: Invalid data was provided to the {nameof(LootGroupsData)} validator!");
                    return;
                }

                // Attempt to create an example group in the LootGroups file
                TryCreateExampleGroup();

                foreach((string profileName, LootProfile? profileData) in Data.LootGroups)
                {
                    Log($"Validating LootGroup: \"{profileName}\"");

                    // NRE Data Check
                    if (profileData.ItemList is null)
                    {
                        Log("- Error: Profile item list is null. Skipping...");
                        continue;
                    } 

                    // Ensure items are valid
                    List<string> invalidItemPrefabs = Pool.Get<List<string>>();
                    invalidItemPrefabs.AddRange(profileData.ItemList.Keys.Where(key => !ItemManager.itemDictionaryByName.ContainsKey(UniqueTagREGEX.Replace(key.ToLower(), string.Empty))));

                    if (profileData.ItemList.RemoveAll(invalidItemPrefabs.Contains) is int removeCount && removeCount > 0)
                        Log($"Error - Removing {removeCount} invalid entries. Please check item names that were not found in the games item dictionary: ({string.Join(", ", invalidItemPrefabs)})");

                    Pool.FreeUnmanaged(ref invalidItemPrefabs);

                    string extraReason = string.Empty;
                    if (profileData.ItemList.Any()) // Ensure we still have items
                    {
                        // Balance loot percentages
                        if (_config.LootGroupsConfig.EnableProbabilityBalancing)
                        {
                            double GetSum() => profileData.ItemList.Sum(x => x.Value.Probability);
                            double Round(double x) => Math.Round(x, 2);

                            const double target = 100;
                            double sum = GetSum();

                            if (Math.Abs(target - sum) > 1e-3)
                            {
                                Log($"- Profile probability sum ({sum}) != 100. Balancing profile!");

                                double _ratio = target / sum;

                                // Set first key as largest by default for empty string edgecase
                                string largestKey = profileData.ItemList.Keys.First();
                                double largestValue = profileData.ItemList[largestKey].Probability;

                                foreach (var item in profileData.ItemList)
                                {
                                    double probability = item.Value.Probability;
                                    if (probability > largestValue)
                                    {
                                        largestValue = probability;
                                        largestKey = item.Key;
                                    }

                                    item.Value.Probability = Round(probability * _ratio);
                                }

                                var largestEntry = profileData.ItemList[largestKey];
                                largestEntry.Probability = Round(largestEntry.Probability - Round(target - GetSum()));
                            }
                        }
                        else
                        {
                            extraReason = "Probability balance skipped.";
                        }
                    }
                    else
                    {
                        extraReason = "No remaining / valid items in profile, skipped.";
                    }

                    Log($"Profile \"{profileName}\" validation complete. {extraReason}");
                }
            }

            public static void TryCreateExampleGroup()
            {
                if (!_config.LootGroupsConfig.EnableExampleGroupCreation)
                    return;

                // Create a default group in the first item of the loot table for reference if none exists
                var firstLootTable = lootTables.LootTables.FirstOrDefault();
                if (!firstLootTable.IsDefault() && !firstLootTable.Value.LootProfiles.Any())
                {
                    firstLootTable.Value.LootProfiles.Add(new PrefabLoot.LootProfileImport("example_group", 30, false));
                    Log($"Added LootGroup Import example to \"{firstLootTable.Key}\"");
                    DataSystem.SaveLootTables();
                }
            }
        }

        private static class DataSystem
        {
            #region Public Methods
            #region Blacklist
            private const string BL_FN = "Blacklist";

            public static void LoadBlacklist() 
                => LoadFile(BL_FN, (blacklistData) => CheckNull(ref blacklistData, ref storedBlacklist, blacklistData?.ItemList), ref storedBlacklist);

            public static void SaveBlacklist()
                => SaveFile(BL_FN, (blacklistData) => CheckNull(ref blacklistData, ref storedBlacklist, blacklistData?.ItemList), ref storedBlacklist);
            #endregion

            #region Loot Tables
            private const string LT_FN = "LootTables";

            public static void LoadLootTables()
                => LoadFile(LT_FN, (tableData) => CheckNull(ref tableData, ref lootTables, tableData?.LootTables), ref lootTables);

            public static void SaveLootTables()
                => SaveFile(LT_FN, (tableData) => CheckNull(ref tableData, ref lootTables, tableData?.LootTables), ref lootTables);
            #endregion

            #region Loot Groups
            private const string LG_FN = "LootGroups";

            public static void LoadLootGroups()
                => LoadFile(LG_FN, (groupsData) => CheckNull(ref groupsData, ref lootGroups), ref lootGroups, LootGroupsData.ValidateGroups);

            public static void SaveLootGroups()
                => SaveFile(LG_FN, (groupsData) => CheckNull(ref groupsData, ref lootGroups), ref lootGroups);
            #endregion
            #endregion

            #region DataFile Error Backup
            public static void BakDataFile(string filename, bool restoreMode = false, BasePlayer? msgPlayer = null)
            {
                // Move these to lang
                const string NoRestoreFileFound = "No backup file to restore.";
                const string NoMainFileFound = "No file found to move to backup, skipping file.";
                const string FileRestored = "Backup restored";

                bool sendPlayer = msgPlayer is not null;
                string notifyMessage = string.Format(restoreMode ? "Restoring backup of {0}" : "Attempting to create backup of datafile {0}", $"{filename}.json");

                if (sendPlayer)
                    _instance?.SendMessage(msgPlayer, notifyMessage);
                else
                    Log(notifyMessage);

                // Rename specified file to *.bak before regenerating a file in place of it
                string path = Path.Combine(Interface.Oxide.DataFileSystem.Directory, $"{nameof(BetterLoot)}/{filename}.json");
                string bakPath = $"{path}.bak";
                
                string existSearchPath = !restoreMode ? bakPath : path;
                if (File.Exists(existSearchPath))
                {
                    File.Delete(existSearchPath);
                }
                else if (restoreMode)
                {
                    
                    if (sendPlayer)
                        _instance?.SendMessage(msgPlayer, NoRestoreFileFound);
                    else
                        Log(NoRestoreFileFound);
                    return;
                }
                else
                {
                    if (sendPlayer)
                        _instance?.SendMessage(msgPlayer, NoMainFileFound);
                    else
                        Log(NoMainFileFound);

                    return;
                } // Main file does not exist, cannot push to backup

                File.Move(restoreMode ? bakPath : path, existSearchPath);
                
                if (sendPlayer)
                    _instance?.SendMessage(msgPlayer, FileRestored);
                else
                    Log(FileRestored);

                if (restoreMode)
                    _instance.InitLootSystem(true);
            }
            #endregion

            #region Save / Load Methods
            /// <summary>
            /// Load a data from a file within the plugin data directory.
            /// </summary>
            /// <typeparam name="T">The structure of the data being read from the file.</typeparam>
            /// <param name="fileName">The name of the file within the plugin data directory</param>
            /// <param name="validator">A custom data validator. Is nullable.</param>
            /// <param name="loadVar">The variable where the loaded data should be stored.</param>
            private static void LoadFile<T>(string fileName, Func<T, T>? validator, ref T loadVar, Action<T>? postLoadMethod = null) where T : class?
            {
                // If no validator was provided, set to check if instance is null, if it is create new instance.
                if (validator is null)
                    validator = (data) => data ?? Activator.CreateInstance<T>();

                try
                {
                    loadVar = validator(Interface.Oxide.DataFileSystem.ReadObject<T>($"{nameof(BetterLoot)}\\{fileName}"));
                    Log($"Loaded file \"{fileName}\" datafile successfully!");
                } catch (Exception e)
                {
                    Log($"ERROR: There was an issue loading your \"{fileName}.json\" datafile, a new one has been created.\n{e.Message}");

                    BakDataFile(fileName);
                    loadVar = Activator.CreateInstance<T>();
                }

                postLoadMethod?.Invoke(loadVar);

                SaveFile(fileName, validator, ref loadVar);
            }

            /// <summary>
            /// Save plugin data to the plugin data directory.
            /// </summary>
            /// <typeparam name="T">The type of the datafile structure</typeparam>
            /// <param name="fileName">The name of the datafile within the plugin data directory.</param>
            /// <param name="validator">A custom data validator. Is nullable.</param>
            /// <param name="saveVar">The variable where this data is currently stored.</param>
            private static void SaveFile<T>(string fileName, Func<T, T>? validator, ref T saveVar) where T : class?
            {
                if (validator is null)
                    validator = (data) => data ?? Activator.CreateInstance<T>();

                Interface.Oxide.DataFileSystem.WriteObject($"{nameof(BetterLoot)}\\{fileName}", validator(saveVar));

                Log($"Saved {fileName}.json");
            }
            #endregion

            #region Data Validator
            /// <summary>
            /// Checks if the provided datafile's data is null, if so create a new instance and optionally write it to file.
            /// </summary>
            /// <typeparam name="T">The type of the data structure that is being checked</typeparam>
            /// <param name="obj">The local instance of the data to check</param>
            /// <param name="target">The global instance of where the data is held</param>
            /// <param name="additional">Additional objects to check if null aside from arguement 'obj'</param>
            /// <returns>>Non null type of provided object type</returns>
            private static T CheckNull<T> (ref T? obj, ref T? target, params object?[] additional) where T : class? 
            {
                if (obj is null || additional.Any(x => x is null))
                {
                    target = Activator.CreateInstance<T>();
                    obj = target;
                }

                return obj;
            }
            #endregion
        }

        private static void AttemptSendLootyLink()
        {
            Log("--------------------------------------------------------------------------");
            Log("Use the Looty Editor to easily edit and create loot tables for BetterLoot!");
            Log("Find it here -> https://looty.cc/betterloot-v4");
            Log("--------------------------------------------------------------------------");
        }
        #endregion

        #region Loot Classes
        /// <summary>
        /// Prefab Loot system will be contained in a list. This is the new loot class for loot containers that will
        /// allow the import of custom loot groups allowing for RNG on groups as well as individual items
        /// </summary>
        private class PrefabLoot
        {
            [JsonProperty("Is Prefab Enabled?")]
            public bool Enabled;

            [JsonProperty("Loot Profiles", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<LootProfileImport> LootProfiles;

            [JsonProperty("Guaranteed Items")]
            public Dictionary<string, LootEntrySettings> GuaranteedItems = new Dictionary<string, LootEntrySettings>();

            [JsonProperty("Ungrouped Items")]
            public Dictionary<string, LootEntry> UngroupedItems;

            [JsonProperty("Item Settings")]
            public ItemProperties ItemSettings;

            [JsonIgnore]
            public int ItemCount => GetTotalItemCount();

            public PrefabLoot()
            {
                LootProfiles = new List<LootProfileImport>();
                UngroupedItems = new Dictionary<string, LootEntry>();
                ItemSettings = new ItemProperties();
            }

            internal class LootProfileImport
            {
                [JsonProperty("Group Enabled?")]
                public bool Enabled = true;
                [JsonProperty("Loot Profile Name")]
                public string LootProfileName = string.Empty;
                [JsonProperty("Loot Profile Probability (1% - 100%)")]
                public double LootProfileProbability;

                internal LootProfileImport() { }

                internal LootProfileImport(string LootProfileName, double LootProfileProbability, bool Enabled = true)
                {
                    this.LootProfileName = LootProfileName;
                    this.LootProfileProbability = LootProfileProbability;
                    this.Enabled = Enabled;
                }
            }

            internal class ItemProperties
            {
                [JsonProperty("Minimum Amount of Items")]
                public int ItemsMin;

                [JsonProperty("Maximum Amount of Items")]
                public int ItemsMax;

                [JsonProperty("Minimum Scrap Amount")]
                public int MinScrap;

                [JsonProperty("Maximum Scrap Amount")]
                public int MaxScrap;

                [JsonProperty("Max Blueprints")]
                public int MaxBPs;

                internal ItemProperties() { }

                #region v4.0.6 Configuration Migration

                [JsonProperty("Scrap Amount", NullValueHandling = NullValueHandling.Ignore)]
                private int? LegacyScrap { get; set; }

                [OnDeserialized]
                private void OnDeserialized(StreamingContext _)
                {
                    if (LegacyScrap.HasValue)
                    {
                        if (MaxScrap == 0)
                        {
                            MaxScrap = LegacyScrap.Value;
                            MinScrap = LegacyScrap.Value;
                        }
                    }
                            
                    LegacyScrap = null;
                }

                [OnSerializing]
                private void OnSerializing(StreamingContext _) => LegacyScrap = null; // force null, no omit
                #endregion
            }

            private int GetTotalItemCount()
            {
                int count = 0;
                count += UngroupedItems.Count();

                if (lootGroups is not null)
                    foreach (LootProfileImport import in LootProfiles)
                        if (lootGroups.LootGroups.TryGetValue(import.LootProfileName, out LootProfile? profile) && profile is not null)
                            count += profile.ItemList.Count;

                return count;
            }
        }

        /// <summary>
        /// LootProfile for containing all items that will be part of a certain profile
        /// Will be referenced by the specified profile name that the user creates within the LootGroups.json
        /// </summary>
        public class LootProfile
        {
            [JsonProperty("Enabled?")]
            public bool Enabled = true;

            [JsonProperty("Guaranteed Items")]
            public Dictionary<string, LootEntrySettings> GuaranteedItems = new Dictionary<string, LootEntrySettings>();

            [JsonProperty("Item List")]
            public Dictionary<string, LootRNG> ItemList;

            [JsonIgnore]
            private List<double> _culminativeProbabilities = new List<double>();

            [JsonIgnore]
            public bool DoProbabilitiesExist
                => _culminativeProbabilities.Any();

            public LootProfile()
                => ItemList = new Dictionary<string, LootRNG>();

            public LootProfile(Dictionary<string, LootRNG> ItemList, bool Enabled = true)
            {
                this.ItemList = ItemList;
                this.Enabled = Enabled;
            }

            public class LootRNG
            {
                [JsonProperty("Item Probability (1-100)")]
                public double Probability;

                [JsonProperty("Item Amount")]
                public LootEntry Amount;

                public LootRNG(double Probability, LootEntry Amount)
                {
                    this.Probability = Probability;
                    this.Amount = Amount;
                }
            }

            #region Probalistic Selector Methods
            public void UpdateProbabilities()
            {
                double _culminative = 0;
                foreach(var item in ItemList.Values)
                {
                    _culminative += item.Probability;
                    _culminativeProbabilities.Add(_culminative);
                }
            }
               
            /// <summary>
            /// Get a random item from this loot group based off of items probabilities
            /// </summary>
            public (Item?, List<Item>?) GetItem()
            {
                List<Item> bonusItems = null;
                double randomSelect = RNG.NextDouble() * 1e2;
                int itemIndex = _culminativeProbabilities.BinarySearch(randomSelect);

                if (itemIndex < 0)
                    itemIndex = ~itemIndex;

                // No item found
                if (itemIndex >= ItemList.Count)
                    return (null, null);

                var entry = ItemList.ElementAt(itemIndex);

                entry.Value.Amount.CreateBonusItems(ref bonusItems);

                // Select Amount
                int amount = GetRNG(entry.Value.Amount.Min, entry.Value.Amount.Max);

                // Get Custom Properties
                ulong skinId = entry.Value.Amount.SkinId;
                string? customName = entry.Value.Amount.DisplayName;

                // Create Item
                Item item = ItemManager.CreateByPartialName(UniqueTagREGEX.Replace(entry.Key, string.Empty), amount);
                if (!string.IsNullOrWhiteSpace(customName))
                    item.name = customName;
                item.skin = skinId;

                entry.Value.Amount.ApplyAttachments(item);
                entry.Value.Amount.ApplyAmmo(item);

                item.MarkDirty();

                if (item is null)
                    Log($"ERROR: item \"{entry.Key}\" could not be created! System returned null entry!");

                return (item, bonusItems);
            }
            #endregion
        }

        #region Loot Entry
        public class LootEntrySettings
        {
            [JsonProperty("Skin ID (0 = default)")]
            public ulong SkinId = 0;

            [JsonProperty("Display Name (empty = none)")]
            public string? DisplayName = string.Empty;

            [JsonProperty("Item Minimum")]
            public int Min;

            [JsonProperty("Item Maximum")]
            public int Max;

            [JsonProperty("Item Durability", NullValueHandling = NullValueHandling.Ignore)]
            public LootEntryDurability? DurabilitySettings;

            // By default will not exist in item entries. System will add it during table scan.
            [JsonProperty("Item Properties", NullValueHandling = NullValueHandling.Ignore)]
            public ItemEntrySettings? ItemEntryModifications;

            public void ApplyAttachments(Item item)
            {
                // No mods to apply
                if (!(ItemEntryModifications?.AttachmentSettings?.itemMods.Any() ?? false))
                    return;

                // Recursively apply
                int total = Math.Clamp(GetRNG(ItemEntryModifications.AttachmentSettings.minModAmount, ItemEntryModifications.AttachmentSettings.maxModAmount), 1, ItemEntryModifications.maxMods);
                for (int i = 0; i < total; i++)
                {
                    for (int r = 0; r < 5; r++)
                    {
                        Item? itemMod = ItemEntryModifications?.GetRandomItemMod();
                        if (itemMod is null) // No item selected! This is ok.
                            break;

                        // Attempt to regenerate if there is a duplicate
                        if (itemMod.MoveToContainer(item.contents))
                            break;
                    }
                }
            }

            public void ApplyAmmo(Item item)
            {
                if (ItemEntryModifications?.AmmunitionSettings is not ItemEntrySettings.AmmoSettings ammoSettings || string.IsNullOrWhiteSpace(ammoSettings.AmmoItemShortname))
                    return;

                // if extended mag is applied, add 25% to calculated ammo amount
                ItemDefinition? ammoDef = null;
                int ammoAmount = 1;
                bool applyExtraAmmo = item.contents?.itemList.Any(x => x.info.shortname.Equals("weapon.mod.extendedmags")) ?? false;            
            
                if (ammoSettings.CanHoldMultipleAmmoUnits)
                {
                    ammoDef = ItemManager.FindDefinitionByPartialName(ammoSettings.AmmoItemShortname);
                    ammoAmount = Math.Clamp(GetRNG(ammoSettings.Min, ammoSettings.Max), 0, ammoSettings.MaxAmmo);

                    if (item.contents?.itemList.Any(x => x.info.shortname.Equals("weapon.mod.extendedmags")) ?? false)
                        ammoAmount = (int)Math.Ceiling(ammoAmount * 1.25);
                }
                else if (GetRNG(0, 100) <= ammoSettings.Probability)
                {
                    ammoDef = ItemManager.FindDefinitionByPartialName(ammoSettings.AmmoItemShortname);
                }

                // No ammo defined
                if (ammoDef is null)
                    return;

                // Type check of how should apply
                var heldEntity = item.GetHeldEntity();
                if (heldEntity is BaseProjectile bp)
                {
                    var magazine = bp.primaryMagazine.ammoType = ammoDef;
                    bp.primaryMagazine.contents = ammoAmount;
                    bp.SendNetworkUpdateImmediate();
                } else if (heldEntity is FlameThrower ft)
                { 
                    // Vanilla rust spawns this with full fuel
                    // By default should only be lowgrade fuel but leave the definition open for allowing custom fuel types
                    ft.fuelType = ammoDef;
                    ft.ammo = ammoAmount;
                    ft.SendNetworkUpdateImmediate();
                } else if (heldEntity is LiquidWeapon lw)
                {
                    if (lw.GetContents() is Item _liquid)
                    {
                        _liquid.amount = ammoAmount;
                        _liquid.MarkDirty();
                    }
                    else
                    {
                        Item liquid = ItemManager.Create(ammoDef, ammoAmount);
                        liquid.MoveToContainer(item.contents);
                        item.MarkDirty();
                    }
                }
            }
        }

        public class LootEntryDurability
        {
            [JsonProperty("Minimum Durability")]
            public int MinDurability = 100;

            [JsonProperty("Maximum Durability")]
            public int MaxDurability = 100;
        }

        #region Attachment System
        /* 
            Added v4.1.1
        */
        public class ItemEntrySettings
        {
            // Should add deserialzier value to set min / max bounds (min <= max ALWAYS)
            public class AmmoSettings  // default state
            {
                [JsonIgnore]
                public bool CanHoldMultipleAmmoUnits = true; // Should be set to false when its a single shot weapon (no magazine. Also declares to not serialize the ItemMods list)

                [JsonProperty("Ammo Item Shortname")]
                public string AmmoItemShortname = string.Empty;  // Item shortname of ammo item

                [JsonIgnore]
                public int MaxAmmo; // Maximum ammo the item can take.

                // These should only be added in config if the weapon can hold more than 1 unit of ammunition. (e.g harpoon gun vs ak)
                [JsonProperty("Minimum Amount")]
                public int Min;
                [JsonProperty("Maximum Amount")]
                public int Max;

                // These should only exist if the weapon can only hold 1 unit of ammo
                [JsonProperty("Spawn Probability")]
                public double Probability;

                // conditional serialization
                public bool ShouldSerializeMin() => CanHoldMultipleAmmoUnits;
                public bool ShouldSerializeMax() => CanHoldMultipleAmmoUnits;
                public bool ShouldSerializeProbability() => !CanHoldMultipleAmmoUnits;
            }

            // A weapon always needs ammo
            [JsonProperty("Ammunition Settings")]
            public AmmoSettings AmmunitionSettings = new AmmoSettings();

            #region Attachment Related
            #region Internal
            [JsonIgnore]
            public int maxMods; // Maximum amount of mods the item can have.
            [JsonIgnore]
            private List<double> _culminativeProbabilities = new List<double>();
            #endregion
            public class ItemModEntry
            {
                [JsonProperty("Spawn Probability (0%-100%)")]  // Culminative probabilty with other items in list. => Implement loot groups like system
                public double Probability;
                [JsonProperty("Durability", NullValueHandling=NullValueHandling.Ignore)]
                public LootEntryDurability? Durability;
            }

            public class ItemModSettings
            {
                [JsonProperty("Minimum Mod Amount")]
                public int minModAmount = 1;
                [JsonProperty("Maximum Mod Amount")]
                public int maxModAmount = 1;
                [JsonProperty("Available Attachments")]
                public Dictionary<string, ItemModEntry> itemMods = new Dictionary<string, ItemModEntry>();
            }

            // Should only be on weapons that have slots for weapon mods
            [JsonProperty("Weapon Attachments", NullValueHandling = NullValueHandling.Ignore)]
            public ItemModSettings? AttachmentSettings; // Item mods / weapon attachments. Null by default. Item shortname: {probabilty, {durability}}

            public void BalanceItemModProbabilities()
            {  
                if (!(AttachmentSettings?.itemMods?.Any() ?? false) || maxMods is 0)
                    return;

                AttachmentSettings.maxModAmount = Math.Clamp(AttachmentSettings.maxModAmount, 1, maxMods);

                double GetSum() => AttachmentSettings.itemMods.Sum(x => x.Value.Probability);
                double Round(double x) => Math.Round(x, 2);

                const double target = 100;
                double sum = GetSum(); // Initial sum

                if (sum > target) // Only balance if greater than target
                {
                    if (_config.Loot.EnableBonusItemsAutoBalanceLogging)
                        Log($"- Bonus items probability sum ({sum}) > 100. Balancing list!");

                    double _ratio = target / sum;
                    
                    string largestKey = AttachmentSettings.itemMods.Keys.First();
                    double largestValue = AttachmentSettings.itemMods[largestKey].Probability;

                    foreach(var item in AttachmentSettings.itemMods)
                    {
                        double probabilty = item.Value.Probability;
                        double probability = item.Value.Probability;
                        if (probability > largestValue)
                        {
                            largestValue = probability;
                            largestKey = item.Key;
                        }

                        item.Value.Probability = Round(probability * _ratio);
                    }

                    var largestEntry = AttachmentSettings.itemMods[largestKey];
                    largestEntry.Probability = Round(largestEntry.Probability - Round(target - GetSum()));
                }
            }

            public void UpdateProbabilities()
            {
                double _culminative = 0;
                foreach (var item in AttachmentSettings.itemMods.Values)
                {
                    _culminative += item.Probability;
                    _culminativeProbabilities.Add(_culminative);
                }
            }

            // Get random attachment from list
            public Item? GetRandomItemMod()
            {
                if (!_culminativeProbabilities.Any())
                    UpdateProbabilities();

                double randomSelect = RNG.NextDouble() * 1e2;
                int itemIndex = _culminativeProbabilities.BinarySearch(randomSelect);

                if (itemIndex < 0)
                    itemIndex = ~itemIndex;

                // No item found
                if (itemIndex >= AttachmentSettings.itemMods.Count)
                    return null;

                var entry = AttachmentSettings.itemMods.ElementAt(itemIndex);

                // Create Item
                Item item = ItemManager.CreateByName(entry.Key);

                // Dont need to send network update, it will be sent with OnVirginSpawn()
                if (item is null)
                {
                    Log($"ERROR: item \"{entry.Key}\" could not be created! System returned null entry!");
                    return null;
                }

                if (entry.Value.Durability is not null)
                    item.ChangeConditionPercentage(GetRNG(entry.Value.Durability.MinDurability, entry.Value.Durability.MaxDurability));

                item.OnVirginSpawn();
                
                return item;
            }
            #endregion
        }
        #endregion

        public class LootEntry : LootEntrySettings
        {
            // Set at top level class to foce only having bonus items at this level and not nested levels.
            [JsonProperty("Bonus Items", Order = 7)] // Forcing field to bottom
            public Dictionary<string, LootEntrySettings> additionalItems = new Dictionary<string, LootEntrySettings>();
            
            public LootEntry(int Min, int Max)
            {
                this.Min = Min;    
                this.Max = Max;
            }

            public void CreateBonusItems(ref List<Item>? bonusItems)
            {
                if (!(additionalItems?.Any() ?? false))
                    return;

                if (bonusItems is null)
                    bonusItems = new List<Item>();

                foreach (var bonusItemEntry in additionalItems)
                {
                    var _bonusItemEntry = bonusItemEntry.Value;
                    Item bonusItem = ItemManager.CreateByName(bonusItemEntry.Key, GetRNG(_bonusItemEntry.Min, _bonusItemEntry.Max) * _config.Loot.LootMultiplier, _bonusItemEntry.SkinId);

                    // Apply attachments if applicable
                    _bonusItemEntry.ApplyAttachments(bonusItem);
                    _bonusItemEntry.ApplyAmmo(bonusItem);

                    // Apply durability
                    if (_bonusItemEntry.DurabilitySettings is not null)
                        bonusItem.ChangeConditionPercentage(GetRNG(_bonusItemEntry.DurabilitySettings.MinDurability, _bonusItemEntry.DurabilitySettings.MaxDurability));

                    bonusItem.OnVirginSpawn();
                    bonusItems.Add(bonusItem);
                }
            }
        }
        #endregion
        #endregion

        #region Util
        private static void Log(string msg, params object[] args) => _instance?.Puts(msg, args);
        private void SendMessage(BasePlayer player, string message, params object[] args) => Player.Reply(player, message, _config.ChatConfig.Prefix, _config.ChatConfig.MessageIcon, args);
        public static int GetRNG(int min, int max)
            => min == max ? min : UnityEngine.Random.Range(Math.Min(min, max), Math.Max(min, max) + 1);
        public static float GetRNG(float min, float max)
            => min == max ? min : UnityEngine.Random.Range(Math.Min(min, max), Math.Max(min, max));
        #endregion

        #region Oxide Hooks
        private object OnLootSpawn(LootContainer container)
        {
            if ((!Initialized || container == null) || (CustomLootSpawns != null && CustomLootSpawns.Call<bool>("IsLootBox", container)))
                return null;

            if (PopulateContainer(container))
                return true;

            return null;
        }

        NPCPlayerCorpse? OnCorpsePopulate(BasePlayer npcPlayer, NPCPlayerCorpse corpse)
            => npcPlayer != null && corpse != null && _config.Generic.WatchedPrefabs.Contains(npcPlayer.PrefabName) && PopulateContainer(npcPlayer.PrefabName, corpse) ? corpse : null;
        #endregion

        #region Loot Methods
        private int ItemWeight(double baseRarity, int index) => (int)(Math.Pow(baseRarity, 4 - index) * 1000);

        // OPTIMIZE
        private LootEntry GetAmounts(ItemAmount amount)
        {
            LootEntry options = new LootEntry(
                (int)amount.amount, 
                ((ItemAmountRanged)amount).maxAmount > 0 && ((ItemAmountRanged)amount).maxAmount > amount.amount
                     ? (int)((ItemAmountRanged)amount).maxAmount 
                     : (int)amount.amount
            );

            return options;
        }

        private void GetLootSpawn(LootSpawn lootSpawn, ref Dictionary<string, LootEntry> items)
        {
            if (lootSpawn.subSpawn != null && lootSpawn.subSpawn.Any())
            {
                foreach (var entry in lootSpawn.subSpawn)
                    GetLootSpawn(entry.category, ref items);
            }
            else if (lootSpawn.items != null && lootSpawn.items.Any())
            {
                foreach (var amount in lootSpawn.items)
                {
                    LootEntry options = GetAmounts(amount);
                    ItemDefinition itemDef = amount.itemDef;
                    string itemName = itemDef.shortname;
                    if (itemDef.spawnAsBlueprint)
                        itemName += ".blueprint";
                    if (!items.ContainsKey(itemName))
                    {
                        // Is fireable weapon type
                        GameObject? entMod = itemDef.GetComponent<ItemModEntity>()?.entityPrefab?.Get();
                        if (entMod is not null && (entMod.HasComponent<BaseProjectile>() || entMod.HasComponent<LiquidWeapon>()))
                        {
                            options.ItemEntryModifications = new ItemEntrySettings();

                            // Allowed to have item mods settings if can have attachments
                            if (itemDef.GetComponent<ItemModContainer>() is ItemModContainer imc && imc.capacity > 0 && imc.availableSlots.Any())
                            {
                                options.ItemEntryModifications.AttachmentSettings = new ItemEntrySettings.ItemModSettings();
                            }
                        }

                        items.Add(itemName, options);
                    }
                }
            }
        }
        
        private void BuildWeaponInfoCache()
        {
            // Build details on all weapons and weapon attachments to build internal loot table flags and options.

            DurabilityItems = Pool.Get<List<string>>();
            WeaponInfoCache = Pool.Get<Dictionary<string, WI_Cache>>();
            WeaponModInfoCache = Pool.Get<Dictionary<string, ItemSlot>>();

            foreach (ItemDefinition itemDef in ItemManager.itemList)
            {
                if (itemDef.condition.enabled)
                    DurabilityItems.Add(itemDef.shortname);

                // Below scans for weapons only, exit if not in category
                if (itemDef.category is not ItemCategory.Weapon)
                    continue;

                var entMod = itemDef.GetComponent<ItemModEntity>()?.entityPrefab?.Get();

                if (entMod is not null && entMod.HasComponent<ProjectileWeaponMod>())
                {
                    WeaponModInfoCache[itemDef.shortname] = itemDef.occupySlots;
                } else
                {
                    List<ItemSlot> modTypes = new List<ItemSlot>();
                    bool isLiquidWeapon = false;
                    int _maxStackSize = 0;
                    int maxMods = 0;
                    int maxAmmo = 0;

                    if (itemDef.GetComponent<ItemModContainer>() is ItemModContainer container)
                    { // Can take item mods
                        // Max mods
                        maxMods = container.capacity;

                        // Mod types
                        modTypes = container.availableSlots;
                        
                        // Liquid weapon check
                        if (container.onlyAllowedContents is ItemContainer.ContentsType.Liquid)
                        {   
                            isLiquidWeapon = true;
                            _maxStackSize = container.maxStackSize;
                        }
                    }

                    // Max ammo
                    if (entMod is not null && (entMod.HasComponent<BaseProjectile>() || entMod.HasComponent<FlameThrower>() || entMod.HasComponent<LiquidWeapon>()))
                    {
                        maxAmmo = _maxStackSize; // only applies to liquid weapon

                        if (entMod.GetComponent<BaseProjectile>() is BaseProjectile bp && bp?.primaryMagazine is BaseProjectile.Magazine magazine)
                        {
                            // Safe to reference the built in size as opposed to the capacity, Built in size seems to always have a value.
                            maxAmmo = magazine.definition.builtInSize;
                        }
                        else if (entMod.GetComponent<FlameThrower>() is FlameThrower ft)
                        {
                            maxAmmo = ft.maxAmmo;
                        }

                        ItemSlot totalFlags = modTypes.Any() ? modTypes[0] : ItemSlot.None;
                        for(int i = 1; i < modTypes.Count; i++)
                            totalFlags |= modTypes[i];

                        WeaponInfoCache[itemDef.shortname] = new WI_Cache(isLiquidWeapon, maxMods, maxAmmo, totalFlags);
                    }
                }
            }
        }

        private void LoadAllContainers()
        {
            var nullTablePrefabs = Pool.Get<List<string>>();
            bool modifiedLootTables = false;

            // OPTIMIZE
            foreach (var lootPrefab in _config.Generic.WatchedPrefabs)
            {
                // If prefab loot table is not currently present loaded from LootTables.json, generate it.
                if (!lootTables.LootTables.ContainsKey(lootPrefab))
                {
                    var basePrefab = GameManager.server.FindPrefab(lootPrefab);
                    if (basePrefab is null)
                    {
                        nullTablePrefabs.Add(lootPrefab);
                        continue;
                    } 

                    if (basePrefab.GetComponent<global::HumanNPC>() is global::HumanNPC npc)
                    { // is npc
                        var container = new PrefabLoot();

                        container.Enabled = !lootPrefab.Contains("bradley_crate") && !lootPrefab.Contains("heli_crate");
                        container.ItemSettings.MaxScrap = 0;

                        var slotItemCount = 0;
                        var itemList = new Dictionary<string, LootEntry>();

                        foreach (var slot in npc.LootSpawnSlots)
                        {
                            GetLootSpawn(slot.definition, ref itemList);
                            slotItemCount += slot.numberToSpawn;
                        }

                        container.ItemSettings.ItemsMin = container.ItemSettings.ItemsMax = slotItemCount;
                        container.UngroupedItems = itemList;

                        lootTables.LootTables.Add(lootPrefab, container);
                        modifiedLootTables = true;
                    }
                    else
                    { // is not npc
                        var loot = basePrefab.GetComponent<LootContainer>();

                        if (loot is null)
                        {
                            nullTablePrefabs.Add(lootPrefab);
                            continue;
                        }

                        var container = new PrefabLoot();

                        container.Enabled = !lootPrefab.Contains("bradley_crate") && !lootPrefab.Contains("heli_crate");
                        container.ItemSettings.MinScrap = loot.scrapAmount;
                        container.ItemSettings.MaxScrap = loot.scrapAmount;

                        int slots = 0;
                        if (loot.LootSpawnSlots.Length > 0)
                        {
                            LootContainer.LootSpawnSlot[] lootSpawnSlots = loot.LootSpawnSlots;
                            for (int i = 0; i < lootSpawnSlots.Length; i++)
                                slots += lootSpawnSlots[i].numberToSpawn;
                        }
                        else
                            slots = loot.maxDefinitionsToSpawn;

                        container.ItemSettings.ItemsMin = container.ItemSettings.ItemsMax = slots;
                        container.ItemSettings.MaxBPs = 1;

                        var itemList = new Dictionary<string, LootEntry>();
                        if (loot.lootDefinition is not null)
                            GetLootSpawn(loot.lootDefinition, ref itemList);
                        else if (loot.LootSpawnSlots.Any())
                        {
                            LootContainer.LootSpawnSlot[] lootSpawnSlots = loot.LootSpawnSlots;
                            foreach (var lootSpawnSlot in lootSpawnSlots)
                                GetLootSpawn(lootSpawnSlot.definition, ref itemList);
                        }

                        // Default items
                        container.UngroupedItems = itemList;

                        lootTables.LootTables.Add(lootPrefab, container);
                        modifiedLootTables = true;
                    } 
                }
            }

            // Some prefabs are loaded but not used (unloaded or invalid prefab)
            if (nullTablePrefabs.Any() && _config.Generic.WatchedPrefabs.RemoveWhere(prefab => nullTablePrefabs.Contains(prefab)) is int missing && missing > 0)
            {
                Puts($"Removed {missing} invalid / unloaded prefabs from watch list:\n{string.Join(", \n", nullTablePrefabs)}");
                SaveConfig();
            }

            Pool.FreeUnmanaged(ref nullTablePrefabs);

            // Write Changes
            if (modifiedLootTables)
            {
                // Try to create an example loot group within the LootTables.json file for user reference :)
                LootGroupsData.TryCreateExampleGroup();
                DataSystem.SaveLootTables();
            }
                
            modifiedLootTables = false;

            void scanEntry(string itemKey, LootEntrySettings itemEntry, string lootTableKey, ref bool modificationFlag)
            {
                var defName = UniqueTagREGEX.Replace(itemKey, string.Empty);

                // Check if needs durability
                if (DurabilityItems.Contains(itemKey))
                {
                    if (itemEntry.DurabilitySettings is null)
                        itemEntry.DurabilitySettings = new();
                }
                else if (itemEntry.DurabilitySettings is not null)
                    itemEntry.DurabilitySettings = null;

                // Check if we need to scan the current entry.
                if (!(WeaponInfoCache?.ContainsKey(defName) ?? false))
                {
                    // Check if a properties object is set but shouldnt be
                    if (itemEntry.ItemEntryModifications != null)
                    {
                        // This should only happen from a misconfiguration, the plugin should never place an entry in a invalid weapon table.
                        Log($"{itemKey} from table: \"{lootTableKey}\" should not have a modification object entry, it does not support this!");

                        itemEntry.ItemEntryModifications = null;
                        modificationFlag = true;
                    }

                    return;
                }

                // Modify the flags from cache.
                WI_Cache WIEntry = WeaponInfoCache[defName]; // Cached entry with static definition data
                itemEntry.ItemEntryModifications ??= new ItemEntrySettings(); // if does not already exist create it

                // Build entry flags
                itemEntry.ItemEntryModifications.AmmunitionSettings.MaxAmmo = WIEntry.MaxAmmo;
                itemEntry.ItemEntryModifications.AmmunitionSettings.CanHoldMultipleAmmoUnits = WIEntry.MaxAmmo > 1;
                itemEntry.ItemEntryModifications.maxMods = WIEntry.MaxMods;

                // Balance profile
                itemEntry.ItemEntryModifications.BalanceItemModProbabilities();

                // Scan item mods
                if (WIEntry.MaxMods > 0 && (itemEntry.ItemEntryModifications.AttachmentSettings ??= !WIEntry.IsLiquidWeapon ? new() : null) is ItemEntrySettings.ItemModSettings itemMods)
                {
                    List<string> invalidMods = Pool.Get<List<string>>();
                    foreach (var modEntry in itemMods.itemMods)
                    {
                        // Remove if invalid
                        if (!WeaponModInfoCache.TryGetValue(modEntry.Key, out ItemSlot modSlotType))
                        {
                            Log($"Invalid weapon mod \"{modEntry.Key}\" removed from table: {lootTableKey}");
                            invalidMods.Add(modEntry.Key);
                            continue;
                        }

                        // Check if needs durability
                        if (modSlotType is ItemSlot.Barrel && modEntry.Value.Durability is not null)
                            modEntry.Value.Durability = null;

                        // Check if incompatible
                        if (!WIEntry.ModTypes.HasFlag(modSlotType))
                        {
                            Log($"Removed incompatible attachment \"{modEntry.Key}\" assigned to item {itemKey} in table: {lootTableKey}");
                            invalidMods.Add(modEntry.Key);
                        }
                    }

                    if (invalidMods.Any())
                        itemEntry.ItemEntryModifications.AttachmentSettings.itemMods.RemoveAll(x => invalidMods.Contains(x));

                    Pool.FreeUnmanaged(ref invalidMods);
                    
                } else if (WIEntry.MaxMods is 0 && itemEntry?.ItemEntryModifications?.AttachmentSettings is not null)
                { // if has mods property but shouldnt.
                    itemEntry.ItemEntryModifications.AttachmentSettings = null;
                }

                modificationFlag = true;
            }

            // Build entries for loot groups
            bool modifiedLootGroups = false;
            foreach(var lootProfile in lootGroups.LootGroups.ToList())
            {
                foreach(var entry in lootProfile.Value.ItemList)
                {
                    scanEntry(entry.Key, entry.Value.Amount, lootProfile.Key, ref modifiedLootGroups);

                    foreach (var bonusItem in entry.Value.Amount.additionalItems)
                        scanEntry(bonusItem.Key, bonusItem.Value, lootProfile.Key, ref modifiedLootGroups);                    
                }

                foreach (var bonusItem in lootProfile.Value.GuaranteedItems)
                    scanEntry(bonusItem.Key, bonusItem.Value, lootProfile.Key, ref modifiedLootGroups);
            }


            // Build entries for loot tables
            int activeTypes = 0;
            foreach (var lootTable in lootTables.LootTables.ToList())
            {
                var basePrefab = GameManager.server.FindPrefab(lootTable.Key);
                
                var npc = basePrefab?.GetComponent<global::HumanNPC>();
                var loot = basePrefab?.GetComponent<LootContainer>();
                var container = lootTable.Value;

                if (npc is null && loot is null)
                {
                    lootTables.LootTables.Remove(lootTable.Key);
                    Log($"Removed Invalid Loot Table {lootTable.Key}");
                    modifiedLootTables = true;

                    continue;
                }

                #region pre-v4 Loot System
                // This is the original plugin's loot system. It has not been touched aside from integrating loot profiles.

                // Groups items by rarity (weight). Reference: ItemDefinition.Rarity enum
                Items.Add(lootTable.Key, new List<string>[5]);
                Blueprints.Add(lootTable.Key, new List<string>[5]);

                for (var i = 0; i < 5; ++i)
                {
                    Items[lootTable.Key][i] = new List<string>();
                    Blueprints[lootTable.Key][i] = new List<string>();
                }

                // Scan guaranteed items
                foreach(var itemEntry in container.GuaranteedItems)
                    scanEntry(itemEntry.Key, itemEntry.Value, lootTable.Key, ref modifiedLootTables);

                // Scan ungrouped items
                foreach (var itemEntry in container.UngroupedItems)
                {
                    #region Entry Internal Flag Mapping
                    // Scan all items in profile and add internal flags
                    scanEntry(itemEntry.Key, itemEntry.Value, lootTable.Key, ref modifiedLootTables);

                    if (itemEntry.Value.additionalItems?.Any() ?? false)
                        foreach (var bonusItem in itemEntry.Value.additionalItems)
                            scanEntry(bonusItem.Key, bonusItem.Value, lootTable.Key, ref modifiedLootTables);
                    #endregion

                    bool isBP = itemEntry.Key.EndsWith(".blueprint");
                    var def = ItemManager.FindItemDefinition(UniqueTagREGEX.Replace(itemEntry.Key.Replace(".blueprint", ""), string.Empty));

                    if (def is not null)
                    {
                        if (isBP && def.Blueprint is not null && def.Blueprint.isResearchable)
                        {
                            int index = (int)def.rarity;
                            if (!Blueprints[lootTable.Key][index].Contains(def.shortname))
                                Blueprints[lootTable.Key][index].Add(def.shortname);
                        }
                        else
                        {
                            int index = (int)def.rarity;
                            if (!Items[lootTable.Key][index].Contains(itemEntry.Key))
                                Items[lootTable.Key][index].Add(itemEntry.Key);
                        }
                    }
                }

                TotalItemWeights.Add(lootTable.Key, 0);
                TotalBlueprintWeights.Add(lootTable.Key, 0);
                ItemWeights.Add(lootTable.Key, new int[5]);
                BlueprintWeights.Add(lootTable.Key, new int[5]);

                for (var i = 0; i < 5; ++i)
                {
                    TotalItemWeights[lootTable.Key] += (ItemWeights[lootTable.Key][i] = ItemWeight(BASE_ITEM_RARITY, i) * Items[lootTable.Key][i].Count);
                    TotalBlueprintWeights[lootTable.Key] += (BlueprintWeights[lootTable.Key][i] = ItemWeight(BASE_ITEM_RARITY, i) * Blueprints[lootTable.Key][i].Count);
                }
                #endregion
            }

            if (modifiedLootTables)
                DataSystem.SaveLootTables();
            
            if (modifiedLootGroups)
                DataSystem.SaveLootGroups();

            activeTypes = lootTables.LootTables.Count(table => table.Value.Enabled);

            Log($"Using '{activeTypes}' active of '{lootTables.LootTables.Count}' supported container types");
        }
        #endregion

        #region Core
        // NPC Implementation
        private bool PopulateContainer(string prefab, NPCPlayerCorpse npc)
        {
            if (npc is null || npc.IsDestroyed || (!(npc.containers?.Any() ?? false)) || npc.containers[0] is not ItemContainer inventory)
                return false;

            // API Call
            if (Interface.CallHook("ShouldBLPopulate_NPC", npc.playerSteamID) != null)
                return false;

            return PopulateContainer(inventory, prefab);
        }

        private bool PopulateContainer(LootContainer container)
        {
            if (container is null || Interface.CallHook("ShouldBLPopulate_Container", container.net.ID.Value) != null)
                return false;

            if (container.inventory is null)
            {
                container.CreateInventory(true);
                container.OnInventoryFirstCreated(container.inventory);
            }

            return PopulateContainer(container?.inventory, container?.PrefabName);
        }

        private bool PopulateContainer(ItemContainer? container, string? prefab)
        {
            if (container is null || prefab is null || !(lootTables?.LootTables?.TryGetValue(prefab, out PrefabLoot? con) ?? false) || con is null || !con.Enabled)
                return false;

            int lootItemCount = con.ItemCount;
            int min = con.ItemSettings.ItemsMin, max = con.ItemSettings.ItemsMax;
            int itemCount = GetRNG(Math.Min(min, max), Math.Max(min, max));
            if (lootItemCount > 0 && itemCount > lootItemCount && lootItemCount < 36)
                itemCount = lootItemCount;

            container.Clear();
            container.capacity = 36;
            
            List<string> itemNames = Pool.Get<List<string>>();
            List<Item> items = Pool.Get<List<Item>>();
            List<int> itemBlueprints = Pool.Get<List<int>>();
            List<KeyValuePair<string, LootEntrySettings>> guaranteedItemEntries = Pool.Get<List<KeyValuePair<string, LootEntrySettings>>>();

            guaranteedItemEntries.AddRange(con.GuaranteedItems);

            int maxRetry = 10;
            for (int i = 0; i < itemCount; ++i)
            {
                if (maxRetry is 0)
                    break;

                Item? item = null;
                List<Item>? bonusItems = null;
                List<KeyValuePair<string, LootEntrySettings>> _guaranteedItemEntries = Pool.Get<List<KeyValuePair<string, LootEntrySettings>>>();

                bool isLootGroupItem = false;

                // TODO bitwise search for larger indexes
                foreach (var import in con.LootProfiles)
                {
                    if (!import.Enabled)
                        continue;

                    if (!lootGroups.LootGroups.TryGetValue(import.LootProfileName, out LootProfile? profile) || profile is null)
                    {
                        Log($"WARNING: prefab \"{prefab}\" requested a loot group import with name \"{import.LootProfileName}\". Group does not exist!");
                        continue;
                    }
                    else if (!profile.Enabled)
                        continue;

                    // RNG => Use Profile
                    double rng_pr = RNG.NextDouble() * 1e2;

                    // RNG Check Failed, profile not used / skipped to next
                    if (rng_pr > import.LootProfileProbability)
                        continue;

                    // Ensure probabilities are generated, if not populate them
                    if (!profile.DoProbabilitiesExist)
                        profile.UpdateProbabilities();

                    // Get item
                    (item, bonusItems) = profile.GetItem();

                    if (item != null)
                    {
                        // Add all guarenteed items from profile (if profile selected use all items regardless)
                        _guaranteedItemEntries.AddRange(profile.GuaranteedItems);
                        isLootGroupItem = true;
                    }
                }

                // Loot import not used, generate from ungrouped items with default rng system
                try
                {
                    if (item == null)
                        (item, bonusItems) = MightyRNG(prefab, itemCount, itemBlueprints.Count >= con.ItemSettings.MaxBPs);
                } catch (Exception e)
                {
                    Puts($"[ERROR]: Failed to generate item for \"{prefab}\". Reason: {e.Message} \n{e.StackTrace}");
                }

                // No item was generated from either system, attempt to regenerate.
                if (item == null)
                {
                    --maxRetry;
                    --i;
                    continue;
                }

                // Duplicate checking
                var duplicatePredicate = (Item item, bool bonusItem) => 
                    ((isLootGroupItem && !_config.LootGroupsConfig.AllowLootGroupDuplicateItems) || (!bonusItem && !_config.Loot.AllowDuplicateItems) || (bonusItem && !_config.Loot.AllowBonusItemsDuplicateItems)) && (itemNames.Contains(item.info.shortname) || (item.IsBlueprint() && itemBlueprints.Contains(item.blueprintTarget)));

                if (duplicatePredicate(item, false)) {
                    item.Remove();
                    --maxRetry;
                    --i;
                    continue;
                }
                else if (item.IsBlueprint())
                {
                    itemBlueprints.Add(item.blueprintTarget);
                }
                else
                {
                    itemNames.Add(item.info.shortname);
                }

                if (storedBlacklist.ItemList.Contains(item.info.shortname))
                {
                    item.Remove(); // broken item fix
                    continue;
                }

                items.Add(item);

                // Only if bonus items are present
                if (bonusItems is not null)
                    foreach (Item bonusItem in bonusItems.Where(x => !duplicatePredicate(x, true)))
                        items.Add(bonusItem);

                guaranteedItemEntries.AddRange(_guaranteedItemEntries);
                
                Pool.FreeUnmanaged(ref _guaranteedItemEntries);
            }

            foreach (var gItemEntry in guaranteedItemEntries)
            {
                // Spawn item. No rng, just spawn em.
                Item gItem = ItemManager.CreateByPartialName(gItemEntry.Key, GetRNG(gItemEntry.Value.Min, gItemEntry.Value.Max), gItemEntry.Value.SkinId);
                if (gItem is null)
                    continue;

                if (gItemEntry.Value.DurabilitySettings is LootEntryDurability durability)
                    gItem.ChangeConditionPercentage(GetRNG(durability.MinDurability, durability.MaxDurability));

                items.Add(gItem);
            }

            int scrapAmt = 0;
            if (con.ItemSettings.MinScrap > con.ItemSettings.MaxScrap)
            { // Lower max to min
                scrapAmt = con.ItemSettings.MinScrap;
                con.ItemSettings.MaxScrap = con.ItemSettings.MinScrap;
            } else if (con.ItemSettings.MaxScrap > con.ItemSettings.MinScrap)
            {
                scrapAmt = GetRNG(con.ItemSettings.MinScrap, con.ItemSettings.MaxScrap);
            } else
            {
                scrapAmt = con.ItemSettings.MaxScrap;
            }

            items.Shuffle((uint)UnityEngine.Random.Range(0, 1000));
            foreach (var item in items.Where(x => x != null && x.IsValid()))
                if (!item.MoveToContainer(container)) // broken item fix / fixes full container 
                    item.DoRemove();

            // Add scrap
            if (scrapAmt > 0)
            {
                Item item = ItemManager.CreateByItemID(-932201673, scrapAmt * _config.Loot.ScrapMultiplier); // Scrap item ID
                if (!item.MoveToContainer(container, -1, false))
                    item.DoRemove();
            }

            container.capacity = container.itemList.Count;
            container.MarkDirty();

            Pool.FreeUnmanaged(ref items);
            Pool.FreeUnmanaged(ref itemNames);
            Pool.FreeUnmanaged(ref itemBlueprints);
            Pool.FreeUnmanaged(ref guaranteedItemEntries);

            return true;
        }

        private void UpdateInternals(bool doLog)
        {
            if (Changed)
            {
                SaveConfig();
                Changed = false;
            }

            if (doLog)
                Log("Updating internals ...");

            int populatedContainers = 0;
            int populatedNPCContainers = 0;
            int trackedNPCs = 0;

            NextTick(() =>
            {
                if (_config.Generic.RemoveStackedContainers)
                    FixLoot();

                foreach (var container in BaseNetworkable.serverEntities.Where(p => p is (not null) and LootContainer or NPCPlayerCorpse))
                {
                    // API Check
                    if (container is LootContainer lootContainer)
                    {
                        if (CustomLootSpawns is not null && CustomLootSpawns.Call<bool>("IsLootBox", container))
                            continue;
                        else if (PopulateContainer(lootContainer))
                            populatedContainers++;
                    }
                }

                if (doLog)
                    Log($"Populated ({populatedContainers}) supported loot containers.");
                    
                Initialized = true;
                populatedContainers = 0;
                trackedNPCs = 0;
            });
        }
        
        private void FixLoot()
        {
            var spawns = Resources.FindObjectsOfTypeAll<LootContainer>()
                .Where(c => c.isActiveAndEnabled)
                .OrderBy(c => c.transform.position.x).ThenBy(c => c.transform.position.z)
                .ToList();
            
            var count = spawns.Count();
            var racelimit = count ^ 2;

            var antirace = 0;
            var deleted = 0;

            for (var i = 0; i < count; i++)
            {
                var box = spawns[i];
                var pos = new Vector2(box.transform.position.x, box.transform.position.z);

                if (++antirace > racelimit)
                    return;

                var next = i + 1;
                while (next < count)
                {
                    var box2 = spawns[next];
                    var pos2 = new Vector2(box2.transform.position.x, box2.transform.position.z);
                    var distance = Vector2.Distance(pos, pos2);

                    if (++antirace > racelimit)
                        return;

                    if (distance < 0.25f)
                    {
                        spawns.RemoveAt(next);
                        count--;

                        if (box2 is BaseEntity _box2 && !_box2.IsDestroyed)
                        {
                            _box2.KillMessage();
                            deleted++;
                        }
                    }
                    else
                        break;
                }
            }

            if (deleted > 0)
                Log($"Removed {deleted} stacked LootContainer");
            else
                Log($"No stacked LootContainer found.");
        }
        
        private (Item? item, List<Item>? bonusItem)MightyRNG(string type, int itemCount, bool blockBPs = false)
        {
            List<string>? selectFrom = Pool.Get<List<string>>();
            List<Item>? bonusItems = null;
            Item? item;
            bool asBP = RNG.NextDouble() < _config.Generic.BlueprintProbability && !blockBPs;
            string itemEntryName = string.Empty;  // Set here for reference after generation
            int maxRetry = 10 * itemCount;
            int limit = 0;
            
            do
            {
                if (selectFrom.Any())
                {
                    Pool.FreeUnmanaged(ref selectFrom);
                    selectFrom = Pool.Get<List<string>>();
                }

                item = null;

                var _totalWeight = 0;
                List<int> _weightList = Pool.Get<List<int>>();
                var _prefabList = Pool.Get<List<List<string>>>();

                _totalWeight = asBP ? TotalBlueprintWeights[type] : TotalItemWeights[type];
                _weightList.AddRange(asBP ? BlueprintWeights[type] : ItemWeights[type]);
                _prefabList.AddRange(asBP ? Blueprints[type] : Items[type]);

                var r = RNG.Next(_totalWeight);
                for (int i = 0; i < 5; ++i)
                {
                    limit += _weightList[i];
                    if (r < limit)
                    {
                        selectFrom.AddRange(_prefabList[i]);
                        break;
                    }
                }

                Pool.FreeUnmanaged(ref _weightList);
                Pool.FreeUnmanaged(ref _prefabList);

                if (!selectFrom.Any())
                {
                    if (--maxRetry <= 0)
                        break;

                    continue;
                }

                // Select item name
                itemEntryName = selectFrom[RNG.Next(0, selectFrom.Count)];

                string itemShortname = UniqueTagREGEX.Replace(itemEntryName, string.Empty);  // Remove tag

                ItemDefinition itemDef = ItemManager.FindItemDefinition(itemShortname);
                if (asBP && itemDef.Blueprint != null && itemDef.Blueprint.isResearchable)
                {
                    var blueprintBaseDef = ItemManager.FindItemDefinition("blueprintbase");
                    item = ItemManager.Create(blueprintBaseDef, 1);
                    item.blueprintTarget = itemDef.itemid;
                }
                else
                    item = ItemManager.CreateByName(itemShortname, 1);

                if (item is null || item.info is null)
                    continue;
                break;
            } while (true);

            if (selectFrom is not null && selectFrom.Any())
                Pool.FreeUnmanaged(ref selectFrom);

            if (item is null)
                return (null, null);

            if (lootTables.LootTables.TryGetValue(type, out PrefabLoot? entry) && entry.UngroupedItems.TryGetValue(itemEntryName, out LootEntry lootEntry))
            {
                // Apply custom properties
                item.amount = GetRNG(Math.Min(lootEntry.Min, lootEntry.Max), Math.Max(lootEntry.Min, lootEntry.Max)) * _config.Loot.LootMultiplier;
                item.skin = lootEntry.SkinId;

                if (!string.IsNullOrWhiteSpace(lootEntry.DisplayName))
                    item.name = lootEntry.DisplayName;

                lootEntry?.ApplyAttachments(item); // Apply attachments to main item
                lootEntry?.ApplyAmmo(item);
                lootEntry?.CreateBonusItems(ref bonusItems); // Create bonus items and apply attachments

                // Apply durability
                if (lootEntry.DurabilitySettings is LootEntryDurability durabilitySettings)
                    item.ChangeConditionPercentage(GetRNG(durabilitySettings.MinDurability, durabilitySettings.MaxDurability));
                else 
                    item.MarkDirty();
            }

            item.OnVirginSpawn();
            return (item, bonusItems);
        }
        
        private bool ItemExists(string name) => 
            ItemManager.itemList.Any(x => x.shortname == name);
        
        // API
        private bool isSupplyDropActive()
        {
            if (!lootTables.LootTables.TryGetValue("assets/prefabs/misc/supply drop/supply_drop.prefab", out PrefabLoot? con) || con is null)
                return false;

            if (con.Enabled)
                return true;

            return false;
        }
        #endregion

        #region Looty API Commands
        [ChatCommand("looty")]
        private void LootyConfigDownload(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, ADMIN_PERM))
            {
                SendMessage(player, BLLang("perm"));
                return;
            }

            if (args.Length != 1)
            {
                SendMessage(player, BLLang("lootycmdformat", player.UserIDString));
                return;
            }

            GetLootyAPI(args[0], player);
        }

        [ConsoleCommand("looty")]
        private void LootyConfigDownload_Console(Arg arg)
        {
            if (!arg.IsRcon)
            {
                arg.ReplyWith("Error: Should not execute command outside of RCON.");
                return;
            }

            if (arg.Args is not string[] args || args.Length != 1)
            {
                Puts(BLLang("lootycmdformat"));
                Puts("Please visit https://looty.cc/betterloot-v4 to create your custom loot configuration!");
                return;
            }

            // Send Request
            GetLootyAPI(args[0]);
        }

        #region Processing Routine
        private void GetLootyAPI(string lootyId, BasePlayer? player = null)
        {
            // Compatibility between console and chat
            void Respond(string key)
            {
                string lang = BLLang(key);
                if (player is not null)
                    SendMessage(player, BLLang(key));
                else
                    Puts(BLLang(key));
            }

            IEnumerator SendRequest()
            {
                using (UnityWebRequest www = UnityWebRequest.Get($"https://looty.cc/api/fetch-loot-table?id={lootyId}"))
                {
                    Respond($"Attempting to download configuration: {lootyId}");
                    yield return www.SendWebRequest();

                    if (www.result is UnityWebRequest.Result.ConnectionError or UnityWebRequest.Result.ProtocolError)
                    {
                        long code = www.responseCode;
                        if (www.result is UnityWebRequest.Result.ProtocolError && (code == 404 || code == 410))
                            Respond("lootynotfound");

                        Puts($"Error: Could not download request: {www.result} ({www.responseCode})");
                    }
                    else
                    {
                        bool restoreFailsafe = false;

                        try
                        {
                            LootyResponse tableData = JsonConvert.DeserializeObject<LootyResponse>(www.downloadHandler.text);

                            if (tableData.IsUnityNull())
                            {
                                Respond("Error: Failed to load data. Aborting...");
                                yield break;
                            }

                            #region LootTable.json Update
                            if (lootTables != null)
                            {
                                lootTables.LootTables = tableData.LootTables;
                            }
                            else
                            {
                                lootTables = new LootTableData();
                                lootTables.LootTables = tableData.LootTables;
                            }

                            DataSystem.BakDataFile("LootTables");
                            restoreFailsafe = true; // Failsafe flag. Restore on error / fail

                            DataSystem.SaveLootTables();

                            Respond("Loaded new LootTable successfully!");
                            #endregion

                            #region LootGroups.json Update
                            if (tableData.LootGroups != null)
                            {
                                if (lootGroups != null)
                                {
                                    lootGroups.LootGroups = tableData.LootGroups;
                                }
                                else
                                {
                                    // Data non-existant, create new data and save.
                                    lootGroups = new LootGroupsData();
                                    LootGroupsData.TryCreateExampleGroup();
                                }

                                DataSystem.BakDataFile("LootGroups");
                                DataSystem.SaveLootGroups();

                                Respond("Loaded new LootGroups.json successfully");
                            }
                            #endregion

                            InitLootSystem(true);
                        }
                        catch (Exception error)
                        {
                            Respond("Error loading requested LootTable.");

                            if (restoreFailsafe)
                            {
                                Respond("Restoring backup file.");
                                DataSystem.BakDataFile("LootTables", true);
                            }

                            Puts($"Please forward this message to the developer. {error}");
                        }
                    }
                }
            }

            ServerMgr.Instance.StartCoroutine(SendRequest());
        }
        #endregion
        #endregion

        #region Commands
        #region Backup / Restore
        [ChatCommand("bl-backup")]
        private void ManualBackupCommand(BasePlayer player)
            => ManualBackupRestore(player, false);

        [ChatCommand("bl-restore")]
        private void RestoreBackupCommand(BasePlayer player)
            => ManualBackupRestore(player, true);

        private void ManualBackupRestore(BasePlayer player, bool restore)
        {
            if (!permission.UserHasPermission(player.UserIDString, ADMIN_PERM))
            {
                SendMessage(player, BLLang("perm"));
                return;
            }

            DataSystem.BakDataFile("LootTables", restore, player);
        }

        [ConsoleCommand("bl-backup")]
        private void ManualBackupCommand_Console(Arg arg)
            => ManualBackupRestore_Console(arg, false);

        [ConsoleCommand("bl-restore")]
        private void RestoreBackupCommand_Console(Arg arg)
            => ManualBackupRestore_Console(arg, true);

        private void ManualBackupRestore_Console(Arg arg, bool restore)
        {
            if (!arg.IsRcon)
                return;

            DataSystem.BakDataFile("LootTables", restore);
        }
        #endregion

        [ChatCommand("blacklist")]
        private void CmdChatBlacklistNew(BasePlayer player, string command, string[] args)
        {
            if (!Initialized)
            {
                SendMessage(player, BLLang("initialized")); 
                return;
            }

            if (!permission.UserHasPermission(player.UserIDString, ADMIN_PERM))
            {
                SendMessage(player, BLLang("perm")); 
                return;
            }

            if (args.Length is 0)
            {
                if (storedBlacklist.ItemList.Count is 0)
                    SendMessage(player, BLLang("none"));
                else
                {
                    string _BLItems = string.Join(", ", storedBlacklist.ItemList);
                    SendMessage(player, BLLang("blocked", player.UserIDString, _BLItems));
                }

                return;
            }

            switch (args[0].ToLower())
            {
                case "additem":
                    if (!ItemExists(args[1]))
                    {
                        SendMessage(player, BLLang("notvalid", player.UserIDString, args[1]));
                        return;
                    }

                    if (!storedBlacklist.ItemList.Contains(args[1]))
                    {
                        storedBlacklist.ItemList.Add(args[1]);
                        UpdateInternals(false);
                        SendMessage(player, BLLang("blockedpass", player.UserIDString, args[1]));
                        DataSystem.SaveBlacklist();
                        return;
                    }

                    SendMessage(player, BLLang("blockedtrue", player.UserIDString, args[1]));
                    break;
                case "deleteitem":
                    if (!ItemExists(args[1]))
                    {
                        SendMessage(player, BLLang("notvalid", player.UserIDString, args[1]));
                        return;
                    }

                    if (storedBlacklist.ItemList.Contains(args[1]))
                    {
                        storedBlacklist.ItemList.Remove(args[1]);
                        UpdateInternals(false);
                        SendMessage(player, BLLang("unblacklisted", player.UserIDString, args[1]));
                        DataSystem.SaveBlacklist();
                        return;
                    }

                    SendMessage(player, BLLang("blockedfalse", player.UserIDString, args[1]));
                    break;
                default:
                    SendMessage(player, BLLang("syntax"));
                    break;
            }
        }
        #endregion

        #region Hammer loot cycle
        private void OnMeleeAttack(BasePlayer player, HitInfo c)
        {
            if (!_config.Loot.EnableHammerLootCycle || player is null || c is null || player.GetActiveItem() is not Item item || item.hasCondition || !player.IsAdmin || !item.ToString().Contains("hammer")) 
                return;

            BaseEntity entity = c.HitEntity;
            ItemContainer container = null;

            string panelName = string.Empty;
            bool isNpc = false;

            if (entity is NPCPlayerCorpse npc)
            {
                _instance.SendMessage(player, "Cannot rotate loot directly on corpse.");
                return;
            }
            else if (entity.GetComponent<LootContainer>() is StorageContainer _inv)
            {
                container = _inv.inventory;
                panelName = _inv.panelName;
            }   
            else
                return;
                
            HammerHitLootCycle lootCycle = entity.gameObject.AddComponent<HammerHitLootCycle>();

            player.inventory.loot.StartLootingEntity(entity, false);
            player.inventory.loot.AddContainer(container);
            player.inventory.loot.SendImmediate();
            player.ClientRPC(RpcTarget.Player("RPC_OpenLootPanel", player), panelName);
        }

        private class HammerHitLootCycle : FacepunchBehaviour
        {
            private void Awake()
                => InvokeRepeating(Repeater, 0, (float)_config.Loot.HammerLootCycleTime);

            private void Repeater()
            {
                if (!enabled) 
                    return;

                LootContainer loot = GetComponent<LootContainer>();
                _instance.PopulateContainer(loot);
            }

            private void PlayerStoppedLooting(BasePlayer _)
            {
                CancelInvoke(Repeater);
                Destroy(this);
            }
        }
        #endregion
    }
}

namespace Oxide.Plugins.BetterLootExtensions
{
    public static class BetterLootExtensions
    {
        public static void ChangeConditionPercentage(this Item item, int conditionPercentage)
            => item.condition = (conditionPercentage / 100f) * item.maxCondition;

        public static bool ContainsPartial(this List<string> list, string partialString)
            => list.Any(partialString.Contains);
        public static bool IsDefault<T>(this T obj)
            => EqualityComparer<T>.Default.Equals(obj, default);
        public static int RemoveAll<TKey, TValue>(this IDictionary<TKey, TValue> dict, Func<TKey, bool> predicate)
        {
            int removeCount = 0;
            var keys = dict.Keys.Where(k => predicate(k)).ToList();
            foreach (var key in keys)
                if (dict.Remove(key))
                    removeCount++;
            return removeCount;
        }
    }
}