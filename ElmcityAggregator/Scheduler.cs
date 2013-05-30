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
using ElmcityUtils;

namespace CalendarAggregator
{
	public enum TaskType { icaltasks, nonicaltasks, regiontasks, none };  // originally one table for ical and nonical tasks 	
																// now split so can run them on different schedules (ical more frequent)

	public class TaskStatus
	{
		public const string stopped = "stopped";
		public const string allocated = "allocated";
		public const string running = "running";
	}

	public class Task // todo: add a worker id
	{
		public string id { get; set; }
		public DateTime start { get; set; }
		public DateTime stop { get; set; }
		public string status { get; set; }    // stopped, allocated, running -> strings not enums for table storage
		public string hostname { get; set; }

		public Task() { }

		public Task(string id)
		{
			this.id = id;
			this.start = Scheduler.dtzero;
			this.stop = Scheduler.dtzero;
			this.status = TaskStatus.stopped;
			this.hostname = System.Net.Dns.GetHostName();
		}
	}

	// model aggregation tasks as records in an azure table, provide methods
	// to start, stop, check if running
	public static class Scheduler
	{

		public static DateTime dtzero { get { return _dtzero; } }
		private static DateTime _dtzero = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();
		private static string master_pk = "master";
		private static string lock_pk = "lock";
		private static string task_query_template = "$filter=(PartitionKey eq '{0}' and RowKey eq '{1}' )";

		public static TimeSpan nonical_interval { get { return _nonical_interval; } }
		private static TimeSpan _nonical_interval = new TimeSpan(Configurator.nonical_aggregate_interval_hours, 0, 0);

		public static TimeSpan ical_interval { get { return _ical_interval; } }
		private static TimeSpan _ical_interval = new TimeSpan(Configurator.ical_aggregate_interval_hours, 0, 0);

		public static TimeSpan region_interval { get { return _region_interval; } }
		private static TimeSpan _region_interval = new TimeSpan(Configurator.region_aggregate_interval_hours, 0, 0);

		public static bool ExistsTaskRecordForId(string id, TaskType type)
		{
			var task = FetchTaskForId(id, type);
			return (task.id == id);
		}

		public static bool ExistsLockRecordForId(string id, TaskType type)
		{
			var q = string.Format(task_query_template, lock_pk, id);
			var tasktable = type.ToString();
			var dict_obj = TableStorage.QueryForSingleEntityAsDictObj(ts, tasktable, q);
			return dict_obj.Keys.Count != 0;
		}

		public static void InitTaskForId(string id, TaskType type)
		{
			Scheduler.UnlockId(id, type);

			var task = new Task(id);
			var dict_obj = ObjectUtils.ObjToDictObj(task);
			var tasktable = type.ToString();
			var ts_response = TableStorage.UpdateDictToTableStore(dict_obj, table: tasktable, partkey: master_pk, rowkey: id);
			var http_response = ts_response.http_response;
			GenUtils.LogMsg("info", "Scheduler.InitTaskForId: " + id, http_response.status.ToString());
		}

		public static void StoreTaskForId(Task task, string id, TaskType type)
		{
			var dict_obj = ObjectUtils.ObjToDictObj(task);
			var tasktable = type.ToString();
			var ts_response = TableStorage.UpmergeDictToTableStore(dict_obj, table: tasktable, partkey: master_pk, rowkey: id);
			GenUtils.LogMsg("info", "Scheduler.StoreTaskForId: " + id, null);
		}

		public static Task FetchTaskForId(string id, TaskType type)
		{
			var q = string.Format(task_query_template, master_pk, id);
			var tasktable = type.ToString();
			var dict_obj = TableStorage.QueryForSingleEntityAsDictObj(ts, tasktable, q);
			var task = (Task)ObjectUtils.DictObjToObj(dict_obj, new Task().GetType());
			task.start = task.start.ToUniversalTime();
			task.stop = task.stop.ToUniversalTime();
			return task;
		}

		public static HttpResponse StartTaskForId(string id, TaskType type)
		{
			var task = new Dictionary<string, object>();
			task["id"] = id;
			task["start"] = DateTime.UtcNow;
			task["status"] = TaskStatus.allocated.ToString();
			task["hostname"] = System.Net.Dns.GetHostName();
			var tasktable = type.ToString();
			var ts_response = TableStorage.UpmergeDictToTableStore(task, tasktable, partkey: master_pk, rowkey: id);
			var http_response = ts_response.http_response;
			GenUtils.LogMsg("info", "Scheduler.StartTaskForId: " + id, http_response.status.ToString());
			return http_response;
		}

		public static HttpResponse UpdateStartTaskForId(string id, TaskType type)
		{
			var task = new Dictionary<string, object>();
			task["id"] = id;
			task["start"] = DateTime.UtcNow;
			task["status"] = TaskStatus.running.ToString();
			var tasktable = type.ToString();
			var ts_response = TableStorage.UpmergeDictToTableStore(task, tasktable, partkey: master_pk, rowkey: id);
			var http_response = ts_response.http_response;
			GenUtils.LogMsg("info", "Scheduler.UpdateStartTaskForId: " + id, http_response.status.ToString());
			return http_response;
		}

		public static void StopTaskForId(string id, TaskType type)
		{
			var task = new Dictionary<string, object>();
			task["id"] = id;
			task["stop"] = DateTime.UtcNow;
			task["status"] = TaskStatus.stopped.ToString();
			var tasktable = type.ToString();
			var ts_response = TableStorage.UpmergeDictToTableStore(task, table: tasktable, partkey: master_pk, rowkey: id);
			GenUtils.LogMsg("info", "Scheduler.StopTaskForId: " + id, ts_response.http_response.status.ToString());
		}

		public static TaskType MaybeStartTaskForId(DateTime now, Calinfo calinfo, TaskType type)
		{
			var id = calinfo.id;

			TimeSpan interval = IntervalFromType(type);

			var task = FetchTaskForId(id, type);

			var start = task.start;

			if (now - interval > start)  // interval has expired
			{
				StartTaskForId(id, type);
				return type;
			}
			else
				return TaskType.none;
		}

		public static HttpResponse LockId(string id, TaskType type)
		{
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", lock_pk);
			entity.Add("RowKey", id);
			entity.Add("LockedAt", DateTime.UtcNow);
			var tasktable = type.ToString();
			var ts_response = ts.InsertEntity(tasktable, entity);
			return ts_response.http_response;
		}

		public static bool IsLockedId(string id, TaskType type)
		{
			var q = string.Format(TableStorage.query_template_pk_rk, lock_pk, id);
			var tasktable = type.ToString();
			return ts.ExistsEntity(tasktable, q);
		}

		public static HttpResponse UnlockId(string id, TaskType type)
		{
			//if (ExistsLockRecordForId(id, type))    // don't need to check, if doesn't exist delete will just fail
			//{
				var tasktable = type.ToString();
				// return ts.DeleteEntity(tasktable, lock_pk, id).http_response;    // don't need to wait for verification
				return ts.MaybeDeleteEntity(tasktable, lock_pk, id).http_response;
			//}
			//else
				return default(HttpResponse);
		}

		public static bool IsAbandoned(string id, TaskType type)
		{
			var task = Scheduler.FetchTaskForId(id, type);

			if (IsLockedId(id, type) == true && task.status != TaskStatus.running)
				return true;

			var start = task.start;
			var now = DateTime.UtcNow;

			if (now - start <= IntervalFromType(type))
				return false;
			else
				return true;
		}

		public static void EnsureTaskRecord(string id, TaskType type)
		{
			if (Scheduler.ExistsTaskRecordForId(id, type) == false)
			{
				GenUtils.LogMsg("info", "MaybeCreateTaskRecord: creating task for " + id, null);
				Scheduler.InitTaskForId(id, type);
			}
		}

		public static TimeSpan IntervalFromType(TaskType type)
		{
			var ts = TimeSpan.FromHours(24);
			switch (type)
			{
				case TaskType.nonicaltasks:
					ts = Scheduler.nonical_interval;
					break;
				case TaskType.icaltasks:
					ts = Scheduler.ical_interval;
					break;
				case TaskType.regiontasks:
					ts = Scheduler.region_interval;
					break;
				case TaskType.none:
					ts = TimeSpan.FromHours(24);
					GenUtils.PriorityLogMsg("warning", "Scheduler.IntervalFromType", "unexpected TaskType.none");
					break;
			}
			return ts;
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

