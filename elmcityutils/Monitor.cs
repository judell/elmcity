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
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.WindowsAzure.Diagnostics;

namespace ElmcityUtils
{
	public class Monitor
	{
		public CounterResponse counters;
		private int interval_minutes;
		private string tablename;
		private TableStorage ts = TableStorage.MakeDefaultTableStorage();
		private List<Dictionary<string, string>> priority_log_triggers;

		public Monitor(int interval_minutes, string tablename)
		{
			this.interval_minutes = interval_minutes;
			this.tablename = tablename;
		}

		public Monitor(int interval_minutes, string tablename, TableStorage ts)
		{
			this.interval_minutes = interval_minutes;
			this.tablename = tablename;
			this.ts = ts;
			priority_log_triggers = GetPriorityLogTriggers(ts);
		}

		private static List<Dictionary<string, string>> GetPriorityLogTriggers(TableStorage ts)
		{
			var query = "$filter=(PartitionKey eq 'prioritylogtriggers')";
			var ts_response = ts.QueryEntities("prioritylogtriggers", query);
			var list_dict_obj = ts_response.list_dict_obj;
			var list_dict_str = new List<Dictionary<string, string>>();
			foreach (var dict_obj in list_dict_obj)
				list_dict_str.Add(ObjectUtils.DictObjToDictStr(dict_obj));
			return list_dict_str;
		}

		public static Monitor TryStartMonitor(int interval_minutes, string tablename)
		{
			Monitor monitor = null;
			try
			{
				monitor = new Monitor(interval_minutes, tablename);
				monitor.StartMonitor();
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "StartMonitor", e.Message + e.StackTrace);
			}
			return monitor;
		}


		public void StartMonitor()
		{
			GenUtils.LogMsg("info", "StartMonitor", "starting");
			var ProcessMonitorThread = new Thread(new ThreadStart(ProcessMonitorThreadMethod));
			ProcessMonitorThread.Start();
		}

		public void ReloadCounters()
		{
			GenUtils.LogMsg("info", "Monitor.ReloadCounters", null);
			this.counters = Counters.GetCounters();
			this.priority_log_triggers = GetPriorityLogTriggers(this.ts);
		}

		public void ProcessMonitorThreadMethod()
		{
			while (true)
			{
				try
				{
					GenUtils.LogMsg("info", "ProcessMonitorThreadMethod", "snapshot");
					this.ReloadCounters();
					var snapshot = Counters.MakeSnapshot(counters);
					this.StoreSnapshot(snapshot);
					this.MaybeWritePriorityLog(snapshot);
					Thread.Sleep(TimeSpan.FromMinutes(this.interval_minutes));
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "ProcessMonitorThreadMethod: snapshot", e.Message + e.StackTrace);
				}
			}
		}

		public void MaybeWritePriorityLog(Dictionary<string, object> snapshot)
		{
			try
			{
				foreach (var trigger_dict in priority_log_triggers)
				{
					var key = trigger_dict["RowKey"];
					var is_min_trigger = trigger_dict.ContainsKey("min");
					var is_max_trigger = trigger_dict.ContainsKey("max");
					if (snapshot.ContainsKey(key))
						EvaluateTrigger(key, trigger_dict, snapshot);
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "MaybeWritePriorityLog", e.Message + e.StackTrace);
			}
		}

		private void EvaluateTrigger(string key, Dictionary<string, string> trigger_dict, Dictionary<string, object> snapshot)
		{
			int snapshot_int;
			float snapshot_float;
			int trigger_int;
			float trigger_float;
			var type = trigger_dict["type"];
			switch (type)
			{
				case "float":
					snapshot_float = (float)snapshot[key];
					if (trigger_dict.ContainsKey("max"))
					{
						trigger_float = float.Parse(trigger_dict["max"]);
						if (snapshot_float > trigger_float)
							GenUtils.PriorityLogMsg("warning", key, String.Format("snapshot ({0}) > trigger ({1})", snapshot_float, trigger_float));
					}
					if (trigger_dict.ContainsKey("min"))
					{
						trigger_float = float.Parse(trigger_dict["min"]);
						if (snapshot_float < trigger_float)
							GenUtils.PriorityLogMsg("warning", key, String.Format("snapshot ({0}) < trigger ({1})", snapshot_float, trigger_float));
					}
					break;
				case "int":
					snapshot_int = Convert.ToInt32(snapshot[key]);
					if (trigger_dict.ContainsKey("max"))
					{
						trigger_int = Convert.ToInt32(trigger_dict["max"]);
						if (snapshot_int > trigger_int)
							GenUtils.PriorityLogMsg("warning", key, String.Format("snapshot ({0}) > trigger ({1})", snapshot_int, trigger_int));
					}
					if (trigger_dict.ContainsKey("min"))
					{
						trigger_int = Convert.ToInt32(trigger_dict["min"]);
						if (snapshot_int < trigger_int)
							GenUtils.PriorityLogMsg("warning", key, String.Format("snapshot ({0}) < trigger ({1})", snapshot_int, trigger_int));
					}
					break;
				default:
					GenUtils.LogMsg("warning", "MaybeWritePriorityLog", "unexpected type: " + type);
					break;
			}

		}

		public void StoreSnapshot(Dictionary<string, object> snapshot)
		{
			try
			{
				snapshot["PartitionKey"] = this.tablename;
				snapshot["RowKey"] = DateTime.Now.Ticks.ToString();
				this.ts.InsertEntity(tablename, snapshot);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "StoreSnapshot", e.Message + e.StackTrace);
			}

		}
	}

	public class CounterResponse
	{
		public Dictionary<string, string> counter_paths;
		public Dictionary<string, PerformanceCounter> counter_objects;

		public CounterResponse(Dictionary<string, string> counter_paths, Dictionary<string, PerformanceCounter> counter_objects)
		{
			this.counter_paths = counter_paths;
			this.counter_objects = counter_objects;
		}
	}

	// enables use of an azure table to record which counters to monitor
	public static class Counters
	{
		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();

		public static void SetTs(TableStorage ts)
		{
			Counters.ts = ts;
		}

		// use the counter_paths
		public static void AddCountersToConfig(DiagnosticMonitorConfiguration config, string excluded_specifier_prefix)
		{
			var counter_paths = GetCounters().counter_paths;

			foreach (string key in counter_paths.Keys)
			{
				var counter_path = counter_paths[key];
				var sample_rate = TimeSpan.FromSeconds(Configurator.default_counter_sample_rate);

				// because, e.g., worker role won't be using ASP.NET counters
				if (excluded_specifier_prefix != null && counter_path.StartsWith(excluded_specifier_prefix))
					continue;

				config.PerformanceCounters.DataSources.Add
						(
						new PerformanceCounterConfiguration()
							{
								CounterSpecifier = counter_path,
								SampleRate = sample_rate
							}
						);
			}
		}

		public static CounterResponse GetCounters()
		{
			return _GetCounters();
		}

		private static CounterResponse _GetCounters()
		{
			/* the counters we want to store and read are named in an azure table, sample record like so:
             
				  Table: counters
			 PartionKey: counters
				 RowKey: proc_total_time
			   Category: Processor
			Description: % Processor Time
			  Qualifier: _Total
            
			 return an object with two dicts
              
			 1. of path-like strings used to specify counters to Azure diagnostics)
              
			 key: processor_pct_proc_time
			 val: /Processor(_Total)/% Processor time
               
			 2. of actual counters used for live snapshot
             
			 key: processor_pct_proc_time
			 val: new PerformanceCounter("Processor", "% Processor Time", "_Total")
			 */

			var counter_paths = new Dictionary<string, string>();
			var counter_objects = new Dictionary<string, PerformanceCounter>();

			var query = "$filter=(PartitionKey eq 'counters')";
			var ts_response = ts.QueryEntities("counters", query);
			var counter_names_and_categories = ts_response.list_dict_obj;

			foreach (var counter_name_and_category in counter_names_and_categories)
			{
				var c = ObjectUtils.DictObjToDictStr(counter_name_and_category);

				if (c.ContainsKey("active") && c["active"] == "no") // skip if marked inactive
					continue;

				var category = c["category"];

				//http://support.microsoft.com/?kbid=2022138 -> solved in v4?

				//if (category == "ASP.NET")
				//	category = "ASP.NET v2.0.50727";

				//if (category == "ASP.NET Applications")
				//	category = "ASP.NET Apps v2.0.50727";

				var qualifier = (c.ContainsKey("qualifier") ? c["qualifier"] : null);
				var description = c["description"];

				try
				{

					if (qualifier != null)
					{
						counter_paths.Add(c["RowKey"], String.Format(@"\{0}({1})\{2}", category, qualifier, description));
						counter_objects.Add(c["RowKey"], new PerformanceCounter(categoryName: category, counterName: description, instanceName: qualifier));
					}
					else
					{
						counter_paths.Add(c["RowKey"], String.Format(@"\{0}\{1}", category, description));
						counter_objects.Add(c["RowKey"], new PerformanceCounter(categoryName: category, counterName: description));

					}
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "GetCounters", category + "/" + description + " -> " + e.Message + e.StackTrace);
				}
			}
			return new CounterResponse(counter_paths: counter_paths, counter_objects: counter_objects);

		}

		public static Dictionary<string, object> MakeSnapshot(CounterResponse counters)
		{
			var p = Process.GetCurrentProcess();

			var dict = new Dictionary<string, object>();

			dict["PartitionKey"] = "ProcessMonitor";
			dict["RowKey"] = DateTime.Now.ToUniversalTime().Ticks.ToString();

			// add general info

			dict["HostName"] = System.Net.Dns.GetHostName();
			dict["ProcName"] = p.ProcessName;
			dict["ProcId"] = System.Diagnostics.Process.GetCurrentProcess().Id;

			// add process info

			dict["ThreadCount"] = p.Threads.Count;
			dict["TotalProcTime"] = p.TotalProcessorTime.Ticks;
			dict["TotalUserTime"] = p.UserProcessorTime.Ticks;
			dict["PagedMemSize"] = p.PagedMemorySize64;
			dict["NonPagedMemSize"] = p.NonpagedSystemMemorySize64;
			dict["PrivateMemSize"] = p.PrivateMemorySize64;
			dict["VirtMemSize"] = p.VirtualMemorySize64;
			dict["PeakWorkingSet"] = p.PeakWorkingSet64;
			dict["MinWorkingSet"] = p.MinWorkingSet;

			// add counter info

			if (counters == null)
				counters = Counters.GetCounters();

			foreach (var key in counters.counter_objects.Keys)  // prime the pump
			{
				var counter = counters.counter_objects[key];
				try
				{ counter.NextValue(); }
				catch
				{ }
			}

			HttpUtils.Wait(1);

			foreach (var key in counters.counter_objects.Keys) // now read values
			{
				var counter = counters.counter_objects[key];
				try
				{
					dict[key] = counter.NextValue();
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", string.Format("MakeSnapshotDict: {0}/{1}/{2}", key, counter.CategoryName, counter.CounterName), e.Message + e.StackTrace);
				}
			}

			return dict;
		}

		private static string FormatSnapshotAsText(Dictionary<string, object> snapshot)
		{
			// snapshot is a dict with keys = process ids and values = dicts of monitor name/value pairs
			// render it as readable text

			var sb = new StringBuilder();

			var dict_str = ObjectUtils.DictObjToDictStr(snapshot);

			foreach (var key in dict_str.Keys)
			{
				sb.Append(String.Format("{0}: {1}\n", key, dict_str[key]));
				sb.Append("\n");
			}

			return sb.ToString();
		}

		public static string DisplaySnapshotAsText()
		{
			try
			{
				var snapshot = Counters.MakeSnapshot(counters: null);
				return FormatSnapshotAsText(snapshot);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "Counters.DisplaySnapshotAsText", e.Message + e.StackTrace);
				return e.Message + e.StackTrace;
			}
		}
	}

	public static class IIS_FailedRequestLogs
	{

		public static void TransferToSqlAzure()
		{
			GenUtils.LogMsg("info", "IIS_FailedRequestLogs.TransferToSqlAzure", null);
			var bs = BlobStorage.MakeDefaultBlobStorage();
			var containername = "wad-iis-failedreqlogfiles";
			var failed_req_dicts = (List<Dictionary<string, string>>)bs.ListBlobs(containername).response;
			failed_req_dicts = failed_req_dicts.FindAll(blob => (blob["Name"].EndsWith("xsl") == false));
			var failed_req_blobs = failed_req_dicts.Select(blob => blob["Name"]);

			var model_name = "iis_failed_request";
			var conn_str = GenUtils.MakeEntityConnectionString(model_name);
			var entities = new iis_failed_request_entities(conn_str);

			foreach (var blobname in failed_req_blobs)
			{
				var iis_failed_request = new iis_failed_request();
				XDocument xdoc;
				XElement failed_request;
				try
				{
					var response = bs.GetBlob(containername, blobname).HttpResponse;
					xdoc = XmlUtils.XdocFromXmlBytes(response.bytes);
					failed_request = xdoc.Root;
					XNamespace evt_ns = "http://schemas.microsoft.com/win/2004/08/events/event";
					var system = failed_request.Descendants(evt_ns + "System").First();
					var time_created_str = system.Element(evt_ns + "TimeCreated").Attribute("SystemTime").Value;

					iis_failed_request.computer = system.Element(evt_ns + "Computer").Value;
					iis_failed_request.created = DateTime.Parse(time_created_str);
					iis_failed_request.reason = failed_request.Attribute("failureReason").Value;
					iis_failed_request.duration = Convert.ToInt32(failed_request.Attribute("timeTaken").Value);
					iis_failed_request.status = failed_request.Attribute("statusCode").Value;
					iis_failed_request.url = failed_request.Attribute("url").Value;
				}
				catch (Exception ex_xml)
				{
					GenUtils.PriorityLogMsg("exception", "IIS_FailedRequestLogs.TransferToSqlAzure Unpack XML", ex_xml.Message + ex_xml.InnerException.Message + ex_xml.StackTrace);
				}

				try
				{
					entities.AddObject(entitySetName: model_name, entity: iis_failed_request);
					var db_result = entities.SaveChanges();

					if (db_result != 1)
						GenUtils.PriorityLogMsg("warning", "IIS_FailedRequestLogs.TransferToSqlAzure expected 1 saved change but got " + db_result.ToString(), null);
				}
				catch (Exception ex_db)
				{
					GenUtils.PriorityLogMsg("exception", "IIS_FailedRequestLogs.TransferToSqlAzure SaveChanges", ex_db.Message + ex_db.InnerException.Message + ex_db.StackTrace);
				}

				try
				{

					var bs_result = bs.DeleteBlob(containername, blobname);
					var status = bs_result.HttpResponse.status;
					if (status != System.Net.HttpStatusCode.Accepted)
						GenUtils.LogMsg("warning", "IIS_FailedRequestLogs.TransferToSqlAzure expected Accepted but got " + status.ToString(), null);
				}
				catch (Exception bs_db)
				{
					GenUtils.PriorityLogMsg("exception", "IIS_FailedRequestLogs.TransferToSqlAzure DeleteBlob", bs_db.Message + bs_db.StackTrace);
				}

			}
		}
	}

	public static class IIS_Logs
	{

		public static void TransferToSqlAzure()
		{
			GenUtils.LogMsg("info", "IIS_Logs.TransferToSqlAzure", null);
			var bs = BlobStorage.MakeDefaultBlobStorage();
			var containername = "wad-iis-logfiles";
			var req_dicts = (List<Dictionary<string, string>>)bs.ListBlobs(containername).response;
			var req_blobs = req_dicts.Select(blob => blob["Name"]);

			var model_name = "iis_log_entry";
			var conn_str = GenUtils.MakeEntityConnectionString(model_name);
			var entities = new iis_log_entry_entities(conn_str);

			foreach (var blobname in req_blobs)
			{
				var response = bs.GetBlob(containername, blobname).HttpResponse;
				var log_str = response.DataAsString();
				var lines = log_str.Split('\n').ToList();
				var comments = lines.FindAll(line => line.StartsWith("#"));
				var empty_or_truncated = lines.FindAll(line => line.Length < 10);
				lines = lines.Except(comments).Except(empty_or_truncated).ToList();
				foreach (var line in lines)
				{
					iis_log_entry iis_log_entry = default(iis_log_entry);
					int db_result = -1;
					try
					{
						iis_log_entry = new iis_log_entry();
						var tmpline = GenUtils.RegexReplace(line, " +", "\t");
						var fields = tmpline.Split('\t');
						iis_log_entry.datetime = DateTime.Parse(fields[0] + "T" + fields[1] + ".000Z"); // datetime
						iis_log_entry.server = fields[3]; // server
						iis_log_entry.verb = fields[5]; // verb
						iis_log_entry.url = fields[7] != "-" ? fields[6] + "?" + fields[7] : fields[6]; // url
						iis_log_entry.ip = fields[10]; // ip
						iis_log_entry.http_version = fields[11]; // http ver
						iis_log_entry.user_agent = fields[12]; // user agent
						iis_log_entry.referrer = fields[14]; // referrer
						iis_log_entry.status = Convert.ToInt16(fields[16]); // status
						iis_log_entry.w32_status = Convert.ToInt32(fields[18]); // w32 status
						iis_log_entry.sent_bytes = Convert.ToInt32(fields[19]); // sent bytes
						iis_log_entry.recv_bytes = Convert.ToInt32(fields[20]); // recv bytes
						iis_log_entry.time_taken = Convert.ToInt32(fields[21]); // time taken
					}
					catch (Exception ex_log)
					{
						GenUtils.PriorityLogMsg("exception", "IIS_Logs.TransferToSqlAzure: " + line, ex_log.Message + ex_log.InnerException.Message);
					}

					try
					{
						entities.AddObject(entitySetName: model_name, entity: iis_log_entry);
						db_result = entities.SaveChanges();
						if (db_result != 1)
							GenUtils.PriorityLogMsg("warning", "IIS_Logs.TransferToSqlAzure expected 1 but got " + db_result.ToString(), null);
					}
					catch (Exception ex_db)
					{
						GenUtils.PriorityLogMsg("exception", "IIS_Logs.TransferToSqlAzure SaveChanges", ex_db.Message + ex_db.InnerException.Message + ex_db.StackTrace);
					}
				}

				try
				{
					var bs_result = bs.DeleteBlob(containername, blobname);
					var bs_status = bs_result.HttpResponse.status;
					if (bs_status != System.Net.HttpStatusCode.Accepted)
						GenUtils.LogMsg("warning", "IIS_Logs.TransferToSqlAzure expected Accepted but got " + bs_status.ToString(), null);
				}
				catch (Exception bs_db)
				{
					GenUtils.PriorityLogMsg("exception", "IIS_Logs.TransferToSqlAzure DeleteBlob", bs_db.Message + bs_db.StackTrace);
				}
			}
		}
	}

	public static class elmcity_logs
	{

		public static void PurgeAndMaybeTransferToSqlAzure(bool transfer, DateTime since, DateTime until)
		{
			GenUtils.LogMsg("info", "elmcity_logs.TransferToSqlAzure", null);

			var model_name = "elmcity_log_entry";
			var conn_str = GenUtils.MakeEntityConnectionString(model_name);
			var entities = new elmcity_log_entry_entities(conn_str);

			var ts = TableStorage.MakeDefaultTableStorage();
			var tablename = Configurator.azure_log_table;

			var q = String.Format("$filter=(PartitionKey eq 'log' and RowKey gt '{0}' and RowKey lt '{1}')", since.Ticks, until.Ticks);
			var ts_response = ts.QueryAllEntitiesAsListDict(tablename, q);
			string rowkey = null;

			elmcity_log_entry elmcity_log_entry;

			foreach (var dict in ts_response.list_dict_obj)
			{
				rowkey = (String)dict["RowKey"];
				ts.MaybeDeleteEntity(tablename, tablename, rowkey);
				if (transfer == false)
					continue;

				elmcity_log_entry = default(elmcity_log_entry);
				int db_result = -1;
				try
				{
					elmcity_log_entry = new elmcity_log_entry();
					elmcity_log_entry.ticks = Convert.ToInt64(dict["RowKey"]);
					elmcity_log_entry.datetime = (DateTime)dict["Timestamp"];
					elmcity_log_entry.type = (String)dict["type"];
					elmcity_log_entry.message = (String)dict["message"];
					elmcity_log_entry.data = (String)dict["data"];
				}
				catch (Exception ex_log)
				{
					GenUtils.PriorityLogMsg("exception", "elmcity_logs.TransferToSqlAzure", ex_log.Message + ex_log.InnerException.Message);
				}

				try
				{

					entities.AddObject(entitySetName: model_name, entity: elmcity_log_entry);
					db_result = entities.SaveChanges();
					if (db_result != 1)
						GenUtils.LogMsg("warning", "elmcity_logs.TransferToSqlAzure expected 1 but got " + db_result.ToString(), null);
				}
				catch (Exception ex_db)
				{
					GenUtils.PriorityLogMsg("exception", "elmcity_logs.TransferToSqlAzure SaveChanges", ex_db.Message + ex_db.InnerException.Message + ex_db.StackTrace);
				}
			}

		}
	}

}
