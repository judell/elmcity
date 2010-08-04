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

        public const string test_container = "events";
        public const string test_category = "test_category";

        static Utils.DateTimeWithZone now_with_zone = Utils.NowInTz(tzinfo);
        static Utils.DateTimeWithZone min_with_zone = Utils.DateTimeWithZone.MinValue(tzinfo);
        //private ZonedEvent in_evt0_zoneless = new ZonedEvent("title0", "http://elmcity.info", "source", now_with_zone, min_with_zone, false, test_category);
        private ZonedEvent in_evt0_zoned = new ZonedEvent("title0", "http://elmcity.info", "source", false, test_category, now_with_zone, min_with_zone);
        private ZonedEvent in_evt1_zoned = new ZonedEvent("title1", "http://elmcity.info", "source", false, test_category, now_with_zone, min_with_zone);

        static DateTime now = DateTime.Now;
        static private DateTime min = DateTime.MinValue;
        private ZonelessEvent in_evt0_zoneless = new ZonelessEvent("title0", "http://elmcity.info", "source", false, test_category, now, DateTime.MinValue);
        private ZonelessEvent in_evt1_zoneless = new ZonelessEvent("title1", "http://elmcity.info", "source", false, test_category, now, DateTime.MinValue);

        static private int expected_year = 9999;
        static private int expected_hour = 9;
        //static iCalDateTime idt = new DateTime(expected_year, 1, 1, expected_hour, 1, 1);

        static private DateTime dt1 = new DateTime(expected_year, 1, 1, expected_hour, 1, 1);
        static private DateTime dt2 = new DateTime(expected_year, 2, 2, expected_hour, 2, 2);

        static private string title1 = "e1";
        static private string title2 = "e2";

        static private string source1 = "s1";
        static private string source2 = "s2";

        static private Utils.DateTimeWithZone dt1_with_zone = new Utils.DateTimeWithZone(dt1, tzinfo);
        static private Utils.DateTimeWithZone dt2_with_zone = new Utils.DateTimeWithZone(dt2, tzinfo);

        static private BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();



        [Test]
        public void AddZonelessEventIncrementsCount()
        {
            var zoneless = new ZonelessEventStore(calinfo, null);
            Assert.AreEqual(0, zoneless.events.Count);
            zoneless.AddEvent(in_evt0_zoneless.title, in_evt0_zoneless.url, in_evt0_zoneless.source, in_evt0_zoneless.dtstart, in_evt0_zoneless.dtend, false, in_evt0_zoneless.categories);
            Assert.AreEqual(1, zoneless.events.Count);
        }

        [Test]
        public void SerializeTwoZonelessEvents()
        {
            var zoneless = new ZonelessEventStore(calinfo, null);
            zoneless.AddEvent(in_evt0_zoneless.title, in_evt0_zoneless.url, in_evt0_zoneless.source, in_evt0_zoneless.dtstart, in_evt0_zoneless.dtend, false, in_evt0_zoneless.categories);
            zoneless.AddEvent(in_evt1_zoneless.title, in_evt1_zoneless.url, in_evt1_zoneless.source, in_evt1_zoneless.dtstart, in_evt1_zoneless.dtend, false, in_evt1_zoneless.categories);
            Assert.AreEqual(2, zoneless.events.Count);
            var response = bs.SerializeObjectToAzureBlob(zoneless, test_container, zoneless.objfile);
            Console.WriteLine(response.HttpResponse.DataAsString());
            Assert.AreEqual(HttpStatusCode.Created, response.HttpResponse.status);
        }

        [Test]
        public void DeserializeTwoZonelessEvents()
        {
            SerializeTwoZonelessEvents();
            Utils.Wait(5);
            var zoneless = new ZonelessEventStore(calinfo, null);
            var uri = BlobStorage.MakeAzureBlobUri(test_container, zoneless.objfile);
            var obj = (ZonelessEventStore)BlobStorage.DeserializeObjectFromUri(uri);
            var out_evts = (List<ZonelessEvent>)obj.events;
            Assert.AreEqual(2, out_evts.Count);
            var out_evt = (ZonelessEvent)out_evts[0];
            Assert.AreEqual(in_evt0_zoneless.dtstart, out_evt.dtstart);
            Assert.AreEqual(in_evt0_zoneless.title, out_evt.title);
        }

        [Test]
        public void SerializeAndDeserializeZonelessEventStoreYieldsExpectedEvents()
        {
            var es = new ZonelessEventStore(calinfo, null);
            es.AddEvent(title1, "http://foo", source1, dt1, min, false, test_category);
            es.AddEvent(title2, "http://bar", source2, dt2, min, false);

            bs.SerializeObjectToAzureBlob(es, test_container, es.objfile);

            var uri = BlobStorage.MakeAzureBlobUri(test_container, es.objfile);
            var es2 = (ZonelessEventStore)BlobStorage.DeserializeObjectFromUri(uri);

            es2.SortEventSublists();
            es2.GroupEventsByDatekey();
            es2.SortDatekeys();
            es2.SortEventSublists();

            Assert.That(es2.event_dict.Keys.Count == 2);
            var list1 = es2.event_dict[es2.event_dict.Keys.First()];
            var list2 = es2.event_dict[es2.event_dict.Keys.Last()];
            var evt1 = list1.First();
            var evt2 = list2.First();

            Assert.AreEqual(evt1.dtstart, dt1);
            Assert.AreEqual(evt2.dtstart, dt2);
            Assert.AreEqual(evt1.title, title1);
            Assert.AreEqual(evt2.title, title2);

            evt1 = es2.events.First();
            evt2 = es2.events.Last();

            Assert.AreEqual(evt1.dtstart, dt1);
            Assert.AreEqual(evt2.dtstart, dt2);
            Assert.AreEqual(evt1.title, title1);
            Assert.AreEqual(evt2.title, title2);


        }


        [Test]
        public void AddZonedEventIncrementsCount()
        {
            var zoned = new ZonedEventStore(calinfo, null);
            Assert.AreEqual(0, zoned.events.Count);
            zoned.AddEvent(in_evt0_zoned.title, in_evt0_zoned.url, in_evt0_zoned.source, in_evt0_zoned.dtstart, in_evt0_zoned.dtend, false, in_evt0_zoned.categories);
            Assert.AreEqual(1, zoned.events.Count);
        }

        [Test]
        public void SerializeTwoZonedEvents()
        {
            var zoned = new ZonedEventStore(calinfo, null);
            zoned.AddEvent(in_evt0_zoned.title, in_evt0_zoned.url, in_evt0_zoned.source, in_evt0_zoned.dtstart, in_evt0_zoned.dtend, in_evt0_zoned.allday);
            zoned.AddEvent(in_evt1_zoned.title, in_evt1_zoned.url, in_evt1_zoned.source, in_evt1_zoned.dtstart, in_evt1_zoned.dtend, in_evt1_zoned.allday);
            Assert.AreEqual(2, zoned.events.Count);
            var response = bs.SerializeObjectToAzureBlob(zoned, test_container, zoned.objfile);
            Console.WriteLine(response.HttpResponse.DataAsString());
            Assert.AreEqual(HttpStatusCode.Created, response.HttpResponse.status);
        }

        [Test]
        public void DeserializeTwoZonedEvents()
        {
            SerializeTwoZonedEvents();
            Utils.Wait(5);
            var zoned = new ZonedEventStore(calinfo, null);
            var uri = BlobStorage.MakeAzureBlobUri(test_container, zoned.objfile);
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
            var es = new ZonedEventStore(calinfo, null);
            es.AddEvent(title1, "http://foo", source1, dt1_with_zone, min_with_zone, false, test_category);
            es.AddEvent(title2, "http://bar", source2, dt2_with_zone, min_with_zone, false);

            bs.SerializeObjectToAzureBlob(es, test_container, es.objfile);

            var uri = BlobStorage.MakeAzureBlobUri(test_container, es.objfile);
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
