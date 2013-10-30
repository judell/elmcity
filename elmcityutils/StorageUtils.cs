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
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Xml.Linq;

namespace ElmcityUtils
{
	// common to table and blob apis,
	// using "SharedKeyLite" for simplicity
	public static class StorageUtils
	{

		public enum services { blob, table };

		public const string API_VERSION_KEY = "x-ms-version";
		public const string API_VERSION_VAL = "2009-09-19";

		const string NEW_LINE = "\x0A";
		const string PREFIX_STORAGE = "x-ms-";
		const string TIME_FORMAT = "ddd, dd MMM yyyy HH:mm:ss";

		public const int max_tries = 30;
		public static TimeSpan timeout_secs = TimeSpan.FromSeconds(100);
		public const int wait_secs = 3;

		public static XNamespace atom_namespace = "http://www.w3.org/2005/Atom";

		public static void AddVersionHeader(Hashtable headers)
		{
			headers.Add(StorageUtils.API_VERSION_KEY, StorageUtils.API_VERSION_VAL);
		}

		public static void AddDateHeader(Hashtable headers)
		{
			DateTime gmt = DateTime.Now.ToUniversalTime();
			string date_header_key = PREFIX_STORAGE + "date";
			string date_header_value = gmt.ToString(TIME_FORMAT) + " GMT";
			headers.Add(date_header_key, date_header_value);
		}

		// see http://msdn.microsoft.com/en-us/library/dd179428.aspx
		public static string MakeSharedKeyLiteHeader(services service_type, string azure_storage_account, string azure_b64_key, string http_method, string path, string query_string, string content_type, Hashtable headers)
		{
			string string_to_sign = "";

			switch (service_type)
			{
				case services.blob:
					string_to_sign += http_method + NEW_LINE;
					string_to_sign += NEW_LINE;
					if (content_type != null)
						string_to_sign += content_type;
					string_to_sign += NEW_LINE + NEW_LINE;
					var ms_headers = new List<string>();
					foreach (string key in headers.Keys)
						if (key.StartsWith(PREFIX_STORAGE, StringComparison.Ordinal))
							ms_headers.Add(key);
					ms_headers.Sort();
					foreach (string header_key in ms_headers)
						string_to_sign += string.Format("{0}:{1}{2}", header_key, headers[header_key], NEW_LINE);
					break;

				case services.table:
					string_to_sign += headers[PREFIX_STORAGE + "date"];
					string_to_sign += NEW_LINE;
					break;
			}

			//string_to_sign += "/" + azure_storage_account + path;

			string_to_sign += "/" + azure_storage_account + Uri.EscapeUriString(path);

			//if (query_string != null && query_string.StartsWith("?comp"))
			//	string_to_sign += query_string;

			if (!String.IsNullOrEmpty(query_string) && query_string.Contains("comp="))
			{
				var qscoll = HttpUtility.ParseQueryString(query_string);
				string_to_sign += "?comp=" + qscoll["comp"];
			}

			return SignAuthString(azure_b64_key, string_to_sign);
		}

		private static string SignAuthString(string azure_b64_key, string string_to_sign)
		{
			Encoding enc = Encoding.UTF8;
			byte[] utf8_string_to_sign = enc.GetBytes(string_to_sign);
			HMACSHA256 hmacsha256 = new HMACSHA256(Convert.FromBase64String(azure_b64_key));
			byte[] bytearray = hmacsha256.ComputeHash(utf8_string_to_sign);
			string bytearray_as_str = System.Convert.ToBase64String(bytearray);
			return bytearray_as_str;
		}

		public static HttpResponse DoStorageRequest(string method, Hashtable headers, byte[] data, string content_type, Uri uri)
		{
			System.Threading.Thread.Sleep(500);
			try
			{
				HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
				request.Method = method;

				if (content_type != null)
					request.ContentType = content_type;

				if (data == null || data.Length == 0 )  
					request.ContentLength = 0;

				foreach (string key in headers.Keys)
					request.Headers.Add(key, (string)headers[key]);

				var response = HttpUtils.DoHttpWebRequest(request, data);

				return response;
			}
			catch (Exception e)
			{
				 GenUtils.PriorityLogMsg("exception", "DoStorageRequest: " +  uri.ToString() , e.Message + e.InnerException.Message + e.StackTrace);
				 return new HttpResponse(HttpStatusCode.ServiceUnavailable, "PossibleAzureTimeout", null, new Dictionary<string, string>());
			}
		}

	}
}
