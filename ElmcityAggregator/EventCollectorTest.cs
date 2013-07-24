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
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DDay.iCal;
using ElmcityUtils;
using NUnit.Framework;

namespace CalendarAggregator
{
	[TestFixture]
	public class EventCollectorTest
	{
		private string test_eventful_args = "date=Next+Week&location=03431&within=15&units=mi";
		private static string min_date = string.Format("{0:yyyy-MM-dd}", DateTime.UtcNow);
		private static Apikeys test_apikeys = new Apikeys();

		private static Calinfo test_calinfo; 
		private static string lookup_lat; 
		private static string lookup_lon; 
		private static int radius; 
		private string test_upcoming_args; 
		private static Dictionary<string, string> settings; 

		private static Calinfo calinfo_berkeley;
		private static Calinfo calinfo_keene; 

		private static Collector collector_berkeley;
		private static Collector collector_keene;

		private static BlobStorage bs;

		private static List<string> ics_examples = new List<string>() { "hillside", "ucb", "berkeleyside", "chorale", "folklore", "graceworship", "cedarhill", "silkcity", "investment"};
		private static List<string> eventful_examples = new List<string>() { "summitscience" };
		private static List<string> upcoming_examples = new List<string>() { "philharmonia" };
		private static List<string> eventbrite_examples = new List<string>() { "eventbrite" };

		private static string keene_test_hub = "testKeene";
		private static string berkeley_test_hub = "testBerkeley";

		private static string basic_ics;

		public EventCollectorTest()
		{
			test_calinfo = new Calinfo(ElmcityUtils.Configurator.azure_compute_account);
			lookup_lat = test_calinfo.lat;
			lookup_lon = test_calinfo.lon;
			radius = Configurator.default_radius;
			test_upcoming_args = string.Format("location={0},{1}&radius={2}&min_date={3}", lookup_lat, lookup_lon, radius, min_date);
			settings = GenUtils.GetSettingsFromAzureTable();
			basic_ics = BlobStorage.MakeDefaultBlobStorage().GetBlob("admin", "basic.ics").HttpResponse.DataAsString();
			bs = BlobStorage.MakeDefaultBlobStorage();
			calinfo_berkeley = new Calinfo(berkeley_test_hub);
			calinfo_keene = new Calinfo(keene_test_hub);
			collector_berkeley = new Collector(calinfo_berkeley, settings);
			collector_keene = new Collector(calinfo_keene,settings);
			foreach (var example in ics_examples)
				UpdateYYYY(example, "ics");

			foreach (var example in eventful_examples)
				UpdateYYYY(example, "xml");

			//foreach (var example in upcoming_examples)
			//	UpdateYYYY(example, "xml");

			foreach (var example in eventbrite_examples)
				UpdateYYYY(example, "xml");
		}

		private static void AlterIcs(string example, string search, string replace)
		{
			var text = HttpUtils.FetchUrl(BlobStorage.MakeAzureBlobUri("admin", example + ".ics", false)).DataAsString();
			text = text.Replace(search, replace);
			bs.PutBlob("admin", example + ".ics", text);
			// to factor daylight savings transitions into tests the month and day must stay the same
			// but the year advances to ensure that the event will be in the future-oriented collection window
			// and that means the test hubs must also have icalendar_horizon_days set beyond the default 90 (currently 1000)
		}

		private static void UpdateYYYY(string example, string ext)
		{
			var text = HttpUtils.FetchUrl(BlobStorage.MakeAzureBlobUri("admin", example + ".tmpl",false)).DataAsString();
			var then = System.DateTime.UtcNow + TimeSpan.FromDays(365);  // see below
			text = text.Replace("YYYY", String.Format("{0:yyyy}", then));
			bs.PutBlob("admin", example + "." + ext, text);
			// to factor daylight savings transitions into tests the month and day must stay the same
			// but the year advances to ensure that the event will be in the future-oriented collection window
			// and that means the test hubs must also have icalendar_horizon_days set beyond the default 90 (currently 1000)
		}

		private static void DeleteZonedObjects(string id)
		{
			foreach (var type in Enum.GetValues(typeof(SourceType)))
			{
				var zoned_object = id + "." + type.ToString() + ".zoned.obj";
				bs.DeleteBlob(id, zoned_object);
			}
		}

		#region ical examples

		[Test]
		public void InvestmentAndFinancialPlanningDoesNotRecurToPresent()
		{
			var example = "investment";
			var source = "Ann Arbor City";
			var fr = new FeedRegistry(keene_test_hub);

			var zes = ProcessIcalExample(example, source, calinfo_keene, fr, collector_keene, true);

			Assert.That(zes.events.Count == 0);
		}

		[Test]
		public void SilkCityIsAllDay()
		{
			var example = "silkcity";
			var source = "Silk City Flick Fest";
			var fr = new FeedRegistry(keene_test_hub);

			var zes = ProcessIcalExample(example, source, calinfo_keene, fr, collector_keene, true);

			Assert.That(zes.events.Count == 1);
			var evt = zes.events[0];
			Assert.That(evt.title.StartsWith("The Silk City Flick Fest"));
			Assert.That(evt.allday == true);
			Assert.That(evt.dtstart.Hour == 0);
			Assert.That(evt.dtstart.Minute == 0);
			Assert.That(evt.dtstart.Second == 0);
		}

		[Test]
		public void CedarHillCommemoratesAt10AMLocal()
		{
			var example = "cedarhill";
			var source = "Cedar Hill Commemorates";
			var fr = new FeedRegistry(keene_test_hub);

			var zes = ProcessIcalExample(example, source, calinfo_keene, fr, collector_keene, true);

			Assert.That(zes.events.Count == 1);
			var evt = zes.events[0];
			Assert.That(evt.title.StartsWith("Cedar Hill Commemorates"));
			Assert.That(evt.dtstart.Hour == 10);
			Assert.That(evt.dtstart.Minute == 0);
			Assert.That(evt.dtstart.Second == 0);
		}

		[Test]
		public void LifeRecoveryMinistryAt6PMLocal()
		{
			var example = "graceworship";
			var source = "Grace Worship";
			var fr = new FeedRegistry(keene_test_hub);

			var zes = ProcessIcalExample(example, source, calinfo_keene, fr, collector_keene,true);

			Assert.That(zes.events.Count == 1);
			var evt = zes.events[0];
			Assert.That(evt.title.StartsWith("LRM"));
			Assert.That(evt.dtstart.Hour == 18);
			Assert.That(evt.dtstart.Minute == 0);
			Assert.That(evt.dtstart.Second == 0);
		}

		[Test]
		public void BrowerYouthAwardsAt530PMLocal()
		{
			var example = "berkeleyside";
			var source = "berkeleyside";
			var fr = new FeedRegistry(berkeley_test_hub);

			var zes = ProcessIcalExample(example, source, calinfo_berkeley, fr, collector_berkeley, true);

			Assert.That(zes.events.Count == 1);
			var evt = zes.events[0];
			Assert.That(evt.title.EndsWith("Brower Youth Awards 2011"));
			Assert.That(evt.dtstart.Hour == 17);
			Assert.That(evt.dtstart.Minute == 30);
			Assert.That(evt.dtstart.Second == 0);
		}

		[Test]
		public void AfternoonTeaAt3PMLocal()
		{
			var example = "hillside";
			var source = "hillside";
			var fr = new FeedRegistry(berkeley_test_hub);
			var zes = ProcessIcalExample(example, source, calinfo_berkeley, fr, collector_berkeley, true);
			HillsideExampleIsCorrectObj(zes, null, null);
		}

		public static bool __AfternoonTeaAt3PMLocal()      // this version leaves cached ics and obj 
		{
			var example = "hillside";
			var source = "hillside";
			var fr = new FeedRegistry(berkeley_test_hub);
			var zes = ProcessIcalExample(example, source, calinfo_berkeley, fr, collector_berkeley, false);
			try
			{
				HillsideExampleIsCorrectObj(zes, null, null);
				return true;
			}
			catch
			{
				return false;
			}

		}

		private static bool _AfternoonTeaAt3PMLocal()    // this version expects an altered feed
		{
			var example = "hillside";
			var source = "hillside";
			var fr = new FeedRegistry(berkeley_test_hub);
			var zes = ProcessIcalExample(example, source, calinfo_berkeley, fr, collector_berkeley, false);
			try
			{
				HillsideExampleIsCorrectObj(zes, "Afternoon Tea", "Afternoon Coffee");
				return true;
			}
			catch 
			{
				return false;
			}
		}



		private static void HillsideExampleIsCorrectObj(ZonelessEventStore zes, string search, string replace)
		{
			var title_substr = "Afternoon Tea 3";
			if (search != null && replace != null)
				title_substr = title_substr.Replace(search, replace);
			Assert.That(zes.events.Count > 0);  // it's a recurring event, don't need/want to test for exact count
			var evt = zes.events[0];
			Assert.That(evt.title.StartsWith(title_substr));
			Assert.That(evt.dtstart.Hour == 15);
			Assert.That(evt.dtstart.Minute == 0);
			Assert.That(evt.dtstart.Second == 0);
		}

		private static void HillsideExampleIsCorrectIcs(DDay.iCal.iCalendar ical)
		{
			Assert.That(ical.Events.Count > 0);  // it's a recurring event, don't need/want to test for exact count
			var evt = ical.Events[0];
			Assert.That(evt.Summary.StartsWith("Afternoon Tea 3"));
			Assert.That(evt.Start.Hour == 15);
			Assert.That(evt.Start.Minute == 0);
			Assert.That(evt.Start.Second == 0);
		}

		[Test]
		public void KeeneChoraleAt4PMLocal()
		{
			var example = "chorale";
			var source = "Keene Chorale";
			var fr = new FeedRegistry(keene_test_hub);

			var zes = ProcessIcalExample(example, source, calinfo_keene, fr, collector_keene, true);

			Assert.That(zes.events.Count == 1);
			var evt = zes.events[0];
			Assert.That(evt.title == "Keene Chorale Winter concert");
			Assert.That(evt.dtstart.Hour == 16);
			Assert.That(evt.dtstart.Minute == 0);
			Assert.That(evt.dtstart.Second == 0);
		}

		[Test]
		public void HealthProfessionsProgramAt10AMLocal()
		{
			var example = "ucb";
			var source = "UC Berkeley";
			var fr = new FeedRegistry(berkeley_test_hub);

			var zes = ProcessIcalExample(example, source, calinfo_berkeley, fr, collector_berkeley, true);

			Assert.That(zes.events.Count == 1);
			var evt = zes.events[0];
			Assert.That(evt.title.EndsWith("Post-Baccalaureate Health Professions Program"));
			Assert.That(evt.dtstart.Hour == 10);
			Assert.That(evt.dtstart.Minute == 0);
			Assert.That(evt.dtstart.Second == 0);
		}

		[Test]
		public void LissaSchneckenburgerAt8PMLocal()
		{
			var example = "folklore";
			var source = "Monadnock Folklore Society";
			var fr = new FeedRegistry(keene_test_hub);

			var zes = ProcessIcalExample(example, source, calinfo_keene, fr, collector_keene, true);

			Assert.That(zes.events.Count == 1);
			var evt = zes.events[0];
			Assert.That(evt.title.EndsWith("Lissa Schneckenburger | Nelson Town Hall"));
			Assert.That(evt.dtstart.Hour == 20);
			Assert.That(evt.dtstart.Minute == 0);
			Assert.That(evt.dtstart.Second == 0);
		}

		private static ZonelessEventStore ProcessIcalExample(string example, string source, Calinfo calinfo, FeedRegistry fr, Collector collector, bool purge)
		{
			DeleteZonedObjects(calinfo.id);
			if ( purge )
				Utils.PurgeFeedCacheForHub(calinfo.id);
			var feedurl = BlobStorage.MakeAzureBlobUri("admin", example + ".ics", false).ToString();
			fr.AddFeed(feedurl, source);
			var es = new ZonedEventStore(calinfo, SourceType.ical);
			collector.CollectIcal(fr, es, false);
			EventStore.CombineZonedEventStoresToZonelessEventStore(calinfo.id, settings);
			var zes = new ZonelessEventStore(calinfo).Deserialize();
			return zes;
		}

		#endregion

		#region ical basic

		[Test]
		public void iCalSingleEventIsFuture()
		{
			var ics = basic_ics;
			var futuredate = DateTime.Now + new TimeSpan(10, 0, 0, 0, 0);
			ics = ics.Replace("__SINGLE_DATE_START__", futuredate.ToString("yyyyMMdd"));
			ics = ics.Replace("__SINGLE_DATE_END__", futuredate.ToString("yyyyMMdd"));
			StringReader sr = new StringReader(ics);
			var ical = iCalendar.LoadFromStream(sr).First().Calendar;
			var evt = ical.Events[0];
			var ical_now = new iCalDateTime(DateTime.Now.ToUniversalTime());
			Assert.That(evt.GetOccurrences(ical_now).Count == 0);
			var collector = new Collector(test_calinfo, settings);
			Assert.IsTrue(Collector.IsCurrentOrFutureDTStartInTz(evt.Start.Date, test_calinfo.tzinfo));
		}

		[Test]
		public void iCalSingleEventIsPast()
		{
			var ics = basic_ics;
			var pastdate = DateTime.Now - new TimeSpan(10, 0, 0, 0, 0);
			ics = ics.Replace("__SINGLE_DATE_START__", pastdate.ToString("yyyyMMdd"));
			ics = ics.Replace("__SINGLE_DATE_END__", pastdate.ToString("yyyyMMdd"));
			StringReader sr = new StringReader(ics);
			var ical = iCalendar.LoadFromStream(sr).First().Calendar;
			var evt = ical.Events[0];
			var collector = new Collector(test_calinfo, settings);
			Assert.IsFalse(Collector.IsCurrentOrFutureDTStartInTz(evt.Start.Date, test_calinfo.tzinfo));
		}

		[Test]
		public void iCalRecurringEventHasFutureOccurrences()
		{
			var ics = basic_ics;
			ics = ics.Replace("__SINGLE_DATE_START__", DateTime.Now.ToString("yyyyMMdd"));
			ics = ics.Replace("__SINGLE_DATE_END__", DateTime.Now.ToString("yyyyMMdd"));
			StringReader sr = new StringReader(ics);
			var ical = iCalendar.LoadFromStream(sr).First().Calendar;
			Assert.That(ical.Events.Count > 0);
			var evt = ical.Events[1];
			DateTime now = DateTime.Now;
			DateTime then = now.AddDays(90);
			IList<Occurrence> occurrences = evt.GetOccurrences(now, then);
			Assert.That(occurrences.Count > 1);
			var period = occurrences[1].Period;
			var collector = new Collector(test_calinfo, settings);
			Assert.That(Collector.IsCurrentOrFutureDTStartInTz(period.StartTime.Date, test_calinfo.tzinfo));
		}

		#endregion

		#region eventful

		[Test]
		public void EventfulQueryYieldsNonzeroVenues()
		{
			string method = "venues/search";
			var collector = new Collector(test_calinfo, settings);
			var xdoc = collector.CallEventfulApi(method, test_eventful_args);
			var venues = from venue in xdoc.Descendants("venue") select venue;
			Assert.That(venues.Count() > 0);
		}

		[Test]
		public void EventfulQueryYieldsNonzeroEvents()
		{
			var collector = new Collector(test_calinfo, settings);
			var events = (IEnumerable<XElement>)collector.EventfulIterator(1, test_eventful_args, "events/search", "event");
			Assert.That(events.Count() > 0);
		}

		[Test]
		public void SummitScienceAt7PMLocal()
		{
			var example = "harriscenter";
			var source = "Summit Science";

			collector_keene.mock_eventful = true;
			var zes = ProcessEventfulExample(example, source, calinfo_keene, collector_keene);
			collector_keene.mock_eventful = false;

			Assert.That(zes.events.Count == 1);
			var evt = zes.events[0];
			Assert.That(evt.title.StartsWith("Summit Science"));
			Assert.That(evt.dtstart.Hour == 19);
			Assert.That(evt.dtstart.Minute == 0);
			Assert.That(evt.dtstart.Second == 0);
		}

		private static ZonelessEventStore ProcessEventfulExample(string example, string source, Calinfo calinfo, Collector collector)
		{
			DeleteZonedObjects(calinfo.id);
			var es = new ZonedEventStore(calinfo, SourceType.eventful);
			collector.CollectEventful(es, test: true);
			EventStore.CombineZonedEventStoresToZonelessEventStore(calinfo.id, settings);
			var zes = new ZonelessEventStore(calinfo).Deserialize();
			return zes;
		}

		#endregion

		#region eventbrite


		[Test]
		public void EventbriteQueryReturnsPageCountOrMinusOne()
		{
			var collector = new Collector(test_calinfo, settings);

			string method = "event_search";
			string args = collector.MakeEventBriteArgs(2, null);
			int count = collector.GetEventBritePageCount(method, args);
			Assert.That(count == -1 || count >= 1);
			if (count >= 1 && settings["eventbrite_quota_reached"] == "True")
			{
				GenUtils.PriorityLogMsg("info", "GetEventBritePageCount", "resetting quota marker");
				var dict = new Dictionary<string, object>() { { "value", false } };
				TableStorage.UpmergeDictToTableStore(dict, "settings", "settings", "eventbrite_quota_reached");
			}
		}

		[Test]
		public void NewYearsEvePartyAt8PMLocal()
		{
			var example = "eventbrite";
			var source = "EventBrite New Year";
			collector_keene.mock_eventbrite = true;
			var zes = ProcessEventBriteExample(example, source, calinfo_keene, collector_keene);
			Assert.That(zes.events.Count == 1);
			var evt = zes.events[0];
			Assert.That(evt.title.StartsWith("Best NYC"));
			Assert.That(evt.dtstart.Hour == 20);
			Assert.That(evt.dtstart.Minute == 0);
			Assert.That(evt.dtstart.Second == 0);
		}

		private static ZonelessEventStore ProcessEventBriteExample(string example, string source, Calinfo calinfo, Collector collector)
		{
			DeleteZonedObjects(calinfo.id);
			var es = new ZonedEventStore(calinfo, SourceType.eventbrite);
			collector.CollectEventBrite(es);
			EventStore.CombineZonedEventStoresToZonelessEventStore(calinfo.id, settings);
			var zes = new ZonelessEventStore(calinfo).Deserialize();
			return zes;
		}

		#endregion

		/*
		#region upcoming

		[Test]
		public void UpcomingCanCallApi()
		{
			string method = "event.search";
			var collector = new Collector(test_calinfo, settings);
			var xdoc = collector.CallUpcomingApi(method, test_upcoming_args);
			var stat = from element in xdoc.Descendants("rsp")
					   select element.Attribute("stat").Value;
			Assert.That(stat.First().ToString() == "ok");
		}

		[Test]
		public void PhilharmoniaAt8PMLocal()
		{
			var example = "philharmonia";
			var source = "First Congregational";

			collector_berkeley.mock_upcoming = true;
			var zes = ProcessUpcomingExample(example, source, calinfo_berkeley, collector_berkeley);
			collector_keene.mock_upcoming = false;

			Assert.That(zes.events.Count == 1);
			var evt = zes.events[0];
			Assert.That(evt.title.StartsWith("Philharmonia Baroque"));
			Assert.That(evt.dtstart.Hour == 20);
			Assert.That(evt.dtstart.Minute == 0);
			Assert.That(evt.dtstart.Second == 0);
		}
		
		private static ZonelessEventStore ProcessUpcomingExample(string example, string source, Calinfo calinfo, Collector collector)
		{
			DeleteZonedObjects(calinfo.id);
			var es = new ZonedEventStore(calinfo, SourceType.upcoming);
			collector.CollectUpcoming(es, test: true);
			EventStore.CombineZonedEventStoresToZonelessEventStore(calinfo.id, settings);
			var zes = new ZonelessEventStore(calinfo).Deserialize();
			return zes;
		}

		#endregion
		 */

		#region feed cache

		[Test]
		public void NonexistingFeedCacheYieldsCachedAndValidIcsAndObj()
		{
			var example = "hillside";
			var feedurl = BlobStorage.MakeAzureBlobUri("admin", example + ".ics", false).ToString();
			Utils.PurgeFeedCacheForHub(calinfo_berkeley.id);
			AfternoonTeaAt3PMLocal(); // process the hillside example
			var ics_cached_uri = GetFeedCacheUri(GetFeedIcsCacheName(feedurl));
			var cached_ics = Utils.GetCachedFeedText(feedurl);
			Assert.That(cached_ics != ""); // should exist now
			var obj_blob_name = Utils.MakeCachedFeedObjName(berkeley_test_hub, feedurl);
			var es_zoned = Utils.GetFeedObjFromCache(calinfo_berkeley, feedurl);                        // verify obj exists
			var es_zoneless = EventStore.ZonedToZoneless(berkeley_test_hub, calinfo_berkeley, es_zoned);
			HillsideExampleIsCorrectObj(es_zoneless, null, null);                                       // verify obj correct
		}

		[Test]
		public void UnchangedFeedUsesCachedObj()
		{
			var example = "hillside";
			var feedurl = BlobStorage.MakeAzureBlobUri("admin", example + ".ics", false).ToString();

			var stopwatch = new System.Diagnostics.Stopwatch();
			stopwatch.Start();
			Assert.That(__AfternoonTeaAt3PMLocal()); // process the hillside example leaving ics and obj cached
			stopwatch.Stop();
			var time1 = stopwatch.Elapsed;

			Assert.That(CachedIcsNonempty(feedurl));                  // should exist now
			Assert.That(CachedObjNonempty(feedurl));                  // should exist now
			var ics_cached_name = BlobStorage.MakeSafeBlobnameFromUrl(feedurl);
			var ics_cached_uri = BlobStorage.MakeAzureBlobUri("feedcache", ics_cached_name);
			var cached_text = HttpUtils.FetchUrl(ics_cached_uri).DataAsString();
			var feed_text = HttpUtils.FetchUrl(new Uri(feedurl)).DataAsString();
			feed_text = Utils.RemoveComponent(feed_text, "DTSTAMP");  // because it's subtracted before MassageFeed          
			Assert.That(cached_text == feed_text);                  // cached ics should match feed ics

			stopwatch.Reset();
			stopwatch.Start();
			AfternoonTeaAt3PMLocal(); // go again to check for use of cached obj
			stopwatch.Stop();
			var time2 = stopwatch.Elapsed;

			var include_text = feedurl + "," + "using cached obj";
			var log_text = Utils.GetRecentLogEntries("log", null, 5, include_text, null);
			Assert.That(log_text.Contains("using cached obj for " + feedurl));
		}

		[Test]
		public void ChangedFeedUpdatesCachedObj()
		{
			var example = "hillside";
			var feedurl = BlobStorage.MakeAzureBlobUri("admin", example + ".ics", false).ToString();
			Utils.PurgeFeedCacheForHub(calinfo_berkeley.id);
			AfternoonTeaAt3PMLocal(); // process the hillside example once to make sure ics and obj cached
			Assert.That(CachedIcsNonempty(feedurl));  // cached ics exists, not empty
			Assert.That(CachedObjNonempty(feedurl));  // cached obj exists, not empty
			var obj_blob_name = Utils.MakeCachedFeedObjName(calinfo_berkeley.id, feedurl);
			var blob_props = bs.GetBlobProperties("feedcache", obj_blob_name);
			var old_last_mod = blob_props.HttpResponse.headers["Last-Modified"];
			AlterIcs(example, "Afternoon Tea", "Afternoon Coffee");  // now alter the ics
			Assert.That(_AfternoonTeaAt3PMLocal()); // process again, using the test that expects the change
			blob_props = bs.GetBlobProperties("feedcache", obj_blob_name);
			var new_last_mod = blob_props.HttpResponse.headers["Last-Modified"];
			Assert.That(old_last_mod != new_last_mod);               // cached obj should differ
			UpdateYYYY(example, "ics");                              // restore the original ics
		}

		private static string GetFeedIcsCacheName(string feedurl)
		{
			return BlobStorage.MakeSafeBlobnameFromUrl(feedurl);
		}

		private static Uri GetFeedCacheUri(string blob_name)
		{
			return BlobStorage.MakeAzureBlobUri("feedcache", blob_name, false);
		}

		private static bool CachedIcsNonempty(string feedurl)
		{
			string blob_name = BlobStorage.MakeSafeBlobnameFromUrl(feedurl);
			var cached_uri = BlobStorage.MakeAzureBlobUri("feedcache", blob_name);
			var ics = HttpUtils.FetchUrl(cached_uri).DataAsString();
			return ics != "";
		}

		private static bool CachedObjNonempty(string feedurl)
		{
			string blob_name = Utils.MakeCachedFeedObjName(berkeley_test_hub, feedurl);
			var es = (ZonedEventStore) ObjectUtils.GetTypedObj<ZonedEventStore>("feedcache", blob_name);
			return es.events.Count > 0;
		}

		#endregion

		#region categories

		[Test]
		public void MaybeApplyCatmapAddsExpectedCats()    // any matching category should expand via the map(without removing the matched category)
		{
			var test_feedurl = "test_feedurl";
			var collector_keene = new Collector(new Calinfo("elmcity"), settings);
			collector_keene.per_feed_catmaps[test_feedurl] = new Dictionary<string, string>() 
				{
					{ "a", "l,M,n,O,p" },   // this will map
					{ "z", "q" }            // this won't
				};

			var feed_metadict = new Dictionary<string, string>() { { "feedurl", test_feedurl } };

			var evt = new DDay.iCal.Event();

			evt.Categories.Add("a");  // set event categories
			evt.Categories.Add("b");
			evt.Categories.Add("b");  // this dupe should go away

			var list = collector_keene.MaybeApplyCatmap(evt.Categories.ToList(), feed_metadict, "testKeene", "Keene Test Hub");
			list.Sort(String.CompareOrdinal);
			Assert.IsTrue(list.SequenceEqual(new List<string>() { "a", "b", "l", "m", "n", "o", "p" }));
		}

		[Test]
		public void CategoriesAreAddeViaFeedOrEventMetadict()
		{
			var list = new List<string>() { "a", "B" };
			var metadict = new Dictionary<string,string>() { { "category", "b, X,y, Z" } };  // spaces should be trimmed, caps lowered
			var cat_string = metadict["category"];
			var l = Collector.SplitLowerTrimAndUniqifyCats(cat_string);
			list = list.Union(l).ToList();
			list.Sort();
			list = Collector.LowerTrimAndUniqifyCats(list);
			Assert.IsTrue(list.SequenceEqual( new List<string>() { "a", "b", "x", "y", "z" } ));
		}

		[Test]
		public void ExpandedCategoryListMergesWithHubTaxonomy()
		{
			var collector_keene = new Collector(new Calinfo("elmcity"), settings);
			collector_keene.tags = new List<string>() { "x", "y", "z" };                    // set hub taxonomy
			var list = new List<string>() { "a", "b", "x", "http://foo", "", "y", "z" };    
			var qualified_list = Collector.RemoveDisqualifyingCats(list);
			var final_list = new List<string>();
			collector_keene.MaybeAddSquigglies(qualified_list, final_list);
			Assert.IsTrue(final_list.SequenceEqual(new List<string>() { "{a}", "{b}", "x", "y", "z" } ) );  // adds to hub taxonomy marked in squigglies

		}

		#endregion

		#region other

		[Test]
		public void TagsAndUrlsAreCoalesced()    // 3 events with same title + start should coalesce tags and urls
		{
			DeleteZonedObjects(keene_test_hub);

			var dtstart = new DateTimeWithZone(DateTime.Now, calinfo_keene.tzinfo);
			var dtend = new DateTimeWithZone(dtstart.LocalTime + TimeSpan.FromHours(1), calinfo_keene.tzinfo);


			var es1 = new ZonedEventStore(calinfo_keene, SourceType.ical);
			es1.AddEvent(
				"event",
				"http://1",
				"source1",
				dtstart,
				dtend,
				"1",
				"1",
				false,
				"cat1",
				"first event",
				"first location"
				);

			es1.Serialize();

			Assert.IsTrue(calinfo_keene.eventful);

			var es2 = new ZonedEventStore(calinfo_keene, SourceType.eventful);
			es2.AddEvent(
				"event",
				"http://2",
				"source2",
				dtstart,
				dtend,
				"2",
				"2",
				false,
				"cat2,cat2a",
				"second event",
				"second location"
				);

			es2.Serialize();

			EventStore.CombineZonedEventStoresToZonelessEventStore(keene_test_hub, settings);
			var es = new ZonelessEventStore(calinfo_keene).Deserialize();

			Assert.That(es.events.Count == 1);

			var evt = es.events.Find(e => e.title == "event");

			Assert.That(evt.categories == "cat1,cat2,cat2a");

			Assert.That(evt.urls_and_sources.Keys.Count == 2);
			Assert.That(evt.urls_and_sources["http://1"] == "source1");
			Assert.That(evt.urls_and_sources["http://2"] == "source2");
		}

		#endregion

	}
}

