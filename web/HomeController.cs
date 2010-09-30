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
using System.Web;
using System.Web.Mvc;
using WebRole;
using System.Timers;
using System.Text;
using CalendarAggregator;
using ElmcityUtils;

namespace WebRole.Controllers
{

    public class HomeController : ElmcityController
    {
        TableStorage ts = TableStorage.MakeDefaultTableStorage();
        BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
        Delicious delicious = Delicious.MakeDefaultDelicious();

        public HomeController()
        {
            while (ElmcityApp.loaded == false)
            {
                ElmcityApp.logger.LogMsg("info", "HomeController", "waiting for reload");
                Utils.Wait(5);
            }
        }

        [OutputCache(Duration = CalendarAggregator.Configurator.home_page_output_cache_duration, VaryByParam = "None")]
        public ActionResult index()
        {
            ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
            ViewData["title"] = ElmcityApp.pagetitle;
            ViewData["where_summary"] = make_where_summary();
            ViewData["what_summary"] = make_what_summary();
            ViewData["version"] = ElmcityApp.version;
            return View();
        }

        [OutputCache(Duration = CalendarAggregator.Configurator.home_page_output_cache_duration, VaryByParam = "None")]
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

        public ActionResult py(string arg1, string arg2, string arg3)
        {
            ElmcityApp.logger.LogHttpRequest(this.ControllerContext);
            if ( this.AuthenticateAsSelf() )
            {
            var script_url = "http://elmcity.blob.core.windows.net/admin/_generic.py";
            var args = new List<string>() { arg1, arg2, arg3 };
            ViewData["result"] = PythonUtils.RunIronPython(script_url, args);
            return View();
            }
            else
            {
             ViewData["result"] = "not authenticated";
             return View();
            }
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
                ElmcityApp.logger.LogMsg("exception", "HomeController reload", ex.Message + ex.StackTrace);
            }
            return View();
        }

		public ActionResult delicious_check(string id)
		{
			ViewData["result"] = Delicious.DeliciousCheck(id);
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
            foreach (var id in ElmcityApp.where_ids)
            {
                if (IsReady(id) == false)
                    continue;
                var metadict = ElmcityApp.calinfos[id].metadict;
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
            foreach (var id in ElmcityApp.what_ids)
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

        private static bool IsReady(string id)
        {
            return ElmcityApp.ready_ids.Contains(id);
        }

    }
}


