using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace CalendarAggregator
	{
    class Logger
    {
        public static void ExceptionLog(string message, Exception e)
        {
            string logmsg = string.Format("{1}\n{2}\n", DateTime.Now.ToString(), message, e.Message);
            //Utils.WriteLogMessage(logmsg);
            TableStorage.ts_write_log_message("exception", logmsg, null);
          
        }

        public static void InfoLog(string message)
        {
            string logmsg = string.Format("{1}\n{2}\n", DateTime.Now.ToString(), message);
            //Utils.WriteLogMessage(logmsg);
            TableStorage.ts_write_log_message("info", logmsg, null);
        }

        public static void StatusReport(string report)
        {
            string logmsg = string.Format("{0}\n{1}\n", DateTime.Now.ToString(), report);
            BlobStorage bs = new BlobStorage();
            byte[] bytes = Encoding.UTF8.GetBytes(report);
            bs.put_blob("events", "events.rpt", new Hashtable(), bytes, null);
        }

      }
	}
