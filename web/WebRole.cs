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
using System.Timers;
using System.Collections.Generic;
using System.Web.Routing;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace WebRole
{
	public class WebRole : RoleEntryPoint
	{
		public string procname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
		public int procid = System.Diagnostics.Process.GetCurrentProcess().Id;
		public string domain_name = AppDomain.CurrentDomain.FriendlyName;
		public int thread_id = System.Threading.Thread.CurrentThread.ManagedThreadId;

		public static WebRoleData wrd;

		public static string local_storage_path = RoleEnvironment.GetLocalResource("LocalStorage1").RootPath;
		private TableStorage ts = TableStorage.MakeDefaultTableStorage();

		public override bool OnStart()
		{
			var local_resource = RoleEnvironment.GetLocalResource("LocalStorage1");
			GenUtils.LogMsg("info", "LocalStorage1", local_resource.RootPath);

			Utils.ScheduleTimer(ElmcityApp.reload, minutes: CalendarAggregator.Configurator.webrole_reload_interval_hours * 60, name: "reload", startnow: false);
			Utils.ScheduleTimer(MakeTablesAndCharts, minutes: CalendarAggregator.Configurator.web_make_tables_and_charts_interval_minutes, name: "make_tables_and_charts", startnow: false);

			RoleEnvironment.Changing += RoleEnvironmentChanging;

			var msg = String.Format("WebRole OnStart: {0} {1} {2} {3}", procname, procid, domain_name, thread_id);
			GenUtils.PriorityLogMsg("info", msg, null);

			return base.OnStart();
		}

		public WebRole()
		{
		}

		public override void OnStop()
		{
			var msg = "WebRole: OnStop";
			GenUtils.PriorityLogMsg("info", msg, null);

			base.OnStop();
		}

		public override void Run()
		{
			var msg = "WebRole: Run";
			GenUtils.PriorityLogMsg("info", msg, null);

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
			GenUtils.PriorityLogMsg("exception", "Application_Error", exception.Message);
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
			GenUtils.PriorityLogMsg("Application_End", shutdown_message, shutdown_stack);
		}

		public static void MaybePurgeCache(Object o, ElapsedEventArgs e)
		{
			try
			{
				var cache = new AspNetCache(ElmcityApp.home_controller.HttpContext.Cache);
				ElmcityUtils.CacheUtils.MaybePurgeCache(cache);
			}
			catch (Exception ex)
			{
				GenUtils.PriorityLogMsg("exception", "WebRole.MaybePurgeCache", ex.Message + ex.StackTrace);
			}
		}

		public static void MakeTablesAndCharts(Object o, ElapsedEventArgs e)
		{
			GenUtils.LogMsg("info", "MakeTablesAndCharts", null);
			try
			{
				PythonUtils.RunIronPython(WebRole.local_storage_path, CalendarAggregator.Configurator.charts_and_tables_script_url, new List<string>() { "", "", "" });
			}
			catch (Exception ex)
			{
				GenUtils.PriorityLogMsg("exception", "MonitorAdmin", ex.Message + ex.StackTrace);
			}
		}

	}
}
