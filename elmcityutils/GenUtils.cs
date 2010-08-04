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
using System.Net;

namespace ElmcityUtils
{
    public class GenUtils
    {

        private static TableStorage default_ts = TableStorage.MakeDefaultTableStorage(); // for logging
        private static string hostname = Dns.GetHostName(); // for status/error reporting

        public static TableStorageResponse LogMsg(string type, string title, string blurb)
        {
            return LogMsg(type, title, blurb, default_ts);
        }

        delegate TableStorageResponse LogWriterDelegate(string type, string title, string blurb);

        public static TableStorageResponse LogMsg(string type, string title, string blurb, TableStorage ts)
        {
            if (ts == null) ts = default_ts;
            var logger = new LogWriterDelegate(ts.WriteLogMessage);
            var procname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            title = string.Format("{0} {1} {2}", hostname, procname, title);
            var result = logger.BeginInvoke(type, title, blurb, null, null);
            return logger.EndInvoke(result);
        }

        public class Actions
        {
            public static Exception RetryExceededMaxTries = new Exception("RetryExceededMaxTries");
            public static Exception RetryTimedOut = new Exception("RetryTimedOut");

            public delegate T RetryDelegate<T>();

            public delegate bool CompletedDelegate<T, Object>(T result, Object o);

            public static T Retry<T>(RetryDelegate<T> Action, CompletedDelegate<T, Object> Completed, Object completed_delegate_object, int wait_secs, int max_tries, TimeSpan timeout_secs)
            {
                T result = default(T);
                DateTime start = DateTime.UtcNow;
                int tries = 0;
                var method_name = Action.Method.Name;
                while (TimeToGiveUp(start, timeout_secs, tries, max_tries) == false)
                {
                    try
                    {
                        tries++;
                        result = Action.Invoke();
                        if (Completed(result, completed_delegate_object) == true)
                            return result;
                    }
                    catch (Exception e)
                    {
                        GenUtils.LogMsg("exception", "RetryDelegate: " + method_name, e.Message + e.StackTrace);
                        throw e;
                    }
                    HttpUtils.Wait(wait_secs);
                }

                if (TimedOut(start, timeout_secs))
                    throw RetryTimedOut;

                if (ExceededTries(tries, max_tries))
                    throw RetryExceededMaxTries;

                return result;  // default(T)
            }

            private static bool TimeToGiveUp(DateTime start, TimeSpan timeout_secs, int tries, int max_tries)
            {
                var timed_out = TimedOut(start, timeout_secs);
                var exceeded_tries = ExceededTries(tries, max_tries);
                bool result = (timed_out || exceeded_tries);
                return result;
            }

            private static bool ExceededTries(int tries, int max_tries)
            {
                var exceeded_tries = tries > max_tries;
                return exceeded_tries;
            }

            private static bool TimedOut(DateTime start, TimeSpan timeout_seconds)
            {
                var timed_out = DateTime.UtcNow > start + timeout_seconds;
                return timed_out;
            }
        }
    }
}
