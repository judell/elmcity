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
using System.Net;
using NUnit.Framework;

namespace CalendarAggregator
{
	[TestFixture]
	public class SchedulerTest
	{
		private static string testid = "test";
		private static Calinfo test_calinfo = new Calinfo(testid);
		private static TimeSpan interval = new TimeSpan(Configurator.nonical_aggregate_interval_hours, 0, 0);
		private static TaskType test_task_type = TaskType.nonicaltasks;

		[Test]
		public void ExistingTaskExists()
		{
			Scheduler.InitTaskForId(testid, test_task_type);
			var task = Scheduler.FetchTaskForId(testid, test_task_type);
			Assert.That(task.id == testid);
		}

		[Test]
		public void InitTaskForIdYieldsStopped()
		{
			Scheduler.InitTaskForId(testid, test_task_type);
			var task = Scheduler.FetchTaskForId(testid, test_task_type);
			Assert.AreEqual(TaskStatus.stopped, task.status);
		}

		[Test]
		public void StartTaskForIdYieldsAllocated()
		{
			Scheduler.InitTaskForId(testid, test_task_type);
			Scheduler.StartTaskForId(testid, test_task_type);
			var task = Scheduler.FetchTaskForId(testid, test_task_type);
			Assert.AreEqual(TaskStatus.allocated, task.status);
		}

		[Test]
		public void UpdateStartTaskForIdYieldsRunning()
		{
			Scheduler.InitTaskForId(testid, test_task_type);
			Scheduler.StartTaskForId(testid, test_task_type);
			Scheduler.UpdateStartTaskForId(testid, test_task_type);
			var task = Scheduler.FetchTaskForId(testid, test_task_type);
			Assert.AreEqual(TaskStatus.running, task.status);
		}

		[Test]
		public void StopTaskForIdYieldsStopTimeGreaterThanStartTime()
		{
			Scheduler.InitTaskForId(testid, test_task_type);
			Scheduler.StartTaskForId(testid, test_task_type);
			Utils.Wait(3);
			Scheduler.StopTaskForId(testid, test_task_type);
			var task = Scheduler.FetchTaskForId(testid, test_task_type);
			Assert.Greater(task.stop, task.start);
		}

		[Test]
		public void MaybeStartTaskFailsForTooShortInterval()
		{
			Scheduler.InitTaskForId(testid, test_task_type);
			var task = Scheduler.FetchTaskForId(testid, test_task_type);
			var ts = new System.TimeSpan(0, (Configurator.nonical_aggregate_interval_hours * 60) - 10, 0);
			var now = task.start + ts;
			Scheduler.MaybeStartTaskForId(now, test_calinfo, test_task_type);
			task = Scheduler.FetchTaskForId(testid, test_task_type);
			Assert.AreEqual(TaskStatus.stopped, task.status);
		}

		[Test]
		public void MaybeStartTaskSucceedsForLongEnoughInterval()
		{
			Scheduler.InitTaskForId(testid, test_task_type);
			var task = Scheduler.FetchTaskForId(testid, test_task_type);
			var ts = new System.TimeSpan(0, (Configurator.nonical_aggregate_interval_hours * 60) + 10, 0);
			var now = task.start + ts;
			Scheduler.MaybeStartTaskForId(now, test_calinfo, test_task_type);
			task = Scheduler.FetchTaskForId(testid, test_task_type);
			Assert.AreEqual(TaskStatus.allocated, task.status);
		}

		[Test]
		public void MaybeStartTaskUpdatesStartTime()
		{
			Scheduler.InitTaskForId(testid, test_task_type);
			var task = Scheduler.FetchTaskForId(testid, test_task_type);
			var now = DateTime.UtcNow;
			Scheduler.MaybeStartTaskForId(now, test_calinfo, test_task_type);
			task = Scheduler.FetchTaskForId(testid, test_task_type);
			Assert.That(task.start > now);
		}

		[Test]
		public void LockIdSucceedsWhenLockRecordDoesNotExist()
		{
			var http_response = Scheduler.UnlockId(testid, test_task_type);
			http_response = Scheduler.LockId(testid, test_task_type);
			Assert.AreEqual(HttpStatusCode.Created, http_response.status);
			Assert.AreEqual(true, Scheduler.IsLockedId(testid, test_task_type));
		}

		[Test]
		public void LockIdFailsWhenLockRecordExists()
		{
			var http_response = Scheduler.UnlockId(testid, test_task_type);
			http_response = Scheduler.LockId(testid, test_task_type);
			http_response = Scheduler.LockId(testid, test_task_type);
			Assert.AreEqual(HttpStatusCode.Conflict, http_response.status);
		}


		[Test]
		public void IsAbandonedIfLockedAndNotRunning()
		{
			Scheduler.InitTaskForId(testid, test_task_type);
			var task = Scheduler.FetchTaskForId(testid, test_task_type);
			//task.running = false;
			task.status = TaskStatus.stopped;
			Scheduler.StoreTaskForId(task, testid, test_task_type);
			Scheduler.LockId(testid, test_task_type);
			Assert.AreEqual(true, Scheduler.IsAbandoned(testid, test_task_type));
		}

		[Test]
		public void IsAbandonedIfBeyondInterval()
		{

			Scheduler.InitTaskForId(testid, test_task_type);
			var task = Scheduler.FetchTaskForId(testid, test_task_type);
			var more_than_interval = new System.TimeSpan(0, (Configurator.nonical_aggregate_interval_hours * 60) + 60, 0);
			task.start = DateTime.UtcNow - more_than_interval;  // started more than 8hrs ago
			Scheduler.StoreTaskForId(task, testid, test_task_type);
			Assert.AreEqual(true, Scheduler.IsAbandoned(testid, test_task_type));
		}

		[Test]
		public void IsNotAbandonedIfWithinInterval()
		{
			Scheduler.InitTaskForId(testid, test_task_type);
			var task = Scheduler.FetchTaskForId(testid, test_task_type);
			var less_than_interval = new System.TimeSpan(0, (Configurator.nonical_aggregate_interval_hours * 60) - 60, 0);
			task.start = DateTime.UtcNow - less_than_interval;
			Scheduler.StoreTaskForId(task, testid, test_task_type);
			Assert.AreEqual(false, Scheduler.IsAbandoned(testid, test_task_type));
		}
	}
}