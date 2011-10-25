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

			var template_uri = BlobStorage.MakeAzureBlobUri("admin", "home.tmpl");
			var page = HttpUtils.FetchUrl(template_uri).DataAsString();

			var css_uri = BlobStorage.MakeAzureBlobUri("admin", "elmcity-1.3.css").ToString();
			page = page.Replace("__CSS__", css_uri);

			var auth_uri = BlobStorage.MakeAzureBlobUri("admin", "home_auth.tmpl");
			var signin = HttpUtils.FetchUrl(auth_uri).DataAsString();

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
				var noauth_uri = BlobStorage.MakeAzureBlobUri("admin", "home_noauth.tmpl");
				var noauth = HttpUtils.FetchUrl(noauth_uri).DataAsString();
				signin = noauth;
			}

			page = page.Replace("__TITLE__", ElmcityApp.pagetitle);
			page = page.Replace("__AUTHENTICATE__", signin);
			page = page.Replace("__WHERE_HUBS__", Utils.GetWhereSummary());
			page = page.Replace("__WHAT_HUBS__", Utils.GetWhatSummary());
			page = page.Replace("__VERSION__", ElmcityApp.version);
			ViewData["page"] = page;
			return View();
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

//		[OutputCache(Duration = CalendarAggregator.Configurator.homepage_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult hubfiles(string id)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			ViewData["title"] = ElmcityApp.pagetitle;
			ViewData["id"] = id;
			return View();
		}

		public ActionResult snapshot()
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			ViewData["title"] = String.Format("{0}: diagnostic snapshot", ElmcityApp.pagetitle);
			ViewData["snapshot"] = ElmcityUtils.Counters.DisplaySnapshotAsText();

			return View();
		}

		public ActionResult table_query(string table, string query, string attrs)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			var list_of_attr = attrs.Split(',').ToList();
			var ts = TableStorage.MakeDefaultTableStorage();
			ViewData["page"] = ts.QueryEntitiesAsHtml(table, query, list_of_attr);
			return View();
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult ics_from_fb_page(string fb_id, string elmcity_id)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			try
			{
				var ics = Utils.IcsFromFbPage(fb_id, elmcity_id, ElmcityController.settings);
				ViewData["ics"] = ics;
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ics_from_fb_page: " + fb_id + ", " + elmcity_id, e.Message + e.StackTrace);
			}
			return View();
		}

		//[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult ics_from_xcal(string url, string source, string tzname)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var tzinfo = Utils.TzinfoFromName(tzname);
			var ics = Utils.IcsFromRssPlusXcal(url, source, tzinfo);
			ViewData["ics"] = ics;
			return View();
		}

		//[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult ics_from_vcal(string url, string source, string tzname)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var tzinfo = Utils.TzinfoFromName(tzname);
			var ics = Utils.IcsFromAtomPlusVCalAsContent(url, source, tzinfo);
			ViewData["ics"] = ics;
			return View();
		}

		public ActionResult py(string arg1, string arg2, string arg3)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			if (this.AuthenticateAsSelf())
			{
				var script_url = "http://elmcity.blob.core.windows.net/admin/_generic.py";
				var args = new List<string>() { arg1, arg2, arg3 };
				ViewData["result"] = PythonUtils.RunIronPython(WebRole.local_storage_path, script_url, args);
				return View();
			}
			else
			{
				ViewData["result"] = "not authenticated";
				return View();
			}
		}

		public ActionResult maybe_purge_cache()
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			if (this.AuthenticateAsSelf())
			{
				var cache = new ElmcityUtils.AspNetCache(this.ControllerContext.HttpContext.Cache);
				ElmcityUtils.CacheUtils.MaybePurgeCache(cache);
			}
			return View();
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
			return View();
		}

		#region hub/feed editor

		public ActionResult meta_history(string a_name, string b_name, string id, string flavor)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			ViewData["result"] = CalendarAggregator.Utils.GetMetaHistory(a_name, b_name, id, flavor);
			return View();
		}

		public ActionResult put_json_metadata(string id, string json)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			GenUtils.LogMsg("info", "put_json_metadata", "id: " + id + " json: " + json);

			var auth_mode = this.Authenticated(id);

			if ( auth_mode != null  )
			{
				ViewData["result"] = json;
				var args = new Dictionary<string,string>() { {"id",id}, {"json",json} };
				ThreadPool.QueueUserWorkItem(new WaitCallback(put_json_metadata_handler), args);
			}
			else
			{
				ViewData["result"] = AuthFailMessage(id);
			}

			return View();
		}

		private void put_json_metadata_handler(Object args)
		{
			var dict = (Dictionary<string, string>)args;
			try
			{
				var id = dict["id"];
				var json = dict["json"];
				Metadata.UpdateMetadataForId(id, json);
				bs.DeleteBlob(id, "metadata.html");
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "put_json_metadata_handler", e.Message + e.StackTrace);
				throw (e);
			}
		}

		public ActionResult put_json_feeds(string id, string json)
		{
			GenUtils.LogMsg("info", "put_json_feeds", "id: " + id + " json: " + json);
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			var auth_mode = this.Authenticated(id);

			if (auth_mode != null)
			{
				ViewData["result"] = json;
				var args = new Dictionary<string, string>() { { "id", id }, { "json", json } };
				ThreadPool.QueueUserWorkItem(new WaitCallback(put_json_feeds_handler), args);
			}
			else
			{
				ViewData["result"] = AuthFailMessage(id);
			}

			return View();
		}

		private void put_json_feeds_handler(Object args)
		{
			var dict = (Dictionary<string, string>)args;
			try
			{
				var id = dict["id"];
				var json = dict["json"];
				Metadata.UpdateFeedsForId(id, json);
				bs.DeleteBlob(id, "metadata.html"); 
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
			var uri = BlobStorage.MakeAzureBlobUri(id, name );
			ViewData["result"] = HttpUtils.FetchUrl(uri).DataAsString();
			return View();
		}

		public ActionResult get_editable_metadata(string id, string flavor)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			var auth_mode = this.Authenticated(id);

			if ( auth_mode != null )
			{
				var uri = BlobStorage.MakeAzureBlobUri("admin", "editable_metadata.html");
				var page = HttpUtils.FetchUrl(uri).DataAsString();
				page = page.Replace("__ID__", id);
				page = page.Replace("__FLAVOR__", flavor);
				ViewData["result"] = page;
			}
			else
			{
				ViewData["result"] = AuthFailMessage(id);
			}
			return View();
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
			//access_token=117860144976406|2.AQCNmy6WOTkvR1lL.3600.1314115200.1-652661115|io2kl5gXx2IXhyvgiEKFeD6K9ks&
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

			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
			var dict = (Dictionary<string, object>)serializer.DeserializeObject(r.DataAsString());
			var access_token = (string)dict["access_token"];

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

		public ActionResult fb_access(string code)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			try
			{
				var token_attribute = "facebook_access_token";
				var q = string.Format("$filter=RowKey eq '{0}'", token_attribute);
				var ts = TableStorage.MakeDefaultTableStorage();
				var existing_record = TableStorage.QueryForSingleEntityAsDictStr(ts, "settings", q);
				var existing_token = existing_record[token_attribute];


				var facebook_token_getter_id = settings["facebook_token_getter_id"];
				var facebook_token_getter_secret = settings["facebook_token_getter_secret"];
				var facebook_token_getter_redirect_uri = settings["facebook_token_getter_redirect_uri"];

				var new_token = GetFacebookAccessToken(code, facebook_token_getter_id, facebook_token_getter_redirect_uri, facebook_token_getter_secret);
				if (new_token != existing_token)
				{
					var entity = new Dictionary<string, object>();
					entity.Add(token_attribute, new_token);
					TableStorage.UpmergeDictToTableStore(entity, "settings", partkey: "settings", rowkey: token_attribute);
					GenUtils.PriorityLogMsg("info", "fb_access: new token", null);
				}
				else
					GenUtils.PriorityLogMsg("info", "fb_access: same token", null);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "fb_access", e.Message + e.StackTrace);
			}

			return new RedirectResult("/");  // could be anything, this is web equivalent of void method
		}

		#endregion

	}
}


