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
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
//using System.Threading.Tasks;
using System.Xml.Linq;
using DDay.iCal;
using DDay.iCal.Components;
using DDay.iCal.DataTypes;
using DDay.iCal.Serialization;
using ElmcityUtils;

namespace CalendarAggregator
{
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

	public class Collector
	{

		private Calinfo calinfo;
		private Apikeys apikeys = new Apikeys();
		private string id;
		private BlobStorage bs;
		private Delicious delicious;

		private Dictionary<string, string> metadict = new Dictionary<string, string>();

		private enum EventFlavor { ical, eventful, upcoming, eventbrite, facebook };

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

		private string lon;
		private string lat;

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
		public Collector(Calinfo calinfo)
		{
			this.calinfo = calinfo;
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

			if (calinfo.hub_type == "where")
			{
				// curator gets to override the lat/lon that will otherwise be looked up based on the location
				// ( from where= in the metadata )
				if (this.metadict.ContainsKey("lat") && this.metadict.ContainsKey("lon"))
				{
					this.lat = this.metadict["lat"];
					this.lon = this.metadict["lon"];
				}
				else
				{
					var lookup_lat = Utils.LookupLatLon(apikeys.yahoo_api_key, this.calinfo.where)[0];
					var lookup_lon = Utils.LookupLatLon(apikeys.yahoo_api_key, this.calinfo.where)[1];
					if (!String.IsNullOrEmpty(lookup_lat) && !String.IsNullOrEmpty(lookup_lon))
					{
						this.lat = lookup_lat;
						this.lon = lookup_lon;
					}
				}

				if (this.lat == null || this.lon == null)
				{
					throw new Exception("EventCollector: no lat/lon for " + this.id);
				}
			}

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

				//Parallel.ForEach(feedurls, (feedurl, loop_state) =>
				foreach (string feedurl in feedurls)
				{
					per_feed_metadata_cache = new Dictionary<string, Dictionary<string, string>>();

					string source = feeds[feedurl];

					string load_msg = string.Format("loading {0}: {1} ({2})", id, source, feedurl);
					GenUtils.LogMsg("info", load_msg, null);

					fr.stats[feedurl].whenchecked = DateTime.Now.ToUniversalTime();

					iCalendar ical;

					var feed_metadict = delicious.LoadFeedMetadataFromAzureTableForFeedurlAndId(feedurl, this.calinfo.delicious_account);

					// allow the "fusecal" service to hook in if it can
					var _feedurl = MaybeRedirectFeedUrl(feedurl, feed_metadict);

					// allow ics_from_xcal to hook in if it can
					_feedurl = MaybeXcalToIcsFeedUrl(_feedurl, feed_metadict);

					var feedtext = "";

					try
					{
						feedtext = GetFeedTextFromRedirectedFeedUrl(fr, source, feedurl, _feedurl);
					}
					catch
					{
						var msg = String.Format("{0}: {1} cannot retrieve feed", id, source);
						GenUtils.LogMsg("warning", msg, null);
					}

					StringReader sr = new StringReader(feedtext);

					try
					{
						ical = iCalendar.LoadFromStream(sr);

						if (ical == null || ical.Events.Count == 0)
						{
							var msg = String.Format("{0}: no events found for {1}", id, source);
							GenUtils.LogMsg("warning", msg, null);
							continue;
							//loop_state.Break();
						}  

						foreach (DDay.iCal.Components.Event evt in ical.Events)
							ProcessIcalEvent(fr, es, utc_midnight_in_tz, then, feedurl, source, evt, ical);

					}
					catch (Exception e)
					{
						GenUtils.LogMsg("exception", "CollectIcal: " + id, e.Message + e.StackTrace);
					}
				}
				//)
				;

				if (nosave == false) // why ever true? see CalendarRenderer.Viewer 
					SerializeStatsAndIntermediateOutputs(fr, es, ical_ical, new NonIcalStats(), EventFlavor.ical);
			}
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
				fr.stats[feedurl].dday_error = msg;
			}
			else
			{
				/* disable validation for now, until icalvalid.cloudapp.net is back online
				try
				{
					fr.stats[feedurl].score = Utils.DDay_Validate(_feedurl);
				}
				catch (Exception e)
				{
					GenUtils.LogMsg("exception", "DDay_Validate: " + e.Message, _feedurl);
				}
				 */

				feedtext = response.DataAsString();

				// because not needed, and dday.ical doesn't allow legal (but very old) dates
				feedtext = GenUtils.RegexReplace(feedtext, "\nCREATED:[^\n]+", "");

				// special favor for matt gillooly :-)
				if (this.id == "localist")
					feedtext = feedtext.Replace("\\;", ";");

				EnsureProdId(fr, feedurl, feedtext);
			}

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

		private void ProcessIcalEvent(FeedRegistry fr, ZonedEventStore es, DateTime midnight_in_tz, DateTime then, string feedurl, string source, DDay.iCal.Components.Event evt, iCalendar ical)
		{
			try
			{
				if (evt.RRule == null) // non-recurring
				{
					if (IsCurrentOrFutureDTStartInTz(evt.DTStart))
					{
						fr.stats[feedurl].singlecount++;
						fr.stats[feedurl].futurecount++;
						AddIcalEvent(evt, fr, es, feedurl, source);
					}
				}

				else // recurring
				{
					List<Occurrence> occurrences = evt.GetOccurrences(midnight_in_tz, then);
					fr.stats[feedurl].recurringcount++;
					foreach (Occurrence occurrence in occurrences)
					{
						if (IsCurrentOrFutureDTStartInTz(occurrence.Period.StartTime))
						{
							fr.stats[feedurl].recurringinstancecount++;
							fr.stats[feedurl].futurecount++;
							PeriodizeRecurringEvent(evt, ical, occurrence.Period);
							AddIcalEvent(evt, fr, es, feedurl, source);
						}
					}
				}
			}

			catch (Exception e)
			{
				var msg = Utils.MakeLengthLimitedExceptionMessage(e);  // could be voluminous, so maybe truncate
				var error = string.Format("Error loading event {0}: {1}");
				GenUtils.LogMsg("exception", error, "");
				fr.stats[feedurl].dday_error = error;
				fr.stats[feedurl].valid = false;
				fr.stats[feedurl].score = "0";
			}
		}

		// update the DDay.iCal event's dtstart (and maybe dtend) with Year/Month/Day for this occurrence
		private static void PeriodizeRecurringEvent(DDay.iCal.Components.Event evt, iCalendar ical, Period period)
		{
			var dtstart = new iCalDateTime(
				period.StartTime.Year,
				period.StartTime.Month,
				period.StartTime.Day,
				evt.DTStart.Hour,
				evt.DTStart.Minute,
				evt.DTStart.Second,
				evt.DTStart.TZID,
				ical);

			evt.DTStart = evt.Start = dtstart;

			if (evt.DTEnd != null)
			{
				var dtend = new iCalDateTime(
					period.EndTime.Year,
					period.EndTime.Month,
					period.EndTime.Day,
					evt.DTEnd.Hour,
					evt.DTEnd.Minute,
					evt.DTEnd.Second,
					evt.DTEnd.TZID,
					ical);

				evt.DTEnd = evt.End = dtend;
			}
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

			dtstart = Utils.UtcDateTimeFromiCalDateTime(evt.DTStart, tzinfo);
			dtend = (evt.DTEnd == null) ? new Utils.DateTimeWithZone(DateTime.MinValue, tzinfo) : Utils.UtcDateTimeFromiCalDateTime(evt.DTEnd, this.calinfo.tzinfo);

			if (evt.Categories != null && evt.Categories.Count() > 0)
			{
				var categories = evt.Categories.First().ToString();
				es.AddEvent(evt.Summary, evt.Url.ToString(), source, dtstart, dtend, evt.IsAllDay, categories);
			}
			else
				es.AddEvent(evt.Summary, evt.Url.ToString(), source, dtstart, dtend, evt.IsAllDay);

			AddEventToDDayIcal(ical_ical, evt);

			fr.stats[feedurl].loaded++;
		}

		// normalize url, description, location, category properties
		private DDay.iCal.Components.Event NormalizeIcalEvt(DDay.iCal.Components.Event evt, string feedurl)
		{
			try
			{
				if (evt.Url == null) evt.Url = "";
				if (evt.Description == null) evt.Description = "";
				if (evt.Location == null) evt.Location = "";

				var feed_metadict = new Dictionary<string, string>();
				if (per_feed_metadata_cache.ContainsKey(feedurl))
					feed_metadict = per_feed_metadata_cache[feedurl];
				else
				{
					try
					{
						feed_metadict = delicious.LoadFeedMetadataFromAzureTableForFeedurlAndId(feedurl, this.calinfo.delicious_account);
						per_feed_metadata_cache[feedurl] = feed_metadict;
					}
					catch (Exception e)
					{
						GenUtils.LogMsg("exception", "NormalizeIcalEvt", e.Message + e.StackTrace);
					}
				}

				// first apply the feed-level url if any

				if (feed_metadict.ContainsKey("url") && String.IsNullOrEmpty(evt.Url.ToString()))
					evt.Url = feed_metadict["url"];

				// then the event-level url if Description is Url

				if (String.IsNullOrEmpty(evt.Url.ToString()))
					if ((!String.IsNullOrEmpty(evt.Description)) && evt.Description.ToString().StartsWith("http://"))
						evt.Url = evt.Description.ToString();

				// or if in Location is Url

				if (String.IsNullOrEmpty(evt.Url.ToString()) && evt.Location.ToString().StartsWith("http://"))
					evt.Url = evt.Location.ToString();

				// or if Description has url=Url

				var metadata_from_description = GenUtils.RegexFindKeysAndValues(Configurator.ical_description_metakeys, evt.Description);

				if (metadata_from_description.ContainsKey("url"))
					evt.Url = metadata_from_description["url"];

				// apply feed-level categories if any

				MaybeAddCategoriesToEvt(evt, feed_metadict);

				// apply event-level categories from Description, if any

				if (metadata_from_description.ContainsKey("category"))
					evt.AddCategory(metadata_from_description["category"]);

				// maybe report Url

				if (evt.Location.ToString().Contains(evt.Url.ToString()) == false)
					evt.Location = string.Format("(see {0})", evt.Url.ToString());

				if ((!String.IsNullOrEmpty(evt.Url.ToString())) && evt.Description.ToString().Contains(evt.Url.ToString()) == false)
				{
					var sb = new StringBuilder(evt.Description);
					string new_desc = string.Format(" (For more info: {0}.)", evt.Url.ToString());
					evt.Description = sb.Append(new_desc).ToString();
				}

			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", this.id + ": adjust_ical_evt", e.Message + e.StackTrace);
			}

			return evt;

		}

		private static void MaybeAddCategoriesToEvt(DDay.iCal.Components.Event evt, Dictionary<string, string> metadict)
		{
			if (metadict.ContainsKey("category"))
			{
				var cats = metadict["category"];
				var catlist = cats.Split(',');
				foreach (var cat in catlist)
					evt.AddCategory(cat);
			}
		}

		// produce the sources widget used in the default html rendering
		public static string GetIcalSources(string id)
		{
			var fr = new FeedRegistry(id);
			var delicious = Delicious.MakeDefaultDelicious();
			fr.LoadFeedsFromAzure();
			var html = "";
			var feeds = fr.feeds;
			string current_feedurl = "";
			try
			{
				foreach (var feedurl in Collector.IcalFeedurlsBySource(fr))
				{
					current_feedurl = feedurl;
					var source = feeds[feedurl];
					var metadict = delicious.LoadFeedMetadataFromAzureTableForFeedurlAndId(feedurl, id);
					var home_url = metadict.ContainsKey("url") ? metadict["url"] : "";
					if (String.IsNullOrEmpty(home_url))
						html += string.Format(@"{0} (<a title=""iCalendar feed"" href=""{1}"">*</a>), ", source, feedurl);
					else
						html += string.Format(@"<a href=""{0}"">{1}</a> (<a title=""iCalendar feed"" href=""{2}"">*</a>), ", home_url, source, feedurl);
				}
				html = GenUtils.RegexReplace(html, ", $", "");
				html += @"<p style=""text-align:right""><a href=""javascript:hide('sources')"">close window</a></p>";
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("CalendarRenderer (get_ical_sources): " + id, e.Message + e.StackTrace, current_feedurl + "," + feeds[current_feedurl]);
			}

			return html;
		}

		// get list of source feedurls ordered by source name
		public static List<string> IcalFeedurlsBySource(FeedRegistry fr)
		{
			var sorted_items = from k in fr.feeds.Keys
							   orderby fr.feeds[k] ascending
							   select k;
			return sorted_items.ToList();
		}

		// alter feed url if it should be handled by the internal "fusecal" service
		// todo: make this table-driven from an azure table
		public string MaybeRedirectFeedUrl(string str_url, Dictionary<string,string> feed_metadict)
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
				str_final_url = String.Format(Configurator.fusecal_service, Uri.EscapeUriString(str_url), filter, tz_source, tz_dest);
			}

			groups = GenUtils.RegexFindGroups(str_url, "(librarything.com/local/place/)(.+)");
			if (groups.Count == 3)
			{
				var place = groups[2];
				var radius = this.calinfo.radius;
				var lt_url = Uri.EscapeUriString(string.Format("http://www.librarything.com/rss/events/location/{0}/distance={1}",
					place, radius));
				str_final_url = String.Format(Configurator.fusecal_service, lt_url, filter, tz_source, tz_dest);
			}

			groups = GenUtils.RegexFindGroups(str_url, "(myspace.com/)(.+)");
			if (groups.Count == 3)
			{
				var musician = groups[2];
				var ms_url = Uri.EscapeUriString(string.Format("http://www.myspace.com/{0}", musician));
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
		public string MaybeXcalToIcsFeedUrl(string str_url, Dictionary<string,string> feed_metadict)
		{
			string str_final_url = str_url;
			try
			{
				var tzname = this.calinfo.tzname;
				var source = feed_metadict["source"];
				if (feed_metadict.ContainsKey("xcal"))
				{
					str_final_url = String.Format(Configurator.ics_from_xcal_service, // ics_from_xcal?url={0}&tzname={1}&source={2}";
							Uri.EscapeDataString(str_url),
							tzname,
							source);
				}
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "MaybeXcalToIcsFeedUrl", e.Message + e.StackTrace);
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
		private void AddTimezoneToDDayICal(DDay.iCal.iCalendar ical)
		{
			var timezone = DDay.iCal.Components.iCalTimeZone.FromSystemTimeZone(this.calinfo.tzinfo);
			ical.AddChild(timezone);
		}

		#endregion ical

		#region eventful

		public void CollectEventful(ZonedEventStore es, bool test)
		{
			using (eventful_ical)
			{
				string location = string.Format("{0},{1}", this.lat, this.lon);
				var page_size = test ? test_pagesize : 100;
				string args = string.Format("date=Future&location={0}&within={1}&units=mi&page_size={2}", location, this.calinfo.radius, page_size);
				string method = "events/search";
				var xdoc = CallEventfulApi(method, args);
				var str_page_count = XmlUtils.GetXeltValue(xdoc.Root, ElmcityUtils.Configurator.no_ns, "page_count");
				int page_count = test ? test_pagecount : Convert.ToInt16(str_page_count);
				var msg = string.Format("{0}: loading {1} eventful events", this.id, page_count * page_size);
				GenUtils.LogMsg("info", msg, null);

				var unique = new Dictionary<string, XElement>(); // for coalescing duplicates

				Dictionary<string, int> event_count_by_venue = new Dictionary<string, int>();

				//Parallel.ForEach(EventfulIterator(page_count, args), evt =>
				foreach (XElement evt in EventfulIterator(page_count, args))
				{
					var ns = ElmcityUtils.Configurator.no_ns;
					var title = XmlUtils.GetXeltValue(evt, ns, "title");
					var start_time = XmlUtils.GetXeltValue(evt, ns, "start_time");
					var venue_name = XmlUtils.GetXeltValue(evt, ns, "venue_name");
					var url = XmlUtils.GetXeltValue(evt, ns, "url");

					/* experimental exclusion filter, idle for now
					if (Utils.ListContainsItemStartingWithString(this.excluded_urls, url))
					{
						GenUtils.LogMsg("info", "CollectEventful: " + id, "excluding " + url);
						continue;
					}*/

					var key = venue_name + title + start_time;

					if (unique.ContainsKey(key) == false)
						unique[key] = evt;
				}
				//)
				;

				foreach (string key in unique.Keys)
				{
					var evt = unique[key];
					var venue_name = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "venue_name");
					IncrementEventCountByVenue(event_count_by_venue, venue_name);
					AddEventfulEvent(es, venue_name, evt);
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

			var event_id = evt.Attribute("id").Value;
			var event_owner = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "owner");
			var title = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "title");
			var venue_url = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "venue_url");
			var all_day = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, "all_day") == "1";

			// idle for now, but this was a way to enable curators to associate venues with tags,
			// such that all events at the venue receive the tag
			//var metadict = get_venue_metadata(venue_meta_cache, "eventful", this.id, venue_url);
			//string categories = metadict.ContainsKey("category") ? metadict["category"] : null;

			string categories = null;

			string event_url = "http://eventful.com/events/" + event_id;
			string source = "eventful: " + venue_name;

			estats.eventcount++;

			var evt_tmp = MakeTmpEvt(dtstart_with_tz, title, event_url, source, all_day, use_utc: false);

			AddEventToDDayIcal(eventful_ical, evt_tmp);

			var min = Utils.DateTimeWithZone.MinValue(this.calinfo.tzinfo);

			if (categories == null)
				es.AddEvent(title, event_url, source, dtstart_with_tz, min, all_day);
			else  // this branch idle for now
				es.AddEvent(title, event_url, source, dtstart_with_tz, min, all_day, categories);
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
				var min_date = string.Format("{0:yyyy-MM-dd}", Utils.MidnightInTz(this.calinfo.tzinfo).LocalTime);
				var args = string.Format("location={0},{1}&radius={2}&min_date={3}", this.lat, this.lon, this.calinfo.radius, min_date);
				var method = "event.search";
				var xdoc = CallUpcomingApi(method, args);
				int page_count = 1;
				try
				{
					var str_result_count = xdoc.Document.Root.Attribute("resultcount").Value;
					int result_count = Convert.ToInt32(str_result_count);
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

				foreach (XElement evt in UpcomingIterator(page_count, method, args))
				{
					string str_start_date = evt.Attribute("start_date").Value; //2010-07-07
					string str_dtstart = evt.Attribute("utc_start").Value;     //2010-07-21 18:00:00 UTC
					str_dtstart = str_start_date + str_dtstart.Substring(10, 13);
					DateTime dtstart = Utils.LocalDateTimeFromUtcDateStr(str_dtstart, this.calinfo.tzinfo);
					var dtstart_with_zone = new Utils.DateTimeWithZone(dtstart, this.calinfo.tzinfo);

					if (dtstart_with_zone.UniversalTime < Utils.MidnightInTz(this.calinfo.tzinfo).UniversalTime)
						continue;

					var venue_name = evt.Attribute("venue_name").Value;
					IncrementEventCountByVenue(event_count_by_venue, venue_name);
					AddUpcomingEvent(es, venue_name, evt, dtstart_with_zone);
				}
				ustats.venuecount = event_count_by_venue.Keys.Count;
				ustats.whenchecked = DateTime.Now.ToUniversalTime();

				SerializeStatsAndIntermediateOutputs(es, upcoming_ical, ustats, EventFlavor.upcoming);
			}
		}

		public void AddUpcomingEvent(ZonedEventStore es, string venue_name, XElement evt, Utils.DateTimeWithZone dtstart)
		{
			var title = evt.Attribute("name").Value;
			var event_url = "http://upcoming.yahoo.com/event/" + evt.Attribute("id").Value;
			var source = "upcoming: " + venue_name;
			var venue_id = evt.Attribute("venue_id").Value;
			var venue_url = "http://upcoming.yahoo.com/venue/" + venue_id;

			var all_day = String.IsNullOrEmpty(evt.Attribute("start_time").Value);

			// see eventful above: idle for now
			//var metadict = get_venue_metadata(venue_meta_cache, "upcoming", this.id, venue_url);
			//var categories = metadict.ContainsKey("category") ? metadict["category"] : null;
			string categories = null;

			ustats.eventcount++;

			var evt_tmp = MakeTmpEvt(dtstart, title, event_url, source, all_day, use_utc: true);

			AddEventToDDayIcal(upcoming_ical, evt_tmp);

			var min = Utils.DateTimeWithZone.MinValue(this.calinfo.tzinfo);

			if (categories == null)
				es.AddEvent(title, event_url, source, dtstart, min, all_day);
			else // idle for now
				es.AddEvent(title, event_url, source, dtstart, min, all_day, categories);
		}

		public IEnumerable<XElement> UpcomingIterator(int page_count, string method, string args)
		{
			for (int i = 1; i <= page_count; i++)
			{
				var this_args = string.Format("{0}&page={1}", args, i);
				var xdoc = CallUpcomingApi(method, this_args);
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
				var min_date = string.Format("{0:yyyy-MM-dd}", Utils.MidnightInTz(this.calinfo.tzinfo).LocalTime);
				var dt_min = Utils.DateTimeFromDateStr(min_date + " 00:00");
				var dt_max = dt_min.AddDays(Configurator.icalendar_horizon_in_days);
				var max_date = string.Format("{0:yyyy-MM-dd}", dt_max);
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

				foreach (XElement evt in EventBriteIterator(page_count, method, args))
				{
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
			var all_day = start_dt.Hour == 0 && start_dt.Minute == 0;

			ebstats.eventcount++;

			var evt_tmp = MakeTmpEvt(dtstart, title, event_url, source, all_day, use_utc: true);

			AddEventToDDayIcal(eventbrite_ical, evt_tmp);

			var min = Utils.DateTimeWithZone.MinValue(this.calinfo.tzinfo);

			es.AddEvent(title, event_url, source, dtstart, min, all_day);
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
				GenUtils.LogMsg("exception", "CallEventBriteApi", e.Message + e.StackTrace);
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

				foreach (FacebookEvent fb_event in FacebookIterator(method, args))
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

			var evt_tmp = MakeTmpEvt(dtstart, title, event_url, source, all_day, use_utc: false);

			AddEventToDDayIcal(facebook_ical, evt_tmp);

			var min = Utils.DateTimeWithZone.MinValue(this.calinfo.tzinfo);

			es.AddEvent(title, event_url, source, dtstart, min, all_day);
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
				GenUtils.LogMsg("exception", "CallFacebookApi", e.Message + e.StackTrace);
				return "";
			}
		}

		#endregion

		private iCalendar NewCalendarWithTimezone()
		{
			var ical = new iCalendar();
			AddTimezoneToDDayICal(ical);
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
		}

		public static DDay.iCal.Components.Event MakeTmpEvt(Utils.DateTimeWithZone dtstart, string title, string event_url, string source, bool allday, bool use_utc)
		{
			iCalendar ical = new iCalendar();
			DDay.iCal.Components.Event evt = new DDay.iCal.Components.Event(ical);
			evt.Summary = title;
			evt.Url = event_url;
			evt.Location = event_url;
			evt.Description = source;
			evt.DTStart = (use_utc) ? dtstart.UniversalTime : dtstart.LocalTime;
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
			var utc_dtstart = Utils.UtcDateTimeFromiCalDateTime(ical_dtstart, this.calinfo.tzinfo);
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
						GenUtils.LogMsg("exception", "within_range", e.Message + e.StackTrace);
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
}

