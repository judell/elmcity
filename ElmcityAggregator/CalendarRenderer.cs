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
using System.Web.Caching;

namespace CalendarAggregator
{

	// render calendar data in various formats

	[Serializable]
	public class CalendarRenderer
	{
		private string id;

		public bool is_region;

		public Calinfo calinfo;
		
		public string template_html;
		public string default_template_html;

		public string default_js_url;

		public Dictionary<string, string> category_images;
		public Dictionary<string, string> source_images;

		//public List<String> default_arg_keys = new List<string>();
		
		public string default_args_json;

		public Dictionary<string,object> default_args;  

		// data might be available in cache,
		// this interface abstracts the cache so its logic can be tested
		public ICache cache
		{
			get { return _cache; }
			set { _cache = value; }
		}
		private ICache _cache;

		// points to a method for rendering individual events in various formats
		public delegate string EventRenderer(ZonelessEvent evt, Calinfo calinfo, Dictionary<string,object> args);

		// points to a method for rendering views of events in various formats
		public delegate string ViewRenderer(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, string source, Dictionary<string,object> args);

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

		public int max_events;

		public CalendarRenderer(string id)
		{
			this.timestamp = DateTime.UtcNow;
			this.calinfo = Utils.AcquireCalinfo(id);
			this.cache = null;
			this.ResetCounters();
			this.es_getter = new EventStoreGetter(GetEventStoreWithCaching);
			this.category_images = new Dictionary<string, string>();
			this.source_images = new Dictionary<string, string>();
			this.is_region = Utils.IsRegion(id);
			this.default_js_url = "http://elmcity.blob.core.windows.net/admin/elmcity-1.17.js";
			this.default_args = new Dictionary<string, object>();

			try
			{
				this.id = id;

				try
				{
					var metadict = Metadata.LoadMetadataForIdFromAzureTable(id);
					if (metadict.ContainsKey("args"))
					{
						this.default_args_json = metadict["args"];
						this.default_args = JsonConvert.DeserializeObject<Dictionary<string, object>>(this.default_args_json);
					}
					else
					{
						this.default_args = new Dictionary<string, object>();
					}
				}
				catch (Exception e)
				{
					GenUtils.LogMsg("exception", "CalendarRenderer: acquiring args", e.Message);
				}

				try
				{
					this.template_html = HttpUtils.FetchUrl(calinfo.template_url).DataAsString();
					this.default_template_html = this.template_html;
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "CalendarRenderer: cannot fetch template", e.Message);
					throw (e);
				}

				try
				{
					GetImages("category");
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "CalendarRenderer: loading category images", e.Message);
					throw (e);
				}

				try
				{
					GetImages("source");
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "CalendarRenderer: loading source images", e.Message);
					throw (e);
				}

				var settings = GenUtils.GetSettingsFromAzureTable();
				try
				{
					this.max_events = Convert.ToInt32(settings["max_html_events_default"]);  // start with service-wide setting

					if (this.default_args.ContainsKey("max_events"))			 // look for hub setting
						this.max_events = Convert.ToInt32( default_args["max_events"] );
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "CalendarRenderer: setting max events", e.Message);
					this.max_events = 1000;
					throw (e);
				}
				finally
				{
					settings = null;
				}

			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "CalenderRenderer.CalendarRenderer: " + id, e.Message + e.StackTrace);
			}

		}

		private void GetImages(string image_type)
		{
			var images_uri = BlobStorage.MakeAzureBlobUri(this.id, image_type + "_images.json");
			var r = HttpUtils.FetchUrl(images_uri);
			if (r.status != System.Net.HttpStatusCode.OK)
			{
				images_uri = BlobStorage.MakeAzureBlobUri("admin", image_type + "_images.json");
				r = HttpUtils.FetchUrl(images_uri);
			}

			if (r.status == System.Net.HttpStatusCode.OK)
			{
				var json = r.DataAsString();
				switch (image_type)
				{
					case "category":
						this.category_images = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
						break;
					case "source":
						this.source_images = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
						break;
					default:
						break;
				}
			}
		}

		#region xml

		public string RenderXml()
		{
			return RenderXml(eventstore: null, view: null, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args:null);
		}

		public string RenderXml(string view)
		{
			return RenderXml(eventstore: null, view: view, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args:null);
		}

		public string RenderXml(int count)
		{
			return RenderXml(eventstore: null, view: null, count: count, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args:null);
		}

		// render an eventstore as xml, optionally limited by view and/or count
		public string RenderXml(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, string source, Dictionary<string,object> args)
		{
			eventstore = GetEventStore(eventstore, view, count, from, to, source, args);

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

		// render an eventstore as text, optionally limited by view and/or count
		public string RenderText(ZonelessEventStore eventstore, EventRenderer event_renderer, string view, int count, DateTime from, DateTime to, string source, Dictionary<string, object> args)
		{
			if (args == null)
				args = new Dictionary<string, object>();

			eventstore = GetEventStore(eventstore, view, count, from, to, source, args);

			var text = new StringBuilder();

			var eventstring = new StringBuilder();

			foreach (var evt in eventstore.events)
				AppendEvent(eventstring, event_renderer, evt, args);

			text.Append(eventstring.ToString());

			return text.ToString();
		}

		public string RenderText(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, string source, Dictionary<string, object> args)
		{
			return RenderText(eventstore, new EventRenderer(RenderEvtAsText), view, count, from, to, source, args);
		}

		// render a single event as text
		public string RenderEvtAsText(ZonelessEvent evt, Calinfo calinfo, Dictionary<string, object> args)
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

		public string RenderCsv(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, string source, Dictionary<string, object> args)
		{
			if (args == null) args = new Dictionary<string, object>();
			args["csv_fields_template"] = @"""__uid__"",""__title__"",""__start_day__"",""__stop_day__"",""__start_time__"",""__stop_time__"",""__description__"",""__location__"",""__url__"",""__categories__""";
			var schema = args["csv_fields_template"].ToString().Replace("__", "") + "\n";
			return schema + RenderText(eventstore, new EventRenderer(RenderEvtAsCsv), view, count, from, to, source, args);
		}

		public string RenderEvtAsCsv(ZonelessEvent evt, Calinfo calinfo, Dictionary<string, object> args)
		{
			var line = (string) args["csv_fields_template"];

			var date_fmt = "yyyy-MM-dd";
			var time_fmt = "hh:mm:ss";
			line = line.Replace("__title__", evt.title.EscapeValueForCsv());
			line = line.Replace("__start_day__", evt.dtstart.ToString(date_fmt));
			line = line.Replace("__start_time__", evt.dtstart.ToString(time_fmt));
			if (evt.dtend != DateTime.MinValue)
			{
				line = line.Replace("__stop_day__", evt.dtstart.ToString(date_fmt));
				line = line.Replace("__stop_time__", evt.dtstart.ToString(time_fmt));
			}
			else
			{
				line = line.Replace("__stop_day__", "");
				line = line.Replace("__stop_time__", "");
			}
			line = line.Replace("__uid__", evt.hash);
			line = line.Replace("__description__", evt.description.EscapeValueForCsv());
			line = line.Replace("__location__", evt.location.EscapeValueForCsv());
			line = line.Replace("__url__", evt.url.ToString().EscapeValueForCsv());
			line = line.Replace("__categories__", evt.categories.EscapeValueForCsv());

			return line + "\n";
		}

		#endregion

		#region json

		public string RenderJson()
		{
			return RenderJson(eventstore: null, view: null, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, source: null, args:null);
		}

		public string RenderJson(string view)
		{
			return RenderJson(eventstore: null, view: view, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args:null);
		}

		public string RenderJson(int count)
		{
			return RenderJson(eventstore: null, view: null, count: count, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args:null);
		}

		public string RenderJson(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, string source, Dictionary<string, object> args)
		{
			eventstore = GetEventStore(eventstore, view, count, from, to, source, args);
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
			var description = MassageDescription(evt);
			return String.Format(jsonp + "('" + description + "')") ;
		}

		public string DescriptionFromUid(int uid, string jsonp)
		{
			var es = this.es_getter(this.cache);
			var evt = es.events.Find(e => e.uid == uid);
			var description = MassageDescription(evt);
			return String.Format(jsonp + "('" + description + "')");
		}

		public string DescriptionFromHash(string hash, string jsonp)
		{
			var es = this.es_getter(this.cache);
			var evt = es.events.Find(e => e.hash == hash);
			var description = MassageDescription(evt);
			return String.Format(jsonp + "('" + description + "')");
		}

		private static string MassageDescription(ZonelessEvent evt)
		{
			string description = "";
			if (!String.IsNullOrEmpty(evt.description) )
				description = evt.description.Replace("'", "\\'").Replace("\n", "<br>").Replace("\r", "");
			string location = "";
			if (!String.IsNullOrEmpty(evt.location))
				location = String.Format(@"<p class=""elmcity_info_para""><b>Location</b>: {0}</p>", evt.location.Replace("'", "\\'")).Replace("\n", " ").Replace("\r", "");
			description = ("<span class=\"desc\">" + location + @"<p class=""elmcity_info_para""><b>Description</b>: " + description + "</p></span>").UrlsToLinks();
			return description;
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
			return RenderHtml(es: null, view: null, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args:null);
		}

		public string RenderHtml(ZonelessEventStore es)
		{
			return RenderHtml(es: es, view: null, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args:null);
		}

		public string RenderHtml(ZonelessEventStore es, string view, int count, DateTime from, DateTime to, string source, Dictionary<string, object> args)
		{
			if (args == null)
				args = new Dictionary<string, object>();

			this.ResetCounters();

			args["advance_to_an_hour_ago"] = true;

			string hub = null;

			try
			{
				if (this.default_args != null && this.default_args.ContainsKey("hub"))			 // look for hub default
					hub = (string)default_args["hub"];
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "RenderHtml: no default_args?", e.Message);
			}

			if (args.HasNonEmptyOrNullStringValue("hub"))   // look for url override
				hub = (string)args["hub"];

			args["html_renderer"] = true;

			es = GetEventStore(es, view, count, from, to, source, args);

			MaybeUseAlternateTemplate(args);

			var builder = new StringBuilder();

			RenderEventsAsHtml(es, builder, args);

			if (args.HasValue("bare_events", true)) 
				return builder.ToString();

			var html = this.template_html.Replace("__EVENTS__", builder.ToString());

			if (args.HasValue("taglist",true))
			{
				if (this.calinfo.hub_enum == HubType.region && String.IsNullOrEmpty(hub) == false)
					html = this.InsertRegionTagAndHubSelectors(html, es, view, hub, eventsonly: false);
				else
					html = this.InsertTagSelector(html, es, view, eventsonly: false);
			}

			html = html.Replace("__VIEW__", view);     // propagate these to client so if changed here it can react
			html = html.Replace("__HUB__", hub);


			html = html.Replace("__APPDOMAIN__", ElmcityUtils.Configurator.appdomain);

			html = html.Replace("__ID__", this.id);

			var css_url = this.GetCssUrl(args);  // default to calinfo.css, maybe override with args["theme"]
			html = html.Replace("__CSSURL__", css_url);

			html = HandleJsUrl(html, args);

			html = HandleDefaultArgs(html);

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

			html = InsertImageJson(html);

			string json_metadata = AssembleMetadata(es);
			html = html.Replace("__METADATA__", json_metadata);

			return html;
		}

		private static string AssembleMetadata(ZonelessEventStore es)
		{
			string json_metadata = "";
			try
			{
				var m = new Dictionary<string, object>();
				m["count"] = es.events.Count;
				m["days"] = es.days;
				m["days_and_counts"] = es.days_and_counts;
				m["finalized"] = es.when_finalized;
				m["first_available_day"] = es.first_available_day;
				m["last_available_day"] = es.last_available_day;
				//m["last_cached_day"] = es.last_cached_day;
				m["rendered"] = DateTime.UtcNow;
				json_metadata = JsonConvert.SerializeObject(m);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "serializing days_and_counts", e.Message + e.StackTrace);
			}
			return json_metadata;
		}

		private string InsertImageJson(string html)
		{
			try
			{
				if (this.category_images.Count > 0)
					html = html.Replace("__CATEGORY_IMAGES__", "category_images = " + JsonConvert.SerializeObject(this.category_images) + ";\n");
				else
					html = html.Replace("__CATEGORY_IMAGES__", "");

				if (this.source_images.Count > 0)
					html = html.Replace("__SOURCE_IMAGES__", "source_images = " + JsonConvert.SerializeObject(this.source_images) + ";\n");
				else
					html = html.Replace("__SOURCE_IMAGES__", "");
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "RenderHtml: category+source images", e.Message);
			}
			return html;
		}

		private List<string> GetDayAnchors(ZonelessEventStore es, string view)
		{
			if (es == null)  // if no eventstore passed in (e.g., for testing)
				es = this.es_getter(this.cache);

			es.AdvanceToAnHourAgo(this.calinfo);

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
			string jsurl_param_value = null;

			try
			{
				if (this.default_args.ContainsKey("jsurl"))                   // look for hub default
					jsurl_param_value = (string)this.default_args["jsurl"];
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "RenderHtml: no default_args?", e.Message);
			}

			if ( args.ContainsKey("jsurl") && (string)args["jsurl"] != null )  // look for override
				jsurl_param_value = (string)args["jsurl"];


			if (jsurl_param_value != null)  // override defaults if jsurl on url-line
			{
				var jsurl = BlobStorage.MakeAzureBlobUri("admin", jsurl_param_value).ToString(); 
				this.default_js_url = jsurl;
			}

			html = html.Replace("__JSURL__", this.default_js_url);

			return html;
		}

		private string HandleDefaultArgs(string html)
		{
			if (this.default_args_json == null)
				return html.Replace("__DEFAULT_ARGS__", "");
			else
				return html.Replace("__DEFAULT_ARGS__", "default_args = " + this.default_args_json + ";\n");
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

		public string RenderHtmlEventsOnly(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, string source, Dictionary<string,object> args)
		{
			args["advance_to_an_hour_ago"] = true;

			var day_anchors = GetDayAnchorsAsJson(eventstore, view);

			eventstore = GetEventStore(eventstore, view, count, from, to, source, args);

			var builder = new StringBuilder();

			MaybeUseAlternateTemplate(args);

			RenderEventsAsHtml(eventstore, builder, args);

			var html = this.template_html.Replace("__EVENTS__", builder.ToString());

			if (args.ContainsKey("taglist") && (bool) args["taglist"] == true )
				html = this.InsertTagSelector(html, eventstore, view, eventsonly: true);

			html = html.Replace("__APPDOMAIN__", ElmcityUtils.Configurator.appdomain);

			html = html.Replace("__ID__", this.id);
			html = html.Replace("__TITLE__", MakeTitle(view));
			html = html.Replace("__META__", MakeTitle(view) + " calendars happenings schedules");

			var css_url = GetCssUrl(args);
			html = html.Replace("__CSSURL__", css_url);

			html = HandleJsUrl(html, args);

			html = html.Replace("__GENERATED__", System.DateTime.UtcNow.ToString());

			html = html.Replace("__METADATA__", day_anchors);

			html = InsertImageJson(html);

			html = Utils.RemoveCommentSection(html, "SIDEBAR");
			html = Utils.RemoveCommentSection(html, "JQUERY_UI_CSS");
			html = Utils.RemoveCommentSection(html, "JQUERY_UI");
			html = Utils.RemoveCommentSection(html, "DATEPICKER");
			html = Utils.RemoveCommentSection(html, "HUBTITLE");
			html = Utils.RemoveCommentSection(html, "TAGS");

			return html;
		}


		public string RenderHtmlForMobile(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, string source, Dictionary<string,object> args)
		{
			//GenUtils.LogMsg("info", "RenderForMobile", JsonConvert.SerializeObject(args));
			this.ResetCounters();

			eventstore = GetEventStore(eventstore, view, count, from, to, source, args);
			count = (int) args["mobile_event_count"];                        // maybe apply further reduction
			eventstore.events = eventstore.events.Take(count).ToList();

			var html = RenderHtmlEventsOnly(eventstore, view, count, from, to, source, args);

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

				if (this.default_args.ContainsKey("template"))			 // look for hub default
					template = (string)default_args["template"];

				if (args.Keys.Contains("template") && ! String.IsNullOrEmpty((string)args["template"]) )    // look for url override
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

		public string InsertTagSelector(string html, ZonelessEventStore es, string view, bool eventsonly)
		{
			var tags = new List<string>();
			var counts = new Dictionary<string,string>();

			var sb = new StringBuilder();
			sb.Append("<select class=\"tag_list\" id=\"tag_select\" onchange=\"show_view()\">\n");
			if (String.IsNullOrEmpty(view))
				view = "all";
			AddTagOptions(view, es, es.non_hub_tags, es.non_hubs_and_counts, sb, squigglies: true);
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

		private void MaybeBuildTagStructures(ZonelessEventStore es)  // transitional until new zoneless objects fully deployed
		{
			if (es.non_hub_tags == null) 
			{
				es.category_hubs = new Dictionary<string, Dictionary<string, int>>();
				es.hubs_and_counts = new Dictionary<string, int>();
				es.hub_tags = new List<string>();
				es.non_hubs_and_counts = new Dictionary<string, int>();
				es.non_hub_tags = new List<string>();
				es.hub_name_map = new Dictionary<string, List<string>>();

				Utils.BuildTagStructures(es, this.calinfo);

				var base_key = Utils.MakeBaseZonelessUrl(this.id);

				if (this.cache[base_key] != null)
				{
					var cached_es = (ZonelessEventStore)BlobStorage.DeserializeObjectFromBytes((byte[])this.cache[base_key]);
					if (cached_es.non_hub_tags == null)
					{
						var bytes = ObjectUtils.SerializeObject(es);
						var logger = new CacheItemRemovedCallback(AspNetCache.LogRemovedItemToAzure);
						var expiration_hours = ElmcityUtils.Configurator.cache_sliding_expiration.Hours;
						var sliding_expiration = new TimeSpan(expiration_hours, 0, 0);
						cache.Insert(base_key, bytes, null, Cache.NoAbsoluteExpiration, sliding_expiration, CacheItemPriority.Normal, logger);
					}
				}
			}
		}

		public string InsertRegionTagAndHubSelectors(string html, ZonelessEventStore es, string view, string hub, bool eventsonly)
		{
			if ( String.IsNullOrEmpty(view) ) 
				view = "all";
			
			if ( String.IsNullOrEmpty(hub) )
				hub = "all";

			int use_case;

			if		(view == "all" && hub == "all")	use_case = 1;
			else if (view != "all" && hub == "all")	use_case = 2;
			else if (view != "all" && hub != "all")	use_case = 3;
			else if (view == "all" && hub != "all")	use_case = 4;
			else throw new Exception("RegionAndHubSelectorsImpossible");

			var sb_tags = new StringBuilder();
			sb_tags.Append("<select class=\"tag_list\" id=\"tag_select\" onchange=\"show_view()\">\n");

			var sb_hubs = new StringBuilder();
			sb_hubs.Append("<select class=\"tag_list\" id=\"hub_select\" onchange=\"show_view()\">\n");

			switch (use_case)
			{
				case 1:
					// view: all
					// hub: all
					// tag: show all nonhub tags for region plus all       all selected
					// hub: show all hubs in region plus all              all selected
					AddTagOptions(view, es,  es.non_hub_tags, es.non_hubs_and_counts, sb_tags, false);
					AddTagOptions(hub, es, es.hub_tags, es.hubs_and_counts, sb_hubs, false);
					break;
				case 2:
					// view: music
					// hub: all
					// tag: show all nonhub tags for region plus all     music selected
					// hub: show only hubs with music events plus all    all selected       
					AddTagOptions(view, es, es.non_hub_tags, es.non_hubs_and_counts, sb_tags, false);
					var hubs_for_view = es.category_hubs[view].Keys.ToList();
					hubs_for_view.Sort();
					AddTagOptions(hub, es, hubs_for_view, es.category_hubs[view], sb_hubs, false);
					break;
				case 3:
					// view: music
					// hub: BrattleboroVT
					// tag: show all tags for BrattleboroVT plus all    music selected
					// hub: show only BrattleboroVT plus all           BrattleboroVT selected
					var es_unfiltered = this.es_getter(this.cache);                          // want all tags for hub as options even if view is filtered
					es_unfiltered.events = ViewFilter(hub.ToLower(), es_unfiltered.events);  // so take the unfiltered es and filter by hub name (as lowercase tag)
					OptionsForSelectedHub(view, hub, sb_tags, sb_hubs, es_unfiltered);
					break;
				case 4:
					// view: all
					// hub: BrattleboroVT
					// tag: show all tags for BrattleboroVT plus all     all selected
					// hub: show only BrattleboroVT plus all            BrattleboroVT selected
					OptionsForSelectedHub(view, hub, sb_tags, sb_hubs, es);
					break;
			}

			sb_tags.Append("</select>");
			sb_hubs.Append("</select>");

			if (eventsonly)
			{
				html = html.Replace("<!-- begin events -->", "<!-- begin events -->\n" + sb_tags.ToString() + sb_hubs.ToString() ); // insert tags at top of event list
			}
			else
			{
				html = html.Replace("__TAGS__", sb_tags.ToString());
				html = html.Replace("__HUBS__", sb_hubs.ToString());   
			}
			return html;
		}

		private void OptionsForSelectedHub(string view, string hub, StringBuilder sb_tags, StringBuilder sb_hubs, ZonelessEventStore es)
		{
			var tags_and_counts = Utils.MakeTagsAndCounts(hub, es, Utils.TagAndCountType.nonhub, es.hub_tags);
			var tags = tags_and_counts.Keys.Select(k => k.ToString()).ToList();
			tags.Sort();
			AddTagOptions(view, es, tags, tags_and_counts, sb_tags, false);
			var hub_as_tag = hub.ToLower();
			AddTagOptions(hub_as_tag, es, new List<string>() { hub_as_tag }, es.hubs_and_counts, sb_hubs, false);
		}

		private void AddTagOptions(string selector, ZonelessEventStore es, List<string> tags, Dictionary<string, int> counts, StringBuilder sb, bool squigglies)
		{
			var _tags = tags.CloneObject();
			_tags.Insert(0, "all");
			foreach (var tag in _tags)
			{
				try
				{
					if (tag.Contains("{") && squigglies == false)
						continue;
					var option = MakeTagOption(selector, es, counts, tag);
					sb.Append(option);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", String.Format("AddTagOptions, selector: {0}, id: {1}, tag {2}", selector, es.id, tag), e.Message);
				}
			}
		}

		private string MakeTagOption(string selector, ZonelessEventStore es, Dictionary<string, int> counts, string tag)
		{
			string maybe_replaced_tag_label = tag;              // newkentva -> New Kent
			if (es.hub_name_map.ContainsKey(tag))
				maybe_replaced_tag_label = es.hub_name_map[tag][1];  // "norfolkva"		: [ "NorfolkVa" , "Norfolk"], -> Norfolk is the readable name

			string maybe_truncated_tag_label = maybe_replaced_tag_label;
			if (maybe_truncated_tag_label.Length > Configurator.max_tag_chars)
				maybe_truncated_tag_label = maybe_truncated_tag_label.Substring(0, Configurator.max_tag_chars) + "&#8230;";

			string option_value;
			
			if ( tag == "all") 
				option_value = "all";
			else if ( es.hub_name_map.ContainsKey(tag) )
				option_value = es.hub_name_map[tag][0];                // -> NorfolkVA is the effective hub name
			else
				option_value = tag;

			var option = "<option value=\"" + option_value + "\">" + maybe_truncated_tag_label;     

			/* idle the count for now
			 * 
			if (tag != "all" && counts.ContainsKey(tag) )
				option += " (" + counts[tag] + ")";
			 */

			option += "</option>\n";
			if (tag == selector)
				option = option.Replace("<option ", "<option selected ");
			return option;
		}

		public void RenderEventsAsHtml(ZonelessEventStore es, StringBuilder builder, Dictionary<string,object> args)
		{
			if (args == null)
				args = new Dictionary<string, object>();

			args["hub_tags"] = es.hub_tags;  // so the event renderer can exclude these

			bool bare_events = args.HasValue("bare_events", true);

			//bool mobile = args.ContainsKey("mobile") && (bool)args["mobile"] == true;

			var event_renderer = new EventRenderer(RenderEvtAsHtml);
			var year_month_anchors = new List<string>(); // e.g. ym201201
			var day_anchors = new List<string>(); // e.g. d20120119
			var current_time_of_day = TimeOfDay.Initialized;
			var sources_dict = new Dictionary<string, int>();
			int sequence_position = 0;
			bool sequence_at_zero = true;
			string last_source_key = null;
			string current_date_key = null;

			var announce_time_of_day = args.HasValue("announce_time_of_day", true) && ! args.HasValue("bare_events",true);

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

				if (announce_time_of_day) 
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
				List<string> catlist = Utils.GetTagListFromTagString(evt.categories);
				if (args.ContainsKey("hub_tags") )  // exclude autogenerated hub names
				{
					var hub_tags = (List<string>)args["hub_tags"];
					catlist = catlist.FindAll(c => hub_tags.Contains(c) == false);
				}

				// catlist = catlist.FindAll(c => c.Contains("{") == false); // don't show squiggly tags inline?
                // actually leave them in so client can show related images. curator can suppress display if desired using css

				foreach (var cat in catlist)
				{
					var category_url = string.Format("javascript:show_view('{0}')", cat);
					catlist_links.Add(string.Format(@"<a title=""open the {1} view"" href=""{0}"">{1}</a>", category_url, cat));
				}
				categories = string.Format(@" <span class=""cat"">{0}</span>", string.Join(", ", catlist_links.ToArray()));
			}

			String description = ( String.IsNullOrEmpty(evt.description) || evt.description.Length < 10 ) ? "" : evt.description.UrlsToLinks();

			string dom_id = "e" + evt.uid;

			string show_desc = ( ! String.IsNullOrEmpty(description) ) ? String.Format(@"<span class=""sd""><a title=""show description"" href=""javascript:show_desc('{0}')"">...</a></span>", dom_id) : "";

			if ( args.HasValue("inline_descriptions",true) ) // for view_calendar
			{
				var location = string.IsNullOrEmpty(evt.location) ? "" : String.Format("{0}<br/><br/>", evt.location);
				show_desc = String.Format(@"<div style=""text-indent:0""><p>{0}</div>", location + description);
			}

			if ( args.HasValue("show_desc", false) ) // in case need to suppress, not used yet
				show_desc = "";

			string add_to_cal = String.Format(@"<span class=""atc""><a title=""add to calendar"" href=""javascript:add_to_cal('{0}')"">+</a></span>", dom_id);

			if ( args.HasValue("add_to_cal",false) ) // for view_calendar
				add_to_cal = "";

			string visibility = "";
			string more = "";
			string source_key = "";
			//string source_attr = "";  // not needed, 
			int sequence_count = 1;
			int sequence_position = 1;
			string show_more_call;

			if (evt.urls_and_sources.Count == 1)
			{
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
@"<div id=""{0}"" class=""bl {12}"" {13} xmlns:v=""http://rdf.data-vocabulary.org/#"" typeof=""v:Event"" >
<span style=""display:none"" class=""uid"">{15}</span>
<span style=""display:none"" class=""hash"">{16}</span>
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
			dom_id,                                                 // 0
			String.Format("{0:yyyy-MM-ddTHH:mm}", evt.dtstart),     // 1
			dtstart,                                                // 2
			evt.url,                                                // 3
			MakeTitleForRDFa(evt),                                  // 4
			evt.urls_and_sources.Keys.Count == 1 ? evt.source : "", // 5 suppress source if multiple
			categories,                                             // 6
			MakeGeoForRDFa(evt),                                    // 7
			show_desc,                                              // 8
			add_to_cal,                                             // 9
			"",														// 10 was source_attr, not needed  
			more,                                                   // 11
			source_key,                                             // 12
		    visibility,                                             // 13
            month_day,												// 14 
            evt.uid,												// 15
			evt.hash												// 16
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
			if (evt.lat != null && evt.lon != null)
				geo = string.Format(
@"<span rel=""v:location"">
    <span rel=""v:geo"">
       <span typeof=""v:Geo"">
          <span property=""v:latitude"" content=""{0}"" ></span>
          <span property=""v:longitude"" content=""{1}"" ></span>
       </span>
    </span>
  </span>",
				evt.lat,
				evt.lon
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
			return RenderIcs(eventstore: null, view: null, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args:null);
		}

		public string RenderIcs(string view)
		{
			return RenderIcs(eventstore: null, view: view, count: 0, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args:null);
		}

		public string RenderIcs(int count)
		{
			return RenderIcs(eventstore: null, view: null, count: count, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args:null);
		}

		public string RenderIcs(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, string source, Dictionary<string,object> args)
		{
			eventstore = GetEventStore(eventstore, view, count, from, to, source, args);
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
			return RenderRss(eventstore: null, view: null, count: Configurator.rss_default_items, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args: null);
		}

		public string RenderRss(int count)
		{
			return RenderRss(eventstore: null, view: null, count: count, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args: null);
		}

		public string RenderRss(string view)
		{
			return RenderRss(eventstore: null, view: view, count: Configurator.rss_default_items, from: DateTime.MinValue, to: DateTime.MinValue, source:null, args: null);
		}

		public string RenderRss(ZonelessEventStore eventstore, string view, int count, DateTime from, DateTime to, string source, Dictionary<string,object> args)
		{
			try
			{
				eventstore = GetEventStore(eventstore, view, count, from, to, source, args);
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

		private ZonelessEventStore GetEventStore(ZonelessEventStore es, string view, int count, DateTime from, DateTime to, string source, Dictionary<string,object> args)
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

			var is_html_renderer = args.HasValue("html_renderer", true);
			var bare_events = args.HasValue("bare_events", true);
			var is_view = !String.IsNullOrEmpty(view);

			if (is_html_renderer)
			{
				MaybeBuildTagStructures(es); // transitional until new generation of objects is fully established
				if (bare_events)
					count = 0;
			}

			if (args.HasValue("advance_to_an_hour_ago", true) && ! bare_events)
				es.AdvanceToAnHourAgo(this.calinfo);

			view = MaybeAddHubTagToView(view, args);

			var original_count = es.events.Count();
			es.events = Filter(view, count, from, to, source, es, args); // apply all filters

			if (is_view)                         
			{
				if (es.days_and_counts != null)  // this check needed only until new generation of objects is established
					es.PopulateDaysAndCounts();  // mainly for html renderer but could be useful to  non-html renderers as well
			}

			return es;
		}

		private static string MaybeAddHubTagToView(string view, Dictionary<string, object> args)
		{
			//if (args.ContainsKey("hub") && String.IsNullOrEmpty((string)args["hub"]) == false)
			if (args.HasNonEmptyOrNullStringValue("hub"))
			{
				var hub = (string)args["hub"];
				hub = hub.ToLower();
				if (String.IsNullOrEmpty(view) && !String.IsNullOrEmpty(hub) && hub != "all")         // hub, no view
					view = hub;
				else if (!String.IsNullOrEmpty(view) && !String.IsNullOrEmpty(hub) && hub != "all")  // hub plus view
					view = view + "," + hub.ToLower();
			}
			return view;
		}

		private ZonelessEventStore GetEventStore(ZonelessEventStore es, string view, Dictionary<string, object> args)
		{
			return GetEventStore(es, view, 0, DateTime.MinValue, DateTime.MinValue, null, args);
		}

		private ZonelessEventStore GetEventStore(ZonelessEventStore es, int count, Dictionary<string,object> args)
		{
			return GetEventStore(es, null, count, DateTime.MinValue, DateTime.MinValue, null, args);
		}

		private ZonelessEventStore GetEventStore(ZonelessEventStore es, DateTime from, DateTime to, Dictionary<string, object> args)
		{
			return GetEventStore(es, null, 0, from, to, null, args);
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
		public List<ZonelessEvent> Filter(string view, int count, DateTime from, DateTime to, string source, ZonelessEventStore es, Dictionary<string,object> args)
		{
			var events = es.events.CloneObject();

			var bare_events = args.HasValue("bare_events", true);
			var is_html_renderer = args.HasValue("html_renderer", true);

			if (!String.IsNullOrEmpty(source))
				events = SourceFilter(source, events);  

			if (!String.IsNullOrEmpty(view))
				events = ViewFilter(view, events);

			if (from != DateTime.MinValue && to != DateTime.MinValue)
				events = TimeFilter(from, to, events);

			if (! bare_events)									// bracket the available range before (maybe) reducing to count
				es.RememberFirstAndLastAvailableDays(events);   // not required for non-html renderers but no reason to exclude them    
                                                 
			if (count != 0)                                      // includes case where bare_events is true
				events = CountFilter(count, events);

			if ( ! is_html_renderer )   // do nothing else for non-html views
				return events;
			else                                   // post-process result set to yield ~500 day-aligned events
			{
				var from_to = new Dictionary<string, DateTime>();

				if (bare_events)
					from_to = HandleBareEvents(from);
				else
					from_to = HandleClothedEvents(events);

				if (from_to == null)
					return events;
				else
					return TimeFilter((DateTime)from_to["from_date"], (DateTime)from_to["to_date"], events);
			}
		}

		private Dictionary<string,DateTime> HandleBareEvents(DateTime from)
		{
			int days = 1;
			return Utils.ConvertDaysIntoFromTo(from, days, this.calinfo);
		}

		private Dictionary<string, DateTime> HandleClothedEvents(List<ZonelessEvent> events)
		{
			if (events.Count == 0)
				return null;
			int days = ZonelessEventStore.CountDays(events, this.max_events);
			var from = events.First().dtstart;
			return Utils.ConvertDaysIntoFromTo(from, days + 1, this.calinfo);
		}

		private static List<ZonelessEvent> ViewFilter(string view, List<ZonelessEvent> events)
		{
			var filtered_events = events.CloneObject();
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
						filtered_events = filtered_events.FindAll(evt => evt.categories != null && !evt.categories.Split(',').ToList().Contains(item));
					}
					else
						remainder.Add(view_item);
				}

				foreach (var view_item in remainder)    // then inclusions
				{
					filtered_events = filtered_events.FindAll(evt => evt.categories != null && evt.categories.Split(',').ToList().Contains(view_item));
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ViewFilter", e.Message + e.StackTrace);
			}

			return filtered_events;
		}

		private static List<ZonelessEvent> CountFilter(int count, List<ZonelessEvent> events)
		{
			var filtered_events = new List<ZonelessEvent>();
			try
			{
				filtered_events = events.Take(count).ToList();
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ViewFilter", e.Message + e.StackTrace);
			}

			return filtered_events;
		}

		private static List<ZonelessEvent> TimeFilter(DateTime from, DateTime to, List<ZonelessEvent> events)
		{
			var filtered_events = new List<ZonelessEvent>();
			try
			{
				filtered_events = events.FindAll(evt => evt.dtstart >= from && evt.dtstart < to);  // reduce to time window
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ViewFilter", e.Message + e.StackTrace);
			}

			return filtered_events;
		}

		private static List<ZonelessEvent> SourceFilter(string source, List<ZonelessEvent> events)
		{
			var filtered_events = new List<ZonelessEvent>();
			try
			{
				filtered_events = events.FindAll(evt => evt.source == source); 
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "SourceFilter", e.Message + e.StackTrace);
			}

			return filtered_events;
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
		public string RenderDynamicViewWithCaching(ControllerContext context, string view_key, ViewRenderer view_renderer, string view, int count, DateTime from, DateTime to, string source, Dictionary<string,object> args)
		{
			try
			{
				var view_is_cached = this.cache[view_key] != null;
				byte[] view_data;
				byte[] response_body;
				if (view_is_cached)
					view_data = (byte[])cache[view_key];
				else
					view_data = Encoding.UTF8.GetBytes(view_renderer(eventstore: null, view: view, count: count, from: from, to: to, source:source, args: args));

				response_body = CacheUtils.MaybeSuppressResponseBodyForView(context, view_data);
				return Encoding.UTF8.GetString(response_body);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "RenderDynamicViewWithCaching: " + view_key, e.Message + e.StackTrace);
				return RenderDynamicViewWithoutCaching(context, view_renderer, view, count, from, to, source, args: args);
			}
		}

		public string RenderDynamicViewWithoutCaching(ControllerContext context, ViewRenderer view_renderer, String view, int count, DateTime from, DateTime to, string source, Dictionary<string,object> args)
		{
			ZonelessEventStore es = GetEventStoreWithCaching(this.cache);
			return view_renderer(es, view, count, from, to, source, args);
		}

		private void ResetCounters()
		{
			this.event_counter = 0;
			//this.day_counter = 0;
			this.time_of_day_counter = 0;
		}

	}

}
