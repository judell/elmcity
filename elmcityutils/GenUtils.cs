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
using System.Data.EntityClient;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Web;
using System.Web.Mvc;

namespace ElmcityUtils
{
	public class GenUtils
	{
		private static TableStorage default_ts = TableStorage.MakeDefaultTableStorage();

		static private XNamespace odata_metadata_namespace = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
		static private XNamespace data_namespace = "http://schemas.microsoft.com/ado/2007/08/dataservices";

		public static void LogMsg(string type, string title, string blurb)
		{
			LogMsg(type, title, blurb, default_ts);
		}

		public static void LogMsg(string type, string title, string blurb, TableStorage ts)
		{
			title = MakeLogMsgTitle(title);
			ts.WriteLogMessage(type, title, blurb);
		}

		public static void PriorityLogMsg(string type, string title, string blurb)
		{
			PriorityLogMsg(type, title, blurb, default_ts);
		}

		public static void PriorityLogMsg(string type, string title, string blurb, TableStorage ts)
		{
			title = MakeLogMsgTitle(title);
			ts.WritePriorityLogMessage(type, title, blurb);
		}

		public static string MakeLogMsgTitle(string title)
		{
			string hostname = Dns.GetHostName();
			var procname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
			title = string.Format("{0} {1} {2}", hostname, procname, title);
			return title;
		}

		public static bool IsNullable<T>(T obj) // http://stackoverflow.com/questions/374651/how-to-check-if-an-object-is-nullable
			{        
			if (obj == null) return true; // obvious        
			Type type = typeof(T);        
			if (!type.IsValueType) return true; // ref-type 
			if (Nullable.GetUnderlyingType(type) != null) return true; // Nullable<T>  
			return false; // value-type    
			}

		public static bool KeyExistsAndHasValue(Dictionary<string, string> metadict, string key)
		 {
			 return metadict.ContainsKey(key) && metadict[key] != String.Empty;
		 }

		public class Actions
		{
			public static Exception RetryExceededMaxTries = new Exception("RetryExceededMaxTries");
			public static Exception RetryTimedOut = new Exception("RetryTimedOut");

			public delegate T RetryDelegate<T>();

			public delegate bool CompletedDelegate<T, Object>(T result, Object o);

			public static T Retry<T>(RetryDelegate<T> Action, CompletedDelegate<T, Object> Completed,
				Object completed_delegate_object, int wait_secs, int max_tries, TimeSpan timeout_secs)
			{
				T result = default(T);
				DateTime start = DateTime.UtcNow;
				int tries = 0;
				var method_name = Action.Method.Name;
				while (TimeToGiveUp(start, timeout_secs, tries, max_tries) == false)
				{
					try
					{
						tries++;
						result = Action.Invoke();
						if (Completed(result, completed_delegate_object) == true)
							return result;
					}
					catch (Exception e)
					{
						GenUtils.PriorityLogMsg("exception", "RetryDelegate: " + method_name,
							e.Message + e.StackTrace);
						throw (e);
					}
					HttpUtils.Wait(wait_secs);
				}

				if (TimedOut(start, timeout_secs))
					throw RetryTimedOut;

				if (ExceededTries(tries, max_tries))
					throw RetryExceededMaxTries;

				return result;  // default(T)
			}

			private static bool TimeToGiveUp(DateTime start, TimeSpan timeout_secs,
				int tries, int max_tries)
			{
				var timed_out = TimedOut(start, timeout_secs);
				var exceeded_tries = ExceededTries(tries, max_tries);
				bool result = (timed_out || exceeded_tries);
				return result;
			}

			private static bool ExceededTries(int tries, int max_tries)
			{
				var exceeded_tries = tries > max_tries;
				return exceeded_tries;
			}

			private static bool TimedOut(DateTime start, TimeSpan timeout_seconds)
			{
				var timed_out = DateTime.UtcNow > start + timeout_seconds;
				return timed_out;
			}
		}

		public static string MakeEntityConnectionString(string model_name)
		{
			var sql_builder = new SqlConnectionStringBuilder();

			/*

			sql_builder.DataSource = "tcp:" + Configurator.sql_azure_host;
			sql_builder.InitialCatalog = "elmcity";
			sql_builder.UserID = Configurator.sql_azure_user;
			sql_builder.Password = Configurator.sql_azure_pass;
			 * 
			 */

			string sql_provider_string = sql_builder.ToString();

			var entity_builder = new EntityConnectionStringBuilder();
			entity_builder.Provider = "System.Data.SqlClient";
			entity_builder.ProviderConnectionString = sql_provider_string;

			entity_builder.Metadata = String.Format(@"res://*/{0}.csdl|res://*/{0}.ssdl|res://*/{0}.msl", model_name);
			return entity_builder.ToString();
		}

		static public int RunTests(string dll_name)
		{
			var tests_failed = 0;

			try
			{
				LogMsg("info", "GenUtils.RunTests", "starting");
				var ts = TableStorage.MakeDefaultTableStorage();
				var a = System.Reflection.Assembly.Load(dll_name);
				var types = a.GetExportedTypes().ToList();
				var test_classes = types.FindAll(type => type.Name.EndsWith("Test")).ToList();
				test_classes.Sort((x, y) => x.Name.CompareTo(y.Name));



				foreach (Type test_class in test_classes)  // e.g. DeliciousTest
				{
					object o = Activator.CreateInstance(test_class);

					var members = test_class.GetMembers().ToList();
					members.Sort((x, y) => x.Name.CompareTo(y.Name));

					foreach (var member in members)
					{
						var attrs = member.GetCustomAttributes(false).ToList();
						var is_test = attrs.Exists(attr => attr.GetType() == typeof(NUnit.Framework.TestAttribute));
						if (is_test == false)
							continue;

						var entity = new Dictionary<string, object>();
						var partition_key = test_class.FullName;
						var row_key = member.Name;
						entity["PartitionKey"] = partition_key;
						entity["RowKey"] = row_key;

						try
						{
							test_class.InvokeMember(member.Name, invokeAttr: BindingFlags.InvokeMethod, binder: null, target: o, args: null);
							entity["outcome"] = "OK";
							entity["reason"] = "";
						}
						catch (Exception e)
						{
							var msg = e.Message + e.StackTrace;
							entity["outcome"] = "Fail";
							entity["reason"] = e.InnerException.Message + e.InnerException.StackTrace;
							tests_failed += 1;
						}

						var tablename = Configurator.test_results_tablename;
						if (ts.ExistsEntity(tablename, partition_key, row_key))
							ts.MergeEntity(tablename, partition_key, row_key, entity);
						else
							ts.InsertEntity(tablename, entity);
					}
				}
				LogMsg("info", "GenUtils.RunTests", "done");
			}
			catch (Exception e)
			{
				PriorityLogMsg("exception", "RunTests", e.Message + e.StackTrace);
				tests_failed = 999;
			}
			return tests_failed;
		}

		#region datetime

		static public string DateTimeForAzureTableQuery(DateTime dt)
		{
			return dt.ToString("yyyy-mm-ddThh:mm:ss");
		}

		#endregion

		#region config

		// try getting value from source-of-truth azure table, else non-defaults if overridden in azure config.
		// why? 
		// 1. dry (don't repeat yourself, in this case by not writing down settings twice, for worker and web role
		// 2. testing: tests run outside azure environment can use same defaults as used within

		// the source of truth for settings is in an azure table called settings
		public static Dictionary<string, string> GetSettingsFromAzureTable()
		{
			return GetSettingsFromAzureTable("settings");
		}

		public static Dictionary<string, string> GetSettingsFromAzureTable(string tablename)
		{
			var settings = new Dictionary<string, string>();
			var query = string.Format("$filter=PartitionKey eq '{0}'", tablename);
			var ts = TableStorage.MakeSecureTableStorage();
			var ts_response = ts.QueryAllEntitiesAsListDict(tablename, query, 0);
			var dicts = ts_response.list_dict_obj;
			foreach (var dict in dicts)
			{
				var dict_of_str = ObjectUtils.DictObjToDictStr(dict);
				var name = "";
				try
				{
					name = dict_of_str["RowKey"];
					var value = dict_of_str["value"];
					settings[name] = value;
				}
				catch (Exception e)
				{
					LogMsg("exception", "Configurator.GetSettings: " + name, e.Message + e.StackTrace);
				}
			}
			return settings;
		}

		#endregion

		#region regex

		public static string RegexReplace(string input, string pattern, string replacement)
		{
			Regex re = new Regex(pattern, RegexOptions.Singleline);
			return re.Replace(input, replacement);
		}

		public static List<string> RegexFindGroups(
			string input,
			string pattern)
		{
			Regex re = new Regex(pattern);
			var groups = re.Match(input).Groups;
			var values = new List<string>();
			foreach (Group g in groups)
				values.Add(g.Value);
			return values;
		}

		public static List<string> RegexFindKeyValue(string input)
		{
			var pattern = @"\s*(\w+)=([^\s]+)\s*";
			var groups = RegexFindGroups(input, pattern);
			var list = new List<string>();
			if (groups[0] == input)
			{
				list.Add(groups[1]);
				list.Add(groups[2]);
			}
			return list;
		}

		public static Dictionary<string, string> RegexFindKeysAndValues(List<string> keys, string input)
		{
			string keystrings = String.Join("|", keys.ToArray());
			string regex = String.Format(@"({0})=([^\s]+)", keystrings);
			Regex reg = new Regex(regex);
			var metadict = new Dictionary<string, string>();
			Match m = reg.Match(input);
			while (m.Success)
			{
				var key_value = RegexFindKeyValue(m.Groups[0].ToString());
				metadict.Add(key_value[0], key_value[1]);
				m = m.NextMatch();
			}
			return metadict;
		}

		public static int RegexCountSubstrings(string input, string pattern)
		{
			Regex re = new Regex(pattern);
			var chunks = re.Split(input);
			return chunks.Count() - 1;
		}

		#endregion regex

		#region dict

		public static Dictionary<string, string> DictTryAddStringValue(Dictionary<string, string> dict, string key, string value)
		{
			if (dict.ContainsKey(key))
				dict[key] = value;
			else
				dict.Add(key, value);
			return dict;
		}

		public static List<Dictionary<string, object>> GetOdataDicts(byte[] bytes)
		{
			var dicts = new List<Dictionary<string, object>>();

			if (bytes.Length > 0)
			{
				var xdoc = XmlUtils.XdocFromXmlBytes(bytes);
				IEnumerable<XElement> query;
				query = from propbags in xdoc.Descendants(odata_metadata_namespace + "properties") select propbags;
				dicts = UnpackPropBags(query);
			}
			return dicts;
		}

		// walk and unpack an enumeration of <m:properties> elements
		private static List<Dictionary<string, object>> UnpackPropBags(IEnumerable<XElement> query)
		{
			var dicts = new List<Dictionary<string, object>>();
			foreach (XElement propbag in query)
			{
				var dict = new Dictionary<string, object>();
				IEnumerable<XElement> query2 = from props in propbag.Descendants() select props;
				foreach (XElement prop in query2)
				{
					object value = prop.Value;
					var attrs = prop.Attributes();
					foreach (var attr in attrs)
					{
						if (attr.Name.LocalName == "type")
						{
							switch (attr.Value)
							{
								case "Edm.DateTime":
									value = Convert.ToDateTime(prop.Value);
									break;

								case "Edm.Int32":
									value = Convert.ToInt32(prop.Value);
									break;

								case "Edm.Int64":
									value = Convert.ToInt64(prop.Value);
									break;

								case "Edm.Float":
									value = float.Parse(prop.Value);
									break;

								case "Edm.Boolean":
									value = Convert.ToBoolean(prop.Value);
									break;
							}
						}
					}
					dict.Add(prop.Name.LocalName, value);
				}
				dicts.Add(dict);
			}
			return dicts;
		}

		#endregion

		#region str

		public static string Shorter(string s1, string s2)
		{
			return s1.Length < s2.Length ? s1 : s2;
		}

		public static string Longer(string s1, string s2)
		{
			return s1.Length > s2.Length ? s1 : s2;
		}

		#endregion str

		#region enum

		public static List<string> EnumToList<T>()
		{
			Type enumType = typeof(T);
			if (enumType.BaseType != typeof(Enum))
			{
				throw new ArgumentException("T must be a System.Enum");
			}
			List<T> enums = (Enum.GetValues(enumType) as IEnumerable<T>).ToList();
			var list = new List<string>();
			foreach (var e in enums)
				list.Add(e.ToString());
			return list;
		}

		#endregion

		#region json

		public static string PrettifyJson(string json)
		{
			var pp = new JsonPrettyPrinter();
			var sb_ugly = new StringBuilder(json);
			var sb_pretty = new StringBuilder();
			pp.PrettyPrint(sb_ugly, sb_pretty);
			return sb_pretty.ToString();
		}

		#endregion

		#region other

		public static bool AreEqualLists<T>(IList<T> A, IList<T> B)
		{
			HashSet<T> setA = new HashSet<T>(A);
			return setA.SetEquals(B);
		}

		public static IEnumerable<int> EveryNth(int start, int step, int stop)
		{
			int i = start;
			for (; i <= stop; i += step)
				yield return i;
		}

		public static string FindCalProps(string property, string text)
		{
			var mc = Regex.Matches(text, property + "[:;][^\r\n]+", RegexOptions.Singleline);
			var sb = new StringBuilder();
			foreach (var m in mc)
				sb.AppendLine(m.ToString());
			return sb.ToString();
		}

		#endregion

	}

	#region extensions

	public static class ListExtensions
	{
		public static bool HasItem<T>(this List<T> list, T item)
		{
			return list.Exists(x => x.Equals(item));
		}

		public static bool IsSubsetOf<T>(this List<T> list_one, List<T> list_two)
		{
			return !list_one.Except(list_two).Any();
		}

		public static IEnumerable<T> Unique<T>(this IEnumerable<T> source)
		{
			var result = new HashSet<T>();

			foreach (T element in source)
			{
				result.Add(element);
			}
			// yield to get deferred execution
			foreach (T element in result)
			{
				yield return element;
			}
		}

		public static IEnumerable<T> OnlyUnique<T>(this IEnumerable<T> source) // http://stackoverflow.com/questions/724479/any-chance-to-get-unique-records-using-linq-c
		{
			// No error checking :)

			HashSet<T> toReturn = new HashSet<T>();
			HashSet<T> seen = new HashSet<T>();

			foreach (T element in source)
			{
				if (seen.Add(element))
				{
					toReturn.Add(element);
				}
				else
				{
					toReturn.Remove(element);
				}
			}
			// yield to get deferred execution
			foreach (T element in toReturn)
			{
				yield return element;
			}
		}

		public static List<T> RemoveUnlessFound<T>(this List<T> source_list, T item, List<T> target_list)
		{
			if (!target_list.Exists(x => x.Equals(item)))
			{
				source_list.Remove(item);
			}
			return source_list;
		}

		public static int FindDictInListOfDict<TKey,TVal>(this List<Dictionary<TKey, TVal>> list_dict, TKey keyname, TVal keyval)
		{
			return list_dict.FindIndex(delegate(Dictionary<TKey, TVal> dict)
			{ return dict[keyname].Equals(keyval); }
			);
		}

	}

	public static class DictionaryExtensions
	{
		public static void AddOrUpdateDictionary<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue value)
		{
			if (dict.ContainsKey(key))
				dict[key] = value;
			else
				dict.Add(key, value);
		}

		public static void AddOrAppendDictOfListT<TKey, TValue>(this IDictionary<TKey, List<TValue>> dict, TKey key, TValue value)
		{
			if (dict.ContainsKey(key))
				dict[key].Add(value);
			else
				dict[key] = new List<TValue>() { value };
		}

		public static void AddOrUpdateDictOfListT<TKey, TValue>(this IDictionary<TKey, List<TValue>> dict, TKey key, List<TValue> values)
		{
			if (dict.ContainsKey(key))
			{
				var diffs = values.Except(dict[key]);
				foreach (var value in diffs)
					dict[key].Add(value);
			}
			else
			{
				dict[key] = values;
			}
		}

		// todo: replace use of this with generic
		public static void AddOrUpdateDictOfListStr(this IDictionary<string, List<string>> dict, string key, List<string> values)
		{
			if (dict.ContainsKey(key))
			{
				var diffs = values.Except(dict[key]);
				foreach (var value in diffs)
					dict[key].Add(value);
			}
			else
			{
				dict[key] = values;
			}
		}

		public static bool KeySetEqual<TKey, TValue>(this IDictionary<TKey, TValue> dict, List<TKey> keys)
		{
			return GenUtils.AreEqualLists<TKey>(dict.Keys.ToList(), keys);
		}

		public static TValue GetValueOrDefault<TKey, TValue> (this IDictionary<TKey, TValue> dict, TKey key)
		{
			if (dict.ContainsKey(key))
				return dict[key];
			else
				return default(TValue);
		}

		public static void IncrementOrAdd<T>(this IDictionary<T,int> dict, T key)
		{
		if (dict.ContainsKey(key))
			dict[key] += 1;
		else
			dict[key] = 1;
		}

		public static bool HasValue<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue value)
		{
			if (dict.ContainsKey(key) && dict[key].Equals(value))
				return true;
			else
				return false;
		}

		public static bool HasNonEmptyOrNullStringValue<TKey,TValue>(this IDictionary<TKey, TValue> dict, TKey key)
		{
			if ( ! dict.ContainsKey(key) || ! (dict[key] is String) )
				return false;

			var value = dict[key] as String;

			if ( ! String.IsNullOrEmpty( value ) )
				return true;
			else
				return false;
		}

		public static bool HasStringValueStartingWith<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, string prefix)
		{
			if (!dict.ContainsKey(key) || !(dict[key] is String))
				return false;

			var value = dict[key] as String;

			if (String.IsNullOrEmpty(value))
				return false;
			else
				return value.ToLower().StartsWith(prefix.ToLower());
				
		}

		public static bool HasNonZeroIntValue<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key)
		{
			if (!dict.ContainsKey(key) || !(dict[key] is int))
				return false;

			var value = Convert.ToInt32(dict[key]);

			if (value != 0 )
				return true;
			else
				return false;
		}

	}

	public static class ObjectExtensions
	{

		public static T CloneObject<T>(this T obj) where T : class        // http://stackoverflow.com/questions/2023210/cannot-access-protected-member-object-memberwiseclone
		{
			if (obj == null) return null;
			System.Reflection.MethodInfo inst = obj.GetType().GetMethod("MemberwiseClone",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			if (inst != null)
				return (T)inst.Invoke(obj, null);
			else
				return null;
		}

		public static bool HasProperty(this object o, string name)
		{
			var type = o.GetType();
			return type.GetMembers().ToList().Exists(x => x.Name == name);
		}
	}

	public static class StringExtensions
	{

		public static string SingleQuote (this string s)
		{
			return "'" + s + "'";
		}

		public static string TruncateToLength(this string s, int length)
		{
			if (s.Length <= length)
				return s;

			return s.Substring(0, length);
		}

		public static bool KeySetEqual<TKey, TValue>(this IDictionary<TKey, TValue> dict, List<TKey> keys)
		{
			return GenUtils.AreEqualLists<TKey>(dict.Keys.ToList(), keys);
		}

		public static string UrlsToLinks(this string text)
		{
			try
			{   // http://rickyrosario.com/blog/converting-a-url-into-a-link-in-csharp-using-regular-expressions/
				string regex = @"((www\.|(http|https|ftp|news|file)+\:\/\/)[_a-z0-9-]+\.[_a-z0-9\/+:@=.+?,##%&~-]*[^.|\'|\# |!|\(|?|,| |>|<|;|\)])";
				Regex r = new Regex(regex, RegexOptions.IgnoreCase);
				return r.Replace(text, "<a href=\"$1\" title=\"Click to open in a new window or tab\" target=\"&#95;blank\">$1</a>").Replace("href=\"www", "href=\"http://www");
			}

			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "UrlsToLinks:  " + text, e.Message);
				return text;
			}
		}

		public static string StripHtmlTags(this string text)
		{
			var re = new Regex("<.*?>", RegexOptions.Compiled); 
			return re.Replace(text, " ");
		}

		public static string EscapeValueForCsv(this string text)
		{
			try
			{
				text = text.Replace("\"", "\"\"");
				text = text.Replace("\n", "\\n");
				return text;
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("warning", "EscapeValueForCsv", e.Message);
				return "";
			}
		}

	}

    #endregion

	public static class MathHelpers
	{
		public static bool Between(this int num, int lower, int upper, bool inclusive = false)
		{
			return inclusive
				? lower <= num && num <= upper
				: lower < num && num < upper;
		}
	}

	public class CaseInsensitiveComparer : IComparer<string>
	{
		public int Compare(string x, string y)
		{
			return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
		}
	}
}