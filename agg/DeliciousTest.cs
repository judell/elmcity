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
using System.Xml.Linq;
using ElmcityUtils;
using NUnit.Framework;

namespace CalendarAggregator
{
    [TestFixture]
    public class DeliciousTest
    {
        private const string contrib = "xyzas 'dfbyas234";
        private static string delicious_account = ElmcityUtils.Configurator.azure_compute_account;
        private const string feedurl = "http://asdasdf/p908asdf/asdf?asdf=asdf&bda=jkl";
        private const string feedname = "9jc2134";

        // private const string testkey = Configurator.test_metadata_property_key;
        // private const string testvalue = Configurator.test_metadata_property_value;

        private const string test_feed_key = "category";
        private const string test_feed_value = "government";

        private const string test_hub_key = "testkey";
        private const string test_hub_value = "123";

        private const string test_prop_prefix = Configurator.test_metadata_property_key_prefix;
        private const string test_prop_key = test_prop_prefix + Configurator.test_metadata_property_key;
        private const string test_prop_value = Configurator.test_metadata_property_value;

        private const string master_account_tag = "calendarcuration";
        private const string master_account_name = "judell";

        private const string test_account_name = "elmcity";
        private const string test_delicious_password = "ec0qvr";


        private const string test_tag = "testtag";

        private string test_feedurl = "http://www.google.com/calendar/ical/cityofkeenenhmeetings%40gmail.com/public/basic.ics";
        private string test_feed_title = "City of Keene - Meetings";
        private static string test_feed_linkback = "http://www.ci.keene.nh.us/calendar/meetings-calendar";

        private string test_venue_service = "eventful";
        private string test_venue_url = "http://eventful.com/peterborough_nh/venues/harlows-pub-/V0-001-000628252-6";

        public Delicious delicious;
        //private Calinfo calinfo;
        private FeedRegistry fr;

        public DeliciousTest()
        {
            delicious = new Delicious(test_account_name, test_delicious_password);
            fr = new FeedRegistry(ElmcityUtils.Configurator.azure_compute_account);
        }

        # region eventful

        [Test]
        public void AddTrustedEventfulContributorIsSuccessful()
        {
            var response = delicious.AddTrustedEventfulContributor(contrib);
            Assert.That(IsSuccessfulDeliciousOperation(response));
            Utils.Wait(Configurator.delicious_delay_seconds);
            Assert.IsTrue(delicious.IsTrustedEventfulContributor(contrib));
            delicious.DeleteTrustedEventfulContributor(contrib);
        }

        [Test]
        public void DeleteTrustedEventfulContributorIsSuccessful()
        {
            delicious.AddTrustedEventfulContributor(contrib);
            Utils.Wait(Configurator.delicious_delay_seconds);
            HttpResponse response = delicious.DeleteTrustedEventfulContributor(contrib);
            Assert.AreEqual(HttpStatusCode.OK, response.status);
            Assert.That(IsSuccessfulDeliciousOperation(response));
        }

        #endregion eventful

        #region ics

        [Test]
        public void AddTrustedIcsFeedIsSuccessful()
        {
            HttpResponse response = delicious.AddTrustedIcsFeed(feedname, feedurl);
            Assert.AreEqual(HttpStatusCode.OK, response.status);
            Assert.That(IsSuccessfulDeliciousOperation(response));
            Utils.Wait(Configurator.delicious_delay_seconds);
            Assert.That(delicious.IsTrustedIcsFeed(feedurl));
            delicious.DeleteTrustedIcsFeed(feedurl);
        }

        [Test]
        public void DeleteTrustedIcsFeedIsSuccessful()
        {
            delicious.AddTrustedIcsFeed(feedname, feedurl);
            Utils.Wait(Configurator.delicious_delay_seconds);
            HttpResponse response = delicious.DeleteTrustedIcsFeed(feedurl);
            Assert.AreEqual(HttpStatusCode.OK, response.status);
            Assert.That(IsSuccessfulDeliciousOperation(response));
        }

        #endregion ics

        # region feeds

        [Test]
        public void LoadHubFeedIsSuccessful()
        {
            var dict = delicious.LoadFeedMetadataFromAzureTableForFeedurlAndId(test_feedurl, delicious_account);
            AssertMetadata(dict, "url", test_feed_linkback);
        }

        [Test]
        public void FetchHubFeedIsSuccessful()
        {
            //var dict = Delicious.FetchFeedsForIdWithTagsFromDelicious(delicious_account, test_tag);
            var response = Delicious.FetchFeedMetadataFromDeliciousForFeedurlAndId(test_feedurl, delicious_account);
			Assert.AreEqual(Delicious.MetadataQueryOutcome.Success, response.outcome);
            var dict = response.dict_response;
            AssertMetadata(dict, "url", test_feed_linkback);
        }

        [Test]
        public void CountFeedsReturnsNonZero()
        {
            var response = Delicious.FetchFeedCountForIdWithTags(test_account_name, Configurator.delicious_trusted_ics_feed);
			Assert.AreEqual(Delicious.MetadataQueryOutcome.Success, response.outcome);
            var count = response.int_response;
            Assert.Greater(count, 0);
        }

        #endregion  feeds

        #region feed metadata

        [Test]
        public void LoadFeedMetadataIsSuccessful()
        {
            var dict = delicious.LoadFeedMetadataFromAzureTableForFeedurlAndId(test_feedurl, delicious_account);
            AssertMetadata(dict, test_feed_key, test_feed_value);
        }

        [Test]
        public void FetchFeedMetadataIsSuccessful()
        {
            var response = Delicious.FetchFeedMetadataFromDeliciousForFeedurlAndId(test_feedurl, delicious_account);
			Assert.AreEqual(Delicious.MetadataQueryOutcome.Success, response.outcome);
            var dict = response.dict_response;
            AssertMetadata(dict, test_feed_key, test_feed_value);
        }

        [Test]
        public void StoreFeedMetadataIsSuccessful()
        {
            var extra_dict = MakeExtraDict(test_feed_key, test_feed_value);
            fr.AddFeed(test_feedurl, test_feed_title);
            delicious.StoreFeedAndMaybeMetadataToAzure(delicious_account, fr, test_feedurl);
            var dict = delicious.LoadMetadataForIdFromAzureTable(delicious_account);
            Assert.AreEqual(test_feed_value, dict[test_feed_key]);
        }

        #endregion feed metadata

        #region hubs

        [Test]
        public void LoadHubsIsSuccessful()
        {
            List<string> list = delicious.LoadHubIdsFromAzureTable();
            Assert.That(ContainsTestAccount(list));
        }


        [Test]
        public void FetchHubsIsSuccessful()
        {
            var master_delicious = new Delicious(Configurator.delicious_master_account, Configurator.delicious_master_password);
            var list = master_delicious.FetchHubIdsFromDelicious();
            Assert.That(ContainsTestAccount(list));
        }

        [Test]
        public void MergeStoreHubsIsSuccessful()
        {
            delicious.StoreHubIdsToAzureTable();
            var list = delicious.LoadHubIdsFromAzureTable();
            Assert.That(ContainsTestAccount(list));
        }

        [Test]
        public void UpdateStoreHubsIsSuccessful()
        {
            delicious.StoreHubIdsToAzureTable();
            var list = delicious.LoadHubIdsFromAzureTable();
            Assert.That(ContainsTestAccount(list));
        }

        #endregion hubs

        #region hub metadata

        [Test]
        public void LoadHubMetadataIsSuccessful()
        {
            var dict = delicious.LoadMetadataForIdFromAzureTable(delicious_account);
            AssertMetadata(dict, test_hub_key, test_hub_value);
        }

        [Test]
        public void FetchHubMetadataIsSuccessful()
        {
            var response = Delicious.FetchMetadataForIdFromDelicious(delicious_account);
			Assert.AreEqual(Delicious.MetadataQueryOutcome.Success, response.outcome);
            var dict = response.dict_response;
            AssertMetadata(dict, test_hub_key, test_hub_value);
        }

        [Test]
        public void MergeStoreHubMetadataIsSuccessful()
        {
            var extra_dict = MakeExtraDict(test_feed_key, test_feed_value);
            delicious.StoreMetadataForIdToAzure(delicious_account, merge: true, extra: extra_dict);
            var dict = delicious.LoadMetadataForIdFromAzureTable(delicious_account);
            Assert.AreEqual(test_feed_value, dict[test_feed_key]);
        }

        /*
        [Test]
        public void UpdateStoreAccountMetadataIsSuccessful()
        {
            var extra_dict = make_extra_dict(testkey, testvalue);
            delicious.StoreMetadataForIdToAzure(delicious_account, false, extra_dict);
            var dict = delicious.LoadMetadataForIdFromAzureTable(delicious_account);
            Assert.AreEqual(testvalue, dict[testkey]);
        }*/

        #endregion hub metadata

        #region venues

        [Test]
        public void LoadVenueMetaDataSuccessful()
        {
            var response = Delicious.FetchVenueMetadataFromDeliciousForVenueUrlAndId(test_venue_url, test_account_name);
			Assert.AreEqual(Delicious.MetadataQueryOutcome.Success, response.outcome);
            var metadict = response.dict_response;
            delicious.StoreVenueMetadataToAzureTableForIdAndVenueUrl(test_account_name, metadict, test_venue_url);
            var dict = delicious.LoadVenueMetadataFromAzureTableForIdAndVenueUrl(test_account_name, test_venue_url);
            Assert.That(dict.ContainsKey("venue") && dict["venue"] == test_venue_service);
            Assert.That(dict.ContainsKey("venue_url") && dict["venue_url"] == test_venue_url);
        }
		
		/* idle for now
        [Test]
        public void FetchVenueMetadataIsSuccessful()
        {
            var response = Delicious.FetchVenueMetadataFromDeliciousForVenueUrlAndId(test_venue_url, test_account_name);
            Assert.IsTrue(response.success);
            var dict = response.dict_response;
            Assert.That(dict.ContainsKey("venue") && dict["venue"] == test_venue_service);
        }

        [Test]
        public void StoreVenueMetadataIsSuccessful()
        {
            var response = Delicious.FetchVenueMetadataFromDeliciousForVenueUrlAndId(test_venue_url, test_account_name);
            Assert.IsTrue(response.success);
            var dict = response.dict_response;
            delicious.StoreVenueMetadataToAzureTableForIdAndVenueUrl(test_account_name, dict, test_venue_url);
            Assert.That(dict.ContainsKey("venue") && dict["venue"] == test_venue_service);
            Assert.That(dict.ContainsKey("venue_url") && dict["venue_url"] == test_venue_url);
        }
		 */


        #endregion venues

        #region helpers

        private static Dictionary<string, string> MakeExtraDict(string key, string value)
        {
            return new Dictionary<string, string>() { { key, value } };
        }

        private static void AssertMetadata(Dictionary<string, string> dict, string test_metadata_key, string test_metadata_value)
        {
            //Console.WriteLine(test_metadata_key);
            //Console.WriteLine(test_metadata_value);
            Assert.That(dict.ContainsKey(test_metadata_key));
            Assert.That(dict[test_metadata_key] == test_metadata_value);
        }

        private static bool ContainsTestAccount(List<string> list)
        {
            return list.Exists(item => item == test_account_name);

        }

        private static bool IsSuccessfulDeliciousOperation(HttpResponse response)
        {
            var xdoc = XDocument.Parse(response.DataAsString());
            bool done = false;
            foreach (XElement result in xdoc.Descendants("result"))
            {
                if ((string)result.Attribute("code") == "done")
                    done = true;
            }
            return done;
        }

        #endregion helpers

    }
}
