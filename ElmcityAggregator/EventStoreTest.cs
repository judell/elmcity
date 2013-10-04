﻿/* ********************************************************************************
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
using System.Linq;
using System.Net;
using ElmcityUtils;
using NUnit.Framework;

namespace CalendarAggregator
{
	[TestFixture]
	public class EventStoreTest
	{
		private Calinfo calinfo = new Calinfo(Configurator.testid);

		static TimeZoneInfo tzinfo = Utils.TzinfoFromName(Configurator.default_tz);

		public const string test_container = Configurator.testid;
		public const string test_category = "test_category";
		public const string test_location = "test_location";
		public const string test_lat = "42.9336";
		public const string test_lon = "-72.2786";
		public const string test_description = "test_description";

		static DateTimeWithZone now_with_zone = Utils.NowInTz(tzinfo);
		static DateTimeWithZone min_with_zone = DateTimeWithZone.MinValue(tzinfo);
		//private ZonedEvent in_evt0_zoneless = new ZonedEvent("title0", "http://elmcity.info", "source", now_with_zone, min_with_zone, false, test_category);
		private ZonedEvent in_evt0_zoned = new ZonedEvent("title0", "http://elmcity.info", "source", false, test_lat, test_lon, test_category, now_with_zone, min_with_zone, test_description, test_location);
		private ZonedEvent in_evt1_zoned = new ZonedEvent("title1", "http://elmcity.info", "source", false, test_lat, test_lon, null, now_with_zone, min_with_zone, test_description, test_location);

		static DateTime now = DateTime.Now;
		static private DateTime min = DateTime.MinValue;
		private ZonelessEvent in_evt0_zoneless = new ZonelessEvent("title0", "http://elmcity.info", "source", false, test_lat, test_lon, test_category, now, DateTime.MinValue, test_description, test_location);
		private ZonelessEvent in_evt1_zoneless = new ZonelessEvent("title1", "http://elmcity.info", "source", false, test_lat, test_lon, null, now, DateTime.MinValue, test_description, test_location);

		static private int expected_year = 9999;
		static private int expected_hour = 9;
		//static iCalDateTime idt = new DateTime(expected_year, 1, 1, expected_hour, 1, 1);

		static private DateTime dt1 = new DateTime(expected_year, 1, 1, expected_hour, 1, 1);
		static private DateTime dt2 = new DateTime(expected_year, 2, 2, expected_hour, 2, 2);

		static private string title1 = "e1";
		static private string title2 = "e2";

		static private string source1 = "s1";
		static private string source2 = "s2";

		static private DateTimeWithZone dt1_with_zone = new DateTimeWithZone(dt1, tzinfo);
		static private DateTimeWithZone dt2_with_zone = new DateTimeWithZone(dt2, tzinfo);

		static private BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();



		[Test]
		public void AddZonelessEventIncrementsCount()
		{
			var zoneless = new ZonelessEventStore(calinfo);
			Assert.AreEqual(0, zoneless.events.Count);
			zoneless.AddEvent(in_evt0_zoneless.title, in_evt0_zoneless.url, in_evt0_zoneless.source, test_lat, test_lon, in_evt0_zoneless.dtstart, in_evt0_zoneless.dtend, allday: false, categories: test_category, description: test_description, location: test_location);
			Assert.AreEqual(1, zoneless.events.Count);
		}

		[Test]
		public void SerializeAndDeserializeZonelessEventStoreYieldsExpectedEvents()
		{
			var es = new ZonelessEventStore(calinfo);

			es.AddEvent(title:title1, url:"http://foo", source:source1, lat: null, lon: null, dtstart: dt1, dtend: min, allday: false, categories: test_category, description: test_description, location: test_location);

			var evt1 = es.events.Find(e => e.title == title1);
			evt1.urls_and_sources = new Dictionary<string, string>() { { "http://foo", source1 } };

			es.AddEvent(title:title2, url:"http://bar", source:source2, lat: null, lon: null, dtstart: dt2, dtend: min, allday: false, categories:null, description: test_description, location: test_location);

			var evt2 = es.events.Find(e => e.title == title2);
			evt2.urls_and_sources = new Dictionary<string, string>() { { "http://bar", source2 } };

			es.Serialize();

			var es2 = new ZonelessEventStore(calinfo).Deserialize();

			es2.SortEventList();

			Assert.That(es2.events.Count == 2);
			evt1 = es2.events[0];
			evt2 = es2.events[1];

			Assert.AreEqual(evt1.dtstart, dt1);
			Assert.AreEqual(evt2.dtstart, dt2);
			Assert.AreEqual(evt1.title, title1);
			Assert.AreEqual(evt2.title, title2);
		}


		[Test]
		public void AddZonedEventIncrementsCount()
		{
			var zoned = new ZonedEventStore(calinfo, SourceType.ical);
			Assert.AreEqual(0, zoned.events.Count);
			zoned.AddEvent(in_evt0_zoned.title, in_evt0_zoned.url, in_evt0_zoned.source, in_evt0_zoned.dtstart, in_evt0_zoned.dtend, test_lat, test_lon, false, in_evt0_zoned.categories, test_description, test_location);
			Assert.AreEqual(1, zoned.events.Count);
		}

		[Test]
		public void SerializeTwoZonedEvents()
		{
			var zoned = new ZonedEventStore(calinfo, SourceType.ical);
			zoned.AddEvent(in_evt0_zoned.title, in_evt0_zoned.url, in_evt0_zoned.source, in_evt0_zoned.dtstart, in_evt0_zoned.dtend, test_lat, test_lon, in_evt0_zoned.allday, test_category, test_description, test_location);
			zoned.AddEvent(in_evt1_zoned.title, in_evt1_zoned.url, in_evt1_zoned.source, in_evt1_zoned.dtstart, in_evt1_zoned.dtend, test_lat, test_lon, in_evt1_zoned.allday, test_category, test_description, test_location);
			Assert.AreEqual(2, zoned.events.Count);
			var response = bs.SerializeObjectToAzureBlob(zoned, test_container, zoned.objfile);
			//Console.WriteLine(response.HttpResponse.DataAsString());
			Assert.AreEqual(HttpStatusCode.Created, response.HttpResponse.status);
		}

		[Test]
		public void DeserializeTwoZonedEvents()
		{
			SerializeTwoZonedEvents();
			Utils.Wait(5);
			var zoned = new ZonedEventStore(calinfo, SourceType.ical);
			var uri = BlobStorage.MakeAzureBlobUri(test_container, zoned.objfile, false);
			var obj = (ZonedEventStore)BlobStorage.DeserializeObjectFromUri(uri);
			var out_evts = (List<ZonedEvent>)obj.events;
			Assert.AreEqual(2, out_evts.Count);
			var out_evt = (ZonedEvent)out_evts[0];
			Assert.AreEqual(in_evt0_zoned.dtstart.UniversalTime.Ticks, out_evt.dtstart.UniversalTime.Ticks);
			Assert.AreEqual(in_evt0_zoned.title, out_evt.title);
		}

		[Test]
		public void SerializeAndDeserializeZonedEventStoreYieldsExpectedEvents()
		{
			var es = new ZonedEventStore(calinfo, SourceType.ical);
			es.AddEvent(title:title1, url:"http://foo", source:source1, dtstart:dt1_with_zone, dtend:min_with_zone, allday:false, lat:test_lat, lon:test_lon, categories:test_category, description:test_description, location: test_location);
			es.AddEvent(title:title2, url:"http://bar", source:source2, dtstart:dt2_with_zone, dtend:min_with_zone, lat:test_lat, lon:test_lon, allday:false, categories:test_category, description:test_description, location: test_location);

			bs.SerializeObjectToAzureBlob(es, test_container, es.objfile);

			var uri = BlobStorage.MakeAzureBlobUri(test_container, es.objfile,false);
			var es2 = (ZonedEventStore)BlobStorage.DeserializeObjectFromUri(uri);

			var evt1 = es2.events.First();
			var evt2 = es2.events.Last();

			Assert.AreEqual(evt1.dtstart.UniversalTime.Ticks, dt1_with_zone.UniversalTime.Ticks);
			Assert.AreEqual(evt2.dtstart.UniversalTime.Ticks, dt2_with_zone.UniversalTime.Ticks);
			Assert.AreEqual(evt1.title, title1);
			Assert.AreEqual(evt2.title, title2);
		}

	}
}
