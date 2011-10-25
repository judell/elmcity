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
using System.IO;
using ElmcityUtils;
using System.Linq;
using NUnit.Framework;
using DDay.iCal;

namespace CalendarAggregator
{
	[TestFixture]
	public class UtilsTest
	{

		static DateTime now = DateTime.Now;
		static System.TimeZoneInfo eastern_tzinfo = Utils.TzinfoFromName("Eastern");
		static System.TimeZoneInfo central_european_tzinfo = Utils.TzinfoFromName("Central Europe");
		static System.TimeZoneInfo gmt_tzinfo = Utils.TzinfoFromName("GMT");

		static private int expected_year = 9999;
		static private int expected_hour = 9;

		static DateTime nine_thirty_pm = new DateTime(now.Year, now.Month, now.Day, 21, 30, 0);
		static DateTimeWithZone nine_thirty_pm_eastern = new DateTimeWithZone(nine_thirty_pm, eastern_tzinfo);

		static DateTime nine_thirty_am = new DateTime(now.Year, now.Month, now.Day, 9, 30, 0);
		static DateTimeWithZone nine_thirty_am_eastern = new DateTimeWithZone(nine_thirty_am, eastern_tzinfo);

		static private string edt = "9999-12-25 09:11";

		static private int expected_liverpool_pop = 435000;
		static private int expected_snoqualmie_pop = 8100;
		static private int acceptable_pop_deviation = 1000;

		static private TableStorage ts = TableStorage.MakeDefaultTableStorage();
		static private Dictionary<string, string> settings = GenUtils.GetSettingsFromAzureTable();

		#region datetime

		[Test]
		public void DateTimeFromEventfulDateTime()
		{
			DateTime got_dt = Utils.LocalDateTimeFromLocalDateStr(edt);
			Assert.AreEqual(expected_year, got_dt.Year);
			Assert.AreEqual(expected_hour, got_dt.Hour);

		}

		[Test]
		public void DateFromDateKey()
		{
			var datekey = "d20090311";
			var date = Utils.DateFromDateKey(datekey);
			Assert.AreEqual("Wed Mar 11 2009", date);
		}

		[Test]
		public void NowEasternIsNowUtcMinusOffset()
		{
			var utc_now = DateTime.UtcNow;
			var eastern_now = TimeZoneInfo.ConvertTimeFromUtc(utc_now, eastern_tzinfo);
			Assert.AreEqual(utc_now - eastern_now, -eastern_tzinfo.GetUtcOffset(utc_now));
		}

		[Test]
		public void TwoAmIsWeeHour()
		{
			var two_am = new DateTime(Configurator.DT_COMP_YEAR, Configurator.DT_COMP_MONTH, Configurator.DT_COMP_DAY, 2, 0, 0);
			Assert.That(Utils.ClassifyTime(two_am) == TimeOfDay.WeeHours);
		}

		[Test]
		public void NineThirtyPmIsNight()
		{
			var nine_thirty_pm = new DateTime(Configurator.DT_COMP_YEAR, Configurator.DT_COMP_MONTH, Configurator.DT_COMP_DAY, 21, 30, 0);
			Assert.That(Utils.ClassifyTime(nine_thirty_pm) == TimeOfDay.Night);
		}

		[Test]
		public void FiveAmIsMorning()
		{
			var five_am = new DateTime(Configurator.DT_COMP_YEAR, Configurator.DT_COMP_MONTH, Configurator.DT_COMP_DAY, 5, 0, 0);
			Assert.That(Utils.ClassifyTime(five_am) == TimeOfDay.Morning);
		}

		[Test]
		public void NoonIsLunch()
		{
			var noon = new DateTime(Configurator.DT_COMP_YEAR, Configurator.DT_COMP_MONTH, Configurator.DT_COMP_DAY, 12, 0, 0);
			Assert.That(Utils.ClassifyTime(noon) == TimeOfDay.Lunch);
		}

		[Test]
		public void SixThirtyPmIsEvening()
		{
			var six_thirty_pm = new DateTime(Configurator.DT_COMP_YEAR, Configurator.DT_COMP_MONTH, Configurator.DT_COMP_DAY, 18, 30, 0);
			Assert.That(Utils.ClassifyTime(six_thirty_pm) == TimeOfDay.Evening);
		}

		[Test]
		public void MidnightIsUndefined()
		{
			var midnight = new DateTime(Configurator.DT_COMP_YEAR, Configurator.DT_COMP_MONTH, Configurator.DT_COMP_DAY, 0, 0, 0);
			Assert.That(Utils.ClassifyTime(midnight) == TimeOfDay.AllDay);
		}

		#endregion

		#region regex

		[Test]
		public void RegexFindGroupValuesFindsDatePattern()
		{
			var pattern = EventStore.datekey_pattern;
			var s = "d11112233";
			var values = GenUtils.RegexFindGroups(s, pattern);
			Assert.AreEqual(s, values[0]);
			Assert.AreEqual("1111", values[1]);
		}

		[Test]
		public void RegexFindKeysAndValuesFindsUrlAndCategory()
		{
			var url = "http://www.co.yavapai.az.us/_Events.aspx?id=32794&foo=bar#asdf%d3-abc";
			var category = "music,arts";
			var s = string.Format(@"

url={0}	
         
sys.path.append(""c:\users\jonu\dev"")
import clr

category={1}  

clr.AddReference(""System.Core"")
import System


",
			url, category);
			var keys = new List<string>() { "url", "category" };
			var metadict = GenUtils.RegexFindKeysAndValues(keys, s);
			Assert.That(metadict["url"] == url);
			Assert.That(metadict["category"] == category);
		}

		#endregion

		#region population

		[Test]
		public void NonUsPopLookupIsReasonable()
		{
			var pop = Utils.FindPop("liverpoolcals", "liverpool", "uk");
			Assert.That(Math.Abs(expected_liverpool_pop - pop) < acceptable_pop_deviation);
		}

		[Test]
		public void UsPopLookupIsReasonable()
		{
			var pop = Utils.FindPop("snoqualmie", "snoqualmie", "wa");
			Assert.That(Math.Abs(expected_snoqualmie_pop - pop) < acceptable_pop_deviation);
		}

		#endregion population

		#region events

		#endregion events

		#region auth

		[Test]
		public void ElmcityIdUsesTwitterAuthSucceeds()
		{
			Assert.That(Utils.ElmcityIdUsesTwitterAuth("elmcity") == true);
		}

		[Test]
		public void ElmcityIdUsesAuthSucceeds()
		{
			Assert.That(Utils.ElmcityIdUsesAuth("elmcity") == true);
		}

		[Test]
		public void ElmcityIdUsesTwitterAuthFails()
		{
			Assert.IsFalse(Utils.ElmcityIdUsesTwitterAuth("MonadnockArtsAlive") == true);
		}

		[Test]
		public void ElmcityIdUsesAuthFails()
		{
			Assert.IsFalse(Utils.ElmcityIdUsesAuth("eventsabarna") == true);
		}

		#endregion

		#region facebook

		[Test]
		public void IcsFromFbPageSucceeds()
		{
			var url = "http://elmcity.cloudapp.net/ics_from_fb_page?fb_id=146837308680597&elmcity_id=socialhartford";
			var ical = DDay.iCal.iCalendar.LoadFromUri(new Uri(url));
			Assert.That(ical.First().GetType() == typeof(DDay.iCal.iCalendar));
		}

		#endregion

		#region xcal

		[Test]
		public void RssPlusXcalYieldsIcal()
		{
			var url = "http://events.pressdemocrat.com/search?city=Santa+Rosa&new=n&rss=1&srad=90&svt=text&swhat=&swhen=&swhere=";
			var tzinfo = Utils.TzinfoFromName("pacific");
			var ical_str = Utils.IcsFromRssPlusXcal(url, "test source", tzinfo);
			var sr = new StringReader(ical_str);
			var ical = iCalendar.LoadFromStream(sr).First().Calendar;
			Assert.AreNotEqual(0, ical.Events.Count);
		}

		#endregion

		#region WebRoleData

		[Test]
		public void WrdCanDeserialize()
		{
			var wrd = Utils.GetWrd();
			Assert.That(wrd.what_ids.Count + wrd.where_ids.Count == wrd.ready_ids.Count );
		}

		[Test]
		public void WrdAllHubsHaveWhatOrWhere()
		{
			var wrd = Utils.GetWrd();
			foreach ( var id in wrd.ready_ids )
			{
				var calinfo = Utils.AcquireCalinfo(id);
				Assert.That(calinfo.where != null && calinfo.what != null);
			}
		}

		[Test]
		public void NoHubRecordsHaveWhereAndWhat()
		{
			var query = "$filter=where ne '' and what ne ''";
			var tsr = ts.QueryAllEntitiesAsListDict("metadata", query);
			Assert.That(tsr.list_dict_obj.Count == 0);
		}

		[Test]
		public void AllHubRecordsHaveProperType()
		{
			var query = "$filter=where ne '' or what ne ''";
			var tsr = ts.QueryAllEntitiesAsListDict("metadata", query);
			var list_dict_obj = tsr.list_dict_obj;
			foreach (var dict_obj in list_dict_obj)
			{
				var dict_str = ObjectUtils.DictObjToDictStr(dict_obj);
				Assert.That(dict_str.ContainsKey("type"));
				if ( dict_str.ContainsKey("where") && dict_str["where"] != "" )
					Assert.That(dict_str["type"] == "where");
			}
		}

		/*
		[Test]
		public void AllFeedRecordsHaveRequiredAttributes()
		{
			var ids = Metadata.LoadHubIdsFromAzureTable();

			foreach ( var id in ids )
			{
				var fr = new FeedRegistry(id);
				fr.LoadFeedsFromAzure(FeedLoadOption.all);

				foreach (string feed_url in fr.feeds.Keys)
				{
				var source = fr.feeds[feed_url];
				var rowkey = Utils.MakeSafeRowkeyFromUrl(feed_url);
				var query = string.Format("$filter=PartitionKey eq '{0}' and RowKey eq '{1}'", id, rowkey);
				var table_record = TableStorage.QueryForSingleEntityAsDictStr(ts, "metadata", query);
				//foreach (var key in new List<string>() { "url", "feedurl", "source", "category" })
				foreach (var key in new List<string>() { "feedurl", "source" })
					Assert.That(table_record.ContainsKey(key));
				}
			}
		}
		 */

		#endregion

	}
}
