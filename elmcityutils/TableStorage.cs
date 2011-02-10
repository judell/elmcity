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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace ElmcityUtils
{
	// encapsulate http response from azure table plus various operation-specific responses
	public class TableStorageResponse
	{

		public HttpResponse http_response
		{
			get { return _http_response; }
		}
		private HttpResponse _http_response;

		public object response
		{
			get { return _response; }
		}
		private object _response;

		// encapsulate the http response from the table store, plus data as a list of dict<str,obj>
		public TableStorageResponse(HttpResponse http_response, List<Dictionary<string, object>> response)
		{
			this._http_response = http_response;
			this._response = response;
		}

		// alternate for data as string
		public TableStorageResponse(HttpResponse http_response, string response)
		{
			this._http_response = http_response;
			this._response = response;
		}

		// alternate version for boolean responses
		public TableStorageResponse(HttpResponse http_response, bool response)
		{
			this._http_response = http_response;
			this._response = response;
		}

		// alternate version for int responses
		public TableStorageResponse(HttpResponse http_response, int response)
		{
			this._http_response = http_response;
			this._response = response;
		}

		// alternate version when only http response needed
		public TableStorageResponse(HttpResponse http_response)
		{
			this._http_response = http_response;
			this._response = null;
		}

	}

	// an http-oriented alternative to the azure sdk wrapper around table store
	public class TableStorage
	{
		public enum Operation { merge, update };

		const string NEW_LINE = "\x0A";
		const string PREFIX_STORAGE = "x-ms-";
		const string TIME_FORMAT = "ddd, dd MMM yyyy HH:mm:ss";
		const string TABLE_STORAGE_CONTENT_TYPE = "application/atom+xml";

		// http://social.msdn.microsoft.com/Forums/en/windowsazure/thread/d624c665-ef19-4289-9ba6-ddba31570a60
		const string DataServiceVersion = "1.0;NetFx";
		const string MaxDataServiceVersion = "1.0;NetFx";

		const string NextPartitionKeyHeaderName = "x-ms-continuation-NextPartitionKey";
		const string NextRowKeyHeaderName = "x-ms-continuation-NextRowKey";

		static private XNamespace odata_namespace = "http://schemas.microsoft.com/ado/2007/08/dataservices";
		static private XNamespace odata_metadata_namespace = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";

		public const string ISO_FORMAT_UTC = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

		// public Dictionary<string, object> entity; // to facilitate sharing between c# and ironpython

		// for queries where partition key and rowkey are known
		public static string query_template_pk_rk = "$filter=(PartitionKey eq '{0}' and RowKey eq '{1}')";

		private string azure_storage_name;
		private string azure_table_host;
		private string azure_b64_secret;
		private string scheme;

		public TableStorage(string azure_storage_name, string azure_table_host, string azure_b64_secret, string scheme)
		{
			this.scheme = scheme;
			this.azure_storage_name = azure_storage_name;
			this.azure_table_host = azure_table_host;
			this.azure_b64_secret = azure_b64_secret;
			// this.entity = new Dictionary<string, object>();
		}

		public static TableStorage MakeDefaultTableStorage()
		{
			return new TableStorage(Configurator.azure_storage_account,
				Configurator.azure_storage_account + "." + Configurator.azure_table_domain,
				Configurator.azure_b64_secret, scheme: "http");
		}

		public static TableStorage MakeTableStorage(string storage_account, string table_domain, string b64_secret)
		{
			return new TableStorage(storage_account,
				storage_account + "." + table_domain,
				b64_secret, scheme: "http");
		}

		public static TableStorage MakeSecureTableStorage()
		{
			return new TableStorage(Configurator.azure_storage_account,
				 Configurator.azure_storage_account + "." + Configurator.azure_table_domain,
				Configurator.azure_b64_secret, scheme: "https");
		}

		// merge partial set of values into existing record
		public static TableStorageResponse UpmergeDictToTableStore(Dictionary<string, object> dict, string table, string partkey, string rowkey)
		{
			return DictObjToTableStore(Operation.merge, dict, table, partkey, rowkey);
		}

		// update full set of values into existing record
		public static TableStorageResponse UpdateDictToTableStore(Dictionary<string, object> dict, string table, string partkey, string rowkey)
		{
			return DictObjToTableStore(Operation.update, dict, table, partkey, rowkey);
		}

		// convert a feed url into a base-64-encoded and uri-escaped string
		// that can be used as an azure table rowkey
		public static string MakeSafeRowkeyFromUrl(string url)
		{
			var b64array = Encoding.UTF8.GetBytes(url);
			return Uri.EscapeDataString(Convert.ToBase64String(b64array)).Replace('%', '_');
		}

		// try to insert a dict<str,obj> into table store
		// if conflict, try to merge or update 
		public static TableStorageResponse DictObjToTableStore(Operation operation, Dictionary<string, object> dict, string table, string partkey, string rowkey)
		{
			TableStorage ts = MakeDefaultTableStorage();
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", partkey);
			entity.Add("RowKey", rowkey);
			foreach (var key in dict.Keys)
				if (key != "PartitionKey" && key != "RowKey")
					entity.Add(key, dict[key]);
			var response = ts.InsertEntity(table, entity);
			if (response.http_response.status != HttpStatusCode.Created)
			{
				switch (operation)
				{
					case Operation.update:
						response = ts.UpdateEntity(table, partkey, rowkey, entity);
						break;
					case Operation.merge:
						response = ts.MergeEntity(table, partkey, rowkey, entity);
						break;
					default:
						GenUtils.LogMsg("info", "DictToTableStore unexpected operation", operation.ToString());
						break;
				}
				if (response.http_response.status != HttpStatusCode.NoContent)
				{
					GenUtils.LogMsg("error", "DictToTableStore: " + operation, response.http_response.status.ToString() + ", " + response.http_response.message);
					//var keys = String.Join(",", dict.Keys.ToArray());
					//var str_vals = new List<string>();
					//foreach (var val in dict.Values)
					//    str_vals.Add(val.ToString());
					//var vals = String.Join(",", str_vals.ToArray());
					//GenUtils.LogMsg("info", "DictToTableStore: " + keys + ", " + str_vals, null);
				}
			}
			return response;
		}

		// this query expects to find just one matching entity
		public static Dictionary<string, object> QueryForSingleEntityAsDictObj(TableStorage ts, string table, string q)
		{
			var ts_response = ts.QueryEntities(table, q);
			var dicts = (List<Dictionary<string, object>>)ts_response.response;
			var dict = new Dictionary<string, object>();

			if (dicts.Count > 0)
				dict = dicts.FirstOrDefault();

			if (dicts.Count > 1)
				// should not happen, but...
				GenUtils.LogMsg("info", "QueryForSingleEntity: " + table, q + ": more than one matching entity");

			return dict;
		}

		public static Dictionary<string, string> QueryForSingleEntityAsDictStr(TableStorage ts, string table, string q)
		{
			var dict = QueryForSingleEntityAsDictObj(ts, table, q);
			return ObjectUtils.DictObjToDictStr(dict);
		}

		public TableStorageResponse WritePriorityLogMessage(string type, string message, string data)
		{
			return WriteLogMessage(type, message, data, Configurator.azure_priority_log_table);
		}

		// for tracing, used everywhere
		public TableStorageResponse WriteLogMessage(string type, string message, string data, string table)
		{
			type = type ?? "";
			message = message ?? "";
			data = data ?? "";
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", "log");
			entity.Add("RowKey", DateTime.Now.ToUniversalTime().Ticks.ToString());
			entity.Add("type", type);
			entity.Add("message", message);
			entity.Add("data", data);
			TableStorageResponse response;
			try
			{
				var tablename = (table == null) ? Configurator.azure_log_table : table;
				response = this.InsertEntity(table, entity);
			}
			catch (Exception e)
			{
				var rs = new HttpResponse(HttpStatusCode.Unused, "unable to write log message, " + e.Message, null, null);
				response = new TableStorageResponse(rs);
			}
			return response;
		}

		// for tracing, used everywhere
		public TableStorageResponse WriteLogMessage(string type, string message, string data)
		{
			type = type ?? "";
			message = message ?? "";
			data = data ?? "";
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", "log");
			entity.Add("RowKey", DateTime.Now.ToUniversalTime().Ticks.ToString());
			entity.Add("type", type);
			entity.Add("message", message);
			entity.Add("data", data);
			TableStorageResponse response;
			try
			{
				response = this.InsertEntity(Configurator.azure_log_table, entity);
			}
			catch (Exception e)
			{
				var rs = new HttpResponse(HttpStatusCode.Unused, "unable to write log message, " + e.Message, null, null);
				response = new TableStorageResponse(rs);
			}
			return response;
		}

		public TableStorageResponse ListTables()
		{
			var request_path = "Tables()";
			var http_response = DoTableStoreRequest(request_path, query_string: null, method: "GET", headers: new Hashtable(), data: null);
			var response = GetTsDicts(http_response);
			return new TableStorageResponse(http_response, response);
		}

		public TableStorageResponse CountTables()
		{
			var ts_response = ListTables();
			var dicts = (List<Dictionary<string, object>>)ts_response.response;
			return new TableStorageResponse(ts_response.http_response, dicts.Count);
		}

		public TableStorageResponse ExistsTable(string tablename)
		{
			var ts_response = ListTables();
			var found = false;
			var dicts = (List<Dictionary<string, object>>)ts_response.response;
			foreach (var dict in dicts)
			{
				if ((string)dict["TableName"] == tablename)
					found = true;
			}
			return new TableStorageResponse(ts_response.http_response, found);
		}

		public TableStorageResponse CreateTable(string tablename)
		{
			var completed_delegate = new GenUtils.Actions.CompletedDelegate<TableStorageResponse, Object>(CompletedIfCreated);
			return GenUtils.Actions.Retry<TableStorageResponse>(delegate() { return MaybeCreateTable(tablename); }, completed_delegate, completed_delegate_object: null, wait_secs: StorageUtils.wait_secs, max_tries: StorageUtils.max_tries, timeout_secs: StorageUtils.timeout_secs);
		}

		public static bool CompletedIfCreated(TableStorageResponse response, Object o)
		{
			if (response == null)
				return false;
			return response.http_response.status == HttpStatusCode.Created;
		}

		public TableStorageResponse MaybeCreateTable(string tablename)
		{
			var inpath = "Tables";
			var d = new Dictionary<string, object>();
			d.Add("TableName", tablename);
			var content = MakeAppContent(d);
			var data = MakeAppPayload(content, "");
			var http_response = DoTableStoreRequest(inpath, query_string: null, method: "POST", headers: new Hashtable(), data: data);
			return new TableStorageResponse(http_response);
		}

		public TableStorageResponse DeleteTable(string tablename)
		{
			var inpath = string.Format("Tables('{0}')", tablename);
			var http_response = DoTableStoreRequest(inpath, query_string: null, method: "DELETE", headers: new Hashtable(), data: null);
			return new TableStorageResponse(http_response);
		}

		public TableStorageResponse DoEntity(string inpath, Dictionary<string, object> entity, string id, string method, bool force_unconditional)
		{
			byte[] data = null;
			if (entity != null && id != null)
			{
				var content = MakeAppContent(entity);
				data = MakeAppPayload(content, id);
			}

			var headers = new Hashtable();
			if (force_unconditional)
				headers["If-Match"] = "*"; // http://msdn.microsoft.com/en-us/library/dd179427.aspx

			var http_response = DoTableStoreRequest(inpath, query_string: null, method: method, headers: headers, data: data);
			//Console.WriteLine("DoTableStoreRequest http_response null? " + (http_response == null).ToString());
			return new TableStorageResponse(http_response, GetTsDicts(http_response));
		}

		public TableStorageResponse InsertEntity(string tablename, Dictionary<string, object> entity)
		{
			var inpath = tablename;
			return DoEntity(inpath, entity, id: "", method: "POST", force_unconditional: false);
		}

		public TableStorageResponse UpdateEntity(string tablename, string partkey, string rowkey, Dictionary<string, object> entity)
		{
			string inpath, id;
			PrepEntityPathAndId(tablename, partkey, rowkey, out inpath, out id);
			return DoEntity(inpath, entity, id: id, method: "PUT", force_unconditional: true);
		}

		public TableStorageResponse MergeEntity(string tablename, string partkey, string rowkey, Dictionary<string, object> entity)
		{
			string inpath, id;
			PrepEntityPathAndId(tablename, partkey, rowkey, out inpath, out id);
			return DoEntity(inpath, entity, id, method: "MERGE", force_unconditional: true);
		}

		public TableStorageResponse DeleteEntity(string tablename, string partkey, string rowkey)
		{
			var completed_delegate = new GenUtils.Actions.CompletedDelegate<TableStorageResponse, Object>(CompletedIfNotFound);
			return GenUtils.Actions.Retry<TableStorageResponse>(delegate() { return MaybeDeleteEntity(tablename, partkey, rowkey); }, completed_delegate, completed_delegate_object: null, wait_secs: StorageUtils.wait_secs, max_tries: StorageUtils.max_tries, timeout_secs: StorageUtils.timeout_secs);
		}

		public static bool CompletedIfNotFound(TableStorageResponse response, Object o)
		{
			if (response == null)
				return false;
			return response.http_response.status == HttpStatusCode.NotFound;
		}

		public TableStorageResponse MaybeDeleteEntity(string tablename, string partkey, string rowkey)
		{
			string inpath, id;
			PrepEntityPathAndId(tablename, partkey, rowkey, out inpath, out id);
			var ts_response = DoEntity(inpath, entity: null, id: null, method: "DELETE", force_unconditional: true);
			return ts_response;
		}

		public void PrepEntityPathAndId(string tablename, string partkey, string rowkey, out string inpath, out string id)
		{
			inpath = string.Format("{0}(PartitionKey='{1}',RowKey='{2}')", tablename, partkey, rowkey);
			id = string.Format("http://{0}.table.core.windows.net/{1}(PartitionKey='{2}',RowKey='{3}')",
				this.azure_storage_name, tablename, partkey, rowkey);
		}

		public TableStorageResponse QueryEntities(string tablename, string query)
		{
			var http_response = DoTableStoreRequest(tablename, query_string: query, method: "GET", headers: new Hashtable(), data: null);
			return new TableStorageResponse(http_response, GetTsDicts(http_response));
		}

		public string QueryEntitiesAsFeed(string tablename, string query)
		{
			var http_response = DoTableStoreRequest(tablename, query_string: query, method: "GET", headers: new Hashtable(), data: null);
			return http_response.DataAsString();
		}

		public enum QueryAllReturnType { as_dicts, as_string };

		public TableStorageResponse QueryAllEntities(string table, string query, QueryAllReturnType return_type)
		{
			string next_pk = "";
			string next_rk = "";
			var http_response = default(HttpResponse);
			var all_dicts = new List<Dictionary<string, object>>();
			var all_responses = new StringBuilder();

			if (return_type == QueryAllReturnType.as_string)
				all_responses.Append(String.Format(@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<feed xml:base=""http://elmcity.table.core.windows.net/"" xmlns:d=""http://schemas.microsoft.com/ado/2007/08/dataservices"" xmlns:m=""http://schemas.microsoft.com/ado/2007/08/dataservices/metadata"" xmlns=""http://www.w3.org/2005/Atom"">
  <title type=""text"">{0}</title>
  <id>http://elmcity.table.core.windows.net/{0}</id>
  <updated>2010-09-22T20:15:03Z</updated>
  <link rel=""self"" title=""monitor"" href=""{0}"" />", table));

			string adjusted_query = "";
			do
			{
				if (next_pk != "")
					adjusted_query = query + String.Format("&NextPartitionKey={0}&NextRowKey={1}", next_pk, next_rk);
				else
					adjusted_query = query;
				http_response = DoTableStoreRequest(table, query_string: adjusted_query, method: "GET", headers: new Hashtable(), data: null);
				if (http_response.headers.ContainsKey(TableStorage.NextPartitionKeyHeaderName))
				{
					next_pk = http_response.headers[TableStorage.NextPartitionKeyHeaderName];
					next_rk = http_response.headers[TableStorage.NextRowKeyHeaderName];
				}
				else
				{
					next_pk = next_rk = null;
				}

				switch (return_type)
				{
					case QueryAllReturnType.as_dicts:
						foreach (var dict in GetTsDicts(http_response))
							all_dicts.Add(dict);
						break;
					case QueryAllReturnType.as_string:
						var xdoc = XmlUtils.XdocFromXmlBytes(http_response.bytes);
						var entries = from entry in xdoc.Descendants(StorageUtils.atom_namespace + "entry") select entry;
						foreach (var entry in entries)
							all_responses.Append(entry.ToString());
						break;
				}

			}
			while (next_pk != null && next_rk != null);

			if (return_type == QueryAllReturnType.as_dicts)
				return new TableStorageResponse(http_response, all_dicts);
			else
			{
				all_responses.Append("</feed>");
				return new TableStorageResponse(http_response, all_responses.ToString());
			}
		}


		/*
		public string QueryEntitiesAsFeed(string tablename, string query, string pubsubhub_uri)
		{
			var inpath = tablename;
			var http_response = DoStoreRequest(inpath, query, "GET", new Hashtable(), data:null, content_type:null);
			var atom_feed_xml = http_response.DataAsString();
		   // return XmlUtils.PubSubHubEnable(atom_feed_xml, pubsubhub_uri);
			return atom_feed_xml;
		}*/

		public bool ExistsEntity(string tablename, string query)
		{
			var ts_response = QueryEntities(tablename, query);
			var dicts = (List<Dictionary<string, object>>)ts_response.response;
			return dicts.Count == 1;
		}

		public bool ExistsEntity(string tablename, string partition_key, string row_key)
		{
			var query = String.Format("$filter=(PartitionKey eq '{0}' and RowKey eq '{1}')", partition_key, row_key);
			return ExistsEntity(tablename, query);
		}

		// generic table request
		public HttpResponse DoTableStoreRequest(string request_path, string query_string, string method, Hashtable headers, byte[] data)
		{
			string path = "/";

			if (request_path != null)
				path += request_path;

			StorageUtils.AddDateHeader(headers);
			StorageUtils.AddVersionHeader(headers);
			headers.Add("DataServiceVersion", DataServiceVersion);
			headers.Add("MaxDataServiceVersion", MaxDataServiceVersion);
			string auth_header = StorageUtils.MakeSharedKeyLiteHeader(StorageUtils.services.table, this.azure_storage_name, this.azure_b64_secret, http_method: null, path: path, query_string: null, content_type: TABLE_STORAGE_CONTENT_TYPE, headers: headers);
			headers["Authorization"] = "SharedKeyLite " + this.azure_storage_name + ":" + auth_header;

			if (query_string != null)
				path = path + "?" + query_string;

			Uri uri = new Uri(string.Format("{0}://{1}{2}", this.scheme, this.azure_table_host, path));

			return StorageUtils.DoStorageRequest(method, headers, data: data, content_type: TABLE_STORAGE_CONTENT_TYPE, uri: uri);

		}

		// package content as an atom feed
		private static byte[] MakeAppPayload(string content, string id)
		{
			var gmt = DateTime.Now.ToUniversalTime();
			var updated = gmt.ToString(ISO_FORMAT_UTC);
			var payload = string.Format(@"<?xml version=""1.0"" 
encoding=""utf-8"" standalone=""yes""?>
<entry xmlns:d=""{0}"" xmlns:m=""{1}"" xmlns=""{2}"">
  <title />
  <updated>{3}</updated>
  <author>
    <name />
  </author>
  <id>{4}</id>
  <content type=""application/xml"">
  {5}
  </content>
</entry>", odata_namespace, odata_metadata_namespace, StorageUtils.atom_namespace, updated, id, content);
			var payload_bytes = Encoding.UTF8.GetBytes(payload);
			return payload_bytes;
		}

		// convert dict<str,obj> into content element for atom feed
		public static string MakeAppContent(Dictionary<string, object> entity)
		{
			var ret = "<m:properties>\n";
			foreach (var key in entity.Keys)
			{
				var type = "";
				var value = entity[key];
				if (value == null)
					value = "";

				switch (value.GetType().Name)
				{
					case "String":
						//type = " m:type:\"Edm.String\" ";           // not needed, is default
						value = XmlUtils.Cdataize(value.ToString());
						break;

					case "Int32":
						type = " m:type=\"Edm.Int32\" ";
						break;

					case "Int64":
						type = " m:type=\"Edm.Int64\" ";
						break;

					case "Single":
					case "Double":
						type = " m:type=\"Edm.Double\" ";
						break;

					case "Boolean":
						type = " m:type=\"Edm.Boolean\" ";
						value = value.ToString().ToLower();
						break;

					case "DateTime":
						type = " m:type=\"Edm.DateTime\" ";
						var dt = (DateTime)value;
						value = dt.ToString(ISO_FORMAT_UTC);
						break;
				}

				ret += string.Format("<d:{0}{1}>{2}</d:{3}>\n", key, type, value, key);
			}
			ret += "</m:properties>\n";
			return ret;
		}

		// package azure table response as list of dict<str,obj>

		private static List<Dictionary<string, object>> GetTsDicts(HttpResponse http_response)
		{
			return GenUtils.GetOdataDicts(http_response.bytes);
		}

	}
}

#region doc
/*
 Table: elmcity

Log entries:

PK = "log"
RK = DateTime in ticks

*/
#endregion
