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
using System.Collections.Generic;
using System.Linq;
using ElmcityUtils;
using System.Text.RegularExpressions;

namespace CalendarAggregator
{
	// the core event object contains as few fields as possible.
	// why? because the service aims to:
	// 1. have the lowest possible activation threshold for events entering the system
	// 2. respect the authority of original sources (by linking back to them)
	[Serializable]
	public class Event
	{
		public string title
		{
			get { return _title; }
			set { _title = value; }
		}
		private string _title;

		public string url
		{
			get { return _url; }
			set { _url = value; }
		}
		private string _url;

		public string source
		{
			get { return _source; }
			set { _source = value; }
		}
		private string _source;

		public bool allday
		{
			get { return _allday; }
			set { _allday = value; }
		}
		private bool _allday;

		public string lat
		{
			get { return _lat; }
			set { _lat = value; }
		}

		private string _lat;

		public string lon
		{
			get { return _lon; }
			set { _lon = value; }
		}

		private string _lon;

		public string categories
		{
			get { return _categories; }
			set { _categories = value; }
		}

		private string _description;

		public string description
		{
			get { return _description; }
			set { _description = value; }
		}

		private string _location;

		public string location
		{
			get { return _location; }
			set { _location = value; }
		}

		private string _categories;

		//public string id { get; set; }

		//public Uri uri { get; set; }

		public Event(string title, string url, string source, bool allday, string categories)
		{
			this.title = title;
			this.url = url;
			this.source = source;
			this.allday = allday;
			this.categories = categories;
		}

		public Event(string title, string url, string source, bool allday, string lat, string lon, string categories)
		{
			this.title = title;
			this.url = url;
			this.source = source;
			this.allday = allday;
			this.lat = lat;
			this.lon = lon;
			this.categories = categories;
		}

		public Event(string title, string url, string source, bool allday, string lat, string lon, string categories, string description)
		{
			this.title = title;
			this.url = url;
			this.source = source;
			this.allday = allday;
			this.lat = lat;
			this.lon = lon;
			this.categories = categories;
			this.description = description;
		}

		public Event(string title, string url, string source, bool allday, string lat, string lon, string categories, string description, string location)
		{
			this.title = title;
			this.url = url;
			this.source = source;
			this.allday = allday;
			this.lat = lat;
			this.lon = lon;
			this.categories = categories;
			this.description = description;
			this.location = location;
		}

		public static string MakeEventUid(DDay.iCal.Event evt)
		{
			//var ticks = DateTime.Now.Ticks;
			//var randnum = Utils._random.Next();
			// return string.Format("{0}-{1}@{2}", ticks, randnum, ElmcityUtils.Configurator.appdomain);
			var summary_bytes = System.Text.Encoding.UTF8.GetBytes(evt.Summary.ToString());
			return string.Format("{0}-{1}@{2}", Convert.ToBase64String(summary_bytes), evt.DTStart.Ticks, ElmcityUtils.Configurator.appdomain);
		}

	}

	// the aggregator saves all intermediate results as ZonedEvent objects, e.g.:
	// http://elmcity.blob.core.windows.net/elmcity/elmcity.ical.zoned.obj
	// why?
	// it's useful to work internally in terms of Utils.DateTimeWithZone objects that encapsulate
	// a UTC datetime plus a TimeZoneInfo
	[Serializable]
	public class ZonedEvent : Event
	{
		public DateTimeWithZone dtstart;
		public DateTimeWithZone dtend;

		public ZonedEvent(string title, string url, string source, bool allday, string lat, string lon, string categories, 
			DateTimeWithZone dtstart, DateTimeWithZone dtend, string description, string location) :
			base(title, url, source, allday, lat, lon, categories, description, location)
		{
			this.dtstart = dtstart;
			this.dtend = dtend;
		}
	}

	// the agggregator combines intermediate results into a pickled list of ZonelessEvent objects
	// for the convenience of renderers, on the assumption they only care about local time
	[Serializable]
	public class ZonelessEvent : Event
	{
		public DateTime dtstart;
		public DateTime dtend;
		public Dictionary<string,string> urls_and_sources { get; set; } 
		//public List<List<string>> list_of_urls_and_sources { get; set; }
		public string original_categories;
		public int? uid;  // nullable only for transitional reasons

		public ZonelessEvent(string title, string url, string source, bool allday, string lat, string lon, string categories,
			DateTime dtstart, DateTime dtend, string description, string location) :
			base(title, url, source, allday, lat, lon, categories, description, location)
		{
			this.dtstart = dtstart;
			this.dtend = dtend;
		}
	}

	[Serializable]
	public class EventStore 
	{
		public string id { get; set; }

		// the datekey looks like d2010-07-04
		// used by, e.g., CalendarRenderer.RenderEventsAsHtml when creating fragment identifiers
		// (e.g. <a name="d2010-07-04"/> ) in the default html rendering
		public const string datekey_pattern = @"d(\d{4})(\d{2})(\d{2})";

		public static List<string> non_ical_types
		{ get { return _non_ical_types; } }

		private static List<string> _non_ical_types = new List<string>() { "eventful", "upcoming", "eventbrite", "facebook" };

		public string objfile { get; set; }

		public Uri uri { get; set; }

		public System.TimeZoneInfo tzinfo { get; set; }

		public EventStore(Calinfo calinfo)
		{
			this.id = calinfo.id;
			this.tzinfo = calinfo.tzinfo;
		}

		public BlobStorageResponse Serialize()
		{
			var bs = BlobStorage.MakeDefaultBlobStorage();
			return bs.SerializeObjectToAzureBlob(this, this.id, this.objfile);
		}

		private static void DeserializeZoned(Uri uri, List<List<ZonedEvent>> lists_of_zoned_events)
		{
			try
			{
				if (BlobStorage.ExistsBlob(uri))
				{
					var es = (ZonedEventStore)BlobStorage.DeserializeObjectFromUri(uri);
					lists_of_zoned_events.Add(es.events);
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "DeserializeZoned: " + uri.ToString(), e.Message + e.StackTrace);
			}
		}

		public static void Finalize(Calinfo calinfo, ZonelessEventStore es_zoneless)
		{
			es_zoneless.events = EventStore.UniqueByTitleAndStart(calinfo.id, es_zoneless.events, save_tag_sources: true); // deduplicate

			es_zoneless.ExcludePastEvents(); // the EventCollector should already have done this, but in case not...

			es_zoneless.SortEventList();     // order by dtstart

			Utils.BuildTagStructures(es_zoneless, calinfo);  // build structures used to generate tag picklists

			int uid = 0;
			foreach (var evt in es_zoneless.events)
			{
				evt.uid = uid;
				uid++;
			}

			es_zoneless.when_finalized = DateTime.UtcNow;

			es_zoneless.Serialize();
		}

		public static void CombineZonedEventStoresToZonelessEventStore(string id, Dictionary<string, string> settings)
		{
			var bs = BlobStorage.MakeDefaultBlobStorage();
			var calinfo = new Calinfo(id);

			var lists_of_zoned_events = new List<List<ZonedEvent>>();

			var ical_uri = BlobStorage.MakeAzureBlobUri(container: id, name: id + "." + SourceType.ical + ".zoned.obj", use_cdn: false);

			DeserializeZoned(ical_uri, lists_of_zoned_events);

			//if (calinfo.hub_type == HubType.where.ToString())
			if ( calinfo.hub_enum == HubType.where )
			{
				Uri non_ical_uri;
				//foreach (var type in non_ical_types)
				foreach (NonIcalType type in Enum.GetValues(typeof(CalendarAggregator.NonIcalType)))
				{
					if (Utils.UseNonIcalService(type, settings, calinfo) == false)
						continue;
					non_ical_uri = BlobStorage.MakeAzureBlobUri(container: id, name: id + "." + type + ".zoned.obj", use_cdn:false);
					if (BlobStorage.ExistsBlob(non_ical_uri)) // // might not exist, e.g. if facebook=no in hub metadata
						DeserializeZoned(non_ical_uri, lists_of_zoned_events);
				}
			}

			var es_zoneless = new ZonelessEventStore(calinfo);

			// combine the various List<ZonedEvent> objects into our new ZonelessEventStore
			// always add the local time
			foreach (var list in lists_of_zoned_events)
				foreach (var evt in list)
				{
					es_zoneless.AddEvent(evt.title, evt.url, evt.source, evt.lat, evt.lon, evt.dtstart.LocalTime, evt.dtend.LocalTime, evt.allday, evt.categories, evt.description, evt.location);
				}

			Finalize(calinfo, es_zoneless);
		}

		public static bool SimilarButNonIdenticalUrls(ZonelessEvent evt1, ZonelessEvent evt2, int min_length)
		{
			if (evt1.url == null || evt1.url.Length < min_length || evt2.url == null || evt2.url.Length < min_length )
				return false;

			if (evt1.url.Substring(0, min_length) == evt2.url.Substring(0, min_length))
				if (evt1.url != evt2.url)
					return true;

			return false;
		}

		public static void MatchSimilarTitles(DateTime dt, Dictionary<DateTime, List<ZonelessEvent>> dt_dict, Dictionary<string,string> settings)
		{
			int min_word = 3;
			int min_words_in_common = 4;
			int min_url_prefix = 16;

			try
			{
				min_word = Convert.ToInt32(settings["fuzzy_title_match_min_word_length"]);
				min_words_in_common = Convert.ToInt32(settings["fuzzy_title_match_min_common_words"]);
				min_url_prefix = Convert.ToInt32(settings["fuzzy_title_match_min_url_compare_prefix"]);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("warning", "unable to load settings for MatchSimilarTitles", e.Message + e.StackTrace);
			}

			
			var candidates = new List<ZonelessEvent>();    

			foreach (ZonelessEvent candidate in dt_dict[dt])
			{
				foreach (ZonelessEvent _candidate in dt_dict[dt])
				{
					if ( candidate.title == _candidate.title )
						continue;  // either found myself, or an exact match that will be coalesced later 

					var title_words = Utils.WordsFromEventTitle(candidate.title, min_word);
					var _title_words = Utils.WordsFromEventTitle(_candidate.title, min_word);

					var min_common_words = Math.Min(min_words_in_common, title_words.Count);
					min_common_words = Math.Min(min_common_words, _title_words.Count);

					if (min_common_words < min_words_in_common)
						continue;

					if (title_words.Intersect(_title_words).ToList().Count >= min_common_words)
						if ( ! SimilarButNonIdenticalUrls(candidate, _candidate, min_url_prefix))
							_candidate.title = candidate.title;  // force the match
				}
			}


		}

		public static List<ZonelessEvent> UniqueByTitleAndStart(string id, List<ZonelessEvent> events, bool save_tag_sources) 
		{
			var tag_sources = new Dictionary<string, Dictionary<string, int>>();
			
			var uniques = new Dictionary<string, ZonelessEvent>(); 

			var merged_tags = new Dictionary<string, List<string>>();
			var all_urls_and_sources = new Dictionary<string, Dictionary<string, string>>();

			var dt_dict = new Dictionary<DateTime, List<ZonelessEvent>>();    // fill up datetime buckets for matching
			foreach (ZonelessEvent evt in events)
				dt_dict.AddOrAppendDictOfListT(evt.dtstart, evt);

			var settings = GenUtils.GetSettingsFromAzureTable();

			foreach (var dt in dt_dict.Keys)       // match similar titles within buckets
				MatchSimilarTitles(dt, dt_dict, settings);

			var _events = new List<ZonelessEvent>();
			foreach (var dt in dt_dict.Keys)         // flatten dt_dict back to list of evt
				foreach (var evt in dt_dict[dt])
					_events.Add(evt);

			foreach (var evt in _events)              // build keyed structures
			{
				var key = evt.TitleAndTime();

				evt.url = Utils.NormalizeEventfulUrl(evt.url);        // try url normalizations
				evt.url = Utils.NormalizeUpcomingUrl(evt.url);

				if (evt.categories != null)
				{
					var tags = evt.categories.Split(',').ToList();
					foreach (var tag in tags)
					{
						if (tag_sources.ContainsKey(tag))
							tag_sources[tag].IncrementOrAdd<string>(evt.source);
						else
							tag_sources[tag] = new Dictionary<string, int>() { { evt.source, 1 } };
					}
					merged_tags.AddOrUpdateDictOfListStr(key, tags);  // update keyed tag list for this key
				}
				
				if (all_urls_and_sources.ContainsKey(key)) // update keyed url/source list for this key
					all_urls_and_sources[key][evt.url] = evt.source;
				else
					all_urls_and_sources[key] = new Dictionary<string, string>() { { evt.url, evt.source } };
			}

			if (save_tag_sources && id != null)
			{
				var bs = BlobStorage.MakeDefaultBlobStorage();
				bs.SerializeObjectToAzureBlob(tag_sources, id, "tag_sources.obj");
			}

			foreach ( var evt in _events )            // use keyed structures
			{
				var key = evt.TitleAndTime();

				if (merged_tags.ContainsKey(key))
				{
					evt.original_categories = evt.categories;                     // remember original categories for reporting
					var tags = merged_tags[key].Unique().ToList();
					tags.Sort(String.CompareOrdinal);
					evt.categories = string.Join(",", tags);          // assign each event its keyed tag union
				}

				// evt.list_of_urls_and_sources = all_urls_and_sources[key];		  // assign each event its keyed url/source pairs
				evt.urls_and_sources = all_urls_and_sources[key];

				uniques.AddOrUpdateDictionary<string, ZonelessEvent>(key, evt);      // deduplicate
			}

			return (List<ZonelessEvent>) uniques.Values.ToList();
		}

		public static ZonelessEventStore ZonedToZoneless(string id, Calinfo calinfo, ZonedEventStore es_zoned)
		{
			var es_zoneless = new ZonelessEventStore(calinfo);
			foreach (var evt in es_zoned.events)
				es_zoneless.AddEvent(evt.title, evt.url, evt.source, evt.lat, evt.lon, evt.dtstart.LocalTime, evt.dtend.LocalTime, evt.allday, evt.categories, evt.description, evt.location);
			EventStore.Finalize(calinfo, es_zoneless);
			return es_zoneless;
		}
	}

	[Serializable]
	public class ZonedEventStore : EventStore
	{
		public List<ZonedEvent> events = new List<ZonedEvent>();

		public ZonedEventStore(Calinfo calinfo, SourceType type)
			: base(calinfo)
		{
			// qualifier is "ical" or one of the non-ical types, so for example:
			// http://elmcity.blob.core.windows.net/a2cal/a2cal.ical.zoned.obj
			// http://elmcity.blob.core.windows.net/a2cal/a2cal.eventful.zoned.obj
			this.objfile = this.id + "." + type.ToString() + ".zoned.obj";
			this.uri = BlobStorage.MakeAzureBlobUri(this.id, this.objfile, false);
		}

		public void AddEvent(string title, string url, string source, DateTimeWithZone dtstart, DateTimeWithZone dtend, string lat, string lon, bool allday, string categories, string description, string location)
		{
			title = title.StripHtmlTags();
			if ( location != null )
				location = location.StripHtmlTags();
			if ( description != null )
				description = description.StripHtmlTags();
			ZonedEvent evt = new ZonedEvent(title: title, url: url, source: source, dtstart: dtstart, dtend: dtend, lat: lat, lon: lon, allday: allday, categories: categories, description: description, location: location);
			events.Add(evt);
		}

		public ZonedEventStore Deserialize()
		{
			var o = BlobStorage.DeserializeObjectFromUri(this.uri);
			return (ZonedEventStore)o;
		}

	}

	[Serializable]
	public class ZonelessEventStore : EventStore
	{
		public List<ZonelessEvent> events = new List<ZonelessEvent>();

		public Dictionary<string, List<ZonelessEvent>> event_dict = new Dictionary<string, List<ZonelessEvent>>();

		public List<string> datekeys = new List<string>();

		public Dictionary<string, Dictionary<string, int>> category_hubs = new Dictionary<string, Dictionary<string, int>>(); // map categories to regional hubs offering them

		public Dictionary<string, int> hubs_and_counts = new Dictionary<string, int>();   // tags denoting hubs belonging to a region, plus associated event counts
		public List<string> hub_tags = new List<string>();                                // just those tags

		public Dictionary<string, int> non_hubs_and_counts = new Dictionary<string, int>(); // tags denoting categories, plus associated counts
		public List<string> non_hub_tags = new List<string>();								// just those tags

		public Dictionary<string, List<string>> hub_name_map; // optional (but important) for regional hubs, ex: for HR { "norfolkva"		: [ "NorfolkVa" , "Norfolk"],

		public List<string> days = new List<string>();

		public Dictionary<string, int> days_and_counts = new Dictionary<string, int>();

		public DateTime when_finalized;

		public ZonelessEventStore(Calinfo calinfo)
			: base(calinfo)
		{
			this.objfile = this.id + ".zoneless.obj";
			this.uri = BlobStorage.MakeAzureBlobUri(this.id, this.objfile,false);
		}

		public void AddEvent(string title, string url, string source, string lat, string lon, DateTime dtstart, DateTime dtend, bool allday, string categories, string description, string location)
		{
			var evt = new ZonelessEvent(title: title, url: url, source: source, dtstart: dtstart, dtend: dtend, lat: lat, lon: lon, allday: allday, categories: categories, description: description, location: location);
			this.events.Add(evt);
		}

		public ZonelessEventStore Deserialize()
		{
			var o = BlobStorage.DeserializeObjectFromUri(this.uri);
			return (ZonelessEventStore)o;
		}

		// recover the UTC datetime that was in the original ZonedEvent,
		// used by CalendarRenderer.RenderJson
		public static ZonelessEvent UniversalFromLocalAndTzinfo(ZonelessEvent evt, TimeZoneInfo tzinfo)
		{
			var _dts = evt.dtstart;
			var _dtstart = new DateTime(_dts.Year, _dts.Month, _dts.Day, _dts.Hour, _dts.Minute, _dts.Second);
			evt.dtstart = TimeZoneInfo.ConvertTimeToUtc(_dtstart, tzinfo);

			if (evt.dtend != null)
			{
				var _dte = evt.dtend;
				var _dtend = new DateTime(_dte.Year, _dte.Month, _dte.Day, _dte.Hour, _dte.Minute, _dte.Second);
				evt.dtend = TimeZoneInfo.ConvertTimeToUtc(_dtend, tzinfo);
			}

			return evt;
		}

		public void SortEventList()
		{
			this.events = this.events.OrderBy(a => a.dtstart).ThenBy(a => a.source).ThenBy(a => a.title).ToList();
		}

		public void ExcludePastEvents()
		{
			this.events = this.events.FindAll(evt => Utils.IsCurrentOrFutureDateTime(evt, this.tzinfo));
		}

		// so renderers can traverse day chunks in order
		public void SortDatekeys()
		{
			var dkeys = new List<string>();
			foreach (string datekey in event_dict.Keys)
				dkeys.Add(datekey);
			this.datekeys = dkeys.OrderBy(dkey => dkey).ToList();
		}

		public static bool IsZeroHourMinSec(ZonelessEvent evt)
		{
			return (evt.dtstart.Hour == 0 && evt.dtstart.Minute == 0 && evt.dtstart.Second == 0);
		}

		// populate the dict of day chunks

		public void GroupEventsByDatekey()
		{
			var dict = new Dictionary<string, List<ZonelessEvent>>();

			foreach (ZonelessEvent evt in this.events) 
			{
				string datekey = Utils.DateKeyFromDateTime(evt.dtstart);

				if (!dict.ContainsKey(datekey))
					dict[datekey] = new List<ZonelessEvent>();

				dict[datekey].Add(evt);
			}

			this.event_dict = dict;
		}

		public void PopulateDaysAndCounts()
		{
			this.GroupEventsByDatekey();
			var keys = this.event_dict.Keys.ToList();
			keys.Sort();
			this.days = keys;
			foreach (var datekey in keys)
				this.days_and_counts[datekey] = this.event_dict[datekey].Count;
			this.event_dict = null;
		}

		public void AdvanceToAnHourAgo(Calinfo calinfo)
		{
			var now_in_tz = Utils.NowInTz(calinfo.tzinfo);         // advance to an hour ago
			var dtnow = now_in_tz.LocalTime - TimeSpan.FromHours(1);
			this.events = this.events.FindAll(evt => evt.dtstart >= dtnow);
		}

		/*
		public bool CompareTagStructures(ZonelessEventStore other)
		{
			if (other.category_hubs == null || other.non_hubs_and_counts == null || other.hubs_and_counts == null)
				return false;

			List<string> keys1;
			List<string> keys2;

			if (this.category_hubs.Count == other.category_hubs.Count)
			{
				keys1 = this.category_hubs.Keys.ToList();
				keys1.Sort();
				keys2 = other.category_hubs.Keys.ToList();
				keys2.Sort();
				if ( keys1.SequenceEqual(keys2) == false )
					return false;
			}

			if (this.non_hubs_and_counts.Count == other.non_hubs_and_counts.Count)
			{
				keys1 = this.non_hub_tags;
				keys2 = other.non_hub_tags;
				if ( keys1.SequenceEqual(keys2) == false )
					return false;
			}

			if (this.hubs_and_counts.Count == other.hubs_and_counts.Count)
			{
				keys1 = this.hub_tags;
				keys2 = other.hub_tags;
				if (keys1.SequenceEqual(keys2) == false)
					return false;
			}

			return true;
		}*/
	} 

	[Serializable]
	public class DateTimeWithZone
	{
		private DateTime _LocalTime;
		private DateTime _UniversalTime;
		private TimeZoneInfo _tzinfo;

		public DateTimeWithZone(DateTime dt, TimeZoneInfo tzinfo)
		{
			this._tzinfo = tzinfo;
			if (dt.Kind == DateTimeKind.Utc)
				GenUtils.PriorityLogMsg("warning", "DateTimeWithZone: expecting DateTimeKind.Local or Unspecified, got " + dt.Kind.ToString(), null);
			this._LocalTime = dt;
			var _dt = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);  // can't convert if kind is local
			this._UniversalTime = TimeZoneInfo.ConvertTimeToUtc(_dt, tzinfo);
		}

		public DateTime UniversalTime { get { return this._UniversalTime; } }
		public DateTime LocalTime { get { return this._LocalTime; } }

		public static DateTimeWithZone MinValue(TimeZoneInfo tz)
		{
			var min = DateTime.MinValue;
			var local_min = new DateTime(min.Year, min.Month, min.Day, min.Hour, min.Minute, min.Second, DateTimeKind.Local);
			return new DateTimeWithZone(local_min, tz);
		}

	}

}
