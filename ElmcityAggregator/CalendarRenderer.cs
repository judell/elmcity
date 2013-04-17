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
using System.Text;
using System.Web;
using System.Web.Mvc;
using ElmcityUtils;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace CalendarAggregator
{

	// render calendar data in various formats

	[Serializable]
	public class CalendarRenderer
	{
		private string id;

		public Calinfo calinfo;
		
		public string template_html;

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

		// points to a method for rendering individual events in various formats
		private delegate string EventRenderer(ZonelessEvent evt, Calinfo calinfo, Dictionary<string,object> args);

		// points to a method for rendering views of events in various formats
		public delegate string ViewRenderer(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string,object> args);

		// points to a method for retrieving a pickled event store
		// normally used with caching: es_getter = new EventStoreGetter(GetEventStoreWithCaching);
		// but could bypass cache: es_getter = new EventStoreGetter(GetEventStoreWithoutCaching);
		// returns a ZonelessEventStore
		private delegate ZonelessEventStore EventStoreGetter(ICache cache);

		private EventStoreGetter es_getter;

		public const string DATETIME_FORMAT_FOR_XML = "yyyy-MM-ddTHH:mm:ss";

		private int event_counter;
		//private int day_counter;
		private int time_of_day_counter;

		// used by the service in both WorkerRole and WebRole
		// in WorkerRole: when saving renderings
		// in WebRole: when serving renderings
		public CalendarRenderer(string id)
		{
			this.calinfo = Utils.AcquireCalinfo(id);
			this.cache = null;
			this.ResetCounters();
			this.es_getter = new EventStoreGetter(GetEventStoreWithCaching);
			try
			{
				this.id = id;

				try
				{
					this.template_html = HttpUtils.FetchUrl(calinfo.template_url).DataAsString();
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "CalendarRenderer: cannot fetch template", e.InnerException.Message);
					throw (e);
				}

				this.xmlfile = this.id + ".xml";
				this.jsonfile = this.id + ".json";
				this.htmlfile = this.id + ".html";

				//  this.ical_sources = Collector.GetIcalSources(this.id);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "CalenderRenderer.CalendarRenderer: " + id, e.Message + e.StackTrace);
			}

		}

		#region xml

		// used by WorkerRole to save current xml rendering to azure blob
		public string SaveAsXml()
		{
			var bs = BlobStorage.MakeDefaultBlobStorage();
			string xml = "";
			xml = this.RenderXml(0);
			byte[] bytes = Encoding.UTF8.GetBytes(xml.ToString());
			//BlobStorage.WriteToAzureBlob(this.bs, this.id, this.xmlfile, "text/xml", bytes);
			bs.PutBlob(this.id, this.xmlfile, xml.ToString(), "text/xml");
			return xml.ToString();
		}

		public string RenderXml()
		{
			return RenderXml(eventstore: null, view: null, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		public string RenderXml(string view)
		{
			return RenderXml(eventstore: null, view: view, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		public string RenderXml(int count)
		{
			return RenderXml(eventstore: null, view: null, count: count, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		// render an eventstore as xml, optionally limited by view and/or count
		public string RenderXml(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string,object> args)
		{
			eventstore = GetEventStore(eventstore, view, count, from, to);

			var xml = new StringBuilder();
			xml.Append("<events>\n");

			var event_renderer = new EventRenderer(RenderEvtAsXml);

			var eventstring = new StringBuilder();

			foreach (var evt in eventstore.events)
				AppendEvent(eventstring, event_renderer, evt, new Dictionary<string,object>() );

			xml.Append(eventstring.ToString());

			xml.Append("</events>\n");

			return xml.ToString();
		}

		// render a single event as an xml element
		private string RenderEvtAsXml(ZonelessEvent evt, Calinfo calinfo, Dictionary<string,object> args)
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
			xml.Append(string.Format("<description>{0}</description>\n", HttpUtility.HtmlEncode(evt.description)));
			xml.Append(string.Format("<location>{0}</location>\n", HttpUtility.HtmlEncode(evt.location)));

			//if (this.calinfo.hub_type == HubType.where.ToString())
			if (calinfo.hub_enum == HubType.where)
			{
				var lat = evt.lat != null ? evt.lat : this.calinfo.lat;
				var lon = evt.lon != null ? evt.lon : this.calinfo.lon;
				xml.Append(string.Format("<lat>{0}</lat>\n", lat));
				xml.Append(string.Format("<lon>{0}</lon>\n", lon));
			}
			xml.Append("</event>\n");
			return xml.ToString();
		}

		#endregion xml

		#region json

		public BlobStorageResponse SaveAsJson()
		{
			var es = new ZonelessEventStore(this.calinfo).Deserialize();
			var json = JsonConvert.SerializeObject(es.events);
			return Utils.SerializeObjectToJson(es.events, this.id, this.jsonfile);
		}

		public string RenderJson()
		{
			return RenderJson(eventstore: null, view: null, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		public string RenderJson(string view)
		{
			return RenderJson(eventstore: null, view: view, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		public string RenderJson(int count)
		{
			return RenderJson(eventstore: null, view: null, count: count, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		public string RenderJson(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string, object> args)
		{
			eventstore = GetEventStore(eventstore, view, count, from, to);
			for (var i = 0; i < eventstore.events.Count; i++)
			{
				var evt = eventstore.events[i];
				if ( this.calinfo.hub_enum == HubType.where )
				{
					evt.lat = evt.lat != null ? evt.lat : this.calinfo.lat;
					evt.lon = evt.lon != null ? evt.lon : this.calinfo.lon;
				}
				// provide utc so browsers receiving the json don't apply their own timezones
				evt = ZonelessEventStore.UniversalFromLocalAndTzinfo(evt, this.calinfo.tzinfo);
				eventstore.events[i] = evt;
			}
			return JsonConvert.SerializeObject(eventstore.events);
		}

		public string DescriptionFromTitleAndDtstart(string title, string dtstart, string jsonp)
		{
			var es = this.es_getter(this.cache);
			var evt = es.events.Find(e => e.title == title && e.dtstart == DateTime.Parse(dtstart));
			var description = evt.description.Replace("'", "\\'").Replace("\n", " ").Replace("\r", " ");
			string location = "";
			if (!String.IsNullOrEmpty(evt.location))
				location = String.Format("<br>{0}", evt.location.Replace("'", "\\'"));
			description = ( "<span class=\"desc\">" + location + "<br><br>" + description + "</span>").UrlsToLinks();
			return String.Format(jsonp + "('" + description + "')") ;
		}

		public string RenderFeedAsJson(string source)
		{
			var es = this.es_getter(this.cache);
			var events = es.events.FindAll(evt => FeedComesFrom(evt, source));
			var json = JsonConvert.SerializeObject(events);
			return GenUtils.PrettifyJson(json);
		}

		public static bool FeedComesFrom(ZonelessEvent evt, string source)
		{
			if (evt.source.StartsWith(source))
				return true;
			foreach (var url_and_source in evt.urls_and_sources)
				if (url_and_source.Value.StartsWith(source))
					return true;
			return false;
		}

		#endregion json

		#region html

		public string SaveAsHtml()
		{
			string html = this.RenderHtml();
			byte[] bytes = Encoding.UTF8.GetBytes(html);
			//BlobStorage.WriteToAzureBlob(this.bs, this.id, this.htmlfile, "text/html", bytes);
			var bs = BlobStorage.MakeDefaultBlobStorage();
			bs.PutBlob(this.id, this.htmlfile, html, "text/html");
			return html;
		}

		public string RenderHtml()
		{
			return RenderHtml(eventstore: null, view: null, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		public string RenderHtml(ZonelessEventStore es)
		{
			return RenderHtml(eventstore: es, view: null, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		public string RenderHtml(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string,object> args)
		{
			this.ResetCounters();

			eventstore = GetEventStore(eventstore, view, count, from, to);

			AdvanceToAnHourAgo(eventstore);

			MaybeUseTestTemplate(args);

			var builder = new StringBuilder();
			RenderEventsAsHtml(eventstore, builder, args);

			var html = this.template_html.Replace("__EVENTS__", builder.ToString());

			html = this.InsertTagSelector(html, view, eventsonly: false);

			html = html.Replace("__APPDOMAIN__", ElmcityUtils.Configurator.appdomain);
			
			html = html.Replace("__ID__", this.id);
			html = html.Replace("__CSSURL__", this.calinfo.css);
			html = MaybeOverrideTheme(args, html);

			//html = html.Replace("__TITLE__", this.calinfo.title);
			html = html.Replace("__TITLE__", MakeTitle(view));
			html = html.Replace("__META__", MakeTitle(view) + " calendars happenings schedules");
			html = html.Replace("__WIDTH__", this.calinfo.display_width);
			html = html.Replace("__CONTACT__", this.calinfo.contact);
			html = html.Replace("__FEEDCOUNT__", this.calinfo.feed_count);

			/*
			string generated = String.Format("{0}\n{1}\n{2}\n{3}",
					System.DateTime.UtcNow.ToString(),
					System.Net.Dns.GetHostName(),
					JsonConvert.SerializeObject(GenUtils.GetSettingsFromAzureTable("usersettings")),
					JsonConvert.SerializeObject(this.calinfo) );
			 */

			html = html.Replace("__GENERATED__", System.DateTime.UtcNow.ToString() + " : " + this.calinfo.version_description);

			return html;
		}

		public string MakeTitle(string view)
		{
			var _view = string.IsNullOrEmpty(view) ? " " : " " + view + " ";
			string _title;
			switch (this.calinfo.hub_enum)
			{
				case HubType.where:
					_title = this.calinfo.where;
					break;
				case HubType.what:
					_title = this.calinfo.what;
					break;
				case HubType.region:
					_title = this.calinfo.id.ToLower();
					break;
				default:
					_title = "";
					break;
			}
			return _title + _view + "events";
		}

	public  static string MaybeOverrideTheme(Dictionary<string,object> args, string html)
	{
		if (args == null)
			return html;
		if (args.ContainsKey("theme") && args["theme"] != null )
		{
			var theme_name = args["theme"].ToString();
			try
			{
				var themes = Utils.GetThemesDict();
				var theme_css = Utils.GetCssTheme(themes, theme_name, (bool)args["mobile"], (string)args["mobile_long"], (string)args["ua"]);
				html = html.Replace("<!-- __CUSTOM_STYLE __ -->", string.Format("<style>\n{0}\n{1}</style>\n", 
					IsDefaultThemeDict(themes) ? "/* this is the fallback theme, if used something went wrong */" : "",
					theme_css
					)
				);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "MaybeOverrideTheme", e.Message + e.StackTrace);
			}
		}
	return html;
	}

		private static bool IsDefaultThemeDict(Dictionary<string,Dictionary<string,string>> theme_dict)
		{
			return theme_dict.Keys.Count == 1 && theme_dict.Keys.ToList()[0] == "default";
		}

		public string RenderHtmlEventsOnly(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string,object> args)
		{
			MaybeUseTestTemplate(args);

			eventstore = GetEventStore(eventstore, view, count, from, to);

			//AdvanceToAnHourAgo(eventstore);

			var builder = new StringBuilder();

			RenderEventsAsHtml(eventstore, builder, args);

			var html = this.template_html.Replace("__EVENTS__", builder.ToString());

			html = this.InsertTagSelector(html, view, eventsonly: true);

			html = html.Replace("__APPDOMAIN__", ElmcityUtils.Configurator.appdomain);

			html = html.Replace("__ID__", this.id);
			html = html.Replace("__TITLE__", MakeTitle(view));
			html = html.Replace("__META__", MakeTitle(view) + " calendars happenings schedules");
			html = html.Replace("__CSSURL__", this.calinfo.css);
			html = MaybeOverrideTheme(args, html);
			html = html.Replace("__GENERATED__", System.DateTime.UtcNow.ToString());

			html = Utils.RemoveCommentSection(html, "SIDEBAR");
			html = Utils.RemoveCommentSection(html, "JQUERY_UI_CSS");
			html = Utils.RemoveCommentSection(html, "JQUERY_UI");
			html = Utils.RemoveCommentSection(html, "DATEPICKER");
			html = Utils.RemoveCommentSection(html, "HUBTITLE");
			html = Utils.RemoveCommentSection(html, "TAGS");

			return html;
		}

		private static bool get_announce_time_of_day(Dictionary<string, object> args)
		{
			var announce_time_of_day = true;
			if (args.ContainsKey("announce_time_of_day") && (bool)args["announce_time_of_day"] == false)
				announce_time_of_day = false;
			return announce_time_of_day;
		}

		public string RenderHtmlForMobile(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string,object> args)
		{
			//GenUtils.LogMsg("info", "RenderForMobile", JsonConvert.SerializeObject(args));
			this.ResetCounters();

			MaybeUseTestTemplate(args);

			eventstore = GetEventStore(eventstore, view, count, from, to);
			if (from == DateTime.MinValue && to == DateTime.MinValue)
				AdvanceToAnHourAgo(eventstore);

			count = (int) args["mobile_event_count"];                        // wait until now to apply the reduction
			eventstore.events = eventstore.events.Take(count).ToList();


			// assume a css link like this:
			// <link type="text/css" rel="stylesheet" href="http://elmcity.cloudapp.net/get_css_theme?theme_name=a2chron"/>

			var css = (string) args["css"];
			var theme = css.Replace("http://elmcity.cloudapp.net/get_css_theme?theme_name=", "");
			args["theme"] = theme;

			var html = RenderHtmlEventsOnly(eventstore, view, count, from, to, args);

			string mobile_long = "";
			string ua = "";
			if (args.ContainsKey("mobile_long") && args.ContainsKey("ua"))
			{
				mobile_long = (string)args["mobile_long"];   // the longest dimension of detected mobile device
				ua = (string)args["ua"];                     // user agent
			}

			html = html.Replace("get_css_theme?", string.Format("get_css_theme?mobile=yes&mobile_long={0}&ua={1}&", mobile_long, ua));

			//html = this.InsertTagSelector(html, (string) args["view"]);

			/*
			html = html.Replace("__MOBILE__", "yes");                          // todo: remove this when conversion to server-side method is complete
			if (args.ContainsKey("mobile_long") && args.ContainsKey("mobile_short"))
			{
				html = html.Replace("__MOBILE_LONG__", args["mobile_long"].ToString());
				html = html.Replace("__MOBILE_SHORT__", args["mobile_short"].ToString());
			}
			 */
			return html;
		}

		public string RenderHtmlEventsOnlyRaw(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string, object> args)
		{
			this.ResetCounters();

			MaybeUseTestTemplate(args);

			eventstore = GetEventStore(eventstore, view, count, from, to);
			string title = null;
			DateTime dtstart = DateTime.MinValue;
			try
			{
				var sentinel = (string)args["raw_sentinel"];
				string[] separators = new string[] { "__|__" };
				var l = sentinel.Split(separators, StringSplitOptions.None);
				title = l[0];
				dtstart = DateTime.Parse(l[1]);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "RenderHtmlEventsOnlyRaw", e.Message + e.StackTrace);
			}

			var events = new List<ZonelessEvent>();

			if ((String.IsNullOrEmpty(title) == false) && (dtstart != DateTime.MinValue))
			{
				var found = false;
				foreach (var evt in eventstore.events)
				{
					if (found)      // add everything after the sentinel
					{
						events.Add(evt);
					}
					if (evt.title == title && evt.dtstart == dtstart)
					{
						found = true;
						continue;     // skip the sentinel
					}

				}
			}
			eventstore.events = events;
			var builder = new StringBuilder();
			RenderEventsAsHtml(eventstore, builder, args);
			var html = builder.ToString();
			return html;
		}

		private void MaybeUseTestTemplate(Dictionary<string, object> args)
		{
			if (args == null)
				return;

			if (args.Keys.Contains("test") && (bool)args["test"] == true)  // maybe use the test template, which invokes the test js
			{
				var settings = GenUtils.GetSettingsFromAzureTable();
				var template_uri = new Uri(settings["test_template"]);
				this.template_html = HttpUtils.FetchUrl(template_uri).DataAsString();
			}
		}

		private void AdvanceToAnHourAgo(ZonelessEventStore eventstore)
		{
			var now_in_tz = Utils.NowInTz(this.calinfo.tzinfo);         // advance to an hour ago
			var dtnow = now_in_tz.LocalTime - TimeSpan.FromHours(1);
			eventstore.events = eventstore.events.FindAll(evt => evt.dtstart >= dtnow);
		}

		private string InsertTagSelector(string html, string view, bool eventsonly)
		{
			var uri = BlobStorage.MakeAzureBlobUri(this.id, "tags.json", false);
			var json = HttpUtils.FetchUrl(uri).DataAsString();
			var list_of_dict = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);
			var tags = new List<string>();
			foreach (var dict in list_of_dict)
				tags.Add(dict.Keys.First());
			var cmp = StringComparer.OrdinalIgnoreCase;
			tags.Sort(cmp);
			var sb = new StringBuilder();
			sb.Append("<select style=\"font-size:130%; margin-bottom:10px;\" id=\"tag_select\" onchange=\"show_view()\">\n");
			if (view == null)
				sb.Append("<option selected>all</option>\n");
			else
				sb.Append("<option>all</option>\n");
			foreach (var tag in tags)
			{
				if (tag != view)
					sb.Append("<option>" + tag + "</option>\n");
				else
					sb.Append("<option selected>" + tag + "</option>\n");
			}

			sb.Append("</select>\n");
			if (eventsonly)
			{
				html = html.Replace("<!-- begin events -->", "<!-- begin events -->\n" + sb.ToString());
				html = html.Replace("<!-- end events -->", "<!-- end events -->\n" + sb.ToString());
			}
			else
				html = html.Replace("<div id=\"tags\"></div>", "<div id=\"tags\">" + sb.ToString() + "</div>");
			return html;
		}



		// the default html rendering chunks by day, this method processes the raw list of events into
		// the ZonelessEventStore's event_dict like so:
		// key: d20100710
		// value: [ <ZonelessEvent>, <ZonelessEvent> ... ]
		public static void OrganizeByDate(ZonelessEventStore es)
		{
			es.GroupEventsByDatekey();
			es.SortEventSublists();
			es.SortDatekeys();
		}

		public void RenderEventsAsHtml(ZonelessEventStore es, StringBuilder builder, Dictionary<string,object> args)
		{
			if (args == null)
				args = new Dictionary<string, object>();
			//OrganizeByDate(es);
			var event_renderer = new EventRenderer(RenderEvtAsHtml);
			var year_month_anchors = new List<string>(); // e.g. ym201201
			var day_anchors = new List<string>(); // e.g. d20120119
			var current_time_of_day = TimeOfDay.Initialized;
			var sources_dict = new Dictionary<string, int>();
			int sequence_position = 0;
			bool sequence_at_zero = true;
			string last_source_key = null;
			string current_date_key = null;

			var announce_time_of_day = get_announce_time_of_day(args);
			BuildSourcesDict(es, announce_time_of_day, sources_dict);

			foreach (ZonelessEvent evt in es.events)
			{
				string datekey = Utils.DateKeyFromDateTime(evt.dtstart);
				var event_builder = new StringBuilder();
				var year_month_anchor = datekey.Substring(1, 6);

				if ((bool)args["mobile"] == false)  // skip day anchors and headers in mobile view
				{
					if (!year_month_anchors.Exists(ym => ym == year_month_anchor))
					{
						builder.Append(string.Format("\n<a name=\"ym{0}\"></a>\n", year_month_anchor));
						year_month_anchors.Add(year_month_anchor);
					}
					if (!day_anchors.Exists(d => d == datekey))
					{
						event_builder.Append(string.Format("\n<a name=\"{0}\"></a>\n", datekey));
						var date = Utils.DateFromDateKey(datekey);
						event_builder.Append(string.Format("<h1 id=\"{0}\" class=\"ed\"><b>{1}</b></h1>\n", datekey, date));
						day_anchors.Add(datekey);
						sequence_at_zero = true;
					}
				}

				if (announce_time_of_day && (bool) args["mobile"] == false) // skip time-of-day markers in mobile view
				{
					var time_of_day = Utils.ClassifyTime(evt.dtstart);

					if (time_of_day != current_time_of_day || datekey != current_date_key) // see http://blog.jonudell.net/2009/06/18/its-the-headings-stupid/
					{
						current_date_key = datekey;
						current_time_of_day = time_of_day;
						event_builder.Append(string.Format("<h2 id=\"t{0}\" class=\"timeofday\">{1}</h2>", this.time_of_day_counter, time_of_day.ToString()));
						this.time_of_day_counter += 1;
						sequence_at_zero = true;
					}
				}

				var source_key = MakeSourceKey(current_time_of_day, datekey, evt);
				if (source_key != last_source_key)
				{
					sequence_at_zero = true;
					last_source_key = source_key;
				}

				if (sequence_at_zero)
				{
					sequence_position = 1;
					sequence_at_zero = false;
				}
				else
					sequence_position++;

				if (evt.urls_and_sources.Count == 1)       // else multiple coalesced sources, nothing to hide
				{
					args["source_key"] = source_key;
					args["sequence_count"] = sources_dict[source_key];
					args["sequence_position"] = sequence_position;
				}

				AppendEvent(event_builder, event_renderer, evt, args );
				builder.Append(event_builder);
			}
		}

		private static void BuildSourcesDict(ZonelessEventStore es, bool announce_time_of_day, Dictionary<string, int> sources_dict)
		{
			var current_time_of_day = TimeOfDay.Initialized;
			foreach (ZonelessEvent evt in es.events)
			{
				string datekey = Utils.DateKeyFromDateTime(evt.dtstart);
				if (announce_time_of_day)
				{
					var time_of_day = Utils.ClassifyTime(evt.dtstart);
					if (time_of_day != current_time_of_day)
						current_time_of_day = time_of_day;
				}
				if (evt.urls_and_sources.Count == 1)  // if not coalesced
					UpdateSourcesDict(sources_dict, current_time_of_day, evt, datekey);
			}
		}

		private static void UpdateSourcesDict(Dictionary<string, int> sources_dict, TimeOfDay current_time_of_day, ZonelessEvent evt, string datekey)
		{
			var source_key = MakeSourceKey(current_time_of_day, datekey, evt);
			sources_dict.IncrementOrAdd(source_key);
		}

		private static string MakeSourceAttr(ZonelessEvent evt)
		{
		var re_source_attr = new Regex(@"[^\w]+");
		return re_source_attr.Replace(evt.source, "");
		}

		private static string MakeSourceKey(TimeOfDay current_time_of_day, string datekey, ZonelessEvent evt)
		{
			var source_attr = MakeSourceAttr(evt);

			return source_attr + "_" + datekey + "_" + string.Format("{0:HHmm}", evt.dtstart);
		}

		public string  RenderEvtAsHtml(ZonelessEvent evt, Calinfo calinfo, Dictionary<string,object> args)
		{
			if (evt.urls_and_sources == null)                                                             
				evt.urls_and_sources = new Dictionary<string, string>() { { evt.url, evt.source } };

			string dtstart;
			 if (evt.allday && evt.dtstart.Hour == 0)
				dtstart = "  ";
			else
				dtstart = evt.dtstart.ToString("ddd hh:mm tt");

			var month_day = evt.dtstart.ToString("M/d");

			string categories = "";
			List<string> catlist_links = new List<string>();
			if (!String.IsNullOrEmpty(evt.categories))
			{
				List<string> catlist = evt.categories.Split(',').ToList();
				foreach (var cat in catlist)
				{
					var category_url = string.Format("/{0}/html?view={1}", this.id, cat);
					catlist_links.Add(string.Format(@"<a title=""open the {1} view"" href=""{0}"">{1}</a>", category_url, cat));
				}
				categories = string.Format(@" <span class=""cat"">{0}</span>", string.Join(", ", catlist_links.ToArray()));
			}

			string label = "e" + this.event_counter.ToString();
			String description = ( String.IsNullOrEmpty(evt.description) || evt.description.Length < 10 ) ? "" : evt.description.UrlsToLinks();
			string show_desc = ( ! String.IsNullOrEmpty(description) ) ? String.Format(@"<span class=""sd""><a title=""show description ({0} chars)"" href=""javascript:show_desc('{1}')"">...</a></span>", description.Length, label) : "";

			if (args.ContainsKey("inline_descriptions") && (bool)args["inline_descriptions"] == true) // for view_calendar
			{
				var location = string.IsNullOrEmpty(evt.location) ? "" : String.Format("{0}<br/><br/>", evt.location);
				show_desc = String.Format(@"<div style=""text-indent:0""><p>{0}</div>", location + description);
			}

			if (args.ContainsKey("show_desc") && (bool)args["show_desc"] == false)  // in case need to suppress, not used yet
				show_desc = "";

			string add_to_cal = String.Format(@"<span class=""atc""><a title=""add to calendar"" href=""javascript:add_to_cal('{0}')"">+</a></span>", label);

			if (args.ContainsKey("add_to_cal") && (bool)args["add_to_cal"] == false) // for view_calendar
				add_to_cal = "";

			string visibility = "";
			string more = "";
			string source_key = "";
			string source_attr = "";
			int sequence_count = 1;
			int sequence_position = 1;
			string show_more_call;

			if (evt.urls_and_sources.Count == 1)
			{
				source_attr = MakeSourceAttr(evt); 
				sequence_count = (int)args["sequence_count"];
				source_key = (string)args["source_key"];
				sequence_position = (int)args["sequence_position"];
			}

			visibility = (sequence_count > 1 && sequence_position > 1) ? @" style=""display:none"" " : "";

			if (sequence_count > 1 && sequence_position == 1)
			{
				show_more_call = "javascript:show_more('" + source_key + "')";
				more = string.Format(@" <span class=""{0}""><a title=""show {2} more from {3}"" href=""{1}"">show {2} more</a></span>",
					source_key,
					show_more_call,
					sequence_count - 1,
					evt.source
					);
			}
			else
			{
				more = "";
			}

			var html = string.Format(
@"<a name=""{0}""></a>
<div id=""{0}"" class=""bl {10} {12}"" {13} xmlns:v=""http://rdf.data-vocabulary.org/#"" typeof=""v:Event"" >
<span class=""md"">{14}</span> 
<span class=""st"" property=""v:startDate"" content=""{1}"">{2}</span>
<span href=""{3}"" rel=""v:url""></span>
<span class=""ttl"">{4}</span> 
<span class=""src"" property=""v:description"">{5}</span> {6} 
{7}
{8}
{9}
{11}
</div>",
			label,                                                  // 0
			String.Format("{0:yyyy-MM-ddTHH:mm}", evt.dtstart),     // 1
			dtstart,                                                // 2
			evt.url,                                                // 3
			MakeTitleForRDFa(evt),                                  // 4
			evt.urls_and_sources.Keys.Count == 1 ? evt.source : "", // 5    suppress source if multiple
			categories,                                             // 6
			MakeGeoForRDFa(evt),                                    // 7
			show_desc,                                              // 8
			add_to_cal,                                             // 9
			source_attr,                                            // 10
			more,                                                   // 11
			source_key,                                             // 12
		    visibility,                                             // 13
            month_day												// 14                              
			);

			this.event_counter += 1;
			return html;
		}

		public string RenderEvtAsListItem(ZonelessEvent evt)
		{
			if (evt.urls_and_sources == null)
				evt.urls_and_sources = new Dictionary<string, string>() { { evt.url, evt.source } };

			string dtstart;
			if (evt.allday && evt.dtstart.Hour == 0)
				dtstart = "";
			else
				dtstart = evt.dtstart.ToString("ddd hh:mm tt");
	
			return string.Format(@"<li>{0} <a href=""{1}"">{2}</a> {3}</li>",
				dtstart,
				evt.urls_and_sources.First().Key,
				evt.title,
				evt.urls_and_sources.First().Value);
		}

		public string RenderEventsAsHtmlList(ZonelessEventStore es)
		{
			var sb = new StringBuilder();
			sb.Append(@"<ul style=""list-style-type:none"">");
			foreach (var evt in es.events)
				sb.Append(RenderEvtAsListItem(evt));
			sb.Append("</ul>");
			return sb.ToString();
		}

		public static string MakeTitleForRDFa(ZonelessEvent evt)
		{
			if (evt.urls_and_sources.Keys.Count == 1)
			{
				return string.Format("<a target=\"{0}\" property=\"v:summary\" title=\"{1}\" href=\"{2}\">{3}</a>",
					Configurator.default_html_window_name, 
					//evt.source,
					"open event page on source site",
					evt.url, 
					evt.title);
			}

			if (evt.urls_and_sources.Keys.Count > 1)
			{
				var evt_title = @"<span property=""v:summary"">" + evt.title + "</span> [";
				int count = 0;
				foreach (var url in evt.urls_and_sources.Keys )
				{
					var source = evt.urls_and_sources[url];
					count++;
					evt_title += string.Format(@"<a target=""{0}"" title=""{1}"" href=""{2}"">&nbsp;{3}&nbsp;</a>",
						Configurator.default_html_window_name,
						source,
						url,
						count);
				}
				evt_title += "]";
				return evt_title;
			}
			GenUtils.PriorityLogMsg("warning", "MakeTitleForRDFa: no title", null);
			return "";
		}

		private string MakeGeoForRDFa(ZonelessEvent evt)
		{
			string geo = "";
			if (this.calinfo.hub_enum == HubType.where)
				geo = string.Format(
@"<span rel=""v:location"">
    <span rel=""v:geo"">
       <span typeof=""v:Geo"">
          <span property=""v:latitude"" content=""{0}"" ></span>
          <span property=""v:longitude"" content=""{1}"" ></span>
       </span>
    </span>
  </span>",
				evt.lat != null ? evt.lat : this.calinfo.lat,
				evt.lon != null ? evt.lon : this.calinfo.lon
				);
			return geo;
		}

		// just today's events
		public string RenderTodayAsHtml()
		{
			ZonelessEventStore es = FindTodayEvents();
			var sb = new StringBuilder();
			var args = new Dictionary<string, object>() { { "show_desc", false }, { "add_to_cal", false } };
			foreach (var evt in es.events)
				sb.Append(RenderEvtAsHtml(evt, this.calinfo, args));
			return sb.ToString();
		}

		#endregion html

		#region ics

		public string RenderIcs()
		{
			return RenderIcs(eventstore: null, view: null, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		public string RenderIcs(string view)
		{
			return RenderIcs(eventstore: null, view: view, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		public string RenderIcs(int count)
		{
			return RenderIcs(eventstore: null, view: null, count: count, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		public string RenderIcs(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string,object> args)
		{
			eventstore = GetEventStore(eventstore, view, count, from, to);
			var ical = new DDay.iCal.iCalendar();
			Collector.AddTimezoneToDDayICal(ical, this.calinfo.tzinfo);
			var tzid = this.calinfo.tzinfo.Id;

			foreach (var evt in eventstore.events)
			{
				var ical_evt = new DDay.iCal.Event();
				ical_evt.Start = new DDay.iCal.iCalDateTime(evt.dtstart);
				ical_evt.Start.TZID = tzid;
				ical_evt.End = new DDay.iCal.iCalDateTime(evt.dtend);
				ical_evt.End.TZID = tzid;
				ical_evt.Summary = evt.title;
				ical_evt.Url = new Uri(evt.url);

				if (evt.description != null)
					ical_evt.Description = evt.description + " " + evt.url;
				else
					ical_evt.Description = evt.url;

				ical_evt.Description = evt.description;
				ical_evt.Location = evt.location;
				Collector.AddCategoriesFromCatString(ical_evt, evt.categories);
				ical.Children.Add(ical_evt);
			}

			var ics_text = Utils.SerializeIcalToIcs(ical);

			return ics_text;
		}

		// in support of add-to-calendar
		public static string RenderEventAsIcs(string elmcity_id, string summary, string start, string end, string description, string location, string url)
		{
			try
			{
				var calinfo = Utils.AcquireCalinfo(elmcity_id);
				var tzname = calinfo.tzname;
				var tzid = calinfo.tzinfo.Id;
				var ical = new DDay.iCal.iCalendar();
				Collector.AddTimezoneToDDayICal(ical, Utils.TzinfoFromName(tzname));
				var evt = new DDay.iCal.Event();
				evt.Summary = summary;
				evt.Description = Utils.MakeAddToCalDescription(description, url, location);
				evt.Location = location;
				evt.Url = new Uri(url);
				var dtstart = DateTime.Parse(start);
				evt.Start = new DDay.iCal.iCalDateTime(dtstart, tzname);
				evt.Start.TZID = tzid;
				if (evt.End != null)
				{
					var dtend = DateTime.Parse(end);
					evt.End = new DDay.iCal.iCalDateTime(dtend, tzname);
					evt.End.TZID = tzid;
				}
				ical.Events.Add(evt);
				var serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer();
				return serializer.SerializeToString(ical);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "RenderEventAsIcs", e.Message);
				return "exception: " + e.Message;
			}
		}

		#endregion ics

		#region rss

		public string RenderRss()
		{
			return RenderRss(eventstore: null, view: null, count: Configurator.rss_default_items, from: DateTime.MinValue, to: DateTime.MinValue, args: null);
		}

		public string RenderRss(int count)
		{
			return RenderRss(eventstore: null, view: null, count: count, from: DateTime.MinValue, to: DateTime.MinValue, args: null);
		}

		public string RenderRss(string view)
		{
			return RenderRss(eventstore: null, view: view, count: Configurator.rss_default_items, from: DateTime.MinValue, to: DateTime.MinValue, args: null);
		}

		public string RenderRss(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string,object> args)
		{
			try
			{
				eventstore = GetEventStore(eventstore, view, count, from, to);
				var query = string.Format("view={0}&count={1}", view, count);
				return Utils.RssFeedFromEventStore(this.id, query, eventstore);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", String.Format("RenderRss: view {0}, count {1}", view, count), e.Message);
				return String.Empty;
			}
		}

		#endregion rss

		#region tags

		public string RenderTagCloudAsHtml()
		{
			var tagcloud = MakeTagCloud(null); // see http://blog.jonudell.net/2009/09/16/familiar-idioms/
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
			var tagcloud = MakeTagCloud(null);
			return JsonConvert.SerializeObject(tagcloud);
		}

		public List<string> GetTags(ZonelessEventStore es)
		{
			var dicts = MakeTagCloud(es);
			var tags = dicts.Select(x => x.Keys.First());
			return tags.ToList();
		}

		// see http://blog.jonudell.net/2009/09/16/familiar-idioms/
		public List<Dictionary<string, int>> MakeTagCloud(ZonelessEventStore es)
		{
			if ( es == null )
				es = this.es_getter(this.cache);
			var tagquery =
				from evt in es.events
				where evt.categories != null
				from tag in evt.categories.Split(',')
				where tag != ""
				group tag by tag into occurrences
				orderby occurrences.Count() descending
				select new Dictionary<string, int>() { { occurrences.Key, occurrences.Count() } };
			return tagquery.ToList();
		}

		#endregion

		private ZonelessEventStore GetEventStore(ZonelessEventStore es, string view, int count, DateTime from, DateTime to)
		{
			// see RenderDynamicViewWithCaching:
			// view_data = Encoding.UTF8.GetBytes(view_renderer(eventstore:null, view:view, view:count));
			// the renderer might be, e.g., CalendarRenderer.RenderHtml, which calls this method
			if (es == null)  // if no eventstore passed in (e.g., for testing)
				es = this.es_getter(this.cache); // get the eventstore. if the getter is GetEventStoreWithCaching
			// then it will use HttpUtils.RetrieveBlobFromServerCacheOrUri
			// which gets from cache if it can, else fetches uri and loads cache
			es.events = Filter(view, count, from, to, es); // then filter if requested
			return es;
		}

		// take a string representation of a set of events, in some format
		// take a per-event renderer for that format
		// take an event object
		// call the renderer to add the event object to the string representation
		// currently uses: RenderEvtAsHtml, RenderEvtAsXml
		private void AppendEvent(StringBuilder eventstring, EventRenderer event_renderer, ZonelessEvent evt, Dictionary<string,object> args)
		{
			eventstring.Append(event_renderer(evt, this.calinfo, args ) );
		}

		// produce a javascript version of today's events, for
		// inclusion on a site using <script src="">
		public string RenderJsWidget()
		{
			ZonelessEventStore es = FindTodayEvents();
			var html = this.RenderEventsAsHtmlList(es);
			html = html.Replace("\'", "\\\'").Replace("\"", "\\\"");
			html = html.Replace(Environment.NewLine, " ");
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
				es = new ZonelessEventStore(this.calinfo);
				GenUtils.PriorityLogMsg("exception", "CalendarRenderer.FindTodayEvents", e.Message + e.StackTrace);
			}
			return es;
		}

		enum FilterMode { single, or_list, and_list };

		// possibly filter an event list by view or count
		public List<ZonelessEvent> Filter(string view, int count, DateTime from, DateTime to, ZonelessEventStore es)
		{
			var events = es.events;

			FilterMode mode;

			if ( from != DateTime.MinValue && to != DateTime.MinValue )
				events = events.FindAll(evt => evt.dtstart >= from && evt.dtstart <= to);  // reduce to time window

			if (view != null) // reduce to matching categories
			{
				if (view.Contains('|'))
					mode = FilterMode.or_list;
				else if (view.Contains(','))
					mode = FilterMode.and_list;
				else
					mode = FilterMode.single;

				var view_list = new List<string>();

				switch (mode)
				{
					case FilterMode.and_list:
						view_list = view.Split(',').ToList();
						foreach (var view_item in view_list)
						{
							var and_item = view_item.Trim();
							events = events.FindAll(evt => evt.categories != null && evt.categories.Split(',').ToList().Contains(and_item));
						}
						break;
					case FilterMode.or_list:
						view_list = view.Split('|').ToList();
						events = events.FindAll(evt => evt.categories != null && evt.categories.Split(',').ToList().Any(x => view_list.Contains(x.Trim())));
						break;
					case FilterMode.single:
						events = events.FindAll(evt => evt.categories != null && evt.categories.Split(',').ToList().Contains(view));
						break;
					default:
						break;
				}

			}
			if (count != 0)   // reduce to first count events
				events = events.Take(count).ToList();
			return events;
		}

		// the CalendarRenderer object uses this to get the pickled object that contains an eventstore,
		// from the CalendarRender's cache if available, else fetching bytes
		private ZonelessEventStore GetEventStoreWithCaching(ICache cache)
		{
			var es = new ZonelessEventStore(this.calinfo);
			var obj_bytes = (byte[])CacheUtils.RetrieveBlobAndEtagFromServerCacheOrUri(cache, es.uri)["response_body"];
			return (ZonelessEventStore)BlobStorage.DeserializeObjectFromBytes(obj_bytes);
		}

		private ZonelessEventStore GetEventStoreWithoutCaching(ICache cache)
		{
			var es = new ZonelessEventStore(this.calinfo);
			var obj_bytes = HttpUtils.FetchUrl(es.uri).bytes;
			return (ZonelessEventStore)BlobStorage.DeserializeObjectFromBytes(obj_bytes);
		}

		// used in WebRole for views built from pickled objects that are cached
		public string RenderDynamicViewWithCaching(ControllerContext context, string view_key, ViewRenderer view_renderer, string view, int count, DateTime from, DateTime to, Dictionary<string,object> args)
		{
			try
			{
				var view_is_cached = this.cache[view_key] != null;
				byte[] view_data;
				byte[] response_body;
				if (view_is_cached)
					view_data = (byte[])cache[view_key];
				else
					view_data = Encoding.UTF8.GetBytes(view_renderer(eventstore: null, view: view, count: count, from: from, to: to, args: args));

				response_body = CacheUtils.MaybeSuppressResponseBodyForView(context, view_data);
				return Encoding.UTF8.GetString(response_body);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "RenderDynamicViewWithCaching: " + view_key, e.Message + e.StackTrace);
				return RenderDynamicViewWithoutCaching(context, view_renderer, view, count, from, to, args: args);
			}
		}

		public string RenderDynamicViewWithoutCaching(ControllerContext context, ViewRenderer view_renderer, String view, int count, DateTime from, DateTime to, Dictionary<string,object> args)
		{
			ZonelessEventStore es = GetEventStoreWithCaching(this.cache);
			return view_renderer(es, view, count, from, to, args);
		}

		private void ResetCounters()
		{
			this.event_counter = 0;
			//this.day_counter = 0;
			this.time_of_day_counter = 0;
		}

	}

}
