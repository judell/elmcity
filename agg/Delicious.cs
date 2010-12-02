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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ElmcityUtils;

namespace CalendarAggregator
{
    // encapsulates:
    // 1. a dict<str,str> derived from a delicious url whose bookmark carries name=value tags
    // 2. the http response from looking up that url
    // 3. success/failure for the lookup
    public class DeliciousResponse
    {
        public HttpResponse http_response { get; set; }
        public Dictionary<string, string> dict_response { get; set; }
        public int int_response { get; set; }
        public Delicious.MetadataQueryOutcome outcome { get; set; }

        public DeliciousResponse(HttpResponse http_response, Dictionary<string, string> dict_response, Delicious.MetadataQueryOutcome outcome)
        {
            this.http_response = http_response;
            this.dict_response = dict_response;
			this.outcome = outcome;
        }

        public DeliciousResponse(HttpResponse http_response, int int_response, Delicious.MetadataQueryOutcome outcome)
        {
            this.http_response = http_response;
            this.int_response = int_response;
			this.outcome = outcome;
        }

    }

    // methods for looking up delicious bookmarks, deriving hub registries and hub metadata from them,
    // caching the results to azure, and retrieving results from the azure cache
    public class Delicious
    {

        public const string eventful_url_template = "http://eventful.com/users/USERNAME/created/events";
        public const string apibase = "https://api.del.icio.us/v1";
        //public const string deliciousbase = "http://www.delicious.com/";
        private string username;
        private string password;
        private TableStorage ts;
        public static string ts_table = "metadata";
        private string ts_master_pk = "master";
        private string ts_master_accounts_rk = "accounts";

        private const string test_prop_prefix = Configurator.test_metadata_property_key_prefix;
        private const string test_prop_key = test_prop_prefix + Configurator.test_metadata_property_key;
        private const string test_prop_value = Configurator.test_metadata_property_value;

        public Delicious(string username, string password)
        {
            this.username = username;
            this.password = password;
            this.ts = TableStorage.MakeDefaultTableStorage();
        }

        public static Delicious MakeDefaultDelicious()
        {
            return new Delicious(Configurator.delicious_master_account, "");
        }

        # region feeds

        public Dictionary<string, string> LoadFeedsFromAzureTableForId(string id)
        {
            var q = string.Format("$filter=(PartitionKey eq '{0}' and feedurl ne '' )", id);
            var qdicts = (List<Dictionary<string, object>>)ts.QueryEntities("metadata", q).response;
            var feed_dict = new Dictionary<string, string>();
            foreach (var dict in qdicts)
            {
                var source = (string)dict["source"];
                var feedurl = (string)dict["feedurl"];
                feed_dict[feedurl] = source;
            }
            return feed_dict;
        }

        public static Dictionary<string, string> FetchFeedsForIdWithTagsFromDelicious(string id, string tags)
        {
            return Delicious.LinkTitleDictFromDeliciousRssQuery(id, Configurator.delicious_rssbase, tags);
        }

        # endregion feeds

        #region metadata for feeds

        public Dictionary<string, string> LoadFeedMetadataFromAzureTableForFeedurlAndId(string feedurl, string id)
        {
            string rowkey = Utils.MakeSafeRowkeyFromUrl(feedurl);
            var q = string.Format("$filter=(PartitionKey eq '{0}' and RowKey eq '{1}')", id, rowkey);
            return TableStorage.QueryForSingleEntityAsDictStr(ts, "metadata", q);
        }

        public static DeliciousResponse FetchFeedMetadataFromDeliciousForFeedurlAndId(string feedurl, string id)
        {
            return FetchMetadataFromDeliciousForUrlAndId(feedurl, id);
        }

        public Dictionary<string,string> StoreFeedAndMaybeMetadataToAzure(string id, FeedRegistry fr, string feedurl)
        {
            string rowkey = Utils.MakeSafeRowkeyFromUrl(feedurl);
			
			var dict = new Dictionary<string, string>();

            // this response should at least include url=http://... so all events in the feed have
            // a default link
            var response = FetchFeedMetadataFromDeliciousForFeedurlAndId(feedurl, id);
			if (response.outcome != MetadataQueryOutcome.Success) // delicious failed, don't overwrite cached info in azure table
				return dict;
            dict = response.dict_response;
            dict = GenUtils.DictTryAddStringValue(dict, "feedurl", feedurl);
            dict = GenUtils.DictTryAddStringValue(dict, "source", fr.feeds[feedurl]);
            TableStorage.UpdateDictToTableStore(ObjectUtils.DictStrToDictObj(dict), ts_table, id, rowkey);
			return dict;
        }

        // used by worker role (UpdateFeedsToAzure) when caching registry + metadata from delicious to azure table
        public List<Dictionary<string,string>> StoreFeedsAndMaybeMetadataToAzure(FeedRegistry fr_delicious, string id)
        {
            string feedurl = "";

			var dicts = new List<Dictionary<string, string>>();

            try
            {
                foreach (string key in fr_delicious.feeds.Keys)
                {
                    feedurl = key;
                    // this will always send source= and feedurl= 
                    // if extra metadata is specified in delicious, e.g. url= and category=, that will go too
                    var dict = StoreFeedAndMaybeMetadataToAzure(id, fr_delicious, feedurl);
					dicts.Add(dict);
                }
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "StoreFeedsAndMaybeMetadataToAzure (reload) " + id + " : " + feedurl, e.Message + e.StackTrace);
            }

			return dicts;
        }

        /*
        public void PurgeDeletedFeedsFromAzure()
        {
            foreach (var id in LoadHubIdsFromAzureTable())
            {
                PurgeDeletedFeedFromAzure(id);
            }
        }*/

        // if a curator deletes a feed from the delicious registry, it 
        // needs to also get deleted from the corresponding azure table
        public void PurgeDeletedFeedFromAzure(FeedRegistry fr_delicious, string id)
        {
            FeedRegistry fr_azure = new FeedRegistry(id);

            fr_azure.LoadFeedsFromAzure();

            var delicious_feeds = fr_delicious.feeds.Keys;
            var azure_feeds = fr_azure.feeds.Keys;

            var diff = azure_feeds.Except(delicious_feeds).ToList();

            if (diff.Count > 0)
            {
                GenUtils.LogMsg("info", "PurgeDeletedFeeds", String.Join(",", diff.ToArray()));
                foreach (var feedurl in diff)
                {
                    var source = fr_azure.feeds[feedurl];
                    try
                    {
                        GenUtils.LogMsg("info", "PurgeDeletedFeeds: " + id, source);
                        string rowkey = Utils.MakeSafeRowkeyFromUrl(feedurl);
                        var ts_response = ts.MaybeDeleteEntity(ts_table, id, rowkey);
                    }
                    catch (Exception e)
                    {
                        GenUtils.LogMsg("exception", "PurgeDeletedFeeds: " + id + ", " + source, e.Message + e.StackTrace);
                    }
                }
            }
        }

        # endregion metadata for feeds

        #region ids

        public List<string> LoadHubIdsFromAzureTable()
        {
            var q = string.Format("$filter=(PartitionKey eq '{0}' and RowKey eq '{1}')", "master", "accounts");
            var dict = TableStorage.QueryForSingleEntityAsDictStr(ts, ts_table, q);
            var str_list = dict["list"];
            var array = str_list.Split(',');
            var list = array.ToList();
            return list;
        }

        public List<string> FetchHubIdsFromDelicious()
        {
            var ids = new List<string>();

            var url = Configurator.delicious_curated_hubs_query;
            Utils.Wait(Configurator.delicious_delay_seconds);
            var response = DoAuthorizedRequest(url);

            if (response.status != HttpStatusCode.OK)
                return ids;

            var xdoc = XmlUtils.XdocFromXmlBytes(response.bytes);

            var hrefs = from e in xdoc.Descendants("post")
                        select e.Attribute("href");

			foreach (string href in hrefs)
			{
				var re = new Regex("/([^/]+)/*$");
				var id = re.Match(href).Groups[1].ToString();
				ids.Add(id);
			}

            return ids;

            /* e.g.:
             <posts user="judell" tag="calendarcuration">
             <post href="http://delicious.com/robg3" hash="c9f2483fc601efdace60e1ff1ab193c9" description="robg3's Bookmarks on Delicious (cambridge,ma hub)" tag="calendarcuration" time="2010-06-24T12:50:28Z" extended=""/>
             <post href="http://delicious.com/utahvalleybusinessblog" hash="a6dc9ce5c7f670a1d20077b854b955fb" description="utahvalleybusinessblog's Bookmarks on Delicious" tag="calendarcuration" time="2010-04-29T21:24:02Z" extended=""/>
             </posts>
            */
        }

        public void StoreHubIdsToAzureTable()
        {
            var dict = new Dictionary<string, object>();
            var master_delicious = new Delicious(Configurator.delicious_master_account, Configurator.delicious_master_password);
            var list = master_delicious.FetchHubIdsFromDelicious();
            if (list.Count() == 0)
            {
                GenUtils.LogMsg("warning", "StoreHubIdsToAzureTable: could not fetch ids", null);
                return;
            }
            string[] items = list.ToArray();
            dict["list"] = string.Join(",", items);
            TableStorage.UpdateDictToTableStore(dict, table: ts_table, partkey: ts_master_pk, rowkey: ts_master_accounts_rk);
        }

        #endregion

        #region metadata for ids

        public Dictionary<string, string> LoadMetadataForIdFromAzureTable(string id)
        {
            var q = string.Format("$filter=(PartitionKey eq '{0}' and RowKey eq '{1}')", id, id);
            var dict = TableStorage.QueryForSingleEntityAsDictStr(ts, "metadata", q);
            return dict;
        }

        public static DeliciousResponse FetchMetadataForIdFromDelicious(string id)
        {
            var metadata_url = string.Format("http://delicious.com/{0}/metadata", id);
            var response = FetchMetadataFromDeliciousForUrlAndId(metadata_url, id);
			if (response.outcome != MetadataQueryOutcome.Success) // allow www. variant
			{
				metadata_url = string.Format("http://www.delicious.com/{0}/metadata", id);
				response = FetchMetadataFromDeliciousForUrlAndId(metadata_url, id);
			}
            if (response.outcome != MetadataQueryOutcome.Success )
                return response;
            var dict = response.dict_response;
            var response2 = FetchFeedCountForIdWithTags(id, Configurator.delicious_trusted_ics_feed);
			if (response2.outcome != MetadataQueryOutcome.Success) 
                return response2;
            var count = response2.int_response;
            // expand the delicious metadata with this computed value for the hub's count of feeds
            dict["feed_count"] = count.ToString();
            return new DeliciousResponse(response2.http_response, dict_response: dict, outcome: response2.outcome);
        }

		public enum MetadataQueryOutcome { Success, Error, NoRedirect, HttpRequestFailed, NotBookmarkedOrTagged, Blocked }

        // this implements the delicious "Look up an URL" feature, which interactively looks like
        // a 1-step process but behind the scenes involves redirection.
        // so, given the feed url http://www.google.com/calendar/ical/keeneastronomy%40gmail.com/public/basic.ics,
        // a GET of http://delicious.com/url/view?url=http%3A%2F%2Fwww.google.com%2Fcalendar%2Fical%2Fkeeneastronomy%2540gmail.com%2Fpublic%2Fbasic.ics 
        // yields Location: http://delicious.com/url/8bf9fae384ed3c765540e1103b967237, which is the canonical
        // delicious url for the bookmark. but that's a web page, and we want to read its data in a structured
        // way, so we convert to the rss variant of the url, thus:
        // http://feeds.delicious.com/v2/rss/url/8bf9fae384ed3c765540e1103b967237
        // the "feed" will have only one entry: a packet of xml we can read more easily than 
        // scraping the html
        public static DeliciousResponse FetchMetadataFromDeliciousForUrlAndId(string metadata_url, string id)
        {
            var dict = new Dictionary<string, string>();

            metadata_url = System.Uri.EscapeDataString(metadata_url);

            // lookup url in delicious

            var url = new Uri(string.Format("http://www.delicious.com/url/view?url={0}", metadata_url));

            Utils.Wait(Configurator.delicious_delay_seconds);
            var http_response = HttpUtils.FetchUrlNoRedirect(url);

            // bail if not found

            if (http_response.headers.ContainsKey("Location") == false)
                return new DeliciousResponse(http_response, dict, outcome: MetadataQueryOutcome.NoRedirect);

			MetadataQueryOutcome outcome = MetadataQueryOutcome.Success;

            // now follow location header to internal url

            try
            {
                var location = http_response.headers["Location"];

                // form corresponding rss url

                var url_id = location.Replace("http://www.delicious.com/url/", "");
                url = new Uri(string.Format("http://feeds.delicious.com/v2/rss/url/{0}", url_id));

                Utils.Wait(Configurator.delicious_delay_seconds);
                http_response = HttpUtils.FetchUrl(url);

                if (http_response.status != HttpStatusCode.OK) // bail if delicious failed
                    return new DeliciousResponse(http_response, dict, outcome: MetadataQueryOutcome.HttpRequestFailed);

                // read metadata (if any)

                var xdoc = XmlUtils.XdocFromXmlBytes(http_response.bytes);

				string domain = string.Format("http://www.delicious.com/{0}/", id);
                var categories = from category in xdoc.Descendants("category")
                                 where category.Attribute("domain").Value.ToLower() == domain.ToLower()
                                 select new { category.Value };

                foreach (var category in categories)
                {
                    var key_value = GenUtils.RegexFindKeyValue(category.Value);
                    if (key_value.Count == 2)
                        dict[key_value[0]] = key_value[1].Replace('+', ' ');
                }

				if (dict.Keys.Count == 0 && metadata_url.EndsWith("metadata"))
				{
					outcome = MetadataQueryOutcome.NotBookmarkedOrTagged;
					GenUtils.LogMsg("info", "FetchMetadataFromDeliciousForUrlAndId: " + id, "metadata url not found or has no tags");
				}
            }
            catch (Exception e)
            {
				outcome = MetadataQueryOutcome.Error;
                GenUtils.LogMsg("exception", "FetchMetadataFromDeliciousForUrlAndId: " + metadata_url, e.Message + e.StackTrace);
            }

            var blocked = (http_response.DataAsString().Contains(Configurator.delicious_blocked_message) ? true : false );
			if (blocked)
			{
				outcome = MetadataQueryOutcome.Blocked;
				GenUtils.LogMsg("warning", Configurator.delicious_blocked_message, null);
			}

            return new DeliciousResponse(http_response, dict, outcome: outcome);

            /* e.g.:
            <item>
            <title>[from elmcity] Keene Astronomy</title>
            <pubDate>Sun, 16 Aug 2009 14:41:50 +0000</pubDate>
            <guid isPermaLink="false">http://delicious.com/url/8bf9fae384ed3c765540e1103b967237#elmcity</guid>
            <link>http://www.google.com/calendar/ical/keeneastronomy%40gmail.com/public/basic.ics</link>
            <dc:creator><![CDATA[elmcity]]></dc:creator>
            <comments>http://delicious.com/url/8bf9fae384ed3c765540e1103b967237</comments>
            <wfw:commentRss>http://feeds.delicious.com/v2/rss/url/8bf9fae384ed3c765540e1103b967237</wfw:commentRss>
            <source url="http://feeds.delicious.com/v2/rss/elmcity">elmcity's bookmarks</source>
            <category domain="http://delicious.com/elmcity/">trusted</category>
            <category domain="http://delicious.com/elmcity/">ics</category>
            <category domain="http://delicious.com/elmcity/">feed</category>
            <category domain="http://delicious.com/elmcity/">url=http://www.keeneastronomy.org/index.html</category>
            <category domain="http://delicious.com/elmcity/">category=astronomy</category>
            </item>
             */
        }

		/*
		public static DeliciousResponse FetchMetadataFromDeliciousForUrlAndId(string metadata_url, string id)
		{
			var completed_delegate = new GenUtils.Actions.CompletedDelegate<DeliciousResponse, Object>(CompletedIfOutcomeIsSuccess);
			return GenUtils.Actions.Retry<DeliciousResponse>(delegate() { return _FetchMetadataFromDeliciousForUrlAndId(metadata_url, id); }, completed_delegate, completed_delegate_object: null, wait_secs: 5, max_tries: 10, timeout_secs: TimeSpan.FromSeconds(100));
		}*/

		public static bool CompletedIfOutcomeIsSuccess(DeliciousResponse r, Object o)
		{
			return (r.outcome == MetadataQueryOutcome.Success);
		}

		/*
        public void StoreMetadataForIdToAzure(string id, bool merge, Dictionary<string, string> extra)
        {
            var response = FetchMetadataForIdFromDelicious(id);
            if (response.outcome != MetadataQueryOutcome.Success)
            {
                GenUtils.LogMsg("warning", "StoreMetadataForIdToAzure: " + id, "could not fetch");
                return;
            }
            response.dict_response = InjectTestKeysAndVals(response.dict_response, extra);
            var dict_obj = ObjectUtils.DictStrToDictObj(response.dict_response);
            if (merge == true)
                TableStorage.UpmergeDictToTableStore(dict_obj, table: ts_table, partkey: id, rowkey: id);
            else
                TableStorage.UpdateDictToTableStore(dict_obj, table: ts_table, partkey: id, rowkey: id);
        }
		*/

		public Dictionary<string,string> StoreMetadataForIdToAzure(string id, bool merge, Dictionary<string,string> extra)
		{
			var response = FetchMetadataForIdFromDelicious(id);
			if (response.outcome != MetadataQueryOutcome.Success)
			{
				GenUtils.LogMsg("warning", "StoreMetadataForIdToAzure: " + id, "could not fetch");
				return new Dictionary<string, string>();
			}
			response.dict_response = InjectTestKeysAndVals(response.dict_response, extra);
			var dict_obj = ObjectUtils.DictStrToDictObj(response.dict_response);
			if (merge == true)
				TableStorage.UpmergeDictToTableStore(dict_obj, table: ts_table, partkey: id, rowkey: id);
			else
				TableStorage.UpdateDictToTableStore(dict_obj, table: ts_table, partkey: id, rowkey: id);
			return response.dict_response;
		}

        public static DeliciousResponse FetchFeedCountForIdWithTags(string id, string tags)
        {
            /*  What: Get count of feeds for, e.g., delicious.com/elmcity/trusted+ics+feed
             *  Why: Could also get this from FetchFeeds, but avoiding dependency on that
             *       method's 100-item limit.
             */
            int count = 0;
			var outcome = Delicious.MetadataQueryOutcome.Success;
            HttpResponse http_response = new HttpResponse(HttpStatusCode.Unused, message: "FetchFeedCountForIdWithTags", bytes: new byte[0], headers: new Dictionary<string, string>());
            try
            {
                var url = new Uri(string.Format("{0}/{1}/{2}", Configurator.delicious_base, id, tags));
                Utils.Wait(Configurator.delicious_delay_seconds);
                http_response = HttpUtils.FetchUrl(url);
				if (http_response.status != HttpStatusCode.OK)
					outcome = MetadataQueryOutcome.HttpRequestFailed;
				else if (http_response.DataAsString().Contains(Configurator.delicious_blocked_message))
					outcome = MetadataQueryOutcome.Blocked;
				else
				{
					var page = http_response.DataAsString();
					// looking for, e.g.: <span id="tagScopeCount">37</span>
					var str_count = GenUtils.RegexFindGroups(page, @"<span id=""tagScopeCount"">(\d+)")[1];
					count = Convert.ToInt32(str_count);
				}
            }
            catch (Exception e)
            {
                GenUtils.LogMsg("exception", "delicious.count_feeds", e.Message + e.StackTrace);
            }
            return new DeliciousResponse(http_response, int_response: count, outcome: outcome);
        }

        #endregion metadata for ids

        #region metadata for venues

        public Dictionary<string, string> LoadVenueMetadataFromAzureTableForIdAndVenueUrl(string id, string venue_url)
        {
            string pk = id + "_" + "venues";
            var q = string.Format("$filter=(PartitionKey eq '{0}' and venue_url eq '{1}')", pk, venue_url);
            return TableStorage.QueryForSingleEntityAsDictStr(ts, "metadata", q);
        }

        public static DeliciousResponse FetchVenueMetadataFromDeliciousForVenueUrlAndId(string venue_url, string id)
        {
            return FetchMetadataFromDeliciousForUrlAndId(venue_url, id);
        }

        public Dictionary<string, string> StoreVenueMetadataToAzureTableForIdAndVenueUrl(string id, Dictionary<string, string> metadict, string venue_url)
        {
            string pk = id + "_" + "venues";
            string rk = Utils.MakeSafeRowkeyFromUrl(venue_url);
            metadict = GenUtils.DictTryAddStringValue(metadict, "id", id);
            metadict = GenUtils.DictTryAddStringValue(metadict, "venue_url", venue_url);
            var metadict_obj = ObjectUtils.DictStrToDictObj(metadict);
            TableStorage.UpmergeDictToTableStore(metadict_obj, table: ts_table, partkey: pk, rowkey: rk);
            return LoadVenueMetadataFromAzureTableForIdAndVenueUrl(id, venue_url);
        }

        # endregion metadata for venues

        #region exclusions

        public static Dictionary<string, string> FetchExcludedUrlsForId(string id)
        {
            return FetchFeedsForIdWithTagsFromDelicious(id, "exclude=yes");
        }

        public void StoreExcludedUrlsForIdToAzure(string id)
        {
            var excluded_urls = FetchExcludedUrlsForId(id);
            foreach (var excluded_url in excluded_urls.Keys)
                StoreExcludedUrlForIdToAzure(id, excluded_url);
        }

        public void StoreExcludedUrlForIdToAzure(string id, string excluded_url)
        {
            string rowkey = Utils.MakeSafeRowkeyFromUrl(excluded_url);
            var dict = new Dictionary<string, string>();
            dict = GenUtils.DictTryAddStringValue(dict, "excluded_url", excluded_url);
            var dict_obj = ObjectUtils.DictStrToDictObj(dict);
            TableStorage.UpdateDictToTableStore(dict_obj, table: ts_table, partkey: id, rowkey: rowkey);
        }

        public Dictionary<string, string> LoadExcludedUrlsForIdFromAzure(string id)
        {
            var q = string.Format("$filter=(PartitionKey eq '{0}' and excluded_url ne '' )", id);
            var qdicts = (List<Dictionary<string, object>>)ts.QueryEntities("metadata", q).response;
            var excluded_dict = new Dictionary<string, string>();
            foreach (var dict in qdicts)
            {
                var excluded_url = (string)dict["excluded_url"];
                excluded_dict[excluded_url] = excluded_url;
            }
            return excluded_dict;
        }

        #endregion

        #region trusted feeds

        public bool IsTrustedIcsFeed(string url)
        {
            url = Uri.EscapeDataString(url);
            var dict = FetchFeedsForIdWithTagsFromDelicious(this.username, Configurator.delicious_trusted_ics_feed);
            var re = new Regex(string.Format("({0})", url));
            return DictContainsOneMatchingKey(dict, re, url);
        }

        public HttpResponse AddTrustedIcsFeed(string name, string feedurl)
        {
            feedurl = Uri.EscapeDataString(Uri.EscapeDataString(feedurl));
            string tags = Configurator.delicious_trusted_ics_feed;
            string args = string.Format("&url={0}&tags={1}&description={2}", feedurl, tags, name);
            var url = string.Format("{0}/posts/add?{1}", apibase, args);
            return DoAuthorizedRequest(url);
        }

        public HttpResponse DeleteTrustedIcsFeed(string feedurl)
        {
            feedurl = Uri.EscapeDataString(Uri.EscapeDataString(feedurl));
            string args = string.Format("&url={0}", feedurl);
            var url = string.Format("{0}/posts/delete?{1}", apibase, args);
            return DoAuthorizedRequest(url);
        }

        #endregion trusted feeds

        #region contributors

        public bool IsTrustedEventfulContributor(string contrib)
        {
            var dict = FetchFeedsForIdWithTagsFromDelicious(this.username, Configurator.delicious_trusted_eventful_contributor);
            var re = new Regex(eventful_url_template.Replace("USERNAME", "([^/]+)"));
            var url = eventful_url_template.Replace("USERNAME", contrib);
            return DictContainsOneMatchingKey(dict, re, url);
        }

        public HttpResponse AddNewEventfulContributor(string contrib)
        {
            return AddNewContributor(contrib, "eventful");
        }

        public HttpResponse AddTrustedEventfulContributor(string contrib)
        {
            return AddTrustedContributor(contrib, "eventful");
        }

        public HttpResponse DeleteTrustedEventfulContributor(string contrib)
        {
            return DeleteTrustedContributor(contrib, "eventful");
        }

        private HttpResponse AddNewContributor(string contrib, string service)
        {
            contrib = contrib.Replace(' ', '+');
            var bookmark_url = BuildBookmarkUrl(contrib, service);
            string tags = "new+contributor+" + service;
            string args = string.Format("&url={0}&tags={1}&description={2}", bookmark_url, tags, contrib);
            var url = string.Format("{0}/posts/add?{1}", apibase, args);
            return DoAuthorizedRequest(url);
        }

        private HttpResponse AddTrustedContributor(string contrib, string service)
        {
            contrib = contrib.Replace(' ', '+');
            var bookmark_url = BuildBookmarkUrl(contrib, service);
            string tags = "trusted+contributor+" + service;
            string args = string.Format("&url={0}&tags={1}&description={2}", bookmark_url, tags, contrib);
            var url = string.Format("{0}/posts/add?{1}", apibase, args);
            return DoAuthorizedRequest(url);
        }

        private HttpResponse DeleteTrustedContributor(string contrib, string service)
        {
            contrib = contrib.Replace(' ', '+');
            string bookmark_url = BuildBookmarkUrl(contrib, service);
            string args = string.Format("&url={0}", bookmark_url);
            var url = string.Format("{0}/posts/delete?{1}", apibase, args);
            return DoAuthorizedRequest(url);
        }

        #endregion contributors

		public static string DeliciousCheck(string id)
		{
			var html = "";

			if (String.IsNullOrEmpty(id))
				return html;

			var r = FetchMetadataForIdFromDelicious(id);

			if (r.outcome == MetadataQueryOutcome.HttpRequestFailed || r.outcome == MetadataQueryOutcome.Error || r.outcome == MetadataQueryOutcome.Blocked)
			{
				html = String.Format("<p>Unable to look up http://delicious.com/{0}/metadata. Reason: {1}", id, r.outcome.ToString());
				return html;
			}

			if ( r.outcome == MetadataQueryOutcome.NotBookmarkedOrTagged )
			{
				html = String.Format("<p>There is either no delicious bookmark for http://delicious.com/{0}/metadata in the delicious account {0}, or the bookmark exists but has no tags.", id);
				return html;
			}

			if ( r.outcome == MetadataQueryOutcome.Success && ! r.dict_response.ContainsKey("where") && ! r.dict_response.ContainsKey("what"))
			{
				html = String.Format(
@"<p>Found a delicious bookmark for http://delicious.com/{0}/metadata but
did not find a where= or what= tag. If you're creating a place hub you 
need a tag that says where=city,st or you you're creating a topic hub you need 
a tag that says what=topic",
					id);
				return html;
			}

			if ( r.outcome == MetadataQueryOutcome.Success && r.dict_response.ContainsKey("where") || ! r.dict_response.ContainsKey("what"))
			{
				html = "<table>";
				html += String.Format(
 @"<p>Found a delicious bookmark for http://delicious.com/{0}/metadata with 
this information: ",
					 id);
				foreach (var key in r.dict_response.Keys)
					html += String.Format("<tr><td>{0}</td><td>{1}</td></tr>", key, r.dict_response[key]);
				html += "</table>";

				var d = Delicious.MakeDefaultDelicious();

				var azure_metadata = d.LoadMetadataForIdFromAzureTable(id);
				if (azure_metadata.Keys.Count > 0)
				{
					html += "<p>Here is the permanent record, along with extra info added by the elmcity service: ";
					html += "<table>";
					foreach (var key in azure_metadata.Keys)
						html += String.Format("<tr><td>{0}</td><td>{1}</td></tr>", key, azure_metadata[key]);
					html += "</table>";
				}

				var feeds = d.LoadFeedsFromAzureTableForId(id);
				if (feeds.Keys.Count > 0)
				{
					html += "<p>Here are the trusted iCalendar feeds: ";
					html += "<table>";
					foreach (var key in feeds.Keys)
						html += String.Format("<tr><td>{0}</td><td>{1}</td></tr>", feeds[key], key);
					html += "</table>";
				}

				return html;
			}

			GenUtils.LogMsg("warning", "DeliciousCheck", "unexpected case");
			TwitterApi.SendTwitterDirectMessage(Configurator.delicious_master_account, "DeliciousCheck: unexpected case for: " + id);
			html = "<p>Sorry, there was a problem. It has been logged and will be investigated.";
			return html;
		}

        public static Dictionary<string, string> LinkTitleDictFromDeliciousRssQuery(string account, string rssbase, string tags)
        /*   
         *   What: Get list of feeds for a delicious id
         *   How: Read, e.g., http://feeds.delicious.com/v2/rss/elmcity/trusted+ics+tags
         *   Issue: 100 is a hard-wired limit!
         */
        {
            var dict = new Dictionary<string, string>();
            var url = new Uri(String.Format("{0}/{1}/{2}?count=100", rssbase, account, tags));
            Utils.Wait(Configurator.delicious_delay_seconds);
            var response = HttpUtils.FetchUrl(url);
            var xdoc = XmlUtils.XdocFromXmlBytes(response.bytes);
            var items = from item in xdoc.Descendants("item")
                        select new
                        {
                            Title = item.Element("title").Value,
                            Link = item.Element("link").Value,
                        };
            foreach (var item in items)
                dict[item.Link] = item.Title;
            return dict;
        }

        private static bool DictContainsOneMatchingKey(Dictionary<string, string> dict, Regex re, string url)
        {
            var keys = dict.Keys.ToList();
            var matched = keys.FindAll(x => re.Match(x).Groups[0].Value == url);
            return matched.Count == 1;
        }

        private static string BuildBookmarkUrl(string contrib, string service)
        {
            string bookmark_url = null;

            switch (service)
            {
                case "eventful":
                    bookmark_url = eventful_url_template.Replace("USERNAME", contrib);
                    break;
                default:
                    //Logger.InfoLog("Unexpected: " + service);
                    break;
            }
            return bookmark_url;
        }

        private HttpResponse DoAuthorizedRequest(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
            return HttpUtils.DoAuthorizedHttpRequest(request, this.username, this.password, new byte[0]);
        }

        private static Dictionary<string, string> InjectTestKeysAndVals(Dictionary<string, string> dict, Dictionary<string, string> extra)
        {
            if (extra != null)
                foreach (var key in extra.Keys)
                    if (dict.ContainsKey(key))
                        dict[key] = extra[key];
                    else
                        dict.Add(key, extra[key]);
            return dict;
        }


    }
}

#region doc
/*

Accounts:

PK = "master"
RK = "accounts"

Account metadata:

PK = elmcity
RK = elmcity

Feed metadata:

PK = elmcity
RK = base64-encoded homeurl for feed
 
Venue metadata

PK = eventfulvenues
RK = base64-encoded venue url
id = id
 
<m:properties>
   --- added ---
   <d:PartitionKey>eventfulvenues</d:PartitionKey>
   <d:RowKey>aAAHYhdasSERSaa==</d:RowKey>
   <d:id>elmcity</d:id>
   <d:venue_url>http://eventful.com/...</d:venue_url>
   ---- inherited from delicious ----
   <d:venue>eventful</d:venue>
   <d:category>animals,pets</d:category>
</m:properties>



*/
#endregion