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
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

namespace ElmcityUtils
{
	public static class ObjectUtils
	{
		public static byte[] SerializeObject(object o)
		{
			IFormatter serializer = new BinaryFormatter();
			var ms = new MemoryStream();
			serializer.Serialize(ms, o);
			var buffer = new byte[ms.Length];
			ms.Seek(0, SeekOrigin.Begin);
			ms.Read(buffer, 0, (int)ms.Length);
			return buffer;
		}
		public static T GetTypedObj<T>(string container, string name)
		{
			var uri = BlobStorage.MakeAzureBlobUri(container, name, false);
			return (T)BlobStorage.DeserializeObjectFromUri(uri);
		}

		public static Dictionary<string, object> ObjToDictObj(Object o)
		{
			Dictionary<string, object> dict_obj = new Dictionary<string, object>();
			var type = o.GetType();
			foreach (var property in type.GetProperties())
			{
				var propval = property.GetValue(o, index: null);
				dict_obj[property.Name] = (propval == null) ? null : propval;
			}
			return dict_obj;
		}

		public static Dictionary<string, string> ObjToDictStr(Object o)
		{
			Dictionary<string, string> dict_str = new Dictionary<string, string>();
			var type = o.GetType();
			foreach (var property in type.GetProperties())
			{
				var propval = property.GetValue(o, index: null);
				dict_str[property.Name] = (propval == null) ? "" : propval.ToString();
			}
			foreach (var field in type.GetFields())
			{
				var fieldval = field.GetValue(o);
				dict_str[field.Name] = (fieldval == null) ? "" : fieldval.ToString();
			}
			return dict_str;
		}

		public static Object DictObjToObj(Dictionary<string, object> dict_obj, Type type)
		{
			var o = Activator.CreateInstance(type);  // create object

			if (type.GetProperties() == null)
			{
				GenUtils.PriorityLogMsg("exception", "DictObjToObj: " + type.Name,
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
					GenUtils.PriorityLogMsg("exception", "DictObjToObj: " + type.Name,
						e.Message + e.StackTrace);
				}
			}

			return o;
		}

		public static Dictionary<string, string> DictObjToDictStr(Dictionary<string, object> dict_obj)
		{
			Dictionary<string, string> dict_str = new Dictionary<string, string>();
			foreach (string key in dict_obj.Keys)
			{
				var val = dict_obj[key] == null ? "" : dict_obj[key];
				dict_str.Add(key, val.ToString());
			}
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
			var json = serializer.Serialize(dict_str);
			return GenUtils.PrettifyJson(json);
		}


		public static string ListDictStrToJson(List<Dictionary<string, string>> list_dict_str)
		{
			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
			var json = serializer.Serialize(list_dict_str);
			return GenUtils.PrettifyJson(json);
		}

		public static Dictionary<string, string> GetDictStrFromJsonUri(Uri uri)
		{
			var json = HttpUtils.FetchUrl(uri).DataAsString();
			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
			var dict_obj = (Dictionary<string, object>)serializer.DeserializeObject(json);
			return ObjectUtils.DictObjToDictStr(dict_obj);
		}

		public static List<Dictionary<string, string>> GetListDictStrFromJsonUri(Uri uri)
		{
			var json = HttpUtils.FetchUrl(uri).DataAsString();
			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
			var obj_array = (object[])serializer.DeserializeObject(json);
			var list_dict_str = new List<Dictionary<string, string>>();
			foreach (var obj in obj_array)
				list_dict_str.Add(ObjectUtils.DictObjToDictStr((Dictionary<string, object>)obj));
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

		public static bool DictOfDictStrEqualsDictOfDictStr(Dictionary<string,Dictionary<string, string>> d1, Dictionary<string,Dictionary<string, string>> d2)
		{
			var d1keys = d1.Keys.ToList(); d1keys.Sort();
			var d2keys = d2.Keys.ToList(); d2keys.Sort();
			if ( Enumerable.SequenceEqual(d1keys, d2keys) == false )
				return false;
			foreach ( var key in d1keys )
			{
				var _d1 = d1[key];
				var _d2 = d2[key];
				if ( DictStrEqualsDictStr(_d1, _d2) == false )
					return false;
			}
			return true;
		}

		public static bool DictStrContainsDictStr(Dictionary<string, string> d1, Dictionary<string, string> d2)
		{
			var d1keys = d1.Keys.ToList();
			foreach (var key in d1keys)
			{
				if (d2.ContainsKey(key) == false)  
					d1.Remove(key);
			}
			return DictStrEqualsDictStr(d1, d2);
		}

		public static bool ListDictStrEqualsListDictStr(List<Dictionary<string, string>> l1, List<Dictionary<string, string>> l2)
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

		public static bool SavedJsonSnapshot(string id, JsonSnapshotType type, string name, Object new_obj)
		{
			try
			{
				var json_blob_name = id + "." + name + ".json";
				var existing_obj_uri = BlobStorage.MakeAzureBlobUri(id, json_blob_name, false);
				var exists = BlobStorage.ExistsBlob(existing_obj_uri);
				JsonCompareResult comparison = NewJsonMatchesExistingJson(type, new_obj, existing_obj_uri);
				if (!exists || comparison == JsonCompareResult.different)
				{
					var bs = BlobStorage.MakeDefaultBlobStorage();
					var timestamped_json_blob_name = string.Format(id + "." + string.Format("{0:yyyy.MM.dd.HH.mm}" + "." + name + ".json", DateTime.UtcNow));
					var timestamped_dict_uri = BlobStorage.MakeAzureBlobUri(id, timestamped_json_blob_name, false);
					string new_obj_as_json;
					if (type == JsonSnapshotType.DictStr)
						new_obj_as_json = ObjectUtils.DictStrToJson((Dictionary<string, string>)new_obj);
					else // JsonSnapshotType.ListDictStr
						new_obj_as_json = ObjectUtils.ListDictStrToJson((List<Dictionary<string, string>>)new_obj);
					//bs.PutBlob(id, json_blob_name, new_obj_as_json, "application/json");
					bs.PutBlob(id, timestamped_json_blob_name, new_obj_as_json, "application/json");
				}

				if (comparison == JsonCompareResult.same || comparison == JsonCompareResult.invalid)
					return false; // either the objects matched, or the comparison failed, either way a json snapshot was not saved
				else
					return true;  // the objects did not match, a json snapshot was saved
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "SavedJsonSnapshot", e.Message + e.StackTrace);
				return false;
			}
		}

		public enum JsonCompareResult { same, different, invalid };

		private static JsonCompareResult NewJsonMatchesExistingJson(JsonSnapshotType type, Object new_obj, Uri existing_obj_uri)
		{
			var result = JsonCompareResult.different;

			try
			{
				if (BlobStorage.ExistsBlob(existing_obj_uri))
				{
					if (type == JsonSnapshotType.DictStr)
					{
						var new_dict_str = (Dictionary<string, string>)new_obj;
						if (new_dict_str.Keys.Count == 0)  
							return JsonCompareResult.invalid;
						var existing_dict_str = ObjectUtils.GetDictStrFromJsonUri(existing_obj_uri);
						if (existing_dict_str.Keys.Count == 0) // this shouldn't happen, but...
							return JsonCompareResult.invalid;
						var equal = ObjectUtils.DictStrEqualsDictStr((Dictionary<string, string>)existing_dict_str, new_dict_str);
						result = equal ? JsonCompareResult.same : JsonCompareResult.different;
					}
					else // JsonSnapshotType.ListDictStr
					{
						var new_list_dict_str = (List<Dictionary<string, string>>)new_obj;
						if (AllDictsHaveKeys(new_list_dict_str) == false)
							return JsonCompareResult.invalid;
						var existing_list_dict_str = ObjectUtils.GetListDictStrFromJsonUri(existing_obj_uri);
						if (AllDictsHaveKeys(existing_list_dict_str) == false)
							return JsonCompareResult.invalid;
						var equal = ObjectUtils.ListDictStrEqualsListDictStr((List<Dictionary<string, string>>)existing_list_dict_str, new_list_dict_str);
						result = equal ? JsonCompareResult.same : JsonCompareResult.different;
					}
				}
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "NewJsonMatchesExistingJson", e.Message + e.StackTrace);
			}

			return result;

		}

		public static List<string> FindDuplicateValuesForKey(List<Dictionary<string, string>> list_dict_str, string key)
		{
			var values = list_dict_str.Select(x => x[key]);				// gather all values for key
			var counts = new Dictionary<string, int>();
			foreach (var val in values)
				counts.IncrementOrAdd(val);
			return counts.Keys.ToList().FindAll(x => counts[x] > 1).ToList();  // return list of values occurring more than once
		}

		public static List<Dictionary<string, string>> FindExactDuplicates(List<Dictionary<string, string>> list_dict_str)
		{
			var distinct = new List<Dictionary<string, string>>();
			foreach (var dict in list_dict_str)
			{
				if (!distinct.Exists(x => ObjectUtils.DictStrEqualsDictStr(dict, x)))
					distinct.Add(dict);
			}
			return list_dict_str.Except(distinct).ToList();
		}

		private static bool AllDictsHaveKeys(List<Dictionary<string,string>> list_dict_str)
		{
			var empty_dicts = list_dict_str.FindAll(x => x.Keys.Count == 0);
			return empty_dicts.Count == 0;
		}

		public static Dictionary<string,string> SimpleMergeDictStrDictStr(Dictionary<string, string> d1, Dictionary<string, string> d2)  
		{
			var d1keys = d1.Keys.ToList(); d1keys.Sort();
			var d2keys = d2.Keys.ToList(); d2keys.Sort();

			var keys_equal = Enumerable.SequenceEqual(d1keys, d2keys);

			if ( keys_equal == false )
				throw new Exception("DictStrKeysNotEqual");

			foreach (var key in d1keys)
				if (String.IsNullOrEmpty(d1[key]) == true && String.IsNullOrEmpty(d2[key]) == false)  // update 1's slot if it is empty and 2's is not
					d1[key] = d2[key];

			return d1;
		}

	}
}
