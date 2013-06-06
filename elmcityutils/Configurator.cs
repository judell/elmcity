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
using System.Xml.Linq;

namespace ElmcityUtils
{
	public static class Configurator
	{
		// -- service-wide values
		public const string azure_b64_secret = ";
		public const string appdomain = "elmcity.cloudapp.net";
		public const string azure_compute_account = "elmcity";
		public const string azure_storage_account = "elmcity";
		public static string azure_cdn_blobhost { get { return _azure_cdn_blobhost; } }
		private static string _azure_cdn_blobhost = "http://az26683.vo.msecnd.net";
		// ---


		public const string azure_blob_domain = "blob.core.windows.net";
		public const string azure_table_domain = "table.core.windows.net";

		public static string azure_blobhost { get { return _azure_blobhost; } }
		private static string _azure_blobhost = string.Format("http://{0}.{1}", azure_storage_account, azure_blob_domain);



		public const string azure_log_table = "log";
		public const string azure_priority_log_table = "prioritylog";

		public static XNamespace azure_ns { get { return _azure_ns; } }
		private static XNamespace _azure_ns = "http://schemas.microsoft.com/windowsazure";

		public static XNamespace no_ns { get { return _no_ns; } }
		private static XNamespace _no_ns = "";

		public const int default_counter_sample_rate = 60; // seconds

		public const int max_cache_hours = 12;                                            // an outer limit, mostly won't be reached
		public const int cache_control_max_age = 60 * 60 * max_cache_hours;               // for http cache-control header
		public static TimeSpan cache_sliding_expiration = new TimeSpan(max_cache_hours, 0, 0);    // for iis cache sliding expiration

		// standard python library
		public static Uri pylib_zip_url = new Uri(String.Format("{0}/admin/python_library.zip", azure_blobhost));
		public static string python_test_script_url = String.Format("{0}/admin/basic.py", azure_blobhost);

		// elmcity-specific python library
		public static Uri elmcity_pylib_zip_url = new Uri(String.Format("{0}/admin/ElmcityLib.zip", azure_blobhost));
		public static string elmcity_python_test_script_url = String.Format("{0}/admin/fusecal.py", azure_blobhost);

		public static string test_results_tablename = "testresults";

	}

}

