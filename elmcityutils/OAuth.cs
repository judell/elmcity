﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace ElmcityUtils
{

	public class OAuthBase
	{
		// from: http://oauth.googlecode.com/svn/code/csharp/OAuthBase.cs

		/// <summary>
		/// Provides a predefined set of algorithms that are supported officially by the protocol
		/// </summary>
		public enum SignatureTypes
		{
			HMACSHA1,
			PLAINTEXT,
			RSASHA1
		}

		/// <summary>
		/// Provides an internal structure to sort the query parameter
		/// </summary>
		protected class QueryParameter
		{
			private string name = null;
			private string value = null;

			public QueryParameter(string name, string value)
			{
				this.name = name;
				this.value = value;
			}

			public string Name
			{
				get { return name; }
			}

			public string Value
			{
				get { return value; }
			}
		}

		/// <summary>
		/// Comparer class used to perform the sorting of the query parameters
		/// </summary>
		protected class QueryParameterComparer : IComparer<QueryParameter>
		{

			#region IComparer<QueryParameter> Members

			public int Compare(QueryParameter x, QueryParameter y)
			{
				if (x.Name == y.Name)
				{
					return string.Compare(x.Value, y.Value);
				}
				else
				{
					return string.Compare(x.Name, y.Name);
				}
			}

			#endregion
		}

		protected const string OAuthVersion = "1.0";
		protected const string OAuthParameterPrefix = "oauth_";

		//
		// List of know and used oauth parameters' names
		//        
		protected const string OAuthConsumerKeyKey = "oauth_consumer_key";
		protected const string OAuthCallbackKey = "oauth_callback";
		protected const string OAuthVersionKey = "oauth_version";
		protected const string OAuthSignatureMethodKey = "oauth_signature_method";
		protected const string OAuthSignatureKey = "oauth_signature";
		protected const string OAuthTimestampKey = "oauth_timestamp";
		protected const string OAuthNonceKey = "oauth_nonce";
		protected const string OAuthTokenKey = "oauth_token";
		protected const string OAuthTokenSecretKey = "oauth_token_secret";

		protected const string HMACSHA1SignatureType = "HMAC-SHA1";
		protected const string PlainTextSignatureType = "PLAINTEXT";
		protected const string RSASHA1SignatureType = "RSA-SHA1";

		protected Random random = new Random();

		protected string unreservedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";

		/// <summary>
		/// Helper function to compute a hash value
		/// </summary>
		/// <param name="hashAlgorithm">The hashing algoirhtm used. If that algorithm needs some initialization, like HMAC and its derivatives, they should be initialized prior to passing it to this function</param>
		/// <param name="data">The data to hash</param>
		/// <returns>a Base64 string of the hash value</returns>
		private string ComputeHash(HashAlgorithm hashAlgorithm, string data)
		{
			if (hashAlgorithm == null)
			{
				throw new ArgumentNullException("hashAlgorithm");
			}

			if (string.IsNullOrEmpty(data))
			{
				throw new ArgumentNullException("data");
			}

			byte[] dataBuffer = System.Text.Encoding.ASCII.GetBytes(data);
			byte[] hashBytes = hashAlgorithm.ComputeHash(dataBuffer);

			return Convert.ToBase64String(hashBytes);
		}

		/// <summary>
		/// Internal function to cut out all non oauth query string parameters (all parameters not begining with "oauth_")
		/// </summary>
		/// <param name="parameters">The query string part of the Url</param>
		/// <returns>A list of QueryParameter each containing the parameter name and value</returns>
		private List<QueryParameter> GetQueryParameters(string parameters)
		{
			if (parameters.StartsWith("?"))
			{
				parameters = parameters.Remove(0, 1);
			}

			List<QueryParameter> result = new List<QueryParameter>();

			if (!string.IsNullOrEmpty(parameters))
			{
				string[] p = parameters.Split('&');
				foreach (string s in p)
				{
					if (!string.IsNullOrEmpty(s) && !s.StartsWith(OAuthParameterPrefix))
					{
						if (s.IndexOf('=') > -1)
						{
							string[] temp = s.Split('=');
							result.Add(new QueryParameter(temp[0], temp[1]));
						}
						else
						{
							result.Add(new QueryParameter(s, string.Empty));
						}
					}
				}
			}

			return result;
		}

		/// <summary>
		/// This is a different Url Encode implementation since the default .NET one outputs the percent encoding in lower case.
		/// While this is not a problem with the percent encoding spec, it is used in upper case throughout OAuth
		/// </summary>
		/// <param name="value">The value to Url encode</param>
		/// <returns>Returns a Url encoded string</returns>
		protected string UrlEncode(string value)
		{
			StringBuilder result = new StringBuilder();

			foreach (char symbol in value)
			{
				if (unreservedChars.IndexOf(symbol) != -1)
				{
					result.Append(symbol);
				}
				else
				{
					result.Append('%' + String.Format("{0:X2}", (int)symbol));
				}
			}

			return result.ToString();
		}

		/// <summary>
		/// Normalizes the request parameters according to the spec
		/// </summary>
		/// <param name="parameters">The list of parameters already sorted</param>
		/// <returns>a string representing the normalized parameters</returns>
		protected string NormalizeRequestParameters(IList<QueryParameter> parameters)
		{
			StringBuilder sb = new StringBuilder();
			QueryParameter p = null;
			for (int i = 0; i < parameters.Count; i++)
			{
				p = parameters[i];
				sb.AppendFormat("{0}={1}", p.Name, p.Value);

				if (i < parameters.Count - 1)
				{
					sb.Append("&");
				}
			}

			return sb.ToString();
		}

		/// <summary>
		/// Generate the signature base that is used to produce the signature
		/// </summary>
		/// <param name="url">The full url that needs to be signed including its non OAuth url parameters</param>
		/// <param name="consumerKey">The consumer key</param>        
		/// <param name="token">The token, if available. If not available pass null or an empty string</param>
		/// <param name="tokenSecret">The token secret, if available. If not available pass null or an empty string</param>
		/// <param name="httpMethod">The http method used. Must be a valid HTTP method verb (POST,GET,PUT, etc)</param>
		/// <param name="signatureType">The signature type. To use the default values use <see cref="OAuthBase.SignatureTypes">OAuthBase.SignatureTypes</see>.</param>
		/// <returns>The signature base</returns>
		public string GenerateSignatureBase(
				Uri url,
				string consumerKey,
				string token,
				string tokenSecret,
				string httpMethod,
				string timeStamp,
				string nonce,
				string signatureType,
				out string normalizedUrl,
				out string normalizedRequestParameters)
		{
			if (token == null)
			{
				token = string.Empty;
			}

			if (tokenSecret == null)
			{
				tokenSecret = string.Empty;
			}

			if (string.IsNullOrEmpty(consumerKey))
			{
				throw new ArgumentNullException("consumerKey");
			}

			if (string.IsNullOrEmpty(httpMethod))
			{
				throw new ArgumentNullException("httpMethod");
			}

			if (string.IsNullOrEmpty(signatureType))
			{
				throw new ArgumentNullException("signatureType");
			}

			normalizedUrl = null;
			normalizedRequestParameters = null;

			List<QueryParameter> parameters = GetQueryParameters(url.Query);
			parameters.Add(new QueryParameter(OAuthVersionKey, OAuthVersion));
			parameters.Add(new QueryParameter(OAuthNonceKey, nonce));
			parameters.Add(new QueryParameter(OAuthTimestampKey, timeStamp));
			parameters.Add(new QueryParameter(OAuthSignatureMethodKey, signatureType));
			parameters.Add(new QueryParameter(OAuthConsumerKeyKey, consumerKey));

			if (!string.IsNullOrEmpty(token))
			{
				parameters.Add(new QueryParameter(OAuthTokenKey, token));
			}

			parameters.Sort(new QueryParameterComparer());

			normalizedUrl = string.Format("{0}://{1}", url.Scheme, url.Host);
			if (!((url.Scheme == "http" && url.Port == 80) || (url.Scheme == "https" && url.Port == 443)))
			{
				normalizedUrl += ":" + url.Port;
			}
			normalizedUrl += url.AbsolutePath;
			normalizedRequestParameters = NormalizeRequestParameters(parameters);

			StringBuilder signatureBase = new StringBuilder();
			signatureBase.AppendFormat("{0}&", httpMethod.ToUpper());
			signatureBase.AppendFormat("{0}&", UrlEncode(normalizedUrl));
			signatureBase.AppendFormat("{0}", UrlEncode(normalizedRequestParameters));

			return signatureBase.ToString();
		}

		/// <summary>
		/// Generate the signature value based on the given signature base and hash algorithm
		/// </summary>
		/// <param name="signatureBase">The signature based as produced by the GenerateSignatureBase method or by any other means</param>
		/// <param name="hash">The hash algorithm used to perform the hashing. If the hashing algorithm requires initialization or a key it should be set prior to calling this method</param>
		/// <returns>A base64 string of the hash value</returns>
		public string GenerateSignatureUsingHash(string signatureBase, HashAlgorithm hash)
		{
			return ComputeHash(hash, signatureBase);
		}

		/// <summary>
		/// Generates a signature using the HMAC-SHA1 algorithm
		/// </summary>          
		/// <param name="url">The full url that needs to be signed including its non OAuth url parameters</param>
		/// <param name="consumerKey">The consumer key</param>
		/// <param name="consumerSecret">The consumer seceret</param>
		/// <param name="token">The token, if available. If not available pass null or an empty string</param>
		/// <param name="tokenSecret">The token secret, if available. If not available pass null or an empty string</param>
		/// <param name="httpMethod">The http method used. Must be a valid HTTP method verb (POST,GET,PUT, etc)</param>
		/// <returns>A base64 string of the hash value</returns>
		public string GenerateSignature(Uri url, string consumerKey, string consumerSecret, string token, string tokenSecret, string httpMethod, string timeStamp, string nonce, out string normalizedUrl, out string normalizedRequestParameters)
		{
			return GenerateSignature(url, consumerKey, consumerSecret, token, tokenSecret, httpMethod, timeStamp, nonce, SignatureTypes.HMACSHA1, out normalizedUrl, out normalizedRequestParameters);
		}

		/// <summary>
		/// Generates a signature using the specified signatureType 
		/// </summary>          
		/// <param name="url">The full url that needs to be signed including its non OAuth url parameters</param>
		/// <param name="consumerKey">The consumer key</param>
		/// <param name="consumerSecret">The consumer seceret</param>
		/// <param name="token">The token, if available. If not available pass null or an empty string</param>
		/// <param name="tokenSecret">The token secret, if available. If not available pass null or an empty string</param>
		/// <param name="httpMethod">The http method used. Must be a valid HTTP method verb (POST,GET,PUT, etc)</param>
		/// <param name="signatureType">The type of signature to use</param>
		/// <returns>A base64 string of the hash value</returns>
		public string GenerateSignature(Uri url, string consumerKey, string consumerSecret, string token, string tokenSecret, string httpMethod, string timeStamp, string nonce, SignatureTypes signatureType, out string normalizedUrl, out string normalizedRequestParameters)
		{
			normalizedUrl = null;
			normalizedRequestParameters = null;

			switch (signatureType)
			{
				case SignatureTypes.PLAINTEXT:
					return HttpUtility.UrlEncode(string.Format("{0}&{1}", consumerSecret, tokenSecret));
				case SignatureTypes.HMACSHA1:
					string signatureBase = GenerateSignatureBase(url, consumerKey, token, tokenSecret, httpMethod, timeStamp, nonce, HMACSHA1SignatureType, out normalizedUrl, out normalizedRequestParameters);

					HMACSHA1 hmacsha1 = new HMACSHA1();
					hmacsha1.Key = Encoding.ASCII.GetBytes(string.Format("{0}&{1}", UrlEncode(consumerSecret), string.IsNullOrEmpty(tokenSecret) ? "" : UrlEncode(tokenSecret)));

					return GenerateSignatureUsingHash(signatureBase, hmacsha1);
				case SignatureTypes.RSASHA1:
					throw new NotImplementedException();
				default:
					throw new ArgumentException("Unknown signature type", "signatureType");
			}
		}

		/// <summary>
		/// Generate the timestamp for the signature        
		/// </summary>
		/// <returns></returns>
		public virtual string GenerateTimeStamp()
		{
			// Default implementation of UNIX time of the current UTC time
			TimeSpan ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
			return Convert.ToInt64(ts.TotalSeconds).ToString();
		}

		/// <summary>
		/// Generate a nonce
		/// </summary>
		/// <returns></returns>
		public virtual string GenerateNonce()
		{
			// Just a simple implementation of a random number between 123400 and 9999999
			return random.Next(123400, 9999999).ToString();
		}

	}

	public class OAuthTwitter : OAuthBase
	{
		// from http://www.voiceoftech.com/swhitley/?p=681

		public enum Method { GET, POST, DELETE };
		public const string REQUEST_TOKEN = "https://twitter.com/oauth/request_token";
		public const string AUTHORIZE = "https://twitter.com/oauth/authorize";
		public const string ACCESS_TOKEN = "https://twitter.com/oauth/access_token";

		private string consumer_key;
		private string consumer_secret;

		private string _token = "";

		public string token
		{
			get { return _token; }
			set { _token = value; }
		}

		private string _token_secret = "";

		public string token_secret
		{
			get { return _token_secret; }
			set { _token_secret = value; }
		}

		private static Dictionary<string, string> settings = GenUtils.GetSettingsFromAzureTable();


		public OAuthTwitter(string consumer_key, string consumer_secret, string token, string token_secret)
		{
			this.consumer_key = consumer_key;
			this.consumer_secret = consumer_secret;
			this.token = token;
			this.token_secret = token_secret;
		}

		public OAuthTwitter(string consumer_key, string consumer_secret)
		{
			this.consumer_key = consumer_key;
			this.consumer_secret = consumer_secret; 
		}

		public string oAuthWebRequest(Method method, string url, string oauth_verifier, string post_data)
		{
			string outUrl = "";
			string querystring = "";
			string ret = "";

			GenUtils.LogMsg("info", "oAuthWebRequest", url + ", (" + post_data + ")");

			try
			{

				//Setup postData for signing.
				//Add the postData to the querystring.
				if (method == Method.POST || method == Method.DELETE)
				{
					if (post_data.Length > 0)
					{
						//Decode the parameters and re-encode using the oAuth UrlEncode method.
						NameValueCollection qs = HttpUtility.ParseQueryString(post_data);
						post_data = "";
						foreach (string key in qs.AllKeys)
						{
							if (post_data.Length > 0)
							{
								post_data += "&";
							}
							qs[key] = HttpUtility.UrlDecode(qs[key]);
							qs[key] = this.UrlEncode(qs[key]);
							post_data += key + "=" + qs[key];

						}
						if (url.IndexOf("?") > 0)
						{
							url += "&";
						}
						else
						{
							url += "?";
						}
						url += post_data;
					}
				}

				Uri uri = new Uri(url);

				string nonce = this.GenerateNonce();
				string timeStamp = this.GenerateTimeStamp();

				//Generate Signature
				string sig = this.GenerateSignature(
						uri,
						this.consumer_key,
						this.consumer_secret,
						this.token,
						this.token_secret,
						method.ToString(),
						timeStamp,
						nonce,
						SignatureTypes.HMACSHA1,
						out outUrl,
						out querystring);

				querystring += "&oauth_signature=" + this.UrlEncode(sig);

				if (oauth_verifier != null)
					querystring += "&oauth_verifier=" + oauth_verifier;

				//Convert the querystring to postData
				if (method == Method.POST || method == Method.DELETE)
				{
					post_data = querystring;
					querystring = "";
				}

				if (querystring.Length > 0)
				{
					outUrl += "?";
				}

				GenUtils.LogMsg("info", "oAuthWebRequest calling helper with ", outUrl + ", " + querystring + ", (" + post_data + ")");
				ret = OAuthWebRequestHelper(method, outUrl + querystring, post_data);
				GenUtils.LogMsg("info", "oAuthWebRequestHelper got ", ret);

				return ret;
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "oAuthWebRequest", e.Message + e.StackTrace);
				return String.Empty;
			}
		}

		public string OAuthWebRequestHelper(Method method, string url, string post_data)
		{
			HttpWebRequest request = null;
			try
			{

				try
				{
					request = System.Net.WebRequest.Create(url) as HttpWebRequest;
					System.Diagnostics.Debug.Assert(request != null);
					request.Method = method.ToString();
					request.ServicePoint.Expect100Continue = false;
				}
				catch (Exception e)
				{
					GenUtils.PriorityLogMsg("exception", "OAuthWebRequestHelper", e.Message + e.StackTrace);
					throw new Exception("OAuthWebRequestHelperNoUrl");
				}

				if (method == Method.POST || method == Method.DELETE)
				{
					request.ContentType = "application/x-www-form-urlencoded";

					/*
					//POST the data.
					requestWriter = new StreamWriter(webRequest.GetRequestStream());
					try
					{
							requestWriter.Write(postData);
					}
					catch
					{
							throw;
					}
					finally
					{
							requestWriter.Close();
							requestWriter = null;
					}*/

				}

				var response = HttpUtils.DoHttpWebRequest(request, Encoding.UTF8.GetBytes(post_data));

				request = null;

				return response.DataAsString();
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("OAuthWebRequestHelper", method + " " + url, e.Message + e.StackTrace);
				return null;
			}

		}

		public string WebResponseGet(HttpWebRequest webRequest)
		{
			StreamReader responseReader = null;
			string responseData = "";

			try
			{
				responseReader = new StreamReader(webRequest.GetResponse().GetResponseStream());
				responseData = responseReader.ReadToEnd();
			}
			catch
			{
				throw;
			}
			finally
			{
				webRequest.GetResponse().GetResponseStream().Close();
				responseReader.Close();
				responseReader = null;
			}

			return responseData;
		}

		public string AuthorizationLinkGet()
		{
			string ret = null;
			string response;

			try
			{
				response = this.oAuthWebRequest(Method.GET, REQUEST_TOKEN, null, String.Empty);
				if (response.Length > 0)
				{
					//response contains token and token secret.  We only need the token.
					NameValueCollection qs = HttpUtility.ParseQueryString(response);

					string oauth_verifier = qs["oauth_verifier"];
					 
					if (oauth_verifier == null)
					{
						var msg =  REQUEST_TOKEN + " sent back null oauth_verifier";
						GenUtils.PriorityLogMsg("AuthorizationLinkGet", msg, null);
						// throw new Exception(msg); is it optional
					}
					
					if (qs["oauth_callback_confirmed"] != null && qs["oauth_callback_confirmed"] != "true")
						{
							var msg = REQUEST_TOKEN + " auth_callback_confirmed not true";
							GenUtils.PriorityLogMsg("AuthorizationLinkGet", msg, null);
							throw new Exception(msg);
						}

					if (qs["oauth_token"] != null)
					{
						ret = AUTHORIZE + "?oauth_token=" + qs["oauth_token"] + "&oauth_verifier=" + qs["oauth_verifier"];
					}
					else
					{
						var msg = REQUEST_TOKEN + " oauth_token_is_null";
						GenUtils.PriorityLogMsg("AuthorizationLinkGet", msg, null);
						throw new Exception(msg);
					}

				}
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "AuthorizationLinkGet", e.Message);
			}
			return ret;
		}

	}

}