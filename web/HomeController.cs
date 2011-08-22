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
using System.Text.RegularExpressions;
using CalendarAggregator;
using ElmcityUtils;
using Newtonsoft.Json;
using System.Threading;

namespace WebRole
{
	public class HomeController : ElmcityController
	{
		BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
		TableStorage ts = TableStorage.MakeDefaultTableStorage();
		Delicious delicious = Delicious.MakeDefaultDelicious();

		public HomeController()
		{
			ElmcityApp.home_controller = this;
			//this.wrd = WebRolePipeReader.Read();
		}

		//[OutputCache(Duration = CalendarAggregator.Configurator.home_page_output_cache_duration, VaryByParam = "None")]
		public ActionResult index()
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			var template_uri = BlobStorage.MakeAzureBlobUri("admin", "home.tmpl");
			var page = HttpUtils.FetchUrl(template_uri).DataAsString();

			var css_uri = BlobStorage.MakeAzureBlobUri("admin", "elmcity-1.2.css").ToString();
			page = page.Replace("__CSS__", css_uri);

			string twitter_auth = "";
			List<string> elmcity_ids;

			if ( AuthenticateViaTwitter() )
			{
				try
				{
					var auth_uri = BlobStorage.MakeAzureBlobUri("admin", "home_auth.tmpl");
					var auth = HttpUtils.FetchUrl(auth_uri).DataAsString();

					var twitter_name = GetAuthenticatedTwitterUserOrNull();
					
					elmcity_ids = Utils.ElmcityIdsFromTwitterName(twitter_name);
					string elmcity_id = elmcity_ids[0]; // if > 1 use first by default, others manual for now

					twitter_auth = auth.Replace("__TWITTERNAME__", twitter_name);
					twitter_auth = auth.Replace("__ELMCITYID__", elmcity_id);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "home page auth", e.Message + e.StackTrace);
				}
			}
			else
			{
				var noauth_uri = BlobStorage.MakeAzureBlobUri("admin", "home_noauth.tmpl");
				var noauth = HttpUtils.FetchUrl(noauth_uri).DataAsString();
				twitter_auth = noauth;
			}

			ViewData["twitter_auth"] = twitter_auth;
			ViewData["title"] = ElmcityApp.pagetitle;
			ViewData["where_summary"] = make_where_summary();
			ViewData["what_summary"] = make_what_summary();
			ViewData["version"] = ElmcityApp.version;
			return View();
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.homepage_output_cache_duration_seconds, VaryByParam = "None")]
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

		public ActionResult viewer(string url, string source)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			ViewData["title"] = String.Format("{0}: viewing {1}",
				ElmcityApp.pagetitle, url);
			ViewData["view"] = CalendarRenderer.Viewer(url, source);
			return View();
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "None")]
		public ActionResult ics_from_xcal(string url, string source, string tzname)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			var tzinfo = Utils.TzinfoFromName(tzname);
			var ics = Utils.IcsFromRssPlusXcal(url, source, tzinfo);
			ViewData["ics"] = ics;
			return View();
		}

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "None")]
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
				ElmcityApp.reload(o, e);
				ElmcityApp.logger.LogMsg("info", "HomeController reload", null);
			}
			catch (Exception ex)
			{
				GenUtils.PriorityLogMsg("exception", "HomeController reload", ex.Message + ex.StackTrace);
			}
			return View();
		}

		public ActionResult delicious_check(string id)
		{
			ViewData["result"] = Delicious.DeliciousCheck(id);
			return View();
		}

		public ActionResult meta_history(string a_name, string b_name, string id, string flavor)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
			ViewData["result"] = CalendarAggregator.Utils.GetMetaHistory(a_name, b_name, id, flavor);
			return View();
		}

		public ActionResult put_json_metadata(string id, string json)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			if (this.AuthenticateViaTwitter(id))
			{
				ViewData["result"] = json;
				var args = new Dictionary<string,string>() { {"id",id}, {"json",json} };
				ThreadPool.QueueUserWorkItem(new WaitCallback(put_json_metadata_handler), args);
				return View();
			}
			else
			{
				ViewData["result"] = "not authenticated";
				return View();
			}
		}

		private void put_json_metadata_handler(Object args)
		{
			var dict = (Dictionary<string, string>)args;
			var id = dict["id"];
			var json = dict["json"];
			try
			{
				Metadata.UpdateMetadataForId(id, json);
				bs.DeleteBlob(id, "metadata.html");
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "put_json_metadata", e.Message + e.StackTrace);
				throw (e);
			}
		}

		public ActionResult put_json_feeds(string id, string json)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			if (this.AuthenticateViaTwitter(id))
			{
				ViewData["result"] = json;
				var args = new Dictionary<string, string>() { { "id", id }, { "json", json } };
				ThreadPool.QueueUserWorkItem(new WaitCallback(put_json_feeds_handler), args);
				return View();
			}
			else
			{
				ViewData["result"] = "not authenticated";
				return View();
			}
		}

		private void put_json_feeds_handler(Object args)
		{
			var dict = (Dictionary<string, string>)args;
			var id = dict["id"];
			var json = dict["json"];
			try
			{
				Metadata.UpdateFeedsForId(id, json);
				bs.DeleteBlob(id, "metadata.html"); 
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "put_json_feeds", e.Message + e.StackTrace);
				throw (e);
			}
		}

		public ActionResult twitter_auth(string method, string url, string post_data, string oauth_token)
		{
			var oauth_twitter = new OAuthTwitter(consumer_key: settings["twitter_auth_consumer_key"], consumer_secret: settings["twitter_auth_consumer_secret"]);

			if (Request.Cookies["ElmcityTwitter"] == null)
			{
				var cookie = new HttpCookie("ElmcityTwitter", DateTime.UtcNow.Ticks.ToString());
				Response.SetCookie(cookie);
			}

			if (oauth_token == null)
			{
				var link = oauth_twitter.AuthorizationLinkGet();
				return new RedirectResult(link);
			}

			if (oauth_token != null)
			{
				var session_id = Request.Cookies["ElmcityTwitter"].Value;
				oauth_twitter.token = oauth_token;
				string response = oauth_twitter.oAuthWebRequest(OAuthTwitter.Method.GET, OAuthTwitter.ACCESS_TOKEN, String.Empty);
				if (response.Length > 0)
				{
					System.Collections.Specialized.NameValueCollection qs = HttpUtility.ParseQueryString(response);
					RememberTwitterUser(session_id, qs["screen_name"]);
				}
			}

			return new RedirectResult("/");

		}

		private void RememberTwitterUser(string session_id, string screen_name)
		{
			var entity = new Dictionary<string, object>();
			entity["screen_name"] = screen_name;
			entity["host_addr"] = Request.UserHostAddress;
			entity["host_name"] = Request.UserHostName;
			TableStorage.DictObjToTableStore(TableStorage.Operation.update, entity, "sessions", "sessions", session_id);
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
			if ( AuthenticateViaTwitter(id) )
			{
				var uri = BlobStorage.MakeAzureBlobUri("admin", "editable_metadata.html");
				var page = HttpUtils.FetchUrl(uri).DataAsString();
				page = page.Replace("__ID__", id);
				page = page.Replace("__FLAVOR__", flavor);
				ViewData["result"] = page;
			}
			else
			{
				var screen_name = GetAuthenticatedTwitterUserOrNull();
				ViewData["result"] = @"You are authenticated via the Twitter name " +
					screen_name + " but that identity isn't linked to the elmcity hub " + id + "." + 
					" If you'd like connect them please contact " + settings["elmcity_admin"];
			}
			return View();
		}

		private string make_where_summary()
		{
			var summary = new StringBuilder();
			summary.Append(@"<table style=""width:90%;margin:auto"">");
			summary.Append(@"
<tr>
<td align=""center""><b>id</b></td>
<td align=""center""><b>location</b></td>
<td align=""center""><b>population</b></td>
<td align=""center""><b>events</b></td>
<td align=""center""><b>density</b></td>
</tr>");
			var row_template = @"
<tr>
<td>{0}</td>
<td>{1}</td>
<td align=""right"">{2}</td>
<td align=""right"">{3}</td>
<td align=""center"">{4}</td>
</tr>";
			//foreach (var id in WebRoleData.where_ids)
			foreach (var id in ElmcityApp.wrd.where_ids)
			{
				if (IsReady(id) == false)
					continue;
				var metadict = ElmcityApp.wrd.calinfos[id].metadict;
				var population = metadict.ContainsKey("population") ? metadict["population"] : "";
				var events = metadict.ContainsKey("events") ? metadict["events"] : "";
				var events_per_person = metadict.ContainsKey("events_per_person") ? metadict["events_per_person"] : "";
				var row = string.Format(row_template,
					String.Format(@"<a title=""view outputs"" href=""/services/{0}"">{0}</a>", id),
					metadict["where"],
					population != "1" ? population : "",
					events,
					population != "1" ? events_per_person : ""
					);
				summary.Append(row);
			}
			summary.Append("</table>");
			return summary.ToString();
		}

		private string make_what_summary()
		{
			var summary = new StringBuilder();
			summary.Append(@"<table style=""width:90%;margin:auto"">");
			summary.Append(@"
<tr>
<td align=""center""><b>id</b></td>
</tr>");
			var row_template = @"
<tr>
<td>{0}</td>
</tr>";
			foreach (var id in ElmcityApp.wrd.what_ids)
			{
				if (IsReady(id) == false)
					continue;
				var row = string.Format(row_template,
					String.Format(@"<a title=""view outputs"" href=""/services/{0}"">{0}</a>", id)
					);
				summary.Append(row);
			}
			summary.Append("</table>");
			return summary.ToString();
		}

		private bool IsReady(string id)
		{
			return ElmcityApp.wrd.ready_ids.Contains(id);
		}

	}
}


