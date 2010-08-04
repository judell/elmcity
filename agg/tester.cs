using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Text;
using System.Net;
using CalendarAggregator;
using Newtonsoft.Json;
using System.Globalization;
using DDay.iCal;
using DDay.iCal.Components;
using DDay.iCal.Serialization;
using DDay.iCal.DataTypes;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace ConsoleApplication1
{
    public class tester
    {
   
        public static void Main()
        {
        var id = "grandstrandbloggers";
        Scheduler.init_task_for_id(id);
        var task = Scheduler.fetch_task_for_id(id);
        }



private static void upcoming_iterator(string id)
{
            var calinfo = new Calinfo(id);
            var collector = new Collector(calinfo);
            var args = string.Format("location={0}", calinfo.where);
            var method = "event.search";
            var page_count = 1;
            foreach (XElement evt in collector.UpcomingIterator(page_count, method, args))
            {
                string str_dtstart = evt.Attribute("utc_start").Value;
            }
}

#region upcoming_example
/*
         <event id="2776811" name="CV Library Endowment Booksale" description="Large ongoing booksale held on the first Thursday of each month to support the CV Library Endowment. Good assortment of very reasonably priced new and used books. Donations welcome." start_date="2009-06-04" end_date="" start_time="09:00:00" end_time="16:00:00" personal="0" selfpromotion="0" metro_id="" venue_id="190007" user_id="732075" category_id="6" date_posted="2009-05-27 14:45:40" watchlist_count="1" url="" distance="38.30" distance_units="miles" latitude="34.571" longitude="-111.8545" geocoding_precision="address" geocoding_ambiguous="0" venue_name="Camp Verde Community Library" venue_address="130 N Black Bridge Loop Rd" venue_city="Camp Verde" venue_state_name="Arizona" venue_state_code="AZ" venue_state_id="1000000" venue_country_name="United States" venue_country_code="us" venue_country_id="1000000" venue_zip="86322" ticket_url="" ticket_price="" ticket_free="0" photo_url="" num_future_events="0" start_date_last_rendition="Jun 4, 2009" utc_start="2009-06-04 16:00:00 UTC" utc_end="2009-06-04 19:00:00 UTC" />
 */
#endregion upcoming_example

private static void eventful_iterator(string id)
    {
    var calinfo = new Calinfo(id);
    var collector = new Collector(calinfo);
    string args = string.Format("date=Future&location={0}&page_size=10", calinfo.where);
    foreach (XElement evt in collector.EventfulIterator(1, args))
            {
            var title = Utils.get_xelt_value(evt, Configurator.no_ns, "title");
            var start_time = Utils.get_xelt_value(evt, Configurator.no_ns, "start_time");
            var venue_name = Utils.get_xelt_value(evt, Configurator.no_ns, "venue_name");
            }
    }

    #region eventful_example
        /*
<event id="E0-001-020377542-7@2009060608">
      <title>sin to win</title>
      <url>http://eventful.com/prescottvalley/events/sin-win-/E0-001-020377542-7@2009060608</url>
      <description></description>
      <start_time>2009-06-06 08:00:00</start_time>
      <stop_time>2009-06-07 02:00:00</stop_time>
      <tz_id></tz_id>
      <tz_olson_path></tz_olson_path>
      <tz_country></tz_country>
      <tz_city></tz_city>
      <venue_id>V0-001-002214929-4</venue_id>
      <venue_url>http://eventful.com/prescottvalley/venues/porkys-barbaque-/V0-001-002214929-4</venue_url>
      <venue_name>porky's barbaque</venue_name>
      <venue_display>1</venue_display>
      <venue_address>the corner of gurley and MCcormick</venue_address>
      <city_name>Prescott Valley</city_name>
      <region_name>Arizona</region_name>
      <region_abbr>AZ</region_abbr>
      <postal_code>86314</postal_code>
      <country_name>United States</country_name>
      <country_abbr2>US</country_abbr2>
      <country_abbr>USA</country_abbr>
      <latitude>34.6278</latitude>
      <longitude>-112.263</longitude>
      <geocode_type>Zip Code Based GeoCodes</geocode_type>
      <all_day>0</all_day>
      <recur_string>weekly until September 26, 2009</recur_string>
      <trackback_count>0</trackback_count>
      <calendar_count>0</calendar_count>
      <comment_count></comment_count>
      <link_count>0</link_count>
      <going_count>0</going_count>
      <watching_count>0</watching_count>
      <created>2009-03-17 00:06:34</created>
      <owner>teeny7777777</owner>
      <modified>2009-03-17 00:07:45</modified>
      <performers></performers>
      <image>
        <url>http://static.eventful.com/images/small/I0-001/001/810/065-0.gif</url>
        <width>48</width>
        <height>48</height>
        <caption></caption>
        <thumb>
          <url>http://static.eventful.com/images/thumb/I0-001/001/810/065-0.gif</url>
          <width>48</width>
          <height>48</height>
        </thumb>
        <small>
          <url>http://static.eventful.com/images/small/I0-001/001/810/065-0.gif</url>
          <width>48</width>
          <height>48</height>
        </small>
        <medium>
          <url>http://static.eventful.com/images/medium/I0-001/001/810/065-0.gif</url>
          <width>128</width>
          <height>128</height>
        </medium>
      </image>
      <privacy>1</privacy>
      <calendars></calendars>
      <groups></groups>
      <going></going>
    </event>
         */
    #endregion eventful_example

private static void store_account_metadata_for_id(string id)
        {
            var d = Utils.make_default_delicious();
            d.store_account_metadata(id, true, new Dictionary<string, string>());
        }

        private static void store_metadata_for_id_feeds(string id)
        {
            var d = Utils.make_default_delicious();
            var fr = new FeedRegistry(id);
            var dict = d.fetch_account_feeds(id, Configurator.trusted_ics_feed);
            foreach (var feedurl in dict.Keys)
                fr.AddFeed(feedurl, dict[feedurl]);
            d.store_metadata_for_feeds(id, fr);
        }
    }

}
