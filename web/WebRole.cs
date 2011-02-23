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
using System.Linq;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using ElmcityUtils;
using CalendarAggregator;
using System.Web;
using System.Reflection;

namespace WebRole
{
	public class WebRole : RoleEntryPoint
	{
		public static string local_storage_path = RoleEnvironment.GetLocalResource("LocalStorage1").RootPath;
		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();

		public override bool OnStart()
		{
			/*
			var config = DiagnosticMonitor.GetDefaultInitialConfiguration();

			//Counters.AddCountersToConfig(config, excluded_specifier_prefix: null);
			//config.PerformanceCounters.ScheduledTransferPeriod = TimeSpan.FromMinutes(ElmcityUtils.Configurator.default_counter_transfer_period);

			config.Logs.ScheduledTransferLogLevelFilter = LogLevel.Verbose;
			config.Logs.ScheduledTransferPeriod = TimeSpan.FromMinutes(CalendarAggregator.Configurator.default_log_transfer_minutes);

			config.WindowsEventLog.DataSources.Add("System!*");
			config.WindowsEventLog.ScheduledTransferPeriod = TimeSpan.FromMinutes(CalendarAggregator.Configurator.default_log_transfer_minutes);

			config.Directories.ScheduledTransferPeriod = TimeSpan.FromMinutes(CalendarAggregator.Configurator.default_file_transfer_minutes);

			DiagnosticMonitor.Start("DiagnosticsConnectionString", config);
			 */

			RoleEnvironment.Changing += RoleEnvironmentChanging;

			var msg = "WebRole: OnStart";
			GenUtils.LogMsg("info", msg, null);
			GenUtils.PriorityLogMsg("info", msg, null, ts);

			return base.OnStart();
		}

		public override void OnStop()
		{
			var msg = "WebRole: OnStop";
			GenUtils.LogMsg("info", msg, null);
			GenUtils.PriorityLogMsg("info", msg, null, ts);

			var snapshot = Counters.MakeSnapshot(Counters.GetCounters());
			ElmcityApp.monitor.StoreSnapshot(snapshot);

			base.OnStop();
		}

		public override void Run()
		{
			var msg = "WebRole: Run";
			GenUtils.LogMsg("info", msg, null);
			GenUtils.PriorityLogMsg("info", msg, null, ts);

			base.Run();
		}

		private void RoleEnvironmentChanging(object sender, RoleEnvironmentChangingEventArgs e)
		{
			// If a configuration setting is changing
			if (e.Changes.Any(change => change is RoleEnvironmentConfigurationSettingChange))
			{
				// Set e.Cancel to true to restart this role instance
				e.Cancel = true;
			}
		}

		protected void Application_Error(object sender, EventArgs e)
		{
			var ts = TableStorage.MakeDefaultTableStorage();
			Exception exception = HttpContext.Current.Server.GetLastError();
			GenUtils.PriorityLogMsg("exception", "Application_Error", exception.Message, ts);
			TwitterApi.SendTwitterDirectMessage(CalendarAggregator.Configurator.delicious_master_account, exception.Message);
			// see http://azuremiscellany.blogspot.com/2010/05/web-role-crash-dumps.html for interesting possibility
		}

		protected void Application_End(object sender, EventArgs e)
		{
			HttpRuntime runtime = (HttpRuntime)typeof(System.Web.HttpRuntime).InvokeMember("_theRuntime",
										  BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.GetField,
										  null,
										  null,
										  null);

			if (runtime == null)
				return;

			string shutdown_message = (string)runtime.GetType().InvokeMember("_shutDownMessage",
										  BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetField,
										  null,
										  runtime,
										  null);

			string shutdown_stack = (string)runtime.GetType().InvokeMember("_shutDownStack",
										   BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetField,
										   null,
										   runtime,
										   null);

			var ts = TableStorage.MakeDefaultTableStorage();
			GenUtils.PriorityLogMsg("Application_End", shutdown_message, shutdown_stack, ts);
			TwitterApi.SendTwitterDirectMessage(CalendarAggregator.Configurator.delicious_master_account, shutdown_message);
		}

	}
}
