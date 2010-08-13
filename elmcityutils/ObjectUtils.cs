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

	}
}
