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
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using System.Timers;
using CalendarAggregator;
using WebRole;
using ElmcityUtils;
using System.Globalization;
using System.Threading;
using System.IO;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Diagnostics;

namespace WebRole
{
    public class ElmcityController : Controller
    {
        private TableStorage ts = TableStorage.MakeDefaultTableStorage();

		public static Dictionary<string, string> settings = GenUtils.GetSettingsFromAzureTable();

        // last-resort exception handler
        protected override void OnException(ExceptionContext filterContext)
        {
			var msg = filterContext.Exception.Message;
            ElmcityApp.logger.LogMsg("exception", "last chance", msg + filterContext.Exception.StackTrace);
			if (msg.Length > 140)
				msg = msg.Substring(0, 140);
			TwitterApi.SendTwitterDirectMessage(CalendarAggregator.Configurator.delicious_master_account, msg);
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
            var trusted_host_dicts = (List<Dictionary<string, object>>)this.ts.QueryEntities(tablename: "trustedhosts", query: query).response;
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

            if  ( trusted_addrs_list.Exists(addr => addr == incoming_addr) )
                return true;
            else
            {
                ElmcityApp.logger.LogMsg("warning", "AuthenticateAsSelf rejected " + incoming_addr, "trusted: " + String.Join(", ", trusted_addrs_list.ToArray()));
                return false;
            }
        }
    }

    public class ElmcityApp : HttpApplication
    {
#if false // true if testing, false if not testing
       private static bool testing = true;
       private static string test_id = "elmcity";
#else    // not testing
        private static bool testing = false;
        private static string test_id = "";
#endif

		public static Logger logger = new Logger();

        public static string version =  "952";

        public static string pagetitle = "the elmcity project";

        // on startup, and then periodically, a calinfo and a renderer is constructed for each hub
        public static Dictionary<string, Calinfo> calinfos;
		public static Dictionary<string, CalendarRenderer> renderers = new Dictionary<string, CalendarRenderer>();

        public static ElmcityUtils.Monitor monitor;        // gather/report diagnostic info

        public static List<string> where_ids;  
        public static List<string> what_ids;

        // on startup, and then periodically, this list of "ready" hubs is constructed
        // ready means that the hub has been added to the system, and there has been at 
        // least one successful aggregation run resulting in an output like:
        // http://elmcity.cloudapp.net/services/ID/html
         
        public static List<string> ready_ids = new List<string>();

        // the stringified version of the list controls the namespace, under /services, that the
        // service responds to. so when a new hub is added, say Peekskill, NY, with id peekskill, 
        // the /services/peekskill family of URLs won't become active until the hub joins the list of ready_ids
        public static string str_ready_ids;

        private static TableStorage ts = TableStorage.MakeDefaultTableStorage();
        private static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
        private static Delicious delicious = Delicious.MakeDefaultDelicious();

        public static bool loaded = false;

		public static OAuthTwitter oauth_twitter = new OAuthTwitter();

		//public static string twitter_oauth_access_token;
		//public static string twitter_oauth_access_token_secret;


        // encapsulate _reload with the signature needed by Utils.ScheduleTimer
        public static void reload(Object o, ElapsedEventArgs e)
        {
            _reload();
        }

        private static void _reload()
        {
            ElmcityApp.logger.LogMsg("info", "_reload", null);
            string current_id = "";

            try
            {
				ElmcityController.settings = GenUtils.GetSettingsFromAzureTable();

                if (testing) // reduce the list to a single test id
                {
                    calinfos = new Dictionary<string, Calinfo>();
                    calinfos.Add(test_id, new Calinfo(test_id));
                }
                else  // construct the full list of hubs
                    calinfos = CalendarAggregator.Configurator.Calinfos;

                where_ids = calinfos.Keys.ToList().FindAll(id => calinfos[id].hub_type == "where");
                var where_ids_as_str = string.Join(",", where_ids.ToArray());
                ElmcityApp.logger.LogMsg("info", "where_ids: " + where_ids_as_str, null);

                what_ids = calinfos.Keys.ToList().FindAll(id => calinfos[id].hub_type == "what");
                var what_ids_as_str = string.Join(",", what_ids.ToArray());
                ElmcityApp.logger.LogMsg("info", "what_ids: " + what_ids_as_str, null);

                where_ids.Sort((a, b) => calinfos[a].where.ToLower().CompareTo(calinfos[b].where.ToLower()));
                what_ids.Sort();

                foreach (var id in calinfos.Keys)
                {
                    ElmcityApp.logger.LogMsg("info", "_reload: readying: " + id, null);
                    current_id = id;

					// todo: move this to worker

                    try
                    {
                        var task = Scheduler.FetchTaskForId(id); // hub has already been processed once
                    }
                    catch (Exception e)                          // hub just added, never processed
                    {
                        ElmcityApp.logger.LogMsg("info", "creating task record for " + id, e.Message + e.StackTrace);
                        Scheduler.InitTaskForId(id);            
                    }

                    var calinfo = calinfos[id];
                    var cr = new CalendarRenderer(calinfo);

					if (renderers.ContainsKey(id))
						renderers[id] = cr;
					else
						renderers.Add(id, cr);

                    if (BlobStorage.ExistsBlob(id, id + ".html")) // there has been at least one aggregation
                        ready_ids.Add(id);
                }

                // this pipe-delimited string defines allowed IDs in the /services/ID/... URL pattern
                str_ready_ids = String.Join("|", ready_ids.ToArray());
                ElmcityApp.logger.LogMsg("info", "str_ready_ids: " + str_ready_ids, null);

                RouteTable.Routes.Clear();

                RegisterRoutes(RouteTable.Routes); // if a hub was acquired, /services/ID namespace will expand

				// RouteDebug.RouteDebugger.RewriteRoutesForTesting(RouteTable.Routes);

                monitor.ReloadCounters();

                if ( loaded == false )  // webrole is starting
                  loaded = true;        // ok for home controller to respond
            }

            catch (Exception e)
            {
                ElmcityApp.logger.LogMsg("exception", "_reload " + current_id, e.Message + e.StackTrace);
                Scheduler.InitTaskForId(current_id);
            }

        }

        public static void RegisterRoutes(RouteCollection routes)
        {
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

            // this pattern covers most uses. gets events for a given hub id in many formats. allows
            // only the specified formats, and only hub ids that are "ready"
            routes.MapRoute(
                "events",
                "services/{id}/{type}",
                new { controller = "Services", action = "GetEvents" },
                new { id = str_ready_ids, type = "html|xml|json|ics|rss|tags_json|stats|tags_html|jswidget|today_as_html|search" }
                );

            // for a bare url like /services/a2cal, emit a page that documents all the available formats
            routes.MapRoute(
                "hubfiles",
                "services/{id}/",
                new { controller = "Home", action = "hubfiles" },
                new { id = str_ready_ids }
                );

            // reach back {minutes} into the log table and spew entries since then
            routes.MapRoute(
                "logs",
                "logs/{id}/{minutes}",
                new { controller = "Services", action = "GetLogEntries" },
                new { id = "all|" + str_ready_ids, minutes = new LogMinutesConstraint() }
              );

            // dump the hub's metadata that was acquired from delicious, extended with computed values,
            // and cached to the azure metadata table
            routes.MapRoute(
                "metadata",
                "services/{id}/metadata",
                new { controller = "Services", action = "GetMetadata" },
                new { id = str_ready_ids }
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

        }

        delegate void AppStarterDelegate(); // for running AppStarter in the background

        // the home controller won't respond until setup is done
        protected void AppStarter()
        {
			var domain = Thread.GetDomain().FriendlyName;
			var thread_id = Thread.CurrentThread.ManagedThreadId;

			var info = String.Format("domain: {0}, thread_id: {1}", domain, thread_id);

			ElmcityApp.logger.LogMsg("info", "AppStarter: " + info, null);

            if (testing == false)
            {
				var local_resource = RoleEnvironment.GetLocalResource("LocalStorage1");
				GenUtils.LogMsg("info", "LocalStorage1", local_resource.RootPath);
				PythonUtils.InstallPythonStandardLibrary(local_resource.RootPath, ts);
                PythonUtils.InstallPythonElmcityLibrary(local_resource.RootPath, ts);
            }

            monitor = ElmcityUtils.Monitor.TryStartMonitor(CalendarAggregator.Configurator.process_monitor_interval_minutes, CalendarAggregator.Configurator.process_monitor_table);

            _reload();

            Utils.ScheduleTimer(reload, minutes: CalendarAggregator.Configurator.webrole_reload_interval_hours * 60, name: "reload", startnow: false);
        }

        protected void Application_Start()
        {
            var app_starter = new AppStarterDelegate(AppStarter);
            app_starter.BeginInvoke(null, null);
        }

        // don't allow log requests to reach back more than 500 minutes
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
                        if (minutes < 1 || minutes > 500)
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