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

namespace ElmcityUtils
{
    using System;
	using System.Collections.Generic;
    using NUnit.Framework;

    public class GenUtilsTest
	{

	#region retry

		private int retries;

		private bool CompletedIfIntIsTwo(int i, object o)
		{
			return i == 2;
		}

		private bool CompletedIfObjectIsSeven(int i, object o)
		{
			return (int)o == 7;
		}

		private bool CompletedNever(int i, object o)
		{
			return false;
		}

		private bool CompletedIfIntIsOdd(int i, object o)
		{
			return i % 2 != 0;
		}

		private bool CompletedIfIntEndsWithZero(int i, object o)
		{
			retries++;
			if (retries == 1)
				return false;
			String s = Convert.ToString(i);
			return s.EndsWith("0");
		}

		private int Twice(int i)
		{
			retries++;
			return i * 2;
		}

		private int RandomEvenNumber()
		{
			retries++;
			var ticks_as_str = Convert.ToString(System.DateTime.Now.Ticks);
			var seed_string = ticks_as_str.Substring(ticks_as_str.Length - 4, 4);
			var random = new Random(Convert.ToInt32(seed_string));
			var i = random.Next();
			while (i % 2 != 0)
				i = random.Next();
			return i;
		}

		private int ExceptionIfOdd(int i)
		{
			if (i % 2 != 0)
				throw new Exception("OddNumberException");
			return i;
		}

		[Test]
		public void RetrySucceedsOnFirstTry()
		{
			retries = 0;
			var completed_delegate =
				new GenUtils.Actions.CompletedDelegate<int, object>(CompletedIfIntIsTwo);
			var r = GenUtils.Actions.Retry<int>(
				delegate() { return Twice(1); },
				completed_delegate,
				completed_delegate_object: null,
				wait_secs: 0,
				max_tries: 1,
				timeout_secs: TimeSpan.FromSeconds(10000));
			Assert.AreEqual(2, r);
			Assert.AreEqual(1, retries);
		}

		[Test]
		public void RetrySucceedsOnSubsequentTry()
		{
			retries = 0;
			var completed_delegate =
				new GenUtils.Actions.CompletedDelegate<int, object>(CompletedIfIntEndsWithZero);
			var r = GenUtils.Actions.Retry<int>(
				delegate() { return RandomEvenNumber(); },
				completed_delegate,
				completed_delegate_object: null,
				wait_secs: 0,
				max_tries: 10000,
				timeout_secs: TimeSpan.FromSeconds(10000));
			Assert.That(Convert.ToString(r).EndsWith("0"));
			Assert.That(retries > 1);
		}

		[Test]
		public void RetryFailsWhenPassedFailingObject()
		{
			var completed_delegate =
				new GenUtils.Actions.CompletedDelegate<int, object>(CompletedIfObjectIsSeven);
			try
			{
				var r = GenUtils.Actions.Retry<int>(
					delegate() { return Twice(1); },
					completed_delegate,
					completed_delegate_object: -7,
					wait_secs: 0,
					max_tries: 3,
					timeout_secs: TimeSpan.FromSeconds(10000));
			}
			catch (Exception e)
			{
				var exceeded_tries = (e == GenUtils.Actions.RetryExceededMaxTries);
				var timed_out = (e == GenUtils.Actions.RetryTimedOut);
				Assert.That(exceeded_tries || timed_out);
			}

		}

		[Test]
		public void RetryEndsAfterTimeout()
		{
			var completed_delegate =
				new GenUtils.Actions.CompletedDelegate<int, object>(CompletedNever);
			try
			{
				var r = GenUtils.Actions.Retry<int>(
					delegate() { return RandomEvenNumber(); },
					completed_delegate,
					completed_delegate_object: null,
					wait_secs: 1,
					max_tries: 100,
					timeout_secs: TimeSpan.FromSeconds(5));
			}
			catch (Exception e)
			{
				Assert.AreEqual(GenUtils.Actions.RetryTimedOut, e);
			}
		}

		[Test]
		public void RetryEndsAfterMaxTriesExceeded()
		{
			var completed_delegate =
				new GenUtils.Actions.CompletedDelegate<int, object>(CompletedNever);
			try
			{
				var r = GenUtils.Actions.Retry<int>(
					delegate() { return RandomEvenNumber(); },
					completed_delegate,
					completed_delegate_object: null,
					wait_secs: 1,
					max_tries: 5,
					timeout_secs: TimeSpan.FromSeconds(100));
			}
			catch (Exception e)
			{
				Assert.AreEqual(GenUtils.Actions.RetryExceededMaxTries, e);
			}
		}

		[Test]
		public void RetryTransmitsException()
		{
			var completed_delegate =
				new GenUtils.Actions.CompletedDelegate<int, object>(CompletedNever);
			try
			{
				var r = GenUtils.Actions.Retry<int>(
					delegate() { return ExceptionIfOdd(1); },
					completed_delegate,
					completed_delegate_object: null,
					wait_secs: 0,
					max_tries: 100,
					timeout_secs: TimeSpan.FromSeconds(5));
			}
			catch (Exception e)
			{
				Assert.AreEqual("OddNumberException", e.Message);
			}
		}

	#endregion retry

	#region regex

		[Test]
		public void FindsThreeGroupsLiteral()
		{
			var pat = "a (b) c (d) e";
			var groups = GenUtils.RegexFindGroups("a b c d e", pat);
			Assert.AreEqual(3, groups.Count);
		}

		[Test]
		public void DoesNotFindThreeGroupsLiteral()
		{
			var pat = "a (b) c (d) e";
			var groups = GenUtils.RegexFindGroups("a b c D e", pat);
			Assert.AreNotEqual(3, groups.Count);
		}

		[Test]
		public void FindsThreeGroupsAbstract()
		{
			var pat = @"a (http://.+\s*) c (\d+) e";
			var groups = GenUtils.RegexFindGroups("a http://foo.com?x=y c 19423 e", pat);
			Assert.AreEqual(3, groups.Count);
		}

		[Test]
		public void DoesNotFindThreeGroupsAbstract()
		{
			var pat = @"a (http://.+\s*) c (\d+) e";
			var groups = GenUtils.RegexFindGroups("a ftp://foo.com?x=y c 19423 e", pat);
			Assert.AreNotEqual(3, groups.Count);
		}

		[Test]
		public void FindsTwoKeyValuePairs()
		{
			var text = @"
Four score and seven years ago our fathers brought forth, 
upon this continent, a new nation, conceived in Liberty, 
and dedicated to the proposition that all men are created equal.

url=http://americancivilwar.com/north/lincoln.html
category=government,speech
";
			var keys = new List<string>() { "url", "category" };
			var dict = GenUtils.RegexFindKeysAndValues(keys, text);
			Assert.AreEqual(dict.Keys.Count, 2);
			Assert.AreEqual(dict["url"], "http://americancivilwar.com/north/lincoln.html");
			Assert.AreEqual(dict["category"], "government,speech");
		}

		[Test]
		public void DoesNotFindTwoKeyValuePairs()
		{
			var text = @"
Four score and seven years ago our fathers brought forth, 
upon this continent, a new nation, conceived in Liberty, 
and dedicated to the proposition that all men are created equal.

url = http://americancivilwar.com/north/lincoln.html
category=government,speech
";
			var keys = new List<string>() { "url", "category" };
			var dict = GenUtils.RegexFindKeysAndValues(keys, text);
			Assert.AreEqual(dict.Keys.Count, 1);
			Assert.IsFalse(dict.ContainsKey("url"));
			Assert.That(dict.ContainsKey("category"));
		}

	#endregion regex
	}
}
