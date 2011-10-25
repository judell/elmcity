using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ElmcityUtils;
using NUnit.Framework;

namespace CalendarAggregator
{
	public class ActionsTest
	{
		private static string test_fb_id = Configurator.test_fb_id;
		private static string test_fb_key = Configurator.test_fb_key;
		private static string test_fb_url = String.Format("http://www.facebook.com/ical/u.php?uid={0}&key={1}", test_fb_id, test_fb_key);
		private string id = "elmcity";  // todo: externalize these
		private string twitter_sender = "judell";
		private string twitter_receiver = Configurator.twitter_account;
		private string row_key = Utils.MakeSafeRowkeyFromUrl(test_fb_url);
		private TableStorage ts = TableStorage.MakeDefaultTableStorage();
		private string text;

		[Test]
		public void AddFacebookPerformsSuccessfully()
		{
			var query = String.Format("$filter=PartitionKey eq '{0}' and RowKey eq '{1}'", id, row_key);
			ts.DeleteEntity("metadata", id, row_key);
			var table_record_exists = ts.ExistsEntity("metadata", query);
			Assert.IsFalse(table_record_exists);
			this.text = "add_fb_feed id=" + test_fb_id + " key=" + test_fb_key + " who=Jon+Udell category=random url=http://jonudell.net";
			var tc = new TwitterCommand(this.id, this.twitter_sender, this.twitter_receiver, this.text);
			var action = new AddFacebookFeed();
			Assert.IsTrue(action.Perform(tc, this.id));
			table_record_exists = ts.ExistsEntity("metadata", query);
			Assert.That(table_record_exists);
		}

		[Test]
		public void AddFacebookFailsForBogusId()
		{
			this.text = "add_fb_feed id=" + 0 + " key=" + test_fb_key + " who=Jon+Udell category=random url=http://jonudell.net";
			var tc = new TwitterCommand(this.id, this.twitter_sender, this.twitter_receiver, this.text);
			var action = new AddFacebookFeed();
			Assert.IsFalse(action.Perform(tc, this.id));
		}

		[Test]
		public void AddFacebookFailsForBogusKey()
		{
			this.text = "add_fb_feed id=" + test_fb_id + " key=" + 0 + " who=Jon+Udell category=random url=http://jonudell.net";
			var tc = new TwitterCommand(this.id, this.twitter_sender, this.twitter_receiver, this.text);
			var action = new AddFacebookFeed();
			Assert.IsFalse(action.Perform(tc, this.id));
		}

		[Test]
		public void AddFacebookFailsForMissingId()
		{
			this.text = "add_fb_feed  key=" + test_fb_key + " who=Jon+Udell category=random url=http://jonudell.net";
			var tc = new TwitterCommand(this.id, this.twitter_sender, this.twitter_receiver, this.text);
			var action = new AddFacebookFeed();
			Assert.IsFalse(action.Perform(tc, this.id));
		}

		[Test]
		public void AddFacebookFailsForMissingWho()
		{
			this.text = "add_fb_feed id=" + test_fb_id + " key=" + test_fb_key + " category=random url=http://jonudell.net";
			var tc = new TwitterCommand(this.id, this.twitter_sender, this.twitter_receiver, this.text);
			var action = new AddFacebookFeed();
			Assert.IsFalse(action.Perform(tc, this.id));
		}

		[Test]
		public void AddFacebookFailsForBogusVerb()
		{
			this.text = "add_facebook_feed id=" + test_fb_id + " key=" + test_fb_key + " who=Jon+Udell category=random url=http://jonudell.net";
			var tc = new TwitterCommand(this.id, this.twitter_sender, this.twitter_receiver, this.text);
			var action = new AddFacebookFeed();
			Assert.IsFalse(action.Perform(tc, this.id));
		}

	}
}
