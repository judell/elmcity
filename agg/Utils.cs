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
using System.Timers;
using System.Xml;
using System.Xml.Linq;
using DDay.iCal.DataTypes;
using ElmcityUtils;
using LINQtoCSV;
using Newtonsoft.Json;


namespace CalendarAggregator
{

	public static class Utils
	{
		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();
		private static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
		private static Delicious delicious = Delicious.MakeDefaultDelicious();

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

		[Serializable]
		public struct DateTimeWithZone
		{
			private readonly DateTime utcDateTime;
			private readonly TimeZoneInfo timeZone;

			public DateTimeWithZone(DateTime dt, TimeZoneInfo tz)
			{
				var _dt = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);
				this.utcDateTime = TimeZoneInfo.ConvertTimeToUtc(_dt, tz);
				this.timeZone = tz;
			}

			public static DateTimeWithZone MinValue(TimeZoneInfo timeZone)
			{
				return new DateTimeWithZone(DateTime.MinValue, timeZone);
			}

			public DateTime UniversalTime { get { return utcDateTime; } }

			public TimeZoneInfo TimeZone { get { return timeZone; } }

			public DateTime LocalTime
			{
				get
				{
					return TimeZoneInfo.ConvertTime(utcDateTime, timeZone);
				}
			}

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

		public static DateTime DateTimeFromDateStr(string str_dt)
		{
			Regex re = new Regex(@"(\d+)\-(\d+)\-(\d+) (\d+):(\d+)", RegexOptions.None);
			DateTime dt = ParseDateTime(str_dt, re);
			return dt;
		}

		public static DateTime DateTimeFromISO8601DateStr(string str_dt)
		{
			//2010-11-28T03:00:00+0000
			Regex re = new Regex(@"(\d+)\-(\d+)\-(\d+)T(\d+):(\d+)", RegexOptions.None);
			DateTime dt = ParseDateTime(str_dt, re);
			return dt;
		}

		public static DateTime DateTimeFromICalDateStr(string str_dt)
		{
			Regex re = new Regex(@"(\d{4,4})(\d{2,2})(\d{2,2})T(\d{2,2})(\d{2,2})", RegexOptions.None);
			DateTime dt = ParseDateTime(str_dt, re);
			return dt;
		}

		private static DateTime ParseDateTime(string str_dt, Regex re)
		{
			Match m = re.Match(str_dt);
			int year = Convert.ToInt16(m.Groups[1].Value);
			int month = Convert.ToInt16(m.Groups[2].Value);
			int day = Convert.ToInt16(m.Groups[3].Value);
			int hour = Convert.ToInt16(m.Groups[4].Value);
			int min = Convert.ToInt16(m.Groups[5].Value);
			DateTime dt = new DateTime(year, month, day, hour, min, 0);
			return dt;
		}

		public static DateTime LocalDateTimeFromUtcDateStr(string str_dt, TimeZoneInfo tzinfo)
		{
			var dt = DateTimeFromDateStr(str_dt);
			return TimeZoneInfo.ConvertTimeFromUtc(dt, tzinfo);
		}

		public static DateTime LocalDateTimeFromISO8601UtcDateStr(string str_dt, TimeZoneInfo tzinfo)
		{
			var dt = DateTimeFromISO8601DateStr(str_dt);
			return TimeZoneInfo.ConvertTimeFromUtc(dt, tzinfo);
		}

		public static DateTime LocalDateTimeFromFacebookDateStr(string str_dt, TimeZoneInfo tzinfo)
		{
			var dt = DateTimeFromISO8601DateStr(str_dt);     // 2010-07-18 01:00
			// off by 7 hours: why?
			var adjusted_dt = dt - new TimeSpan(7, 0, 0);    // 2010-07-17 18:00
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

		public static DateTimeWithZone NowInTz()
		{
			var tzinfo = Utils.TzinfoFromName("GMT");
			return new DateTimeWithZone(DateTime.UtcNow, tzinfo);
		}

		public static DateTimeWithZone NowInTz(System.TimeZoneInfo tzinfo)
		{
			var utc_now = DateTime.UtcNow;
			var offset = tzinfo.GetUtcOffset(utc_now);
			var now_in_tz = utc_now + offset;
			return new DateTimeWithZone(now_in_tz, tzinfo);
		}

		public static DateTimeWithZone MidnightInTz(System.TimeZoneInfo tzinfo)
		{
			var now_in_tz = Utils.NowInTz(tzinfo).LocalTime;
			var midnight = new DateTime(now_in_tz.Year, now_in_tz.Month, now_in_tz.Day, 0, 0, 0);
			var midnight_in_tz = new DateTimeWithZone(midnight, tzinfo);
			return midnight_in_tz;
		}

		public static bool DtWithZoneIsTodayInTz(Utils.DateTimeWithZone dt_with_zone, System.TimeZoneInfo tzinfo)
		{
			Utils.DateTimeWithZone midnight_in_tz = MidnightInTz(tzinfo);
			DateTime next_midnight = Utils.TwentyFourHoursAfter(midnight_in_tz.LocalTime);
			Utils.DateTimeWithZone next_midnight_in_tz = new Utils.DateTimeWithZone(next_midnight, tzinfo);
			bool result = (midnight_in_tz.UniversalTime <= dt_with_zone.UniversalTime) && (dt_with_zone.UniversalTime < next_midnight_in_tz.UniversalTime);
			return result;
		}

		public static bool DtIsTodayInTz(DateTime dt, System.TimeZoneInfo tzinfo)
		{
			var dt_with_zone = new Utils.DateTimeWithZone(dt, tzinfo);
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
			name = name + " Standard Time";
			TimeZoneInfo tzinfo;
			try
			{
				//tzinfo = System.TimeZoneInfo.FindSystemTimeZoneById(name + suffix);
				//tzinfo = System.TimeZoneInfo.FindSystemTimeZoneById(tzinfo.StandardName);
				tzinfo = System.TimeZoneInfo.FindSystemTimeZoneById(name);
			}
			catch (Exception e)
			{
				tzinfo = System.TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
				GenUtils.LogMsg("exception", "no such tz: " + name, e.Message);
			}

			return tzinfo;
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

			var metadict = delicious.LoadMetadataForIdFromAzureTable(id);

			if (metadict.ContainsKey("population"))
				return Convert.ToInt32(metadict["population"]);

			pop = LookupUsPop(city_or_town, qualifier);

			if (pop == Configurator.default_population)
				pop = LookupNonUsPop(city_or_town, qualifier);

			if (pop != Configurator.default_population)
			{
				var dict = new Dictionary<string, string>();
				dict.Add("population", pop.ToString());
				var dict_obj = ObjectUtils.DictStrToDictObj(dict);
				TableStorage.UpmergeDictToTableStore(dict_obj, table: "metadata", partkey: id, rowkey: id);
				return pop;
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
				GenUtils.LogMsg("exception", "lookup_non_us_pop", e.Message);
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
				GenUtils.LogMsg("exception", "lookup_us_pop", ae.Message);
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

		#endregion population

		#region filtering

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
			var timer = new Timer();
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
					GenUtils.LogMsg("exception", "schedule_timer: startnow: " + name, ex.Message + ex.StackTrace);
				}
			}

		}

		#endregion cron

		#region logging

		private static DateTime Since(int minutes)
		{
			if (minutes > Configurator.azure_log_max_minutes)
				minutes = Configurator.azure_log_max_minutes;
			var delta = System.TimeSpan.FromMinutes(minutes);
			var dt = System.DateTime.UtcNow - delta;
			return dt;
		}

		public static string GetRecentLogEntries(int minutes, string id)
		{
			var sb = new StringBuilder();
			var dt = Since(minutes);
			GetLogEntriesLaterThanTicks(dt.Ticks, id, FilterByAllOrId, sb);
			return sb.ToString();
		}

		public static string GetRecentMonitorEntries(int minutes, string conditions)
		{
			var sb = new StringBuilder();
			var dt = Since(minutes);
			GetMonitorEntriesLaterThanTicks(dt.Ticks, Configurator.process_monitor_table, conditions, sb);
			return sb.ToString();
		}

		public static void GetTableEntriesLaterThanTicks(string tablename, string partition_key, string row_key, string conditions, List<Dictionary<string, string>> entries)
		{
			string query = String.Format("$filter=(PartitionKey eq '{0}') and (RowKey gt '{1}')", partition_key, row_key);
			if (String.IsNullOrEmpty(conditions) == false)
				query += conditions;
			TableStorageResponse r = ts.QueryEntities(tablename, query);
			var dicts = r.response as List<Dictionary<string, object>>;
			if (dicts.Count == 0)
				return;
			var dict_str = new Dictionary<string, string>();
			foreach (var dict in dicts)
			{
				dict_str = ObjectUtils.DictObjToDictStr(dict);
				entries.Add(dict_str);
			}
			var new_ticks = dict_str["RowKey"];
			GetTableEntriesLaterThanTicks(tablename, partition_key, row_key: new_ticks, conditions: conditions, entries: entries);
		}

		public delegate List<Dictionary<string, string>> ByAllOrId(List<Dictionary<string, string>> filter, string id);

		public static List<Dictionary<string, string>> FilterByAllOrId(List<Dictionary<string, string>> entries, string id)
		{
			return entries.FindAll(entry => (id == "all") || entry["message"].Contains(id));
		}

		public static void GetLogEntriesLaterThanTicks(long ticks, string id, ByAllOrId filter, StringBuilder sb)
		{
			var entries = new List<Dictionary<string, string>>();
			GetTableEntriesLaterThanTicks(tablename: "log", partition_key: "log", row_key: ticks.ToString(), conditions: null, entries: entries);
			var filtered_entries = filter(entries, id);
			FormatLogEntries(sb, filtered_entries);
		}

		private static void FormatLogEntries(StringBuilder sb, List<Dictionary<string, string>> filtered_entries)
		{
			foreach (var filtered_entry in filtered_entries)
				sb.AppendLine(FormatLogEntry(filtered_entry));
		}

		public static void GetMonitorEntriesLaterThanTicks(long ticks, string tablename, string conditions, StringBuilder sb)
		{
			var entries = new List<Dictionary<string, string>>();
			GetTableEntriesLaterThanTicks(tablename: tablename, partition_key: tablename, row_key: ticks.ToString(), conditions: conditions, entries: entries);
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

		static public string IcsFromRssPlusXcal(string rss_plus_xcal_url, string source, TimeZoneInfo tzinfo, bool use_utc)
		{
			XNamespace xcal = "urn:ietf:params:xml:ns:xcal";
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
				var dtstart = Utils.DateTimeFromDateStr(item.Element(xcal + "dtstart").Value);
				var dtstart_with_zone = new DateTimeWithZone(dtstart, tzinfo);
				var location = item.Element(xcal + "location").Value;
				//var evt = Collector.MakeTmpEvt(dtstart_with_zone, title, url, source, allday: false, use_utc: use_utc);
				//var evt = Collector.MakeTmpEvt(dtstart_with_zone,  Utils.DateTimeWithZone.MinValue(tzinfo), title, source, allday: false, use_utc: use_utc);
				var evt = Collector.MakeTmpEvt(dtstart_with_zone, Utils.DateTimeWithZone.MinValue(tzinfo), tzinfo, tzinfo.Id, title, url:url, location: location, description: source, allday: false, use_utc: false);
				Collector.AddEventToDDayIcal(ical, evt);
			}
			var serializer = new DDay.iCal.Serialization.iCalendarSerializer(ical);
			var ics_text = serializer.SerializeToString();
			return ics_text;
		}
		
		#endregion

		#region vcal

		static public string IcsFromAtomPlusVCalAsContent(string atom_plus_vcal_url, string source, TimeZoneInfo tzinfo, bool use_utc)
		{
			//var uri = new Uri("http://www.techhui.com/events/event/feed");
			var ns = StorageUtils.atom_namespace;
		
			var atom = HttpUtils.FetchUrl(new Uri(atom_plus_vcal_url));
			var xdoc = XmlUtils.XdocFromXmlBytes(atom.bytes);
			var entryquery = from items in xdoc.Descendants(ns + "entry") select items;
			var ical = new DDay.iCal.iCalendar();
			Collector.AddTimezoneToDDayICal(ical, tzinfo);

			foreach (var entry in entryquery)
			{
				var title = entry.Element(ns+"title").Value;
				var url = entry.Element(ns+"link").Attribute("href").Value;
				var dtstart_str = entry.Descendants(ns + "dtstart").First().Value;
				var dtstart = Utils.DateTimeFromICalDateStr(dtstart_str);
				var dtstart_with_zone = new DateTimeWithZone(dtstart, tzinfo);
				var location = entry.Descendants(ns + "location").First().Value;
				//var evt = Collector.MakeTmpEvt(dtstart_with_zone, title, url, source, allday: false, use_utc: use_utc);
				var evt = Collector.MakeTmpEvt(dtstart_with_zone, Utils.DateTimeWithZone.MinValue(tzinfo), tzinfo, tzinfo.Id, title, url: url, location: location, description: source, allday: false, use_utc: use_utc);
				Collector.AddEventToDDayIcal(ical, evt);
			}
			var serializer = new DDay.iCal.Serialization.iCalendarSerializer(ical);
			var ics_text = serializer.SerializeToString();
			return ics_text;
		}

		#endregion

		#region other

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
			return string.Format("{0}/{1}/{1}.zoneless.obj",
				ElmcityUtils.Configurator.azure_blobhost,
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

		public static string EmbedHtmlSnippetInDefaultPageWrapper(string id, string snippet, string title)
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
		   id,
		   title,
		   Configurator.default_css,
		   snippet);
		}

		public static string GetMetadataForId(string id)
		{
			var metadict = delicious.LoadMetadataForIdFromAzureTable(id);
			var sb = new StringBuilder();
			foreach (var key in metadict.Keys)
			{
				sb.AppendLine(key + ": " + metadict[key]);
			}
			return sb.ToString();
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

		public static bool ListContainsItemStartingWithString(List<String> list, string str)
		{
			return list.Exists(item => str.StartsWith(item));
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


		#endregion other

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

}

