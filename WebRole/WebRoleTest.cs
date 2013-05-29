using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ElmcityUtils;
using Newtonsoft.Json;
using System.Xml;

namespace WebRole
{
	[TestFixture]
	public class WebRoleTest
	{

		private string hub = CalendarAggregator.Configurator.testid;
		private string category = "test_category";
		private string host = "http://" + ElmcityUtils.Configurator.appdomain;
		private string path;

		public WebRoleTest()
		{
			this.path = host + "/" + hub;
		}

		[Test]
		public void EventsAsHtml()
		{
			var uri = new Uri(path);
			var html = HttpUtils.FetchUrl(uri).DataAsString();
			Assert.That(html.Contains(@"<span class=""st"" property=""v:startDate"" content=""9999-01-01T09:01"">Fri 09:01 AM</span>"));
		}

		[Test]
		public void EventsAsHtmlView()
		{
			var uri = new Uri(path + "?view=" + category);
			var html = HttpUtils.FetchUrl(uri).DataAsString();
			Assert.That(html.Contains(@"<span class=""st"" property=""v:startDate"" content=""9999-01-01T09:01"">Fri 09:01 AM</span>"));
		}

		[Test]
		public void EventsAsXml()
		{
			var uri = new Uri(path + "/xml");
			var r = HttpUtils.FetchUrl(uri);
			var doc = XmlUtils.XmlDocumentFromHttpResponse(r);
			XmlNodeList nodes = doc.SelectNodes("//event");
			Assert.That(nodes.Count == 2);
		}

		[Test]
		public void EventsAsXmlView()
		{
			var uri = new Uri(path + "/xml?view=" + category);
			var r = HttpUtils.FetchUrl(uri);
			var doc = XmlUtils.XmlDocumentFromHttpResponse(r);
			XmlNodeList nodes = doc.SelectNodes("//event");
			Assert.That(nodes.Count == 1);
		}

		[Test]
		public void EventsAsJson()
		{
			var uri = new Uri(path + "/json");
			var json = HttpUtils.FetchUrl(uri).DataAsString();
			var list_of_dict = (List<Dictionary<string, object>>) JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
			Assert.That(list_of_dict.Count == 2);
		}

		[Test]
		public void EventsAsJsonView()
		{
			var uri = new Uri(path + "/json?view=test_category");
			var json = HttpUtils.FetchUrl(uri).DataAsString();
			var list_of_dict = (List<Dictionary<string, object>>)JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
			Assert.That(list_of_dict.Count == 1);
		}


	}

}