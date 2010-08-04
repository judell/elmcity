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

using System.Net;
using NUnit.Framework;

namespace CalendarAggregator
{
    [TestFixture]
    public class FeedRegistryTest
    {
        string id = Configurator.testid;
        FeedRegistry fr;
        //int testcount = 7;
        bool testbool = true;
        string testscore = "0";
        string testurl = "testurl";
        string testsource = "testsource";
        string statsfile = "ical_stats.json";
        string blobhost = ElmcityUtils.Configurator.azure_blobhost;
        string containername;

        public FeedRegistryTest()
        {
            containername = this.id;
            fr = new FeedRegistry(this.id);
            fr.AddFeed(testurl, testsource);
        }

        [Test]
        public void SerializeStatsYieldsHttpCreated()
        {
            fr.stats[testurl].valid = testbool;
            fr.stats[testurl].score = testscore;
            var response = fr.SerializeIcalStatsToJson();
            Assert.AreEqual(HttpStatusCode.Created, response.HttpResponse.status);
        }

        [Test]
        public void DeserializeStatsYieldsExpectedValue()
        {
            SerializeStatsYieldsHttpCreated();
            var dict = FeedRegistry.DeserializeIcalStatsFromJson(blobhost, containername, statsfile);
            Assert.AreEqual(testscore, dict[testurl].score);
        }


    }
}
