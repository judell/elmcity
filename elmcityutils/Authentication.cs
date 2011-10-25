using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;

namespace ElmcityUtils
{
	public class Authentications
	{
		public static Authentication TwitterAuthentication = new Authentication(Authentication.Mode.twitter, Authentication.TrustedTable.trustedtwitterers, Authentication.TrustedField.screen_name, Authentication.CookieName.ElmcityTwitter);
		public static Authentication FacebookAuthentication = new Authentication(Authentication.Mode.facebook, Authentication.TrustedTable.trustedfacebookers, Authentication.TrustedField.id_or_name, Authentication.CookieName.ElmcityFacebook);
		public static Authentication LiveAuthentication = new Authentication(Authentication.Mode.live, Authentication.TrustedTable.trustedlivers, Authentication.TrustedField.email, Authentication.CookieName.ElmcityLive);
		public static Authentication GoogleAuthentication = new Authentication(Authentication.Mode.google, Authentication.TrustedTable.trustedgooglers, Authentication.TrustedField.email, Authentication.CookieName.ElmcityGoogle);

		public static List<Authentication> AuthenticationList = new List<Authentication> {
 			TwitterAuthentication,
			FacebookAuthentication,
			LiveAuthentication,
			GoogleAuthentication
		};
	}

	public class Authentication
	{
		public enum Mode { twitter, facebook, live, google };
		public enum TrustedTable { trustedtwitterers, trustedfacebookers, trustedlivers, trustedgooglers };
		public enum TrustedField { screen_name, id_or_name, email };
		public enum CookieName { ElmcityTwitter, ElmcityFacebook, ElmcityLive, ElmcityGoogle };

		public Mode mode;
		public TrustedTable trusted_table;
		public TrustedField trusted_field;
		public CookieName cookie_name;

		TableStorage ts = TableStorage.MakeDefaultTableStorage();

		public static void RememberUser(string host_addr, string host_name, string session_id, string mode, string target_field, string target_value)
		{
			var entity = new Dictionary<string, object>();
			entity["mode"] = mode;
			entity[target_field] = target_value;
			entity["host_addr"] = host_addr;
			entity["host_name"] = host_name;
			TableStorage.DictObjToTableStore(TableStorage.Operation.update, entity, "sessions", "sessions", session_id);
		}

		public Authentication(ElmcityUtils.Authentication.Mode mode, ElmcityUtils.Authentication.TrustedTable trusted_table, ElmcityUtils.Authentication.TrustedField trusted_field, ElmcityUtils.Authentication.CookieName cookie_name)
		{
			this.mode = mode;
			this.trusted_field = trusted_field;
			this.trusted_table = trusted_table;
			this.cookie_name = cookie_name;
		}

		public bool IsTrustedId(string foreign_id)
		{
			var q = String.Format("$filter=PartitionKey eq '{0}' and {1} eq '{2}'", this.trusted_table, this.trusted_field, foreign_id);
			return ts.QueryEntities(this.trusted_table.ToString(), q).list_dict_obj.Count > 0; // foreign id to elmcity id is many to one
		}

		public bool ElmcityIdIsAuthorized(string id)
		{
			try
			{
				var q = String.Format("$filter=RowKey eq '{0}'", id);
				var list = ts.QueryEntities(this.trusted_table.ToString(), q).list_dict_obj;
				return list.Count >= 1;
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "ElmcityIdUsesForeignAuth:" + this.trusted_table.ToString() + ":" + id, e.Message);
				return false;
			}
		}

		public string GetAuthenticatedUserOrNull(HttpRequestBase request)
		{
			HttpCookie cookie;
			string session_id;
			try
			{
				cookie = request.Cookies[this.cookie_name.ToString()];
				if (cookie == null) return null;
				session_id = request.Cookies[cookie_name.ToString()].Value;
				var q = String.Format("$filter=PartitionKey eq 'sessions' and RowKey eq '{0}'", session_id);
				var results = ts.QueryEntities("sessions", q);
				if (results.list_dict_obj.Count > 0)
					return (string)results.list_dict_obj[0][this.trusted_field.ToString()];
				else
					return null;
			}
			catch (Exception e)
			{
				session_id = null;
				GenUtils.PriorityLogMsg("exception", "GetAuthenticatedUserOrNull:" + cookie_name + ":" + session_id + ":" + this.trusted_field, e.Message + e.StackTrace);
				return null;
			}
		}

		public string AuthenticatedVia(HttpRequestBase request)  //  authenticate any trusted twitter/facebook/live/google identity to the home page
		// (e.g. appears as target_field in table) to the home page
		{
			var authenticated_id = this.GetAuthenticatedUserOrNull(request);
			if (authenticated_id != null && this.IsTrustedId(authenticated_id))
				return authenticated_id;
			else
				return null;
		}

		public string AuthenticatedVia(HttpRequestBase request, string elmcity_id)
		// authenticate only mapped accounts to elmcity-id-specific services
		// (e.g. elmcity id appears as RowKey in mapping table)
		{
			var authenticated_id = this.GetAuthenticatedUserOrNull(request);
			var mapped_ids = this.AuthenticatedElmcityIds(authenticated_id);
			var maps_to_trusted = mapped_ids.Exists(x => x == elmcity_id);
			if (authenticated_id != null && maps_to_trusted)
				return authenticated_id;
			else
				return null;
		}

		public List<string> AuthenticatedElmcityIds(string foreign_id)
		{
			var q = String.Format("$filter={0} eq '{1}'", this.trusted_field, foreign_id);
			try
			{
				var list = ts.QueryEntities(this.trusted_table.ToString(), q).list_dict_obj;
				return list.Select(x => (string)x["RowKey"]).ToList();
			}
			catch (Exception e)
			{
				GenUtils.PriorityLogMsg("exception", "AuthenticatedElmcityIds: " + this.trusted_table + ":" + foreign_id + ":" + this.trusted_field, e.Message);
				return null;
			}
		}

	}
}
