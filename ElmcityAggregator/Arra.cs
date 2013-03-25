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
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ElmcityUtils;
using Ionic.Zip;

// not central to the elmcity project, but included as a result of:
// http://blog.jonudell.net/2009/11/09/where-is-the-money-going/

namespace CalendarAggregator
{
	// encapsulate recovery.gov data for a location
	public class ArraData
	{
		public string award_description { get; set; }
		public string award_amount { get; set; }
		public string project_name { get; set; }
		public string project_description { get; set; }
		public string project_status { get; set; }
		public string funding_agency_name { get; set; }
		public string recipient_name { get; set; }
		public string award_date { get; set; }
		public string infrastructure_contact_nm { get; set; }
		public string infrastructure_contact_email { get; set; }
		public string award_number { get; set; }

		public ArraData(string award_description, string award_amount, string project_name,
			string project_description, string project_status, string funding_agency_name,
			string recipient_name, string award_date, string infrastructure_contact_nm,
			string infrastructure_contact_email, string award_number)
		{
			this.award_description = award_description;
			this.award_amount = award_amount;
			this.project_name = project_name;
			this.project_description = project_description;
			this.project_status = project_status;
			this.funding_agency_name = funding_agency_name;
			this.recipient_name = recipient_name;
			this.award_date = award_date;
			this.infrastructure_contact_nm = infrastructure_contact_nm;
			this.infrastructure_contact_email = infrastructure_contact_email;
			this.award_number = award_number;
		}
	}

	// view recovery.gov data for a location
	public static class Arra
	{

		public static string MakeArraDescription(int id, string description)
		{
			var max = 140;
			if (description.Length <= max)
				return description;
			var ida = id.ToString() + 'a';
			var idb = id.ToString() + 'b';
			var blurb = description.Substring(0, max);
			blurb = String.Format(@"<span id=""{0}"">{1} <a href=""javascript:toggle({2})"">...more...</a></span>",
				ida, blurb, id);
			description = String.Format(@"<span style=""display:none"" id=""{0}"">{1} <a href=""javascript:toggle({2})"">...less...</a></span>",
				idb, description, id);
			return blurb + description;
		}

		public static string MakeArraPage(string state, string town, string year, string quarter)
		{
			state = state ?? "nh";
			town = town ?? "keene";
			year = year ?? "11";
			quarter = quarter ?? "4";

			var awards = ArraAwardsForYearQuarterStateTown(year, quarter, state, town);

			var summary_template = @"<table cellpadding=""10"">
<tr><td># of awards</td><td align=""right"">{0:0,0}</td><td></td></tr>
<tr><td># of awards with descriptions</td><td align=""right"">{1:0,0}</td><td>{2:0%}</td></tr>
<tr><td># of awards without descriptions</td><td align=""right"">{3:0,0}</td><td>{4:0%}</td></tr>
<tr><td>$ of awards</td><td align=""right"">{5:0,0}</td><td></td></tr>
<tr><td>$ of awards with descriptions</td><td align=""right"">{6:0,0}</td><td>{7:0%}</td></tr>
<tr><td>$ of awards without descriptions</td><td align=""right"">{8:0,0}</td><td>{9:0%}</td></tr>
</table>
";

			var pct_count_with_desc = (float)(int)awards["awards_with_desc"] / (int)awards["award_count"];
			var pct_count_without_desc = (float)(int)awards["awards_without_desc"] / (int)awards["award_count"];
			var pct_dollars_with_desc = (float)awards["awards_with_desc_sum"] / (float)awards["awards_sum"];
			var pct_dollars_without_desc = (float)awards["awards_without_desc_sum"] / (float)awards["awards_sum"];

			var summary = String.Format(summary_template,
			   awards["award_count"],
			   awards["awards_with_desc"],
			   pct_count_with_desc,
			   awards["awards_without_desc"],
			   pct_count_without_desc,
			   awards["awards_sum"],
			   awards["awards_with_desc_sum"],
			   pct_dollars_with_desc,
			   awards["awards_without_desc_sum"],
			   pct_dollars_without_desc
			   );

			var details_header = @"<tr>
<td align=""center"">award</td>
<td align=""center"">amount</td>
<td align=""center"">funding agency</td>
<td align=""center"">recipient</td>
<td align=""center"">description</td>
</tr>";

			var details_row_template = @"<tr>
<td>{0} (<a href=""http://google.com/search?q=%22{0}%22"">google</a>, <a href=""http://bing.com/search?q=%22{0}%22"">bing</a>)</td>
<td align=""right"">{1:0,0}</td>
<td>{2}</td>
<td>{3}</td>
<td>{4}</td>
</tr>
";
			var details = new StringBuilder();
			details.Append(@"<table cellpadding=""5"">" + details_header);
			var id = 0;
			foreach (var arra_data in (List<ArraData>)awards["awards"])
			{
				id++;
				details.Append(String.Format(@"<tr><td style=""padding:0"" colspan=""4""><a name=""{0}""></td></tr>", id));
				var tr = String.Format(details_row_template,
						arra_data.award_number,
						float.Parse(arra_data.award_amount),
						arra_data.funding_agency_name,
						arra_data.recipient_name,
						MakeArraDescription(id, arra_data.award_description)
					   );
				details.Append(tr);
			}
			details.Append("</table>");

			var style = @"<style>
td { border: 0.5px solid #E8E8E8;}
table { border-spacing: 0 }
</style>";

			var script = @"<script>
function toggle(id)
  {
  var ida = '#' + id + 'a';
  var idb = '#' + id + 'b';
  var display = $(ida).get(0).style.display;
  if ( display == 'none' )
    {
    $(idb).hide();
    $(ida).show();
    }
  else
    {
    $(ida).hide();
    $(idb ).show();
    }
  location.href = ""#"" + id;
  }
</script>";

			var html = new StringBuilder();
			html.Append(String.Format(@"<html>
<head>
<link type=""text/css"" rel=""stylesheet"" href=""http://jonudell.net/css/elmcity.css"">
<script src=""http://elmcity.blob.core.windows.net/admin/jquery-1.3.2.js"" type=""text/javascript""></script>
<title>arra recipient-reported data for {0} {1} Y20{2} Q{3}</title>
{4} 
</head>
<body>
<p><form method=""get"" action=""/arra""> 
arra <a href=""http://www.recovery.gov/FAQ/Pages/DownloadCenter.aspx"">recipient-reported data</a> for 
<input name=""town"" value=""{0}""> 
<input style=""width:30"" name=""state"" value=""{1}""> 
 Y20<input style=""width:30"" name=""year"" value=""{2}""> 
 Q<input style=""width:20"" name=""quarter"" value=""{3}"">
 <input type=""submit"" value=""go""> 
</p>
{5}
{6}
</body>
</html>",
			town,
			state,
			year,
			quarter,
			style,
			script,
			summary + details
			));

			//File.WriteAllText(@"c:\users\jon\dev\test.html", html);
			return html.ToString();

		}

		public static string ArraValue(XElement e)
		{
			if (e == null)
				return "-";
			else
				return (string)e.Value;
		}

		public static Dictionary<string, object> ArraAwardsForYearQuarterStateTown(string year, string quarter, string state, string town)
		{
			town = town.ToLower();
			state = state.ToUpper();
			var url = new Uri(String.Format("http://recovery.download.s3-website-us-east-1.amazonaws.com/Y{0}Q{1}/{2}_Y{3}Q{4}.xml.zip",
				year, quarter, state, year, quarter));
			var rsp = HttpUtils.FetchUrl(url);
			var zs = new MemoryStream(rsp.bytes);
			var zip = ZipFile.Read(zs);
			var ms = new MemoryStream();
			var entry = zip.Entries[0];
			entry.Extract(ms);
			ms.Seek(0, 0);
			byte[] data = new byte[entry.UncompressedSize];
			Utils.ReadWholeArray(ms, data);
			var doc = XmlUtils.XdocFromXmlBytes(data);

			var awards = from row in doc.Descendants("Table")
						 where row.Element("pop_city") != null && row.Element("pop_city").Value.ToLower() == town
						 orderby float.Parse(row.Element("local_amount").Value) descending
						 select new ArraData(
							 ArraValue(row.Element("award_description")),
							 ArraValue(row.Element("local_amount")),
							 ArraValue(row.Element("project_name")),
							 ArraValue(row.Element("project_description")),
							 ArraValue(row.Element("project_status")),
							 ArraValue(row.Element("funding_agency_name")),
							 ArraValue(row.Element("recipient_name")),
							 ArraValue(row.Element("award_date")),
							 ArraValue(row.Element("infrastructure_contact_nm")),
							 ArraValue(row.Element("infrastructure_contact_email")),
							 ArraValue(row.Element("award_number"))
							 );


			var dict = new Dictionary<string, object>();
			dict["awards"] = awards.ToList();
			dict["award_count"] = awards.Count();

			var awards_with_desc = from row in awards
								   where (string)row.award_description != "-"
								   select new { row };

			dict["awards_with_desc"] = awards_with_desc.Count();

			dict["awards_without_desc"] = (int)dict["award_count"] - (int)dict["awards_with_desc"];

			var amounts = from row in awards
						  select float.Parse(row.award_amount.ToString());
			dict["awards_sum"] = amounts.Sum();

			var amounts_with_desc = from row in awards_with_desc
									select float.Parse(row.row.award_amount.ToString());
			dict["awards_with_desc_sum"] = amounts_with_desc.Sum();

			dict["awards_without_desc_sum"] = (float)dict["awards_sum"] - (float)dict["awards_with_desc_sum"];

			return dict;
		}

	}
}
