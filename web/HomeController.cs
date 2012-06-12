﻿/* ********************************************************************************
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
using System.Text;
using System.Timers;
using System.Web;
using System.Web.Mvc;
using System.Linq;
using System.Text.RegularExpressions;
using CalendarAggregator;
using ElmcityUtils;
using Newtonsoft.Json;
using System.Threading;
using System.Net;

namespace WebRole
{
	public class HomeController : ElmcityController
	{
		BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
		TableStorage ts = TableStorage.MakeDefaultTableStorage();

		public HomeController()
		{
			ElmcityApp.home_controller = this;
		}

		//[OutputCache(Duration = CalendarAggregator.Configurator.homepage_output_cache_duration_seconds, VaryByParam = "None")]
		// cannot because this is the authentication portal, needs to react immediately to login
		public ActionResult index()
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			var template_uri = BlobStorage.MakeAzureBlobUri("admin", "home2.tmpl", true);
			var page = HttpUtils.FetchUrl(template_uri).DataAsString();

			var auth_uri = BlobStorage.MakeAzureBlobUri("admin", "home_auth.tmpl", true);
			var noauth_uri = BlobStorage.MakeAzureBlobUri("admin", "home_noauth.tmpl", true);
			var featured_uri = BlobStorage.MakeAzureBlobUri("admin", "featured.html", true);

			var signin = HttpUtils.FetchUrlNoCache (auth_uri).DataAsString();

			var authenticated = false;

			foreach (var auth in Authentications.AuthenticationList)
			{
				var foreign_id = auth.AuthenticatedVia(this.Request);
				if (foreign_id != null)
				{
					authenticated = true;
					var elmcity_ids = auth.AuthenticatedElmcityIds(foreign_id);
					signin = MakeSigninWidget(signin, auth.mode.ToString(), foreign_id, elmcity_ids);
				}
			}

			if ( authenticated == false )
			{
				var noauth = HttpUtils.FetchUrl(noauth_uri).DataAsString();
				signin = noauth;
			}

			var featured = HttpUtils.FetchUrlNoCache(featured_uri).DataAsString();
			page = page.Replace("__FEATURED__", featured);

			page = page.Replace("__AUTHENTICATE__", signin);

			page = page.Replace("__VERSION__", ElmcityApp.version);

			return Content(page, "text/html");
		}

		private string MakeSigninWidget(string signin, string mode, string foreign_id, List<string> elmcity_ids)
		{
			try
			{
				string elmcity_id = elmcity_ids[0]; // if > 1 use first by default, others manual for now
				signin = signin.Replace("__AUTH_MODE__", mode);
				signin = signin.Replace("__AUTH_ID__", foreign_id);
				signin = signin.Replace("__ELMCITYID__", elmcity_id);
				return signin;
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "MakeSigninWidget", e.Message + e.StackTrace);
				return null;
			}
		}

		public ActionResult add_to_cal(string elmcity_id, string flavor, string start, string end, string summary, string description, string url, string location)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			end = end ?? "";
			url = url ?? "";
			if (!String.IsNullOrEmpty(url))
			{
				try		{	var uri = new Uri(url);	}
				catch 	{	url = "";				}
			}
			location = location ?? "";
			switch (flavor)
			{
				case "ical":
					var result = CalendarRenderer.RenderEventAsIcs(elmcity_id, summary, start, end, description, location, url);
					return Content(result, "text/calendar");
				case "google":
					Utils.PrepForAddToCalRedirect(ref description, location, url, ref start, ref end, elmcity_id, false);
					var google_redirect = String.Format("https://www.google.com/calendar/render?action=TEMPLATE&text={0}&dates={1}/{2}&details={3}&location=", summary, start, end, description, location);
					return new RedirectResult(google_redirect);
				case "hotmail":
					Utils.PrepForAddToCalRedirect(ref description, location, url, ref start, ref end, elmcity_id, false);
					var hotmail_redirect = String.Format("https://bay04.calendar.live.com/calendar/calendar.aspx?rru=addevent&summary={0}&dtstart={1}&dtend={2}&description={3}&location={4}", summary, start, end, description,location); 
					return new RedirectResult(hotmail_redirect);
				case "facebook":
					Utils.PrepForAddToCalRedirect(ref description, location, url, ref start, ref end, elmcity_id, true);
					var facebook_redirect = String.Format("http://elmcity.cloudapp.net/add_fb_event?name={0}&start_time={1}&end_time{2}&description={3}&location={4}", summary, start, end, description,location); 
					return new RedirectResult(facebook_redirect);
				default:
					result = "unexpected type: " + flavor;
					return Content(result);
			}
		}

		private static string MakeAddToCalDateTime(string elmcity_id, string start)
		{
			var dt = DateTime.Parse(start);
			var calinfo = Utils.AcquireCalinfo(elmcity_id);
			var utc = Utils.DtWithZoneFromDtAndTzinfo(dt, calinfo.tzinfo).UniversalTime;
			return utc.ToString("yyyyMMddThhmm00Z");
		}

		private static string MakeAddToCalDescription(string description, string url, string location)
		{
			if (!String.IsNullOrEmpty(url))
				description = "Url: " + url + "\n" + description;
			if (!String.IsNullOrEmpty(location))
				description = "Location: " + location + "\n" + description;
			return description;
		}

		public ActionResult description_from_title_and_dtstart(string id, string title, string dtstart, string jsonp)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var description = "";
			try
			{
				var cr = ElmcityApp.wrd.renderers[id];
				description = cr.DescriptionFromTitleAndDtstart(title, dtstart, jsonp);
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "description_from_title_and_dtstart", e.Message + e.StackTrace);
			}
			return Content(description, "application/json");
		}

		public ActionResult feed2json(string id, string source, string jsonp)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			string result;
			try
			{
				var renderer = Utils.AcquireRenderer(id);
				result = renderer.RenderFeedAsJson(source);
				if (jsonp != null)
					result = jsonp + "(" + result + ")";
				return Content(result, "application/json");
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "feed2json", e.Message + e.StackTrace);
				result = string.Format(
@"sorry, cannot export {0} as json. 
are you sure it is the name of an active elmcity feed for the {1} hub?
if unsure please check http://elmcity.cloudapp.net/{1}/stats",
				source,
				id
				);
				return Content(result, "text/plain");
			}
		}

		public ActionResult get_high_school_sports_url(string school, string tz)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var result = Utils.get_high_school_sports_ical_url(school, tz);
			return Content(result);
		}

		public ActionResult get_fb_ical_url(string fb_page_url, string elmcity_id)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var regex = settings["fb_page_id_regex"];
			var result = Utils.get_fb_ical_url(fb_page_url, elmcity_id, regex);
			return Content(result);
		}

		public ActionResult get_csv_ical_url(string feed_url, string home_url, string skip_first_row, string title_col, string date_col, string time_col, string tzname)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var result = Utils.get_csv_ical_url(feed_url, home_url, skip_first_row, title_col, date_col, time_col, tzname);
			return Content(result);
		}

		public ActionResult get_ics_to_ics_ical_url(string feedurl, string elmcity_id, string source, string after, string before, string include_keyword, string exclude_keyword, string summary_only, string url_only, string location_only)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var result = Utils.get_ics_to_ics_ical_url(feedurl, elmcity_id, source, after, before, include_keyword, exclude_keyword, summary_only, url_only, location_only);
			return Content(result);
		}

		public ActionResult get_ical_url_from_eventbrite_event_page(string url, string elmcity_id)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var result = Utils.get_ical_url_from_eventbrite_event_page(url, elmcity_id);
			return Content(result);
		}

		public ActionResult get_ical_url_from_eid_of_eventbrite_event_page(string url, string tzname)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var result = Utils.get_ical_url_from_eid_of_eventbrite_event_page(url, tzname);
			return Content(result);
		}

		public ActionResult get_rss_xcal_ical_url(string feedurl, string tzname)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var result = Utils.get_rss_xcal_ical_url(feedurl, tzname);
			return Content(result);
		}

		public ActionResult DiscardMisfoldedDescriptionsAndBogusCategoriesThenAddEasternVTIMEZONE(string url)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var result = Utils.DiscardMisfoldedDescriptionsAndBogusCategoriesThenAddEasternVTIMEZONE(new Uri(url));
			return Content(result);
		}
		
//		[OutputCache(Duration = CalendarAggregator.Configurator.homepage_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult hubfiles(string id)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			string template = HttpUtils.FetchUrl(new Uri("http://elmcity.blob.core.windows.net/admin/hubfiles.tmpl")).DataAsString();
			var page = template.Replace("__ID__", id);
			page = page.Replace("__LOWERID__", id.ToLower());
			return Content(page, "text/html");
		}

		public ActionResult snapshot()
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			return Content(ElmcityUtils.Counters.DisplaySnapshotAsText(), "text/plain");
		}

		public ActionResult table_query(string table, string query, string attrs)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			var list_of_attr = attrs.Split(',').ToList();
			var ts = TableStorage.MakeDefaultTableStorage();
			var page = ts.QueryEntitiesAsHtml(table, query, list_of_attr);
			return Content(page, "text/html");
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.tag_cloud_cache_duration_seconds, VaryByParam="*")]
		public ActionResult tag_cloud(string id)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var url = BlobStorage.MakeAzureBlobUri(id, "tags.json",true);
			var page = HttpUtils.FetchUrl(url).DataAsString();
			return Content(page, "application/json");
		}

		public enum UrlHelper { get_fb_ical_url, get_high_school_sports_url, get_csv_ical_url };

		public ActionResult url_helpers()
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			var uri = BlobStorage.MakeAzureBlobUri("admin", "url_helpers.html",false);
			var page = HttpUtils.FetchUrlNoCache(uri).DataAsString();
			var content_type = "text/html";

			return Content(page, content_type);
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult ics_from_fb_page(string fb_id, string elmcity_id)
		{
			this.HttpContext.Server.ScriptTimeout = CalendarAggregator.Configurator.webrole_script_timeout_seconds;
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			string ics = "";
			try
			{
				ics = Utils.IcsFromFbPage(fb_id, elmcity_id, ElmcityController.settings);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ics_from_fb_page: " + fb_id + ", " + elmcity_id, e.Message + e.StackTrace);
			}
			return Content(ics, "text/calendar");
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult ics_from_eventbrite_organizer(string organizer, string elmcity_id)
		{
			this.HttpContext.Server.ScriptTimeout = CalendarAggregator.Configurator.webrole_script_timeout_seconds;
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			string ics = "";
			try
			{
				ics = Utils.IcsFromEventBriteOrganizerByName(organizer, elmcity_id, ElmcityController.settings);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ics_from_eventbrite_organizer: " + organizer + ", " + elmcity_id, e.Message + e.StackTrace);
			}
			return Content(ics, "text/calendar");
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult ics_from_eventbrite_organizer_id(string organizer_id, string elmcity_id)
		{
			this.HttpContext.Server.ScriptTimeout = CalendarAggregator.Configurator.webrole_script_timeout_seconds;
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			string ics = "";
			try
			{
				ics = Utils.IcsFromEventBriteOrganizerById(organizer_id, elmcity_id, ElmcityController.settings);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ics_from_eventbrite_organizer_id: " + organizer_id + ", " + elmcity_id, e.Message + e.StackTrace);
			}
			return Content(ics, "text/calendar");
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult ics_from_eventbrite_eid(string eid, string tzname)
		{
			this.HttpContext.Server.ScriptTimeout = CalendarAggregator.Configurator.webrole_script_timeout_seconds;
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			string ics = "";
			try
			{
				ics = Utils.IcsFromEventBriteEid(eid, tzname, ElmcityController.settings);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ics_from_eventbrite_eid: " + eid + ", " + tzname, e.Message + e.StackTrace);
			}
			return Content(ics, "text/calendar");
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult ics_from_xcal(string url, string source, string tzname)
		{
			this.HttpContext.Server.ScriptTimeout = CalendarAggregator.Configurator.webrole_script_timeout_seconds;
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var tzinfo = Utils.TzinfoFromName(tzname);
			string ics = "";
			try
			{
				ics = Utils.IcsFromRssPlusXcal(url, source, tzinfo);
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "IcsFromRssPlusXcal", e.Message + e.StackTrace);
			}
			return Content(ics, "text/calendar");
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult ics_from_vcal(string url, string source, string tzname)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			string ics = "";
			try
			{
				var tzinfo = Utils.TzinfoFromName(tzname);
				ics = Utils.IcsFromAtomPlusVCalAsContent(url, source, tzinfo);
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "IcsFromVcal", e.Message + e.StackTrace);
			}
			return Content(ics, "text/calendar");
		}

		public ActionResult soon(string id, string type, string view, string count, string hours, string days)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			int nearest_minute = 15;
			if ( view == null ) view = "";
			if (count == null) count = "0";
			int d = 0;
			int h = 0;
			var msg = "please use a digit for hours and/or days";
			if (hours == null && days == null)
				return Content(msg, "text/plain");
			try
			{
				h = Convert.ToInt32(hours);
				d = Convert.ToInt32(days);
			}
			catch 
			{
				return Content(msg, "text/plain");
			}
			var timespan = TimeSpan.FromDays(d) + TimeSpan.FromHours(h);
			var calinfo = Utils.AcquireCalinfo(id);
			var now_in_tz = Utils.DateTimeSecsToZero(Utils.NowInTz(calinfo.tzinfo).LocalTime);
			now_in_tz = now_in_tz - TimeSpan.FromHours(1); // catch events that started less than an hour ago
			now_in_tz = Utils.RoundDateTimeUpToNearest(now_in_tz, nearest_minute);
			var then_in_tz = Utils.DateTimeSecsToZero(now_in_tz + TimeSpan.FromDays(d) + TimeSpan.FromHours(h));
			then_in_tz = Utils.RoundDateTimeUpToNearest(then_in_tz, nearest_minute);
			var fmt = "{0:yyyy-MM-ddTHH:mm}";
			var str_now = string.Format(fmt, now_in_tz);
			var str_then = string.Format(fmt, then_in_tz);
			var url  = string.Format("/{0}/{1}?view={2}&count={3}&from={4}&to={5}", 
				id,
				type,
				view,
				count,
				str_now,
				str_then);

			return new RedirectResult(url);
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult ics_from_csv(string feed_url, string home_url, string source, string skip_first_row, string title_col, string date_col, string time_col, string tzname)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			string ics = "";	
			try
			{

				bool skip_first = skip_first_row.ToLower() == "yes";
				int title = Convert.ToInt32(title_col);
				int date = Convert.ToInt32(date_col);
				int time = Convert.ToInt32(time_col);
				ics = Utils.IcsFromCsv(feed_url, home_url, source, skip_first, title, date, time, tzname);
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "IcsFromCsv", e.Message + e.StackTrace);
			}
			return Content(ics, "text/calendar");
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult ics_from_ics(string feedurl, string elmcity_id, string source, string after, string before, string include_keyword, string exclude_keyword, string summary_only, string url_only, string location_only)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			string ics = "";
			bool t_summary_only = Utils.UrlParameterIsTrue(summary_only);
			bool t_url_only = Utils.UrlParameterIsTrue(url_only);
			bool t_location_only = Utils.UrlParameterIsTrue(location_only);
			try
			{

				var calinfo = Utils.AcquireCalinfo(elmcity_id);
				ics = Utils.IcsFromIcs(feedurl, calinfo, source, after, before, include_keyword, exclude_keyword, t_summary_only, t_url_only, t_location_only);
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "IcsFromIcs", e.Message + e.StackTrace);
			}
			return Content(ics, "text/calendar");
		}

		public ActionResult py(string arg1, string arg2, string arg3)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			string result = "";
			if (this.AuthenticateAsSelf())
			{
				var script_url = "http://elmcity.blob.core.windows.net/admin/_generic.py";
				var args = new List<string>() { arg1, arg2, arg3 };
				result = PythonUtils.RunIronPython(WebRole.local_storage_path, script_url, args);
			}
			else
			{
				result = "not authenticated";
			}
			return Content(result, "text/plain");
		}

		public ActionResult maybe_purge_cache()
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			if (this.AuthenticateAsSelf())
			{
				var cache = new ElmcityUtils.AspNetCache(this.ControllerContext.HttpContext.Cache);
				ElmcityUtils.CacheUtils.MaybePurgeCache(cache);
			}
			return Content("OK", "text/plain");
		}

		public ActionResult reload()
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			if (!this.AuthenticateAsSelf())
				return new EmptyResult();

			ElapsedEventArgs e = null;
			object o = null;
			try
			{
				ElmcityApp.ReloadSettingsAndRoutes(o, e);
				ElmcityApp.logger.LogMsg("info", "HomeController reload", null);
			}
			catch (Exception ex)
			{
				GenUtils.PriorityLogMsg("exception", "HomeController reload", ex.Message + ex.StackTrace);
			}
			return Content("OK", "text/plain");
		}

		#region hub/feed editor

		public ActionResult meta_history(string a_name, string b_name, string id, string flavor)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var page = CalendarAggregator.Utils.GetMetaHistory(a_name, b_name, id, flavor);
			return Content(page, "text/html");
		}

		public ActionResult put_json_metadata(string id, string json)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			GenUtils.LogMsg("info", "put_json_metadata", "id: " + id + " json: " + json);

			var auth_mode = this.Authenticated(id);
			string result = "";

			if ( auth_mode != null  )
			{
				result = json;
				var args = new Dictionary<string,string>() { {"id",id}, {"json",json} };
				ThreadPool.QueueUserWorkItem(new WaitCallback(put_json_metadata_handler), args);
			}
			else
			{
				result = AuthFailMessage(id);
			}

			return Content(result, "text/plain");
		}

		private void put_json_metadata_handler(Object args)
		{
			var dict = (Dictionary<string, string>)args;
			try
			{
				var id = dict["id"];
				var json = dict["json"];
				Metadata.UpdateMetadataForId(id, json);
				//bs.DeleteBlob(id, "metadata.html");
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "put_json_metadata_handler", e.Message + e.StackTrace);
				throw (e);
			}
		}

		[HttpPost, ValidateInput(false)]
		public ActionResult put_json_feeds(string id, string json)
		{
			GenUtils.LogMsg("info", "put_json_feeds", "id: " + id + " json: " + json);
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			var auth_mode = this.Authenticated(id);
			string result = "";

			if (auth_mode != null)
			{
				result = json;
				var args = new Dictionary<string, string>() { { "id", id }, { "json", json } };
				ThreadPool.QueueUserWorkItem(new WaitCallback(put_json_feeds_handler), args);
			}
			else
			{
				result = AuthFailMessage(id);
			}

			return Content(result, "text/plain");
		}

		private void put_json_feeds_handler(Object args)
		{
			var dict = (Dictionary<string, string>)args;
			try
			{
				var id = dict["id"];
				var json = dict["json"];
				Metadata.UpdateFeedsForId(id, json);
				//bs.DeleteBlob(id, "metadata.html"); 
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "put_json_feeds_handler", e.Message + e.StackTrace);
				throw (e);
			}
		}

		public ActionResult get_json_metadata(string id, string flavor)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var name = string.Format("{0}.{1}.json", id, flavor);
			var uri = BlobStorage.MakeAzureBlobUri(id, name, false);
			return Content(HttpUtils.FetchUrl(uri).DataAsString(), "text/json");
		}

		public ActionResult get_editable_metadata(string id, string flavor)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			var auth_mode = this.Authenticated(id);
			string result = "";

			if ( auth_mode != null )
			{
				var uri = BlobStorage.MakeAzureBlobUri("admin", "editable_metadata.html", false);
				var page = HttpUtils.FetchUrl(uri).DataAsString();
				page = page.Replace("__ID__", id);
				page = page.Replace("__FLAVOR__", flavor);
				result = page;
			}
			else
			{
				result = AuthFailMessage(id);
			}
			return Content(result, "text/html");
		}

		private string AuthFailMessage(string id)
		{
			return String.Format(settings["auth_fail_message"],
				id, settings["elmcity_admin"]);
		}

		#endregion

		#region authentication

		public ActionResult twitter_auth(string method, string url, string post_data, string oauth_token)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var oauth_twitter = new OAuthTwitter(consumer_key: settings["twitter_auth_consumer_key"], consumer_secret: settings["twitter_auth_consumer_secret"]);

			var auth = Authentications.AuthenticationList.Find(x => x.mode == Authentication.Mode.twitter);

			if (Request.Cookies[auth.cookie_name.ToString()] == null)
			{
				var cookie = new HttpCookie(auth.cookie_name.ToString(), DateTime.UtcNow.Ticks.ToString());
				Response.SetCookie(cookie);
			}

			if (oauth_token == null)
			{
				var link = oauth_twitter.AuthorizationLinkGet();
				return new RedirectResult(link);
			}

			if (oauth_token != null)
			{
				var session_id = Request.Cookies[auth.cookie_name.ToString()].Value;
				oauth_twitter.token = oauth_token;
				string response = oauth_twitter.oAuthWebRequest(OAuthTwitter.Method.GET, OAuthTwitter.ACCESS_TOKEN, String.Empty);
				if (response.Length > 0)
				{
					System.Collections.Specialized.NameValueCollection qs = HttpUtility.ParseQueryString(response);
					var user_id = qs[auth.trusted_field.ToString()];
					Authentication.RememberUser(Request.UserHostAddress, Request.UserHostName, session_id, auth.mode.ToString(), auth.trusted_field.ToString(), user_id);
				}
			}

			return new RedirectResult("/");
		}

		public ActionResult facebook_auth(string code)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var auth = Authentications.AuthenticationList.Find(x => x.mode == Authentication.Mode.facebook);

			var facebook_app_id = settings["facebook_app_id"];
			var facebook_app_secret = settings["facebook_app_secret"];
			var facebook_redirect_uri = settings["facebook_redirect_uri"];

			if (code == null)
			{

				var redirect = String.Format("https://www.facebook.com/dialog/oauth?client_id={0}&redirect_uri={1}",
					facebook_app_id,
					facebook_redirect_uri);
				return new RedirectResult(redirect);
			}

			GenUtils.LogMsg("info", "facebook_auth: code (" + code + ")", null);

			if (Request.Cookies[auth.cookie_name.ToString()] == null)
			{
				var cookie = new HttpCookie(auth.cookie_name.ToString(), DateTime.UtcNow.Ticks.ToString());
				Response.SetCookie(cookie);
			}

			string access_token = GetFacebookAccessToken(code, facebook_app_id, facebook_redirect_uri, facebook_app_secret);

			var url = "https://graph.facebook.com/me?access_token=" + Uri.EscapeUriString(access_token);
			var r = HttpUtils.FetchUrl(new Uri(url));

			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
			var dict = (Dictionary<string, object>)serializer.DeserializeObject(r.DataAsString());

			if (dict != null)
			{
				var session_id = Request.Cookies[auth.cookie_name.ToString()].Value;
				var id_or_name = dict.ContainsKey("username") ? (string)dict["username"] : (string)dict["id"];
				Authentication.RememberUser(Request.UserHostAddress, Request.UserHostName, session_id, auth.mode.ToString(), auth.trusted_field.ToString(), id_or_name);
			}

			return new RedirectResult("/");

		}

		private static string GetFacebookAccessToken(string code, string id, string redirect_uri, string secret)
		{
			var url = string.Format("https://graph.facebook.com/oauth/access_token?client_id={0}&redirect_uri={1}&client_secret={2}&code={3}",
				id, 
				redirect_uri,
				secret,
				code
				);
			GenUtils.LogMsg("info", "GetFacebookAccessToken", url);
			var r = HttpUtils.FetchUrl(new Uri(url));
			GenUtils.LogMsg("info", "GetFacebookAccessToken", r.DataAsString());
			System.Collections.Specialized.NameValueCollection qs = HttpUtility.ParseQueryString(r.DataAsString());
			var access_token = qs["access_token"];
			return access_token;
		}

		public ActionResult live_auth(string code)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			var auth = Authentications.AuthenticationList.Find(x => x.mode == Authentication.Mode.live);

			var live_client_id = settings["live_client_id"];
			var live_client_secret = settings["live_client_secret"];
			var redirect_uri = settings["live_redirect_uri"];

			if (code == null)
			{
				var redirect = String.Format("https://oauth.live.com/authorize?client_id={0}&scope=wl.emails%20wl.signin&response_type=code&redirect_uri={1}",
					live_client_id,
					redirect_uri);
				return new RedirectResult(redirect);
			}

			if (Request.Cookies[auth.cookie_name.ToString()] == null)
			{
				var cookie = new HttpCookie(auth.cookie_name.ToString(), DateTime.UtcNow.Ticks.ToString());
				Response.SetCookie(cookie);
			}

			//https://oauth.live.com/token?client_id=CLIENT_ID&redirect_uri=REDIRECT_URL&client_secret=CLIENT_SECRET&code=AUTHORIZATION_CODE&grant_type=authorization_code

			var url= string.Format("https://oauth.live.com/token?client_id={0}&redirect_uri={1}&client_secret={2}&code={3}&grant_type=authorization_code",
				live_client_id,
				redirect_uri,
				live_client_secret,
				code
				);

			var r = HttpUtils.FetchUrl(new Uri(url));

			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
			var dict = (Dictionary<string, object>)serializer.DeserializeObject(r.DataAsString());
			var access_token = (string) dict["access_token"];

			var me_uri = new Uri("https://apis.live.net/v5.0/me?access_token=" + access_token);
			r = HttpUtils.FetchUrl(me_uri);
			dict = (Dictionary<string, object>)serializer.DeserializeObject(r.DataAsString());

			var emails = (Dictionary<string, object>)dict["emails"];
			var email = (string) emails["preferred"];

			if (email != null)
			{
				var session_id = Request.Cookies[auth.cookie_name.ToString()].Value;
				ElmcityUtils.Authentication.RememberUser(Request.UserHostAddress, Request.UserHostName, session_id, auth.mode.ToString(), auth.trusted_field.ToString(), email);
			}

			return new RedirectResult("/");

		}

		public ActionResult google_auth(string code)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			var auth = Authentications.AuthenticationList.Find(x => x.mode == Authentication.Mode.google);

			var google_client_id = settings["google_client_id"];
			var google_client_secret = settings["google_client_secret"];
			var redirect_uri = settings["google_redirect_uri"];

			if (code == null)
			{
				var redirect = String.Format("https://accounts.google.com/o/oauth2/auth?client_id={0}&scope={1}&response_type=code&redirect_uri={2}",
					google_client_id,
					Uri.EscapeUriString("https://www.google.com/calendar/feeds/"),
					redirect_uri);
				return new RedirectResult(redirect);
			}

			if (Request.Cookies[auth.cookie_name.ToString()] == null)
			{
				var cookie = new HttpCookie(auth.cookie_name.ToString(), DateTime.UtcNow.Ticks.ToString());
				Response.SetCookie(cookie);
			}

			var request = (HttpWebRequest)WebRequest.Create("https://accounts.google.com/o/oauth2/token");
			request.Method = "POST";
			request.ContentType = "application/x-www-form-urlencoded";
			var post_data = string.Format("code={0}&client_id={1}&client_secret={2}&redirect_uri={3}&grant_type=authorization_code",
				Uri.EscapeUriString(code), 
				Uri.EscapeUriString(google_client_id), 
				Uri.EscapeUriString(google_client_secret), 
				Uri.EscapeUriString(redirect_uri)
				);
			var r = HttpUtils.DoHttpWebRequest(request, Encoding.UTF8.GetBytes(post_data));

			string access_token = null;

			try
			{
				var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
				var dict = (Dictionary<string, object>)serializer.DeserializeObject(r.DataAsString());
				access_token = (string)dict["access_token"];
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "google_auth", e.Message + ", " + r.DataAsString());
			}

			var owncalendars = new Uri("https://www.google.com/calendar/feeds/default/owncalendars/full?access_token=" + Uri.EscapeUriString(access_token));
			r = HttpUtils.FetchUrl(owncalendars);

			string email = null;
			var s = r.DataAsString();
			var addrs = GenUtils.RegexFindGroups(s, "<author><name>[^>]+</name><email>([^>]+)</email></author>");
			email = addrs[1];

			if (email != null)
			{
				var session_id = Request.Cookies[auth.cookie_name.ToString()].Value;
				Authentication.RememberUser(Request.UserHostAddress, Request.UserHostName, session_id, auth.mode.ToString(), auth.trusted_field.ToString(), email);
			}

			return new RedirectResult("/");

		}

		public ActionResult add_fb_event(string code, string name, string description, string location, string start_time, string state)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			name = name ?? "";
			description = description ?? "";
			location = location ?? "";
			start_time = start_time ?? "";
			var settings = GenUtils.GetSettingsFromAzureTable();
			var app_id = settings["facebook_event_adder_id"];
			var app_secret = settings["facebook_event_adder_secret"];
			var redirect_uri = settings["facebook_event_adder_redirect_uri"];

			if (code == null)
			{
				state = string.Format("name={0}&description={1}&location={2}&start_time={3}",
					name,
					description,
					location,
					start_time
					);
				state = Uri.EscapeDataString(state);
				var redirect = String.Format("https://www.facebook.com/dialog/oauth?client_id={0}&redirect_uri={1}&scope=&state={2}",
					app_id,
					redirect_uri,
					state);
				return new RedirectResult(redirect);
			}
			else
			{
				try
				{
					var access_token = GetFacebookAccessToken(code, app_id, redirect_uri, app_secret);
					System.Collections.Specialized.NameValueCollection st = HttpUtility.ParseQueryString(state);
					var _name = st["name"] ?? "";
					var _description = st["description"] ?? "";
					var _location = st["location"] ?? "";
					var _start_time = st["start_time"] ?? "";
					_add_fb_event(access_token, _name, _description, _location, _start_time);
				}
				catch (Exception e)
				{
					var result = new ContentResult();
					GenUtils.PriorityLogMsg("exception", "add_fb_event", e.Message + e.StackTrace);
					result.Content = "Sorry, unable to add event to Facebook. The error was: " + e.Message.ToString();
					result.ContentType = "text/html";
				}
			}

			return new RedirectResult("https://www.facebook.com/events");
		}

		private void _add_fb_event(string access_token, string name, string description, string location, string start_time)
		{
			string host = "https://graph.facebook.com";
			var url = string.Format("{0}/me/events?access_token={1}", host, access_token);
			var data = string.Format("name={0}&description={1}&location={2}&start_time={3}",
				name,
				description,
				location,
				start_time
				);
			GenUtils.LogMsg("info", url, data);
			var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
			request.Method = "POST";
			var response = HttpUtils.DoHttpWebRequest(request, Encoding.UTF8.GetBytes(data));
			GenUtils.LogMsg("info", "_add_fb_event", response.status.ToString() + ", " + response.DataAsString());
		}

		#endregion

	}
}


