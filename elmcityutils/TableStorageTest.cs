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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NUnit.Framework;

namespace ElmcityUtils
{
	[TestFixture]
	public class TableStorageTest
	{
		public static string test_partition = "test_partition";
		public static string test_row = "test_row";
		private static Random r = new Random();
		public string test_table = String.Format("test{0}", r.Next());
		public string test_table_2 = String.Format("test2{0}", r.Next());

		//public static string test_table = "test1";
		//public static string test_table_2 = "test2";

		public static DateTime test_dt = DateTime.Now;
		public static Int32 test_int32 = 32;
		public static Int64 test_int64 = 64;
		public static String test_str = "test_str";
		public static bool test_bool = false;

		private static int short_wait = 5;
		private static int long_wait = 30;

		~TableStorageTest()
		{
			ts.DeleteTable(test_table);
			ts.DeleteTable(test_table_2);
		}


		public static Dictionary<string, object> test_dict = new Dictionary<string, object>()
         {
            { "TestDt", test_dt },
            { "TestInt32", test_int32},
            { "TestInt64", test_int64 },
            { "TestStr", test_str},
            { "TestBool", test_bool}
        };

		public static string test_query = string.Format("$filter=(PartitionKey eq '{0}') and (RowKey eq '{1}') and (TestInt32 eq {2}) and (TestDt eq datetime'{3}')",
				test_partition, test_row, test_int32, test_dt.ToString(TableStorage.ISO_FORMAT_UTC));
		public static int count = 12345;
		public TableStorage ts = TableStorage.MakeDefaultTableStorage();
		public TableStorage secure_ts = TableStorage.MakeSecureTableStorage();


		[Test]
		public void CreateTableIsSuccessful()
		{
			ts.DeleteTable(test_table);
			HttpUtils.Wait(long_wait);
			ts.CreateTable(test_table);
			HttpUtils.Wait(short_wait);
			Assert.IsTrue((bool)ts.ExistsTable(test_table).boolean);
		}

		[Test]
		public void CountTwoTables()
		{
			ts.DeleteTable(test_table);
			ts.DeleteTable(test_table_2);
			HttpUtils.Wait(long_wait);
			ts.CreateTable(test_table);
			HttpUtils.Wait(short_wait);
			Assert.IsTrue(ts.ExistsTable(test_table).boolean);
			var count = ts.CountTables().i;
			ts.CreateTable(test_table_2);
			Assert.IsTrue(ts.ExistsTable(test_table_2).boolean);
			Assert.That(ts.CountTables().i == count + 1);
		}

		[Test]
		public void CreateEntityIsSuccessful()
		{
			ts.DeleteEntity(test_table, test_partition, test_row);
			HttpUtils.Wait(short_wait);
			if (ts.ExistsTable(test_table).boolean == false)
				ts.CreateTable(test_table);
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", test_partition);
			entity.Add("RowKey", test_row);
			foreach (var key in test_dict.Keys)
				entity.Add(key, test_dict[key]);
			Assert.AreEqual(HttpStatusCode.Created, ts.InsertEntity(test_table, entity).http_response.status);
		}

		[Test]
		public void SecureCreateEntityIsSuccessful()
		{
			secure_ts.DeleteEntity(test_table, test_partition, test_row);
			HttpUtils.Wait(short_wait);
			if (secure_ts.ExistsTable(test_table).boolean == false)
				secure_ts.CreateTable(test_table);
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", test_partition);
			entity.Add("RowKey", test_row);
			foreach (var key in test_dict.Keys)
				entity.Add(key, test_dict[key]);
			Assert.AreEqual(HttpStatusCode.Created, secure_ts.InsertEntity(test_table, entity).http_response.status);
		}

		[Test]
		public void ExistsEntitySucceedsForExistingEntity()
		{
			CreateEntityIsSuccessful();
			var q = string.Format(TableStorage.query_template_pk_rk,
				test_partition, test_row);
			Assert.AreEqual(true, ts.ExistsEntity(test_table, q));
		}

		[Test]
		public void ExistsEntityFailsForNonExistingEntity()
		{
			DeleteEntityIsSuccessful();
			var q = string.Format(TableStorage.query_template_pk_rk,
				test_partition, test_row);
			Assert.AreEqual(false, ts.ExistsEntity(test_table, q));
		}

		[Test]
		public void QueryEntitiesIsSuccessful()
		{
			if (ts.ExistsEntity(test_table, test_query) == false)
				CreateEntityIsSuccessful();
			var ts_response = ts.QueryEntities(test_table, test_query);
			var dicts = ts_response.list_dict_obj;
			AssertEntity(dicts);
		}

		private static void AssertEntity(List<Dictionary<string, object>> dicts)
		{
			Assert.That(dicts.Count == 1);
			Assert.That((String)dicts[0]["TestStr"] == test_str);
			Assert.That((Int32)dicts[0]["TestInt32"] == test_int32);
			Assert.That((Int64)dicts[0]["TestInt64"] == test_int64);
			Assert.That(((DateTime)dicts[0]["TestDt"]).ToUniversalTime().Ticks == test_dt.Ticks);
			Assert.That((bool)dicts[0]["TestBool"] == test_bool);
		}

		[Test]
		public void QueryAllEntitiesAsDictsIsSuccessful()
		{
			if (ts.ExistsEntity(test_table, test_query) == false)
				CreateEntityIsSuccessful();
			var ts_response = ts.QueryAllEntitiesAsListDict(test_table, test_query, 0);
			var dicts = ts_response.list_dict_obj;
			AssertEntity(dicts);
		}

		[Test]
		public void QueryAllEntitiesRestricts()
		{
			CreateEntityIsSuccessful();
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", test_partition);
			entity.Add("RowKey", "Row2");
			Assert.AreEqual(HttpStatusCode.Created, ts.InsertEntity(test_table, entity).http_response.status);
			entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", test_partition);
			entity.Add("RowKey", "Row3");
			Assert.AreEqual(HttpStatusCode.Created, ts.InsertEntity(test_table, entity).http_response.status);
			var ts_response = ts.QueryAllEntitiesAsListDict(test_table, null, 2);
			var dicts = ts_response.list_dict_obj;
			Assert.AreEqual(2, dicts.Count);
			ts.DeleteEntity(test_table, test_partition, "Row2");
			ts.DeleteEntity(test_table, test_partition, "Row3");
			HttpUtils.Wait(short_wait);
		}

		[Test]
		public void QueryAllEntitiesAsStringIsSuccessful()
		{
			if (ts.ExistsEntity(test_table, test_query) == false)
				CreateEntityIsSuccessful();
			var feed_xml = ts.QueryAllEntitiesAsODataFeed(test_table, test_query);
			var xdoc = XmlUtils.XdocFromXmlBytes(System.Text.Encoding.UTF8.GetBytes(feed_xml));
			var entries = from entry in xdoc.Descendants(StorageUtils.atom_namespace + "entry") select entry;
			Assert.AreEqual(1, entries.Count());

		}

		[Test]
		public void QueryForSingleEntityIsSuccessful()
		{
			if (ts.ExistsEntity(test_table, test_query) == false)
				CreateEntityIsSuccessful();
			var ts_response = ts.QueryEntities(test_table, test_query);
			var dicts = ts_response.list_dict_obj;
			Assert.That(dicts.Count == 1);
			if (ts.ExistsEntity(test_table, test_query) == false)
				CreateEntityIsSuccessful();
			var multi_response_dict = ObjectUtils.DictObjToDictStr(dicts[0]);
			var single_response_dict = TableStorage.QueryForSingleEntityAsDictStr(ts, test_table, test_query);
			Assert.That(multi_response_dict["TestInt32"] == single_response_dict["TestInt32"]);
			Assert.That(multi_response_dict["TestInt64"] == single_response_dict["TestInt64"]);
			Assert.That(multi_response_dict["TestBool"] == single_response_dict["TestBool"]);
		}

		[Test]
		public void DeleteEntityIsSuccessful()
		{
			if (ts.ExistsEntity(test_table, test_query) == false)
				CreateEntityIsSuccessful();
			var ts_response = ts.DeleteEntity(test_table, test_partition, test_row);
			HttpUtils.Wait(short_wait);
			ts_response = ts.DeleteEntity(test_table, test_partition, test_row);
			Assert.IsNotNull(ts_response);
			Assert.IsNotNull(ts_response.http_response);
			Assert.IsNotNull(ts_response.http_response.status);
			Assert.AreEqual(HttpStatusCode.NotFound, ts_response.http_response.status);
		}

		[Test]
		public void UpdateEntityIsSuccessful()
		{
			if (ts.ExistsEntity(test_table, test_query) == false)
				CreateEntityIsSuccessful();
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", test_partition);
			entity.Add("RowKey", test_row);
			entity.Add("name", "entity_name");
			entity.Add("dt", test_dt);
			entity.Add("count", count + 1);
			Assert.AreEqual(HttpStatusCode.NoContent, ts.UpdateEntity(test_table, test_partition, test_row, entity).http_response.status);
			var q = string.Format("$filter=(count eq {0}) and (dt eq datetime'{1}')",
				count + 1, test_dt.ToString(TableStorage.ISO_FORMAT_UTC));
			var ts_response = ts.QueryEntities(test_table, q);
			var dicts = ts_response.list_dict_obj;
			Assert.That(dicts.Count == 1);
			Assert.That((int)dicts[0]["count"] == count + 1);
		}

		[Test]
		public void MergeEntityIsSuccessful()
		{
			DeleteEntityIsSuccessful();
			UpdateEntityIsSuccessful();
			var entity = new Dictionary<string, object>();
			entity.Add("PartitionKey", test_partition);
			entity.Add("RowKey", test_row);
			entity.Add("count", count + 2);
			Assert.AreEqual(HttpStatusCode.NoContent, ts.MergeEntity(test_table, test_partition, test_row, entity).http_response.status);
			var q = string.Format("$filter=(count eq {0}) and (dt eq datetime'{1}')",
				count + 2, test_dt.ToString(TableStorage.ISO_FORMAT_UTC));
			var ts_response = ts.QueryEntities(test_table, q);
			var dicts = ts_response.list_dict_obj;
			Assert.That(dicts.Count == 1);
			Assert.That((int)dicts[0]["count"] == count + 2);
		}

		[Test]
		public void WriteLogMessageIsSuccessful()
		{
			var ts_response = this.ts.WriteLogMessage("test", "test_message", "test_data");
			Assert.AreEqual(HttpStatusCode.Created, ts_response.http_response.status);
		}

	}
}
