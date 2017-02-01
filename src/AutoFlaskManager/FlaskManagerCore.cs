﻿using PoeHUD.Controllers;
using PoeHUD.Poe.Components;
using PoeHUD.Plugins;
using System.Collections.Generic;
using PoeHUD.Poe;
using System.Windows.Forms;
using System.Threading;
using System;
using PoeHUD.Poe.EntityComponents;
using PoeHUD.Poe.Elements;
using System.Runtime.InteropServices;
using SharpDX;
using System.Diagnostics;
using PoeHUD.Hud.Health;
using System.IO;
using Newtonsoft.Json;

namespace FlaskManager
{
    public class FlaskManagerCore : BaseSettingsPlugin<FlaskManagerSettings>
    {
        private bool DEBUG = false;
        private int logmsg_time = 1;
        private int errmsg_time = 10;
        private bool isThreadEnabled;
        private Element FlasksRoot;
        private IntPtr gameHandle;

        private Entity localPlayer;
        private Life playerHealth;
        private Actor playerMovement;
        private DebuffPanelConfig debuffInfo;

        private float moveCounter;
        private float lastManaUsed;
        private float lastLifeUsed;
        private List<PlayerFlask> playerFlaskList;
        private FlaskKeys keyinfo;

        #region FlaskInformations
        private FlaskAction flask_name_to_action(string flaskname)
        {
            flaskname = flaskname.ToLower();
            FlaskAction ret = FlaskAction.NONE;
            String defense_pattern = @"bismuth|jade|stibnite|granite|amethyst|ruby|sapphire|topaz|aquamarine|quartz";
            String offense_pattern = @"silver|sulphur|basalt|diamond";
            if (flaskname.Contains("life"))
                ret = FlaskAction.LIFE;
            else if (flaskname.Contains("mana"))
                ret = FlaskAction.MANA;
            else if (flaskname.Contains("hybrid"))
                ret = FlaskAction.HYBRID;
            else if (flaskname.Contains("quicksilver"))
                ret = FlaskAction.SPEEDRUN;
            else if (System.Text.RegularExpressions.Regex.IsMatch(flaskname, defense_pattern))
                ret = FlaskAction.DEFENSE;
            else if (System.Text.RegularExpressions.Regex.IsMatch(flaskname, offense_pattern))
                ret = FlaskAction.OFFENSE;
            return ret;
        }
        private FlaskAction flask_mod_to_action(string flaskmodRawName)
        {
            flaskmodRawName = flaskmodRawName.ToLower();
            FlaskAction ret = FlaskAction.NONE;
            String defense_pattern = @"armour|evasion|lifeleech|manaleech|resistance";
            String ignore_pattern = @"levelrequirement|duration|charges|recharge|recovery|extramana|extralife|consecrate|smoke|ground";
            if (flaskmodRawName.Contains("poison"))
                ret = FlaskAction.POISON_IMMUNE;
            else if (flaskmodRawName.Contains("chill") && !flaskmodRawName.Contains("ground"))
                ret = FlaskAction.FREEZE_IMMUNE;
            else if (flaskmodRawName.Contains("burning"))
                ret = FlaskAction.IGNITE_IMMUNE;
            else if (flaskmodRawName.Contains("shock"))
                ret = FlaskAction.SHOCK_IMMUNE;
            else if (flaskmodRawName.Contains("bleeding"))
                ret = FlaskAction.BLEED_IMMUNE;
            else if (flaskmodRawName.Contains("curse"))
                ret = FlaskAction.CURSE_IMMUNE;
            else if (flaskmodRawName.Contains("knockback"))
                ret = FlaskAction.OFFENSE;
            else if (flaskmodRawName.Contains("movementspeed"))
                ret = FlaskAction.SPEEDRUN;
            else if (System.Text.RegularExpressions.Regex.IsMatch(flaskmodRawName, defense_pattern))
                ret = FlaskAction.DEFENSE;
            else if (System.Text.RegularExpressions.Regex.IsMatch(flaskmodRawName, ignore_pattern))
                ret = FlaskAction.IGNORE;
            return ret;
        }
        #endregion

        #region FlaskSlotHack
        private void SearchFlasksInventoryHack()
        {
            if (FlasksRoot != null)
            {
                foreach (Element flaskElem in FlasksRoot.Children)
                {
                    Entity item = flaskElem.AsObject<InventoryItemIcon>().Item;
                    if (item != null && item.HasComponent<Charges>())
                    {
                        var flaskCharges = item.GetComponent<Charges>();
                        var mods = item.GetComponent<Mods>();
                        var flaskName = GameController.Files.BaseItemTypes.Translate(item.Path).BaseName;
                        PlayerFlask newFLask = new PlayerFlask();
                        newFLask.FlaskName = flaskName;
                        newFLask.Slot = playerFlaskList.Count;
                        newFLask.SetSettings(Settings);
                        newFLask.Item = item;
                        newFLask.MaxCharges = flaskCharges.ChargesMax;
                        newFLask.UseCharges = flaskCharges.ChargesPerUse;
                        newFLask.CurrentCharges = flaskCharges.NumCharges;
                        #region UniqueFlaskNotImplemented
                        var isUnique = false;
                        foreach (var mod in mods.ItemMods)
                            if (mod.RawName.ToLower().Contains("unique"))
                                isUnique = true;
                        if (isUnique)
                        {
                            if (newFLask.isEnabled)
                                LogError("Unique Flasks are not implemented yet. Disable this flask slot manually.", errmsg_time);
                            newFLask.FlaskAction1 = FlaskAction.UNIQUE_FLASK;
                            newFLask.FlaskAction2 = FlaskAction.UNIQUE_FLASK;
                            try
                            {
                                int tmpIndex = playerFlaskList.FindIndex(x => x.FlaskName == newFLask.FlaskName && x.FlaskAction1 == newFLask.FlaskAction1
                                && x.FlaskAction2 == newFLask.FlaskAction2);
                                playerFlaskList[tmpIndex] = newFLask;
                                playerFlaskList[tmpIndex].Slot = tmpIndex;
                                playerFlaskList[tmpIndex].EnableDisableFlask();
                            }
                            catch (Exception)
                            {
                                LogMessage("Error adding flask to the list", errmsg_time);
                            }
                            continue;
                        }
                        #endregion
                        FlaskAction action1 = flask_name_to_action(flaskName);
                        if (action1 == FlaskAction.NONE)
                            LogError("Error: " + flaskName + " name not found", errmsg_time);
                        else if (action1 != FlaskAction.IGNORE)
                            newFLask.FlaskAction1 = action1;
                        FlaskAction action2 = FlaskAction.NONE;
                        foreach (var mod in mods.ItemMods)
                        {
                            action2 = flask_mod_to_action(mod.RawName);
                            if (action2 == FlaskAction.NONE)
                                LogError("Error: " + mod.RawName + "mod not found", errmsg_time);
                            else if (action2 != FlaskAction.IGNORE)
                                newFLask.FlaskAction2 = action2;
                        }
                        try
                        {
                            int tmp = playerFlaskList.FindIndex(x => x.FlaskName == newFLask.FlaskName && x.FlaskAction1 == newFLask.FlaskAction1
                            && x.FlaskAction2 == newFLask.FlaskAction2);
                            playerFlaskList[tmp] = newFLask;
                            playerFlaskList[tmp].Slot = tmp;
                            playerFlaskList[tmp].EnableDisableFlask();
                        }
                        catch (Exception)
                        {
                            LogMessage("Error adding flask to the list", errmsg_time);
                        }
                    }
                }
            }
        }
        #endregion

        public override void Render()
        {
            base.Render();
            if (Settings.Enable.Value && Settings.uiEnable.Value)
            {
                float X = GameController.Window.GetWindowRectangle().Width * Settings.positionX.Value * .01f;
                float Y = GameController.Window.GetWindowRectangle().Height * Settings.positionY.Value * .01f;
                Vector2 position = new Vector2(X, Y);
                float maxWidth = 0;
                float maxheight = 0;

                foreach (var flasks in playerFlaskList.ToArray())
                {
                    Color textColor = (flasks.isEnabled) ? Color.DarkKhaki : Color.Red;
                    var size = Graphics.DrawText(flasks.FlaskName, Settings.textSize.Value, position, textColor);
                    position.Y += size.Height;
                    maxheight += size.Height;
                    maxWidth = Math.Max(maxWidth, size.Width);
                }
                var background = new RectangleF(X, Y, maxWidth, maxheight);
                Graphics.DrawFrame(background, 5, Color.Black);
                Graphics.DrawImage("lightBackground.png", background);
            }
        }
        public override void Initialise()
        {

            if ( File.Exists("config/flaskbind.json") )
            {
                string keyfile = File.ReadAllText("config/flaskbind.json");
                keyinfo = JsonConvert.DeserializeObject<FlaskKeys>(keyfile);
            } else
            {
                keyinfo = new FlaskKeys(Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5);
                File.WriteAllText("config/flaskbind.json", JsonConvert.SerializeObject(keyinfo));
            }
            playerFlaskList = new List<PlayerFlask>();
            string json = File.ReadAllText("config/debuffPanel.json");
            debuffInfo = JsonConvert.DeserializeObject<DebuffPanelConfig>(json);
            OnFlaskManagerToggle();
            GameController.Area.OnAreaChange += area => UpdateFlasksList();
            Settings.Enable.OnValueChanged +=  OnFlaskManagerToggle;
        }
        
        private void OnFlaskManagerToggle()
        {
            try
            {
                if (Settings.Enable.Value)
                {
                    if (DEBUG)
                        LogMessage("Enabling FlaskManager.",logmsg_time);
                    moveCounter = 0;
                    isThreadEnabled = true;
                    PluginName = "Flask Manager";
                    lastManaUsed = 100f;
                    lastLifeUsed = 100f;
                    gameHandle = GameController.Window.Process.MainWindowHandle;
                    ScanForFlaskAddress(GameController.Game.IngameState.UIRoot);
                    localPlayer = GameController.Game.IngameState.Data.LocalPlayer;
                    playerHealth = localPlayer.GetComponent<Life>();
                    playerMovement = localPlayer.GetComponent<Actor>();
                    SearchFlasksInventory();
                    //We are creating our plugin thread inside PoEHUD!
                    Thread flaskThread = new Thread(FlaskThread) { IsBackground = true };
                    flaskThread.Start();
                }
                else
                {
                    if (DEBUG)
                        LogMessage("Disabling FlaskManager.",logmsg_time);
                    FlasksRoot = null;
                    gameHandle = IntPtr.Zero;
                    playerFlaskList.Clear();
                    isThreadEnabled = false;
                }
            }
            catch (Exception)
            {

                LogError("Error Starting FlaskManager Thread.", errmsg_time);
            }
        }

        private void UpdateFlasksList()
        {
            if (Settings.Enable.Value)
            {
                ScanForFlaskAddress(GameController.Game.IngameState.UIRoot);
                SearchFlasksInventory();
            }
        }
        private void SearchFlasksInventory()
        {
            if (DEBUG)
                LogMessage("Searching for flasks in inventory.", logmsg_time);
            playerFlaskList = new List<PlayerFlask>();
            if (FlasksRoot != null)
            {
                foreach (Element flaskElem in FlasksRoot.Children)
                {
                    Entity item = flaskElem.AsObject<InventoryItemIcon>().Item;
                    if (item != null && item.HasComponent<Flask>())
                    {
                        var flaskCharges = item.GetComponent<Charges>();
                        var mods = item.GetComponent<Mods>();
                        var flaskName = GameController.Files.BaseItemTypes.Translate(item.Path).BaseName;
                        PlayerFlask newFLask = new PlayerFlask();
                        newFLask.FlaskName = flaskName;
                        newFLask.Slot = playerFlaskList.Count;
                        newFLask.SetSettings(Settings);
                        newFLask.EnableDisableFlask();
                        newFLask.Item = item;
                        newFLask.MaxCharges = flaskCharges.ChargesMax;
                        newFLask.UseCharges = flaskCharges.ChargesPerUse;
                        newFLask.CurrentCharges = flaskCharges.NumCharges;
                        #region UniqueFlaskNotImplemented
                        var isUnique = false;
                        foreach (var mod in mods.ItemMods)
                            if (mod.RawName.ToLower().Contains("unique"))
                                isUnique = true;
                        if (isUnique)
                        {
                            if (newFLask.isEnabled)
                                LogError("Unique Flasks are not implemented yet. Disable this flask slot manually.", errmsg_time);
                            newFLask.FlaskAction1 = FlaskAction.UNIQUE_FLASK;
                            newFLask.FlaskAction2 = FlaskAction.UNIQUE_FLASK;
                            playerFlaskList.Add(newFLask);
                            continue;
                        }
                        #endregion
                        FlaskAction action1 = flask_name_to_action(flaskName);
                        if (action1 == FlaskAction.NONE)
                            LogError("Error: " + flaskName + " name not found", errmsg_time);
                        else if (action1 != FlaskAction.IGNORE)
                            newFLask.FlaskAction1 = action1;
                        FlaskAction action2 = FlaskAction.NONE;
                        foreach (var mod in mods.ItemMods)
                        {
                            action2 = flask_mod_to_action(mod.RawName);
                            if (action2 == FlaskAction.NONE)
                                LogError("Error: " + mod.RawName + "mod not found", errmsg_time);
                            else if (action2 != FlaskAction.IGNORE)
                                newFLask.FlaskAction2 = action2;
                        }

                        playerFlaskList.Add(newFLask);
                    }
                }
            }
        }
        private void ScanForFlaskAddress(Element elm)
        {
            foreach (var child in elm.Children)
            {
                Entity item = null;
                try
                {
                    item = child.AsObject<InventoryItemIcon>().Item;
                }
                catch (Exception){ }
                if (item != null)
                {
                    if (item.HasComponent<Flask>())
                    {
                        FlasksRoot = child.Parent;
                        if (DEBUG)
                            LogMessage("Found Flask Address.", logmsg_time);
                        return;
                    }
                }
                ScanForFlaskAddress(child);
            }
        }
        private void UpdateFlaskChargesInfo(PlayerFlask flask)
        {
            flask.CurrentCharges = flask.Item.GetComponent<Charges>().NumCharges;
        } 
        private void UseFlask(PlayerFlask flask)
        {
            KeyPressRelease(keyinfo.k[flask.Slot]);
        }
        private bool FindDrinkFlask(FlaskAction type1, FlaskAction type2)
        {
            var flaskList = playerFlaskList.FindAll(x => x.FlaskAction1 == type1 || x.FlaskAction2 == type2);
            foreach (var flask in flaskList)
            {
                if (flask.isEnabled && flask.CurrentCharges >= flask.UseCharges)
                {
                    UseFlask(flask);
                    UpdateFlaskChargesInfo(flask);
                    // if there are multiple flasks, drinking 1 of them at a time is enough.
                    return true;
                }
                else
                {
                    UpdateFlaskChargesInfo(flask);
                }

            }
            return false;
        }

        private void UpdatePlayerVariables()
        {
            localPlayer = GameController.Game.IngameState.Data.LocalPlayer;
            playerHealth = localPlayer.GetComponent<Life>();
            playerMovement = localPlayer.GetComponent<Actor>();
        }
        private int ExitPoe(string ExeName, string arguments)
        {
            // Prepare the process to run
            ProcessStartInfo start = new ProcessStartInfo();
            // Enter in the command line arguments, everything you would enter after the executable name itself
            start.Arguments = arguments;
            // Enter the executable to run, including the complete path
            start.FileName = ExeName;
            // Do you want to show a console window?
            start.WindowStyle = ProcessWindowStyle.Hidden;
            start.CreateNoWindow = true;
            int exitCode;


            // Run the external process & wait for it to finish
            using (Process proc = Process.Start(start))
            {
                proc.WaitForExit();

                // Retrieve the app's exit code
                exitCode = proc.ExitCode;
            }
            return exitCode;
        }
        private bool HasDebuff(Dictionary<string, int> dictionary, string buffName, bool isHostile)
        {
            int filterId;
            if (dictionary.TryGetValue(buffName, out filterId))
            {
                return filterId == 0 || isHostile == (filterId == 1);
            }
            return false;
        }

        private void AutoChicken()
        {
            if (Settings.isPercentQuit.Value && localPlayer.IsValid)
            {
                if (playerHealth.HPPercentage * 100 <= Settings.percentHPQuit.Value)
                {
                    try
                    {
                        ExitPoe("cports.exe", "/close * * * * " + GameController.Window.Process.ProcessName + ".exe");
                    }
                    catch (Exception)
                    {
                        LogError("Error: Cannot find cports.exe, you must die now!", errmsg_time);
                    }

                }
                if (playerHealth.ESPercentage * 100 <= Settings.percentESQuit.Value)
                {
                    try
                    {
                        ExitPoe("cports.exe", "/close * * * * " + GameController.Window.Process.ProcessName + ".exe");
                    }
                    catch (Exception)
                    {
                        LogError("Error: Cannot find cports.exe, you must die now!", errmsg_time);
                    }
                }
            }
            return;
        }
        private void SpeedFlaskLogic()
        {
            moveCounter = playerMovement.isMoving ? moveCounter += 0.1f : 0;
            if (localPlayer.IsValid && Settings.qSEnable.Value && moveCounter >= Settings.qSDur.Value &&
                !playerHealth.HasBuff("flask_bonus_movement_speed") &&
                !playerHealth.HasBuff("flask_utility_sprint"))
            {
                FindDrinkFlask(FlaskAction.SPEEDRUN, FlaskAction.SPEEDRUN);
            }
        }
        private void ManaLogic()
        {
            lastManaUsed += 0.1f;
            if (lastManaUsed < Settings.ManaDelay.Value)
                return;
            if (Settings.autoFlask.Value && localPlayer.IsValid)
            {
                if (playerHealth.MPPercentage * 100 <= Settings.PerManaFlask.Value)
                {
                    if (FindDrinkFlask(FlaskAction.MANA, FlaskAction.IGNORE))
                        lastManaUsed = 0f;
                    else if (FindDrinkFlask(FlaskAction.HYBRID, FlaskAction.IGNORE))
                        lastManaUsed = 0f;
                }
            }
        }
        private void LifeLogic()
        {
            lastLifeUsed += 0.1f;
            if (lastLifeUsed < Settings.HPDelay.Value)
                return;
            if (Settings.autoFlask.Value && localPlayer.IsValid)
            {
                if (playerHealth.HPPercentage * 100 <= Settings.perHPFlask.Value)
                {
                    if (FindDrinkFlask(FlaskAction.LIFE, FlaskAction.IGNORE))
                        lastLifeUsed = 0f;
                    else if (FindDrinkFlask(FlaskAction.LIFE, FlaskAction.IGNORE))
                        lastLifeUsed = 0f;
                }
            }
        }
        private void AilmentLogic()
        {
            foreach (var buff in playerHealth.Buffs)
            {
                if (DEBUG)
                    LogMessage("buffs:" + buff.Name + "time:" + buff.Timer, 0.05f);
                var buffName = buff.Name;
                if (!Settings.remAilment.Value || !localPlayer.IsValid)
                    return;
                if (Settings.remCorrupt.Value && !float.IsInfinity(buff.Timer) && HasDebuff(debuffInfo.Bleeding, buffName, false))
                    LogMessage("Bleeding -> hasDrunkFlask:" + FindDrinkFlask(FlaskAction.IGNORE,FlaskAction.BLEED_IMMUNE), logmsg_time);
                else if (Settings.remPoison.Value && !float.IsInfinity(buff.Timer) && HasDebuff(debuffInfo.Poisoned, buffName, false))
                    LogMessage("Poison -> hasDrunkFlask:" + FindDrinkFlask(FlaskAction.IGNORE, FlaskAction.POISON_IMMUNE), logmsg_time);
                else if (Settings.remFrozen.Value && !float.IsInfinity(buff.Timer) && HasDebuff(debuffInfo.ChilledFrozen, buffName, false))
                    LogMessage("Frozen -> hasDrunkFlask:" + FindDrinkFlask(FlaskAction.IGNORE, FlaskAction.FREEZE_IMMUNE), logmsg_time);
                else if (Settings.remBurning.Value && !float.IsInfinity(buff.Timer) && HasDebuff(debuffInfo.Burning, buffName, false))
                    LogMessage("Burning -> hasDrunkFlask:" + FindDrinkFlask(FlaskAction.IGNORE, FlaskAction.IGNITE_IMMUNE), logmsg_time);
                else if (Settings.remShocked.Value && !float.IsInfinity(buff.Timer) && HasDebuff(debuffInfo.Shocked, buffName, false))
                    LogMessage("Shock -> hasDrunkFlask:" + FindDrinkFlask(FlaskAction.IGNORE, FlaskAction.SHOCK_IMMUNE), logmsg_time);
                else if (Settings.remCurse.Value && !float.IsInfinity(buff.Timer) && HasDebuff(debuffInfo.WeakenedSlowed, buffName, false))
                    LogMessage("Curse -> hasDrunkFlask:" + FindDrinkFlask(FlaskAction.IGNORE, FlaskAction.CURSE_IMMUNE), logmsg_time);
            }
        }
        private void FlaskMain()
        {

            if (!localPlayer.IsValid)
                UpdatePlayerVariables();
            foreach (var flask in playerFlaskList.ToArray())
                if (!flask.Item.IsValid)
                {
                    ScanForFlaskAddress(GameController.Game.IngameState.UIRoot);
                    SearchFlasksInventoryHack();
                    break;
                }

            SpeedFlaskLogic();
            ManaLogic();
            LifeLogic();
            AilmentLogic();
            return;
        }

        #region Keyboard Input
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, UIntPtr wParam, UIntPtr lParam);
        [DllImport("User32.dll")]
        public static extern short GetAsyncKeyState(System.Windows.Forms.Keys vKey);
        public void KeyDown(Keys Key)
        {
            SendMessage(gameHandle, 0x100, (int)Key, 0);
        }
        public void KeyUp(Keys Key)
        {
            SendMessage(gameHandle, 0x101, (int)Key, 0);
        }
        public void KeyPressRelease(Keys key)
        {
            KeyDown(key);
            Thread.Sleep(Convert.ToInt32(GameController.Game.IngameState.CurLatency));
            // working as a double key.
            //KeyUp(key);
        }
        private void Write(string text, params object[] args)
        {
            foreach (var character in string.Format(text, args))
            {
                PostMessage(gameHandle, 0x0102, new UIntPtr(character), UIntPtr.Zero);
            }
        }
        #endregion
        #region Threading, do not touch
        private void FlaskThread()
        {
            while (isThreadEnabled)
            {
                FlaskMain();
                for (int j=0; j< 10; j++)
                {
                    AutoChicken();
                    Thread.Sleep(10);
                }
            }
       }
        #endregion
    }
    public class PlayerFlask
    {
        public string FlaskName;
        public int Slot;
        public bool isEnabled;

        public Entity Item;
        public FlaskAction FlaskAction1;
        public FlaskAction FlaskAction2;

        public int CurrentCharges;
        public int UseCharges;
        public int MaxCharges;

        private FlaskManagerSettings Settings;
        public void SetSettings(FlaskManagerSettings s)
        {
            Settings = s;
        }
        public void EnableDisableFlask()
        {
            switch (Slot)
            {
                case 0:
                    isEnabled = Settings.flaskSlot1Enable.Value;
                    Settings.flaskSlot1Enable.OnValueChanged += this.EnableDisableFlask;
                    break;
                case 1:
                    isEnabled = Settings.flaskSlot2Enable.Value;
                    Settings.flaskSlot2Enable.OnValueChanged += this.EnableDisableFlask;
                    break;
                case 2:
                    isEnabled = Settings.flaskSlot3Enable.Value;
                    Settings.flaskSlot3Enable.OnValueChanged += this.EnableDisableFlask;
                    break;
                case 3:
                    isEnabled = Settings.flaskSlot4Enable.Value;
                    Settings.flaskSlot4Enable.OnValueChanged += this.EnableDisableFlask;
                    break;
                case 4:
                    isEnabled = Settings.flaskSlot5Enable.Value;
                    Settings.flaskSlot5Enable.OnValueChanged += this.EnableDisableFlask;
                    break;
                default:
                    break;
            }
        }
        ~PlayerFlask()
        {
            switch (Slot)
            {
                case 0:
                    if (Settings.flaskSlot1Enable.OnValueChanged != null)
                        Settings.flaskSlot1Enable.OnValueChanged -= this.EnableDisableFlask;
                    break;
                case 1:
                    if (Settings.flaskSlot2Enable.OnValueChanged != null)
                        Settings.flaskSlot2Enable.OnValueChanged -= this.EnableDisableFlask;
                    break;
                case 2:
                    if (Settings.flaskSlot3Enable.OnValueChanged != null)
                        Settings.flaskSlot3Enable.OnValueChanged -= this.EnableDisableFlask;
                    break;
                case 3:
                    if (Settings.flaskSlot4Enable.OnValueChanged != null)
                        Settings.flaskSlot4Enable.OnValueChanged -= this.EnableDisableFlask;
                    break;
                case 4:
                    if (Settings.flaskSlot5Enable.OnValueChanged != null)
                        Settings.flaskSlot5Enable.OnValueChanged -= this.EnableDisableFlask;
                    break;
                default:
                    break;
            }
        }
    }
    public enum FlaskAction : int
    {
        IGNORE = 0, // ignore mods and don't give error
        NONE, // flask isn't initilized.
        LIFE, //life
        MANA, //mana
        HYBRID, //hybrid flasks
        DEFENSE, //bismuth, jade, stibnite, granite,
                 //amethyst, ruby, sapphire, topaz,
                 // aquamarine, quartz
                 //MODS: iron skin, reflexes, gluttony,
                 // craving, resistance
        SPEEDRUN, //quicksilver, MOD: adrenaline,
        OFFENSE, //silver, sulphur, basalt, diamond, MOD: Fending
        POISON_IMMUNE,// MOD: curing
        FREEZE_IMMUNE,// MOD: heat
        IGNITE_IMMUNE,// MOD: dousing
        SHOCK_IMMUNE,// MOD: grounding
        BLEED_IMMUNE,// MOD: staunching
        CURSE_IMMUNE, // MOD: warding
        UNIQUE_FLASK
    }
    public class FlaskKeys
    {
        public Keys[] k;
        public FlaskKeys(Keys k1, Keys k2, Keys k3, Keys k4, Keys k5)
        {
            k = new Keys[5];
            k[0] = k1;
            k[1] = k2;
            k[2] = k3;
            k[3] = k4;
            k[4] = k5;
        }
    }
}
