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
		private static string id = ElmcityUtils.Configurator.azure_compute_account;

		private FeedRegistry fr = new FeedRegistry(ElmcityUtils.Configurator.azure_compute_account);

		public MetadataTest()
		{
			fr.LoadFeedsFromAzure(FeedLoadOption.all);		
		}

		[Test]
		public void LoadFeedIsSuccessful()
		{
			var feedurl = fr.feeds.Keys.First();
			var dict = Metadata.LoadFeedMetadataFromAzureTableForFeedurlAndId(feedurl, id);
			Assert.That(dict.ContainsKey("feedurl") && dict.ContainsKey("source"));
		}

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
		}

	}
}
