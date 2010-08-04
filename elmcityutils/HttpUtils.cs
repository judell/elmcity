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
        // equivalent of curl --head
        public static HttpResponse HeadFetchUrl(Uri url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "HEAD";
            byte[] UTf8ByteArray = new byte[0];
            return DoHttpWebRequest(request, UTf8ByteArray);
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
                GenUtils.LogMsg("exception", "FetchResponseBodyAndETagFromUri", e.Message + e.StackTrace);
            }
        }

        public static HttpResponse FetchUrl(Uri url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            byte[] UTf8ByteArray = new byte[0];
            return DoHttpWebRequest(request, UTf8ByteArray);
        }

        public static HttpResponse FetchUrlNoRedirect(Uri url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.AllowAutoRedirect = false;
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
            try
            {
                string message = "";
                request.ContentLength = 0;

                if (data != null && data.Length > 0)
                {
                    request.ContentLength = data.Length;
                    var bw = new BinaryWriter(request.GetRequestStream());
                    bw.Write(data);
                    bw.Flush();
                    bw.Close();
                }

                var response = (HttpWebResponse)request.GetResponse2();
                var status = response.StatusCode;
                message = response.StatusDescription;
                var headers = new Dictionary<string, string>();
                foreach (string key in response.Headers.Keys)
                    headers[key] = response.Headers[key];

                byte[] return_data = GetResponseData(response);

                response.Close();

                return new HttpResponse(status, message, return_data, headers);
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "DoHttpWebRequest", e.Message + e.StackTrace);
                return default(HttpResponse);
            }
        }

        private static byte[] GetResponseData(HttpWebResponse response)
        {
            Stream response_stream = response.GetResponseStream();
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

        delegate TableStorageResponse HttpRequestLoggerDelegate(System.Web.Mvc.ControllerContext c);

        public static TableStorageResponse LogHttpRequest(System.Web.Mvc.ControllerContext c)
        {
            var logger = new HttpRequestLoggerDelegate(HttpRequestLogger);
            var result = logger.BeginInvoke(c, callback: null, @object: null);
            return logger.EndInvoke(result);
        }

        public static TableStorageResponse HttpRequestLogger(System.Web.Mvc.ControllerContext c)
        {
            string requestor_ip_addr = "none";
            string requestor_dns_name = "none";
            string url = "none";
            try
            {
                requestor_ip_addr = c.HttpContext.Request.UserHostAddress;
                requestor_dns_name = DnsUtils.TryGetHostName(requestor_ip_addr);
                url = c.HttpContext.Request.Url.ToString();
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "HttpRequestLogger", e.Message + e.StackTrace);
            }

            var msg = string.Format("{0} {1} ",
                requestor_dns_name,
                url);
            return GenUtils.LogMsg("info", msg, null);
        }

        /*
        // unused, for pshb
        public static HttpResponse NotifyPubSubHub(string hub_uri_str, string topic_uri)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(new Uri(hub_uri_str));
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            var body = string.Format("hub.mode=publish&hub.url={0}", topic_uri);
            var body_bytes = Encoding.UTF8.GetBytes(body);
            request.ContentLength = body_bytes.Length;
            return DoHttpWebRequest(request, body_bytes);
        }

        // e for pshb
        public static HttpResponse SubscribePubSubHub(string mode, string verify, string hub_uri_str, string topic_uri_str, string callback_uri_str)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(new Uri(hub_uri_str));
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            var body = string.Format("hub.mode={0}&hub.verify={1}&hub.topic={2}&hub.callback={3}", mode, verify, topic_uri_str, callback_uri_str);
            var body_bytes = Encoding.UTF8.GetBytes(body);
            request.ContentLength = body_bytes.Length;
            return DoHttpWebRequest(request, body_bytes);
        }*/


    }
}

