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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DDay.iCal;
using DDay.iCal.Components;
using DDay.iCal.DataTypes;
using DDay.iCal.Serialization;
using ElmcityUtils;

namespace CalendarAggregator
{
	public class Collector
	{
		private Calinfo calinfo;
		private Dictionary<string, string> settings;
		private Apikeys apikeys = new Apikeys();
		private string id;
		private BlobStorage bs;
		private Delicious delicious;

		public enum RecurrenceType { Recurring, NonRecurring };

		private Dictionary<string, string> metadict = new Dictionary<string, string>();
		private int population;

		private enum EventFlavor { ical, eventful, upcoming, eventbrite, facebook };
		private enum UpcomingSearchStyle { location, latlon };

		private TableStorage ts = TableStorage.MakeDefaultTableStorage();

		// one for each non-ical source
		private NonIcalStats estats; // eventful
		private NonIcalStats ustats; // upcoming
		private NonIcalStats ebstats; // eventbrite
		private NonIcalStats fbstats; // facebook

		// every source flavor is serialized to an intermediate ics file, e.g.:
		// http://elmcity.blob.core.windows.net/a2cal/a2cal_ical.ics
		// http://elmcity.blob.core.windows.net/a2cal/a2cal_upcoming.ics

		// why? just for convenience of running/managing the service, it's helpful for each phase of 
		// processing to yield an inspectable output

		// these flavors are later merged to create, e.g.:
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

		private Dictionary<string, Dictionary<string, string>> per_feed_metadata_cache;

		// public methods used by worker to collect events from all source flavors
		public Collector(Calinfo calinfo, Dictionary<string,string> settings)
		{
			this.calinfo = calinfo;
			this.settings = settings;
			this.id = calinfo.delicious_account;
			this.bs = BlobStorage.MakeDefaultBlobStorage();
			this.delicious = Delicious.MakeDefaultDelicious();

			// an instance of a DDay.iCal for each source flavor, used to collect intermediate ICS
			// results which are saved, then combined to produce a merged ICS, e.g.:
			// http://elmcity.blob.core.windows.net/a2cal/a2cal.ics
			this.ical_ical = NewCalendarWithTimezone();
			this.eventful_ical = NewCalendarWithTimezone();
			this.upcoming_ical = NewCalendarWithTimezone();
			this.eventbrite_ical = NewCalendarWithTimezone();
			this.facebook_ical = NewCalendarWithTimezone();

			// cache the metadata for this hub
			this.metadict = delicious.LoadMetadataForIdFromAzureTable(this.id);

			this.population = this.metadict.ContainsKey("population") ? Convert.ToInt32(this.calinfo.metadict["population"]) : 0;

			this.estats = new NonIcalStats();
			this.estats.blobname = "eventful_stats";
			this.ustats = new NonIcalStats();
			this.ustats.blobname = "upcoming_stats";
			this.ebstats = new NonIcalStats();
			this.ebstats.blobname = "eventbrite_stats";
			this.fbstats = new NonIcalStats();
			this.fbstats.blobname = "facebook_stats";
		}

		#region ical

		public void CollectIcal(FeedRegistry fr, ZonedEventStore es, bool test, bool nosave)
		{
			using (ical_ical)
			{
				Dictionary<string, string> feeds = fr.feeds;

				DateTime utc_midnight_in_tz = Utils.MidnightInTz(this.calinfo.tzinfo).UniversalTime;

				// enforce the limit. necessary because processing of icalendar sources can involve
				// the unrolling of recurrence, and that can't go on forever

				DateTime then = utc_midnight_in_tz.AddDays(Configurator.icalendar_horizon_in_days);

				var feedurls = test ? feeds.Keys.ToList().Take(test_feeds) : feeds.Keys;

				ParallelOptions options = new ParallelOptions();
				//options.MaxDegreeOfParallelism = 1;  // use this for debugging

				Parallel.ForEach(source: feedurls, parallelOptions: options, body: (feedurl, loop_state) =>
				//foreach (string feedurl in feedurls)
				{
					try
					{
						per_feed_metadata_cache = new Dictionary<string, Dictionary<string, string>>();

						string source = feeds[feedurl];

						string load_msg = string.Format("loading {0}: {1} ({2})", id, source, feedurl);
						GenUtils.LogMsg("info", load_msg, null);

						fr.stats[feedurl].whenchecked = DateTime.Now.ToUniversalTime();

						iCalendar ical;

						var feed_metadict = delicious.LoadFeedMetadataFromAzureTableForFeedurlAndId(feedurl, this.calinfo.delicious_account);
						var _feedurl = MaybeRedirect(feedurl, feed_metadict);
						    
						MaybeValidate(fr, feedurl, _feedurl);

						var feedtext = "";

						try
						{
							feedtext = GetFeedTextFromRedirectedFeedUrl(fr, source, feedurl, _feedurl);
						}
						catch
						{
							var msg = String.Format("{0}: {1} cannot retrieve feed", id, source);
							GenUtils.LogMsg("warning", msg, null);
						    //continue;
							return; // http://stackoverflow.com/questions/3765038/is-there-an-equivalent-to-continue-in-a-parallel-foreach
						}

						StringReader sr = new StringReader(feedtext);

						try
						{
							ical = iCalendar.LoadFromStream(sr);

							var events_to_include = new List<DDay.iCal.Components.Event>();
							var event_recurrence_types = new Dictionary<DDay.iCal.Components.Event, RecurrenceType>();

							if (ical == null || ical.Events.Count == 0)
							{
								var msg = String.Format("{0}: no events found for {1}", id, source);
								GenUtils.LogMsg("warning", msg, null);
								//continue;
								return;
							}

							var ical_tmp = NewCalendarWithTimezone();
			 
							foreach (DDay.iCal.Components.Event evt in ical.Events)             // gather future events
								IncludeFutureEvent(events_to_include, event_recurrence_types, evt, utc_midnight_in_tz, then, ical_tmp);

							var titles = events_to_include.Select(evt => evt.Summary.ToString()).OrderBy(title => title);

							var uniques = new Dictionary<string, DDay.iCal.Components.Event>(); // dedupe by summary + start
							foreach (var evt in events_to_include)
							{
								var key = evt.Summary.ToString() + evt.DTStart.ToString();
								uniques.AddOrUpdateDDayEvent(key, evt);
							}

							HashSet<string> recurring_uids = new HashSet<string>();

							foreach (var unique in uniques.Values)                       // count as single event or instance of recurring
							{
								fr.stats[feedurl].futurecount++;
								var recurrence_type = event_recurrence_types[unique];
								if (recurrence_type == RecurrenceType.Recurring)
								{
									fr.stats[feedurl].recurringinstancecount++;
									recurring_uids.Add(unique.UID);
								}
								else
									fr.stats[feedurl].singlecount++;
							}

							fr.stats[feedurl].recurringcount = recurring_uids.Count;    // count recurring events

							foreach (var unique in uniques.Values)                      // add to eventstore
								AddIcalEvent(unique, fr, es, feedurl, source);
						}
						catch (Exception e)
						{
							GenUtils.PriorityLogMsg("exception", "CollectIcal: " + id, e.Message);
							fr.stats[feedurl].dday_error = e.Message;
						}
					}

					catch (Exception e)
					{
						GenUtils.PriorityLogMsg("exception", feedurl, e.Message);
					}

				}

				); 

				if (nosave == false) // why ever true? see CalendarRenderer.Viewer 
					SerializeStatsAndIntermediateOutputs(fr, es, ical_ical, new NonIcalStats(), EventFlavor.ical);
			}
		}

		private static void MaybeValidate(FeedRegistry fr, string feedurl, string _feedurl)
		{
			if (Configurator.do_ical_validation)
			{
				try
				{
					var score = Utils.DDay_Validate(_feedurl);
					var rounded_score = fr.stats[feedurl].score = Double.Parse(score).ToString("00");
					GenUtils.LogMsg("info", "DDay_Validate: " + score, null);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "DDay_Validate: " + e.Message, _feedurl);
				}
			}
		}

		private TableStorageResponse StoreRedirectedUrl(string feedurl, string redirected_url, Dictionary<string, string> feed_metadict)
		{
			string rowkey = Utils.MakeSafeRowkeyFromUrl(feedurl);
			feed_metadict["redirected_url"] = redirected_url;
			return TableStorage.UpdateDictToTableStore(ObjectUtils.DictStrToDictObj(feed_metadict), Delicious.ts_table, this.id, rowkey);
		}

		public string MaybeRedirect(string feedurl, Dictionary<string, string> feed_metadict)
		{

			// allow the "fusecal" service to hook in if it can
			var _feedurl = MaybeRedirectFeedUrl(feedurl, feed_metadict);

			// allow ics_from_xcal to hook in if it can
			_feedurl = MaybeXcalToIcsFeedUrl(_feedurl, feed_metadict);

			// allow ics_from_vcal to hook in if it can
			_feedurl = MaybeVcalToIcsFeedUrl(_feedurl, feed_metadict);

			if (_feedurl != feedurl)
				StoreRedirectedUrl(feedurl, _feedurl, feed_metadict);

			return _feedurl;
		}

		private string GetFeedTextFromRedirectedFeedUrl(FeedRegistry fr, string source, string feedurl, string _feedurl)
		{
			var request = (HttpWebRequest)WebRequest.Create(new Uri(_feedurl));
			var response = HttpUtils.RetryExpectingOK(request, data: null, wait_secs: this.wait_secs, max_tries: this.max_retries, timeout_secs: this.timeout_secs);

			string feedtext = "";

			if (response.status != HttpStatusCode.OK)
			{
				var msg = "could not fetch " + source;
				GenUtils.LogMsg("warning", msg, null);
				return String.Empty;
			}

			feedtext = response.DataAsString();

			// because not needed, and dday.ical doesn't allow legal (but very old) dates
			feedtext = GenUtils.RegexReplace(feedtext, "\nCREATED:[^\n]+", "");

			// special favor for matt gillooly :-)
			if (this.id == "localist")
				feedtext = feedtext.Replace("\\;", ";");

			EnsureProdId(fr, feedurl, feedtext);

			return feedtext;
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

		private void IncludeFutureEvent(List<DDay.iCal.Components.Event> events_to_include, Dictionary<DDay.iCal.Components.Event, RecurrenceType> event_recurrence_types, DDay.iCal.Components.Event evt, DateTime midnight_in_tz, DateTime then, DDay.iCal.iCalendar ical)
		{
			try
			{
				if (evt.RRule == null) // non-recurring
				{
					if (IsCurrentOrFutureDTStartInTz(evt.DTStart))
					{
						events_to_include.Add(evt);
						event_recurrence_types.AddOrUpdateEventWithRecurrenceType(evt, RecurrenceType.NonRecurring);
					}
				}
				else // recurring
				{
					var occurrences = evt.GetOccurrences(midnight_in_tz, then);
					foreach (Occurrence occurrence in occurrences)
					{
						if (IsCurrentOrFutureDTStartInTz(occurrence.Period.StartTime))
						{
							var instance = PeriodizeRecurringEvent(evt, ical, occurrence.Period);
							events_to_include.Add(instance);
							event_recurrence_types.AddOrUpdateEventWithRecurrenceType(instance, RecurrenceType.Recurring);
						}
					}
				}
			}

			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "IncludeFutureEvent", e.Message);
			}
		}


	private void ProcessIcalEvent(DDay.iCal.Components.Event evt, ZonedEventStore es, FeedRegistry fr, DateTime midnight_in_tz, DateTime then, string feedurl, string source)
		{
			try
			{
				if (evt.RRule == null) // non-recurring
				{
					fr.stats[feedurl].singlecount++;
					fr.stats[feedurl].futurecount++;

				}
				else // recurring
				{
					fr.stats[feedurl].recurringinstancecount++;
					fr.stats[feedurl].futurecount++;
					AddIcalEvent(evt, fr, es, feedurl, source);
				}
			}

			catch (Exception e)
			{
				var msg = Utils.MakeLengthLimitedExceptionMessage(e);  // could be voluminous, so maybe truncate
				var error = string.Format("Error loading event {0}: {1}", source, evt.Summary);
				GenUtils.PriorityLogMsg("exception", error, msg);
				//fr.stats[feedurl].dday_error = error;
				//fr.stats[feedurl].valid = false;
				//fr.stats[feedurl].score = "0";
			}
		}

		// clone the DDay.iCal event, update dtstart (and maybe dtend) with Year/Month/Day for this occurrence
		private DDay.iCal.Components.Event PeriodizeRecurringEvent(DDay.iCal.Components.Event evt, iCalendar ical, Period period)
		{
			iCalDateTime dtstart = null;
			iCalDateTime dtend = null;

			dtstart = new iCalDateTime(
				period.StartTime.Year,
				period.StartTime.Month,
				period.StartTime.Day,
				evt.DTStart.Hour,
				evt.DTStart.Minute,
				evt.DTStart.Second,
				evt.DTStart.TZID,
				ical);

			if (evt.DTEnd != null)
			{
				dtend = new iCalDateTime(
					period.EndTime.Year,
					period.EndTime.Month,
					period.EndTime.Day,
					evt.DTEnd.Hour,
					evt.DTEnd.Minute,
					evt.DTEnd.Second,
					evt.DTEnd.TZID,
					ical);
			}

			var instance = new DDay.iCal.Components.Event(ical);
			instance.DTStart = instance.Start = dtstart;
			instance.DTEnd = instance.DTEnd = dtend;
			instance.Summary = evt.Summary;
			instance.Description = evt.Description;
			instance.Categories = evt.Categories;
			instance.Location = evt.Location;
			instance.Geo = evt.Geo;
			instance.UID = evt.UID;
			return instance;
		}

		// save the intermediate ics file for the source flavor represented in ical
		private BlobStorageResponse SerializeIcalEventsToIcs(iCalendar ical, string suffix)
		{
			var serializer = new iCalendarSerializer(ical);
			var ics_text = serializer.SerializeToString();
			var ics_bytes = Encoding.UTF8.GetBytes(ics_text);
			var containername = this.id;
			return bs.PutBlob(containername, containername + "_" + suffix + ".ics", new Hashtable(), ics_bytes, "text/calendar");
		}

		// put the event into a) the eventstore, and b) the per-flavor intermediate icalendar object
		private void AddIcalEvent(DDay.iCal.Components.Event evt, FeedRegistry fr, ZonedEventStore es, string feedurl, string source)
		{
			evt = NormalizeIcalEvt(evt, feedurl);

			Utils.DateTimeWithZone dtstart;
			Utils.DateTimeWithZone dtend;
			var tzinfo = this.calinfo.tzinfo;

			dtstart = Utils.DtWithZoneFromICalDateTime(evt.DTStart, tzinfo);
			dtend = (evt.DTEnd == null) ? new Utils.DateTimeWithZone(DateTime.MinValue, tzinfo) : Utils.DtWithZoneFromICalDateTime(evt.DTEnd, this.calinfo.tzinfo);

			MakeGeo(this, evt, this.calinfo.lat, this.calinfo.lon);

			if (evt.Categories != null && evt.Categories.Count() > 0)
			{
				var categories = string.Join(",", evt.Categories.ToList().Select(cat => cat.ToString()));
				es.AddEvent(evt.Summary, evt.Url.ToString(), source, dtstart, dtend, this.calinfo.lat, this.calinfo.lon, evt.IsAllDay, categories);
			}
			else
				es.AddEvent(evt.Summary, evt.Url.ToString(), source, dtstart, dtend, this.calinfo.lat, this.calinfo.lon, evt.IsAllDay);

			//var evt_tmp = MakeTmpEvt(dtstart, title, event_url, source, all_day, use_utc: true);
			var evt_tmp = MakeTmpEvt(this, dtstart, dtend, this.calinfo.tzinfo, this.calinfo.tzinfo.Id, title: evt.Summary, url: evt.Url.ToString(), location: evt.Location, description: source, lat: this.calinfo.lat, lon: this.calinfo.lon, allday: evt.IsAllDay, use_utc: evt.DTStart.IsUniversalTime);
			AddEventToDDayIcal(ical_ical, evt_tmp);

			fr.stats[feedurl].loaded++;
		}

		private static void MakeGeo(Collector collector, DDay.iCal.Components.Event evt, string lat, string lon)
		{
			if (collector == null ||                                    // called from outside the class, e.g. IcsFromRssPlusXcal
				collector.calinfo.hub_type == HubType.where.ToString()) // called from inside the class
			{
				if (evt.Geo == null)           // override with hub's location
				{
					try
					{
						lat = collector.calinfo.lat; 
						lon = collector.calinfo.lon;
						evt.Geo = new Geo();
						evt.Geo.Latitude = Double.Parse(lat);
						evt.Geo.Longitude = Double.Parse(lon);
					}
					catch (Exception e)
					{
						GenUtils.PriorityLogMsg("exception", "AddIcalEvent: cannot make evt.Geo", e.Message + e.StackTrace);
					}
				}
			}
		}

		// normalize url, description, location, category properties
		private DDay.iCal.Components.Event NormalizeIcalEvt(DDay.iCal.Components.Event evt, string feedurl)
		{
			try
			{
				if (evt.Url == null) evt.Url = "";
				if (evt.Description == null) evt.Description = "";
				if (evt.Location == null) evt.Location = "";

				var feed_metadict = GetFeedMetadictWithCaching(feedurl);

				var metadata_from_description = GenUtils.RegexFindKeysAndValues(Configurator.ical_description_metakeys, evt.Description);

				SetUrl(evt, feed_metadict, metadata_from_description);

				SetCategories(evt, feed_metadict, metadata_from_description);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", this.id + ": NormalizeIcalEvent", e.Message + e.StackTrace);
			}

			return evt;

		}

		private static void SetCategories(DDay.iCal.Components.Event evt, Dictionary<string, string> feed_metadict, Dictionary<string, string> metadata_from_description)
		{
			// apply feed-level categories from feed metadata

			if (feed_metadict.ContainsKey("category"))
			{
				var cat_string = feed_metadict["category"];
				AddCategoriesFromCatString(evt, cat_string);
			}

			// apply event-level categories from Description

			if (metadata_from_description.ContainsKey("category"))
			{
				var cat_string = metadata_from_description["category"];
				AddCategoriesFromCatString(evt, cat_string);
			}
		}

		private static void AddCategoriesFromCatString(DDay.iCal.Components.Event evt, string cats)
		{
			var catlist = cats.Split(',');
			foreach (var cat in catlist)
				evt.AddCategory(cat);
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
					feed_metadict = delicious.LoadFeedMetadataFromAzureTableForFeedurlAndId(feedurl, this.id);
					per_feed_metadata_cache[feedurl] = feed_metadict;
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "NormalizeIcalEvt", e.Message + e.StackTrace);
				}
			}
			return feed_metadict;
		}

		private static void SetUrl(DDay.iCal.Components.Event evt, Dictionary<string, string> feed_metadict, Dictionary<string,string> metadata_from_description)
		{
			if (EventUrlPropertyIsHttp(evt))  // use the URL property if it exists and is http:
				return;

			if (feed_metadict.ContainsKey("url"))  // use the feed metadata's URL if it exists
			{
				evt.Url = feed_metadict["url"];
			}

			if (DescriptionNotEmptyAndStartsWithHttp(evt)) // override with event's Description if URL-like
			{
				evt.Url = evt.Description.ToString();
			}

			if (LocationNotEmptyAndStartsWithHttp(evt))   // override with the event's Location if URL-like
			{
				evt.Url = evt.Location.ToString();
			}

			if (metadata_from_description.ContainsKey("url")) // finally override with event's url=URL if it exists
			{
				evt.Url = metadata_from_description["url"];
			}

			// otherwise evt.URL stays empty

		}

		private static bool EventUrlPropertyIsHttp(DDay.iCal.Components.Event evt)
		{
			string url = evt.Url.ToString();
			return !String.IsNullOrEmpty(url) && url.StartsWith("http:"); // URL:message:%3C001401cbb263$05c84af0$1158e0d0$@net%3E doesn't qualify
		}

		private static bool DescriptionNotEmptyAndStartsWithHttp(DDay.iCal.Components.Event evt)
		{
			return (!String.IsNullOrEmpty(evt.Description)) && evt.Description.ToString().StartsWith("http://");
		}

		private static bool LocationNotEmptyAndStartsWithHttp(DDay.iCal.Components.Event evt)
		{
			string location = evt.Location.ToString();
			return !String.IsNullOrEmpty(location) && location.StartsWith("http://");
		}

		private static bool UrlInLocation(DDay.iCal.Components.Event evt)
		{
			return evt.Location.ToString().Contains(evt.Url.ToString()) == false;
		}

		private static bool UrlNotEmptyAndDescriptionContainsUrl(DDay.iCal.Components.Event evt)
		{
			return (!String.IsNullOrEmpty(evt.Url.ToString())) && evt.Description.ToString().Contains(evt.Url.ToString()) == false;
		}

		// alter feed url if it should be handled by the internal "fusecal" service, or the vcal or xcal converters
		// todo: make this table-driven 
		public string MaybeRedirectFeedUrl(string str_url, Dictionary<string, string> feed_metadict)
		{
			List<string> groups;

			string str_final_url = null;

			var filter = GetFusecalFilter(feed_metadict);

			var tz_source = this.calinfo.tzname;
			var tz_dest = tz_source;

			//obscure, leave out for now 
			//if (feed_metadict.ContainsKey("tz"))
			//    tz_dest = tz_source = feed_metadict["tz"];

			groups = GenUtils.RegexFindGroups(str_url, "(libraryinsight.com)(.+)");
			if (groups.Count == 3)
			{
				str_final_url = String.Format(Configurator.fusecal_service, Uri.EscapeDataString(str_url), filter, tz_source, tz_dest);
			}

			groups = GenUtils.RegexFindGroups(str_url, "(librarything.com/local/place/)(.+)");
			if (groups.Count == 3)
			{
				var place = groups[2];
				var radius = this.calinfo.radius;
				var lt_url = Uri.EscapeDataString(string.Format("http://www.librarything.com/rss/events/location/{0}/distance={1}",
					place, radius));
				str_final_url = String.Format(Configurator.fusecal_service, lt_url, filter, tz_source, tz_dest);
			}

			groups = GenUtils.RegexFindGroups(str_url, "(myspace.com/)(.+)");
			if (groups.Count == 3)
			{
				var musician = groups[2];
				var ms_url = Uri.EscapeDataString(string.Format("http://www.myspace.com/{0}", musician));
				str_final_url = String.Format(Configurator.fusecal_service, ms_url, filter, tz_source, tz_dest);
			}

			if (str_final_url == null)
				return str_url;
			else
			{
				GenUtils.LogMsg("info", "MaybeRedirectFeedUrl: " + id, str_url + " -> " + str_final_url);
				return str_final_url;
			}

		}

		// alter feed url if it should be transformed from rss+xcal to ics
		public string MaybeXcalToIcsFeedUrl(string str_url, Dictionary<string, string> feed_metadict)
		{
			string str_final_url = str_url;
			str_final_url = RedirectFeedUrl(str_url, Configurator.ics_from_xcal_service, feed_metadict, trigger_key: "xcal", str_final_url: str_final_url);
			return str_final_url;
		}

		// alter feed url if it should be transformed from atom+vcal to ics
		public string MaybeVcalToIcsFeedUrl(string str_url, Dictionary<string, string> feed_metadict)
		{
			string str_final_url = str_url;
			str_final_url = RedirectFeedUrl(str_url, Configurator.ics_from_vcal_service, feed_metadict, trigger_key: "vcal", str_final_url: str_final_url);
			return str_final_url;
		}

		private string RedirectFeedUrl(string str_url, string service_url, Dictionary<string, string> feed_metadict, string trigger_key, string str_final_url)
		{
			try
			{
				var tzname = this.calinfo.tzname;
				var source = feed_metadict["source"];
				if (feed_metadict.ContainsKey(trigger_key))
				{
					str_final_url = String.Format(service_url,  // e.g. ics_from_xcal?url={0}&tzname={1}&source={2}";
							Uri.EscapeDataString(str_url),
							tzname,
							source);
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "RedirectFeedUrl", e.Message + e.StackTrace);
			}
			return str_final_url;
		}

		// get the filter= property from metadata
		// todo: generalize for getting any property from metadata
		private static string GetFusecalFilter(Dictionary<string, string> feed_metadict)
		{
			string filter;
			if (feed_metadict.ContainsKey("filter"))
			{
				var unescaped = feed_metadict["filter"];
				filter = Uri.EscapeDataString(unescaped);
			}
			else
				filter = "";
			return filter;
		}

		// add VTIMEZONE to intermediate or final ics outputs
		public static void AddTimezoneToDDayICal(DDay.iCal.iCalendar ical, TimeZoneInfo tzinfo)
		{
			var timezone = DDay.iCal.Components.iCalTimeZone.FromSystemTimeZone(tzinfo);

			if (timezone.TimeZoneInfos.Count == 0)
			{
				var dday_tzinfo_standard = new DDay.iCal.Components.iCalTimeZoneInfo();
				dday_tzinfo_standard.Name = "STANDARD";
				dday_tzinfo_standard.TimeZoneName = tzinfo.StandardName;
				dday_tzinfo_standard.Start = new DateTime(1970, 1, 1);
				var utcOffset = tzinfo.BaseUtcOffset;
				dday_tzinfo_standard.TZOffsetFrom = new DDay.iCal.DataTypes.UTC_Offset(utcOffset);
				dday_tzinfo_standard.TZOffsetTo = new DDay.iCal.DataTypes.UTC_Offset(utcOffset);
				// Add the "standard" time rule to the time zone
				timezone.AddChild(dday_tzinfo_standard);
			}

			ical.AddChild(timezone);
		}

		#endregion ical

		#region eventful

		public void CollectEventful(ZonedEventStore es, bool test)
		{
			using (eventful_ical)
			{
				//string location = string.Format("{0},{1}", this.calinfo.lat, this.calinfo.lon);
				string location = this.calinfo.where;
				var page_size = test ? test_pagesize : 100;
				var now = Utils.MidnightInTz(this.calinfo.tzinfo).LocalTime;
				string fmt = "{0:yyyyMMdd}00";
				string min_date = String.Format(fmt, now);
				string max_date = MakeDateArg(fmt, now);
				string daterange = min_date + "-" + max_date;
				string args = string.Format("date={0}&location={1}&within={2}&units=mi&page_size={3}", daterange, location, this.calinfo.radius, page_size);
				string method = "events/search";
				var xdoc = CallEventfulApi(method, args);
				var str_page_count = XmlUtils.GetXeltValue(xdoc.Root, ElmcityUtils.Configurator.no_ns, "page_count");
				int page_count = test ? test_pagecount : Convert.ToInt16(str_page_count);
				var msg = string.Format("{0}: loading {1} eventful events", this.id, page_count * page_size);
				GenUtils.LogMsg("info", msg, null);

				var uniques = new Dictionary<string, XElement>(); // dedupe by title + start
				foreach (XElement evt in EventfulIterator(page_count, args))
					uniques.AddOrUpdateXElement(
						XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "title") +
							XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "start_time"),
						evt);

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

					/* experimental exclusion filter, idle for now
					if (Utils.ListContainsItemStartingWithString(this.excluded_urls, url))
					{
						GenUtils.LogMsg("info", "CollectEventful: " + id, "excluding " + url);
						continue;
					}*/
				}

				estats.venuecount = event_count_by_venue.Keys.Count;
				estats.whenchecked = DateTime.Now.ToUniversalTime();

				SerializeStatsAndIntermediateOutputs(es, eventful_ical, estats, EventFlavor.eventful);
			}

		}

		public void AddEventfulEvent(ZonedEventStore es, string venue_name, XElement evt)
		{
			var str_dtstart = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "start_time");
			DateTime dtstart = Utils.DateTimeFromDateStr(str_dtstart);
			var dtstart_with_tz = new Utils.DateTimeWithZone(dtstart, this.calinfo.tzinfo);

			if (dtstart_with_tz.UniversalTime < Utils.MidnightInTz(this.calinfo.tzinfo).UniversalTime)
				return;

			var no_ns = ElmcityUtils.Configurator.no_ns;

			var event_id = evt.Attribute("id").Value;
			var event_owner = XmlUtils.GetXeltValue(evt, no_ns, "owner");
			var title = XmlUtils.GetXeltValue(evt, no_ns, "title");
			var venue_url = XmlUtils.GetXeltValue(evt, no_ns, "venue_url");
			var all_day = XmlUtils.GetXeltValue(evt, no_ns, "all_day") == "1";

			string lat = this.calinfo.lat;   // default to hub lat/lon
			string lon = this.calinfo.lon;
			try
			{
				lat = XmlUtils.GetXeltValue(evt, no_ns, "latitude");
				lon = XmlUtils.GetXeltValue(evt, no_ns, "longitude");
			}
			catch 
			{
				GenUtils.LogMsg("warning", "AddEventfulEvent", "cannot parse lat/lon");
			}

			// idle for now, but this was a way to enable curators to associate venues with tags,
			// such that all events at the venue receive the tag
			//var metadict = get_venue_metadata(venue_meta_cache, "eventful", this.id, venue_url);
			//string categories = metadict.ContainsKey("category") ? metadict["category"] : null;
			string categories = null;

			string event_url = "http://eventful.com/events/" + event_id;
			string source = "eventful: " + venue_name;

			estats.eventcount++;

			//var evt_tmp = MakeTmpEvt(dtstart_with_tz, title, event_url, source, all_day, use_utc: false);
			var evt_tmp = MakeTmpEvt(this, dtstart_with_tz, Utils.DateTimeWithZone.MinValue(this.calinfo.tzinfo), this.calinfo.tzinfo, this.calinfo.tzinfo.Id, title, url: event_url, location: event_url, description: source, lat: lat, lon: lon, allday: all_day, use_utc: false);


			AddEventToDDayIcal(eventful_ical, evt_tmp);

			var min = Utils.DateTimeWithZone.MinValue(this.calinfo.tzinfo);

			if (categories == null)
				//es.AddEvent(title, event_url, source, dtstart_with_tz, min, all_day);
				es.AddEvent(title, event_url, source, dtstart_with_tz, min, lat, lon, all_day);
			else // idle for now
				//es.AddEvent(title, event_url, source, dtstart_with_tz, min, all_day, categories);
				es.AddEvent(title, event_url, source, dtstart_with_tz, min, lat, lon, all_day, categories);
		}

		public IEnumerable<XElement> EventfulIterator(int page_count, string args)
		{
			for (int i = 0; i < page_count; i++)
			{
				string this_args = string.Format("{0}&page_number={1}", args, i + 1);
				string method = "events/search";
				XDocument xdoc = CallEventfulApi(method, this_args);
				IEnumerable<XElement> query = from events in xdoc.Descendants("event") select events;
				foreach (XElement evt in query)
					yield return evt;
			}
		}

		public XDocument CallEventfulApi(string method, string args)
		{
			var key = this.apikeys.eventful_api_key;
			string host = "http://api.eventful.com/rest";
			string url = string.Format("{0}/{1}?app_key={2}&{3}", host, method, key, args);
			//GenUtils.LogMsg("eventful", url, null);
			var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
			//byte[] bytes = HttpUtils.DoHttpWebRequest(request, null).bytes;
			var response = HttpUtils.RetryExpectingOK(request, data: null, wait_secs: 3, max_tries: 10, timeout_secs: TimeSpan.FromSeconds(60));
			return XmlUtils.XdocFromXmlBytes(response.bytes);
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
					GenUtils.LogMsg("info", "CollectUpcoming", "resultcount unavailable");
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
					uniques.AddOrUpdateXElement(evt.Attribute("name").ToString() + evt.Attribute("start_date").ToString(), evt);

				foreach (XElement evt in uniques.Values)
				{
					event_num += 1;
					if (event_num > Configurator.upcoming_max_events)
						break;

					var unpacked = UnpackUpcomingXelement(evt);
					var dtstart_with_zone = (Utils.DateTimeWithZone) unpacked["dtstart_with_zone"];
					var title = (string) unpacked["title"];
					var venue_name = (string) unpacked["venue_name"];
					var str_dtstart = (string) unpacked["str_dtstart"];

					if (dtstart_with_zone.UniversalTime < Utils.MidnightInTz(this.calinfo.tzinfo).UniversalTime)
						continue;

					IncrementEventCountByVenue(event_count_by_venue, venue_name);

					AddUpcomingEvent(es, venue_name, evt, dtstart_with_zone);
				}

				ustats.venuecount = event_count_by_venue.Keys.Count;
				ustats.whenchecked = DateTime.Now.ToUniversalTime();

				SerializeStatsAndIntermediateOutputs(es, upcoming_ical, ustats, EventFlavor.upcoming);
			}
		}

		private static int GetUpcomingResultCount(XDocument xdoc)
		{
			var str_result_count = xdoc.Document.Root.Attribute("resultcount").Value;
			return Convert.ToInt32(str_result_count);
		}

		private string MakeUpcomingApiArgs(UpcomingSearchStyle search_style)
		{
			string fmt = "{0:yyyy-MM-dd}";
			var now = Utils.MidnightInTz(this.calinfo.tzinfo).LocalTime;
			var min_date = string.Format(fmt, now);
			var max_date = MakeDateArg(fmt, now);

			string location_arg;
			if (search_style == UpcomingSearchStyle.latlon)
				location_arg = String.Format("{0},{1}", this.calinfo.lat, this.calinfo.lon);
			else
				location_arg = String.Format("{0}", this.calinfo.where);
			
			return string.Format("location={0}&radius={1}&min_date={2}&max_date={3}", location_arg, this.calinfo.radius, min_date, max_date);
		}

		public string MakeDateArg(string fmt, DateTime now)
		{
			string date_arg = "";
			if (this.population > 100000)
				date_arg = String.Format(fmt, now + TimeSpan.FromDays(90));
			if (this.population > 300000)
				date_arg = String.Format(fmt, now + TimeSpan.FromDays(60));
			if (this.population > 500000)
				date_arg = String.Format(fmt, now + TimeSpan.FromDays(30));
			return date_arg;
		}

		private Dictionary<string, object> UnpackUpcomingXelement(XElement evt)
		{
			string str_start_date = evt.Attribute("start_date").Value; //2010-07-07
			string str_dtstart = evt.Attribute("utc_start").Value;     //2010-07-21 18:00:00 UTC
			str_dtstart = str_start_date + str_dtstart.Substring(10, 13);
			DateTime dtstart = Utils.LocalDateTimeFromUtcDateStr(str_dtstart, this.calinfo.tzinfo);
			var dtstart_with_zone = new Utils.DateTimeWithZone(dtstart, this.calinfo.tzinfo);
			var title = evt.Attribute("name").Value;
			var venue_name = evt.Attribute("venue_name").Value;
			return new Dictionary<string, object>() {
				{"str_dtstart"	, str_dtstart },
				{"title"		, title },
				{"venue_name"	, venue_name },
				{"dtstart_with_zone" , dtstart_with_zone }
			};
		}

		public void AddUpcomingEvent(ZonedEventStore es, string venue_name, XElement evt, Utils.DateTimeWithZone dtstart)
		{
			var title = evt.Attribute("name").Value;
			var event_url = "http://upcoming.yahoo.com/event/" + evt.Attribute("id").Value;
			var source = "upcoming: " + venue_name;
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


			var all_day = String.IsNullOrEmpty(evt.Attribute("start_time").Value);

			// see eventful above: idle for now
			//var metadict = get_venue_metadata(venue_meta_cache, "upcoming", this.id, venue_url);
			//var categories = metadict.ContainsKey("category") ? metadict["category"] : null;
			string categories = null;

			ustats.eventcount++;

			var evt_tmp = MakeTmpEvt(this, dtstart, Utils.DateTimeWithZone.MinValue(this.calinfo.tzinfo), this.calinfo.tzinfo, this.calinfo.tzinfo.Id, title, url: event_url, location: event_url, description: source, lat: lat, lon: lon, allday: all_day, use_utc: true);

			AddEventToDDayIcal(upcoming_ical, evt_tmp);

			var min = Utils.DateTimeWithZone.MinValue(this.calinfo.tzinfo);

			if (categories == null)
				es.AddEvent(title, event_url, source, dtstart, min, lat, lon, all_day);
			else 
				es.AddEvent(title, event_url, source, dtstart, min, lat, lon, all_day, categories);
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
			var key = this.apikeys.upcoming_api_key;
			string host = "http://upcoming.yahooapis.com/services/rest/";
			string url = string.Format("{0}?rollup=none&api_key={1}&method={2}&{3}", host, key, method, args);
			//GenUtils.LogMsg("info", url, null);
			var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
			var response = HttpUtils.RetryExpectingOK(request, data: null, wait_secs: this.wait_secs, max_tries: this.max_retries, timeout_secs: this.timeout_secs);
			return XmlUtils.XdocFromXmlBytes(response.bytes);
		}

		#endregion upcoming

		#region eventbrite

		public void CollectEventBrite(ZonedEventStore es, bool test)
		{
			using (eventbrite_ical)
			{
				var page_size = test ? test_pagesize : 10;
				var where = System.Text.RegularExpressions.Regex.Split(this.calinfo.where, "\\s+|,");
				var city = where[0];
				var region = where[1].ToUpper();
				var now = Utils.MidnightInTz(this.calinfo.tzinfo).LocalTime;
				string fmt = "{0:yyyy-MM-dd}";
				var min_date = string.Format(fmt, now);
				string max_date = MakeDateArg(fmt, now);
				var date = min_date + ' ' + max_date;
				var args = string.Format("city={0}&region={1}&within={2}&date={3}", city, region, this.calinfo.radius, date);
				var method = "event_search";
				var xdoc = CallEventBriteApi(method, args);
				int page_count = 1;
				try
				{
					var str_result_count = xdoc.Descendants("total_items").FirstOrDefault().Value;
					int result_count = Convert.ToInt32(str_result_count);
					page_count = result_count / page_size;
				}
				catch
				{
					GenUtils.LogMsg("info", "CollectEventBrite", "resultcount unavailable");
					return;
				}

				if (test == true && page_count > test_pagecount) page_count = test_pagecount;
				if (page_count == 0) page_count = 1;

				var msg = string.Format("{0}: loading {1} eventbrite events", this.id, page_count * page_size);
				GenUtils.LogMsg("info", msg, null);

				int event_num = 0;

				foreach (XElement evt in EventBriteIterator(page_count, method, args))
				{
					if (event_num > Configurator.eventbrite_max_events)
						break;
					string str_dtstart = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "start_date");
					DateTime dtstart = Utils.DateTimeFromDateStr(str_dtstart);
					var dtstart_with_tz = new Utils.DateTimeWithZone(dtstart, this.calinfo.tzinfo);

					if (dtstart_with_tz.UniversalTime < Utils.MidnightInTz(this.calinfo.tzinfo).UniversalTime)
						continue;

					AddEventBriteEvent(es, evt, dtstart_with_tz);
				}
				ebstats.whenchecked = DateTime.Now.ToUniversalTime();

				SerializeStatsAndIntermediateOutputs(es, eventbrite_ical, ebstats, EventFlavor.eventbrite);
			}
		}

		public void AddEventBriteEvent(ZonedEventStore es, XElement evt, Utils.DateTimeWithZone dtstart)
		{
			var title = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "title");
			var event_url = evt.Element(ElmcityUtils.Configurator.no_ns + "url").Value;
			var source = "eventbrite";

			var start_dt_str = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "start_date");
			var start_dt = Utils.DateTimeFromDateStr(start_dt_str);
			var start_dt_with_zone = new Utils.DateTimeWithZone(start_dt, this.calinfo.tzinfo);
			var all_day = start_dt.Hour == 0 && start_dt.Minute == 0;

			ebstats.eventcount++;

			//var evt_tmp = MakeTmpEvt(dtstart, title, event_url, source, all_day, use_utc: true);
			var evt_tmp = MakeTmpEvt(this, start_dt_with_zone, Utils.DateTimeWithZone.MinValue(this.calinfo.tzinfo), this.calinfo.tzinfo, this.calinfo.tzinfo.Id, title, url: event_url, location: event_url, description: source, allday: all_day, lat: null, lon: null, use_utc: true);

			AddEventToDDayIcal(eventbrite_ical, evt_tmp);

			var min = Utils.DateTimeWithZone.MinValue(this.calinfo.tzinfo);

			//es.AddEvent(title, event_url, source, dtstart, min, all_day);
			es.AddEvent(title, event_url, source, dtstart, min, lat:null, lon:null, allday:all_day);
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
			try
			{
				var key = this.apikeys.eventbrite_api_key;
				string host = "https://www.eventbrite.com/xml";
				string url = string.Format("{0}/{1}?app_key={2}&{3}", host, method, key, args);
				//GenUtils.LogMsg("info", url, null);
				var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
				//var str_data = HttpUtils.DoHttpWebRequest(request, data: null).DataAsString();
				var response = HttpUtils.RetryExpectingOK(request, data: null, wait_secs: this.wait_secs, max_tries: this.max_retries, timeout_secs: this.timeout_secs);
				var str_data = response.DataAsString();
				str_data = GenUtils.RegexReplace(str_data, "<description>[^<]+</description>", "");
				byte[] bytes = Encoding.UTF8.GetBytes(str_data);
				return XmlUtils.XdocFromXmlBytes(bytes);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "CallEventBriteApi", e.Message + e.StackTrace);
				return new XDocument();
			}
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
					uniques.AddOrUpdateFacebookEvent(fb_event.name + fb_event.start_time, fb_event);

				foreach (FacebookEvent fb_event in uniques.Values)
				{
					DateTime dtstart = Utils.LocalDateTimeFromFacebookDateStr(fb_event.start_time, this.calinfo.tzinfo);

					var dtstart_with_zone = new Utils.DateTimeWithZone(dtstart, this.calinfo.tzinfo);

					if (dtstart_with_zone.UniversalTime < Utils.MidnightInTz(this.calinfo.tzinfo).UniversalTime)
						continue;

					AddFacebookEvent(es, fb_event, dtstart_with_zone);
				}

				fbstats.whenchecked = DateTime.Now.ToUniversalTime();

				SerializeStatsAndIntermediateOutputs(es, facebook_ical, fbstats, EventFlavor.facebook);
			}
		}

		public void AddFacebookEvent(ZonedEventStore es, FacebookEvent fb_event, Utils.DateTimeWithZone dtstart)
		{
			var title = fb_event.name;
			var event_url = "http://www.facebook.com/event.php?eid=" + fb_event.id;
			var source = "facebook";

			var all_day = dtstart.LocalTime.Hour == 0 && dtstart.LocalTime.Hour == 0;

			fbstats.eventcount++;

			//var evt_tmp = MakeTmpEvt(dtstart, title, event_url, source, all_day, use_utc: false);
			var evt_tmp = MakeTmpEvt(this, dtstart, Utils.DateTimeWithZone.MinValue(this.calinfo.tzinfo), this.calinfo.tzinfo, this.calinfo.tzinfo.Id, title, url: event_url, location: event_url, description: source, lat: null, lon: null, allday: all_day, use_utc: false);

			AddEventToDDayIcal(facebook_ical, evt_tmp);

			var min = Utils.DateTimeWithZone.MinValue(this.calinfo.tzinfo);

			//es.AddEvent(title, event_url, source, dtstart, min, all_day);
			es.AddEvent(title, event_url, source, dtstart, min, lat:null, lon:null, allday:all_day);
		}

		// using the built-in json deserializer here, in contrast to the 3rd party newtonsoft.json
		public IEnumerable<FacebookEvent> FacebookIterator(string method, string args)
		{
			var json = CallFacebookApi(method, args);
			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
			var dict = (Dictionary<string, object>)serializer.DeserializeObject(json);
			var items = (Object[])dict["data"];
			foreach (Dictionary<string, object> item in items)
			{
				var name = (string)item["name"];
				var location = (string)"";
				try { location = (string)item["location"]; }
				catch { };
				var start_time = (string)item["start_time"];
				var id = (string)item["id"];
				yield return new FacebookEvent(name, location, start_time, id);
			}
		}

		public string CallFacebookApi(string method, string args)
		{
			//  SEARCH_URL='http://graph.facebook.com/search?q=%s&type=event&access_token=106452869398676|9dbd3ef444640025e3eea22a-100001048487772|eEuiFknpHtEnf2w3m-TyW8AZEBE.' % urllib.quote_plus(location)

			try
			{
				var key = this.apikeys.facebook_api_key;
				string host = "https://graph.facebook.com";
				string url = string.Format("{0}/{1}?access_token={2}&type=event&{3}", host, method, key, args);
				GenUtils.LogMsg("info", url, null);
				var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
				var response = HttpUtils.RetryExpectingOK(request, data: null, wait_secs: this.wait_secs, max_tries: this.max_retries, timeout_secs: this.timeout_secs);
				return response.DataAsString();
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "CallFacebookApi", e.Message + e.StackTrace);
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

		public static void AddEventToDDayIcal(iCalendar ical, DDay.iCal.Components.Event evt)
		{
			var ical_evt = new DDay.iCal.Components.Event(ical);
			ical_evt.Categories = evt.Categories;
			ical_evt.Summary = evt.Summary;
			ical_evt.Url = evt.Url;
			ical_evt.Location = evt.Location;
			ical_evt.Description = evt.Description;
			ical_evt.DTStart = evt.DTStart;
			if (evt.DTEnd != null && evt.DTEnd.Value != DateTime.MinValue)
				ical_evt.DTEnd = evt.DTEnd;
			ical_evt.IsAllDay = evt.IsAllDay;
			ical_evt.UID = Event.MakeEventUid(ical_evt);
			ical_evt.Geo = evt.Geo;
		}

		public  static DDay.iCal.Components.Event MakeTmpEvt(Collector collector, Utils.DateTimeWithZone dtstart, Utils.DateTimeWithZone dtend, TimeZoneInfo tzinfo, string tzid, string title, string url, string location, string description, string lat, string lon, bool allday, bool use_utc)
		{
			iCalendar ical = new iCalendar();
			AddTimezoneToDDayICal(ical, tzinfo);
			DDay.iCal.Components.Event evt = new DDay.iCal.Components.Event(ical);
			evt.Summary = title;
			evt.Url = url;
			if (location != null)
				evt.Location = location;
			if (description != null)
				evt.Description = description;
			if (evt.Geo == null)
				MakeGeo(collector, evt, lat, lon);
			evt.DTStart = (use_utc) ? dtstart.UniversalTime : dtstart.LocalTime;
			evt.DTStart.IsUniversalTime = (use_utc ? true : false);
			evt.DTStart.TZID = tzid;
			evt.DTStart.iCalendar = ical;
			/* disable until dday.ical bug found/fixed
			if (! dtend.Equals(Utils.DateTimeWithZone.MinValue(tzinfo)))
			{
				evt.DTEnd = (use_utc) ? dtend.UniversalTime : dtend.LocalTime;
				evt.DTStart.IsUniversalTime = (use_utc ? true : false);
				evt.DTEnd.TZID = tzid;
				evt.DTEnd.iCalendar = ical;
			}
			 */
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
			var utc_dtstart = Utils.DtWithZoneFromICalDateTime(ical_dtstart, this.calinfo.tzinfo);
			var utc_last_midnight = Utils.MidnightInTz(this.calinfo.tzinfo);
			return utc_dtstart.UniversalTime >= utc_last_midnight.UniversalTime;
		}

		/* can be used to enforce radius, but not needed since current non-ical sources all
		 * support radius in their query apis
         
				private bool within_range(XElement item)
				{
					var ret = false;
					try
					{
						var str_lat = item.Descendants(Configurator.geo_ns + "lat").FirstOrDefault().Value;
						var str_lon = item.Descendants(Configurator.geo_ns + "long").FirstOrDefault().Value;
						var radius = this.calinfo.radius;
						var center_lat = Convert.ToDouble(this.lat);
						var center_lon = Convert.ToDouble(this.lon);
						var event_lat = Convert.ToDouble(str_lat);
						var event_lon = Convert.ToDouble(str_lon);
						var d = Utils.GeoCodeCalc.CalcDistance(center_lat, center_lon, event_lat, event_lon);
						//Console.WriteLine(d);
						ret = d <= radius;
					}
					catch (Exception e)
					{
						GenUtils.PriorityLogMsg("exception", "within_range", e.Message + e.StackTrace);
					}
					return ret;
				}*/

		public static NonIcalStats DeserializeEventAndVenueStatsFromJson(string containername, string filename)
		{
			return Utils.DeserializeObjectFromJson<NonIcalStats>(containername, filename);
		}

		private void SerializeStatsAndIntermediateOutputs(FeedRegistry fr, EventStore es, iCalendar ical, NonIcalStats stats, EventFlavor flavor)
		{
			BlobStorageResponse bsr;
			TableStorageResponse tsr;
			var flavor_str = flavor.ToString();

			if (BlobStorage.ExistsContainer(this.id) == false)
				bs.CreateContainer(this.id, is_public: true, headers: new Hashtable());

			if (flavor == EventFlavor.ical)
			{
				bsr = fr.SerializeIcalStatsToJson();
				GenUtils.LogMsg("info", this.id + ": SerializeIcalStatsToJson: " + stats.blobname, bsr.HttpResponse.status.ToString());
				tsr = fr.SaveStatsToAzure();
				GenUtils.LogMsg("info", this.id + ": FeedRegistry.SaveStatsToAzure: " + stats.blobname, tsr.http_response.status.ToString());
			}
			else
			{

				bsr = Utils.SerializeObjectToJson(stats, this.id, stats.blobname + ".json");
				GenUtils.LogMsg("info", this.id + ": Collector: SerializeObjectToJson: " + stats.blobname + ".json", bsr.HttpResponse.status.ToString());
				tsr = this.SaveStatsToAzure(flavor);
				GenUtils.LogMsg("info", this.id + ": Collector: SaveStatsToAzure", tsr.http_response.status.ToString());

			}

			bsr = this.SerializeIcalEventsToIcs(ical, flavor_str);
			GenUtils.LogMsg("info", this.id + ": SerializeIcalStatsToIcs: " + id + "_" + flavor_str + ".ics", bsr.HttpResponse.status.ToString());

			bsr = es.Serialize(es.objfile);
			GenUtils.LogMsg("info", this.id + ": EventStore.Serialize: " + es.objfile, bsr.HttpResponse.status.ToString());
		}

		private void SerializeStatsAndIntermediateOutputs(EventStore es, iCalendar ical, NonIcalStats stats, EventFlavor flavor)
		{
			SerializeStatsAndIntermediateOutputs(new FeedRegistry(this.id), es, ical, stats, flavor);
		}

		private TableStorageResponse SaveStatsToAzure(EventFlavor flavor)
		{
			var entity = new Dictionary<string, object>();
			entity["PartitionKey"] = entity["RowKey"] = this.id;
			switch (flavor)
			{
				case EventFlavor.facebook:
					entity["facebook_events"] = this.fbstats.eventcount;
					break;
				case EventFlavor.upcoming:
					entity["upcoming_events"] = this.ustats.eventcount;
					break;
				case EventFlavor.eventful:
					entity["eventful_events"] = this.estats.eventcount;
					break;
				case EventFlavor.eventbrite:
					entity["eventbrite_events"] = this.ebstats.eventcount;
					break;
			}
			return ts.MergeEntity("metadata", this.id, this.id, entity);
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
		public string start_time;
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
		public FacebookEvent(string name, string location, string start_time, string id)
		{
			this.name = name;
			this.location = location;
			this.start_time = start_time;
			this.id = id;
		}
	}
}

