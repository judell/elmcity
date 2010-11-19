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
using System.Xml.Linq;
using ElmcityUtils;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace CalendarAggregator
{
    // used in default html rendering to carve up the day into chunks
    public enum TimeOfDay { Initialized, AllDay, Morning, Lunch, Afternoon, Evening, Night, WeeHours };

    public static class Configurator
    {
		private static Dictionary<string, string> settings = new Dictionary<string, string>();

        // encapsulated settings

        #region apikeys

        public static string eventful_api_key
        { get { return _eventful_api_key; } }

        private static string _eventful_api_key
        { get { return GetSettingValue("eventful_api_key"); } }

        public static string upcoming_api_key
        { get { return _upcoming_api_key; } }

        private static string _upcoming_api_key
        { get { return GetSettingValue("upcoming_api_key"); } }

        public static string yahoo_api_key
        { get { return _yahoo_api_key; } }

        private static string _yahoo_api_key
        { get { return GetSettingValue("yahoo_api_key"); } }

        public static string eventbrite_api_key
        { get { return _eventbrite_api_key; } }

        private static string _eventbrite_api_key
        { get { return GetSettingValue("eventbrite_api_key"); } }

        public static string facebook_api_key
        { get { return _facebook_api_key; } }

        private static string _facebook_api_key
        { get { return GetSettingValue("facebook_api_key"); } }

        #endregion

        #region delicious

        public static string delicious_master_account
        {  get { return _delicious_master_account; } }

        private static string _delicious_master_account
        {  get { return GetSettingValue("delicious_master_account"); } }

        public static string delicious_master_password
        {  get { return _delicious_master_password; } }

        private static string _delicious_master_password
        {  get { return GetSettingValue("delicious_master_password"); } }

        public static int delicious_delay_seconds
        {  get { return _delicious_delay_seconds; } }

        private static int _delicious_delay_seconds
        {  get { return Convert.ToInt32(GetSettingValue("delicious_delay_seconds")); } }

        public static int delicious_admin_interval_hours
        {  get { return _delicious_admin_interval_hours; } }

        private static int _delicious_admin_interval_hours
        {  get { return Convert.ToInt32(GetSettingValue("delicious_admin_interval_hours")); } }

        // unencapsulated for now

        public const string delicious_blocked_message = "you have been blocked";
        public const string delicious_curated_hubs_query = "https://api.del.icio.us/v1/posts/recent?tag=calendarcuration&count=100";

        public const string delicious_curation_tag = "calendarcuration";
        public const string delicious_trusted_ics_feed = "trusted+ics+feed";
        public const string delicious_trusted_indirect_feed = "trusted+indirect+feed";
        public const string delicious_trusted_eventful_contributor = "trusted+eventful+contributor";
        public const string delicious_rssbase = "http://feeds.delicious.com/v2/rss";
        public const string delicious_base = "http://delicious.com";

        #endregion

        #region twitter

        public static string twitter_account
        { get { return _twitter_account; } }

        private static string _twitter_account
        { get { return GetSettingValue("twitter_account"); } }

        public static string twitter_password
        { get { return _twitter_password; } }

        private static string _twitter_password
        { get { return GetSettingValue("twitter_password"); } }

        public const int twitter_max_direct_messages = 200;

        #endregion

        #region numeric settings

        public static int default_log_transfer_minutes
        { get { return _default_log_transfer_minutes; } }

        private static int _default_log_transfer_minutes
        { get { return Convert.ToInt32(GetSettingValue("default_log_transfer_minutes")); } }

        public static int default_file_transfer_minutes
        { get { return _default_file_transfer_minutes; } }

        private static int _default_file_transfer_minutes
        { get { return Convert.ToInt32(GetSettingValue("default_file_transfer_minutes")); } }

        public static int scheduler_check_interval_minutes
        { get { return _scheduler_check_interval_minutes; } }

        private static int _scheduler_check_interval_minutes
        { get { return Convert.ToInt32(GetSettingValue("scheduler_check_interval_minutes")); } }

        public static int where_aggregate_interval_hours
        { get { return _where_aggregate_interval_hours; } }

        private static int _where_aggregate_interval_hours
        { get { return Convert.ToInt32(GetSettingValue("where_aggregate_interval_hours")); } }

        public static int what_aggregate_interval_hours
        { get { return _what_aggregate_interval_hours; } }

        private static int _what_aggregate_interval_hours
        { get { return Convert.ToInt32(GetSettingValue("what_aggregate_interval_hours")); } }

        public static int general_admin_interval_hours
        { get { return _general_admin_interval_hours; } }

        private static int _general_admin_interval_hours
        { get { return Convert.ToInt32(GetSettingValue("general_admin_interval_hours")); } }

        public static int webrole_reload_interval_hours
        { get { return _webrole_reload_interval_hours; } }

        private static int _webrole_reload_interval_hours
        { get { return Convert.ToInt32(GetSettingValue("webrole_reload_interval_hours")); } }

        public static int webrole_script_timeout_seconds
        { get { return _webrole_script_timeout_seconds; } }

        private static int _webrole_script_timeout_seconds
        { get { return Convert.ToInt32(GetSettingValue("webrole_script_timeout_seconds")); } }

        public static int process_monitor_interval_minutes
        { get { return _process_monitor_interval_minutes; } }

        private static int _process_monitor_interval_minutes
        { get { return Convert.ToInt32(GetSettingValue("process_monitor_interval_minutes")); } }

        public static int worker_reload_interval_hours
        { get { return _worker_reload_interval_hours; } }

        private static int _worker_reload_interval_hours
        { get { return Convert.ToInt32(GetSettingValue("worker_reload_interval_hours")); } }

        public static string process_monitor_table
        { get { return _process_monitor_table; } }

        private static string _process_monitor_table
        { get { return GetSettingValue("process_monitor_table"); } }

		public static int testrunner_interval_hours
		{ get { return _testrunner_interval_hours; } }

		private static int _testrunner_interval_hours
		{ get { return Convert.ToInt32(GetSettingValue("testrunner_interval_hours")); } }

		// http://blog.jonudell.net/2010/05/07/facebook-is-now-an-elmcity-event-source/
		public static int facebook_mystery_offset_hours
		{ get { return _facebook_mystery_offset_hours; } }

		private static int _facebook_mystery_offset_hours
		{ get { return Convert.ToInt32(GetSettingValue("facebook_mystery_offset_hours")); } }

        #endregion

		#region boolean settings

		public static bool do_ical_validation
		{ get { return _do_ical_validation; } }

		private static bool _do_ical_validation
		{ get { return (bool)(GetSettingValue("do_ical_validation", reload: true) == "true"); } }

		#endregion

		#region template settings

		public static string default_css { get { return _default_css; } }
		private static string _default_css = ElmcityUtils.Configurator.azure_blobhost + "/admin/" + GetSettingValue("elmcity_css");

		public static string default_img_html { get { return _default_img_html; } }
		private static string _default_img_html { get { return GetSettingValue("default_img_html"); } }

		public static Uri default_img_url { get { return _default_img_url; } }
		private static Uri _default_img_url = new Uri(ElmcityUtils.Configurator.azure_blobhost + "/admin/" + GetSettingValue("elmcity_img"));

		public static Uri default_template_url { get { return _default_template_url; } }
		private static Uri _default_template_url = new Uri(ElmcityUtils.Configurator.azure_blobhost + "/admin/" + GetSettingValue("elmcity_tmpl"));

		public static Uri default_contribute_url { get { return _default_contribute_url; } }
		private static Uri _default_contribute_url = new Uri(GetSettingValue("elmcity_contribute"));

		public static string default_display_width { get { return _default_display_width; } }
		private static string _default_display_width = GetSettingValue("elmcity_display_width");

		public static Uri what_template_url { get { return _what_template_url; } }
		private static Uri _what_template_url = new Uri(ElmcityUtils.Configurator.azure_blobhost + "/admin/" + GetSettingValue("elmcity_tmpl"));

#endregion template settings

		// non-encapsulated (for now) settings

        public const int azure_log_max_minutes = 500;
        public const int reverse_dns_timeout_seconds = 3;
        public const int rss_default_items = 50;
        public const int odata_since_hours_ago = 24;

        public const string default_html_window_name = "elmcity_events";

        // these are the keys for key/value pairs parsed from the DESCRIPTION property of any iCalendar event
        // e.g.:
        // url=http://...
        // category=music,bluegrass
        public static List<string> ical_description_metakeys
        { get { return _ical_description_metakeys; } }

        private static List<string> _ical_description_metakeys = new List<string>() { "url", "category" };

        // used commonly by various tests, so collected here
        public const string testid = "events";
        public const string test_metadata_property_key_prefix = "property";
        public const string test_metadata_property_key = "testkey";
        public const string test_metadata_property_value = "testvalue";

        public static XNamespace xcal_ns { get { return _xcal_ns; } }
        private static XNamespace _xcal_ns = "urn:ietf:params:xml:ns:xcal";

        // was used by EventCollector.within_range, but idle for now
        // public static XNamespace geo_ns { get { return _geo_ns; } }
        // private static XNamespace _geo_ns = "http://www.w3.org/2003/01/geo/wgs84_pos#";

        // dublin core, not needed for now
        // public static XNamespace dc_ns { get { return _dc_ns; } }
        // private static XNamespace _dc_ns = "http://purl.org/dc/elements/1.1/";

        // for doug day's ical validation service
        public static XNamespace icalvalid_ns { get { return _icalvalid_ns; } }
        private static XNamespace _icalvalid_ns = "http://icalvalid.wikidot.com/validation";

        public const string local_ical_validator = "http://elmcity.cloudapp.net/validate";
        public const string remote_ical_validator = "http://icalvalid.cloudapp.net/?uri=";

        // todo: make this configurable in hub metadata
        public const int icalendar_horizon_in_days = 90;

        // used by the (nascent) recreation of the fusecal html->ical service.
        // example: myspace
        // the curator bookmarks: http://www.myspace.com/lonesomelake, and tags filter=keene
        // internally it becomes: http://elmcity.cloudapp.net/services=fusecal?url=http://www.myspace.com/lonesomelake&filter=keene&tz_source=eastern&tz_dest=eastern
        public const string fusecal_service = "http://" + ElmcityUtils.Configurator.appdomain + "/services/fusecal?url={0}&filter={1}&tz_source={2}&tz_dest={3}";

		public const string ics_from_xcal_service = "http://" + ElmcityUtils.Configurator.appdomain + "/ics_from_xcal?url={0}&tzname={1}&source={2}&use_utc={3}";

		public const string ics_from_vcal_service = "http://" + ElmcityUtils.Configurator.appdomain + "/ics_from_vcal?url={0}&tzname={1}&source={2}&use_utc={3}";

        // the "fusecal" service is written in python, this is the dispatcher that runs the appropriate 
        // parser for, e.g., myspace or librarything
        public static string fusecal_dispatcher = ElmcityUtils.Configurator.azure_blobhost + "/admin/fusecal.py";

		// things to do each time the worker's Run method loops
		public static string iron_python_run_script_url = ElmcityUtils.Configurator.azure_blobhost + "/admin/_run.py";

        // used to report populations (and event densities) for where hubs
        public const string census_city_population_estimates = "http://www.census.gov/popest/cities/files/SUB-EST2008-ALL.csv";

        public const string ALL_DAY = "All Day";

        // baseline year/month/day for time-of-day chunks
        public const int DT_COMP_YEAR = 2000;
        public const int DT_COMP_MONTH = 1;
        public const int DT_COMP_DAY = 1;

        private static DateTime MakeCompDT(int day, int hour, int minute)
        {
            return new DateTime(DT_COMP_YEAR, DT_COMP_MONTH, day, hour, minute, 0);
        }

        public static DateTime MIDNIGHT_LAST { get { return _MIDNIGHT_LAST; } }
        private static DateTime _MIDNIGHT_LAST = MakeCompDT(day: DT_COMP_DAY, hour: 0, minute: 0);

        public static DateTime MIDNIGHT_NEXT { get { return _MIDNIGHT_NEXT; } }
        private static DateTime _MIDNIGHT_NEXT = MakeCompDT(day: DT_COMP_DAY + 1, hour: 0, minute: 0);

        public static DateTime MORNING_BEGIN { get { return _MORNING_BEGIN; } }
        private static DateTime _MORNING_BEGIN = MakeCompDT(day: DT_COMP_DAY, hour: 5, minute: 0);

        public static DateTime LUNCH_BEGIN { get { return _LUNCH_BEGIN; } }
        private static DateTime _LUNCH_BEGIN = MakeCompDT(day: DT_COMP_DAY, hour: 11, minute: 30);

        public static DateTime AFTERNOON_BEGIN { get { return _AFTERNOON_BEGIN; } }
        private static DateTime _AFTERNOON_BEGIN = MakeCompDT(day: DT_COMP_DAY, hour: 13, minute: 30);

        public static DateTime EVENING_BEGIN { get { return _EVENING_BEGIN; } }
        private static DateTime _EVENING_BEGIN = MakeCompDT(day: DT_COMP_DAY, hour: 17, minute: 30);

        public static DateTime NIGHT_BEGIN { get { return _NIGHT_BEGIN; } }
        private static DateTime _NIGHT_BEGIN = MakeCompDT(day: DT_COMP_DAY, hour: 21, minute: 0);

        public static DateTime WEE_HOURS_BEGIN { get { return _WEE_HOURS_BEGIN; } }
        private static DateTime _WEE_HOURS_BEGIN = MakeCompDT(day: DT_COMP_DAY, hour: 1, minute: 30);

        public const int max_radius = 15;         // limit for where hubs
        public const int default_radius = 5;     // default for where hubs
        public const int default_population = 1;  // in case pop lookup fails

        public const string default_tz = "Eastern";
        public const string default_contact = "nobody-yet";

        public const string nowhere = "nowhere";
        public const string nothing = "nothing";

        // defaults for iis output cache
        public const int home_page_output_cache_duration = 60 * 60; // 1 hour
        public const int services_output_cache_duration = 60 * 10;   // 10 min

        // routine admin tasks, run from worker on a scheduled basis, are in this python script
        public static string iron_python_admin_script_url = ElmcityUtils.Configurator.azure_blobhost + "/admin/_admin.py";

        // part of experimental pshb implementation, idle for now
        //public static string pubsubhubub_uri = "http://pubsubhubbub.appspot.com/";

        // used by Utils.LookupUsPop
        public static Dictionary<string, string> state_abbrevs
        {
            get { return _state_abbrevs; }
        }
        private static Dictionary<string, string> _state_abbrevs = new Dictionary<string, string>()
{
{"al","alabama"},
{"ak","alaska"},
{"as","american samoa"},
{"az","arizona"},
{"ar","arkansas"},
{"ca","california"},
{"co","colorado"},
{"ct","connecticut"},
{"de","delaware"},
{"dc","district of columbia"},
{"fm","federated states of micronesia"},
{"fl","florida"},
{"ga","georgia"},
{"gu","guam"},
{"hi","hawaii"},
{"id","idaho"},
{"il","illinois"},
{"in","indiana"},
{"ia","iowa"},
{"ks","kansas"},
{"ky","kentucky"},
{"la","louisiana"},
{"me","maine"},
{"mh","marshall islands"},
{"md","maryland"},
{"ma","massachusetts"},
{"mi","michigan"},
{"mn","minnesota"},
{"ms","mississippi"},
{"mo","missouri"},
{"mt","montana"},
{"ne","nebraska"},
{"nv","nevada"},
{"nh","new hampshire"},
{"nj","new jersey"},
{"nm","new mexico"},
{"ny","new york"},
{"nc","north carolina"},
{"nd","north dakota"},
{"mp","northern mariana islands"},
{"oh","ohio"},
{"ok","oklahoma"},
{"or","oregon"},
{"pw","palau"},
{"pa","pennsylvania"},
{"pr","puerto rico"},
{"ri","rhode island"},
{"sc","south carolina"},
{"sd","south dakota"},
{"tn","tennessee"},
{"tx","texas"},
{"ut","utah"},
{"vt","vermont"},
{"vi","virgin islands"},
{"va","virginia"},
{"wa","washington"},
{"wv","west virginia"},
{"wi","wisconsin"},
{"wy","wyoming"}
};

		// try getting value from source-of-truth azure table, else non-defaults if overridden in azure config.
		// why? 
		// 1. dry (don't repeat yourself, in this case by not writing down settings twice, for worker and web role
		// 2. testing: tests run outside azure environment can use same defaults as used within
		private static string GetSettingValue(string setting_name, bool reload)
		{
			// GenUtils.LogMsg("info", "GetSettingValue", setting_name);
			string setting_value = null;

			if (settings.Count == 0 || reload)
				settings = GenUtils.GetSettingsFromAzureTable();

			if (settings.ContainsKey(setting_name))
				setting_value = settings[setting_name];

			if (setting_value == null)
			{
				try
				{
					if (RoleEnvironment.IsAvailable)
						setting_value = RoleEnvironment.GetConfigurationSettingValue(setting_name);
				}
				catch (Exception e)
				{
					GenUtils.LogMsg("exception", "GetSettingValue", e.Message + e.StackTrace);
				}
			}

			if (setting_value == null)
				GenUtils.LogMsg("info", "GetSettingValue: " + setting_name, " is null");

			return setting_value;
		}

		private static string GetSettingValue(string setting_name)
		{
			return GetSettingValue(setting_name, reload: false);
		}

        private static Delicious delicious = Delicious.MakeDefaultDelicious();

        // one Calinfo per hub, each an encapsulation of hub metadata
        public static Dictionary<string, Calinfo> Calinfos
        {
            get
            {
                var calinfos = new Dictionary<string, Calinfo>();
                var ids = delicious.LoadHubIdsFromAzureTable(); // could come from delicious, but prefer azure cache of that data
                foreach (var id in ids)
                {
                    try
                    {
                        var calinfo = new Calinfo(id);
                        calinfos.Add(id, new Calinfo(id));
                    }
                    catch (Exception e)
                    {
                        GenUtils.LogMsg("exception", "Calinfos.get", id + "," + e.Message);
                    }

                }
                return calinfos;
            }
        }
    }

    public class Apikeys
    {
        public string eventful_api_key = Configurator.eventful_api_key;
        public string upcoming_api_key = Configurator.upcoming_api_key;
        public string yahoo_api_key = Configurator.yahoo_api_key;
        public string eventbrite_api_key = Configurator.eventbrite_api_key;
        public string facebook_api_key = Configurator.facebook_api_key;
    }

    [Serializable] // because included in the pickled event store
    public class Calinfo
    {

        // todo: use an enumeration instead of string values "what" and "where"
        public string hub_type
        { get { return _hub_type; } }
        private string _hub_type;

        // idle for now, since each hub shares the master delicious account for registry and metadata,
        // but the idea is that each hub might need its own account
        public string delicious_account
        {  get { return _delicious_account; } }
        private string _delicious_account;

        // the twitter account for a hub enables the curator to send authenticated messages to the hub
        // see: http://blog.jonudell.net/2009/10/21/to-elmcity-from-curator-message-start/
        public string twitter_account
        { get { return _twitter_account; } }
        private string _twitter_account;

        // a value like toronto,on 
        public string where
        { get { return _where; } }
        private string _where;

        // a value like MadisonJazz
        public string what
        { get { return _what; } }
        private string _what;

        // tagline for the hub (title of default html rendering)
        public string title
        { get { return _title; } }
        private string _title;

        // radius for a where hub
        public int radius
        { get { return _radius; } }

        private int _radius;

        // only for where hub
        public int population
        { get { return _population; } }
        private int _population;

        // value is yes/no, see: http://blog.jonudell.net/2010/05/07/facebook-is-now-an-elmcity-event-source/
        // same for upcoming, eventbrite, facebook
        public bool eventful
        { get { return _eventful; } }
        private bool _eventful;

        public bool upcoming
        { get { return _upcoming; } }
        private bool _upcoming;

        public bool eventbrite
        { get { return _eventbrite; } }
        private bool _eventbrite;

        public bool facebook
        { get { return _facebook; } }
        private bool _facebook;

        // values documented at: http://blog.jonudell.net/elmcity-project-faq/#tzvalues
        public string tzname
        { get { return _tzname; } }
        private string _tzname = Configurator.default_tz;

        public System.TimeZoneInfo tzinfo
        { get { return _tzinfo; } }
        private System.TimeZoneInfo _tzinfo;

        public string css
        { get { return _css; } }
        private string _css = Configurator.default_css;

        public bool has_img
        { get { return _has_img; } }
        private bool _has_img;

        public Uri img_url
        { get { return _img_url; } }
        private Uri _img_url = Configurator.default_img_url;

        public string contact
        { get { return _contact; } }
        private string _contact = Configurator.default_contact;

        public Uri template_url
        { get { return _template_url; } }
        private Uri _template_url = Configurator.default_template_url;

		public Uri contribute_url
		{ get { return _contribute_url; } }
		private Uri _contribute_url = Configurator.default_contribute_url;

		public string display_width
		{ get { return _display_width; } }
		private string _display_width = Configurator.default_display_width;

		public string feed_count
		{ get { return _feed_count; } }
		private string _feed_count = "0";

        public Dictionary<string, string> metadict
        { get { return _metadict; } }
        private Dictionary<string, string> _metadict;

        public Calinfo(string id)
        {
            this._delicious_account = id;
            var delicious = Delicious.MakeDefaultDelicious();
            this._metadict = delicious.LoadMetadataForIdFromAzureTable(id);

            this._tzinfo = Utils.TzinfoFromName(this.tzname); // start with default

            if (metadict.ContainsKey("where") == false && metadict.ContainsKey("what") == false)
                GenUtils.LogMsg("exception", "new calinfo: neither what nor where", id);

            if (metadict.ContainsKey("where") == true && metadict.ContainsKey("what") == true)
                GenUtils.LogMsg("exception", "new calinfo: both what and where", id);

            if (metadict.ContainsKey("where"))
            {
                this._hub_type = "where";
                this._where = metadict[this.hub_type];
                this._what = Configurator.nothing;

                this._radius = metadict.ContainsKey("radius") ? Convert.ToInt16(metadict["radius"]) : Configurator.default_radius;
                // enforce the default max radius
                if (Convert.ToInt16(this._radius) > Convert.ToInt16(Configurator.max_radius))
                    this._radius = Configurator.max_radius;

                this._population = metadict.ContainsKey("population") ? Convert.ToInt32(metadict["population"]) : Configurator.default_population;
                this._tzname = metadict.ContainsKey("tz") ? metadict["tz"] : this._tzname;
                this._tzinfo = Utils.TzinfoFromName(this._tzname);
                this._title = metadict.ContainsKey("title") ? metadict["title"] : this._where;

                this._eventful = (metadict.ContainsKey("eventful") && metadict["eventful"] == "no") ? false : true;
                this._upcoming = (metadict.ContainsKey("upcoming") && metadict["upcoming"] == "no") ? false : true;
                this._eventbrite = (metadict.ContainsKey("eventbrite") && metadict["eventbrite"] == "no") ? false : true;
                this._facebook = (metadict.ContainsKey("facebook") && metadict["facebook"] == "yes") ? true : false;
            }

            if (metadict.ContainsKey("what"))
            {
                this._hub_type = "what";
                this._what = metadict[this.hub_type];
                this._where = Configurator.nowhere;
                this._tzname = metadict.ContainsKey("tz") ? metadict["tz"] : "GMT";
                this._tzinfo = Utils.TzinfoFromName(this._tzname);
                this._title = metadict.ContainsKey("title") ? metadict["title"] : this._what;
                this._template_url = Configurator.what_template_url;
            }

            this._twitter_account = (metadict.ContainsKey("twitter") ? metadict["twitter"] : null);
            this._css = metadict.ContainsKey("css") ? metadict["css"] : this._css;

            this._has_img = metadict.ContainsKey("header_image") && metadict["header_image"] == "no" ? false : true;
            this._img_url = metadict.ContainsKey("img") ? new Uri(metadict["img"]) : this._img_url;

            this._contact = metadict.ContainsKey("contact") ? metadict["contact"] : this._contact;
            this._template_url = metadict.ContainsKey("template") ? new Uri(metadict["template"]) : this._template_url;

			this._display_width = metadict.ContainsKey("display_width") ? metadict["display_width"] : this._display_width;

			this._feed_count = metadict.ContainsKey("feed_count") ? metadict["feed_count"] : this._display_width;
        }

        // how long to wait between aggregator runs
        public TimeSpan Interval
        {
            get
            {
                if (this.hub_type == "what")
                    return Scheduler.what_interval;
                else
                    return Scheduler.where_interval;
            }
        }

    }

}

