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
using System.Web.Caching;
using System.Web.Mvc;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using WebRole;
using ElmcityUtils;
using CalendarAggregator;
using System.Net;
using System.Text.RegularExpressions;
using System.Collections.Specialized;

namespace WebRole
{
    public class ServicesController : ElmcityController
    {
        private static UTF8Encoding UTF8 = new UTF8Encoding(false);

        private static TableStorage ts = TableStorage.MakeDefaultTableStorage();

        private static List<string> cacheable_types = new List<string>() { "ics", "search", "stats" };

        #region events

        //[OutputCache(Duration = Configurator.services_output_cache_duration, VaryByParam="*")]
        public ActionResult GetEvents(string id, string type, string view, string jsonp, string count)
        {
            HttpUtils.LogHttpRequest(this.ControllerContext);

            if (view != null)
                view = view.ToLower();

            EventsResult r = null;
            try
            {
                var cr = ElmcityApp.renderers[id];
                r = new EventsResult(this.ControllerContext, cr, id, type, view, jsonp, count);
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "GetEvents: " + id, e.Message);
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

            CalendarRenderer.ViewRenderer renderer = null;
            string response_body = null;
            byte[] response_bytes = new byte[0];

            public EventsResult(ControllerContext context, CalendarRenderer cr, string id, string type, string view, string jsonp, string count)
            {
                this.context = context;
                this.cr = cr;
                this.cr.cache = new ElmcityUtils.AspNetCache(context.HttpContext.Cache);
                this.id = id;
                this.type = type;
                this.view = view;
                this.jsonp = jsonp;
                this.count = (count == null) ? 0 : Convert.ToInt16(count);
            }

            public override void ExecuteResult(ControllerContext context)
            {

                // for dynamic views derived from the static file 
                // which is the base object for this id, e.g.:
                //  http://elmcity.blob.core.windows.net/a2cal/a2cal.zoneless.obj
                // cache the base object if uncached
                var base_key = Utils.MakeBaseUrl(this.id);
                if (this.cr.cache[base_key] == null)
                {
                    var bytes = HttpUtils.FetchUrl(new Uri(base_key)).bytes;
                    InsertIntoCache(bytes, ElmcityUtils.Configurator.cache_sliding_expiration, dependency: null, key: base_key);
                }

               // uri for static content, e.g.:
               // http://elmcity.blob.core.windows.net/a2cal/a2cal.stats.html
               // http://elmcity.blob.core.windows.net/a2cal/a2cal.search.html
               // this generated files could be served using their blob urls, but 
               // here are repackaged into the /services namespace, e.g.:
               // http://elmcity.cloudapp.net/services/a2cal/stats
               // http://elmcity.cloudapp.net/services/a2cal/search

               var blob_uri = BlobStorage.MakeAzureBlobUri(this.id, this.id + "." + this.type);

               // cache static content
               var blob_key = blob_uri.ToString();
               if ( cacheable_types.Exists(t => t == this.type) && this.cr.cache[blob_key] == null)
               {
                   var bytes = HttpUtils.FetchUrl(blob_uri).bytes;
                   var dependency = new ElmcityCacheDependency(base_key);
                   InsertIntoCache(bytes, ElmcityUtils.Configurator.cache_sliding_expiration, dependency: dependency, key: blob_key);
               }

                var view_key = Utils.MakeViewKey(this.id, this.type, this.view, this.count.ToString());
       
                switch (this.type)
                {
                    case "html":
                        this.renderer = new CalendarRenderer.ViewRenderer(cr.RenderHtml);
                        MaybeCacheView(view_key, this.renderer, new ElmcityCacheDependency(base_key));
                        this.response_body = cr.RenderDynamicViewWithCaching(context, view_key, this.renderer, this.view, this.count);
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
                        this.response_body = cr.RenderDynamicViewWithCaching(context, view_key, this.renderer, this.view, this.count);
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
                        this.response_body = cr.RenderDynamicViewWithCaching(context, view_key, this.renderer, this.view, this.count);

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
                        string jcontent = cr.RenderDynamicViewWithCaching(context, view_key, this.renderer, this.view, this.count);
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
                        blob_uri = BlobStorage.MakeAzureBlobUri(this.id, this.id + ".stats.html");
                        this.response_bytes = (byte[])CacheUtils.RetrieveBlobAndEtagFromServerCacheOrUri(this.cr.cache, blob_uri)["response_body"];
                        new ContentResult
                        {
                            ContentEncoding = UTF8,
                            ContentType = "text/html",
                            Content = Encoding.UTF8.GetString(this.response_bytes),
                        }.ExecuteResult(context);
                        break;

                    case "ics":
                        this.response_bytes = (byte[])CacheUtils.RetrieveBlobAndEtagFromServerCacheOrUri(this.cr.cache, blob_uri)["response_body"];
                        new ContentResult
                        {
                            ContentType = "text/calendar",
                            // todo: implement cr.RenderICS(string view)
                            // for now, return whole ICS file
                            Content = Encoding.UTF8.GetString(this.response_bytes),
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
                        blob_uri = BlobStorage.MakeAzureBlobUri(this.id, this.id + ".search.html");
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

            private void InsertIntoCache(byte[] bytes, TimeSpan sliding_expiration, CacheDependency dependency, string key)
            {
                var logger = new CacheItemRemovedCallback(AspNetCache.LogRemovedItemToAzure);
                this.cr.cache.Insert(key, bytes, dependency, Cache.NoAbsoluteExpiration, sliding_expiration, CacheItemPriority.Normal, logger);
            }

            private void MaybeCacheView(string view_key, CalendarRenderer.ViewRenderer view_renderer, CacheDependency dependency)
            {
                if (this.cr.cache[view_key] == null)
                {
                    var view_str = this.cr.RenderDynamicViewWithoutCaching(this.context, view_renderer, this.view, this.count);
                    byte[] view_bytes = Encoding.UTF8.GetBytes(view_str);
                    InsertIntoCache(view_bytes, ElmcityUtils.Configurator.cache_sliding_expiration, dependency, view_key);
                }
            }

        }

        #endregion

        #region logentries

        public ActionResult GetLogEntries(string id, string minutes)
        {
            HttpUtils.LogHttpRequest(this.ControllerContext);

            LogEntriesResult r = null;

            try
            {
                r = new LogEntriesResult(id, minutes);
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "GetLogEntries: " + id, e.Message);
            }
            return r;
        }

        public class LogEntriesResult : ActionResult
        {
            string id;
            int minutes;

            public LogEntriesResult(string id, string minutes)
            {
                this.id = id;
                this.minutes = Convert.ToInt16(minutes);
            }

            public override void ExecuteResult(ControllerContext context)
            {
                // it can take a while to fetch a large result
                context.HttpContext.Server.ScriptTimeout = CalendarAggregator.Configurator.webrole_script_timeout_seconds;
                new ContentResult
                {
                    ContentType = "text/plain",
                    Content = Utils.GetRecentLogEntries(this.minutes, this.id),
                    ContentEncoding = UTF8
                }.ExecuteResult(context);
            }
        }

        #endregion

        #region metadata

        public ActionResult GetMetadata(string id)
        {
            HttpUtils.LogHttpRequest(this.ControllerContext);

            MetadataResult r = null;

            try
            {
                r = new MetadataResult(id);
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "GetMetadata: " + id, e.Message);
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
                new ContentResult
                {
                    ContentType = "text/plain",
                    Content = Utils.GetMetadataForId(id),
                    ContentEncoding = UTF8
                }.ExecuteResult(context);
            }
        }

        #endregion

        #region fusecal

        [OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration, VaryByParam = "*")]
        public ActionResult GetFusecalICS(string url, string filter, string tz_source, string tz_dest)
        {
            HttpUtils.LogHttpRequest(this.ControllerContext);

            FusecalICSResult r = null;

            try
            {
                r = new FusecalICSResult(url, filter, tz_source, tz_dest);
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "GetFusecalICS: " + url, e.Message);
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
                var ics = PythonUtils.RunIronPython(CalendarAggregator.Configurator.fusecal_dispatcher, args);
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
            HttpUtils.LogHttpRequest(this.ControllerContext);
           
            if (!this.AuthenticateAsSelf())
                return new EmptyResult();

            RemoveCacheEntryResult r = null;

            try
            {
                r = new RemoveCacheEntryResult(this.ControllerContext, cached_uri);
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "RemoveCacheEntry: " + cached_uri, e.Message);
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
            HttpUtils.LogHttpRequest(this.ControllerContext);

            ViewCacheResult r = null;

            try
            {
                r = new ViewCacheResult(this.ControllerContext);
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "ViewCache", e.Message);
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

        [OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration, VaryByParam = "*")]
        public ActionResult GetArraData(string state, string town, string year, string quarter)
        {
            HttpUtils.LogHttpRequest(this.ControllerContext);

            ArraResult r = null;

            try
            {
                r = new ArraResult(state, town, year, quarter);
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "GetArraData: " + state + ' ' + town, e.Message);
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

        [OutputCache(Duration = CalendarAggregator.Configurator.services_output_cache_duration, VaryByParam = "*")]
        public ActionResult GetODataFeed(string table, string query, string since_hours_ago)
        {
            HttpUtils.LogHttpRequest(this.ControllerContext);

            ODataFeedResult r = null;

            try
            {
                r = new ODataFeedResult(table, query, since_hours_ago);
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "GetODataFeed: " + table + ": " + query, e.Message);
            }
            return r;
        }

        public class ODataFeedResult : ActionResult
        {
            string table;
            string query;
            TableStorage ts = TableStorage.MakeDefaultTableStorage();

            public ODataFeedResult(string table, string query, string since_hours_ago)
            {
                this.table = table;
                int hours_ago;
                if ( String.IsNullOrEmpty(since_hours_ago) )
                  hours_ago = CalendarAggregator.Configurator.odata_since_hours_ago;
                else
                  hours_ago = Convert.ToInt32(since_hours_ago);
                var since = DateTime.UtcNow - new TimeSpan(hours_ago, 0, 0);
                var final_query = string.Format("PartitionKey eq '{0}' and RowKey gt '{1}'", table, since.Ticks);
                if (String.IsNullOrEmpty(query) == false)
                    final_query += " and " + query;
                this.query = string.Format("$filter=({0})", final_query);
            }

            public override void ExecuteResult(ControllerContext context)
            {
                var content = this.ts.QueryEntitiesAsFeed(this.table, this.query);
                new ContentResult
                {
                    ContentType = "application/atom+xml",
                    Content = content,
                    ContentEncoding = UTF8
                }.ExecuteResult(context);
            }

        }

        #endregion

    }

}

