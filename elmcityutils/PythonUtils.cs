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
    public static class PythonUtils
    {

        public static void InstallPythonStandardLibrary(TableStorage ts)
        {
            GenUtils.LogMsg("info", "installing python standard library", null, ts);
            try
            {
                var zip_url = Configurator.pylib_zip_url;
                FileUtils.UnzipFromUrlToCurrentDirectory(zip_url, existing_dir: "Lib");
                var args = new List<string> { "", "", "" };
                var script_url = Configurator.python_test_script_url;
                var result = RunIronPython(script_url, args);
                GenUtils.LogMsg("info", "result of python standard lib install test", result, ts);
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "InstallPythonStandardLibrary", e.Message + e.StackTrace, ts);
            }
        }

        // todo: externalize test url as a setting
        public static void InstallPythonElmcityLibrary(TableStorage ts)
        {
            {
                GenUtils.LogMsg("info", "installing python elmcity library", null, ts);
                try
                {
                    var zip_url = Configurator.elmcity_pylib_zip_url;
                    FileUtils.UnzipFromUrlToCurrentDirectory(zip_url, existing_dir: "ElmcityLib");
                    var args = new List<string> { "http://www.libraryinsight.com/calendar.asp?jx=ea", "", "eastern" };
                    var script_url = Configurator.elmcity_python_test_script_url;
                    var result = RunIronPython(script_url, args);
                    GenUtils.LogMsg("info", "result of python elmcity install test", result, ts);
                }
                catch (Exception e)
                {
                    GenUtils.LogMsg("exception", "InstallPythonElmcityLibrary", e.Message + e.StackTrace, ts);
                }
            }
        }

        public static string RunIronPython(string str_script_url, List<string> args)
        {
            GenUtils.LogMsg("info", "Utils.run_ironpython: " + str_script_url, args[0] + "," + args[1] + "," + args[2]);
            var result = "";
            try
            {
                var python = Python.CreateEngine();
                var paths = new List<string>();
                paths.Add("./Lib");        // standard python lib
                paths.Add("./Lib/site-packages");
                paths.Add("./ElmcityLib"); // Elmcity python lib
                python.SetSearchPaths(paths);
                var ipy_args = new IronPython.Runtime.List();
                foreach (var item in args)
                    ipy_args.Add(item);
                var s = HttpUtils.FetchUrl(new Uri(str_script_url)).DataAsString();
                var source = python.CreateScriptSourceFromString(s, SourceCodeKind.Statements);
                var scope = python.CreateScope();
                var sys = python.GetSysModule();
                sys.SetVariable("argv", ipy_args);
                source.Execute(scope);
                result = scope.GetVariable("result").ToString();
                python.Runtime.Shutdown();
            }
            catch (Exception e)
            {
                result = e.Message.ToString() + e.StackTrace.ToString();
            }
            return result;
        }
    }
}
