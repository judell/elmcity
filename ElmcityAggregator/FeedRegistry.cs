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
using System.Collections.Concurrent;
using ElmcityUtils;
using Newtonsoft.Json;

namespace CalendarAggregator
{
	// encapsulates the ical-specific part of what gets reported at, e.g.,
	// http://elmcity.cloudapp.net/services/a2cal/stats
	public class IcalStats
	{
		public string prodid
		{
			get { return _prodid; }
			set { _prodid = value; }
		}
		private string _prodid;

		public string source
		{
			get { return _source; }
			set { _source = value; }
		}
		private string _source;

		public bool valid
		{
			get { return _valid; }
			set { _valid = value; }
		}
		private bool _valid;

		public string score
		{
			get { return _score; }
			set { _score = value; }
		}
		private string _score;

		public int loaded
		{
			get { return _loaded; }
			set { _loaded = value; }
		}
		private int _loaded;

		public string dday_error
		{
			get { return _dday_error; }
			set { _dday_error = value; }
		}
		private string _dday_error;

		public string contenttype
		{
			get { return _contenttype; }
			set { _contenttype = value; }
		}
		private string _contenttype;

		public int singlecount
		{
			get { return _singlecount; }
			set { _singlecount = value; }
		}
		private int _singlecount;

		public int recurringcount
		{
			get { return _recurringcount; }
			set { _recurringcount = value; }
		}
		private int _recurringcount;

		public int recurringinstancecount
		{
			get { return _recurringinstancecount; }
			set { _recurringinstancecount = value; }
		}
		private int _recurringinstancecount;

		public int futurecount
		{
			get { return _futurecount; }
			set { _futurecount = value; }
		}
		private int _futurecount;

		public DateTime whenchecked
		{
			get { return _whenchecked; }
			set { _whenchecked = value; }
		}
		private DateTime _whenchecked;
	}

	// encapsulates an IcalStats and a Dictionary<string,IcalStats> e.g.:
	// key=feedurl: http://www.google.com/calendar/ical/9g7s7kfol4mgvodlscb9aiq4g0@group.calendar.google.com/public/basic.ics
	// value = IcalStats object
	public class FeedRegistry
	{

		private TableStorage ts = TableStorage.MakeDefaultTableStorage();
		private string id;
		public ConcurrentDictionary<string, IcalStats> stats
		{
			get { return _stats; }
		}

		private ConcurrentDictionary<string, IcalStats> _stats = new ConcurrentDictionary<string, IcalStats>();

		public Dictionary<string, string> feeds
		{
			get { return _feeds; }
		}
		private Dictionary<string, string> _feeds = new Dictionary<string, string>();

		private string containername;
		private string statsfile;

		public FeedRegistry(string id)
		{
			this.id = id;
			this.containername = id;
			this.statsfile = "ical_stats";
		}

		public void AddFeed(string feedurl, string source)
		{
			feedurl = Utils.MaybeChangeWebcalToHttp(feedurl);
			if (feeds.ContainsKey(feedurl))
			{
				GenUtils.LogMsg("warning", "FeedRegistry.AddFeed", "duplicate feed: " + feedurl + "(" + source + ")");
				return;
			}
			source = source.Replace("\"", "");
			feeds[feedurl] = source;
			var fs = new IcalStats();
			fs.source = source;
			fs.loaded = 0;
			fs.valid = false;
			fs.score = "0";
			fs.singlecount = 0;
			fs.recurringcount = 0;
			fs.recurringinstancecount = 0;
			fs.dday_error = "";
			fs.prodid = null;
			stats.TryAdd(feedurl, fs);
		}

		// populate feed registry from azure table 
		public void LoadFeedsFromAzure(FeedLoadOption option)
		{
			var dict = Metadata.LoadFeedsFromAzureTableForId(this.id, option);
			var settings = GenUtils.GetSettingsFromAzureTable("settings");
			foreach (var url in dict.Keys)
			{
				try
				{
					if (settings["eventful_feeds_enabled"] == "False" && url.StartsWith("http://eventful.com/"))
						continue;
					if (settings["eventbrite_feeds_enabled"] == "False" && url.Contains("ics_from_eventbrite"))
						continue;
					if (settings["lastfm_feeds_enabled"] == "False" && url.Contains("ics_from_lastfm_venue"))
						continue;
					if (url.StartsWith("http://upcoming.yahoo.com"))
						continue;
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "CollectIcal", e.Message + e.StackTrace);
				}
				this.AddFeed(url, dict[url]);
			}
		}

		// could have used pickled .NET objects here, but wanted to explore json serialization using
		// the newtonsoft library. the urls look like, e.g.:
		// http://elmcity.blob.core.windows.net/a2cal/ical_stats.json
		// http://elmcity.blob.core.windows.net/a2cal/eventbrite_stats.json

		public BlobStorageResponse SerializeIcalStatsToJson()
		{
			return Utils.SerializeObjectToJson(this.stats, containername, this.statsfile + ".json");
		}

		public static Dictionary<string, IcalStats> DeserializeIcalStatsFromJson(string blobhost, string containername, string filename)
		{
			containername = containername.ToLower();
			// var url = new Uri(string.Format("{0}/{1}/{2}", blobhost, containername, filename));
			var url = BlobStorage.MakeAzureBlobUri(containername, filename, false);
			string json = HttpUtils.FetchUrl(url).DataAsString();
			return JsonConvert.DeserializeObject<Dictionary<string, IcalStats>>(json);
		}

		public HttpResponse SaveStatsToAzure()
		{
			var entity = new Dictionary<string, object>();
			entity["PartitionKey"] = entity["RowKey"] = this.id;
			var events_loaded = 0;
			foreach (var feedurl in this.feeds.Keys)
			{
				if (this.stats.ContainsKey(feedurl))
				{
					var ical_stats = this.stats[feedurl];
					events_loaded += ical_stats.loaded;
				}
				else
				{
					GenUtils.LogMsg("warning", "FeedRegistry.SaveStatsToAzure", "stats dict does not contain expected feedurl " + feedurl);
				}
			}
			entity["ical_events"] = events_loaded;
			return this.ts.MergeEntity("metadata", this.id, this.id, entity).http_response;
		}
	}
}
