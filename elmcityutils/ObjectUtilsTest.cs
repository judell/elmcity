﻿/* ********************************************************************************
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
	public class ObjectUtilsTest
	{

		[Test]
		public void DictStrContainsDictStrSucceeds()
		{
			var d1 = new Dictionary<string, string>() { { "a", "1" }, { "b", "2" } };
			var d2 = new Dictionary<string, string>() { { "a", "1" } };
			Assert.That(ObjectUtils.DictStrContainsDictStr(d1, d2));
		}

		[Test]
		public void DictStrContainsDictStrFails()
		{
			var d1 = new Dictionary<string, string>() { { "a", "1" }, { "b", "2" } };
			var d2 = new Dictionary<string, string>() { { "a", "2" } };
			Assert.IsFalse(ObjectUtils.DictStrContainsDictStr(d1, d2));
		}

		[Test]
		public void ListDictStrEqualsListDictStrSucceeds()
		{
			var d1a = new Dictionary<string, string>() { { "a", "1" }, { "z", "2" } };
			var d1b = new Dictionary<string, string>() { { "b", "3" }, { "y", "4" } };
			var l1 = new List<Dictionary<string, string>>() { d1a, d1b };
			var l2 = new List<Dictionary<string, string>>() { d1a, d1b };
			Assert.That(ObjectUtils.ListDictStrEqualsListDictStr(l1, l2));
		}

		[Test]
		public void ListDictStrEqualsListDictStrFailsForSwappedElements()       // just to remind me that order matters
		{
			var d1a = new Dictionary<string, string>() { { "a", "1" }, { "z", "2" } };
			var d1b = new Dictionary<string, string>() { { "b", "3" }, { "y", "4" } };
			var l1 = new List<Dictionary<string, string>>() { d1a, d1b };
			var l2 = new List<Dictionary<string, string>>() { d1b, d1a };
			Assert.IsFalse(ObjectUtils.ListDictStrEqualsListDictStr(l1, l2));
		}

		[Test]
		public void ListDictStrEqualsListDictStrFailsForExtraDict()
		{
			var d1a = new Dictionary<string, string>() { { "a", "1" }, { "z", "2" } };
			var d1b = new Dictionary<string, string>() { { "b", "3" }, { "y", "4" } };

			var d2a = new Dictionary<string, string>() { { "a", "1" }, { "q", "7"}, { "z", "2" } }; // second list has extra dict

			var l1 = new List<Dictionary<string, string>>() { d1a, d1b };
			var l2 = new List<Dictionary<string, string>>() { d1b, d2a };
			Assert.IsFalse(ObjectUtils.ListDictStrEqualsListDictStr(l1, l2));
		}

		[Test]
		public void ListDictStrEqualsListDictStrFailsForChangedDict()
		{
			var d1a = new Dictionary<string, string>() { { "a", "1" }, { "z", "2" } };
			var d1b = new Dictionary<string, string>() { { "b", "3" }, { "y", "4" } };

			var d2a = new Dictionary<string, string>() { { "a", "1" }, { "z", "0" } }; // second list has changed dict

			var l1 = new List<Dictionary<string, string>>() { d1a, d1b };
			var l2 = new List<Dictionary<string, string>>() { d1b, d2a };
			Assert.IsFalse(ObjectUtils.ListDictStrEqualsListDictStr(l1, l2));
		}

		[Test]
		public void ChangedObjectIsDetected()
		{
			var o1 = new TestObject(1, "one", true);
			var o2 = new TestObject(1, "one", true);
			Assert.That(ObjectUtils.DictStrEqualsDictStr(
				ObjectUtils.ObjToDictStr(o1),
				ObjectUtils.ObjToDictStr(o2)
				)
			);
			o2.b = false;
			Assert.IsFalse(ObjectUtils.DictStrEqualsDictStr(
				ObjectUtils.ObjToDictStr(o1),
				ObjectUtils.ObjToDictStr(o2)
				)
			);
		}


		[Test]
		public void MergeDictStrDictStrYieldsExpectedDictStr()
		{
			var d1 = new Dictionary<string, string>() { { "a", "1" }, { "b", "" }, { "c", "3" } };
			var d2 = new Dictionary<string, string>() { { "a", "" }, { "b", "2" }, { "c", "3" } };
			var merged = ObjectUtils.SimpleMergeDictStrDictStr(d1, d2);
			var expected = new Dictionary<string, string>() { { "a", "1" }, { "b", "2" }, { "c", "3" } };
			Assert.That(ObjectUtils.DictStrEqualsDictStr(merged, expected));
		}

		[Test]
		public void MergeDictStrDictStrFailsWhenKeysUnequal()
		{
			var d1 = new Dictionary<string, string>() { { "a", "1" }, { "b", "" }, { "c", "3" } };
			var d2 = new Dictionary<string, string>() { { "a", "" }, { "b", "2" }, };
			var result = true;
			try
			{
				var merged = ObjectUtils.SimpleMergeDictStrDictStr(d1, d2);
			}
			catch (Exception e)
			{
				Assert.That(e.Message == "DictStrKeysNotEqual");
				result = false;
			}

			Assert.That(result == false);

		}

	}

	public class TestObject
	{
		public int i;
		public string s;
		public bool b;

		public TestObject(int i, string s, bool b)
		{
			this.i = i;
			this.s = s;
			this.b = b;
		}
	}

}
