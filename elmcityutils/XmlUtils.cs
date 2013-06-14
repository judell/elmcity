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
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Collections.Generic;

namespace ElmcityUtils
{
	public static class XmlUtils
	{

		public static XmlDocument XmlDocumentFromHttpResponse(HttpResponse response)
		{
			var sr = new MemoryStream(response.bytes);
			XmlDocument doc = new XmlDocument();
			try
			{
				doc.Load(sr);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "XmlDocumentFromHttpResponse", e.Message + e.StackTrace);
			}
			return doc;
		}

		public static XDocument XdocFromXmlBytes(byte[] xml)
		{
			var xdoc = new XDocument();
			if (xml.Length == 0)
				return xdoc;
			var ms = new MemoryStream(xml);
			var xr = XmlReader.Create(ms);
			try
			{
				xdoc = XDocument.Load(xr);
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "XdocFromXmlBytes", e.Message + e.StackTrace);
			}
			return xdoc;
		}

		public static string GetXeltValue(XContainer xelt, XNamespace ns, string elt_name)
		{
			var descendants = xelt.Descendants(ns + elt_name);
			if (descendants.Count() > 0)
			{
				var first = descendants.FirstOrDefault();
				var value = first.Value;
				return value;
			}
			else
			{
				return null;
			}
		}


		public static string NodeValue(XmlNode node, string xpath)
		{
			var value_node = node.SelectSingleNode(xpath);
			if (value_node != null)
				return value_node.FirstChild.Value;
			else
				return null;
		}


		public static string GetXAttrValue(XContainer xelt, XNamespace ns, string elt_name, string attr_name)
		{
			var descendants = xelt.Descendants(ns + elt_name);
			if (descendants.Count() > 0)
			{
				var first = descendants.FirstOrDefault();
				var value = first.Attribute(attr_name).Value;
				return value;
			}
			else
			{
				return null;
			}
		}

		public static string Cdataize(string str)
		{
			return string.Format("<![CDATA[{0}]]>", str);
		}

		public static string XmlEscape(string str)
		{
			return str.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
		}

		// idle for now, used to pshb-enable an odata feed
		/*
		public static string PubSubHubEnable(string atom_feed_xml, string hub_uri)
		{
		  var xml = new XmlDocument();
		  xml.LoadXml(atom_feed_xml);
		  var nsmgr = new XmlNamespaceManager(xml.NameTable);
		  var atom_namespace = TableStorage.atom_namespace.ToString();
		  nsmgr.AddNamespace("atom", atom_namespace);
		  XmlNode feed = xml.SelectSingleNode("//atom:feed", nsmgr);
		  var element = xml.CreateElement("link", atom_namespace);
		  element.SetAttribute("rel", "hub");
		  element.SetAttribute("href", hub_uri);
		  feed.InsertBefore(element, feed.FirstChild);
		  return feed.OuterXml.ToString();
		}*/

	}
}
