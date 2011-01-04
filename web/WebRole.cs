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
using System.Web;

namespace WebRole
{
	public class WebRole : RoleEntryPoint
	{
		public static string local_storage_path;

		public override bool OnStart()
		{
			local_storage_path = RoleEnvironment.GetLocalResource("LocalStorage1").RootPath;

			var config = DiagnosticMonitor.GetDefaultInitialConfiguration();

			//Counters.AddCountersToConfig(config, excluded_specifier_prefix: null);
			//config.PerformanceCounters.ScheduledTransferPeriod = TimeSpan.FromMinutes(ElmcityUtils.Configurator.default_counter_transfer_period);

			config.Logs.ScheduledTransferLogLevelFilter = LogLevel.Verbose;
			config.Logs.ScheduledTransferPeriod = TimeSpan.FromMinutes(CalendarAggregator.Configurator.default_log_transfer_minutes);

			config.WindowsEventLog.DataSources.Add("System!*");
			config.WindowsEventLog.ScheduledTransferPeriod = TimeSpan.FromMinutes(CalendarAggregator.Configurator.default_log_transfer_minutes);

			config.Directories.ScheduledTransferPeriod = TimeSpan.FromMinutes(CalendarAggregator.Configurator.default_file_transfer_minutes);

			DiagnosticMonitor.Start("DiagnosticsConnectionString", config);

			RoleEnvironment.Changing += RoleEnvironmentChanging;

			GenUtils.LogMsg("info", "Webrole: OnStart", null);

			return base.OnStart();
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
			Exception exception = HttpContext.Current.Server.GetLastError();
			// currently unused, see http://azuremiscellany.blogspot.com/2010/05/web-role-crash-dumps.html for interesting possibility
		}

	}
}
