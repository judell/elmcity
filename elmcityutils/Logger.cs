using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace ElmcityUtils
{
	public class Logger
	{
		private static TableStorage default_ts = TableStorage.MakeDefaultTableStorage(); // for logging
        private static string hostname = Dns.GetHostName(); // for status/error reporting

		delegate TableStorageResponse LogWriterDelegate(string type, string title, string blurb);

		public void LogMsg(string type, string title, string blurb)
		{
			LogMsg(type, title, blurb, default_ts);
		}

		public void LogMsg(string type, string title, string blurb, TableStorage ts)
		{
			if (ts == null) ts = default_ts;
			var procname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
			title = string.Format("{0} {1} {2}", hostname, procname, title);
			var logger_delegate = new LogWriterDelegate(ts.WriteLogMessage);
			var result = logger_delegate.BeginInvoke(type, title, blurb, null, null);
			logger_delegate.EndInvoke(result);
		}

		delegate void HttpRequestLoggerDelegate(System.Web.Mvc.ControllerContext c);

		public void LogHttpRequest(System.Web.Mvc.ControllerContext c)
		{
			var logger_delegate = new HttpRequestLoggerDelegate(HttpUtils.LogHttpRequest);
			var result = logger_delegate.BeginInvoke(c, null, null);
			logger_delegate.EndInvoke(result);
		}

		/*
		public class LogMsg
		{
			public string type;
			public string title;
			public string blurb;
			public TableStorage ts;
		}	
		 
		private static Queue<LogMsg> log_queue = new Queue<LogMsg>();
		  
		private static int wait_milliseconds = 50;

		private static Thread log_thread;
		
		public Logger()
		{
        log_thread = new Thread(new ThreadStart(LogThreadMethod));
		log_thread.Start();
		}

		private static void LogThreadMethod()
		{
			while ( true )
			{
				if ( log_queue.Count > 0 )
				{
					try
					{
						var msg = log_queue.Dequeue();
						msg.ts.WriteLogMessage(msg.type, msg.title, msg.blurb);
					}
					catch { }
				}

				Thread.Sleep(wait_milliseconds);
			}
		}

        public void LogMsg(string type, string title, string blurb)
        {
            LogMsg(type, title, blurb, default_ts);
        }

        public void LogMsg(string type, string title, string blurb, TableStorage ts)
        {
			if (ts == null) ts = default_ts;
			title = GenUtils.MakeLogMsgTitle(title);
			var msg = new LogMsg { type = type, title = title, blurb = blurb, ts = ts };
			log_queue.Enqueue(msg);
        }
		 */
	}
}
