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
using DDay.iCal.Components;
using DDay.iCal.DataTypes;
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
        //private static Calinfo test_calinfo = new Calinfo(Configurator.testid);
        private static Calinfo test_calinfo = new Calinfo(ElmcityUtils.Configurator.azure_compute_account);
        private static Apikeys test_apikeys = new Apikeys();
        private static string lookup_lat = Utils.LookupLatLon(test_apikeys.yahoo_api_key, test_calinfo.where)[0];
        private static string lookup_lon = Utils.LookupLatLon(test_apikeys.yahoo_api_key, test_calinfo.where)[1];
        private static int radius = Configurator.default_radius;
        private string test_upcoming_args = string.Format("location={0},{1}&radius={2}&min_date={3}", lookup_lat, lookup_lon, radius, min_date);

		private Dictionary<string, string> settings = GenUtils.GetSettingsFromAzureTable();

        private string sample_ics = @"BEGIN:VCALENDAR
PRODID:-//Google Inc//Google Calendar 70.9054//EN
VERSION:2.0
CALSCALE:GREGORIAN
METHOD:PUBLISH
X-WR-CALNAME:Saskatoon Canoe Club
X-WR-TIMEZONE:America/Regina
X-WR-CALDESC:A calendar of upcoming trips\, races & events for the Saskatoo
 n Canoe Club.
BEGIN:VTIMEZONE
TZID:America/Regina
X-LIC-LOCATION:America/Regina
BEGIN:STANDARD
TZOFFSETFROM:-0600
TZOFFSETTO:-0600
TZNAME:CST
DTSTART:19700101T000000
END:STANDARD
END:VTIMEZONE
BEGIN:VEVENT
DTSTART:__SINGLE_DATE_START__T010000Z
DTEND:__SINGLE_DATE_END__T030000Z
UID:enrmd2s6g0bmjallkilod7dc3k@google.com
LOCATION:Broadway Theatre\, 715 Broadway Ave\, Saskatoon
SUMMARY:ROWED TRIP\, Colin Angus and Julie Angus
END:VEVENT
BEGIN:VEVENT
DTSTART;TZID=America/Regina:20070913T190000
DTEND;TZID=America/Regina:20070913T203000
RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=TH;WKST=MO
UID:8go52ag1v0qf8t5417u55dmnpk@google.com
LOCATION:Tastebuds coffee shop\, 1624 Lorne Avenue\, Saskatoon
SUMMARY:SCC Social/Coffee: Tastebuds
END:VEVENT
END:VCALENDAR";


        [Test]
        public void IcalFeedYieldsNonzeroEvents()
        {
            var response = HttpUtils.FetchUrl(test_feedurl);
            string feedtext = response.DataAsString();
            StringReader sr = new StringReader(feedtext);
            var ical = iCalendar.LoadFromStream(sr);
            Assert.That(ical.Events.Count > 0);
        }

        [Test]
        public void iCalSingleEventIsFuture()
        {
            var ics = sample_ics;
            var futuredate = DateTime.Now + new TimeSpan(10, 0, 0, 0, 0);
            ics = ics.Replace("__SINGLE_DATE_START__", futuredate.ToString("yyyyMMdd"));
            ics = ics.Replace("__SINGLE_DATE_END__", futuredate.ToString("yyyyMMdd"));
            StringReader sr = new StringReader(ics);
            var ical = iCalendar.LoadFromStream(sr);
            var evt = ical.Events[0];
            var ical_now = new iCalDateTime(DateTime.Now.ToUniversalTime());
            Assert.That(evt.GetOccurrences(ical_now).Count == 0);
			var collector = new Collector(test_calinfo, settings);
            Assert.IsTrue(collector.IsCurrentOrFutureDTStartInTz(evt.DTStart));
        }

        [Test]
        public void iCalSingleEventIsPast()
        {
            var ics = sample_ics;
            var pastdate = DateTime.Now - new TimeSpan(10, 0, 0, 0, 0);
            ics = ics.Replace("__SINGLE_DATE_START__", pastdate.ToString("yyyyMMdd"));
            ics = ics.Replace("__SINGLE_DATE_END__", pastdate.ToString("yyyyMMdd"));
            StringReader sr = new StringReader(ics);
            var ical = iCalendar.LoadFromStream(sr);
            var evt = ical.Events[0];
			var collector = new Collector(test_calinfo, settings);
            Assert.IsFalse(collector.IsCurrentOrFutureDTStartInTz(evt.DTStart));
        }

        [Test]
        public void iCalRecurringEventHasFutureOccurrences()
        {
            var ics = sample_ics;
            ics = ics.Replace("__SINGLE_DATE_START__", DateTime.Now.ToString("yyyyMMdd"));
            ics = ics.Replace("__SINGLE_DATE_END__", DateTime.Now.ToString("yyyyMMdd"));
            StringReader sr = new StringReader(ics);
            var ical = iCalendar.LoadFromStream(sr);
            Assert.That(ical.Events.Count > 0);
            var evt = ical.Events[1];
            DateTime now = DateTime.Now;
            DateTime then = now.AddDays(90);
            List<Occurrence> occurrences = evt.GetOccurrences(now, then);
            Assert.That(occurrences.Count > 1);
            var period = occurrences[1].Period;
			var collector = new Collector(test_calinfo, settings);
            Assert.That(collector.IsCurrentOrFutureDTStartInTz(period.StartTime));
        }

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
            var events = (IEnumerable<XElement>)collector.EventfulIterator(1, test_eventful_args);
            Assert.That(events.Count() > 0);
        }

        [Test]
        public void EventfulQueryYieldsValidFirstEvent()
        {
			var collector = new Collector(test_calinfo, settings);
            var events = (IEnumerable<XElement>)collector.EventfulIterator(1, test_eventful_args);
            var es = new ZonedEventStore(test_calinfo, null);
            collector.AddEventfulEvent(es, test_venue, events.First());
            Assert.That(es.events[0].title != "");
        }

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
			var calinfo = new Calinfo("sfcals");
			var collector = new Collector(calinfo, settings);
            var events = (IEnumerable<XElement>)collector.UpcomingIterator(1, method);
            var es = new ZonedEventStore(test_calinfo, null);
            //Console.WriteLine(events.Count());
            var evt = events.First();
            string str_dtstart = evt.Attribute("utc_start").Value;
            var dtstart = Utils.DateTimeFromDateStr(str_dtstart);
            var dtstart_with_zone = new Utils.DateTimeWithZone(dtstart, test_calinfo.tzinfo);
            collector.AddUpcomingEvent(es, test_venue, evt, dtstart_with_zone);
            Assert.That(es.events[0].title != "");
        }
    }
}

