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
			Assert.That(elmcity_ids.Exists(x => x == "elmcity"));
		}

		[Test]
		public void AuthenticatedElmcityIdsDoesNotIncludeUnxpectedTwitterName()
		{
			var elmcity_ids = twitter_auth.AuthenticatedElmcityIds("judell");
			Assert.IsFalse(elmcity_ids.Exists(x => x == "a2cal"));
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
			Assert.That(elmcity_ids.Exists(x => x == "elmcity"));
		}

		[Test]
		public void AuthenticatedElmcityIdsDoesNotIncludeUnxpectedFacebookId()
		{
			var elmcity_ids = facebook_auth.AuthenticatedElmcityIds("Jon.R.Udell");
			Assert.IsFalse(elmcity_ids.Exists(x => x == "a2cal"));
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

	}
}
