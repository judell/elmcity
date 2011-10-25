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
using System.Xml.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ElmcityUtils;
using Newtonsoft.Json;

namespace CalendarAggregator
{

	public static class Metadata
	{
		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();
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
					GenUtils.LogMsg("info", "UpdateFeedsForId: processing changed snapshot", null);

					var current_feed_urls = list_metadict_str.Select(feed => feed["feedurl"]);

					GenUtils.LogMsg("info", "UpdateFeedsForId: find existing", null);

					// query existing feeds
					var existing_query = string.Format("$filter=PartitionKey eq '{0}' and feedurl ne ''", id);
					var existing_feeds = TableStorage.QueryEntities("metadata", existing_query, ts).list_dict_obj;
					var existing_feed_urls = existing_feeds.Select(feed => feed["feedurl"]);

					GenUtils.LogMsg("info", "UpdateFeedsForId: find new and add", null);

					// add
					var added_feed_urls = current_feed_urls.Except(existing_feed_urls);
					foreach (string added_feed_url in added_feed_urls)
					{
						var rowkey = Utils.MakeSafeRowkeyFromUrl(added_feed_url);
						var entity = (Dictionary<string, string>)list_metadict_str.Find(feed => feed["feedurl"] == added_feed_url);
						TableStorage.UpdateDictToTableStore(ObjectUtils.DictStrToDictObj(entity), "metadata", id, rowkey);
					}

					GenUtils.LogMsg("info", "UpdateFeedsForId: find deleted and delete", null);

					// delete

					var deleted_feed_urls = existing_feed_urls.Except(current_feed_urls);
					foreach (string deleted_feed_url in deleted_feed_urls)
					{
						var rowkey = Utils.MakeSafeRowkeyFromUrl(deleted_feed_url);
						ts.DeleteEntity("metadata", id, rowkey);
					}

					GenUtils.LogMsg("info", "UpdateFeedsForId: find updated and update", null);

					// update
					var maybe_updated_feed_urls = current_feed_urls.Except(deleted_feed_urls);
					foreach (string maybe_updated_feed_url in maybe_updated_feed_urls)
					{
						var rowkey = Utils.MakeSafeRowkeyFromUrl(maybe_updated_feed_url);
						var query = string.Format("$filter=PartitionKey eq '{0}' and RowKey eq '{1}'", id, rowkey);
						var table_record = TableStorage.QueryForSingleEntityAsDictStr(ts, "metadata", query);
						var json_record = (Dictionary<string, string>)list_metadict_str.Find(feed => feed["feedurl"] == maybe_updated_feed_url);
						var updated = false;
						foreach (var key in new List<string>() { "source", "category", "feedurl", "url" })
						{
							try
							{
								if ( table_record.ContainsKey(key) == false )
									GenUtils.PriorityLogMsg("warning", "UpdateFeedsForId: table record missing key: " + key, null);
								if (json_record.ContainsKey(key) == false)
									GenUtils.PriorityLogMsg("warning", "UpdateFeedsForId: json record missing key: " + key, null);
								if (table_record[key] != json_record[key])
									updated = true;
							}
							catch (Exception e)
							{
								GenUtils.PriorityLogMsg("info", "UpdateFeedsForId: missing key: " + key , e.Message + e.StackTrace);
							}
						}
						if (updated)
						{
							UpmergeChangedMetadict(id, rowkey, json_record);
						}
					}

					Utils.RecreatePickledCalinfoAndRenderer(id);
				}
				catch (Exception e)
				{
					GenUtils.LogMsg("info", "UpdateFeedsForId", e.Message + e.StackTrace);
				}

			}
		}

		public static void UpmergeChangedMetadict(string id, string rowkey, Dictionary<string, string> metadict_str)
		{
			Dictionary<string, object> metadict_obj = ObjectUtils.DictStrToDictObj(metadict_str);
			TableStorage.UpmergeDictToTableStore(metadict_obj, table: "metadata", partkey: id, rowkey: rowkey);
		}

		public static List<string> LoadHubIdsFromAzureTable()
		{
			var q = string.Format("$filter=type ne ''");
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
			var r = TableStorage.QueryEntities("metadata", q, ts);
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
			var r = TableStorage.QueryEntities("metadata", q, ts);
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
			string rowkey = Utils.MakeSafeRowkeyFromUrl(feedurl);
			var q = string.Format("$filter=(PartitionKey eq '{0}' and RowKey eq '{1}')", id, rowkey);
			return TableStorage.QueryForSingleEntityAsDictStr(ts, "metadata", q);
		}

		public static Dictionary<string, string> LoadFeedsFromAzureTableForId(string id, FeedLoadOption option)
		{
			var q = string.Format("$filter=(PartitionKey eq '{0}' and feedurl ne '' )", id);
			var qdicts = ts.QueryEntities("metadata", q).list_dict_obj;
			var feed_dict = new Dictionary<string, string>();
			foreach (var dict in qdicts)
			{
				var is_private = dict.ContainsKey("private") && (bool)dict["private"] == true;

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
			string rowkey = Utils.MakeSafeRowkeyFromUrl(feedurl);
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

		public static bool IsPrivateFeed(string id, string feedurl)
		{
			var is_private = true; // safe default
			var q = string.Format("$filter=(PartitionKey eq '{0}' and RowKey eq '{1}' )", id, Utils.MakeSafeRowkeyFromUrl(feedurl));
			var qdicts = ts.QueryEntities(ts_table, q).list_dict_obj;
			if (qdicts.Count != 1)
				GenUtils.PriorityLogMsg("warning", "IsPrivateFeed", "non-singular result for " + q);
			else
			{
				var dict = qdicts.First();
				is_private = dict.ContainsKey("private") && (bool)dict["private"] == true;
			}
			return is_private;
		}

	}

}

