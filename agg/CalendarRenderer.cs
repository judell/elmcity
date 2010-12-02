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
using System.Text;
using System.Web;
using System.Web.Mvc;
using ElmcityUtils;
using Newtonsoft.Json;

namespace CalendarAggregator
{

    // render calendar data in various formats

    public class CalendarRenderer
    {

        private string id;
        private Calinfo calinfo;
        private BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
        private string template_html;

        public string xmlfile
        {
            get { return _xmlfile; }
            set { _xmlfile = value; }
        }
        private string _xmlfile;

        public string jsonfile
        {
            get { return _jsonfile; }
            set { _jsonfile = value; }
        }
        private string _jsonfile;

        public string htmlfile
        {
            get { return _htmlfile; }
            set { _htmlfile = value; }
        }
        private string _htmlfile;

        // data might be available in cache,
        // this interface abstracts the cache so its logic can be tested
        public ICache cache
        {
            get { return _cache; }
            set { _cache = value; }
        }
        private ICache _cache;

        // used to list ical sources in the html rendering
        // todo: make this a query that returns the list in all formats
        public string ical_sources
        {
            get { return _ical_sources; }
            set { _ical_sources = value; }
        }
        private string _ical_sources;

        // points to a method for rendering individual events in various formats
        private delegate string EventRenderer(ZonelessEvent evt, Calinfo calinfo);

        // points to a method for rendering views of events in various formats
        public delegate string ViewRenderer(ZonelessEventStore eventstore, string view, int count);

        // points to a method for retrieving a pickled event store
        // normally used with caching: es_getter = new EventStoreGetter(GetEventStoreWithCaching);
        // but could bypass cache: es_getter = new EventStoreGetter(GetEventStoreWithoutCaching);
        // returns a ZonelessEventStore
        private delegate ZonelessEventStore EventStoreGetter(ICache cache);

        private EventStoreGetter es_getter;

        public const string DATETIME_FORMAT_FOR_XML = "yyyy-MM-ddTHH:mm:ss";

        // used by the service in both WorkerRole and WebRole
        // in WorkerRole: when saving renderings
        // in WebRole: when serving renderings
        public CalendarRenderer(Calinfo calinfo)
        {
            this.cache = null;
            this.es_getter = new EventStoreGetter(GetEventStoreWithCaching);
            try
            {
                this.calinfo = calinfo;
                this.id = calinfo.delicious_account;

                try
                {
                    this.template_html = HttpUtils.FetchUrl(this.calinfo.template_url).DataAsString();
                }
                catch (Exception e)
                {
                    GenUtils.LogMsg("exception", "CalendarRenderer: cannot fetch template", e.InnerException.Message);
                    throw;
                }

                this.xmlfile = this.id + ".xml";
                this.jsonfile = this.id + ".json";
                this.htmlfile = this.id + ".html";

                this.ical_sources = Collector.GetIcalSources(this.id);
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "CalenderRenderer.CalendarRenderer: " + id, e.Message + e.StackTrace);
            }

        }

        #region xml

        // used by WorkerRole to save current xml rendering to azure blob
        public string SaveAsXml()
        {
            string xml = "";
            xml = this.RenderXml(0);
            byte[] bytes = Encoding.UTF8.GetBytes(xml.ToString());
            BlobStorage.WriteToAzureBlob(this.bs, this.id, this.xmlfile, "text/xml", bytes);
            return xml.ToString();
        }

        public string RenderXml()
        {
            return RenderXml(eventstore: null, view: null, count: 0);
        }

        public string RenderXml(string view)
        {
            return RenderXml(eventstore: null, view: view, count: 0);
        }

        public string RenderXml(int count)
        {
            return RenderXml(eventstore: null, view: null, count: count);
        }

        // render an eventstore as xml, optionally limited by view and/or count
        public string RenderXml(ZonelessEventStore eventstore, string view, int count)
        {
            eventstore = GetEventStore(eventstore, view, count);

            var xml = new StringBuilder();
            xml.Append("<events>\n");

            var event_renderer = new EventRenderer(RenderEvtAsXml);

            var eventstring = new StringBuilder();

            foreach (var evt in eventstore.events)
                AppendEvent(eventstring, event_renderer, evt);

            xml.Append(eventstring.ToString());

            xml.Append("</events>\n");

            return xml.ToString();
        }

        // render a single event as an xml element
        private string RenderEvtAsXml(ZonelessEvent evt, Calinfo calinfo)
        {
            var xml = new StringBuilder();
            xml.Append("<event>\n");
            xml.Append(string.Format("<title>{0}</title>\n", HttpUtility.HtmlEncode(evt.title)));
            xml.Append(string.Format("<url>{0}</url>\n", HttpUtility.HtmlEncode(evt.url)));
            xml.Append(string.Format("<source>{0}</source>\n", HttpUtility.HtmlEncode(evt.source)));
            xml.Append(string.Format("<dtstart>{0}</dtstart>\n", evt.dtstart.ToString(DATETIME_FORMAT_FOR_XML)));
            if (evt.dtend != null)
                xml.Append(string.Format("<dtend>{0}</dtend>\n", evt.dtend.ToString(DATETIME_FORMAT_FOR_XML)));
            xml.Append(string.Format("<allday>{0}</allday>\n", evt.allday));
            xml.Append(string.Format("<categories>{0}</categories>\n", HttpUtility.HtmlEncode(evt.categories)));
            xml.Append("</event>\n");
            return xml.ToString();
        }

        #endregion xml

        #region json

        public BlobStorageResponse SaveAsJson()
        {
            var uri = BlobStorage.MakeAzureBlobUri(this.id, this.id + ".zoneless.obj");
            var es = (ZonelessEventStore)BlobStorage.DeserializeObjectFromUri(uri);
            var json = JsonConvert.SerializeObject(es.events);
            return Utils.SerializeObjectToJson(es.events, this.id, this.jsonfile);
        }

        public string RenderJson()
        {
            return RenderJson(eventstore: null, view: null, count: 0);
        }

        public string RenderJson(string view)
        {
            return RenderJson(eventstore: null, view: view, count: 0);
        }

        public string RenderJson(int count)
        {
            return RenderJson(eventstore: null, view: null, count: count);
        }

        public string RenderJson(ZonelessEventStore eventstore, string view, int count)
        {
            eventstore = GetEventStore(eventstore, view, count);
            for (var i = 0; i < eventstore.events.Count; i++)
            {
                var evt = eventstore.events[i];
                // provide utc so browsers receiving the json don't apply their own timezones
                evt = ZonelessEventStore.UniversalFromLocalAndTzinfo(evt, this.calinfo.tzinfo);
                eventstore.events[i] = evt;
            }
            return JsonConvert.SerializeObject(eventstore.events);
        }

        #endregion json

        #region html

        public string SaveAsHtml()
        {
            string html = this.RenderHtml();
            byte[] bytes = Encoding.UTF8.GetBytes(html);
            BlobStorage.WriteToAzureBlob(this.bs, this.id, this.htmlfile, "text/html", bytes);
            return html;
        }

        public string RenderHtml()
        {
            return RenderHtml(eventstore: null, view: null, count: 0);
        }

        public string RenderHtml(ZonelessEventStore es)
        {
            return RenderHtml(eventstore: es, view: null, count: 0);
        }

        public string RenderHtml(ZonelessEventStore eventstore, string view, int count)
        {
            eventstore = GetEventStore(eventstore, view, count);

            var builder = new StringBuilder();

            RenderEventsAsHtml(eventstore, builder, announce_time_of_day: true);

            var html = this.template_html.Replace("__EVENTS__", builder.ToString());

            html = html.Replace("__ID__", this.id);
            html = html.Replace("__CSSURL__", this.calinfo.css);
            html = html.Replace("__TITLE__", this.calinfo.title);
			html = html.Replace("__CONTRIBUTE__", this.calinfo.contribute_url.ToString());

			html = html.Replace("__WIDTH__", this.calinfo.display_width);

            if (this.calinfo.has_img)
            {
                html = html.Replace("__IMG__", Configurator.default_img_html);
                html = html.Replace("__IMGURL__", this.calinfo.img_url.ToString());
            }
            else
            {
                html = html.Replace("__IMG__", "");
            }

            html = html.Replace("__CONTACT__", this.calinfo.contact);

			var sources_builder = new StringBuilder();

			if (this.calinfo.eventful)
				sources_builder.Append(@"<p class=""sources""><a target=""_new"" href=""http://eventful.com"">Eventful</a></p>");

			if (this.calinfo.upcoming)
				sources_builder.Append(@"<p class=""sources""><a target=""_new"" href=""http://upcoming.yahoo.com"">Upcoming</a></p>");

			if (this.calinfo.eventbrite)
				sources_builder.Append(@"<p class=""sources""><a target=""_new"" href=""http://eventbrite.com"">EventBrite</a></p>");

			if (this.calinfo.facebook)
				sources_builder.Append(@"<p class=""sources""><a target=""_new"" href=""http://facebook.com"">Facebook</a></p>");

			var ical_feeds = string.Format(@"<p class=""sources""><a target=""_new"" href=""http://elmcity.cloudapp.net/services/{0}/stats"">{1} iCalendar feeds</a></p>", 
				this.calinfo.delicious_account, this.calinfo.feed_count);
			sources_builder.Append(ical_feeds);

			html = html.Replace("__SOURCES__", sources_builder.ToString());

            return html;
        }

        // the default html rendering chunks by day, this method processes the raw list of events into
        // the ZonelessEventStore's event_dict like so:
        // key: d20100710
        // value: [ <ZonelessEvent>, <ZonelessEvent> ... ]
        private static void OrganizeByDate(ZonelessEventStore es)
        {
            es.GroupEventsByDatekey();
            es.SortEventSublists();
            es.SortDatekeys();
        }

        private void RenderEventsAsHtml(ZonelessEventStore es, StringBuilder builder, bool announce_time_of_day)
        {
            OrganizeByDate(es);
            TimeOfDay current_time_of_day;
            var event_renderer = new EventRenderer(RenderEvtAsHtml);
			var year_month_anchors = new List<string>(); // e.g. ym201110
            foreach (string datekey in es.datekeys)
            {
				var year_month_anchor = datekey.Substring(1, 6);
				if (! year_month_anchors.Exists(ym => ym == year_month_anchor))
				{
					builder.Append(string.Format("\n<a name=\"ym{0}\"></a>\n", year_month_anchor));
					year_month_anchors.Add(year_month_anchor);
				}
                current_time_of_day = TimeOfDay.Initialized;
                var event_builder = new StringBuilder();
                var date = Utils.DateFromDateKey(datekey);
                event_builder.Append(string.Format("\n<a name=\"{0}\"></a>\n", datekey));
                event_builder.Append(string.Format("<h1 class=\"eventDate\"><b>{0}</b></h1>\n", date));
                foreach (ZonelessEvent evt in es.event_dict[datekey])
                {

                    if (announce_time_of_day)
                        // see http://blog.jonudell.net/2009/06/18/its-the-headings-stupid/
                        MaybeAnnounceTimeOfDay(event_builder, ref current_time_of_day, evt.dtstart);
                    AppendEvent(event_builder, event_renderer, evt);
                }
                builder.Append(event_builder);
            }
        }

        public string RenderEvtAsHtml(ZonelessEvent evt, Calinfo calinfo)
        {
            var hour = evt.dtstart.Hour;
            var min = evt.dtstart.Minute;
            var sec = evt.dtstart.Second;
            string dtstart;
            if (evt.allday)
                dtstart = "  ";
            else
                dtstart = evt.dtstart.ToString("ddd hh:mm tt  ");
            string evt_title;
            if (!String.IsNullOrEmpty(evt.url))
                evt_title = string.Format("<a target=\"{0}\" href=\"{1}\">{2}</a>",
                    Configurator.default_html_window_name, evt.url, evt.title);
            else
                evt_title = evt.title;

            string categories = "";
            List<string> catlist_links = new List<string>();
            if (!String.IsNullOrEmpty(evt.categories))
            {
                List<string> catlist = evt.categories.Split(',').ToList();
                foreach (var cat in catlist)
                {
                    var bare_cat = cat.Trim();
                    var category_url = string.Format("http://{0}/services/{1}/html?view={2}", ElmcityUtils.Configurator.appdomain, this.id, bare_cat);
                    catlist_links.Add(string.Format(@"<a href=""{0}"">{1}</a>", category_url, bare_cat));
                }
                categories = string.Format(@" <span class=""eventSource"">{0}</span>", string.Join(", ", catlist_links.ToArray()));
            }

            return string.Format(
@"<h3 class=""eventBlurb""> 
<span class=""eventStart"">{0}</span> 
<span class=""eventTitle"">{1}</span> 
<span class=""eventSource"">{2}</span>
{3}
</h3>
",
            dtstart,
            evt_title,
            evt.source,
            categories);
        }

        // just today's events, used by, e.g., RenderJsWidget
        public string RenderTodayAsHtml()
        {
            ZonelessEventStore es = FindTodayEvents();
            var sb = new StringBuilder();
            foreach (var evt in es.events)
                sb.Append(RenderEvtAsHtml(evt, this.calinfo));
            return sb.ToString();
        }

        #endregion html

        #region ics

        public string RenderIcs(string view)
        {
            // todo: implement this
            return "";
        }

        #endregion ics

        #region rss

        public string RenderRss()
        {
            return RenderRss(eventstore: null, view: null, count: Configurator.rss_default_items);
        }

        public string RenderRss(int count)
        {
            return RenderRss(eventstore: null, view: null, count: count);
        }

        public string RenderRss(string view)
        {
            return RenderRss(eventstore: null, view: view, count: Configurator.rss_default_items);
        }

        public string RenderRss(ZonelessEventStore eventstore, string view, int count)
        {
			try
			{
				eventstore = GetEventStore(eventstore, view, count);
				var query = string.Format("view={0}&count={1}", view, count);
				return Utils.RssFeedFromEventStore(this.id, query, eventstore);
			}
			catch ( Exception e )
			{
				GenUtils.LogMsg("exception", String.Format("RenderRss: view {0}, count {1}", view, count), e.Message);
				return String.Empty;
			}
        }

        #endregion rss

        #region tags

        public string RenderTagCloudAsHtml()
        {
            var tagcloud = MakeTagCloud(); // see http://blog.jonudell.net/2009/09/16/familiar-idioms/
            var html = new StringBuilder("<h1>tags for " + this.id + "</h1>");
            html.Append(@"<table cellpadding=""6"">");
            foreach (var pair in tagcloud)
            {
                var key = pair.Keys.First();
                html.Append("<tr>");
                html.Append("<td>" + key + "</td>");
                html.Append(@"<td align=""right"">" + pair[key] + "</td>");
                html.Append("</tr>");
            }
            html.Append("</table>");
            return html.ToString();
        }

        public string RenderTagCloudAsJson()
        {
            var tagcloud = MakeTagCloud();
            return JsonConvert.SerializeObject(tagcloud);
        }

        // see http://blog.jonudell.net/2009/09/16/familiar-idioms/
        private List<Dictionary<string, int>> MakeTagCloud()
        {
            var es = this.es_getter(this.cache);
            var tagquery =
                from evt in es.events
                where evt.categories != null
                from tag in evt.categories.Split(',')
                group tag by tag into occurrences
                orderby occurrences.Count() descending
                select new Dictionary<string, int>() { { occurrences.Key, occurrences.Count() } };
            return tagquery.ToList();
        }

        #endregion

        private void MaybeAnnounceTimeOfDay(StringBuilder eventstring, ref TimeOfDay current_time_of_day, DateTime dt)
        {
            if (this.calinfo.hub_type == "what")
                return;

            if (Utils.ClassifyTime(dt) != current_time_of_day)
            {
                TimeOfDay new_time_of_day = Utils.ClassifyTime(dt);
                string tod;
                if (new_time_of_day != TimeOfDay.AllDay)
                    tod = new_time_of_day.ToString();
                else
                    tod = Configurator.ALL_DAY;
                eventstring.Append(string.Format(@"<h2 class=""timeofday"">{0}</h2>", tod));
                current_time_of_day = new_time_of_day;
            }
        }

        private ZonelessEventStore GetEventStore(ZonelessEventStore es, string view, int count)
        {
            // see RenderDynamicViewWithCaching:
            // view_data = Encoding.UTF8.GetBytes(view_renderer(eventstore:null, view:view, view:count));
            // the renderer might be, e.g., CalendarRenderer.RenderHtml, which calls this method
            if (es == null)  // if no eventstore passed in (e.g., for testing)
                es = this.es_getter(this.cache); // get the eventstore. if the getter is GetEventStoreWithCaching
            // then it will use HttpUtils.RetrieveBlobFromServerCacheOrUri
            // which gets from cache if it can, else fetches uri and loads cache
            es.events = MaybeFilterByViewOrCount(view, count, es); // then filter by view or count if requested
            return es;
        }

        // take a string representation of a set of events, in some format
        // take a per-event renderer for that format
        // take an event object
        // call the renderer to add the event object to the string representation
        // currently uses: RenderEvtAsHtml, RenderEvtAsXml
        private void AppendEvent(StringBuilder eventstring, EventRenderer event_renderer, ZonelessEvent evt)
        {
            eventstring.Append(event_renderer(evt, this.calinfo));
        }

        // produce a javascript version of today's events, for
        // inclusion on a site using <script src="">
        public string RenderJsWidget()
        {
            ZonelessEventStore es = FindTodayEvents();
            var html = new StringBuilder();
            foreach (var evt in es.events)
                html.Append(RenderEvtAsHtml(evt, this.calinfo));
            html = html.Replace("\'", "\\\'").Replace("\"", "\\\"");
            html = html.Replace(Environment.NewLine, "");
            return (string.Format("document.write('{0}')", html));
        }

        // return an eventstore with just today's events for this hub
        public ZonelessEventStore FindTodayEvents()
        {
            ZonelessEventStore es;
            try
            {
                es = this.es_getter(this.cache);
                es.events = es.events.FindAll(e => Utils.DtIsTodayInTz(e.dtstart, this.calinfo.tzinfo));
                var events_having_dt = es.events.FindAll(evt => ZonelessEventStore.IsZeroHourMinSec(evt) == false);
                var events_not_having_dt = es.events.FindAll(evt => ZonelessEventStore.IsZeroHourMinSec(evt) == true);
                es.events = new List<ZonelessEvent>();
                foreach (var evt in events_having_dt)
                    es.events.Add(evt);
                foreach (var evt in events_not_having_dt)
                    es.events.Add(evt);
            }
            catch (Exception e)
            {
                es = new ZonelessEventStore(this.calinfo, null);
                GenUtils.LogMsg("exception", "CalendarRenderer.FindTodayEvents", e.Message + e.StackTrace);
            }
            return es;
        }

        // possibly filter an event list by view or count
        public static List<ZonelessEvent> MaybeFilterByViewOrCount(string view, int count, ZonelessEventStore es)
        {
            var events = es.events;
            if (view != null) // reduce the list of events to those with matching categories
                events = events.FindAll(evt => evt.categories != null && evt.categories.ToLower().Contains(view));
            if (count != 0)   // reduce the list to the first count events
                events = events.Take(count).ToList();
            return events;
        }

        // run ical collection for a hub, send results through html renderer,
        // intended to help people visualize ical feeds
        public static string Viewer(string str_feedurl, string source)
        {
            var id = "viewer";
            var qualifier = ".ical.tmp";
            var calinfo = new Calinfo(id);
            var fr = new FeedRegistry(id);
            if (source == null)
                fr.AddFeed(str_feedurl, str_feedurl);
            else
                fr.AddFeed(str_feedurl, source);
            var es = new ZonedEventStore(calinfo, qualifier);
			var collector = new Collector(calinfo, GenUtils.GetSettingsFromAzureTable());
            collector.CollectIcal(fr, es, test: false, nosave: true);
            var zes = ZonelessEventStore.ZonedToZoneless(es, calinfo, qualifier);
            var cr = new CalendarRenderer(calinfo);
            var builder = new StringBuilder();
            cr.RenderEventsAsHtml(zes, builder, false);
            return builder.ToString();
        }

        // the CalendarRenderer object uses this to get the pickled object that contains an eventstore,
        // from the CalendarRender's cache if available, else fetching bytes
        private ZonelessEventStore GetEventStoreWithCaching(ICache cache)
        {
            var blob_url = BlobStorage.MakeAzureBlobUri(container: this.id, name: this.id + ".zoneless.obj");
            var obj_bytes = (byte[])CacheUtils.RetrieveBlobAndEtagFromServerCacheOrUri(cache, blob_url)["response_body"];
            ZonelessEventStore es = (ZonelessEventStore)BlobStorage.DeserializeObjectFromBytes(obj_bytes);
            return es;
        }

        private ZonelessEventStore GetEventStoreWithoutCaching(ICache cache)
        {
            var blob_url = BlobStorage.MakeAzureBlobUri(container: this.id, name: this.id + ".zoneless.obj");
            var obj_bytes = HttpUtils.FetchUrl(blob_url).bytes;
            ZonelessEventStore es = (ZonelessEventStore)BlobStorage.DeserializeObjectFromBytes(obj_bytes);
            return es;
        }

        // used in WebRole for views built from pickled objects that are cached
        public string RenderDynamicViewWithCaching(ControllerContext context, string view_key, ViewRenderer view_renderer, string view, int count)
        {
            var view_is_cached = this.cache[view_key] != null;
            byte[] view_data;
            byte[] response_body;
            if (view_is_cached)
                view_data = (byte[])cache[view_key];
            else
                view_data = Encoding.UTF8.GetBytes(view_renderer(eventstore: null, view: view, count: count));

            response_body = CacheUtils.MaybeSuppressResponseBodyForView(cache, context, view_data);
            return Encoding.UTF8.GetString(response_body);
        }

        public string RenderDynamicViewWithoutCaching(ControllerContext context, ViewRenderer view_renderer, String view, int count)
        {
            ZonelessEventStore es = GetEventStoreWithoutCaching(this.cache);
            return view_renderer(es, view, count);
        }

    }
}
