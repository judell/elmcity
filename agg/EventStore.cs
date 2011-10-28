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

		private string _categories;

		public string id { get; set; }

		public Uri uri { get; set; }

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

		public static string MakeEventUid(DDay.iCal.Event evt)
		{
			//var ticks = DateTime.Now.Ticks;
			//var randnum = Utils._random.Next();
			// return string.Format("{0}-{1}@{2}", ticks, randnum, ElmcityUtils.Configurator.appdomain);
			var summary_bytes = System.Text.Encoding.UTF8.GetBytes(evt.Summary.ToString());
			return string.Format("{0}-{1}@{2}", Convert.ToBase64String(summary_bytes), evt.DTStart.Ticks, ElmcityUtils.Configurator.appdomain);
		}

		public void NormalizeTitle()
		{
			this.title = Regex.Replace(this.title, "[\"\']+", "");
			this.title = Regex.Replace(this.title, @"[\s]+", " ");
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
			DateTimeWithZone dtstart, DateTimeWithZone dtend, string description) :
			base(title, url, source, allday, lat, lon, categories, description)
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

		public ZonelessEvent(string title, string url, string source, bool allday, string lat, string lon, string categories,
			DateTime dtstart, DateTime dtend, string description) :
			base(title, url, source, allday, lat, lon, categories, description)
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

		public static void CombineZonedEventStoresToZonelessEventStore(string id, Dictionary<string, string> settings)
		{
			var bs = BlobStorage.MakeDefaultBlobStorage();
			var calinfo = new Calinfo(id);

			var lists_of_zoned_events = new List<List<ZonedEvent>>();

			var ical_uri = BlobStorage.MakeAzureBlobUri(container: id, name: id + "." + SourceType.ical + ".zoned.obj");

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
					non_ical_uri = BlobStorage.MakeAzureBlobUri(container: id, name: id + "." + type + ".zoned.obj");
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
				//	if (evt.dtstart.LocalTime.Kind != DateTimeKind.Local)  // might be unspecified, that's ok
				//		GenUtils.PriorityLogMsg("warning", "CombineZonedEventStores: expecting DateTimeKind.Local, got " + evt.dtstart.LocalTime.Kind.ToString(), null);
					es_zoneless.AddEvent(evt.title, evt.url, evt.source, evt.lat, evt.lon, evt.dtstart.LocalTime, evt.dtend.LocalTime, evt.allday, evt.categories, evt.description);
				}

			es_zoneless.events = EventStore.UniqueByTitleAndStart(es_zoneless.events); // deduplicate

			es_zoneless.ExcludePastEvents(); // the EventCollector should already have done this, but in case not...

			es_zoneless.SortEventList();     // order by dtstart

			es_zoneless.Serialize();
		}

		public static List<ZonelessEvent> UniqueByTitleAndStart(List<ZonelessEvent> events)
		{
			 var uniques = new Dictionary<string, ZonelessEvent>(); 

			var tags = new Dictionary<string, List<string>>();
			var dict_of_urls_and_sources = new Dictionary<string, Dictionary<string, string>>() { };

			foreach (var evt in events)              // build keyed structures
			{
				var key = evt.TitleAndTime();

				evt.url = Utils.NormalizeEventfulUrl(evt.url);        // try url normalizations
				evt.url = Utils.NormalizeUpcomingUrl(evt.url);
				
				if ( evt.categories != null )
					tags.AddOrUpdateDictOfListStr(key, evt.categories.Split(',').ToList());  // update keyed tag list for this key

				evt.urls_and_sources = new Dictionary<string, string>() { { evt.url, evt.source } }; // create url/source dict for this key

				dict_of_urls_and_sources.AddOrUpdateDictOfDictStr(key, evt.urls_and_sources );  // update keyed url/source dict for this key
			}

			foreach ( var evt in events )            // use keyed structures
			{
				var key = evt.TitleAndTime();

				if (tags.ContainsKey(key))
					evt.categories = string.Join(",", tags[key]);                 // assign each event its keyed tag union

				evt.urls_and_sources = dict_of_urls_and_sources[key];                 // assign each event its keyed url/source dict

				uniques.AddOrUpdateDictionary<string, ZonelessEvent>(key, evt);      // deduplicate
			}

			return (List<ZonelessEvent>) uniques.Values.ToList();
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
			this.uri = BlobStorage.MakeAzureBlobUri(this.id, this.objfile);
		}

		public void AddEvent(string title, string url, string source, DateTimeWithZone dtstart, DateTimeWithZone dtend, string lat, string lon, bool allday, string categories, string description)
		{
			ZonedEvent evt = new ZonedEvent(title: title, url: url, source: source, dtstart: dtstart, dtend: dtend, lat: lat, lon: lon, allday: allday, categories: categories, description: description);
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

		// used to chunk the list by datekey (e.g. "d2010-07-04") for the convenience of renderers
		public Dictionary<string, List<ZonelessEvent>> event_dict = new Dictionary<string, List<ZonelessEvent>>();
		public List<string> datekeys = new List<string>();

		public ZonelessEventStore(Calinfo calinfo)
			: base(calinfo)
		{
			this.objfile = this.id + ".zoneless.obj";
			this.uri = BlobStorage.MakeAzureBlobUri(this.id, this.objfile);
		}

		public void AddEvent(string title, string url, string source, string lat, string lon, DateTime dtstart, DateTime dtend, bool allday, string categories, string description)
		{
			var evt = new ZonelessEvent(title: title, url: url, source: source, dtstart: dtstart, dtend: dtend, lat: lat, lon: lon, allday: allday, categories: categories, description: description);
			evt.NormalizeTitle();
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
			this.events = this.events.OrderBy(evt => evt.dtstart).ToList();
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

		// order the events within each day chunk, for convenience of renderers.
		// put times like 00:00:00 at the end, so they land in the All Day bucket.
		// note: it is not necessarily true that a time of midnight means an event is all-day, 
		// and it really shouldn't mean that, but sources often use that convention.
		public void SortEventSublists()
		{
			var sorted_dict = new Dictionary<string, List<ZonelessEvent>>();

			foreach (string datekey in event_dict.Keys)
			{
				List<ZonelessEvent> list = event_dict[datekey];

				IEnumerable<ZonelessEvent> sorted =
					from evt in list
					orderby evt.dtstart.TimeOfDay ascending, evt.title ascending
					select evt;

				list = sorted.ToList();

				var events_having_dt = list.FindAll(evt => IsZeroHourMinSec(evt) == false);
				var events_not_having_dt = list.FindAll(evt => IsZeroHourMinSec(evt) == true);
				sorted_dict[datekey] = events_having_dt;

				foreach (var evt in events_not_having_dt)
					sorted_dict[datekey].Add(evt);
			}
	
			this.event_dict = sorted_dict;
		}

		// populate the dict of day chunks
		public void GroupEventsByDatekey()
		{
			var dict = new Dictionary<string, List<ZonelessEvent>>();
			foreach (ZonelessEvent evt in events)
			{
				string datekey = Utils.DateKeyFromDateTime(evt.dtstart);

				if (!dict.ContainsKey(datekey))
					dict[datekey] = new List<ZonelessEvent>();

				dict[datekey].Add(evt);
			}
			this.event_dict = dict;
		 }

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
