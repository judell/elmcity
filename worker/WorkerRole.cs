﻿/* ********************************************************************************
 *
 * Copyright 2010 Microsoft Corporation
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

namespace WorkerRole
{
	public class WorkerRole : RoleEntryPoint
	{
#if false // true if testing, false if not testing
        private static int startup_delay = 100000000; // add delay if want to focus on webrole
		//private static int startup_delay = 0;
        private static List<string> testids = new List<string>() { "elmcity" };  // focus on a hub
        private static List<string> testfeeds = new List<string>() // optionally focus on a feed in that hub
			{ "http://www.facebook.com/ical/u.php?uid=652661115&key=AQDkPMjwIPc30qcT" };
        private static bool testing = true;
#else     // not testing
		private static int startup_delay = 0;
		private static List<string> testids = new List<string>();
		private static List<string> testfeeds = new List<string>();
		private static bool testing = false;
#endif

		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();
		private static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
		private static Delicious delicious = Delicious.MakeDefaultDelicious();
		private static Logger logger = new Logger();
		private static Dictionary<string, string> settings = GenUtils.GetSettingsFromAzureTable();

		private static string blobhost = ElmcityUtils.Configurator.azure_blobhost;
		public static List<string> ids { get; set; }
		private static Dictionary<string, int> feedcounts = new Dictionary<string, int>();

		private static string local_storage_path;

		private static List<TwitterDirectMessage> twitter_direct_messages;

		private static ElmcityUtils.Monitor monitor;

		public override bool OnStart()
		{
			try
			{
				var hostname = System.Net.Dns.GetHostName();
				var msg = "Worker: OnStart: " + hostname;
				GenUtils.PriorityLogMsg("info", msg, null);

				HttpUtils.Wait(startup_delay);

				local_storage_path = RoleEnvironment.GetLocalResource("LocalStorage1").RootPath;

				RoleEnvironment.Changing += RoleEnvironmentChanging;

				GenUtils.LogMsg("info", "LocalStorage1", local_storage_path);

				// PythonUtils.InstallPythonStandardLibrary(local_storage_path, ts);

				HttpUtils.SetAllowUnsafeHeaderParsing(); //http://www.cookcomputing.com/blog/archives/000556.html

				Utils.ScheduleTimer(IronPythonAdmin, minutes: CalendarAggregator.Configurator.ironpython_admin_interval_hours * 60, name: "IronPythonAdmin", startnow: false);

				Utils.ScheduleTimer(HighFrequencyScript, minutes: CalendarAggregator.Configurator.high_frequency_script_interval_minutes, name: "HighFrequencyScript", startnow: false);

				//Utils.ScheduleTimer(DeliciousAdmin, minutes: CalendarAggregator.Configurator.delicious_admin_interval_hours * 60, name: "DeliciousAdmin", startnow: false);

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
			logger.LogMsg("info", msg, null);
			GenUtils.PriorityLogMsg("info", msg, null);
			var snapshot = Counters.MakeSnapshot(Counters.GetCounters());
			monitor.StoreSnapshot(snapshot);

			base.OnStop();
		}

		private void RoleEnvironmentChanging(object sender, RoleEnvironmentChangingEventArgs e)
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
				GenUtils.PriorityLogMsg("info", message, null);

				while (true)
				{
					logger.LogMsg("info", "worker waking", null);

					settings = GenUtils.GetSettingsFromAzureTable();

					// SaveSettings(settings); // not needed

					ids = Metadata.LoadHubIdsFromAzureTable();

					twitter_direct_messages = TwitterApi.GetNewTwitterDirectMessages(); // get new control messages

					ids = MaybeAdjustIdsForTesting(ids);

					foreach (var id in ids)
					{
						var calinfo = new Calinfo(id);

						bool got_start_request = false;
						bool got_meta_refresh_request = false;

						if ( twitter_direct_messages.Count > 0 && calinfo.twitter_account != null) // any messages for this id?
							try
							{
								var messages = twitter_direct_messages.FindAll(msg => msg.sender_screen_name.ToLower() == calinfo.twitter_account.ToLower());
								// see http://friendfeed.com/elmcity/53437bec/copied-original-css-file-to-my-own-server for why ToLower()

								got_meta_refresh_request = messages.FindAll(msg => msg.text == "meta_refresh").Count > 0;

								if (got_meta_refresh_request)
								{
									GenUtils.PriorityLogMsg("info", "Received meta_refresh message from " + id, null);
									TwitterApi.SendTwitterDirectMessage(calinfo.twitter_account, "elmcity received your meta_refresh message");
									if (Utils.ElmcityIdUsesTwitterAuth(id) == false) // revert to old delicious-based mechanism
										UpdateFeedsAndMetadataForIdToAzure(id);
									Utils.MakeMetadataPage(id);
									TwitterApi.SendTwitterDirectMessage(calinfo.twitter_account, "elmcity processed your meta_refresh message, you can verify the result at http://elmcity.cloudapp.net/services/" + id + "/metadata");
									calinfo = new Calinfo(id);
								}

								got_start_request = messages.FindAll(msg => msg.text == "start").Count > 0;

								HandleMessages(messages, id);  // handle all other messages
							}
							catch (Exception e)
							{
								GenUtils.PriorityLogMsg("exception", "WorkerRole.Run: handling twitter messages", e.Message);
							}

						if (StartTask(id, calinfo, got_start_request) == false)
							continue;

						ProcessHub(id, calinfo);

						if (got_start_request)
						{
							logger.LogMsg("info", "Processed start message from " + id, null);
							TwitterApi.SendTwitterDirectMessage(calinfo.twitter_account, "elmcity processed your start message");
						}

						StopTask(id);
					}

					logger.LogMsg("info", "worker sleeping", null);
					Utils.Wait(CalendarAggregator.Configurator.scheduler_check_interval_minutes * 60);
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "Worker.Run", e.Message + e.StackTrace);
			}
		}

		public void HandleMessages(List<TwitterDirectMessage> messages, string id)
		{
			foreach (var message in messages)
			{
				try
				{
					var twitter_command = new TwitterCommand(message.id, message.sender_screen_name, message.recipient_screen_name, message.text);
					if (twitter_command.command != null)
					{
						switch (twitter_command.command)
						{
							case "add_fb_feed":
								var action = new AddFacebookFeed();
								action.Perform(twitter_command, id);
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
		}

		public void ProcessHub(string id, Calinfo calinfo)
		{
			logger.LogMsg("info", "worker processing hub: " + id, null);

			var fr = new FeedRegistry(id);

			try
			{

				DoIcal(fr, calinfo);

				if (calinfo.hub_enum == CalendarAggregator.HubType.where)
				{
					DoEventful(calinfo);
					DoUpcoming(calinfo);
					DoEventBrite(calinfo);
					DoFacebook(calinfo);
				}

				EventStore.CombineZonedEventStoresToZonelessEventStore(id, settings);

				RenderHtmlXmlJson(id, calinfo);

				if (calinfo.hub_enum == CalendarAggregator.HubType.where)
					SaveWhereStats(fr, calinfo);

				if (calinfo.hub_enum == CalendarAggregator.HubType.what)
					SaveWhatStats(fr, calinfo);

				MergeIcs(calinfo);

			}
			catch (Exception e)
			{
				var msg = "worker main loop: " + id;
				var data = e.Message + e.StackTrace;
				GenUtils.PriorityLogMsg("exception", msg, data);
			}

			logger.LogMsg("info", "worker done processing: " + id, null);

		}

		public static void UpdateMetadataToAzure(string id)
		{
			logger.LogMsg("info", "UpdateMetadataToAzure", null);
			try
			{
				var dict = delicious.StoreMetadataForIdToAzure(id, true, new Dictionary<string, string>());
				if (dict == null)  // bail if feed metadata query returns null (because MetadataQueryOutcome != Success)
				{
					logger.LogMsg("info", "StoreMetadataForid: " + id + " : null response", null);
					return;
				}
				var snapshot_changed = ObjectUtils.SavedJsonSnapshot(id, ObjectUtils.JsonSnapshotType.DictStr, "metadata", dict);
				if (snapshot_changed == true)
				{
					Metadata.UpdateDependentStructures(id);
				}
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "UpdateMetadataToAzure", e.Message + e.StackTrace);
			}
		}

		public static void RecacheHubIdsToAzure()
		{
			try
			{
				delicious.StoreHubIdsToAzureTable();
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "RecacheHubIdsToAzure", e.Message + e.StackTrace);
			}
		}

		private static void StopTask(string id)
		{
			Scheduler.StopTaskForId(id);
			Scheduler.UnlockId(id);
		}

		private static bool StartTask(string id, Calinfo calinfo, bool got_start_request)
		{
			Scheduler.EnsureTaskRecord(id);

			if (Scheduler.IsAbandoned(id, calinfo.Interval))  // abandoned?
				Scheduler.UnlockId(id);                            // unlock

			if (Scheduler.IsLockedId(id))          // locked?
				return false;                       // skip

			if (AcquireLock(id) == false)            // can't lock?
				return false;                        // skip

			var now = System.DateTime.Now.ToUniversalTime();

			bool started;

			if (got_start_request)
			{
				logger.LogMsg("info", "Received start message from " + id, null);
				TwitterApi.SendTwitterDirectMessage(calinfo.twitter_account, "elmcity received your start message");
				started = true;
			}
			else
				started = Scheduler.MaybeStartTaskForId(now, calinfo);

			if (started == false)
				Scheduler.UnlockId(id);

			return started;
		}

		private static bool AcquireLock(string id)
		{
			var lock_response = Scheduler.LockId(id);

			if (lock_response.status != HttpStatusCode.Created)
			{
				logger.LogMsg("warning", "LockId", "expected to create lock but could not");
				return false;
			}
			else
				return true;
		}

		public static void UpdateFeedCountToAzure(string id)
		{
			logger.LogMsg("info", "UpdateFeedCountToAzure", null);
			try
			{
				var response = Delicious.FetchFeedCountForIdWithTags(id, CalendarAggregator.Configurator.delicious_trusted_ics_feed);

				if (response.outcome != Delicious.MetadataQueryOutcome.Success)
				{
					logger.LogMsg("warning", "FetchFeedCountForIdWithTags: " + id, response.outcome.ToString());
					return;
				}

				var count = response.int_response;

				if (feedcounts.ContainsKey(id) && feedcounts[id] != count)
				{
					logger.LogMsg("info", "feedcount changed for " + id, null);
					Scheduler.InitTaskForId(id);
				}

				feedcounts[id] = count;
			}
			catch (Exception e)
			{
				ts.WriteLogMessage("exception", "delicious.FetchFeedCountForIdWithTags", e.Message + e.StackTrace);
			}
		}

		private static void PurgeDeletedFeeds(FeedRegistry fr_delicious, string id)
		{
			logger.LogMsg("info", "PurgeDeletedFeeds", null);
			try
			{
				delicious.PurgeDeletedFeedsFromAzure(fr_delicious, id);
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "PurgeDeletedFeed", e.Message + e.StackTrace);
			}
		}

		public static void UpdateFeedsToAzure(FeedRegistry fr_delicious, string id)
		{
			logger.LogMsg("info", "UpdateFeedsToAzure", null);
			try
			{
				var dicts = delicious.StoreFeedsAndMaybeMetadataToAzure(fr_delicious, id);
				var has_no_nulls = dicts.Count > 0 && dicts.FindAll(x => x == null).Count == 0;
				if (has_no_nulls)  // don't save a snapshot if null or incomplete response from delicious
				{
					var saved = ObjectUtils.SavedJsonSnapshot(id, ObjectUtils.JsonSnapshotType.ListDictStr, "feeds", dicts);
					if (saved)
						GenUtils.PriorityLogMsg("info", "SavedJsonSnapshot for " + id, null);
				}
				else
				{
					GenUtils.PriorityLogMsg("warning", "StoreFeedsAndMaybeMetadataToAzure: " + id, "null or incomplete response");
				}
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "UpdateFeedsToAzure", e.Message + e.StackTrace);
			}
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

		public static void RenderHtmlXmlJson(string id, Calinfo calinfo)
		{
			var cr = new CalendarRenderer(calinfo);

			try
			{
				cr.SaveAsXml();
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "SaveAsXml: " + id, e.Message + e.StackTrace);
			}

			try
			{
				cr.SaveAsJson();
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "SaveAsJson: " + id, e.Message + e.StackTrace);
			}

			try
			{
				cr.SaveAsHtml();
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "SaveAsHtml: " + id, e.Message + e.StackTrace);
			}

		}

		public static void DoIcal(FeedRegistry fr, Calinfo calinfo)
		{
			if (testfeeds.Count > 0)
				foreach (var testfeed in testfeeds)
					fr.AddFeed(testfeed, "testing: " + testfeed);
			else
				fr.LoadFeedsFromAzure(FeedLoadOption.all);

			if ( Convert.ToInt32(calinfo.feed_count) != fr.feeds.Count ) // feeds were added or deleted
			{
				var new_feed_count = Utils.UpdateFeedCount(calinfo.id);
				calinfo.feed_count = new_feed_count.ToString();                // update this instance
			}

			var id = calinfo.id;

			ZonedEventStore ical = new ZonedEventStore(calinfo, ".ical");
			try
			{

				logger.LogMsg("info", "DoIcal: " + id, null);
				Collector coll = new Collector(calinfo, settings);
				coll.CollectIcal(fr, ical, test: testing, nosave: false);
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
				var eventful = new ZonedEventStore(calinfo, ".eventful");
				Collector coll = new Collector(calinfo, settings);
				coll.CollectEventful(eventful, testing);
			}
		}

		public static void DoUpcoming(Calinfo calinfo)
		{
			if (settings["use_upcoming"] == "true" && calinfo.upcoming)
			{
				var upcoming = new ZonedEventStore(calinfo, ".upcoming");
				Collector coll = new Collector(calinfo, settings);
				coll.CollectUpcoming(upcoming, testing);
			}
		}

		public static void DoEventBrite(Calinfo calinfo)
		{
			if (settings["use_eventbrite"] == "true" && calinfo.eventbrite)
			{
				var eventbrite = new ZonedEventStore(calinfo, ".eventbrite");
				Collector coll = new Collector(calinfo, settings);
				coll.CollectEventBrite(eventbrite, testing);
			}
		}

		public static void DoFacebook(Calinfo calinfo)
		{
			if (settings["use_facebook"] == "true" && calinfo.facebook)
			{
				var facebook = new ZonedEventStore(calinfo, ".facebook");
				Collector coll = new Collector(calinfo, settings);
				coll.CollectFacebook(facebook, testing);
			}
		}

		public static void SaveWhereStats(FeedRegistry fr, Calinfo calinfo)
		{
			var id = calinfo.id;
			logger.LogMsg("info", "SaveWhereStats: " + id, null);
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
			string report = "";

			report = MakeTableHeader(report);
			var futurecount = 0;
			futurecount += estats.eventcount;
			futurecount += ustats.eventcount;
			futurecount += ebstats.eventcount;

			foreach (var feedurl in fr.feeds.Keys)
			{
				StatsRow(id, istats, ref report, ref futurecount, feedurl);
			}

			report += "</table>\n";

			var events_per_person = Convert.ToInt32(futurecount) / (float)pop;
			string preamble = MakeWherePreamble(estats, ustats, ebstats, fbstats, pop, futurecount, events_per_person);
			report = preamble + report;
			report = Utils.EmbedHtmlSnippetInDefaultPageWrapper(calinfo, report, "stats");
			bs.PutBlob(id, id + ".stats.html", new Hashtable(), Encoding.UTF8.GetBytes(report), null);

			var dict = new Dictionary<string, object>();
			dict.Add("events", futurecount.ToString());
			dict.Add("events_per_person", string.Format("{0:f}", events_per_person));
			TableStorage.UpmergeDictToTableStore(dict, "metadata", id, id);
		}

		private static void StatsRow(string id, Dictionary<string, IcalStats> istats, ref string report, ref int futurecount, string feedurl)
		{
			var feed_metadict = Metadata.LoadFeedMetadataFromAzureTableForFeedurlAndId(feedurl, id);
			string homeurl;
			feed_metadict.TryGetValue("url", out homeurl);
			string redirected_url;
			feed_metadict.TryGetValue("redirected_url", out redirected_url);
			if (String.IsNullOrEmpty(redirected_url))
				redirected_url = feedurl;
			DoStatsRow(id, istats, ref report, ref futurecount, feedurl, redirected_url, homeurl);
		}

		public static void SaveWhatStats(FeedRegistry fr, Calinfo calinfo)
		{
			var id = calinfo.id;
			logger.LogMsg("info", "SaveWhatStats: " + id, null);
			Dictionary<string, IcalStats> istats = GetIcalStats(id);
			string report = "";
			report = MakeTableHeader(report);
			var futurecount = 0;
			foreach (var feedurl in istats.Keys)
			{
				StatsRow(id, istats, ref report, ref futurecount, feedurl);
			}
			report += "</table>\n";
			string preamble = MakeWhatPreamble(futurecount);
			report = preamble + report;
			report = Utils.EmbedHtmlSnippetInDefaultPageWrapper(calinfo, report, "stats");
			bs.PutBlob(id, id + ".stats.html", new Hashtable(), Encoding.UTF8.GetBytes(report), null);
		}

		private static string MakeTableHeader(string report)
		{
			report += string.Format(@"
<table class=""icalstats"">
<tr>
<td>feed</td>
<td>validation</td>
<td>future</td>
<td>single</td>
<td>recurring</td>
<td>instances</td>
<td>loaded</td>
<td>when</td>
<td>PRODID</td>
</tr>");
			return report;
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

		private static string MakeWherePreamble(NonIcalStats estats, NonIcalStats ustats, NonIcalStats ebstats, NonIcalStats fbstats, int pop, int futurecount, float events_per_person)
		{
			string preamble = string.Format(@"
<div>
<p>
Eventful: {0} venues, {1} events ({2})
</p>
<p>
Upcoming: {3} venues, {4} events ({5})
</p>
<p>
EventBrite: {6} events
</p>
<p>
Facebook: {7} events
</p>
<p>
All events {8}, population {9}, events/person {10:f}
</p>
",
					 estats.venuecount,
					 estats.eventcount,
					 estats.whenchecked.ToString(),
					 ustats.venuecount,
					 ustats.eventcount,
					 ustats.whenchecked.ToString(),
					 ebstats.eventcount,
					 fbstats.eventcount,
					 futurecount,
					 pop,
					 events_per_person
					 );
			return preamble;
		}

		private static void DoStatsRow(string id, Dictionary<string, IcalStats> istats, ref string report, ref int futurecount, string feedurl, string redirected_url, string homeurl)
		{
			try
			{
				var ical_stats = istats[feedurl];
				var is_private = Delicious.IsPrivateFeed(id, feedurl);

				var feed_column = is_private
					? String.Format(@"{0} (<a title=""click to visit calendar's home page"" href=""{1}"">home</a>)", ical_stats.source, homeurl)
					: String.Format(@"<a title=""click to load calendar"" href=""{0}"">{1}</a> (<a title=""click to visit calendar's home page"" href=""{2}"">home</a>)", feedurl, ical_stats.source, homeurl);
	
				var validation_column = is_private
					? ""
					: String.Format(@"<a href=""{0}"">validate</a>", Utils.ValidationUrlFromFeedUrl(redirected_url));
										
				futurecount += istats[feedurl].futurecount;
				report += string.Format(@"
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
</tr>
",
				feed_column,                                             // 0
				validation_column,                                       // 1
				ical_stats.futurecount,                                  // 2
				ical_stats.singlecount,                                  // 3
				ical_stats.recurringcount,                               // 4
				ical_stats.recurringinstancecount,                       // 5
				ical_stats.loaded,                                       // 6
				ical_stats.whenchecked,                                  // 7
				//ical_stats.dday_error,                                 // 
				ical_stats.prodid                                        // 8
				);
			}
			catch (Exception ex)
			{
				logger.LogMsg("exception", feedurl + "," + istats[feedurl].source + " :  stats not ready", ex.Message);
			}
		}

		public static void MergeIcs(Calinfo calinfo)
		{
			var id = calinfo.id;
			logger.LogMsg("info", "MergeIcs: " + id, null);
			List<string> suffixes = new List<string>() { "ical" };
			if (calinfo.hub_enum == CalendarAggregator.HubType.where)
				foreach (NonIcalType type in Enum.GetValues(typeof(CalendarAggregator.NonIcalType)))
				{
					if (Utils.UseNonIcalService(type, settings, calinfo) == true)
						suffixes.Add(type.ToString());
				}
			try
			{
				var metadict = Metadata.LoadMetadataForIdFromAzureTable(id);

				var all_ical = new iCalendar();
				Collector.AddTimezoneToDDayICal(all_ical, calinfo.tzinfo);
				foreach (var tz in all_ical.TimeZones)
					tz.TZID = calinfo.tzinfo.Id;

				foreach (var suffix in suffixes)  // todo: skip if disabled in settings
				{
					var url = MakeIcsUrl(id, suffix);
					try
					{
						var feedtext = HttpUtils.FetchUrl(url).DataAsString();
						var sr = new StringReader(feedtext);
						var ical = iCalendar.LoadFromStream(sr);
						foreach (var tz in ical.TimeZones)
							tz.TZID = calinfo.tzinfo.Id;
						all_ical.MergeWith(ical);
					}
					catch (Exception e)
					{
						GenUtils.PriorityLogMsg("warning", "MergeICS: " + url, e.Message);
					}
				}

				//var deduped = new iCalendar().iCalendar;
				//DedupeIcal(all_ical, deduped);
				//var serializer = new iCalendarSerializer(deduped);

				var serializer = new iCalendarSerializer(all_ical);
				var icsbytes = Encoding.UTF8.GetBytes(serializer.SerializeToString());
				bs.PutBlob(id, id + ".ics", new Hashtable(), icsbytes, "text/calendar");
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "MergeIcs: " + id, e.Message + e.StackTrace);
			}
		}

		private static void DedupeIcal(iCalendar all_ical, iCalendar deduped)
		{
			var dict = new Dictionary<string, DDay.iCal.Components.Event>();
			foreach (DDay.iCal.Components.Event evt in all_ical.Events)
			{
				var key = evt.Summary.ToString() + "_" + evt.DTStart.ToString();
				var val = evt;
				dict.Add(key, val);
			}
			foreach (var key in dict.Keys)
			{
				Collector.AddEventToDDayIcal(deduped, dict[key]);
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

		public static void MonitorAdmin(object o, ElapsedEventArgs args)
		{
			logger.LogMsg("info", "MonitorAdmin", null);
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
			GenUtils.PriorityLogMsg("info", "GeneralAdmin", null);

			try    // acquire new hubs added since last cycle
			{
				RecacheHubIdsToAzure();
			}
			catch (Exception e0)
			{
				GenUtils.PriorityLogMsg("exception", "GeneralAdmin.RecacheHubs", e0.Message);
			}

			var ids = Metadata.LoadHubIdsFromAzureTable();
			ids = MaybeAdjustIdsForTesting(ids);

			try  // fetch feeds and metadata from delicious, store in azure table, update metadata pages
			{
				foreach (var id in ids)
				{
					if ( Utils.ElmcityIdUsesTwitterAuth(id) == false ) // revert to old delicious-based mechanism
						UpdateFeedsAndMetadataForIdToAzure(id);
				}
			}
			catch (Exception e1)
			{
				GenUtils.PriorityLogMsg("exception", "GeneralAdmin", e1.Message);
			}

			WebRoleData wrd;

			try  // create WebRoleData structure and store as blob, available to webrole on next _reload
			{
				wrd = new WebRoleData(testing: false, test_id: null);
				bs.SerializeObjectToAzureBlob(wrd, "admin", "wrd.obj");
			}
			catch (Exception e3)
			{
				GenUtils.PriorityLogMsg("exception", "GeneralAdmin: creating wrd", e3.Message);
			}

		}

		public static void UpdateFeedsAndMetadataForIdToAzure(string id)
		{
			var fr_delicious = new FeedRegistry(id);
			fr_delicious.LoadFeedsFromDelicious();
			PurgeDeletedFeeds(fr_delicious, id);
			UpdateFeedsToAzure(fr_delicious, id);
			UpdateFeedCountToAzure(id);
			UpdateMetadataToAzure(id);
			Utils.MakeMetadataPage(id);
		}

		public static void TestRunnerAdmin(object o, ElapsedEventArgs args)
		{
			logger.LogMsg("info", "TestRunnerAdmin", null);
			var calinfo = new Calinfo(CalendarAggregator.Configurator.delicious_test_account);
			try
			{
				var failed = GenUtils.RunTests("CalendarAggregator");
				if (failed > 0)
					TwitterApi.SendTwitterDirectMessage(calinfo.twitter_account, failed + " tests failed: CalendarAggregator");

				failed = GenUtils.RunTests("ElmcityUtils");
				if (failed > 0)
					TwitterApi.SendTwitterDirectMessage(calinfo.twitter_account, failed + " tests failed: CalendarElmcityUtils");

				failed = GenUtils.RunTests("WorkerRole");
				if (failed > 0)
					TwitterApi.SendTwitterDirectMessage(calinfo.twitter_account, failed + " tests failed: WorkerRole");
			}
			catch (Exception e)
			{
				logger.LogMsg("exception", "TestRunnerAdmin", e.Message + e.StackTrace);
			}
		}

		public static void IronPythonAdmin(object o, ElapsedEventArgs e)
		{
			try
			{
				logger.LogMsg("info", "IronPythonAdmin", null);
				PythonUtils.RunIronPython(local_storage_path, CalendarAggregator.Configurator.iron_python_admin_script_url, new List<string>() { "", "", "" });

			}
			catch (Exception ex)
			{
				logger.LogMsg("exception", "IronPythonAdmin", ex.Message + ex.StackTrace);
			}
		}

		public static void HighFrequencyScript(object o, ElapsedEventArgs e)
		{
			try
			{
				logger.LogMsg("info", "HighFrequencyScript", null);
				PythonUtils.RunIronPython(local_storage_path, CalendarAggregator.Configurator.iron_python_run_script_url, new List<string>() { "", "", "" });

			}
			catch (Exception ex)
			{
				logger.LogMsg("exception", "HighFrequencyScript", ex.Message + ex.StackTrace);
			}
		}

		/*
		public override RoleStatus GetHealthStatus()
			{
			return RoleStatus.Healthy;
			}
		 */

	}
}

