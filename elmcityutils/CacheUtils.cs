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
using System.Linq;
using System.Text;
using System.Web.Caching;
using System.Web.Mvc;

namespace ElmcityUtils
{
	// minimal interface to enable mocking IIS cache
	public interface ICache
	{
		void Insert(
			 string key,
			  Object value,
			  CacheDependency dependency,
			  DateTime absolute_expiration,
			  TimeSpan sliding_expiration,
			  CacheItemPriority priority,
			  CacheItemRemovedCallback removed_callback
			 );

		Object Remove(string key);

		Object this[string key]
		{ get; set; }

	}

	// one of two implementations of ICache. this one
	// encapsulates the real IIS cache. the other,
	// HttpUtilsTest.MockCache, tests the cache logic.
	public class AspNetCache : ICache
	{
		private Cache cache;
		private TableStorage ts = TableStorage.MakeDefaultTableStorage();

		public AspNetCache(Cache cache)
		{
			this.cache = cache;
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
			this.cache.Insert(key, value, dependency, absolute_expiration, sliding_expiration, priority, removed_callback);
		}

		public Object Remove(string key)
		{
			Object o = null;
			try
			{
				o = this.cache.Remove(key);
				GenUtils.LogMsg("info", "AspNetCache.Remove", key);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "AspNetCache.Remove: " + key, e.Message + e.StackTrace);
			}
			return o;
		}

		public static void LogRemovedItemToAzure(String key, Object o, CacheItemRemovedReason r)
		{
			GenUtils.LogMsg("info", "LogRemovedItemToAzure: " + key, r.ToString());
		}

		public static void LogRemovedItemToConsole(String key, Object o, CacheItemRemovedReason r)
		{
			Console.WriteLine("info: " + key + " " + r.ToString());
		}

		public Object this[string key]
		{
			get
			{
				return this.cache.Get(key);
			}
			set
			{
				this.cache[key] = value;
			}
		}

	}

	// Placeholder for extension, not used yet
	public class ElmcityCacheDependency : CacheDependency
	{
		public ElmcityCacheDependency(string key)
			: base(new string[0], new string[] { key }, null)
		{
		}
	}

	/* unused
	public interface ICacheDependency
	{
		void Dispose();
		bool HasChanged();
	}

	public class AspNetCacheDependency : ICacheDependency
	{
		private CacheDependency dependency;

		public AspNetCacheDependency(string key)
		{
			this.dependency = new CacheDependency(new string[0], new string[] { key }, null);
		}

		public void Dispose()
		{
			Dispose();
		}

		public bool HasChanged()
		{
			return false;
		}
	}
	 */

	public static class CacheUtils
	{
		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();

		public static string cache_control_tablename = "cacheurls";
		public static string cache_control_partkey = cache_control_tablename;

		// used by CalendarRenderer.RenderDynamicViewWithCaching, 
		// emits not-modified if client already has the rendered view
		public static byte[] MaybeSuppressResponseBodyForView(ICache cache, ControllerContext context, byte[] view_data)
		{
			byte[] response_body = view_data;
			var request_if_none_match = HttpUtils.MaybeExtractHeaderFromRequestContext("If-None-Match", context);
			var response_etag = HttpUtils.GetMd5Hash(view_data);

			var client_presents_correct_validator = (request_if_none_match == response_etag);

			if (client_presents_correct_validator)
			{
				response_body = new byte[0];
				context.HttpContext.Response.StatusCode = (int)System.Net.HttpStatusCode.NotModified; // emit not-modified
			}

			context.HttpContext.Response.AddHeader("ETag", response_etag);
			context.HttpContext.Response.AddHeader("Cache-Control", "max-age=" + Configurator.cache_control_max_age);

			return response_body;
		}

		public static Dictionary<string, object> RetrieveBlobAndEtagFromServerCacheOrUri(ICache cache, Uri uri)
		{
			var dict = new Dictionary<string, object>();

			if (cache == null) // called from worker role, not web role or test harness, cache logic doesn't apply
				HttpUtils.FetchResponseBodyAndETagFromUri(uri, dict);

			else               // called from web role or test harness, cache logic applies
			{
				var key = uri.ToString();

				byte[] response_body = (byte[])cache[key]; // take from cache if available

				if (response_body == null)   // not in cache, so fetch blob, etag comes along for the ride
				{
					HttpUtils.FetchResponseBodyAndETagFromUri(uri, dict);
				}
				else                         // use cached blob, still need to get the etag from the server
				{
					dict["response_body"] = response_body;
					dict["ETag"] = HttpUtils.MaybeGetHeaderFromUriHead("ETag", uri);
				}
			}

			return dict;
		}

		public static string ViewCache(ControllerContext context)
		{
			var tr_template = "<tr><td>{0}</td><td align=\"right\">{1}</td></tr>\n";
			var cache = context.HttpContext.Cache;
			var dict = new Dictionary<string, string>();
			int total = 0;
			IDictionaryEnumerator e = cache.GetEnumerator();
			while (e.MoveNext())
			{
				var entry = (DictionaryEntry)e.Current;
				string value;
				if (entry.Value.GetType().FullName == "System.Byte[]")
				{
					System.Byte[] bytes = (System.Byte[])entry.Value;
					var length = bytes.Length;
					total += length;
					value = String.Format("{0}", length);
				}
				else
					value = entry.Value.ToString();
				dict.Add(entry.Key.ToString(), value);
			}
			var html = new StringBuilder("<html>\n<body>\n");
			var p = System.Diagnostics.Process.GetCurrentProcess();
			var info = string.Format("<p>host {0}, procname {1}, procid {2}</p>",
				System.Net.Dns.GetHostName(), p.ProcessName, p.Id);
			html.Append(info);
			html.Append("<table>\n");
			List<string> keys = dict.Keys.ToList();
			keys.Sort();
			foreach (string key in keys)
				html.Append(String.Format(tr_template, key, dict[key]));
			html.Append("</table>\n</body>\n");
			html.Append("<p>Total: " + total + "</p>\n");
			html.Append("</html>");
			return html.ToString();
		}

		public static void MarkBaseCacheEntryForRemoval(string cached_uri, int instance_count)
		{
			var entity = new Dictionary<string, object>();
			entity.Add("cached_uri", cached_uri);
			entity.Add("count", instance_count);
			TableStorage.DictObjToTableStore(TableStorage.Operation.merge, entity, cache_control_tablename, cache_control_partkey, TableStorage.MakeSafeRowkeyFromUrl(cached_uri));
		}

		public static void MaybePurgeCache(ICache cache)
		{
			try
			{
				var purgeable_entities = FetchPurgeableCacheDicts();
				GenUtils.LogMsg("info", String.Format("MaybePurgeCache: {0} purgeable entities", purgeable_entities.Count), null);
				foreach (var purgeable_entity in purgeable_entities)
				{
					var purgeable_cache_url = (string)purgeable_entity["cached_uri"];
					if (cache[purgeable_cache_url] != null)
					{
						GenUtils.LogMsg("info", "MaybePurgeCache", purgeable_cache_url);
						cache.Remove(purgeable_cache_url);
						var count = (int)purgeable_entity["count"];
						count -= 1;
						if (count < 0)
						{
							count = 0;
							GenUtils.LogMsg("warning", "CacheUtils.MaybePurgeCache", "count went subzero, reset to zero");
						}
						purgeable_entity["count"] = count;
						var rowkey = TableStorage.MakeSafeRowkeyFromUrl(purgeable_cache_url);
						ts.UpdateEntity(cache_control_tablename, cache_control_partkey, rowkey, purgeable_entity);
					}
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "MaybePurgeCache", e.Message + e.StackTrace);
			}
		}

		public static List<Dictionary<string, object>> FetchPurgeableCacheDicts()
		{
			var query = String.Format("$filter=(PartitionKey eq '{0}')", cache_control_tablename);
			var marked_cache_url_dicts = (List<Dictionary<string, object>>)ts.QueryAllEntities(cache_control_tablename, query, TableStorage.QueryAllReturnType.as_dicts).response;
			var purgeable_cache_dicts = marked_cache_url_dicts.FindAll(dict => (int)dict["count"] > 0);
			return purgeable_cache_dicts;
		}

	}
}
