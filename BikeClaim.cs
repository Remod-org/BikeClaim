#region License (GPL v2)
/*
    BikeClaim
    Copyright (c) 2024 RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License v2.0.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using Rust;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("BikeClaim", "RFC1920", "0.0.2")]
    [Description("Manage bicycle ownership and access")]

    internal class BikeClaim : RustPlugin
    {
        private ConfigData configData;

        [PluginReference]
        private readonly Plugin Friends, Clans, GridAPI;

        private static Dictionary<ulong, ulong> bikes = new Dictionary<ulong, ulong>();
        private static Dictionary<ulong, HTimer> htimer = new Dictionary<ulong, HTimer>();
        private const string permClaim_Use = "bikeclaim.claim";
        private const string permSpawn_Use = "bikeclaim.spawn";
        private const string permFind_Use = "bikeclaim.find";
        private const string permVIP = "bikeclaim.vip";
        private bool enabled;

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        #endregion

        #region hooks
        private void LoadData() => bikes = Interface.GetMod().DataFileSystem.ReadObject<Dictionary<ulong, ulong>>($"{Name}/ridables");
        private void SaveData() => Interface.GetMod().DataFileSystem.WriteObject($"{Name}/ridables", bikes);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["notauthorized"] = "You are not authorized for this command!",
                ["bikeclaimed"] = "You have claimed this bike!",
                ["bikelimit"] = "You have reached the limit for claiming bikes({0})!",
                ["bikereleased"] = "You have released this bike!",
                ["yourbike"] = "You have already claimed this bike!",
                ["yourbike2"] = "Well, hello there.",
                ["bikespawned"] = "You have spawned a bike!",
                ["bikeowned"] = "Someone else owns this bike!",
                ["serverowned"] = "Server-owned, free bike.",
                ["bikeinfo"] = "Health: {0}\n  Owner: {1}\n  {2}",
                ["notyourbike"] = "Someone else owns this bike.  Perhaps no one...",
                ["nobikes"] = "No bikes found.",
                ["foundbike"] = "Your bike is {0}m away in {1}."
            }, this);
        }

        private void OnServerInitialized()
        {
            LoadConfigVariables();
            LoadData();
            enabled = true;

            AddCovalenceCommand("bclaim", "CmdClaim");
            AddCovalenceCommand("brelease", "CmdRelease");
            AddCovalenceCommand("bspawn", "CmdSpawn");
            AddCovalenceCommand("bremove", "CmdRemove");
            AddCovalenceCommand("bfind", "CmdFindBike");
            AddCovalenceCommand("binfo", "CmdBikeInfo");
            permission.RegisterPermission(permClaim_Use, this);
            permission.RegisterPermission(permFind_Use, this);
            permission.RegisterPermission(permSpawn_Use, this);
            permission.RegisterPermission(permVIP, this);

            // Fix ownership for bikes perhaps previously claimed but not current managed.
            foreach (Bike bike in UnityEngine.Object.FindObjectsOfType<Bike>())
            {
                if (!bikes.ContainsKey((uint)bike.net.ID.Value) && bike.OwnerID != 0)
                {
                    DoLog($"Setting owner of unmanaged bike {bike.net.ID} back to server.");
                    bike.OwnerID = 0;
                }
            }
        }

        private void OnServerShutdown()
        {
            if (configData.Options.EnableTimer)
            {
                // Prevent bike ownership from persisting across reboots if the timeout timer was enabled
                foreach (KeyValuePair<ulong, ulong> data in bikes)
                {
                    BaseNetworkable bike = BaseNetworkable.serverEntities.Find(new NetworkableId(data.Key));
                    if (bike != null)
                    {
                        BaseEntity bbike = bike as BaseEntity;
                        if (bbike != null) bbike.OwnerID = 0;
                    }
                }
                bikes = new Dictionary<ulong, ulong>();
                SaveData();
            }
        }

        private void OnNewSave()
        {
            bikes = new Dictionary<ulong, ulong>();
            SaveData();
        }

        private object OnEntityTakeDamage(Bike bike, HitInfo hitInfo)
        {
            if (!enabled) return null;
            if (bike == null) return null;
            if (hitInfo == null) return null;
            DamageType majority = hitInfo.damageTypes.GetMajorityDamageType();
            if (bikes.ContainsKey((uint)bike.net.ID.Value))
            {
                if (bike.InSafeZone()) return true;

                if (majority == DamageType.Decay)
                {
                    if (configData.Options.AllowDecay) return null;
                    DoLog("Blocking decay damage.");
                    return true;
                }

                DoLog($"{bike.net.ID} owned by {bike.OwnerID} is being attacked!");

                if (!configData.Options.AllowDamage)
                {
                    DoLog($"{bike.net.ID} damaged blocked");
                    return true;
                }

                if (configData.Options.TCPreventDamage)
                {
                    BuildingPrivlidge tc = bike.GetBuildingPrivilege();
                    if (tc != null)
                    {
                        if (configData.Options.TCMustBeAuthorized)
                        {
                            // Verify bike owner is registered to the TC
                            //foreach (ProtoBuf.PlayerNameID p in tc.authorizedPlayers)
                            foreach (ulong auth in tc.authorizedPlayers.Select(x => x.userid).ToArray())
                            {
                                if (auth == bike.OwnerID)
                                {
                                    // Bike owner is registered to the TC, block damage.
                                    DoLog($"{bike.net.ID} owned by {bike.OwnerID} protected by local TC to which the owner is registered.");
                                    return true;
                                }
                            }
                            // Bike owner is NOT registered to the TC, allow damage.
                            DoLog($"{bike.net.ID} owned by {bike.OwnerID} NOT protected by local TC since the owner is not registered.");
                            return null;
                        }
                        // No local auth required, block damage since we are in BP
                        DoLog($"{bike.net.ID} owned by {bike.OwnerID} protected by local TC.");
                        return true;
                    }
                }
            }
            return null;
        }

        private void OnEntityDeath(Bike entity, HitInfo info)
        {
            if (!enabled) return;
            if (entity == null) return;
            if (bikes.ContainsKey((uint)entity.net.ID.Value))
            {
                DoLog($"DeadBike: {entity.net.ID} owned by {entity.OwnerID}");
                bikes.Remove((uint)entity.net.ID.Value);
                SaveData();
            }
        }

        private object CanLootEntity(BasePlayer player, Bike bike)
        {
            if (!enabled) return null;
            if (!configData.Options.RestrictStorage) return null;
            if (player == null) return null;

            if (bike != null && bikes.ContainsKey((uint)bike.net.ID.Value))
            {
                if (IsFriend(player.userID, bike.OwnerID))
                {
                    DoLog("Bike storage access allowed.");
                    return null;
                }
                Message(player.IPlayer, "bikeowned");
                DoLog($"Bike storage access blocked.");
                return true;
            }

            return null;
        }

        private object CanMountEntity(BasePlayer player, BaseMountable mountable)
        {
            if (!enabled) return null;
            if (player == null) return null;
            if (mountable == null) return null;

            if (!configData.Options.RestrictMounting) return null;
            if (player == null) return null;
            if (mountable == null) return null;

            Bike bike = mountable.GetComponentInParent<Bike>();
            if (bike != null)
            {
                if (bike?.OwnerID == 0) return null;

                if (bikes?.Count > 0 && bikes.ContainsKey((uint)bike.net.ID.Value))
                {
                    DoLog($"Player {player.userID} wants to mount bike {bike.net.ID}");
                    if (IsFriend(player.userID, bike.OwnerID))
                    {
                        DoLog("Mounting allowed.");
                        return null;
                    }
                    Message(player.IPlayer, "bikeowned");
                    DoLog("Mounting blocked.");
                    return true;
                }
            }

            return null;
        }

        private void OnEntityMounted(BaseMountable mountable, BasePlayer player)
        {
            if (!enabled) return;
            if (player == null) return;
            if (!player.userID.IsSteamId()) return;
            if (mountable == null) return;
            if (!configData.Options.SetOwnerOnFirstMount) return;

            Bike bike = mountable.GetComponentInParent<Bike>();

            if (bike != null)
            {
                ulong userid = player.userID;
                if (IsAtLimit(userid) && bike.OwnerID != userid)
                {
                    if (permission.UserHasPermission(userid.ToString(), permVIP))
                    {
                        Message(player.IPlayer, "bikelimit", configData.Options.VIPLimit);
                    }
                    else
                    {
                        Message(player.IPlayer, "bikelimit", configData.Options.Limit);
                    }
                    return;
                }

                if (bike.OwnerID == userid)
                {
                    Message(player.IPlayer, "yourbike2");
                }
                else if (!bikes.ContainsKey((uint)bike.net.ID.Value) && bike.OwnerID == 0)
                {
                    ClaimBike(bike, player);
                    DoLog($"Player {player.userID} mounted bike {bike.net.ID} and now owns it.");
                }
                else
                {
                    Message(player.IPlayer, "bikeowned");
                }
            }
        }
        #endregion

        public void ClaimBike(Bike bike, BasePlayer player)
        {
            bike.OwnerID = player.userID;

            uint bikeid = (uint)bike.net.ID.Value;
            bikes.Remove(bikeid);
            bikes.Add(bikeid, player.userID);
            SaveData();

            if (configData.Options.EnableTimer)
            {
                htimer.Add(bikeid, new HTimer() { start = Time.realtimeSinceStartup, countdown = configData.Options.ReleaseTime, userid = player.userID });
                HandleTimer(bikeid, player.userID, true);
            }
            if (configData.Options.SetHealthOnClaim)
            {
                bike.SetHealth(100);
            }

            Message(player.IPlayer, "bikeclaimed");
        }

        #region commands
        [Command("binfo")]
        private void CmdBikeInfo(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permClaim_Use)) { Message(iplayer, "notauthorized"); return; }

            BasePlayer player = iplayer.Object as BasePlayer;
            List<Bike> hlist = new List<Bike>();
            Vis.Entities(player.transform.position, 1f, hlist);
            foreach (Bike bike in hlist)
            {
                if (bike != null)
                {
                    string owner = bike.OwnerID > 0 ? FindPlayerById(bike.OwnerID) : Lang("serverowned");
                    Message(iplayer, "bikeinfo", bike.health, owner);
                }
            }
        }

        [Command("bfind")]
        private void CmdFindBike(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permFind_Use)) { Message(iplayer, "notauthorized"); return; }
            bool found = false;

            foreach (KeyValuePair<ulong, ulong> h in bikes)
            {
                if (h.Value == Convert.ToUInt64(iplayer.Id))
                {
                    found = true;
                    BasePlayer player = iplayer.Object as BasePlayer;
                    BaseEntity entity = BaseNetworkable.serverEntities.Find(new NetworkableId(h.Key)) as BaseEntity;

                    string hloc = PositionToGrid(entity.transform.position);
                    string dist = Math.Round(Vector3.Distance(entity.transform.position, player.transform.position)).ToString();
                    Message(iplayer, "foundbike", dist, hloc);

                    break;
                }
            }
            if (!found) Message(iplayer, "nobikes");
        }

        [Command("bspawn")]
        private void CmdSpawn(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permSpawn_Use)) { Message(iplayer, "notauthorized"); return; }

            if (IsAtLimit(Convert.ToUInt64(iplayer.Id)))
            {
                if (iplayer.HasPermission(permVIP))
                {
                    Message(iplayer, "bikelimit", configData.Options.VIPLimit);
                }
                else
                {
                    Message(iplayer, "bikelimit", configData.Options.Limit);
                }
                return;
            }

            BasePlayer player = iplayer.Object as BasePlayer;
            const string staticprefab = "assets/content/vehicles/bikes/pedalbike.prefab";

            Vector3 spawnpos = player.eyes.position + (player.transform.forward * 2f);
            spawnpos.y = TerrainMeta.HeightMap.GetHeight(spawnpos);
            Vector3 rot = player.transform.rotation.eulerAngles;
            rot = new Vector3(rot.x, rot.y + 180, rot.z);
            BaseEntity bike = GameManager.server.CreateEntity(staticprefab, spawnpos, Quaternion.Euler(rot), true);

            if (bike)
            {
                bike.Spawn();
                if (iplayer.HasPermission(permClaim_Use))
                {
                    CmdClaim(iplayer, "bclaim", null);
                }
                Message(iplayer, "bikespawned");
            }
            else
            {
                bike.Kill();
            }
        }

        [Command("bremove")]
        private void CmdRemove(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permSpawn_Use)) { Message(iplayer, "notauthorized"); return; }

            List<Bike> hlist = new List<Bike>();
            BasePlayer player = iplayer.Object as BasePlayer;
            Vis.Entities(player.transform.position, 1f, hlist);
            bool found = false;
            foreach (Bike bike in hlist)
            {
                if (bike)
                {
                    found = true;
                    if (bike.OwnerID == player.userID && bikes.ContainsKey((uint)bike.net.ID.Value))
                    {
                        bike.Hurt(500);
                    }
                    else
                    {
                        Message(iplayer, "notyourbike");
                    }
                }
            }
            if (!found) Message(iplayer, "nobikes");
        }

        [Command("bclaim")]
        private void CmdClaim(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permClaim_Use)) { Message(iplayer, "notauthorized"); return; }

            BasePlayer player = iplayer.Object as BasePlayer;
            List<Bike> hlist = new List<Bike>();
            Vis.Entities(player.transform.position, 1f, hlist);
            bool found = false;
            foreach (Bike bike in hlist)
            {
                if (bike)
                {
                    found = true;
                    ulong userid = player.userID;
                    if (IsAtLimit(userid))
                    {
                        if (iplayer.HasPermission(permVIP))
                        {
                            Message(iplayer, "bikelimit", configData.Options.VIPLimit);
                        }
                        else
                        {
                            Message(iplayer, "bikelimit", configData.Options.Limit);
                        }
                        return;
                    }

                    if (bike.OwnerID == player.userID)
                    {
                        Message(iplayer, "yourbike");
                    }
                    else if (!bikes.ContainsKey((uint)bike.net.ID.Value) && bike.OwnerID == 0)
                    {
                        ClaimBike(bike, player);
                        break;
                    }
                    else
                    {
                        Message(iplayer, "bikeowned");
                    }
                }
                break;
            }
            if (!found) Message(iplayer, "nobikes");
        }

        [Command("brelease")]
        private void CmdRelease(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permClaim_Use)) { Message(iplayer, "notauthorized"); return; }

            BasePlayer player = iplayer.Object as BasePlayer;
            List<Bike> hlist = new List<Bike>();
            Vis.Entities(player.transform.position, 1f, hlist);
            bool found = false;
            foreach (Bike bike in hlist)
            {
                if (bike)
                {
                    found = true;
                    if (bike.OwnerID == player.userID)
                    {
                        bike.OwnerID = 0;
                        uint bikeid = (uint)bike.net.ID.Value;
                        bikes.Remove(bikeid);
                        HandleTimer(bikeid, bike.OwnerID);
                        SaveData();
                        Message(iplayer, "bikereleased");
                        break;
                    }
                    else
                    {
                        Message(iplayer, "notyourbike");
                    }
                }
            }
            if (!found) Message(iplayer, "nobikes");
        }
        #endregion

        #region helpers
        private static string FindPlayerById(ulong userid)
        {
            foreach (BasePlayer current in BasePlayer.allPlayerList)
            {
                if (current.userID == userid)
                {
                    return current.displayName;
                }
            }
            return "";
        }

        public string PositionToGrid(Vector3 position)
        {
            if (GridAPI != null)
            {
                string[] g = (string[])GridAPI.CallHook("GetGrid", position);
                return string.Concat(g);
            }
            else
            {
                // From GrTeleport
                Vector2 r = new Vector2((World.Size / 2) + position.x, (World.Size / 2) + position.z);
                float x = Mathf.Floor(r.x / 146.3f) % 26;
                float z = Mathf.Floor(World.Size / 146.3f) - Mathf.Floor(r.y / 146.3f);

                return $"{(char)('A' + x)}{z - 1}";
            }
        }

        private void DoLog(string message)
        {
            if (configData.Options.debug) Interface.GetMod().LogInfo($"[{Name}] {message}");
        }

        private void PurgeInvalid()
        {
            bool found = false;
            List<ulong> toremove = new List<ulong>();
            foreach (ulong bike in bikes.Keys)
            {
                if (BaseNetworkable.serverEntities.Find(new NetworkableId((uint)bike)) == null)
                {
                    toremove.Add(bike);
                    found = true;
                }
            }
            foreach (ulong bike in toremove)
            {
                bikes.Remove(bike);
            }
            if (found) SaveData();
        }

        private bool IsAtLimit(ulong userid)
        {
            PurgeInvalid();
            if (configData.Options.EnableLimit)
            {
                DoLog($"Checking bike limit for {userid}");
                float amt = 0f;
                foreach (KeyValuePair<ulong, ulong> bike in bikes)
                {
                    if (bike.Value == userid)
                    {
                        DoLog($"Found matching userid {bike.Value}");
                        amt++;
                    }
                }
                DoLog($"Player has {amt} bikes");
                if (amt > 0 && amt >= configData.Options.VIPLimit && permission.UserHasPermission(userid.ToString(), permVIP))
                {
                    DoLog($"VIP player has met or exceeded the limit of {configData.Options.VIPLimit}");
                    return true;
                }
                if (amt > 0 && amt >= configData.Options.Limit)
                {
                    DoLog($"Non-vip player has met or exceeded the limit of {configData.Options.Limit}");
                    return true;
                }
                DoLog("Player is under the limit.");
                return false;
            }
            DoLog("Limits not enabled.");
            return false;
        }

        private void HandleTimer(ulong bikeid, ulong userid, bool start = false)
        {
            if (htimer.ContainsKey(bikeid))
            {
                if (start)
                {
                    htimer[bikeid].timer = timer.Once(htimer[bikeid].countdown, () => HandleTimer(bikeid, userid, false));
                    DoLog($"Started release timer for bike {bikeid} owned by {userid}");
                }
                else
                {
                    if (htimer.ContainsKey(bikeid))
                    {
                        htimer[bikeid].timer.Destroy();
                        htimer.Remove(bikeid);
                    }

                    try
                    {
                        BaseNetworkable bike = BaseNetworkable.serverEntities.Find(new NetworkableId((uint)bikeid));
                        BasePlayer player = RustCore.FindPlayerByIdString(userid.ToString());
                        Bike mounted = player.GetMounted().GetComponentInParent<Bike>();

                        if ((uint)mounted.net.ID.Value == bikeid && configData.Options.ReleaseOwnerOnBike)
                        {
                            // Player is on this bike and we allow ownership to be removed while on the bike
                            mounted.OwnerID = 0;
                            bikes.Remove(bikeid);
                            DoLog($"Released bike {bikeid} owned by {userid}");
                        }
                        else if ((uint)mounted.net.ID.Value == bikeid && !configData.Options.ReleaseOwnerOnBike)
                        {
                            // Player is on this bike and we DO NOT allow ownership to be removed while on the bike
                            // Reset the timer...
                            htimer.Add(bikeid, new HTimer() { start = Time.realtimeSinceStartup, countdown = configData.Options.ReleaseTime, userid = userid });
                            htimer[bikeid].timer = timer.Once(configData.Options.ReleaseTime, () => HandleTimer(bikeid, userid));
                            DoLog($"Reset ownership timer for bike {bikeid} owned by {userid}");
                        }
                        else
                        {
                            // Player is NOT mounted on this bike...
                            BaseEntity bbike = bike as BaseEntity;
                            bbike.OwnerID = 0;
                            bikes.Remove(bikeid);
                            DoLog($"Released bike {bikeid} owned by {userid}");
                        }
                        SaveData();
                    }
                    catch
                    {
                        BaseNetworkable bike = BaseNetworkable.serverEntities.Find(new NetworkableId((uint)bikeid));
                        BaseEntity bbike = bike as BaseEntity;
                        bbike.OwnerID = 0;
                        bikes.Remove(bikeid);
                        SaveData();
                        DoLog($"Released bike {bikeid} owned by {userid}");
                    }
                }
            }
        }

        private bool IsFriend(ulong playerid, ulong ownerid)
        {
            if (ownerid == playerid) return true;
            if (configData.Options.useFriends && Friends != null)
            {
                object fr = Friends?.CallHook("AreFriends", playerid, ownerid);
                if (fr != null && (bool)fr)
                {
                    return true;
                }
            }
            if (configData.Options.useClans && Clans != null)
            {
                string playerclan = (string)Clans?.CallHook("GetClanOf", playerid);
                string ownerclan = (string)Clans?.CallHook("GetClanOf", ownerid);
                if (playerclan == ownerclan && playerclan != null && ownerclan != null)
                {
                    return true;
                }
            }
            if (configData.Options.useTeams)
            {
                RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(playerid);
                if (playerTeam?.members.Contains(ownerid) == true)
                {
                    return true;
                }
            }
            return false;
        }
        #endregion

        #region config
        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < new VersionNumber(1, 0, 17))
            {
                configData.Options.AllowDecay = false;
                configData.Options.AllowDamage = true;
                configData.Options.TCPreventDamage = true;
                configData.Options.TCMustBeAuthorized = true;
            }
            if (configData.Version < new VersionNumber(1, 0, 18))
            {
                configData.Options.SetHealthOnClaim = true;
            }

            configData.Version = Version;
            SaveConfig(configData);
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData
            {
                Options = new Options()
                {
                    useClans = false,
                    useFriends = false,
                    useTeams = false,
                    debug = false,
                    SetOwnerOnFirstMount = true,
                    ReleaseOwnerOnBike = false,
                    RestrictMounting = false,
                    RestrictStorage = false,
                    EnableTimer = false,
                    EnableLimit = true,
                    AllowDecay = false,
                    AllowDamage = true,
                    TCPreventDamage = true,
                    TCMustBeAuthorized = true,
                    ReleaseTime = 600f,
                    Limit = 2f,
                    VIPLimit = 5f
                },
                Version = Version
            };
            SaveConfig(config);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }

        private class ConfigData
        {
            public Options Options;
            public VersionNumber Version;
        }

        public class Options
        {
            public bool useClans;
            public bool useFriends;
            public bool useTeams;
            public bool debug;
            public bool SetOwnerOnFirstMount;
            public bool ReleaseOwnerOnBike;
            public bool RestrictMounting;
            public bool RestrictStorage;
            public bool EnableTimer;
            public bool EnableLimit;
            public bool AllowDecay;
            public bool AllowDamage;
            public bool TCPreventDamage;
            public bool TCMustBeAuthorized;
            public float ReleaseTime;
            public float Limit;
            public float VIPLimit;
            public bool SetHealthOnClaim;
        }

        public class HTimer
        {
            public Timer timer;
            public float start;
            public float countdown;
            public ulong userid;
        }
        #endregion
    }
}
