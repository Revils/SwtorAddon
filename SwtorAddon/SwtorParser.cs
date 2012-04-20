// Danial Afzal
// iotasquared@gmail.com
// revils@live.it
using System;
using System.Collections.Generic;
using System.Text;
using Advanced_Combat_Tracker;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Drawing;
using System.IO;
using System.Threading;

namespace SwtorAddon
{
    public class SwtorParser : IActPluginV1
    {
        private bool _cbIdleEnd;
        public void DeInitPlugin()
        {
            ActGlobals.oFormActMain.BeforeLogLineRead -= ParseLine;

            UserControl opMainTableGen = (UserControl) ActGlobals.oFormActMain.OptionsControlSets[@"Main Table/Encounters\General"][0];
            CheckBox cbIdleEnd = (CheckBox) opMainTableGen.Controls["cbIdleEnd"];
            cbIdleEnd.Checked = _cbIdleEnd;
            ActGlobals.oFormActMain.UpdateCheckClicked -= oFormActMain_UpdateCheckClicked;
        }

        Regex regex;
        const int DMG = (int) SwingTypeEnum.Melee, HEALS = (int) SwingTypeEnum.Healing, THREAT = (int) SwingTypeEnum.Threat;
        Label lblStatus;	// The status label that appears in ACT's Plugin tab        

        public void InitPlugin(System.Windows.Forms.TabPage pluginScreenSpace, System.Windows.Forms.Label pluginStatusText)
        {
            lblStatus = pluginStatusText;	// Hand the status label's reference to our local var
            this.SetupSwtorEnvironment();
            ActGlobals.oFormActMain.LogPathHasCharName = false;
            ActGlobals.oFormActMain.LogFileFilter = "*.txt";
            ActGlobals.oFormActMain.ResetCheckLogs();

            ActGlobals.oFormActMain.BeforeLogLineRead += new LogLineEventDelegate(ParseLine);
            ActGlobals.oFormActMain.GetDateTimeFromLog = new FormActMain.DateTimeLogParser(ParseDateTime);
            ActGlobals.oFormActMain.LogFileChanged += new LogFileChangedDelegate(oFormActMain_LogFileChanged);
            regex = new Regex(@"\[(.*)\] \[(.*)\] \[(.*)\] \[(.*)\] \[(.*)\] \((.*)\)[.<]*([!>]*)[.<]*([!>]*)[>]*",
                RegexOptions.Compiled);

            // All encounters are set by Enter/ExitCombat.
            UserControl opMainTableGen = (UserControl) ActGlobals.oFormActMain.OptionsControlSets[@"Main Table/Encounters\General"][0];
            CheckBox cbIdleEnd = (CheckBox) opMainTableGen.Controls["cbIdleEnd"];
            _cbIdleEnd = cbIdleEnd.Checked;
            cbIdleEnd.Checked = false;

            /* disabled
            ActGlobals.oFormActMain.UpdateCheckClicked += new FormActMain.NullDelegate(oFormActMain_UpdateCheckClicked);
            if (ActGlobals.oFormActMain.GetAutomaticUpdatesAllowed())   // If ACT is set to automatically check for updates, check for updates to the plugin
                new Thread(new ThreadStart(oFormActMain_UpdateCheckClicked)).Start();	// If we don't put this on a separate thread, web latency will delay the plugin init phase
            */
            lblStatus.Text = "Plugin Started";
        }


        private void SetupSwtorEnvironment()
        {
            EncounterData.ExportVariables.Remove("cures");
            EncounterData.ExportVariables.Remove("powerdrain");
            EncounterData.ExportVariables.Remove("powerheal");
            EncounterData.ExportVariables.Remove("maxhealward");
            EncounterData.ExportVariables.Remove("MAXHEALWARD");

            CombatantData.ColumnDefs.Remove("Cures");
            CombatantData.ColumnDefs.Remove("PowerDrain");
            CombatantData.ColumnDefs.Remove("PowerReplenish");
            CombatantData.ColumnDefs["Threat +/-"] =
                new CombatantData.ColumnDef("Threat +/-", false, "VARCHAR(32)",
                    "ThreatStr", (Data) => { return Data.GetThreatStr("Threat Done"); },
                    (Data) => { return Data.GetThreatStr("Threat Done"); },
                    (Left, Right) => { return Left.GetThreatDelta("Threat Done").CompareTo(Right.GetThreatDelta("Threat Done")); });
            CombatantData.ColumnDefs["ThreatDelta"] =
                new CombatantData.ColumnDef("ThreatDelta", false, "INT", "ThreatDelta",
                    (Data) => { return Data.GetThreatDelta("Threat Done").ToString(ActGlobals.mainTableShowCommas ? "#,0" : "0"); },
                    (Data) => { return Data.GetThreatDelta("Threat Done").ToString(); },
                    (Left, Right) => { return Left.GetThreatDelta("Threat Done").CompareTo(Right.GetThreatDelta("Threat Done")); });
            CombatantData.OutgoingDamageTypeDataObjects = new Dictionary<string, CombatantData.DamageTypeDef>
	        {
		        {"Damage Done", new CombatantData.DamageTypeDef("Damage Done", -1, Color.Orange)},
		        {"Healing Done", new CombatantData.DamageTypeDef("Healing Done", 1, Color.Blue)},
		        {"Threat Done", new CombatantData.DamageTypeDef("Threat Done", 0, Color.Black)},
                //{"Resource Gain", new CombatantData.DamageTypeDef("Resource Gain", 0, Color.DarkBlue)},
                //{"Resource Loss", new CombatantData.DamageTypeDef("Resource Loss", 0, Color.DarkBlue)},
                // I dont understand why, but the last entry is always the sum of all other counters. 
                // Its not particularly useful to have a counter for Damage+Threat
		        {"All Outgoing", new CombatantData.DamageTypeDef("All Outgoing", 0, Color.Transparent)} 
	        };
            CombatantData.IncomingDamageTypeDataObjects = new Dictionary<string, CombatantData.DamageTypeDef>
	        {
		        {"Damage Recieved", new CombatantData.DamageTypeDef("Damage Recieved", -1, Color.Red)},
		        {"Healing Recieved",new CombatantData.DamageTypeDef("Healing Recieved", 1, Color.Brown)},
		        {"Threat Recieved",new CombatantData.DamageTypeDef("Threat Recieved", 0, Color.Yellow)},
		        {"All Incoming",new CombatantData.DamageTypeDef("All Incoming", 0, Color.Transparent)}
	        };
            CombatantData.SwingTypeToDamageTypeDataLinksOutgoing = new SortedDictionary<int, List<string>>
	        { 
		        {DMG, new List<string> { "Damage Done" } },
		        {HEALS, new List<string> { "Healing Done" } },
		        {THREAT, new List<string> { "Threat Done" } },
                //{20, new List<string> { "Resource Gain" } },
                //{21, new List<string> { "Resource Loss" } },
	        };
            CombatantData.SwingTypeToDamageTypeDataLinksIncoming = new SortedDictionary<int, List<string>>
	        { 
		        {DMG, new List<string> { "Damage Recieved" } },
		        {HEALS, new List<string> { "Healing Recieved" } },
		        {THREAT, new List<string> { "Threat Recieved" } }
	        };

            CombatantData.DamageSwingTypes = new List<int> { DMG };
            CombatantData.HealingSwingTypes = new List<int> { HEALS };

            CombatantData.DamageTypeDataOutgoingDamage = "Damage Done";
            CombatantData.DamageTypeDataNonSkillDamage = "Damage Done";
            CombatantData.DamageTypeDataOutgoingHealing = "Healing Done";
            CombatantData.DamageTypeDataIncomingDamage = "Damage Recieved";
            CombatantData.DamageTypeDataIncomingHealing = "Healing Recieved";

            CombatantData.ExportVariables.Remove("cures");
            CombatantData.ExportVariables.Remove("maxhealward");
            CombatantData.ExportVariables.Remove("MAXHEALWARD");
            CombatantData.ExportVariables.Remove("powerdrain");
            CombatantData.ExportVariables.Remove("powerheal");

            ActGlobals.oFormActMain.ValidateLists();
            ActGlobals.oFormActMain.ValidateTableSetup();
            ActGlobals.oFormActMain.TimeStampLen = 14;
        }

        private class LogLine
        {
            public string source;
            public string target;
            public string ability;
            public string event_type, event_detail;
            public bool crit_value;
            public Dnum value;
            public string value_type;
            public Dnum threat;
            public Dnum absorb;
            public string special = "None";
            public string direction = "Increase";

            private string line;

            static Regex regex =
                new Regex(@"\[(.*)\] \[(.*)\] \[(.*)\] \[(.*)\] \[(.*)\] \((.*)\)[\s<]*(-?\d*)?[>]*",
                    RegexOptions.Compiled);
            static Regex id_regex = new Regex(@"\s*\{\d*}\s*", RegexOptions.Compiled);

            public LogLine(string logline)
            {
                line = id_regex.Replace(logline, "");
                MatchCollection matches = regex.Matches(line);
                source = matches[0].Groups[2].Value;
                target = matches[0].Groups[3].Value;
                ability = matches[0].Groups[4].Value;

                value = new Dnum(0);
                absorb = new Dnum(0);

                if (matches[0].Groups[5].Value.Contains(":"))
                {
                    event_type = matches[0].Groups[5].Value.Split(':')[0];
                    event_detail = matches[0].Groups[5].Value.Split(':')[1].Trim();
                }
                else
                {
                    event_type = matches[0].Groups[5].Value;
                    event_detail = "";
                }

                crit_value = matches[0].Groups[6].Value.Contains("*");

                string damageString = matches[0].Groups[6].Value.Replace("*", "");
                string absorbString = string.Empty;
                int dmg = 0, absorbeddmg = 0;

                if (matches[0].Groups[6].Value.Contains("absorbed)"))
                {
                    string[] raw_damage = damageString.Split('(');
                    damageString = raw_damage[0];
                    absorbString = raw_damage[1];
                    absorbString = absorbString.Remove(absorbString.Length - 10);
                    absorbeddmg = int.Parse(absorbString);
                }

                string[] raw_value = damageString.Split(' ');
                if (raw_value[0].Length > 0)
                {
                    dmg = int.Parse(raw_value[0]);
                    value = new Dnum(dmg - absorbeddmg);
                    absorb = new Dnum(absorbeddmg);
                }

                if (raw_value.Length > 1)
                {
                    string[] raw_type = raw_value[1].Split('-');
                    value_type = raw_type[0];
                    if (raw_type.Length > 1)
                        special = raw_type[1];
                }
                else
                    value_type = "Unknown";

                // TODO: move outside
                if (value_type.Contains("-miss")) // {836045448945502}
                {
                    value = Dnum.Miss;
                    value_type = "Unknown";
                }
                else if (value_type.Contains("-parry")) // {{836045448945503}}
                {
                    value = new Dnum(Dnum.Unknown, "Parry");
                    value_type = "Unknown";
                }
                else if (value_type.Contains("-dodge")) // {836045448945505}
                {
                    value = new Dnum(Dnum.Unknown, "Dodge");
                    value_type = "Unknown";
                }
                else if (value_type.Contains("-deflect")) // {836045448945508}
                {
                    value = new Dnum(Dnum.Unknown, "Deflect");
                    value_type = "Unknown";
                }

                int raw_threat = matches[0].Groups[7].Value.Length > 0 ? int.Parse(matches[0].Groups[7].Value) : 0;
                if (raw_threat < 0)
                    direction = "Decrease";

                threat = new Dnum(Math.Abs(raw_threat));
            }
        }

        static DateTime default_date = new DateTime(2012, 1, 1);
        private void ParseLine(bool isImport, LogLineEventArgs log)
        {
            ActGlobals.oFormActMain.GlobalTimeSorter++;
            log.detectedType = Color.Black.ToArgb();
            DateTime time = ActGlobals.oFormActMain.LastKnownTime;
            LogLine line = new LogLine(log.logLine);
            if (log.logLine.Contains("{836045448945490}")) // Exit Combat
            {
                ActGlobals.oFormActMain.EndCombat(!isImport);
                log.detectedType = Color.Purple.ToArgb();
                return;
            }
            if (log.logLine.Contains("{836045448945489}")) // Enter Combat
            {
                ActGlobals.oFormActMain.EndCombat(!isImport);
                ActGlobals.charName = line.source;
                ActGlobals.oFormActMain.SetEncounter(time, line.source, line.target);
                log.detectedType = Color.Purple.ToArgb();
                return;
            }

            if (log.logLine.Contains("{836045448945488}")) // Taunt
            {
                log.detectedType = Color.Blue.ToArgb();
                ActGlobals.oFormActMain.AddCombatAction(THREAT, line.crit_value, line.special, line.source, "Taunt",
                     line.threat, time, ActGlobals.oFormActMain.GlobalTimeSorter, line.target, line.direction);

                return;
            }

            if (log.logLine.Contains("{836045448945483}")) // Threat
            {
                log.detectedType = Color.Blue.ToArgb();
                ActGlobals.oFormActMain.AddCombatAction(THREAT, line.crit_value, line.special, line.source, string.IsNullOrEmpty(line.ability) ? "Threat" : line.ability,
                     line.threat, time, ActGlobals.oFormActMain.GlobalTimeSorter, line.target, line.direction);

                return;
            }

            int type = 0;
            if (log.logLine.Contains("{836045448945501}")) // Damage
            {
                log.detectedType = Color.Red.ToArgb();
                type = DMG;
            }
            else if (log.logLine.Contains("{836045448945500}")) // Heals
            {
                log.detectedType = Color.Green.ToArgb();
                type = HEALS;
            }
            else if (log.logLine.Contains("{836045448945493}")) // Death
            {
                ActGlobals.oFormActMain.AddCombatAction(DMG, line.crit_value,
                    line.special, line.source, "Killing Blow", Dnum.Death, time,
                    ActGlobals.oFormActMain.GlobalTimeSorter, line.target, "Death");
            }

            /*else if (line.event_type.Contains("Restore"))
            {
                log.detectedType = Color.OrangeRed.ToArgb();
                type = 20;
            }
            else if (line.event_type.Contains("Spend"))
            {
                log.detectedType = Color.Cyan.ToArgb();
                type = 21;
            }
            if (line.ability != "")
            {
                last_ability = line.ability;
            }
            if ((type == 20 || type == 21) && ActGlobals.oFormActMain.SetEncounter(time, line.source, line.target))
            {
                ActGlobals.oFormActMain.AddCombatAction(type, line.crit_value, "None", line.source, last_ability, new Dnum(line.value), time, ActGlobals.oFormActMain.GlobalTimeSorter, line.target, "");
            }
            */
            if (!ActGlobals.oFormActMain.InCombat)
            {
                return;
            }
            if (line.threat > 0 && ActGlobals.oFormActMain.SetEncounter(time, line.source, line.target))
            {
                ActGlobals.oFormActMain.AddCombatAction(type, line.crit_value, line.special, line.source, line.ability,
                    line.value, time, ActGlobals.oFormActMain.GlobalTimeSorter, line.target, line.value_type);

                ActGlobals.oFormActMain.AddCombatAction(THREAT, line.crit_value, line.special, line.source, line.ability,
                    line.threat, time, ActGlobals.oFormActMain.GlobalTimeSorter, line.target, (line.threat.Number > 0 ? "Increase" : "Decrease"));

                // FIXME!: tank absorb should be handle in another way
                if (line.absorb > 0)
                {
                    ActGlobals.oFormActMain.AddCombatAction(HEALS, false, "Shield", line.source, line.ability,
                        line.absorb, time, ActGlobals.oFormActMain.GlobalTimeSorter, line.target, "Absorption");
                }
            }
            return;
        }
        DateTime logFileDate = DateTime.Now;
        Regex logfileDateTimeRegex = new Regex(@"combat_(?<Y>\d{4})-(?<M>\d\d)-(?<D>\d\d)_(?<h>\d\d)_(?<m>\d\d)_(?<s>\d\d)_\d+\.txt", RegexOptions.Compiled);
        void oFormActMain_LogFileChanged(bool IsImport, string NewLogFileName)
        {
            if (!File.Exists(NewLogFileName))
            {
                return;
            }
            //combat_2012-04-02_09_20_30_162660.txt
            FileInfo newFile = new FileInfo(NewLogFileName);
            Match match = logfileDateTimeRegex.Match(newFile.Name);
            if (match.Success)	// If we can parse the creation date from the filename
            {
                try
                {
                    logFileDate = new DateTime(
                        Int32.Parse(match.Groups[1].Value),		// Y
                        Int32.Parse(match.Groups[2].Value),		// M
                        Int32.Parse(match.Groups[3].Value),		// D
                        Int32.Parse(match.Groups[4].Value),		// h
                        Int32.Parse(match.Groups[5].Value),		// m
                        Int32.Parse(match.Groups[6].Value));		// s
                }
                catch
                {
                    logFileDate = newFile.CreationTime;
                }
            }
            else
            {
                logFileDate = newFile.CreationTime;
            }
        }
        private DateTime ParseDateTime(string line)
        {
            try
            {
                //[22:55:28.335] 
                if (line.Length < ActGlobals.oFormActMain.TimeStampLen)
                    return ActGlobals.oFormActMain.LastEstimatedTime;

                int hour, min, sec, millis;

                hour = Convert.ToInt32(line.Substring(1, 2));
                min = Convert.ToInt32(line.Substring(4, 2));
                sec = Convert.ToInt32(line.Substring(7, 2));
                millis = Convert.ToInt32(line.Substring(10, 3));
                DateTime parsedTime = new DateTime(logFileDate.Year, logFileDate.Month, logFileDate.Day, hour, min, sec, millis);
                if (parsedTime < logFileDate)			// if time loops from 23h to 0h, the parsed time will be less than the log creation time, so add one day
                    parsedTime = parsedTime.AddDays(1);	// only works for log files that are less than 24h in duration

                return parsedTime;
            }
            catch
            {
                return ActGlobals.oFormActMain.LastEstimatedTime;
            }
        }

        void oFormActMain_UpdateCheckClicked()
        {
            int pluginId = 61;
            try
            {
                DateTime localDate = ActGlobals.oFormActMain.PluginGetSelfDateUtc(this);
                DateTime remoteDate = ActGlobals.oFormActMain.PluginGetRemoteDateUtc(pluginId);
                if (localDate.AddHours(2) < remoteDate)
                {
                    DialogResult result = MessageBox.Show("There is an updated version of the SwtorParsing Plugin.  Update it now?\n\n(If there is an update to ACT, you should click No and update ACT first.)", "New Version", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.Yes)
                    {
                        FileInfo updatedFile = ActGlobals.oFormActMain.PluginDownload(pluginId);
                        ActPluginData pluginData = ActGlobals.oFormActMain.PluginGetSelfData(this);
                        pluginData.pluginFile.Delete();
                        updatedFile.MoveTo(pluginData.pluginFile.FullName);
                        ThreadInvokes.CheckboxSetChecked(ActGlobals.oFormActMain, pluginData.cbEnabled, false);
                        Application.DoEvents();
                        ThreadInvokes.CheckboxSetChecked(ActGlobals.oFormActMain, pluginData.cbEnabled, true);
                    }
                }
            }
            catch (Exception ex)
            {
                ActGlobals.oFormActMain.WriteExceptionLog(ex, "Plugin Update Check");
            }
        }
    }
}
