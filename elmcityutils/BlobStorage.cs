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
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml;
using System.Text;
using System.Threading.Tasks;

namespace ElmcityUtils
{
	// packaging for responses from blob operations
	public class BlobStorageResponse
	{
		public HttpResponse HttpResponse { get; set; }
		public object response { get; set; }

		public BlobStorageResponse(HttpResponse http_response, List<Dictionary<string, string>> response)
		{
			this.HttpResponse = http_response;
			this.response = response;
		}

		public BlobStorageResponse(HttpResponse http_response)
		{
			this.HttpResponse = http_response;
			this.response = null;
		}

	}

	// http-oriented alternative to azure sdk blob storage interface
	[Serializable]
	public class BlobStorage
	{
		private string azure_storage_account;
		private string azure_blob_host;
		private string azure_b64_secret;

		public const string PREFIX_METADATA = "x-ms-meta-"; //http://msdn.microsoft.com/en-us/library/dd179404.aspx
		private const string PREFIX_STORAGE = "x-ms-";
		private const string NEW_LINE = "\x0A"; // http://msdn.microsoft.com/en-us/library/dd179428.aspx
		private const bool DEBUG = true;
		private const string TIME_FORMAT = "ddd, dd MMM yyyy HH:mm:ss";

		private string[] container_elements = { "Name", "Url" };
		private string[] blob_elements = { "Name", "Url" };

		public BlobStorage(string azure_storage_account, string azure_blob_host,
				string azure_b64_secret)
		{
			this.azure_storage_account = azure_storage_account;
			this.azure_blob_host = azure_blob_host;
			this.azure_b64_secret = azure_b64_secret;
		}

		public static BlobStorage MakeDefaultBlobStorage()
		{
			var	key = Configurator.GetStorageKey();
			return new BlobStorage(Configurator.azure_storage_account,
				  Configurator.azure_storage_account + "." + Configurator.azure_blob_domain,
				  key);
		}

		public static BlobStorageResponse WriteToAzureBlob(BlobStorage bs, string containername, string blobname, string content_type, byte[] bytes)
		{
			if (BlobStorage.ExistsContainer(containername) == false)
				bs.CreateContainer(containername, true, new Hashtable());
			var headers = new Hashtable();
			BlobStorageResponse bs_response;
			bs_response = bs.PutBlob(containername, blobname, headers, bytes, content_type);
			return bs_response;
		}

		// waits and retries if container is being deleted
		public BlobStorageResponse CreateContainer(string containername, bool is_public, Hashtable headers)
		{
			var completed_delegate = new GenUtils.Actions.CompletedDelegate<BlobStorageResponse, Object>(CompletedIfContainerIsNotBeingDeleted);
			return GenUtils.Actions.Retry<BlobStorageResponse>(delegate() { return MaybeCreateContainer(containername, is_public, (Hashtable)headers.Clone()); }, completed_delegate, completed_delegate_object: null, wait_secs: StorageUtils.wait_secs, max_tries: StorageUtils.max_tries, timeout_secs: StorageUtils.timeout_secs);
		}

		public static bool CompletedIfContainerIsNotBeingDeleted(BlobStorageResponse response, Object o)
		{
			if (response == null)
				return false;
			var xml_response = response.HttpResponse.DataAsString();
			if (xml_response.Contains("ContainerBeingDeleted"))
				return false;
			else
				return true;
		}

		// fails if container is being deleted
		public BlobStorageResponse MaybeCreateContainer(string containername, bool is_public, Hashtable headers)
		{
			if (is_public)
				headers.Add("x-ms-blob-public-access", "blob");
			headers.Add("x-ms-blob-type", "BlockBlob");
			HttpResponse http_response = DoBlobStoreRequest(containername, blobname: null, method: "PUT", headers: headers, data: null, content_type: null, query_string: "?restype=container");
			return new BlobStorageResponse(http_response);
		}

		// retries until deleted
		public BlobStorageResponse DeleteContainer(string containername)
		{
			containername = LegalizeContainerName(containername);
			var completed_delegate = new GenUtils.Actions.CompletedDelegate<BlobStorageResponse, Object>(CompletedIfContainerIsGone);
			return GenUtils.Actions.Retry<BlobStorageResponse>(delegate() { return MaybeDeleteContainer(containername); }, completed_delegate, completed_delegate_object: containername, wait_secs: StorageUtils.wait_secs, max_tries: StorageUtils.max_tries, timeout_secs: StorageUtils.timeout_secs);
		}

		public static bool CompletedIfContainerIsGone(BlobStorageResponse response, object o)
		{
			if (response == null)
				return false;
			string container;
			try
			{
				container = (String)o;
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "CompletedIfContainerIsGone", e.Message + e.StackTrace);
				throw new Exception("CompletedObjectDelegateException");
			}

			if (!ExistsContainer(container))
				return true;
			else
				return false;
		}

		public BlobStorageResponse MaybeDeleteContainer(string containername)
		{
			try
			{
				HttpResponse http_response = DoBlobStoreRequest(containername, blobname: null, method: "DELETE", headers: new Hashtable(), data: null, content_type: null, query_string: "?restype=container");
				return new BlobStorageResponse(http_response);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "MaybeDeleteContainer: " + containername, e.Message + e.StackTrace);
				return default(BlobStorageResponse);
			}
		}

		public BlobStorageResponse ListContainers()
		{
			return ListContainers(path: null);
		}

		public BlobStorageResponse ListContainers(string path)
		{
			var qs = "?restype=container&comp=list";
			HttpResponse http_response = DoBlobStoreRequest(containername: path, blobname: null, method: "GET", headers: new Hashtable(), data: null, content_type: null, query_string: qs);
			String next_marker = null;
			var dicts = new List<Dictionary<string, string>>();
			do
			{
				foreach (var dict in DictsFromBlobStorageResponse(http_response, "//Containers/Container", container_elements, ref next_marker))
					dicts.Add(dict);
			}
			while (next_marker != null);

			return new BlobStorageResponse(http_response, dicts);
		}

		public static string LegalizeContainerName(string containername)
		{
			containername = containername.ToLower();
			containername = containername.Replace("_", "-");
			return containername;
		}

		public static bool ExistsContainer(string containername)
		{
			//var url = MakeAzureBlobUri(containername.ToLower(), "");
			var url = MakeAzureBlobUri(LegalizeContainerName(containername), "", false);
			var response = HttpUtils.FetchUrl(url);
			return (response.status == HttpStatusCode.OK);
		}

		public BlobStorageResponse ListBlobs(string containername)
		{
			return ListBlobs(containername, null);
		}

		public BlobStorageResponse ListBlobs(string containername, string prefix)
		{
			HttpResponse http_response;
			String next_marker = null;
			var dicts = new List<Dictionary<string, string>>();
			do
			{
				var qs = "?comp=list&restype=container&maxresults=5000";
				//var qs = "?comp=list&restype=container";
				if (!String.IsNullOrEmpty(next_marker))
					qs += "&marker=" + next_marker;
				if (!String.IsNullOrEmpty(prefix))
					qs += "&prefix=" + prefix;
				http_response = DoBlobStoreRequest(containername, blobname: null, method: "GET", headers: new Hashtable(), data: null, content_type: null, query_string: qs);
				foreach (var dict in DictsFromBlobStorageResponse(http_response, "//Blobs/Blob", blob_elements, ref next_marker))
					dicts.Add(dict);
			}
			while (next_marker != null);

			return new BlobStorageResponse(http_response, dicts);
		}

		public static bool ExistsBlob(string containername, string blobname)
		{
			var uri = MakeAzureBlobUri(containername, blobname, false);
			return ExistsBlob(uri);
		}

		public static bool ExistsBlob(Uri uri)
		{
			var response = HttpUtils.HeadFetchUrl(uri);
			return (response.status == HttpStatusCode.OK);
		}

		public BlobStorageResponse PutBlobWithLease(string container, string blobname, Hashtable headers, byte[] data, string content_type)
		{
			try
			{
				if (BlobStorage.ExistsBlob(container, blobname))
				{
					var r = this.RetryAcquireLease(container, blobname);
					if (r.status == HttpStatusCode.Created)
						headers.Add("x-ms-lease-id", r.headers["x-ms-lease-id"]);
					else
						GenUtils.PriorityLogMsg("warning", "PutBlobWithLease: Did not acquire lease for: ", container + ", " + blobname);
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "PutBlobWithLease", e.Message + e.StackTrace);
			}

			return this.PutBlob(container, blobname, headers, data, content_type);
		}

		public BlobStorageResponse PutBlobWithLease(string container, string blobname, Hashtable headers, string utf_string, string content_type)
		{
			var data = Encoding.UTF8.GetBytes(utf_string);
			return this.PutBlobWithLease(container, blobname, headers, data, content_type);
		}


		public BlobStorageResponse PutBlob(string containername, string blobname, Hashtable headers, byte[] data, string content_type)
		{
			headers.Add("x-ms-blob-type", "BlockBlob");
			HttpResponse http_response = DoBlobStoreRequest(containername, blobname, method: "PUT", headers: headers, data: data, content_type: content_type, query_string: null);
			return new BlobStorageResponse(http_response);
		}

		public BlobStorageResponse PutBlob(string containername, string blobname, byte[] data)
		{
			return PutBlob(containername, blobname, new Hashtable(), data, null);
		}

		public BlobStorageResponse PutBlob(string containername, string blobname, byte[] data, string content_type)
		{
			return PutBlob(containername, blobname, new Hashtable(), data, content_type);
		}

		public BlobStorageResponse PutBlob(string containername, string blobname, string data)
		{
			return PutBlob(containername, blobname, new Hashtable(), System.Text.Encoding.UTF8.GetBytes(data), null);
		}

		public BlobStorageResponse PutBlob(string containername, string blobname, string data, string content_type)
		{
			return PutBlob(containername, blobname, new Hashtable(), System.Text.Encoding.UTF8.GetBytes(data), content_type);
		}

		public BlobStorageResponse GetBlobProperties(string containername, string blobname)
		{
			HttpResponse http_response = DoBlobStoreRequest(containername, blobname, method: "HEAD", headers: new Hashtable(), data: null, content_type: null, query_string: null);
			return new BlobStorageResponse(http_response);
		}

		public BlobStorageResponse DeleteBlob(string containername, string blobname)
		{
			HttpResponse http_response = DoBlobStoreRequest(containername, blobname, method: "DELETE", headers: new Hashtable(), data: null, content_type: null, query_string: null);
			return new BlobStorageResponse(http_response);
		}

		public BlobStorageResponse GetBlob(string containername, string blobname)
		{
			HttpResponse http_response = DoBlobStoreRequest(containername, blobname, method: "GET", headers: new Hashtable(), data: null, content_type: null, query_string: null);
			return new BlobStorageResponse(http_response);
		}

		public HttpResponse RetryAcquireLease(string containername, string blobname)
		{
			var completed_delegate = new GenUtils.Actions.CompletedDelegate<HttpResponse, Object>(HttpUtils.CompletedIfStatusEqualsExpected);
			return GenUtils.Actions.Retry<HttpResponse>(delegate() { return AcquireLease(containername, blobname); }, completed_delegate, completed_delegate_object:HttpStatusCode.Created, wait_secs: 10, max_tries: 12, timeout_secs: TimeSpan.FromSeconds(120));
		}

		public HttpResponse AcquireLease(string containername, string blobname)
		{
			containername = LegalizeContainerName(containername);
			var headers = new Hashtable() { { "x-ms-lease-action", "acquire" } };
			return DoBlobStoreRequest(containername, blobname, method: "PUT", headers: headers, data: null, content_type: null, query_string: "?comp=lease");
		}

		// see http://msdn.microsoft.com/en-us/library/dd179428.aspx for authentication details
		public HttpResponse DoBlobStoreRequest(string containername, string blobname, string method, Hashtable headers, byte[] data, string content_type, string query_string)
		{
			string path = "/";

			if (containername != null)
			{
				containername = LegalizeContainerName(containername);
				path = path + containername;
				if (blobname != null)
					path = path + "/" + blobname;
			}

			StorageUtils.AddDateHeader(headers);
			StorageUtils.AddVersionHeader(headers);

			string auth_header = StorageUtils.MakeSharedKeyLiteHeader(StorageUtils.services.blob, this.azure_storage_account, this.azure_b64_secret, method, path, query_string, content_type, headers);
			headers["Authorization"] = "SharedKeyLite " + this.azure_storage_account + ":" + auth_header;

			Uri uri = new Uri(string.Format("http://{0}{1}{2}", this.azure_blob_host, path, query_string));

			//return StorageUtils.DoStorageRequest(method, headers, data, content_type, uri);
			return HttpUtils.RetryStorageRequestExpectingServiceAvailable(method, headers, data, content_type, uri);
		}

		// read atom response, select desired elements, return enum of dict<str,str>
		public List<Dictionary<string, string>> DictsFromBlobStorageResponse(HttpResponse response, string xpath, string[] elements, ref string next_marker)
		{
			var dicts = new List<Dictionary<string, string>>();
			var doc = XmlUtils.XmlDocumentFromHttpResponse(response);
			XmlNodeList nodes = doc.SelectNodes(xpath);
			foreach (XmlNode node in nodes)
			{
				var dict = new Dictionary<string, string>();
				foreach (string element in elements)
				{
					dict[element] = node.SelectSingleNode(element).FirstChild.Value;
					var lm = node.SelectSingleNode("Properties/Last-Modified").FirstChild.Value;
					dict["Last-Modified"] = lm;
				}
				dicts.Add(dict);
			}

			XmlNode next_marker_node = doc.SelectSingleNode("//NextMarker");
			if (next_marker_node != null && next_marker_node.HasChildNodes)
				next_marker = next_marker_node.FirstChild.Value;
			else
				next_marker = null;

			return dicts;
		}

		public BlobStorageResponse SerializeObjectToAzureBlob(object o, string container, string blobname)
		{
			var headers = new Hashtable();
			var bytes = ObjectUtils.SerializeObject(o);
			return this.PutBlobWithLease(container, blobname, headers, bytes, "application/octet-stream");
		}

		public static object DeserializeObjectFromUri(Uri uri)
		{
			try
			{
				var buffer = HttpUtils.FetchUrlNoCache(uri).bytes;
				return DeserializeObjectFromBytes(buffer);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "DeserializeObjectFromUri: " + uri.ToString(), e.Message);
				throw;
			}
		}

		public static object DeserializeObjectFromBytes(byte[] buffer)
		{
			IFormatter serializer = new BinaryFormatter();
			var ms = new MemoryStream(buffer);
			var o = serializer.Deserialize(ms);
			return o;
		}

		public static Uri MakeAzureBlobUri(string container, string name)
		{
			return MakeAzureBlobUri(container, name, false);
		}

		public static Uri MakeAzureBlobUri(string container, string name, bool use_cdn)
		{
			string url = string.Format("{0}/{1}/{2}",
				use_cdn ? Configurator.azure_cdn_blobhost : Configurator.azure_blobhost,
				LegalizeContainerName(container), // http://msdn.microsoft.com/en-us/library/dd135715.aspx
				name);
			return new Uri(url);
		}

		public static string GetAzureBlobAsString(string container, string name, bool use_cdn)
		{
			var uri = BlobStorage.MakeAzureBlobUri(container, name, use_cdn);
			return HttpUtils.FetchUrl(uri).DataAsString();
		}

		public static string GetAzureBlobAsString(string container, string name)
		{
			return GetAzureBlobAsString(container, name, false);
		}

		public static string MakeSafeBlobnameFromUrl(string url)
		{
			var name = TableStorage.MakeSafeRowkey(url);
			if (name.Length > 250)
				return HttpUtils.GetMd5Hash(Encoding.UTF8.GetBytes(name));
			else
				return name;
		}

		public  void PurgeBlobs(string container, TimeSpan keep)
		{
			var l = (List<Dictionary<string, string>>)this.ListBlobs(container).response;

			Parallel.ForEach(source: l, body: (blob) =>
			{
				var name = blob["Name"];
				var props = this.GetBlobProperties(container, name).HttpResponse;
				var mod = DateTime.Parse(props.headers["Last-Modified"]);
				var sentinel = DateTime.UtcNow - keep;
				if (mod < sentinel)
					this.DeleteBlob(container, name);
			});
		}

	}

}