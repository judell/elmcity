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
using System.Globalization;
using System.Linq;
using System.Timers;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using CalendarAggregator;
using ElmcityUtils;

namespace WebRole
{

	public class ElmcityController : Controller
	{
		public static string procname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
		public static int procid = System.Diagnostics.Process.GetCurrentProcess().Id;
		public static string domain_name = AppDomain.CurrentDomain.FriendlyName;
		public static int thread_id = System.Threading.Thread.CurrentThread.ManagedThreadId;

		private TableStorage ts = TableStorage.MakeDefaultTableStorage();

		public static Dictionary<string, string> settings = GenUtils.GetSettingsFromAzureTable();

		// last-resort exception handler
		protected override void OnException(ExceptionContext filterContext)
		{
			var msg = filterContext.Exception.Message;
			GenUtils.PriorityLogMsg("exception", "last chance", msg + filterContext.Exception.StackTrace);
			if (msg.Length > 140)
				msg = msg.Substring(0, 140);
			TwitterApi.SendTwitterDirectMessage(CalendarAggregator.Configurator.twitter_account, "last chance: " + msg);
			filterContext.ExceptionHandled = true;
			this.View("FinalError").ExecuteResult(this.ControllerContext);
		}

		// allow only trusted ip addresses
		public bool AuthenticateAsSelf()
		{
			var self_ip_addr = DnsUtils.TryGetHostAddr(ElmcityUtils.Configurator.appdomain);

			var trusted_addrs_list = new List<string>();

			// trust requests from self, e.g. http://elmcity.cloudapp.net            
			trusted_addrs_list.Add(self_ip_addr);

			// only for local testing!
			//trusted_addrs_list.Add("127.0.0.1");

			// trust requests from hosts named in an azure table
			var query = "$filter=(PartitionKey eq 'trustedhosts')";
			var trusted_host_dicts = this.ts.QueryAllEntitiesAsListDict(table: "trustedhosts", query: query).list_dict_obj;
			foreach (var trusted_host_dict in trusted_host_dicts)
			{
				if (trusted_host_dict.ContainsKey("host"))
				{
					var trusted_host_name = trusted_host_dict["host"].ToString();
					var trusted_addr = DnsUtils.TryGetHostAddr(trusted_host_name);
					trusted_addrs_list.Add(trusted_addr);
				}
			}

			var incoming_addr = this.HttpContext.Request.UserHostAddress;

			if (trusted_addrs_list.Exists(addr => addr == incoming_addr))
				return true;
			else
			{
				var msg = "AuthenticateAsSelf rejected " + incoming_addr;
				var data = "trusted: " + String.Join(", ", trusted_addrs_list.ToArray());
				GenUtils.PriorityLogMsg("warning", msg, data);
				return false;
			}
		}

		public ElmcityController()
		{
		}
	}

	public class ElmcityApp : HttpApplication
	{
		public static string version = "1158";

#if false // true if testing, false if not testing
		private static bool testing = true;
		private static string test_id = "socialhartford";
#else    // not testing
		private static bool testing = false;
		private static string test_id = "";
#endif

		public static string procname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
		public static int procid = System.Diagnostics.Process.GetCurrentProcess().Id;
		public static string domain_name = AppDomain.CurrentDomain.FriendlyName;
		public static int thread_id = System.Threading.Thread.CurrentThread.ManagedThreadId;

		private BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();

		public static Logger logger = new Logger();

		public static string pagetitle = "the elmcity project";

		public static ElmcityController home_controller;
		public static ElmcityController services_controller;

		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();

		public static OAuthTwitter oauth_twitter = new OAuthTwitter();

		public static WebRoleData wrd = null;

		public ElmcityApp()
		{
			GenUtils.LogMsg("info", String.Format("ElmcityApp {0} {1} {2} {3}", procname, procid, domain_name, thread_id), null);
		}

		public static void RegisterRoutes(RouteCollection routes, WebRoleData wrd)
		{
			GenUtils.LogMsg("info", "RegisterRoutes", "ready_ids: " + wrd.ready_ids.Count());
			routes.MapRoute(
				"home",
				"",
				new { controller = "Home", action = "index" }
			);

			// run an ics feed through the machinery and dump the html rendering
			routes.MapRoute(
				"viewer",
				"viewer",
				new { controller = "Home", action = "viewer" }
				);

			// dump a snapshot of diagnostic data
			routes.MapRoute(
				"snapshot",
				"snapshot",
				new { controller = "Home", action = "snapshot" }
				 );

			// run the method named arg1 in _generic.py, passing arg2 and arg3
			routes.MapRoute(
			   "py",
			   "py/{arg1}/{arg2}/{arg3}",
			   new { controller = "Home", action = "py", arg1 = "", arg2 = "", arg3 = "" }
			   );

			// force a reload
			routes.MapRoute(
				"reload",
				"reload",
				 new { controller = "Home", action = "reload" }
				);

			// check a delicious account
			routes.MapRoute(
				"delicious_check",
				"delicious_check",
				 new { controller = "Home", action = "delicious_check" }
				);

			// parse rss+xcal, return ics
			routes.MapRoute(
				"ics_from_xcal",
				"ics_from_xcal",
				 new { controller = "Home", action = "ics_from_xcal" }
				);

			// parse atom+vcal, return ics
			routes.MapRoute(
				"ics_from_vcal",
				"ics_from_vcal",
				 new { controller = "Home", action = "ics_from_vcal" }
				);

			// for a bare url like /services/a2cal, emit a page that documents all the available formats
			routes.MapRoute(
				"hubfiles",
				"services/{id}/",
				new { controller = "Home", action = "hubfiles" },
				new { id = wrd.str_ready_ids }
				);

			// this pattern covers most uses. gets events for a given hub id in many formats. allows
			// only the specified formats, and only hub ids that are "ready"
			routes.MapRoute(
				"events",
				"services/{id}/{type}",
				new { controller = "Services", action = "GetEvents" },
				new { id = wrd.str_ready_ids, type = "html|xml|json|ics|rss|tags_json|stats|tags_html|jswidget|today_as_html|search" }
				);

			// reach back {minutes} into the log table and spew entries since then
			routes.MapRoute(
				"logs",
				"logs/{id}/{minutes}",
				new { controller = "Services", action = "GetLogEntries" },
				new { id = "priority|all|" + wrd.str_ready_ids, minutes = new LogMinutesConstraint() }
			  );

			// dump the hub's metadata that was acquired from delicious, extended with computed values,
			// and cached to the azure metadata table
			routes.MapRoute(
				"metadata",
				"services/{id}/metadata",
				new { controller = "Services", action = "GetMetadata" },
				new { id = wrd.str_ready_ids }
				);

			// used by worker to remove pickled objects from cache after an aggregator run
			// todo: protect this endpoint
			routes.MapRoute(
				"remove",
				"services/remove_cache_entry",
				 new { controller = "Services", action = "RemoveCacheEntry" }
				 );

			routes.MapRoute(
				 "viewcache",
				 "services/viewcache",
				 new { controller = "Services", action = "ViewCache" }
				 );

			// performance monitor data as an odata feed
			routes.MapRoute(
			 "odata",
			 "services/odata",
			  new { controller = "Services", action = "GetODataFeed" }
			  );

			// entry point for the fusecal system: runs fusecal.py which dispatches to an
			// html-or-rss-or-ics to ics parser for myspace, librarything, libraryinsight, etc.
			routes.MapRoute(
			   "fusecal",
			   "services/fusecal",
			  new { controller = "Services", action = "GetFusecalICS" }
		  );

			// see http://blog.jonudell.net/2009/11/09/where-is-the-money-going/
			routes.MapRoute(
				 "arra",
				 "arra",
				 new { controller = "Services", action = "GetArraData" }
				 );

			routes.MapRoute(
				 "call_twitter_api",
				 "services/call_twitter_api",
				 new { controller = "Services", action = "CallTwitterApi" }
				 );

			GenUtils.LogMsg("info", routes.Count() + " routes", null);

		}

		protected void Application_Start()
		{
			var msg = "WebRole: Application_Start";
			GenUtils.PriorityLogMsg("info", msg, null);
			ElmcityUtils.Monitor.TryStartMonitor(CalendarAggregator.Configurator.process_monitor_interval_minutes, CalendarAggregator.Configurator.process_monitor_table);
			Utils.ScheduleTimer(WebRole.MaybePurgeCache, minutes: CalendarAggregator.Configurator.webrole_cache_purge_interval_minutes, name: "maybe_purge_cache", startnow: false);
			// scheduling cache purge here because context will be w3wp not WaIISHost
			_reload();
		}

		// encapsulate _reload with the signature needed by Utils.ScheduleTimer
		public static void reload(Object o, ElapsedEventArgs e)
		{
			_reload();
		}

		public static void _reload()
		{
			GenUtils.LogMsg("info", "webrole _reload start", null);

			try
			{
				ElmcityController.settings = GenUtils.GetSettingsFromAzureTable();
			}
			catch (Exception e0)
			{
				var msg = "_reload: cannot get settings from azure!";
				GenUtils.PriorityLogMsg("exception", msg, e0.Message);
			}

			try
			{
				var uri = BlobStorage.MakeAzureBlobUri("admin", "wrd.obj");
				wrd = (WebRoleData)BlobStorage.DeserializeObjectFromUri(uri);
			}
			catch (Exception e1)
			{
				var msg = "_reload: cannot unpickle webrole data, recreating";
				GenUtils.PriorityLogMsg("exception", msg, e1.Message);
				wrd = new WebRoleData(testing: testing, test_id: test_id);
			}
			finally
			{
				if (wrd.GetType() != typeof(WebRoleData))
				{
					var msg = "_reload: cannot recreate webrole data!";
					GenUtils.PriorityLogMsg("exception", msg, null);
				}
			}

			try
			{
				GenUtils.LogMsg("info", "_reload: registering routes", null);
				RouteTable.Routes.Clear();
				ElmcityApp.RegisterRoutes(RouteTable.Routes, wrd);
				//RouteDebug.RouteDebugger.RewriteRoutesForTesting(RouteTable.Routes);
				GenUtils.LogMsg("info", "_reload: registered routes", null);
			}
			catch (Exception e2)
			{
				var msg = "_reload: registering routes";
				GenUtils.PriorityLogMsg("exception", msg, e2.Message, ts);
			}

			GenUtils.LogMsg("info", "webrole _reload stop", null);
		}

		// don't allow log requests to reach back more than 1000 minutes
		// there are simpler ways, but this is here as an example to myself of how to do 
		// custom constraints.
		public class LogMinutesConstraint : IRouteConstraint
		{
			public bool Match(System.Web.HttpContextBase httpContext, Route route, string parameterName, RouteValueDictionary values, RouteDirection routeDirection)
			{
				if ((routeDirection == RouteDirection.IncomingRequest) && (parameterName.ToLower(CultureInfo.InvariantCulture) == "minutes"))
				{
					try
					{
						int minutes = Convert.ToInt32(values["minutes"]);
						if (minutes < 1 || minutes > 1000)
							return false;
					}
					catch
					{
						return false;
					}
				}
				return true;
			}
		}

	}
}