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
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Timers;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.Caching;
using CalendarAggregator;
using ElmcityUtils;
using System.Net;
using Newtonsoft.Json;

namespace WebRole
{
    public class ElmcityController : Controller
    {
        public static UTF8Encoding UTF8 = new UTF8Encoding(false);
        public static string procname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        public static int procid = System.Diagnostics.Process.GetCurrentProcess().Id;
        public static string domain_name = AppDomain.CurrentDomain.FriendlyName;
        public static int thread_id = System.Threading.Thread.CurrentThread.ManagedThreadId;

        private TableStorage ts = TableStorage.MakeDefaultTableStorage();

        public static Dictionary<string, string> settings = GenUtils.GetSettingsFromAzureTable();

        public static Dictionary<string, int> smartphone_screen_dimensions = Utils.GetSmartPhoneScreenDimensions();

        public static Dictionary<string, Dictionary<string, string>> themes = new Dictionary<string, Dictionary<string, string>>();

        // last-resort exception handler
        protected override void OnException(ExceptionContext filterContext)
        {
            var msg = String.Format("url: {0}, message: {1}", filterContext.HttpContext.Request.Url.ToString(), filterContext.Exception.Message);
            GenUtils.PriorityLogMsg("exception", "last chance", msg + filterContext.Exception.StackTrace);
            if (msg.Length > 140)
                msg = msg.Substring(0, 140);
            TwitterApi.SendTwitterDirectMessage(CalendarAggregator.Configurator.twitter_account, "last chance: " + msg);
            filterContext.ExceptionHandled = true;
            this.View("FinalError").ExecuteResult(this.ControllerContext);
        }

        #region foreign auth

        public string Authenticated()
        {
            foreach (var auth in Authentications.AuthenticationList)
            {
                if (auth.AuthenticatedVia(this.Request) != null)
                    return auth.mode.ToString();
            }
            return null;
        }

        public string Authenticated(string id)
        {
            foreach (var auth in Authentications.AuthenticationList)
            {
                if (auth.AuthenticatedVia(this.Request, id) != null)
                    return auth.mode.ToString();
            }
            return null;
        }

        #endregion

        public ElmcityController()
        {
        }

        public void MaybeCacheDependentObject(string url, string base_key)
        {
            if (this.HttpContext.Cache[url] == null)
            {
                var bytes = HttpUtils.FetchUrlNoCache(new Uri(url)).bytes;
                var dependency = new ElmcityCacheDependency(base_key);
                var cache = new AspNetCache(this.HttpContext.Cache);
                InsertIntoCache(cache, bytes, dependency, url);
            }
        }

        public void InsertIntoCache(AspNetCache cache, byte[] bytes, ElmcityCacheDependency dependency, string key)
        {
            var logger = new CacheItemRemovedCallback(AspNetCache.LogRemovedItemToAzure);
            var expiration_hours = ElmcityUtils.Configurator.cache_sliding_expiration.Hours;
            var sliding_expiration = new TimeSpan(expiration_hours, 0, 0);
            cache.Insert(key, bytes, dependency, Cache.NoAbsoluteExpiration, sliding_expiration, CacheItemPriority.Normal, logger);
        }

        public byte[] GetObjectFromCacheOrWeb(AspNetCache cache, string key, string url)
        {
            byte[] data;
            try
            {
                var object_is_cached = cache[key] != null;
                if (object_is_cached)
                    data = (byte[])cache[key];
                else
                {
                    var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
                    var response = HttpUtils.RetryHttpRequestExpectingStatus(request, HttpStatusCode.OK, data: null, wait_secs: 3, max_tries: 3, timeout_secs: TimeSpan.FromSeconds(10));
                    if (response.status != HttpStatusCode.OK)
                        GenUtils.PriorityLogMsg("exception", "GetObjectFromCacheOrWeb: " + url, response.status.ToString());
                    data = CacheUtils.MaybeSuppressResponseBodyForView(this.ControllerContext, response.bytes);
                }
            }
            catch (Exception e)
            {
                GenUtils.PriorityLogMsg("exception", "GetObjectFromCacheOrWeb: " + key + ", " + url, e.Message + e.StackTrace);
                data = Encoding.UTF8.GetBytes(e.Message);
            }
            return data;
        }


    }

    public class ElmcityApp : HttpApplication
    {
        public static string version = "2471";

        public static string procname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        public static int procid = System.Diagnostics.Process.GetCurrentProcess().Id;
        public static string domain_name = AppDomain.CurrentDomain.FriendlyName;
        public static int thread_id = System.Threading.Thread.CurrentThread.ManagedThreadId;

        private static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();

        public static Logger logger = new Logger();

        public static string pagetitle = "the elmcity project";

        public static ElmcityController home_controller;
        public static ElmcityController services_controller;

        private static TableStorage ts = TableStorage.MakeDefaultTableStorage();

        // public static OAuthTwitter oauth_twitter = new OAuthTwitter(consumer_key: ElmcityController.settings["twitter_auth_consumer_key"], consumer_secret: ElmcityController.settings["twitter_auth_consumer_secret"]);

        public static WebRoleData wrd = new WebRoleData();

        public static string get_events_param_types = "html|xml|json|ics|rss|tags_json|stats|tags_html|jswidget|today_as_html";

        public ElmcityApp()
        {
            GenUtils.LogMsg("info", String.Format("ElmcityApp {0} {1} {2} {3}", procname, procid, domain_name, thread_id), null);
        }

        public static void RegisterRoutes(RouteCollection routes, WebRoleData wrd)
        {
            GenUtils.LogMsg("info", "RegisterRoutes", "ready_ids: " + wrd.ready_ids.Count());

            #region HomeController

            routes.MapRoute(
                "add_to_cal",
                "add_to_cal",
                new { controller = "Home", action = "add_to_cal" }
                );

            routes.MapRoute(
                "add_fb_event",
                "add_fb_event",
                new { controller = "Home", action = "add_fb_event" }
                );

            routes.MapRoute(
                "description_from_title_and_dtstart",
                "{id}/description_from_title_and_dtstart",
                new { controller = "Home", action = "description_from_title_and_dtstart" },
                new { id = wrd.str_ready_ids }
                );

            routes.MapRoute(
                "DiscardMisfoldedDescriptionsAndBogusCategoriesThenAddEasternVTIMEZONE",
                "DiscardMisfoldedDescriptionsAndBogusCategoriesThenAddEasternVTIMEZONE",
                new { controller = "Home", action = "DiscardMisfoldedDescriptionsAndBogusCategoriesThenAddEasternVTIMEZONE" }
                );

            routes.MapRoute(
                "facebook_auth",
                "facebook_auth",
                new { controller = "Home", action = "facebook_auth" }
                );

            routes.MapRoute(
                "feed2json",
                "{id}/feed2json",
                new { controller = "Home", action = "feed2json" },
                new { id = wrd.str_ready_ids }
                );

            routes.MapRoute(
                "get_css_theme",
                "get_css_theme",
                new { controller = "Home", action = "get_css_theme" }
                );

            /*
            // target for facebook oauth redirect, used periodically to refresh fb api access token
            todo: revisit in light of fb offline_access transition
            routes.MapRoute(
                "fb_access",
                "fb_access",
                new { controller = "Home", action = "fb_access" }
                );
             */

            routes.MapRoute(
                "get_editable_metadata",
                "services/{id}/get_editable_metadata",
                new { controller = "Home", action = "get_editable_metadata" },
                new { id = wrd.str_ready_ids }
            );

            routes.MapRoute(
                "get_fb_ical_url",
                "get_fb_ical_url",
                new { controller = "Home", action = "get_fb_ical_url" }
            );

            routes.MapRoute(
                "get_csv_ical_url",
                "get_csv_ical_url",
                new { controller = "Home", action = "get_csv_ical_url" }
            );

            routes.MapRoute(
                "get_high_school_sports_url",
                "get_high_school_sports_url/{school}/{tz}",
                new { controller = "Home", action = "get_high_school_sports_url" },
                new { tz = "ET|CT|MT|PT|AT|HT" }
            );

            routes.MapRoute(
                "get_ics_to_ics_ical_url",
                "get_ics_to_ics_ical_url",
                new { controller = "Home", action = "get_ics_to_ics_ical_url" }
            );

            routes.MapRoute(
                "get_rss_xcal_ical_url",
                "get_rss_xcal_ical_url",
                new { controller = "Home", action = "get_rss_xcal_ical_url" }
            );

            routes.MapRoute(
                "get_ical_url_from_eventbrite_event_page",
                "get_ical_url_from_eventbrite_event_page",
                new { controller = "Home", action = "get_ical_url_from_eventbrite_event_page" }
            );

            routes.MapRoute(
                "get_ical_url_from_eid_of_eventbrite_event_page",
                "get_ical_url_from_eid_of_eventbrite_event_page",
                new { controller = "Home", action = "get_ical_url_from_eid_of_eventbrite_event_page" }
            );

            routes.MapRoute(
                "get_json_metadata",
                "services/{id}/get_json_metadata",
                new { controller = "Home", action = "get_json_metadata" },
                new { id = wrd.str_ready_ids }
            );

            routes.MapRoute(
                "google_auth",
                "google_auth",
                new { controller = "Home", action = "google_auth" }
                );

            routes.MapRoute(
                "home",
                "",
                new { controller = "Home", action = "index" }
            );

            routes.MapRoute(
                "events3",
                "{id}",
                new { controller = "Services", action = "GetEvents", type = "html" },
                new { id = wrd.str_ready_ids }
                );

            routes.MapRoute(
                "about",
                "{id}/about",
                new { controller = "Home", action = "about" },
                new { id = wrd.str_ready_ids }
                );

            // parse csv, return ics
            routes.MapRoute(
                "ics_from_csv",
                "ics_from_csv",
                 new { controller = "Home", action = "ics_from_csv" }
                );

            // return ics filtered by after, before, include_keyword, or exclude_keyword
            routes.MapRoute(
                "ics_from_ics",
                "ics_from_ics",
                 new { controller = "Home", action = "ics_from_ics" }
                );

            // parse facebook page events, return ics
            routes.MapRoute(
                "ics_from_fb_page",
                "ics_from_fb_page",
                 new { controller = "Home", action = "ics_from_fb_page" }
                );

            // ical from a lastfm venue id
            routes.MapRoute(
                "ics_from_lastfm_venue",
                "ics_from_lastfm_venue",
                 new { controller = "Home", action = "ics_from_lastfm_venue" }
                );

            // parse atom+vcal, return ics
            routes.MapRoute(
                "ics_from_vcal",
                "ics_from_vcal",
                 new { controller = "Home", action = "ics_from_vcal" }
                );

            // search eventbrite by name and location, return ics feed
            routes.MapRoute(
                "ics_from_eventbrite_organizer",
                "ics_from_eventbrite_organizer",
                 new { controller = "Home", action = "ics_from_eventbrite_organizer" }
                );

            // list eventbrite events by organizer, return ics feed
            routes.MapRoute(
                "ics_from_eventbrite_organizer_id",
                "ics_from_eventbrite_organizer_id",
                 new { controller = "Home", action = "ics_from_eventbrite_organizer_id" }
                );

            // get an ical for a single eventbrite id
            routes.MapRoute(
                "ics_from_eventbrite_eid",
                "ics_from_eventbrite_eid",
                 new { controller = "Home", action = "ics_from_eventbrite_eid" }
                );

            // parse rss+xcal, return ics
            routes.MapRoute(
                "ics_from_xcal",
                "ics_from_xcal",
                 new { controller = "Home", action = "ics_from_xcal" }
                );

            routes.MapRoute(
                "ics_from_findlocal",
                "ics_from_findlocal",
                 new { controller = "Home", action = "ics_from_findlocal" }
                );

            routes.MapRoute(
                "keep_only_vevents",
                "keep_only_vevents",
                new { controller = "Home", action = "keep_only_vevents" }
                );

            routes.MapRoute(
                "live_auth",
                "live_auth",
                new { controller = "Home", action = "live_auth" }
                );


            // visualize changes between two json snapshots (flavor feeds is list of dicts, flavor metadata is single dict)
            // http://elmcity.cloudapp.net/services/elmcity/meta_history?id=elmcity&flavor=feeds&a_name=elmcity.2011.07.25.00.56.feeds.json&b_name=elmcity.2011.07.25.10.00.feeds.json
            // http://elmcity.cloudapp.net/services/elmcity/meta_history?id=elmcity&flavor=metadata&a_name=elmcity.2011.07.25.00.56.metadata.json&b_name=elmcity.2011.07.25.10.00.metadata.json
            routes.MapRoute(
                "meta_history",
                "services/{id}/meta_history",
                new { controller = "Home", action = "meta_history" },
                new { id = wrd.str_ready_ids }
                );

            // events happening now + offset
            routes.MapRoute(
                "soon",
                "{id}/{type}/soon",
                new { controller = "Home", action = "soon" },
                new { id = wrd.str_ready_ids, type = "html|rss|ics|xml|json" }
                );

            routes.MapRoute(
                "put_json_metadata",
                "services/{id}/put_json_metadata",
                new { controller = "Home", action = "put_json_metadata" },
                new { id = wrd.str_ready_ids }
            );

            routes.MapRoute(
                "put_json_feeds",
                "services/{id}/put_json_feeds",
                new { controller = "Home", action = "put_json_feeds" },
                new { id = wrd.str_ready_ids }
            );


            // dump a snapshot of diagnostic data
            routes.MapRoute(
                "snapshot",
                "snapshot",
                new { controller = "Home", action = "snapshot" }
                 );

            // query the query-safe tables
            routes.MapRoute(
                "table_query",
                "table_query/{table}",
                new { controller = "Home", action = "table_query" },
                new { table = ElmcityController.settings["query_safe_tables"] }
            );

            // get the static json tag cloud
            routes.MapRoute(
                "tag_cloud",
                "{id}/tag_cloud",
                new { controller = "Home", action = "tag_cloud" },
                new { id = wrd.str_ready_ids }
            );

            // fetch plain text of an ical feed, optionally grepped by property
            routes.MapRoute(
                "text_from_ics",
                "text_from_ics",
                new { controller = "Home", action = "text_from_ics" }
            );

            // url helpers page
            routes.MapRoute(
                "url_helpers",
                "url_helpers",
                new { controller = "Home", action = "url_helpers" }
                );

            // alias
            routes.MapRoute(
                "helpers",
                "helpers",
                new { controller = "Home", action = "helpers" }
                );

            routes.MapRoute(
                "twitter_auth",
                "twitter_auth",
                new { controller = "Home", action = "twitter_auth" }
                );


            routes.MapRoute(
                "view_calendar",
                "view_calendar",
                new { controller = "Home", action = "view_calendar" }
                );

            routes.MapRoute(
                "welcome",
                "welcome",
                new { controller = "Home", action = "welcome" }
                );


            #endregion

            #region ServicesController

            // this pattern covers most uses. gets events for a given hub id in many formats. allows
            // only the specified formats, and only hub ids that are "ready"
            routes.MapRoute(
                "events",
                "services/{id}/{type}",
                new { controller = "Services", action = "GetEvents" },
                new { id = wrd.str_ready_ids, type = get_events_param_types }
                );

            // also allow bare id/type 
            routes.MapRoute(
                "events2",
                "{id}/{type}",
                new { controller = "Services", action = "GetEvents" },
                new { id = wrd.str_ready_ids, type = get_events_param_types }
                );


            routes.MapRoute(
                "logs",
                "logs",
                new { controller = "Services", action = "GetLogEntries" }
              );

            // dump the hub's metadata, extended with computed values,
            routes.MapRoute(
                "metadata",
                "services/{id}/metadata",
                new { controller = "Services", action = "GetMetadata" },
                new { id = wrd.str_ready_ids }
                );

            // alternate at root
            routes.MapRoute(
                "metadata2",
                "{id}/metadata",
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

            #endregion

            GenUtils.LogMsg("info", routes.Count() + " routes", null);

        }

        protected void Application_Start()
        {
            var msg = "WebRole: Application_Start";
            GenUtils.PriorityLogMsg("info", msg, null);

            Utils.ScheduleTimer(PurgeCache, CalendarAggregator.Configurator.webrole_cache_purge_interval_minutes, name: "PurgeCache", startnow: false);
            Utils.ScheduleTimer(ReloadSettingsAndRoutes, minutes: CalendarAggregator.Configurator.webrole_reload_interval_minutes, name: "ReloadSettingsAndRoutes", startnow: true);
            Utils.ScheduleTimer(MakeTablesAndCharts, minutes: CalendarAggregator.Configurator.web_make_tables_and_charts_interval_minutes, name: "MakeTablesAndCharts", startnow: false);
            ElmcityUtils.Monitor.TryStartMonitor(CalendarAggregator.Configurator.process_monitor_interval_minutes, CalendarAggregator.Configurator.process_monitor_table);
        }

        // encapsulate _reload with the signature needed by Utils.ScheduleTimer
        public static void ReloadSettingsAndRoutes(Object o, ElapsedEventArgs e)
        {
            _ReloadSettingsAndRoutes();
        }

        public static void _ReloadSettingsAndRoutes()
        {
            GenUtils.LogMsg("info", "webrole _ReloadRoutes", null);

            bool new_routes = false;

            try
            {
                ElmcityController.settings = GenUtils.GetSettingsFromAzureTable();
            }
            catch (Exception e0)
            {
                var msg = "_ReloadSettingsAndRoutes: settings";
                GenUtils.PriorityLogMsg("exception", msg, e0.Message);
            }

            try
            {
                var _wrd = WebRoleData.GetWrd();
                if (_wrd.ready_ids.Count != ElmcityApp.wrd.ready_ids.Count)  // did # of hubs change?
                {
                    new_routes = true;                                       // force rebuild of route map
                    GenUtils.LogMsg("info", "Reload: found a new hub", null);
                    lock (ElmcityApp.wrd)
                    {
                        ElmcityApp.wrd = _wrd;                               // update WebRoleData
                    }
                }
                foreach (var id in ElmcityApp.wrd.ready_ids)                  // did any hub's CalendarRenderer.Calinfo change?
                {
                    var cached_calinfo = ElmcityApp.wrd.renderers[id].calinfo;
                    var current_calinfo = Utils.AcquireCalinfo(id);
                    var cached_dict = ObjectUtils.ObjToDictStr(cached_calinfo);
                    var current_dict = ObjectUtils.ObjToDictStr(current_calinfo);
                    if (ObjectUtils.DictStrEqualsDictStr(cached_dict, current_dict) == false)  // calinfo.obj on blob store is different
                    {
                        GenUtils.LogMsg("info", "Reload: new calinfo+renderer for " + id, null);
                        lock (ElmcityApp.wrd)
                        {
                            var renderer = Utils.AcquireRenderer(id);                         // load new renderer, maybe custom for this hub
                            GenUtils.LogMsg("info", "renderer.calinfo.version_description: " + renderer.calinfo.version_description, null);
                            ElmcityApp.wrd.renderers[id] = renderer;                          // update the renderer (and its calinfo)
                            var cache = new AspNetCache(ElmcityApp.home_controller.HttpContext.Cache);
                            var url = Utils.MakeBaseZonelessUrl(id);
                            cache.Remove(url);                                               // flush cached objects for id
                            var obj = HttpUtils.FetchUrl(new Uri(url));						// rewarm cache
                        }
                    }
                }
            }
            catch (Exception e1)
            {
                GenUtils.PriorityLogMsg("exception", "_ReloadSettingsAndRoutes: cannot check/update wrd", e1.Message);
                try
                {
                    var __wrd = ElmcityApp.wrd = WebRoleData.MakeWebRoleData();
                    lock (ElmcityApp.wrd)
                    {
                        ElmcityApp.wrd = __wrd;
                    }
                }
                catch (Exception e2)
                {
                    GenUtils.PriorityLogMsg("exception", "_ReloadSettingsAndRoutes: cannot remake wrd", e2.Message);
                }
            }


            try
            {
                var themes = Utils.GetThemesDict();
                if (ObjectUtils.DictOfDictStrEqualsDictOfDictStr(themes, ElmcityController.themes) == false)
                {
                    lock (ElmcityController.themes)
                    {
                        ElmcityController.themes = themes;
                    }
                }
            }
            catch (Exception e2)
            {
                var msg = "_ReloadSettingsAndRoutes: themes";
                GenUtils.PriorityLogMsg("exception", msg, e2.Message);
            }

            if (new_routes)
            {
                try
                {
                    lock (RouteTable.Routes)
                    {
                        RouteTable.Routes.Clear();
                        ElmcityApp.RegisterRoutes(RouteTable.Routes, ElmcityApp.wrd);
                        // RouteDebug.RouteDebugger.RewriteRoutesForTesting(RouteTable.Routes);
                    }
                }
                catch (Exception e3)
                {
                    GenUtils.PriorityLogMsg("exception", "_ReloadSettingsAndRoutes: registering routes", e3.Message);
                }
            }

        }

        public static void PurgeCache(Object o, ElapsedEventArgs e)
        {
            try
            {
                var cache = new AspNetCache(ElmcityApp.home_controller.HttpContext.Cache);
                ElmcityUtils.CacheUtils.MaybePurgeCache(cache);
            }
            catch (Exception ex)
            {
                GenUtils.PriorityLogMsg("exception", "PurgeCache", ex.Message);
            }

        }

        public static void EmptyCache()
        {
            try
            {
                var cache = ElmcityApp.home_controller.HttpContext.Cache;
                var e = cache.GetEnumerator();
                while (e.MoveNext())
                {
                    var item = e.Current.ToString();
                    cache.Remove(item);
                }
            }
            catch (Exception ex)
            {
                GenUtils.PriorityLogMsg("exception", "PurgeCache", ex.Message);
            }

        }

        public static void MakeTablesAndCharts(Object o, ElapsedEventArgs e)
        {
            GenUtils.LogMsg("info", "MakeTablesAndCharts", null);
            try
            {
                PythonUtils.RunIronPython(WebRole.local_storage_path, CalendarAggregator.Configurator.charts_and_tables_script_url, new List<string>() { "", "", "" });
            }
            catch (Exception ex)
            {
                GenUtils.PriorityLogMsg("exception", "MonitorAdmin", ex.Message + ex.StackTrace);
            }
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


////   

/*
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Routing;
using ElmcityUtils;
 */

/*
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Timers;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
//using System.Web.Caching;
using CalendarAggregator;
using ElmcityUtils;
using System.Net;
using Newtonsoft.Json;

namespace WebRole
{
    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801
    public class MvcApplication : System.Web.HttpApplication
    {
       protected void Application_Start()
        {
            GenUtils.PriorityLogMsg("DEBUG", "WebRole: Application_Start", "MVC4");

            AreaRegistration.RegisterAllAreas();

            RouteConfig.RegisterRoutes(RouteTable.Routes);
        }
    }
}
*/