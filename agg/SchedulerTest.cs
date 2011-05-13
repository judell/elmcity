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
using System.Net;
using NUnit.Framework;

namespace CalendarAggregator
{
	[TestFixture]
	public class SchedulerTest
	{
		private static string testid = "test";
		private static Calinfo test_calinfo = new Calinfo(testid);
		private static int delay_milli = 1000;
		private static TimeSpan interval = new TimeSpan(Configurator.where_aggregate_interval_hours, 0, 0);

		[Test]
		public void ExistingTaskExists()
		{
			var task = Scheduler.FetchTaskForId(testid);
			Assert.That(task.id == testid);
		}

		[Test]
		public void InitTaskForIdYieldsRunningFalse()
		{
			Scheduler.InitTaskForId(testid);
			var task = Scheduler.FetchTaskForId(testid);
			Assert.AreEqual(false, task.running);
		}

		[Test]
		public void StartTaskForIdYieldsRunningTrue()
		{
			Scheduler.InitTaskForId(testid);
			Scheduler.StartTaskForId(testid);
			var task = Scheduler.FetchTaskForId(testid);
			Assert.AreEqual(true, task.running);
		}

		[Test]
		public void StopTaskForIdYieldsStopTimeGreaterThanStartTime()
		{
			Scheduler.InitTaskForId(testid);
			Scheduler.StartTaskForId(testid);
			System.Threading.Thread.Sleep(delay_milli);
			Scheduler.StopTaskForId(testid);
			var task = Scheduler.FetchTaskForId(testid);
			Assert.Greater(task.stop, task.start);
		}

		[Test]
		public void MaybeStartTaskFailsForTooShortInterval()
		{
			Scheduler.InitTaskForId(testid);
			var task = Scheduler.FetchTaskForId(testid);
			var ts = new System.TimeSpan(0, (Configurator.where_aggregate_interval_hours * 60) - 10, 0);
			var now = task.start + ts;
			Scheduler.MaybeStartTaskForId(now, test_calinfo);
			task = Scheduler.FetchTaskForId(testid);
			Assert.AreEqual(task.running, false);
		}

		[Test]
		public void MaybeStartTaskSucceedsForLongEnoughInterval()
		{
			Scheduler.InitTaskForId(testid);
			var task = Scheduler.FetchTaskForId(testid);
			var ts = new System.TimeSpan(0, (Configurator.where_aggregate_interval_hours * 60) + 10, 0);
			var now = task.stop + ts;
			Scheduler.MaybeStartTaskForId(now, test_calinfo);
			task = Scheduler.FetchTaskForId(testid);
			Assert.AreEqual(task.running, true);
		}

		[Test]
		public void MaybeStartTaskUpdatesStartTime()
		{
			Scheduler.InitTaskForId(testid);
			var task = Scheduler.FetchTaskForId(testid);
			var ts = new System.TimeSpan(0, (Configurator.where_aggregate_interval_hours * 60) + 10, 0);
			var now = task.start + ts;
			Scheduler.MaybeStartTaskForId(now, test_calinfo);
			Assert.AreEqual(task.start + ts, now);
		}

		[Test]
		public void LockIdSucceedsWhenLockRecordDoesNotExist()
		{
			var ts_response = Scheduler.UnlockId(testid);
			ts_response = Scheduler.LockId(testid);
			Assert.AreEqual(HttpStatusCode.Created, ts_response.http_response.status);
			Assert.AreEqual(true, Scheduler.IsLockedId(testid));
		}

		[Test]
		public void LockIdFailsWhenLockRecordExists()
		{
			var ts_response = Scheduler.UnlockId(testid);
			ts_response = Scheduler.LockId(testid);
			ts_response = Scheduler.LockId(testid);
			Assert.AreEqual(HttpStatusCode.Conflict, ts_response.http_response.status);
		}


		[Test]
		public void IsAbandonedIfLockedAndNotRunning()
		{
			Scheduler.InitTaskForId(testid);
			var task = Scheduler.FetchTaskForId(testid);
			task.running = false;
			Scheduler.StoreTaskForId(task, testid);
			Scheduler.LockId(testid);
			Assert.AreEqual(true, Scheduler.IsAbandoned(testid, interval));
		}

		[Test]
		public void IsAbandonedIfBeyondInterval()
		{

			Scheduler.InitTaskForId(testid);
			var task = Scheduler.FetchTaskForId(testid);
			var more_than_interval = new System.TimeSpan(0, (Configurator.where_aggregate_interval_hours * 60) + 60, 0);
			task.start = DateTime.Now.ToUniversalTime() - more_than_interval;  // started more than 8hrs ago
			Scheduler.StoreTaskForId(task, testid);
			Assert.AreEqual(true, Scheduler.IsAbandoned(testid, interval));
		}

		[Test]
		public void IsNotAbandonedIfWithinInterval()
		{
			Scheduler.InitTaskForId(testid);
			var task = Scheduler.FetchTaskForId(testid);
			var less_than_interval = new System.TimeSpan(0, (Configurator.where_aggregate_interval_hours * 60) - 60, 0);
			task.start = DateTime.Now.ToUniversalTime() - less_than_interval;
			Scheduler.StoreTaskForId(task, testid);
			Assert.AreEqual(false, Scheduler.IsAbandoned(testid, interval));
		}
	}
}