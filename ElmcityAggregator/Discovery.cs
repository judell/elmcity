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

		public enum TaggableSourceType { facebook, eventful, meetup, eventbrite };

		public static void VisualizeTaggables(string id)
		{
			var curated_feeds = GetCuratedFeeds(id);

			var taggable_source_types = GenUtils.EnumToList<TaggableSourceType>();

			var taggables_template_uri = BlobStorage.MakeAzureBlobUri("admin", "taggables.tmpl", false);
			var html = HttpUtils.FetchUrl(taggables_template_uri).DataAsString();
			html = html.Replace("__TITLE__", "taggables for " + id);

			var sb_links = new StringBuilder();
			var sb_json = new StringBuilder();

			foreach (var type in taggable_source_types)
			{
				sb_links.Append("<h1>" + type + "</h1>\n");

				var taggables = GetTaggables(id, type);

				var curated = from unique in taggables
							  where curated_feeds.Exists(feed => feed["feedurl"] == unique.ical_url)
							  select unique;

				var uncurated = taggables.Except(curated);

				var inactive = from unique in uncurated
								where unique.has_future_events == false
								select unique;

				//if (type == TaggableSourceType.facebook.ToString())
				//	uncurated = uncurated.Except(inactive);

				foreach (var taggable in taggables)
				{
					var is_curated = curated_feeds.Exists(feed => feed["feedurl"] == taggable.ical_url);
					if (is_curated == false)
						sb_json.Append(RenderTaggableJson(taggable, curated_feeds, type));
				}

				sb_links.Append("<p><b>Curated</b></p>");

				foreach (var taggable in curated)
					sb_links.Append(RenderTaggableLink(taggable, curated_feeds, type));

				sb_links.Append("<p><b>Uncurated (calendar has future events)</b></p>");

				foreach (var taggable in uncurated)
					sb_links.Append(RenderTaggableLink(taggable, curated_feeds, type));

				if (type == TaggableSourceType.facebook.ToString() && inactive.Count() > 0)
				{
					sb_links.Append("<p><b>Uncurated (calendar has only past events)</b></p>");
					foreach (var taggable in inactive)
						sb_links.Append(RenderTaggableLink(taggable, curated_feeds, type));
				}

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

		private static List<TaggableSource> GetTaggables(string id, string type)
		{
			var dict = new Dictionary<string, TaggableSource>();
		    var list = (List<TaggableSource>)BlobStorage.DeserializeObjectFromUri(BlobStorage.MakeAzureBlobUri(id, type + ".taggables.obj", false));
			foreach (var taggable in list)
			{
				if (dict.ContainsKey(taggable.home_url))
				{
					if (!String.IsNullOrEmpty(taggable.extra_url))
						dict[taggable.home_url] = taggable;
				}
				else
					dict.Add(taggable.home_url, taggable);
			}
			return dict.Values.ToList();
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

			var name = taggable.name.Replace("<title>", "").Replace("</title>", "");

			if (type == "facebook")
			{
				name = name.Replace("&#039;", " ");
				name = Regex.Replace(name, " = [^|]+ ", "");
				name = Regex.Replace(name, "| Facebook ", "");
			}

			if (!String.IsNullOrEmpty(taggable.city))
				name = name + " (" + taggable.city + ")";

			var extra = "";
			if (!String.IsNullOrEmpty(taggable.extra_url))
				extra = string.Format(@" [<a href=""{0}"">{0}</a>]", taggable.extra_url);

			var html = string.Format("<div><p><a href=\"{0}\">{1}</a>{2}</p></div>\n",
				taggable.home_url,
				name,
				extra
				);

			return html;
		}

		public static void StoreTaggables(string id, string location, Dictionary<string, string> settings)
		{
			var calinfo = new Calinfo(id);
			StoreFacebookTaggables(id, location, calinfo);
			StoreEventfulTaggables(id, settings, calinfo);
	//		StoreUpcomingTaggables(id, settings, calinfo);
			StoreEventBriteTaggables(id, settings, calinfo);
			StoreMeetupTaggables(id, settings, calinfo);
		}

		public static void StoreEventBriteTaggables(string id, Dictionary<string, string> settings, Calinfo calinfo)
		{
			try
			{
				var eventbrite_taggables = GetEventBriteOrganizers(calinfo, settings);
				bs.SerializeObjectToAzureBlob(eventbrite_taggables, id, "eventbrite.taggables.obj");
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "StoreTaggables: EventBrite", e.Message);
			}
		}

		public static void StoreMeetupTaggables(string id, Dictionary<string, string> settings, Calinfo calinfo)
		{
			try
			{
				var meetup_taggables = GetMeetupGroups(calinfo, 1, settings);
				bs.SerializeObjectToAzureBlob(meetup_taggables, id, "meetup.taggables.obj");
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "StoreTaggables: Meetup", e.Message);
			}
		}

		public static void StoreEventfulTaggables(string id, Dictionary<string, string> settings, Calinfo calinfo)
		{
			try
			{
				var eventful_taggables = GetEventfulVenues(calinfo, min_per_venue: 1, settings: settings);
				bs.SerializeObjectToAzureBlob(eventful_taggables, id, "eventful.taggables.obj");
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "StoreTaggables: Eventful", e.Message);
			}
		}

		public static void StoreFacebookTaggables(string id, string location, Calinfo calinfo)
		{
			try
			{
				var facebook_taggables = GetFacebookPages(calinfo, location);
				bs.SerializeObjectToAzureBlob(facebook_taggables, id, "facebook.taggables.obj");
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "StoreTaggables: Facebook", e.Message);
			}
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
				//if (venue.city_name != calinfo.City)
				if ( ! calinfo.City.Contains(venue.city_name) )
					return;
				if (venue.count < min_per_venue)
					return;
				var home_url = Regex.Replace(venue.home_url, "\\?.+", "");
				var ical_url = home_url.Replace("eventful.com/", "eventful.com/ical/");
				var taggable = new TaggableSource(venue.name, calinfo.id, home_url, ical_url, venue.city_name);
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
				var taggable = new TaggableSource(name, calinfo.id, home_url, ical_url, id_name_city.city);
				RememberTaggable(name_and_pk, organizer_id, taggable);
				organizers.Add(taggable);
			});

			return organizers;
		}

		public static List<TaggableSource> GetFacebookPages(Calinfo calinfo, string location)
		{
			var search_template = String.Format( "site:www.facebook.com/__TARGET__ \"{0}\"", location);
			var search_for_fan_pages = search_template.Replace("__TARGET__", "pages");
			var search_for_groups = search_template.Replace("__TARGET__", "groups");
			var stats = new Dictionary<string, object>();
			var fan_page_results = Search.BingSearch(search_for_fan_pages, 1000, stats);
			// var group_results = Search.BingSearch(search_for_groups, 1000, stats); // doesn't work, location string won't usually appear
			var group_results = new List<SearchResult>();                             // placeholder for now
			var bing_results = fan_page_results.Concat(group_results).ToList();

			var taggable_sources = InitializeTaggables(calinfo, "facebook");

			var seen_ids = new List<string>();
			string name_and_pk = "facebooksources";

			var settings = GenUtils.GetSettingsFromAzureTable();
			var options = new ParallelOptions();

			Parallel.ForEach(source: bing_results, parallelOptions: options, body: (result) =>
			//foreach (var result in bing_results)
			{
				try
				{
					var url = Regex.Replace(result.url, @"\?.+", "");  // remove query string if any
					var name = Regex.Match(result.url, "facebook.com/(pages|groups)/([^/]+)").Groups[2].Value;
					 name = name.Replace('-', ' ');

					var fb_id = Utils.id_from_fb_fanpage_or_group(url);

					if (seen_ids.Exists(x => x == fb_id))
						return;
					else
						seen_ids.Add(fb_id);

					string slat = null;
					string slon = null;
					var ical = new DDay.iCal.iCalendar();
					var facebook_access_token = settings["facebook_access_token"]; // todo: merge with code in collector
					var j_obj = Utils.GetFacebookEventsAsJsonObject(fb_id, facebook_access_token);
					var events = Utils.iCalendarizeJsonObjectFromFacebook(j_obj, calinfo, ical, slat, slon);

					if (events.Count == 0)  // no calendar on this page
						return;

					string page;

					if (FacebookPageMatchesLocation(url, location, settings, out page) == false)
						return;

					string origin_url = "";
					if ( ! String.IsNullOrEmpty(page) )
						origin_url = GetFacebookPageOrGroupOriginUrl(page);

					var ical_url = string.Format("http://elmcity.cloudapp.net/ics_from_fb_page?fb_id={0}&elmcity_id={1}",
							fb_id,
							calinfo.id);

					var has_future_events = FacebookPageHasFutureEvents(events, calinfo);

					var taggable = new TaggableSource(name, calinfo.id, url + "?sk=events", ical_url, has_future_events, origin_url);
		
					taggable_sources.Add(taggable);

					RememberTaggable(name_and_pk, fb_id, taggable);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "GetFacebookPages", e.Message + e.StackTrace);
					return;
				}

				});

			return taggable_sources;
		}

		private static string GetFacebookPageOrGroupOriginUrl(string page)
		{
		var re = new Regex(@"<td class=""vTop pls""><a href=""/l.php\?u=([^&]+)");
		var result = "";
		try
		{
			result = re.Match(page).Groups[1].Value;
		}
		catch { }
		return Uri.UnescapeDataString(result);
		}


		private static List<TaggableSource> InitializeTaggables(Calinfo calinfo, string flavor)
		{
			List<TaggableSource> taggable_sources;
			try
			{
				taggable_sources = (List<TaggableSource>)ObjectUtils.GetTypedObj<List<TaggableSource>>(calinfo.id, flavor + ".taggables.obj");
			}
			catch
			{
				taggable_sources = new List<TaggableSource>();
			}
			return taggable_sources;
		}

		private static bool FacebookPageHasFutureEvents(List<DDay.iCal.Event> events, Calinfo calinfo)
		{
			try
			{
				foreach (DDay.iCal.Event evt in events)
				{
					if (Utils.IsCurrentOrFutureDateTime(evt.Start.Date, calinfo.tzinfo))
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

		private static bool FacebookPageMatchesLocation(string url, string location, Dictionary<string, string> settings, out string page)
		{
			page = "";
			// expects a wap-formatted page
			try
			{
				var user_agent = settings["facebook_user_agent"];
				url = url + "?sk=info";
				page = HttpUtils.FetchUrlAsUserAgent(new Uri(url), user_agent).DataAsString();
				var _page = page.ToLower();
				return ( _page.IndexOf(location) > 0 || _page.IndexOf(location.ToLower()) > 0 ) ;
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

		public static List<TaggableSource> GetMeetupGroups2(Calinfo calinfo, int delay, Dictionary<string, string> settings)
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
							calinfo.id,
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

		public static List<TaggableSource> GetMeetupGroups(Calinfo calinfo, int delay, Dictionary<string, string> settings)
		{
			var meetup_key = settings["meetup_api_key"];
			var template = "https://api.meetup.com/2/groups?key={0}&lat={1}&lon={2}&radius={3}&page=200";
			var url = String.Format(template,
						meetup_key,
						calinfo.lat,
						calinfo.lon,
						calinfo.radius);

			var json = HttpUtils.SlowFetchUrl(new Uri(url), delay).DataAsString();
			var obj = JsonConvert.DeserializeObject<Dictionary<string,object>>(json);

			var taggable_sources = new List<TaggableSource>();
			string name_and_pk = "meetupsources";
			foreach ( var group in ((JArray) obj["results"]).ToList()  )
			{
				try
				{
				var name = (string) group["name"];
				var urlname = (string)group["urlname"];
				var id = group["id"].ToString();
				var home_url = "http://www.meetup.com/" + urlname;
				var ical_url = string.Format("http://www.meetup.com/{0}/events/ical/{1}/",
					urlname,
					Uri.EscapeDataString(name));
				ical_url = ical_url.Replace("%20", "+");  // otherwise meetup weirdly reports a 505 error
				var taggable = new TaggableSource(
						name,
						calinfo.id,
						home_url,
						ical_url,
						calinfo.where);

					taggable_sources.Add(taggable);
					RememberTaggable(name_and_pk, id, taggable);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "FindMeetupGroups", e.Message + e.StackTrace);
				}
			}
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
			var qresult = ts.QueryAllEntitiesAsListDict(name_and_pk, query, 0);
			return qresult.list_dict_obj.Count > 0;
		}

	}
}
