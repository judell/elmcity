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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Mvc;
using System.Web.Routing;
using Moq;
using NUnit.Framework;

namespace ElmcityUtils
{

    public class MockController : Controller { }

    // One of two implementations of ICache. 
    // This one is a mock for testing. The other,
    // HttpUtils.AspNetCache, encapsulates the real IIS cache.
    public class MockCache : ICache
    {
        private Dictionary<string, byte[]> cache;

        public MockCache()
        {
            this.cache = new Dictionary<string, byte[]>();
        }

        public void Insert(
             string key,
             Object value,
             CacheDependency dependency,
             DateTime absolute_expiration,
             TimeSpan sliding_expiration,
             CacheItemPriority priority,
             CacheItemRemovedCallback removed_callback
           )
        {
            this.cache[key] = (byte[])value;
            var remover = new CacheEntryRemover(Remover);
            var result = remover.BeginInvoke(key, sliding_expiration, null, null);
        }

        public Object Remove(string key)
        {
            Object value = null;
            if (this.cache.ContainsKey(key))
            {
                value = this.cache[key];
                this.cache.Remove(key);
                GenUtils.LogMsg("info", "MockCache.Remove", key);
            }
            return value;
        }

        public Object this[string key]
        {
            get
            {
                if (this.cache.ContainsKey(key))
                    return this.cache[key];
                else
                    return null;
            }
            set
            {
                this.cache[key] = (byte[])value;
            }
        }


        private delegate bool CacheEntryRemover(string key, TimeSpan sliding_expiration);

        private bool Remover(string key, TimeSpan sliding_expiration)
        {
            HttpUtils.Wait((int)sliding_expiration.TotalSeconds);
            this.cache.Remove(key);
            return this.cache.ContainsKey(key) == false;
        }
    }


    public class CacheUtilsTest
    {
        private BlobStorage bs = BlobStorage.MakeDefaultBlobStorage();
        private string containername = "cachetest";
        private string blobname = "testblob";
        private byte[] view_contents = Encoding.UTF8.GetBytes("contents of test blob");
        private byte[] cached_blob_contents = Encoding.UTF8.GetBytes("cached contents of test blob");
        private Uri blob_uri;
        private Uri view_uri;
        private Uri cached_base_uri = new Uri("http://foo.bar/cached_base_uri");
        private string blob_etag;
        private string view_etag;


        public CacheUtilsTest()
        {
            this.blob_uri = BlobStorage.MakeAzureBlobUri(this.containername, this.blobname);
            this.view_uri = new Uri(this.blob_uri.ToString() + "?view=government&count=10");
            var bs_response = bs.PutBlob(containername, blobname, new Hashtable(), view_contents, null);
            Assert.That(bs_response.HttpResponse.status == System.Net.HttpStatusCode.Created);
            this.blob_etag = HttpUtils.MaybeGetHeaderFromUriHead("ETag", blob_uri);
            this.view_etag = HttpUtils.GetMd5Hash(cached_blob_contents);
        }

        [Test]
        public void RetrieveUncachedBlobYieldsBlob()
        {
            MockCache cache = new MockCache();
            var direct_fetch = HttpUtils.FetchUrl(blob_uri).bytes;
            var indirect_fetch = CacheUtils.RetrieveBlobAndEtagFromServerCacheOrUri(cache, blob_uri)["response_body"];
            Assert.AreEqual(indirect_fetch, direct_fetch);
        }

        [Test]
        public void CacheWithBlobYieldsCachedBlob()
        {
            MockCache cache = new MockCache();
            var cache_span = new TimeSpan(1, 0, 0);
            cache.Insert(blob_uri.ToString(), cached_blob_contents, null, Cache.NoAbsoluteExpiration, cache_span, CacheItemPriority.Normal, null);
            var indirect_fetch = CacheUtils.RetrieveBlobAndEtagFromServerCacheOrUri(cache, blob_uri);
            Assert.AreEqual(indirect_fetch["response_body"], cached_blob_contents);
        }

        [Test]
        public void ResponseBodySuppressedForViewIfRequestIfNoneMatchEqualsResponseEtag()
        {
            var cache = new MockCache();
            var headers = new System.Net.WebHeaderCollection() { { "If-None-Match", view_etag } };
            var mock_controller_context = SetupMockControllerHeaders(headers);
            var response = CacheUtils.MaybeSuppressResponseBodyForView(cache, mock_controller_context, cached_blob_contents);
            Assert.AreEqual(new byte[0], response);
        }

        [Test]
        public void ResponseBodyNotSuppressedForViewIfRequestIfNoneMatchNotEqualsResponseEtag()
        {
            var cache = new MockCache();
            var headers = new System.Net.WebHeaderCollection() { { "If-None-Match", "NOT_BLOB_ETAG" } };
            var mock_controller_context = SetupMockControllerHeaders(headers);
            var response = CacheUtils.MaybeSuppressResponseBodyForView(cache, mock_controller_context, cached_blob_contents);
            Assert.AreNotEqual(new byte[0], response);
            Assert.AreEqual(cached_blob_contents, response);
        }

        [Test]
        public void ItemInCacheIfWithinSlidingExpiration()
        {
            var cache = new MockCache();
            var sliding_expiration = new TimeSpan(0, 0, 5);
            var key = view_uri.ToString();
            cache.Insert(key, view_contents, null, Cache.NoAbsoluteExpiration, sliding_expiration, CacheItemPriority.Normal, null);
            Assert.AreEqual(cache[key], view_contents);
        }

        [Test]
        public void ItemGoneFromCacheIfBeyondSlidingExpiration()
        {
            var cache = new MockCache();
            var sliding_expiration = new TimeSpan(0, 0, 1);
            var key = view_uri.ToString();
            cache.Insert(key, view_contents, null, Cache.NoAbsoluteExpiration, sliding_expiration, CacheItemPriority.Normal, null);
            HttpUtils.Wait(2);
            Assert.AreNotEqual(cache[key], view_contents);
        }

        [Test]
        public void ExpiredObjectIsGone()
        {
            var cache = new MockCache();
            var cache_span = new TimeSpan(0, 0, 2);
            var key = blob_uri.ToString();
            cache.Insert(key, view_contents, null, Cache.NoAbsoluteExpiration, cache_span, CacheItemPriority.Normal, null);
            HttpUtils.Wait(5);
            Assert.That(cache[blob_uri.ToString()] == null);
        }

        [Test]
        public void RemovedObjectIsGone()
        {
            var cache = new MockCache();
            var cache_span = new TimeSpan(0, 0, 2);
            var key = blob_uri.ToString();
            cache.Insert(key, view_contents, null, Cache.NoAbsoluteExpiration, cache_span, CacheItemPriority.Normal, null);
            cache.Remove(key);
            Assert.That(cache[key] == null);
        }

		[Test]
		public void PurgeIsSuccessful()
		{
			var cache = new MockCache();
			var cache_span = new TimeSpan(1, 0, 0);
			var key = blob_uri.ToString();
			cache.Insert(key, view_contents, null, Cache.NoAbsoluteExpiration, cache_span, CacheItemPriority.Normal, null);
			CacheUtils.MarkBaseCacheEntryForRemoval(key, 2);
			var purgeable_dicts = CacheUtils.FetchPurgeableCacheDicts();
			var test_dict = purgeable_dicts.Find(d => (string) d["cached_uri"] == key);
			Assert.That( (int) test_dict["count"] == 2);
			CacheUtils.MaybePurgeCache(cache);
			Assert.That(cache[key] == null);
			purgeable_dicts = CacheUtils.FetchPurgeableCacheDicts();
			test_dict = purgeable_dicts.Find(d => (string) d["cached_uri"] == key);
			Assert.That((int)test_dict["count"] == 1);
		}

        [Test]
        public void OkToRemoveNonExistingObject()
        {
            var cache = new MockCache();
            var cache_span = new TimeSpan(0, 0, 2);
            var key = blob_uri.ToString();
            cache.Remove(key);
            Assert.That(cache[key] == null);
        }

        public static ControllerContext SetupMockControllerHeaders(System.Net.WebHeaderCollection headers)
        {
            var req = new Mock<HttpRequestBase>();
            var rsp = new Mock<HttpResponseBase>();
            req.SetupGet(x => x.Headers).Returns(
                new System.Net.WebHeaderCollection { headers }
                );
            var context = new Mock<HttpContextBase>();
            context.Setup(ctx => ctx.Request).Returns(req.Object);
            context.Setup(ctx => ctx.Response).Returns(rsp.Object);
            var mock_controller = new MockController();
            var mock_controller_context = new ControllerContext(context.Object, new RouteData(), mock_controller);
            return mock_controller_context;
        }

    }

}

