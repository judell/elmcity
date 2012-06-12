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
					GenUtils.PriorityLogMsg("exception", "CalcDistance: " + str_lat1 + "," + str_lat2, e.Message);
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

		#region ics filters

		public enum IcsFromIcsOperator { before, after };

		static public string IcsFromIcs(string feedurl, Calinfo calinfo, string source, string after, string before, string include_keyword, string exclude_keyword, bool summary_only, bool url_only, bool location_only)
		{
			var feedtext = Collector.GetFeedTextFromFeedUrl(null, calinfo, source, feedurl, wait_secs:3, max_retries:3, timeout_secs:TimeSpan.FromSeconds(5));
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

				if ( ! String.IsNullOrEmpty(include_keyword) )
				{
					if (ContainsKeyword(evt, include_keyword, summary_only, url_only, location_only) == false)
					{
						ical.RemoveChild(evt);
						continue;
					}
				}

				if ( ! String.IsNullOrEmpty(exclude_keyword) )
				{
					if (ContainsKeyword(evt, exclude_keyword, summary_only, url_only, location_only) == true)
					{
						ical.RemoveChild(evt);
						continue;
					}
				}
			}

			return Utils.SerializeIcalToIcs(ical);
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

		static public bool ContainsKeyword(DDay.iCal.Event evt, string keyword, bool summary_only, bool url_only, bool location_only)
		{
			keyword = keyword.ToLower();

			if (location_only)
				return evt.Location.ToLower().Contains(keyword);

			if (url_only)
			{
				if (evt.Url == null)
					return false;
				else
					return evt.Url.ToString().Contains(keyword);
			}

			bool is_in_summary = true;
			bool is_in_description = true; 
			
			if (evt.Summary == null)
				is_in_summary = false;
			else
				is_in_summary = evt.Summary.ToLower().Contains(keyword);

			if (summary_only)
				return is_in_summary;

			if (evt.Description == null)
				is_in_description = false;
			else
				is_in_description = evt.Description.ToLower().Contains(keyword);

			return is_in_summary || is_in_description;
		}

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

		public static string IcsFromCsv(string feed_url, string home_url, string source, bool skip_first_row, int title_col, int date_col, int time_col, string tzname)
		{
			var csv = HttpUtils.FetchUrl(new Uri(feed_url)).DataAsString();
			var lines = csv.Split('\n').ToList();
			if (skip_first_row)
				lines = lines.Skip(1).ToList<string>();
			var events = new List<ZonedEvent>();
			var ical = new DDay.iCal.iCalendar();
			var tzinfo = Utils.TzinfoFromName(tzname);
			Collector.AddTimezoneToDDayICal(ical, tzinfo);
			foreach (var line in lines)
			{
				try
				{
					if (line == "")
						continue;
					var fields = line.Split(',');
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
					evt.Start = new iCalDateTime(dtstart);
					evt.Start.TZID = tzinfo.Id;
					evt.Url = new Uri(home_url);
					Collector.AddEventToDDayIcal(ical, evt);
				}
				catch ( Exception e )
				{
					GenUtils.PriorityLogMsg("warning", "IcsFromCsv: " + feed_url, e.Message + e.StackTrace);
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
				var evt = Collector.MakeTmpEvt(collector:null, dtstart:dtstart_with_zone, dtend:DateTimeWithZone.MinValue(tzinfo), tzinfo:tzinfo, tzid:tzinfo.Id, title:name, url:url, location: location, description: location, lat: null, lon: null, allday: false);
				Collector.AddEventToDDayIcal(ical, evt);
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

		static public string IcsFromEventBriteOrganizerByName(string organizer_name, string elmcity_id, Dictionary<string, string> settings)
		{
			var calinfo = Utils.AcquireCalinfo(elmcity_id);
			iCalendar ical = InitializeCalendarForCalinfo(calinfo);
			var collector = new Collector(calinfo, settings); 

			string method = "event_search";
			string args = collector.MakeEventBriteArgs(radius_multiplier: 2, organizer: organizer_name); // first enlarge the catchment area

			int page_count = collector.GetEventBritePageCount(method, args);

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
					GenUtils.PriorityLogMsg("exception", "IcsFromEventBriteOrganizerByLocation: unpacking city/state", e.Message + e.StackTrace);
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
					GenUtils.PriorityLogMsg("exception", "IcsFromEventBriteOrganizerByLocation: Unpacking evt", e.Message + e.StackTrace);
					continue;
				}
				var dday_event = Collector.MakeTmpEvt(collector: null, dtstart: dtstart_with_zone, dtend: DateTimeWithZone.MinValue(calinfo.tzinfo), tzinfo: calinfo.tzinfo, tzid: calinfo.tzinfo.Id, title: title, url: url, location: location, description: location, lat: calinfo.lat, lon: calinfo.lon, allday: false);
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

			int page_count = collector.GetEventBritePageCount(method, args);

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
					GenUtils.PriorityLogMsg("exception", "IcsFromEventBriteOrganizer: " + organizer_id, e.Message + e.StackTrace);
					continue;
				}

				if ( Utils.IsCurrentOrFutureDateTime(dtstart_with_zone.LocalTime, calinfo.tzinfo ) )
				{
					var dday_event = Collector.MakeTmpEvt(collector: null, dtstart: dtstart_with_zone, dtend: DateTimeWithZone.MinValue(calinfo.tzinfo), tzinfo: calinfo.tzinfo, tzid: calinfo.tzinfo.Id, title: title, url: url, location: location, description: location, lat: calinfo.lat, lon: calinfo.lon, allday: false);
					Collector.AddEventToDDayIcal(ical, dday_event);
				}

			}

			var ical_serializer = new DDay.iCal.Serialization.iCalendar.iCalendarSerializer(ical);
			var ics_text = ical_serializer.SerializeToString(ical);
			return ics_text;
		}

		static public string IcsFromEventBriteEid(string eid, string tzname, Dictionary<string, string> settings)
		{
			string ics_text;
			try
			{
				var tzinfo = Utils.TzinfoFromName(tzname);
				iCalendar ical = InitializeCalendarForTzinfo(tzinfo);

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

				UnpackEventBriteEvt(tzinfo, evt, venue_name, venue_address, out dtstart_with_zone, out dtend_with_zone, out url, out title, out location, out description);

				var dday_event = Collector.MakeTmpEvt(collector: null, dtstart: dtstart_with_zone, dtend: dtend_with_zone, tzinfo: tzinfo, tzid: tzinfo.Id, title: title, url: url, location: location, description: description, lat: null, lon: null, allday: false);
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
				GenUtils.PriorityLogMsg("exception", "GetEventBriteOrganizerFromEventId", e.Message + e.StackTrace);
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
				GenUtils.PriorityLogMsg("exception", "GetEventBriteVenue", e.Message + e.StackTrace);
			}
			return venue;
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
				//if (IsReady(wrd, id) == false)
				//	continue;
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

		#endregion

		#region curatorial url-bulding helpers

		public static string get_csv_ical_url(string feed_url, string home_url, string skip_first_row, string title_col, string date_col, string time_col, string tzname)
		{
			var csv_ical_url = "";
			try
			{
				csv_ical_url = String.Format("http://elmcity.cloudapp.net/ics_from_csv?feed_url={0}&home_url={1}&skip_first_row={2}&title_col={3}&date_col={4}&time_col={5}&tzname={6}",
					Uri.EscapeDataString(feed_url), 
					Uri.EscapeDataString(home_url), 
					skip_first_row, 
					title_col, 
					date_col, 
					time_col, 
					tzname
					);

			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "get_csv_ical_url", e.Message + e.StackTrace);
			}
			return csv_ical_url;
		}

		public static string get_fb_ical_url(string fb_page_url, string elmcity_id, string regex)
		{
			var fb_ical_url = "";
			try
			{
				var uri = new Uri(fb_page_url);
				var page = HttpUtils.FetchUrl(uri).DataAsString();
				var match = Regex.Match(page, regex);
				var fb_id = match.Groups[1].Value;
				fb_ical_url = String.Format("http://elmcity.cloudapp.net/ics_from_fb_page?fb_id={0}&elmcity_id={1}",
					fb_id,
					elmcity_id);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "get_fb_ical_url: " + fb_page_url + "," + elmcity_id, e.Message + e.StackTrace);
			}
			return fb_ical_url;
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
			var ics_to_ics_ical_url = "";
			try
			{
				ics_to_ics_ical_url = string.Format("http://elmcity.cloudapp.net/ics_from_ics?feedurl={0}&elmcity_id={1}&source={2}&after={3}&before={4}&include_keyword={5}&exclude_keyword={6}&summary_only={7}&url_only={8}&location_only={9}",
					Uri.EscapeDataString(feedurl),
					elmcity_id,
					source,
					after,
					before,
					include_keyword,
					exclude_keyword,
					summary_only,
					url_only,
					location_only);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "get_ics_to_ics_ical_url: " + feedurl + "," + elmcity_id, e.Message + e.StackTrace);
			}

			return ics_to_ics_ical_url;
		}

		public static string get_rss_xcal_ical_url(string feedurl, string tzname)
		{
			var rss_xcal_ical_url = "";
			try
			{
				rss_xcal_ical_url = string.Format("http://elmcity.cloudapp.net/ics_from_xcal?url={0}&tzname={1}",
					Uri.EscapeDataString(feedurl),
					tzname);
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
					eventbrite_ical_url = string.Format("http://elmcity.cloudapp.net/ics_from_eventbrite_organizer_id?organizer_id={0}&elmcity_id={1}",
						organizer_id,
						elmcity_id);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "get_ical_url_from_eventbrite_event_page: " + url, e.Message + e.StackTrace);
			}

			return eventbrite_ical_url;
		}

		public static string get_ical_url_from_eid_of_eventbrite_event_page(string url, string tzname)
		{
			var eventbrite_ical_url = "";
			try
			{
				var page = HttpUtils.FetchUrl(new Uri(url)).DataAsString();
				//http://www.eventbrite.com/calendar?eid=3441799515&amp;calendar=ical" />
				var eid = Regex.Matches(page, @"eventbrite.com/calendar\?eid=(\d+)")[0].Groups[1].Captures[0].Value;
				eventbrite_ical_url = string.Format("http://elmcity.cloudapp.net/ics_from_eventbrite_eid?eid={0}&tzname={1}",
						eid,
						tzname);
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

		public static string MakeViewKey(string id, string type, string view, string count, string from, string to)
		{
			return string.Format("/services/{0}/{1}?view={2}&count={3}&from={4}&to={5}", id, type, view, count, from, to);
		}

		public static void RemoveBaseCacheEntry(string id)
		{
			var cached_base_uri = MakeBaseZonelessUrl(id);
			var url = string.Format("http://{0}/services/remove_cache_entry?cached_uri={1}",
				ElmcityUtils.Configurator.appdomain,
				cached_base_uri);
			var result = HttpUtils.FetchUrl(new Uri(url));
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

		public static string FixMiswrappedComponent(string calendar_text, string component)
		{
			var lines = calendar_text.Split('\n');
			var sb = new StringBuilder();
			bool in_component = false;
			var re = new Regex("^[A-Z]+[:;]");
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

		public static string WrapMiswrappedUID(string ical_text)
		{
			{
				var lines = ical_text.Split('\n');
				var sb = new StringBuilder();
				foreach (var line in lines)
				{
					if (line.Contains("UID:") && !line.StartsWith("UID:"))
					{
						var parts = Regex.Split(line, "UID:");
						sb.Append(parts[0] + "\n");
						sb.Append("UID:" + parts[1] + "\n");
					}
					else
						sb.Append(line + "\n");
				}

				return sb.ToString();
			}
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
			return GetRegionIds().Exists(x => x == id);
		}

		public static List<string> GetIdsForRegion(string region)
		{
			var q = string.Format("$filter=PartitionKey eq '{0}'", region);
			var dict = TableStorage.QueryForSingleEntityAsDictStr(ts, "regions", q);
			var ids = dict["ids"].Split(',').ToList();
			return ids;
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
			var json = Utils.DeserializeObjectFromJson<List<Dictionary<String, String>>>(id, id + ".feeds.json");
			var tags =
				from dict in json
				where dict.ContainsKey("category") && !String.IsNullOrEmpty(dict["category"])
				from tag in dict["category"].Split(',')
				select tag.ToLower();
			return tags.Distinct().ToList();
		}

		public static bool UrlParameterIsTrue(string param)
		{
			return ( param != null && param.ToLower() == "yes" ) ? true : false;
		}

		public static string TryGetCachedFeed(string feedurl)
		{
			var blob_name = BlobStorage.MakeSafeBlobnameFromUrl(feedurl);
			var blob_uri = BlobStorage.MakeAzureBlobUri("feedcache", blob_name, false);
			if (BlobStorage.ExistsBlob(blob_uri))
				return HttpUtils.FetchUrl(blob_uri).DataAsString();
			else
				return "";
		}

		public static void SaveFeedToCache(string feedurl, string feedtext)
		{
			var blob_name = BlobStorage.MakeSafeBlobnameFromUrl(feedurl);
			bs.PutBlob("feedcache", blob_name, feedtext, "text/calendar");
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

