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
using System.Collections.Concurrent;
using System.Linq;
using System.Xml.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ElmcityUtils;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CalendarAggregator
{

	public static class Metadata
	{
		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();
		private static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
		public static string ts_table = "metadata";

		public static void UpdateMetadataForId(string id, string json)
		{
			Dictionary<string, string> metadict_str;
			try
			{
				metadict_str = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "UpdateMetadataForId: " + json, e.Message + e.StackTrace);
				throw (e);
			}
			var snapshot_changed = ObjectUtils.SavedJsonSnapshot(id, ObjectUtils.JsonSnapshotType.DictStr, "metadata", metadict_str);
			if (snapshot_changed == true)
			{
				var json_blob_name = id + ".metadata.json";                                 // update json blob
				bs.PutBlob(id, json_blob_name, json, "application/json");
				bs.DeleteBlob(id, "metadata.html");
				UpmergeChangedMetadict(id, id, metadict_str);
				Utils.RecreatePickledCalinfoAndRenderer(id);
			}
		}

		public static void UpdateFeedsForId(string id, string json)
		{
			TableStorage ts = TableStorage.MakeDefaultTableStorage();
			List<Dictionary<string, string>> list_metadict_str;
			try
			{
				list_metadict_str = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);
				var dupes = FindDuplicateFeeds(list_metadict_str);
				if (dupes.Count > 0)
					GenUtils.PriorityLogMsg("warning", string.Format("{0} duplicate feeds for {1}", dupes.Count, id), null);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "UpdateFeedsForId: " + json, e.Message + e.StackTrace);
				throw (e);
			}

			var snapshot_changed = ObjectUtils.SavedJsonSnapshot(id, ObjectUtils.JsonSnapshotType.ListDictStr, "feeds", list_metadict_str);

			if (snapshot_changed == true)
			{
				try
				{
					var json_blob_name = id + ".feeds.json";                                 // update json blob
					var updated_json = ObjectUtils.ListDictStrToJson(list_metadict_str);
					bs.PutBlob(id, json_blob_name, updated_json, "application/json");

					var fes = "feedentryschema";
					var fesquery = string.Format("$filter=PartitionKey eq '{0}' and RowKey eq '{1}'", fes, fes);
					var feed_entry_schema = TableStorage.QueryForSingleEntityAsDictStr(ts, fes, fesquery)["fields"];

					GenUtils.LogMsg("info", "UpdateFeedsForId: processing changed snapshot", null);

					var current_feed_urls = list_metadict_str.Select(feed => feed["feedurl"]).ToList();

					GenUtils.LogMsg("info", "UpdateFeedsForId: find existing", null);

					// query existing feeds
					var existing_query = string.Format("$filter=PartitionKey eq '{0}' and feedurl ne ''", id);
					var existing_feeds = ts.QueryAllEntitiesAsListDict("metadata", existing_query).list_dict_obj;
					List<object> _existing_feed_urls = (List<object>) existing_feeds.Select(feed => feed["feedurl"]).ToList();
					List<string> existing_feed_urls = (List<string>) _existing_feed_urls.Select(x => x.ToString()).ToList();

					GenUtils.LogMsg("info", "UpdateFeedsForId: find new and add", null);

					HandleFeedAdds(id, list_metadict_str, current_feed_urls, existing_feed_urls);

					var deleted_feed_urls = HandleFeedDeletes(id, ts, current_feed_urls, existing_feed_urls);

					HandleFeedUpdates(id, ts, list_metadict_str, feed_entry_schema, current_feed_urls, deleted_feed_urls);

					Utils.RecreatePickledCalinfoAndRenderer(id);
					Scheduler.InitTaskForId(id, TaskType.icaltasks);
					bs.DeleteBlob(id, "metadata.html");        // dump the static page
					var metadata_url = string.Format("http://{0}/{1}/metadata", ElmcityUtils.Configurator.appdomain, id);
					var args = new Dictionary<string, string>() { { "metadata_url", metadata_url } };
					ThreadPool.QueueUserWorkItem(new WaitCallback(rebuild_metahistory_handler), args); // do this on another thread
				}
				catch (Exception e)
				{
					GenUtils.LogMsg("info", "UpdateFeedsForId", e.Message + e.StackTrace);
				}

			}
		}

		private static void rebuild_metahistory_handler(Object args)
		{
			var dict = (Dictionary<string, string>)args;
			try
			{
				var metadata_url = dict["metadata_url"];
				HttpUtils.FetchUrl(new Uri(metadata_url)); // force a rebuild of that page
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "rebuild_metahistory_handler", e.Message + e.StackTrace);
			}
		}


		private static bool HandleFeedUpdates(string id, TableStorage ts, List<Dictionary<string, string>> list_metadict_str, string feed_entry_schema, List<string> current_feed_urls, List<string> deleted_feed_urls)
		{
			List<string> maybe_updated_feed_urls = (List<string>)current_feed_urls.Except(deleted_feed_urls).ToList();
			var update_lock = new Object();
			var updated_feeds = false;

			Parallel.ForEach(source: maybe_updated_feed_urls, body: (maybe_updated_feed_url) =>
			{
			var rowkey = TableStorage.MakeSafeRowkeyFromUrl(maybe_updated_feed_url);
			var query = string.Format("$filter=PartitionKey eq '{0}' and RowKey eq '{1}'", id, rowkey);
			var table_record = TableStorage.QueryForSingleEntityAsDictStr(ts, "metadata", query);
			var json_record = (Dictionary<string, string>)list_metadict_str.Find(feed => feed["feedurl"] == maybe_updated_feed_url);
			bool updated_feed = false;
			//foreach (var key in new List<string>() { "source", "category", "feedurl", "url", "catmap", "approved", "comments" })
			foreach (var key in feed_entry_schema.Split(','))
			{
				try
				{
					// if (table_record.ContainsKey(key) == false)
					//	GenUtils.LogMsg("info", id, "UpdateFeedsForId: table record missing key: " + key + " (will be added)");

					if (json_record.ContainsKey(key) == false)
					{
						// GenUtils.LogMsg("info", id, "UpdateFeedsForId: json record missing key: " + key + " (adding it now)");
						json_record[key] = "";
					}

					if (table_record.ContainsKey(key) == false || table_record[key] != json_record[key])
						lock (update_lock)
						{
							updated_feeds = updated_feed = true;
						}
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("info", "UpdateFeedsForId: missing key: " + key, e.Message + e.StackTrace);
				}
			}
			if (updated_feed)
			{
				UpmergeChangedMetadict(id, rowkey, json_record);
			}

			});

//			foreach (string maybe_updated_feed_url in maybe_updated_feed_urls)
//			{
//			}

				return updated_feeds;
		}

		private static List<string> HandleFeedDeletes(string id, TableStorage ts, List<string> current_feed_urls, List<string> existing_feed_urls)
		{
			var deleted_feed_urls = existing_feed_urls.Except(current_feed_urls);
			foreach (string deleted_feed_url in deleted_feed_urls)
			{
				var rowkey = TableStorage.MakeSafeRowkeyFromUrl(deleted_feed_url);
				ts.DeleteEntity("metadata", id, rowkey);
			}

			GenUtils.LogMsg("info", "UpdateFeedsForId: find updated and update", null);
			return deleted_feed_urls.ToList();
		}

		private static void HandleFeedAdds(string id, List<Dictionary<string, string>> list_metadict_str, List<string> current_feed_urls, List<string> existing_feed_urls)
		{
			var added_feed_urls = current_feed_urls.Except(existing_feed_urls);
			foreach (string added_feed_url in added_feed_urls)
			{
				var rowkey = TableStorage.MakeSafeRowkeyFromUrl(added_feed_url);
				var entity = (Dictionary<string, string>)list_metadict_str.Find(feed => feed["feedurl"] == added_feed_url);
				TableStorage.UpdateDictToTableStore(ObjectUtils.DictStrToDictObj(entity), "metadata", id, rowkey);
			}

			GenUtils.LogMsg("info", "UpdateFeedsForId: find deleted and delete", null);
		}

		public static void UpmergeChangedMetadict(string id, string rowkey, Dictionary<string, string> metadict_str)
		{
			Dictionary<string, object> metadict_obj = ObjectUtils.DictStrToDictObj(metadict_str);
			TableStorage.UpmergeDictToTableStore(metadict_obj, table: "metadata", partkey: id, rowkey: rowkey);
		}

		public static List<string> FindDuplicateFeeds(List<Dictionary<string,string>> list_dict_str)
		{
			var feedurls = new Dictionary<string, int>();
			foreach (var dict in list_dict_str)
				feedurls.IncrementOrAdd(dict["feedurl"]);
			var dupes = feedurls.ToList().FindAll(x => Convert.ToInt16(x.Value) > 1);
			return dupes.Select(x => x.Key).ToList();
		}

		public static List<string> LoadHubIdsFromAzureTable()
		{
			var q = string.Format("$filter=type eq 'what' or type eq 'where' or type eq 'region'");
			var ids = QueryIds(q);
			return ids;
		}

		public static List<string> LoadHubIdsFromAzureTableByType(HubType hub_type)
		{
			var type = hub_type.ToString();
			var q = string.Format("$filter=type eq '{0}'",type);
			var ids = QueryIds(q);
			return ids;
		}

		public static Dictionary<string,string> QueryIdsAndLocations()
		{
			var q = string.Format("$filter=type eq 'where'");
			var d = new Dictionary<string, string>();

			var ts = TableStorage.MakeDefaultTableStorage();
			var r = ts.QueryAllEntitiesAsListDict("metadata", q);
			try
			{
				foreach (var dict in r.list_dict_obj)
				{
					var id = (string)dict["PartitionKey"];
					d[id] = (string)dict["where"];
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "QueryIdsAndLocations", e.Message + e.StackTrace);
			}
			
			return d;

		}

		private static List<string> QueryIds(string q)
		{
			var ids = new List<string>();
			var r = ts.QueryAllEntitiesAsListDict("metadata", q);
			foreach (var dict in r.list_dict_obj)
				ids.Add((string)dict["PartitionKey"]);
			return ids;
		}

		public static Dictionary<string, string> LoadMetadataForIdFromAzureTable(string id)
		{
			var q = string.Format("$filter=(PartitionKey eq '{0}' and RowKey eq '{1}')", id, id);
			var dict = TableStorage.QueryForSingleEntityAsDictStr(ts, "metadata", q);
			return dict;
		}

		public static Dictionary<string, string> LoadFeedMetadataFromAzureTableForFeedurlAndId(string feedurl, string id)
		{
			string rowkey = TableStorage.MakeSafeRowkeyFromUrl(feedurl);
			var q = string.Format("$filter=(PartitionKey eq '{0}' and RowKey eq '{1}')", id, rowkey);
			return TableStorage.QueryForSingleEntityAsDictStr(ts, "metadata", q);
		}

		public static Dictionary<string, string> LoadFeedsFromAzureTableForId(string id, FeedLoadOption option)
		{
			var q = string.Format("$filter=(PartitionKey eq '{0}' and feedurl ne '' )", id);
			var qdicts = ts.QueryAllEntitiesAsListDict("metadata", q).list_dict_obj;
			var feed_dict = new Dictionary<string, string>();
			foreach (var dict in qdicts)
			{
				var is_private = dict.ContainsKey("private") && (bool)dict["private"] == true;

				var unapproved = dict.ContainsKey("approved") && (string)dict["approved"] == "no";

				if (unapproved)
					continue;

				switch (option)
				{
					case FeedLoadOption.all:
						AddSourceAndFeedUrlToDict(feed_dict, dict);
						break;
					case FeedLoadOption.only_public:
						if (is_private == false)
							AddSourceAndFeedUrlToDict(feed_dict, dict);
						break;
					case FeedLoadOption.only_private:
						if (is_private == true)
							AddSourceAndFeedUrlToDict(feed_dict, dict);
						break;
				}

			}
			return feed_dict;
		}

		public static HttpResponse StoreFeedAndMetadataToAzure(string id, string feedurl, Dictionary<string, object> metadict)
		{
			string rowkey = TableStorage.MakeSafeRowkeyFromUrl(feedurl);
			var r = TableStorage.UpdateDictToTableStore(metadict, ts_table, id, rowkey);
			if (r.http_response.status != System.Net.HttpStatusCode.Created)
				GenUtils.PriorityLogMsg("warning", "StoreFeedAndMetadataToAzure", r.http_response.status.ToString());
			return r.http_response;
		}

		private static void AddSourceAndFeedUrlToDict(Dictionary<string, string> feed_dict, Dictionary<string, object> dict)
		{
			var source = (string)dict["source"];
			var feedurl = (string)dict["feedurl"];
			feed_dict[feedurl] = source;
		}

		public static void AlterHubMetadata(string id, Dictionary<string,string> dict)
		{
			try
			{
			var metadict = Metadata.LoadMetadataForIdFromAzureTable(id);
			foreach ( var key in dict.Keys )
				metadict[key] = dict[key];
			var json = Newtonsoft.Json.JsonConvert.SerializeObject(metadict);
			Metadata.UpdateMetadataForId(id, json);
			}
			catch (Exception e)
			{
			GenUtils.PriorityLogMsg("exception", "AlterHubMetadata: " + String.Join(",", dict.Keys.ToList()), e.Message);
			}

		}

		public static void AlterAllHubMetadata(Dictionary<string,string> dict)
		{
			var ids = Metadata.LoadHubIdsFromAzureTable();
			Parallel.ForEach(source: ids, body: (id) =>
			{
				AlterHubMetadata(id, dict);
			});
		}

		public static void TryLoadCatmapFromMetadict(ConcurrentDictionary<string, Dictionary<string, string>> per_feed_catmaps, Dictionary<string, string> metadict)
		{
			if (metadict.ContainsKey("catmap") && !String.IsNullOrEmpty(metadict["catmap"])) // found a catmap
			{
				try
				{
					var feedurl = metadict["feedurl"];
					var catmap_value = metadict["catmap"].ToLower();
					string json = "";
					if (catmap_value.StartsWith("http:"))                            // try indirect
					{
						var uri = new Uri(metadict["catmap"]);
						json = HttpUtils.FetchUrl(uri).DataAsString();
					}
					else
					{
						json = catmap_value;                                          // try direct
					}
					per_feed_catmaps[feedurl] = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "TryLoadCatmap: " + JsonConvert.SerializeObject(metadict), e.Message);
				}
			}


		}

	}

}

