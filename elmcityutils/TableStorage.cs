/* ********************************************************************************
 *
 * Copyright 2010-2013 Microsoft Corporation
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
using System.Xml;

namespace ElmcityUtils
{
	// encapsulate http response from azure table plus various operation-specific responses

	public class TableStorageHttpResponse
	{
		public HttpResponse http_response
		{
			get { return _http_response; }
		}
		private  HttpResponse _http_response;

		public TableStorageHttpResponse(HttpResponse http_response, bool null_bytes)
		{
			this._http_response = http_response;
			if (null_bytes == true)
				this._http_response.bytes = null;
		}

	}

	public class TableStorageListDictResponse : TableStorageHttpResponse
	{
		public List<Dictionary<string,object>> list_dict_obj
		{
			get { return _list_dict_obj; }
		}
		private List<Dictionary<string,object>> _list_dict_obj;

		// encapsulate the http response from the table store, plus data as a list of dict<str,obj>
		public TableStorageListDictResponse(HttpResponse http_response, List<Dictionary<string,object>> list_dict_obj)
			: base(http_response, null_bytes: true)
		{
			this._list_dict_obj = list_dict_obj;
		}
	}

	public class TableStorageStringResponse : TableStorageHttpResponse
	{
		public string str
		{
			get { return _str; }
		}
		private string _str;

		// alternate for data as string
		public TableStorageStringResponse(HttpResponse http_response, string str)
			: base(http_response, null_bytes: false)
		{
			this._str = str;
		}
	}

	public class TableStorageBoolResponse : TableStorageHttpResponse
	{
		public bool boolean
		{
			get { return _boolean; }
		}
		private bool _boolean;
		// alternate version for boolean responses
		public TableStorageBoolResponse(HttpResponse http_response, bool boolean)
			: base(http_response, null_bytes:true)
		{
			this._boolean = boolean;
		}
	}

	public class TableStorageIntResponse : TableStorageHttpResponse
	{
		public int i
		{
			get { return _i; }
		}
		private int _i;
		// alternate version for int responses
		public TableStorageIntResponse(HttpResponse http_response, int i)
			: base(http_response, null_bytes:true)
		{
			this._i = i;
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

		const string NextTableName = "x-ms-continuation-NextTableName";

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

		public string no_continuation = "none";

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
		public static TableStorageListDictResponse UpmergeDictToTableStore(Dictionary<string, object> dict, string table, string partkey, string rowkey)
		{
			return DictObjToTableStore(Operation.merge, dict, table, partkey, rowkey);
		}

		// update full set of values into existing record
		public static TableStorageListDictResponse UpdateDictToTableStore(Dictionary<string, object> dict, string table, string partkey, string rowkey)
		{
			return DictObjToTableStore(Operation.update, dict, table, partkey, rowkey);
		}

		// convert a feed url into a base-64-encoded and uri-escaped string
		// that can be used as an azure table rowkey
		public static string MakeSafeRowkeyFromUrl(string url)
		{
			var rowkey = MakeSafeRowkey(url);
			if (rowkey.Length > 1000)
				rowkey = HttpUtils.GetMd5Hash(Encoding.UTF8.GetBytes(rowkey));
			return rowkey;
		}

		public static string MakeSafeRowkey(string key)
		{
			var b64array = Encoding.UTF8.GetBytes(key);
			var rowkey = Uri.EscapeDataString(Convert.ToBase64String(b64array)).Replace('%', '_');
			return rowkey;
		}

		public static string MakeSafeBlobnameFromUrl(string url)
		{
			var name = MakeSafeRowkey(url);
			if (name.Length > 250)
				return HttpUtils.GetMd5Hash(Encoding.UTF8.GetBytes(name));
			else
				return name;
		}

		// try to insert a dict<str,obj> into table store
		// if conflict, try to merge or update 
		public static TableStorageListDictResponse DictObjToTableStore(Operation operation, Dictionary<string, object> dict, string table, string partkey, string rowkey)
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
						GenUtils.LogMsg("warning", "DictToTableStore unexpected operation", operation.ToString());
						break;
				}
				if (response.http_response.status != HttpStatusCode.NoContent)
				{
					GenUtils.PriorityLogMsg("error", "DictToTableStore: " + operation, response.http_response.status.ToString() + ", " + response.http_response.message);
				}
			}
			return response;
		}

		// this query expects to find just one matching entity
		public static Dictionary<string, object> QueryForSingleEntityAsDictObj(TableStorage ts, string table, string q)
		{
			var ts_response = ts.QueryEntities(table, q);
			var dicts = ts_response.list_dict_obj;
			var dict = new Dictionary<string, object>();

			if (dicts.Count > 0)
				dict = dicts.FirstOrDefault();

			if (dicts.Count > 1)
				// should not happen, but...
				GenUtils.LogMsg("warning", "QueryForSingleEntity: " + table, q + ": more than one matching entity");

			return dict;
		}

		public static Dictionary<string, string> QueryForSingleEntityAsDictStr(TableStorage ts, string table, string q)
		{
			var dict = QueryForSingleEntityAsDictObj(ts, table, q);
			return ObjectUtils.DictObjToDictStr(dict);
		}

		public TableStorageHttpResponse WritePriorityLogMessage(string type, string message, string data)
		{
			return WriteLogMessage (type, message, data, Configurator.azure_priority_log_table);
		}

		// for tracing, used everywhere
		public TableStorageHttpResponse WriteLogMessage(string type, string message, string data, string table)
		{
			HttpResponse http_response;
			type = type ?? "";
			message = message ?? "";
			data = data ?? "";
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", "log");
			entity.Add("RowKey", DateTime.Now.ToUniversalTime().Ticks.ToString());
			entity.Add("type", type);
			entity.Add("message", message);
			entity.Add("data", data);
			try
			{
				var tablename = (table == null) ? Configurator.azure_log_table : table;
				http_response = this.InsertEntity(table, entity).http_response;
			}
			catch (Exception e)
			{
				http_response = new HttpResponse(HttpStatusCode.Unused, "unable to write log message, " + e.Message, null, null);
			}
			return new TableStorageHttpResponse(http_response, null_bytes: true);
		}

		// for tracing, used everywhere
		public TableStorageHttpResponse WriteLogMessage(string type, string message, string data)
		{
			HttpResponse http_response;
			type = type ?? "";
			message = message ?? "";
			data = data ?? "";
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", "log");
			entity.Add("RowKey", DateTime.Now.ToUniversalTime().Ticks.ToString());
			entity.Add("type", type);
			entity.Add("message", message);
			entity.Add("data", data);
			try
			{
				http_response = this.InsertEntity(Configurator.azure_log_table, entity).http_response;
			}
			catch (Exception e)
			{
				http_response = new HttpResponse(HttpStatusCode.Unused, "unable to write log message, " + e.Message, null, null);
			}
			return new TableStorageHttpResponse(http_response, null_bytes: true);
		}

		public TableStorageListDictResponse ListTables()
		{
			var request_path = "Tables()";
			var http_response = DoTableStoreRequest(request_path, query_string: "timeout=30", method: "GET", headers: new Hashtable(), data: null);
			if (http_response.status == HttpStatusCode.ServiceUnavailable)
				throw new Exception("TableServiceUnavailable");
			if (http_response.headers.ContainsKey(TableStorage.NextTableName))
			{
				var next_table = http_response.headers[TableStorage.NextTableName];
				var query_string = "NextTableName=" + next_table;
				http_response = DoTableStoreRequest(request_path, query_string: query_string, method: "GET", headers: new Hashtable(), data: null);
			}

			var response = GetTsDicts(http_response);
			return new TableStorageListDictResponse(http_response, response);
		}

		public TableStorageIntResponse CountTables()
		{
			var response = ListTables();
			var dicts = response.list_dict_obj;
			return new TableStorageIntResponse (response.http_response, dicts.Count);
		}

		public TableStorageBoolResponse ExistsTable(string tablename)
		{
			var ts_response = ListTables();
			var found = false;
			var dicts = ts_response.list_dict_obj;
			foreach (var dict in dicts)
			{
				if ((string)dict["TableName"] == tablename)
					found = true;
			}
			return new TableStorageBoolResponse (ts_response.http_response, found);
		}

		public TableStorageHttpResponse CreateTable(string tablename)
		{
			var completed_delegate = new GenUtils.Actions.CompletedDelegate<TableStorageHttpResponse, Object>(CompletedIfCreated);
			return GenUtils.Actions.Retry<TableStorageHttpResponse>(delegate() { return MaybeCreateTable(tablename); }, completed_delegate, completed_delegate_object: null, wait_secs: StorageUtils.wait_secs, max_tries: StorageUtils.max_tries, timeout_secs: StorageUtils.timeout_secs);
		}

		public static bool CompletedIfCreated(TableStorageHttpResponse response, Object o)
		{
			if (response == null)
				return false;
			return response.http_response.status == HttpStatusCode.Created;
		}

		public TableStorageHttpResponse MaybeCreateTable(string tablename)
		{
			var inpath = "Tables";
			var d = new Dictionary<string, object>();
			d.Add("TableName", tablename);
			var content = MakeAppContent(d);
			var data = MakeAppPayload(content, "");
			var http_response = DoTableStoreRequest(inpath, query_string: null, method: "POST", headers: new Hashtable(), data: data);
			if (http_response.status == HttpStatusCode.ServiceUnavailable)
				throw new Exception("TableServiceUnavailable");
			else
				return new TableStorageHttpResponse(http_response, null_bytes: true);
		}

		public TableStorageHttpResponse DeleteTable(string tablename)
		{
			var inpath = string.Format("Tables('{0}')", tablename);
			var http_response = DoTableStoreRequest(inpath, query_string: null, method: "DELETE", headers: new Hashtable(), data: null);
			if (http_response.status == HttpStatusCode.ServiceUnavailable)
				throw new Exception("TableServiceUnavailable");
			else
				return new TableStorageHttpResponse(http_response, null_bytes: true);
		}

		public TableStorageListDictResponse DoEntity(string inpath, Dictionary<string, object> entity, string id, string method, bool force_unconditional)
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

			if (http_response.status == HttpStatusCode.ServiceUnavailable)
				throw new Exception("TableServiceUnavailable");
			else
				return new TableStorageListDictResponse (http_response, GetTsDicts(http_response));
		}

		public TableStorageListDictResponse InsertEntity(string tablename, Dictionary<string, object> entity)
		{
			var inpath = tablename;
			return DoEntity(inpath, entity, id: "", method: "POST", force_unconditional: false);
		}

		public TableStorageListDictResponse UpdateEntity(string tablename, string partkey, string rowkey, Dictionary<string, object> entity)
		{
			string inpath, id;
			PrepEntityPathAndId(tablename, partkey, rowkey, out inpath, out id);
			return DoEntity(inpath, entity, id: id, method: "PUT", force_unconditional: true);
		}

		public TableStorageListDictResponse MergeEntity(string tablename, string partkey, string rowkey, Dictionary<string, object> entity)
		{
			string inpath, id;
			PrepEntityPathAndId(tablename, partkey, rowkey, out inpath, out id);
			return DoEntity(inpath, entity, id, method: "MERGE", force_unconditional: true);
		}

		public TableStorageListDictResponse DeleteEntity(string tablename, string partkey, string rowkey)
		{
			var completed_delegate = new GenUtils.Actions.CompletedDelegate<TableStorageListDictResponse, Object>(CompletedIfNotFound);
			return GenUtils.Actions.Retry<TableStorageListDictResponse>(delegate() { return MaybeDeleteEntity(tablename, partkey, rowkey); }, completed_delegate, completed_delegate_object: null, wait_secs: StorageUtils.wait_secs, max_tries: StorageUtils.max_tries, timeout_secs: StorageUtils.timeout_secs);
		}

		public static bool CompletedIfNotFound(TableStorageHttpResponse response, Object o)
		{
			if (response == null)
				return false;
			return response.http_response.status == HttpStatusCode.NotFound;
		}

		public TableStorageListDictResponse MaybeDeleteEntity(string tablename, string partkey, string rowkey)
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

		public TableStorageListDictResponse QueryEntities(string tablename, string query)
		{
			var http_response = DoTableStoreRequest(tablename, query_string: query, method: "GET", headers: new Hashtable(), data: null);
			if (http_response.status == HttpStatusCode.ServiceUnavailable)
				throw new Exception("TableServiceUnavailable");
			else
			{
				var response = GetTsDicts(http_response);
				return new TableStorageListDictResponse(http_response, GetTsDicts(http_response));
			}

		}

		public static TableStorageListDictResponse QueryEntities(string tablename, string query, TableStorage ts)
		{
			var http_response = ts.DoTableStoreRequest(tablename, query_string: query, method: "GET", headers: new Hashtable(), data: null);
			if (http_response.status == HttpStatusCode.ServiceUnavailable)
				throw new Exception("TableServiceUnavailable");
			else
			{
				var response = GetTsDicts(http_response);
				return new TableStorageListDictResponse(http_response, GetTsDicts(http_response));
			}
		}

		public string QueryEntitiesAsFeed(string tablename, string query)
		{
			var http_response = DoTableStoreRequest(tablename, query_string: query, method: "GET", headers: new Hashtable(), data: null);
			if (http_response.status == HttpStatusCode.ServiceUnavailable)
				throw new Exception("TableServiceUnavailable");
			else
			{
				var response = GetTsDicts(http_response);
				return http_response.DataAsString();
			}
			
		}

		public string QueryEntitiesAsHtml(string tablename, string query, List<string> attrs)
		{
			var ts_response = QueryEntities(tablename, query);
			var list_dict_obj = ts_response.list_dict_obj;
			var sb = new StringBuilder();
			var html = sb.Append("<table>");
			sb.Append("<tr>");
			foreach (var attr in attrs)
				sb.Append("<td>" + attr + "</td>");
			sb.Append("</tr>");
			foreach (var dict_obj in list_dict_obj)
			{
				sb.Append("<tr>");
				var dict_str = ObjectUtils.DictObjToDictStr(dict_obj);
				foreach (var attr in attrs)
				{
					string value = "";
					if (dict_str.ContainsKey(attr))
						value = "<td>" + dict_str[attr] + "</td>";
					sb.Append(value);
				}
				sb.Append("</tr>");
			}
			sb.Append("</table>");
			return sb.ToString();
		}

		public TableStorageListDictResponse QueryAllEntitiesAsListDict(string table, string query, int max)
		{
			var list_dict_obj = new List<Dictionary<string, object>>();
			HttpResponse last_http_response = default(HttpResponse);

			foreach (HttpResponse http_response in QueryAll(table, query) )
			{
				last_http_response = http_response;
				var response_dicts = TableStorage.GetTsDicts(http_response);
				foreach (var response_dict in response_dicts)
				{
					list_dict_obj.Add(response_dict);
					if (max != 0 && list_dict_obj.Count >= max)
						break;
				}

			}

			return new TableStorageListDictResponse(last_http_response, list_dict_obj);
		}

		public string QueryAllEntitiesAsODataFeed(string table, string query)
		{
			var preamble = string.Format(@"<?xml version=""1.0"" encoding=""utf-8"" standalone=""yes""?>
<feed xml:base=""http://elmcity.table.core.windows.net/"" xmlns:d=""http://schemas.microsoft.com/ado/2007/08/dataservices"" xmlns:m=""http://schemas.microsoft.com/ado/2007/08/dataservices/metadata"" xmlns=""http://www.w3.org/2005/Atom"">
  <title type=""text"">{0}</title>
  <id>http://elmcity.table.core.windows.net/monitor</id>
  <updated>2013-10-15T22:09:53Z</updated>
  <link rel=""self"" title=""{0}"" href=""{0}"" />",
            String.Format("{0}/{1}", Configurator.azure_tablehost, table),
			table);
			var sb = new StringBuilder();
			sb.Append(preamble);
			HttpResponse last_http_response = default(HttpResponse);

			foreach (HttpResponse http_response in QueryAll(table, query))
			{
				last_http_response = http_response;
				var xml = new XmlDocument();
				xml.LoadXml(http_response.DataAsString());
				var nsmgr = new XmlNamespaceManager(xml.NameTable);
				nsmgr.AddNamespace("atom", StorageUtils.atom_namespace.ToString());
				XmlNodeList entries = xml.SelectNodes("//atom:entry", nsmgr);
				foreach (XmlNode entry in entries)
					sb.Append(entry.OuterXml);
			}

			sb.Append("</feed>");

			return sb.ToString();
		}

		private IEnumerable<HttpResponse> QueryAll(string table, string query)
		{
			string next_pk = null;
			string next_rk = null;
			var adjusted_query = "";
			HttpResponse http_response;

			do
			{
				if (next_pk != null && next_pk != no_continuation)
				{
					adjusted_query = query + "&NextPartitionKey=" + next_pk;
					if (next_rk != null)
						adjusted_query += "&NextRowKey=" + next_rk;
				}
				else
					adjusted_query = query;

				http_response = DoTableStoreRequest(table, query_string: adjusted_query, method: "GET", headers: new Hashtable(), data: null);
				if (http_response.headers.ContainsKey(TableStorage.NextPartitionKeyHeaderName))
				{
					next_pk = http_response.headers[TableStorage.NextPartitionKeyHeaderName];
					if (http_response.headers.ContainsKey(TableStorage.NextRowKeyHeaderName))
						next_rk = http_response.headers[TableStorage.NextRowKeyHeaderName];
				}
				else
					next_pk = no_continuation;

				yield return http_response;
			}

			while (next_pk != no_continuation);

		}

		public bool ExistsEntity(string tablename, string query)
		{
			var ts_response = QueryEntities(tablename, query);
			var dicts = ts_response.list_dict_obj;
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

			// return StorageUtils.DoStorageRequest(method, headers, data: data, content_type: TABLE_STORAGE_CONTENT_TYPE, uri: uri);
			return HttpUtils.RetryStorageRequestExpectingServiceAvailable(method, headers, data: data, content_type: TABLE_STORAGE_CONTENT_TYPE, uri: uri);
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

		public static List<Dictionary<string, object>> AzureTableAsXmlToListDictObj(string xmltext)
		{
			var list = new List<Dictionary<string, object>>();

			XmlDocument doc = new XmlDocument();
			doc.LoadXml(xmltext);

			var nsmgr = new XmlNamespaceManager(doc.NameTable);
			var atom_ns = "http://www.w3.org/2005/Atom";
			var dataservices_ns = "http://schemas.microsoft.com/ado/2007/08/dataservices";
			var metadata_ns = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
			nsmgr.AddNamespace("atom", atom_ns);
			nsmgr.AddNamespace("d", dataservices_ns);
			nsmgr.AddNamespace("m", metadata_ns);

			var entries = doc.SelectNodes("//atom:feed/atom:entry", nsmgr);

			foreach (XmlNode entry in entries)
			{
				var propbag = entry.SelectSingleNode("//m:properties", nsmgr);
				var dict_obj = new Dictionary<string, object>();
				foreach (XmlNode prop in propbag.ChildNodes)
				{
					var propname = prop.Name.ToString().Replace("d:", "");
					var propval = prop.FirstChild.Value;
					var proptype = prop.Attributes["type", metadata_ns];
					if (proptype == null)
					{
						dict_obj[propname] = propval;
					}
					else
					{
						switch (proptype.Value)
						{
							case "Edm.Boolean":
								dict_obj[propname] = Boolean.Parse(propval);
								break;
							case "Edm.Int32":
								dict_obj[propname] = Convert.ToInt32(propval);
								break;
							case "Edm.Int64":
								dict_obj[propname] = Convert.ToInt64(propval);
								break;
							case "Edm.Double":
								dict_obj[propname] = Convert.ToDouble(propval);
								break;
							case "Edm.DateTime":
								dict_obj[propname] = DateTime.Parse(propval);
								break;
						}
					}

					list.Add(dict_obj);
				}
			}

			return list;
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
