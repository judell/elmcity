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
using System.Linq;
using System.Net;
using System.Text;
using NUnit.Framework;

namespace ElmcityUtils
{
	[TestFixture]
	public class BlobStorageTest
	{
		private const string containername = "AAATestContainer"; // should become aaatestcontainer
		//private const string blobname = "AAATestBlob";
		private static byte[] blobcontent = Encoding.UTF8.GetBytes("AAATestContent");
		private static Hashtable blobmeta;
		private static BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
		private const int delay = 3;

		[Test]
		public void CreateNewPublicContainerIsSuccessful()
		{
			bs.DeleteContainer(containername);
			BlobStorageResponse response;
			response = bs.CreateContainer(containername, true, new Hashtable());
			Assert.That(response != null);
			Assert.AreEqual(HttpStatusCode.Created, response.HttpResponse.status);
		}

		[Test]
		public void CreateExistingPublicContainerIsConflict()
		{
			bs.DeleteContainer(containername);
			bs.CreateContainer(containername, true, new Hashtable());
			BlobStorageResponse response = bs.CreateContainer(containername, true, new Hashtable());
			Assert.That(response != null);
			Assert.AreEqual(System.Net.HttpStatusCode.Conflict, response.HttpResponse.status);
		}

		[Test]
		public void ListContainersIncludesKnownContainer()
		{
			CreateNewPublicContainerIsSuccessful();
			var e = (IEnumerable<Dictionary<string, string>>)bs.ListContainers().response;
			var l = e.ToList();
			var found = l.Exists(d => d["Name"] == BlobStorage.LegalizeContainerName(containername));
			Assert.IsTrue(found);
		}

		[Test]
		public void PutBlobYieldsHttpCreated()
		{
			var blobname = CreateBlob();
			blobmeta = new Hashtable();
			var bs_response = bs.PutBlob(containername, blobname, blobmeta, blobcontent, null);
			Assert.AreEqual(HttpStatusCode.Created, bs_response.HttpResponse.status);
			bs.DeleteBlob(containername, blobname);
		}

		[Test]
		public void PutBlobCreatesExpectedMetadata()
		{
			var blobname = CreateBlob();
			blobmeta = new Hashtable();
			blobmeta.Add(BlobStorage.PREFIX_METADATA + "metakey", "metavalue");
			string content_type = "text/random";
			var bs_response = bs.PutBlob(containername, blobname, blobmeta, blobcontent, content_type);
			string domain = Configurator.azure_storage_account + "." + Configurator.azure_blob_domain;
			string str_url = string.Format("http://{0}/{1}/{2}", domain, BlobStorage.LegalizeContainerName(containername), blobname);
			var request = (HttpWebRequest)WebRequest.Create(new Uri(str_url));
			var response = HttpUtils.DoHttpWebRequest(request, null);
			Assert.AreEqual(blobcontent, response.bytes);
			Assert.AreEqual("text/random", response.headers["Content-Type"]);
			Assert.AreEqual("metavalue", response.headers[BlobStorage.PREFIX_METADATA + "metakey"]);
			bs.DeleteBlob(containername, blobname);
		}


		[Test]
		public void DeleteExistingBlobReturnsHttpAccepted()
		{
			CreateNewPublicContainerIsSuccessful();
			blobmeta = new Hashtable();
			var blobname = MakeBlobName();
			bs.PutBlob(containername, blobname, blobmeta, blobcontent, "");
			var bs_response = bs.DeleteBlob(containername, blobname);
			Assert.AreEqual(HttpStatusCode.Accepted, bs_response.HttpResponse.status);
			bs.DeleteBlob(containername, blobname);
		}

		[Test]
		public void DeleteNonExistingBlobReturnsHttpNotFound()
		{
			var blobname = MakeBlobName();
			var bs_response = bs.DeleteBlob(containername, blobname);
			Assert.AreEqual(HttpStatusCode.NotFound, bs_response.HttpResponse.status);
		}

		[Test]
		public void ExistsExistingBlobIsTrue()
		{
			var blobname = CreateBlob();
			blobmeta = new Hashtable();
			bs.PutBlob(containername, blobname, blobmeta, blobcontent, "");
			HttpUtils.Wait(delay);
			Assert.That(BlobStorage.ExistsBlob(containername, blobname) == true);
			bs.DeleteBlob(containername, blobname);
		}

		[Test]
		public void DeleteExistingPublicContainerReturnsHttpAccepted()
		{
			bs.CreateContainer(containername, true, new Hashtable());
			BlobStorageResponse response = bs.DeleteContainer(containername);
			Assert.AreEqual(HttpStatusCode.Accepted, response.HttpResponse.status);
		}

		[Test]
		public void DeleteNonExistingPublicContainerReturnsHttpAccepted()
		{
			bs.DeleteContainer(containername);
			BlobStorageResponse response = bs.DeleteContainer(containername);
			Assert.AreEqual(HttpStatusCode.Accepted, response.HttpResponse.status);
		}

		[Test]
		public void AcquireLeaseReturnsHttpStatusCreated()
		{
			var blobname = CreateBlob();
			var r = bs.AcquireLease(containername, blobname);
			Assert.AreEqual(HttpStatusCode.Created, r.status);
			bs.DeleteBlob(containername, blobname);
		}

		[Test]
		public void AcquireLeaseFailsWithConflict()
		{
			var blobname = CreateBlob();
			var r = bs.AcquireLease(containername, blobname);
			Assert.AreEqual(HttpStatusCode.Created, r.status);
			HttpUtils.Wait(1);
			r = bs.AcquireLease(containername, blobname);
			Assert.AreEqual(HttpStatusCode.Conflict, r.status);
			bs.DeleteBlob(containername, blobname);
		}

		private string CreateBlob()
		{
			bs.CreateContainer(containername, true, new Hashtable());
			var blobname = MakeBlobName();
			bs.PutBlob(containername, blobname, blobcontent);
			return blobname;
		}

		[Test]
		public void RetryLeaseReturnsStatusCreated()
		{
			var blobname = CreateBlob();
			var r = bs.AcquireLease(containername, blobname);
			Assert.AreEqual(HttpStatusCode.Created, r.status);
			r = bs.RetryAcquireLease(containername, blobname);
			Assert.AreEqual(HttpStatusCode.Created, r.status);
			bs.DeleteBlob(containername, blobname);
		}

		private string MakeBlobName()
		{
			return DateTime.Now.Ticks.ToString();
		}

	}
}
