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
    using NUnit.Framework;

    public class GenUtilsTest
    {

        public GenUtilsTest()
        {
        }

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

        private int Twice(int i)
        {
            return i * 2;
        }

        private int RandomEvenNumber(int i)
        {
            var random = new Random(DateTime.Now.Millisecond * i);
            var j = random.Next();
            HttpUtils.Wait(1);
            while (j % 2 != 0)
            {
                HttpUtils.Wait(1);
                j = random.Next();
            }
            Console.WriteLine("RandomEvenNumber: " + j);
            return j;
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
            var completed_delegate = new GenUtils.Actions.CompletedDelegate<int, object>(CompletedIfIntIsTwo);
            var r = GenUtils.Actions.Retry<int>(delegate() { return Twice(1); }, completed_delegate, completed_delegate_object: null, wait_secs: 0, max_tries: 1, timeout_secs: TimeSpan.FromSeconds(10000));
            Assert.AreEqual(2, r);
        }

        [Test]
        public void RetrySucceedsOnSecondTry()
        {
        }

        [Test]
        public void RetryFailsWhenPassedFailingObject()
        {
            var completed_delegate = new GenUtils.Actions.CompletedDelegate<int, object>(CompletedIfObjectIsSeven);
            try
            {
                var r = GenUtils.Actions.Retry<int>(delegate() { return Twice(1); }, completed_delegate, completed_delegate_object: -7, wait_secs: 0, max_tries: 3, timeout_secs: TimeSpan.FromSeconds(10000));
            }
            catch (Exception e)
            {
                var exceeded_tries = (e == GenUtils.Actions.RetryExceededMaxTries);
                var timed_out = (e == GenUtils.Actions.RetryTimedOut);
                Assert.That(exceeded_tries || timed_out);
            }

        }

        [Test]
        public void RetryExceedsMaxTries()
        {
            var completed_delegate = new GenUtils.Actions.CompletedDelegate<int, object>(CompletedNever);
            try
            {
                var r = GenUtils.Actions.Retry<int>(delegate() { return Twice(1); }, completed_delegate, completed_delegate_object: null, wait_secs: 0, max_tries: 3, timeout_secs: TimeSpan.FromSeconds(10000));
            }
            catch (Exception e)
            {
                Assert.AreEqual(GenUtils.Actions.RetryExceededMaxTries, e);
            }
        }

        [Test]
        public void RetryWithLongWaitExceedsTimeout()
        {
            var completed_delegate = new GenUtils.Actions.CompletedDelegate<int, object>(CompletedNever);
            try
            {
                var r = GenUtils.Actions.Retry<int>(delegate() { return Twice(1); }, completed_delegate, completed_delegate_object: null, wait_secs: 2, max_tries: 100, timeout_secs: TimeSpan.FromSeconds(3));
            }
            catch (Exception e)
            {
                Assert.AreEqual(GenUtils.Actions.RetryTimedOut, e);
            }
        }

        [Test]
        public void RetryWithLongWaitExceedsMaxTries()
        {
            var completed_delegate = new GenUtils.Actions.CompletedDelegate<int, object>(CompletedNever);
            try
            {
                var r = GenUtils.Actions.Retry<int>(delegate() { return Twice(1); }, completed_delegate, completed_delegate_object: null, wait_secs: 2, max_tries: 2, timeout_secs: TimeSpan.FromSeconds(10000));
            }
            catch (Exception e)
            {
                Assert.AreEqual(GenUtils.Actions.RetryExceededMaxTries, e);
            }
        }

        [Test]
        public void RetryEndsAfterTimeout()
        {
            var completed_delegate = new GenUtils.Actions.CompletedDelegate<int, object>(CompletedIfIntIsOdd);
            try
            {
                var r = GenUtils.Actions.Retry<int>(delegate() { return RandomEvenNumber(1); }, completed_delegate, completed_delegate_object: null, wait_secs: 0, max_tries: 100, timeout_secs: TimeSpan.FromSeconds(5));
            }
            catch (Exception e)
            {
                Assert.AreEqual(GenUtils.Actions.RetryTimedOut, e);
            }
        }

        [Test]
        public void RetryTransmitsException()
        {
            var completed_delegate = new GenUtils.Actions.CompletedDelegate<int, object>(CompletedNever);
            try
            {
                var r = GenUtils.Actions.Retry<int>(delegate() { return ExceptionIfOdd(1); }, completed_delegate, completed_delegate_object: null, wait_secs: 0, max_tries: 100, timeout_secs: TimeSpan.FromSeconds(5));
            }
            catch (Exception e)
            {
                Assert.AreEqual("OddNumberException", e.Message);
            }

        }

    }

}
