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
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Web.Mvc;

namespace ElmcityUtils
{
	// turn what would otherwise be exceptions into normal responses
	// see http://blog.jonudell.net/2009/01/22/unifying-http-success-and-failure-in-net
	public static class HttpWebRequestExtensions
	{
		public static HttpWebResponse GetResponse2(this WebRequest request)
		{
			HttpWebResponse response;

			try
			{
				response = request.GetResponse() as HttpWebResponse;
			}
			catch (WebException ex)
			{
				response = ex.Response as HttpWebResponse;
			}
			catch (Exception e)
			{
				var msg = "GetResponse2" + " " + e.Message;
				GenUtils.PriorityLogMsg("exception", msg, e.StackTrace);
				throw new Exception(msg);
			}

			return response;
		}
	}

	// simple, high-level encapsulation of basic stuff needed in an http response
	public class HttpResponse
	{
		public HttpStatusCode status;
		public string message;
		public byte[] bytes;
		public Dictionary<string, string> headers;

		public HttpResponse(HttpStatusCode status, string message, byte[] bytes, Dictionary<string, string> headers)
		{
			this.status = status;
			this.message = message;
			this.bytes = bytes;
			this.headers = headers;
		}

		public string DataAsString()
		{
			return Encoding.UTF8.GetString(this.bytes);
		}
	}

	// basic http-related operations
	public static class HttpUtils
	{
		public static string elmcity_user_agent = "elmcity";

		public const int wait_secs = 3;
		public const int retries = 3;
		public const int timeout = 10;

		// equivalent of curl --head
		public static HttpResponse HeadFetchUrl(Uri url)
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.Method = "HEAD";
			byte[] UTf8ByteArray = new byte[0];
			return DoHttpWebRequest(request, UTf8ByteArray);
		}

		public static HttpResponse PostUrl(Uri uri, string query)
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
			request.Method = "POST";
			request.ContentType = "application/x-www-form-urlencoded";
			request.ContentLength = query.Length;
			var bytes = Encoding.UTF8.GetBytes(query);
			return DoHttpWebRequest(request, bytes);
		}

		// return header if it appears in the request context 
		public static String MaybeExtractHeaderFromRequestContext(string header_name, ControllerContext context)
		{
			var request_headers = context.HttpContext.Request.Headers;
			if (request_headers.AllKeys.Contains(header_name))
				return request_headers[header_name];
			else
				return null;
		}

		// return header if it appears in the response to an HTTP HEAD
		public static String MaybeGetHeaderFromUriHead(string header, Uri uri)
		{
			var head_response = HttpUtils.HeadFetchUrl(uri);
			if (head_response.headers.ContainsKey(header))
				return head_response.headers[header];
			else
				return null;
		}


		public static void FetchResponseBodyAndETagFromUri(Uri uri, Dictionary<string, object> dict)
		{
			try
			{
				var response = FetchUrl(uri);
				dict["response_body"] = (byte[])response.bytes;
				dict["ETag"] = response.headers["ETag"];
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "FetchResponseBodyAndETagFromUri: " + uri.ToString(), e.Message);
			}
		}

		public static HttpResponse FetchUrl(Uri url)
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			byte[] UTf8ByteArray = new byte[0];
			return DoHttpWebRequest(request, UTf8ByteArray);
		}

		public static HttpResponse FetchUrl(string url)
		{
			var uri = new Uri(url);
			return FetchUrl(uri);
		}


		public static HttpResponse SlowFetchUrl(Uri url, int delay_secs)
		{
			System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));
			return FetchUrl(url);
		}

		public static HttpResponse FetchUrlNoRedirect(Uri url)
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.AllowAutoRedirect = false;
			byte[] UTf8ByteArray = new byte[0];
			return DoHttpWebRequest(request, UTf8ByteArray);
		}

		public static HttpResponse FetchUrlAsUserAgent(Uri url, string user_agent)
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.UserAgent = user_agent;
			byte[] UTf8ByteArray = new byte[0];
			return DoHttpWebRequest(request, UTf8ByteArray);
		}

		public static HttpResponse FetchUrlNoCache(Uri url)
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.Headers.Add("Cache-Control", "no-cache");
			byte[] UTf8ByteArray = new byte[0];
			return DoHttpWebRequest(request, UTf8ByteArray);
		}

		public static HttpResponse DoAuthorizedHttpRequest(HttpWebRequest request, string user, string pass, byte[] data)
		{
			byte[] auth = Encoding.UTF8.GetBytes(string.Format("{0}:{1}", user, pass));
			string authstr = Convert.ToBase64String(auth);
			request.Headers.Add("Authorization", "Basic " + authstr);
			return DoHttpWebRequest(request, data);
		}

		public static HttpResponse DoCertifiedServiceManagementRequest(byte[] cert, HttpWebRequest request, byte[] data)
		{
			X509Certificate2 x509 = new X509Certificate2();
			x509.Import(cert);
			request.ClientCertificates.Add(x509);
			request.Headers.Add("x-ms-version", "2009-10-01");
			request.ContentType = "text/xml";
			return DoHttpWebRequest(request, data);
		}

		public static HttpResponse DoHttpWebRequest(HttpWebRequest request, byte[] data)
		{
			if (request.UserAgent == null)
				request.UserAgent = elmcity_user_agent;

			try
			{
				if (data != null && data.Length > 0)
				{
					request.ContentLength = data.Length;
					var bw = new BinaryWriter(request.GetRequestStream());
					bw.Write(data);
					bw.Flush();
					bw.Close();
				}
			}
			catch (Exception ex_write)
			{
				GenUtils.PriorityLogMsg("exception", "DoHttpWebRequest: writing data", ex_write.Message + ex_write.InnerException.Message + ex_write.StackTrace);
				return new HttpResponse(HttpStatusCode.ServiceUnavailable, "DoHttpWebRequest cannot write", null, new Dictionary<string, string>());
			}

			try
			{
				var response = (HttpWebResponse)request.GetResponse2();

				var status = response.StatusCode;

				var message = response.StatusDescription;

				var headers = new Dictionary<string, string>();
				foreach (string key in response.Headers.Keys)
					headers[key] = response.Headers[key];

				byte[] return_data = GetResponseData(response);

				response.Close();
	
				return new HttpResponse(status, message, return_data, headers);
			}
			catch (Exception ex_read)
			{
				GenUtils.PriorityLogMsg("exception", "DoHttpWebRequest: reading data", ex_read.Message + ex_read.InnerException.Message + ex_read.StackTrace);
				return new HttpResponse(HttpStatusCode.ServiceUnavailable, "DoHttpWebRequest cannot read", null, new Dictionary<string, string>());
			}
		}

		private static byte[] GetResponseData(HttpWebResponse response)
		{
			try
			{
				//NUnit.Framework.Assert.IsNotNull(response);
				Stream response_stream = response.GetResponseStream();
				if (response_stream.CanRead == false)
					return new byte[0];
				//NUnit.Framework.Assert.IsNotNull(response_stream);
				Encoding encoding;
				var charset = response.CharacterSet ?? "";
				switch (charset.ToLower())
				{
					case "utf-8":
						encoding = Encoding.UTF8;
						break;
					default:
						encoding = Encoding.Default;
						break;
				}
				if (response.ContentLength > 0)
				{
					var reader = new BinaryReader(response_stream);
					return reader.ReadBytes((int)response.ContentLength);
				}
				else // empty or unspecified, read zero or more bytes
				{
					var reader = new StreamReader(response_stream);
					return encoding.GetBytes(reader.ReadToEnd());
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "HttpUtils.GetResponseData", e.Message + e.StackTrace);
				throw (e);
			}
		}


		// see http://www.cookcomputing.com/blog/archives/000556.html
		public static bool SetAllowUnsafeHeaderParsing()
		{
			//Get the assembly that contains the internal class
			Assembly aNetAssembly = Assembly.GetAssembly(
			  typeof(System.Net.Configuration.SettingsSection));
			if (aNetAssembly != null)
			{
				//Use the assembly in order to get the internal type for 
				// the internal class
				Type aSettingsType = aNetAssembly.GetType(
				  "System.Net.Configuration.SettingsSectionInternal");
				if (aSettingsType != null)
				{
					//Use the internal static property to get an instance 
					// of the internal settings class. If the static instance 
					// isn't created allready the property will create it for us.
					object anInstance = aSettingsType.InvokeMember("Section",
					  BindingFlags.Static | BindingFlags.GetProperty
					  | BindingFlags.NonPublic, null, null, new object[] { });
					if (anInstance != null)
					{
						//Locate the private bool field that tells the 
						// framework is unsafe header parsing should be 
						// allowed or not
						FieldInfo aUseUnsafeHeaderParsing = aSettingsType.GetField(
						  "useUnsafeHeaderParsing",
						  BindingFlags.NonPublic | BindingFlags.Instance);
						if (aUseUnsafeHeaderParsing != null)
						{
							aUseUnsafeHeaderParsing.SetValue(anInstance, true);
							return true;
						}
					}
				}
			}
			return false;
		}

		// used to create a response etag from a response body
		public static string GetMd5Hash(byte[] bytes)
		{
			MD5 md5 = new MD5CryptoServiceProvider();
			byte[] result = md5.ComputeHash(bytes);
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < result.Length; i++)
				sb.Append(result[i].ToString("X2"));
			return sb.ToString();
		}

		public static void Wait(int seconds)
		{
			System.Threading.Thread.Sleep(seconds * 1000);
		}

		public static LogMsg MakeHttpLogMsg(System.Web.Mvc.ControllerContext c)
		{
			string requestor_ip_addr;
			string url;

			try
			{
				requestor_ip_addr = c.HttpContext.Request.UserHostAddress;
				url = c.HttpContext.Request.Url.ToString();
			}
			catch
			{
				requestor_ip_addr = "ip_unavailable";
				url = "url_unavailable";
			}

			var msg = new LogMsg("request", requestor_ip_addr, url);
			return msg;
		}

		public static bool CompletedIfStatusEqualsExpected(HttpResponse r, Object o)
		{
			return r.status == (HttpStatusCode) o;
		}

		public static bool CompletedIfStatusNotServiceUnavailable(HttpResponse r, Object o)
		{
			return r.status != HttpStatusCode.ServiceUnavailable;
		}

		public static HttpResponse RetryHttpRequestExpectingStatus(HttpWebRequest request, HttpStatusCode expected_status, byte[] data, int wait_secs, int max_tries, TimeSpan timeout_secs)
		{
			try
			{
				return GenUtils.Actions.Retry<HttpResponse>(
					delegate()
					{
						try
						{
							return DoHttpWebRequest(request, data);
						}
						catch (Exception e)
						{
							string msg;
							if (e.GetType() != typeof(NullReferenceException))
							{
								msg = "RetryHttpRequest " + e.ToString() + ": " + request.RequestUri;
								GenUtils.LogMsg("warning", msg, null);
							}
							else
								msg = request.RequestUri.ToString();
							return new HttpResponse(HttpStatusCode.NotFound, msg, null, null);
						}
					},
					CompletedIfStatusEqualsExpected,
					completed_delegate_object: expected_status,
					wait_secs: wait_secs,
					max_tries: max_tries,
					timeout_secs: timeout_secs);
			}
			catch (Exception e)
			{
				var msg = "RetryHttpRequest " + e.ToString() + ": " + request.RequestUri;
				GenUtils.LogMsg("exception", msg, null);
				return new HttpResponse(HttpStatusCode.NotFound, msg, null, null);
			}
		}

		public static HttpResponse RetryHttpRequestExpectingStatus(string url, HttpStatusCode expected_status)
		{
			var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
			return HttpUtils.RetryHttpRequestExpectingStatus(request, HttpStatusCode.OK, null, 1, 3, TimeSpan.FromSeconds(10));
		}

		public static HttpResponse RetryStorageRequestExpectingServiceAvailable(string method, Hashtable headers, byte[] data, string content_type, Uri uri, int wait_secs, int max_tries, TimeSpan timeout_secs)
		{
			try
			{
				return GenUtils.Actions.Retry<HttpResponse>(
					delegate()
					{
						try
						{
							return StorageUtils.DoStorageRequest(method, headers, data, content_type, uri);
						}
						catch // (Exception e)
						{
							//GenUtils.PriorityLogMsg("exception", "RetryStorageRequest: " + uri, e.Message + e.StackTrace);
							throw new Exception("RetryStorageRequestException");
						}
					},
					CompletedIfStatusNotServiceUnavailable,
					completed_delegate_object: null,
					wait_secs: wait_secs,
					max_tries: max_tries,
					timeout_secs: timeout_secs);
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "RetryStorageRequest: " + uri, e.Message + e.StackTrace);
				return new HttpResponse(HttpStatusCode.ServiceUnavailable, uri.ToString(), null, new Dictionary<string, string>());
			}
		}

		public static HttpResponse RetryStorageRequestExpectingServiceAvailable(string method, Hashtable headers, byte[] data, string content_type, Uri uri)
		{
			try
			{
				return RetryStorageRequestExpectingServiceAvailable(method, headers, data, content_type, uri, 60, 3, TimeSpan.FromSeconds(500));
			}
			catch (Exception e)
			{
				GenUtils.LogMsg("exception", "RetryStorageRequest: " + uri, e.Message + e.StackTrace);
				return new HttpResponse(HttpStatusCode.ServiceUnavailable, uri.ToString(), null, new Dictionary<string, string>());
			}			

		}

	}
}

