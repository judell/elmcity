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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ElmcityUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CalendarAggregator
{
    // see http://blog.jonudell.net/2009/03/13/searching-for-calendar-information/
    public class SearchResult
    {
        public string url
        {
            get { return _url; }
            set { _url = value; }
        }
        private string _url;

        public string title
        {
            get { return _title; }
            set { _title = value; }
        }
        private string _title;

        public string content
        {
            get { return _content; }
            set { _content = value; }
        }
        private string _content;

        public string engine
        {
            get { return _engine; }
            set { _engine = value; }
        }
        private string _engine;

        public bool google
        {
            get { return _google; }
            set { _google = value; }
        }
        private bool _google;

        public bool live
        {
            get { return _live; }
            set { _live = value; }
        }
        private bool _live;

        public SearchResult(string url, string title, string content, string engine)
        {
            this.url = url;
            this.title = title;
            this.content = content;
            this.engine = engine;
            this.google = false;
            this.live = false;
        }
    }


    public static class Search
    {
        private static Dictionary<string, SearchResult> dict;
        private static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
        //private static TableStorage ts = TableStorage.make_default_tablestorage();

        private static List<string> qualifiers = new List<string>() 
            {
                "every",
                "first",
                "1st",
                "second",
                "2nd",
                "third",
                "3rd",
                "fourth",
                "4th", 
                "last",
            };

        private static List<string> days = new List<string>() 
            {
                "monday",
                "tuesday",
                "wednesday",
                "thursday",
                "friday",
                "saturday",
                "sunday"
            };

        public static void SearchLocation(string id, string where)
        {
            //Console.WriteLine("search_location: " + where);
            dict = new Dictionary<string, SearchResult>();
            foreach (var qualifier in qualifiers)
            {
                foreach (var day in days)
                {
                    string q;
                    try
                    {
                        q = string.Format(@" ""{0}"" ""{1} {2}"" ", where, qualifier, day);
                        GoogleSearch(q);
                    }
                    catch (Exception ex1)
                    {
                        GenUtils.LogMsg("exception", "search_location: google: " + where, ex1.Message);
                    }
                    try
                    {
                        q = string.Format(@" ""{0}"" near:100 ""{1} {2}"" ", where, qualifier, day);
                        LiveSearch(q);
                    }
                    catch (Exception ex2)
                    {
                        GenUtils.LogMsg("exception", "search_location: live: " + where, ex2.Message);
                    }
                }
            }

            var sb = new StringBuilder();
            sb.Append(@"<html><head><title>elmcity calendar finder</title></head><body style=""font-family:verdana,arial""><h1>possible sources of calendar information for " + where + "</h1>");
            var count = 0;
            var max = dict.Keys.Count();
            var cutoff = Convert.ToInt32(max - (max * 0.01));
            foreach (var url in dict.Keys)
            {
                count++;
                if (count > cutoff) break;
                var result = dict[url];
                string engine = "";
                if (result.google && result.live) engine = "google, live";
                if (result.google && !result.live) engine = "google";
                if (!result.google && result.live) engine = "live";
                sb.Append(string.Format("<p>{0}. <a href=\"{1}\">{2}</a><div>{3}</div>({4})</p>",
                    count,
                    result.url,
                    result.title,
                    result.content,
                    engine));
            }
            sb.Append("</body></html>");

            var html = sb.ToString();

            html = GenUtils.RegexReplace(html, "</*(em|strong|b|i|center)>", "");

            var data = Encoding.UTF8.GetBytes(html.ToString());

            bs.PutBlob(id, id + ".search.html", new Hashtable(), data, "text/html");
        }

        public static void LiveSearch(string query)
        {
            var url_template = "http://api.search.live.net/json.aspx?AppId=8F9BDD3C8AAD34D18AFE5099AE3894CE02BC1CF6&Market=en-US&Sources=Web&Adult=Strict&Query={0}&Web.Count=50";
            var offset_template = "&Web.Offset={1}";
            Uri search_url;
            int[] offsets = { 0, 50, 100, 150 };
            foreach (var offset in offsets)
            {
                if (offset == 0)
                    search_url = new Uri(string.Format(url_template, query));
                else
                    search_url = new Uri(string.Format(url_template + offset_template, query, offset));

                //Console.WriteLine(search_url);

                var page = HttpUtils.FetchUrl(search_url).DataAsString();
                JObject o = (JObject)JsonConvert.DeserializeObject(page);

                var results =
                    from result in o["SearchResponse"]["Web"]["Results"].Children()
                    select new SearchResult
                          (
                          result.Value<string>("Url").ToString(),
                          result.Value<string>("Title").ToString(),
                          result.Value<string>("Description").ToString(),
                          "live"
                          );

                //Console.WriteLine("Results: " + results.Count());

                Dictify(results);

            }
        }

        public static void GoogleSearch(string query)
        {
            var url_template = "http://ajax.googleapis.com/ajax/services/search/web?v=1.0&rsz=large&safe=active&q={0}&start={1}";
            Uri search_url;
            int[] offsets = { 0, 8, 16, 24, 32, 40, 48 };
            foreach (var offset in offsets)
            {
                search_url = new Uri(string.Format(url_template, query, offset));

                var page = HttpUtils.FetchUrl(search_url).DataAsString();
                JObject o = (JObject)JsonConvert.DeserializeObject(page);

                var results =
                    from result in o["responseData"]["results"].Children()
                    select new SearchResult(
                            result.Value<string>("url").ToString(),
                            result.Value<string>("title").ToString(),
                            result.Value<string>("content").ToString(),
                            "google"
                            );

                //Console.WriteLine("Results: " + results.Count());

                Dictify(results);

            }
        }

        private static void Dictify(IEnumerable<SearchResult> results)
        {
            foreach (var result in results)
            {
                var r = result;

                if (IsLegitContent(result.content) == false) continue;

                if (dict.ContainsKey(r.url) == false)
                {
                    r.google = r.engine == "google";
                    r.live = r.engine == "live";
                    dict.Add(r.url, r);
                }
                else
                {
                    r.live = dict[r.url].live;
                    r.google = dict[r.url].google;

                    if (r.engine == "google")
                        r.google = true;

                    if (r.engine == "live")
                    {
                        r.live = true;
                    }

                    dict[r.url] = r;
                }
            }
        }

        private static bool IsLegitContent(string content)
        {
            bool daymatch = false;
            bool qualifiermatch = false;
            content = content.ToLower();
            foreach (var day in days)
                if (content.Contains(day))
                    daymatch = true;
            foreach (var qualifier in qualifiers)
                if (content.Contains(qualifier))
                    qualifiermatch = true;
            return (daymatch == true && qualifiermatch == true);
        }
    }
}
