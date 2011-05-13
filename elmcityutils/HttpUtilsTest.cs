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
using NUnit.Framework;

namespace ElmcityUtils
{
	public class HttpUtilsTest
	{

		private static Uri view_uri;

		public HttpUtilsTest()
		{
			var r = new Random();
			var count = r.Next(200);
			view_uri = MakeViewUri("services/elmcity/html", String.Format("view={0}", count));
		}

		[Test]
		public void HttpHeadIsSuccessful()
		{
			var r = HttpUtils.HeadFetchUrl(new Uri("http://elmcity.cloudapp.net"));
			Assert.That(r.bytes.Length == 0);
			Assert.That(r.headers.ContainsKey("X-AspNetMvc-Version"));
		}

		[Test]
		public void FetchResponseBodyAndETagFromBlobUriIsSuccessful()
		{
			var dict_obj = new Dictionary<string, object>();
			HttpUtils.FetchResponseBodyAndETagFromUri(view_uri, dict_obj);
			Assert.That(dict_obj.ContainsKey("response_body"));
			Assert.That(dict_obj.ContainsKey("ETag"));
			var encapsulated_response_bytes = (byte[])dict_obj["response_body"];
			var fetched_response_bytes = HttpUtils.FetchUrl(view_uri).bytes;
			Assert.That(encapsulated_response_bytes.Length == fetched_response_bytes.Length);
		}

		[Test]
		public void ViewETagMatchesExpected()
		{
			var dict_obj = new Dictionary<string, object>();
			HttpUtils.FetchResponseBodyAndETagFromUri(view_uri, dict_obj);
			var body = (byte[])dict_obj["response_body"];
			var etag = HttpUtils.GetMd5Hash(body);
			Assert.That(etag == (string)dict_obj["ETag"]);
		}

		private Uri MakeViewUri(string path, string query)
		{
			return new Uri(String.Format("http://{0}/{1}?{2}", Configurator.appdomain, path, query));
		}

	}

}
