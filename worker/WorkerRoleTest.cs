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
using System.Net;
using NUnit.Framework;
using System.Collections.Generic;

namespace CalendarAggregator
{
	[TestFixture]
	public class WorkerRoleTest
	{

		const string test_id = "elmcity";

		[Test]
		public void UpdateWrdForIdSucceeds()
		{
			var delicious = Delicious.MakeDefaultDelicious();

			var test_tag = "test_update_wrd";
			var test_val = System.DateTime.UtcNow.Ticks.ToString();

			var id = "elmcity";
			// construct a wrd for test hub
			var wrd = new WebRoleData(true, id);

			// update metadata for a test hub in delicious

			var metadict = Delicious.FetchMetadataForIdFromDelicious(test_id).dict_response;
			metadict[test_tag] = test_val;
			var tag_string = Delicious.MetadictToTagString(metadict);
			delicious.PostDeliciousBookmark("metadata", "http://delicious.com/" + test_id + "/metadata", tag_string, Configurator.delicious_test_account, Configurator.delicious_test_password);

			// propagate update to azure table

			delicious.StoreMetadataForIdToAzure(id, merge: true, extra: new Dictionary<string, string>());

			// update the wrd structure);
			WebRoleData.UpdateCalinfoAndRendererForId(id, wrd);

			// verify it was updated

			Assert.That(wrd.calinfos[id].metadict[test_tag] == test_val);
		
		}

	}
}