﻿/* ********************************************************************************
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
		public string default_template_html;

		public string default_js_url;
		public string test_js_url;

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

		public DateTime timestamp;

		public CalendarRenderer(string id)
		{
			this.timestamp = DateTime.UtcNow;
			this.calinfo = Utils.AcquireCalinfo(id);
			this.cache = null;
			this.ResetCounters();
			this.es_getter = new EventStoreGetter(GetEventStoreWithCaching);
			try
			{
				this.id = id;

				try
				{
					var settings = GenUtils.GetSettingsFromAzureTable();
					this.default_js_url =	BlobStorage.MakeAzureBlobUri("admin", settings["elmcity_js"]).ToString();
					this.test_js_url =		BlobStorage.MakeAzureBlobUri("admin", settings["elmcity_js_test"]).ToString();
					settings = null;
				}
				catch (Exception e)
				{
					this.default_js_url =	BlobStorage.MakeAzureBlobUri("admin", "elmcity-1.7.js").ToString();
					this.test_js_url =		BlobStorage.MakeAzureBlobUri("admin", "elmcity-1.7-test.js").ToString();
					GenUtils.LogMsg("exception", "CalendarRenderer: setting js urls", e.Message);
				}

				try
				{
					this.template_html = HttpUtils.FetchUrl(calinfo.template_url).DataAsString();
					this.default_template_html = this.template_html;
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "CalendarRenderer: cannot fetch template", e.InnerException.Message);
					throw (e);
				}

				//  this.ical_sources = Collector.GetIcalSources(this.id);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "CalenderRenderer.CalendarRenderer: " + id, e.Message + e.StackTrace);
			}

		}

		#region xml

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
			eventstore = GetEventStore(eventstore, view, count, from, to, args);

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

			var lat = ! String.IsNullOrEmpty(evt.lat) ? evt.lat : "";
			var lon = ! String.IsNullOrEmpty(evt.lon) ? evt.lon : "";
			xml.Append(string.Format("<lat>{0}</lat>\n", lat));
			xml.Append(string.Format("<lon>{0}</lon>\n", lon));

			xml.Append("</event>\n");
			return xml.ToString();
		}

		#endregion xml

		#region text

		// render an eventstore as xml, optionally limited by view and/or count
		public string RenderText(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string, object> args)
		{
			eventstore = GetEventStore(eventstore, view, count, from, to, args);

			var text = new StringBuilder();

			var event_renderer = new EventRenderer(RenderEvtAsText);

			var eventstring = new StringBuilder();

			foreach (var evt in eventstore.events)
				AppendEvent(eventstring, event_renderer, evt, new Dictionary<string, object>());

			text.Append(eventstring.ToString());

			return text.ToString();
		}

		// render a single event as text
		private string RenderEvtAsText(ZonelessEvent evt, Calinfo calinfo, Dictionary<string, object> args)
		{
			var text = new StringBuilder();

			text.AppendLine(evt.title);
			var start = evt.dtstart.ToString("M/d/yyyy h:m tt").Replace(":0 ", " ");
			text.AppendLine(start);
			if ( ! String.IsNullOrEmpty(evt.source) )
				text.AppendLine(evt.source);
			if ( ! String.IsNullOrEmpty(evt.location) )
				text.AppendLine(evt.location);
			if ( ! String.IsNullOrEmpty(evt.description) && evt.description != evt.location)
				text.AppendLine(evt.description);
			text.AppendLine();

			return text.ToString();
		}

		#endregion

		#region json

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
			eventstore = GetEventStore(eventstore, view, count, from, to, args);
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
			var description = evt.description.Replace("'", "\\'").Replace("\n", "<br>").Replace("\r", "");
			string location = "";
			if (!String.IsNullOrEmpty(evt.location))
				location = String.Format("<br>{0}", evt.location.Replace("'", "\\'")).Replace("\n", "<br>").Replace("\r", "");
			description = ( "<span class=\"desc\">" + location + "<br><br>" + description + "</span>").UrlsToLinks();
			return String.Format(jsonp + "('" + description + "')") ;
		}

		public string RenderFeedAsJson(string source, string view)  
		{
			var es = this.es_getter(this.cache);
			var events = es.events.FindAll(evt => FeedComesFrom(evt, source));
			if ( ! String.IsNullOrEmpty(view) )
				events = events.FindAll(evt => evt.categories != null && evt.categories.Split(',').ToList().Contains(view));
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

		public string RenderHtml()
		{
			return RenderHtml(eventstore: null, view: null, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		public string RenderHtml(ZonelessEventStore es)
		{
			return RenderHtml(eventstore: es, view: null, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
		}

		public string RenderHtml(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string, object> args)
		{
			if (args == null)
				args = new Dictionary<string, object>();

			this.ResetCounters();

			string day_anchors = "";
			try
			{
				day_anchors = GetDayAnchorsAsJson(eventstore, view);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "GetDayAnchors", e.Message + e.StackTrace);
			}

			args["AdvanceToAnHourAgo"] = true;
			eventstore = GetEventStore(eventstore, view, count, from, to, args);

			MaybeUseAlternateTemplate(args);

			var builder = new StringBuilder();
			RenderEventsAsHtml(eventstore, builder, args);

			if (args.ContainsKey("bare_events") && (bool)args["bare_events"] == true)
				return builder.ToString();

			var html = this.template_html.Replace("__EVENTS__", builder.ToString());

			if (args.ContainsKey("taglist") && (bool)args["taglist"] == true)
				html = this.InsertTagSelector(html, view, eventsonly: false);

			html = html.Replace("__APPDOMAIN__", ElmcityUtils.Configurator.appdomain);

			html = html.Replace("__ID__", this.id);

			var css_url = this.GetCssUrl(args);  // default to calinfo.css, maybe override with args["theme"]
			html = html.Replace("__CSSURL__", css_url);

			html = HandleJsUrl(html, args);

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

			html = RenderBadges(html);

			html = html.Replace("__GENERATED__", System.DateTime.UtcNow.ToString());

			html = html.Replace("__METADATA__", day_anchors);

			return html;
		}

		private List<string> GetDayAnchors(ZonelessEventStore es, string view)
		{
			if (es == null)  // if no eventstore passed in (e.g., for testing)
				es = this.es_getter(this.cache); 

			this.AdvanceToAnHourAgo(es);

			var filtered_events = ViewFilter(view, es.events);
			
			var day_anchors = new List<string>();

			foreach (ZonelessEvent evt in filtered_events)
			{
				string datekey = Utils.DateKeyFromDateTime(evt.dtstart);
				if (!day_anchors.Exists(d => d == datekey))
				{
					day_anchors.Add(datekey);
				}
			}
			return day_anchors;
		}

		public string GetDayAnchorsAsJson(ZonelessEventStore es, string view)
		{
			var events = this.GetDayAnchors(es, view);

			var json = JsonConvert.SerializeObject(events);

			return json;
		}

		private string RenderBadges(string html)
		{
			html = html.Replace("__EVENTFUL_LOGO_DISPLAY__",	this.calinfo.show_eventful_badge	? "inline" : "none");
			html = html.Replace("__EVENTBRITE_LOGO_DISPLAY__",	this.calinfo.show_eventbrite_badge	? "inline" : "none");
			html = html.Replace("__MEETUP_LOGO_DISPLAY__",		this.calinfo.show_meetup_badge		? "inline" : "none");
			html = html.Replace("__FACEBOOK_LOGO_DISPLAY__",	this.calinfo.show_facebook_badge	? "inline" : "none");
			return html;
		}

		private string HandleJsUrl(string html, Dictionary<string,object> args)
		{
			var test = args.ContainsKey("test") && (bool)args["test"];

			var jsurl = args.ContainsKey("jsurl") ? (string)args["jsurl"] : null;

			if (this.default_js_url == null)                                                              // only until new renderers deployed
				this.default_js_url = "http://elmcity.blob.core.windows.net/admin/elmcity-1.7.js";

			if ( this.test_js_url == null )
				this.test_js_url = "http://elmcity.blob.core.windows.net/admin/elmcity-1.7-test.js";

			var test_js = this.test_js_url;
			var default_js = this.default_js_url;

			if (jsurl != null)  // override defaults if jsurl on url-line
			{
				var js = BlobStorage.MakeAzureBlobUri("admin", (string)args["jsurl"]).ToString(); ;
				test_js = js;
				default_js = js;
			}

			if (test)
				html = html.Replace("__JSURL__", test_js);
			else
				html = html.Replace("__JSURL__", default_js);

			return html;
		}

		public string MakeTitle(string view)
		{
			var _view = string.IsNullOrEmpty(view) ? " " : " " + view + " ";
			string _title;
			switch (this.calinfo.hub_enum)
			{
				case HubType.where:
					_title = String.IsNullOrEmpty(this.calinfo.title) ? this.calinfo.where : this.calinfo.title;
					break;
				case HubType.what:
					_title = String.IsNullOrEmpty(this.calinfo.title) ? this.calinfo.what : this.calinfo.title;
					break;
				case HubType.region:
					_title = String.IsNullOrEmpty(this.calinfo.title) ? this.calinfo.where : this.calinfo.title;
					break;
				default:
					_title = "";
					break;
			}
			return _title + _view + "events";
		}

	public string GetCssUrl(Dictionary<string,object> args)
	{
		if (args == null)
			return this.calinfo.css;

		string mobile;
		if ( args.ContainsKey("mobile") )
			mobile = (bool) args["mobile"] ? "yes" : "no";
		else
			mobile = "no";
		string mobile_long = args.ContainsKey("mobile_long") ? (string) args["mobile_long"] : "";
		string ua = args.ContainsKey("ua") ? (string) args["ua"] : "";

		string css_url = this.calinfo.css;

		if (args.ContainsKey("theme") && args["theme"] != null )
		{
			var theme_name = args["theme"].ToString();
			css_url = String.Format("http://{0}/get_css_theme?theme_name={1}&mobile={2}&mobile_long={3}&ua={4}",
				ElmcityUtils.Configurator.appdomain,
				theme_name,
				mobile,
				mobile_long,
				ua);
		}

		return css_url;

	}

		private static bool IsDefaultThemeDict(Dictionary<string,Dictionary<string,string>> theme_dict)
		{
			return theme_dict.Keys.Count == 1 && theme_dict.Keys.ToList()[0] == "default";
		}

		public string RenderHtmlEventsOnly(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string,object> args)
		{
			args["AdvanceToAnHourAgo"] = true;

			var day_anchors = GetDayAnchorsAsJson(eventstore, view);

			eventstore = GetEventStore(eventstore, view, count, from, to, args);

			var builder = new StringBuilder();

			MaybeUseAlternateTemplate(args);

			RenderEventsAsHtml(eventstore, builder, args);

			var html = this.template_html.Replace("__EVENTS__", builder.ToString());

			if (args.ContainsKey("taglist") && (bool) args["taglist"] == true )
				html = this.InsertTagSelector(html, view, eventsonly: true);

			html = html.Replace("__APPDOMAIN__", ElmcityUtils.Configurator.appdomain);

			html = html.Replace("__ID__", this.id);
			html = html.Replace("__TITLE__", MakeTitle(view));
			html = html.Replace("__META__", MakeTitle(view) + " calendars happenings schedules");

			var css_url = GetCssUrl(args);
			html = html.Replace("__CSSURL__", css_url);

			html = HandleJsUrl(html, args);

			html = html.Replace("__GENERATED__", System.DateTime.UtcNow.ToString());

			html = html.Replace("__METADATA__", day_anchors);

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

			eventstore = GetEventStore(eventstore, view, count, from, to, args);
			count = (int) args["mobile_event_count"];                        // maybe apply further reduction
			eventstore.events = eventstore.events.Take(count).ToList();

			var html = RenderHtmlEventsOnly(eventstore, view, count, from, to, args);

			string mobile_long = "";
			string ua = "";
			if (args.ContainsKey("mobile_long") && args.ContainsKey("ua"))
			{
				mobile_long = (string)args["mobile_long"];   // the longest dimension of detected mobile device
				ua = (string)args["ua"];                     // user agent
			}

			html = html.Replace("get_css_theme?", string.Format("get_css_theme?mobile=yes&mobile_long={0}&ua={1}&", mobile_long, ua));

			if ( args.ContainsKey("mobile_detected") && (bool) args["mobile_detected"] )
				html = html.Replace("__MOBILE_NOT_DETECTED__", "__MOBILE_DETECTED__");

			return html;
		}

		/*
		public string RenderHtmlEventsOnlyRaw(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, Dictionary<string, object> args)
		{
			this.ResetCounters();

			MaybeUseAlternateTemplate(args);

			eventstore = GetEventStore(eventstore, view, count, from, to, args);
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
		 */

		public void MaybeUseAlternateTemplate(Dictionary<string, object> args)
		{
			try
			{
				if (args == null)
					return;

				string template = null;

				if (args.Keys.Contains("template") )
					template = (string) args["template"];

				if ( ! String.IsNullOrEmpty(template) )
				{
					try
					{
						Uri template_uri;
						if (template.StartsWith("http://"))
							template_uri = new Uri(template);
						else
							template_uri = BlobStorage.MakeAzureBlobUri("admin", template);

						this.template_html = HttpUtils.FetchUrl(template_uri).DataAsString();
					}
					catch (Exception e)
					{
						GenUtils.LogMsg("exception", "MaybeUseAlternateTemplate", e.Message);
						this.template_html = this.default_template_html;
					}
				}
				else
				{
					this.template_html = this.default_template_html;
				}
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "UseTestTemplate", e.Message + e.StackTrace);
				return;
			}
		}

		private void AdvanceToAnHourAgo(ZonelessEventStore eventstore)
		{
			var now_in_tz = Utils.NowInTz(this.calinfo.tzinfo);         // advance to an hour ago
			var dtnow = now_in_tz.LocalTime - TimeSpan.FromHours(1);
			eventstore.events = eventstore.events.FindAll(evt => evt.dtstart >= dtnow);
		}

		public string InsertTagSelector(string html, string view, bool eventsonly)
		{
			var list_of_dict = Utils.GetTagsAndCountsForHubAsListDict(this.id);
			var tags = new List<string>();
			var counts = new Dictionary<string,string>();
			foreach (var dict in list_of_dict)
			{
				var tag = dict.Keys.First();
				tags.Add(tag);
				counts[tag] = dict[tag];
			}
			var cmp = StringComparer.OrdinalIgnoreCase;
			tags.Sort(cmp);
			var sb = new StringBuilder();
			sb.Append("<select style=\"margin-bottom:10px; margin-top:10px;\" id=\"tag_select\" onchange=\"show_view()\">\n");
			if (view == null)
				sb.Append("<option selected>all</option>\n");
			else
				sb.Append("<option>all</option>\n");
			foreach (var tag in tags)
			{
				string maybe_truncated_tag = tag;
				if ( tag.Length > Configurator.max_tag_chars )
					maybe_truncated_tag = tag.Substring(0, Configurator.max_tag_chars) + "&#8230;";
				var option = "<option value=\"" + tag + "\">" + maybe_truncated_tag + " (" + counts[tag] + ")" + "</option>\n";
				if (tag == view)
					option = option.Replace("<option ", "<option selected ");
				sb.Append(option);
			}

			sb.Append("</select>\n");
			if (eventsonly)
			{
				html = html.Replace("<!-- begin events -->", "<!-- begin events -->\n" + sb.ToString()); // insert tags at top of event list
			}
			else
			{
				html = html.Replace("<!--__CATEGORIES__-->", "<div style=\"font-style:italic\">categories</div>"); // label the tags
				html = html.Replace("<!--__TAGS__-->", sb.ToString());   // insert tags in sidebar
				var tags2 = sb.ToString().Replace("id=\"tag_select\"", "id=\"tag_select2\""); // secondary for when main is gone
				html = html.Replace("<!--__TAGS2__-->", tags2);  
			}
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

			bool bare_events = args.ContainsKey("bare_events") && (bool)args["bare_events"] == true;

			bool mobile = args.ContainsKey("mobile") && (bool)args["mobile"] == true;

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

				if (! year_month_anchors.Exists(ym => ym == year_month_anchor) )
					{
						builder.Append(string.Format("\n<a name=\"ym{0}\"></a>\n", year_month_anchor));
						year_month_anchors.Add(year_month_anchor);
					}

				if (! day_anchors.Exists(d => d == datekey))
				{
					event_builder.Append(string.Format("\n<a name=\"{0}\"></a>\n", datekey));
					var date = Utils.DateFromDateKey(datekey);
					event_builder.Append(string.Format("<h1 id=\"{0}\" class=\"ed\"><b>{1}</b></h1>\n", datekey, date));
					day_anchors.Add(datekey);
					sequence_at_zero = true;
				}

				if (announce_time_of_day && mobile == false && bare_events == false) // skip time-of-day markers in mobile view
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
					var category_url = string.Format("javascript:show_view('{0}')", cat);
					catlist_links.Add(string.Format(@"<a title=""open the {1} view"" href=""{0}"">{1}</a>", category_url, cat));
				}
				categories = string.Format(@" <span class=""cat"">{0}</span>", string.Join(", ", catlist_links.ToArray()));
			}

			string label;

			if (args.ContainsKey("bare_events") && (bool)args["bare_events"] == true) // injecting, need to decorate the id
				label = "e_" + month_day.Replace("/", "_") + "_" + this.event_counter.ToString();
			else
				label = "e" + this.event_counter.ToString();

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
			eventstore = GetEventStore(eventstore, view, count, from, to, args);
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
				eventstore = GetEventStore(eventstore, view, count, from, to, args);
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

		private ZonelessEventStore GetEventStore(ZonelessEventStore es, string view, int count, DateTime from, DateTime to, Dictionary<string,object> args)
		{
			if (args == null)
				args = new Dictionary<string, object>();
			// see RenderDynamicViewWithCaching:
			// view_data = Encoding.UTF8.GetBytes(view_renderer(eventstore:null, view:view, view:count));
			// the renderer might be, e.g., CalendarRenderer.RenderHtml, which calls this method
			if (es == null)  // if no eventstore passed in (e.g., for testing)
				es = this.es_getter(this.cache); // get the eventstore. if the getter is GetEventStoreWithCaching
			// then it will use HttpUtils.RetrieveBlobFromServerCacheOrUri
			// which gets from cache if it can, else fetches uri and loads cache
			if (args.ContainsKey("AdvanceToAnHourAgo") && (bool)args["AdvanceToAnHourAgo"] == true)
				AdvanceToAnHourAgo(es);
			es.events = Filter(view, count, from, to, es); // then filter if requested
			return es;
		}

		private ZonelessEventStore GetEventStore(ZonelessEventStore es, string view, Dictionary<string, object> args)
		{
			return GetEventStore(es, view, 0, DateTime.MinValue, DateTime.MinValue, args);
		}

		private ZonelessEventStore GetEventStore(ZonelessEventStore es, int count, Dictionary<string,object> args)
		{
			return GetEventStore(es, null, count, DateTime.MinValue, DateTime.MinValue, args);
		}

		private ZonelessEventStore GetEventStore(ZonelessEventStore es, DateTime from, DateTime to, Dictionary<string, object> args)
		{
			return GetEventStore(es, null, 0, from, to, args);
		}

		public ZonelessEventStore GetEventStoreRoundedUpToLastFullDay(int max, ZonelessEventStore es, string view, int count, DateTime from, DateTime to, Dictionary<string, object> args)
		{
			if (args == null)
				args = new Dictionary<string, object>();

			if (es == null) 
				es = this.es_getter(this.cache);

			if ( es.events.Count <= max )
				return GetEventStore(es, from, to, args);

			var rounded_list = MakeRoundedList(max, es);

			es.events = rounded_list;

			return es;
		}

		public static List<ZonelessEvent> MakeRoundedList(int max, ZonelessEventStore es)
		{
			var rounded_list = new List<ZonelessEvent>();

			foreach (var datekey in es.event_dict.Keys)
			{
				var sublist = es.event_dict[datekey];
				rounded_list.AddRange(sublist);
				if (rounded_list.Count > max)
					break;
			}
			return rounded_list;
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

		// possibly filter an event list by view or count
		public List<ZonelessEvent> Filter(string view, int count, DateTime from, DateTime to, ZonelessEventStore es)
		{
			var events = es.events;

			if (!String.IsNullOrEmpty(view))
				events = ViewFilter(view, events);                        

			if (from != DateTime.MinValue && to != DateTime.MinValue)
				events = TimeFilter(from, to, events);                    

			if (count != 0)
				events = CountFilter(count, events);                     
		
			return events;
		}

		private static List<ZonelessEvent> ViewFilter(string view, List<ZonelessEvent> events)
		{
			try
			{
				if (view == null)
					return events;

				var view_list = view.Split(',').ToList();    // view=newportnewsva,sports,-soccer,-baseball
				var remainder = new List<string>();

				foreach (var view_item in view_list)
				{
					if (view_item.StartsWith("-"))      // do exclusions first
					{
						var item = view_item.TrimStart('-');
						events = events.FindAll(evt => evt.categories != null && !evt.categories.Split(',').ToList().Contains(item));
					}
					else
						remainder.Add(view_item);
				}

				foreach (var view_item in remainder)    // then inclusions
				{
					events = events.FindAll(evt => evt.categories != null && evt.categories.Split(',').ToList().Contains(view_item));
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ViewFilter", e.Message + e.StackTrace);
			}

			return events;
		}

		private static List<ZonelessEvent> CountFilter(int count, List<ZonelessEvent> events)
		{
			try
			{
				events = events.Take(count).ToList();
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ViewFilter", e.Message + e.StackTrace);
			}

			return events;
		}

		private static List<ZonelessEvent> TimeFilter(DateTime from, DateTime to, List<ZonelessEvent> events)
		{
			try
			{
				events = events.FindAll(evt => evt.dtstart >= from && evt.dtstart <= to);  // reduce to time window
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ViewFilter", e.Message + e.StackTrace);
			}

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
