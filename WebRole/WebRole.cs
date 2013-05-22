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
using System.Linq;
using System.Reflection;
using System.Timers;
using System.Web;
using CalendarAggregator;
using ElmcityUtils;
using Microsoft.WindowsAzure.ServiceRuntime;

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
        public static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();

        public override bool OnStart()
        {
            var local_resource = RoleEnvironment.GetLocalResource("LocalStorage1");
            GenUtils.LogMsg("info", "LocalStorage1", local_resource.RootPath);

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

    }
}


