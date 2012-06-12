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
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Mvc;
using CalendarAggregator;
using ElmcityUtils;
using HtmlAgilityPack;

namespace WebRole
{
	public class ServicesController : ElmcityController
	{
		private static UTF8Encoding UTF8 = new UTF8Encoding(false);

		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();
		private static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();

		//private static List<string> cacheable_types = new List<string>() { "ics", "search", "stats" };
		private static List<string> cacheable_types = new List<string>() { };

		public ServicesController()
		{
		}

		#region events

		//[OutputCache(Duration = ... // output cache not used here, iis cache is managed directly
		public ActionResult GetEvents(string id, string type, string view, string jsonp, string count, string from, string to)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			if (view != null)
				view = view.ToLower();

			EventsResult r = null;
			try
			{
				var cr = ElmcityApp.wrd.renderers[id];
				r = new EventsResult(this.ControllerContext, cr, id, type, view, jsonp, count, from, to);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "GetEvents: " + id, e.Message);
			}
			return r;
		}

		public class EventsResult : ActionResult
		{
			ControllerContext context;
			CalendarRenderer cr;
			string id;
			string type;
			string view;
			string jsonp;
			int count { get; set; }
			DateTime from;
			DateTime to;

			CalendarRenderer.ViewRenderer renderer = null;
			string response_body = null;
			byte[] response_bytes = new byte[0];

			public EventsResult(ControllerContext context, CalendarRenderer cr, string id, string type, string view, string jsonp, string count, string from, string to)
			{
				this.context = context;
				this.cr = cr;
				this.cr.cache = new ElmcityUtils.AspNetCache(context.HttpContext.Cache);
				this.id = id;
				this.type = type;
				this.view = view;
				this.jsonp = jsonp;
				this.count = (count == null) ? 0 : Convert.ToInt16(count);
				this.from = from == null ? DateTime.MinValue : Utils.DateTimeFromISO8601DateStr(from, DateTimeKind.Local);
				this.to = from == null ? DateTime.MinValue : Utils.DateTimeFromISO8601DateStr(to, DateTimeKind.Local);
			}

			public override void ExecuteResult(ControllerContext context)
			{

				// for dynamic views derived from the static file 
				// which is the base object for this id, e.g.:
				//  http://elmcity.blob.core.windows.net/a2cal/a2cal.zoneless.obj
				// cache the base object if uncached
				var base_key = Utils.MakeBaseZonelessUrl(this.id);
				if (this.cr.cache[base_key] == null)
				{
					var bytes = HttpUtils.FetchUrl(new Uri(base_key)).bytes;
					//InsertIntoCache(bytes, ElmcityUtils.Configurator.cache_sliding_expiration, dependency: null, key: base_key);
					InsertIntoCache(bytes, dependency: null, key: base_key);
				}

				// uri for static content, e.g.:
				// http://elmcity.blob.core.windows.net/a2cal/a2cal.stats.html
				// http://elmcity.blob.core.windows.net/a2cal/a2cal.search.html
				// these generated files could be served using their blob urls, but 
				// here are repackaged into the /services namespace, e.g.:
				// http://elmcity.cloudapp.net/services/a2cal/stats
				// http://elmcity.cloudapp.net/services/a2cal/search

				var blob_uri = BlobStorage.MakeAzureBlobUri(this.id, this.id + "." + this.type, false);

				// cache static content
				var blob_key = blob_uri.ToString();
				if (cacheable_types.Exists(t => t == this.type) && this.cr.cache[blob_key] == null)
				{
					var bytes = HttpUtils.FetchUrl(blob_uri).bytes;
					var dependency = new ElmcityCacheDependency(base_key);
					InsertIntoCache(bytes, dependency: dependency, key: blob_key);
				}

				var fmt = "{0:yyyyMMddTHHmm}";
				var from_str = string.Format(fmt, this.from);
				var to_str = string.Format(fmt, this.to); 
				var view_key = Utils.MakeViewKey(this.id, this.type, this.view, this.count.ToString(), from_str, to_str);

				switch (this.type)
				{
					case "html":
						this.renderer = new CalendarRenderer.ViewRenderer(cr.RenderHtml);
						MaybeCacheView(view_key, this.renderer, new ElmcityCacheDependency(base_key));
						this.response_body = cr.RenderDynamicViewWithCaching(context, view_key, this.renderer, this.view, this.count, this.from, this.to);
						new ContentResult
						{
							ContentType = "text/html",
							Content = this.response_body,
							ContentEncoding = UTF8
						}.ExecuteResult(context);
						break;

					case "xml":
						this.renderer = new CalendarRenderer.ViewRenderer(cr.RenderXml);
						MaybeCacheView(view_key, this.renderer, new ElmcityCacheDependency(base_key));
						this.response_body = cr.RenderDynamicViewWithCaching(context, view_key, this.renderer, this.view, this.count, this.from, this.to);
						new ContentResult
						{
							ContentType = "text/xml",
							Content = this.response_body,
							ContentEncoding = UTF8
						}.ExecuteResult(context);
						break;

					case "rss":
						if (count == 0) count = CalendarAggregator.Configurator.rss_default_items;
						this.renderer = new CalendarRenderer.ViewRenderer(cr.RenderRss);
						MaybeCacheView(view_key, this.renderer, new ElmcityCacheDependency(base_key));
						this.response_body = cr.RenderDynamicViewWithCaching(context, view_key, this.renderer, this.view, this.count, this.from, this.to);

						new ContentResult
						{
							ContentType = "text/xml",
							Content = response_body,
							ContentEncoding = UTF8
						}.ExecuteResult(context);
						break;

					case "json":
						this.renderer = new CalendarRenderer.ViewRenderer(cr.RenderJson);
						MaybeCacheView(view_key, this.renderer, new ElmcityCacheDependency(base_key));
						string jcontent = cr.RenderDynamicViewWithCaching(context, view_key, this.renderer, this.view, this.count, this.from, this.to);
						if (this.jsonp != null)
							jcontent = this.jsonp + "(" + jcontent + ")";
						new ContentResult
						{
							ContentEncoding = UTF8,
							ContentType = "application/json",
							Content = jcontent
						}.ExecuteResult(context);
						break;

					case "tags_json":
						string tjcontent = cr.RenderTagCloudAsJson();
						if (this.jsonp != null)
							tjcontent = this.jsonp + "(" + tjcontent + ")";
						new ContentResult
						{
							ContentEncoding = UTF8,
							ContentType = "application/json",
							Content = tjcontent,
						}.ExecuteResult(context);
						break;

					case "tags_html":
						string thcontent = cr.RenderTagCloudAsHtml();
						new ContentResult
						{
							ContentEncoding = UTF8,
							ContentType = "text/html",
							Content = thcontent,
						}.ExecuteResult(context);
						break;

					case "stats":
						blob_uri = BlobStorage.MakeAzureBlobUri(this.id, this.id + ".stats.html",false);
						//this.response_bytes = (byte[])CacheUtils.RetrieveBlobAndEtagFromServerCacheOrUri(this.cr.cache, blob_uri)["response_body"];
						this.response_bytes = HttpUtils.FetchUrl(blob_uri).bytes;
						new ContentResult
						{
							ContentEncoding = UTF8,
							ContentType = "text/html",
							Content = Encoding.UTF8.GetString(this.response_bytes),
						}.ExecuteResult(context);
						break;

					case "ics":
						this.renderer = new CalendarRenderer.ViewRenderer(cr.RenderIcs);
						MaybeCacheView(view_key, this.renderer, new ElmcityCacheDependency(base_key));
						string ics_text = cr.RenderDynamicViewWithCaching(context, view_key, this.renderer, this.view, this.count, this.from, this.to);
						new ContentResult
						{
							ContentType = "text/calendar",
							Content = ics_text,
							ContentEncoding = UTF8
						}.ExecuteResult(context);
						break;

					case "jswidget":
						new ContentResult
						{
							ContentType = "text/html",
							Content = cr.RenderJsWidget(),
							ContentEncoding = UTF8
						}.ExecuteResult(context);
						break;

					case "search":
						blob_uri = BlobStorage.MakeAzureBlobUri(this.id, this.id + ".search.html",false);
						this.response_bytes = (byte[])CacheUtils.RetrieveBlobAndEtagFromServerCacheOrUri(this.cr.cache, blob_uri)["response_body"];
						new ContentResult
						{
							ContentType = "text/html",
							Content = Encoding.UTF8.GetString(this.response_bytes),
							ContentEncoding = UTF8
						}.ExecuteResult(context);
						break;

					case "today_as_html":
						new ContentResult
						{
							ContentType = "text/html",
							Content = cr.RenderTodayAsHtml(),
							ContentEncoding = UTF8
						}.ExecuteResult(context);
						break;
				}

			}

			private void InsertIntoCache(byte[] bytes, CacheDependency dependency, string key)
			{
				var logger = new CacheItemRemovedCallback(AspNetCache.LogRemovedItemToAzure);
				var expiration_hours = ElmcityUtils.Configurator.cache_sliding_expiration.Hours;
				var sliding_expiration = new TimeSpan(expiration_hours, 0, 0);
				this.cr.cache.Insert(key, bytes, dependency, Cache.NoAbsoluteExpiration, sliding_expiration, CacheItemPriority.Normal, logger);
			}

			private void MaybeCacheView(string view_key, CalendarRenderer.ViewRenderer view_renderer, CacheDependency dependency)
			{
				if (this.cr.cache[view_key] == null)
				{
					var view_str = this.cr.RenderDynamicViewWithoutCaching(this.context, view_renderer, this.view, this.count, this.from, this.to);
					byte[] view_bytes = Encoding.UTF8.GetBytes(view_str);
					InsertIntoCache(view_bytes, dependency, view_key);
				}
			}

		}

		#endregion

		#region logentries

		public ActionResult GetLogEntries(string log, string type, string id, string minutes, string targets)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			LogEntriesResult r = null;

			try
			{
				r = new LogEntriesResult(log, type, id, minutes, targets);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "GetLogEntries: " + id, e.Message);
			}
			return r;
		}

		public class LogEntriesResult : ActionResult
		{
			string log;
			string type;
			string id;
			int minutes;
			string targets;

			public LogEntriesResult(string log, string type, string id, string minutes, string targets)
			{
				this.log = log;
				this.type = type;
				this.id = id;
				this.minutes = Convert.ToInt16(minutes);
				this.targets = targets;
			}

			public override void ExecuteResult(ControllerContext context)
			{
				// it can take a while to fetch a large result
				context.HttpContext.Server.ScriptTimeout = CalendarAggregator.Configurator.webrole_script_timeout_seconds;
				var content = Utils.GetRecentLogEntries(this.log, this.type, this.minutes, this.targets);
				new ContentResult
				{
					ContentType = "text/plain",
					Content = content,
					ContentEncoding = UTF8
				}.ExecuteResult(context);
			}
		}

		#endregion

		#region metadata

		public ActionResult GetMetadata(string id)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			MetadataResult r = null;

			try
			{
				r = new MetadataResult(id);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "GetMetadata: " + id, e.Message);
			}
			return r;
		}

		public class MetadataResult : ActionResult
		{
			string id;

			public MetadataResult(string id)
			{
				this.id = id;
			}

			public override void ExecuteResult(ControllerContext context)
			{
				var name = "metadata.html";
				var exists = BlobStorage.ExistsBlob(this.id, name); // periodically recreated in Worker.GeneralAdmin
				if (!exists)
					Utils.MakeMetadataPage(this.id);
				var response = bs.GetBlob(this.id, name);
				if (response.HttpResponse.status != HttpStatusCode.OK)
					GenUtils.PriorityLogMsg("warning", "cannot get metadata.html for " + this.id, null);
				new ContentResult
				{
					ContentType = "text/html",
					Content = response.HttpResponse.DataAsString(),
					ContentEncoding = UTF8
				}.ExecuteResult(context);
			}
		}

		#endregion

		#region fusecal

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult GetFusecalICS(string url, string filter, string tz_source, string tz_dest)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			FusecalICSResult r = null;

			try
			{
				r = new FusecalICSResult(url, filter, tz_source, tz_dest);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "GetFusecalICS: " + url, e.Message);
			}
			return r;
		}

		public class FusecalICSResult : ActionResult
		{
			string fusecal_url;
			string filter;
			string tz_source;
			string tz_dest;

			public FusecalICSResult(string url, string filter, string tz_source, string tz_dest)
			{
				this.fusecal_url = url;
				this.filter = filter;
				this.tz_source = tz_source;
				this.tz_dest = tz_dest;
			}

			public override void ExecuteResult(ControllerContext context)
			{
				// this one calls out to python, can take a while
				context.HttpContext.Server.ScriptTimeout = CalendarAggregator.Configurator.webrole_script_timeout_seconds;
				var args = new List<string>() { this.fusecal_url, this.filter, this.tz_source, this.tz_dest };
				var ics = PythonUtils.RunIronPython(WebRole.local_storage_path, CalendarAggregator.Configurator.fusecal_dispatcher, args);
				new ContentResult
				{
					ContentType = "text/calendar",
					Content = ics,
					ContentEncoding = UTF8
				}.ExecuteResult(context);
			}
		}

		#endregion

		#region cache

		public ActionResult RemoveCacheEntry(string cached_uri)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			if (!this.AuthenticateAsSelf())
				return new EmptyResult();

			RemoveCacheEntryResult r = null;

			try
			{
				r = new RemoveCacheEntryResult(this.ControllerContext, cached_uri);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "RemoveCacheEntry: " + cached_uri, e.Message);
			}
			return r;
		}

		public class RemoveCacheEntryResult : ActionResult
		{
			string cached_uri;

			public RemoveCacheEntryResult(ControllerContext context, string cached_uri)
			{
				this.cached_uri = cached_uri;
				context.HttpContext.Cache.Remove(cached_uri);
			}

			public override void ExecuteResult(ControllerContext context)
			{
				new ContentResult
				{
					ContentType = "text/plain",
					Content = cached_uri + ": removed",
					ContentEncoding = UTF8
				}.ExecuteResult(context);
			}
		}

		public ActionResult ViewCache()
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			ViewCacheResult r = null;

			try
			{
				r = new ViewCacheResult(this.ControllerContext);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ViewCache", e.Message);
			}
			return r;
		}

		public class ViewCacheResult : ActionResult
		{
			ControllerContext context;

			public ViewCacheResult(ControllerContext context)
			{
				this.context = context;
			}

			public override void ExecuteResult(ControllerContext context)
			{
				new ContentResult
				{
					ContentType = "text/html",
					Content = CacheUtils.ViewCache(context),
					ContentEncoding = UTF8
				}.ExecuteResult(context);
			}
		}

		#endregion

		#region arra

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult GetArraData(string state, string town, string year, string quarter)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			ArraResult r = null;

			try
			{
				r = new ArraResult(state, town, year, quarter);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "GetArraData: " + state + ' ' + town, e.Message);
			}
			return r;
		}

		public class ArraResult : ActionResult
		{
			string state;
			string town;
			string year;
			string quarter;

			public ArraResult(string state, string town, string year, string quarter)
			{
				this.state = state;
				this.town = town;
				this.year = year;
				this.quarter = quarter;
			}

			public override void ExecuteResult(ControllerContext context)
			{
				string result;
				try
				{
					result = Arra.MakeArraPage(state, town, year, quarter);
				}
				catch
				{
					result = string.Format(@"<p>Error looking up {0} {1}. Try <a href=""/arra"">another</a>?</p>",
					town, state);
				}
				new ContentResult
				{
					ContentType = "text/html",
					Content = result,
					ContentEncoding = UTF8
				}.ExecuteResult(context);
			}
		}

		#endregion

		#region odata

		[OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration_seconds, VaryByParam = "*")]
		public ActionResult GetODataFeed(string table, string pk, string rk, string constraints, string since_minutes_ago)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			ODataFeedResult r = null;

			try
			{
				r = new ODataFeedResult(table, pk, rk, constraints, since_minutes_ago);
			}
			catch (Exception e)
			{
				var args = String.Format("table {0}, pk {1}, rk {2}, constraints {3}, since_minutes_ago {4}",
					table, pk, rk, constraints, since_minutes_ago);

				GenUtils.PriorityLogMsg("exception", "GetODataFeed: " + args, e.Message);
			}
			return r;
		}

		public class ODataFeedResult : ActionResult
		{
			string table;
			string query;
			TableStorage ts = TableStorage.MakeDefaultTableStorage();

			public ODataFeedResult(string table, string pk, string rk, string constraints, string since_minutes_ago)
			{
				this.table = table;

				string final_query = "";
				if (pk != null)
					final_query += String.Format("PartitionKey eq '{0}'", pk);

				string rowkey_clause = "";

				if (rk != null)
					rowkey_clause = String.Format("{0}RowKey eq '{1}'", pk != null ? " and " : "", rk);

				if (since_minutes_ago != null)
				{
					var minutes_ago = Convert.ToInt32(since_minutes_ago);
					var since = DateTime.UtcNow - TimeSpan.FromMinutes(minutes_ago);
					rowkey_clause = string.Format("{0}RowKey gt '{1}'", pk != null ? " and " : "", since.Ticks);
				}

				final_query += rowkey_clause;

				if (constraints != null)
					final_query += String.Format("{0}{1}", (pk != null || rk != null) ? " and " : "", constraints);

				this.query = string.Format("$filter=({0})", final_query);
			}

			public override void ExecuteResult(ControllerContext context)
			{
				var tsr = this.ts.QueryAllEntitiesAsStringOfXml(this.table, this.query).str;
				new ContentResult
				{
					ContentType = "application/atom+xml",
					Content = (string)tsr,
					ContentEncoding = UTF8
				}.ExecuteResult(context);
			}

		}

		#endregion

		#region twitter

		public ActionResult CallTwitterApi(string method, string url, string post_data, string oauth_token)
		{
			ElmcityApp.logger.LogHttpRequest(this.ControllerContext);

			if (!this.AuthenticateAsSelf())
				return new EmptyResult();

			// on startup, invoke redirect to get called back with an oauth_token which enables
			// the getting of an access token
			//if (ElmcityApp.twitter_oauth_access_token == null && oauth_token == null)
			if (settings["twitter_access_token"] == "none" && oauth_token == null)
			{
				var redirect_url = AutoOAuthLinkGet();
				redirect_url += String.Format("&method={0}&url={1}&post_data={2}",
					method,
					Url.Encode(url),
					post_data);
				return new RedirectResult(redirect_url);
			}

			// on callback, get/store the access token
			//if (ElmcityApp.twitter_oauth_access_token == null && oauth_token != null)
			if (settings["twitter_access_token"] == "none" && oauth_token != null)
			{
				OAuthAccessTokenGet(oauth_token);
				settings = GenUtils.GetSettingsFromAzureTable();
			}

			TwitterApiResult r = null;

			try
			{
				r = new TwitterApiResult(method, url, post_data);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "TwitterApiResult: " + method + ", " + url + ", " + post_data, e.Message);
			}

			return r;
		}

		public class TwitterApiResult : ActionResult
		{
			OAuthTwitter.Method method;
			string url;
			string post_data;

			public TwitterApiResult(string method, string url, string post_data)
			{
				this.method = GetTwitterMethod(method);
				this.url = url;
				this.post_data = post_data;
			}

			public override void ExecuteResult(ControllerContext context)
			{
				var content = CallTwitterApi(this.method, this.url, this.post_data);
				new ContentResult
				{
					ContentType = "application/xml",
					Content = content,
					ContentEncoding = UTF8
				}.ExecuteResult(context);
			}

			private OAuthTwitter.Method GetTwitterMethod(string str_method)
			{
				OAuthTwitter.Method method = OAuthTwitter.Method.GET;
				switch (str_method)
				{
					case "GET":
						break;
					case "POST":
						method = OAuthTwitter.Method.POST;
						break;
					case "DELETE":
						method = OAuthTwitter.Method.DELETE;
						break;
				}

				return method;

			}

			private string CallTwitterApi(OAuthTwitter.Method method, string api_url, string post_data)
			{
				ElmcityApp.oauth_twitter.token = settings["twitter_access_token"];
				ElmcityApp.oauth_twitter.token_secret = settings["twitter_access_token_secret"];
				string xml = ElmcityApp.oauth_twitter.oAuthWebRequest(method, api_url, post_data);
				return xml;
			}

		}

		public string AutoOAuthLinkGet()
		{
			var link = ElmcityApp.oauth_twitter.AuthorizationLinkGet();
			var initial_response = HttpUtils.FetchUrl(new Uri(link));
			HtmlDocument doc = new HtmlDocument();
			doc.LoadHtml(initial_response.DataAsString());

			HtmlNode auth_form = doc.DocumentNode.SelectNodes("//form").First();
			var authenticity_token_node = auth_form.SelectNodes("//input[@name='authenticity_token']").First();
			var authenticity_token = authenticity_token_node.Attributes["value"].Value;
			var oauth_token_node = auth_form.SelectNodes("//input[@name='oauth_token']").First();
			var oauth_token = oauth_token_node.Attributes["value"].Value;

			var post_data_template = "authenticity_token={0}&oauth_token={1}&session%5Busername_or_email%5D={2}&session%5Bpassword%5D={3}";

			var auth_uri = OAuthTwitter.AUTHORIZE;
			var post_data = String.Format(post_data_template, authenticity_token, oauth_token, settings["twitter_account"], settings["twitter_password"]);
			var request = (HttpWebRequest)WebRequest.Create(auth_uri);
			request.Method = "POST";
			var login_response = HttpUtils.DoHttpWebRequest(request, Encoding.UTF8.GetBytes(post_data));

			doc.LoadHtml(login_response.DataAsString());
			HtmlNode redirect_page = doc.DocumentNode;
			var callback_node = redirect_page.SelectNodes("//div[@class='message-content']//a[@href]").First();
			var callback_url = callback_node.Attributes["href"].Value;

			return callback_url;

		}

		private void OAuthAccessTokenGet(string auth_token)
		{
			ElmcityApp.oauth_twitter.token = auth_token;

			string response = ElmcityApp.oauth_twitter.oAuthWebRequest(OAuthTwitter.Method.GET, OAuthTwitter.ACCESS_TOKEN, String.Empty);

			if (response.Length > 0)
			{
				//Store the Token and Token Secret
				NameValueCollection qs = HttpUtility.ParseQueryString(response);
				if (qs["oauth_token"] != null)
				{
					//ElmcityApp.oauth_twitter.token = ElmcityApp.twitter_oauth_access_token = qs["oauth_token"];
					var access_token = qs["oauth_token"];
					UpdateTokenSetting("twitter_access_token", access_token);
				}
				if (qs["oauth_token_secret"] != null)
				{
					var access_token_secret = qs["oauth_token_secret"];
					UpdateTokenSetting("twitter_access_token_secret", access_token_secret);
				}

			}
		}

		private static void UpdateTokenSetting(string name, string value)
		{
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", "settings");
			entity.Add("RowKey", name);
			entity.Add("value", value);
			ts.UpdateEntity("settings", "settings", name, entity);
		}

		#endregion

	}

}

