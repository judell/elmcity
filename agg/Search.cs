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
using System.Net;
using System.Text;
using ElmcityUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;


namespace CalendarAggregator
{
	// see http://blog.jonudell.net/2009/03/13/searching-for-calendar-information/
	public class SearchResult
	{
		public string url;
		public string title;
		public string content;
		public FindingEngine engine;

		public enum FindingEngine { google, bing, google_and_bing };

		public SearchResult(string url, string title, string content, FindingEngine engine)
		{
			this.url = url;
			this.title = title;
			this.content = content;
			this.engine = engine;
		}
	}


	public static class Search
	{
		private static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();

		private static Dictionary<string, string> settings = GenUtils.GetSettingsFromAzureTable();

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

		public static Dictionary<string, object> SearchLocation(string id, string where)
		{
			var final_results_dict = new Dictionary<string, SearchResult>();
			var stats_dict = new Dictionary<string, object>();
			stats_dict["search_terms_not_in_blurb"] = new Dictionary<string, int>();
			var engines = Enum.GetValues(typeof(SearchResult.FindingEngine));

			foreach (var engine in engines)
				stats_dict[engine.ToString()] = 0;

			PerformSearches(where, final_results_dict, stats_dict);

			CountResults(final_results_dict, stats_dict);

			var html = RenderResultsAsHtml(where, final_results_dict, stats_dict);

			var data = Encoding.UTF8.GetBytes(html.ToString());

			bs.PutBlob(id, id + ".search.html", new Hashtable(), data, "text/html");

			return stats_dict;
		}

		private static void CountResults(Dictionary<string, SearchResult> final_results_dict, Dictionary<string, object> stats_dict)
		{
			foreach (var url in final_results_dict.Keys)
			{
				var engine = final_results_dict[url].engine;
				IncrementEngineCount(engine, stats_dict);
			}
		}

		private static void IncrementEngineCount(SearchResult.FindingEngine engine, Dictionary<string, object> stats_dict)
		{
			var engine_name = engine.ToString();
			var count = (int)stats_dict[engine_name];
			count++;
			stats_dict[engine_name] = count;
		}

		private static string RenderResultsAsHtml(string where, Dictionary<string, SearchResult> final_results_dict, Dictionary<string, object> stats_dict)
		{
			var sb = new StringBuilder();

			sb.Append(string.Format(@"
<html>
<head><title>elmcity calendar finder</title></head>
<body style=""font-family:verdana,arial"">
<h1>possible sources of calendar information for {0} </h1>",
			where)
			);

			foreach (var engine in Enum.GetValues(typeof(SearchResult.FindingEngine)))
			{
				sb.Append(String.Format("<div>{0}: {1}</div>", engine, stats_dict[engine.ToString()]));
			}

			var count = 0;
			//var max = final_results_dict.Keys.Count();
			//var cutoff = Convert.ToInt32(max - (max * 0.01)); // skip last 1%
			foreach (var url in final_results_dict.Keys)
			{
				count++;
				//if (count > cutoff) break;
				var result = final_results_dict[url];
				sb.Append(
					string.Format("<p>{0}. <a href=\"{1}\">{2}</a> ({3})<div>{4}</div></p>",
						count,
						result.url,
						result.title,
						result.engine,
						result.content)
						);
			}

			sb.Append("</body></html>");

			var html = sb.ToString();

			var tags = "em|strong|b|i|center|font";
			html = EraseTags(html, tags);
			html = EraseTags(html, tags.ToUpper());

			return html;
		}

		private static string EraseTags(string html, string tags)
		{
			html = GenUtils.RegexReplace(html, "<\\*\\s*(" + tags + ")[^>]+\\s*>", "");
			return html;
		}

		private static void PerformSearches(string where, Dictionary<string, SearchResult> final_results_dict, Dictionary<string, object> stats_dict)
		{
			foreach (var qualifier in qualifiers)
			{
				foreach (var day in days)
				{
					string q;
					try
					{
						q = string.Format(@" ""{0}"" ""{1} {2}"" ", where, qualifier, day);
						var results = GoogleSearch(q, stats_dict);
						DictifyResults(results, final_results_dict, stats_dict);

					}
					catch (Exception ex1)
					{
						GenUtils.PriorityLogMsg("exception", "search_location: google: " + where, ex1.Message);
					}
					try
					{
						q = string.Format(@" ""{0}"" near:100 ""{1} {2}"" ", where, qualifier, day);
						var results = BingSearch(q, 500, stats_dict);
						DictifyResults(results, final_results_dict, stats_dict);
					}
					catch (Exception ex2)
					{
						GenUtils.PriorityLogMsg("exception", "search_location: bing: " + where, ex2.Message);
					}
				}
			}
		}

		public static List<SearchResult> BingSearch(string search_expression, int max, Dictionary<string, object> stats_dict)
		{
			var url_template = "http://api.search.live.net/json.aspx?AppId=" + Configurator.bing_api_key + "&Market=en-US&Sources=Web&Adult=Strict&Query={0}&Web.Count=50";
			var offset_template = "&Web.Offset={1}";
			var results_list = new List<SearchResult>();
			Uri search_url;
			List<int> offsets = GenUtils.EveryNth(start: 0, step: 50, stop: max).ToList();

			Parallel.ForEach(source: offsets, body: (offset) =>
			// foreach (var offset in offsets)
			{
				if (offset == 0)
					search_url = new Uri(string.Format(url_template, search_expression));
				else
					search_url = new Uri(string.Format(url_template + offset_template, search_expression, offset));

				var page = CallSearchApi(search_url);
				if (page == null)
					//continue;
					return;
				try
				{
					JObject o = (JObject)JsonConvert.DeserializeObject(page);

					var results_query =
						from result in o["SearchResponse"]["Web"]["Results"].Children()
						select new SearchResult
							  (
							  url: result.Value<string>("Url").ToString() ?? "NoUrl",
							  title: result.Value<string>("Title").ToString() ?? "NoTitle",
							  content: result.Value<string>("Description").ToString() ?? "NoDescription",
							  engine: SearchResult.FindingEngine.bing
							  );

					foreach (var result in results_query)
						results_list.Add(result);
				}
				catch
				{
					GenUtils.PriorityLogMsg("exception", "BingSearch", search_url.ToString());
				}
			}
			
			);

			return results_list;
		}

		private static string CallSearchApi(Uri search_url)
		{
			var delay = 2;
			try
			{
				delay = Convert.ToInt32(settings["search_engine_api_delay_secs"]);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "CallSearchApi", e.Message + e.StackTrace);
			}
			Utils.Wait(delay);
			var r = HttpUtils.FetchUrl(search_url);
			if (r.status != HttpStatusCode.OK)
			{
				GenUtils.LogMsg("warning", "CallSearchApi" + r.status.ToString(), search_url.ToString());
				return null;
			}
			else
				return r.DataAsString();
		}

		public static List<SearchResult> GoogleSearch(string search_expression,
			Dictionary<string, object> stats_dict)
		{
			var url_template = "http://ajax.googleapis.com/ajax/services/search/web?v=1.0&rsz=large&safe=active&q={0}&start={1}";
			Uri search_url;
			var results_list = new List<SearchResult>();
			int[] offsets = { 0, 8, 16, 24, 32, 40, 48 };
			foreach (var offset in offsets)
			{
				search_url = new Uri(string.Format(url_template, search_expression, offset));

				var page = CallSearchApi(search_url);
				if (page == null)
					continue;

				try
				{
					JObject o = (JObject)JsonConvert.DeserializeObject(page);

					var results_query =
						from result in o["responseData"]["results"].Children()
						select new SearchResult(
								url: result.Value<string>("url").ToString() ?? "NoUrl",
								title: result.Value<string>("title").ToString() ?? "NoTitle",
								content: result.Value<string>("content").ToString() ?? "NoContent",
								engine: SearchResult.FindingEngine.google
								);

					foreach (var result in results_query)
						results_list.Add(result);
				}
				catch
				{
					GenUtils.PriorityLogMsg("exception", "GoogleSearch", search_url.ToString());
				}

			}

			return results_list;
		}

		private static void DictifyResults(List<SearchResult> results_list, Dictionary<string, SearchResult> final_results_dict, Dictionary<string, object> stats_dict)
		{
			foreach (var search_result in results_list)
			{
				//if (VerifyFoundContent(search_result.url, search_result.content, stats_dict) == false) continue;
				VerifyFoundContent(search_result.url, search_result.content, stats_dict);

				if (!final_results_dict.ContainsKey(search_result.url))  // found first by either engine
				{
					final_results_dict.Add(search_result.url, search_result);
				}
				else                                                        // found again, maybe by other engine
				{
					MaybeUpdateFindingEngine(final_results_dict, search_result, stats_dict);
				}
			}
		}

		private static void MaybeUpdateFindingEngine(Dictionary<string, SearchResult> final_results_dict, SearchResult search_result, Dictionary<string, object> stats_dict)
		{
			var temp_result = final_results_dict[search_result.url];
			var already_found_by = temp_result.engine;
			if (
				(already_found_by == SearchResult.FindingEngine.bing && search_result.engine == SearchResult.FindingEngine.google)
				||
				(already_found_by == SearchResult.FindingEngine.google && search_result.engine == SearchResult.FindingEngine.bing)
				)
			{
				temp_result.engine = SearchResult.FindingEngine.google_and_bing;
				final_results_dict[search_result.url] = temp_result;
			}
		}

		private static bool VerifyFoundContent(string url, string content, Dictionary<string, object> stats_dict)
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
			var verified = (daymatch == true && qualifiermatch == true);
			if (verified == false)
			{
				var dict = (Dictionary<string, int>)stats_dict["search_terms_not_in_blurb"];
				if (dict.ContainsKey(url))
					dict[url] += 1;
				else
					dict.Add(url, 1);
			}
			return verified;
		}
	}
}
