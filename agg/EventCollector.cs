/* ********************************************************************************
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
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using DDay.iCal;
using DDay.iCal.Serialization.iCalendar;
using ElmcityUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CalendarAggregator
{

	public class Collector
	{
		private Calinfo calinfo;
		private Dictionary<string, string> settings;
		private Apikeys apikeys = new Apikeys();
		private string id;
		private BlobStorage bs;

		public bool mock_eventful { get; set; }
		public bool mock_upcoming { get; set; }
		public bool mock_eventbrite { get; set; }

		public enum RecurrenceType { Recurring, NonRecurring };

		//private Dictionary<string, string> metadict = new Dictionary<string, string>();

		private List<string> tags;

		private HashSet<string> eventbrite_tags = new HashSet<string>();

		private Dictionary<string, string> eventful_cat_map;
		private Dictionary<string, string> eventbrite_cat_map;

		public enum UpcomingSearchStyle { location, latlon };

		private TableStorage ts = TableStorage.MakeDefaultTableStorage();

		// one for each non-ical source
		private NonIcalStats estats; // eventful
		private NonIcalStats ustats; // upcoming
		private NonIcalStats ebstats; // eventbrite
		private NonIcalStats fbstats; // facebook

		// every source type is serialized to an intermediate ics file, e.g.:
		// http://elmcity.blob.core.windows.net/a2cal/a2cal_ical.ics
		// http://elmcity.blob.core.windows.net/a2cal/a2cal_upcoming.ics

		// why? just for convenience of running/managing the service, it's helpful for each phase of 
		// processing to yield an inspectable output

		// these types are later merged to create, e.g.:
		// http://elmcity.blob.core.windows.net/a2cal/a2cal.ics

		private iCalendar ical_ical;
		private iCalendar eventful_ical;
		private iCalendar upcoming_ical;
		private iCalendar eventbrite_ical;
		private iCalendar facebook_ical;

		// values used when running the worker in test mode
		int test_pagecount = 1;
		int test_feeds = 6;
		int test_pagesize = 10;

		// retry constants

		int wait_secs = 1;
		int max_retries = 10;
		TimeSpan timeout_secs = TimeSpan.FromSeconds(100);

		private const int eventbrite_page_size = 10;

		private List<string> eventful_tag_ids = new List<string>() { 
"music",
"learning_education",
"family_fun_kids",
"festivals_parades",
"movies_film",
"food",
"fundraisers",
"art",
"support",
"books",
"attractions",
"community",
"business",
"singles_social",
"schools_alumni",
"clubs_associations",
"outdoors_recreation",
"performing_arts",
"animals",
"politics_activism",
"sales",
"science",
"religion_spirituality",
"sports",
"technology"
};


		private ConcurrentDictionary<string, Dictionary<string, string>> per_feed_metadata_cache = new ConcurrentDictionary<string, Dictionary<string, string>>();

		// public methods used by worker to collect events from all source types
		public Collector(Calinfo calinfo, Dictionary<string, string> settings)
		{
			this.calinfo = calinfo;
			this.settings = settings;

			this.mock_eventful = false;
			this.mock_upcoming = false;
			this.mock_eventbrite = false;

			this.id = calinfo.id;
			this.bs = BlobStorage.MakeDefaultBlobStorage();

			// an instance of a DDay.iCal for each source type, used to collect intermediate ICS
			// results which are saved, then combined to produce a merged ICS, e.g.:
			// http://elmcity.blob.core.windows.net/a2cal/a2cal.ics
			this.ical_ical = NewCalendarWithTimezone();
			this.eventful_ical = NewCalendarWithTimezone();
			this.upcoming_ical = NewCalendarWithTimezone();
			this.eventbrite_ical = NewCalendarWithTimezone();
			this.facebook_ical = NewCalendarWithTimezone();

			this.estats = new NonIcalStats();
			this.estats.blobname = "eventful_stats";
			this.ustats = new NonIcalStats();
			this.ustats.blobname = "upcoming_stats";
			this.ebstats = new NonIcalStats();
			this.ebstats.blobname = "eventbrite_stats";
			this.fbstats = new NonIcalStats();
			this.fbstats.blobname = "facebook_stats";
		}

		private void MaybeLoadTags()
		{
			if (this.tags != null)
				return;

			try
			{
				eventbrite_cat_map = GenUtils.GetSettingsFromAzureTable("eventbritecatmap");
				eventful_cat_map = GenUtils.GetSettingsFromAzureTable("eventfulcatmap");
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "new Collector: cannot acquire cat maps", e.Message + e.StackTrace);
			}
			try
			{
				this.tags = Utils.GetTagsFromJson(this.id);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "new Collector: cannot acquire tags", e.Message + e.StackTrace);
			}
		}

		#region ical

		public void CollectIcal(FeedRegistry fr, ZonedEventStore es)
		{
			CollectIcal(fr, es, false, false);
		}

		public void CollectIcal(FeedRegistry fr, ZonedEventStore es, bool test, bool nosave)
		{
			this.MaybeLoadTags();

			using (ical_ical)
			{
				Dictionary<string, string> feeds = fr.feeds;
				DateTime utc_midnight_in_tz = Utils.MidnightInTz(this.calinfo.tzinfo).UniversalTime;

				// enforce the limit. necessary because processing of icalendar sources can involve
				// the unrolling of recurrence, and that can't go on forever
				DateTime then = utc_midnight_in_tz.AddDays(calinfo.icalendar_horizon_days);

				List<string> feedurls = test ? feeds.Keys.Take(test_feeds).ToList() : feeds.Keys.ToList();

				ParallelOptions options = new ParallelOptions();
				options.MaxDegreeOfParallelism = Convert.ToInt32(this.settings["max_feed_processing_parallelism"]);
				//options.MaxDegreeOfParallelism = 1; // for debugging

				List<string> hubs_to_skip_date_only_recurrence = new List<string>();

				try
				{
					hubs_to_skip_date_only_recurrence = settings["hubs_to_skip_date_only_recurrence"].Split(',').ToList();
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "hubs_to_skip_date_only_recurrence", e.Message);
				}

				GenUtils.LogMsg("info", id + " loading " + feedurls.Count() + " feeds", null);

				var results_dict = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

				try
				{
					//foreach ( var feedurl in feedurls )
					Parallel.ForEach(source: feedurls, parallelOptions: options, body: (feedurl) =>
					{
						var tid = System.Threading.Thread.CurrentThread.ManagedThreadId;
						GenUtils.LogMsg("info", string.Format("loading {0}, {1}, {2}", this.id, tid, feedurl), null);

						results_dict.TryAdd(feedurl, null);

						iCalendar ical = new DDay.iCal.iCalendar();
						string source_name = "source_name";
						List<DDay.iCal.Event> events_to_include = new List<DDay.iCal.Event>();
						Dictionary<DDay.iCal.Event, RecurrenceType> event_recurrence_types = new Dictionary<DDay.iCal.Event, RecurrenceType>();
						List<DDay.iCal.Event> uniques = new List<DDay.iCal.Event>();
						string feedtext = "";

						try
						{
							source_name = FeedSetup(fr, feeds, feedurl, tid, source_name);
						}
						catch (Exception e0)
						{
							try
							{
								HandleE0(results_dict, feedurl, tid, e0);
								return;  // http://stackoverflow.com/questions/3765038/is-there-an-equivalent-to-continue-in-a-parallel-foreach
							}
							catch (Exception e) { GenUtils.LogMsg("exception", "HandleE0", e.Message); }
						}

						try
						{
							feedtext = GetFeedTextFromFeedUrl(fr, this.calinfo, source_name, feedurl, this.wait_secs, this.max_retries, this.timeout_secs);
						}
						catch (Exception e1)  // exception while loading feed
						{
							try
							{
								HandleE1(results_dict, feedurl, tid, source_name, e1);
								return;
							}
							catch (Exception e) { GenUtils.LogMsg("exception", "HandleE1", e.Message); return; }
						}

						try
						{
							StringReader sr = new StringReader(feedtext);
							ical = (DDay.iCal.iCalendar)iCalendar.LoadFromStream(sr).FirstOrDefault().iCalendar;
						}
						catch (Exception e2)
						{
							try
							{
								HandleE2(fr, results_dict, feedurl, tid, source_name, e2);
								return;
							}
							catch (Exception e) { GenUtils.LogMsg("exception", "HandleE2", e.Message); return; }
						}

						if (ical == null || ical.Events.Count == 0)
						{
							try
							{
								HandleNoEvents(results_dict, feedurl, tid, source_name);
								return;
							}
							catch (Exception e) { GenUtils.LogMsg("exception", "HandleNoEvents", e.Message); return; }
						}

						try
						{
							var skip_date_only_recurrence = hubs_to_skip_date_only_recurrence.Exists(x => x == this.id);
							FeedGather(utc_midnight_in_tz, then, tid, ical, source_name, events_to_include, event_recurrence_types, skip_date_only_recurrence);
						}
						catch (Exception e3)
						{
							try
							{
								HandleE3(fr, results_dict, feedurl, tid, source_name, e3);
								return;
							}
							catch (Exception e) { GenUtils.LogMsg("exception", "HandleE3", e.Message); return; }
						}

						try
						{
							uniques = Utils.UniqueByTitleAndStart(events_to_include);
						}
						catch (Exception e4)
						{
							try
							{
								HandleE4(fr, results_dict, feedurl, tid, source_name, e4);
								return;
							}
							catch (Exception e) { GenUtils.LogMsg("exception", "HandleE4", e.Message); return; }
						}

						try
						{
							foreach (var unique in uniques)                      // add to eventstore
							{
								lock (es)
								{
									AddIcalEvent(unique, fr, es, feedurl, source_name);
								}
							}
						}
						catch (Exception e5)
						{
							try
							{
								HandleE5(fr, results_dict, feedurl, tid, source_name, e5);
								return;
							}
							catch (Exception e) { GenUtils.LogMsg("exception", "HandleE5", e.Message); return; }
						}

						try
						{
							FeedStats(fr, feedurl, event_recurrence_types, uniques);
						}
						catch (Exception e6)
						{
							try
							{
								HandleE6(results_dict, feedurl, tid, source_name, e6);
								return;
							}
							catch (Exception e) { GenUtils.LogMsg("exception", "HandleE6", e.Message); return; }
						}

						try
						{
							results_dict[feedurl] = "ok: " + source_name;
						}
						catch (Exception e) { GenUtils.LogMsg("exception", "update results_dict[feedurl]", e.Message); return; }

					});
				}
				catch (AggregateException agg_ex)
				{
					var inners = agg_ex.InnerExceptions;
					foreach (var inner in inners)
						GenUtils.LogMsg("exception", "CollectIcal: parallel inner exception: " + inner.Message, inner.StackTrace);
				}

				var json = JsonConvert.SerializeObject(results_dict);
				json = GenUtils.PrettifyJson(json);
				bs.PutBlob(id, "feed_processing_results.json", json);


				if (nosave == false) // why ever true? see CalendarRenderer.Viewer 
					SerializeStatsAndIntermediateOutputs(fr, es, ical_ical, new NonIcalStats(), SourceType.ical);
			}
		}

		private void HandleE6(ConcurrentDictionary<string, string> results_dict, string feedurl, int tid, string source_name, Exception e6)
		{
			var msg = String.Format("FeedStats {0}, {1}, {2}, {3}", id, tid, source_name, e6.Message);
			GenUtils.LogMsg("exception", msg, e6.StackTrace);
			results_dict[feedurl] = msg;
		}

		private void HandleE5(FeedRegistry fr, ConcurrentDictionary<string, string> results_dict, string feedurl, int tid, string source_name, Exception e5)
		{
			var msg = string.Format("exception adding events for {0} {1}, {2}, {3}", id, tid, source_name, e5.Message);
			GenUtils.PriorityLogMsg("exception", msg, e5.StackTrace + "," + e5.Data.ToString() + "," + e5.InnerException.Message);
			results_dict[feedurl] = msg;
			fr.stats[feedurl].dday_error += " | " + msg;
		}

		private void HandleE4(FeedRegistry fr, ConcurrentDictionary<string, string> results_dict, string feedurl, int tid, string source_name, Exception e4)
		{
			var msg = String.Format("exception deduplicating events for {0}, {1}, {2}, {3}", id, tid, source_name, e4.Message);
			GenUtils.PriorityLogMsg("exception", msg, e4.StackTrace);
			results_dict[feedurl] = msg;
			fr.stats[feedurl].dday_error += " | " + msg;
		}

		private void HandleE3(FeedRegistry fr, ConcurrentDictionary<string, string> results_dict, string feedurl, int tid, string source_name, Exception e3)
		{
			var msg = String.Format("exception gathering future events for {0}, {1}, {2}, {3}, {4}", id, tid, source_name, e3.Message, e3.InnerException.Message);
			GenUtils.PriorityLogMsg("exception", msg, e3.StackTrace);
			results_dict[feedurl] = msg;
			fr.stats[feedurl].dday_error += " | " + msg;
		}

		private void HandleNoEvents(ConcurrentDictionary<string, string> results_dict, string feedurl, int tid, string source_name)
		{
			var msg = String.Format("no events found for {0}, {1}, {2}", id, tid, source_name);
			GenUtils.LogMsg("warning", msg, null);
			results_dict[feedurl] = msg;
		}

		private void HandleE2(FeedRegistry fr, ConcurrentDictionary<string, string> results_dict, string feedurl, int tid, string source_name, Exception e2)
		{
			var msg = String.Format("exception loading calendar {0}, {1}, {2}, {3}", id, tid, source_name, e2.Message);
			GenUtils.LogMsg("exception", msg, e2.StackTrace);
			fr.stats[feedurl].dday_error = e2.Message;
			results_dict[feedurl] = msg;
		}

		private void HandleE1(ConcurrentDictionary<string, string> results_dict, string feedurl, int tid, string source_name, Exception e1)
		{
			var msg = String.Format("cannot retrieve feed for {0}, {1}, {2}, {3}", id, tid, source_name, e1.Message);
			GenUtils.LogMsg("warning", msg, e1.StackTrace);
			results_dict[feedurl] = msg;
		}

		private void HandleE0(ConcurrentDictionary<string, string> results_dict, string feedurl, int tid, Exception e0)
		{
			var msg = String.Format("FeedSetup {0}, {1}, {2}", id, tid, e0.Message);
			GenUtils.PriorityLogMsg("exception", msg, e0.StackTrace);
			results_dict[feedurl] = msg;
		}

		private static void FeedStats(FeedRegistry fr, string feedurl, Dictionary<DDay.iCal.Event, RecurrenceType> event_recurrence_types, List<DDay.iCal.Event> uniques)
		{
			HashSet<string> recurring_uids = new HashSet<string>();
			UpdateIcalStats(fr, feedurl, event_recurrence_types, uniques, recurring_uids);
			fr.stats[feedurl].recurringcount = recurring_uids.Count;    // count recurring events
		}

		private void FeedGather(DateTime utc_midnight_in_tz, DateTime then, int tid, iCalendar ical, string source_name, List<DDay.iCal.Event> events_to_include, Dictionary<DDay.iCal.Event, RecurrenceType> event_recurrence_types, bool skip_date_only_recurrence)
		{
			foreach (DDay.iCal.Event evt in ical.Events)             // gather future events
			{
				if (evt.Start == null)
				{
					var msg = String.Format("skipping {0}, {1}, {2}, {3}", id, tid, evt.Summary, source_name);
					GenUtils.PriorityLogMsg("warning", msg, null);
					continue;
				}

				if (evt.End == null || evt.End.Year == 1)
				{
					evt.End = new iCalDateTime(evt.Start.Add(TimeSpan.FromHours(1)));
				}

				IncludeFutureEvent(events_to_include, event_recurrence_types, evt, utc_midnight_in_tz, then, skip_date_only_recurrence);
			}
		}

		private string FeedSetup(FeedRegistry fr, Dictionary<string, string> feeds, string feedurl, int tid, string source_name)
		{
			source_name = feeds[feedurl];
			string load_msg = string.Format("loading {0}, {1}, {2}, {3}", this.id, tid, source_name, feedurl);
			GenUtils.LogMsg("info", load_msg, null);
			fr.stats[feedurl].whenchecked = DateTime.Now.ToUniversalTime();
			per_feed_metadata_cache[feedurl] = Metadata.LoadFeedMetadataFromAzureTableForFeedurlAndId(feedurl, id);
			return source_name;
		}

		private static void UpdateIcalStats(FeedRegistry fr, string feedurl, Dictionary<DDay.iCal.Event, RecurrenceType> event_recurrence_types, List<DDay.iCal.Event> uniques, HashSet<string> recurring_uids)
		{

			foreach (var unique in uniques)                       // count as single event or instance of recurring
			{
				fr.stats[feedurl].futurecount++;
				var recurrence_type = event_recurrence_types[unique];
				if (recurrence_type == RecurrenceType.Recurring)
				{
					fr.stats[feedurl].recurringinstancecount++;
					lock (recurring_uids)
					{
						recurring_uids.Add(unique.UID);
					}
				}
				else
					fr.stats[feedurl].singlecount++;
			}
		}

		/*  BEGIN:VEVENT
			ORGANIZER;CN=Luann Udell
			DTSTART:20110528T210000Z
			DTEND:20110528T230000Z
			UID:e216380751720294@facebook.com
			SUMMARY:Luann Udell Art Exhibit--Reception in Keene NH
			LOCATION:The Starving Artist\, 10 West Street
		 * 
		 * In this example from Luann's Facebook, this is the only event for which she appears in the ICS feed as ORGANIZER.
		 * If the ICS feed is tagged with facebook_organizer=Luann+Udell then this event will be included.
		 */

		private static List<DDay.iCal.Event> RestrictFacebookIcsToOrganizersPublicEvents(Dictionary<string, string> feed_metadict, List<DDay.iCal.Event> events_to_include)
		{
			var copy_of_events_to_include = new List<DDay.iCal.Event>(events_to_include);
			try
			{
				foreach (DDay.iCal.Event evt in events_to_include)
				{
					var meta_key = "facebook_organizer";
					if (feed_metadict.ContainsKey(meta_key))
					{
						var organizer = feed_metadict[meta_key];

						if (evt.Class.ToString() != "PUBLIC")
							continue;

						if (evt.Organizer.CommonName != null && evt.Organizer.CommonName.ToString() == organizer)
							continue;

						if (evt.Organizer != null && evt.Organizer.ToString() == organizer)
							continue;

						copy_of_events_to_include.Remove(evt);
					}
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "CollectIcal: facebook filter", e.Message);
			}

			return copy_of_events_to_include;
		}

		public static string GetFeedTextFromFeedUrl(FeedRegistry fr, Calinfo calinfo, string source, string feedurl, int wait_secs, int max_retries, TimeSpan timeout_secs)
		{
			if (feedurl.StartsWith("webcal:"))
				feedurl = feedurl.Replace("webcal:", "http:");

			if (feedurl.StartsWith("webcals:"))
				feedurl = feedurl.Replace("webcals:", "https:");

			HttpWebRequest request;

			try
			{
				request = (HttpWebRequest)WebRequest.Create(new Uri(feedurl));
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("warning", "GetFeedTextFromFeedUrl", "cannot use " + feedurl + " : " + e.Message);
				return String.Empty;
			}

			string feedtext = "";

			var response = HttpUtils.RetryHttpRequestExpectingStatus(request, HttpStatusCode.OK, data: null, wait_secs: wait_secs, max_tries: max_retries, timeout_secs: timeout_secs);
			if (response.status != HttpStatusCode.OK)
			{
				var msg = "could not fetch " + source;
				GenUtils.LogMsg("warning", msg, response.status.ToString());
				feedtext = Utils.TryGetCachedFeed(feedurl);
			}
			else
			{
				feedtext = response.DataAsString();
				Utils.SaveFeedToCache(feedurl, feedtext);
			}

			feedtext = MassageFeedText(calinfo, feedurl, feedtext);

			if (fr != null)
				EnsureProdId(fr, feedurl, feedtext);

			return feedtext;
		}

		public static string MassageFeedText(Calinfo calinfo, string feedurl, string feedtext)
		{
			// because not needed, and dday.ical doesn't allow legal (but very old) dates
			feedtext = GenUtils.RegexReplace(feedtext, "\nCREATED:[^\n]+", "");

			// because DDay.iCal can't parse ATTENDEE
			feedtext = Utils.RemoveComponent(feedtext, "ATTENDEE");

			// because of issue with http://www.brattleborology.com/calendar/
			feedtext = Utils.RemoveComponent(feedtext, "ORGANIZER");

			// because of issues with events.umich.edu
			feedtext = Utils.FixMiswrappedComponent(feedtext, "SUMMARY");

			// because of property-name-folding issue https://github.com/dougrday/icalvalid/issues/10
			feedtext = Unfold(feedtext);

			// handle non-standard X_WR_TIMEZONE if usersetting asked
			if (calinfo.use_x_wr_timezone)
				feedtext = Utils.Handle_X_WR_TIMEZONE(feedtext);

			if (feedurl.Contains("berkeley.edu"))  // todo: remove when this reported bug is fixed
			{
				feedtext = Utils.RemoveLine(feedtext, " *No event on these dates");
				feedtext = Utils.WrapMiswrappedUID(feedtext);
				feedtext = Utils.TrimLine(feedtext, "*No event on these dates");
			}
			return feedtext;
		}

		public static string Unfold(string s)
		{
			s = s.Replace("\r", "");
			s = s.Replace("\n", "_NEWLINE_");
			var re = new Regex(@"_NEWLINE_[\s]+");
			s = re.Replace(s, "");
			s = s.Replace("_NEWLINE_", "\n");
			return s;
		}

		// a feed without a PRODID property is actually invalid, but some homegrown feeds
		// don't include it, try giving benefit of doubt
		private static void EnsureProdId(FeedRegistry fr, string feedurl, string feedtext)
		{
			try
			{
				fr.stats[feedurl].prodid = GenUtils.RegexFindGroups(feedtext, "PRODID:(.+)")[1];
			}
			catch
			{
				fr.stats[feedurl].prodid = "unknown";
			}
		}

		private void IncludeFutureEvent(List<DDay.iCal.Event> events_to_include, Dictionary<DDay.iCal.Event, RecurrenceType> event_recurrence_types, DDay.iCal.Event evt, DateTime midnight_in_tz, DateTime then, bool skip_date_only)
		{
			try
			{
				var occurrences = evt.GetOccurrences(midnight_in_tz, then);
				foreach (Occurrence occurrence in occurrences)
				{
					try
					{
						var recurrence_type = occurrence.Source.RecurrenceRules.Count == 0 ? RecurrenceType.NonRecurring : RecurrenceType.Recurring;

						if (recurrence_type == RecurrenceType.Recurring && skip_date_only && evt.DTStart.HasTime == false) // https://github.com/dougrday/icalvalid/issues/7 and 8
							continue;

						if (IsCurrentOrFutureDTStartInTz(occurrence.Period.StartTime.UTC))
						{
							var instance = PeriodizeRecurringEvent(evt, occurrence.Period);
							events_to_include.Add(instance);
							event_recurrence_types.AddOrUpdateDictionary<DDay.iCal.Event, Collector.RecurrenceType>(instance, recurrence_type);
						}
					}
					catch (Exception e)
					{
						try
						{
							GenUtils.PriorityLogMsg("exception", "IncludeFutureEvent (per occurrence)", evt.Summary + ", " + e.Message + e.StackTrace);
						}
						catch { continue; }
					}
				}
			}

			catch (Exception e)
			{
				try
				{
					GenUtils.PriorityLogMsg("exception", "IncludeFutureEvent (GetOccurrences) detail: " + evt.Summary, e.Message + e.StackTrace);
				}
				catch { return; }
			}
		}

		// clone the DDay.iCal event, update dtstart (and maybe dtend) with Year/Month/Day for this occurrence
		private DDay.iCal.Event PeriodizeRecurringEvent(DDay.iCal.Event evt, IPeriod period)
		{
			var kind = evt.Start.IsUniversalTime ? DateTimeKind.Utc : DateTimeKind.Local;

			var dtstart = new DateTime(
				period.StartTime.Year,
				period.StartTime.Month,
				period.StartTime.Day,
				evt.Start.Hour,
				evt.Start.Minute,
				evt.Start.Second,
				kind);

			var idtstart = new iCalDateTime(dtstart);

			var idtend = default(iCalDateTime);
			DateTime dtend = default(DateTime);

			if (evt.DTEnd != null && evt.DTEnd.Year != 1 )
			{
				dtend = new DateTime(
					period.EndTime.Year,
					period.EndTime.Month,
					period.EndTime.Day,
					evt.End.Hour,
					evt.End.Minute,
					evt.End.Second,
					kind);

				idtend = new iCalDateTime(dtend);
			}

			var instance = new DDay.iCal.Event();
			instance.Start = idtstart;
			instance.End = idtend;
			instance.Summary = evt.Summary;
			instance.Description = evt.Description;
			foreach (var cat in evt.Categories)
				instance.Categories.Add(cat);
			instance.Location = evt.Location;
			instance.GeographicLocation = evt.GeographicLocation;
			instance.UID = evt.UID;
			instance.Url = evt.Url;
			return instance;
		}

		// save the intermediate ics file for the source type represented in ical
		private BlobStorageResponse SerializeIcalEventsToIcs(iCalendar ical, SourceType type)
		{
			var serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer();
			var ics_text = serializer.SerializeToString(ical);
			var ics_bytes = Encoding.UTF8.GetBytes(ics_text);
			var containername = this.id;
			return bs.PutBlob(containername, containername + "_" + type.ToString() + ".ics", new Hashtable(), ics_bytes, "text/calendar");
		}

		// put the event into a) the eventstore, and b) the per-type intermediate icalendar object
		private void AddIcalEvent(DDay.iCal.Event evt, FeedRegistry fr, ZonedEventStore es, string feedurl, string source)
		{
			try
			{
				evt = NormalizeIcalEvt(evt, feedurl);

				DateTimeWithZone dtstart;
				DateTimeWithZone dtend;
				var tzinfo = this.calinfo.tzinfo;

				//dtstart = Utils.DtWithZoneFromICalDateTime(evt.Start.Value, tzinfo);
				//dtend = (evt.DTEnd == null) ? new Utils.DateTimeWithZone(DateTime.MinValue, tzinfo) : Utils.DtWithZoneFromICalDateTime(evt.End.Value, tzinfo);

				//dtstart = new Utils.DateTimeWithZone(evt.Start.Value,tzinfo);
				//dtend = new Utils.DateTimeWithZone(evt.End.Value,tzinfo);

				var localstart = evt.DTStart.IsUniversalTime ? TimeZoneInfo.ConvertTimeFromUtc(evt.Start.UTC, tzinfo) : evt.Start.Local;
				dtstart = new DateTimeWithZone(localstart, tzinfo);

				var localend = evt.DTEnd.IsUniversalTime ? TimeZoneInfo.ConvertTimeFromUtc(evt.End.UTC, tzinfo) : evt.End.Local;
				dtend = new DateTimeWithZone(localend, tzinfo);

				MakeGeo(this, evt, this.calinfo.lat, this.calinfo.lon);

				string categories = null;
				if (evt.Categories != null && evt.Categories.Count() > 0)
					categories = string.Join(",", evt.Categories.ToList().Select(cat => cat.ToString().ToLower()));

				string description = this.calinfo.has_descriptions ? evt.Description : null;

				string location = this.calinfo.has_locations ? evt.Location : null;

				es.AddEvent(title: evt.Summary, url: evt.Url.ToString(), source: source, dtstart: dtstart, dtend: dtend, lat: this.calinfo.lat, lon: this.calinfo.lon, allday: evt.IsAllDay, categories: categories, description: description, location: location);

				var evt_tmp = MakeTmpEvt(this, dtstart: dtstart, dtend: dtend, tzinfo: this.calinfo.tzinfo, tzid: this.calinfo.tzinfo.Id, title: evt.Summary, url: evt.Url.ToString(), location: evt.Location, description: source, lat: this.calinfo.lat, lon: this.calinfo.lon, allday: evt.IsAllDay);
				AddEventToDDayIcal(ical_ical, evt_tmp);

				fr.stats[feedurl].loaded++;
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "AddIcalEvent", source + ": " + e.Message + ": " + evt.Summary);
			}
		}

		private static void MakeGeo(Collector collector, DDay.iCal.Event evt, string lat, string lon)
		{
			if (String.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lon))
				return;

			if (collector == null ||                                    // called from outside the class, e.g. IcsFromRssPlusXcal
				collector.calinfo.hub_enum == HubType.where) // called from inside the class
			{
				if (evt.GeographicLocation == null)           // override with hub's location
				{
					try
					{
						if (lat == null)                // e.g., because called from IcsFromRssPlusXcal
							lat = collector.calinfo.lat;

						if (lon == null)
							lon = collector.calinfo.lon;

						evt.GeographicLocation = new GeographicLocation();
						try
						{
							evt.GeographicLocation.Latitude = Double.Parse(lat);
							evt.GeographicLocation.Longitude = Double.Parse(lon);
						}
						catch (Exception e)
						{
							GenUtils.LogMsg("warning", "MakeGeo cannot parse " + lat + "," + lon, e.Message);
						}
					}
					catch (Exception e)
					{
						GenUtils.PriorityLogMsg("exception", "AddIcalEvent: " + collector.id + " cannot make evt.Geo", e.Message + evt.Summary.ToString());
					}
				}
			}
		}

		// normalize url, description, location, category properties
		private DDay.iCal.Event NormalizeIcalEvt(DDay.iCal.Event evt, string feedurl)
		{
			try
			{
				if (evt.Description == null) evt.Description = "";
				if (evt.Location == null) evt.Location = "";

				if (evt.Summary.StartsWith("Event: ")) // zvents does this, it's annoying
					evt.Summary = evt.Summary.Replace("Event: ", "");

				var feed_metadict = GetFeedMetadictWithCaching(feedurl);
				var metadata_from_description = Utils.GetMetadataFromDescription(Configurator.ical_description_metakeys, evt.Description);
				SetUrl(evt, feed_metadict, metadata_from_description);

				// evt.Categories.Clear(); // not needed now that we are scoping tags down to the active taxonomy
				
				SetCategories(evt, feed_metadict, metadata_from_description);

				var re = new Regex("<.*?>", RegexOptions.Compiled); // strip html tags from description
				evt.Description = re.Replace(evt.Description, String.Empty);
				evt.Summary = re.Replace(evt.Summary, String.Empty);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", this.id + ": NormalizeIcalEvent", e.Message + e.StackTrace);
			}

			return evt;

		}

		private void SetCategories(DDay.iCal.Event evt, Dictionary<string, string> feed_metadict, Dictionary<string, string> metadata_from_description)
		{

			var list = evt.Categories.ToList();

			list = ExpandCats(list);
			
			// foreach (var cat in evt.Categories)                         // restrict to active taxonomy -- but now for now
			//	list = list.RemoveUnlessFound(cat.ToLower(), this.tags);
			
			evt.Categories.Clear();
			foreach (var cat in list)
			{
				var c = cat.ToLower();
				if (c.StartsWith("http:"))								// lose bogus categories
					continue;
				if (this.tags.Exists(x => x == c))                      // if matches a tag in the active taxonomy
					evt.Categories.Add(c);								// use unmodified
				else
					evt.Categories.Add("{" + c + "}");                  // else mark as contributor-provided 
			}

			if (feed_metadict.ContainsKey("category"))				// apply feed-level categories from feed metadata
			{
				var cat_string = feed_metadict["category"];
				AddCategoriesFromCatString(evt, cat_string);
			}

			if (metadata_from_description.ContainsKey("category"))			// apply event-level categories from Description
			{
				var cat_string = metadata_from_description["category"];
				AddCategoriesFromCatString(evt, cat_string);
			}

			list = evt.Categories.ToList();   // sort
			list.Sort(String.CompareOrdinal);
			evt.Categories.Clear();
			foreach (var cat in list)
				evt.Categories.Add(cat);
		}

		private List<string> ExpandCats(List<string> list)
		{
			var expanded_list = new List<string>();
			foreach (var cat in list)
			{
				var items = cat.Split(',');
				foreach (var item in items)
				{
					var c = item;
					c = c.Replace(" and", "").Replace("and ", "");
					c = c.Trim();
					expanded_list.Add(c);
				}
			}
			return expanded_list;
		}

		public static void AddCategoriesFromCatString(DDay.iCal.Event evt, string cats)
		{
			try
			{
				var catlist = cats.Split(',');
				foreach (var cat in catlist)
					evt.Categories.Add(cat.Trim());
			}
			catch
			{
				GenUtils.PriorityLogMsg("exception", "AddCategoriesFromCatString: " + evt.Summary + ": " + cats, null);
			}
		}

		private Dictionary<string, string> GetFeedMetadictWithCaching(string feedurl)
		{
			var feed_metadict = new Dictionary<string, string>();
			if (per_feed_metadata_cache.ContainsKey(feedurl))
				feed_metadict = per_feed_metadata_cache[feedurl];
			else
			{
				try
				{
					feed_metadict = Metadata.LoadFeedMetadataFromAzureTableForFeedurlAndId(feedurl, this.id);
					per_feed_metadata_cache[feedurl] = feed_metadict;
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "NormalizeIcalEvt", e.Message + e.StackTrace);
				}
			}
			return feed_metadict;
		}

		private static void SetUrl(DDay.iCal.Event evt, Dictionary<string, string> feed_metadict, Dictionary<string, string> metadata_from_description)
		{
			if (EventUrlPropertyIsHttp(evt))  // use the URL property if it exists and is http:
				return;

			if (feed_metadict.ContainsKey("url"))  // use the feed metadata's URL if it exists
			{
				try
				{
					evt.Url = new Uri(feed_metadict["url"]);
				}
				catch
				{
					GenUtils.LogMsg("warning", "SetUrl: no url for " + feed_metadict["source"], null);
				}
			}

			/*
			if (DescriptionStartsWithUrl(evt)) // override with event's Description if URL-like
			{
				evt.Url = new Uri(evt.Description.ToString());
			}*/

			if (LocationStartsWithUrl(evt))   // override with the event's Location if URL-like
			{
				evt.Url = new Uri(evt.Location.ToString());
			}

			if (metadata_from_description.ContainsKey("url")) // override with event's url=URL if it exists
			{
				evt.Url = new Uri(metadata_from_description["url"]);
			}

			if (evt.Url == null)
				evt.Url = new Uri("http://unspecified");	// finally this

		}

		private static bool EventUrlPropertyIsHttp(DDay.iCal.Event evt)
		{
			string url = (evt.Url == null) ? null : evt.Url.ToString();
			return !String.IsNullOrEmpty(url) && url.StartsWith("http:"); // URL:message:%3C001401cbb263$05c84af0$1158e0d0$@net%3E doesn't qualify
		}

		private static bool DescriptionStartsWithUrl(DDay.iCal.Event evt)
		{
			string description = evt.Description.ToString();
			return !String.IsNullOrEmpty(description) && Utils.StartsWithUrl(description) != null;
		}

		private static bool LocationStartsWithUrl(DDay.iCal.Event evt)
		{
			string location = evt.Location.ToString();
			return !String.IsNullOrEmpty(location) && Utils.StartsWithUrl(location) != null;
		}

		// add VTIMEZONE to intermediate or final ics outputs
		public static void AddTimezoneToDDayICal(DDay.iCal.iCalendar ical, TimeZoneInfo tzinfo)
		{
			var timezone = DDay.iCal.iCalTimeZone.FromSystemTimeZone(tzinfo);

			//timezone.TZID = tzinfo.Id; // not being set in DDay.iCal 0.8 for some reason

			/*
			 *  Interesting situation if the source calendar says, e.g., America/Chicago, but
			 *  the OS says, e.g., Central. In that case, DDay's UTC property method will fail to match
			 *  the names and it will fall back to the OS conversion:
			 *  
			 *  value = DateTime.SpecifyKind(Value, DateTimeKind.Local).ToUniversalTime();   
			 *  
			 *  This happens when:
			 *    - A recurring event has a start and end
			 *    - DDay substracts DTEnd.UTC - DTStart.UTC
			 *    
			 *  Todo: Recheck all this when upgraded to DDay 1.0
			 */

			if (timezone.TimeZoneInfos.Count == 0)
			{
				var dday_tzinfo_standard = new DDay.iCal.iCalTimeZoneInfo();
				dday_tzinfo_standard.Name = "STANDARD";
				dday_tzinfo_standard.TimeZoneName = tzinfo.StandardName;
				//dday_tzinfo_standard.Start.Date = new DateTime(1970, 1, 1);
				var utcOffset = tzinfo.BaseUtcOffset;
				dday_tzinfo_standard.TZOffsetFrom = new DDay.iCal.UTCOffset(utcOffset);
				dday_tzinfo_standard.TZOffsetTo = new DDay.iCal.UTCOffset(utcOffset);
				// Add the "standard" time rule to the time zone
				timezone.AddChild(dday_tzinfo_standard);
			}

			ical.AddChild(timezone);

		}

		#endregion ical

		#region eventful

		public void CollectEventful(ZonedEventStore es, bool test)
		{
			this.MaybeLoadTags();

			using (eventful_ical)
			{
				string location = this.calinfo.where;
				var page_size = test ? test_pagesize : 100;
				string method = "events/search";

				var uniques = new Dictionary<string, XElement>(); // dedupe by title + start

				if (this.mock_eventful)							// normally get tags from compiled object
					this.tags = new List<string>() { "music" }; // but for testing, just use one that will pass the filter

				foreach (var tag in this.tags)        // do tagwise search of eventful
				{
					var eventful_tag_id = GetEventfulTagId(tag, eventful_cat_map);   // map its taxonomy to ours
					if (!eventful_tag_ids.Exists(x => x == eventful_tag_id))         // skip if no match
						continue;

					var msg = string.Format("{0}: loading eventful events for {1} ", this.id, tag);
					GenUtils.LogMsg("info", msg, null);

					string args = MakeEventfulArgs(location, page_size, eventful_tag_id);
					var xdoc = CallEventfulApi(method, args);
					var str_page_count = XmlUtils.GetXeltValue(xdoc.Root, ElmcityUtils.Configurator.no_ns, "page_count");
					int page_count = test ? test_pagecount : Convert.ToInt16(str_page_count);

					foreach (XElement evt in EventfulIterator(page_count, args, "events/search", "event"))
					{
						evt.Add(new XElement("category", tag));
						var lat = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "latitude");
						var lon = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "longitude");

						if (lat != null && lon != null)  // check if eventful's radius filter failed us
						{
							var distance = Utils.GeoCodeCalc.CalcDistance(lat, lon, this.calinfo.lat, this.calinfo.lon);
							if (distance > this.calinfo.radius)   // if so
								continue;                         // skip this one
						}

						var ns = ElmcityUtils.Configurator.no_ns;

						uniques.AddOrUpdateDictionary<string, XElement>(
							XmlUtils.GetXeltValue(evt, ns, "title") +
							XmlUtils.GetXeltValue(evt, ns, "start_time") +
							XmlUtils.GetXeltValue(evt, ns, "category"),
						  evt);
					}
				}

				Dictionary<string, int> event_count_by_venue = new Dictionary<string, int>();
				int event_num = 0;

				foreach (XElement evt in uniques.Values)
				{
					event_num += 1;
					if (event_num > Configurator.eventful_max_events)
						break;

					var ns = ElmcityUtils.Configurator.no_ns;
					var title = XmlUtils.GetXeltValue(evt, ns, "title");
					var start_time = XmlUtils.GetXeltValue(evt, ns, "start_time");
					var venue_name = XmlUtils.GetXeltValue(evt, ns, "venue_name");
					var url = XmlUtils.GetXeltValue(evt, ns, "url");

					IncrementEventCountByVenue(event_count_by_venue, venue_name);
					AddEventfulEvent(es, venue_name, evt);
				}

				estats.venuecount = event_count_by_venue.Keys.Count;
				estats.whenchecked = DateTime.Now.ToUniversalTime();

				SerializeStatsAndIntermediateOutputs(es, eventful_ical, estats, SourceType.eventful);

				//bs.PutBlob(this.id, SourceType.eventful.ToString() + "_cats.txt", String.Join(",", this.eventful_tags.ToList()));

			}

		}

		public string MakeEventfulArgs(string location, int page_size, string category)
		{
			var now = Utils.MidnightInTz(this.calinfo.tzinfo).LocalTime;
			string fmt = "{0:yyyyMMdd}00";
			string min_date = String.Format(fmt, now);
			string max_date = MakeDateArg(fmt, now, this.calinfo.population);
			string daterange = min_date + "-" + max_date;
			string args = string.Format("date={0}&location={1}&within={2}&units=mi&page_size={3}&category={4}", daterange, this.calinfo.lat + "," + this.calinfo.lon, this.calinfo.radius, page_size, category);
			return args;
		}

		public void AddEventfulEvent(ZonedEventStore es, string venue_name, XElement evt)
		{
			var str_dtstart = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "start_time");
			DateTime dtstart = Utils.LocalDateTimeFromLocalDateStr(str_dtstart);
			var dtstart_with_tz = new DateTimeWithZone(dtstart, this.calinfo.tzinfo);

			if (dtstart_with_tz.UniversalTime < Utils.MidnightInTz(this.calinfo.tzinfo).UniversalTime)
				return;

			var no_ns = ElmcityUtils.Configurator.no_ns;

			var event_id = evt.Attribute("id").Value;
			var event_owner = XmlUtils.GetXeltValue(evt, no_ns, "owner");
			var title = XmlUtils.GetXeltValue(evt, no_ns, "title");
			var venue_url = XmlUtils.GetXeltValue(evt, no_ns, "venue_url");
			var all_day = XmlUtils.GetXeltValue(evt, no_ns, "all_day") == "1";
			var venue_address = XmlUtils.GetXeltValue(evt, no_ns, "venue_address");

			string lat = this.calinfo.lat;   // default to hub lat/lon
			string lon = this.calinfo.lon;

			lat = XmlUtils.GetXeltValue(evt, no_ns, "latitude");
			lon = XmlUtils.GetXeltValue(evt, no_ns, "longitude");

			if (lat == null || lon == null)
			{
				GenUtils.LogMsg("warning", "AddEventfulEvent", "no lat/lon");
			}

			var category = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "category");
			if (String.IsNullOrWhiteSpace(category))
				category = SourceType.eventful.ToString();
			else
				category = SourceType.eventful.ToString() + "," + category;

			string event_url = "http://eventful.com/events/" + event_id;

			string source = venue_name;

			if (String.IsNullOrEmpty(source))
				source = "Unnamed Eventful Venue";
			string location;
			if (!String.IsNullOrEmpty(source))
			{
				location = venue_name;
				if (!String.IsNullOrEmpty(venue_address))
					location += ", " + venue_address;
			}
			else
			{
				location = event_url;
			}

			estats.eventcount++;

			var evt_tmp = MakeTmpEvt(this, dtstart_with_tz, DateTimeWithZone.MinValue(this.calinfo.tzinfo), this.calinfo.tzinfo, this.calinfo.tzinfo.Id, title, url: event_url, location: location, description: source, lat: lat, lon: lon, allday: all_day);
			AddEventToDDayIcal(eventful_ical, evt_tmp);

			var min = DateTimeWithZone.MinValue(this.calinfo.tzinfo);

			// suppress description to minimize amount of eventful info carried in the event packet
			// allow location but it is subject to the per-hub locations setting (calinfo.has_locations)
			es.AddEvent(title: title, url: event_url, source: source, dtstart: dtstart_with_tz, dtend: min, lat: lat, lon: lon, allday: all_day, categories: category, description: null, location: location);
		}

		public IEnumerable<XElement> EventfulIterator(int page_count, string args, string method, string element)
		{
			for (int i = 0; i < page_count; i++)
			{
				string this_args = string.Format("{0}&page_number={1}", args, i + 1);
				XDocument xdoc = CallEventfulApi(method, this_args);
				IEnumerable<XElement> query = from events in xdoc.Descendants(element) select events;
				foreach (XElement xelt in query)
					yield return xelt;
			}
		}

		public XDocument CallEventfulApi(string method, string args)
		{
			XDocument xdoc = new XDocument();
			if (this.mock_eventful)
			{
				var uri = BlobStorage.MakeAzureBlobUri("admin", "summitscience.xml", false);
				var r = HttpUtils.FetchUrl(uri);
				xdoc = XmlUtils.XdocFromXmlBytes(r.bytes);
			}
			else
			{
				HttpResponse response = default(HttpResponse);
				try
				{
					var key = this.apikeys.eventful_api_key;
					string host = "http://api.eventful.com/rest";
					string url = string.Format("{0}/{1}?app_key={2}&{3}", host, method, key, args);
					var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
					response = HttpUtils.RetryHttpRequestExpectingStatus(request, HttpStatusCode.OK, data: null, wait_secs: 3, max_tries: 10, timeout_secs: TimeSpan.FromSeconds(60));
					xdoc = XmlUtils.XdocFromXmlBytes(response.bytes);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "CallEventfulApi", response.status + "," + response.message + "," + e.Message);
				}
			}
			return xdoc;
		}

		#endregion eventful

		#region upcoming

		public void CollectUpcoming(ZonedEventStore es, bool test)
		{
			using (upcoming_ical)
			{
				var page_size = test ? test_pagesize : 100;
				var args = MakeUpcomingApiArgs(UpcomingSearchStyle.location);
				var method = "event.search";
				var xdoc = CallUpcomingApi(method, args);
				int page_count = 1;
				try
				{
					var result_count = GetUpcomingResultCount(xdoc);

					if (result_count == 0) // try the other search style (upcoming seems flaky that way)
					{
						args = MakeUpcomingApiArgs(UpcomingSearchStyle.latlon);
						xdoc = CallUpcomingApi(method, args);
						GetUpcomingResultCount(xdoc);
					}

					page_count = result_count / page_size;
				}
				catch
				{
					GenUtils.LogMsg("warning", "CollectUpcoming", "resultcount unavailable");
					return;
				}

				if (test == true && page_count > test_pagecount) page_count = test_pagecount;
				if (page_count == 0) page_count = 1;

				var msg = string.Format("{0}: loading {1} upcoming events", this.id, page_count * page_size);
				GenUtils.LogMsg("info", msg, null);

				Dictionary<string, int> event_count_by_venue = new Dictionary<string, int>();
				int event_num = 0;

				var uniques = new Dictionary<string, XElement>();  // dedupe by name + start
				foreach (var evt in UpcomingIterator(page_count, method))
					uniques.AddOrUpdateDictionary<string, XElement>(evt.Attribute("name").ToString() + evt.Attribute("start_date").ToString(), evt);

				foreach (XElement evt in uniques.Values)
				{
					event_num += 1;
					if (event_num > Configurator.upcoming_max_events)
						break;

					var dtstart = DateTimeWithZoneFromUpcomingXEvent(evt);

					if (dtstart.UniversalTime < Utils.MidnightInTz(this.calinfo.tzinfo).UniversalTime)
						continue;

					var venue_name = evt.Attribute("venue_name").Value;
					IncrementEventCountByVenue(event_count_by_venue, venue_name);

					AddUpcomingEvent(es, venue_name, evt);
				}

				ustats.venuecount = event_count_by_venue.Keys.Count;
				ustats.whenchecked = DateTime.Now.ToUniversalTime();

				SerializeStatsAndIntermediateOutputs(es, upcoming_ical, ustats, SourceType.upcoming);
			}
		}

		private DateTimeWithZone DateTimeWithZoneFromUpcomingXEvent(XElement evt)
		{
			bool all_day = false;
			return DateTimeWithZoneFromUpcomingXEvent(evt, ref all_day);
		}

		private DateTimeWithZone DateTimeWithZoneFromUpcomingXEvent(XElement evt, ref bool allday)
		{
			string str_date = "";
			string str_time = "";
			try
			{
				str_date = evt.Attribute("start_date").Value;
				str_time = evt.Attribute("start_time").Value;
				if (str_time == "")
				{
					str_time = "00:00:00";
					allday = true;
				}
				var str_dtstart = str_date + " " + str_time;
				var _dtstart = Utils.LocalDateTimeFromLocalDateStr(str_dtstart);
				var dtstart = new DateTimeWithZone(_dtstart, this.calinfo.tzinfo);
				return dtstart;
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "DateTimeWithZoneFromUpcomingXEvent: " + string.Format("date[{0}] time[{1}]", str_date, str_time), e.Message + e.StackTrace);
				return new DateTimeWithZone(DateTime.MinValue, this.calinfo.tzinfo);
			}
		}

		public static int GetUpcomingResultCount(XDocument xdoc)
		{
			var str_result_count = xdoc.Document.Root.Attribute("resultcount").Value;
			return Convert.ToInt32(str_result_count);
		}

		public string MakeUpcomingApiArgs(UpcomingSearchStyle search_style)
		{
			string fmt = "{0:yyyy-MM-dd}";
			var now = Utils.MidnightInTz(this.calinfo.tzinfo).LocalTime;
			var min_date = string.Format(fmt, now);
			var max_date = MakeDateArg(fmt, now, this.calinfo.population);

			string location_arg;
			if (search_style == UpcomingSearchStyle.latlon)
				location_arg = String.Format("{0},{1}", this.calinfo.lat, this.calinfo.lon);
			else
				location_arg = String.Format("{0}", this.calinfo.where);

			return string.Format("location={0}&radius={1}&min_date={2}&max_date={3}", location_arg, this.calinfo.radius, min_date, max_date);
		}

		public string MakeDateArg(string fmt, DateTime now, int population)
		{
			string date_arg = String.Format(fmt, now + TimeSpan.FromDays(90));
			if (population > 250000)
				date_arg = String.Format(fmt, now + TimeSpan.FromDays(60));
			if (population > 300000)
				date_arg = String.Format(fmt, now + TimeSpan.FromDays(60));
			return date_arg;
		}

		public void AddUpcomingEvent(ZonedEventStore es, string venue_name, XElement evt)
		{
			var title = evt.Attribute("name").Value;
			var event_url = "http://upcoming.yahoo.com/event/" + evt.Attribute("id").Value;
			var source = venue_name;
			var venue_id = evt.Attribute("venue_id").Value;
			var venue_url = "http://upcoming.yahoo.com/venue/" + venue_id;

			string lat = this.calinfo.lat;  // default to hub's lat/lon
			string lon = this.calinfo.lon;

			try
			{
				lat = evt.Attribute("latitude").Value;
				lon = evt.Attribute("longitude").Value;
			}
			catch
			{
				GenUtils.LogMsg("warning", "AddUpcomingEvent", "cannot parse lat/lon");
			}

			string categories = "upcoming";

			ustats.eventcount++;

			bool all_day = false;
			var dtstart = DateTimeWithZoneFromUpcomingXEvent(evt, ref all_day);

			string location = venue_name;
			string venue_address = evt.Attribute("venue_address").Value;
			if (!String.IsNullOrEmpty(venue_address))
				location += ", " + venue_address;

			var evt_tmp = MakeTmpEvt(this, dtstart, DateTimeWithZone.MinValue(this.calinfo.tzinfo), this.calinfo.tzinfo, this.calinfo.tzinfo.Id, title, url: event_url, location: location, description: source, lat: lat, lon: lon, allday: all_day);
			AddEventToDDayIcal(upcoming_ical, evt_tmp);

			var min = DateTimeWithZone.MinValue(this.calinfo.tzinfo);

			es.AddEvent(title: title, url: event_url, source: source, dtstart: dtstart, dtend: min, lat: lat, lon: lon, allday: all_day, categories: categories, description: null, location: location);
		}

		public IEnumerable<XElement> UpcomingIterator(int page_count, string method)
		{
			for (int i = 1; i <= page_count; i++)
			{
				var this_args = string.Format("{0}&page={1}", MakeUpcomingApiArgs(UpcomingSearchStyle.location), i);
				var xdoc = CallUpcomingApi(method, this_args);
				if (GetUpcomingResultCount(xdoc) == 0)
				{
					this_args = string.Format("{0}&page={1}", MakeUpcomingApiArgs(UpcomingSearchStyle.latlon), i); // try other way
					xdoc = CallUpcomingApi(method, this_args);
				}
				foreach (XElement evt in xdoc.Descendants("event"))
					yield return evt;
			}
		}

		public XDocument CallUpcomingApi(string method, string args)
		{
			XDocument xdoc = new XDocument();
			if (this.mock_upcoming)
			{
				var uri = BlobStorage.MakeAzureBlobUri("admin", "philharmonia.xml", false);
				var r = HttpUtils.FetchUrl(uri);
				xdoc = XmlUtils.XdocFromXmlBytes(r.bytes);
			}
			else
			{
				var response = default(HttpResponse);
				try
				{
					var key = this.apikeys.upcoming_api_key;
					string host = "http://upcoming.yahooapis.com/services/rest/";
					string url = string.Format("{0}?rollup=none&api_key={1}&method={2}&{3}", host, key, method, args);
					var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
					response = HttpUtils.RetryHttpRequestExpectingStatus(request, HttpStatusCode.OK, data: null, wait_secs: this.wait_secs, max_tries: this.max_retries, timeout_secs: this.timeout_secs);
					xdoc = XmlUtils.XdocFromXmlBytes(response.bytes);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "CallUpcomingApi", response.status + "," + response.message + "," + e.Message + e.StackTrace);
				}
			}
			return xdoc;
		}

		#endregion upcoming

		#region eventbrite

		public void CollectEventBrite(ZonedEventStore es)
		{
			this.MaybeLoadTags();

			using (eventbrite_ical)
			{
				string method = "event_search";
				string args = MakeEventBriteArgs();

				int page_count = GetEventBritePageCount(method, args);

				var msg = string.Format("{0}: found about {1} pages of eventbrite events", this.id, page_count * eventbrite_page_size);
				GenUtils.LogMsg("info", msg, null);

				int event_num = 0;

				foreach (XElement evt in EventBriteIterator(page_count, method, args))
				{
					if (event_num > Configurator.eventbrite_max_events)
						break;
					var dtstart_with_tz = Utils.ExtractEventBriteDateTime(evt, this.calinfo.tzinfo, "start_date");
					var dtend_with_tz = Utils.ExtractEventBriteDateTime(evt, this.calinfo.tzinfo, "end_date");

					if (dtstart_with_tz.UniversalTime < Utils.MidnightInTz(this.calinfo.tzinfo).UniversalTime)
						continue;

					AddEventBriteEvent(es, evt, dtstart_with_tz, dtend_with_tz);
				}
				ebstats.whenchecked = DateTime.Now.ToUniversalTime();

				SerializeStatsAndIntermediateOutputs(es, eventbrite_ical, ebstats, SourceType.eventbrite);

				bs.PutBlob(this.id, SourceType.eventbrite.ToString() + "_cats.txt", String.Join(",", this.eventbrite_tags.ToList()));
			}
		}

		public int GetEventBritePageCount(string method, string args)
		{
			var xdoc = CallEventBriteApi(method, args);
			int page_count = 1;
			try
			{
				var str_result_count = xdoc.Descendants("total_items").FirstOrDefault().Value;
				int result_count = Convert.ToInt32(str_result_count);
				page_count = ( result_count / eventbrite_page_size ) + 1;
			}
			catch
			{
				GenUtils.LogMsg("info", "CollectEventBrite", "resultcount unavailable");
			}
			return page_count;
		}

		public string MakeEventBriteArgs()
		{
			return MakeEventBriteArgs(1, null);
		}

		public string MakeEventBriteArgs(int radius_multiplier, string organizer)
		{
			var now = Utils.MidnightInTz(this.calinfo.tzinfo).LocalTime;
			string fmt = "{0:yyyy-MM-dd}";
			var min_date = string.Format(fmt, now);
			string max_date = MakeDateArg(fmt, now, calinfo.population);
			var date = min_date + ' ' + max_date;
			string args = string.Format("latitude={0}&longitude={1}&within={2}&date={3}", this.calinfo.lat, this.calinfo.lon, this.calinfo.radius * radius_multiplier, date);
			if (organizer != null)
				args += "&organizer=" + organizer;
			return args;
		}

		public void AddEventBriteEvent(ZonedEventStore es, XElement evt, DateTimeWithZone dtstart, DateTimeWithZone dtend)
		{
			string title;
			string event_url;
			string source;
			bool all_day;
			string categories;
			var evt_tmp = MakeDDayEventFromEventBriteEvent(evt, out title, out event_url, out source, out all_day, out categories);

			AddEventToDDayIcal(eventbrite_ical, evt_tmp);

			var min = DateTimeWithZone.MinValue(this.calinfo.tzinfo);

			// todo: dig out location from http://developer.eventbrite.com/doc/events/event_search/

			es.AddEvent(title: title, url: event_url, source: source, dtstart: dtstart, dtend: dtend, lat: null, lon: null, allday: all_day, categories: categories, description: null, location: null);
		}

		private DDay.iCal.Event MakeDDayEventFromEventBriteEvent(XElement evt, out string title, out string event_url, out string source, out bool all_day, out string categories)
		{
			title = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "title");
			event_url = evt.Element(ElmcityUtils.Configurator.no_ns + "url").Value;
			source = SourceType.eventbrite.ToString();

			var start_dt_with_zone = Utils.ExtractEventBriteDateTime(evt, this.calinfo.tzinfo, "start_date");
			var end_dt_with_zone = Utils.ExtractEventBriteDateTime(evt, this.calinfo.tzinfo, "end_date");

			all_day = false;

			var cat_str = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "category");
			categories = IntersectCategories(cat_str, source, this.eventbrite_tags, this.eventbrite_cat_map);

			ebstats.eventcount++;

			return MakeTmpEvt(this, start_dt_with_zone, end_dt_with_zone, this.calinfo.tzinfo, this.calinfo.tzinfo.Id, title, url: event_url, location: event_url, description: source, allday: all_day, lat: this.calinfo.lat, lon: this.calinfo.lon);
		}

		private string GetEventfulTagId(string tag, Dictionary<string, string> cat_map)
		{
			{
				if (cat_map.ContainsKey(tag))
					return cat_map[tag];
				else
					return tag;
			}
		}

		private string IntersectCategories(string cat_str, string source, HashSet<string> tag_set, Dictionary<string, string> cat_map)
		{
			string categories = source;
			if (String.IsNullOrEmpty(cat_str))
				return categories;

			List<string> cats = cat_str.Split(',').ToList();
			cats = cats.Select(x => x.Trim()).ToList();
			List<string> mapped_cats = new List<string>();
			MapCats(cats, tag_set, cat_map, mapped_cats);
			var intersection = mapped_cats.Intersect(this.tags);
			if (intersection.Count() > 0)
				categories += "," + String.Join(",", intersection);
			return categories;
		}

		private static void MapCats(List<string> cats, HashSet<string> tag_set, Dictionary<string, string> cat_map, List<string> mapped_cats)
		{
			foreach (var cat in cats)
			{
				tag_set.Add(cat);
				if (cat_map.Keys.ToList().Exists(x => x == cat))
					mapped_cats.Add(cat_map[cat]);
				else
					mapped_cats.Add(cat);
			}
		}

		public IEnumerable<XElement> EventBriteIterator(int page_count, string method, string args)
		{
			for (int i = 1; i <= page_count; i++)
			{
				var this_args = string.Format("{0}&page={1}", args, i);
				var xdoc = CallEventBriteApi(method, this_args);
				foreach (XElement evt in xdoc.Descendants("event"))
					yield return evt;
			}
		}

		public XDocument CallEventBriteApi(string method, string args)
		{
			var xdoc = new XDocument();
			var response = default(HttpResponse);
			if (this.mock_eventbrite)
			{
				var uri = BlobStorage.MakeAzureBlobUri("admin", "eventbrite.xml", false);
				var r = HttpUtils.FetchUrl(uri);
				xdoc = XmlUtils.XdocFromXmlBytes(r.bytes);
			}
			else
			{
				try
				{
					var key = this.apikeys.eventbrite_api_key;
					string host = "https://www.eventbrite.com/xml";
					string url = string.Format("{0}/{1}?app_key={2}&{3}", host, method, key, args);
					//GenUtils.LogMsg("info", url, null);
					var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
					//var str_data = HttpUtils.DoHttpWebRequest(request, data: null).DataAsString();
					response = HttpUtils.RetryHttpRequestExpectingStatus(request, HttpStatusCode.OK, data: null, wait_secs: this.wait_secs, max_tries: this.max_retries, timeout_secs: this.timeout_secs);
					var str_data = response.DataAsString();
					str_data = GenUtils.RegexReplace(str_data, "<description>[^<]+</description>", "");
					byte[] bytes = Encoding.UTF8.GetBytes(str_data);
					xdoc = XmlUtils.XdocFromXmlBytes(bytes);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "CallEventBriteApi", response.status + "," + response.message + "," + e.Message + e.StackTrace);
				}
			}

			return xdoc;
		}

		#endregion

		#region facebook

		public void CollectFacebook(ZonedEventStore es, bool test)
		{
			using (facebook_ical)
			{
				var args = string.Format("q={0}&since=yesterday&limit=1000", this.calinfo.where);
				var method = "search";

				var msg = string.Format("{0}: loading facebook events", this.id);
				GenUtils.LogMsg("info", msg, null);

				var uniques = new Dictionary<string, FacebookEvent>();  // dedupe by title + start
				foreach (var fb_event in FacebookIterator(method, args))
					uniques.AddOrUpdateDictionary<string, FacebookEvent>(fb_event.name + fb_event.dt.ToString(), fb_event);

				foreach (FacebookEvent fb_event in uniques.Values)
				{
					var dtstart_with_zone = new DateTimeWithZone(fb_event.dt, this.calinfo.tzinfo);

					if (dtstart_with_zone.UniversalTime < Utils.MidnightInTz(this.calinfo.tzinfo).UniversalTime)
						continue;

					AddFacebookEvent(es, fb_event, dtstart_with_zone);
				}

				fbstats.whenchecked = DateTime.Now.ToUniversalTime();

				SerializeStatsAndIntermediateOutputs(es, facebook_ical, fbstats, SourceType.facebook);
			}
		}

		public void AddFacebookEvent(ZonedEventStore es, FacebookEvent fb_event, DateTimeWithZone dtstart)
		{
			var title = fb_event.name;
			var event_url = "http://www.facebook.com/event.php?eid=" + fb_event.id;
			var source = "facebook";
			var location = fb_event.location;

			var all_day = false;

			fbstats.eventcount++;

			var evt_tmp = MakeTmpEvt(this, dtstart, DateTimeWithZone.MinValue(this.calinfo.tzinfo), this.calinfo.tzinfo, this.calinfo.tzinfo.Id, title, url: event_url, location: location, description: source, lat: this.calinfo.lat, lon: this.calinfo.lon, allday: all_day);
			AddEventToDDayIcal(facebook_ical, evt_tmp);

			var min = DateTimeWithZone.MinValue(this.calinfo.tzinfo);

			es.AddEvent(title: title, url: event_url, source: source, dtstart: dtstart, dtend: min, lat: null, lon: null, allday: all_day, categories: "facebook", description: null, location: location);
		}

		public IEnumerable<FacebookEvent> FacebookIterator(string method, string args)
		{
			var json = CallFacebookApi(method, args);
			var j_obj = (JObject)JsonConvert.DeserializeObject(json);
			var events = Utils.UnpackFacebookEventsFromJson(j_obj);
			foreach (var evt in events)
				yield return evt;
		}

		public string CallFacebookApi(string method, string args)
		{
			var response = default(HttpResponse);
			try
			{
				var key = this.apikeys.facebook_api_key;
				string host = "https://graph.facebook.com";
				string url = string.Format("{0}/{1}?access_token={2}&type=event&{3}", host, method, key, args);
				GenUtils.LogMsg("info", url, null);
				var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
				response = HttpUtils.RetryHttpRequestExpectingStatus(request, HttpStatusCode.OK, data: null, wait_secs: this.wait_secs, max_tries: this.max_retries, timeout_secs: this.timeout_secs);
				return response.DataAsString();
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "CallFacebookApi", response.status + "," + response.message + "," + e.Message + e.StackTrace);
				return "";
			}
		}

		#endregion

		private iCalendar NewCalendarWithTimezone()
		{
			var ical = new iCalendar();
			AddTimezoneToDDayICal(ical, this.calinfo.tzinfo);
			return ical;
		}

		public static void AddEventToDDayIcal(iCalendar ical, DDay.iCal.Event evt)
		{
			var ical_evt = new DDay.iCal.Event();
			ical_evt.Categories = evt.Categories;
			ical_evt.Summary = evt.Summary;
			ical_evt.Url = evt.Url;
			ical_evt.Location = evt.Location;
			ical_evt.Description = evt.Description;
			ical_evt.Start = evt.Start;
			if (evt.DTEnd != null && evt.DTEnd.Year != 1)
				ical_evt.End = evt.DTEnd;
			ical_evt.IsAllDay = evt.IsAllDay;
			ical_evt.UID = Event.MakeEventUid(ical_evt);
			ical_evt.GeographicLocation = evt.GeographicLocation;
			ical.Events.Add(ical_evt);
		}

		public static DDay.iCal.Event MakeTmpEvt(Collector collector, DateTimeWithZone dtstart, DateTimeWithZone dtend, TimeZoneInfo tzinfo, string tzid, string title, string url, string location, string description, string lat, string lon, bool allday)
		{
			DDay.iCal.Event evt = new DDay.iCal.Event();  // for export to the intermediate ics file
			evt.Summary = title;
			evt.Url = new Uri(url);
			if (location != null)
				evt.Location = location;
			if (description != null)
				evt.Description = description;
			else
				evt.Description = url;
			MakeGeo(collector, evt, lat, lon);
			evt.Start = new iCalDateTime(dtstart.LocalTime);               // always local because the final ics file will use vtimezone
			evt.Start.TZID = tzid;
			if (!dtend.Equals(DateTimeWithZone.MinValue(tzinfo)))
			{
				evt.End = new iCalDateTime(dtend.LocalTime);
				evt.End.TZID = tzid;
			}
			evt.IsAllDay = allday;
			evt.UID = Event.MakeEventUid(evt);
			return evt;
		}

		private static void IncrementEventCountByVenue(Dictionary<string, int> event_count_by_venue, string venue_name)
		{
			if (event_count_by_venue.ContainsKey(venue_name))
				event_count_by_venue[venue_name]++;
			else
				event_count_by_venue.Add(venue_name, 1);
		}

		public bool IsCurrentOrFutureDTStartInTz(iCalDateTime ical_dtstart)
		{
			var utc_last_midnight = Utils.MidnightInTz(this.calinfo.tzinfo);
			return ical_dtstart.UTC >= utc_last_midnight.UniversalTime;
		}

		public static NonIcalStats DeserializeEventAndVenueStatsFromJson(string containername, string filename)
		{
			return Utils.DeserializeObjectFromJson<NonIcalStats>(containername, filename);
		}

		private void SerializeStatsAndIntermediateOutputs(FeedRegistry fr, EventStore es, iCalendar ical, NonIcalStats stats, SourceType type)
		{
			BlobStorageResponse bsr;
			HttpResponse tsr;

			if (BlobStorage.ExistsContainer(this.id) == false)
				bs.CreateContainer(this.id, is_public: true, headers: new Hashtable());

			if (type == SourceType.ical)
			{
				bsr = fr.SerializeIcalStatsToJson();
				GenUtils.LogMsg("info", this.id + ": SerializeIcalStatsToJson: " + stats.blobname, bsr.HttpResponse.status.ToString());
				tsr = fr.SaveStatsToAzure();
				GenUtils.LogMsg("info", this.id + ": FeedRegistry.SaveStatsToAzure: " + stats.blobname, tsr.status.ToString());
			}
			else
			{

				bsr = Utils.SerializeObjectToJson(stats, this.id, stats.blobname + ".json");
				GenUtils.LogMsg("info", this.id + ": Collector: SerializeObjectToJson: " + stats.blobname + ".json", bsr.HttpResponse.status.ToString());
				tsr = this.SaveStatsToAzure(type);
				GenUtils.LogMsg("info", this.id + ": Collector: SaveStatsToAzure", tsr.status.ToString());

			}

			bsr = this.SerializeIcalEventsToIcs(ical, type);
			GenUtils.LogMsg("info", this.id + ": SerializeIcalStatsToIcs: " + id + "_" + type.ToString() + ".ics", bsr.HttpResponse.status.ToString());

			bsr = es.Serialize();
			GenUtils.LogMsg("info", this.id + ": EventStore.Serialize: " + es.objfile, bsr.HttpResponse.status.ToString());
		}

		private void SerializeStatsAndIntermediateOutputs(EventStore es, iCalendar ical, NonIcalStats stats, SourceType type)
		{
			SerializeStatsAndIntermediateOutputs(new FeedRegistry(this.id), es, ical, stats, type);
		}

		private HttpResponse SaveStatsToAzure(SourceType type)
		{
			var entity = new Dictionary<string, object>();
			entity["PartitionKey"] = entity["RowKey"] = this.id;
			switch (type)
			{
				case SourceType.facebook:
					entity["facebook_events"] = this.fbstats.eventcount;
					break;
				case SourceType.upcoming:
					entity["upcoming_events"] = this.ustats.eventcount;
					break;
				case SourceType.eventful:
					entity["eventful_events"] = this.estats.eventcount;
					break;
				case SourceType.eventbrite:
					entity["eventbrite_events"] = this.ebstats.eventcount;
					break;
			}
			return ts.MergeEntity("metadata", this.id, this.id, entity).http_response;
		}
	}

	// encapsulates stats for non-ical sources
	public class NonIcalStats
	{
		public int eventcount
		{
			get { return _eventcount; }
			set { _eventcount = value; }
		}
		private int _eventcount;

		public int venuecount
		{
			get { return _venuecount; }
			set { _venuecount = value; }
		}
		private int _venuecount;

		public DateTime whenchecked
		{
			get { return _whenchecked; }
			set { _whenchecked = value; }
		}
		private DateTime _whenchecked;

		public string blobname
		{
			get { return _blobname; }
			set { _blobname = value; }
		}
		private string _blobname;

	}

	// encapsulates the json object that's returned by FacebookIterator. 
	public class FacebookEvent
	{
		public string name;
		public string location;
		public DateTime dt;
		public string id;

		// contrast with the Eventful|Upcoming|Eventbrite iterators, which use XElements coming from the XML APIs
		// of those services.

		// all the iterators are "IEnumerable-of-T" -- that is, they iterate over types. since XElement is a type
		// there was no need to encapsulate it for the other iterators. 

		// given that what comes back from Facebook's json API is a Dictionary<string,object>, the iterator could be:

		// public IEnumerable<Dictonary<string,object>> FacebookIterator(string method, string args)

		// but instead, it is:

		// public IEnumerable<FacebookEvent> FacebookIterator(string method, string args)

		// cost: the effort of defining and using a FacebookEvent object
		// benefit: the object is self-documenting, no need to remember or look up dictionary keys

		// note: the same cost/benefit would apply to the other iterators. doing things both ways
		// here just because you can, and because the tradeoffs in this case seem like a wash
		public FacebookEvent(string name, string location, DateTime dt, string id)
		{
			this.name = name;
			this.location = location;
			this.dt = dt;
			this.id = id;
		}
	}
}

