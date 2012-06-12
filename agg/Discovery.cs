using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ElmcityUtils;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using HtmlAgilityPack;

namespace CalendarAggregator
{
	public class Discovery
	{
		static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
		static TableStorage ts = TableStorage.MakeDefaultTableStorage();

		public enum TaggableSourceType { facebook, eventful, upcoming, meetup, eventbrite };

		public static void VisualizeTaggables(string id)
		{
			var feeds = GetCuratedFeeds(id);

			var taggable_source_types = GenUtils.EnumToList<TaggableSourceType>();

			var taggables_template_uri = BlobStorage.MakeAzureBlobUri("admin", "taggables.tmpl", false);
			var html = HttpUtils.FetchUrl(taggables_template_uri).DataAsString();
			html = html.Replace("__TITLE__", "taggables for " + id);

			var sb_links = new StringBuilder();
			var sb_json = new StringBuilder();

			foreach (var type in taggable_source_types)
			{
				sb_links.Append("<h1>" + type + "</h1>\n");

				var uniques = new HashSet<TaggableSource>();

				try
				{
					uniques = GetUniqueTaggables(id, type);
				}
				catch { };

				var curated = from unique in uniques
							  where feeds.Exists(feed => feed["feedurl"] == unique.ical_url)
							  select unique;

				var uncurated = uniques.Except(curated);

				foreach (var taggable in uniques)
				{
					var is_curated = feeds.Exists(feed => feed["feedurl"] == taggable.ical_url);
					if (!is_curated)
						sb_json.Append(RenderTaggableJson(taggable, feeds, type));
				}

				foreach (var taggable in curated)
					sb_links.Append(RenderTaggableLink(taggable, feeds, type));

				foreach (var taggable in uncurated)
					sb_links.Append(RenderTaggableLink(taggable, feeds, type));
			}

			html = html.Replace("__BODY__", sb_links.ToString());
			bs.PutBlob(id, "taggable_sources.html", html.ToString(), "text/html");

			var json = "[" + sb_json.ToString().TrimEnd(',').TrimEnd(',', '\n') + "]";
			json = GenUtils.PrettifyJson(json);
			bs.PutBlob(id, "taggable_sources.json", json, "application/json");
		}

		private static List<Dictionary<string, string>> GetCuratedFeeds(string id)
		{
			var feeds_json_uri = BlobStorage.MakeAzureBlobUri(id, id + ".feeds.json", false);
			var json = HttpUtils.FetchUrl(feeds_json_uri).DataAsString();
			var feeds = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);
			return feeds;
		}

		private static HashSet<TaggableSource> GetUniqueTaggables(string id, string type)
		{
			var list = (List<TaggableSource>)BlobStorage.DeserializeObjectFromUri(BlobStorage.MakeAzureBlobUri(id, type + ".taggables.obj", false));

			var uniques = new HashSet<TaggableSource>();

			foreach (var taggable in list)
				uniques.Add(taggable);

			return uniques;
		}

		public static string RenderTaggableJson(TaggableSource taggable, List<Dictionary<string, string>> feeds, string type)
		{
			var json = string.Format("{{\"url\" : \"{0}\", \"feedurl\" : \"{1}\",  \"source\" : \"{2}\", \"category\" : \"{3}\" }},\n",
				taggable.home_url,
				taggable.ical_url,
				taggable.name.Replace("<title>", "").Replace("</title>", "") + " (" + type + ")",
				type);

			return json;
		}

		public static string RenderTaggableLink(TaggableSource taggable, List<Dictionary<string, string>> feeds, string type)
		{
			var is_curated = feeds.Exists(feed => feed["feedurl"] == taggable.ical_url);
			var style = is_curated ? "curated" : "uncurated";
			var name = taggable.name.Replace("<title>", "").Replace("</title>", "");
			if (!String.IsNullOrEmpty(taggable.city))
				name = name + " (" + taggable.city + ")";
			var html = string.Format("<div class=\"{0}\"><p><a href=\"{1}\">{2}</a></p></div>\n",
				style,
				taggable.home_url,
				name
				);

			return html;
		}

		public static void StoreTaggables(string id, string location, Dictionary<string, string> settings)
		{
			var calinfo = new Calinfo(id);

			var facebook_taggables = GetFacebookPages(calinfo, location);
			bs.SerializeObjectToAzureBlob(facebook_taggables, id, "facebook.taggables.obj");

			var eventful_taggables = GetEventfulVenues(calinfo, min_per_venue: 1, settings: settings);
			bs.SerializeObjectToAzureBlob(eventful_taggables, id, "eventful.taggables.obj");

			var upcoming_taggables = GetUpcomingVenues(calinfo, settings);
			bs.SerializeObjectToAzureBlob(upcoming_taggables, id, "upcoming.taggables.obj");

			var meetup_taggables = GetMeetupGroups(calinfo, 1, settings);
			bs.SerializeObjectToAzureBlob(meetup_taggables, id, "meetup.taggables.obj");

			var eventbrite_taggables = GetEventBriteOrganizers(calinfo, settings);
			bs.SerializeObjectToAzureBlob(eventbrite_taggables, id, "eventbrite.taggables.obj");

		}

		public static List<TaggableSource> GetUpcomingVenues(Calinfo calinfo, Dictionary<string, string> settings)
		{
			var collector = new Collector(calinfo, settings);

			var args = collector.MakeUpcomingApiArgs(Collector.UpcomingSearchStyle.latlon);
			var method = "event.search";
			var xdoc = collector.CallUpcomingApi(method, args);
			int page_count = 1;
			var result_count = Collector.GetUpcomingResultCount(xdoc);

			page_count = (result_count / 100 == 0) ? 1 : result_count / 100;

			var events = collector.UpcomingIterator(page_count, "event.search");

			var venues = new List<TaggableSource>();
			var name_and_pk = "upcomingsources";

			Parallel.ForEach(source: events, body: (xelt) =>
			{
				var city = xelt.Attribute("venue_city");
				if (city.Value.ToLower() != calinfo.City)
					return;
				var id = xelt.Attribute("venue_id").Value;
				MaybeAddSource(name_and_pk, calinfo.id, name_and_pk);
				var state = xelt.Attribute("venue_state_code");

				var name = xelt.Attribute("venue_name");
				// http://upcoming.yahoo.com/venue/863238/NH/Keene/The-Colonial-Theatre/
				var home_url = string.Format("http://upcoming.yahoo.com/venue/{0}/{1}/{2}/{3}/",
					id,
					state.Value,
					city.Value,
					name.Value.Replace(" ", "-")
					);
				var ical_url = "http://upcoming.yahoo.com/calendar/v2/venue/" + id;
				var taggable = new TaggableSource(name.Value, home_url, ical_url, city.Value);
				venues.Add(taggable);
				RememberTaggable(name_and_pk, id, taggable);
			});
			return venues;
		}

		public static List<TaggableSource> GetEventfulVenues(Calinfo calinfo, int min_per_venue, Dictionary<string, string> settings)
		{
			var collector = new Collector(calinfo, settings);
			string args = collector.MakeEventfulArgs(calinfo.where, 100, "");
			string method = "venues/search";
			var xdoc = collector.CallEventfulApi(method, args);
			var str_page_count = XmlUtils.GetXeltValue(xdoc.Root, ElmcityUtils.Configurator.no_ns, "page_count");
			int page_count = Convert.ToInt16(str_page_count);

			var ns = ElmcityUtils.Configurator.no_ns;
			var results = from venue in collector.EventfulIterator(page_count, args, "venues/search", "venue")
						  select new
						  {
							  id = venue.Attribute("id").Value,
							  name = XmlUtils.GetXeltValue(venue, ns, "name"),
							  city_name = XmlUtils.GetXeltValue(venue, ns, "city_name").ToLower(),
							  count = Convert.ToInt32(XmlUtils.GetXeltValue(venue, ns, "event_count")),
							  home_url = XmlUtils.GetXeltValue(venue, ns, "url")
						  };

			var venues = new List<TaggableSource>();
			var name_and_pk = "eventfulsources";

			Parallel.ForEach(source: results, body: (venue) =>
			{
				if (venue.city_name != calinfo.City)
					return;
				MaybeAddSource(venue.id, calinfo.id, name_and_pk);
				if (venue.count < min_per_venue)
					return;
				var home_url = Regex.Replace(venue.home_url, "\\?.+", "");
				var ical_url = home_url.Replace("eventful.com/", "eventful.com/ical/");
				var taggable = new TaggableSource(venue.name, home_url, ical_url, venue.city_name);
				RememberTaggable(name_and_pk, venue.id, taggable);
				venues.Add(taggable);
			});
			return venues;
		}

		public static List<TaggableSource> GetEventBriteOrganizers(Calinfo calinfo, Dictionary<string, string> settings)
		{
			var organizers = new List<TaggableSource>();
			var name_and_pk = "eventbritesources";
			var collector = new Collector(calinfo, settings);
			string method = "event_search";
			string args = collector.MakeEventBriteArgs();
			int page_count = collector.GetEventBritePageCount(method, args);

			var results = from evt in collector.EventBriteIterator(page_count, method, args)
						  select new
						  {
							  id = evt.Descendants("organizer").Descendants("id").FirstOrDefault().Value,
							  name = evt.Descendants("organizer").Descendants("name").FirstOrDefault().Value,
							  city = evt.Descendants("venue").Descendants("city").FirstOrDefault().Value.ToLower()
						  };

			results = results.Distinct();

			Parallel.ForEach(source: results, body: (id_name_city) =>
			{
				if (id_name_city.city != calinfo.City)
					return;
				var organizer_id = id_name_city.id;
				var name = id_name_city.name;
				var home_url = "http://www.eventbrite.com/org/" + organizer_id;
				var escaped_name = Uri.EscapeDataString(name);
				var ical_url = string.Format("http://elmcity.cloudapp.net/ics_from_eventbrite_organizer?organizer={0}&elmcity_id={1}", escaped_name, calinfo.id);
				var taggable = new TaggableSource(name, home_url, ical_url, id_name_city.city);
				RememberTaggable(name_and_pk, organizer_id, taggable);
				organizers.Add(taggable);
			});

			return organizers;
		}

		public static List<TaggableSource> GetFacebookPages(Calinfo calinfo, string location)
		{
			var search_string = string.Format("site:www.facebook.com/pages \"{0}\" ", location);
			var stats = new Dictionary<string, object>();
			var bing_results = Search.BingSearch(search_string, 1000, stats);
			var taggable_sources = new List<TaggableSource>();
			var seen_ids = new List<string>();
			string name_and_pk = "facebooksources";

			var settings = GenUtils.GetSettingsFromAzureTable();
			var options = new ParallelOptions();
			Parallel.ForEach(source: bing_results, parallelOptions: options, body: (result) =>
			{
				try
				{
					var url = Regex.Replace(result.url, @"\?.+", "");  // remove query string if any

					var re = new Regex(@"facebook.com/pages/([^/]+)/(\d{8,})");
					var m = re.Match(url);
					if (!m.Success) // not of the form www.facebook.com/â€‹pages/â€‹The-Starving-Artist/â€‹136721478562
						return;

					var name = m.Groups[1].Value.Replace("-", " ");
					var fb_id = m.Groups[2].Value;

					if (seen_ids.Exists(x => x == fb_id))
						return;
					else
					{
						MaybeAddSource(fb_id, calinfo.id, name_and_pk);
						/* in this case don't skip because a page that formerly wasn't using the events app may have added it
						if (IsCaptured(id, name_and_pk))  
							return;
						 */
						seen_ids.Add(fb_id);
					}

					var facebook_access_token = settings["facebook_access_token"]; // todo: merge with code in collector
					// https://graph.facebook.com/https://graph.facebook.com/142525312427391/events?access_token=...
					var graph_uri_template = "https://graph.facebook.com/{0}/events?access_token={1}";
					var graph_uri = new Uri(string.Format(graph_uri_template, fb_id, facebook_access_token));
					var json = HttpUtils.FetchUrl(graph_uri).DataAsString();
					var j_obj = (JObject) JsonConvert.DeserializeObject(json);
					var events = Utils.UnpackFacebookEventsFromJson(j_obj);

					if (FacebookPageHasFutureEvents(events, calinfo))
					{
						if (FacebookPageMatchesLocation(url, location, settings))
						{
							var ical_url = string.Format("http://elmcity.cloudapp.net/ics_from_fb_page?fb_id={0}&elmcity_id={1}",
								fb_id,
								calinfo.id);
							var taggable = new TaggableSource(name, url + "?sk=events", ical_url);
							taggable_sources.Add(taggable);
							RememberTaggable(name_and_pk, fb_id, taggable);
						}
					}

				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "GetFacebookPages", e.Message + e.StackTrace);
					return;
				}
			
			});
			
			return taggable_sources;
		}

		private static bool FacebookPageHasFutureEvents(List<FacebookEvent> events, Calinfo calinfo)
		{
			try
			{
				foreach (FacebookEvent evt in events)
				{
					if (Utils.IsCurrentOrFutureDateTime(evt.dt, calinfo.tzinfo))
						return true;
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "FacebookPageHasFutureEvents", e.Message + e.StackTrace);
				return false;
			}

			return false;
		}

		private static bool FacebookPageMatchesLocation(string url, string location, Dictionary<string, string> settings)
		{
			// expects a wap-formatted page
			try
			{
				var user_agent = settings["facebook_user_agent"];
				string page = HttpUtils.FetchUrlAsUserAgent(new Uri(url), user_agent).DataAsString();
				return page.IndexOf(location) > 0;
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "FacebookPageMatchesLocation", e.Message + e.StackTrace);
				return false;
			}
		}

		private static string GetMeetupCity(JToken jtoken)
		{
			var null_city = "";
			try
			{
				if (jtoken["venue"] == null)
					return null_city;
				else
					return jtoken["venue"]["city"].Value<string>().ToLower();
			}
			catch { }
			return null_city;
		}

		public static List<TaggableSource> GetMeetupGroups(Calinfo calinfo, int delay, Dictionary<string, string> settings)
		{
			var meetup_key = settings["meetup_api_key"];
			var template = "https://api.meetup.com/2/open_events?key={0}&lat={1}&lon={2}&radius={3}";
			var url = String.Format(template,
						meetup_key,
						calinfo.lat,
						calinfo.lon,
						calinfo.radius);

			var json = HttpUtils.SlowFetchUrl(new Uri(url), delay).DataAsString();

			var dict = JsonConvert.DeserializeObject<Dictionary<String, object>>(json);
			var results = (JArray)dict["results"];
			var ids_and_cities = from result in results
								 select new
								 {
									 id = result["group"]["id"].Value<string>(),
									 city = GetMeetupCity(result)
								 };

			var unique_ids_and_cities = ids_and_cities.Distinct();

			var taggable_sources = new List<TaggableSource>();

			Parallel.ForEach(source: unique_ids_and_cities, body: (id_and_city) =>
			{
				if (id_and_city.city != calinfo.City)
					return;
				string name_and_pk = "meetupsources";
				MaybeAddSource(id_and_city.id, calinfo.id, name_and_pk);
				try
				{
					template = "https://api.meetup.com/2/groups?key={0}&group_id={1}";
					url = String.Format(template,
							meetup_key,
							id_and_city.id);
					json = HttpUtils.SlowFetchUrl(new Uri(url), delay).DataAsString();
					dict = JsonConvert.DeserializeObject<Dictionary<String, object>>(json);
					results = (JArray)dict["results"];
					var result = results.First();
					var name = result["name"].Value<string>();
					var urlname = result["urlname"].Value<string>();
					var home_url = "http://www.meetup.com/" + urlname;
					var ical_url = string.Format("http://www.meetup.com/{0}/events/ical/{1}/",
						urlname,
						Uri.EscapeDataString(name));
					ical_url = ical_url.Replace("%20", "+");  // otherwise meetup weirdly reports a 505 error
					var taggable = new TaggableSource(
							name,
							home_url,
							ical_url,
							id_and_city.city);
					taggable_sources.Add(taggable);
					RememberTaggable(name_and_pk, id_and_city.id, taggable);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "FindMeetupGroups", e.Message + e.StackTrace);
				}
			});
			return taggable_sources;
		}

		private static void RememberTaggable(string name_and_pk, string rowkey, TaggableSource taggable)
		{
			var entity = ObjectUtils.ObjToDictObj(taggable);
			TableStorage.UpmergeDictToTableStore(entity, name_and_pk, name_and_pk, rowkey);
		}

		private static bool IsCaptured(string group_id, string name_and_pk)
		{
			var query = string.Format("$filter=PartitionKey eq '{0}' and RowKey eq '{1}' and name ne ''",
				name_and_pk,
				group_id);
			var qresult = ts.QueryAllEntitiesAsListDict(name_and_pk, query);
			return qresult.list_dict_obj.Count > 0;
		}

		private static void MaybeAddSource(string rowkey, string elmcity_id, string name_and_pk)
		{
			var entity = new Dictionary<string, object>() { { "elmcity_id", elmcity_id } };
			TableStorage.UpmergeDictToTableStore(entity, name_and_pk, name_and_pk, rowkey);
		}


	}
}
