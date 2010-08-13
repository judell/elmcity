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
using System.Xml;
using System.Xml.Linq;
using Microsoft.WindowsAzure.Diagnostics;

namespace ElmcityUtils
{
    public class Monitor
    {
        private CounterResponse counters;
        private int interval_minutes;
        private string tablename;
        private TableStorage ts = TableStorage.MakeDefaultTableStorage();

        public Monitor(int interval_minutes, string tablename)
        {
            this.interval_minutes = interval_minutes;
            this.tablename = tablename;
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
                GenUtils.LogMsg("exception", "StartMonitor", e.Message + e.StackTrace);
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
                    Thread.Sleep(TimeSpan.FromMinutes(this.interval_minutes));
                }
                catch (Exception e)
                {
                    GenUtils.LogMsg("exception", "ProcessMonitor: snapshot", e.Message + e.StackTrace);
                }
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
                GenUtils.LogMsg("exception", "StoreSnapshot", e.Message + e.StackTrace);
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
            var counter_names_and_categories = (List<Dictionary<string, object>>)ts_response.response;

            foreach (var counter_name_and_category in counter_names_and_categories)
            {
                var c = ObjectUtils.DictObjToDictStr(counter_name_and_category);
                var category = c["category"];

                //http://support.microsoft.com/?kbid=2022138

                if (category == "ASP.NET")
                    category = "ASP.NET v2.0.50727";

                if (category == "ASP.NET Applications")
                    category = "ASP.NET Apps v2.0.50727";

                var qualifier = (c.ContainsKey("qualifier") ? c["qualifier"] : null);
                var description = c["description"];

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

            foreach (var key in counters.counter_objects.Keys)
            {
                var counter = counters.counter_objects[key];
                try
                {
                    dict[key] = counter.NextValue();
                }
                catch (Exception e)
                {
                    GenUtils.LogMsg("exception", string.Format("MakeSnapshotDict: {0}/{1}/{2}", key, counter.CategoryName, counter.CounterName), e.Message + e.StackTrace);
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
                GenUtils.LogMsg("exception", "Counters.DisplaySnapshotAsText", e.Message + e.StackTrace);
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

			foreach ( var blobname in failed_req_blobs )
				{
				var response = bs.GetBlob(containername, blobname).HttpResponse;
				var xdoc = XmlUtils.XdocFromXmlBytes(response.bytes);
				var failed_request = xdoc.Root;
				XNamespace evt_ns = "http://schemas.microsoft.com/win/2004/08/events/event";
				var system = failed_request.Descendants(evt_ns + "System").First();
				var time_created_str = system.Element(evt_ns + "TimeCreated").Attribute("SystemTime").Value;
				var iis_failed_request = new iis_failed_request();
				iis_failed_request.computer = system.Element(evt_ns + "Computer").Value;
				iis_failed_request.created = DateTime.Parse(time_created_str);
				iis_failed_request.reason = failed_request.Attribute("failureReason").Value;
				iis_failed_request.duration = Convert.ToInt16(failed_request.Attribute("timeTaken").Value);
				iis_failed_request.status = Convert.ToInt16(failed_request.Attribute("statusCode").Value);
				iis_failed_request.url = failed_request.Attribute("url").Value;

				entities.AddObject(entitySetName: model_name, entity: iis_failed_request);
				var db_result = entities.SaveChanges();

				if (db_result != 1)
					GenUtils.LogMsg("warning", "IIS_FailedRequestLogs.TransferToSqlAzure expected 1 saved change but got " + db_result.ToString(), null);

				var bs_result = bs.DeleteBlob(containername, blobname);
				var status = bs_result.HttpResponse.status;
				if ( status != System.Net.HttpStatusCode.Accepted )
					GenUtils.LogMsg("warning", "IIS_FailedRequestLogs.TransferToSqlAzure expected Accepted but got " + status.ToString(), null);
			}
		}
	}

	public static class IIS_Logs
	{
	}
}
