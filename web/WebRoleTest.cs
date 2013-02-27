using System;
using System.Net;
using NUnit.Framework;
using System.Collections.Generic;
using Moq;
using System.Web;
using ElmcityUtils;

namespace WebRole
{
	[TestFixture]
	public class WebRoleTest
		{
		Authentication twitter_auth = Authentications.TwitterAuthentication;
		Authentication facebook_auth = Authentications.FacebookAuthentication;
		Authentication live_auth = Authentications.LiveAuthentication;
		Authentication google_auth = Authentications.GoogleAuthentication;

		string test_hub = "testKeene";

		#region general urls

		[Test]
		public void UrlHomePage()
		{
			var uri = MakeUri("/");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("the elmcity project"));
		}

		[Test]
		public void UrlEventPage()
		{
			var uri = MakeUri("/" + test_hub);
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains(test_hub));
		}

		[Test]
		public void UrlEventXml()
		{
			var uri = MakeUri("/" + test_hub + "/xml");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().StartsWith("<events>"));
		}

		[Test]
		public void UrlEventJson()
		{
			var uri = MakeUri("/" + test_hub + "/json");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().StartsWith("[{"));
		}

		[Test]
		public void UrlEventIcs()
		{
			var uri = MakeUri("/" + test_hub + "/ics");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().StartsWith("BEGIN:VCALENDAR"));
		}

		[Test]
		public void UrlCssTheme()
		{
			var uri = MakeUri("/get_css_theme?theme_name=a2chron");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.headers["Content-Type"].Contains("text/css"));
			Assert.That(response.DataAsString().Contains("#datepicker"));
		}

		[Test]
		public void UrlAbout()
		{
			var uri = MakeUri("/" + test_hub + "/about");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("about the " + test_hub + " hub"));
		}

		[Test]
		public void UrlTagCloud()
		{
			var uri = MakeUri("/" + test_hub + "/tag_cloud");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().StartsWith("[{"));
		}

		#endregion

		#region connector urls

		[Test]
		public void UrlAddToGoogle()
		{
			var uri = MakeUri("/add_to_cal?elmcity_id=AnnArborChronicle&flavor=google&start=2012-10-24T08%3A00&end=&summary=Travel%20Through%20Maps%20and%20Narrative%3A%20An%20Exhibition%20on%20Travel%20and%20Tourism&url=http%3A%2F%2Fevents.umich.edu%2Fevent%2F10401-1174075&description=UM%20Exhibitions&location=");
			var response = HttpUtils.FetchUrlNoRedirect(uri);
			Assert.That(response.status == HttpStatusCode.Redirect);
			Assert.That(response.headers["Location"].Contains("www.google.com"));
		}

		[Test]
		public void UrlAddToFacebook()
		{
			var uri = MakeUri("/add_to_cal?elmcity_id=AnnArborChronicle&flavor=facebook&start=2012-10-24T08%3A00&end=&summary=Travel%20Through%20Maps%20and%20Narrative%3A%20An%20Exhibition%20on%20Travel%20and%20Tourism&url=http%3A%2F%2Fevents.umich.edu%2Fevent%2F10401-1174075&description=UM%20Exhibitions&location=");
			var response = HttpUtils.FetchUrlNoRedirect(uri);
			Assert.That(response.status == HttpStatusCode.Redirect);
			Assert.That(response.headers["Location"].Contains("cloudapp.net/add_fb_event"));
		}

		#endregion

		#region helper urls

		[Test]
		public void UrlIcsUrlFromFacebookPage()
		{
			var uri = MakeUri("/get_fb_ical_url?fb_page_url=https%3A%2F%2Fwww.facebook.com%2FBerkeleyUndergroundFilmSociety&elmcity_id=berkeley");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("/ics_from_fb_page?fb_id=179173202101438&elmcity_id=berkeley"));
		}


		[Test]
		public void UrlIcsFromFacebookPage()
		{
			var uri = MakeUri("/ics_from_fb_page?fb_id=179173202101438&elmcity_id=berkeley");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().StartsWith("BEGIN:VCALENDAR"));
		}

		[Test]
		public void UrlIcsUrlFromEventBritePage()
		{
			var uri = MakeUri("/get_ical_url_from_eid_of_eventbrite_event_page?url=http%3A%2F%2Fcitycampral.eventbrite.com%2F&elmcity_id=BostonMA");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("/ics_from_eventbrite_eid?eid=3441799515&elmcity_id=BostonMA"));
		}

		[Test]
		public void UrlIcsFromEventBritePage()
		{
			var uri = MakeUri("/ics_from_eventbrite_eid?eid=3441799515&elmcity_id=BostonMA");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().StartsWith("BEGIN:VCALENDAR"));
		}

		[Test]
		public void UrlIcsUrlFromEventBriteOrganizer()
		{
			var uri = MakeUri("/get_ical_url_from_eventbrite_event_page?url=http%3A%2F%2Fwebinno33.eventbrite.com%2F&elmcity_id=BostonMA");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("/ics_from_eventbrite_organizer_id?organizer_id=36534967&elmcity_id=BostonMA"));
		}

		[Test]
		public void UrlIcsFromEventBriteOrganizer()
		{
			var uri = MakeUri("/ics_from_eventbrite_organizer_id?organizer_id=36534967&elmcity_id=BostonMA");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().StartsWith("BEGIN:VCALENDAR"));
		}

		[Test]
		public void UrlIcsUrlFromCsv()
		{
			var uri = MakeUri("/get_csv_ical_url?feed_url=http%3A%2F%2Fgrfx.cstv.com%2Fschools%2Frice%2Fgraphics%2Fcsv%2F12-rice-m-bskb.csv&home_url=http%3A%2F%2Fwww.riceowls.com%2Fcalendar%2Fevents%2F&skip_first_row=yes&title_col=0&date_col=1&time_col=2&tzname=eastern");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("/ics_from_csv?feed_url=http%3A%2F%2Fgrfx.cstv.com%2Fschools%2Frice%2Fgraphics%2Fcsv%2F12-rice-m-bskb.csv&home_url=http%3A%2F%2Fwww.riceowls.com%2Fcalendar%2Fevents%2F&skip_first_row=yes&title_col=0&date_col=1&time_col=2&tzname=eastern"));
		}

		[Test]
		public void UrlIcsFromCsv()
		{
			var uri = MakeUri("/ics_from_csv?feed_url=http%3A%2F%2Fgrfx.cstv.com%2Fschools%2Frice%2Fgraphics%2Fcsv%2F12-rice-m-bskb.csv&home_url=http%3A%2F%2Fwww.riceowls.com%2Fcalendar%2Fevents%2F&skip_first_row=yes&title_col=0&date_col=1&time_col=2&tzname=eastern");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().StartsWith("BEGIN:VCALENDAR"));
		}

		[Test]
		public void UrlIcsUrlFromFilter()
		{
			var uri = MakeUri("/get_ics_to_ics_ical_url?feedurl=http%3A%2F%2Fwww.google.com%2Fcalendar%2Fical%2Fvinology110%40gmail.com%2Fpublic%2Fbasic.ics&include_keyword=jazz&elmcity_id=a2cal");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("/ics_from_ics?feedurl=http%3A%2F%2Fwww.google.com%2Fcalendar%2Fical%2Fvinology110%40gmail.com%2Fpublic%2Fbasic.ics&elmcity_id=a2cal&source=&after=&before=&include_keyword=jazz"));
		}

		[Test]
		public void UrlIcsFromFilter()
		{
			var uri = MakeUri("/ics_from_ics?feedurl=http%3A%2F%2Fwww.google.com%2Fcalendar%2Fical%2Fvinology110%40gmail.com%2Fpublic%2Fbasic.ics&elmcity_id=a2cal&source=&after=&before=&include_keyword=jazz");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().StartsWith("BEGIN:VCALENDAR"));
		}

		[Test]
		public void UrlIcsUrlFromXcal()
		{
			var uri = MakeUri("/get_rss_xcal_ical_url?feedurl=http%3A%2F%2Fevents.seattlepi.com%2Fsearch%3Fcat%3D1%26new%3Dn%26rss%3D1%26srad%3D10%26st%3Devent%26svt%3Dtext&elmcity_id=seattleopencalendar");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("/ics_from_xcal?url=http%3A%2F%2Fevents.seattlepi.com%2Fsearch%3Fcat%3D1%26new%3Dn%26rss%3D1%26srad%3D10%26st%3Devent%26svt%3Dtext&elmcity_id=seattleopencalendar"));
		}

		[Test]
		public void UrlIcsFromXcal()
		{
			var uri = MakeUri("/ics_from_xcal?url=http%3A%2F%2Fevents.seattlepi.com%2Fsearch%3Fcat%3D1%26new%3Dn%26rss%3D1%26srad%3D10%26st%3Devent%26svt%3Dtext&elmcity_id=seattleopencalendar");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().StartsWith("BEGIN:VCALENDAR"));
		}

		#endregion

		#region admin urls

		[Test]
		public void UrlViewCache()
		{
			var uri = MakeUri("/services/viewcache");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("Total:"));
		}

		[Test]
		public void UrlIcalTasks()
		{
			var uri = MakeUri("/table_query/icaltasks?attrs=id,status,start,stop");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("stopped"));
		}

		[Test]
		public void UrlNonIcalTasks()
		{
			var uri = MakeUri("/table_query/nonicaltasks?attrs=id,status,start,stop");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("stopped"));
		}

		[Test]
		public void UrlRegionTasks()
		{
			var uri = MakeUri("/table_query/regiontasks?attrs=id,status,start,stop");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("stopped"));
		}

		[Test]
		public void UrlLogEntries()
		{
			var uri = MakeUri("/logs?log=log&conditions=type eq 'request'");
			var response = HttpUtils.FetchUrl(uri);
			Assert.That(response.status == HttpStatusCode.OK);
			Assert.That(response.DataAsString().Contains("request"));
		}

		#endregion

		#region twitter

		[Test]
		public void IsTrustedTwittererSucceeds()
		{
			Assert.That(twitter_auth.IsTrustedId("judell"));
		}

		[Test]
		public void IsTrustedTwittererFails()
		{
			Assert.IsFalse(twitter_auth.IsTrustedId("ev"));
		}

		[Test]
		public void TwitterUserAuthorizedForElmcityId()
		{
			var session_id = System.DateTime.UtcNow.Ticks.ToString();

			ElmcityUtils.Authentication.RememberUser("test", "test", session_id, twitter_auth.mode.ToString(), twitter_auth.trusted_field.ToString(), "judell");

			var req = MakeRequest(twitter_auth, session_id);

			Assert.That(twitter_auth.AuthenticatedVia(req.Object, "elmcity") == "judell");
		}

		[Test]
		public void TwitterUserNotAuthorizedForElmcityId()
		{
			var session_id = System.DateTime.UtcNow.Ticks.ToString();

			ElmcityUtils.Authentication.RememberUser("test", "test", session_id, twitter_auth.mode.ToString(), twitter_auth.trusted_field.ToString(), "judell");

			var req = MakeRequest(twitter_auth, session_id);

			Assert.IsFalse(twitter_auth.AuthenticatedVia(req.Object, "a2cal") == "judell");
		}

		[Test]
		public void TwitterUserIsAuthenticated()
		{
			var session_id = System.DateTime.UtcNow.Ticks.ToString();

			ElmcityUtils.Authentication.RememberUser("test", "test", session_id, twitter_auth.mode.ToString(), twitter_auth.trusted_field.ToString(), "judell");

			var req = MakeRequest(twitter_auth, session_id);

			Assert.That(twitter_auth.AuthenticatedVia(req.Object) == "judell");
		}

		[Test]
		public void TwitterUserIsNotAuthenticated()
		{
			var session_id = System.DateTime.UtcNow.Ticks.ToString();

     		var req = MakeRequest(twitter_auth, session_id);

			Assert.IsFalse(twitter_auth.AuthenticatedVia(req.Object) == "judell");
		}

		[Test]
		public void AuthenticatedElmcityIdsIncludesExpectedTwitterName()
		{
			var elmcity_ids = twitter_auth.AuthenticatedElmcityIds("judell");
			Assert.That(elmcity_ids.HasItem("elmcity"));
		}

		[Test]
		public void AuthenticatedElmcityIdsDoesNotIncludeUnxpectedTwitterName()
		{
			var elmcity_ids = twitter_auth.AuthenticatedElmcityIds("judell");
			Assert.IsFalse(elmcity_ids.HasItem("a2cal"));
		}

		[Test]
		public void ElmcityIdIsAuthorizedForTwitterSucceeds()
		{
			Assert.That(twitter_auth.ElmcityIdIsAuthorized("elmcity"));
		}

		[Test]
		public void ElmcityIdIsAuthorizedForTwitterFails()
		{
			Assert.IsFalse(twitter_auth.ElmcityIdIsAuthorized("a2cal"));
		}

		#endregion

		#region facebook

		[Test]
		public void IsTrustedFacebookerSucceeds()
		{
			Assert.That(facebook_auth.IsTrustedId("Jon.R.Udell"));
		}

		[Test]
		public void IsTrustedFacebookerFails()
		{
			Assert.IsFalse(facebook_auth.IsTrustedId("Zuck"));
		}

		[Test]
		public void FacebookUserAuthorizedForElmcityId()
		{
			var session_id = System.DateTime.UtcNow.Ticks.ToString();

			ElmcityUtils.Authentication.RememberUser("test", "test", session_id, facebook_auth.mode.ToString(), facebook_auth.trusted_field.ToString(), "Jon.R.Udell");

			var req = MakeRequest(facebook_auth, session_id);

			Assert.That(facebook_auth.AuthenticatedVia(req.Object, "elmcity") == "Jon.R.Udell");
		}

		[Test]
		public void FacebookUserNotAuthorizedForElmcityId()
		{
			var session_id = System.DateTime.UtcNow.Ticks.ToString();

			ElmcityUtils.Authentication.RememberUser("test", "test", session_id, facebook_auth.mode.ToString(), facebook_auth.trusted_field.ToString(), "Jon.R.Udell");

			var req = MakeRequest(facebook_auth, session_id);

			Assert.IsFalse(facebook_auth.AuthenticatedVia(req.Object, "a2cal") == "Jon.R.Udell");
		}

		[Test]
		public void FacebookUserIsAuthenticated()
		{
			var session_id = System.DateTime.UtcNow.Ticks.ToString();

			ElmcityUtils.Authentication.RememberUser("test", "test", session_id, facebook_auth.mode.ToString(), facebook_auth.trusted_field.ToString(), "Jon.R.Udell");

			var req = MakeRequest(facebook_auth, session_id);

			Assert.That(facebook_auth.AuthenticatedVia(req.Object) == "Jon.R.Udell");
		}

		[Test]
		public void FacebookUserIsNotAuthenticated()
		{
			var session_id = System.DateTime.UtcNow.Ticks.ToString();

			var req = MakeRequest(facebook_auth, session_id);

			Assert.IsFalse(facebook_auth.AuthenticatedVia(req.Object) == "Jon.R.Udell");
		}

		[Test]
		public void AuthenticatedElmcityIdsIncludesExpectedFacebookId()
		{
			var elmcity_ids = facebook_auth.AuthenticatedElmcityIds("Jon.R.Udell");
			Assert.That(elmcity_ids.HasItem("elmcity"));
		}

		[Test]
		public void AuthenticatedElmcityIdsDoesNotIncludeUnxpectedFacebookId()
		{
			var elmcity_ids = facebook_auth.AuthenticatedElmcityIds("Jon.R.Udell");
			Assert.IsFalse(elmcity_ids.HasItem("a2cal"));
		}

		[Test]
		public void ElmcityIdIsAuthorizedForFacebookSucceeds()
		{
			Assert.That(facebook_auth.ElmcityIdIsAuthorized("elmcity"));
		}

		[Test]
		public void ElmcityIdIsAuthorizedForFacebookFails()
		{
			Assert.IsFalse(facebook_auth.ElmcityIdIsAuthorized("a2cal"));
		}

		#endregion

		private Mock<HttpRequestBase> MakeRequest(ElmcityUtils.Authentication authentication, string session_id)
		{
			var req = new Mock<HttpRequestBase>();
			var cookie = new HttpCookie(authentication.cookie_name.ToString(), session_id);
			var cookies = new HttpCookieCollection();
			cookies.Add(cookie);
			req.SetupGet(x => x.Cookies).Returns(cookies);
			return req;
		}

		private Uri MakeUri(string relative_uri)
		{
			return new Uri(string.Format("http://{0}{1}", ElmcityUtils.Configurator.appdomain, relative_uri));
		}

	}
}
