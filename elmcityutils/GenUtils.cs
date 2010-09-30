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
using System.Data;
using System.Data.SqlClient;
using System.Data.EntityClient;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ElmcityUtils
{
	public class GenUtils
	{
		private static TableStorage default_ts = TableStorage.MakeDefaultTableStorage();

		public static void LogMsg(string type, string title, string blurb, TableStorage ts)
		{
			if (ts == null) ts = default_ts;
			title = MakeLogMsgTitle(title);
			ts.WriteLogMessage(type, title, blurb);
		}

		public static string MakeLogMsgTitle(string title)
		{
			string hostname = Dns.GetHostName(); 
			var procname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
			title = string.Format("{0} {1} {2}", hostname, procname, title);
			return title;
		}

		public static void LogMsg(string type, string title, string blurb)
		{
			LogMsg(type, title, blurb, default_ts);
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
						LogMsg("exception", "RetryDelegate: " + method_name, 
							e.Message + e.StackTrace);
						throw e;
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

			sql_builder.DataSource = "tcp:" + Configurator.sql_azure_host;
			sql_builder.InitialCatalog = "elmcity";
			sql_builder.UserID = Configurator.sql_azure_user;
			sql_builder.Password = Configurator.sql_azure_pass;

			string sql_provider_string = sql_builder.ToString();

			var entity_builder = new EntityConnectionStringBuilder();
			entity_builder.Provider = "System.Data.SqlClient";
			entity_builder.ProviderConnectionString = sql_provider_string;

			entity_builder.Metadata = String.Format(@"res://*/{0}.csdl|res://*/{0}.ssdl|res://*/{0}.msl", model_name);
			return entity_builder.ToString();
		}

		static public void RunTests(string dll_name)
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
			var settings = new Dictionary<string, string>();
			var query = "$filter=PartitionKey eq 'settings'";
			var ts = TableStorage.MakeSecureTableStorage();
			var ts_response = ts.QueryEntities("settings", query).response;
			var dicts = (List<Dictionary<string, object>>)ts_response;
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

		public static List<string> RegexFindAll(string input, string pattern)
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
			var pattern = @"\s*(\w+)=(.+)\s*";
			var groups = RegexFindAll(input, pattern);
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
			string regex = @"(category|url)=([^\b\s\n]+)";
			Regex reg = new System.Text.RegularExpressions.Regex(regex);
			string keystrings = String.Join("|", keys.ToArray());
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
	}
}