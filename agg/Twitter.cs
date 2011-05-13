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
using System.Text;
using ElmcityUtils;

namespace CalendarAggregator
{

	public class TwitterDirectMessage
	{
		public string id { get; set; }
		public string sender_screen_name { get; set; }
		public string recipient_screen_name { get; set; }
		public string text { get; set; }

		public TwitterDirectMessage() { }

		public TwitterDirectMessage(string id, string sender_screen_name, string recipient_screen_name, string text)
		{
			this.id = id;
			this.sender_screen_name = sender_screen_name;
			this.recipient_screen_name = recipient_screen_name;
			this.text = text;
		}

		/* ObjectUtils.DictObjToObj eliminates the need for idioms like this:
          
		  public TwitterDirectMessage(Dictionary<string, object> dict_obj)
	   {
		   this.id = (string) dict_obj["id"];
		   this.sender_screen_name = (string) dict_obj["sender_screen_name"];
		   this.recipient_screen_name = (string) dict_obj["recipient_screen_name"];
		   this.text = (string) dict_obj["text"];
	   }*/
	}

	// see http://blog.jonudell.net/2009/10/21/to-elmcity-from-curator-message-start/
	public class TwitterApi
	{

		private static string ts_table = "twitter";
		private static string pk_directs = "direct_messages";
		private static TableStorage ts = TableStorage.MakeDefaultTableStorage();
		private static Dictionary<string, string> settings = null;

		private static byte[] CallTwitterApi(OAuthTwitter.Method method, string api_url, string post_data)
		{
			if (settings == null)
				settings = GenUtils.GetSettingsFromAzureTable();
			var oauth_twitter = new ElmcityUtils.OAuthTwitter();
			oauth_twitter.token = settings["twitter_access_token"];
			oauth_twitter.token_secret = settings["twitter_access_token_secret"];
			string xml = oauth_twitter.oAuthWebRequest(method, api_url, post_data);
			return Encoding.UTF8.GetBytes(xml);
		}

		public static List<TwitterDirectMessage> GetDirectMessagesFromAzure()
		{
			{
				var q = string.Format("$filter=(PartitionKey eq '{0}')", pk_directs);
				var qdicts = (List<Dictionary<string, object>>)ts.QueryEntities(ts_table, q).response;
				var messages = new List<TwitterDirectMessage>();
				foreach (var qdict in qdicts)
				{
					var message = (TwitterDirectMessage)ObjectUtils.DictObjToObj(qdict, new TwitterDirectMessage().GetType());
					messages.Add(message);
				}
				return messages;
			}
		}

		private static TableStorageResponse StoreDirectMessageToAzure(TwitterDirectMessage message)
		{
			var dict = ObjectUtils.ObjToDictObj(message);
			return TableStorage.UpdateDictToTableStore
				(
				dict,
				table: ts_table,
				partkey: pk_directs,
				rowkey: message.id
				);
		}


		public static List<TwitterDirectMessage> GetDirectMessagesFromTwitter(int count)
		{
			if (count == 0)
				count = Configurator.twitter_max_direct_messages;
			var url = String.Format("http://api.twitter.com/direct_messages.xml?count={0}", count);

			//var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
			//var response = HttpUtils.DoAuthorizedHttpRequest(request, Configurator.twitter_account, Configurator.twitter_password, new byte[0]);
			//if (response.status != HttpStatusCode.OK)
			//{
			//    GenUtils.LogMsg("warning", "Twitter.GetDirectMessages", response.status.ToString() + ", " + response.message);
			//    return default(List<TwitterDirectMessage>);
			// }
			var xml = CallTwitterApi(OAuthTwitter.Method.GET, url, String.Empty);
			var xdoc = XmlUtils.XdocFromXmlBytes(xml);
			var messages = from message in xdoc.Descendants("direct_message")
						   select new TwitterDirectMessage()
						   {
							   id = message.Descendants("id").First().Value,
							   sender_screen_name = message.Descendants("sender_screen_name").First().Value,
							   recipient_screen_name = message.Descendants("recipient_screen_name").First().Value,
							   text = message.Descendants("text").First().Value
						   };
			return messages.ToList();
		}

		public static List<TwitterDirectMessage> GetNewTwitterDirectMessages()
		{

			var new_messages = new List<TwitterDirectMessage>();
			try
			{
				var stored_messages = GetDirectMessagesFromAzure();
				var fetched_messages = GetDirectMessagesFromTwitter(0);
				var stored_ids = from message in stored_messages select message.id;
				var fetched_ids = from message in fetched_messages select message.id;
				var new_ids = fetched_ids.Except(stored_ids).ToList();
				foreach (var new_id in new_ids)
				{
					var new_message = fetched_messages.Find(msg => msg.id == new_id);
					new_messages.Add(new_message);
					StoreDirectMessageToAzure(new_message);
					var query = String.Format("$filter=(RowKey eq '{0}')", new_id);
					if (ts.ExistsEntity("twitter", query))
						DeleteTwitterDirectMessage(new_id);
				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "GetNewTwitterDirectMessages", e.Message + e.StackTrace);
			}
			return new_messages;
		}

		public static string SendTwitterDirectMessage(string recipient, string text)
		{
			var url = "http://api.twitter.com/direct_messages/new.xml";
			//var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
			//request.ServicePoint.Expect100Continue = false; // http://a-kicker-n.blogspot.com/2009/03/how-to-disable-passing-of-http-header.html
			//request.Method = "POST";
			var post_data = String.Format("user={0}&text={1}", recipient, text);
			//var response = HttpUtils.DoAuthorizedHttpRequest(request, Configurator.twitter_account, Configurator.twitter_password, data);
			var xml = CallTwitterApi(OAuthTwitter.Method.POST, url, post_data);
			return Encoding.UTF8.GetString(xml);
		}

		public static string DeleteTwitterDirectMessage(string id)
		{
			var url = String.Format("http://api.twitter.com/direct_messages/destroy/{0}.xml", id);
			var xml = CallTwitterApi(OAuthTwitter.Method.POST, url, String.Empty);
			//var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
			//request.Method = "POST";
			//var response = HttpUtils.DoAuthorizedHttpRequest(request, Configurator.twitter_account, Configurator.twitter_password, new byte[0]);
			return Encoding.UTF8.GetString(xml);
		}

		public static string FollowTwitterAccount(string account)
		{
			var url = String.Format("http://api.twitter.com/friendships/create/{0}.xml", account);
			var xml = CallTwitterApi(OAuthTwitter.Method.POST, url, String.Empty);
			//var request = (HttpWebRequest)WebRequest.Create(new Uri(url));
			//request.Method = "POST";
			//var response = HttpUtils.DoAuthorizedHttpRequest(request, Configurator.twitter_account, Configurator.twitter_password, new byte[0]);
			return Encoding.UTF8.GetString(xml);
		}

		public static List<TwitterDirectMessage> GetNewTwitterDirectMessagesFromId(string id)
		{
			var messages = GetNewTwitterDirectMessages();
			return messages.FindAll(msg => msg.sender_screen_name == id);
		}
	}
}
