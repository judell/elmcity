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
using ElmcityUtils;

namespace CalendarAggregator
{
	public class Task
	{
		public string id { get; set; }
		public DateTime start { get; set; }
		public DateTime stop { get; set; }
		public bool running { get; set; }

		public Task() { }

		public Task(string id, DateTime start, DateTime stop, bool running)
		{
			this.id = id;
			this.start = start;
			this.stop = stop;
			this.running = running;
		}
	}

	// model aggregation tasks as records in an azure table, provide methods
	// to start, stop, check if running
	public static class Scheduler
	{

		public static DateTime dtzero { get { return _dtzero; } }
		private static DateTime _dtzero = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();
		private static string tasktable = "tasks";
		private static string master_pk = "master";
		private static string lock_pk = "lock";
		private static string task_query_template = "$filter=(PartitionKey eq '{0}' and RowKey eq '{1}' )";

		public static TimeSpan where_interval { get { return _where_interval; } }
		private static TimeSpan _where_interval = new TimeSpan(Configurator.where_aggregate_interval_hours, 0, 0);

		public static TimeSpan what_interval { get { return _what_interval; } }
		private static TimeSpan _what_interval = new TimeSpan(Configurator.what_aggregate_interval_hours, 0, 0);


		public static bool ExistsTaskRecordForId(string id)
		{
			var task = FetchTaskForId(id);
			return (task.id == id);
		}

		public static bool ExistsLockRecordForId(string id)
		{
			var q = string.Format(task_query_template, lock_pk, id);
			var dict_obj = TableStorage.QueryForSingleEntityAsDictObj(ts, tasktable, q);
			return dict_obj.Keys.Count != 0;
		}

		public static void InitTaskForId(string id)
		{
			Scheduler.UnlockId(id);

			var task = new Task(id, start: dtzero, stop: dtzero, running: false);
			var dict_obj = ObjectUtils.ObjToDictObj(task);

			var ts_response = TableStorage.UpdateDictToTableStore(dict_obj, table: tasktable, partkey: master_pk, rowkey: id);
			var http_response = ts_response.http_response;
			GenUtils.LogMsg("info", "Scheduler.InitTaskForId: " + id, http_response.status.ToString());
		}

		public static void StoreTaskForId(Task task, string id)
		{
			var dict_obj = ObjectUtils.ObjToDictObj(task);
			var ts_response = TableStorage.UpmergeDictToTableStore(dict_obj, table: tasktable, partkey: master_pk, rowkey: id);
			GenUtils.LogMsg("info", "Scheduler.StoreTaskForId: " + id, null);
		}

		public static Task FetchTaskForId(string id)
		{
			var q = string.Format(task_query_template, master_pk, id);
			var dict_obj = TableStorage.QueryForSingleEntityAsDictObj(ts, tasktable, q);
			var task = (Task)ObjectUtils.DictObjToObj(dict_obj, new Task().GetType());
			return task;
		}

		public static HttpResponse StartTaskForId(string id)
		{
			var task = new Dictionary<string, object>();
			task["id"] = id;
			task["start"] = DateTime.Now.ToUniversalTime();
			task["running"] = true;
			var ts_response = TableStorage.UpmergeDictToTableStore(task, tasktable, partkey: master_pk, rowkey: id);
			var http_response = ts_response.http_response;
			GenUtils.LogMsg("info", "Scheduler.StartTaskForId: " + id, http_response.status.ToString());
			return http_response;
		}

		public static void StopTaskForId(string id)
		{
			var task = new Dictionary<string, object>();
			task["id"] = id;
			task["stop"] = DateTime.Now.ToUniversalTime();
			task["running"] = false;
			var ts_response = TableStorage.UpmergeDictToTableStore(task, table: tasktable, partkey: master_pk, rowkey: id);
			GenUtils.LogMsg("info", "Scheduler.StopTaskForId: " + id, ts_response.http_response.status.ToString());
		}

		public static bool MaybeStartTaskForId(DateTime now, Calinfo calinfo)
		{
			var id = calinfo.delicious_account;

			if (Scheduler.ExistsTaskRecordForId(id) == false)
			{
				GenUtils.PriorityLogMsg("error", "MaybeStartTaskForId: " + id, "task record does not exist but should");
				return false;
			}

			TimeSpan interval = calinfo.Interval;

			var task = FetchTaskForId(id);

			var start = task.start;

			if (now - interval > start)  // interval has expired
			{
				StartTaskForId(id);
				return true;
			}
			else
				return false;
		}

		public static HttpResponse LockId(string id)
		{
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", lock_pk);
			entity.Add("RowKey", id);
			entity.Add("LockedAt", DateTime.Now.ToUniversalTime());
			var ts_response = ts.InsertEntity(tasktable, entity);
			return ts_response.http_response;
		}

		public static bool IsLockedId(string id)
		{
			var q = string.Format(TableStorage.query_template_pk_rk, lock_pk, id);
			return ts.ExistsEntity(tasktable, q);
		}

		public static HttpResponse UnlockId(string id)
		{
			if (ExistsLockRecordForId(id))
				return ts.DeleteEntity(tasktable, lock_pk, id).http_response;
			else
				return default(HttpResponse);
		}

		public static bool IsAbandoned(string id, TimeSpan interval)
		{
			var task = Scheduler.FetchTaskForId(id);

			if (IsLockedId(id) == true && task.running == false)
				return true;

			var start = task.start;
			start = start.ToUniversalTime();
			var now = DateTime.Now.ToUniversalTime();

			if (now - start <= interval)
				return false;
			else
				return true;
		}

		public static void EnsureTaskRecord(string id)
		{
			if (Scheduler.ExistsTaskRecordForId(id) == false)
			{
				GenUtils.LogMsg("info", "MaybeCreateTaskRecord: creating task for " + id, null);
				Scheduler.InitTaskForId(id);
			}
		}

	}
}

/*
 Table: tasks
 
 Active records:
 
PK = 'master'
RK = id
 
 <m:properties>
   <d:PartitionKey>a2cal</d:PartitionKey>
   <d:RowKey>master</d:RowKey>
   <d:start>DateTime</d:start>
   <d:stop>DateTime</d:stop>
   <d:running>bool</d:stop>
</m:properties>

 Lock records:
 
 PK = 'lock'
 RK = id
 
>bool</d:stop>
</m:properties>

*/

/*
interval: 8hr
        
case 1a:  stop > start, within interval: not abandoned
 
start: 13:00  
stop:  13:05
now:   14:00
now - start: 1hr

case 1b) stop > start, beyond interval: abandoned

start: 13:00  
stop:  13:05
now:   23:00 
now - start: 10hr

case 2a) stop < start, within interval: not abandoned

start: 13:00 
 stop:  7:05
  now: 14:00
now - start: 1hr

case 2b) stop < start, beyond interval: abandoned

start: 13:00  
 stop:  7:05
  now: 13:00
now - start: 10hr

*/

