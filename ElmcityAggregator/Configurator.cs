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
using System.Xml.Linq;
using ElmcityUtils;
using System.Text.RegularExpressions;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace CalendarAggregator
{
	public enum HubType { where, what, region };

	public enum NonIcalType { eventful, upcoming, eventbrite, facebook };

	public enum SourceType { eventful, upcoming, eventbrite, facebook, ical };

	public enum FeedLoadOption { only_private, only_public, all };

	// used in default html rendering to carve up the day into chunks
	public enum TimeOfDay { Initialized, AllDay, Morning, Lunch, Afternoon, Evening, Night, WeeHours };

	public static class TimesOfDay
	{
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
		private static DateTime _WEE_HOURS_BEGIN = MakeCompDT(day: DT_COMP_DAY, hour: 0, minute: 1);
	}

	public static class Configurator
	{
		public static Dictionary<string, string> settings = GetSettings("settings");

		public static Dictionary<string, string> usersettings = GetSettings("usersettings");

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

		public static string bing_api_key
		{ get { return _bing_api_key; } }

		private static string _bing_api_key
		{ get { return GetSettingValue("bing_api_key"); } }

		public static string bing_maps_key
		{ get { return _bing_maps_key; } }

		private static string _bing_maps_key
		{ get { return GetSettingValue("bing_maps_key"); } }

		#endregion

		/* Idle for now. Supports http://blog.jonudell.net/2011/06/02/syndicating-facebook-events/
		#region facebook

		public static string test_fb_key
		{ get { return _test_fb_key; } }

		private static string _test_fb_key
		{ get { return GetSettingValue("test_fb_key"); } }

		public static string test_fb_id
		{ get { return _test_fb_id; } }

		private static string _test_fb_id
		{ get { return GetSettingValue("test_fb_id"); } }

		#endregion
	 */

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

		public static int eventful_max_events
		{ get { return _eventful_max_events; } }

		private static int _eventful_max_events
		{ get { return Convert.ToInt32(GetSettingValue("eventful_max_events")); } }

		public static int upcoming_max_events
		{ get { return _upcoming_max_events; } }

		private static int _upcoming_max_events
		{ get { return Convert.ToInt32(GetSettingValue("upcoming_max_events")); } }

		public static int eventbrite_max_events
		{ get { return _eventbrite_max_events; } }

		private static int _eventbrite_max_events
		{ get { return Convert.ToInt32(GetSettingValue("eventbrite_max_events")); } }

		// todo: remove after transition to calinfo.icalendar_horizon_days / usersettings["icalendar_horizon_days"]
		//public static int icalendar_horizon_days
		//{ get { return _icalendar_horizon_days; } }
		//private static int _icalendar_horizon_days
		//{ get { return Convert.ToInt32(GetSettingValue("icalendar_horizon_days")); } }

		public static int webrole_instance_count
		{ get { return _webrole_instance_count; } }

		private static int _webrole_instance_count
		{ get { return Convert.ToInt32(GetSettingValue("webrole_instance_count")); } }

		public static int webrole_cache_purge_interval_minutes
		{ get { return _webrole_cache_purge_interval_minutes; } }

		private static int _webrole_cache_purge_interval_minutes
		{ get { return Convert.ToInt32(GetSettingValue("webrole_cache_purge_interval_minutes")); } }

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

		public static int nonical_aggregate_interval_hours
		{ get { return _nonical_aggregate_interval_hours; } }

		private static int _nonical_aggregate_interval_hours
		{ get { return Convert.ToInt32(GetSettingValue("nonical_aggregate_interval_hours")); } }

		public static int ical_aggregate_interval_hours
		{ get { return _ical_aggregate_interval_hours; } }

		private static int _ical_aggregate_interval_hours
		{ get { return Convert.ToInt32(GetSettingValue("ical_aggregate_interval_hours")); } }

		public static int region_aggregate_interval_hours
		{ get { return _ical_aggregate_interval_hours; } }

		private static int _region_aggregate_interval_hours
		{ get { return Convert.ToInt32(GetSettingValue("region_aggregate_interval_hours")); } }

		public static int worker_general_admin_interval_hours
		{ get { return _worker_general_admin_interval_hours; } }

		private static int _worker_general_admin_interval_hours
		{ get { return Convert.ToInt32(GetSettingValue("worker_general_admin_interval_hours")); } }

		public static int ironpython_admin_interval_hours
		{ get { return _ironpython_admin_interval_hours; } }

		private static int _ironpython_admin_interval_hours
		{ get { return Convert.ToInt32(GetSettingValue("ironpython_admin_interval_hours")); } }

		public static int webrole_reload_interval_minutes
		{ get { return _webrole_reload_interval_minutes; } }

		private static int _webrole_reload_interval_minutes
		{ get { return Convert.ToInt32(GetSettingValue("webrole_reload_interval_minutes")); } }

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

		public static int worker_gather_monitor_data_interval_minutes
		{ get { return _worker_gather_monitor_data_interval_minutes; } }

		private static int _worker_gather_monitor_data_interval_minutes
		{ get { return Convert.ToInt32(GetSettingValue("worker_gather_monitor_data_interval_minutes")); } }

		public static int web_make_tables_and_charts_interval_minutes
		{ get { return _web_make_tables_and_charts_interval_minutes; } }

		private static int _web_make_tables_and_charts_interval_minutes
		{ get { return Convert.ToInt32(GetSettingValue("web_make_tables_and_charts_interval_minutes")); } }

		public static int high_frequency_script_interval_minutes
		{ get { return _high_frequency_script_interval_minutes; } }

		private static int _high_frequency_script_interval_minutes
		{ get { return Convert.ToInt32(GetSettingValue("high_frequency_script_interval_minutes")); } }

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

		public static bool use_eventful
		{ get { return _use_eventful; } }

		private static bool _use_eventful
		{ get { return (bool)(GetSettingValue("use_eventful", reload: true) == "true"); } }

		public static bool use_upcoming
		{ get { return _use_upcoming; } }

		private static bool _use_upcoming
		{ get { return (bool)(GetSettingValue("use_upcoming", reload: true) == "true"); } }

		public static bool use_eventbrite
		{ get { return _use_eventbrite; } }

		private static bool _use_eventbrite
		{ get { return (bool)(GetSettingValue("use_eventbrite", reload: true) == "true"); } }

		public static bool use_facebook
		{ get { return _use_facebook; } }

		private static bool _use_facebook
		{ get { return (bool)(GetSettingValue("use_facebook", reload: true) == "true"); } }



		#endregion

		// non-encapsulated (for now) settings

		public const int azure_log_max_minutes = 500;
		public const int reverse_dns_timeout_seconds = 3;
		public const int rss_default_items = 50;
		public const int odata_since_hours_ago = 24;

		// todo: find some way to make these cache settings dynamic

		public const int services_output_cache_duration_seconds = 60 * 10;
		public const int tag_cloud_cache_duration_seconds = 60 * 5;

		public const string default_html_window_name = "elmcity";

		// these are the keys for key/value pairs parsed from the DESCRIPTION property of any iCalendar event
		// e.g.:
		// url=http://...
		// category=music,bluegrass
		public static List<string> ical_description_metakeys
		{ get { return _ical_description_metakeys; } }

		private static List<string> _ical_description_metakeys = new List<string>() { "url", "category" };

		// used commonly by various tests, so collected here
		public const string testid = "zzztest";
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

		public const string remote_ical_validator = "http://icalvalid.cloudapp.net/?uri=";

		// used by the (nascent) recreation of the fusecal html->ical service.
		// example: myspace
		// the curator bookmarks: http://www.myspace.com/lonesomelake, and tags filter=keene
		// internally it becomes: http://elmcity.cloudapp.net/services=fusecal?url=http://www.myspace.com/lonesomelake&filter=keene&tz_source=eastern&tz_dest=eastern
		public const string fusecal_service = "http://" + ElmcityUtils.Configurator.appdomain + "/services/fusecal?url={0}&filter={1}&tz_source={2}&tz_dest={3}";

		public const string ics_from_xcal_service = "http://" + ElmcityUtils.Configurator.appdomain + "/ics_from_xcal?url={0}&tzname={1}&source={2}";

		public const string ics_from_vcal_service = "http://" + ElmcityUtils.Configurator.appdomain + "/ics_from_vcal?url={0}&tzname={1}&source={2}";

		// the "fusecal" service is written in python, this is the dispatcher that runs the appropriate 
		// parser for, e.g., myspace or librarything
		public static string fusecal_dispatcher = ElmcityUtils.Configurator.azure_blobhost + "/admin/fusecal.py";

		// things to do each time the worker's Run method loops
		public static string iron_python_run_script_url = ElmcityUtils.Configurator.azure_blobhost + "/admin/_run.py";

		// used to report populations (and event densities) for where hubs
		public const string census_city_population_estimates = "http://www.census.gov/popest/data/cities/totals/2009/files/SUB-EST2009_ALL.csv";

		public const int max_radius = 15;         // limit for where hubs
		public const int default_radius = 5;     // default for where hubs
		public const int default_population = 0;  // in case pop lookup fails

		public const int max_tag_chars = 22;

		public const string default_tz = "Eastern";

		public const string nowhere = "nowhere";
		public const string nothing = "nothing";

		// routine admin tasks, run from worker on a scheduled basis, are in this python script
		public static string iron_python_admin_script_url = ElmcityUtils.Configurator.azure_blobhost + "/admin/_admin.py";

		// for worker role to gather monitor data, write charts and reports
		public static string monitor_script_url = ElmcityUtils.Configurator.azure_blobhost + "/admin/monitor.py";

		// for web role to gather log data, write charts and reports
		public static string charts_and_tables_script_url = ElmcityUtils.Configurator.azure_blobhost + "/admin/charts.py";

		// for web and worker roles to update dashboard
		public static string dashboard_script_url = ElmcityUtils.Configurator.azure_blobhost + "/admin/dashboard.py";

		// part of experimental pshb implementation, idle for now
		//public static string pubsubhubub_uri = "http://pubsubhubbub.appspot.com/";

		public static string webrole_sentinel = "webrole_sentinel.txt";
		public static string tags_json = "tags.json";
		
		public static int GetIntSetting(Dictionary<string,string> metadict, Dictionary<string,string> usersettings, string key)
		    {
			string value;
			value = GetMetadictValueOrSettingsValue(metadict, usersettings, key);
			metadict[key] = value;
			return Convert.ToInt32(value);
		    }

		 public static string GetStrSetting(Dictionary<string, string> metadict, Dictionary<string, string> usersettings, string key)
		 {
			 string value = GetMetadictValueOrSettingsValue(metadict, usersettings, key);
			 metadict[key] = value;
			 return value;
		 }

		 public static bool GetBoolSetting(Dictionary<string, string> metadict, Dictionary<string, string> usersettings, string key)
		 {
			 string value = GetMetadictValueOrSettingsValue(metadict, usersettings, key);
			 metadict[key] = value;
			 return value == "yes";
		 }

		 public static Uri GetUriSetting(Dictionary<string, string> metadict, Dictionary<string, string> usersettings, string key)
		 {
			 string value = GetMetadictValueOrSettingsValue(metadict, usersettings, key);
			 string final;
			  if (!value.StartsWith("http://"))
				 final = BlobStorage.MakeAzureBlobUri("admin", value, false).ToString();
			 else
				 final = value;
			 metadict[key] = final;
			 return new Uri(final);
		 }

		 public static string GetTitleSetting(Dictionary<string, string> metadict, Dictionary<string, string> usersettings, string key)
		 {
			 string value = GetStrSetting(metadict, usersettings, key);
			 string final;
			 if (value == null)
				 final = metadict["PartitionKey"];
			 else
				 final = value;
			 metadict[key] = final;
			 return final;
		 }

		 public static int GetPopSetting(string id, Dictionary<string, string> metadict, Dictionary<string, string> usersettings, string key)
		 {
			 int pop = Configurator.default_population;
			 if ( metadict.ContainsKey("population") )
			 {
				 var str_pop = metadict["population"];
				 if (str_pop != "0" && str_pop != "")
					 pop = Convert.ToInt32(str_pop);
			 }
		 return pop;
		 }

		 public static string  GetMetadictValueOrSettingsValue(Dictionary<string, string> metadict, Dictionary<string, string> usersettings, string key)
		 {
			 if ( GenUtils.KeyExistsAndHasValue(metadict, key) )
				 return metadict[key];

			 if ( ! GenUtils.KeyExistsAndHasValue(metadict,key) && usersettings.ContainsKey(key))
			 {
				 return usersettings[key];
			 }

			 if ( ! GenUtils.KeyExistsAndHasValue(metadict,key) && ! usersettings.ContainsKey(key))
			 {
				 return null;
			 }

			 return null;
		 }

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
					GenUtils.PriorityLogMsg("exception", "GetSettingValue", e.Message + e.StackTrace);
				}
			}

			if (setting_value == null)
				GenUtils.LogMsg("info", "GetSettingValue: " + setting_name, " is null");

			return setting_value;
		}

		public static string GetSettingValue(string setting_name)
		{
			return GetSettingValue(setting_name, reload: false);
		}

		private static Dictionary<string,string> GetSettings(string table)
		{
			return GenUtils.GetSettingsFromAzureTable(table);
		}
	}

	public class Apikeys
	{
		public string eventful_api_key = Configurator.eventful_api_key;
		public string upcoming_api_key = Configurator.upcoming_api_key;
		public string yahoo_api_key = Configurator.yahoo_api_key;
		public string eventbrite_api_key = Configurator.eventbrite_api_key;
		public string facebook_api_key = Configurator.facebook_api_key;
		public string bing_maps_key = Configurator.bing_maps_key;
	}

	[Serializable]
	public class TaggableSource
	{
		public string name { get; set; }
		public string elmcity_id { get; set; }
		public string home_url { get; set; }
		public string ical_url { get; set; }
		public string city { get; set; }
		public bool has_future_events { get; set; }
		public string extra_url { get; set; }

		public TaggableSource(string name, string elmcity_id, string home_url, string ical_url)
		{
			this.name = name;
			this.elmcity_id = elmcity_id;
			this.home_url = home_url;
			this.ical_url = ical_url;
		}

		public TaggableSource(string name, string elmcity_id, string home_url, string ical_url, string city)
		{
			this.name = name;
			this.elmcity_id = elmcity_id;
			this.home_url = home_url;
			this.ical_url = ical_url;
			this.city = city;
		}

		public TaggableSource(string name, string elmcity_id, string home_url, string ical_url, bool has_future_events, string extra_url) 
		{
			this.name = name;
			this.elmcity_id = elmcity_id;
			this.home_url = home_url;
			this.ical_url = ical_url;
			this.has_future_events = has_future_events;
			this.extra_url = extra_url;
		}

		public TaggableSource()  // need paramaterless constructor in order to use Activator.CreateInstance
		{
		}

		public override int GetHashCode()
		{
			return (this.name + this.ical_url).GetHashCode();
		}


		public override bool Equals(object other)
		{
			var taggable = (TaggableSource)other;
			if (taggable == null)
				return false;
			return this.name == taggable.name && this.ical_url == taggable.ical_url;
		}

	}

	[Serializable] 
	public class Calinfo
	{
		public HubType hub_enum
		{ get { return _hub_enum; } }
		private HubType _hub_enum;

		public string id
		{ get { return _id; } }
		private string _id;

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
		public string lat
		{ get { return _lat; } }
		private string _lat;

		public string lon
		{ get { return _lon; } }
		private string _lon;

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

		// option to disable RDFa, value is yes/no, default is yes
		public bool use_rdfa
		{ get { return _use_rdfa; } }
		private bool _use_rdfa;

		// values enumerated in admin/editable_metadata.html
		public string tzname
		{ get { return _tzname; } }
		private string _tzname;

		public int icalendar_horizon_days
		{ get { return _icalendar_horizon_days; } }
		private int _icalendar_horizon_days;

		public bool use_x_wr_timezone
		{ get { return _use_x_wr_timezone; } }
		private bool _use_x_wr_timezone;

		public System.TimeZoneInfo tzinfo
		{ get { return _tzinfo; } }
		private System.TimeZoneInfo _tzinfo;

		public string css
		{ get { return _css; } }
		private string _css;

		public bool has_img
		{ get { return _has_img; } }
		private bool _has_img;

		public Uri img_url
		{ get { return _img_url; } }
		private Uri _img_url;

		public string default_img_html
		{ get { return _default_image_html; } }
		private string _default_image_html;

		public string contact
		{ get { return _contact; } }
		private string _contact;

		public Uri template_url
		{ get { return _template_url; } }
		private Uri _template_url;

		public string display_width
		{ get { return _display_width; } }
		private string _display_width;

		public string feed_count
		{ 
			get { return _feed_count; }
			set { _feed_count = value; } // settable so worker can update on the fly
		}
		private string _feed_count;

		public bool has_descriptions
		{ get { return _has_descriptions; } }
		private bool _has_descriptions;

		public bool has_locations
		{ get { return _has_locations; } }
		private bool _has_locations;

		public bool show_eventful_badge = false;
		public bool show_eventbrite_badge = false;
		public bool show_meetup_badge = false;
		public bool show_facebook_badge = false;

		public DateTime timestamp;

		/*
		public Dictionary<string, string> metadict
		{ get { return _metadict; } }
		private Dictionary<string, string> _metadict;
		 */

		public Calinfo(string id)
		{
			this.timestamp = DateTime.UtcNow;

			var metadict = Metadata.LoadMetadataForIdFromAzureTable(id);
			try
			{

				this._id = id;

				if (metadict.ContainsKey("type") == false)
				{
					GenUtils.PriorityLogMsg("exception", "new calinfo: no hub type for id (" + id + ")", null);
					return;
				}

				this._contact = Configurator.GetStrSetting(metadict, Configurator.usersettings, "contact");

				this._css = Configurator.GetUriSetting(metadict, Configurator.usersettings, "css").ToString();

				this._default_image_html = Configurator.GetStrSetting(metadict, Configurator.usersettings, "default_img_html");

				//this._has_descriptions = Configurator.GetBoolSetting(metadict, Configurator.usersettings, "descriptions");
				this._has_descriptions = true;

				//this._has_locations = Configurator.GetBoolSetting(metadict, Configurator.usersettings, "locations");
				this._has_locations = true;

				this._display_width = Configurator.GetStrSetting(metadict, Configurator.usersettings, "display_width"); // todo: obsolete this

				this._feed_count = Configurator.GetMetadictValueOrSettingsValue(metadict, Configurator.usersettings, "feed_count");

				this._has_img = Configurator.GetBoolSetting(metadict, Configurator.usersettings, "header_image");

				this._icalendar_horizon_days = Configurator.GetIntSetting(metadict, Configurator.usersettings, "icalendar_horizon_days");

				this._img_url = Configurator.GetUriSetting(metadict, Configurator.usersettings, "img");

				this._title = Configurator.GetTitleSetting(metadict, Configurator.usersettings, "title");

				this._template_url = Configurator.GetUriSetting(metadict, Configurator.usersettings, "template");
				//this._template_url = new Uri(Configurator.settings["template"]);

				this._twitter_account = Configurator.GetStrSetting(metadict, Configurator.usersettings, "twitter");

				this._tzname = Configurator.GetStrSetting(metadict, Configurator.usersettings, "tz");
				this._tzinfo = Utils.TzinfoFromName(this._tzname);

				this._use_rdfa = Configurator.GetBoolSetting(metadict, Configurator.usersettings, "use_rdfa");

				this._use_x_wr_timezone = Configurator.GetBoolSetting(metadict, Configurator.usersettings, "use_x_wr_timezone");

				//if (metadict.ContainsKey("where"))
				if (metadict["type"] == "where")
				{
					this._hub_enum = HubType.where;
					this._where = metadict[this.hub_enum.ToString()];
					this._what = Configurator.nothing;

					//this._radius = metadict.ContainsKey("radius") ? Convert.ToInt16(metadict["radius"]) : Configurator.default_radius;
					this._radius = Configurator.GetIntSetting(metadict, Configurator.usersettings, "radius");

					// enforce the default max radius
					if (this._radius > Configurator.max_radius)
						this._radius = Configurator.max_radius;

					this._population = Configurator.GetPopSetting(this.id, metadict, Configurator.usersettings, "population");
					this._eventful = Configurator.GetBoolSetting(metadict, Configurator.usersettings, "eventful");
					this._upcoming = Configurator.GetBoolSetting(metadict, Configurator.usersettings, "upcoming");
					this._eventbrite = Configurator.GetBoolSetting(metadict, Configurator.usersettings, "eventbrite");
					this._facebook = Configurator.GetBoolSetting(metadict, Configurator.usersettings, "facebook");

					// curator gets to override the lat/lon that will otherwise be looked up based on the location

					// if (!metadict.ContainsKey("lat") && !metadict.ContainsKey("lon"))
					if ( GenUtils.KeyExistsAndHasValue(metadict, "lat") && GenUtils.KeyExistsAndHasValue(metadict, "lon") )
					{
						this._lat = metadict["lat"];
						this._lon = metadict["lon"];
					}
					else
					{
						var apikeys = new Apikeys();
						var lookup_lat = Utils.LookupLatLon(apikeys.bing_maps_key, this.where)[0];
						var lookup_lon = Utils.LookupLatLon(apikeys.bing_maps_key, this.where)[1];

						if (!String.IsNullOrEmpty(lookup_lat) && !String.IsNullOrEmpty(lookup_lon))
						{
							this._lat = metadict["lat"] = lookup_lat;
							this._lon = metadict["lon"] = lookup_lon;
							Utils.UpdateLatLonToAzureForId(id, lookup_lat, lookup_lon);
						}
					}

					if (String.IsNullOrEmpty(this.lat) && String.IsNullOrEmpty(this.lon))
					{
						GenUtils.PriorityLogMsg("warning", "Configurator: no lat and/or lon for " + id, null);
					}

					this.SetShowBadgesForHub();

				}

				// if (metadict.ContainsKey("what"))
				if (metadict["type"] == "what")
				{
					this._hub_enum = HubType.what;
					this._what = metadict[this.hub_enum.ToString()];
					this._where = Configurator.nowhere;
					this.SetShowBadgesForHub();
				}

				if (metadict["type"] == "region")
				{
					this._hub_enum = HubType.region;
					this._what = Configurator.nothing;
					this._where = Configurator.nowhere;
					this.SetShowBadgesForRegion();
				}
			}

			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "new Calinfo: " + id, e.Message + e.StackTrace);
			}

		}

		private void SetShowBadgesForHub()
		{
			try
			{
				this.show_eventbrite_badge = Utils.ShowEventBriteBadge(this);
				this.show_eventful_badge = Utils.ShowEventfulBadge(this);
				this.show_meetup_badge = Utils.ShowMeetupBadge(this);
				this.show_facebook_badge = Utils.ShowFacebookBadge(this);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "SetShowBadgesForHub: " + this.id, e.Message + e.StackTrace);
			}
		}

		private void SetShowBadgesForRegion()
		{
			try
			{
				var ids = Utils.GetIdsForRegion(this.id);

				foreach (var _id in ids)
				{
					var calinfo = Utils.AcquireCalinfo(_id);

					if (calinfo.show_eventbrite_badge)
						this.show_eventbrite_badge = true;

					if (calinfo.show_eventful_badge)
						this.show_eventful_badge = true;

					if (calinfo.show_meetup_badge)
						this.show_meetup_badge = true;

					if (calinfo.show_facebook_badge)
						this.show_facebook_badge = true;

					if (this.show_eventbrite_badge && this.show_eventful_badge && this.show_meetup_badge && this.show_facebook_badge)
						break;
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "SetShowBadgesForRegion: " + this.id, e.Message + e.StackTrace);
			}

		}

		public Calinfo(TimeZoneInfo tzinfo)
		{
			this._tzinfo = tzinfo;
		}

		public string City
		{
			get
			{
				if (this.hub_enum == HubType.where)
					return Regex.Replace(this.where, ",.+", "").ToLower();
				else
					return null;
			}
		}

	}

}

