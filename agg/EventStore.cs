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

        public string categories
        {
            get { return _categories; }
            set { _categories = value; }
        }
        private string _categories;

        public Event(string title, string url, string source, bool allday, string categories)
        {
            this.title = title;
            this.url = url;
            this.source = source;
            this.allday = allday;
            this.categories = categories;
        }

        public static string MakeEventUid(DDay.iCal.Components.Event evt)
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
        public Utils.DateTimeWithZone dtstart;
        public Utils.DateTimeWithZone dtend;

        public ZonedEvent(string title, string url, string source, bool allday, string categories,
            Utils.DateTimeWithZone dtstart, Utils.DateTimeWithZone dtend) :
            base(title, url, source, allday, categories)
        {
            this.dtstart = dtstart;
            this.dtend = dtend;
        }
    }

    // the agggregator combines intermediate results into a pickled list of ZonelessEvents objects
    // for the convenience of renderers, on the assumption they only care about local time
    [Serializable]
    public class ZonelessEvent : Event
    {
        public DateTime dtstart;
        public DateTime dtend;

        public ZonelessEvent(string title, string url, string source, bool allday, string categories,
            DateTime dtstart, DateTime dtend) :
            base(title, url, source, allday, categories)
        {
            this.dtstart = dtstart;
            this.dtend = dtend;
        }
    }

    [Serializable]
    public class EventStore
    {
        public string id { get; set; }

        public Calinfo calinfo { get; set; }

        // the datekey looks like d2010-07-04
        // used by, e.g., CalendarRenderer.RenderEventsAsHtml when creating fragment identifiers
        // (e.g. <a name="d2010-07-04"/> ) in the default html rendering
        public const string datekey_pattern = @"d(\d{4})(\d{2})(\d{2})";

        public static List<string> non_ical_types
        { get { return _non_ical_types; } }

        private static List<string> _non_ical_types = new List<string>() { "eventful", "upcoming", "eventbrite", "facebook" };

        private BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();

        public string xmlfile { get; set; }
        public string objfile { get; set; }

        public System.TimeZoneInfo tzinfo { get; set; }

        public EventStore(Calinfo calinfo)
        {
            this.id = calinfo.delicious_account;
            this.calinfo = calinfo;
            this.tzinfo = calinfo.tzinfo;
        }

        public BlobStorageResponse Serialize(string file)
        {
            return this.bs.SerializeObjectToAzureBlob(this, this.id, file);
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
                GenUtils.LogMsg("exception", "DeserializeZoned: " + uri.ToString(), e.Message + e.StackTrace);
            }
        }

        public static void CombineZonedEventStoresToZonelessEventStore(string id)
        {
            var bs = BlobStorage.MakeDefaultBlobStorage();
            var calinfo = new Calinfo(id);

            var lists_of_zoned_events = new List<List<ZonedEvent>>();

            var ical_uri = BlobStorage.MakeAzureBlobUri(container: id, name: id + ".ical.zoned.obj");

            DeserializeZoned(ical_uri, lists_of_zoned_events);

            if (calinfo.hub_type == "where")
            {
                Uri non_ical_uri;
                foreach (var type in non_ical_types)
                {
                    non_ical_uri = BlobStorage.MakeAzureBlobUri(container: id, name: id + "." + type + ".zoned.obj");
                    if (BlobStorage.ExistsBlob(non_ical_uri)) // // might not exist, e.g. if facebook=no in hub metadata
                        DeserializeZoned(non_ical_uri, lists_of_zoned_events);
                }
            }

            var es_zoneless = new ZonelessEventStore(calinfo, null);

            // combine the various List<ZonedEvent> objects into our new ZonelessEventStore
            foreach (var list in lists_of_zoned_events)
                foreach (var evt in list)

                    es_zoneless.AddEvent(evt.title, evt.url, evt.source, evt.dtstart.LocalTime, evt.dtend.LocalTime, evt.allday, evt.categories);

            es_zoneless.ExcludePastEvents(); // the EventCollector should already have done this, but in case not...
            es_zoneless.SortEventList();     // order by dtstart
            Utils.RemoveBaseCacheEntry(id);  // purge cache entry for the pickled object being rebuilt, 
            // which also triggers purge of dependencies, so if the base entry is
            // http://elmcity.blob.core.windows.net/a2cal/a2cal.zoneless.obj
            // then dependencies also ousted from cache include:
            // /services/a2cal/html
            // /services/a2cal/rss?view=government
            // /services/a2cal/xml?view=music&count=10    ... etc.

            bs.SerializeObjectToAzureBlob(es_zoneless, id, es_zoneless.objfile); // save new object as a blob
        }
    }

    [Serializable]
    public class ZonedEventStore : EventStore
    {
        public List<ZonedEvent> events = new List<ZonedEvent>();

        public ZonedEventStore(Calinfo calinfo, string qualifier)
            : base(calinfo)
        {
            // qualifier is "ical" or one of the non-ical types, so for example:
            // http://elmcity.blob.core.windows.net/a2cal/a2cal.ical.zoned.obj
            // http://elmcity.blob.core.windows.net/a2cal/a2cal.eventful.zoned.obj
            // todo: enumerate these instead of relying on strings
            qualifier = qualifier ?? "";
            this.xmlfile = this.id + qualifier + ".zoned.xml";
            this.objfile = this.id + qualifier + ".zoned.obj";
        }

        // add event (sans categories) to store
        public void AddEvent(string title, string url, string source, Utils.DateTimeWithZone dtstart, Utils.DateTimeWithZone dtend, bool allday)
        {
            ZonedEvent evt = new ZonedEvent(title, url, source, allday, null, dtstart, dtend);
            events.Add(evt);
        }

        // add event (with categories) to store
        public void AddEvent(string title, string url, string source, Utils.DateTimeWithZone dtstart, Utils.DateTimeWithZone dtend, bool allday, string categories)
        {
            ZonedEvent evt = new ZonedEvent(title, url, source, allday, categories, dtstart, dtend);
            events.Add(evt);
        }

    }

    [Serializable]
    public class ZonelessEventStore : EventStore
    {
        public List<ZonelessEvent> events = new List<ZonelessEvent>();

        // used to chunk the list by datekey (e.g. "d2010-07-04") for the convenience of renderers
        public Dictionary<string, List<ZonelessEvent>> event_dict = new Dictionary<string, List<ZonelessEvent>>();
        public List<string> datekeys = new List<string>();

        public ZonelessEventStore(Calinfo calinfo, string qualifier)
            : base(calinfo)
        {
            this.objfile = this.id + ".zoneless.obj";
            this.xmlfile = this.id + ".zoneless.xml";
        }

        // add event sans categories
        public void AddEvent(string title, string url, string source, DateTime dtstart, DateTime dtend, bool allday)
        {
            var evt = new ZonelessEvent(title, url, source, allday, null, dtstart, dtend);
            this.events.Add(evt);
        }

        // add event with categories
        public void AddEvent(string title, string url, string source, DateTime dtstart, DateTime dtend, bool allday, string categories)
        {
            var evt = new ZonelessEvent(title, url, source, allday, categories, dtstart, dtend);
            this.events.Add(evt);
        }

        // recover the UTC datetime that was in the original ZonedEvent,
        // used by CalendarRenderer.RenderJson
        public static ZonelessEvent UniversalFromLocalAndTzinfo(ZonelessEvent evt, TimeZoneInfo tzinfo)
        {
            evt.dtstart = TimeZoneInfo.ConvertTimeToUtc(evt.dtstart, tzinfo);

            if (evt.dtend != null)
                evt.dtend = TimeZoneInfo.ConvertTimeToUtc(evt.dtend, tzinfo);

            return evt;
        }

        public void SortEventList()
        {
            this.events = this.events.OrderBy(evt => evt.dtstart).ToList();
        }

        public void ExcludePastEvents()
        {
            this.events = this.events.FindAll(evt => Utils.IsCurrentOrFutureDateTime(evt, this.calinfo.tzinfo));
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
                list = list.OrderBy(evt => evt.dtstart).ToList();
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

                var local_evt = new ZonelessEvent(evt.title, evt.url, evt.source, evt.allday, evt.categories, evt.dtstart, evt.dtend);
                dict[datekey].Add(local_evt);
            }
            this.event_dict = dict;
        }

        public static ZonelessEventStore ZonedToZoneless(ZonedEventStore es, Calinfo calinfo, string qualifier)
        {
            var zoned_events = es.events;
            var zes = new ZonelessEventStore(calinfo, qualifier);
            foreach (var evt in zoned_events)
                zes.AddEvent(evt.title, evt.url, evt.source, evt.dtstart.LocalTime, evt.dtend.LocalTime, evt.allday, evt.categories);
            zes.SortEventList();
            return zes;
        }

    }

}
