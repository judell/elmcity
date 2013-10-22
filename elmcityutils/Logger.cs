using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace ElmcityUtils
{
	public class LogMsg
	{
		public string type { get; set; }
		public string title { get; set; }
		public string blurb { get; set; }

		public LogMsg(string type, string title, string blurb)
		{
			this.type = type;
			this.title = GenUtils.MakeLogMsgTitle(title);
			this.blurb = blurb;
		}
	}

	public class Logger
	{
		private TableStorage ts { get; set; }

		private int wait_milliseconds = 100;

		//private int max_messages = 1000;

		private static string hostname = Dns.GetHostName(); // for status/error reporting

		private Queue<LogMsg> log_queue = new Queue<LogMsg>();

		private Dictionary<string, string> settings;

		private int loglevel { get; set; }

		private int level_info = 1;
		private int level_status_or_warning = 2;
		//private int level_exception = 3;

		private Thread dequeue_thread;

		public Logger()
		{
			this.ts = TableStorage.MakeDefaultTableStorage();
			this.settings = GenUtils.GetSettingsFromAzureTable();
			this.loglevel = Convert.ToInt32(settings["loglevel"]);
			this.Start();
		}

		public Logger(int milliseconds, int max_messages)
		{
			this.ts = TableStorage.MakeDefaultTableStorage();
			this.wait_milliseconds = milliseconds;
			this.Start();
			//this.max_messages = max_messages;
		}

		public Logger(int milliseconds, int max_messages, TableStorage ts)
		{
			this.ts = ts;
			this.wait_milliseconds = milliseconds;
			this.Start();
			//this.max_messages = max_messages;
		}

		~Logger()
		{
			this.dequeue_thread.Abort();
		}

		public void Start()
		{
			this.dequeue_thread = new Thread(new ThreadStart(LogThreadMethod));
			this.dequeue_thread.Start();
		}

		public void LogMsg(string type, string title, string blurb)
		{
			switch (type)
			{
				case "info":
					if (this.loglevel > this.level_info)  // skip if loglevel is status_or_warning
						return;
					break;
				case "status":
				case "warning":
					if (this.loglevel > this.level_status_or_warning)  // skip of loglevel is exception
						return;
					break;
				case "exception":                           // other types: always log
					break;
				default:
					break;
			}
			var msg = new LogMsg(type: type, title: title, blurb: blurb);
			log_queue.Enqueue(msg);
		}

		public void LogHttpRequest(System.Web.Mvc.ControllerContext c)
		{
			var msg = HttpUtils.MakeHttpLogMsg(c);
			log_queue.Enqueue(msg);
		}

		// todo: flesh this out with extra info
		public void LogHttpRequestEx(System.Web.Mvc.ControllerContext c)
		{
			var msg = HttpUtils.MakeHttpLogMsg(c);
			var r = c.HttpContext.Request;
			var extra = new Dictionary<string, string>();
			// msg += JsonConvert(...)
			log_queue.Enqueue(msg);
		}

		private void LogThreadMethod()
		{
			while (true)
			{
				//if (this.log_queue.Count > this.max_messages)
				//	GenUtils.PriorityLogMsg("warning", "Logger", String.Format("{0} messages", this.log_queue.Count));

				if (this.log_queue.Count > 0)
				{
					var msg = log_queue.Dequeue();
					if (msg == null)
						this.ts.WriteLogMessage("warning", "LogThreadMethod", "unexpectedly dequeued a null value");
					else
						this.ts.WriteLogMessage(msg.type, msg.title, msg.blurb);
				}

				Thread.Sleep(wait_milliseconds);
			}
		}
	}
}
