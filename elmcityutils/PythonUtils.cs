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
using IronPython.Hosting;
using Microsoft.Scripting;

namespace ElmcityUtils
{

	[Serializable]
	public class PythonArgs : MarshalByRefObject
	{
		public IronPython.Runtime.List args;
	}

	public static class PythonUtils
	{

		public static void InstallPythonStandardLibrary(string directory, TableStorage ts)
		{
			GenUtils.PriorityLogMsg("info", "installing python standard library", null);
			try
			{

				var zip_url = Configurator.pylib_zip_url;
				FileUtils.UnzipFromUrlToDirectory(zip_url, directory);
				var args = new List<string> { "", "", "" };
				var script_url = Configurator.python_test_script_url;
				var result = RunIronPython(directory, script_url, args);
				GenUtils.LogMsg("info", "result of python standard lib install test", result);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "InstallPythonStandardLibrary", e.Message + e.StackTrace);
			}
		}

		// todo: externalize test url as a setting
		public static void InstallPythonElmcityLibrary(string directory, TableStorage ts)
		{
			{
				GenUtils.LogMsg("info", "installing python elmcity library", null);
				try
				{
					var zip_url = Configurator.elmcity_pylib_zip_url;
					FileUtils.UnzipFromUrlToDirectory(zip_url, directory: directory);
					var args = new List<string> { "http://www.libraryinsight.com/calendar.asp?jx=ea", "", "eastern" };
					var script_url = Configurator.elmcity_python_test_script_url;
					var result = RunIronPython(directory, script_url, args);
					GenUtils.LogMsg("info", "result of python elmcity install test", result, ts);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "InstallPythonElmcityLibrary", e.Message + e.StackTrace);
				}
			}
		}

		public static string RunIronPython(string directory, string str_script_url, List<string> args)
		{
			GenUtils.LogMsg("info", "Utils.run_ironpython: " + str_script_url, args[0] + "," + args[1] + "," + args[2]);
			//var app_domain_name = "ironpython";
			string result = "";
			try
			{
				/*
				string domain_id = app_domain_name;
				var setup = new AppDomainSetup();
				setup.ApplicationName = app_domain_name;
				setup.ApplicationBase = AppDomain.CurrentDomain.BaseDirectory;
				var python_domain = AppDomain.CreateDomain(domain_id, securityInfo: null, info: setup);
				 */
				var options = new Dictionary<string, object>();
				options["LightweightScopes"] = true;
				var python = Python.CreateEngine(options);
				var paths = new List<string>();
				paths.Add(directory + "Lib");        // standard python lib
				paths.Add(directory + "Lib\\site-packages");
				paths.Add(directory + "ElmcityLib"); // Elmcity python lib

				GenUtils.LogMsg("info", "Utils.run_ironpython", String.Join(":", paths.ToArray()));

				python.SetSearchPaths(paths);
				var ipy_args = new IronPython.Runtime.List();
				foreach (var item in args)
					ipy_args.Add(item);
				var s = HttpUtils.FetchUrl(new Uri(str_script_url)).DataAsString();

				try
				{
					var common_script_url = BlobStorage.MakeAzureBlobUri("admin", "common.py");
					var common_script = HttpUtils.FetchUrl(common_script_url).DataAsString();
					s = s.Replace("#include common.py", common_script);
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "RunIronPython: cannot #include common.py", e.Message);
				}

				var source = python.CreateScriptSourceFromString(s, SourceCodeKind.Statements);
				var scope = python.CreateScope();
				var sys = python.GetSysModule();
				//sys.SetVariable("argv", new PythonArgs() { args = ipy_args } );
				sys.SetVariable("argv", args);
				source.Execute(scope);
				try
				{
					result = scope.GetVariable("result").ToString();
				}
				catch
				{
					GenUtils.LogMsg("info", "RunIronPython: " + str_script_url, "no result");
				}
				python.Runtime.Shutdown();
				//AppDomain.Unload(python_domain);
			}
			catch (Exception e)
			{
				result = e.Message.ToString() + e.StackTrace.ToString();
			}
			return result;
		}
	}
}
