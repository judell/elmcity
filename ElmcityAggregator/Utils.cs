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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using System.Xml;
using System.Xml.Linq;
using ElmcityUtils;
using LINQtoCSV;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Web;
using DDay.iCal;
using DDay.iCal.Serialization;
using HtmlAgilityPack;


namespace CalendarAggregator
{
	public static class Utils
	{
		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();
		private static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();

		//private static Int32 pid = System.Diagnostics.Process.GetCurrentProcess().Id;
		private static string hostname = System.Net.Dns.GetHostName();

		#region syndication

		public static string RssFeedFromEventStore(string id, string query, ZonelessEventStore es)
		{
			var url = string.Format("http://{0}/services/{1}/rss?{2}", ElmcityUtils.Configurator.appdomain, id, query);
			var uri = new System.Uri(url);
			var title = string.Format("{0}: {1}", id, query);
			var feed = new SyndicationFeed(title, "items from " + id + " matching " + query, uri);
			var items = new System.Collections.ObjectModel.Collection<SyndicationItem>();
			foreach (var evt in es.events)
			{
				if (evt.url == "")
					continue;
				var dt = evt.dtstart.ToString();
				var item = new SyndicationItem(evt.title + " " + dt, evt.source, new System.Uri(evt.url));
				items.Add(item);
			}
			feed.Items = items;
			var feed_builder = new StringBuilder();
			//var settings = new XmlWriterSettings();
			//settings.Encoding = Encoding.UTF8;
			// http://www.undermyhat.org/blog/2009/08/tip-force-utf8-or-other-encoding-for-xmlwriter-with-stringbuilder/
			var writer = XmlWriter.Create(feed_builder);
			feed.SaveAsRss20(writer);
			writer.Close();
			feed_builder.Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"", 0, 56);
			return feed_builder.ToString();
		}

		static public Dictionary<string, List<string>> CategoriesFromEventfulAtomFeed(string atom_url)
		{
			var eids_and_categories = new Dictionary<string, List<string>>();

			try
			{
				var gd = "http://schemas.google.com/g/2005";
				var atom = "http://www.w3.org/2005/Atom";

				var rss = HttpUtils.FetchUrl(new Uri(atom_url));
				var xml = XmlUtils.XmlDocumentFromHttpResponse(rss);
				var nsmgr = new XmlNamespaceManager(xml.NameTable);

				nsmgr.AddNamespace("gd", gd);
				nsmgr.AddNamespace("atom", atom);

				var entries = xml.SelectNodes("//atom:feed/atom:entry", nsmgr);

				foreach (XmlNode entry in entries)
				{
					var url = entry.SelectSingleNode("atom:link", nsmgr).Attributes["href"].Value;
					var eid = url.Split('/').Last();
					var category_nodes = entry.SelectNodes("atom:category", nsmgr);
					var categories = new List<string>();
					foreach (XmlNode x in category_nodes)
						categories.Add(x.Attributes["term"].Value);

					eids_and_categories[eid] = categories;
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "CategoriesFromEventfulAtomFeed", e.Message + e.StackTrace);
			}

			return eids_and_categories;
		}
		#endregion

		#region datetime

		public static DateTime DateTimeSecsToZero(DateTime dt)
		{
			return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0);
		}

		public static DateTime RoundDateTimeUpToNearest(DateTime dt, int minutes)
		{
			TimeSpan abs = (dt.Subtract(DateTime.MinValue)).Add(new TimeSpan(0, minutes, 0));
			var mins = ((int)abs.TotalMinutes / minutes) * minutes;
			var rounded = DateTime.MinValue.Add(new TimeSpan(0, mins, 0));
			return rounded;
		}

		public static DateTimeWithZone DtWithZoneFromICalDateTime(iCalDateTime idt, System.TimeZoneInfo tzinfo)
		{
			return DtWithZoneFromIdtAndTzinfo(idt, tzinfo);
		}

		public static DateTimeWithZone DtWithZoneFromDtAndTzinfo(DateTime dt, TimeZoneInfo tzinfo)
		{
			var idt = new iCalDateTime(dt);
			return DtWithZoneFromIdtAndTzinfo(idt, tzinfo);
		}

		private static DateTimeWithZone DtWithZoneFromIdtAndTzinfo(iCalDateTime idt, System.TimeZoneInfo tzinfo)
		{
			DateTimeWithZone dt_with_tz;
			DateTime dt;
			if (idt.IsUniversalTime)
			{
				dt = new DateTime(idt.Year, idt.Month, idt.Day, idt.Hour, idt.Minute, idt.Second);
				dt_with_tz = new DateTimeWithZone(TimeZoneInfo.ConvertTimeFromUtc(dt, tzinfo), tzinfo);
			}
			else
			{
				dt = new DateTime(idt.Year, idt.Month, idt.Day, idt.Hour, idt.Minute, idt.Second);
				dt_with_tz = new DateTimeWithZone(dt, tzinfo);
			}
			return dt_with_tz;
		}

		public static DateTime LocalDateTimeFromiCalDateTime(iCalDateTime idt, System.TimeZoneInfo tzinfo)
		{
			DateTime dt;
			if (idt.IsUniversalTime)
			{
				dt = new DateTime(idt.Year, idt.Month, idt.Day, idt.Hour, idt.Minute, idt.Second);
				dt = System.TimeZoneInfo.ConvertTimeFromUtc(dt, tzinfo);
			}
			else
			{
				dt = new DateTime(idt.Year, idt.Month, idt.Day, idt.Hour, idt.Minute, idt.Second, DateTimeKind.Local);
			}
			return dt;
		}

		public static DateTime LocalDateTimeFromLocalDateStr(string str_dt)
		{
			Regex re = new Regex(@"(\d+)\-(\d+)\-(\d+) (\d+):(\d+)", RegexOptions.None);
			DateTime dt = ParseDateTime(str_dt, re, DateTimeKind.Local);
			return dt;
		}

		public static DateTime DateTimeFromISO8601DateStr(string str_dt, DateTimeKind kind)
		{
			//2010-11-28T03:00:00+0000
			Regex re = new Regex(@"(\d+)\-(\d+)\-(\d+)T(\d+):(\d+)", RegexOptions.None);
			DateTime dt = ParseDateTime(str_dt, re, kind);
			return dt;
		}

		public static DateTime DateTimeFromICalDateStr(string str_dt, DateTimeKind kind)
		{
			Regex re = new Regex(@"(\d{4,4})(\d{2,2})(\d{2,2})T(\d{2,2})(\d{2,2})", RegexOptions.None);
			DateTime dt = ParseDateTime(str_dt, re, kind);
			return dt;
		}

		private static DateTime ParseDateTime(string str_dt, Regex re, DateTimeKind kind)
		{
			Match m = re.Match(str_dt);
			int year = Convert.ToInt16(m.Groups[1].Value);
			int month = Convert.ToInt16(m.Groups[2].Value);
			int day = Convert.ToInt16(m.Groups[3].Value);
			int hour = Convert.ToInt16(m.Groups[4].Value);
			int min = Convert.ToInt16(m.Groups[5].Value);
			DateTime dt = new DateTime(year, month, day, hour, min, 0, kind);
			return dt;
		}

		public static DateTime LocalDateTimeFromUtcDateStr(string str_dt, TimeZoneInfo tzinfo)
		{
			var dt = LocalDateTimeFromLocalDateStr(str_dt);
			return TimeZoneInfo.ConvertTimeFromUtc(dt, tzinfo);
		}

		public static DateTime DateTimeFromISO8601UtcDateStr(string str_dt, TimeZoneInfo tzinfo)
		{
			var dt = DateTimeFromISO8601DateStr(str_dt, DateTimeKind.Utc );
			return TimeZoneInfo.ConvertTimeFromUtc(dt, tzinfo);
		}

		/*
		public static DateTime LocalDateTimeFromFacebookDateStr(string str_dt, TimeZoneInfo tzinfo)
		{
			var dt = DateTimeFromISO8601DateStr(str_dt, DateTimeKind.Local);     // 2010-07-18 01:00
			var adjusted_dt = dt - new TimeSpan(Configurator.facebook_mystery_offset_hours, 0, 0);    // 2010-07-17 18:00
			return adjusted_dt;
		}*/

		public static string TimeStrFromDateTime(DateTime dt)
		{
			return string.Format("{0:HH:mm tt}", dt);
		}

		public static string DateFromDateKey(string datekey)
		{
			var values = GenUtils.RegexFindGroups(datekey, EventStore.datekey_pattern);
			var yyyy = Convert.ToInt32(values[1]);
			var MMM = Convert.ToInt32(values[2]);
			var dd = Convert.ToInt32(values[3]);
			var dt = new DateTime(yyyy, MMM, dd);
			return dt.ToString("ddd MMM dd yyyy");
		}

		public static string DateKeyFromDateTime(DateTime dt)
		{
			return string.Format("d{0:yyyyMMdd}", dt);
		}

		public static string XsdDateTimeFromDateTime(DateTime dt)
		{
			return string.Format("{0:yyyy-MM-ddTHH:mm:ss}", dt);
		}

		public static DateTime TwentyFourHoursBefore(DateTime dt)
		{
			var ts = new TimeSpan(24, 0, 0);
			return dt - ts;
		}

		public static DateTime TwentyFourHoursAfter(DateTime dt)
		{
			var ts = new TimeSpan(24, 0, 0);
			return dt + ts;
		}

		public static DateTimeWithZone NowInTz(System.TimeZoneInfo tzinfo)
		{
			var dt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzinfo);
			return new DateTimeWithZone(dt, tzinfo);
		}

		public static DateTimeWithZone MidnightInTz(System.TimeZoneInfo tzinfo)
		{
			var now_in_tz = Utils.NowInTz(tzinfo).LocalTime;
			var midnight = new DateTime(now_in_tz.Year, now_in_tz.Month, now_in_tz.Day, 0, 0, 0);
			var midnight_in_tz = new DateTimeWithZone(midnight, tzinfo);
			return midnight_in_tz;
		}

		public static bool DtWithZoneIsTodayInTz(DateTimeWithZone dt_with_zone, System.TimeZoneInfo tzinfo)
		{
			DateTimeWithZone midnight_in_tz = MidnightInTz(tzinfo);
			DateTime next_midnight = TwentyFourHoursAfter(midnight_in_tz.LocalTime);
			DateTimeWithZone next_midnight_in_tz = new DateTimeWithZone(next_midnight, tzinfo);
			bool result = (midnight_in_tz.UniversalTime <= dt_with_zone.UniversalTime) && (dt_with_zone.UniversalTime < next_midnight_in_tz.UniversalTime);
			return result;
		}

		public static bool DtIsTodayInTz(DateTime dt, System.TimeZoneInfo tzinfo)
		{
			var dt_with_zone = new DateTimeWithZone(dt, tzinfo);
			return DtWithZoneIsTodayInTz(dt_with_zone, tzinfo);
		}

		public static bool IsCurrentOrFutureDateTime(DateTime dt, System.TimeZoneInfo tzinfo)
		{
			var utc_last_midnight = Utils.MidnightInTz(tzinfo);
			return dt.ToUniversalTime() >= utc_last_midnight.UniversalTime;
		}

		public static bool IsCurrentOrFutureDateTime(ZonelessEvent evt, System.TimeZoneInfo tzinfo)
		{
			var dt = evt.dtstart;
			return IsCurrentOrFutureDateTime(dt, tzinfo);
		}

		public static System.TimeZoneInfo TzinfoFromName(string name)
		{
			name = name.ToLower();
			name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
			if ( ! name.EndsWith ("Standard Time") )
				name = name + " Standard Time";
			TimeZoneInfo tzinfo;
			try
			{
				tzinfo = System.TimeZoneInfo.FindSystemTimeZoneById(name);
			}
			catch (Exception e)
			{
				tzinfo = System.TimeZoneInfo.FindSystemTimeZoneById("GMT");
				GenUtils.PriorityLogMsg("exception", "no such tz: " + name, e.Message);
			}

			return tzinfo;
		}

		public static TimeZoneInfo TzInfoFromOlsonName(string olson_name)
		{
			string tzname = "GMT";
			try
			{
				var olson_windows_map = new Uri("http://unicode.org/repos/cldr/trunk/common/supplemental/windowsZones.xml");
				var xml = HttpUtils.FetchUrl(olson_windows_map).DataAsString();
				xml = xml.Replace(@"<!DOCTYPE supplementalData SYSTEM ""../../common/dtd/ldmlSupplemental.dtd"">", "");
				var xdoc = XmlUtils.XdocFromXmlBytes(Encoding.UTF8.GetBytes(xml));
				var mapZones = from mapZone in xdoc.Descendants("mapZone")
							   where mapZone.Attribute("type").Value == olson_name
							   select mapZone.Attribute("other").Value;
				tzname = mapZones.First();
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "TzInfoFromOlsonName: " + olson_name, "falling back to GMT\n" + e.Message + e.StackTrace);
			}
			return TzinfoFromName(tzname);
		}

		public static TimeOfDay ClassifyTime(DateTime dt)
		{
			dt = MakeCompYear(dt);

			if (TimesOfDay.MORNING_BEGIN <= dt && dt < TimesOfDay.LUNCH_BEGIN)
				return TimeOfDay.Morning;

			else if (TimesOfDay.LUNCH_BEGIN <= dt && dt < TimesOfDay.AFTERNOON_BEGIN)
				return TimeOfDay.Lunch;

			else if (TimesOfDay.AFTERNOON_BEGIN <= dt && dt < TimesOfDay.EVENING_BEGIN)
				return TimeOfDay.Afternoon;

			else if (TimesOfDay.EVENING_BEGIN <= dt && dt < TimesOfDay.NIGHT_BEGIN)
				return TimeOfDay.Evening;

			else if
					(
					   (TimesOfDay.MIDNIGHT_LAST < dt && dt < TimesOfDay.WEE_HOURS_BEGIN) ||
					   (TimesOfDay.NIGHT_BEGIN <= dt && dt < TimesOfDay.MIDNIGHT_NEXT)
					)
				return TimeOfDay.Night;

			else if (TimesOfDay.WEE_HOURS_BEGIN <= dt && dt < TimesOfDay.MORNING_BEGIN)
				return TimeOfDay.WeeHours;

			else
				return TimeOfDay.AllDay;
		}

		public static DateTime MakeCompYear(DateTime dt)
		{
			return new DateTime(
				TimesOfDay.DT_COMP_YEAR,
				TimesOfDay.DT_COMP_MONTH,
				TimesOfDay.DT_COMP_DAY,
				dt.Hour,
				dt.Minute,
				dt.Second);
		}

		public static string MakeAddToCalDateTime(string elmcity_id, string str_dt, bool for_facebook)
		{
			var dt = DateTime.Parse(str_dt);
			return MakeAddToCalDateTime(elmcity_id, dt, for_facebook);
		}

		public static string MakeAddToCalDateTime(string elmcity_id, DateTime dt, bool for_facebook)
		{
			if (for_facebook)
				return dt.ToString("yyyy-MM-ddTHH:mm:ss");
			else
			{
				var calinfo = Utils.AcquireCalinfo(elmcity_id);
				var utc = Utils.DtWithZoneFromDtAndTzinfo(dt, calinfo.tzinfo).UniversalTime;
				return utc.ToString("yyyyMMddTHHmmssZ");
			}
		}

		public static string MakeAddToCalDescription(string description, string url, string location)
		{
			description = "Source: " + description;
			if (!String.IsNullOrEmpty(url))
				description += " | Url: " + url;
			if (!String.IsNullOrEmpty(location))
				description += " | Location: " + location;
			return description;
		}

		public static void PrepForAddToCalRedirect(ref string description, string location, string url, ref string start, ref string end, string elmcity_id, bool for_facebook)
		{
			if (!String.IsNullOrEmpty(end))
				end = Utils.MakeAddToCalDateTime(elmcity_id, end, for_facebook);
			else
				end = Utils.MakeAddToCalDateTime(elmcity_id, DateTime.Parse(start).Add(TimeSpan.FromHours(2)), for_facebook);

			start = Utils.MakeAddToCalDateTime(elmcity_id, start, for_facebook);

			description = Utils.MakeAddToCalDescription(description, url, location);
		}

		#endregion datetime

		#region population

		public static string[] FindCityOrTownAndStateAbbrev(string where)
		{
			var city_or_town = "";
			var state_abbrev = "";
			var groups = GenUtils.RegexFindGroups(where, @"([^\s,]+)([\s,]+)([^\s]+)");
			if (groups.Count > 1)
			{
				city_or_town = groups[1].ToLower();
				state_abbrev = groups[3].ToLower();
			}
			return new string[] { city_or_town, state_abbrev };
		}

		public static int FindPop(string id, string city_or_town, string qualifier)
		{
			 var pop = Configurator.default_population;

			var metadict = Metadata.LoadMetadataForIdFromAzureTable(id);

			if (metadict.ContainsKey("population"))
			{
				try
				{
					pop = Convert.ToInt32(metadict["population"]);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("warning", id + " FindPop", e.Message + e.StackTrace);
				}
			}

			if (pop == Configurator.default_population)
			{
				try
				{
					pop = LookupUsPop(city_or_town, qualifier);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("warning", id + " FindPop / LookupUsPop", e.Message + e.StackTrace);
				}
			}

			if (pop == Configurator.default_population)
			{
				try
				{
					pop = LookupNonUsPop(city_or_town, qualifier);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("warning", id + " FindPop / LookupNonUsPop", e.Message + e.StackTrace);
				}
			}

			return pop;
		}

		private static int LookupNonUsPop(string city_or_town, string state_abbrev)
		{
			var pop = Configurator.default_population;
			try
			{
				var url = new Uri(ElmcityUtils.Configurator.azure_blobhost + "/admin/pop.txt");
				var r = HttpUtils.FetchUrl(url);
				Debug.Assert(r.status == HttpStatusCode.OK);
				var s = HttpUtils.FetchUrl(url).DataAsString();
				var lines = s.Split('\n');
				foreach (string line in lines)
				{
					var fields = line.Split('\t');
					if (fields[0] == city_or_town + "," + state_abbrev)
						pop = Convert.ToInt32(fields[1]);
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "lookup_non_us_pop", e.Message);
			}
			return pop;
		}

		public static int LookupUsPop(string target_city, string target_state_abbrev)
		{
			var pop = Configurator.default_population;
			if (Configurator.state_abbrevs.ContainsKey(target_state_abbrev) == false)
				return pop;
			var target_state = Configurator.state_abbrevs[target_state_abbrev];
			var csv = new WebClient().DownloadString(Configurator.census_city_population_estimates);
			csv = csv.ToLower();
			var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
			var sr = new StreamReader(stream);
			var cc = new CsvContext();
			var fd = new CsvFileDescription { };
			var census_rows = cc.Read<USCensusPopulationData>(sr, fd);
			try
			{
				var list = census_rows.ToList();
				var matching = list.FindAll(row => row.name.ToLower().StartsWith(target_city) && row.statename.ToLower() == target_state);
				if (matching.Count >= 1)
					pop = Convert.ToInt32(matching[0].pop_2009);
			}
			catch (AggregatedException ae)
			{
				GenUtils.PriorityLogMsg("exception", "lookup_us_pop", ae.Message);
				/*
				List<Exception> innerExceptionsList =
					(List<Exception>)ae.Data["InnerExceptionsList"];
				foreach (Exception e in innerExceptionsList)
				{
					Console.WriteLine(e.Message);
				}
				 */
			}
			return pop;
		}

		public static void UpdatePopulationToAzureForId(string id, int pop)
		{
			var dict = new Dictionary<string, string>();
			dict.Add("population", pop.ToString());
			var dict_obj = ObjectUtils.DictStrToDictObj(dict);
			TableStorage.UpmergeDictToTableStore(dict_obj, table: "metadata", partkey: id, rowkey: id);
		}

		#endregion population

		#region filtering

		public static List<DDay.iCal.Event> UniqueByTitleAndStart(List<DDay.iCal.Event> events)
		{
			var uniques = new Dictionary<string, DDay.iCal.Event>(); // dedupe by summary + start
			foreach (var evt in events)
			{
				if (String.IsNullOrEmpty(evt.Summary))
					continue;
				var key = evt.TitleAndTime();
				uniques.AddOrUpdateDictionary<string, DDay.iCal.Event>(key, evt);
			}
			return (List<DDay.iCal.Event>)uniques.Values.ToList();
		}

		public class Filter<T> where T : class
		{
			private readonly Predicate<T> criteria;

			public Filter(Predicate<T> criteria)
			{
				this.criteria = criteria;
			}

			public bool IsSatisfied(T obj)
			{
				return criteria(obj);
			}
		}

		#endregion filtering

		#region geo

		/*
		public static string[] LookupLatLon(string appid, string where)
		{
			var url = new Uri(string.Format("http://local.yahooapis.com/MapsService/V1/geocode?appid={0}&city={1}", appid, where));
			var resp = HttpUtils.FetchUrl(url);
			string lat = "";
			string lon = "";
			if (resp.status == HttpStatusCode.OK)
			{
				var xml = HttpUtils.FetchUrl(url).DataAsString();
				lat = GenUtils.RegexFindGroups(xml, "<Latitude>([^<]+)")[1];
				lon = GenUtils.RegexFindGroups(xml, "<Longitude>([^<]+)")[1];
			}
			return new string[] { lat, lon };
		}*/

		public static string[] LookupLatLon(string bing_api_key, string where)
		{
			var url = string.Format("http://dev.virtualearth.net/REST/v1/Locations?key={0}&query={1}", bing_api_key, where);

			string lat = null;
			string lon = null;

			try
			{
			var json = HttpUtils.FetchUrl( new Uri(url)).DataAsString();
			json = json.Replace("__type", "type"); // http://stackoverflow.com/questions/2005534/json-serialization-deserialization-mismatch-asp-net
			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
			var dict = (Dictionary<string,object>) serializer.DeserializeObject(json);

			lat = ((Object[])((Dictionary<string, object>)((Dictionary<string, object>)(((Object[])((Dictionary<string, object>)(((Object[])dict["resourceSets"])[0]))["resources"])[0]))["point"])["coordinates"])[0].ToString();
			lon = ((Object[])((Dictionary<string, object>)((Dictionary<string, object>)(((Object[])((Dictionary<string, object>)(((Object[])dict["resourceSets"])[0]))["resources"])[0]))["point"])["coordinates"])[1].ToString();
			}

			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "LookupLatLon", e.Message + e.StackTrace);
			}

			return new string[] { lat, lon };
		}

		public static void UpdateLatLonToAzureForId(string id, string lat, string lon)
		{
			var latlon_dict = new Dictionary<string, object>();
			latlon_dict["lat"] = lat;
			latlon_dict["lon"] = lon;
			ts.MergeEntity("metadata", id, id, latlon_dict);
		}

		//http://pietschsoft.com/post/2008/02/Calculate-Distance-Between-Geocodes-in-C-and-JavaScript.aspx
		public static class GeoCodeCalc
		{
			public const double EarthRadiusInMiles = 3956.0;
			public const double EarthRadiusInKilometers = 6367.0;
			public static double ToRadian(double val) { return val * (Math.PI / 180); }
			public static double DiffRadian(double val1, double val2)
			{ return ToRadian(val2) - ToRadian(val1); }

			public static double CalcDistance(string str_lat1, string str_lng1, string str_lat2, string str_lng2)
			{
				double distance;
				try
				{
					distance = CalcDistance(Convert.ToDouble(str_lat1), Convert.ToDouble(str_lng1), Convert.ToDouble(str_lat2), Convert.ToDouble(str_lng2));
				}
				catch (Exception e)
				{
					GenUtils.LogMsg("exception", "CalcDistance: " + str_lat1 + "," + str_lat2, e.Message);
					distance = 0;
				}
				return distance;
			}

			public static double CalcDistance(double lat1, double lng1, double lat2, double lng2)
			{
				return CalcDistance(lat1, lng1, lat2, lng2, GeoCodeCalcMeasurement.Miles);
			}

			public static double CalcDistance(double lat1, double lng1, double lat2, double lng2, GeoCodeCalcMeasurement m)
			{
				double radius = GeoCodeCalc.EarthRadiusInMiles;
				if (m == GeoCodeCalcMeasurement.Kilometers)
				{ radius = GeoCodeCalc.EarthRadiusInKilometers; }
				return radius * 2 * Math.Asin(Math.Min(1, Math.Sqrt((Math.Pow(Math.Sin((DiffRadian(lat1, lat2)) / 2.0), 2.0) + Math.Cos(ToRadian(lat1)) * Math.Cos(ToRadian(lat2)) * Math.Pow(Math.Sin((DiffRadian(lng1, lng2)) / 2.0), 2.0)))));
			}
		}

		public enum GeoCodeCalcMeasurement : int
		{
			Miles = 0,
			Kilometers = 1
		}
		#endregion geo

		#region cron

		public static void ScheduleTimer(ElapsedEventHandler handler, int minutes, string name, bool startnow)
		{
			var timer = new System.Timers.Timer();
			timer.Elapsed += handler;
			timer.AutoReset = true;
			timer.Interval = 1000 * 60 * minutes;
			timer.Start();

			object o = null;
			ElapsedEventArgs e = null;

			GenUtils.LogMsg("info", "ScheduleTimer", String.Format("handler {0}, name {1}, minutes {2}", handler.ToString(), name, minutes));

			if (startnow)
			{
				try
				{
					GenUtils.LogMsg("info", "schedule_timer: startnow: " + name, null);
					handler(o, e);
				}
				catch (Exception ex)
				{
					GenUtils.PriorityLogMsg("exception", "schedule_timer: startnow: " + name, ex.Message + ex.StackTrace);
				}
			}

		}

		#endregion cron

		#region logging

		private static DateTime Since(int minutes)
		{
			//if (minutes > Configurator.azure_log_max_minutes)
			//	minutes = Configurator.azure_log_max_minutes;
			var delta = System.TimeSpan.FromMinutes(minutes);
			var dt = System.DateTime.UtcNow - delta;
			return dt;
		}

		public static string GetRecentLogEntries(string log, string conditions, int minutes, string include, string exclude)
		{
			var sb = new StringBuilder();
			var dt = Since(minutes);
			var postfilter = new LogFilter(include, exclude);
			GetLogEntriesLaterThanTicks(log, dt.Ticks, conditions, postfilter, sb);
			return sb.ToString();
		}

		public static void GetTableEntriesBetweenTicks(string tablename, string partition_key, string row_key, string until_ticks, string conditions, List<Dictionary<string, string>> entities)
		{
			GetTableEntriesLaterThanTicks(tablename, partition_key, row_key, until_ticks, conditions, entities);
		}

		public static void GetTableEntriesLaterThanTicks(string tablename, string partition_key, string row_key, string conditions, List<Dictionary<string, string>> entities)
		{
			GetTableEntriesLaterThanTicks(tablename, partition_key, row_key, null, conditions, entities);
		}


		public static void GetTableEntriesLaterThanTicks(string tablename, string partition_key, string row_key, string until_ticks, string conditions, List<Dictionary<string, string>> entities)
		{
			string query = String.Format("$filter=PartitionKey eq '{0}' and RowKey gt '{1}' ", partition_key, row_key);
			if ( ! String.IsNullOrEmpty(until_ticks))
				query += String.Format(" and RowKey lt '{0}'", until_ticks);
			if (! String.IsNullOrEmpty(conditions))
				query += " and " + conditions;
			TableStorageListDictResponse r = ts.QueryAllEntitiesAsListDict(tablename, query);
			var dicts = r.list_dict_obj;
			if (dicts.Count == 0)
				return;
			var dict_str = new Dictionary<string, string>();
			foreach (var dict in dicts)
			{
				dict_str = ObjectUtils.DictObjToDictStr(dict);
				entities.Add(dict_str);
			}
		}

		public static void GetLogEntriesLaterThanTicks(string log, long ticks, string conditions, CalendarAggregator.LogFilter filter, StringBuilder sb)
		{
			var entities = new List<Dictionary<string, string>>();
			GetTableEntriesLaterThanTicks(tablename: log, partition_key: "log", row_key: ticks.ToString(), conditions: conditions, entities: entities );
			filter.SetEntities(entities);
			entities = filter.Apply();
			FormatLogEntries(sb, entities);
		}

		public static string GetLogEntriesBetweenTicks(string log, long ticks_from, long ticks_to, string conditions, string include, string exclude)
		{
			var entities = new List<Dictionary<string, string>>();
			GetTableEntriesBetweenTicks(log, "log", ticks_from.ToString(), ticks_to.ToString(), conditions, entities);
			var filter = new CalendarAggregator.LogFilter(include, exclude);
			filter.SetEntities(entities);
			entities = filter.Apply();
			var sb = new StringBuilder();
			FormatLogEntries(sb, entities);
			return sb.ToString();
		}

		private static void FormatLogEntries(StringBuilder sb, List<Dictionary<string, string>> filtered_entries)
		{
			foreach (var filtered_entry in filtered_entries)
				sb.AppendLine(FormatLogEntry(filtered_entry));
		}

		public static string FormatLogEntry(Dictionary<string, string> dict)
		{
			var ticks = dict["RowKey"];
			var str_timestamp = dict["Timestamp"];      //10/23/2009 2:26:51 AM
			var s = String.Format("{0} {1} {2} {3} {4}",
				   ticks, str_timestamp, dict["type"], dict["message"], dict["data"]);
			return s;
		}

		#endregion

		#region ics filters

		public enum IcsFromIcsOperator { before, after };

		static public string IcsFromIcs(string feedurl, Calinfo calinfo, string source, string after, string before, string include_keyword, string exclude_keyword, bool summary_only, bool description_only, bool url_only, bool location_only, Dictionary<string,string> settings)
		{
			bool changed = false;
			var feedtext = Collector.GetFeedTextFromFeedUrl(null, calinfo, source, feedurl, wait_secs:3, max_retries:3, timeout_secs:TimeSpan.FromSeconds(5), settings: settings, changed: ref changed);
			var sr = new StringReader(feedtext);
			iCalendar ical = default(iCalendar);
			try
			{
				ical = (DDay.iCal.iCalendar)iCalendar.LoadFromStream(sr).First().iCalendar;
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "IcsFromIcs: " + feedurl, e.Message + e.StackTrace);
			}

			var events = ical.Events.ToList();
			foreach (DDay.iCal.Event evt in events)
			{
				var tzinfo = calinfo.tzinfo;
				var dtstart = evt.DTStart.IsUniversalTime ? TimeZoneInfo.ConvertTimeFromUtc(evt.Start.UTC, tzinfo) : evt.Start.Local;
				var evt_abs_minutes = dtstart.Hour * 60 + dtstart.Minute;
				MaybeFilterHourMin(after,  ical, evt, evt_abs_minutes, IcsFromIcsOperator.after);
				MaybeFilterHourMin(before, ical, evt, evt_abs_minutes, IcsFromIcsOperator.before);
			}

			if ( ! String.IsNullOrEmpty(include_keyword) )
			{
				foreach (DDay.iCal.Event evt in events)
				{
					if (ShouldRemoveEvt(ContainsKeywordOperator.include, evt, include_keyword, summary_only, description_only, url_only, location_only))
					{
						ical.RemoveChild(evt);
						continue;
					}
				}
			}

			if (!String.IsNullOrEmpty(exclude_keyword))
			{
				foreach (DDay.iCal.Event evt in events)
				{
					if (ShouldRemoveEvt(ContainsKeywordOperator.exclude, evt, exclude_keyword, summary_only, description_only, url_only, location_only))
					{
						ical.RemoveChild(evt);
						continue;
					}
				}
			}

			return Utils.SerializeIcalToIcs(ical);
		}

		static public string IcsFromIcs(string feedurl, Calinfo calinfo, string source, string after, string before, string include_keyword, string exclude_keyword, bool summary_only, bool url_only, bool location_only, Dictionary<string, string> settings)
		{
			return IcsFromIcs(feedurl, calinfo, source, after, before, include_keyword, exclude_keyword, summary_only, false, url_only, location_only, settings);
		}

		private static void MaybeFilterHourMin(string str_hour_min, iCalendar ical, DDay.iCal.Event evt, int evt_abs_minutes, IcsFromIcsOperator op)
		{
			if (! String.IsNullOrEmpty(str_hour_min) ) 
			{
				int hour = -1;
				int minute = -1;
				GetHourMinute(str_hour_min, out hour, out minute);
				if (hour != -1)
				{
					var compare_abs_minutes = hour * 60 + minute;
					if (op == IcsFromIcsOperator.after)
					{
						if (evt_abs_minutes < compare_abs_minutes)
							ical.RemoveChild(evt);
					}
					else
					{
						if (evt_abs_minutes > compare_abs_minutes)
							ical.RemoveChild(evt);
					}
				}
			}
		}

		private static void GetHourMinute(string after, out int hour, out int minute)
		{
			var hour_minute = after.Trim().Split(':');
			hour = Convert.ToInt32(hour_minute[0]);
			minute = Convert.ToInt32(hour_minute[1]);
		}

		public enum ContainsKeywordOperator { include, exclude };

		static public bool ShouldRemoveEvt(ContainsKeywordOperator op, DDay.iCal.Event evt, string keyword, bool summary_only, bool description_only, bool url_only, bool location_only)
		{
			keyword = keyword.ToLower();
			var keywords = keyword.Split(',');
			var _keywords = new List<string>();
			foreach ( var k in keywords )
			{
				_keywords.Add(k.ToLower().Trim());
			}

			if (!summary_only && !description_only && !url_only && !location_only)  // if no property specified, default to summary + description
			{
				var summary = evt.Summary ?? "";
				var description = evt.Description ?? "";
				var input = summary + " " + description;
				return ShouldRemoveEvtHelper(op, _keywords, input);
			}

			if (location_only)
			{
				if (evt.Location == null)
					return true;
				else
				{
					var input = evt.Location;
					return ShouldRemoveEvtHelper(op, _keywords, input);
				}
			}

			if (url_only)
			{
				if (evt.Url == null)
					return true;
				else
				{
					var input = evt.Url.ToString();
					return ShouldRemoveEvtHelper(op, _keywords, input);
				}
			}

			if (summary_only)
			{
				if (evt.Summary == null)
					return true;
				else
				{
					var input = evt.Summary.ToString();
					return ShouldRemoveEvtHelper(op, _keywords, input);
				}
			}

			if (description_only)
			{
				if (evt.Description == null)
					return true;
				else
				{
					var input = evt.Description.ToString();
					return ShouldRemoveEvtHelper(op, _keywords, input);
				}
			}

			return false;

		}

		private static bool ShouldRemoveEvtHelper(ContainsKeywordOperator op, List<string> _keywords, string input)
		{
			return op == ContainsKeywordOperator.include ? RemoveUnlessAllIncluded(input, _keywords) : RemoveIfAnyExcluded(input, _keywords);
		}

		static public bool RemoveUnlessAllIncluded(string input, List<string> keywords)
		{
			input = input.ToLower();
			bool should_remove = false;
			var dict = new Dictionary<string, bool>();
			foreach (var keyword in keywords)
				dict.Add(keyword, input.Contains(keyword));
			var count_false = 0;
			foreach (var key in dict.Keys)  
			{
				if (dict[key] == false)
					count_false++;
			}

			if (count_false == dict.Keys.Count) // if any included keyword is missing from input, we're done, the event should be removed
				should_remove = true;

			return should_remove;
		}

		static public bool RemoveIfAnyExcluded(string input, List<string> keywords)
		{
			input = input.ToLower();
			bool should_remove = false;
			foreach (var keyword in keywords)  // if any excluded keyword is found in input, we're done, the event should be removed
			{
				if (input.Contains(keyword) == true)
				{
					should_remove = true;
					break;
				}
			}
			return should_remove;
		}

		static public string IcsFromRssPlusXcal(string rss_plus_xcal_url, string source, Calinfo calinfo)
		{
			XNamespace xcal = "urn:ietf:params:xml:ns:xcal";
			XNamespace geo = "http://www.w3.org/2003/01/geo/wgs84_pos#";
			//var uri = new Uri("http://events.pressdemocrat.com/search?city=Santa+Rosa&new=n&rss=1&srad=90&svt=text&swhat=&swhen=&swhere=");
			var rss = HttpUtils.FetchUrl(new Uri(rss_plus_xcal_url));
			var xdoc = XmlUtils.XdocFromXmlBytes(rss.bytes);
			var itemquery = from items in xdoc.Descendants("item") select items;
			var ical = new DDay.iCal.iCalendar();
			Collector.AddTimezoneToDDayICal(ical, calinfo.tzinfo);

			foreach (var item in itemquery)
			{
				var title = item.Element("title").Value;
				var url = item.Element("link").Value;
				var dtstart = Utils.LocalDateTimeFromLocalDateStr(item.Element(xcal + "dtstart").Value);
				var dtstart_with_zone = new DateTimeWithZone(dtstart, calinfo.tzinfo);
				var location = item.Element(xcal + "location").Value;
				string lat = null;
				string lon = null;
				try
				{
					lat = item.Element(geo + "lat").Value;
					lon = item.Element(geo + "long").Value;
				}
				catch
				{
					GenUtils.LogMsg("warning", "IcsFromRssPlusXcal", "unable to parse lat/lon");
				}
				var evt = Collector.MakeTmpEvt(calinfo, dtstart_with_zone, DateTimeWithZone.MinValue(calinfo.tzinfo), title, url: url, location: location, description: source, lat: lat, lon: lon, allday: false);
				Collector.AddEventToDDayIcal(ical, evt);
			}
			var serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer(ical);
			var ics_text = serializer.SerializeToString(ical);
			return ics_text;
		}

		static public string IcsFromAtomPlusVCalAsContent(string atom_plus_vcal_url, string source, Calinfo calinfo)
		{
			//var uri = new Uri("http://www.techhui.com/events/event/feed");  (should work for all ning sites)
			var ns = StorageUtils.atom_namespace;

			var atom = HttpUtils.FetchUrl(new Uri(atom_plus_vcal_url));
			var xdoc = XmlUtils.XdocFromXmlBytes(atom.bytes);
			var entryquery = from items in xdoc.Descendants(ns + "entry") select items;
			var ical = new DDay.iCal.iCalendar();
			var tzinfo = calinfo.tzinfo;
			Collector.AddTimezoneToDDayICal(ical, tzinfo);

			foreach (var entry in entryquery)
			{
				var title = entry.Element(ns + "title").Value;
				var url = entry.Element(ns + "link").Attribute("href").Value;
				var dtstart_str = entry.Descendants(ns + "dtstart").First().Value;
				var dtstart = Utils.DateTimeFromICalDateStr(dtstart_str, DateTimeKind.Local);
				var dtstart_with_zone = new DateTimeWithZone(dtstart, tzinfo);
				var location = entry.Descendants(ns + "location").First().Value;
				var evt = Collector.MakeTmpEvt(calinfo, dtstart_with_zone, DateTimeWithZone.MinValue(tzinfo), title, url: url, location: location, description: source, lat: null, lon: null, allday: false);
				Collector.AddEventToDDayIcal(ical, evt);
			}
			var serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer();
			var ics_text = serializer.SerializeToString(ical);
			return ics_text;
		}

		public static string IcsFromCsv(string feed_url, string home_url, string source, bool skip_first_row, int title_col, int date_col, int time_col, string tzname)
		{
			return IcsFromCsv(feed_url, home_url, source, skip_first_row, title_col, date_col, time_col, location_col: -1, tzname: tzname);
		}

		public static string IcsFromCsv(string feed_url, string home_url, string source, bool skip_first_row, int title_col, int date_col, int time_col, int location_col, string tzname)
		{
			var csv = HttpUtils.FetchUrl(new Uri(feed_url)).DataAsString();
			var lines = Regex.Split(csv, "\r\n").ToList();
			if (skip_first_row)
				lines = lines.Skip(1).ToList<string>();
			var events = new List<ZonedEvent>();
			var ical = new DDay.iCal.iCalendar();
			var tzinfo = Utils.TzinfoFromName(tzname);
			Collector.AddTimezoneToDDayICal(ical, tzinfo);
			foreach (var line in lines)
			{
				var l = line;
				try
				{
					if (l == "")
						continue;
					var fields = Regex.Split(l, ",").ToList();
					var evt = new DDay.iCal.Event();
					evt.Summary = fields[title_col].Trim('"');
					DateTime dtstart;
					try
					{
						dtstart = DateTime.Parse(fields[date_col].Trim('"') + ' ' + fields[time_col].Trim('"'));
					}
					catch
					{
						dtstart = DateTime.Parse(fields[time_col].Trim('"'));
					}
					try
					{
						if (location_col != -1)
							evt.Location = fields[location_col];
					}
					catch
					{
					}
					evt.Start = new iCalDateTime(dtstart);
					evt.Start.TZID = tzinfo.Id;
					evt.Url = new Uri(home_url);
					Collector.AddEventToDDayIcal(ical, evt);
				}
				catch ( Exception e )
				{
					GenUtils.LogMsg("warning", "IcsFromCsv: " + feed_url, e.Message + e.StackTrace);
				}
			}

			var serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer();
			return serializer.SerializeToString(ical);
		}

		static public string IcsFromFbPage(string fb_id, string elmcity_id, Dictionary<string,string> settings)
		{
			var facebook_access_token = settings["facebook_access_token"];

			// https://graph.facebook.com/https://graph.facebook.com/142525312427391/events?access_token=...
			var graph_uri_template = "https://graph.facebook.com/{0}/events?access_token={1}";
			var graph_uri = new Uri(string.Format(graph_uri_template, fb_id, facebook_access_token));
			var json = HttpUtils.FetchUrl(graph_uri).DataAsString();
			var j_obj = (JObject)JsonConvert.DeserializeObject(json);
			var count = j_obj["data"].Count();

			var calinfo = Utils.AcquireCalinfo(elmcity_id);
			var tzinfo = calinfo.tzinfo;
			var ical = new DDay.iCal.iCalendar();
			Collector.AddTimezoneToDDayICal(ical, tzinfo);

			foreach (JObject event_dict in j_obj["data"])
			{
				string id;
				string name;
				string location;
				DateTime dt;
				try
				{
					UnpackFacebookEventFromJson(event_dict, out id, out name, out dt, out location);
				}
				catch
				{
					continue;
				}
				var url = string.Format("http://www.facebook.com/events/{0}", id);
				var dtstart_with_zone = new DateTimeWithZone(dt, calinfo.tzinfo);
				try
				{
					location = event_dict["location"].Value<string>();
				}
				catch { }
				var evt = Collector.MakeTmpEvt(calinfo, dtstart:dtstart_with_zone, dtend:DateTimeWithZone.MinValue(calinfo.tzinfo), title:name, url:url, location: location, description: location, lat: null, lon: null, allday: false);
				Collector.AddEventToDDayIcal(ical, evt);
			}

			var ical_serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer(ical);
			var ics_text = ical_serializer.SerializeToString(ical);
			return ics_text;
		}

		public static string IcsFromLastFmVenue(string elmcity_id, string venue_id, Dictionary<string, string> settings)
		{
			var lastfm_api_key = settings["lastfm_api_key"];
			var calinfo = Utils.AcquireCalinfo(elmcity_id);
			iCalendar ical = Utils.InitializeCalendarForTzinfo(calinfo.tzinfo);
			var xml = HttpUtils.FetchUrl(new Uri("http://ws.audioscrobbler.com/2.0/?method=venue.getevents&api_key=" + lastfm_api_key + "&venue=" + venue_id)).bytes;
			var xdoc = ElmcityUtils.XmlUtils.XdocFromXmlBytes(xml);
			var events = xdoc.Descendants("event");
			var dict = new Dictionary<string, string>();
			foreach (var evt in events)
			{
				var id = XmlUtils.GetXeltValue(evt, "", "id");
				var url = "http://last.fm/event/" + id;
				var title = XmlUtils.GetXeltValue(evt, "", "title");
				var start = XmlUtils.GetXeltValue(evt, "", "startDate");

				var dtstart = DateTime.Parse(start);
				var dtstart_with_zone = new DateTimeWithZone(dtstart, calinfo.tzinfo);

				var dday_event = Collector.MakeTmpEvt(calinfo, dtstart: dtstart_with_zone, dtend: DateTimeWithZone.MinValue(calinfo.tzinfo), title: title, url: url, location: null, description: null, lat: null, lon: null, allday: false);
				Collector.AddEventToDDayIcal(ical, dday_event);
			}

			var ical_serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer(ical);
			var ics_text = ical_serializer.SerializeToString(ical);
			return ics_text;
		}

		public static string IcsFromJson(string json, string source, string tzname)
		{
			var tzinfo = Utils.TzinfoFromName(tzname);
			iCalendar ical = Utils.InitializeCalendarForTzinfo(tzinfo);
			var list_of_dict = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
			foreach (var dict in list_of_dict)
			{
				/* 1 = required key/val, 0 = optional
			 {
	1			"dtstart": "2013-04-14T08:00:00",      # JsonConvert reads this as DateTime
	0			"dtend": "2013-04-14T11:00:00",        # can be (typically is) empty
	1			"title": "The Ticket ~ Dance ~ Rock",
	0			"url": "http://www.facebook.com/events/616496838376153",
	0	        "lat": "42.977274,",                    
	0	        "lon": "-72.175455",
	1			"allday": false,                       # JsonConvert readst his as Boolean
	0			"description": "Mole Hill Theatre",     # can be (often is) empty
	0			"location": "789 Gilsum Mine Road, East Alstead"  # in real life often not this useful
		    } */
				try
				{
					DateTimeWithZone dtstart_with_zone;
					DateTimeWithZone dtend_with_zone;

					DateTime dtstart = (DateTime) dict["dtstart"];
					dtstart_with_zone = new DateTimeWithZone(dtstart,tzinfo);

					DateTime dtend = DateTime.MinValue;
					if (dict.ContainsKey("dtend"))
						dtend = (DateTime) dict["dtend"];
					dtend_with_zone = new DateTimeWithZone(dtend, tzinfo);

					var title = (string)dict["title"];

					string url = "";
					if ( dict.ContainsKey("url") )
						url = (string) dict["url"];

					string lat = "";
					if (dict.ContainsKey("lat"))
						lat = (string)dict["lat"];

					string lon = "";
					if (dict.ContainsKey("lon"))
						lon = (string)dict["lon"];

					bool allday = (bool)dict["allday"];

					string description = "";
					if (dict.ContainsKey("description"))
						description = (string)dict["description"];

					string location = "";
					if (dict.ContainsKey("location"))
						location = (string)dict["location"];

					var dday_event = Collector.MakeTmpEvt(tzinfo, dtstart: dtstart_with_zone, dtend: dtend_with_zone, title: title, url: url, location: location, description: description, lat: lat, lon: lon, allday: allday);
					Collector.AddEventToDDayIcal(ical, dday_event);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "IcsFromJson: " + source, e.Message + e.StackTrace);
				}
			}

			var ical_serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer(ical);
			var ics_text = ical_serializer.SerializeToString(ical);
			return ics_text;
		}

		public static List<FacebookEvent> UnpackFacebookEventsFromJson(JObject fb_json)
		{
			var fb_events = new List<FacebookEvent>();
			foreach (var event_dict in fb_json["data"])
			{
				string id;
				string name;
				string location;
				DateTime dt;
				UnpackFacebookEventFromJson(event_dict, out id, out name, out dt, out location);
				fb_events.Add(new FacebookEvent(name, location, dt, id));
			}
			return fb_events;
		}

		public static void UnpackFacebookEventFromJson(JToken event_dict, out string id, out string name, out DateTime dt, out string location)
		{
			id = event_dict["id"].Value<string>();
			name = event_dict["name"].Value<string>();
			try	{ location = event_dict["location"].Value<string>(); }
			catch { location = ""; }
			var _dt = event_dict["start_time"].Value<DateTime>(); // this is gmt apparently
			dt = _dt - TimeSpan.FromHours(Configurator.facebook_mystery_offset_hours);
		}

		private static string MakeEmptyEventBriteCal()
		{
			var ticks = System.DateTime.Now.Ticks.ToString();   // use ticks to defeat cache, otherwise if quota-throttled will never see new data
			var ics = String.Format(@"BEGIN:VCALENDAR
X-QUOTA-THROTTLED:{0}
END:VCALENDAR",
			  ticks);
			return ics;
		}

		static public string IcsFromEventBriteOrganizerByName(string organizer_name, string elmcity_id, Dictionary<string, string> settings)
		{
			var calinfo = Utils.AcquireCalinfo(elmcity_id);
			iCalendar ical = InitializeCalendarForCalinfo(calinfo);
			var collector = new Collector(calinfo, settings); 

			string method = "event_search";
			string args = collector.MakeEventBriteArgs(radius_multiplier: 2, organizer: organizer_name); // first enlarge the catchment area

			if (settings["eventbrite_quota_reached"] == "True")            // above quota
				return MakeEmptyEventBriteCal();

			int page_count = collector.GetEventBritePageCount(method, args);

			if (page_count == -1)            // above quota
				return MakeEmptyEventBriteCal();

			foreach (XElement evt in collector.EventBriteIterator(page_count, method, args))
			{
				var o_name = evt.Descendants("organizer").Descendants("name").FirstOrDefault().Value;
				if (organizer_name != o_name)  // should already be scoped to specified organizer name, but in case not...
					continue;

				string constructed_where;
				string declared_where;

				string venue_name;
				string venue_address;
				XElement venue = GetEventBriteVenue(evt, out venue_name, out venue_address);

				try
				{
					var city = venue.Descendants("city").FirstOrDefault().Value;
					var region = venue.Descendants("region").FirstOrDefault().Value;
					constructed_where = city.ToLower() + "," + region.ToLower();
					var city_state = Utils.FindCityOrTownAndStateAbbrev(calinfo.where);
					declared_where = city_state[0].ToLower() + "," + city_state[1].ToLower();
				}
				catch ( Exception e )
				{
					GenUtils.LogMsg("exception", "IcsFromEventBriteOrganizerByLocation: unpacking city/state", e.Message + e.StackTrace);
					continue;
				}

				if (constructed_where != declared_where)                     // now scope down to the city/state
					continue;

				DateTimeWithZone dtstart_with_zone;
				DateTimeWithZone dtend_with_zone;
				string url;
				string title;
				string location;
				string description;

				try
				{
					UnpackEventBriteEvt(calinfo.tzinfo, evt, venue_name, venue_address, out dtstart_with_zone, out dtend_with_zone, out url, out title, out location, out description);
				}
				catch (Exception e)
				{
					GenUtils.LogMsg("exception", "IcsFromEventBriteOrganizerByLocation: Unpacking evt", e.Message + e.StackTrace);
					continue;
				}
				var dday_event = Collector.MakeTmpEvt(calinfo, dtstart: dtstart_with_zone, dtend: DateTimeWithZone.MinValue(calinfo.tzinfo), title: title, url: url, location: location, description: location, lat: calinfo.lat, lon: calinfo.lon, allday: false);
				Collector.AddEventToDDayIcal(ical, dday_event);
			}

			var ical_serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer(ical);
			var ics_text = ical_serializer.SerializeToString(ical);
			return ics_text;
		}

		private static void UnpackEventBriteEvt(TimeZoneInfo tzinfo, XElement evt, string venue_name, string venue_address, out DateTimeWithZone dtstart_with_zone, out DateTimeWithZone dtend_with_zone, out string url, out string title, out string location, out string description)
		{
			title = evt.Descendants("title").FirstOrDefault().Value;
			url = evt.Descendants("url").FirstOrDefault().Value;
			description = evt.Descendants("description").FirstOrDefault().Value;
			description = Regex.Replace(description, @"</*[A-Z]+>", "");
			description = String.Format(@"
{0}

{1}
", url, description);

			location = venue_name + " " + venue_address;
			dtstart_with_zone = Utils.ExtractEventBriteDateTime(evt, tzinfo, "start_date");
			dtend_with_zone = Utils.ExtractEventBriteDateTime(evt, tzinfo, "end_date");
		}

		public static DateTimeWithZone ExtractEventBriteDateTime(XElement evt, TimeZoneInfo tzinfo, string element)
		{
			string str_datetime = XmlUtils.GetXeltValue(evt, ElmcityUtils.Configurator.no_ns, element);
			DateTime datetime = Utils.LocalDateTimeFromLocalDateStr(str_datetime);
			var datetime_with_tz = new DateTimeWithZone(datetime, tzinfo);
			return datetime_with_tz;
		}

		static public string IcsFromEventBriteOrganizerById(string organizer_id, string elmcity_id, Dictionary<string, string> settings)
		{
			var calinfo = Utils.AcquireCalinfo(elmcity_id);
			iCalendar ical = InitializeCalendarForCalinfo(calinfo);
			var collector = new Collector(calinfo, settings); 

			string method = "organizer_list_events";
			var args = "id=" + organizer_id;

			if (settings["eventbrite_quota_reached"] == "True")            // above quota
				return MakeEmptyEventBriteCal();

			int page_count = collector.GetEventBritePageCount(method, args);

			if (page_count == -1)            // above quota
				return MakeEmptyEventBriteCal();

			foreach (XElement evt in collector.EventBriteIterator(page_count, method, args))
			{
				string venue_name;
				string venue_address;
				GetEventBriteVenue(evt, out venue_name, out venue_address);

				DateTimeWithZone dtstart_with_zone;
				DateTimeWithZone dtend_with_zone;
				string url;
				string title;
				string location;
				string description;

				try
				{
					UnpackEventBriteEvt(calinfo.tzinfo, evt, venue_name, venue_address, out dtstart_with_zone, out dtend_with_zone, out url, out title, out location, out description);
				}
				catch (Exception e)
				{
					GenUtils.LogMsg("exception", "IcsFromEventBriteOrganizer: " + organizer_id, e.Message + e.StackTrace);
					continue;
				}

				if ( Utils.IsCurrentOrFutureDateTime(dtstart_with_zone.LocalTime, calinfo.tzinfo ) )
				{
					var dday_event = Collector.MakeTmpEvt(calinfo, dtstart: dtstart_with_zone, dtend: DateTimeWithZone.MinValue(calinfo.tzinfo), title: title, url: url, location: location, description: location, lat: calinfo.lat, lon: calinfo.lon, allday: false);
					Collector.AddEventToDDayIcal(ical, dday_event);
				}

			}

			var ical_serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer(ical);
			var ics_text = ical_serializer.SerializeToString(ical);
			return ics_text;
		}

		static public string IcsFromEventBriteEid(string eid, Calinfo calinfo, Dictionary<string, string> settings)
		{
			string ics_text;
			try
			{
				iCalendar ical = InitializeCalendarForTzinfo(calinfo.tzinfo);

				var key = settings["eventbrite_api_key"];
				string host = "https://www.eventbrite.com/xml";
				string eventbrite_api_url = string.Format("{0}/event_get?app_key={1}&id={2}", host, key, eid);
				var r = HttpUtils.FetchUrl(new Uri(eventbrite_api_url));
				var str_data = r.DataAsString();
				byte[] bytes = Encoding.UTF8.GetBytes(str_data);
				var xdoc = XmlUtils.XdocFromXmlBytes(bytes);
				XElement evt = xdoc.Descendants("event").FirstOrDefault();

				string venue_name;
				string venue_address;
				GetEventBriteVenue(evt, out venue_name, out venue_address);

				DateTimeWithZone dtstart_with_zone;
				DateTimeWithZone dtend_with_zone;
				string url;
				string title;
				string location;
				string description;

				UnpackEventBriteEvt(calinfo.tzinfo, evt, venue_name, venue_address, out dtstart_with_zone, out dtend_with_zone, out url, out title, out location, out description);

				var dday_event = Collector.MakeTmpEvt(calinfo, dtstart: dtstart_with_zone, dtend: dtend_with_zone, title: title, url: url, location: location, description: description, lat: null, lon: null, allday: false);
				Collector.AddEventToDDayIcal(ical, dday_event);

				var ical_serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer(ical);
				ics_text = ical_serializer.SerializeToString(ical);
			}
			catch (Exception e)
			{
				ics_text = String.Format("IcsFromEventBriteEid ({0}): {1}", eid, e.Message);
				GenUtils.PriorityLogMsg("exception", ics_text, e.StackTrace);
			}
		return ics_text;
		}

		public static DDay.iCal.iCalendar InitializeCalendarForCalinfo(Calinfo calinfo)
		{
			var tzinfo = calinfo.tzinfo;
			var ical = new DDay.iCal.iCalendar();
			Collector.AddTimezoneToDDayICal(ical, tzinfo);
			return ical;
		}

		public static DDay.iCal.iCalendar InitializeCalendarForTzinfo(TimeZoneInfo tzinfo)
		{
			var ical = new DDay.iCal.iCalendar();
			Collector.AddTimezoneToDDayICal(ical, tzinfo);
			return ical;
		}

		public static string GetEventBriteOrganizerIdFromEventId(string eid, Calinfo calinfo)
		{
			string organizer_id = null;

			try
			{
				var settings = GenUtils.GetSettingsFromAzureTable();
				var collector = new Collector(calinfo, settings);
				var xdoc = collector.CallEventBriteApi("event_get", "id=" + eid);
				organizer_id = xdoc.Descendants("organizer").Descendants("id").FirstOrDefault().Value;
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "GetEventBriteOrganizerFromEventId", e.Message + e.StackTrace);
			}

			return organizer_id;
		}

		public static XElement GetEventBriteVenue(XElement evt, out string venue_name, out string venue_address)
		{
			XElement venue = default(XElement);
			venue_name = "";
			venue_address = "";
			try
			{
				venue = evt.Descendants("venue").FirstOrDefault();
				venue_name = venue.Descendants("name").FirstOrDefault().Value;
				venue_address = venue.Descendants("address").FirstOrDefault().Value;
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "GetEventBriteVenue", e.Message + e.StackTrace);
			}
			return venue;
		}

		public static string IcsFromFindLocal(string tzname, string source, string url_template)
		{
			var ical = new DDay.iCal.iCalendar();
			var tzinfo = Utils.TzinfoFromName(tzname);
			Collector.AddTimezoneToDDayICal(ical, tzinfo);
			var ns = StorageUtils.atom_namespace;
			foreach (var entry in Collector.FindLocalIterator(url_template))
			{
				try
				{
					var title = entry.Element(ns + "title").Value.StripHtmlTags().Replace("&amp;", "&");
					var url = entry.Element(ns + "link").Attribute("href").Value;
					var description = entry.Descendants(ns + "description").First().Value.Replace("&lt;", "<")
						.Replace("&gt;", ">")
						.Replace("&amp;", "&")
						.Replace("&nbsp;", "")
						.Replace("&ndash;", "-")
						.Replace("&eacute;", "")
						.Replace("&ldquo;", "\"")
						.Replace("&rdquo;", "\"")
						.Replace("&lsquo;", "'")
						.Replace("&rsquo;", "'")
						.Replace("&trade;", "")
						.Replace("&bull;", "")
						.StripHtmlTags();
					description = Regex.Replace(description, @"\s+", " ");
					var dtstart_str = entry.Descendants(ns + "next_occurrence_date").First().Value;
					var dtstart = Utils.DateTimeFromISO8601DateStr(dtstart_str, DateTimeKind.Local);
					var dtstart_with_zone = new DateTimeWithZone(dtstart, tzinfo);
					var location = entry.Descendants(ns + "street_address").First().Value + ", " +
									entry.Descendants(ns + "city").First().Value;
					var lat = entry.Descendants(ns + "latitude").First().Value;
					var lon = entry.Descendants(ns + "longitude").First().Value;
					var evt = Collector.MakeTmpEvt(tzinfo, dtstart_with_zone, DateTimeWithZone.MinValue(tzinfo), title, url: url, location: location, description: description, lat: lat, lon: lon, allday: false);
					var categories = entry.Descendants(ns + "category");
					foreach (var cat in categories)
					{
						evt.Categories.Add(cat.Value);
					}
					Collector.AddEventToDDayIcal(ical, evt);
				}
				catch (Exception e)
				{
					GenUtils.LogMsg("exception", "IcsFromFindLocal", e.Message);
				}
			}
			var serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer();
			return serializer.SerializeToString(ical);
		}
		
		#endregion

		#region metadata 

		//	ViewData["result"] = CalendarAggregator.Utils.GetMetaHistory(a_name, b_name, id, flavor);

		static public string GetMetaHistory(string a_name, string b_name, string id, string flavor)
		{
			id = BlobStorage.LegalizeContainerName(id);

			var host = "http://elmcity.blob.core.windows.net";
			var path = host + "/" + id + "/";

			string json_a;
			string json_b;

			if (flavor == "feeds")
			{
				json_a = JsonListToJsonDict(path + a_name, "feedurl");
				json_b = JsonListToJsonDict(path + b_name, "feedurl");
			}
			else // flavor == "metadata"
			{
				json_a = HttpUtils.FetchUrlNoCache(new Uri(path + a_name)).DataAsString();
				json_b = HttpUtils.FetchUrlNoCache(new Uri(path + b_name)).DataAsString();
			}

			var page = HttpUtils.FetchUrlNoCache(new Uri(host + "/admin/jsondiff.html")).DataAsString();

			page = page.Replace("__JSON_A__", json_a);
			page = page.Replace("__JSON_B__", json_b);

			return page;
		}

		static private string JsonListToJsonDict(string url, string key)
		{
			var json = HttpUtils.FetchUrl(new Uri(url)).DataAsString();
			var list = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);
			var dict = new Dictionary<string, Dictionary<string, string>>();
			foreach (var d in list)
			{
				var k = d[key];
				dict.AddOrUpdateDictionary(k, d);
			}
			var new_json = JsonConvert.SerializeObject(dict);
			return new_json;
		}

		public static List<string> GetMetadataHistoryNamesForId(string id, string flavor) // flavor: "metadata" or "feeds"
		{
			var bs = BlobStorage.MakeDefaultBlobStorage();
			var r = bs.ListBlobs(id);
			var list_dict_str = (List<Dictionary<string, string>>)r.response;
			var re = new Regex(@"\d+\." + flavor + @"\.json");
			var snapshots = list_dict_str.FindAll(x => re.Match(x["Name"]).Success);
			var names = snapshots.Select(x => x["Name"] );
			var list = names.ToList();
			list.Sort();
			list.Reverse();
			return list;
		}

		public static string GetHubMetadataAsHtml(string id)
		{
			var settings = GenUtils.GetSettingsFromAzureTable("settings");

			var exclude_keys_str = settings["hub_metadict_exclude_list"];
			List<string> exclude_keys = exclude_keys_str.Split(',').ToList();
			var sb = new StringBuilder();
			sb.Append("<table>");
			var metadict = Metadata.LoadMetadataForIdFromAzureTable(id);
			// var exclude_keys = new List<string>() { "PartitionKey", "RowKey", "Timestamp", "default_img_html", 
			//	"eventbrite_events", "eventful_events", "events", "events_per_person", "facebook_events", "ical_events", "upcoming_events" };
			foreach (var key in metadict.Keys)
			{
				if (exclude_keys.Exists(k => k == key))
					continue;
				sb.Append(string.Format("<tr><td>{0}</td><td>{1}</td></tr>", key, metadict[key]));
			}
			sb.Append("</table>");
			return sb.ToString();
		}

		public static string GetFeedMetadataAsHtml(string id)
		{
			var sb = new StringBuilder();
			sb.Append("<div>");

			var rows = new List<string>();

			Dictionary<string, string> feeds = Metadata.LoadFeedsFromAzureTableForId(id, FeedLoadOption.only_public);
			var include_attrs = new List<string>() { "url", "category" };
			foreach (var feedurl in feeds.Keys)
			{
				var source = feeds[feedurl];
				var row = new StringBuilder();
				row.Append("<table>");
				row.Append(string.Format("<tr><td>{0}</td><td>{1}</td></tr>", "source", source));
				row.Append(string.Format("<tr><td>{0}</td><td>{1}</td></tr>", "feed url", feedurl));
				var feed_metadict = Metadata.LoadFeedMetadataFromAzureTableForFeedurlAndId(feedurl, id);
				foreach (var attr in include_attrs)
				{
					if (feed_metadict.ContainsKey(attr) && feed_metadict[attr] != "")
						//sb.AppendLine(attr + ": " + feed_metadict[attr]);
						row.Append(string.Format("<tr><td>{0}</td><td>{1}</td></tr>", attr, feed_metadict[attr]));
				}
				row.Append("</table>");

				rows.Add(row.ToString());
			}

			sb.Append(string.Join("<div>............</div>", rows));

			sb.Append("</div>");
			return sb.ToString();
		}

		public static string GetMetadataHistoryChooser(string id, string flavor) // flavor: "metadata" or "feeds" 
		{
			var names = GetMetadataHistoryNamesForId(id, flavor);
			var sb = new StringBuilder();
			sb.Append("<table>");
			var row_template = @"<tr>
<td>
<input id=""{0}_history_1"" name=""{0}_history_1"" type=""radio"" value=""{1}"">
<input id=""{0}_history_2"" name=""{0}_history_2"" type=""radio"" value=""{1}"">
{1}
</td>
</tr>";
			foreach (var name in names)
			{
				var row = String.Format(row_template, flavor, name);
				sb.Append(row);
			}

			sb.Append("</table>");
			return sb.ToString();
		}

		public static string GetMetadataChooserHandler(string id, string flavor)
		{
			//var script = HttpUtils.FetchUrl(new Uri("http://elmcity.blob.core.windows.net/admin/metadata_chooser_handler.tmpl")).DataAsString();
			var script = BlobStorage.GetAzureBlobAsString("admin", "metadata_chooser_handler.tmpl", false);
			script = script.Replace("__FLAVOR__", flavor);
			script = script.Replace("__ID__", id);
			return script;
		}

		public static void MakeMetadataPage(string id)
		{
			var template = new Uri("http://elmcity.blob.core.windows.net/admin/meta_history.html");
			var page = HttpUtils.FetchUrl(template).DataAsString();

			var hub_metadata = Utils.GetHubMetadataAsHtml(id);
			page = page.Replace("__HUB_METADATA__", hub_metadata);

			var hub_history = Utils.GetMetadataHistoryChooser(id, "metadata");
			page = page.Replace("__HUB_HISTORY__", hub_history);

			var hub_handler = Utils.GetMetadataChooserHandler(id, "metadata");
			page = page.Replace("__HUB_HANDLER__", hub_handler);

			var feed_metadata = Utils.GetFeedMetadataAsHtml(id);
			page = page.Replace("__FEED_METADATA__", feed_metadata);

			var feed_history = Utils.GetMetadataHistoryChooser(id, "feeds");
			page = page.Replace("__FEED_HISTORY__", feed_history);

			var feed_handler = Utils.GetMetadataChooserHandler(id, "feeds");
			page = page.Replace("__FEED_HANDLER__", feed_handler);

			bs.PutBlob(id, "metadata.html", Encoding.UTF8.GetBytes(page));
		}

		public static void PurgePickledCalinfoAndRenderer(string id)
		{
			foreach (string pickle in new List<string>() { "renderer", "calinfo" })
			{
				var pickle_name = String.Format("{0}.{1}.obj", id, pickle);
				bs.DeleteBlob(id, pickle_name);
			}
		}

		public static void PurgeAllPickles()
		{
			var ids = Metadata.LoadHubIdsFromAzureTable();
			foreach (var id in ids)
				PurgePickledCalinfoAndRenderer(id);
		}

		public static void RecreatePickledCalinfoAndRenderer(string id)
		{
			try  // create and cache calinfo and renderer, these are nonessential and recreated on demand if needed
			{
				var calinfo = new Calinfo(id);
				bs.SerializeObjectToAzureBlob(calinfo, id, id + ".calinfo.obj");

				var cr = new CalendarRenderer(id);
				bs.SerializeObjectToAzureBlob(cr, id, id + ".renderer.obj");
			}
			catch (Exception e2)
			{
				GenUtils.PriorityLogMsg("exception", "GeneralAdmin: saving calinfo and renderer: " + id, e2.Message);
			}
		}

		public static void RecreateAllPickles()
		{
			var ids = Metadata.LoadHubIdsFromAzureTable();
			Parallel.ForEach(source: ids, body: (id) =>
			{
				Utils.RecreatePickledCalinfoAndRenderer(id);
			}
					);
		}


		#endregion

		#region uses_foreign_auth

		public static bool ElmcityIdUsesForeignAuth(string table, string id)
		{
			try
			{
				var q = String.Format("$filter=RowKey eq '{0}'", id);
				var list = ts.QueryAllEntitiesAsListDict(table, q).list_dict_obj;
				return list.Count >= 1;
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ElmcityIdUsesForeignAuth:" + table + ":" + id, e.Message);
				return false;
			}
		}

		public static bool ElmcityIdUsesTwitterAuth(string id)
		{
			return ElmcityIdUsesForeignAuth(ElmcityUtils.Authentication.TrustedTable.trustedtwitterers.ToString(), id);
		}

		public static bool ElmcityIdUsesFacebookAuth(string id)
		{
			return ElmcityIdUsesForeignAuth(ElmcityUtils.Authentication.TrustedTable.trustedfacebookers.ToString(), id);
		}

		public static bool ElmcityIdUsesLiveAuth(string id)
		{
			return ElmcityIdUsesForeignAuth(ElmcityUtils.Authentication.TrustedTable.trustedlivers.ToString(), id);
		}

		public static bool ElmcityIdUsesGoogleAuth(string id)
		{
			return ElmcityIdUsesForeignAuth(ElmcityUtils.Authentication.TrustedTable.trustedgooglers.ToString(), id);
		}

		public static bool ElmcityIdUsesAuth(string id)
		{
			return ElmcityIdUsesTwitterAuth(id) || ElmcityIdUsesFacebookAuth(id) || ElmcityIdUsesLiveAuth(id) || ElmcityIdUsesGoogleAuth(id);
		}

		#endregion uses_foreign_auth

		#region homepage_summaries

		public static string GetWhereSummary()
		{
			var uri = BlobStorage.MakeAzureBlobUri("admin", "where_summary.html", false);
			return HttpUtils.FetchUrl(uri).DataAsString();
		}

		public static void MakeWhereSummary()
		{
			var wrd = WebRoleData.GetWrd();

			var summary = new StringBuilder();
			summary.Append("<html><head><title>Elm City regions and hubs</title><style>body {width:90%;font-family:verdana;margin:.5in} table { border-spacing:10px}</style></head><body>");

			summary.Append("<h1>Regions</h1>");

			var region_ids = Utils.GetRegionIds();

			summary.Append(@"<table style=""width:90%;margin:auto"">");
			summary.Append(@"
<tr>
<td><b>region</b></td>
<td><b>feeds</b></td>
<td><b>hubs</b></td>
</tr>");

			var row_template = @"
<tr>
<td>{0}</td>
<td align=""right"">{1}</td>
<td>{2}</td>
</tr>";

			foreach (var id in region_ids)
			{
				var calinfo = Utils.AcquireCalinfo(id);
				if (Convert.ToInt32(calinfo.feed_count) == 0)
					continue;
				var hubs = Utils.GetIdsForRegion(id);
				hubs = hubs.Select(x => String.Format(@"<a href=""http://{0}/{1}"">{1}</a>", ElmcityUtils.Configurator.appdomain, x)).ToList();
				var hub_str = String.Join(", ", hubs.ToArray());
				var row = string.Format(row_template,
					String.Format(@"<a title=""view hub"" href=""http://{0}/{1}"">{1}</a>", ElmcityUtils.Configurator.appdomain, id),
					String.Format(@"<a title=""view sources"" href=""http://{0}/{1}/stats"">{2}</a>", ElmcityUtils.Configurator.appdomain, id, calinfo.feed_count),
					hub_str
					);
				summary.Append(row);
			}

			summary.Append("</table>");

			summary.Append("<h1>Hubs</h1>");

			summary.Append(@"<table style=""width:90%;margin:auto"">");
			summary.Append(@"
<tr>
<td align=""left""><b>id</b></td>
<td align=""left""><b>location</b></td>
<td align=""right""><b>feeds</b></td>
<td align=""right""><b>events</b></td>
</tr>");
			row_template = @"
<tr>
<td align=""left"">{0}</td>
<td align=""left"">{1}</td>
<td align=""right"">{2}</td>
<td align=""right"">{3}</td>
</tr>";
			//foreach (var id in WebRoleData.where_ids
			foreach (var id in wrd.where_ids)
			{
				//if (IsReady(wrd, id) == false)
				//	continue;
				var calinfo = Utils.AcquireCalinfo(id);
				if ( Convert.ToInt32(calinfo.feed_count) == 0)
					continue;
				var metadict = Metadata.LoadMetadataForIdFromAzureTable(id);
				var events = metadict.ContainsKey("events") ? metadict["events"] : "";
				var row = string.Format(row_template,
					String.Format(@"<a title=""view hub"" href=""http://{0}/{1}"">{1}</a>", ElmcityUtils.Configurator.appdomain, id),
					calinfo.where.ToLower(),
					String.Format(@"<a title=""view sources"" href=""http://{0}/{1}/stats"">{2}</a>", ElmcityUtils.Configurator.appdomain, id, calinfo.feed_count),
					events
					);
				summary.Append(row);
			}
			summary.Append("</table></body></html>");

			bs.PutBlob("admin", "where_summary.html", summary.ToString(), "text/html");
		}

		public static string GetWhatSummary()
		{
			var uri = BlobStorage.MakeAzureBlobUri("admin", "what_summary.html", false);
			return HttpUtils.FetchUrl(uri).DataAsString();
		}

		public static void MakeWhatSummary()
		{
			var wrd = WebRoleData.GetWrd();

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
			foreach (var id in wrd.what_ids)
			{
				//if (IsReady(wrd, id) == false)
				//	continue;
				var row = string.Format(row_template,
					String.Format(@"<a title=""view outputs"" href=""/services/{0}"">{0}</a>", id)
					);
				summary.Append(row);
			}
			summary.Append("</table>");

			bs.PutBlob("admin", "what_summary.html", summary.ToString());
		}

		/* idle for now
		public static void MakeFeaturedHubs()
		{
			var tmpl_uri = BlobStorage.MakeAzureBlobUri("admin", "featured.tmpl");
			var tmpl = HttpUtils.FetchUrl(tmpl_uri).DataAsString();
			tmpl = tmpl.Replace("\r", "");
			var rows = tmpl.Split('\n');
			foreach (var row in rows)
			{
				if (row.Length == 0)
					continue;
				var fields = row.Split(',');
				var name = fields[0];
				var id = fields[1];
				var tag_count = GetTagCountForId(id);
				string event_count;
				string feed_count;
				var home_url = "/services/" + id + "/html";
				GetEventAndFeedCountsForId(id, out event_count, out feed_count);
				var tr = string.Format(@"<tr><td class=""place"">{0}</td><td>{1}</td><td>{2}</td><td>{3}</td></tr>",
					string.Format(@"<a href=""{0}"">{1}</a>", home_url, name),
					feed_count,
					event_count,
					tag_count);
				tmpl = tmpl.Replace(row.ToString(), tr);
			}
			bs.PutBlob("admin", "featured.html", tmpl);
		}
		  
		public static int GetTagCountForId(string id)
		{
			var tags_json_uri = new Uri("http://elmcity.cloudapp.net/services/" + id + "/tags_json");
			var tags_json_text = HttpUtils.FetchUrl(tags_json_uri).DataAsString();
			var tags_json = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(tags_json_text);
			return tags_json.Count;
		}

		public static void GetEventAndFeedCountsForId(string id, out string event_count, out string feed_count)
		{
			var q = string.Format("$filter=PartitionKey eq '{0}' and RowKey eq '{1}'", id, id);
			var entity = TableStorage.QueryForSingleEntityAsDictStr(ts, "metadata", q);
			event_count = "0";
			feed_count = "0";
			try
			{
				event_count = entity["events"];
				feed_count = entity["feed_count"];
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "GetEventCountForId", e.Message + e.StackTrace);
			}
		}
		  
		 */

		public static bool IsReady(WebRoleData wrd, string id)
		{
			return wrd.ready_ids.Contains(id);
		}

		#endregion

		#region tag sources

		private static void GetTagSources(string id)
		{
			var calinfo = Utils.AcquireCalinfo(id);
			var es = ObjectUtils.GetTypedObj<ZonelessEventStore>(id, id + ".zoneless.obj");
			var events = es.events;

			var tag_sources = new Dictionary<string, Dictionary<string, int>>();   //	umma:
			//		UMMA (Museum of Art): Artmaking			8
			//		UMMA (Museum of Art): Talks and Tours	12
			foreach (var evt in events)
			{
				if (String.IsNullOrEmpty(evt.categories))
					continue;

				List<string> tags;

				if (evt.original_categories == null)             // singular event
					tags = evt.categories.Split(',').ToList();
				else
					tags = evt.original_categories.Split(',').ToList(); // merged event

				foreach (var tag in tags)
				{
					Dictionary<string, int> sources_for_tag;

					if (tag_sources.ContainsKey(tag) == false)
						sources_for_tag = new Dictionary<string, int>();
					else
						sources_for_tag = tag_sources[tag];

					foreach (var key in evt.urls_and_sources.Keys)
						sources_for_tag.IncrementOrAdd<string>(evt.urls_and_sources[key]);

					tag_sources[tag] = sources_for_tag;
				}
			}
			bs.SerializeObjectToAzureBlob(tag_sources, id, "tag_sources.obj");
		}

		public static string VisualizeTagSources(string id)
		{
			var skip_tags = new List<string>() { "eventful", "upcoming", "facebook", "eventbrite", "meetup" };
			Dictionary<string, Dictionary<string, int>> tag_sources_dict = ObjectUtils.GetTypedObj<Dictionary<string, Dictionary<string, int>>>(id, "tag_sources.obj");
			var tag_sources_tmpl = BlobStorage.GetAzureBlobAsString("admin", "tag_sources.tmpl", false);
			var tag_tmpl = BlobStorage.GetAzureBlobAsString("admin", "tag.tmpl", false);
			var source_tmpl = BlobStorage.GetAzureBlobAsString("admin", "source.tmpl", false);
			var tags = tag_sources_dict.Keys.ToList();
			tags.Sort();
			var tag_sources = new StringBuilder();
			foreach (var tag in tags)
			{
				if (skip_tags.Exists(x => x == tag)) continue;
				if (tag.Length < 2) continue;
				var source_dict = tag_sources_dict[tag];
				var rows = new StringBuilder();
				foreach (var source in source_dict.Keys)
				{
					var row = source_tmpl;
					row = row.Replace("__SOURCE__", source);
					row = row.Replace("__COUNT__", source_dict[source].ToString());
					rows.Append(row);
				}
				var tag_chunk = tag_tmpl;
				tag_chunk = tag_chunk.Replace("__SOURCES__", rows.ToString());
				var anchor = "<a name=\"" + tag + "\"/>";
				tag_chunk = tag_chunk.Replace("__TAG__", tag);
				tag_sources.Append(anchor + tag_chunk);
			}
			var page = tag_sources_tmpl;
			page = page.Replace("__ID__", id);
			page = page.Replace("__TAG_SOURCES__", tag_sources.ToString());
			bs.PutBlob(id, "tag_sources.html", page, "text/html");
			return page;
		}

		public static string TagsByHub(string region)
		{
			var ids = Utils.GetIdsForRegion(region);
			var tags_by_hub = new Dictionary<string,List<string>>();
			foreach (var id in ids)
			{
				var hubtags = Utils.GetTagsForHub(id);
				foreach (var tag in hubtags)
					tags_by_hub.AddOrAppendDictOfListT(tag, id);
			}

			List<string> tags = tags_by_hub.Keys.ToList();
			tags.Sort(String.CompareOrdinal);

			var html = new StringBuilder();
			html.AppendLine(String.Format(@"<html>
<head><style>body {{ font-family: verdana; margin:.5in }}</style><title>tags by hub for region {0}</title></head>
<body><h1>tags by hub for region {0}</h1>",
			region)
			);

			foreach (var tag in tags)
			{
				List<string> hubs = tags_by_hub[tag];

				var links = hubs.Select(x => "<a href=\"" + ElmcityUtils.Configurator.azure_blobhost + "/" + x.ToLower() + "/tag_sources.html#" + tag + "\">" + x + "</a>");

				html.AppendLine(@"<p>" + "<b>" + tag + "</b>: " + String.Join(", ", links) + "</p>");
			}

			html.AppendLine("</html>");

			bs.PutBlob(region, "tags_by_hub.html", html.ToString(), "text/html");

			return html.ToString();
		}

		public static List<Dictionary<string, string>> GetTagsAndCountsForHubAsListDict(string id)
		{
			try
			{
				var uri = BlobStorage.MakeAzureBlobUri(id, Configurator.tags_json, false);
				var json = HttpUtils.FetchUrl(uri).DataAsString();
				var list_of_dict = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);
				return list_of_dict;
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "GetTagsForHubAsListDict: " + id, e.Message + e.StackTrace);
				return new List<Dictionary<string, string>>();
			}
		}

		public static List<string> GetTagsForHub(string id)
		{
			var list_of_dict = GetTagsAndCountsForHubAsListDict(id);
			var list = list_of_dict.Select(x => x.Keys.First()).ToList();
			return list;
		}

		#endregion

		#region curatorial url-bulding helpers

		public static string get_csv_ical_url(string feed_url, string home_url, string skip_first_row, string title_col, string date_col, string time_col, string tzname)
		{
			var csv_ical_url = "";
			try
			{
				csv_ical_url = String.Format("http://{0}/ics_from_csv?feed_url={1}&home_url={2}&skip_first_row={3}&title_col={4}&date_col={5}&time_col={6}&tzname={7}",
					ElmcityUtils.Configurator.appdomain,  // 0
					Uri.EscapeDataString(feed_url),       // 1
					Uri.EscapeDataString(home_url),       // 2
					skip_first_row,                       // 3
					title_col,                            // 4
					date_col,                             // 5
					time_col,                             // 6
					tzname                                // 7
					);

			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "get_csv_ical_url", e.Message + e.StackTrace);
			}
			return csv_ical_url;
		}

		public static string get_fb_ical_url(string fb_page_url, string elmcity_id)
		{
			var fb_ical_url = "";
			try
			{
				var fb_id = Utils.id_from_fb_fanpage_or_group(fb_page_url);
				fb_ical_url = String.Format("http://{0}/ics_from_fb_page?fb_id={1}&elmcity_id={2}",
					ElmcityUtils.Configurator.appdomain,  // 0
					fb_id,                                // 1
					elmcity_id);                          // 2
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "get_fb_ical_url: " + fb_page_url + "," + elmcity_id, e.Message + e.StackTrace);
			}
			return fb_ical_url;
		}

		public static string id_from_fb_fanpage_or_group(string url)
		{
			//r.headers["Location"]
			//"http://m.facebook.com/groups/3351007946"
			// or
			//"http://m.facebook.com/BerkeleyUndergroundFilmSociety?id=179173202101438&refsrc=http%3A%2F%2Fwww.facebook.com%2FBerkeleyUndergroundFilmSociety&_rdr"
			var id = "unknown";
			try
			{
				url = url.Replace("www.facebook.com", "m.facebook.com");
				var page = HttpUtils.FetchUrl(new Uri(url)).DataAsString();
				page = page.Replace("&nbsp", " ");
				var xdoc = XDocument.Parse(page);
				XNamespace html = "http://www.w3.org/1999/xhtml";
				var imgs = xdoc.Descendants(html + "img");
				var srcs = imgs.Select(x => x.Attribute("src"));
				var re = new Regex(@"(\d+)_(\d+)_(\d+)");
				foreach (var src in srcs)
				{
					if (re.Match(src.Value).Success)
					{
						id = re.Match(src.Value).Groups[2].Value;
						break;
					}
				}
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "id_from_fb_fanpage_or_group: " + url, e.Message);
			}

			return id;
		}

		public enum HighSchoolSportsTZ { ET, CT, MT, PT, AT, HT };

		public static string get_high_school_sports_ical_url(string school, string tz)
		{
			var hs_ical_url = "";
			try
			{
				tz = tz.ToUpper();
				var tzs = GenUtils.EnumToList<HighSchoolSportsTZ>();
				if (tzs.Exists(x => x == tz) == false)
					return "HighSchoolSports will not recognize the tz " + tz;

				HtmlDocument doc = new HtmlDocument();
				var url = "http://www.highschoolsports.net/school/" + school;
				doc.LoadHtml(HttpUtils.FetchUrl(new Uri(url)).DataAsString());

				HtmlNode select = doc.DocumentNode.SelectSingleNode("//select[@id='syncGLS']");
				HtmlNodeCollection options = select.SelectNodes("option");
				var sb = new StringBuilder();
				foreach (var option in options)
				{
					sb.Append(option.Attributes.First().Value);
					sb.Append(",");
				}

				var teams = sb.ToString().TrimEnd(',');

				hs_ical_url = string.Format("http://www.highschoolsports.net/ical.cfm?seo_url={0}&teams={1}&gamespractices=g&tz={2}",
					school,
					teams,
					tz);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "get_high_school_sports_ical_url: " + school + "," + tz, e.Message + e.StackTrace);
			}

			return hs_ical_url;
		}

		public static string get_ics_to_ics_ical_url(string feedurl, string elmcity_id, string source, string after, string before, string include_keyword, string exclude_keyword, string summary_only, string url_only, string location_only)
		{
			return get_ics_to_ics_ical_url(feedurl, elmcity_id, source, after, before, include_keyword, exclude_keyword, summary_only, "", url_only, location_only);
		}

		public static string get_ics_to_ics_ical_url(string feedurl, string elmcity_id, string source, string after, string before, string include_keyword, string exclude_keyword, string summary_only, string description_only, string url_only, string location_only)
		{
			var ics_to_ics_ical_url = "";
			try
			{
				ics_to_ics_ical_url = string.Format("http://{0}/ics_from_ics?feedurl={1}&elmcity_id={2}&source={3}", // &after={4}&before={5}&include_keyword={6}&exclude_keyword={7}&summary_only={8}&description_only={9}&url_only={10}&location_only={11}",
					ElmcityUtils.Configurator.appdomain,	// 0
					Uri.EscapeDataString(feedurl),			// 1
					elmcity_id,								// 2
					source);								// 3

				if (!String.IsNullOrEmpty(after))
					ics_to_ics_ical_url += "&after=" + after;

				if (!String.IsNullOrEmpty(before))
					ics_to_ics_ical_url += "&before=" + before;

				if (!String.IsNullOrEmpty(include_keyword))
					ics_to_ics_ical_url += "&include_keyword=" + include_keyword;

				if (!String.IsNullOrEmpty(exclude_keyword))
					ics_to_ics_ical_url += "&exclude_keyword=" + exclude_keyword;

				if (!String.IsNullOrEmpty(summary_only))
					ics_to_ics_ical_url += "&summary_only=" + summary_only;

				if (!String.IsNullOrEmpty(description_only))
					ics_to_ics_ical_url += "&description_only=" + description_only;

				if (!String.IsNullOrEmpty(url_only))
					ics_to_ics_ical_url += "&url_only=" + url_only;

				if (!String.IsNullOrEmpty(location_only))
					ics_to_ics_ical_url += "&location_only=" + location_only;
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "get_ics_to_ics_ical_url: " + feedurl + "," + elmcity_id, e.Message + e.StackTrace);
			}

			return ics_to_ics_ical_url;
		}

		public static string get_rss_xcal_ical_url(string feedurl, string elmcity_id)
		{
			var rss_xcal_ical_url = "";
			try
			{
				rss_xcal_ical_url = string.Format("http://{0}/ics_from_xcal?url={1}&elmcity_id={2}",
					ElmcityUtils.Configurator.appdomain,  // 0
					Uri.EscapeDataString(feedurl),        // 1
					elmcity_id);                          // 2
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "get_rss_xcal_ical_url: " + feedurl, e.Message + e.StackTrace);
			}

			return rss_xcal_ical_url;
		}

		public static string get_ical_url_from_eventbrite_event_page(string url, string elmcity_id)
		{
			var eventbrite_ical_url = "";
			try
			{
				var page = HttpUtils.FetchUrl(new Uri(url)).DataAsString();
				var eid = Regex.Matches(page, "eid=(\\d+)")[0].Groups[1].Captures[0].Value;
				var calinfo = Utils.AcquireCalinfo(elmcity_id);
				var organizer_id = GetEventBriteOrganizerIdFromEventId(eid, calinfo);
				if (String.IsNullOrEmpty(organizer_id))
					eventbrite_ical_url = "error: unable to form URL based on event id " + eid;
				else
					eventbrite_ical_url = string.Format("http://{0}/ics_from_eventbrite_organizer_id?organizer_id={1}&elmcity_id={2}",
						ElmcityUtils.Configurator.appdomain,	// 0
						organizer_id,							// 1
						elmcity_id);							// 2
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "get_ical_url_from_eventbrite_event_page: " + url, e.Message + e.StackTrace);
			}

			return eventbrite_ical_url;
		}

		public static string get_ical_url_from_eid_of_eventbrite_event_page(string url, string elmcity_id)
		{
			var eventbrite_ical_url = "";
			try
			{
				var page = HttpUtils.FetchUrl(new Uri(url)).DataAsString();
				//http://www.eventbrite.com/calendar?eid=3441799515&amp;calendar=ical" />
				var eid = Regex.Matches(page, @"eventbrite.com/calendar\?eid=(\d+)")[0].Groups[1].Captures[0].Value;
				eventbrite_ical_url = string.Format("http://{0}/ics_from_eventbrite_eid?eid={1}&elmcity_id={2}",
						ElmcityUtils.Configurator.appdomain,	// 0
						eid,									// 1
						elmcity_id);							// 2
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "get_ical_url_from_eid_of_eventbrite_event_page: " + url, e.Message + e.StackTrace);
			}

			return eventbrite_ical_url;
		}


		#endregion

		#region other

		public static int UpdateFeedCountForId(string id)
		{
			var ids = new List<string>() { }; // the container
			var final_count = 0;

			if (Utils.IsRegion(id))  // add many nodes to container
			{
				foreach (var _id in Utils.GetIdsForRegion(id))
					ids.Add(_id);
			}
			else                     // add single node to container
				ids.Add(id);

			foreach (var _id in ids)  // update node(s)
			{
				var fr = new FeedRegistry(_id);
				fr.LoadFeedsFromAzure(FeedLoadOption.all);
				var count = fr.feeds.Count();
				final_count += count;
				var dict = new Dictionary<string, object>() { { "feed_count", count.ToString() } };
				TableStorage.DictObjToTableStore(TableStorage.Operation.merge, dict, "metadata", _id, _id);
			}

			if (Utils.IsRegion(id))  // update container
			{
				var dict = new Dictionary<string, object>() { { "feed_count", final_count.ToString() } };
				TableStorage.DictObjToTableStore(TableStorage.Operation.merge, dict, "metadata", id, id);
			}

			return final_count;
		}

		public static bool UseNonIcalService(NonIcalType type, Dictionary<string, string> settings, Calinfo calinfo)
		{
			if (settings["use_" + type.ToString()] != "true")
				return false;

			if (type.ToString() == "eventful" && !calinfo.eventful)
				return false;

			if (type.ToString() == "upcoming" && !calinfo.upcoming)
				return false;

			if (type.ToString() == "eventbrite" && !calinfo.eventbrite)
				return false;

			if (type.ToString() == "facebook" && !calinfo.facebook)
				return false;

			return true;
		}

		public static string MakeLengthLimitedExceptionMessage(Exception e)
		{
			var trace = new System.Diagnostics.StackTrace(e, fNeedFileInfo: true);
			var index = Math.Min(e.Message.Length, 500);
			var msg = e.Message.Substring(0, index) + e.StackTrace;

			msg += string.Format("\n{0}, line {1}, col {2}",
				trace.GetFrame(0).GetMethod().Name,
				trace.GetFrame(0).GetFileLineNumber(),
				trace.GetFrame(0).GetFileColumnNumber()
				);
			return msg;
		}

		public static void PrintDict(Dictionary<string, string> dict)
		{
			foreach (var key in dict.Keys)
				Console.WriteLine(key + ": " + dict[key]);
		}

		public static void PrintDict(Dictionary<string, object> dict)
		{
			foreach (var key in dict.Keys)
				Console.WriteLine(key + ": " + dict[key]);
		}

		public static string TextifyDictInt(Dictionary<string, int> dict)
		{
			var sb = new StringBuilder();
			foreach (var key in dict.Keys)
				sb.Append(key + " : " + dict[key] + "\n");
			return sb.ToString();
		}

		public static string ValidationUrlFromFeedUrl(string feed_url)
		{
			return Configurator.remote_ical_validator + Uri.EscapeDataString(feed_url);
		}

		public static string DDay_Validate(string feed_url)
		{
			var validator_uri = ValidationUrlFromFeedUrl(feed_url);
			var xml_str = HttpUtils.FetchUrl(new Uri(validator_uri)).DataAsString();
			var xml_bytes = Encoding.UTF8.GetBytes(xml_str);
			var xdoc = XmlUtils.XdocFromXmlBytes(xml_bytes);
			var score = XmlUtils.GetXAttrValue(xdoc, Configurator.icalvalid_ns, "validationResults", "score");
			return score;
		}

		public static string iCal4J_Validate(string str_url)
		{
			Uri service_url = new Uri("http://severinghaus.org/projects/icv/?url=" + str_url);
			ElmcityUtils.HttpResponse response = HttpUtils.FetchUrl(service_url);
			var page = response.DataAsString();
			var error = "";
			if (page.Contains("Congratulations") == false)
			{
				error = GenUtils.RegexFindGroups(page, "Error was: ([^<]+)")[1];
			}
			return error;
		}

		public static Random _random = new Random();

		public static void Wait(int seconds)
		{
			System.Threading.Thread.Sleep(seconds * 1000);
		}

		public static string MakeBaseZonelessUrl(string id)
		{
			return string.Format("{0}/{1}/{2}.zoneless.obj",
				ElmcityUtils.Configurator.azure_blobhost,
				BlobStorage.LegalizeContainerName(id),
				id);
		}

		public static string MakeViewKey(string id, string type, string view, string count, string from, string to, string eventsonly, string mobile, string test, string raw, string style, string theme, string taglist, string tags)
		{
			var viewkey = string.Format("/services/{0}/{1}?view={2}&count={3}&from={4}&to={5}", id, type, view, count, from, to);
			if (type == "html")
				viewkey += "&eventsonly=" + eventsonly + "&mobile=" + mobile + "&test=" + test + "&raw=" + raw + "&style=" + style + "&theme=" + theme + "&taglist=" + taglist + "&tags=" + tags;
			return viewkey;
		}

		public static void RemoveBaseCacheEntry(string id)
		{
			var cached_base_uri = MakeBaseZonelessUrl(id);
			var url = string.Format("http://{0}/services/remove_cache_entry?cached_uri={1}",
				ElmcityUtils.Configurator.appdomain,
				cached_base_uri);
			var result = HttpUtils.FetchUrl(new Uri(url));
		}

		public static string EmbedHtmlSnippetInDefaultPageWrapper(Calinfo calinfo, string snippet)
		{                                       // note: { and } must be escaped as {{ and }} in the format string
			try
			{
				return string.Format(@"
<html>
<head> 
<title>{0}</title>
<link type=""text/css"" rel=""stylesheet"" href=""{1}"">
<style>
td {{ text-align: center }}
</style>
</head>
<body>
{2}
</body>
</html>
",
			   calinfo.id,
			   calinfo.css,
			   snippet);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "EmbedHtmlSnippet", e.Message + e.StackTrace);
				return snippet;
			}
		}

		public static T DeserializeObjectFromJson<T>(string containername, string blobname)
		{
			var uri = BlobStorage.MakeAzureBlobUri(containername, blobname, false);
			string json = HttpUtils.FetchUrl(uri).DataAsString();
			return JsonConvert.DeserializeObject<T>(json);
		}

		public static BlobStorageResponse SerializeObjectToJson(object obj, string containername, string file)
		{
			var json = JsonConvert.SerializeObject(obj);
			byte[] bytes = Encoding.UTF8.GetBytes(json);
			return BlobStorage.WriteToAzureBlob(bs, containername, file, "application/json", bytes);
		}

		public static void ReadWholeArray(Stream stream, byte[] data)
		{
			int offset = 0;
			int remaining = data.Length;
			while (remaining > 0)
			{
				int read = stream.Read(data, offset, remaining);
				if (read <= 0)
					throw new EndOfStreamException
						(String.Format("End of stream reached with {0} bytes left to read", remaining));
				remaining -= read;
				offset += read;
			}
		}

		public static Calinfo AcquireCalinfo(string id)
		{
			Calinfo calinfo;
			try
			{
				var name = id + ".calinfo.obj";
				var calinfo_uri = BlobStorage.MakeAzureBlobUri(id, name,false);
				calinfo = (Calinfo)BlobStorage.DeserializeObjectFromUri(calinfo_uri);
			}
			catch (Exception e)
			{
				var msg = "AcquireCalinfo: " + id;
				GenUtils.PriorityLogMsg("exception", msg, e.Message);
				calinfo = new Calinfo(id);
			}
			return calinfo;
		}

		public static CalendarRenderer AcquireRenderer(string id)
		{
			CalendarRenderer cr;
			try
			{
				var name = id + ".renderer.obj";
				var renderer_uri = BlobStorage.MakeAzureBlobUri(id, name,false);
				cr = (CalendarRenderer)BlobStorage.DeserializeObjectFromUri(renderer_uri);
			}
			catch (Exception e)
			{
				var msg = "AcquireRenderer: " + id;
				GenUtils.PriorityLogMsg("exception", msg, e.Message);
				cr = new CalendarRenderer(id);
			}
			return cr;
		}

		public static string StripTagsFromUnfoldedComponent(string calendar_text, string target)
		{
			var lines = calendar_text.Split('\n');
			var sb = new StringBuilder();
			bool in_component = false;
			foreach (var line in lines)
			{
				var re = new Regex( "^([A-Z]+)[:;]" );
				if ( re.Match(line).Success )
				{
					var propname = re.Match(line).Groups[1].ToString();
					if ( propname == target )
						in_component = true;
					else
						in_component = false;
				}

				if (in_component)
				{
					var tmp = line;
					tmp = tmp.StripHtmlTags();
					sb.Append(tmp + "\n");
				}
				else
				{
					sb.Append(line + "\n");
				}
			}
			return sb.ToString();
		}
		
		public static string RemoveLine(string calendar_text, string target)  
		{
			var lines = calendar_text.Split('\n');
			var sb = new StringBuilder();
			foreach (var line in lines)
			{
				if (line.StartsWith(target))
					continue;

				sb.Append(line + "\n");
			}

			return sb.ToString();
		}

		/*
		public static string TrimLine(string calendar_text, string pattern)
		{
			var lines = calendar_text.Split('\n');
			var sb = new StringBuilder();
			foreach (var line in lines)
			{
				var s = line;
				if (s.Contains(pattern))
				{
					string p = pattern.Replace("*", "\\*");
					s = Regex.Replace(s, p + ".+", "");
				}

				sb.Append(s + "\n");
			}

			return sb.ToString();
		} */

		public static string ChangeDateOnlyUntilToDateTime(string calendar_text) // workaround for https://github.com/dougrday/icalvalid/issues/7 and 8
		{
			var s = calendar_text;
			s = Regex.Replace(s, "UNTIL=(\\d{8})(;.+)", "UNTIL=$1T000000$2", RegexOptions.Multiline);
			s = Regex.Replace(s, "UNTIL=(\\d{8})(\r*\n)", "UNTIL=$1T000000$2", RegexOptions.Multiline);
			return s;
		}

		public static string RemoveNegativeCOUNT(string calendar_text)
		{
			var lines = calendar_text.Split('\n');
			var sb = new StringBuilder();
			var re = new Regex(@"RRULE:.+(COUNT=\-\d+;*).+");
			foreach (var line in lines)
			{
				var s = line;
				if (re.Match(s).Success)
					s = s.Replace(re.Match(s).Groups[1].Value, "");
				sb.Append(s + "\n");
			}
			return sb.ToString();
		}

		public static string RemoveComponent(string calendar_text, string component) 
		{
			var lines = calendar_text.Split('\n');
			var sb = new StringBuilder();
			bool in_component = false;
			foreach (var line in lines)
			{
				if (in_component && line.StartsWith(" "))
					continue;

				if (in_component && !line.StartsWith(" "))
				{
					in_component = false;
				}

				if (line.StartsWith(component))
				{
					in_component = true;
					continue;
				}

				sb.Append(line + "\n");
			}

			return sb.ToString();
		}

		public static string KeepOnlyVEVENTs(string calendar_uri)
		{
			try
			{
				var text = HttpUtils.FetchUrl(new Uri(calendar_uri)).DataAsString();
				var lines = text.Split('\n');
				var sb = new StringBuilder();
				bool in_vevent = false;
				foreach (var line in lines)
				{
					if (line.StartsWith("BEGIN:VEVENT"))
						in_vevent = true;

					if (in_vevent)
					{
						sb.Append(line + "\n");
						if (line.StartsWith("END:VEVENT"))
							in_vevent = false;
					}

				}

				return string.Format("BEGIN:VCALENDAR\r\n" + sb.ToString() + "END:VCALENDAR");
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "KeepOnlyVEVENTs", e.Message);
				return e.Message;
			}
		}

		public static string AddColonToBarePropnames(string calendar_text)
		{
			var re = new Regex("^(SUMMARY|DESCRIPTION|LOCATION|CATEGORIES|URL|GEO)\r$");
			var lines = calendar_text.Split('\n');
			var sb = new StringBuilder();
			foreach (var line in lines)
			{
				var _line = line;
				if (re.Match(line).Success)
					_line = line.Replace("\r",":\r");
				sb.Append(_line + "\n");
			}
			return sb.ToString();
		}

		public static string AdjustCategories(string calendar_text)
		{
			var re = new Regex("^CATEGORIES");
			var lines = calendar_text.Split('\n');
			var sb = new StringBuilder();
			foreach (var line in lines)
			{
				var _line = line;
				if (re.Match(line).Success)
					_line = line.Replace("\\", "");
				sb.Append(_line + "\n");
			}
			return sb.ToString();
		}

		public static string FixMiswrappedComponent(string calendar_text, string component)
		{
			var lines = calendar_text.Split('\n');
			var sb = new StringBuilder();
			bool in_component = false;
			var re = new Regex("^(BEGIN:VEVENT|END:VEVENT|DTSTART|DTEND|SUMMARY|DESCRIPTION|LOCATION|CATEGORIES|URL|GEO)[:;]*");
			foreach (var line in lines)
			{
				string leading_space = "";

				if (line.StartsWith(component))
					in_component = true;

				if (in_component && ! line.StartsWith(component) && !line.StartsWith(" "))
					leading_space = " ";

				if (in_component && !line.StartsWith(component) && re.Match(line).Success)
				{
					in_component = false;
					leading_space = "";
				}

				sb.Append(leading_space + line + "\n");
				
			}

			return sb.ToString();
		}

		public static string Handle_X_WR_TIMEZONE(string text)  
		{
			var has_x_wr_timezone = Regex.Match(text, "^X-WR-TIMEZONE", RegexOptions.Multiline).Success;
			var has_vtimezone = Regex.Match(text, "^BEGIN:VTIMEZONE", RegexOptions.Multiline).Success;

			if (has_x_wr_timezone == false)   // nothing to see here
				return text;

			if (has_vtimezone)                // VTIMEZONE supersedes
				return text;

			var x_wr_timezone_pattern = @"^X-WR-TIMEZONE:([^\r\n]+)[\r\n]+";
			var olson_name = Regex.Match(text, x_wr_timezone_pattern, RegexOptions.Multiline).Groups[1].Value.ToString();
			var tzinfo = Utils.TzInfoFromOlsonName(olson_name);

			var replace_patterns = new Dictionary<string, string>  // alter dates in target calendar, *before* adding VTIMEZONE which can't include TZIDs
				{
					{ "^DTSTART:(\\d)", "DTSTART;TZID=" + tzinfo.StandardName + ":$1" },
					{ "^DTEND:(\\d)", "DTEND;TZID=" + tzinfo.StandardName + ":$1"}
				};
			foreach (var replace_pattern in replace_patterns.Keys)
				text = Regex.Replace(text, replace_pattern, replace_patterns[replace_pattern], RegexOptions.Multiline);

			var ical_tmp = new iCalendar();                        // empty cal to hold fabricated VTIMEZONE
			Collector.AddTimezoneToDDayICal(ical_tmp, tzinfo);
			var serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer();
			string vtimezone = serializer.SerializeToString(ical_tmp);
			var delete_patterns = new List<string>()              // things to delete from the vtimezone-only calendar
				{
					@"^BEGIN:VCALENDAR.+[\r\n]+",
					@"^VERSION:.+[\r\n]+",
					@"^PRODID:.+[\r\n]+",
					@"^END:VCALENDAR.+[\r\n]+"
				};
			foreach (var pattern in delete_patterns)               // reduce to just VTIMEZONE
				vtimezone = Regex.Replace(vtimezone, pattern, "", RegexOptions.Multiline);

			text = Regex.Replace(text, x_wr_timezone_pattern, vtimezone, RegexOptions.Multiline); // swap VTIMEZONE for X-WR-TIMEZONE

			return text;
		}

		public static string NormalizeEventfulUrl(string url)
		{
			if ( url.StartsWith("http://eventful") )
				return url.Replace("/events/", "/");
			else 
				return url;
		}

		public static string NormalizeUpcomingUrl(string url)
		{
			if ( url.StartsWith("http://upcoming") )
			{
				var trimchars = new char[1];
				trimchars[0] = '/';
				return url.TrimEnd(trimchars);
			}
			else
				return url;
		}

		public static string DiscardMisfoldedDescriptionsAndBogusCategoriesThenAddEasternVTIMEZONE(Uri uri) // work around the disaster of http://events.umich.edu/month/feed/ical
		{
			var s = HttpUtils.FetchUrl(uri).DataAsString();
			s = s.Replace("\r", "");
			s = s.Replace("\n", "_NEWLINE_");
			s = Regex.Replace(s, "DESCRIPTION:.+?UID:", "UID:");
			s = Regex.Replace(s, "CATEGORIES:.+?LOCATION:", "LOCATION:");
			s = s.Replace("_NEWLINE_", "\n");
			s = s.Replace("TZID=US/Eastern", "TZID=Eastern Standard Time");
			s = s.Replace("CALSCALE:GREGORIAN", @"CALSCALE:GREGORIAN
BEGIN:VTIMEZONE
TZID:Eastern Standard Time
BEGIN:STANDARD
DTSTART:20101102T020000
RRULE:FREQ=YEARLY;BYDAY=1SU;BYHOUR=2;BYMINUTE=0;BYMONTH=11
TZNAME:Eastern Standard Time
TZOFFSETFROM:-0400
TZOFFSETTO:-0500
END:STANDARD
BEGIN:DAYLIGHT
DTSTART:20100301T020000
RRULE:FREQ=YEARLY;BYDAY=2SU;BYHOUR=2;BYMINUTE=0;BYMONTH=3
TZNAME:Eastern Daylight Time
TZOFFSETFROM:-0500
TZOFFSETTO:-0400
END:DAYLIGHT
END:VTIMEZONE");
			return s;
		}

		public static string SerializeIcalToIcs(DDay.iCal.iCalendar ical)
		{
			var serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer();
			var ics_text = serializer.SerializeToString(ical);
			return ics_text;
		}

		public static List<string> GetRegionIds()
		{
			var dicts = ts.QueryAllEntitiesAsListDict("regions", "").list_dict_obj;
			var ids = dicts.Select(x => x["PartitionKey"].ToString()).ToList();
			return ids;
		}

		public static bool IsRegion(string id)
		{
			return GetRegionIds().HasItem(id);
		}

		public static List<string> GetIdsForRegion(string region)
		{
			var q = string.Format("$filter=PartitionKey eq '{0}'", region);
			var dict = TableStorage.QueryForSingleEntityAsDictStr(ts, "regions", q);
			var ids = dict["ids"].Split(',').ToList();
			ids.Sort();
			return ids;
		}

		public static List<string> RegionsBelongedTo(string id)
		{
			var regions = ts.QueryAllEntitiesAsListDict("regions", "$filter=PartitionKey ne ''").list_dict_obj;
			var list = new List<string>();
			foreach (var region in regions)
			{
				var region_id = region["PartitionKey"].ToString();
				var test_ids = region["ids"].ToString().Split(',');
				foreach (var test_id in test_ids)
					if (id == test_id)
					{
						list.Add(region_id);
						break;
					}
			}
			list.Sort();
			return list;
		}

		public static string StartsWithUrl(string text)
		{
			string url = null;
			try
			{
				var re = new Regex(@"^(https?)\://[A-Za-z0-9\.\-]+(/[A-Za-z0-9\?\&\=;\+!'\(\)\*\-\._~%]*)*");
				var m = re.Matches(text);
				if (m.Count > 0)
					url = m[0].ToString();
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "FindUrlInText: " + text, e.Message + e.StackTrace);
			}
			return url;
		}

		public static void PostExcludedEvent(string elmcity_id, string title, string start, string source)
		{
			var dict = new Dictionary<string, object>()
			{
				{ "elmcity_id"	,	elmcity_id },
				{ "title"		,	title },
				{ "start"		,	start },
				{ "source"		,	source },
				{ "when"		,	DateTime.UtcNow },
				{ "active"		,	true }
			};

			var rowkey = TableStorage.MakeSafeRowkey(elmcity_id + title + start + source);
			TableStorage.UpdateDictToTableStore(dict, "exclusions", elmcity_id, rowkey);
		}

		public static List<string> GetTagsFromJson(string id)
		{
			var url = BlobStorage.MakeAzureBlobUri(id, "tags.json", false);
			var json = HttpUtils.FetchUrl(url).DataAsString();
			var tag_dicts = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);
			var list = new List<string>();
			foreach (var tag_dict in tag_dicts)
				list.Add(tag_dict.Keys.First());
			return list;
		}

		/*
		public static List<string> GetTagsFromJson(string id)
		{
			var json = Utils.DeserializeObjectFromJson<List<Dictionary<String, String>>>(id, id + ".feeds.json");
			var tags =
				from dict in json
				where dict.ContainsKey("category") && !String.IsNullOrEmpty(dict["category"])
				from tag in dict["category"].Split(',')
				select tag.ToLower();
			return tags.Distinct().ToList();
		}*/

		public static bool UrlParameterIsTrue(string param)
		{
			return ( param != null && param.ToLower() == "yes" ) ? true : false;
		}

		public static string GetCachedFeedText(string feedurl)
		{
			var blob_name = BlobStorage.MakeSafeBlobnameFromUrl(feedurl);
			var blob_uri = BlobStorage.MakeAzureBlobUri("feedcache", blob_name, false);
			if (BlobStorage.ExistsBlob(blob_uri))
				return HttpUtils.FetchUrl(blob_uri).DataAsString();
			else
				return "";
		}

		public static void SaveFeedTextToCache(string feedurl, string feedtext)
		{
			var blob_name = BlobStorage.MakeSafeBlobnameFromUrl(feedurl);
			bs.PutBlob("feedcache", blob_name, feedtext, "text/calendar");
		}

		public static void SaveFeedObjToCache(string id, string feedurl, ZonedEventStore es)
		{
			var bs = BlobStorage.MakeDefaultBlobStorage();
			var blob_name = Utils.MakeCachedFeedObjName(id, feedurl);
			bs.SerializeObjectToAzureBlob(es, "feedcache", blob_name);
		}

		public static bool CachedFeedObjExists(string id, string feedurl)
		{
			var cached_obj_name = Utils.MakeCachedFeedObjName(id, feedurl);
			var cached_obj_uri = BlobStorage.MakeAzureBlobUri("feedcache", cached_obj_name);
			return BlobStorage.ExistsBlob(cached_obj_uri);
		}

		public static BlobStorageResponse DeleteCachedFeedObj(string id, string feedurl)
		{
			var cached_obj_name = Utils.MakeCachedFeedObjName(id, feedurl);
			return bs.DeleteBlob(id, cached_obj_name);
		}

		public static ZonedEventStore GetFeedObjFromCache(Calinfo calinfo, string feedurl)
		{
			var blob_name = Utils.MakeCachedFeedObjName(calinfo.id, feedurl);
			var uri = BlobStorage.MakeAzureBlobUri("feedcache", blob_name, false);
			if (BlobStorage.ExistsBlob(uri))
				return (ZonedEventStore)BlobStorage.DeserializeObjectFromUri(uri);
			else
				return new ZonedEventStore(calinfo, SourceType.ical);
		}

		public static void SaveFeedJsonToCache(string feedurl, ZonelessEventStore es)
		{
			var blob_name = BlobStorage.MakeSafeBlobnameFromUrl(feedurl) + ".json";
			Utils.SerializeObjectToJson(es, "feedcache", blob_name);
		}

		public static List<string> WordsFromEventTitle(string title, int min_word_length)
		{
			var re = new Regex("[^\\s,:;\\?\\(\\)]+");
			var final_words = new List<string>();

			var words = new HashSet<string>();
			foreach (var m in re.Matches(title))     // make set of words in title
				words.Add(m.ToString().ToLower());

			foreach ( var word in words )
			{
				if (word.Length <= min_word_length)  // exclude shorter than 3
					continue;

				int n;                 // exclude starts with number
				if ( int.TryParse(word.First().ToString(), out n) ) 
					continue;

				if (CalendarAggregator.Search.days.Exists(w => w == word)) // skip days of week 
					continue;

				final_words.Add(word);
			}
			final_words.Sort();
			return final_words;
		}

		public static Dictionary<string, string> GetMetadataFromDescription(List<string> metakeys, string description)
		{
			var dict = GenUtils.RegexFindKeysAndValues(metakeys, description);

			if (dict.ContainsKey("url"))               // verify it
			{
				var url = dict["url"];
				try { var uri = new Uri(url); }
				catch { dict.Remove("url"); }

			}
			if (dict.ContainsKey("category"))          // ensure legal strings
			{
				var category = dict["category"];
				var legal_catstring = new Regex("[\\w\\-,]+$");
				if (!legal_catstring.IsMatch(category))
					dict.Remove("category");
			}
			return dict;
		}

		public static string RemoveCommentSection(string s, string name)
		{
			var pat = string.Format("<!-- {0} -->.+<!-- /{0} -->", name);
			var re = new Regex(pat, RegexOptions.Singleline);
			return re.Replace(s, "");
		}

		public static Dictionary<string,int> GetSmartPhoneScreenDimensions()
		{
			var screendata = ts.QueryAllEntitiesAsListDict("mobilescreens", "$filter=Type eq 'Smartphone'").list_dict_obj;
			var pairs = screendata.Select(pair => new List<string>() { pair["Width"] as string, pair["Height"] as string });
			var sorted_pairs = new Dictionary<string, int>();
			foreach (var pair in pairs)
			{
				var w = Convert.ToInt32(pair[0]);
				var h = Convert.ToInt32(pair[1]);
				var l = new List<int>() { w, h };
				l.Sort();
				sorted_pairs.IncrementOrAdd(l[0].ToString() + "x" + l[1].ToString());
			}
			return sorted_pairs;
		}

		public static string MaybeChangeWebcalToHttp(string feedurl)
		{
			if (feedurl.StartsWith("webcal:"))
				feedurl = feedurl.Replace("webcal:", "http:");

			if (feedurl.StartsWith("webcals:"))
				feedurl = feedurl.Replace("webcals:", "https:");
			return feedurl;
		}

		public static string MakeAboutPage(string id, Dictionary<string,string> settings, string auth_mode)
		{
			var calinfo = Utils.AcquireCalinfo(id);
			var authenticated = auth_mode != null;

			List<string> regions_belonged_to = new List<string>();
			List<string> ids_for_region = new List<string>();
			bool is_contained = false;
			bool is_container = false;

			if (calinfo.hub_enum == HubType.where)
			{
				regions_belonged_to = Utils.RegionsBelongedTo(id);
				is_contained = regions_belonged_to.Count > 0;
			}

			if (calinfo.hub_enum == HubType.region)
			{
				ids_for_region = Utils.GetIdsForRegion(id);
				is_container = ids_for_region.Count > 0;
			}

			var is_standalone = (!is_contained && !is_container) ||  // where hub that is a member of region or a region with members
								calinfo.hub_enum == HubType.what;    // what hub

			var url = BlobStorage.MakeAzureBlobUri("admin", "hubfiles.tmpl", false);
			var page = HttpUtils.FetchUrl(url).DataAsString();

			if (!is_standalone)
				page = page.Replace("__REGION_DISPLAY_STYLE__", "block");
			else
				page = page.Replace("__REGION_DISPLAY_STYLE__", "none");

			string region_msg = "";

			if (is_contained)
			{
				region_msg = @"<p>Regions that contain this hub:</p> <ul style=""list-style-type:none"">";
				foreach (var container in regions_belonged_to)
					region_msg += string.Format(@"<li><p><a href=""http://{0}/{1}"">{1}</a></p></li>", 
						ElmcityUtils.Configurator.appdomain,	// 0
						container);								// 1
				region_msg += "</ul>";
				var edit_feeds_link = MakeEditFeedsLink(id, auth_mode);
				region_msg += "<p>" + edit_feeds_link + "</p>";
			}

			if (is_container)
			{
				region_msg = @"<p>This is a regional hub. The hubs that belong to this region are: </p><ul style=""list-style-type:none"">";
				foreach (var contained in ids_for_region)
				{
					var edit_feeds_link = MakeEditFeedsLink(contained, auth_mode);
					region_msg += string.Format(@"<li><p><a href=""http://{0}/{1}"">{1}</a> {2} </p></li>",
						ElmcityUtils.Configurator.appdomain,	// 0
						contained,								// 1
						edit_feeds_link);						// 2
				}
				region_msg += "</ul>";
			}

			page = page.Replace("__REGION__", region_msg);

			page = page.Replace("__APPDOMAIN__", ElmcityUtils.Configurator.appdomain);

			page = page.Replace("__ID__", id);

			page = page.Replace("__LOWERID__", id.ToLower());

			var query = string.Format("$filter=PartitionKey eq '{0}' and RowKey eq '{1}'", id, id);
			var dict = TableStorage.QueryForSingleEntityAsDictStr(ts, "metadata", query);

			var row_template = @"<tr><td>{0}</td><td style=""text-align:right"">{1}</td></tr>";

			string tbody = "";

			try
			{
				tbody += string.Format(row_template, "# of iCalendar feeds", dict["feed_count"]);

				if (is_contained || is_standalone)
				{
					var task = Scheduler.FetchTaskForId(id, TaskType.icaltasks);
					var dtstop = TimeZoneInfo.ConvertTimeFromUtc(task.stop, calinfo.tzinfo);
					var interval = Convert.ToInt16(settings["ical_aggregate_interval_hours"]);
					var time_format = "M/d/yyyy h:mm tt";
					if (dtstop != DateTime.MinValue)
					{
						var dtnext = dtstop.AddHours(interval);
						tbody += string.Format(row_template, "last scan of iCalendar feeds", dtstop.ToString(time_format));
						tbody += string.Format(row_template, "next scan at", dtnext.ToString(time_format));
					}
					else
					{
						tbody += string.Format(row_template, "next scan at", "in progress now");
					}
					tbody += string.Format(row_template, "# of events from iCalendar feeds", dict["ical_events"]);
					if (calinfo.hub_enum != HubType.what)
					{
						tbody += string.Format(row_template, "# of events from Eventful", dict["eventful_events"]);
						tbody += string.Format(row_template, "# of events from Upcoming", dict["upcoming_events"]);
						tbody += string.Format(row_template, "# of events from EventBrite", dict["eventbrite_events"]);
					}
				}
				else if (is_container)
					tbody += string.Format(row_template, "(see member hubs for details)", "");

			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "hubfiles", e.Message + e.StackTrace);
				tbody = "";
			}

			page = page.Replace("__HUBSTATS__", tbody);
			return page;
		}

		public static string MakeEditFeedsLink(string id, string auth_mode)
		{
			string edit_link = "";
			if (auth_mode != null)
				edit_link = string.Format(@"<span style=""font-size:smaller""><a href=""http://{0}/services/{1}/edit?flavor=feeds"">(edit feeds)</a></span>",
						ElmcityUtils.Configurator.appdomain,
						id
						);
			return edit_link;
		}

		public static iCalendar NewCalendarWithTimezone(TimeZoneInfo tzinfo)
		{
			var ical = new iCalendar();
			Collector.AddTimezoneToDDayICal(ical, tzinfo);
			return ical;
		}

		public static DDay.iCal.iCalendar GetDDayCalFromZonelessFeedObj(Calinfo calinfo, string feedurl)
		{
		var ical = NewCalendarWithTimezone(calinfo.tzinfo);
		var es_zoned = GetFeedObjFromCache(calinfo, feedurl);
		var es_zoneless = EventStore.ZonedToZoneless(calinfo.id, calinfo, es_zoned);
		foreach ( var evt in es_zoneless.events )
			{
			var dtstart = new DateTimeWithZone(evt.dtstart, calinfo.tzinfo);
			var dtend = new DateTimeWithZone(evt.dtend, calinfo.tzinfo);
			var evt_tmp = Collector.MakeTmpEvt(calinfo, dtstart, dtend, evt.title, evt.url, evt.location, evt.description, evt.lat, evt.lon, evt.allday);
			Collector.AddEventToDDayIcal(ical, evt_tmp);
			}
		return ical;
		}

		public static DDay.iCal.iCalendar ParseIcs(string ics)
		{
			StringReader sr = new StringReader(ics);
			IICalendarCollection icals = iCalendar.LoadFromStream(sr);
			return (DDay.iCal.iCalendar)icals.First().iCalendar;
		}

		public static string MakeCachedFeedObjName(string id, string feedurl)
		{
			return id + "_" + BlobStorage.MakeSafeBlobnameFromUrl(feedurl) + ".obj";
		}

		public static Dictionary<string, Dictionary<string, string>> GetThemesDict()
		{
			var themes = new Dictionary<string, Dictionary<string, string>>();
			try
			{
				var themes_json_uri = BlobStorage.MakeAzureBlobUri("admin", "themes.json");
				var themes_json = HttpUtils.FetchUrl(themes_json_uri).DataAsString();
				themes = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(themes_json);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "GetThemesDict", e.Message + e.StackTrace);
				var themes_str = @"{
  ""default"" : {
    ""body""         :  "" { 'font-family':'verdana,arial,sans-serif', 'font-size':'10pt' } "",
    "".hubtitle""    :  "" { 'font-size' : '10pt', 'font-weight':'bold' } "",
    "".timeofday""   :  "" { 'font-size':'8pt', 'margin-left':'30%', 'margin-top':'0px', 'margin-bottom':'4pt' } "",
    ""#datepicker""  :  "" { 'font-size':'6pt', 'position':'fixed', 'left':'320px', 'top':'30px' } "",
    ""#sidebar""     :  "" { 'font-size':'smaller', 'position':'fixed', 'left':'320px', 'top':'280px', 'width':'150px' } "",
    ""#tags""        :  "" { 'text-align':'center', 'position' : 'fixed', 'font-size':'smaller', 'left':'330px', 'top':'200px', 'width':'150px' } "",
    "".bl""          :  "" { 'margin-right':'40%', 'margin-top':'0px', 'margin-bottom':'10pt', 'text-indent':'-1em', 'margin-left':'1em' } "",
    "".st""          :  "" { 'font-size':'smaller', 'color':'#333333'} "",
    "".menu li""     :  "" { 'list-style-type':'none', 'font-size':'smaller', 'line-height':'1.5' } "",
    "".ed""          :  "" { 'font-size':'9pt', 'font-weight':'bold' }"",
    ""a""            :  "" { 'color' : 'black', 'text-decoration' : 'none' }"",
    ""a:hover""      :  "" { 'color' : 'gray', 'text-decoration' : 'underline' }"",
    "".ttl""         :  "" {  }"",
    "".src""         :  "" { 'font-size':'smaller' } "",
    "".cat""         :  "" { 'font-size':'smaller' } "",
    "".sd""          :  "" { } "",
    "".sd a""        :  "" { } "",
    "".atc""         :  "" { } "",
    "".atc a""       :  "" { } "",
    "".desc""        :  "" { } ""
    }
}";
				themes = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(themes_str);
			}
			return themes;
		}

		// not using mobile_long at the moment, but might want it for scaling
		public static string GetCssTheme(Dictionary<string, Dictionary<string, string>> themes, string theme_name, bool mobile, string mobile_long, string ua)
		{
			var theme = new Dictionary<string, string>();
			try
			{
				theme = themes[theme_name];
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "GetCssTheme falling back to default", e.Message);
				if (themes.ContainsKey("default"))
					theme = themes["default"];
			}

			if (mobile)
			{
				try
				{
					theme[".bl"] = " { 'margin-bottom':'3%' } ";
					theme[".timeofday"] = " { 'display':'none' } ";
					theme[".hubtitle"] = " { 'display':'none' } ";
					theme[".ed"] = " { 'display':'none' } ";
					if (ua.Contains("Windows Phone"))   // todo: investigate viewport-based method
					{
						var body_dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(theme["body"]);
						body_dict.AddOrUpdateDictionary("font-size", "300%");  
						theme["body"] = JsonConvert.SerializeObject(body_dict);
					}
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "GetCssTheme: tweaking mobile settings", e.Message);
				}
			}

			var css_text = new StringBuilder();
			foreach (var selector in theme.Keys)
			{
				var decl_dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(theme[selector]);
				css_text.Append(string.Format("{0} {{\n", selector));
				foreach (var key in decl_dict.Keys)
					css_text.Append(string.Format(key + ":" + decl_dict[key] + ";\n"));
				css_text.Append("}\n\n");
			}

			return css_text.ToString();
		}

		public static iCalendar iCalFromFeedUrl(string feedurl, Dictionary<string,string> settings)
		{
			DDay.iCal.iCalendar ical = default(DDay.iCal.iCalendar);
			try
			{

				var feedtext = Encoding.UTF8.GetString(HttpUtils.FetchUrl(new Uri(feedurl)).bytes);
				feedtext = Collector.MassageFeedText(null, feedurl, feedtext, settings);
				return Collector.ParseTheFeed(feedtext);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "iCalFromFeedUrl: " + feedurl, e.Message + e.StackTrace);
			}
			return ical;
		}

		public static string iCalToJson(iCalendar ical, string tzname, int days)
		{
			var list_of_dict = new List<Dictionary<string, string>>();

			foreach (DDay.iCal.Event evt in ical.Events)
			{
				var tzinfo = Utils.TzinfoFromName(tzname);
				DateTime utc_midnight_in_tz = Utils.MidnightInTz(tzinfo).UniversalTime;
				DateTime then = utc_midnight_in_tz.AddDays(days);

				var occurrences = evt.GetOccurrences(utc_midnight_in_tz, then);
				foreach (Occurrence occurrence in occurrences)
				{
					try
					{
						if (Collector.IsCurrentOrFutureDTStartInTz(occurrence.Period.StartTime.UTC, tzinfo))
						{
							var instance = Collector.PeriodizeRecurringEvent(evt, occurrence.Period);
							DateTime dtstart = Utils.LocalDateTimeFromiCalDateTime((DDay.iCal.iCalDateTime)instance.DTStart, tzinfo);
							DateTime dtend = Utils.LocalDateTimeFromiCalDateTime((DDay.iCal.iCalDateTime)instance.DTEnd, tzinfo);

							var dict = new Dictionary<string, string>();
							dict.Add("summary", instance.Summary ?? "");
							if ( instance.Url != null)
								dict.Add("url", instance.Url.ToString());
							dict.Add("location", instance.Location ?? "");
							dict.Add("description", instance.Description ?? "");
							dict.Add("dtstart", dtstart.ToString("yyyy-MM-ddTHH:mm:ss"));
							dict.Add("dtend", dtend.ToString("yyyy-MM-ddTHH:mm:ss"));
							var cats = ical.Events[0].Categories.ToList();
							if ( cats.Count > 0 )
								dict.Add("categories", String.Join(",", ical.Events[0].Categories.ToList()));
							if (instance.GeographicLocation != null)
								dict.Add("geo", instance.GeographicLocation.ToString());
							list_of_dict.Add(dict);
						}
					}
					catch (Exception e)
					{
						GenUtils.PriorityLogMsg("exception", "RenderIcsAsJson", e.Message + e.StackTrace);
					}
				}
			}

			var json = JsonConvert.SerializeObject(list_of_dict);
			return GenUtils.PrettifyJson(json);
		}

		public static void ZeroCountForService(string id, string event_service)
		{
			try
			{
				var dict = Metadata.LoadMetadataForIdFromAzureTable(id);
				var key = event_service + "_events";
				dict[key] = "0";
				var dict_obj = ObjectUtils.DictStrToDictObj(dict);
				TableStorage.UpmergeDictToTableStore(dict_obj, "metadata", id, id);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ZeroEventCountForService", e.Message);
			}
		}

		public static void CheckFeedAndHomeUrls(string id)
		{
			var report = new StringBuilder();

			report.AppendLine(@"<html>
<head><style>th { text-align:left }</style></head>
<body>
<table style=""table-layout: fixed; width:100%; border-spacing:8px"">
<thead><th>id</th><th>source</th><th>feed well-formed</th><th>feed exists</th><th>feed parses</th><th>feed url</th><th>home well-formed</th><th>home exists</th><th>home url</th></thead>
<tbody>");

			var ids = new List<string>();

			if (Utils.IsRegion(id))
				ids = Utils.GetIdsForRegion(id);
			else
				ids.Add(id);

			foreach (var _id in ids)
			{
				var fr = new FeedRegistry(_id);
				fr.LoadFeedsFromAzure(FeedLoadOption.all);
				var feedurls = fr.feeds.Keys.ToList();
				feedurls.Sort();

				Parallel.ForEach(source: feedurls, body: (feedurl) =>
				{
					var metadict = Metadata.LoadFeedMetadataFromAzureTableForFeedurlAndId(feedurl, _id);
					var result = CheckFeedRegistryEntry(_id, fr, feedurl, metadict);
					if (result != null)
					{
						lock (report)
						{
							report.AppendLine(result);
						}
					}
				});
			}
			report.AppendLine(@"</tbody>
</body>
</html>");
			var html = report.ToString();
			bs.PutBlob(id, "registry_check.html", html, "text/html");
		}

		public static string CheckFeedRegistryEntry(string id, FeedRegistry fr, string feedurl, Dictionary<string, string> metadict)
		{
			bool feed_is_uri = false;
			bool feed_is_200 = false;
			bool feed_is_ical = false;

			bool home_is_uri = false;
			bool home_is_200 = false;

			Uri feed_uri = default(Uri);
			Uri home_uri = default(Uri);

			try
			{
				feed_uri = new Uri(feedurl);
				feed_is_uri = true;
				home_uri = new Uri(metadict["url"]);
				home_is_uri = true;
			}
			catch { }

			try
			{
				var r = HttpUtils.HeadFetchUrl(feed_uri);
				if (r.status == HttpStatusCode.OK)
					feed_is_200 = true;
				r = HttpUtils.HeadFetchUrl(home_uri);
				if (r.status == HttpStatusCode.OK)
					home_is_200 = true;
			}
			catch { }

			try
			{
				var feedtext = HttpUtils.FetchUrl(feed_uri).DataAsString();
				var ical = CalendarAggregator.Collector.ParseTheFeed(feedtext);
				if (ical != null)
					feed_is_ical = true;
				else
					feed_is_ical = false;
			}
			catch { }

			string result = null;

			var style = "";

			if (!feed_is_uri || !feed_is_200 || !feed_is_ical || !home_is_uri || !home_is_200)
				style = "background-color:rgb(206, 186, 186);";

			result = string.Format(@"<tr style=""{0}""><td>{1}</td><td>{2}<td>{3}</td><td>{4}</td><td>{5}</td><td>{6}</td><td>{7}</td><td>{8}</td><td>{9}</td></tr>",
				style, id, fr.feeds[feedurl], feed_is_uri, feed_is_200, feed_is_ical, feedurl, home_is_uri, home_is_200, home_uri);
			return result;

		}

		public static bool ShowEventfulBadge(Calinfo calinfo)
		{
			return calinfo.eventful;
		}

		public static bool ShowFacebookBadge(Calinfo calinfo)
		{
			var fr = new FeedRegistry(calinfo.id);
			fr.LoadFeedsFromAzure(FeedLoadOption.all);
			return fr.feeds.Keys.ToList().Exists(x => x.Contains("ics_from_fb_page"));
		}

		public static bool ShowMeetupBadge(Calinfo calinfo)
		{
			var fr = new FeedRegistry(calinfo.id);
			fr.LoadFeedsFromAzure(FeedLoadOption.all);
			return fr.feeds.Keys.ToList().Exists(x => x.Contains("www.meetup.com"));
		}

		public static bool ShowEventBriteBadge(Calinfo calinfo)
		{
			var uses_eventbrite_service = calinfo.eventbrite;
			var fr = new FeedRegistry(calinfo.id);
			fr.LoadFeedsFromAzure(FeedLoadOption.all);
			var uses_eventbrite_feeds =
				fr.feeds.Keys.ToList().Exists(x => x.Contains("get_ical_url_from_eid_of_eventbrite_event_page")) ||
				fr.feeds.Keys.ToList().Exists(x => x.Contains("ics_from_eventbrite_organizer_id"));
			return uses_eventbrite_service || uses_eventbrite_feeds;
		}

		public static bool RenderersAreEqual(CalendarRenderer r1, CalendarRenderer r2, List<string> except_keys)
		{
			var dict_renderer_1 = ObjectUtils.ObjToDictStr(r1);
			except_keys.ForEach(x => dict_renderer_1.Remove(x));

			var dict_calinfo_1 = ObjectUtils.ObjToDictStr(r1.calinfo);
			except_keys.ForEach(x => dict_calinfo_1.Remove(x));

			var dict_renderer_2 = ObjectUtils.ObjToDictStr(r2);
			except_keys.ForEach(x => dict_renderer_2.Remove(x));

			var dict_calinfo_2 = ObjectUtils.ObjToDictStr(r2.calinfo);
			except_keys.ForEach(x => dict_calinfo_2.Remove(x));

			return (
				ObjectUtils.DictStrEqualsDictStr(dict_renderer_1, dict_renderer_2) &&
				ObjectUtils.DictStrEqualsDictStr(dict_calinfo_1, dict_calinfo_2)
				);
		}

		#endregion

	}

	public class USCensusPopulationData
	{

#pragma warning disable 0169, 0414, 0649

		public string sumlev;
		public string state;
		public string county;
		public string place;
		public string cousub;
		public string name;
		public string statename;
		public string popcensus_2000;
		public string popbase_2000;
		public string pop_2000;
		public string pop_2001;
		public string pop_2002;
		public string pop_2003;
		public string pop_2004;
		public string pop_2005;
		public string pop_2006;
		public string pop_2007;
		public string pop_2008;
		public string pop_2009;


#pragma warning restore 0169, 0414, 0649

		public override string ToString()
		{
			return
				name + ", " + statename + " " +
				"pop2000=" + pop_2000 + " | " +
				"pop2008=" + pop_2008;
		}

		public static float percent(string a, string b)
		{
			return -((float.Parse(a) / float.Parse(b) * 100) - 100);
		}

		public static float diff(string a, string b)
		{
			return Convert.ToInt32(a) - Convert.ToInt32(b);
		}

	}

	public class LogFilter
	{
		private List<string> include;
		private List<string> exclude;
		private List<Dictionary<string, string>> entities;

		public enum op { include, exclude };

		public LogFilter(string include, string exclude)
		{
			this.include = String.IsNullOrEmpty(include) ? new List<string>() : include.Split(',').ToList();
			this.exclude = String.IsNullOrEmpty(exclude) ? new List<string>() : exclude.Split(',').ToList();
		}

		private void IncludeAll(List<string> targets)
		{
			foreach (var target in targets)
			{
				var _target = target.ToLower();
				this.entities.RemoveAll( e => 
					! e["type"].ToLower().Contains(_target)    &&
					! e["message"].ToLower().Contains(_target) && 
					! e["data"].ToLower().Contains(_target) 
					);
			}
		}

		private void ExcludeAny(List<string> targets)
		{
			foreach (var target in targets)
			{
				var _target = target.ToLower();
				this.entities.RemoveAll( e => 
					e["type"].ToLower().Contains(_target)    ||
					e["message"].ToLower().Contains(target)  || 
					e["data"].ToLower().Contains(target) 
					);
			}
		}

		public void SetEntities(List<Dictionary<string,string>> entities)
		{
			this.entities = entities;
		}

		public List<Dictionary<string, string>> Apply()
		{
			IncludeAll(this.include);
			ExcludeAny(this.exclude);
			return this.entities;
		}
	}

	#region extensions

	public static class DDayExtensions
	{
		public static string TitleAndTime(this DDay.iCal.Event evt)
		{
			return evt.Summary.ToString().ToLower() + evt.Start.ToString();
		}
	}

	public static class ZonelessEventExtensions
	{
		public static string TitleAndTime(this ZonelessEvent evt)
		{
			return evt.title.ToLower() + evt.dtstart.ToString();
		}

	}



	#endregion

}

