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
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using ElmcityUtils;
using CalendarAggregator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class ImageSelection
{
	public static string NoCurrentImageUrl = BlobStorage.MakeAzureBlobUri("admin", "NoCurrentImage.jpg").ToString();

	public static void BuildCategoryImagesForHub(string id)
	{
		var category_images = GetCategoryImages(id);  // current set of images 
		var categories = GetNonHubCategories(id);     // categories that could associate to images
		categories.Sort(StringComparer.Ordinal);
		var current_selections = GetCurrentImageSelections(id, category_images, categories, "category");
		SaveCategoryOrSourceImages("category", id, current_selections);
	}

	public static void BuildSourceImagesForHub(string id)
	{
		var ts = TableStorage.MakeDefaultTableStorage();
		var source_image_results = new ConcurrentDictionary<string, List<Dictionary<string, string>>>();
		var feeds = new List<Dictionary<string, object>>();
		var sources = new List<string>();
		var query = string.Format("$filter=PartitionKey eq '{0}' and feedurl ne ''", id);
		feeds = ts.QueryAllEntitiesAsListDict("metadata", query, 5000).list_dict_obj;
		feeds = feeds.FindAll(x => x["feedurl"].ToString().Contains("eventful.com") == false);
		feeds = feeds.OrderBy(x => x["source"].ToString()).ToList();
		sources = feeds.Select(x => x["source"].ToString()).ToList();
		sources = sources.Select(x => x.Replace("\n", "")).ToList();
		sources.Sort();
		var source_images = GetSourceImages(id);
		var current_selections = GetCurrentImageSelections(id, source_images, sources, "source");
		SaveCategoryOrSourceImages("source", id, current_selections);
	}

	private static void SaveCategoryOrSourceImages(string type, string id, Dictionary<string, string> images)
	{
		var bs = BlobStorage.MakeDefaultBlobStorage();
		var template = BlobStorage.GetAzureBlobAsString("admin", "image_selector.tmpl");
		var sb = new StringBuilder();
		Random rnd = new Random();
		sb.Append(RenderCategoryOrSourceImages(type, images, rnd));
		var html = template.Replace("__ID__", id);
		html = html.Replace("__HOST__", ElmcityUtils.Configurator.appdomain);
		html = html.Replace("__TYPE__", type);
		html = html.Replace("__BODY__", sb.ToString());
		//File.WriteAllText(@"c:\users\jon\dev\" + type + "_images_" + id + ".html", html);
		bs.PutBlob(id, type + "_images.html", html, "text/html");
	}

	public static Dictionary<string, string> GetCurrentImageSelections(string id, Dictionary<string,string> images, List<string> items, string type)
	{
		var blobname = type + "_images.json";

		var hub_selections = new Dictionary<string, string>();
		var hub_uri = BlobStorage.MakeAzureBlobUri(id, blobname);  // look for hub images
		if (BlobStorage.ExistsBlob(id, blobname))
			hub_selections = GetSelections(hub_uri);

		var global_selections = new Dictionary<string,string>();
		var global_uri = BlobStorage.MakeAzureBlobUri("admin", blobname); // acquire global images
		global_selections = GetSelections(global_uri);

		foreach (var item in items)
		{
			if (hub_selections.ContainsKey(item) == false)  // nothing selected for category or source 
				hub_selections[item] = NoCurrentImageUrl;   // force default selection

			if ( hub_selections[item] == NoCurrentImageUrl ) // if unassigned check for global assignment
			{
				if ( global_selections.ContainsKey(item) )
					hub_selections[item] = global_selections[item];
			}
		}

		return hub_selections;
	}

	private static Dictionary<string, string> GetSelections(Uri uri)
	{
		var selections = new Dictionary<string, string>();
		try
		{
			var json = HttpUtils.FetchUrl(uri).DataAsString();
			selections = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
		}
		catch (Exception e)
		{
			GenUtils.PriorityLogMsg("exception", "GetCurrentImageSelections", e.Message);
		}
		return selections;
	}

	private static List<string> GetNonHubCategories(string id)
	{
		var hubs = new List<string>();
		if (Utils.IsRegion(id))
		{
			hubs = Utils.GetIdsForRegion(id);
			hubs = hubs.Select(t => t.ToLower()).ToList();
		}

		var es = (ZonelessEventStore)ObjectUtils.GetTypedObj<ZonelessEventStore>(id, id + ".zoneless.obj");
		var categories = Utils.GetTagsForHub(es, id, Utils.TagAndCountType.nonhub, hubs);
		//categories = categories.FindAll(x => x.Contains("{") == false).ToList();
		categories = categories.FindAll(x => String.IsNullOrWhiteSpace(x) == false);
		categories.Sort(String.CompareOrdinal);
		return categories;
	}

	public static Dictionary<string,string> GetCategoryImages(string id)
	{
		var renderer = Utils.AcquireRenderer(id);
		return renderer.category_images;
	}

	public static Dictionary<string, string> GetSourceImages(string id)
	{
		var renderer = Utils.AcquireRenderer(id);
		return renderer.source_images;
	}

	/*
	public static void GetCategoryImages(ConcurrentDictionary<string, List<Dictionary<string, string>>> category_image_results, List<string> categories, int max)
	{
		Parallel.ForEach(source: categories, body: (category) =>
		{
			if (category_image_results.ContainsKey(category))
				return;
			category_image_results[category] = BingImageSearch(category, max);
		});
	}

	public static void GetSearchImages(ConcurrentDictionary<string, List<Dictionary<string, string>>> search_image_results, List<string> sources, string where, int max)
	{
		Parallel.ForEach(source: sources, body: (source) =>
		{
			search_image_results[source] = BingImageSearch(source.Replace("meetup", "").Replace("eventbrite", "").Replace("facebook", "") + " " + where, max);
		});
	}
	 */

	/*private static string RenderCategoryOrSourceImages(string type, ConcurrentDictionary<string, List<Dictionary<string, string>>> image_results, Dictionary<string, string> current_selections, Random rnd)
	{
		var sb = new StringBuilder();
		var items = image_results.Keys.ToList();
		items.Sort(StringComparer.Ordinal);
		foreach (var item in items)
		{
			try
			{
				var rand = rnd.Next(1000000).ToString();

				sb.AppendLine(string.Format("<p class=\"{0}\" style=\"font-size:xx-large\">images for {1} {2}</p>", rand, type, item));

				RenderCurrentSourceOrCategoryImage(current_selections, sb, item, rand);

				var results = image_results[item];

				foreach (var result in results)
				{
					rand = rnd.Next(1000000).ToString();
					sb.Append(RenderImageResult(item, rand, result));
				}
			}
			catch (Exception e)
			{
				Console.WriteLine("exception for " + type + " " + item + " " + e.Message);
			}

			sb.Append(String.Format(@"<div>
<p>
or search for more on <a target=""bingsearch"" href=""http://www.bing.com/images/search?qft=%2Bfilterui%3Alicense-L1&q={0}"">bing</a> or <a target=""googlesearch"" href=""http://google.com/search?tbm=isch&safe=active&tbs=sur:f&as_q={0}"">google</a>
</p>
<p>
then specify your own image URL: <input style=""width:50%"" class=""override"" name=""{0}"" onchange=""url_specified('{0}')"" value="""">
<img name=""{0}"" class=""override_image"" style=""width:140px;display:none;vertical-align: middle;margin: 20px;"" src="""">
</p>
</div>", item));

		}
		return sb.ToString();
	}*/

	private static string RenderCategoryOrSourceImages(string type, Dictionary<string, string> current_selections, Random rnd)
	{
		var sb = new StringBuilder();
		var items = current_selections.Keys.ToList();
		items.Sort(StringComparer.Ordinal);
		foreach (var item in items)
		{
			var rand = rnd.Next(1000000).ToString();
			sb.AppendLine(string.Format("<p class=\"{0}\" style=\"font-size:xx-large\">images for {1} {2}</p>", rand, type, item));
			RenderCurrentSourceOrCategoryImage(current_selections, sb, item, rand);
			sb.Append(String.Format(@"<div>
<p>
search for public domain images on <a target=""bingsearch"" href=""http://www.bing.com/images/search?qft=%2Bfilterui%3Alicense-L1&q={0}"">bing</a> or <a target=""googlesearch"" href=""http://google.com/search?tbm=isch&safe=active&tbs=sur:f&as_q={0}"">google</a>
</p>
<p>
then specify your own image URL: <input style=""width:50%"" class=""override"" name=""{0}"" onchange=""url_specified('{1}')"" value="""">
<img name=""{0}"" class=""override_image"" style=""width:140px;display:none;vertical-align: middle;margin: 20px;"" src="""">
</p>
</div>", item, item.Replace("'","\\'")));

		}
		return sb.ToString();
	}


	private static void RenderCurrentSourceOrCategoryImage(Dictionary<string, string> current_selections, StringBuilder sb, string selector, String rand)
	{
		string img_url;

		if (current_selections.ContainsKey(selector))
			img_url = current_selections[selector];
		else
			img_url = NoCurrentImageUrl;

		sb.AppendLine(string.Format(@"
<p> current image 
<div id=""{2}"">
<input class=""current_selection {2}"" onclick=""highlight_selection({2},'{3}')"" style=""white-space:nowrap"" checked type=""radio"" name=""{1}"" value=""{0}"">
<img style=""vertical-align:middle"" src=""{0}"">
</div>
</p>",
				img_url,
				selector,
				rand,
				selector.Replace("'","\\'")
				)
			);
	}

	private static string RenderImageResult(string label, String rand, Dictionary<string, string> result)
	{
		try
		{
			var rendering = string.Format(@"
<span style=""white-space:nowrap"" class=""{0}"">
<input onclick=""highlight_selection({0},'{2}')"" type=""radio"" name=""{2}"" value=""{1}"">
<img style=""width:140px;margin:20px;vertical-align:middle"" src=""{1}"">
</span>
",
		rand,
		result["Thumbnail"],
		label
		);
			return rendering;
		}
		catch
		{
			Console.WriteLine("RenderImageResult");
			return "";
		}
	}

	public static List<Dictionary<string, string>> BingImageSearch(string search_expression, int max)
	{
		HttpUtils.Wait(1);
		var url_template = "http://api.search.live.net/json.aspx?AppId=" + CalendarAggregator.Configurator.bing_api_key + "&Market=en-US&Sources=Image&Adult=Strict&Query={0}&Image.Count={1}";
		var results_list = new List<Dictionary<string, string>>();
		Uri search_url;

		search_url = new Uri(string.Format(url_template, search_expression, max));

		var page = HttpUtils.RetryHttpRequestExpectingStatus(search_url.ToString(), HttpStatusCode.OK).DataAsString();

		try
		{
			JObject o = (JObject)JsonConvert.DeserializeObject(page);

			var results_query =
				from result in o["SearchResponse"]["Image"]["Results"].Children()
				select new Dictionary<string, string>() {
							  { "Width", result.Value<int>("Width").ToString() ?? ""  },
							  { "Height", result.Value<int>("Height").ToString() ?? "" },
							  { "MediaUrl", result.Value<string>("MediaUrl").ToString() ?? "" },
							  { "Title", result.Value<string>("Title").ToString() ?? "" },
							  { "Thumbnail", result["Thumbnail"].Value<string>("Url").ToString() ?? ""},
							};

			foreach (var result in results_query)
			{
				if (result == null)
					continue;

				var w = Convert.ToDouble(result["Width"]);
				var h = Convert.ToDouble(result["Height"]);

				if (w < 140)
					continue;

				if (h >= w)
					continue;

				var ratio = w / h;

				if (ratio > 1.5)
					continue;

				results_list.Add(result);
			}
		}

		catch
		{
			Console.WriteLine("BingImageSearch: " + search_expression);
		}

		return results_list;
	}

	// http://forums.asp.net/t/1038068.aspx?C+Image+resize
	public static byte[] ResizeImageFromByteArray(int MaxSideSize, Byte[] byteArrayIn)
	{
		byte[] byteArray = null;  // really make this an error gif
		MemoryStream ms = new MemoryStream(byteArrayIn);
		byteArray = ResizeImageFromStream(MaxSideSize, ms);
		return byteArray;
	}

	public static byte[] ResizeImageFromStream(int MaxSideSize, Stream Buffer)
	{
		byte[] byteArray = null;
		try
		{
			Bitmap bitMap = new Bitmap(Buffer);
			int intOldWidth = bitMap.Width;
			int intOldHeight = bitMap.Height;

			int intNewWidth;
			int intNewHeight;

			int intMaxSide;

			if (intOldWidth >= intOldHeight)
				intMaxSide = intOldWidth;
			else
				intMaxSide = intOldHeight;

			if (intMaxSide > MaxSideSize)
			{
				//set new width and height
				double dblCoef = MaxSideSize / (double)intMaxSide;
				intNewWidth = Convert.ToInt32(dblCoef * intOldWidth);
				intNewHeight = Convert.ToInt32(dblCoef * intOldHeight);
			}
			else
			{
				intNewWidth = intOldWidth;
				intNewHeight = intOldHeight;
			}

			Size ThumbNailSize = new Size(intNewWidth, intNewHeight);
			System.Drawing.Image oImg = System.Drawing.Image.FromStream(Buffer);
			System.Drawing.Image oThumbNail = new Bitmap
				(ThumbNailSize.Width, ThumbNailSize.Height);
			Graphics oGraphic = Graphics.FromImage(oThumbNail);
			oGraphic.CompositingQuality = CompositingQuality.HighQuality;
			oGraphic.SmoothingMode = SmoothingMode.HighQuality;
			oGraphic.InterpolationMode = InterpolationMode.HighQualityBicubic;
			Rectangle oRectangle = new Rectangle
				(0, 0, ThumbNailSize.Width, ThumbNailSize.Height);

			oGraphic.DrawImage(oImg, oRectangle);
			MemoryStream ms = new MemoryStream();
			oThumbNail.Save(ms, ImageFormat.Jpeg);
			byteArray = new byte[ms.Length];
			ms.Position = 0;
			ms.Read(byteArray, 0, Convert.ToInt32(ms.Length));

			oGraphic.Dispose();
			oImg.Dispose();
			ms.Close();
			ms.Dispose();
		}
		catch (Exception e)
		{
			Console.WriteLine(e.Message);
		}
		return byteArray;
	}

	private static void BuildCategoriesForRegionSourcesForHubs(string region, string where)
	{
		ImageSelection.BuildCategoryImagesForHub(region);
		foreach (var id in Utils.GetIdsForRegion(region))
		{
			ImageSelection.BuildSourceImagesForHub(id);
		}
	}

}
