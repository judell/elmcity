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
				UpdateDependentStructures(id);
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
				foreach (var new_metadict in list_metadict_str)
				{
					var feedurl = new_metadict["feedurl"];
					var rowkey = Utils.MakeSafeRowkeyFromUrl(feedurl);
					var query = String.Format("$filter=PartitionKey eq '{0}' and RowKey eq '{1}'", id, rowkey);
					var old_metadict = TableStorage.QueryForSingleEntityAsDictStr(ts, "metadata", query);
					if (ObjectUtils.DictStrContainsDictStr(old_metadict, new_metadict) == false)
						UpmergeChangedMetadict(id, rowkey, new_metadict);
				}
				Utils.UpdateFeedCount(id);
			}
		}

		public static void UpmergeChangedMetadict(string id, string rowkey, Dictionary<string, string> metadict_str)
		{
			Dictionary<string, object> metadict_obj = ObjectUtils.DictStrToDictObj(metadict_str);
			TableStorage.UpmergeDictToTableStore(metadict_obj, table: "metadata", partkey: id, rowkey: rowkey);
		}

		public static void UpdateDependentStructures(string id)
		{
			Utils.PurgePickledCalinfoAndRenderer(id);    // delete [id].calinfo.obj and [id].renderer.obj
			Utils.RecreatePickledCalinfoAndRenderer(id); // recreate them
			Utils.UpdateWrdForId(id);   // update wrd.obj (webrole data)
		}

		public static List<string> LoadHubIdsFromAzureTable()
		{
			var q = string.Format("$filter=type ne ''");
			var ids = new List<string>();
			var r = TableStorage.QueryEntities("metadata", q, ts);
			foreach (var dict in r.list_dict_obj)
				ids.Add( (string) dict["PartitionKey"]);
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


	}
	
}

