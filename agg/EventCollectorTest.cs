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
		private Uri test_feedurl = new Uri("http://cid-dffec23daaf5ee89.calendar.live.com/calendar/Jon+Udell+(public)/calendar.ics");
		private string test_venue = "testvenue";
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

			foreach (var example in upcoming_examples)
				UpdateYYYY(example, "xml");
		}

		private static void UpdateYYYY(string example, string ext)
		{
			var text = HttpUtils.FetchUrl(BlobStorage.MakeAzureBlobUri("admin", example + ".tmpl")).DataAsString();
			var then = System.DateTime.UtcNow + TimeSpan.FromDays(365);  // see below
			text = text.Replace("YYYY", String.Format("{0:yyyy}", then));
			bs.PutBlob("admin", example + "." + ext, text);
			// to factor daylight savings transitions into tests the month and day must stay the same
			// but the year advances to ensure that the event will be in the future-oriented collection window
			// and that means the test hubs must also have icalendar_horizon_days set beyond the default 90 (currently 1000)
		}

		private static void DeleteZonedObjects(string id)
		{
			foreach (var flavor in Enum.GetValues(typeof(SourceType)))
			{
				var zoned_object = id + "." + flavor.ToString() + ".zoned.obj";
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

			var zes = ProcessIcalExample(example, source, calinfo_keene, fr, collector_keene);

			Assert.That(zes.events.Count == 0);
		}

		[Test]
		public void SilkCityIsAllDay()
		{
			var example = "silkcity";
			var source = "Silk City Flick Fest";
			var fr = new FeedRegistry(keene_test_hub);

			var zes = ProcessIcalExample(example, source, calinfo_keene, fr, collector_keene);

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

			var zes = ProcessIcalExample(example, source, calinfo_keene, fr, collector_keene);

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

			var zes = ProcessIcalExample(example, source, calinfo_keene, fr, collector_keene);

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

			var zes = ProcessIcalExample(example, source, calinfo_berkeley, fr, collector_berkeley);

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

			var zes = ProcessIcalExample(example, source, calinfo_berkeley, fr, collector_berkeley);

			Assert.That(zes.events.Count > 0);  // it's a recurring event, don't need/want to test for exact count
			var evt = zes.events[0];
			Assert.That(evt.title.StartsWith("Afternoon Tea 3"));
			Assert.That(evt.dtstart.Hour == 15);
			Assert.That(evt.dtstart.Minute == 0);
			Assert.That(evt.dtstart.Second == 0);
		}

		[Test]
		public void KeeneChoraleAt4PMLocal()
		{
			var example = "chorale";
			var source = "Keene Chorale";
			var fr = new FeedRegistry(keene_test_hub);

			var zes = ProcessIcalExample(example, source, calinfo_keene, fr, collector_keene);

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

			var zes = ProcessIcalExample(example, source, calinfo_berkeley, fr, collector_berkeley);

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

			var zes = ProcessIcalExample(example, source, calinfo_keene, fr, collector_keene);

			Assert.That(zes.events.Count == 1);
			var evt = zes.events[0];
			Assert.That(evt.title.EndsWith("Lissa Schneckenburger | Nelson Town Hall"));
			Assert.That(evt.dtstart.Hour == 20);
			Assert.That(evt.dtstart.Minute == 0);
			Assert.That(evt.dtstart.Second == 0);
		}

		private static ZonelessEventStore ProcessIcalExample(string example, string source, Calinfo calinfo, FeedRegistry fr, Collector collector)
		{
			DeleteZonedObjects(calinfo.id);
			var feedurl = BlobStorage.MakeAzureBlobUri("admin", example + ".ics").ToString();
			fr.AddFeed(feedurl, source);
			var es = new ZonedEventStore(calinfo, SourceType.ical);
			collector.CollectIcal(fr, es, false, false);
			EventStore.CombineZonedEventStoresToZonelessEventStore(calinfo.id, settings);
			var zes = new ZonelessEventStore(calinfo).Deserialize();
			return zes;
		}

		#endregion

		#region ical basic

		[Test]
		public void IcalFeedYieldsNonzeroEvents()
		{
			var response = HttpUtils.FetchUrl(test_feedurl);
			string feedtext = response.DataAsString();
			StringReader sr = new StringReader(feedtext);
			var ical = iCalendar.LoadFromStream(sr).First().Calendar;
			Assert.That(ical.Events.Count > 0);
		}

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
			Assert.IsTrue(collector.IsCurrentOrFutureDTStartInTz(evt.Start.Date));
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
			Assert.IsFalse(collector.IsCurrentOrFutureDTStartInTz(evt.Start.Date));
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
			Assert.That(collector.IsCurrentOrFutureDTStartInTz(period.StartTime.Date));
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
		public void EventfulQueryYieldsValidFirstEvent()
		{
			var collector = new Collector(test_calinfo, settings);
			var events = (IEnumerable<XElement>)collector.EventfulIterator(1, test_eventful_args, "events/search", "event");
			var es = new ZonedEventStore(test_calinfo, SourceType.ical);
			collector.AddEventfulEvent(es, test_venue, events.First());
			Assert.That(es.events[0].title != "");
		}

		[Test]
		public void SummitScienceAt7PMLocal()
		{
			var example = "harriscenter";
			var source = "Summit Science";

			collector_keene.test_eventful_api = true;
			var zes = ProcessEventfulExample(example, source, calinfo_keene, collector_keene);
			collector_keene.test_eventful_api = false;

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
			var zes = new ZonelessEventStore(calinfo).Deserialize();
			return zes;
		}

		#endregion

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
		public void UpcomingQueryYieldsNonzeroEvents()
		{
			string method = "event.search";
			var calinfo = new Calinfo("sfcals");
			var collector = new Collector(calinfo, settings);
			var events = (IEnumerable<XElement>)collector.UpcomingIterator(1, method);
			//Console.WriteLine(events.Count());
			Assert.That(events.Count() > 0);
		}

		[Test]
		public void UpcomingQueryYieldsValidFirstEvent()
		{
			string method = "event.search";
			var calinfo = new Calinfo("berkeley");
			var collector = new Collector(calinfo, settings);
			var events = (IEnumerable<XElement>)collector.UpcomingIterator(1, method);
			var es = new ZonedEventStore(test_calinfo, SourceType.ical);
			//Console.WriteLine(events.Count());
			var evt = events.First();
			string str_dtstart = evt.Attribute("start_date").Value + " " + evt.Attribute("end_date").Value;
			var dtstart = Utils.LocalDateTimeFromLocalDateStr(str_dtstart);
			var dtstart_with_zone = new DateTimeWithZone(dtstart, test_calinfo.tzinfo);
			collector.AddUpcomingEvent(es, test_venue, evt);
			Assert.That(es.events[0].title != "");
		}

		[Test]
		public void PhilharmoniaAt8PMLocal()
		{
			var example = "philharmonia";
			var source = "First Congregational";

			collector_berkeley.test_upcoming_api = true;
			var zes = ProcessUpcomingExample(example, source, calinfo_berkeley, collector_berkeley);
			collector_keene.test_upcoming_api = true;

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
			var zes = new ZonelessEventStore(calinfo).Deserialize();
			return zes;
		}

		#endregion

		#region mixed

		[Test]
		public void TagsAndUrlsAreCoalesced()    // 3 events with same title + start should coalesce tags and urls
		{                                          
			DeleteZonedObjects(keene_test_hub);

			var dtstart = new DateTimeWithZone( DateTime.Now, calinfo_keene.tzinfo);
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
				"first event"
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
				"second event"
				);

			es2.Serialize();

			Assert.IsTrue(calinfo_keene.upcoming);

			var es3 = new ZonedEventStore(calinfo_keene, SourceType.upcoming);
			es3.AddEvent(
				"event",
				"http://3",
				"source3",
				dtstart,
				dtend,
				"3",
				"3",
				false,
				"cat3,cat3a",
				"third event"
				);

			es3.AddEvent(
				"another event",
				"http://4",
				"source4",
				dtstart,
				dtend,
				"4",
				"4",
				false,
				"cat4,cat4a",
				"fourth event"
				);

			es3.Serialize();

			EventStore.CombineZonedEventStoresToZonelessEventStore(keene_test_hub, settings);
			var es = new ZonelessEventStore(calinfo_keene).Deserialize();

			Assert.That(es.events.Count == 2);

			var evt = es.events.Find(e => e.title == "event");

			Assert.That(evt.categories == "cat1,cat2,cat2a,cat3,cat3a");

			Assert.That(evt.urls_and_sources.Keys.Count == 3);
			Assert.That(evt.urls_and_sources["http://1"] == "source1");
			Assert.That(evt.urls_and_sources["http://2"] == "source2");
			Assert.That(evt.urls_and_sources["http://3"] == "source3");
		}

		[Test]
		public void UpcomingUrlsAreNormalized()   
		{
			DeleteZonedObjects(keene_test_hub);

			var dtstart = new DateTimeWithZone(DateTime.Now, calinfo_keene.tzinfo);
			var dtend = new DateTimeWithZone(dtstart.LocalTime + TimeSpan.FromHours(1), calinfo_keene.tzinfo);


			var es1 = new ZonedEventStore(calinfo_keene, SourceType.ical);
			es1.AddEvent(
				"event",
				"http://upcoming.yahoo.com/event/8504144/",
				"Comedy Showcase",
				dtstart,
				dtend,
				"1",
				"1",
				false,
				"comedy",
				"first event"
				);

			es1.Serialize();

			var es2 = new ZonedEventStore(calinfo_keene, SourceType.upcoming);
			es2.AddEvent(
				"event",
				"http://upcoming.yahoo.com/event/8504144",
				"Ann Arbor Comedy Showcase",
				dtstart,
				dtend,
				"1",
				"1",
				false,
				"upcoming",
				"first event"
				);

			es2.Serialize();
	
			EventStore.CombineZonedEventStoresToZonelessEventStore(keene_test_hub, settings);

			var es = new ZonelessEventStore(calinfo_keene).Deserialize();
			Assert.That(es.events.Count == 1);

			var evt = es.events.Find(e => e.title == "event");

			var categories = evt.categories.Split(',').ToList();
			categories.Sort();
			Assert.That(categories.SequenceEqual(new List<string>() { "comedy", "upcoming" }));
			Assert.That(evt.urls_and_sources.Keys.Count == 1);
		}

		[Test]
		public void EventfulUrlsAreNormalized()
		{
			DeleteZonedObjects(keene_test_hub);

			var dtstart = new DateTimeWithZone(DateTime.Now, calinfo_keene.tzinfo);
			var dtend = new DateTimeWithZone(dtstart.LocalTime + TimeSpan.FromHours(1), calinfo_keene.tzinfo);


			var es1 = new ZonedEventStore(calinfo_keene, SourceType.ical);
			es1.AddEvent(
				"event",
				"http://eventful.com/E0-001-039987477-3",
				"The Blind Pig",
				dtstart,
				dtend,
				"1",
				"1",
				false,
				"music",
				"first event"
				);

			es1.Serialize();

			var es2 = new ZonedEventStore(calinfo_keene, SourceType.eventful);
			es2.AddEvent(
				"event",
				"http://eventful.com/events/E0-001-039987477-3",
				"Blind Pig",
				dtstart,
				dtend,
				"1",
				"1",
				false,
				"eventful",
				"first event"
				);

			es2.Serialize();

			EventStore.CombineZonedEventStoresToZonelessEventStore(keene_test_hub, settings);

			var es = new ZonelessEventStore(calinfo_keene).Deserialize();

			Assert.That(es.events.Count == 1);

			var evt = es.events.Find(e => e.title == "event");

			var categories = evt.categories.Split(',').ToList();
			categories.Sort();
			Assert.That(categories.SequenceEqual(new List<string>() { "eventful", "music" }));
			Assert.That(evt.urls_and_sources.Keys.Count == 1);
		}

		#endregion

	}
}

