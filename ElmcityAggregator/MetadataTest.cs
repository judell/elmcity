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

using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Xml.Linq;
using ElmcityUtils;
using NUnit.Framework;

namespace CalendarAggregator
{
	[TestFixture]
	public class MetadataTest
	{
		private static string id = "testKeene";
		private TableStorage ts = TableStorage.MakeDefaultTableStorage();

		[Test]
		public void LoadHubsIsSuccessful()
		{
			List<string> list = Metadata.LoadHubIdsFromAzureTable();
			Assert.That(list.Exists(x => x == id));
		}

		[Test]
		public void LoadHubMetadataIsSuccessful()
		{
			var dict = Metadata.LoadMetadataForIdFromAzureTable(id);
			Assert.That(dict.ContainsKey("type") && dict.ContainsKey("tz"));
		}

		[Test]
		public void FeedUpdateIsSuccessful()
		{
		}

		[Test]
		public void FeedAddIsSuccessful()
		{
		}

		[Test]
		public void FeedDeleteIsSuccessful()
		{
		}

	}
}
