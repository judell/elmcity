/* ********************************************************************************
 *
 * Copyright 2010-2013 Microsoft Corporation
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you
 * may not use this file except in compliance with the License. You may
 * obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0 
 * Unless required by applicable law or agreed to in writing, software distributed 
 * under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
 * CONDITIONS OF ANY KIND, either express or implied. See the License for the 
 * specific language governing permissions and limitations under the License. 
 *
 * *******************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Timers;
using CalendarAggregator;
using DDay.iCal;
using DDay.iCal.Serialization;
using ElmcityUtils;
using Microsoft.WindowsAzure.ServiceRuntime;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Threading;
using HtmlAgilityPack;

namespace WorkerRole
{
	public class Todo
	{
		public List<string> icaltasks, nonicaltasks, regiontasks, twitter_messages, start_requests, meta_refresh_requests;

		public Todo()
		{
			icaltasks = new List<string>();
			nonicaltasks = new List<string>();
			//regiontasks = new List<string>();
			//twitter_messages = new List<string>();
			start_requests = new List<string>();
		}
	};

	public class WorkerRole : RoleEntryPoint
	{
#if false // true if testing, false if not testing
        private static int startup_delay = 100000000; // add delay if want to focus on webrole
		//private static int startup_delay = 0;
        private static List<string> testids = new List<string>() { "PboroNhEvents" };  // focus on a hub
		private static List<string> testfeeds = new List<string>(); 
		//	testfeeds.Add("http://www.facebook.com/ical/u.php?uid=652661115&key=AQDkPMjwIPc30qcT"); // optionally focus on a feed in that hub
        private static bool testing = true;
#else     // not testing
		private static int startup_delay = 0;
		private static List<string> testids = new List<string>();
		private static List<string> testfeeds = new List<string>();
		private static bool testing = false;
#endif

		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();
		private static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
		private static Logger logger = new Logger();
		private static Dictionary<string, string> settings = GenUtils.GetSettingsFromAzureTable();

		private static string blobhost = ElmcityUtils.Configurator.azure_blobhost;

		public static List<string> ids { get; set; }

		public static List<string> regions { get; set; }

		private static Dictionary<string, int> feedcounts = new Dictionary<string, int>();

		private static string local_storage_path;

		// private static List<TwitterDirectMessage> twitter_direct_messages; // disabled for now, twitter didn't like this approach

		private static ElmcityUtils.Monitor monitor;

		private static Todo todo;

		public override bool OnStart()
		{
			try
			{
				var hostname = System.Net.Dns.GetHostName();
				var msg = "Worker: OnStart: " + hostname;
				GenUtils.PriorityLogMsg("status", msg, null);

				HttpUtils.Wait(startup_delay);

				local_storage_path = RoleEnvironment.GetLocalResource("LocalStorage1").RootPath;

				RoleEnvironment.Changing += RoleEnvironmentChanging;

				GenUtils.LogMsg("status", "LocalStorage1", local_storage_path);

				// PythonUtils.InstallPythonStandardLibrary(local_storage_path, ts);

				HttpUtils.SetAllowUnsafeHeaderParsing(); //http://www.cookcomputing.com/blog/archives/000556.html

				Utils.ScheduleTimer(IronPythonAdmin, minutes: CalendarAggregator.Configurator.ironpython_admin_interval_hours * 60, name: "IronPythonAdmin", startnow: false);

				Utils.ScheduleTimer(GeneralAdmin, minutes: CalendarAggregator.Configurator.worker_general_admin_interval_hours * 60, name: "GeneralAdmin", startnow: false);

				Utils.ScheduleTimer(TestRunnerAdmin, minutes: CalendarAggregator.Configurator.testrunner_interval_hours * 60, name: "TestRunnerAdmin", startnow: false);

				Utils.ScheduleTimer(ReloadMonitorCounters, minutes: CalendarAggregator.Configurator.worker_reload_interval_hours * 60, name: "WorkerReloadCounters", startnow: false);

				Utils.ScheduleTimer(MonitorAdmin, minutes: CalendarAggregator.Configurator.worker_gather_monitor_data_interval_minutes, name: "GatherMonitorData", startnow: false);

				monitor = ElmcityUtils.Monitor.TryStartMonitor(CalendarAggregator.Configurator.process_monitor_interval_minutes, CalendarAggregator.Configurator.process_monitor_table);
			}
			catch (Exception e)
			{
				var msg = "Worker.OnStart";
				GenUtils.PriorityLogMsg("exception", msg, e.Message + e.StackTrace);
			}
			return base.OnStart();
		}

		public override void OnStop()
		{
			var msg = "Worker: OnStop";
			logger.LogMsg("status", msg, null);
			GenUtils.PriorityLogMsg("status", msg, null);
			var snapshot = Counters.MakeSnapshot(Counters.GetCounters());
			monitor.StoreSnapshot(snapshot);

			base.OnStop();
		}

		private void RoleEnvironmentChanging(object sender, RoleEnvironmentChangingEventArgs e)  // pro forma, not used, most config settings are in an azure table
		{
			// If a configuration setting is changing
			if (e.Changes.Any(change => change is RoleEnvironmentConfigurationSettingChange))
			{
				// Set e.Cancel to true to restart this role instance
				e.Cancel = true;
			}
		}


		public override void Run()
		{
			try
			{
				var message = "Worker: Run";
				GenUtils.PriorityLogMsg("status", message, null);

				while (true)
				{
					logger.LogMsg("status", "worker waking", null);

					settings = GenUtils.GetSettingsFromAzureTable();
					logger.LogMsg("status", "worker updated " + settings.Count + " settings", null);

					ids = Metadata.LoadHubIdsFromAzureTable();
					logger.LogMsg("status", "worker loaded " + ids.Count + " ids", null);

					regions = Utils.GetRegionIds();
					logger.LogMsg("status", "worker found " + regions.Count + " regions", null);

					//twitter_direct_messages = TwitterApi.GetNewTwitterDirectMessages(); // get new control messages // disabled for now, twitter didn't like this
					//logger.LogMsg("status", "worker got " + twitter_direct_messages.Count + " messages", null);

					//ids = MaybeAdjustIdsForTesting(ids);

					todo = new Todo();

					var icals_and_nonicals = ids.Except(regions).ToList();

					try
					{
						BuildTodo(todo, icals_and_nonicals);
					}
					catch (Exception e)
					{
						GenUtils.PriorityLogMsg("exception", "BuildTodo", e.Message);
						continue;
					}

					//HandleTwitterMessages(todo, ids);

					//MaybeRemakeWebRoleData();

					var sw_total = new Stopwatch();
					var sw_ical = new Stopwatch();
					var sw_finalize = new Stopwatch();
					var sw_nonical = new Stopwatch();
					var sw_region = new Stopwatch();

					sw_total.Start();

					sw_ical.Start();

					var options = new ParallelOptions();
					int max_ical_tasks;
					try
					{
						max_ical_tasks = Convert.ToInt32(settings["max_concurrent_icaltasks"]);
					}
					catch (Exception e)
					{
						GenUtils.PriorityLogMsg("exception", "Run: getting max_concurrent_icaltasks", e.Message);
						max_ical_tasks = 3;
					}
					options.MaxDegreeOfParallelism = max_ical_tasks;
					Parallel.ForEach(source: todo.icaltasks, parallelOptions: options, body: (id) =>
					//foreach (var id in todo.icaltasks)              // this can be parallelized because there are many separate/unique endpoints
					{
						try
						{
							Scheduler.UpdateStartTaskForId(id, TaskType.icaltasks);
							ProcessIcal(id);
							StopTask(id, TaskType.icaltasks);
						}
						catch (Exception e)
						{
							GenUtils.PriorityLogMsg("exception", "Worker icaltasks", e.Message);
							return;
						}
					});
					sw_ical.Stop();

					sw_nonical.Start();
					foreach (var id in todo.nonicaltasks)           // this won't be parallelized because of api rate throttling in nonical service endpoints
					{
						try
						{
							Scheduler.UpdateStartTaskForId(id, TaskType.nonicaltasks);  // the todo list has a general start time, now update it to actual start
							ProcessNonIcal(id);
							StopTask(id, TaskType.nonicaltasks);
						}
						catch (Exception e)
						{
							GenUtils.PriorityLogMsg("exception", "Worker nonicaltasks", e.Message);
							return;
						}
					}
					sw_nonical.Stop();

					sw_finalize.Start();
					var finalizers = todo.nonicaltasks.Union(todo.icaltasks);  // finalize ical and/or nonical updates
					Parallel.ForEach(source: finalizers, body: (id) =>
					{
						try
						{
							FinalizeHub(id);
						}
						catch (Exception e)
						{
							GenUtils.PriorityLogMsg("exception", "Worker finalize", e.Message);
							return;
						}
					}
					);
					sw_finalize.Stop();

					sw_region.Start();
					try
					{
						foreach (var id in regions )            // now update regions, this can also be parallelized as needed
						{
							if (RegionIsStale(id))
							{
								Scheduler.UpdateStartTaskForId(id, TaskType.regiontasks);
								ProcessRegion(id);
								StopTask(id, TaskType.regiontasks);
							}
						}
					}
					catch (Exception e)
					{
						GenUtils.PriorityLogMsg("exception", "Worker regiontasks", e.Message);
						return;
					}

					sw_region.Stop();

					sw_total.Stop();

					logger.LogMsg("status", String.Format("worker: ical {0}, finalize {1}, nonical {2}, region {3}, total {4}",
							sw_ical.Elapsed.ToString(),
							sw_finalize.Elapsed.ToString(),
							sw_nonical.Elapsed.ToString(),
							sw_region.Elapsed.ToString(),
							sw_total.Elapsed.ToString()
							),
						null
						);

					logger.LogMsg("status", "worker sleeping", null);
					Utils.WaitMinutes(Convert.ToInt32(settings["scheduler_check_interval_minutes"]));

				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "Worker.Run", e.Message + e.StackTrace);
			}
		}

		public static void MaybeRemakeWebRoleData()
		{
			var webrole_sentinel_uri = BlobStorage.MakeAzureBlobUri("admin", CalendarAggregator.Configurator.webrole_sentinel, false);
			if (BlobStorage.ExistsBlob(webrole_sentinel_uri))
			{
				var list = HttpUtils.FetchUrl(webrole_sentinel_uri).DataAsString();
				GenUtils.LogMsg("status", "worker role remaking wrd for " + list, null);
				var args = new Dictionary<string, string>();
				WebRoleData.MakeWebRoleData();
				bs.DeleteBlob("admin", CalendarAggregator.Configurator.webrole_sentinel);
			}
		}

		private void BuildTodo(Todo todo, List<string> ids)
		{
			var options = new ParallelOptions();
			//options.MaxDegreeOfParallelism = 1;
			Parallel.ForEach(source: ids, parallelOptions:options, body: (id) =>
			{
				var calinfo = Utils.AcquireCalinfo(id);

				/* disabled for now, twitter didn't like this
				 * 
				var messages_for_hub = TwitterMessagesForHub(twitter_direct_messages, calinfo);

				if (messages_for_hub.Count > 0)
				{
					lock (todo.twitter_messages)
					{
						todo.twitter_messages.Add(id);
					}
				}

				if (messages_for_hub.FindAll(msg => msg.text == "start").Count > 0)  // start needs special handling, generic handler takes care of other messages
					lock (todo.start_requests)
					{
						todo.start_requests.Add(id);
					}
				 */

				if (calinfo.hub_enum == HubType.where)
				{
					if (StartTask(id, calinfo, TaskType.nonicaltasks) == TaskType.nonicaltasks) // time to reaggregate a where hub's nonical sources?
						lock (todo.nonicaltasks)
						{
							todo.nonicaltasks.Add(id);
						}
				}

				if (calinfo.hub_enum == HubType.where || calinfo.hub_enum == HubType.what)
				{
					if (StartTask(id, calinfo, TaskType.icaltasks) == TaskType.icaltasks) // time to reaggregate a hub's ical sources?
						lock (todo.icaltasks)
						{
							todo.icaltasks.Add(id);
						}
				}

			}
			);

		}

		private static void MaybePutRegionOnTodo(Todo todo, string id)
		{
			if (RegionIsStale(id)) // time to rebuild a region?
			{
				lock (todo.regiontasks)
				{
					todo.regiontasks.Add(id);
				}
			}
		}

		/* twitter messaging disabled for now, twitter did not like it
        private List<TwitterDirectMessage> TwitterMessagesForHub(List<TwitterDirectMessage> twitter_direct_messages, Calinfo calinfo)
        {
            List<TwitterDirectMessage> ret = new List<TwitterDirectMessage>();

            if (twitter_direct_messages.Count > 0 && calinfo.twitter_account != null) // any messages for this id?
                ret = twitter_direct_messages.FindAll(msg => msg.sender_screen_name.ToLower() == calinfo.twitter_account.ToLower());
            // see http://friendfeed.com/elmcity/53437bec/copied-original-css-file-to-my-own-server for why ToLower()

            return ret;
        }

        private void HandleTwitterMessages(Todo todo, List<string> ids)
        {
            foreach (var id in ids)
            {
                if (todo.twitter_messages.HasItem(id))
                {
                    var calinfo = Utils.AcquireCalinfo(id);
                    var messages_for_hub = TwitterMessagesForHub(twitter_direct_messages, calinfo);
                    if (messages_for_hub.Count > 0)
                        HandleTwitterMessagesForHub(messages_for_hub, id);
                }
            }
        }

        public void HandleTwitterMessagesForHub(List<TwitterDirectMessage> messages, string id)
        {
            foreach (var message in messages)
            {
                try
                {
                    var twitter_command = new TwitterCommand(message.id, message.sender_screen_name, message.recipient_screen_name, message.text);
                    if (twitter_command.command != TwitterCommandName.none)
                    {
                        switch (twitter_command.command)
                        {
                            case TwitterCommandName.meta_refresh:
                                var calinfo = Utils.AcquireCalinfo(id);
                                GenUtils.PriorityLogMsg("status", "Received meta_refresh message from " + id, null);
                                TwitterApi.SendTwitterDirectMessage(calinfo.twitter_account, "elmcity received your meta_refresh message");
                                Utils.MakeMetadataPage(id);
                                TwitterApi.SendTwitterDirectMessage(calinfo.twitter_account, "elmcity processed your meta_refresh message, you can verify the result at http://" + ElmcityUtils.Configurator.appdomain + "/services/" + id + "/metadata");
                                break;
                            case TwitterCommandName.add_fb_feed:  // disable for now
                                //var action = new AddFacebookFeed();
                                //action.Perform(twitter_command, id);
                                break;
                            default:
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    GenUtils.PriorityLogMsg("exception", "HandleMessages", e.Message);
                }
            }
        } */

		public static void ProcessNonIcal(string id)
		{
			logger.LogMsg("status", "worker starting on nonical tasks for " + id, null);
			var calinfo = Utils.AcquireCalinfo(id);

			try
			{
				CalendarAggregator.Utils.ZeroCountForService(id, "eventful");
				DoEventful(calinfo);
			}
			catch (Exception e1)
			{
				GenUtils.PriorityLogMsg("exception", "DoEventful", e1.Message + e1.StackTrace);
			}

			try
			{
				CalendarAggregator.Utils.ZeroCountForService(id, "eventbrite");
				DoEventBrite(calinfo);
			}
			catch (Exception e3)
			{
				GenUtils.PriorityLogMsg("exception", "DoEventBrite", e3.Message + e3.StackTrace);
			}

			try
			{
				CalendarAggregator.Utils.ZeroCountForService(id, "facebook");
				DoFacebook(calinfo);
			}
			catch (Exception e4)
			{
				GenUtils.PriorityLogMsg("exception", "DoFacebook", e4.Message + e4.StackTrace);
			}

		}

		public static void ProcessIcal(string id)
		{
			var calinfo = Utils.AcquireCalinfo(id);
			logger.LogMsg("status", "worker starting on ical tasks for " + id, null);
			var fr = new FeedRegistry(id);
			DoIcal(fr, calinfo);
		}

		public static void ProcessRegion(string region)
		{
			var calinfo = Utils.AcquireCalinfo(region);
			logger.LogMsg("status", "worker starting on region tasks for " + region, null);

			try
			{
				var es_region = new ZonelessEventStore(calinfo);

				var ids = Utils.GetIdsForRegion(region);

				Parallel.ForEach(source: ids, body: (id) =>
				{
					var uri = BlobStorage.MakeAzureBlobUri(id, id + ".zoneless.obj", false);
					var es = (ZonelessEventStore)BlobStorage.DeserializeObjectFromUri(uri);
					foreach (var evt in es.events)
					{
						evt.categories += "," + id.ToLower();  // so tag viewer can slice region by hub name
						lock (es_region)
						{
							es_region.events.Add(evt);
						}
					}
				}
				);

				Utils.UpdateFeedCountForId(region);

				EventStore.UniqueFilterSortSerialize(region, es_region);

				CacheUtils.MarkBaseCacheEntryForRemoval(Utils.MakeBaseZonelessUrl(region), Convert.ToInt32(settings["webrole_instance_count"]));

				RenderTags(region);

				Utils.TagsByHub(region, authorized: true);
				Utils.TagsByHub(region, authorized: false);

				SaveRegionStats(region);

				Utils.SaveQuickStatsForRegion(region);
			}
			catch (Exception e)
			{
				var msg = "process region: " + region;
				var data = e.Message + e.StackTrace;
				GenUtils.PriorityLogMsg("exception", msg, data);
			}
			logger.LogMsg("status", "worker done processing region " + region, null);
		}

		public static void FinalizeHub(string id)
		{
			logger.LogMsg("status", "worker finalizing hub: " + id, null);

			Utils.UpdateFeedCountForId(id);

			var calinfo = Utils.AcquireCalinfo(id);

			EventStore.CombineZonedEventStoresToZonelessEventStore(id, settings); 

			// Create or update an entry in the cacheurls table for the base object. 
			// key is http://elmcity.blob.core.windows.net/ID/ID.zoneless.obj
			// value is # of webrole instances that could be holding this in cache
			// each instance will check this table periodically. if value is nonzero and url in cache, it'll evict the object
			// and decrement the count

			// note when removal occurs it also triggers a purge of dependencies, so if the base entry is
			// http://elmcity.blob.core.windows.net/a2cal/a2cal.zoneless.obj
			// then dependencies also ousted from cache include:
			// /services/a2cal/html?view=&count=0
			// /services/a2cal/rss?view=government
			// /services/a2cal/xml?view=music&count=10    ... etc.

			CacheUtils.MarkBaseCacheEntryForRemoval(Utils.MakeBaseZonelessUrl(id), Convert.ToInt32(settings["webrole_instance_count"]));

			RenderTags(id);  // static renderings, mainly for debugging now that GetEvents uses dynamic rendering

			var fr = new FeedRegistry(id);
			fr.LoadFeedsFromAzure(FeedLoadOption.all);

			if (calinfo.hub_enum != HubType.region)
			{
				SaveWhereStats(fr, calinfo);
				Utils.SaveMeetupLocations(calinfo, settings);
			}

			if (calinfo.hub_enum == CalendarAggregator.HubType.what)
				SaveWhatStats(fr, calinfo);

			if (calinfo.hub_enum == CalendarAggregator.HubType.region)
				SaveRegionStats(id);

			//if (!Utils.IsRegion(id))
			//	MergeIcs(calinfo);
			// else  
			// todo: create MergeRegionIcs

			Utils.VisualizeTagSources(id);

			logger.LogMsg("status", "worker done finalizing hub " + id, null);
		}

		private static void StopTask(string id, TaskType type)
		{
			Scheduler.StopTaskForId(id, type);
			Scheduler.UnlockId(id, type);
		}

		private static TaskType StartTask(string id, Calinfo calinfo, TaskType type)
		{
			Scheduler.EnsureTaskRecord(id, type);

			if (Scheduler.IsAbandoned(id, type))           // abandoned?
				Scheduler.UnlockId(id, type);                 // unlock

			if (Scheduler.IsLockedId(id, type))          // locked?
				return TaskType.none;                       // skip

			if (AcquireLock(id, type) == false)            // can't lock?
				return TaskType.none;                        // skip

			var now = System.DateTime.Now.ToUniversalTime();

			TaskType started = TaskType.none;

			/*
			if (todo.start_requests.HasItem(id))
			{
				logger.LogMsg("status", "Received start message from " + id, null);
				//TwitterApi.SendTwitterDirectMessage(calinfo.twitter_account, "elmcity received your start message");
				started = type;
			}
			else */

			started = Scheduler.MaybeStartTaskForId(now, calinfo, type);

			if (started == TaskType.none)
				Scheduler.UnlockId(id, type);

			return started;
		}

		private static bool AcquireLock(string id, TaskType type)
		{
			var lock_response = Scheduler.LockId(id, type);

			if (lock_response.status != HttpStatusCode.Created)
			{
				logger.LogMsg("warning", "LockId", "expected to create lock but could not");
				return false;
			}
			else
				return true;
		}

		private static List<string> MaybeAdjustIdsForTesting(List<string> ids)
		{
			if (testids.Count > 0)
			{
				List<string> tmp = new List<string>();
				foreach (var testid in testids)
					tmp.Add(testid);
				ids = tmp;
			}

			if (ids.Count == 1 && ids[0] == "")  // bench the worker
				ids = new List<string>();

			return ids;
		}

		private static List<string> ExcludeRegions(List<string> ids)
		{
			var regions = Utils.GetRegionIds();
			var final_ids = new List<string>();
			foreach (var id in ids)
			{
				if (!regions.Exists(x => x == id))
					final_ids.Add(id);
			}
			return final_ids;
		}

		public static void RenderTags(string id)
		{
			var cr = Utils.AcquireRenderer(id);

			try
			{
				var es = ObjectUtils.GetTypedObj<ZonelessEventStore>(id, id + ".zoneless.obj");
				var tags = cr.MakeTagCloud(es);
				var tags_json = JsonConvert.SerializeObject(tags);
				bs.PutBlob(id, CalendarAggregator.Configurator.tags_json, tags_json);
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "save tags_json: " + id, e.Message + e.StackTrace);
			}

		}

		public static void DoIcal(FeedRegistry fr, Calinfo calinfo)
		{
			if (testfeeds.Count > 0)
				foreach (var testfeed in testfeeds)
					fr.AddFeed(testfeed, "testing: " + testfeed);
			else
				fr.LoadFeedsFromAzure(FeedLoadOption.all);

			var id = calinfo.id;

			ZonedEventStore ical = new ZonedEventStore(calinfo, SourceType.ical);
			try
			{

				logger.LogMsg("status", "DoIcal: " + id, null);
				Collector coll = new Collector(calinfo, settings);
				coll.CollectIcal(fr, ical, test: testing);
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "DoIcal: " + id, e.Message + e.StackTrace);
			}
		}

		public static void DoEventful(Calinfo calinfo)
		{
			if (settings["use_eventful"] == "true" && calinfo.eventful)
			{
				var eventful = new ZonedEventStore(calinfo, SourceType.eventful);
				Collector coll = new Collector(calinfo, settings);
				coll.CollectEventful(eventful, testing);
			}
		}

		public static void DoEventBrite(Calinfo calinfo)
		{
			if (settings["use_eventbrite"] == "true" && calinfo.eventbrite)
			{
				var eventbrite = new ZonedEventStore(calinfo, SourceType.eventbrite);
				Collector coll = new Collector(calinfo, settings);
				coll.CollectEventBrite(eventbrite);
			}
		}

		public static void DoFacebook(Calinfo calinfo)
		{
			if (settings["use_facebook"] == "true" && calinfo.facebook)
			{
				var facebook = new ZonedEventStore(calinfo, SourceType.facebook);
				Collector coll = new Collector(calinfo, settings);
				coll.CollectFacebook(facebook, testing);
			}
		}

		public static void SaveWhereStats(FeedRegistry fr, Calinfo calinfo)
		{
			var id = calinfo.id;
			logger.LogMsg("status", "SaveWhereStats: " + id, null);
			NonIcalStats estats = GetNonIcalStats(NonIcalType.eventful, id, calinfo, settings);
			NonIcalStats ustats = GetNonIcalStats(NonIcalType.upcoming, id, calinfo, settings);
			NonIcalStats ebstats = GetNonIcalStats(NonIcalType.eventbrite, id, calinfo, settings);
			NonIcalStats fbstats = GetNonIcalStats(NonIcalType.facebook, id, calinfo, settings);
			Dictionary<string, IcalStats> istats = GetIcalStats(id);

			var where = calinfo.where;
			string[] response = Utils.FindCityOrTownAndStateAbbrev(where);
			var city_or_town = response[0];
			var state_abbrev = response[1];
			int pop = Utils.FindPop(id, city_or_town, state_abbrev);
			var futurecount = 0;
			futurecount += estats.eventcount;
			futurecount += ustats.eventcount;
			futurecount += ebstats.eventcount;

			var sb_report = new StringBuilder();
			sb_report.Append(MakeTableHeader());

			var options = new ParallelOptions();

			Parallel.ForEach(source: fr.feeds.Keys, parallelOptions: options, body: (feedurl) =>
			//		foreach (var feedurl in fr.feeds.Keys)
			{
				StatsRow(id, istats, sb_report, ref futurecount, feedurl);
			}
			);

			sb_report.Append("</table>\n");

			var events_per_person = Convert.ToInt32(futurecount) / (float)pop;
			var report = Utils.EmbedHtmlSnippetInDefaultPageWrapper(calinfo, sb_report.ToString());
			bs.PutBlob(id, id + ".stats.html", new Hashtable(), Encoding.UTF8.GetBytes(report), "text/html");

			var dict = new Dictionary<string, object>();
			dict.Add("events", futurecount.ToString());
			dict.Add("events_per_person", string.Format("{0:f}", events_per_person));
			TableStorage.UpmergeDictToTableStore(dict, "metadata", id, id);
		}

		private static void StatsRow(string id, Dictionary<string, IcalStats> istats, StringBuilder sb_report, ref int futurecount, string feedurl)
		{
			var feed_metadict = Metadata.LoadFeedMetadataFromAzureTableForFeedurlAndId(feedurl, id);
			string homeurl;
			feed_metadict.TryGetValue("url", out homeurl);
			string redirected_url;
			feed_metadict.TryGetValue("redirected_url", out redirected_url);
			if (String.IsNullOrEmpty(redirected_url))
				redirected_url = feedurl;
			DoStatsRow(id, istats, sb_report, ref futurecount, feedurl, redirected_url, homeurl);
		}

		public static void SaveWhatStats(FeedRegistry fr, Calinfo calinfo)
		{
			var id = calinfo.id;
			logger.LogMsg("status", "SaveWhatStats: " + id, null);
			Dictionary<string, IcalStats> istats = GetIcalStats(id);
			var sb_report = new StringBuilder();
			sb_report.Append(MakeTableHeader());
			var futurecount = 0;

			var options = new ParallelOptions();
			Parallel.ForEach(source: fr.feeds.Keys, parallelOptions: options, body: (feedurl) =>
			//		foreach (var feedurl in fr.feeds.Keys)
			{
				StatsRow(id, istats, sb_report, ref futurecount, feedurl);
			}
			);

			sb_report.Append("</table>\n");
			string preamble = MakeWhatPreamble(futurecount);
			var report = preamble + sb_report.ToString();
			report = Utils.EmbedHtmlSnippetInDefaultPageWrapper(calinfo, report);
			bs.PutBlob(id, id + ".stats.html", new Hashtable(), Encoding.UTF8.GetBytes(report), null);
		}

		public static void SaveRegionStats(string region)
		{
			logger.LogMsg("status", "SaveRegionStats: " + region, null);
			var ids = Utils.GetIdsForRegion(region);
			var region_report = new StringBuilder();

			var chunks = new Dictionary<string, string>();

			Parallel.ForEach(source: ids, body: (id) =>
			{
				var hub_report = new StringBuilder();
				hub_report.Append("<h1>" + id + "</h1>\n");
				var hub_report_uri = BlobStorage.MakeAzureBlobUri(id, id + ".stats.html");
				var hub_report_html = HttpUtils.FetchUrl(hub_report_uri).DataAsString();
				var doc = new HtmlDocument();
				doc.LoadHtml(hub_report_html);
				hub_report.AppendLine("<table style=\"border-spacing:6px\" class=\"icalstats\">");
				var table = doc.DocumentNode.SelectSingleNode("//table");
				hub_report.AppendLine(table.WriteContentTo());
				hub_report.AppendLine("</table>");
				lock (chunks)
				{
					chunks[id] = hub_report.ToString();    // to enable sorted output
				}
			}
			);

			foreach (var id in ids)
				region_report.AppendLine(chunks[id]);

			var region_calinfo = Utils.AcquireCalinfo(region);
			var report = Utils.EmbedHtmlSnippetInDefaultPageWrapper(region_calinfo, region_report.ToString());
			bs.PutBlob(region, region + ".stats.html", report, "text/html");
		}

		private static string MakeTableHeader()
		{
			return string.Format(@"
<table style=""border-spacing:6px"" class=""icalstats"">
<tr>
<td>feed</td> 
<td>home</td>         
<td>raw view</td>
<td>html view</td>
<td>validation</td>
<td>error?</td>
<td>future</td>
<td>single</td>
<td>recurring</td>
<td>instances</td>
<td>loaded</td>
<td>when</td>
<td>PRODID</td>
</tr>");
		}

		private static string MakeWhatPreamble(int futurecount)
		{
			string preamble = string.Format(@"
<div style=""font-size:smaller"">
<p>
Future events {0}
</p>
",

					 futurecount
					 );
			return preamble;
		}

		private static void DoStatsRow(string id, Dictionary<string, IcalStats> istats, StringBuilder sb_report, ref int futurecount, string feedurl, string redirected_url, string homeurl)
		{
			try
			{
				var ical_stats = istats[feedurl];

				// var is_private = Metadata.IsPrivateFeed(id, feedurl); // Related to http://blog.jonudell.net/2011/06/02/syndicating-facebook-events/

				// var is_private = false; // Private FB feeds idle for now

				var feed_column = String.Format(@"<a title=""click to load calendar ics"" href=""{0}"">{1}</a>", feedurl, ical_stats.source);
				var home_column = String.Format(@"<a title=""click to visit calendar page"" href=""{0}""><img src=""http://elmcity.blob.core.windows.net/admin/home.png""></a>", homeurl);
				var raw_view_url = string.Format("/text_from_ics?url={0}", Uri.EscapeDataString(feedurl));
				var html_view_url = string.Format("/view_calendar?feedurl={0}&id={1}", Uri.EscapeDataString(feedurl), id);
				var raw_view_column = string.Format(@"<a title=""click to view raw feed"" href=""{0}""><img src=""http://elmcity.blob.core.windows.net/admin/glasses.png""></a>", raw_view_url);
				var html_view_column = string.Format(@"<a title=""click to view feed as html"" href=""{0}""><img src=""http://elmcity.blob.core.windows.net/admin/glasses2.png""></a>", html_view_url);
				var validation_column = String.Format(@"<a title=""click to validate feeed"" href=""{0}""><img src=""http://elmcity.blob.core.windows.net/admin/checkbox.png""></a>", Utils.ValidationUrlFromFeedUrl(redirected_url));

				System.Threading.Interlocked.Exchange(ref futurecount, futurecount + istats[feedurl].futurecount);

				lock (sb_report)
				{
					sb_report.Append(
						string.Format(@"
<tr>
<td>{0}</td>
<td>{1}</td>
<td>{2}</td>
<td>{3}</td>
<td>{4}</td>
<td>{5}</td>
<td>{6}</td>
<td>{7}</td>
<td>{8}</td>
<td>{9}</td>
<td>{10}</td>
<td>{11}</td>
<td>{12}</td>
</tr>
",
					feed_column,                                             // 0
					home_column,											 // 1
					raw_view_column,										 // 2
					html_view_column,										 // 3
					validation_column,                                       // 4
					ical_stats.dday_error,                                   // 5
					ical_stats.futurecount,                                  // 6
					ical_stats.singlecount,                                  // 7
					ical_stats.recurringcount,                               // 8
					ical_stats.recurringinstancecount,                       // 9
					ical_stats.loaded,                                       // 10
					ical_stats.whenchecked,                                  // 11
					ical_stats.prodid                                        // 12
					)
					);
				}
			}
			catch (Exception ex)
			{
				logger.LogMsg("exception", feedurl + "," + istats[feedurl].source + " :  stats not ready", ex.Message);
			}
		}

		public static void MergeIcs(Calinfo calinfo)
		{
			var id = calinfo.id;
			logger.LogMsg("status", "MergeIcs: " + id, null);
			List<string> suffixes = new List<string>() { "ical" };
			if (calinfo.hub_enum == HubType.where)
				foreach (NonIcalType type in Enum.GetValues(typeof(CalendarAggregator.NonIcalType)))
				{
					if (Utils.UseNonIcalService(type, settings, calinfo) == true)
						suffixes.Add(type.ToString());
				}
			try
			{
				var all_ical = new iCalendar();
				Collector.AddTimezoneToDDayICal(all_ical, calinfo.tzinfo);

				foreach (var suffix in suffixes)  // todo: skip if disabled in settings
				{
					var url = MakeIcsUrl(id, suffix);
					try
					{
						var feedtext = HttpUtils.FetchUrl(url).DataAsString();
						var sr = new StringReader(feedtext);
						var ical = iCalendar.LoadFromStream(sr).First().Calendar;
						all_ical.MergeWith(ical);
					}
					catch (Exception e)
					{
						GenUtils.LogMsg("warning", "MergeICS: " + url, e.Message);
					}
				}

				var serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer(all_ical);
				var icsbytes = Encoding.UTF8.GetBytes(serializer.SerializeToString(all_ical));
				bs.PutBlob(id, id + ".ics", new Hashtable(), icsbytes, "text/calendar");
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "MergeIcs: " + id, e.Message + e.StackTrace);
			}
		}

		private static NonIcalStats GetNonIcalStats(NonIcalType type, string id, Calinfo calinfo, Dictionary<string, string> settings)
		{
			var name = type.ToString() + "_stats.json";
			var stats = new NonIcalStats();
			if (settings["use_" + type.ToString()] != "true" || Utils.UseNonIcalService(type, settings, calinfo) != true)
			{
				stats.venuecount = 0;
				stats.eventcount = 0;
				stats.whenchecked = DateTime.MinValue;
				return stats;
			}
			try
			{
				if (BlobStorage.ExistsBlob(id, name))
					stats = Utils.DeserializeObjectFromJson<NonIcalStats>(id, name);
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "GetEventAndVenueStats: " + id + " " + name, e.Message + e.StackTrace);
			}
			return stats;
		}

		private static Dictionary<string, IcalStats> GetIcalStats(string container)
		{
			return FeedRegistry.DeserializeIcalStatsFromJson(blobhost, container, "ical_stats.json");
		}

		private static Uri MakeIcsUrl(string id, string suffix)
		{
			string url = string.Format("{0}/{1}/{2}",
				ElmcityUtils.Configurator.azure_blobhost,
				id.ToLower(),
				id + "_" + suffix + ".ics");
			return new Uri(url);
		}

		private static void ReloadMonitorCounters(object o, ElapsedEventArgs args)
		{
			monitor.ReloadCounters();
		}

		/*
		 # runs blobroot/monitor.py
		 # pull 24 hours of diagnostics from odata feed into a file
		 # run logparser queries against the file
		 # output charts (gifs) and/or tables (htmls) to charts container in azure storage
		 # calls blobroot/dashboard.py to update pages that include charts and tables  
		*/
		public static void MonitorAdmin(object o, ElapsedEventArgs args)
		{
			logger.LogMsg("status", "MonitorAdmin", null);
			try
			{
				PythonUtils.RunIronPython(local_storage_path, CalendarAggregator.Configurator.monitor_script_url, new List<string>() { "", "", "" });
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "MonitorAdmin", e.Message + e.StackTrace);
			}
		}

		public void GeneralAdmin(object o, ElapsedEventArgs args)
		{
			try
			{
				GenUtils.PriorityLogMsg("status", "GeneralAdmin", null);

				Utils.MakeWhereSummary();  // refresh http://elmcity.blob.core.windows.net/admin/where_summary.html

				Utils.MakeWhatSummary();  // refresh http://elmcity.blob.core.windows.net/admin/what_summary.html

				var ids = Metadata.LoadWhatWhereIdsFromAzureTable();
				Parallel.ForEach(source: ids, body: (id) =>
				{
					var calinfo = Utils.AcquireCalinfo(id);
					Utils.SaveIcalPerFeedLocations(calinfo, settings);
				});
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "GeneralAdmin", e.Message + e.StackTrace);
			}

			//Utils.MakeFeaturedHubs(); // idle for now

		}

		public static void TestRunnerAdmin(object o, ElapsedEventArgs args)
		{
			logger.LogMsg("status", "TestRunnerAdmin", null);
			var calinfo = new Calinfo(ElmcityUtils.Configurator.azure_compute_account);
			try
			{
				int failed;
				failed = GenUtils.RunTests("CalendarAggregator");
				failed += GenUtils.RunTests("ElmcityUtils");
				//failed += GenUtils.RunTests("WorkerRole");
				//failed += GenUtils.RunTests("WebRole");
				bs.PutBlob("admin", "testrunner", failed.ToString() + " " + System.DateTime.UtcNow.ToString(), "text/plain");
				if (failed > 0)
					//TwitterApi.SendTwitterDirectMessage(calinfo.twitter_account, failed + " tests failed");
					GenUtils.PriorityLogMsg("warning", "TestRunner", failed.ToString() + " tests failed");
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "TestRunnerAdmin", e.Message + e.StackTrace);
			}
		}

		// runs _admin.py from blobroot/admin, most duties have migrated out of python and into c#
		public static void IronPythonAdmin(object o, ElapsedEventArgs e)
		{
			try
			{
				logger.LogMsg("status", "IronPythonAdmin", null);
				PythonUtils.RunIronPython(local_storage_path, CalendarAggregator.Configurator.iron_python_admin_script_url, new List<string>() { "", "", "" });

			}
			catch (Exception ex)
			{
				logger.LogMsg("exception", "IronPythonAdmin", ex.Message + ex.StackTrace);
			}
		}

		public static bool RegionIsStale(string region)
		{
			var stale = false;
			try
			{
				var bs = BlobStorage.MakeDefaultBlobStorage();
				var region_props = bs.GetBlobProperties(region.ToLower(), region + ".zoneless.obj");
				var region_modified = DateTime.Parse(region_props.HttpResponse.headers["Last-Modified"]);
				foreach (var hub in Utils.GetIdsForRegion(region))
				{
					var hub_props = bs.GetBlobProperties(hub.ToLower(), hub + ".zoneless.obj");
					var hub_modified = DateTime.Parse(hub_props.HttpResponse.headers["Last-Modified"]);
					if (hub_modified > region_modified)
					{
						stale = true;
						break;
					}
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "RegionIsStale: " + region, e.Message + e.StackTrace);
			}
			return stale;
		}

		/*
		public override RoleStatus GetHealthStatus()
			{
			return RoleStatus.Healthy;
			}
		 */

	}
}

/*
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;
using ElmcityUtils;

namespace WorkerRole
{
    public class WorkerRole : RoleEntryPoint
    {
        public override void Run()
        {

            while (true)
            {
                Thread.Sleep(10000);
                GenUtils.LogMsg("DEBUG", "WorkerRole: Run", "MVC4");
           
            }
        }

        public override bool OnStart()
        {
            GenUtils.LogMsg("DEBUG", "WorkerRole: OnStart", "MVC4");
            // Set the maximum number of concurrent connections 
            ServicePointManager.DefaultConnectionLimit = 12;

            return base.OnStart();
        }
    }
}
*/