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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ElmcityUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CalendarAggregator
{
	[TestFixture]
	public class CalendarRendererTest
	{
		private CalendarRenderer cr;
		private ZonelessEventStore es;
		private string event_html_header = "class=\"bl";
		private static Calinfo calinfo = Utils.AcquireCalinfo(Configurator.testid);

		static Uri view_uri = new Uri("http://elmcity.cloudapp.net/services/elmcity/xml?view=government");
		static byte[] view_contents = HttpUtils.FetchUrl(view_uri).bytes;
		static string view_etag = HttpUtils.GetMd5Hash(view_contents);
		static Uri cached_base_uri = new Uri(Utils.MakeBaseZonelessUrl(Configurator.testid));

		private static ZonelessEventStore filterable = new ZonelessEventStore(calinfo);

		public CalendarRendererTest()
		{
			this.cr = new CalendarRenderer(Configurator.testid);
			this.cr.cache = new MockCache();
			var est = new EventStoreTest();
			est.SerializeAndDeserializeZonelessEventStoreYieldsExpectedEvents();
			this.es = new ZonelessEventStore(calinfo);
			var uri = BlobStorage.MakeAzureBlobUri(EventStoreTest.test_container, this.es.objfile,false);
			this.es = (ZonelessEventStore)BlobStorage.DeserializeObjectFromUri(uri);

			filterable.AddEvent("e1", "", "", null, null, DateTime.Parse("2013/11/01 8 AM"), DateTime.MinValue, false, null, null, null);
			filterable.AddEvent("e2", "", "", null, null, DateTime.Parse("2013/11/02 8 AM"), DateTime.MinValue, false, null, null, null);
			filterable.AddEvent("e3", "", "", null, null, DateTime.Parse("2013/11/03 8 AM"), DateTime.MinValue, false, null, null, null);
			filterable.AddEvent("e4", "", "", null, null, DateTime.Parse("2013/11/04 8 AM"), DateTime.MinValue, false, null, null, null);
			filterable.AddEvent("e5", "", "", null, null, DateTime.Parse("2013/11/05 8 AM"), DateTime.MinValue, false, "music", null, null);
		}

		[Test]
		public void ViewFilterReturnsOneEvent()
		{
			var events = cr.Filter("music", 0, DateTime.MinValue, DateTime.MinValue, filterable);
			Assert.That(events.Count == 1);
			Assert.That(events.First().title == "e5");
		}

		[Test]
		public void TimeFilterReturnsNov4And5()
		{
			var events = cr.Filter(null, 0, DateTime.Parse("2013-11-04T00:00"), DateTime.Parse("2013-11-06T00:00"), filterable);
			Assert.That(events.Count == 2);
			var titles = events.Select(x => x.title).ToList();
			titles.Sort();
			Assert.That(titles.SequenceEqual( new List<string>() { "e4", "e5" } ) );
		}

		[Test]
		public void CountFilterReturns2()
		{
			var events = cr.Filter(null, 2, DateTime.MinValue, DateTime.MinValue, filterable);
			Assert.That(events.Count == 2);
			var titles = events.Select(x => x.title).ToList();
			titles.Sort();
			Assert.That(titles.SequenceEqual(new List<string>() { "e1", "e2" }));
		}

		[Test]
		public void RenderedHtmlEventCountMatchesStoredEventCount()
		{
			var es_count = this.es.events.Count;
			var html = cr.RenderHtml();
			var html_count = GenUtils.RegexCountSubstrings(html, this.event_html_header);
			Assert.AreEqual(es_count, html_count);
			Assert.AreEqual(2, html_count);
		}

		[Test]
		public void RenderedHtmlViewMatchesExpectedCount()
		{
			var es_count = es.events.Count;
			var html = cr.RenderHtml(null, EventStoreTest.test_category, 0, from: DateTime.MinValue, to: DateTime.MinValue, args:null);
			var html_count = GenUtils.RegexCountSubstrings(html, event_html_header);
			Assert.AreEqual(1, html_count);
		}

		[Test]
		public void RenderedXmlEventCountMatchesStoredEventCount()
		{
			var es_count = es.events.Count;
			var xml = cr.RenderXml(count: 0);
			var xdoc = XmlUtils.XdocFromXmlBytes(Encoding.UTF8.GetBytes(xml));

			var xml_events = from evt in xdoc.Descendants("event")
							 select new { evt };

			Assert.AreEqual(es_count, xml_events.Count());
			Assert.AreEqual(2, xml_events.Count());
		}

		[Test]
		public void RenderedJsonEventCountMatchesStoredEventCount()
		{
			var es_count = es.events.Count;
			var json = cr.RenderJson(0);
			//var events = JsonConvert.DeserializeObject<List<ZonelessEvent>>(json);
			var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
			var events = (object[])serializer.DeserializeObject(json);
			Assert.AreEqual(es_count, events.ToList().Count());
			Assert.AreEqual(2, events.ToList().Count());
		}

		[Test]
		public void TagCloudYieldsValidDict()
		{

			var json = cr.RenderTagCloudAsJson();

			//Console.WriteLine(json);

			JArray objects = (JArray)JsonConvert.DeserializeObject(json);

			var tagcloud = new List<Dictionary<string, int>>();

			foreach (JObject obj in objects)
			{
				{
					var key = obj.Properties().First().Name;
					var val = obj[key].Value<int>();
					tagcloud.Add(new Dictionary<string, int>() { { key, val } });
				}
			}

			Assert.That(tagcloud.Count > 0);

			var firstdict = tagcloud[0];
			var firstkey = firstdict.Keys.First();
			Assert.That(firstdict[firstkey] > 0);
		}

		[Test]
		public void ResponseBodySuppressedForViewIfRequestIfNoneMatchEqualsResponseEtagAndItemIsCached()
		{
			var headers = new System.Net.WebHeaderCollection() { { "If-None-Match", view_etag } };
			var mock_controller_context = CacheUtilsTest.SetupMockControllerHeaders(headers);
			var cache = new MockCache();
			cache[view_uri.ToString()] = view_contents;
			var response = CacheUtils.MaybeSuppressResponseBodyForView(mock_controller_context, view_contents);
			Assert.AreEqual(new byte[0], response);
		}

		[Test]
		public void ResponseBodyNotSuppressedForViewIfRequestIfNoneMatchNotEqualsResponseEtag()
		{
			var headers = new System.Net.WebHeaderCollection() { { "If-None-Match", "NOT_VIEW_ETAG" } };
			var mock_controller_context = CacheUtilsTest.SetupMockControllerHeaders(headers);
			var response = CacheUtils.MaybeSuppressResponseBodyForView(mock_controller_context, view_contents);
			Assert.AreNotEqual(new byte[0], response);
			Assert.AreEqual(response, view_contents);
		}

		[Test]
		public void UsesAlternateTemplateWhenRequested()
		{
			this.cr = new CalendarRenderer(Configurator.testid);
			var orig = this.cr.template_html;
			var args = new Dictionary<string,object>();
			args.Add("template", "a2chron.tmpl");
			this.cr.MaybeUseAlternateTemplate(args);
			Assert.AreNotEqual(this.cr.template_html, orig);
		}

		[Test]
		public void RestoresOriginalTemplateWhenAlternateNotRequested()
		{
			this.cr = new CalendarRenderer(Configurator.testid);
			var orig = this.cr.template_html;
			var args = new Dictionary<string, object>();
			args.Add("template", "a2chron.tmpl");
			this.cr.MaybeUseAlternateTemplate(args);
			args = new Dictionary<string, object>();
			this.cr.MaybeUseAlternateTemplate(args);
			Assert.AreEqual(this.cr.template_html, orig);
		}





	}
}
