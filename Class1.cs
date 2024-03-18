using StardewModdingAPI;
using System.Collections.Generic;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using System.Text;
using System.IO;
using System;
using StardewValley.TerrainFeatures;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace StardewExporter
{
    public class ModEntry : Mod
    {
        static IModHelper hlp;

        private IList<KeyValuePair<string, string>> globalLabels = new List<KeyValuePair<string, string>>();

        public override void Entry(IModHelper helper)
        {
            hlp = helper;

            hlp.Events.GameLoop.SaveLoaded += this.SaveLoaded;
            hlp.Events.GameLoop.TimeChanged += this.TimeChanged;
            hlp.Events.GameLoop.DayStarted += this.DayStarted;

            Task task = Task.Run((Action)Serve);
            
            
        }

        private string promMetrics = "";

        private void Serve()
        {
            try
            {
                HttpListener listener = new HttpListener();
                listener.Prefixes.Add("http://localhost:8585/metrics/");
                listener.Start();
                this.Monitor.Log("LISTENing", LogLevel.Warn);
                while (true)
                {
                    try
                    {
                        HttpListenerContext context = listener.GetContext();
                        HttpListenerRequest request = context.Request;
                        // Obtain a response object.
                        HttpListenerResponse response = context.Response;
                        response.AppendHeader("Content-Type", "text/plain; version=0.0.4");
                        // Construct a response.
                        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(promMetrics);
                        // Get a response stream and write the response to it.
                        response.ContentLength64 = buffer.Length;
                        System.IO.Stream output = response.OutputStream;
                        output.Write(buffer, 0, buffer.Length);
                        // You must close the output stream.
                        output.Close();
                    }catch(Exception e)
                    {
                        this.Monitor.Log("!!!!!!!!!" + e.Message, LogLevel.Error);
                    }
                }
            }catch (Exception e)
            {
                this.Monitor.Log("!!!!!!!!!"+e.Message, LogLevel.Error);
            }
        }

        private static IDictionary<string, int> seasonVals = new Dictionary<string, int>
        {
            { "spring",1 },
            { "summer",2 },
            { "fall",3 },
            { "winter",4 },
        };
        private static IDictionary<int, string> skills = new Dictionary<int, string>
        {
            { 0,"farming" },
            {1, "fishing" },
            { 2,"foraging" },
            { 3,"mining" },
              { 4,"combat" },
        };

        private void TimeChanged(object sender, TimeChangedEventArgs e)
        {
            Scrape(e.NewTime);
        }

        private IDictionary<string, int> allCrops = new Dictionary<string, int>();

        private void Scrape(int time)
        {
            var build = new StringBuilder();
            void a(string name, IList<KeyValuePair<string, string>> labels, double val) => build.AppendLine(FormatMetric(name, labels, val));
            IList<KeyValuePair<string, string>> l(string k, string v) => new List<KeyValuePair<string, string>>() { new KeyValuePair<string, string>(k, v) };
            a("gold", null, Game1.MasterPlayer.Money);
            a("day", null, Game1.dayOfMonth);
            a("year", null, Game1.year);
            a("time", null, time);
            a("season", null, seasonVals[Game1.currentSeason]);

            foreach (var kvp in Game1.MasterPlayer.friendshipData.Pairs)
            {
                var labels = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("name", kvp.Key)
                };
                a("friendship", labels, kvp.Value.Points);
                a("talked_today", labels, kvp.Value.TalkedToToday ? 1 : 0);
                a("gifts_week", labels, kvp.Value.GiftsThisWeek);
                a("gifts_today", labels, kvp.Value.GiftsToday);
            }
            foreach (var kvp in skills)
            {
                var labels = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("name", kvp.Value)
                };
                a("exp", labels, Game1.MasterPlayer.experiencePoints[kvp.Key]);
                a("skill", labels, Game1.MasterPlayer.GetSkillLevel(kvp.Key));
            }
            a("hp", null, Game1.MasterPlayer.health);
            a("hp_max", null, Game1.MasterPlayer.maxHealth);
            a("stamina", null, Game1.MasterPlayer.Stamina);
            a("stamina_max", null, Game1.MasterPlayer.MaxStamina);

            var f = Game1.getFarm();
            var tfs = f.terrainFeatures.Pairs;

            var grass = 0;
            var trees = 0;
            var saplings = 0;

            foreach (var kvp in tfs)
            {
                if (kvp.Value is Tree)
                {
                    if (((Tree)kvp.Value).growthStage.Value == Tree.treeStage)
                    {
                        trees++;
                    }
                    else
                    {
                        saplings++;
                    }
                }
                else if (kvp.Value is Grass)
                {
                    grass++;
                }
                else
                {
                    //this.Monitor.Log($"??{kvp.Value.GetType()}", LogLevel.Warn);
                }
            }

            var hoes = tfs.Where(x => x.Value is HoeDirt).Select(x => x.Value as HoeDirt).Where(x => x.crop != null)
                .GroupBy(x => string.Join(";",x.crop.indexOfHarvest.Value.ToString(),x.readyForHarvest().ToString(), (x.needsWatering() && x.state.Value != 1).ToString()));

            foreach (var k in allCrops.Keys)
            {
                allCrops[k] = 0;
            }

            foreach (var h in hoes)
            {
                allCrops[h.Key] = h.Count();
            }

            foreach (var kvp in allCrops)
            {
                var parts = kvp.Key.Split(";");

                StardewValley.Object sampleHarvestItem = new StardewValley.Object(int.Parse(parts[0]), 1);
                var labels = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("location", "farm"),
                    new KeyValuePair<string, string>("crop", sampleHarvestItem.DisplayName),
                    new KeyValuePair<string, string>("harvestable", parts[1]),
                    new KeyValuePair<string, string>("needsWater", parts[2]),
                };
                a("crops", labels, kvp.Value);
            }

            a("farm_objects", l("kind", "grass"), grass);
            a("farm_objects", l("kind", "trees"), trees);
            a("farm_objects", l("kind", "saplings"), saplings);

            var rocks = 0;
            var weeds = 0;
            var twigs = 0;
            var deb = f.debris.ToList();
            var objs = f.Objects;

            foreach (var obj in f.Objects.Values.ToList())
            {
                if (obj.DisplayName == "Stone")
                {
                    rocks++;
                }
                if (obj.DisplayName == "Twig")
                {
                    twigs++;
                }
                if (obj.DisplayName == "Weeds")
                {
                    weeds++;
                }
            }
            a("farm_objects", l("kind", "stone"), rocks);
            a("farm_objects", l("kind", "twigs"), twigs);
            a("farm_objects", l("kind", "weeds"), weeds);


            this.promMetrics = build.ToString();
            //File.WriteAllText(Path.Join(this.Helper.DirectoryPath, $"{Game1.MasterPlayer.farmName}-{Game1.uniqueIDForThisGame}.prom"), build.ToString());
        }

        private void DayStarted(object sender, DayStartedEventArgs e)
        {
            Scrape(600);
        }

        private void SaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            this.globalLabels.Clear();
            this.globalLabels.Add(new KeyValuePair<string, string>("farmer", Game1.MasterPlayer.Name));
            this.globalLabels.Add(new KeyValuePair<string, string>("farm", Game1.MasterPlayer.farmName.Value));
        }

        private string FormatMetric(string metric, IList<KeyValuePair<string, string>> labels, double val)
        {
            metric = "stardew_" + metric;
            var lvals = new List<string>();
            foreach (var kvp in globalLabels)
            {
                lvals.Add(kvp.Key + "=\"" + kvp.Value + "\"");
            }
            if (labels != null)
            {
                foreach (var kvp in labels)
                {
                    lvals.Add(kvp.Key + "=\"" + kvp.Value + "\"");
                }
            }
            var lstr = "{" + string.Join(",", lvals) + "}";
            return $"{metric}{lstr} {val}";
        }


    }

}