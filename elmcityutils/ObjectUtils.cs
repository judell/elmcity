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
using System.Text;
using System.Web;
using System.Web.Extensions;
using Newtonsoft.Json;

namespace ElmcityUtils
{
	public static class ObjectUtils
	{

		public static Dictionary<string, object> ObjToDictObj(Object o)
		{
			Dictionary<string, object> dict_obj = new Dictionary<string, object>();
			var type = o.GetType();
			foreach (var property in type.GetProperties())
				dict_obj[property.Name] = property.GetValue(o, index: null);
			return dict_obj;
		}

		public static Object DictObjToObj(Dictionary<string, object> dict_obj, Type type)
		{
			var o = Activator.CreateInstance(type);  // create object

			if (type.GetProperties() == null)
			{
				GenUtils.LogMsg("exception", "DictObjToObj: " + type.Name, 
					"target type does not define properties");
				return o;
			}

			foreach (var key in dict_obj.Keys)
			{
				try                                  // set properties
				{
					type.GetProperty(key).SetValue(o, dict_obj[key], index: null);
				}
				catch (NullReferenceException)
				{
					// this is normal since an azure table includes PartitionKey, RowKey, 
					// and Timestamp which will not map into the object
				}
				catch (Exception e)
				{
					GenUtils.LogMsg("exception", "DictObjToObj: " + type.Name,
						e.Message + e.StackTrace);
				}
			}

			return o;
		}

		public static Dictionary<string, string> DictObjToDictStr(Dictionary<string, object> dict_obj)
		{
			Dictionary<string, string> dict_str = new Dictionary<string, string>();
			foreach (string key in dict_obj.Keys)
				dict_str.Add(key, dict_obj[key].ToString());
			return dict_str;
		}

		public static Dictionary<string, object> DictStrToDictObj(Dictionary<string, string> dict_str)
		{
			Dictionary<string, object> dict_obj = new Dictionary<string, object>();
			foreach (var key in dict_str.Keys)
				dict_obj.Add(key, (object)dict_str[key]);
			return dict_obj;
		}

		public static string DictStrToKeyValText(Dictionary<string, string> dict_str)
		{
			var text = new StringBuilder();
			foreach (var key in dict_str.Keys)
				text.Append(String.Format("{0}: {1}\n", key, dict_str[key]));
			return text.ToString();
		}

		public static string DictStrToJson(Dictionary<string, string> dict_str)
		{
			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
			return serializer.Serialize(dict_str);
		}

		public static string ListDictStrToJson(List<Dictionary<string,string>> list_dict_str)
		{
			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
			return serializer.Serialize(list_dict_str);
		}

		public static BlobStorageResponse SaveDictAsTextToBlob(Dictionary<string, string> dict_str, BlobStorage bs, string container, string blob_name)
		{
			var settings_text_bytes = Encoding.UTF8.GetBytes(ObjectUtils.DictStrToKeyValText(dict_str));
			return BlobStorage.WriteToAzureBlob(bs, container, blob_name, content_type: null, bytes: settings_text_bytes);
		}

		public static BlobStorageResponse SaveDictAsJsonToBlob(Dictionary<string, string> dict_str, BlobStorage bs, string container, string blob_name)
		{
			var json_bytes = Encoding.UTF8.GetBytes(DictStrToJson(dict_str));
			return BlobStorage.WriteToAzureBlob(bs, container, blob_name, content_type: null, bytes: json_bytes);
		}

		public static BlobStorageResponse SaveListDictAsJsonToBlob(List<Dictionary<string, string>> list_dict_str, BlobStorage bs, string container, string blob_name)
		{
			var json_bytes = Encoding.UTF8.GetBytes(ListDictStrToJson(list_dict_str));
			return BlobStorage.WriteToAzureBlob(bs, container, blob_name, content_type: null, bytes: json_bytes);
		}

		public static Dictionary<string,string> GetDictStrFromJsonUri(Uri uri)
		{
			var json = HttpUtils.FetchUrl(uri).DataAsString();
			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer(); 
			var dict_obj = (Dictionary<string,object>) serializer.DeserializeObject(json);
			return ObjectUtils.DictObjToDictStr(dict_obj);
		}

		public static List<Dictionary<string, string>> GetListDictStrFromJsonUri(Uri uri)
		{
			var json = HttpUtils.FetchUrl(uri).DataAsString();
			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();  
			var obj_array = (object[]) serializer.DeserializeObject(json);  
			var list_dict_str = new List<Dictionary<string, string>>();
			foreach (var obj in obj_array)
				list_dict_str.Add(ObjectUtils.DictObjToDictStr( (Dictionary<string,object>) obj));
			return list_dict_str;
		}

		public static bool DictStrEqualsDictStr(Dictionary<string, string> d1, Dictionary<string, string> d2)
		{
			var d1keys = d1.Keys.ToList(); d1keys.Sort();
			var d2keys = d2.Keys.ToList(); d2keys.Sort();
			var d1vals = d1.Values.ToList(); d1vals.Sort();
			var d2vals = d2.Values.ToList(); d2vals.Sort();
			return (Enumerable.SequenceEqual(d1keys, d2keys) && Enumerable.SequenceEqual(d1vals, d2vals));
		}

		public static bool ListDictStrEqualsDictStr(List<Dictionary<string, string>> l1, List<Dictionary<string, string>> l2)
		{
			if (l1.Count != l2.Count) return false;
			for (var i = 0; i < l1.Count; i++)
			{
				var d1 = l1[i];
				var d2 = l2[i];
				if (DictStrEqualsDictStr(d1, d2) == false)
					return false;
			}
			return true;
		}

		public enum JsonSnapshotType { DictStr, ListDictStr };

		public static void MaybeSaveJsonSnapshot(string id, JsonSnapshotType type, string name, Object new_obj)
		{
			var json_blob_name = id + "." + name + ".json";
			var existing_obj_uri = BlobStorage.MakeAzureBlobUri(id, json_blob_name);
			bool equal = false;
			if (BlobStorage.ExistsBlob(existing_obj_uri))
			{
				if ( type == JsonSnapshotType.DictStr)
				{
					var existing_obj = ObjectUtils.GetDictStrFromJsonUri(existing_obj_uri);
					equal = ObjectUtils.DictStrEqualsDictStr((Dictionary<string, string>)existing_obj, (Dictionary<string, string>)new_obj);
				}
				else // JsonSnapshotType.ListDictStr
				{
					var existing_obj = ObjectUtils.GetListDictStrFromJsonUri(existing_obj_uri);
					equal = ObjectUtils.ListDictStrEqualsDictStr((List<Dictionary<string, string>>)existing_obj, (List<Dictionary<string, string>>)new_obj);
				}
			}
			if (equal == false)
			{
				var bs = BlobStorage.MakeDefaultBlobStorage();
				var timestamped_json_blob_name = string.Format(id + "." + string.Format("{0:yyyy.MM.dd.HH.mm}" + "." + name + ".json", DateTime.UtcNow));
				var timestamped_dict_uri = BlobStorage.MakeAzureBlobUri(id, timestamped_json_blob_name);
				string new_obj_as_json;
				if (type == JsonSnapshotType.DictStr)
					new_obj_as_json = ObjectUtils.DictStrToJson((Dictionary<string, string>)new_obj);
				else // JsonSnapshotType.ListDictStr
					new_obj_as_json = ObjectUtils.ListDictStrToJson((List<Dictionary<string, string>>)new_obj);
				var new_obj_as_json_bytes = Encoding.UTF8.GetBytes(new_obj_as_json);
				BlobStorage.WriteToAzureBlob(bs, id, json_blob_name, null, new_obj_as_json_bytes);
				BlobStorage.WriteToAzureBlob(bs, id, timestamped_json_blob_name, null, new_obj_as_json_bytes);
			}
		}

	}
}
