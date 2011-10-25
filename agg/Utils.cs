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
using System.Diagnostics;
using System.IO;
using System.Linq;
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

		#endregion

		#region datetime

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

		public static DateTime LocalDateTimeFromFacebookDateStr(string str_dt, TimeZoneInfo tzinfo)
		{
			var dt = DateTimeFromISO8601DateStr(str_dt, DateTimeKind.Local);     // 2010-07-18 01:00
			// off by 7 or 8 hours: why?
			var adjusted_dt = dt - new TimeSpan(Configurator.facebook_mystery_offset_hours, 0, 0);    // 2010-07-17 18:00
			return adjusted_dt;
		}

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

		public static bool IsCurrentOrFutureDateTime(ZonelessEvent evt, System.TimeZoneInfo tzinfo)
		{
			var dt = evt.dtstart;
			var utc_last_midnight = Utils.MidnightInTz(tzinfo);
			return dt.ToUniversalTime() >= utc_last_midnight.UniversalTime;
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

			if (Configurator.MORNING_BEGIN <= dt && dt < Configurator.LUNCH_BEGIN)
				return TimeOfDay.Morning;

			else if (Configurator.LUNCH_BEGIN <= dt && dt < Configurator.AFTERNOON_BEGIN)
				return TimeOfDay.Lunch;

			else if (Configurator.AFTERNOON_BEGIN <= dt && dt < Configurator.EVENING_BEGIN)
				return TimeOfDay.Afternoon;

			else if (Configurator.EVENING_BEGIN <= dt && dt < Configurator.NIGHT_BEGIN)
				return TimeOfDay.Evening;

			else if
					(
					   (Configurator.MIDNIGHT_LAST < dt && dt < Configurator.WEE_HOURS_BEGIN) ||
					   (Configurator.NIGHT_BEGIN <= dt && dt < Configurator.MIDNIGHT_NEXT)
					)
				return TimeOfDay.Night;

			else if (Configurator.WEE_HOURS_BEGIN <= dt && dt < Configurator.MORNING_BEGIN)
				return TimeOfDay.WeeHours;

			else
				return TimeOfDay.AllDay;
		}

		public static DateTime MakeCompYear(DateTime dt)
		{
			return new DateTime(
				Configurator.DT_COMP_YEAR,
				Configurator.DT_COMP_MONTH,
				Configurator.DT_COMP_DAY,
				dt.Hour,
				dt.Minute,
				dt.Second);
		}

		#endregion datetime

		#region population

		public static string[] FindCityOrTownAndStateAbbrev(string where)
		{
			var city_or_town = "";
			var state_abbrev = "";
			var groups = GenUtils.RegexFindGroups(where, @"(.+)([\s+,])([^\s]+)");
			if (groups.Count > 1)
			{
				city_or_town = groups[1];
				state_abbrev = groups[3];
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
			var pop = 1;
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
				var matching = list.FindAll(row => row.name.StartsWith(target_city) && row.statename == target_state);
				if (matching.Count >= 1)
					pop = Convert.ToInt32(matching[0].pop_2008);

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

		public static string GetRecentLogEntries(string log, string type, int minutes, string targets)
		{
			var sb = new StringBuilder();
			var dt = Since(minutes);

			var filters = new List<LogFilter>();

			List<string> target_list = new List<string>();
			if ( targets != null )
				target_list = targets.Split(',').ToList();

			if (type != null)
				filters.Add( new LogFilter( type, null) );

			foreach (var target in target_list)
				filters.Add(new LogFilter(null, target));

			GetLogEntriesLaterThanTicks(log, dt.Ticks, filters, sb);
			return sb.ToString();
		}

		public static string GetRecentMonitorEntries(int minutes, string conditions)
		{
			var sb = new StringBuilder();
			var dt = Since(minutes);
			GetMonitorEntriesLaterThanTicks(dt.Ticks, Configurator.process_monitor_table, conditions, sb);
			return sb.ToString();
		}

		public static void GetTableEntriesLaterThanTicks(string tablename, string partition_key, string row_key, string conditions, List<Dictionary<string, string>> entities)
		{
			string query = String.Format("$filter=(PartitionKey eq '{0}') and (RowKey gt '{1}')", partition_key, row_key);
			if (String.IsNullOrEmpty(conditions) == false)
				query += conditions;
			//TableStorageResponse r = ts.QueryEntities(tablename, query);
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
			var new_ticks = dict_str["RowKey"];
			GetTableEntriesLaterThanTicks(tablename, partition_key, row_key: new_ticks, conditions: conditions, entities: entities);
		}

		public static List<Dictionary<string, string>> FilterByAny(List<Dictionary<string, string>> entries, string any_string)
		{
			return entries.FindAll(entry => (entry["message"].Contains(any_string)));
		}

		public static List<Dictionary<string, string>> FilterByType(List<Dictionary<string, string>> entries, string type)
		{
			return entries.FindAll(entry => (entry["type"].Contains(type)));
		}

		public static void GetLogEntriesLaterThanTicks(string log, long ticks, List<CalendarAggregator.LogFilter> filters, StringBuilder sb)
		{
			var entities = new List<Dictionary<string, string>>();
			GetTableEntriesLaterThanTicks(tablename: log, partition_key: "log", row_key: ticks.ToString(), conditions: null, entities: entities );
			foreach (var filter in filters)
			{
				filter.SetEntities(entities);
				entities = filter.Apply();
			}
			FormatLogEntries(sb, entities);
		}

		private static void FormatLogEntries(StringBuilder sb, List<Dictionary<string, string>> filtered_entries)
		{
			foreach (var filtered_entry in filtered_entries)
				sb.AppendLine(FormatLogEntry(filtered_entry));
		}

		public static void GetMonitorEntriesLaterThanTicks(long ticks, string tablename, string conditions, StringBuilder sb)
		{
			var entries = new List<Dictionary<string, string>>();
			GetTableEntriesLaterThanTicks(tablename: tablename, partition_key: tablename, row_key: ticks.ToString(), conditions: conditions, entities: entries);
			FormatLogEntries(sb, entries);
		}

		public static string FormatLogEntry(Dictionary<string, string> dict)
		{
			var str_timestamp = dict["Timestamp"];      //10/23/2009 2:26:51 AM
			var pattern = @"(\d+/\d+)/2\d\d\d(.+)";
			var re = new Regex(pattern);
			str_timestamp = re.Replace(str_timestamp, "$1 $2", 1);
			var s = String.Format("{0} UTC {1} {2} {3}",
				   str_timestamp, dict["type"], dict["message"], dict["data"]);
			return s;
		}

		#endregion

		#region xcal

		static public string IcsFromRssPlusXcal(string rss_plus_xcal_url, string source, TimeZoneInfo tzinfo)
		{
			XNamespace xcal = "urn:ietf:params:xml:ns:xcal";
			XNamespace geo = "http://www.w3.org/2003/01/geo/wgs84_pos#";
			//var uri = new Uri("http://events.pressdemocrat.com/search?city=Santa+Rosa&new=n&rss=1&srad=90&svt=text&swhat=&swhen=&swhere=");
			var rss = HttpUtils.FetchUrl(new Uri(rss_plus_xcal_url));
			var xdoc = XmlUtils.XdocFromXmlBytes(rss.bytes);
			var itemquery = from items in xdoc.Descendants("item") select items;
			var ical = new DDay.iCal.iCalendar();
			Collector.AddTimezoneToDDayICal(ical, tzinfo);

			foreach (var item in itemquery)
			{
				var title = item.Element("title").Value;
				var url = item.Element("link").Value;
				var dtstart = Utils.LocalDateTimeFromLocalDateStr(item.Element(xcal + "dtstart").Value);
				var dtstart_with_zone = new DateTimeWithZone(dtstart, tzinfo);
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
				var evt = Collector.MakeTmpEvt(null, dtstart_with_zone, DateTimeWithZone.MinValue(tzinfo), tzinfo, tzinfo.Id, title, url: url, location: location, description: source, lat: lat, lon: lon, allday: false);
				Collector.AddEventToDDayIcal(ical, evt);
			}
			var serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer(ical);
			var ics_text = serializer.SerializeToString(ical);
			return ics_text;
		}

		#endregion

		#region vcal

		static public string IcsFromAtomPlusVCalAsContent(string atom_plus_vcal_url, string source, TimeZoneInfo tzinfo)
		{
			//var uri = new Uri("http://www.techhui.com/events/event/feed");  (should work for all ning sites)
			var ns = StorageUtils.atom_namespace;

			var atom = HttpUtils.FetchUrl(new Uri(atom_plus_vcal_url));
			var xdoc = XmlUtils.XdocFromXmlBytes(atom.bytes);
			var entryquery = from items in xdoc.Descendants(ns + "entry") select items;
			var ical = new DDay.iCal.iCalendar();
			Collector.AddTimezoneToDDayICal(ical, tzinfo);

			foreach (var entry in entryquery)
			{
				var title = entry.Element(ns + "title").Value;
				var url = entry.Element(ns + "link").Attribute("href").Value;
				var dtstart_str = entry.Descendants(ns + "dtstart").First().Value;
				var dtstart = Utils.DateTimeFromICalDateStr(dtstart_str, DateTimeKind.Local);
				var dtstart_with_zone = new DateTimeWithZone(dtstart, tzinfo);
				var location = entry.Descendants(ns + "location").First().Value;
				var evt = Collector.MakeTmpEvt(null, dtstart_with_zone, DateTimeWithZone.MinValue(tzinfo), tzinfo, tzinfo.Id, title, url: url, location: location, description: source, lat: null, lon: null, allday: false);
				Collector.AddEventToDDayIcal(ical, evt);
			}
			var serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer();
			var ics_text = serializer.SerializeToString(ical);
			return ics_text;
		}

		#endregion

		#region fbpage

		static public string IcsFromFbPage(string fb_id, string elmcity_id, Dictionary<string,string> settings)
		{
			var facebook_access_token = settings["facebook_access_token"];

			// https://graph.facebook.com/https://graph.facebook.com/142525312427391/events?access_token=...
			var graph_uri_template = "https://graph.facebook.com/{0}/events?access_token={1}";
			var graph_uri = new Uri(string.Format(graph_uri_template, fb_id, facebook_access_token));

			var json = HttpUtils.FetchUrl(graph_uri).DataAsString();

			var calinfo = new Calinfo(elmcity_id);
			var tzinfo = calinfo.tzinfo;
			var ical = new DDay.iCal.iCalendar();
			Collector.AddTimezoneToDDayICal(ical, tzinfo);

			var dict = JsonConvert.DeserializeObject(json);

			var j_obj = (JObject)JsonConvert.DeserializeObject(json);

			foreach (JObject event_dict in j_obj["data"])
			{
				var title = event_dict["name"].Value<string>();
				var url = string.Format("http://www.facebook.com/pages/{0}?sk=events", fb_id);
				var dtstart = Utils.LocalDateTimeFromFacebookDateStr(event_dict["start_time"].Value<string>(), tzinfo);
				var dtstart_with_zone = new DateTimeWithZone(dtstart, calinfo.tzinfo);
				string location = "";
				string evt_id = "";
				try
				{
					location = event_dict["location"].Value<string>();
					evt_id = event_dict["id"].Value<string>();
					url = "http://www.facebook.com/event.php?eid=" + evt_id;
				}
				catch (Exception e)
				{
					GenUtils.LogMsg("warning", "ICcsFromFbPage: empty location and/or evt id: " + title + " " + fb_id, e.Message + e.StackTrace);
				}

				var evt = Collector.MakeTmpEvt(null, dtstart_with_zone, DateTimeWithZone.MinValue(tzinfo), tzinfo, tzinfo.Id, title, url: url, location: location, description: location, lat: calinfo.lat, lon: calinfo.lon, allday: false);
				Collector.AddEventToDDayIcal(ical, evt);
			}

			var ical_serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer(ical);
			var ics_text = ical_serializer.SerializeToString(ical);
			return ics_text;
		}

		#endregion

		#region metadata history

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
				dict.Add(k, d);
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
			// var exclude_keys = new List<string>() { "PartitionKey", "RowKey", "Timestamp", "contribute_url", "default_img_html", 
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
			var script = HttpUtils.FetchUrl(new Uri("http://elmcity.blob.core.windows.net/admin/metadata_chooser_handler.tmpl")).DataAsString();
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
			foreach (var id in ids)
				RecreatePickledCalinfoAndRenderer(id);
		}

		#endregion

		#region uses_foreign_auth

		public static bool ElmcityIdUsesForeignAuth(string table, string id)
		{
			try
			{
				var q = String.Format("$filter=RowKey eq '{0}'", id);
				var list = ts.QueryEntities(table, q).list_dict_obj;
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

		public static WebRoleData GetWrd()
		{
			var uri = BlobStorage.MakeAzureBlobUri("admin","wrd.obj");
			return (WebRoleData) BlobStorage.DeserializeObjectFromUri(uri);
		}

		public static string GetWhereSummary()
		{
			var uri = BlobStorage.MakeAzureBlobUri("admin", "where_summary.html");
			return HttpUtils.FetchUrl(uri).DataAsString();
		}

		public static void MakeWhereSummary()
		{
			var wrd = GetWrd();

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
			//foreach (var id in WebRoleData.where_ids)
			foreach (var id in wrd.where_ids)
			{
				if (IsReady(wrd, id) == false)
					continue;
				var calinfo = Utils.AcquireCalinfo(id);
				var population = calinfo.population;
				var metadict = Metadata.LoadMetadataForIdFromAzureTable(id);
				var events = metadict.ContainsKey("events") ? metadict["events"] : "";
				var events_per_person = metadict.ContainsKey("events_per_person") ? metadict["events_per_person"] : "";
				var row = string.Format(row_template,
					String.Format(@"<a title=""view outputs"" href=""/services/{0}"">{0}</a>", id),
					metadict["where"],
					population != 0 ? population.ToString() : "",
					events,
					population != 0 ? events_per_person : ""
					);
				summary.Append(row);
			}
			summary.Append("</table>");

			bs.PutBlob("admin", "where_summary.html", summary.ToString());
		}

		public static string GetWhatSummary()
		{
			var uri = BlobStorage.MakeAzureBlobUri("admin", "what_summary.html");
			return HttpUtils.FetchUrl(uri).DataAsString();
		}

		public static void MakeWhatSummary()
		{
			var wrd = GetWrd();

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
				if (IsReady(wrd, id) == false)
					continue;
				var row = string.Format(row_template,
					String.Format(@"<a title=""view outputs"" href=""/services/{0}"">{0}</a>", id)
					);
				summary.Append(row);
			}
			summary.Append("</table>");

			bs.PutBlob("admin", "what_summary.html", summary.ToString());
		}

		public static bool IsReady(WebRoleData wrd, string id)
		{
			return wrd.ready_ids.Contains(id);
		}

		#endregion

		#region meetup

		public static string FindMeetupGroups(Calinfo calinfo, int delay, Dictionary<string,string> settings)
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
			var ids = from result in results
					  select Convert.ToString(result["group"]["id"].Value<int>());
			var uniques = ids.ToList().Unique();

			var group_infos = new List<Dictionary<string, string>>();

			foreach (var group_id in uniques)
			{
				try
				{
					template = "https://api.meetup.com/2/groups?key={0}&group_id={1}";
					url = String.Format(template,
							meetup_key,
							group_id);
					json = HttpUtils.SlowFetchUrl(new Uri(url), delay).DataAsString();
					dict = JsonConvert.DeserializeObject<Dictionary<String, object>>(json);
					results = (JArray)dict["results"];
					var result = results.First();
					var name = result["name"].Value<string>();
					var urlname = result["urlname"].Value<string>();
					group_infos.Add(
						new Dictionary<string, string>() 
					{
						{ "name", name },
						{ "urlname", urlname },
						{ "homepage", "http://www.meetup.com/" + urlname },
						{ "ical", string.Format("http://www.meetup.com/{0}/events/ical/{1}/", 
							urlname,
							Uri.EscapeUriString(name) ) }
					}
					);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "FindMeetupGroups", e.Message + e.StackTrace);
				}
			}

			var html = new StringBuilder();

			foreach (var group_info in group_infos)
			{
				var tmpl = @"<p><a href=""{0}"">{1}</a> (<a href=""{0}"">iCal feed</a>)</p>";
				var chunk = string.Format(tmpl,
					group_info["homepage"],
					group_info["name"],
					group_info["ical"]
					);
				html.Append(chunk);
			}

			return html.ToString();

		}

		#endregion

		#region other

		public static int UpdateFeedCount(string id)
		{
			var fr = new FeedRegistry(id);
			fr.LoadFeedsFromAzure(FeedLoadOption.all);
			var new_feed_count = fr.feeds.Count();
			var dict = new Dictionary<string, object>() { { "feed_count", new_feed_count.ToString() } };
			TableStorage.DictObjToTableStore(TableStorage.Operation.merge, dict, "metadata", id, id);
			return new_feed_count;
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

		public static string MakeBaseUrl(string id)
		{
			return string.Format("{0}/{1}/{2}.zoneless.obj",
				ElmcityUtils.Configurator.azure_blobhost,
				BlobStorage.LegalizeContainerName(id),
				id);
		}

		public static string MakeViewKey(string id, string type, string view, string count)
		{
			return string.Format("/services/{0}/{1}?view={2}&count={3}", id, type, view, count);
		}

		public static void RemoveBaseCacheEntry(string id)
		{
			var cached_base_uri = MakeBaseUrl(id);
			var url = string.Format("http://{0}/services/remove_cache_entry?cached_uri={1}",
				ElmcityUtils.Configurator.appdomain,
				cached_base_uri);
			var result = HttpUtils.FetchUrl(new Uri(url));
		}

		// convert a feed url into a base-64-encoded and uri-escaped string
		// that can be used as an azure table rowkey
		public static string MakeSafeRowkeyFromUrl(string feedurl)
		{
			var b64array = Encoding.UTF8.GetBytes(feedurl);
			return Uri.EscapeDataString(Convert.ToBase64String(b64array)).Replace('%', '_');
		}

		public static string EmbedHtmlSnippetInDefaultPageWrapper(Calinfo calinfo, string snippet, string title)
		{

			return string.Format(@"
<html>
<head> 
<title>{0}: {1}</title>
<link type=""text/css"" rel=""stylesheet"" href=""{2}"">
</head>
<body>
{3}
</body>
</html>
",
		   calinfo.id,
		   title,
		   calinfo.css,
		   snippet);
		}

		public static T DeserializeObjectFromJson<T>(string containername, string blobname)
		{
			var uri = BlobStorage.MakeAzureBlobUri(containername, blobname);
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
				var calinfo_uri = BlobStorage.MakeAzureBlobUri(id, name);
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
				var renderer_uri = BlobStorage.MakeAzureBlobUri(id, name);
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

		public static string RemoveAttendeeComponent(string calendar_text)  // todo: remove when DDay.ICal can parse ATTENDEE
		{
			var lines = calendar_text.Split('\n');
			var sb = new StringBuilder();
			bool in_attendee = false;
			foreach (var line in lines)
			{
				if (in_attendee && line.StartsWith(" "))
					continue;

				if (in_attendee && !line.StartsWith(" "))
				{
					in_attendee = false;
				}

				if (line.StartsWith("ATTENDEE"))
				{
					in_attendee = true;
					continue;
				}

				sb.Append(line + "\n");
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

		#endregion
	}

	[Serializable]
	public class WebRoleData
	{
		private static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();

		public static string procname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
		public static int procid = System.Diagnostics.Process.GetCurrentProcess().Id;
		public static string domain_name = AppDomain.CurrentDomain.FriendlyName;
		public static int thread_id = System.Threading.Thread.CurrentThread.ManagedThreadId;

		// on startup, and then periodically, a renderer is constructed for each hub
		public Dictionary<string, CalendarRenderer> renderers = new Dictionary<string, CalendarRenderer>();

		//public Dictionary<string, Calinfo> calinfos = new Dictionary<string, Calinfo>(); // todo: remove this vestige 

		public List<string> where_ids = new List<string>();
		public List<string> what_ids = new List<string>();

		// on startup, and then periodically, this list of "ready" hubs is constructed
		// ready means that the hub has been added to the system, and there has been at 
		// least one successful aggregation run resulting in an output like:
		// http://elmcity.cloudapp.net/services/ID/html

		public List<string> ready_ids = new List<string>();

		// the stringified version of the list controls the namespace, under /services, that the
		// service responds to. so when a new hub is added, say Peekskill, NY, with id peekskill, 
		// the /services/peekskill family of URLs won't become active until the hub joins the list of ready_ids
		public string str_ready_ids;

		public WebRoleData(bool testing, string test_id)
		{
			GenUtils.LogMsg("info", String.Format("WebRoleData: {0}, {1}, {2}, {3}", procname, procid, domain_name, thread_id), null);

			MakeWhereAndWhatIdLists();

			var ids = Metadata.LoadHubIdsFromAzureTable();

			Parallel.ForEach(ids, id =>
			//foreach (var id in ids)
			{
				GenUtils.LogMsg("info", "GatherWebRoleData: readying: " + id, null);

				var cr = Utils.AcquireRenderer(id);
				this.renderers.Add(id, cr);

				if (BlobStorage.ExistsBlob(id, id + ".html")) // there has been at least one aggregation
					this.ready_ids.Add(id);
			});
			//}

			// this pipe-delimited string defines allowed IDs in the /services/ID/... URL pattern
			this.str_ready_ids = String.Join("|", this.ready_ids.ToArray());
			GenUtils.LogMsg("info", "GatherWebRoleData: str_ready_ids: " + this.str_ready_ids, null);
		}

		private void MakeWhereAndWhatIdLists()
		{
			this.where_ids = Metadata.LoadHubIdsFromAzureTableByType(HubType.where);
			var where_ids_as_str = string.Join(",", this.where_ids.ToArray());
			GenUtils.LogMsg("info", "where_ids: " + where_ids_as_str, null);

			this.what_ids = Metadata.LoadHubIdsFromAzureTableByType(HubType.what);
			var what_ids_as_str = string.Join(",", this.what_ids.ToArray());
			GenUtils.LogMsg("info", "what_ids: " + what_ids_as_str, null);

			Dictionary<string,string> ids_and_locations = Metadata.QueryIdsAndLocations();

			this.where_ids.Sort((a, b) => ids_and_locations[a].ToLower().CompareTo(ids_and_locations[b].ToLower()));
			this.what_ids.Sort();
		}

		public static WebRoleData MakeWebRoleData() // todo: lease the blob
		{
			WebRoleData wrd = null;
			var bs = BlobStorage.MakeDefaultBlobStorage();
			try  // create WebRoleData structure and store as blob, available to webrole on next _reload
			{
				wrd = new WebRoleData(testing: false, test_id: null);
				bs.SerializeObjectToAzureBlob(wrd, "admin", "wrd.obj");
			}
			catch (Exception e3)
			{
				GenUtils.PriorityLogMsg("exception", "MakeWebRoleData: creating wrd", e3.Message);
			}
			return wrd;
		}

		public static void UpdateRendererForId(string id) // todo: lease the blob
		{
			var wrd = Utils.GetWrd();
			try
			{
				var cr = Utils.AcquireRenderer(id);
				wrd.renderers[id] = cr;
				bs.SerializeObjectToAzureBlob(wrd, "admin", "wrd.obj");
			}
			catch (Exception e2)
			{
				GenUtils.PriorityLogMsg("exception", "UpdateRendererForId", e2.Message);
			}
		}

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
		private string type;
		private string search_text;
		private List<Dictionary<string, string>> entities;

		public List<Dictionary<string, string>> FilterByType(string search_text)
		{
		return this.entities.FindAll(entry => (entry["type"].Contains(type)));
		}

		public List<Dictionary<string, string>> FilterByAny(string search_text)
		{
			return this.entities.FindAll(entry => entry["message"].Contains(search_text) || entry["data"].Contains(search_text) );
		}

		public LogFilter(string type, string search_text)
		{
			this.type = type;
			this.search_text = search_text;
		}

		public void SetEntities(List<Dictionary<string,string>> entities)
		{
			this.entities = entities;
		}

		public List<Dictionary<string, string>> Apply()
		{
			if (this.type != null)
				return FilterByType(this.search_text);
			else
				return FilterByAny(this.search_text);
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

